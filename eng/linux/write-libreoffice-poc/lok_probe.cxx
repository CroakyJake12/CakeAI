#define LOK_USE_UNSTABLE_API

#include <LibreOfficeKit/LibreOfficeKit.h>
#include <LibreOfficeKit/LibreOfficeKitEnums.h>
#include <LibreOfficeKit/LibreOfficeKitInit.h>

#include <algorithm>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>

namespace
{
constexpr const char* kEditProofMarker = "HAVEN_WRITE_LOK_EDIT_PROOF_20260907";

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

void fail(LibreOfficeKit* kit, const std::string& message)
{
    std::cerr << "FAIL: " << message;
    if (kit && kit->pClass && kit->pClass->getError)
    {
        char* raw = kit->pClass->getError(kit);
        const auto error = takeString(kit, raw);
        if (!error.empty())
            std::cerr << " | LibreOfficeKit: " << error;
    }
    std::cerr << '\n';
}

bool hasRequiredDocumentApi(LibreOfficeKitDocument* document)
{
    return document
        && document->pClass
        && document->pClass->destroy
        && document->pClass->saveAs
        && document->pClass->getDocumentType
        && LIBREOFFICEKIT_DOCUMENT_HAS(document, initializeForRendering)
        && document->pClass->initializeForRendering
        && LIBREOFFICEKIT_DOCUMENT_HAS(document, paintTile)
        && document->pClass->paintTile
        && LIBREOFFICEKIT_DOCUMENT_HAS(document, getDocumentSize)
        && document->pClass->getDocumentSize
        && LIBREOFFICEKIT_DOCUMENT_HAS(document, paste)
        && document->pClass->paste
        && LIBREOFFICEKIT_DOCUMENT_HAS(document, setAccessibilityState)
        && document->pClass->setAccessibilityState
        && LIBREOFFICEKIT_DOCUMENT_HAS(document, getA11yFocusedParagraph)
        && document->pClass->getA11yFocusedParagraph
        && LIBREOFFICEKIT_DOCUMENT_HAS(document, getA11yCaretPosition)
        && document->pClass->getA11yCaretPosition;
}

LibreOfficeKitDocument* loadWriterDocument(
    LibreOfficeKit* kit,
    const std::filesystem::path& path,
    const char* options)
{
    const auto url = fileUrl(path);
    LibreOfficeKitDocument* document = kit->pClass->documentLoadWithOptions
        ? kit->pClass->documentLoadWithOptions(kit, url.c_str(), options)
        : kit->pClass->documentLoad(kit, url.c_str());

    if (!document || !document->pClass)
        return nullptr;

    if (!document->pClass->getDocumentType
        || document->pClass->getDocumentType(document) != LOK_DOCTYPE_TEXT)
    {
        document->pClass->destroy(document);
        return nullptr;
    }

    return document;
}

bool renderProofTile(
    LibreOfficeKitDocument* document,
    const std::filesystem::path& tilePath,
    long& widthTwips,
    long& heightTwips)
{
    document->pClass->getDocumentSize(document, &widthTwips, &heightTwips);
    if (widthTwips <= 0 || heightTwips <= 0)
        return false;

    constexpr int pixelWidth = 512;
    constexpr int pixelHeight = 512;
    std::vector<unsigned char> pixels(static_cast<std::size_t>(pixelWidth) * pixelHeight * 4U);
    document->pClass->paintTile(
        document,
        pixels.data(),
        pixelWidth,
        pixelHeight,
        0,
        0,
        static_cast<int>(std::min<long>(widthTwips, 12240)),
        static_cast<int>(std::min<long>(heightTwips, 15840)));

    if (!tilePath.empty())
    {
        std::ofstream tile(tilePath, std::ios::binary | std::ios::trunc);
        tile.write(reinterpret_cast<const char*>(pixels.data()), static_cast<std::streamsize>(pixels.size()));
        if (!tile)
            return false;
    }

    return true;
}
}

