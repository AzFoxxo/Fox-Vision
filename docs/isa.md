# Specification of the Fox Vision architecture (ISA V2.0)

## Processor

The CPU is a single threaded 8MHz RISC chip. It uses the FoxVision architecture with support for FoxVision16 legacy operations and FoxVision32 extended operations (introduced in V2.0), featuring 32-bit registers and a 32-bit address bus with configurable 16-bit or 8-bit addressing modes.

### Registers

In ISA V2.0, registers are 32-bit wide to support the 32-bit address bus. For 16-bit operations and backwards compatibility, each 32-bit register exposes high (`H`) and low (`L`) 16-bit sub-register versions:
- **Base register** (e.g. `R0`, `X`): operates on the full 32 bits (`bits 31..0`).
- **Lower sub-register** (e.g. `R0L`, `XL`): operates on the lower 16 bits (`bits 15..0`).
- **Higher sub-register** (e.g. `R0H`, `XH`): operates on the upper 16 bits (`bits 31..16`).

Writing to an `L` sub-register updates bits 15–0 while preserving bits 31–16 of the underlying 32-bit register. Writing to an `H` sub-register updates bits 31–16 while preserving bits 15–0. Writing to the base 32-bit register updates all 32 bits.

#### Primary Registers

| ID  | Register | Size   | Read/Write | Description                                                                                                                                                                                                                                                                          |
| --- | -------- | ------ | ---------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 0x0 | X        | 32-bit | Yes        | General-purpose register #1 (full 32-bit). Aliased by `XL` (low 16) and `XH` (high 16).                                                                                                                                                                                              |
| 0x1 | Y        | 32-bit | Yes        | General-purpose register #2 (full 32-bit). Aliased by `YL` (low 16) and `YH` (high 16).                                                                                                                                                                                              |
| 0x2 | PC       | 32-bit | No         | Program counter (32-bit address bus pointer; modified by control-flow logic). Aliased by `PCL` and `PCH`.                                                                                                                                                                            |
| 0x3 | STATUS   | 8-bit  | Limited    | CPU flags register (written by CPU operations like CMP, DIV, HLT, CLR, and by `POP STATUS`).                                                                                                                                                                                         |
| 0x4 | SP       | 32-bit | Yes        | Stack Pointer (full 32-bit memory address). Points to top of stack. Aliased by `SPL` and `SPH`.                                                                                                                                                                                      |
| 0x5 | CYC      | 32-bit | Read-only  | Global cycle counter (wrapping at 0xFFFFFFFF → 0x00000000). Represents elapsed CPU cycles since reset. Aliased by `CYCL` and `CYCH`.                                                                                                                                                 |
| 0x6 | EM       | 16-bit | Yes        | Extension Mode control register. Defaults to `0x0000`. `0x0000` = legacy mode; `0x0001` = extension mode (16-bit address space, up to 32K words ROM, port I/O); `0x0002` = 32-bit addressing space for memory (unlocks 32-bit address bus). Used alongside `AM` for modern hardware. |
| 0x7 | R0       | 32-bit | Yes        | General-purpose register #0 (Introduced with ISA V1.11, extended to 32-bit in V2.0). Aliased by `R0L` and `R0H`.                                                                                                                                                                     |
| 0x8 | R1       | 32-bit | Yes        | General-purpose register #1 (Introduced with ISA V1.11, extended to 32-bit in V2.0). Aliased by `R1L` and `R1H`.                                                                                                                                                                     |
| 0x9 | R2       | 32-bit | Yes        | General-purpose register #2 (Introduced with ISA V1.11, extended to 32-bit in V2.0). Aliased by `R2L` and `R2H`.                                                                                                                                                                     |
| 0xA | R3       | 32-bit | Yes        | General-purpose register #3 (Introduced with ISA V1.11, extended to 32-bit in V2.0). Aliased by `R3L` and `R3H`.                                                                                                                                                                     |
| 0xB | R4       | 32-bit | Yes        | General-purpose register #4 (Introduced with ISA V1.11, extended to 32-bit in V2.0). Aliased by `R4L` and `R4H`.                                                                                                                                                                     |
| 0xC | R5       | 32-bit | Yes        | General-purpose register #5 (Introduced with ISA V1.11, extended to 32-bit in V2.0). Aliased by `R5L` and `R5H`.                                                                                                                                                                     |
| 0xD | R6       | 32-bit | Yes        | General-purpose register #6 (Introduced with ISA V1.11, extended to 32-bit in V2.0). Aliased by `R6L` and `R6H`.                                                                                                                                                                     |
| 0xE | R7       | 32-bit | Yes        | General-purpose register #7 (Introduced with ISA V1.11, extended to 32-bit in V2.0). Aliased by `R7L` and `R7H`.                                                                                                                                                                     |
| 0xF | AM       | 16-bit | Yes        | Addressing Mode control register (Introduced with ISA V2.0). Defaults to `0x0000`. `0` = 16-bit word addressing, `1` = 8-bit byte addressing. Usable independently from `EM`, but combined with `EM=2` for modern hardware.                                                          |

