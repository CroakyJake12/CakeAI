#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/un.h>
#include <unistd.h>

#include <chrono>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

namespace
{
constexpr std::uint32_t kMagic = 0x48575254U;
constexpr std::uint16_t kVersion = 1;
constexpr std::size_t kMaxPayloadBytes = 1024U * 1024U;
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
    UnauthorizedPeer = 8,
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

struct Response
{
    Status status;
    std::string payload;
};

class Client
{
public:
    explicit Client(const std::filesystem::path& socketPath)
    {
        struct stat socketStat{};
        if (::stat(socketPath.c_str(), &socketStat) != 0 || !S_ISSOCK(socketStat.st_mode))
            throw std::runtime_error("helper socket is missing or is not a Unix socket");
        if ((socketStat.st_mode & 0777) != 0600)
            throw std::runtime_error("helper socket mode is not 0600");

        m_fd = ::socket(AF_UNIX, SOCK_SEQPACKET | SOCK_CLOEXEC, 0);
        if (m_fd < 0)
            throw std::runtime_error("socket() failed");

        timeval timeout{};
        timeout.tv_sec = 10;
        if (::setsockopt(m_fd, SOL_SOCKET, SO_RCVTIMEO, &timeout, sizeof(timeout)) != 0
            || ::setsockopt(m_fd, SOL_SOCKET, SO_SNDTIMEO, &timeout, sizeof(timeout)) != 0)
        {
            throw std::runtime_error("failed to set IPC socket timeout");
        }

        sockaddr_un address{};
        address.sun_family = AF_UNIX;
        if (socketPath.string().size() >= sizeof(address.sun_path))
            throw std::runtime_error("helper socket path is too long");
        std::memcpy(address.sun_path, socketPath.c_str(), socketPath.string().size() + 1);
        if (::connect(m_fd, reinterpret_cast<const sockaddr*>(&address), sizeof(address)) != 0)
            throw std::runtime_error("connect() failed");
    }

    ~Client()
    {
        if (m_fd >= 0)
            ::close(m_fd);
    }

    Response transact(std::uint16_t opcode, std::string_view payload = {})
    {
        const std::uint64_t requestId = m_nextRequestId++;
        FrameHeader header{
            kMagic,
            kVersion,
            opcode,
            requestId,
            0,
            static_cast<std::uint32_t>(payload.size()),
        };

        if (payload.size() > kMaxPayloadBytes)
            throw std::runtime_error("client payload exceeds protocol limit");

        std::vector<std::byte> frame(sizeof(header) + payload.size());
        std::memcpy(frame.data(), &header, sizeof(header));
        if (!payload.empty())
            std::memcpy(frame.data() + sizeof(header), payload.data(), payload.size());

        const ssize_t sent = ::send(m_fd, frame.data(), frame.size(), MSG_NOSIGNAL);
        if (sent != static_cast<ssize_t>(frame.size()))
            throw std::runtime_error("failed to send complete IPC request");

        std::vector<std::byte> responseBuffer(sizeof(FrameHeader) + kMaxPayloadBytes);
        const ssize_t received = ::recv(m_fd, responseBuffer.data(), responseBuffer.size(), 0);
        if (received < static_cast<ssize_t>(sizeof(FrameHeader)))
            throw std::runtime_error("failed to receive complete IPC response");

        FrameHeader responseHeader{};
        std::memcpy(&responseHeader, responseBuffer.data(), sizeof(responseHeader));
        if (responseHeader.magic != kMagic
            || responseHeader.version != kVersion
            || responseHeader.requestId != requestId
            || responseHeader.opcode != static_cast<std::uint16_t>(opcode | 0x8000U)
            || responseHeader.payloadLength > kMaxPayloadBytes
            || sizeof(FrameHeader) + responseHeader.payloadLength != static_cast<std::size_t>(received))
        {
            throw std::runtime_error("IPC response correlation/framing check failed");
        }

        std::string responsePayload;
        if (responseHeader.payloadLength > 0)
        {
            const auto* start = reinterpret_cast<const char*>(responseBuffer.data() + sizeof(FrameHeader));
            responsePayload.assign(start, responseHeader.payloadLength);
        }

        return Response{static_cast<Status>(responseHeader.status), std::move(responsePayload)};
    }

private:
    int m_fd = -1;
    std::uint64_t m_nextRequestId = 1;
};

void requireStatus(const Response& response, Status expected, std::string_view operation)
{
    if (response.status != expected)
    {
        std::cerr << "FAIL: " << operation << " returned status "
                  << static_cast<std::int32_t>(response.status)
                  << " payload=" << response.payload << '\n';
        throw std::runtime_error("unexpected helper response status");
    }
}

std::filesystem::path waitForSocket(const std::filesystem::path& path)
{
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    while (std::chrono::steady_clock::now() < deadline)
    {
        struct stat value{};
        if (::stat(path.c_str(), &value) == 0 && S_ISSOCK(value.st_mode))
            return path;
        std::this_thread::sleep_for(std::chrono::milliseconds(20));
    }
    throw std::runtime_error("helper socket did not appear before startup deadline");
}
}

