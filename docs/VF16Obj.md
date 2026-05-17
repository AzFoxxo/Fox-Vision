VF16Obj — VF16 Object File Format (simple ELF-like)
=================================================

Purpose
-------
VF16Obj is a minimal, ELF-inspired object file format for the Fox-Vision VF16 toolchain. It is designed to store relocatable sections, a symbol table, and relocations so a simple linker can combine multiple object files into a single VF16 binary.

High-level layout
-----------------
All integer fields are little-endian.

- Header (fixed):
  - Magic: 7 bytes ASCII "VF16OBJ" (0x56 0x46 0x31 0x36 0x4F 0x42 0x4A)
  - Version: 1 byte (currently 1)
  - Section count: u32
  - Symbol count: u32
  - Relocation count: u32

- Sections: repeated `Section count` times
  - Section name length: u8
  - Section name: ASCII (no NUL)
  - Section flags: u8 (bitflags, e.g., 1 = alloc, 2 = exec)
  - Section size: u32 (number of bytes)
  - Section data: `size` bytes

- Symbols: repeated `Symbol count` times
  - Name length: u8
  - Name: ASCII
  - Flags: u8 (1 = global, 2 = weak)
  - Section index: i32 (0-based section index, -1 for absolute/undefined)
  - Offset: u32 (offset inside section, ignored if section index == -1)

- Relocations: repeated `Relocation count` times
  - Section index: u32 (the section that contains the relocation site)
  - Offset: u32 (offset into the section where relocation will be applied)
  - Symbol index: u32 (index into the Symbols array that this relocation references)
  - Type: u8 (relocation type — e.g., 1 = ABS16)

Semantics
---------
- Sections are arbitrarily named (commonly `.text`, `.data`, `.bss`). The format stores section contents directly; `.bss` sections should be stored with zero size (size=0) and flagged appropriately, and the linker will allocate space for them.
- Symbols with the global flag are exported by the object file. An undefined symbol is represented by Section index == -1. The linker resolves undefined symbols by finding a global symbol with the same name in one of the inputs.
- Relocations encode where in a section a symbol's address must be written. The only relocation type required by the simple linker is `ABS16` (Type=1), which writes the 16-bit absolute address of the referenced symbol, little-endian, at the relocation offset.

Example usage with a simple linker
---------------------------------
1. Parse each VF16Obj file and collect sections, symbols and relocations.
2. Assign virtual addresses to each allocatable section (e.g., place `.text` at 0x0000 and follow with `.data`).
3. For each global symbol defined in a section, compute its absolute address = section_base + symbol.offset.
4. For each relocation, look up the symbol's absolute address and write the 16-bit value into the output image at (section_base + relocation.offset).

Notes and limitations
---------------------
- This is intentionally minimal: it targets a 16-bit address space and simple relocation needs for the VF16 educational CPU.
- Future extensions could include additional relocation types (PC-relative, hi/lo splits), symbol visibility, and debug sections.

Specification version history
----------------------------
- v1: initial simple spec.
