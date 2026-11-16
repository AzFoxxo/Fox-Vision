# Fox 16ASM Language
Fox 16ASM is a simple assembly language which compiles to native ROM files used by Viso Fox. The assembly language always implements the most current machine specification details, including extension-mode and port I/O changes when the target spec adds them.

## Running the Assembler

The Fox16ASM assembler converts `.f16` assembly files into ROM binary files.

### Basic Usage
```bash
fox16asm -i <file.f16>
```

This will compile `file.f16` and produce `vfox16.bin` in the same directory.

### Command-line Options
- `-i, --input <file>` - Input assembly file (.f16) **[required]**
- `-o, --output <rom>` - Output ROM file (default: `vfox16.bin`)
- `--tokens` - Show token debug output during compilation
- `--labels` - Show label resolution debug output during compilation
- `--mode <legacy|extended|em2_am1>` - Select the machine mode for this build (default: `legacy`). Use `--mode extended` for 16-bit extended mode (`EM=0x0001`), or `--mode em2_am1` (aliases: `extended2`, `v2`, `em2`) for the ISA V2.0 instruction set with 32-bit address space and 32-bit registers. The assembler does not itself choose an addressing granularity; the program sets `AM` at run time (`AM=1` for byte addressing, `AM=0` for word addressing).
- `--strict-format` - Enforce the ROM payload limit for the selected mode
- `--load-address <addr>` - Load the ROM payload at a non-zero address, such
  as `0x4000` for a FoxOS foreground program
- `-h, --help` - Display help information

### Examples
```bash
# Compile with default output name
fox16asm -i myprogram.f16

# Compile with custom output name
fox16asm -i myprogram.f16 -o custom.bin

# Compile with debug output
fox16asm -i myprogram.f16 --tokens --labels

# Compile forcing ROM size compliance in legacy mode (4K-word limit)
fox16asm -i myprogram.f16 --strict-format

# Compile with extension mode enabled and the larger ROM payload limit
fox16asm -i myprogram.f16 --mode extended --strict-format

# Build a program for FoxOS's user-program area
fox16asm -i myprogram.f16 -o myprogram.bin --mode extended \
  --load-address 0x4000 --strict-format
```

FoxOS foreground programs can return to the resident shell with the
zero-operand `OSRET` instruction:

```assembly
; ... foreground program ...
OSRET
```

To start a foreground program from a resident OS image, jump to its loaded
address. The assembler has no separate `OSSTART` opcode; use the normal
absolute-address jump form:

```assembly
JMP $4000
```

The equivalent FoxC built-ins are `os_start(0x4000)` (the address must be a
16-bit constant and is emitted as `JMP`) and `os_return()` (emitted as
`OSRET`). A foreground program should be assembled or compiled with its load
address and finish with `OSRET`.

## Basic structure
Each program must start with a `:main` definition, this must be the first piece of code within the program as address zero is used as the start of execution.

The language supports a few features:
- Comments
- Instructions
- Labels

### Comments
All comments begin with `;` this may be on its own line or come after the final part of an existing instruction e.g.
`; my comment`
and
`NOP ; my comment`
are both valid comments


### Instructions
Instructions (opcodes) are always spelt in capitals. Basic instructions use three character codes such as `NOP` and `AXY`. These instructions can take a singular operand or no operand like `HLT`.

`OSRET` is the FoxOS process-control instruction. It takes no operands, is
available in extended mode, and returns execution to the resident OS at
address zero. `JMP` remains the assembler-level operation for transferring
control to a foreground program address.

Frame pacing instructions include `WAIT` for cycle-based delays and `VBLANK` for waiting until the next rendered frame refresh. The assembler also accepts the shorter alias `VBL`.

Extension mode adds the `EM` and `AM` machine registers and the `IN`/`OUT` port I/O instructions in the machine spec. When writing assembly that targets extended-capable ROMs, use `EM` and `AM` to switch between modes:

- Legacy (default, `EM = 0x0000`): active-register instruction forms are permitted.
- Extended (`EM = 0x0001`): active-register instruction forms are disabled; the ISA exposes port I/O via `IN`/`OUT` and named port identifiers. Multi-operand, comparison/jump, ALU, stack, and port instructions require `--mode extended`.
- Modern V2.0 (`EM = 0x0002`): 32-bit address bus for memory addressing, 32-bit registers (`R0`–`R7`, `X`, `Y`, `PC`, `SP`, `CYC`) and high/low sub-registers (`R0L`/`R0H`, etc.). Select via `--mode em2_am1`. The program chooses `AM=1` for 8-bit byte addressing or `AM=0` for 16-bit word addressing; both are valid with `EM=2`.

In extended modes, the full 16-bit port space `0x0000` through `0xFFFF` is accessible. The assembler accepts symbolic port names `PORT0`..`PORT65535` (case-insensitive) as well as immediate values `%0`..`%65535` or `$0`..`$FFFF` when used as port operands.

Example (extended mode):

```assembly
; Read 16-bit value from PORT1 into register X (assembler will encode PORT1 as 0x0001)
IN PORT1 X

; Write register Y out to PORT0
OUT Y PORT0

; Read from higher port into 32-bit sub-register
IN PORT100 R0L
OUT R0H PORT100
```

