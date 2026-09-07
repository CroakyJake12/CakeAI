#ifndef _GNU_SOURCE
#define _GNU_SOURCE
#endif
#define LOK_USE_UNSTABLE_API

#include <LibreOfficeKit/LibreOfficeKit.h>
#include <LibreOfficeKit/LibreOfficeKitEnums.h>
#include <LibreOfficeKit/LibreOfficeKitInit.h>

#include <poll.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/un.h>
#include <unistd.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <cctype>
#include <cerrno>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <iostream>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace
{
constexpr std::uint32_t kMagic = 0x48575254U; // HWRT
constexpr std::uint16_t kVersion = 1;
constexpr std::size_t kMaxPayloadBytes = 1024U * 1024U;
constexpr auto kCommandDeadline = std::chrono::seconds(10);
constexpr std::string_view kSemanticMarker = "HAVEN_WRITE_LOK_IPC_PROOF_20260907";

enum class Opcode : std::uint16_t
{
    Ping = 1,
    Open = 2,
    InsertText = 3,
    SelectAll = 4,
    Bold = 5,
    Save = 6,
    Close = 7,
    Shutdown = 8,
};

enum class Status : std::int32_t
{
    Ok = 0,
    InvalidCommand = 1,
    InvalidState = 2,
    InvalidPayload = 3,
    EngineFailure = 4,
    ForbiddenPath = 5,
    VersionMismatch = 6,
    Busy = 7,
};

#pragma pack(push, 1)
struct FrameHeader
{
    std::uint32_t magic;
    std::uint16_t version;
    std::uint16_t opcode;
    std::uint64_t requestId;
    std::int32_t status;
    std::uint32_t payloadLength;
};
#pragma pack(pop)

static_assert(sizeof(FrameHeader) == 24);

struct PendingCommand
{
    Opcode opcode;
    std::uint64_t requestId;
    std::string unoCommand;
    std::string expectedSelectionText;
    bool completed = false;
    bool failed = false;
    std::chrono::steady_clock::time_point deadline;
};

std::string fileUrl(const std::filesystem::path& path)
{
    return "file://" + std::filesystem::absolute(path).lexically_normal().generic_string();
}

std::string compactPayload(const std::string& value)
{
    std::string compact;
    compact.reserve(value.size());
    for (const unsigned char character : value)
    {
        if (!std::isspace(character))
            compact.push_back(static_cast<char>(character));
    }
    return compact;
}

std::string takeString(LibreOfficeKit* kit, char* value)
{
    if (!value)
        return {};

    std::string result(value);
    if (kit && kit->pClass && kit->pClass->freeError)
        kit->pClass->freeError(value);
    return result;
}

bool escapesRoot(const std::filesystem::path& relative)
{
    const auto first = relative.begin();
    return relative.empty() || relative.is_absolute()
        || (first != relative.end() && *first == "..");
}

class EngineService
{
public:
    EngineService(
        LibreOfficeKit* kit,
        std::filesystem::path socketPath,
        std::filesystem::path ioRoot)
        : m_kit(kit)
        , m_socketPath(std::move(socketPath))
        , m_ioRoot(std::filesystem::canonical(std::move(ioRoot)))
    {
    }

    ~EngineService()
    {
        // LibreOffice 24.2.7 showed repeatable post-Shutdown crashes when LOK
        // document/kit destruction was attempted from or immediately after the
        // external unipoll loop. This one-document PoC therefore makes process
        // exit the LOK reclamation boundary while explicitly cleaning our IPC.
        m_document = nullptr;
        m_pending.reset();
        closeClient();
        if (m_listenFd >= 0)
            ::close(m_listenFd);
        if (!m_socketPath.empty())
            ::unlink(m_socketPath.c_str());
    }

    bool initialiseSocket()
    {
        if (!m_socketPath.is_absolute()
            || m_socketPath.string().size() >= sizeof(sockaddr_un::sun_path)
            || std::filesystem::exists(m_socketPath))
        {
            std::cerr << "FAIL: invalid, overlong, or pre-existing helper socket path\n";
            return false;
        }

        m_listenFd = ::socket(AF_UNIX, SOCK_SEQPACKET | SOCK_NONBLOCK | SOCK_CLOEXEC, 0);
        if (m_listenFd < 0)
        {
            std::cerr << "FAIL: socket(): " << std::strerror(errno) << '\n';
            return false;
        }

        sockaddr_un address{};
        address.sun_family = AF_UNIX;
        std::memcpy(address.sun_path, m_socketPath.c_str(), m_socketPath.string().size() + 1);
        if (::bind(m_listenFd, reinterpret_cast<const sockaddr*>(&address), sizeof(address)) != 0)
        {
            std::cerr << "FAIL: bind(): " << std::strerror(errno) << '\n';
            return false;
        }
        if (::chmod(m_socketPath.c_str(), S_IRUSR | S_IWUSR) != 0)
        {
            std::cerr << "FAIL: chmod(0600): " << std::strerror(errno) << '\n';
            return false;
        }
        if (::listen(m_listenFd, 1) != 0)
        {
            std::cerr << "FAIL: listen(): " << std::strerror(errno) << '\n';
            return false;
        }

        std::cout << "Helper listening on local socket: " << m_socketPath << '\n';
        return true;
    }

    int pollOnce(int timeoutUs)
    {
        if (m_shutdown)
            return -1;

        if (m_pending)
        {
            if (m_pending->completed)
            {
                finishPending();
                return m_shutdown ? -1 : 1;
            }
            if (std::chrono::steady_clock::now() >= m_pending->deadline)
            {
                sendResponse(
                    static_cast<std::uint16_t>(m_pending->opcode),
                    m_pending->requestId,
                    Status::EngineFailure,
                    "command completion deadline expired");
                m_pending.reset();
                return 1;
            }
        }

        std::array<pollfd, 2> descriptors{};
        nfds_t count = 0;
        descriptors[count++] = pollfd{m_listenFd, POLLIN, 0};
        if (m_clientFd >= 0)
            descriptors[count++] = pollfd{m_clientFd, POLLIN | POLLHUP | POLLERR, 0};

        const int timeoutMs = m_pending
            ? 0
            : (timeoutUs < 0 ? 100 : std::clamp(timeoutUs / 1000, 0, 100));

        const int result = ::poll(descriptors.data(), count, timeoutMs);
        if (result < 0)
        {
            if (errno == EINTR)
                return 0;
            std::cerr << "FAIL: poll(): " << std::strerror(errno) << '\n';
            m_shutdown = true;
            return -1;
        }

        bool handled = false;
        if (descriptors[0].revents & POLLIN)
            handled = acceptClient() || handled;

        if (m_clientFd >= 0 && count > 1)
        {
            if (descriptors[1].revents & (POLLHUP | POLLERR | POLLNVAL))
            {
                closeClient();
                handled = true;
            }
            else if (descriptors[1].revents & POLLIN)
            {
                handled = receiveRequest() || handled;
            }
        }

        return handled ? 1 : 0;
    }

    void onDocumentCallback(int type, const char* payload)
    {
        if (!m_pending || type != LOK_CALLBACK_UNO_COMMAND_RESULT)
            return;

        const std::string value = payload ? payload : "";
        if (value.find(m_pending->unoCommand) == std::string::npos)
            return;

        m_pending->failed = compactPayload(value).find("\"success\":false") != std::string::npos;
        m_pending->completed = true;
        std::cout << "Helper observed command-result callback: " << m_pending->unoCommand << '\n';
    }

private:
    static void documentCallback(int type, const char* payload, void* data)
    {
        auto* service = static_cast<EngineService*>(data);
        if (service)
            service->onDocumentCallback(type, payload);
    }

    bool acceptClient()
    {
        if (m_clientFd >= 0)
        {
            const int extra = ::accept4(m_listenFd, nullptr, nullptr, SOCK_NONBLOCK | SOCK_CLOEXEC);
            if (extra >= 0)
                ::close(extra);
            return false;
        }

        const int fd = ::accept4(m_listenFd, nullptr, nullptr, SOCK_NONBLOCK | SOCK_CLOEXEC);
        if (fd < 0)
        {
            if (errno == EAGAIN || errno == EWOULDBLOCK)
                return false;
            std::cerr << "FAIL: accept4(): " << std::strerror(errno) << '\n';
            return false;
        }

        ucred credentials{};
        socklen_t length = sizeof(credentials);
        if (::getsockopt(fd, SOL_SOCKET, SO_PEERCRED, &credentials, &length) != 0
            || credentials.uid != ::geteuid())
        {
            std::cerr << "Rejected Unix-socket peer with mismatched UID\n";
            ::close(fd);
            return true;
        }

        m_clientFd = fd;
        std::cout << "Accepted same-UID local IPC client pid=" << credentials.pid << '\n';
        return true;
    }

    void closeClient()
    {
        if (m_clientFd >= 0)
        {
            ::close(m_clientFd);
            m_clientFd = -1;
        }
    }

    bool receiveRequest()
    {
        std::vector<std::byte> buffer(sizeof(FrameHeader) + kMaxPayloadBytes);
        const ssize_t received = ::recv(m_clientFd, buffer.data(), buffer.size(), MSG_DONTWAIT);
        if (received == 0)
        {
            closeClient();
            return true;
        }
        if (received < 0)
        {
            if (errno == EAGAIN || errno == EWOULDBLOCK)
                return false;
            closeClient();
            return true;
        }
        if (static_cast<std::size_t>(received) < sizeof(FrameHeader))
        {
            closeClient();
            return true;
        }

        FrameHeader header{};
        std::memcpy(&header, buffer.data(), sizeof(header));
        if (header.magic != kMagic || header.version != kVersion)
        {
            sendResponse(header.opcode, header.requestId, Status::VersionMismatch, "protocol magic/version mismatch");
            return true;
        }
        if (header.payloadLength > kMaxPayloadBytes
            || sizeof(FrameHeader) + header.payloadLength != static_cast<std::size_t>(received))
        {
            sendResponse(header.opcode, header.requestId, Status::InvalidPayload, "invalid payload length");
            return true;
        }
        if (header.requestId == 0)
        {
            sendResponse(header.opcode, header.requestId, Status::InvalidPayload, "request id must be non-zero");
            return true;
        }

        std::string payload;
        if (header.payloadLength > 0)
        {
            const auto* start = reinterpret_cast<const char*>(buffer.data() + sizeof(FrameHeader));
            payload.assign(start, header.payloadLength);
            if (payload.find('\0') != std::string::npos)
            {
                sendResponse(header.opcode, header.requestId, Status::InvalidPayload, "NUL bytes are not accepted");
                return true;
            }
        }

        handleRequest(header.opcode, header.requestId, payload);
        return true;
    }

    void handleRequest(std::uint16_t rawOpcode, std::uint64_t requestId, const std::string& payload)
    {
        const auto opcode = static_cast<Opcode>(rawOpcode);
        if (!isAllowedOpcode(opcode))
        {
            sendResponse(rawOpcode, requestId, Status::InvalidCommand, "command is not allow-listed");
            return;
        }
        if (m_pending)
        {
            sendResponse(rawOpcode, requestId, Status::Busy, "engine command already pending");
            return;
        }

        switch (opcode)
        {
            case Opcode::Ping:
                requireEmptyPayload(opcode, requestId, payload, "pong");
                break;
            case Opcode::Open:
                handleOpen(requestId, payload);
                break;
            case Opcode::InsertText:
                handleInsertText(requestId, payload);
                break;
            case Opcode::SelectAll:
                handleSemanticCommand(opcode, requestId, payload, ".uno:SelectAll", std::string(kSemanticMarker));
                break;
            case Opcode::Bold:
                handleSemanticCommand(opcode, requestId, payload, ".uno:Bold", {});
                break;
            case Opcode::Save:
                handleSave(requestId, payload);
                break;
            case Opcode::Close:
                handleClose(requestId, payload);
                break;
            case Opcode::Shutdown:
                handleShutdown(requestId, payload);
                break;
        }
    }

    static bool isAllowedOpcode(Opcode opcode)
    {
        switch (opcode)
        {
            case Opcode::Ping:
            case Opcode::Open:
            case Opcode::InsertText:
            case Opcode::SelectAll:
            case Opcode::Bold:
            case Opcode::Save:
            case Opcode::Close:
            case Opcode::Shutdown:
                return true;
        }
        return false;
    }

    void requireEmptyPayload(
        Opcode opcode,
        std::uint64_t requestId,
        const std::string& payload,
        std::string_view successPayload)
    {
        if (!payload.empty())
        {
            sendResponse(static_cast<std::uint16_t>(opcode), requestId, Status::InvalidPayload, "payload must be empty");
            return;
        }
        sendResponse(static_cast<std::uint16_t>(opcode), requestId, Status::Ok, successPayload);
    }

    std::optional<std::filesystem::path> confinedExistingPath(const std::string& payload)
    {
        try
        {
            const std::filesystem::path path(payload);
            if (!path.is_absolute() || !std::filesystem::is_regular_file(path))
                return std::nullopt;
            const auto candidate = std::filesystem::canonical(path);
            if (escapesRoot(std::filesystem::relative(candidate, m_ioRoot)))
                return std::nullopt;
            return candidate;
        }
        catch (const std::filesystem::filesystem_error&)
        {
            return std::nullopt;
        }
    }

    std::optional<std::filesystem::path> confinedOutputPath(const std::string& payload)
    {
        try
        {
            const std::filesystem::path path(payload);
            if (!path.is_absolute() || path.filename().empty())
                return std::nullopt;
            const auto candidate = std::filesystem::canonical(path.parent_path()) / path.filename();
            if (escapesRoot(std::filesystem::relative(candidate, m_ioRoot)))
                return std::nullopt;
            return candidate;
        }
        catch (const std::filesystem::filesystem_error&)
        {
            return std::nullopt;
        }
    }

    bool hasRequiredDocumentApi() const
    {
        return m_document
            && m_document->pClass
            && m_document->pClass->getDocumentType
            && LIBREOFFICEKIT_DOCUMENT_HAS(m_document, registerCallback)
            && m_document->pClass->registerCallback
            && LIBREOFFICEKIT_DOCUMENT_HAS(m_document, postUnoCommand)
            && m_document->pClass->postUnoCommand
            && LIBREOFFICEKIT_DOCUMENT_HAS(m_document, getTextSelection)
            && m_document->pClass->getTextSelection
            && LIBREOFFICEKIT_DOCUMENT_HAS(m_document, paste)
            && m_document->pClass->paste
            && m_document->pClass->saveAs;
    }

    bool documentIsUsable() const
    {
        return m_document && !m_logicallyClosed;
    }

    void handleOpen(std::uint64_t requestId, const std::string& payload)
    {
        if (m_document)
        {
            sendResponse(static_cast<std::uint16_t>(Opcode::Open), requestId, Status::InvalidState, "a document is already open for this helper lifetime");
            return;
        }

        const auto path = confinedExistingPath(payload);
        if (!path)
        {
            sendResponse(static_cast<std::uint16_t>(Opcode::Open), requestId, Status::ForbiddenPath, "source path is outside the allowed I/O root or invalid");
            return;
        }

        const auto url = fileUrl(*path);
        m_document = m_kit->pClass->documentLoadWithOptions
            ? m_kit->pClass->documentLoadWithOptions(m_kit, url.c_str(), "ReadOnly=false")
            : m_kit->pClass->documentLoad(m_kit, url.c_str());

        if (!hasRequiredDocumentApi()
            || m_document->pClass->getDocumentType(m_document) != LOK_DOCTYPE_TEXT)
        {
            m_document = nullptr;
            sendResponse(static_cast<std::uint16_t>(Opcode::Open), requestId, Status::EngineFailure, "Writer document load/API validation failed");
            return;
        }

        if (LIBREOFFICEKIT_DOCUMENT_HAS(m_document, initializeForRendering)
            && m_document->pClass->initializeForRendering)
        {
            m_document->pClass->initializeForRendering(m_document, "{\"Author\":\"Haven Write IPC PoC\"}");
        }
        m_document->pClass->registerCallback(m_document, documentCallback, this);
        m_logicallyClosed = false;
        sendResponse(static_cast<std::uint16_t>(Opcode::Open), requestId, Status::Ok, "writer-open");
    }

    void handleInsertText(std::uint64_t requestId, const std::string& payload)
    {
        if (!documentIsUsable())
        {
            sendResponse(static_cast<std::uint16_t>(Opcode::InsertText), requestId, Status::InvalidState, "no document is open");
            return;
        }
        if (payload.empty() || payload.size() > 64U * 1024U)
        {
            sendResponse(static_cast<std::uint16_t>(Opcode::InsertText), requestId, Status::InvalidPayload, "text payload must be 1..65536 bytes");
            return;
        }

        if (!m_document->pClass->paste(
                m_document,
                "text/plain;charset=utf-8",
                payload.data(),
                payload.size()))
        {
            sendResponse(static_cast<std::uint16_t>(Opcode::InsertText), requestId, Status::EngineFailure, "Writer paste failed");
            return;
        }

        // LOK paste() itself has no completion listener. Use an allow-listed,
        // callback-driven SelectAll as an observable FIFO fence, then prove the
        // completed selection contains the exact inserted payload before the
        // helper acknowledges InsertText. This intentionally leaves the PoC
        // selection expanded; a production helper must preserve desired caret/
        // selection semantics with a non-mutating completion mechanism.
        beginCommand(Opcode::InsertText, requestId, ".uno:SelectAll", payload);
    }

    void handleSemanticCommand(
        Opcode opcode,
        std::uint64_t requestId,
        const std::string& payload,
        std::string unoCommand,
        std::string expectedSelectionText)
    {
        if (!payload.empty())
        {
            sendResponse(static_cast<std::uint16_t>(opcode), requestId, Status::InvalidPayload, "semantic command payload must be empty");
            return;
        }
        if (!documentIsUsable())
        {
            sendResponse(static_cast<std::uint16_t>(opcode), requestId, Status::InvalidState, "no document is open");
            return;
        }
        beginCommand(opcode, requestId, std::move(unoCommand), std::move(expectedSelectionText));
    }

    void beginCommand(
        Opcode opcode,
        std::uint64_t requestId,
        std::string unoCommand,
        std::string expectedSelectionText)
    {
        m_pending = PendingCommand{
            opcode,
            requestId,
            std::move(unoCommand),
            std::move(expectedSelectionText),
            false,
            false,
            std::chrono::steady_clock::now() + kCommandDeadline,
        };
        m_document->pClass->postUnoCommand(
            m_document,
            m_pending->unoCommand.c_str(),
            nullptr,
            true);
        std::cout << "Helper dispatched notifying command request=" << requestId
                  << " uno=" << m_pending->unoCommand << '\n';
    }

    void finishPending()
    {
        if (!m_pending)
            return;

        const auto pending = *m_pending;
        m_pending.reset();
        if (pending.failed)
        {
            sendResponse(static_cast<std::uint16_t>(pending.opcode), pending.requestId, Status::EngineFailure, "LibreOffice reported command failure");
            return;
        }

        if (pending.opcode == Opcode::InsertText || pending.opcode == Opcode::SelectAll)
        {
            char* usedMimeType = nullptr;
            const auto selectedText = takeString(
                m_kit,
                m_document->pClass->getTextSelection(
                    m_document,
                    "text/plain;charset=utf-8",
                    &usedMimeType));
            const auto actualMimeType = takeString(m_kit, usedMimeType);

            if (pending.expectedSelectionText.empty()
                || selectedText.find(pending.expectedSelectionText) == std::string::npos)
            {
                std::cerr << "Selection fence completed but expected text was absent; selected bytes="
                          << selectedText.size() << '\n';
                sendResponse(static_cast<std::uint16_t>(pending.opcode), pending.requestId, Status::EngineFailure, "completed selection did not contain expected text");
                return;
            }

            std::cout << "Helper verified selected text after callback: " << selectedText.size()
                      << " bytes mime=" << (actualMimeType.empty() ? "unknown" : actualMimeType) << '\n';

            if (pending.opcode == Opcode::InsertText)
            {
                sendResponse(static_cast<std::uint16_t>(pending.opcode), pending.requestId, Status::Ok, "text-inserted");
                return;
            }

            sendResponse(static_cast<std::uint16_t>(pending.opcode), pending.requestId, Status::Ok, selectedText);
            return;
        }

        sendResponse(static_cast<std::uint16_t>(pending.opcode), pending.requestId, Status::Ok, "command-complete");
    }

    void handleSave(std::uint64_t requestId, const std::string& payload)
    {
        if (!documentIsUsable())
        {
            sendResponse(static_cast<std::uint16_t>(Opcode::Save), requestId, Status::InvalidState, "no document is open");
            return;
        }

        const auto output = confinedOutputPath(payload);
        if (!output)
        {
            sendResponse(static_cast<std::uint16_t>(Opcode::Save), requestId, Status::ForbiddenPath, "output path is outside the allowed I/O root or invalid");
            return;
        }

        const auto url = fileUrl(*output);
        if (!m_document->pClass->saveAs(m_document, url.c_str(), "odt", nullptr))
        {
            sendResponse(static_cast<std::uint16_t>(Opcode::Save), requestId, Status::EngineFailure, "Writer saveAs failed");
            return;
        }
        sendResponse(static_cast<std::uint16_t>(Opcode::Save), requestId, Status::Ok, output->string());
    }

    void handleClose(std::uint64_t requestId, const std::string& payload)
    {
        if (!payload.empty())
        {
            sendResponse(static_cast<std::uint16_t>(Opcode::Close), requestId, Status::InvalidPayload, "payload must be empty");
            return;
        }
        if (!documentIsUsable())
        {
            sendResponse(static_cast<std::uint16_t>(Opcode::Close), requestId, Status::InvalidState, "no document is open");
            return;
        }

        m_logicallyClosed = true;
        m_pending.reset();
        sendResponse(static_cast<std::uint16_t>(Opcode::Close), requestId, Status::Ok, "closed");
    }

    void handleShutdown(std::uint64_t requestId, const std::string& payload)
    {
        if (!payload.empty())
        {
            sendResponse(static_cast<std::uint16_t>(Opcode::Shutdown), requestId, Status::InvalidPayload, "payload must be empty");
            return;
        }

        m_logicallyClosed = true;
        m_pending.reset();
        sendResponse(static_cast<std::uint16_t>(Opcode::Shutdown), requestId, Status::Ok, "shutdown");
        m_shutdown = true;
    }

    void sendResponse(
        std::uint16_t requestOpcode,
        std::uint64_t requestId,
        Status status,
        std::string_view payload)
    {
        if (m_clientFd < 0)
            return;
        if (payload.size() > kMaxPayloadBytes)
            payload = "response payload exceeded limit";

        const FrameHeader header{
            kMagic,
            kVersion,
            static_cast<std::uint16_t>(requestOpcode | 0x8000U),
            requestId,
            static_cast<std::int32_t>(status),
            static_cast<std::uint32_t>(payload.size()),
        };

        std::vector<std::byte> frame(sizeof(header) + payload.size());
        std::memcpy(frame.data(), &header, sizeof(header));
        if (!payload.empty())
            std::memcpy(frame.data() + sizeof(header), payload.data(), payload.size());

        const ssize_t sent = ::send(m_clientFd, frame.data(), frame.size(), MSG_NOSIGNAL);
        if (sent != static_cast<ssize_t>(frame.size()))
            closeClient();
    }

    LibreOfficeKit* m_kit = nullptr;
    LibreOfficeKitDocument* m_document = nullptr;
    std::filesystem::path m_socketPath;
    std::filesystem::path m_ioRoot;
    int m_listenFd = -1;
    int m_clientFd = -1;
    bool m_shutdown = false;
    bool m_logicallyClosed = false;
    std::optional<PendingCommand> m_pending;
};

int clientPoll(void* data, int timeoutUs)
{
    auto* service = static_cast<EngineService*>(data);
    return service ? service->pollOnce(timeoutUs) : -1;
}

void clientWake(void* data)
{
    (void)data;
}

int main(int argc, char** argv)
{
    if (argc != 5)
    {
        std::cerr << "usage: haven_write_engine_poc <libreoffice-program-dir> <profile-dir> <socket-path> <io-root>\n";
        return 64;
    }

    try
    {
        const std::filesystem::path programPath(argv[1]);
        const std::filesystem::path profilePath(argv[2]);
        const std::filesystem::path socketPath(argv[3]);
        const std::filesystem::path ioRoot(argv[4]);

        if (!std::filesystem::is_directory(programPath)
            || !std::filesystem::is_directory(ioRoot)
            || !profilePath.is_absolute())
        {
            std::cerr << "FAIL: invalid program/profile/I/O-root path\n";
            return 65;
        }

        std::filesystem::create_directories(profilePath);
        if (::setenv("SAL_LOK_OPTIONS", "unipoll", 1) != 0)
        {
            std::cerr << "FAIL: could not enable LibreOfficeKit unipoll mode\n";
            return 66;
        }

        const auto profileUrl = fileUrl(profilePath);
        LibreOfficeKit* kit = lok_init_2(programPath.string().c_str(), profileUrl.c_str());
        if (!kit || !kit->pClass || !LIBREOFFICEKIT_HAS(kit, runLoop) || !kit->pClass->runLoop)
        {
            std::cerr << "FAIL: LibreOfficeKit runLoop is unavailable\n";
            return 67;
        }

        EngineService service(kit, socketPath, ioRoot);
        if (!service.initialiseSocket())
            return 68;

        kit->pClass->runLoop(kit, clientPoll, clientWake, &service);
        std::cout << "PASS: isolated Haven Write helper exited cleanly\n";
        return 0;
    }
    catch (const std::exception& exception)
    {
        std::cerr << "FAIL: helper exception: " << exception.what() << '\n';
        return 69;
    }
}