int main(int argc, char** argv)
{
    if (argc != 4)
    {
        std::cerr << "usage: haven_write_engine_client <socket-path> <source-document> <output-document>\n";
        return 64;
    }

    try
    {
        const std::filesystem::path socketPath(argv[1]);
        const std::filesystem::path sourcePath = std::filesystem::absolute(argv[2]).lexically_normal();
        const std::filesystem::path outputPath = std::filesystem::absolute(argv[3]).lexically_normal();

        Client client(waitForSocket(socketPath));

        auto response = client.transact(static_cast<std::uint16_t>(Opcode::Ping));
        requireStatus(response, Status::Ok, "Ping");
        if (response.payload != "pong")
            throw std::runtime_error("Ping payload mismatch");

        response = client.transact(999);
        requireStatus(response, Status::InvalidCommand, "non-allow-listed opcode");
        std::cout << "PASS: non-allow-listed IPC opcode rejected without terminating helper\n";

        response = client.transact(static_cast<std::uint16_t>(Opcode::Open), "/etc/passwd");
        requireStatus(response, Status::ForbiddenPath, "out-of-root Open");
        std::cout << "PASS: out-of-root document path rejected\n";

        response = client.transact(static_cast<std::uint16_t>(Opcode::Open), sourcePath.string());
        requireStatus(response, Status::Ok, "Open");

        const std::string marker = std::string(" ") + std::string(kSemanticMarker);
        response = client.transact(static_cast<std::uint16_t>(Opcode::InsertText), marker);
        requireStatus(response, Status::Ok, "InsertText");

        response = client.transact(static_cast<std::uint16_t>(Opcode::SelectAll));
        requireStatus(response, Status::Ok, "SelectAll");
        if (response.payload.find(kSemanticMarker) == std::string::npos)
            throw std::runtime_error("completed SelectAll response did not contain IPC proof marker");
        std::cout << "PASS: SelectAll response correlated after command completion; selected bytes="
                  << response.payload.size() << '\n';

        response = client.transact(static_cast<std::uint16_t>(Opcode::Bold));
        requireStatus(response, Status::Ok, "Bold");
        if (response.payload != "command-complete")
            throw std::runtime_error("Bold completion payload mismatch");

        response = client.transact(static_cast<std::uint16_t>(Opcode::Save), outputPath.string());
        requireStatus(response, Status::Ok, "Save");
        if (!std::filesystem::is_regular_file(outputPath)
            || std::filesystem::file_size(outputPath) == 0)
        {
            throw std::runtime_error("helper Save did not produce a non-empty ODT");
        }

        response = client.transact(static_cast<std::uint16_t>(Opcode::Close));
        requireStatus(response, Status::Ok, "Close");

        response = client.transact(static_cast<std::uint16_t>(Opcode::Shutdown));
        requireStatus(response, Status::Ok, "Shutdown");

        std::cout << "PASS: helper IPC request correlation, allow-list, path confinement, semantic commands, save and shutdown completed\n";
        return 0;
    }
    catch (const std::exception& exception)
    {
        std::cerr << "FAIL: IPC client exception: " << exception.what() << '\n';
        return 65;
    }
}
