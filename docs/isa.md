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
| 0x7 | R0       | 16-bit | Yes        | General-purpose register (Introduced with ISA V1.11)                                                                                                                                                                |
| 0x8 | R1       | 16-bit | Yes        | General-purpose register (Introduced with ISA V1.11)                                                                                                                                                                |
| 0x9 | R2       | 16-bit | Yes        | General-purpose register (Introduced with ISA V1.11)                                                                                                                                                                |
| 0xA | R3       | 16-bit | Yes        | General-purpose register (Introduced with ISA V1.11)                                                                                                                                                                |
| 0xB | R4       | 16-bit | Yes        | General-purpose register (Introduced with ISA V1.11)                                                                                                                                                                |
| 0xC | R5       | 16-bit | Yes        | General-purpose register (Introduced with ISA V1.11)                                                                                                                                                                |
| 0xD | R6       | 16-bit | Yes        | General-purpose register (Introduced with ISA V1.11)                                                                                                                                                                |
| 0xE | R7       | 16-bit | Yes        | General-purpose register (Introduced with ISA V1.11)                                                                                                                                                                |



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

`CMP` (V1.5) writes all comparison bits using `X` vs `Y` and writes bit 0 using equality. Or general registers R0-R7 in V1.11 using operand supplied registers.

## Instruction Encoding

All instructions fetch 48 bits (3 words). None operand encoded ones such as `NOP` and `SRA` know how many operands they should take zero and one respectively whereas MOI instructions encode the operand count.

- Word 0: Opcode
- Word 1: Operand 1
- Word 2: Operand 2

Some instructions may ignore one or more operand words (e.g. `HLT`), but all three words are still fetched even if the CPU only advances one word.

See [ROM.md](ROM.md) for the ROM file format specification.

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

See [port_devices.md](port_devices.md) for detailed port device specifications including VF16Pad, VF16Keyboard, VF16Mouse, and VF16TTY.

### Multi-operand Instructions (V1.4)

Multi-operand Instructions (MOIs) use multiple operands to simplify writing assembly and reduce the need to set active registers.

MOI register operands support `X`, `Y`, and `STATUS` (source-only for status reads).

- `0000 0000` `0001 1001` - `MOV` `SRC` `DST`
- `0000 0000` `0001 1010` - `STR` `SRC` `DST`
- `0000 0000` `0001 1011` - `LOD` `SRC` `DST`

### Extended comparison and jump instructions (V1.5 and V1.11 revision)

Extended comparison and jump instructions (ECJI) are instructions which modernise comparison and jump logic to reduce assembly instructions and make better use of redundant space in the status register.

- `0000 0000` `0001 1100` - `CMP` - Compare `X` and `Y`, update Status comparison bits/Compare a given register to a second given register, update Status comparison bits (same opcode but with operand count set to 2)
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
- The ROM container's `.VFOX16EXT` header does not by itself enable V1.10 features; it selects the ROM4K/ROM32K mapping used for file-size handling, provides the reset vector used after reset, and defines the layout policy for any ROM-resident constant data segment

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

See [port_devices.md](port_devices.md) for port device specifications and port mappings.

