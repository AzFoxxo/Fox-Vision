# Specification of the Fox Vision architecture

## Processor
The CPU is a single threaded 8MHz RISC chip. It uses the FoxVision16 architecture.

### Registers
| ID  | Register | Size   | Read/Write | Description                                                                                                                                                                                                         |
| --- | -------- | ------ | ---------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 0x0 | X        | 16-bit | Yes        | General-purpose register #1                                                                                                                                                                                         |
| 0x1 | Y        | 16-bit | Yes        | General-purpose register #2                                                                                                                                                                                         |
| 0x2 | PC       | 16-bit | No         | Program counter (only modified internally by CPU control-flow logic; not directly accessible)                                                                                                                       |
| 0x3 | STATUS   | 8-bit  | Limited    | CPU flags register (written by CPU operations like CMP, DIV, HLT, CLR, and by `POP STATUS`)                                                                                                                         |
| 0x4 | SP       | 16-bit | Yes        | Stack Pointer. Points to the top of the stack in memory. Modified by PUSH/POP instructions and may be read/written directly for low-level control.                                                                  |
| 0x5 | CYC      | 16-bit | Read-only  | Global cycle counter. Increments by 1 every CPU cycle (wrapping at 0xFFFF → 0x0000). Represents elapsed CPU cycles since reset and is used for timing and synchronisation.                                          |
| 0x6 | EM       | 16-bit | Yes        | Extension Mode control register. Defaults to `0x0000`. Writing `0x0001` enables extension mode; writing `0x0000` disables it. Used to toggle extended CPU features and unlock up to 32K words of ROM address space. |

| Bit | Name                   | Meaning                                              |
| --- | ---------------------- | ---------------------------------------------------- |
| 0   | Equality / Result flag | `0x0` = false, `0x1` = true                          |
| 1   | Less-than flag         | `0x0` = false, `0x1` = true                          |
| 2   | Greater-than flag      | `0x0` = false, `0x1` = true                          |
| 3   | Not-equal flag         | `0x0` = false, `0x1` = true                          |
| 4   | Active register        | `0x0` = X register, `0x1` = Y register               |
| 5   | Illegal division flag  | `0x0` = OK, `0x1` = divide-by-zero occurred          |
| 6   | Halt flag              | `0x0` = continue execution, `0x1` = halt after cycle |
| 7   | Reserved               | Implementation-defined (must not be relied upon)     |


`JPZ` and `JNZ` evaluate bit 0. `EQU` writes bit 0 using equality. `LEQ` preserves legacy flow by writing bit 0 using the less-than result while also updating less-than/greater-than/not-equal bits.

`CMP` (V1.5) writes all comparison bits using `X` vs `Y` and writes bit 0 using equality.

## Instruction Encoding

All instructions fetch 48 bits (3 words). None operand encoded ones such as `NOP` and `SRA` know how many operands they should take zero and one respectively whereas MOI instructions encode the operand count.

- Word 0: Opcode
- Word 1: Operand 1
- Word 2: Operand 2

Some instructions may ignore one or more operand words (e.g. `HLT`), but all three words are still fetched.

## ROM File Format

The on-disk ROM format used by the assembler and emulator is a small wrapper around a sequence of 16-bit words (the machine words used by the CPU). The following rules describe the exact layout produced by the assembler and consumed by the emulator implementation in this repository:

- Header: an ASCII identifier written first and read by the emulator to identify the file as a Fox Vision ROM. Two header variants are recognised:
  - Legacy image header (default): the literal string `.VISOFOX16` (10 bytes). Legacy images use the simple 10-byte header only.
  - Extended-mode image header: the literal string `.VFOX16EXT` (10 bytes) followed by an extended header block. When the emulator observes the `.VFOX16EXT` header it SHOULD treat the file as an extended-mode container and parse the extended header fields described below.

### Extended-mode header (container) fields

Extended-mode ROM images are containers that include additional metadata immediately after the 10-byte magic. The extended header uses network (big-endian) byte ordering for multi-byte fields. The canonical layout is:

| Field              | Size (bytes) | Meaning                                                                       |
| ------------------ | -----------: | ----------------------------------------------------------------------------- |
| Magic              |           10 | Container magic (`.VFOX16EXT`)                                                |
| ROM format version |            1 | Format/version byte (major version number)                                    |
| Mapper             |            2 | ROM container mapping selector (0 = ROM4K, 1 = ROM32K)                        |
| ROM start          |            2 | 16-bit load address (big-endian) where the payload should be placed in memory |
| ROM size           |            2 | 16-bit payload length in words (big-endian)                                   |