#### 16-Bit Sub-Registers (H / L Versions)

Each 32-bit register provides direct 16-bit operand access to its lower 16 bits (`L`) and upper 16 bits (`H`):

| ID   | Sub-Register | Parent | Bits    | Description                               |
| ---- | ------------ | ------ | ------- | ----------------------------------------- |
| 0x10 | XL           | X      | [15:0]  | X lower 16 bits                           |
| 0x11 | XH           | X      | [31:16] | X upper 16 bits                           |
| 0x12 | YL           | Y      | [15:0]  | Y lower 16 bits                           |
| 0x13 | YH           | Y      | [31:16] | Y upper 16 bits                           |
| 0x14 | PCL          | PC     | [15:0]  | PC lower 16 bits                          |
| 0x15 | PCH          | PC     | [31:16] | PC upper 16 bits                          |
| 0x16 | SPL          | SP     | [15:0]  | SP lower 16 bits                          |
| 0x17 | SPH          | SP     | [31:16] | SP upper 16 bits                          |
| 0x18 | CYCL         | CYC    | [15:0]  | Cycle counter lower 16 bits               |
| 0x19 | CYCH         | CYC    | [31:16] | Cycle counter upper 16 bits               |
| 0x20 | R0L          | R0     | [15:0]  | R0 lower 16 bits (general-purpose 16-bit) |
| 0x21 | R0H          | R0     | [31:16] | R0 upper 16 bits (general-purpose 16-bit) |
| 0x22 | R1L          | R1     | [15:0]  | R1 lower 16 bits (general-purpose 16-bit) |
| 0x23 | R1H          | R1     | [31:16] | R1 upper 16 bits (general-purpose 16-bit) |
| 0x24 | R2L          | R2     | [15:0]  | R2 lower 16 bits (general-purpose 16-bit) |
| 0x25 | R2H          | R2     | [31:16] | R2 upper 16 bits (general-purpose 16-bit) |
| 0x26 | R3L          | R3     | [15:0]  | R3 lower 16 bits (general-purpose 16-bit) |
| 0x27 | R3H          | R3     | [31:16] | R3 upper 16 bits (general-purpose 16-bit) |
| 0x28 | R4L          | R4     | [15:0]  | R4 lower 16 bits (general-purpose 16-bit) |
| 0x29 | R4H          | R4     | [31:16] | R4 upper 16 bits (general-purpose 16-bit) |
| 0x2A | R5L          | R5     | [15:0]  | R5 lower 16 bits (general-purpose 16-bit) |
| 0x2B | R5H          | R5     | [31:16] | R5 upper 16 bits (general-purpose 16-bit) |
| 0x2C | R6L          | R6     | [15:0]  | R6 lower 16 bits (general-purpose 16-bit) |
| 0x2D | R6H          | R6     | [31:16] | R6 upper 16 bits (general-purpose 16-bit) |
| 0x2E | R7L          | R7     | [15:0]  | R7 lower 16 bits (general-purpose 16-bit) |
| 0x2F | R7H          | R7     | [31:16] | R7 upper 16 bits (general-purpose 16-bit) |



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

`CMP` (V1.5) writes all comparison bits using `X` vs `Y` and writes bit 0 using equality. General registers R0-R7 were added in V1.11 using operand supplied registers. In V2.0, `CMP` supports comparing 32-bit registers (e.g. `R0` vs `R1`) as well as 16-bit H/L sub-registers (e.g. `R0L` vs `R1L`).

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

## Legacy Instructions

**Note**: Set Active Register (SRA) instructions are considered legacy and cease working once you enable extended mode for machine and these Multiple Operand Instructions (MOIs) should be used instead:

