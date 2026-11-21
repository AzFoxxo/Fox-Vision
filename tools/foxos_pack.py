#!/usr/bin/env python3
"""Embed a foreground ROM payload into a resident FoxOS ROM."""

import argparse
import struct
from pathlib import Path

EMBED_ADDRESS = 0x20000
EMBED_MAGIC = 0xF057
EMBED_FILE_MAGIC = 0xF058
EMBED_NAME_MAX = 8
PROGRAM_BASE = 0x100000


def read_rom(path: Path) -> tuple[int, list[int], bytes]:
    data = path.read_bytes()
    if data.startswith(b".VFOX16EXT"):
        if len(data) < 11:
            raise ValueError("extended ROM header is truncated")
        version = data[10]
        if version == 1:
            if len(data) < 17:
                raise ValueError("extended ROM header v1 is truncated")
            start = struct.unpack_from(">H", data, 13)[0]
            size = struct.unpack_from(">H", data, 15)[0]
            header_len = 17
        elif version == 2:
            if len(data) < 19:
                raise ValueError("extended ROM header v2 is truncated")
            start = struct.unpack_from(">H", data, 13)[0]
            size = struct.unpack_from(">H", data, 17)[0]
            header_len = 19
        elif version == 3:
            if len(data) >= 27:
                test_size = struct.unpack_from(">I", data, 23)[0]
                payload_len = len(data) - 27
                if payload_len % 2 != 0:
                    payload_len += 1
                if payload_len // 2 == test_size:
                    header_len = 27
                    start = struct.unpack_from(">I", data, 15)[0]
                    size = test_size
                else:
                    if len(data) < 25:
                        raise ValueError("extended ROM header v3 is truncated")
                    header_len = 25
                    start = struct.unpack_from(">I", data, 13)[0]
                    size = struct.unpack_from(">I", data, 21)[0]
            elif len(data) >= 25:
                header_len = 25
                start = struct.unpack_from(">I", data, 13)[0]
                size = struct.unpack_from(">I", data, 21)[0]
            else:
                raise ValueError("extended ROM header v3 is truncated")
        else:
            raise ValueError(f"unsupported extended ROM version: {version}")
        payload = data[header_len:]
        header_bytes = data[:header_len]
    elif data.startswith(b".VISOFOX16"):
        start, size, payload, header_bytes = 0, None, data[10:], data[:10]
    else:
        raise ValueError(f"{path} is not a Fox Vision ROM image")

    if size is not None:
        if size * 2 > len(payload):
            raise ValueError(f"{path} header declares {size} words, but payload is shorter")
        payload = payload[:size * 2]
    elif len(payload) % 2:
        raise ValueError(f"{path} has an odd payload length")
    return start, list(struct.unpack(f">{len(payload) // 2}H", payload)), header_bytes


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("os_rom", type=Path, help="resident FoxOS ROM")
    parser.add_argument(
        "--embed-program",
        type=Path,
        action="append",
        required=True,
        help="foreground ROM to embed; repeat for several programs",
    )
    parser.add_argument(
        "--embed-name",
        action="append",
        required=True,
        help="name to create under /bin (1-8 ASCII characters); pairs with --embed-program in order",
    )
    parser.add_argument(
        "--embed-file",
        type=Path,
        action="append",
        default=[],
        help="data file to embed in /usr/include; repeat for several files",
    )
    parser.add_argument(
        "--embed-file-name",
        action="append",
        default=[],
        help="name to create under /usr/include (1-8 ASCII characters); pairs with --embed-file in order",
    )
    parser.add_argument("-o", "--output", type=Path, required=True)
    parser.add_argument("--embed-address", type=lambda value: int(value, 0), default=EMBED_ADDRESS)
    args = parser.parse_args()

    if len(args.embed_program) != len(args.embed_name):
        raise ValueError("--embed-program and --embed-name must be given the same number of times")
    if len(args.embed_file) != len(args.embed_file_name):
        raise ValueError("--embed-file and --embed-file-name must be given the same number of times")

    os_start, os_words, header = read_rom(args.os_rom)
    if os_start != 0 or not header.startswith(b".VFOX16EXT"):
        raise ValueError("OS ROM must be an extended image loaded at address 0")
    if len(os_words) > args.embed_address:
        raise ValueError("OS ROM overlaps the embedded-program address")

    combined = os_words + [0] * (args.embed_address - len(os_words))

    # Each record contains every embedded program, in the order given on the
    # command line, immediately followed by the next record. The shell walks
    # the chain until it finds a word that is not EMBED_MAGIC.
    for program_path, name in zip(args.embed_program, args.embed_name):
        program_start, program_words, _ = read_rom(program_path)
        if program_start != PROGRAM_BASE:
            raise ValueError(
                f"foreground program {program_path} must be built with "
                f"--load-address 0x{PROGRAM_BASE:X}"
            )
        # Records must stay below the foreground load address. The boot
        # loader copies the first embedded image to PROGRAM_BASE, so an
        # overlapping record would be overwritten.
        remaining = PROGRAM_BASE - len(combined)
        if not 0 < len(program_words) or len(program_words) + 11 > remaining:
            raise ValueError(
                f"embedded programs must end before 0x{PROGRAM_BASE:X}; "
                f"{program_path} needs {len(program_words) + 11} more words, "
                f"only {remaining} available"
            )
        try:
            embed_name = name.encode("ascii")
        except UnicodeEncodeError as error:
            raise ValueError("--embed-name must contain ASCII characters") from error
        if not 0 < len(embed_name) <= EMBED_NAME_MAX:
            raise ValueError(f"--embed-name must contain 1-{EMBED_NAME_MAX} characters")
        combined += [
            EMBED_MAGIC,
            len(embed_name),
            *embed_name,
            *([0] * (EMBED_NAME_MAX - len(embed_name))),
            len(program_words),
            *program_words,
        ]

    version = header[10] if len(header) > 10 else 0
    mapper = struct.unpack_from(">H", header, 11)[0] if len(header) >= 13 else 0
    max_limit = 0x80000000 if mapper == 2 or version == 3 else 0x8000
    if len(combined) > max_limit:
        raise ValueError("combined OS image exceeds extended ROM space")

    output_header = bytearray(header)
    if version == 3:
        size_offset = 23 if len(output_header) >= 27 else 21
        struct.pack_into(">I", output_header, size_offset, len(combined))
    elif version == 2:
        struct.pack_into(">H", output_header, 17, len(combined))
    elif version == 1:
        struct.pack_into(">H", output_header, 15, len(combined))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(bytes(output_header) + struct.pack(f">{len(combined)}H", *combined))
    print(f"embedded {len(args.embed_program)} program(s) into {args.output}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except ValueError as error:
        print(f"error: {error}")
        raise SystemExit(1)
