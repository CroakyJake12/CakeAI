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
        && LIBREOFFICEKIT_DOCUMENT_HAS(document, initializeForRendering)
        && document->pClass->initializeForRendering
        && LIBREOFFICEKIT_DOCUMENT_HAS(document, paintTile)
        && document->pClass->paintTile
        && LIBREOFFICEKIT_DOCUMENT_HAS(document, getDocumentSize)
        && document->pClass->getDocumentSize
        && LIBREOFFICEKIT_DOCUMENT_HAS(document, postUnoCommand)
        && document->pClass->postUnoCommand
        && LIBREOFFICEKIT_DOCUMENT_HAS(document, setAccessibilityState)
        && document->pClass->setAccessibilityState
        && LIBREOFFICEKIT_DOCUMENT_HAS(document, getA11yFocusedParagraph)
        && document->pClass->getA11yFocusedParagraph
        && LIBREOFFICEKIT_DOCUMENT_HAS(document, getA11yCaretPosition)
        && document->pClass->getA11yCaretPosition;
}
}

int main(int argc, char** argv)
{
    if (argc < 4)
    {
        std::cerr << "usage: lok_probe <libreoffice-install> <profile-dir> <source-document> [output-document] [tile-rgba]\n";
        return 64;
    }

    const std::filesystem::path installPath(argv[1]);
    const std::filesystem::path profilePath(argv[2]);
    const std::filesystem::path sourcePath(argv[3]);
    const std::filesystem::path outputPath = argc >= 5
        ? std::filesystem::path(argv[4])
        : std::filesystem::path{};
    const std::filesystem::path tilePath = argc >= 6
        ? std::filesystem::path(argv[5])
        : std::filesystem::path{};

    if (!std::filesystem::is_directory(installPath))
    {
        std::cerr << "FAIL: LibreOffice installation path does not exist: " << installPath << '\n';
        return 65;
    }
    if (!std::filesystem::is_regular_file(sourcePath))
    {
        std::cerr << "FAIL: source document does not exist: " << sourcePath << '\n';
        return 66;
    }

    std::filesystem::create_directories(profilePath);
    const auto profileUrl = fileUrl(profilePath);

    LibreOfficeKit* kit = lok_init_2(installPath.string().c_str(), profileUrl.c_str());
    if (!kit || !kit->pClass)
    {
        std::cerr << "FAIL: lok_init_2 returned null\n";
        return 67;
    }

    const auto version = LIBREOFFICEKIT_HAS(kit, getVersionInfo) && kit->pClass->getVersionInfo
        ? takeString(kit, kit->pClass->getVersionInfo(kit))
        : std::string{};
    std::cout << "LibreOfficeKit version: " << (version.empty() ? "unknown" : version) << '\n';

    const auto sourceUrl = fileUrl(sourcePath);
    LibreOfficeKitDocument* document = kit->pClass->documentLoadWithOptions
        ? kit->pClass->documentLoadWithOptions(kit, sourceUrl.c_str(), "ReadOnly=false")
        : kit->pClass->documentLoad(kit, sourceUrl.c_str());
    if (!document || !document->pClass)
    {
        fail(kit, "document load failed");
        kit->pClass->destroy(kit);
        return 68;
    }

    if (!document->pClass->getDocumentType
        || document->pClass->getDocumentType(document) != LOK_DOCTYPE_TEXT)
    {
        std::cerr << "FAIL: loaded document is not a Writer/text document\n";
        document->pClass->destroy(document);
        kit->pClass->destroy(kit);
        return 69;
    }

    if (!hasRequiredDocumentApi(document))
    {
        std::cerr << "FAIL: required unstable LibreOfficeKit Writer API members are unavailable\n";
        document->pClass->destroy(document);
        kit->pClass->destroy(kit);
        return 70;
    }

    document->pClass->initializeForRendering(document, "{\"Author\":\"Haven Write PoC\"}");

    long widthTwips = 0;
    long heightTwips = 0;
    document->pClass->getDocumentSize(document, &widthTwips, &heightTwips);
    if (widthTwips <= 0 || heightTwips <= 0)
    {
        std::cerr << "FAIL: Writer reported an invalid document size\n";
        document->pClass->destroy(document);
        kit->pClass->destroy(kit);
        return 71;
    }
    std::cout << "Document size (twips): " << widthTwips << " x " << heightTwips << '\n';

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
        {
            std::cerr << "FAIL: could not write tile output\n";
            document->pClass->destroy(document);
            kit->pClass->destroy(kit);
            return 72;
        }
        std::cout << "Rendered raw 512x512x4 tile: " << tilePath << '\n';
    }

    const int viewId = LIBREOFFICEKIT_DOCUMENT_HAS(document, getView) && document->pClass->getView
        ? document->pClass->getView(document)
        : 0;
    document->pClass->setAccessibilityState(document, viewId, true);
    const auto focusedParagraph = takeString(kit, document->pClass->getA11yFocusedParagraph(document));
    const auto caret = document->pClass->getA11yCaretPosition(document);
    std::cout << "Accessibility caret: " << caret << '\n';
    std::cout << "Accessibility paragraph bytes: " << focusedParagraph.size() << '\n';

    document->pClass->postUnoCommand(document, ".uno:Bold", nullptr, false);
    std::cout << "UNO command accepted for dispatch: .uno:Bold\n";

    if (!outputPath.empty())
    {
        const auto outputUrl = fileUrl(outputPath);
        if (!document->pClass->saveAs
            || !document->pClass->saveAs(document, outputUrl.c_str(), nullptr, nullptr))
        {
            fail(kit, "saveAs failed");
            document->pClass->destroy(document);
            kit->pClass->destroy(kit);
            return 73;
        }
        std::cout << "Saved: " << outputPath << '\n';
    }

    document->pClass->destroy(document);
    kit->pClass->destroy(kit);
    std::cout << "PASS: LibreOfficeKit Writer probe completed\n";
    return 0;
}
