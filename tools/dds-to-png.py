"""Converts the game's DDS icons to PNG, ready for the ingest tool's --icons switch.

The gallery draws a handful of the game's own glyphs: technology icons, class badges, pet
affinities and move stats. They ship inside the game's archives as BC7-compressed DDS, and
neither half of getting them out can happen inside the ingest tool - the archives are Hello
Games' own HGPAK format rather than PSARC, and SkiaSharp cannot decode BC7. Pillow can.

So the route is:

    1. Unpack the game archives with the PCBANKS Explorer that ships with AMUMSS (NMSPE).
       Only TEXTURES/UI/FRONTEND/ICONS is needed.
    2. Run this, pointing it at the unpack folder.
    3. Point `extract-tech --icons` or `extract-pets --icons` at the output.

Usage:

    python tools/dds-to-png.py <unpack-folder> <output-folder>

The folder structure under ICONS is kept, because a few names appear twice - every glyph in
PETS/MOVES has a same-named silhouette in PETS/MOVES/BUTTONBG - and the ingest tool picks the
shallower of the two. Flattening would make that choice arbitrary.
"""

import os
import sys

try:
    from PIL import Image
except ImportError:
    sys.exit("Pillow is needed to decode BC7: python -m pip install --upgrade Pillow")

# The only part of the unpack the gallery reads. Anything else is megabytes of nothing.
WANTED = os.path.join("TEXTURES", "UI", "FRONTEND", "ICONS")


def convert(source: str, target: str) -> tuple[int, int]:
    """Writes every DDS under *source* to *target* as PNG. Returns (written, failed)."""
    written = failed = 0

    for folder, _, files in os.walk(source):
        for name in files:
            if not name.upper().endswith(".DDS"):
                continue

            path = os.path.join(folder, name)
            out = os.path.join(target, os.path.relpath(folder, source), name[:-4] + ".png")

            try:
                with Image.open(path) as image:
                    image.load()
                    os.makedirs(os.path.dirname(out), exist_ok=True)
                    image.convert("RGBA").save(out)
                written += 1
            except Exception as error:  # noqa: BLE001 - one bad texture should not stop the rest
                print(f"  {name}: {error}")
                failed += 1

    return written, failed


def main() -> int:
    if len(sys.argv) != 3:
        return print(__doc__) or 2

    source, target = sys.argv[1], sys.argv[2]

    # Accept either the unpack root or the icons folder itself, so it does not matter which
    # one is to hand.
    if os.path.isdir(os.path.join(source, WANTED)):
        source = os.path.join(source, WANTED)

    if not os.path.isdir(source):
        return print(f"No such folder: {source}") or 1

    print(f"reading {source}")
    written, failed = convert(source, target)
    print(f"{written} icon(s) written to {target}, {failed} failed")
    return 1 if written == 0 else 0


if __name__ == "__main__":
    raise SystemExit(main())