- `LFM` → `LOD`
- `WTM` → `STR`
- `SRA` → Explicit register operands (X / Y and R0-R7 in V1.11 onwards)
- `AXY` → `ADD` X Y
- `SXY` → `SUB` X Y
- `MXY` → `MUL` X Y
- `DXY` → `DIV` X Y
- `EQU` → `CMP` X Y + JEQ
- `LEQ` → `CMP` X Y + JLE
- `JPZ` → `JEQ`
- `JNZ` → `JNE`
- `BSL` → `SHL`
- `BSR` → `SHR`
- `AND` (one operand) → AND SRC DST
- `ORA` → `OR` SRC DST
- `XOR` (one operand) → XOR SRC DST
- `DWR` → `MOV` immediate DST
- `ILM` → `LOD`
- `IWR` → `STR`
- `INC` → `ADD` 1 DST
- `DEC` → `SUB` 1 DST

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
- `0000 0000` `0000 1101` - `CLR` - Clear all Status register bits (set to zero) (Partly-legacy mode, one bit contains active register)
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

In V2.0, `MOV` between two primary registers (`X`, `Y`, `R0`–`R7`, `SP`, `PC`, `CYC`) copies the full 32 bits. A move whose source is an immediate or an `H`/`L` sub-register remains 16 bits wide and zero-extends when the destination is a primary register; writing an `H`/`L` destination preserves the other half of the underlying register.

`LOD` and `STR` transfer one addressing unit: one 16-bit word when `AM=0`, or one 8-bit byte when `AM=1`. They do not transfer a full 32-bit register in one instruction.

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

- `SRC` can be an immediate 16-bit value or a register operand (`X`/`Y`/`R0`–`R7`/`STATUS`)
- `DST` must be a register operand (`X`/`Y`/`R0`–`R7` or an `H`/`L` sub-register)
- Result is written to `DST`
- When `DST` is a primary 32-bit register, the operation is performed on all 32 bits; when `DST` is an `H`/`L` sub-register, the operation is performed on 16 bits and the other half of the base register is preserved
- Arithmetic wraps to the destination width (32 bits or 16 bits)

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

- `0000 0000` `0010 1100` - `PUSH SRC` - Push register `SRC` onto the stack
- `0000 0000` `0010 1101` - `POP DST` - Pop the top of stack into register `DST`

`PUSH`/`POP` consume one operand word (register id) and no longer depend on the active-register bit.

In V2.0 the transferred width follows the register class:

- Primary 32-bit registers (`X`, `Y`, `SP`, `R0`–`R7`) transfer 32 bits.
- `H`/`L` sub-registers and 16-bit control registers (`STATUS`, `EM`, `AM`) transfer 16 bits.
- The stack step is addressing-mode aware: a 32-bit value occupies 4 bytes under `AM=1` or 2 words under `AM=0`; a 16-bit value occupies 2 bytes under `AM=1` or 1 word under `AM=0`.

### Timing and Cycle Control (V1.8)

V1.8 introduces a global cycle counter and a blocking timing instruction for deterministic execution delays.

- A new read-only 32-bit register `CYC` is introduced (aliased by `CYCL`/`CYCH`)
- `CYC` increments by 1 every CPU cycle
- `CYC` wraps from `0xFFFFFFFF → 0x00000000`
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

### Extension mode and ports (V1.10 and V2.0 revision)

Fox Vision introduces a machine extension mode control register (`EM`).

- `0x0000`: Legacy mode (16-bit address space, 4K words ROM)
- `0x0001`: Extension mode (16-bit address space, up to 32K words ROM, port I/O enabled)
- `0x0002`: 32-bit addressing space for memory (introduced in V2.0). Unlocks a 32-bit address bus for memory addressing.
- The ROM container's `.VFOX16EXT` header does not by itself enable V1.10/V2.0 features; it selects the ROM4K/ROM32K mapping used for file-size handling, provides the reset vector used after reset, and defines the layout policy for any ROM-resident constant data segment

When extension mode is enabled (`EM = 0x0001` or `EM = 0x0002`):

- `SRA` is disabled
- Legacy extension debug opcodes (EDO) are unavailable
- Memory-mapped I/O is removed except for VRAM
- Port I/O becomes available

Programs that use port I/O should initialize the runtime mode register before their first `IN` or `OUT` instruction by writing `0x0001` (or `0x0002` for 32-bit addressing space) to `EM`:

- `MOV %1 EM`  ; Extended 16-bit mode
- `MOV %2 EM`  ; Extended 32-bit memory addressing mode

Compilers that target extended mode are expected to emit this initialization at the start of the program image.

Ports are not memory-mapped.

- Each port is 16 bits wide
- Port numbers are encoded as 16-bit immediate values in the range `0x0000` to `0xFFFF` (0 to 65,535)
- Ports `0x0000` through `0x0007` map to default emulator devices; ports `0x0008` through `0xFFFF` are available for system expansion and user devices

Port I/O and FoxOS process-control instructions use the following encodings:

