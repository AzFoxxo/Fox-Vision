# Fox Vision ROM File Format

The on-disk ROM format used by the assembler and emulator is a small wrapper around a sequence of 16-bit words (the machine words used by the CPU). The following rules describe the exact layout produced by the assembler and consumed by the emulator implementation in this repository:

- Header: an ASCII identifier written first and read by the emulator to identify the file as a Fox Vision ROM. Two header variants are recognised:
  - Legacy image header (default): the literal string `.VISOFOX16` (10 bytes). Legacy images use the simple 10-byte header only.
  - Extended-mode image header: the literal string `.VFOX16EXT` (10 bytes) followed by an extended header block. When the emulator observes the `.VFOX16EXT` header it SHOULD treat the file as an extended-mode container and parse the extended header fields described below.

## Extended-mode header (container) fields

Extended-mode ROM images are containers that include additional metadata immediately after the 10-byte magic. The extended header uses network (big-endian) byte ordering for multi-byte fields. The canonical layout is:

| Field              | Size (bytes) | Meaning                                                                       |
| ------------------ | -----------: | ----------------------------------------------------------------------------- |
| Magic              |           10 | Container magic (`.VFOX16EXT`)                                                |
| ROM format version |            1 | Format/version byte (1, 2, or 3; version 2 adds reset vector, version 3 uses 32-bit addresses and sizes) |
| Mapper             |            2 | ROM container mapping selector (0 = ROM4K, 1 = ROM32K, 2 = ROMByte8_32) and layout policy |
| ROM start          |       2 or 4 | Load address (big-endian; 16-bit in v1/v2, 32-bit in v3)                      |
| Reset vector       |       2 or 4 | Execution address used after reset (16-bit in v2, 32-bit in v3)               |
| ROM size           |       2 or 4 | Payload length in words (big-endian; 16-bit in v1/v2, 32-bit in v3)           |

Total extended header length: 17 bytes for version 1, 19 bytes for version 2, and 25 bytes (or 27 bytes with 32-bit mapper) for version 3. After these fields the payload follows (the ROM words in big-endian file order). The emulator and other loaders SHOULD validate `ROM size` against the actual payload length and may reject mismatches.

### Semantics

- `ROM format version`: allows changes to the container structure. Tools support version 1, version 2, and version 3. Version 2 introduces the `Reset vector` field. Version 3 expands `ROM start`, `Reset vector`, and `ROM size` to full 32-bit fields to handle larger ROM payloads and 32-bit address spaces.
- `Mapping`: selects the ROM container size policy and layout policy:
  - `0`: `ROM4K` (up to 4K words / 4,096 words).
  - `1`: `ROM32K` (up to 32K words / 32,768 words).
  - `2`: `ROMByte8_32` (supports up to the 32-bit limit, and initializes the CPU in 8-bit byte addressing mode `AM = 1` with 32-bit memory address space `EM = 2`).
- `ROM start`: allows images to specify a non-zero load address (useful for relocation or alternative memory layouts). When a data segment is present, loaders SHOULD treat this field as the base offset for that segment so literal constants can be written directly into ROM. In version 3, this is a 32-bit value.
- `Reset vector`: provides the address the CPU SHOULD jump to immediately after a hardware or power-on reset. In version 3, this is a 32-bit value.
- `ROM size`: the number of 16-bit words in the payload. In version 3, this is a 32-bit value, allowing images beyond 64K words up to the 32-bit limit.

### ROM-resident constant layout

Extended-mode images MAY reserve a ROM-resident constant window for assembler-emitted literal data. In that case, the mapper value selects the ROM layout and the `ROM start` field identifies the base address where the constant window is written. The `Reset vector` field can point at a bootstrap or entry routine that transfers control into the program or data layout chosen by the image. This allows tools to place immutable words directly into the ROM image without requiring a runtime copy step.

Compatibility note: existing tools in this repository currently write a 10-byte magic only. Adopting the extended container header is a backwards-compatible evolution: legacy loaders expecting only the 10-byte magic should continue to work with legacy images, while updated loaders that inspect the post-magic bytes can enable the extended file-size limit when the `.VFOX16EXT` magic is present and the extended header parses successfully. Version 2 containers additionally provide a reset vector for reset-time execution.

### Payload structure

- Payload: a contiguous sequence of 16-bit words representing the program and data. Words are written in big-endian byte order in the file (most-significant byte first). The assembler uses big-endian encoding for portability; the emulator decodes words assuming big-endian file order.
- Footer: the generator appends two final words to every ROM payload: a `NOP` word followed by a `HLT` word to provide a safe termination sequence for simple ROMs. These are written as 16-bit opcode words after the program payload.
- Padding: if the payload byte-count after the 10-byte header is odd, the loader/payload decoder pads a single trailing zero byte when decoding to word values. This ensures the final ROM word is well-formed.

## Size limits and enforcement (current implementation)

- The assembler generator enforces a strict-format check when requested via the assembler's `--strict-format` option. In that mode legacy images are limited to 4,096 payload words, while extended and `em2_am1` images are limited to 32,768 payload words.
- The emulator's runtime loader applies the limit selected by the container mapper: `rom4k` accepts up to `0x1000` words, `rom32k` up to `0x8000` words, and `rombyte8_32` up to `0x1_0000_0000` words when the CPU is in the 32-bit address space (`EM=2`). Images loaded at a non-zero start address must also fit within the selected address space.

## Notes for tool authors and integrators

- The file format is intentionally minimal and self-describing via the 10-byte header. Tools should preserve the header when producing ROM images.
- When writing ROMs from tools running on little-endian hosts, ensure big-endian word ordering is used in the file (the assembler in this repo already handles this).
- The generator appends a terminating `NOP`/`HLT` pair; toolchains that need precise control over footer words should emit their own termination sequence before finalising the payload.