To denote the type of data, the following prefixes are used:
- `Aa-Zz + _` - Label reference (resolved to the label's address within ROM) e.g. `main`
- `%` - Decimal value e.g. `%1`
- `$` - Hexadecimal value (used for address representation) e.g. `$FFFF`

# Labels
Labels are defined by writing `:` followed by name using Aa-Zz + _ e.g.
```assembly
:main
; Code here
```

These labels can then be jumped to using jump instructions e.g. `JMP main` or an equivalent conditional jump.

## EFox16 Preprocessor

The EFox16 preprocessor is now part of the standard spec offering preprocessor directives.

### Constants (`@const`)
Define named constants that will be substituted throughout your code:

```assembly
@const WIDTH %50
@const HEIGHT %100

; Use constants in your code
DWR <WIDTH>
WTM $2005
```

The preprocessor will replace all instances of `<WIDTH>` with `%50` and `<HEIGHT>` with `%100` before compilation.

### Segment Directives (`@segment code|data`)
The assembler spec defines explicit code and data segments. `@segment code` selects normal instruction parsing. `@segment data` selects a constant insertion data segment for ROM-resident literals that should be written straight into ROM, rather than encoded as executable instructions.

```assembly
@segment data
table:
@word %1234
next_value:
@word $ABCD
width_value:
@word <WIDTH>
@segment code
```

Within a data segment, each data directive emits raw ROM words. A data item SHOULD be preceded by a label when later code needs to reference its address. The initial spec requires `@word <value>` for a single 16-bit word, and the label marks that word's ROM address. `<NAME>` constant references MAY be used where a word value is expected. In extended ROM images, the emitted words SHOULD be placed at the mapper-selected `ROM start` offset described in [ROM.md](ROM.md). Additional data directives MAY be added later for packed bytes, fills, or string emission.

### Preprocessor Directives
- `@const <name> <value>` - Define a constant that can be used throughout the file
- `@segment code` - Switch back to instruction parsing
- `@segment data` - Begin a constant insertion data segment
- `@word <value>` - Emit one 16-bit word while in data segment mode

Data labels use the normal `:name` label syntax and may precede a data item directly. Use those labels in code to reference the emitted ROM address.

All preprocessor directives (lines starting with `@`) are removed before compilation.

## Machine Mode Notes

- `EM` is a machine register used to select the active mode:
  - `0x0000` (legacy mode): active-register instruction forms permitted, 16-bit address space.
  - `0x0001` (extended mode): port I/O enabled, active-register forms disabled, 16-bit address space.
  - `0x0002` (32-bit memory address space): unlocks 32-bit address bus for memory addressing.
- `AM` is a machine register used to select addressing granularity:
  - `0` (`0x0000`): 16-bit word addressing.
  - `1` (`0x0001`): 8-bit byte addressing.
- `AM=1` and `EM=2` can be used independently. Use `AM=1` for a byte-addressed memory model and `AM=0` for a word-addressed model. Some toolchains (for example FoxC) keep `AM=0` because their labels, globals and pointers are word-based.
- `--mode` is the assembler flag used to select the instruction set/machine mode; use `--mode extended` for 16-bit extension mode or `--mode em2_am1` for ISA V2.0. The program is responsible for writing `EM` and `AM` at startup.
- `--strict-format` limits ROM payloads to 4K words in legacy mode and 32K words in extension mode.
- Ports do not latch, queue, clear, or acknowledge input.

## Default constants

To simplify assembly, the following register and port constants are recognised by the assembler and map to the canonical machine IDs used by the CPU spec:

- Registers:
	- `X` : `0x0` (general-purpose, 32-bit; aliased by `XL` `0x10` and `XH` `0x11`)
	- `Y` : `0x1` (general-purpose, 32-bit; aliased by `YL` `0x12` and `YH` `0x13`)
	- `PC` : `0x2` (program counter, 32-bit, not writable; aliased by `PCL` `0x14` and `PCH` `0x15`)
	- `STATUS` : `0x3` (flags register)
	- `SP` : `0x4` (stack pointer, 32-bit; aliased by `SPL` `0x16` and `SPH` `0x17`)
	- `CYC` : `0x5` (cycle counter, 32-bit, read-only; aliased by `CYCL` `0x18` and `CYCH` `0x19`)
	- `EM` : `0x6` (extension mode register: `0` = legacy, `1` = extended 16-bit, `2` = 32-bit memory address space)
	- `R0` - `R7` : `0x7` - `0xE` (general-purpose 32-bit registers; aliased by `R0L`/`R0H` through `R7L`/`R7H` `0x20` - `0x2F`)
	- `AM` : `0xF` (addressing mode register: `0` = 16-bit word addressing, `1` = 8-bit byte addressing)

- Ports (symbolic immediate encodings):
	- `PORT0` - `PORT65535` = `0x0000` - `0xFFFF` (symbolic port aliases for immediate port operands)
	- Default emulator mapping:
		- `PORT0` = `0x0000` (VF16Pad Player 1)
		- `PORT1` = `0x0001` (VF16Pad Player 2)
		- `PORT2` = `0x0002` (VF16Keyboard)
		- `PORT3` = `0x0003` (VF16Mouse)
		- `PORT4` = `0x0004` (VF16TTY)
		- `PORT5` = `0x0005` (VF16Flash)
		- `PORT6` - `PORT7` = `0x0006` - `0x0007` (reserved/custom devices)
		- `PORT8` - `PORT65535` = `0x0008` - `0xFFFF` (user and expansion ports)

These names can be used in instruction operands where appropriate (for example `IN PORT1 X` in extended mode). Port-to-device mappings remain implementation-defined and are assigned by the emulator.
