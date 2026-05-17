#!/usr/bin/env python3
import sys
import struct
import subprocess
import tempfile
import os
from pathlib import Path

def read_u8(f):
    b = f.read(1)
    if not b: raise EOFError
    return b[0]

def read_u32_le(f):
    bs = f.read(4)
    if len(bs) < 4: raise EOFError
    return struct.unpack('<I', bs)[0]

def read_i32_le(f):
    bs = f.read(4)
    if len(bs) < 4: raise EOFError
    return struct.unpack('<i', bs)[0]

def hexdump(bs, width=16, max_lines=8):
    lines = []
    for i in range(0, min(len(bs), width*max_lines), width):
        chunk = bs[i:i+width]
        hexb = ' '.join(f"{b:02X}" for b in chunk)
        ascii_ = ''.join((chr(b) if 32 <= b < 127 else '.') for b in chunk)
        lines.append(f"{i:08X}  {hexb:<{width*3}}  |{ascii_}|")
    if len(bs) > width*max_lines:
        lines.append(f"... ({len(bs)} bytes total)")
    return '\n'.join(lines)

def find_decompiler_project():
    # search upwards from current working directory for VF16Decompiler/VF16Decompiler.csproj
    cur = Path.cwd()
    for p in [cur] + list(cur.parents):
        candidate = p / 'VF16Decompiler' / 'VF16Decompiler.csproj'
        if candidate.exists():
            return str(candidate)
    return None

def try_invoke_decompiler_on_section(section_data: bytes, decompiler_proj: str):
    # write legacy header + payload (section_data is already big-endian words from the assembler)
    try:
        with tempfile.NamedTemporaryFile(delete=False, suffix='.bin') as tf:
            tf.write(b'.VISOFOX16')
            tf.write(section_data)
            tmpname = tf.name

        cwd = os.path.dirname(decompiler_proj) or os.getcwd()
        proc = subprocess.run([
            'dotnet', 'run', '--project', decompiler_proj, '--', tmpname
        ], cwd=cwd, capture_output=True, text=True)

        if proc.stdout:
            print('\n-- Decompiler output --')
            print(proc.stdout)
        if proc.stderr:
            print('\n-- Decompiler stderr --', file=sys.stderr)
            print(proc.stderr, file=sys.stderr)
    except Exception as ex:
        print('Failed to invoke decompiler:', ex)
    finally:
        try:
            os.remove(tmpname)
        except Exception:
            pass


def inspect(path: Path):
    with path.open('rb') as f:
        magic = f.read(7)
        try:
            magic_s = magic.decode('ascii')
        except Exception:
            magic_s = repr(magic)
        print(f"Magic: {magic_s}")
        if magic_s != 'VF16OBJ':
            print("Warning: magic doesn't match 'VF16OBJ'")
        version = read_u8(f)
        print(f"Version: {version}")
        section_count = read_u32_le(f)
        symbol_count = read_u32_le(f)
        reloc_count = read_u32_le(f)
        print(f"Sections: {section_count}, Symbols: {symbol_count}, Relocations: {reloc_count}\n")

        sections = []
        for i in range(section_count):
            name_len = read_u8(f)
            name = f.read(name_len).decode('ascii')
            flags = read_u8(f)
            size = read_u32_le(f)
            data = f.read(size)
            sections.append({'name': name, 'flags': flags, 'size': size, 'data': data})
            print(f"[{i}] Section '{name}' flags=0x{flags:02X} size={size}")
            print(hexdump(data))
            print()

        symbols = []
        for i in range(symbol_count):
            nlen = read_u8(f)
            name = f.read(nlen).decode('ascii')
            flags = read_u8(f)
            section_index = read_i32_le(f)
            offset = read_u32_le(f)
            symbols.append({'name': name, 'flags': flags, 'section_index': section_index, 'offset': offset})
            print(f"[{i}] Symbol '{name}' flags=0x{flags:02X} sec={section_index} off=0x{offset:04X}")

        print()
        relocs = []
        for i in range(reloc_count):
            section_index = read_u32_le(f)
            offset = read_u32_le(f)
            sym_index = read_u32_le(f)
            rtype = read_u8(f)
            relocs.append({'section_index': section_index, 'offset': offset, 'sym_index': sym_index, 'type': rtype})
            print(f"[{i}] Reloc: sec={section_index} off=0x{offset:04X} sym={sym_index} type={rtype}")

        print() 
        # show any remaining bytes
        rest = f.read()
        if rest:
            print(f"Extra {len(rest)} bytes at end of file:")
            print(hexdump(rest))

        # If we have a .text section and a decompiler project is available, invoke the decompiler on that section
        decompiler_proj = find_decompiler_project()
        if decompiler_proj:
            text_section = None
            for s in sections:
                if s['name'] == '.text':
                    text_section = s
                    break
            if text_section is not None:
                print('\nFound VF16Decompiler project; invoking on .text section...')
                try_invoke_decompiler_on_section(text_section['data'], decompiler_proj)
            else:
                print('\nVF16Decompiler found but no .text section present to decompile.')


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
    except EOFError:
        print("File ended unexpectedly (truncated or corrupt)")
        return 3
    return 0

if __name__ == '__main__':
    raise SystemExit(main(sys.argv))
