#!/usr/bin/env python3
"""Inspect legacy FoxFS word-encoded text.

Old `edit` builds saved one character per 32-bit little-endian word (type 1).
New files are type 3 with four bytes per word, so host editors read them
directly. This helper is only for legacy data; `encode` recreates the old
format and must not be used on a type-3 file.

    foxfs_text.py decode LEGACY_FILE TEXT_FILE
    foxfs_text.py encode TEXT_FILE LEGACY_FILE

Use "-" for stdin/stdout. Prefer foxfs_migrate_text.py to convert a whole
image in place.
"""

import argparse
import struct
import sys


def decode(data: bytes) -> bytes:
    if len(data) % 4:
        raise ValueError("input length is not a multiple of 4 bytes")
    words = struct.unpack("<%dI" % (len(data) // 4), data)
    if any(word > 0xFF for word in words):
        raise ValueError("input is not FoxFS word-encoded text")
    return bytes(words)


def encode(data: bytes) -> bytes:
    return b"".join(struct.pack("<I", byte) for byte in data)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("decode", "encode"))
    parser.add_argument("input", nargs="?", default="-")
    parser.add_argument("output", nargs="?", default="-")
    args = parser.parse_args()

    try:
        source = sys.stdin.buffer.read() if args.input == "-" else open(args.input, "rb").read()
        result = decode(source) if args.mode == "decode" else encode(source)
        if args.output == "-":
            sys.stdout.buffer.write(result)
        else:
            with open(args.output, "wb") as target:
                target.write(result)
    except (OSError, ValueError) as error:
        print(f"foxfs_text: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
