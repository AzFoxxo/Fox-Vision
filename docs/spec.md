# Specification of the Fox Vision architecture

## Processor
The CPU is a single threaded 8MHz RISC chip. It uses the FoxVision16 architecture.

### Registers
| ID  | Register | Size   | Read/Write | Description                                                                                                                                        |
| --- | -------- | ------ | ---------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| 0x0 | X        | 16-bit | Yes        | General-purpose register #1                                                                                                                        |
| 0x1 | Y        | 16-bit | Yes        | General-purpose register #2                                                                                                                        |
| 0x2 | PC       | 16-bit | No         | Program counter (only modified internally by CPU control-flow logic; not directly accessible)                                                      |
| 0x3 | STATUS   | 8-bit  | Limited    | CPU flags register (read-only via instructions; written only by CPU operations like CMP, DIV, HLT, CLR)                                            |
| 0x4 | SP       | 16-bit | Yes        | Stack Pointer. Points to the top of the stack in memory. Modified by PUSH/POP instructions and may be read/written directly for low-level control. |

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

All instructions are fixed-width 48-bit values composed of three 16-bit words:

- Word 0: Opcode
- Word 1: Operand 1
- Word 2: Operand 2

Some instructions may ignore one or more operand words (e.g. `HLT`), but all three words are still fetched.

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
- `0000 0000` `0000 0001` - `LFM` - Load 2 byte value from memory in active register
- `0000 0000` `0000 0010` - `WTM` - Write to memory the value of the active register
- `0000 0000` `0000 0011` - `SRA` - Set register active (X - 0, Y - 1)
- `0000 0000` `0000 0100` - `AXY` - Add X and Y and store result in active register
- `0000 0000` `0000 0101` - `SXY` - Subtract X from Y and store result in active register
- `0000 0000` `0000 0110` - `MXY` - Multiply X by Y and store result in active register
- `0000 0000` `0000 0111` - `DXY` - Divide X by Y and store result in active register
- `0000 0000` `0000 1000` - `EQU` - Check if X and Y registers are equal
- `0000 0000` `0000 1001` - `LEQ` - Check if X register is less than Y register
- `0000 0000` `0000 1010` - `JPZ` - Jump if zero to 2 byte wide address
- `0000 0000` `0000 1011` - `JNZ` - Jump if not zero to 2 byte wide address
- `0000 0000` `0000 1100` - `JMP` - Jump to 2 byte wide address
- `0000 0000` `0000 1101` - `CLR` - Clear all Status register bits (set to zero)
- `0000 0000` `0000 1110` - `HLT` - Halt program execution (quit/power-off)
- `0000 0000` `0000 1111` - `BSL` - Bitshift left value in active register
- `0000 0000` `0001 0000` - `BSR` - Bitshift right value in active register
- `0000 0000` `0001 0001` - `AND` - AND bitwise value in active register by value in non-active register
- `0000 0000` `0001 0010` - `ORA` - OR bitwise value in active register by value in non-active register
- `0000 0000` `0001 0011` - `XOR` - XOR bitwise value in active register by value in non-active register
- `0000 0000` `0001 0100` - `DWR` - Direct write sets the given 16 bit value to the active register

### CPU usability additions (V1.1)
- `0000 0000` `0001 0101` - `ILM` - Indirect load from memory - load address stored in active register
- `0000 0000` `0001 0110` - `IWR` - Indirect write register to memory - write value in active register to address stored in inactive register

### CPU shorthand additions (V1.2)
- `0000 0000` `0001 0111` - `INC` - Increase the value in the active register by one
- `0000 0000` `0001 1000` - `DEC` - Decrease the value in the active register by one

### Input Output Controls (V1.3)

Fox Vision supports one digital controller.

The controller is exposed through memory mapped input.

- Address `0x1000` - Controller state byte

Reading address `0x1000` returns an 8 bit value representing all buttons.

Controller input is latched:

- A key press sets the corresponding bit to `1`
- Key release does not clear the bit
- Bits remain set until ROM code writes a new value to `0x1000`

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

Button states:

- `1` = Pressed
- `0` = Released

Examples:

- `00000000` = No buttons pressed
- `00010000` = A pressed
- `01000000` = Start pressed
- `00001001` = Up + Right pressed

Programs should read controller state using standard memory load instructions.
Programs should clear handled inputs by writing to `0x1000` using standard memory write instructions.

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

- `0000 0000` `0010 1100` - `PUSH` - Push a value onto the stack
- `0000 0000` `0010 1101` - `POP` - Pop a value from the stack

NOTE: `RET` (defined )

### Extension Debug Opcodes (EDO) - 2.0

**Note:** These instructions are reserved exclusively for debugging and testing the virtual machine. They provide console I/O, memory inspection, and input facilities.

---

## Opcode Class

All EDO instructions belong to the debug opcode class:

- HIGH byte = `1111 1111` (0xFF)

If the opcode HIGH byte is `0xFF`:
- The instruction is treated as an EDO instruction
- Operand-type metadata is ignored
- The LOW byte is interpreted as an EDO sub-opcode

---

## EDO Instructions

- `1111 1111 0000 0000` - `LOG` - Output a single ASCII character to the debug console
- `1111 1111 0000 0001` - `MEMDUMP` - Output memory contents in hexadecimal format
- `1111 1111 0000 0010` - `INPUT` - Read a line of input from the user and return it as an unsigned 16-bit value

---

### Character Encoding

All text I/O in EDO uses standard ASCII encoding.

- When using `LOG`, the operand value is interpreted as an ASCII code.
- When using `INPUT`, returned values are encoded as ASCII-derived data.

### Examples

- To output `"A"`:
  - `LOG` with value `65`

- If the user inputs `"A"`:
  - VM returns `65`

---

### Compatibility

EDO 2.0 is not backward compatible in either binary or source form.

## Graphics

Fox Vision supports a display size of 100x100 with four bits used to represent each colour (colours are predefined) and retrieves this data from RAM 60 times a second to display it.

The total memory size for an uncompressed frame is:

`4bits * (100 * 100) = 40000 bits (5000 bytes or 5kb approx)`

VRAM starts at address `FFFF` and descends the next 5000 bytes.

`FFFF` corresponds to top-left corner, moving right then down.

## Memory

The device has a total of 65,536 bytes (64kb) of addressable space.

This memory is broken up into several sections:

- `0x0000 - 0x0FFF` ROM (4kb)
- `0x1000` Controller input
- Remaining space available for RAM and VRAM