Total extended header length: 16 bytes. After these fields the payload follows (the ROM words in big-endian file order). The emulator and other loaders SHOULD validate `ROM size` against the actual payload length and may reject mismatches.

Semantics:

- `ROM format version`: allows future changes to the container structure. Tools SHOULD support version 1 and may reject unknown higher versions unless explicitly configured to be permissive.
- `Mapping`: selects the ROM container size policy only. `0` means ROM4K and `1` means ROM32K. This field does not replace the runtime `EM` register.
- `ROM start`: allows images to specify a non-zero load address (useful for relocation or alternative memory layouts). Loaders MAY ignore this field and place the payload at address 0, but SHOULD respect it when present and supported.
- `ROM size`: the number of 16-bit words in the payload. Loaders SHOULD use this to avoid reading beyond the expected payload and to pre-allocate memory structures.

Compatibility note: existing tools in this repository currently write a 10-byte magic only. Adopting the extended container header is a backwards-compatible evolution: legacy loaders expecting only the 10-byte magic should continue to work with legacy images, while updated loaders that inspect the post-magic bytes can enable the extended file-size limit when the `.VFOX16EXT` magic is present and the extended header parses successfully.
- Payload: a contiguous sequence of 16-bit words representing the program and data. Words are written in big-endian byte order in the file (most-significant byte first). The assembler uses big-endian encoding for portability; the emulator decodes words assuming big-endian file order.
- Footer: the generator appends two final words to every ROM payload: a `NOP` word followed by a `HLT` word to provide a safe termination sequence for simple ROMs. These are written as 16-bit opcode words after the program payload.
- Padding: if the payload byte-count after the 10-byte header is odd, the loader/payload decoder pads a single trailing zero byte when decoding to word values. This ensures the final ROM word is well-formed.

Size limits and enforcement (current implementation):

- The assembler generator enforces a strict-format check when requested via the assembler's `--strict-format` option. In that mode the assembler enforces a maximum payload size of 2,048 words (4K words) excluding the 10-byte header. This preserves compatibility with legacy ROM size expectations.
- The emulator's runtime loader accepts ROMs up to 0x1000 words (4,096 words) when copying into the machine memory area; ROMs larger than this will be rejected by the emulator. The repository's configuration and extension-mode design allow larger ROM sizes conceptually (extension mode may expand ROM space up to 32K words), but the current emulator enforces the limits above.

Notes for tool authors and integrators:

- The file format is intentionally minimal and self-describing via the 10-byte header. Tools should preserve the header when producing ROM images.
- When writing ROMs from tools running on little-endian hosts, ensure big-endian word ordering is used in the file (the assembler in this repo already handles this).
- The generator appends a terminating `NOP`/`HLT` pair; toolchains that need precise control over footer words should emit their own termination sequence before finalising the payload.


---

## Opcode Format (Word 0)

The opcode word is split into two 8-bit fields:

- High byte (bits 15–8): Opcode ID
- Low byte (bits 7–0): Operand interpretation control

---

## Operand Interpretation Control (Low byte)

The low byte defines how the two operand words are interpreted:

- Bits 0–1: Operand count
- Bits 2–3: Operand 1 type
- Bits 4–5: Operand 2 type
- Bits 6–7: Reserved

---

## Operand Types (2-bit encoding)

- `00` = Register
- `01` = Immediate
- `10` = Direct Memory Address
- `11` = Indirect Memory Address

