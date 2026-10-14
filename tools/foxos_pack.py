#!/usr/bin/env python3
"""Embed a foreground ROM payload into a resident FoxOS ROM."""

import argparse
import struct
from pathlib import Path

EMBED_ADDRESS = 0x7000
EMBED_MAGIC = 0xF057


def read_rom(path: Path) -> tuple[int, list[int], bytes]:
    data = path.read_bytes()
    if data.startswith(b".VFOX16EXT"):
        if len(data) < 19:
            raise ValueError("extended ROM header is truncated")
        start = struct.unpack_from(">H", data, 13)[0]
        size = struct.unpack_from(">H", data, 17)[0]
        payload = data[19:]
    elif data.startswith(b".VISOFOX16"):
        start, size, payload = 0, None, data[10:]
    else:
        raise ValueError(f"{path} is not a Fox Vision ROM image")

    if size is not None:
        if size * 2 > len(payload):
            raise ValueError(f"{path} header declares {size} words, but payload is shorter")
        payload = payload[:size * 2]
    elif len(payload) % 2:
        raise ValueError(f"{path} has an odd payload length")
    return start, list(struct.unpack(f">{len(payload) // 2}H", payload)), data[:19]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("os_rom", type=Path, help="resident FoxOS ROM")
    parser.add_argument("--embed-program", type=Path, required=True)
    parser.add_argument("-o", "--output", type=Path, required=True)
    parser.add_argument("--embed-address", type=lambda value: int(value, 0), default=EMBED_ADDRESS)
    args = parser.parse_args()

    os_start, os_words, header = read_rom(args.os_rom)
    program_start, program_words, _ = read_rom(args.embed_program)
    if os_start != 0 or not header.startswith(b".VFOX16EXT"):
        raise ValueError("OS ROM must be an extended image loaded at address 0")
    if program_start != 0x4000:
        raise ValueError("foreground program must be built with --load-address 0x4000")
    if not 0 < len(program_words) <= 1000:
        raise ValueError(f"foreground program must contain 1-1000 words, found {len(program_words)}")
    if len(os_words) > args.embed_address:
        raise ValueError("OS ROM overlaps the embedded-program address")
    combined = os_words + [0] * (args.embed_address - len(os_words))
    combined += [EMBED_MAGIC, len(program_words), *program_words]
    if len(combined) > 0x8000:
        raise ValueError("combined OS image exceeds extended ROM space")

    output_header = bytearray(header)
    struct.pack_into(">H", output_header, 17, len(combined))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(bytes(output_header) + struct.pack(f">{len(combined)}H", *combined))
    print(f"embedded {args.embed_program} ({len(program_words)} words) into {args.output}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except ValueError as error:
        print(f"error: {error}")
        raise SystemExit(1)