int main(int argc, char** argv)
{
    if (argc < 4)
    {
        std::cerr << "usage: lok_probe <libreoffice-program-dir> <profile-dir> <source-document> [output-document] [tile-rgba]\n";
        return 64;
    }

    const std::filesystem::path programPath(argv[1]);
    const std::filesystem::path profilePath(argv[2]);
    const std::filesystem::path sourcePath(argv[3]);
    const std::filesystem::path outputPath = argc >= 5
        ? std::filesystem::path(argv[4])
        : std::filesystem::path{};
    const std::filesystem::path tilePath = argc >= 6
        ? std::filesystem::path(argv[5])
        : std::filesystem::path{};

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
    if (outputPath.empty())
    {
        std::cerr << "FAIL: an output document is required for persistence proof\n";
        return 67;
    }

    std::filesystem::create_directories(profilePath);
    const auto profileUrl = fileUrl(profilePath);

    LibreOfficeKit* kit = lok_init_2(programPath.string().c_str(), profileUrl.c_str());
    if (!kit || !kit->pClass)
    {
        std::cerr << "FAIL: lok_init_2 returned null\n";
        return 68;
    }

    const auto version = LIBREOFFICEKIT_HAS(kit, getVersionInfo) && kit->pClass->getVersionInfo
        ? takeString(kit, kit->pClass->getVersionInfo(kit))
        : std::string{};
    std::cout << "LibreOfficeKit version: " << (version.empty() ? "unknown" : version) << '\n';

    LibreOfficeKitDocument* document = loadWriterDocument(kit, sourcePath, "ReadOnly=false");
    if (!document)
    {
        fail(kit, "Writer document load failed");
        kit->pClass->destroy(kit);
        return 69;
    }

    if (!hasRequiredDocumentApi(document))
    {
        std::cerr << "FAIL: required LibreOfficeKit Writer API members are unavailable\n";
        document->pClass->destroy(document);
        kit->pClass->destroy(kit);
        return 70;
    }

    document->pClass->initializeForRendering(document, "{\"Author\":\"Haven Write PoC\"}");

    long widthTwips = 0;
    long heightTwips = 0;
    if (!renderProofTile(document, tilePath, widthTwips, heightTwips))
    {
        std::cerr << "FAIL: Writer tile render proof failed\n";
        document->pClass->destroy(document);
        kit->pClass->destroy(kit);
        return 71;
    }
    std::cout << "Document size (twips): " << widthTwips << " x " << heightTwips << '\n';
    if (!tilePath.empty())
        std::cout << "Rendered raw 512x512x4 tile: " << tilePath << '\n';

    const int viewId = LIBREOFFICEKIT_DOCUMENT_HAS(document, getView) && document->pClass->getView
        ? document->pClass->getView(document)
        : 0;
    document->pClass->setAccessibilityState(document, viewId, true);
    const auto focusedParagraph = takeString(kit, document->pClass->getA11yFocusedParagraph(document));
    const auto caret = document->pClass->getA11yCaretPosition(document);
    std::cout << "Accessibility caret: " << caret << '\n';
    std::cout << "Accessibility paragraph bytes: " << focusedParagraph.size() << '\n';

    const std::string editProof = std::string(" ") + kEditProofMarker;
    if (!document->pClass->paste(
            document,
            "text/plain;charset=utf-8",
            editProof.data(),
            editProof.size()))
    {
        fail(kit, "Writer paste edit failed");
        document->pClass->destroy(document);
        kit->pClass->destroy(kit);
        return 72;
    }
    std::cout << "Writer paste accepted marker: " << kEditProofMarker << '\n';

    const auto outputUrl = fileUrl(outputPath);
    if (!document->pClass->saveAs(document, outputUrl.c_str(), "odt", nullptr))
    {
        fail(kit, "saveAs failed");
        document->pClass->destroy(document);
        kit->pClass->destroy(kit);
        return 73;
    }
    std::cout << "Saved ODT: " << outputPath << '\n';

    document->pClass->destroy(document);
    document = nullptr;

    if (!std::filesystem::is_regular_file(outputPath)
        || std::filesystem::file_size(outputPath) == 0)
    {
        std::cerr << "FAIL: saved document is missing or empty\n";
        kit->pClass->destroy(kit);
        return 74;
    }

    LibreOfficeKitDocument* reopened = loadWriterDocument(kit, outputPath, "ReadOnly=true");
    if (!reopened)
    {
        fail(kit, "saved Writer document could not be reopened");
        kit->pClass->destroy(kit);
        return 75;
    }
    if (!hasRequiredDocumentApi(reopened))
    {
        std::cerr << "FAIL: reopened document does not expose the required LibreOfficeKit API\n";
        reopened->pClass->destroy(reopened);
        kit->pClass->destroy(kit);
        return 76;
    }

    reopened->pClass->initializeForRendering(reopened, "{\"Author\":\"Haven Write PoC\"}");
    long reopenedWidth = 0;
    long reopenedHeight = 0;
    if (!renderProofTile(reopened, {}, reopenedWidth, reopenedHeight))
    {
        std::cerr << "FAIL: reopened Writer document could not be rendered\n";
        reopened->pClass->destroy(reopened);
        kit->pClass->destroy(kit);
        return 77;
    }
    std::cout << "Reopened document size (twips): " << reopenedWidth << " x " << reopenedHeight << '\n';

    reopened->pClass->destroy(reopened);
    kit->pClass->destroy(kit);
    std::cout << "PASS: LibreOfficeKit Writer render/edit/save/reopen probe completed\n";
    return 0;
}
