#define LOK_USE_UNSTABLE_API

#include <LibreOfficeKit/LibreOfficeKit.h>
#include <LibreOfficeKit/LibreOfficeKitEnums.h>
#include <LibreOfficeKit/LibreOfficeKitInit.h>

#include <chrono>
#include <cctype>
#include <cstdlib>
#include <filesystem>
#include <iostream>
#include <string>

namespace
{
constexpr const char* kSemanticMarker = "HAVEN_WRITE_LOK_SEMANTIC_PROOF_20260907";
constexpr auto kProbeDeadline = std::chrono::seconds(15);

enum class Phase
{
    Start,
    AwaitSelectAll,
    AwaitBold,
    Complete,
    Failed,
};

struct ProbeContext
{
    LibreOfficeKit* kit = nullptr;
    LibreOfficeKitDocument* document = nullptr;
    std::filesystem::path sourcePath;
    std::filesystem::path outputPath;
    std::chrono::steady_clock::time_point deadline;
    Phase phase = Phase::Start;
    bool selectAllCompleted = false;
    bool selectAllFailed = false;
    bool boldCompleted = false;
    bool boldFailed = false;
    bool selectionCallbackObserved = false;
    bool boldStateTrueObserved = false;
    std::string failure;
};

std::string fileUrl(const std::filesystem::path& path)
{
    const auto absolute = std::filesystem::absolute(path).lexically_normal().generic_string();
    return "file://" + absolute;
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

void setFailure(ProbeContext& context, std::string message)
{
    if (context.failure.empty())
        context.failure = std::move(message);
    context.phase = Phase::Failed;
}

bool hasRequiredDocumentApi(LibreOfficeKitDocument* document)
{
    return document
        && document->pClass
        && document->pClass->saveAs
        && document->pClass->getDocumentType
        && LIBREOFFICEKIT_DOCUMENT_HAS(document, initializeForRendering)
        && document->pClass->initializeForRendering
        && LIBREOFFICEKIT_DOCUMENT_HAS(document, registerCallback)
        && document->pClass->registerCallback
        && LIBREOFFICEKIT_DOCUMENT_HAS(document, postUnoCommand)
        && document->pClass->postUnoCommand
        && LIBREOFFICEKIT_DOCUMENT_HAS(document, getTextSelection)
        && document->pClass->getTextSelection
        && LIBREOFFICEKIT_DOCUMENT_HAS(document, paste)
        && document->pClass->paste;
}

LibreOfficeKitDocument* loadWriterDocument(ProbeContext& context)
{
    const auto url = fileUrl(context.sourcePath);
    LibreOfficeKitDocument* document = context.kit->pClass->documentLoadWithOptions
        ? context.kit->pClass->documentLoadWithOptions(context.kit, url.c_str(), "ReadOnly=false")
        : context.kit->pClass->documentLoad(context.kit, url.c_str());

    if (!document || !document->pClass)
        return nullptr;

    if (!document->pClass->getDocumentType
        || document->pClass->getDocumentType(document) != LOK_DOCTYPE_TEXT)
    {
        if (document->pClass->destroy)
            document->pClass->destroy(document);
        return nullptr;
    }

    return document;
}

void documentCallback(int type, const char* payload, void* data)
{
    auto* context = static_cast<ProbeContext*>(data);
    if (!context || context->phase == Phase::Failed || context->phase == Phase::Complete)
        return;

    const std::string value = payload ? payload : "";
    const auto compact = compactPayload(value);

    if (type == LOK_CALLBACK_UNO_COMMAND_RESULT)
    {
        const bool explicitFailure = compact.find("\"success\":false") != std::string::npos;
        if (value.find(".uno:SelectAll") != std::string::npos)
        {
            context->selectAllCompleted = true;
            context->selectAllFailed = explicitFailure;
            std::cout << "Observed command-result callback: .uno:SelectAll"
                      << (explicitFailure ? " (failure)" : "") << '\n';
        }
        else if (value.find(".uno:Bold") != std::string::npos)
        {
            context->boldCompleted = true;
            context->boldFailed = explicitFailure;
            std::cout << "Observed command-result callback: .uno:Bold"
                      << (explicitFailure ? " (failure)" : "") << '\n';
        }
    }
    else if (type == LOK_CALLBACK_TEXT_SELECTION && !value.empty() && value != "EMPTY")
    {
        context->selectionCallbackObserved = true;
    }
    else if (type == LOK_CALLBACK_STATE_CHANGED
             && compact.find(".uno:Bold=true") != std::string::npos)
    {
        context->boldStateTrueObserved = true;
    }
}

int clientPoll(void* data, int timeoutUs)
{
    (void)timeoutUs;
    auto* context = static_cast<ProbeContext*>(data);
    if (!context)
        return -1;

    if (context->phase == Phase::Failed || context->phase == Phase::Complete)
        return -1;

    if (std::chrono::steady_clock::now() >= context->deadline)
    {
        setFailure(*context, "semantic callback deadline expired before completion");
        return -1;
    }

    if (context->phase == Phase::Start)
    {
        context->document = loadWriterDocument(*context);
        if (!context->document)
        {
            setFailure(*context, "Writer document load failed inside the LibreOfficeKit run loop");
            return -1;
        }
        if (!hasRequiredDocumentApi(context->document))
        {
            setFailure(*context, "required semantic-command LibreOfficeKit API members are unavailable");
            return -1;
        }

        context->document->pClass->initializeForRendering(
            context->document,
            "{\"Author\":\"Haven Write semantic PoC\"}");
        context->document->pClass->registerCallback(
            context->document,
            documentCallback,
            context);

        const std::string marker = std::string(" ") + kSemanticMarker;
        if (!context->document->pClass->paste(
                context->document,
                "text/plain;charset=utf-8",
                marker.data(),
                marker.size()))
        {
            setFailure(*context, "failed to insert semantic proof marker before selection");
            return -1;
        }

        context->document->pClass->postUnoCommand(
            context->document,
            ".uno:SelectAll",
            nullptr,
            true);
        context->phase = Phase::AwaitSelectAll;
        std::cout << "Dispatched semantic command: .uno:SelectAll (notifyWhenFinished=true)\n";
        return 1;
    }

    if (context->phase == Phase::AwaitSelectAll)
    {
        if (!context->selectAllCompleted)
            return 0;
        if (context->selectAllFailed)
        {
            setFailure(*context, ".uno:SelectAll reported failure");
            return -1;
        }

        char* usedMimeType = nullptr;
        const auto selectedText = takeString(
            context->kit,
            context->document->pClass->getTextSelection(
                context->document,
                "text/plain;charset=utf-8",
                &usedMimeType));
        const auto actualMimeType = takeString(context->kit, usedMimeType);

        if (selectedText.find(kSemanticMarker) == std::string::npos)
        {
            setFailure(*context, "SelectAll completion callback arrived but selected text did not contain the proof marker");
            return -1;
        }

        std::cout << "Selection read after completion callback: " << selectedText.size()
                  << " bytes, mime=" << (actualMimeType.empty() ? "unknown" : actualMimeType) << '\n';

        context->document->pClass->postUnoCommand(
            context->document,
            ".uno:Bold",
            nullptr,
            true);
        context->phase = Phase::AwaitBold;
        std::cout << "Dispatched semantic command: .uno:Bold (notifyWhenFinished=true)\n";
        return 1;
    }

    if (context->phase == Phase::AwaitBold)
    {
        if (!context->boldCompleted)
            return 0;
        if (context->boldFailed)
        {
            setFailure(*context, ".uno:Bold reported failure");
            return -1;
        }

        const auto outputUrl = fileUrl(context->outputPath);
        if (!context->document->pClass->saveAs(
                context->document,
                outputUrl.c_str(),
                "odt",
                nullptr))
        {
            setFailure(*context, "saveAs failed after completed Bold command");
            return -1;
        }

        context->phase = Phase::Complete;
        std::cout << "Saved semantic ODT after completed Bold command: " << context->outputPath << '\n';
        return -1;
    }

    return 0;
}

void clientWake(void* data)
{
    (void)data;
}
}

int main(int argc, char** argv)
{
    if (argc != 5)
    {
        std::cerr << "usage: lok_semantic_probe <libreoffice-program-dir> <profile-dir> <source-document> <output-document>\n";
        return 64;
    }

    const std::filesystem::path programPath(argv[1]);
    const std::filesystem::path profilePath(argv[2]);
    const std::filesystem::path sourcePath(argv[3]);
    const std::filesystem::path outputPath(argv[4]);

    if (!std::filesystem::is_directory(programPath))
    {
        std::cerr << "FAIL: LibreOffice program directory does not exist: " << programPath << '\n';
        return 65;
    }
    if (!std::filesystem::is_regular_file(sourcePath))
    {
        std::cerr << "FAIL: source document does not exist: " << sourcePath << '\n';
        return 66;
    }

    std::filesystem::create_directories(profilePath);
    const auto profileUrl = fileUrl(profilePath);

    if (::setenv("SAL_LOK_OPTIONS", "unipoll", 1) != 0)
    {
        std::cerr << "FAIL: could not enable LibreOfficeKit unipoll mode\n";
        return 67;
    }

    LibreOfficeKit* kit = lok_init_2(programPath.string().c_str(), profileUrl.c_str());
    if (!kit || !kit->pClass)
    {
        std::cerr << "FAIL: lok_init_2 returned null in unipoll mode\n";
        return 68;
    }
    if (!LIBREOFFICEKIT_HAS(kit, runLoop) || !kit->pClass->runLoop)
    {
        std::cerr << "FAIL: LibreOfficeKit runLoop API is unavailable\n";
        return 69;
    }

    ProbeContext context;
    context.kit = kit;
    context.sourcePath = sourcePath;
    context.outputPath = outputPath;
    context.deadline = std::chrono::steady_clock::now() + kProbeDeadline;

    std::cout << "Starting LibreOfficeKit unipoll semantic-command loop\n";
    kit->pClass->runLoop(kit, clientPoll, clientWake, &context);

    if (context.phase != Phase::Complete)
    {
        std::cerr << "FAIL: "
                  << (context.failure.empty() ? "semantic command loop exited without completion" : context.failure)
                  << '\n';
        return 70;
    }
    if (!std::filesystem::is_regular_file(outputPath)
        || std::filesystem::file_size(outputPath) == 0)
    {
        std::cerr << "FAIL: semantic round-trip output is missing or empty\n";
        return 71;
    }

    std::cout << "Selection callback observed: "
              << (context.selectionCallbackObserved ? "yes" : "no") << '\n';
    std::cout << "Bold=true state callback observed: "
              << (context.boldStateTrueObserved ? "yes" : "no") << '\n';
    std::cout << "PASS: callback-driven SelectAll/Bold completion proof completed\n";
    return 0;
}
