#!/usr/bin/env python3
import sys
import struct
import subprocess
import tempfile
import os
from pathlib import Path


def read_u8(f):
    b = f.read(1)
    if not b:
        raise EOFError
    return b[0]


def read_u32_le(f):
    bs = f.read(4)
    if len(bs) < 4:
        raise EOFError
    return struct.unpack("<I", bs)[0]


def read_i32_le(f):
    bs = f.read(4)
    if len(bs) < 4:
        raise EOFError
    return struct.unpack("<i", bs)[0]


def hexdump(bs, width=16, max_lines=8):
    lines = []
    limit = width * max_lines

    for i in range(0, min(len(bs), limit), width):
        chunk = bs[i:i + width]
        hexb = " ".join(f"{b:02X}" for b in chunk)
        ascii_ = "".join(chr(b) if 32 <= b < 127 else "." for b in chunk)
        lines.append(f"{i:08X}  {hexb:<{width*3}}  |{ascii_}|")

    if len(bs) > limit:
        lines.append(f"... ({len(bs)} bytes total)")

    return "\n".join(lines)


# -----------------------------
# TOOL RESOLUTION (IMPORTANT FIX)
# -----------------------------
def find_tool(name):
    from shutil import which
    return which(name)


def invoke_decompiler(section_data: bytes):
    decompiler = find_tool("vf16decompiler")

    if not decompiler:
        print("\n[warn] vf16decompiler not found in PATH")
        print("       install to ~/vf16/tools or add to PATH")
        return

    tmp_path = None

    try:
        with tempfile.NamedTemporaryFile(delete=False, suffix=".vf16bin") as tf:
            tf.write(b".VISOFOX16")
            tf.write(section_data)
            tmp_path = tf.name

        proc = subprocess.run(
            [decompiler, tmp_path],
            capture_output=True,
            text=True
        )

        print("\n-- Decompiler output --")
        if proc.stdout:
            print(proc.stdout)

        if proc.stderr:
            print("\n-- Decompiler stderr --", file=sys.stderr)
            print(proc.stderr, file=sys.stderr)

    finally:
        if tmp_path and os.path.exists(tmp_path):
            os.remove(tmp_path)


def inspect(path: Path):
    with path.open("rb") as f:
        magic = f.read(7)
        magic_s = magic.decode("ascii", errors="replace")

        print(f"Magic: {magic_s}")

        if magic_s != "VF16OBJ":
            print("Warning: unexpected magic")

        version = read_u8(f)
        print(f"Version: {version}")

        section_count = read_u32_le(f)
        symbol_count = read_u32_le(f)
        reloc_count = read_u32_le(f)

        print(f"Sections: {section_count}, Symbols: {symbol_count}, Relocations: {reloc_count}\n")

        sections = []

        # -----------------------------
        # SECTIONS
        # -----------------------------
        for i in range(section_count):
            name_len = read_u8(f)
            name = f.read(name_len).decode("ascii", errors="replace")
            flags = read_u8(f)
            size = read_u32_le(f)
            data = f.read(size)

            sections.append({"name": name, "data": data})

            print(f"[{i}] Section '{name}' flags=0x{flags:02X} size={size}")
            print(hexdump(data))
            print()

        # -----------------------------
        # SYMBOLS
        # -----------------------------
        for i in range(symbol_count):
            nlen = read_u8(f)
            name = f.read(nlen).decode("ascii", errors="replace")
            flags = read_u8(f)
            section_index = read_i32_le(f)
            offset = read_u32_le(f)

            print(f"[{i}] Symbol '{name}' flags=0x{flags:02X} sec={section_index} off=0x{offset:04X}")

        # -----------------------------
        # RELOCATIONS
        # -----------------------------
        for i in range(reloc_count):
            section_index = read_u32_le(f)
            offset = read_u32_le(f)
            sym_index = read_u32_le(f)
            rtype = read_u8(f)

            print(f"[{i}] Reloc: sec={section_index} off=0x{offset:04X} sym={sym_index} type={rtype}")

        # -----------------------------
        # LEFTOVER
        # -----------------------------
        rest = f.read()
        if rest:
            print(f"\nExtra {len(rest)} bytes at end:")
            print(hexdump(rest))

        # -----------------------------
        # DECOMPILER HOOK (FIXED)
        # -----------------------------
        text = next((s for s in sections if s["name"] == ".text"), None)

        if text:
            print("\nInvoking vf16decompiler on .text ...")
            invoke_decompiler(text["data"])
        else:
            print("\nNo .text section found.")


def main(argv):
    if len(argv) != 2:
        print("Usage: vf16obj_inspect.py <file>")
        return 2

    path = Path(argv[1])

    if not path.exists():
        print("File not found:", path)
        return 2

    try:
        inspect(path)
        return 0
    except EOFError:
        print("File truncated or corrupt")
        return 3


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))