- `0000 0000` `0011 0000` - `IN`  `PORT DST` - Read the 16-bit value from port `PORT` into register `DST`
- `0000 0000` `0011 0010` - `OSRET` - Return from a FoxOS foreground program to the resident OS at address `0`
- `0000 0000` `0011 0001` - `OUT` `SRC PORT` - Write the 16-bit value from register `SRC` to port `PORT`

`OSRET` is the only FoxOS-specific CPU opcode. A resident OS starts a
foreground image with an ordinary absolute `JMP` to that image's load address;
there is no separate `OSSTART` opcode. The FoxC `os_start(address)` built-in
emits this `JMP`, while `os_return()` emits `OSRET`.

### Addressing Mode (AM) and 32-bit Architecture (V2.0)

ISA V2.0 introduces the **Addressing Mode (`AM`)** register and the **32-bit Addressing Space mode (`EM = 2`)** to provide independent control over memory address space width and addressing granularity.

#### Independent Control: EM and AM

`EM` and `AM` represent orthogonal controls over the memory architecture:

- **`EM` (Extension Mode)** selects the **memory address space width**:
  - `EM = 0` (`0x0000`): **Legacy 16-bit Address Space** (64K words addressable, 4K ROM limit).
  - `EM = 1` (`0x0001`): **Extended 16-bit Address Space** (up to 32K words ROM, port I/O enabled).
  - `EM = 2` (`0x0002`): **32-bit Memory Address Space** (unlocks 32-bit address bus for memory addressing).
- **`AM` (Addressing Mode)** selects the **memory addressing granularity**:
  - `AM = 0` (`0x0000`): **16-bit Word Addressing** (each address unit indexes a 16-bit word).
  - `AM = 1` (`0x0001`): **8-bit Byte Addressing** (each address unit indexes an individual 8-bit byte).

`AM=1` and `EM=2` can be used completely independently. Byte addressing (`AM=1`) is the usual choice for modern hardware with a conventional 32-bit byte-addressed memory space. Word addressing (`AM=0`) remains the natural fit for word-oriented toolchains whose labels, globals, stack slots and pointers are all counted in 16-bit words; the FoxC compiler is an example and starts V2 programs with `EM=2, AM=0`.

#### Addressing and Mode Matrix

| Configuration    | Memory Address Bus | Addressing Granularity | Total Addressable Space | Usage                                    |
| ---------------- | ------------------ | ---------------------- | ----------------------- | ---------------------------------------- |
| `EM = 0, AM = 0` | 16-bit             | 16-bit word            | 64K words (128 KB)      | Legacy mode (V1.0)                       |
| `EM = 1, AM = 0` | 16-bit             | 16-bit word            | 64K words (128 KB)      | Extended mode with port I/O (V1.10)      |
| `EM = 1, AM = 1` | 16-bit             | 8-bit byte             | 64 KB bytes             | 16-bit byte-addressed mode               |
| `EM = 2, AM = 0` | 32-bit             | 16-bit word            | $2^{32}$ words (8 GB)   | 32-bit word-addressed mode (FoxC V2)     |
| `EM = 2, AM = 1` | 32-bit             | 8-bit byte             | $2^{32}$ bytes (4 GB)   | **Modern byte-addressed target**         |

Modern hand-written programs targeting a byte-addressed memory model should initialize both registers at startup:
```assembly
MOV %2 EM   ; Enable 32-bit memory addressing space
MOV %1 AM   ; Enable 8-bit byte addressing
```

Word-oriented toolchains can instead keep `AM=0`:
```assembly
MOV %2 EM   ; Enable 32-bit memory addressing space
MOV %0 AM   ; Keep 16-bit word addressing
```

#### Addressing Mode Control Register (`AM`)

- Register ID: `0xF` (`AM`)
- Size: 16-bit
- Default value: `0x0000` (16-bit word addressing)

- `0x0000` (`0`): **16-bit Addressing Mode**. Memory operands and pointers reference 16-bit words.
- `0x0001` (`1`): **8-bit Addressing Mode**. Memory operands and pointers reference 8-bit bytes.

Programs switch addressing granularity dynamically:
```assembly
MOV %1 AM   ; Switch to 8-bit byte addressing
MOV %0 AM   ; Return to 16-bit word addressing
```

#### 32-bit Registers and High/Low Sub-Registers