## CPU opcodes (V1.0)
- `0000 0000` `0000 0000` - `NOP` - Waste clock cycle
- `0000 0000` `0000 0001` - `LFM` - Load 2 byte value from memory in active register (Legacy mode only)
- `0000 0000` `0000 0010` - `WTM` - Write to memory the value of the active register (Legacy mode only)
- `0000 0000` `0000 0011` - `SRA` - Set register active (X - 0, Y - 1) (Legacy mode only)
- `0000 0000` `0000 0100` - `AXY` - Add X and Y and store result in active register (Legacy mode only)
- `0000 0000` `0000 0101` - `SXY` - Subtract X from Y and store result in active register (Legacy mode only)
- `0000 0000` `0000 0110` - `MXY` - Multiply X by Y and store result in active register (Legacy mode only)
- `0000 0000` `0000 0111` - `DXY` - Divide X by Y and store result in active register (Legacy mode only)
- `0000 0000` `0000 1000` - `EQU` - Check if X and Y registers are equal (Legacy mode only)
- `0000 0000` `0000 1001` - `LEQ` - Check if X register is less than Y register (Legacy mode only)
- `0000 0000` `0000 1010` - `JPZ` - Jump if zero to 2 byte wide address (Legacy mode only)
- `0000 0000` `0000 1011` - `JNZ` - Jump if not zero to 2 byte wide address (Legacy mode only)
- `0000 0000` `0000 1100` - `JMP` - Jump to 2 byte wide address
- `0000 0000` `0000 1101` - `CLR` - Clear all Status register bits (set to zero) (Legacy mode only)
- `0000 0000` `0000 1110` - `HLT` - Halt program execution (quit/power-off)
- `0000 0000` `0000 1111` - `BSL` - Bitshift left value in active register (Legacy mode only)
- `0000 0000` `0001 0000` - `BSR` - Bitshift right value in active register (Legacy mode only)
- `0000 0000` `0001 0001` - `AND` - AND bitwise value in active register by value in non-active register (Legacy mode only)
- `0000 0000` `0001 0010` - `ORA` - OR bitwise value in active register by value in non-active register (Legacy mode only)
- `0000 0000` `0001 0011` - `XOR` - XOR bitwise value in active register by value in non-active register (Legacy mode only)
- `0000 0000` `0001 0100` - `DWR` - Direct write sets the given 16 bit value to the active register (Legacy mode only)

### CPU usability additions (V1.1)
- `0000 0000` `0001 0101` - `ILM` - Indirect load from memory - load address stored in active register (Legacy mode only)
- `0000 0000` `0001 0110` - `IWR` - Indirect write register to memory - write value in active register to address stored in inactive register (Legacy mode only)

### CPU shorthand additions (V1.2)
- `0000 0000` `0001 0111` - `INC` - Increase the value in the active register by one (Legacy mode only)
- `0000 0000` `0001 1000` - `DEC` - Decrease the value in the active register by one (Legacy mode only)

### Input Output Controls (V1.3, deprecated)

V1.3 controller support is deprecated. To use controllers, ROMs must run in extended mode (`EM = 0x0001`) and read controller state through configured ports.

**NOTE:** V1.10 (`EM=1` mode) introduces ports. When enabled, the system exposes eight generic 16-bit I/O ports (`0x0000`–`0x0007`). Ports are simple bidirectional data endpoints with no fixed semantic meaning in the ISA. Device behaviour is defined externally by the emulator/system configuration.

In a typical configuration, controller devices may be mapped by the emulator to PORT0 (`0x0000`) and PORT1 (`0x0001`), each exposing a 16-bit input state. This mapping is not part of the CPU specification and is fully implementation-defined.

Device state (including controllers) is maintained continuously by the emulator as a live snapshot. The CPU does not receive input events and does not rely on buffering, latching, or consumption semantics.

Each `IN` instruction reads the current state of the connected device at the time of execution.

Frame timing (including VBlank) is a system-level synchronisation mechanism used for rendering and scheduling. It is not part of the I/O system and is not transmitted through ports or memory-mapped registers.

Ports do not latch, queue, clear, or acknowledge input. They always return the most recent device state.

---

## Basic VF16Pad Port Layout

Controller state is a level-based snapshot exposed by a configured port device:

- `1` = Button is currently held
- `0` = Button is not held

State is continuously updated by the emulator from host input events.

Bit layout:

| Bit | Button |
| --- | ------ |
| 0   | Up     |
| 1   | Down   |
| 2   | Left   |
| 3   | Right  |
| 4   | A      |
| 5   | B      |
| 6   | Start  |
| 7   | Select |

Examples:

- `00000000` = No buttons held
- `00010000` = A held
- `01000000` = Start held
- `00001001` = Up + Right held

---

## Programming Model

- Programs read controller state through the configured port device.
- Input is polled; it is not event-driven.
- No explicit clearing or acknowledgment is required.

### Multi-operand Instructions (V1.4)

Multi-operand Instructions (MOIs) use multiple operands to simplify writing assembly and reduce the need to set active registers.

MOI register operands support `X`, `Y`, and `STATUS` (source-only for status reads).

- `0000 0000` `0001 1001` - `MOV` `SRC` `DST`
- `0000 0000` `0001 1010` - `STR` `SRC` `DST`
- `0000 0000` `0001 1011` - `LOD` `SRC` `DST`

### Extended comparison and jump instructions (V1.5)

Extended comparison and jump instructions (ECJI) are instructions which modernise comparison and jump logic to reduce assembly instructions and make better use of redundant space in the status register.

