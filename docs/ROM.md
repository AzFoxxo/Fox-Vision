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
| ROM format version |            1 | Format/version byte (major version number; version 2 adds a reset vector)     |
| Mapper             |            2 | ROM container mapping selector (0 = ROM4K, 1 = ROM32K) and layout policy      |
| ROM start          |            2 | 16-bit load address (big-endian) where the payload should be placed in memory |
| Reset vector       |            2 | 16-bit execution address (big-endian) used after reset                        |
| ROM size           |            2 | 16-bit payload length in words (big-endian)                                   |

Total extended header length: 17 bytes for version 1 and 19 bytes for version 2. After these fields the payload follows (the ROM words in big-endian file order). The emulator and other loaders SHOULD validate `ROM size` against the actual payload length and may reject mismatches.

### Semantics

- `ROM format version`: allows future changes to the container structure. Tools SHOULD support version 1 and version 2; version 2 introduces the `Reset vector` field. Tools may reject unknown higher versions unless explicitly configured to be permissive.
- `Mapping`: selects the ROM container size policy and the layout policy for ROM-resident data. `0` means ROM4K and `1` means ROM32K. This field does not replace the runtime `EM` register.
- `ROM start`: allows images to specify a non-zero load address (useful for relocation or alternative memory layouts). When a data segment is present, loaders SHOULD treat this field as the base offset for that segment so literal constants can be written directly into ROM.
- `Reset vector`: provides the address the CPU SHOULD jump to immediately after a hardware or power-on reset. If present, the machine starts execution at this address instead of assuming the payload begins at address zero.
- `ROM size`: the number of 16-bit words in the payload. Loaders SHOULD use this to avoid reading beyond the expected payload and to pre-allocate memory structures.

### ROM-resident constant layout

Extended-mode images MAY reserve a ROM-resident constant window for assembler-emitted literal data. In that case, the mapper value selects the ROM layout and the `ROM start` field identifies the base address where the constant window is written. The `Reset vector` field can point at a bootstrap or entry routine that transfers control into the program or data layout chosen by the image. This allows tools to place immutable words directly into the ROM image without requiring a runtime copy step.

Compatibility note: existing tools in this repository currently write a 10-byte magic only. Adopting the extended container header is a backwards-compatible evolution: legacy loaders expecting only the 10-byte magic should continue to work with legacy images, while updated loaders that inspect the post-magic bytes can enable the extended file-size limit when the `.VFOX16EXT` magic is present and the extended header parses successfully. Version 2 containers additionally provide a reset vector for reset-time execution.

### Payload structure

- Payload: a contiguous sequence of 16-bit words representing the program and data. Words are written in big-endian byte order in the file (most-significant byte first). The assembler uses big-endian encoding for portability; the emulator decodes words assuming big-endian file order.
- Footer: the generator appends two final words to every ROM payload: a `NOP` word followed by a `HLT` word to provide a safe termination sequence for simple ROMs. These are written as 16-bit opcode words after the program payload.
- Padding: if the payload byte-count after the 10-byte header is odd, the loader/payload decoder pads a single trailing zero byte when decoding to word values. This ensures the final ROM word is well-formed.

## Size limits and enforcement (current implementation)

- The assembler generator enforces a strict-format check when requested via the assembler's `--strict-format` option. In that mode the assembler enforces a maximum payload size of 2,048 words (4K words) excluding the 10-byte header. This preserves compatibility with legacy ROM size expectations.
- The emulator's runtime loader accepts ROMs up to 0x1000 words (4,096 words) when copying into the machine memory area; ROMs larger than this will be rejected by the emulator. The repository's configuration and extension-mode design allow larger ROM sizes conceptually (extension mode may expand ROM space up to 32K words), but the current emulator enforces the limits above.

## Notes for tool authors and integrators

- The file format is intentionally minimal and self-describing via the 10-byte header. Tools should preserve the header when producing ROM images.
- When writing ROMs from tools running on little-endian hosts, ensure big-endian word ordering is used in the file (the assembler in this repo already handles this).
- The generator appends a terminating `NOP`/`HLT` pair; toolchains that need precise control over footer words should emit their own termination sequence before finalising the payload.