To allow flexible manipulation of 32-bit addresses and data within a 16-bit instruction format:
- Core registers (`R0`–`R7`, `X`, `Y`, `PC`, `SP`, `CYC`) are 32 bits wide.
- For each register, a High (`H`) and Low (`L`) 16-bit sub-register is provided:
  - Base register (e.g. `R0`): full 32-bit value (`[31:0]`). Used to hold 32-bit memory pointers or 32-bit calculation values.
  - Low sub-register (e.g. `R0L`): lower 16 bits (`[15:0]`). Reading/writing affects only bits 15–0 while preserving bits 31–16 of the base register.
  - High sub-register (e.g. `R0H`): upper 16 bits (`[31:16]`). Reading/writing affects only bits 31–16 while preserving bits 15–0 of the base register.

For example, a 32-bit address pointer can be constructed using 16-bit immediate moves:
```assembly
MOV $1234 R0H    ; Set upper 16 bits of R0
MOV $5678 R0L    ; Set lower 16 bits of R0 -> R0 now contains $12345678
LOD [R0] R1      ; Load value from 32-bit address in EM=2, AM=1 mode
```

This architecture allows programs running on modern hardware (`EM=2`, `AM=1`) to easily construct, index, and manipulate pointers across the entire 4 GB address space.

The wide datapath is enabled by `EM=2`. In `EM=0`/`EM=1`, register and ALU operations keep their 16-bit behaviour for backwards compatibility, even if a register happens to hold a larger value. With `EM=2`:

- `MOV` between two primary registers copies all 32 bits; moves from an immediate or an `H`/`L` source remain 16 bits wide and zero-extend into a primary destination.
- ALU instructions (`ADD`, `SUB`, `MUL`, `DIV`, `AND`, `OR`, `XOR`, `SHL`, `SHR`) operate on 32 bits when the destination is a primary register, and on 16 bits when the destination is an `H`/`L` sub-register.
- `CMP` compares 32 bits when either operand is a primary register and 16 bits otherwise. `SHL`/`SHR` mask the shift count to 5 bits.
- `PUSH`/`POP` transfer 32 bits for primary registers and 16 bits for `H`/`L` and 16-bit control registers.

`AM` only controls memory addressing granularity (`LOD`/`STR` width and address units); it does not change the register or ALU width.

#### 32-bit data in memory

A 32-bit value is stored as two consecutive 16-bit words in little-endian word order: the low word at the lower address and the high word at the next addressing unit. Under `AM=0` the words are at `address` and `address + 1`; under byte addressing they are at `address` and `address + 2`. `LOD`/`STR` move one word (or byte) at a time, so compilers load or store the two halves separately and combine them through the `H`/`L` sub-registers.

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

The Fox Vision architecture memory space is determined by the Extension Mode (`EM`) and Addressing Mode (`AM`) registers:

### 16-bit Address Space Modes (`EM = 0x0000` or `0x0001`)

Under 16-bit address space modes, memory addresses are 16 bits wide:

- **When `AM = 0` (16-bit word addressing)**: The device has a total of 65,536 ushort (64K words / 128 KB) of addressable space. Each address indexes a 16-bit word:
  - **Legacy mode (`EM = 0x0000, AM = 0x0000`)**: `0x0000 - 0x0FFF` ROM (4K words), `0xEC78 - 0xFFFF` VRAM, remaining space available for RAM and VRAM.
  - **Extension mode (`EM = 0x0001, AM = 0x0000`)**: ROM may expand up to 32K words; VRAM remains fixed at 5000 bytes (`0xEC78 - 0xFFFF`); memory-mapped I/O is removed except for VRAM; port I/O enabled.
- **When `AM = 1` (8-bit byte addressing)**: Addresses index 8-bit bytes within a 16-bit address space (up to 64 KB total addressable memory).

### 32-bit Memory Address Space (`EM = 0x0002`, V2.0)

Writing `0x0002` to `EM` enables a full 32-bit address bus for memory:

- **When `AM = 0` (16-bit word addressing)**:
  - Memory is addressed in 16-bit words across a 32-bit address space.
  - Up to $2^{32}$ words (8 GB) of addressable space.
- **When `AM = 1` (8-bit byte addressing — Modern Hardware Target)**:
  - **Address Bus**: Full 32-bit address bus providing 4 GB (4,294,967,296 bytes) of addressable space.
  - **Addressing Granularity**: 8-bit (byte-addressed). Address increments reference individual 8-bit bytes.
  - **Pointers**: Full 32-bit registers (`R0`–`R7`, `X`, `Y`, `SP`, `PC`) serve as 32-bit memory addresses and base pointers.
  - **Sub-Registers**: `R0L`–`R7L`, `XL`, `YL`, etc., manipulate 16-bit word values or address offsets.
  - **Stack Pointer**: `SP` acts as a 32-bit pointer to the top of the stack in memory.

See [port_devices.md](port_devices.md) for port device specifications and port mappings.