- `0000 0000` `0001 1100` - `CMP` - Compare `X` and `Y`, update Status comparison bits
- `0000 0000` `0001 1101` - `JEQ` - Jump if equal (Status bit 0)
- `0000 0000` `0001 1110` - `JNE` - Jump if not equal (Status bit 3)
- `0000 0000` `0001 1111` - `JLT` - Jump if less-than (Status bit 1)
- `0000 0000` `0010 0000` - `JGT` - Jump if greater-than (Status bit 2)
- `0000 0000` `0010 0001` - `JLE` - Jump if less-than or equal
- `0000 0000` `0010 0010` - `JGE` - Jump if greater-than or equal

### Multi-operand ALU instructions (V1.6)

V1.6 adds arithmetic and bitwise MOIs using the form `OP SRC DST`.

- `SRC` can be an immediate 16-bit value or a register operand (`X`/`Y`)
- `SRC` can be an immediate 16-bit value or a register operand (`X`/`Y`/`STATUS`)
- `DST` must be a register operand (`X`/`Y`)
- Result is written to `DST`
- Arithmetic wraps to 16 bits

Instruction list:

- `0000 0000` `0010 0011` - `ADD` `SRC` `DST` - `DST = DST + SRC`
- `0000 0000` `0010 0100` - `SUB` `SRC` `DST` - `DST = DST - SRC`
- `0000 0000` `0010 0101` - `MUL` `SRC` `DST` - `DST = DST * SRC`
- `0000 0000` `0010 0110` - `DIV` `SRC` `DST` - `DST = DST / SRC` (divide by zero sets illegal division flag and writes `0`)
- `0000 0000` `0010 0111` - `AND` `SRC` `DST` - `DST = DST & SRC`
- `0000 0000` `0010 1000` - `OR` `SRC` `DST` - `DST = DST | SRC`
- `0000 0000` `0010 1001` - `XOR` `SRC` `DST` - `DST = DST ^ SRC`
- `0000 0000` `0010 1010` - `SHL` `SRC` `DST` - `DST = DST << SRC`
- `0000 0000` `0010 1011` - `SHR` `SRC` `DST` - `DST = DST >> SRC`

Legacy one-word ALU instructions remain available:

- `AXY`/`SXY`/`MXY`/`DXY`
- `AND`/`ORA`/`XOR` (active/inactive register form)
- `BSL`/`BSR`

When these legacy mnemonics are written with two operands (for example `AND X Y`), the assembler emits the V1.6 MOI opcode.

### Stack Instructions (V1.7)

- `0000 0000` `0010 1100` - `PUSH SRC` - Push register `SRC` (`X`/`Y`/`SP`/`STATUS`) onto the stack
- `0000 0000` `0010 1101` - `POP DST` - Pop the top of stack into register `DST` (`X`/`Y`/`SP`/`STATUS`)

`PUSH`/`POP` consume one operand word (register id) and no longer depend on the active-register bit.

### Timing and Cycle Control (V1.8)

V1.8 introduces a global cycle counter and a blocking timing instruction for deterministic execution delays.

- A new read-only 16-bit register `CYC` is introduced
- `CYC` increments by 1 every CPU cycle
- `CYC` wraps from `0xFFFF → 0x0000`
- Represents total elapsed CPU cycles since reset
- Used for timing, scheduling, and frame control

---

### WAIT instruction

- `0000 0000` `0010 1110` - `WAIT` `SRC` - Stall execution until cycle delay has elapsed

Where:
- `SRC` is a 16-bit immediate or register value

Behaviour:
- CPU captures current `CYC` value at execution start
- CPU enters WAIT state (execution stalls)
- Program counter and registers remain frozen
- No instructions are fetched or executed during WAIT state
- `CYC` continues to increment normally
- Execution resumes when:
  - `(CYC - start) >= SRC`

---

### Execution model notes

- WAIT is a blocking CPU state, not a no-op loop
- It does not consume instruction flow while stalled
- Designed for deterministic timing (animation, frame pacing, delays)
- Can be used as a lightweight timing primitive in place of interrupts

### VBlank synchronisation (V1.9)

Fox Vision exposes a VBlank-style synchronisation point once per rendered frame.

- `0000 0000` `0010 1111` - `VBLANK` - Stall until the next frame VBlank signal

Behaviour:

- The emulator raises a VBlank signal once per display refresh tick
- `VBLANK` blocks execution until the next signal is observed
- Program counter and registers remain frozen while stalled
- `CYC` continues to advance normally while the CPU is blocked
- This is intended for frame pacing, VRAM updates, and animation loops

