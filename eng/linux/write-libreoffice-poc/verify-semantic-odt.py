#!/usr/bin/env python3
from __future__ import annotations

import sys
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZipFile

MARKER = "HAVEN_WRITE_LOK_SEMANTIC_PROOF_20260907"
BOLD_VALUES = {"bold", "700", "800", "900"}


def local_name(value: str) -> str:
    return value.rsplit("}", 1)[-1]


def attribute(element: ET.Element, name: str) -> str | None:
    for key, value in element.attrib.items():
        if local_name(key) == name:
            return value
    return None


def style_definitions(roots: list[ET.Element]) -> dict[str, tuple[bool, str | None]]:
    styles: dict[str, tuple[bool, str | None]] = {}
    for root in roots:
        for element in root.iter():
            if local_name(element.tag) != "style":
                continue
            name = attribute(element, "name")
            if not name:
                continue
            parent = attribute(element, "parent-style-name")
            bold = False
            for descendant in element.iter():
                for key, value in descendant.attrib.items():
                    if local_name(key) == "font-weight" and value.lower() in BOLD_VALUES:
                        bold = True
            styles[name] = (bold, parent)
    return styles


def resolves_bold(
    style_name: str,
    styles: dict[str, tuple[bool, str | None]],
    seen: set[str] | None = None,
) -> bool:
    if seen is None:
        seen = set()
    if style_name in seen:
        return False
    seen.add(style_name)

    definition = styles.get(style_name)
    if not definition:
        return False
    bold, parent = definition
    if bold:
        return True
    return bool(parent and resolves_bold(parent, styles, seen))


def verify(path: Path) -> None:
    if not path.is_file() or path.stat().st_size == 0:
        raise SystemExit(f"FAIL: semantic ODT is missing or empty: {path}")

    with ZipFile(path) as archive:
        members = set(archive.namelist())
        if "content.xml" not in members:
            raise SystemExit("FAIL: semantic ODT has no content.xml")
        content_bytes = archive.read("content.xml")
        roots = [ET.fromstring(content_bytes)]
        if "styles.xml" in members:
            roots.append(ET.fromstring(archive.read("styles.xml")))

    content_text = content_bytes.decode("utf-8")
    if MARKER not in content_text:
        raise SystemExit("FAIL: semantic proof marker did not persist in content.xml")

    styles = style_definitions(roots)
    content_root = roots[0]
    marker_style: str | None = None

    for element in content_root.iter():
        if MARKER not in "".join(element.itertext()):
            continue
        style_name = attribute(element, "style-name")
        if style_name and resolves_bold(style_name, styles):
            marker_style = style_name
            break

    if not marker_style:
        raise SystemExit(
            "FAIL: semantic marker persisted, but no bold style could be resolved for an element containing it"
        )

    print(f"PASS: semantic marker persisted with resolved bold style {marker_style!r}")


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: verify-semantic-odt.py <semantic-output.odt>", file=sys.stderr)
        return 64
    verify(Path(sys.argv[1]))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
