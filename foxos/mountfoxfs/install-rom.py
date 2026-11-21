#!/usr/bin/env python3
"""Install a Fox16/FoxC ROM into a mounted FoxFS directory.

A built .bin file has a header and big-endian 16-bit instruction words. FoxFS
stores one instruction word per 32-bit disk word (low halfword), so copying the
.bin directly produces an executable that fails with an illegal opcode.

    install-rom.py BUILD/*.bin MOUNTPOINT/bin/NAME

MOUNTPOINT is normally ~/vf16/hd. The destination must be inside a mounted
FoxFS image; the FUSE mount will keep the entry as a type-1 program.
"""

import argparse
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools"))
from foxos_pack import read_rom  # noqa: E402


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("rom", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()

    _start, words, _header = read_rom(args.rom)
    data = b"".join(struct.pack("<I", word) for word in words)

    with args.destination.open("wb") as target:
        target.write(data)

    print("installed %s: %d instruction words" % (args.destination, len(words)))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError) as error:
        print("install-rom: %s" % error, file=sys.stderr)
        sys.exit(1)