Typical usage:

- Render or update game state
- Call `VBLANK`
- Repeat on the next frame

### Extension mode and ports (V1.10)

Fox Vision introduces a machine extension mode control register.

- The default value is `0x0000` for legacy mode
- Setting the register to `0x0001` enables extension mode and V1.10 features at runtime
- The ROM container's `.VFOX16EXT` header does not by itself enable V1.10 features; it only selects the ROM4K/ROM32K mapping used for file-size handling

When extension mode is enabled (`0x0001`):

- `SRA` is disabled
- Legacy extension debug opcodes (EDO) are unavailable
- Memory-mapped I/O is removed except for VRAM
- Port I/O becomes available

Programs that use port I/O should initialize the runtime mode register before their first `IN` or `OUT` instruction by writing `0x0001` to `EM`:

- `MOV %1 EM`

Compilers that target V1.10 extended mode are expected to emit this initialization at the start of the program image.

Ports are not memory-mapped.

- Eight ports are exposed
- Each port is 16 bits wide
- Port numbers are encoded as 16-bit immediate values in the range `0x0000` to `0x0007`
- Port `0x0000` through `0x0007` are the only valid IN/OUT targets

Port I/O instructions use an immediate operand for the port number:

- `0000 0000` `0011 0000` - `IN`  `PORT DST` - Read the 16-bit value from port `PORT` into register `DST`
- `0000 0000` `0011 0001` - `OUT` `SRC PORT` - Write the 16-bit value from register `SRC` to port `PORT`

### Extension debug opcodes (EDO) - Legacy mode only

**Note:** These are instructions which are only used for testing the virtual machine. They allow console I/O, printing memory, etc. They are only available in legacy mode (`0x0000`).

**Note:** All extension debug instructions start with `11` so the first instruction is `1100 0000` `0000 0000`.

- `1100 0000` `0000 0000` - `DBG_LGC` - Log a character to the console
- `1100 0000` `0000 0001` - `DBG_MEM` - Log the memory in hex to the console
- `1100 0000` `0000 0010` - `DBG_INP` - Prompt the user for input which is then converted to an unsigned uint16

Compatibility aliases for older sources are also accepted by the assembler:

- `DGB_MEM` maps to `DBG_MEM`
- `DGB_INP` maps to `DBG_INP`

## Extension debug character encoding

**Note:** Unknown displays `?` when outputting and unknown reading in is converted to `40`

- `#` = `0`
- `A-Z` = `1-26`
- `-` = `27`
- `0-9` = `28-37`
- Newline = `38`
- Space = `39`

### Instruction breakdown

first 16 bits: opcode
second 16 bits: addresses/values (if applicable)
third 16 bits: addresses/values (if applicable)

## Graphics

Fox Vision supports a display size of 100x100 with four bits used to represent each colour (colours are predefined) and retrieves this data from RAM 60 times a second to display it.

The total memory size for an uncompressed frame is:

`4bits * (100 * 100) = 40000 bits (5000 bytes or 5kb approx)`

VRAM starts at address `FFFF` and descends the next 5000 bytes.

`FFFF` corresponds to top-left corner, moving right then down.

## Memory

The device has a total of 65,536 ushort (64K words) of addressable space.

This memory is broken up into several sections.

### Legacy mode (`EM = 0x0000`)

- `0x0000 - 0x0FFF` ROM (4K words)
- `0xEC78 - 0xFFFF` VRAM
- Remaining space available for RAM and VRAM

### Extension mode (`EM = 0x0001`)

- ROM may expand to use up to 32K words of address space
- VRAM remains fixed at 5000 bytes (`0xEC78 - 0xFFFF`)
- Memory-mapped I/O is removed except for VRAM
- Remaining space available for RAM and VRAM

## Port devices

The machine supports 8 port devices, these are not defined by the CPU ISA as they are platform devices.

They are all use bidirectional single lane to send/retrieve data and use same port type `VF16P` which delivers power and offers single lane for data transfers.

## VF16Pad

Controller state is a level-based snapshot exposed by a configured port device:

- `1` = Button is currently held
- `0` = Button is not held

State is continuously updated by the emulator from host input events.

Bit layout:

| Bit | Button |
| --- | ------ |
| 0   | Up     |
| 1   | Down   |
| 2   | Left   |
| 3   | Right  |
| 4   | A      |
| 5   | B      |
| 6   | Start  |
| 7   | Select |
