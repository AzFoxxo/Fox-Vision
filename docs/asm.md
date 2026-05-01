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
- `--mode <legacy|extended>` - Select the machine mode for this build (default: `legacy`). Use `--mode extended` to enable extension mode (`EM=0x0001`).
- `--strict-format` - Enforce the ROM payload limit for the selected mode
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
```

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

For debug extension instructions, the canonical names are `DBG_LGC`, `DBG_MEM`, and `DBG_INP`. The assembler also accepts legacy aliases `DGB_MEM` and `DGB_INP` for backward compatibility.

Frame pacing instructions include `WAIT` for cycle-based delays and `VBLANK` for waiting until the next rendered frame refresh. The assembler also accepts the shorter alias `VBL`.

Extension mode adds the `EM` machine register and the `IN`/`OUT` port I/O instructions in the machine spec. When writing assembly that targets V1.10-capable ROMs, use `EM` to switch between modes:

- Legacy (default, `EM = 0x0000`): active-register instruction forms are permitted.
- Extended (new, `EM = 0x0001`): active-register instruction forms are disabled; the ISA exposes port I/O via `IN`/`OUT` and named port identifiers.

In extended mode ports `0x0000` through `0x0007` are available as 16-bit bidirectional endpoints and return the current device state at the time of execution. The assembler accepts symbolic port names `PORT0`..`PORT7` (case-insensitive) which encode to immediate values `0x0000`..`0x0007` when used as port operands.

Example (extended mode):

```assembly
; Read 16-bit value from PORT1 into register X (assembler will encode PORT1 as 0x0001)
IN PORT1 X

; Write register Y out to PORT0
OUT Y PORT0
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

### Preprocessor Directives
- `@const <name> <value>` - Define a constant that can be used throughout the file

All preprocessor directives (lines starting with `@`) are removed before compilation.

## Machine Mode Notes

- `EM` is a machine register used to select the active mode.
- `0x0000` keeps legacy behavior enabled.
- `0x0001` enables extension mode and port I/O.
- `--mode` is the assembler flag used to select the machine mode; use `--mode extended` to enable extension mode for a build.
- `--strict-format` limits ROM payloads to 4K words in legacy mode and 32K words in extension mode.
- Ports do not latch, queue, clear, or acknowledge input.

## Default constants

To simplify assembly, the following register and port constants are recognised by the assembler and map to the canonical machine IDs used by the CPU spec:

- Registers:
	- `X` : `0x0` (general-purpose)
	- `Y` : `0x1` (general-purpose)
	- `PC` : `0x2` (program counter, not writable)
	- `STATUS` : `0x3` (flags register)
	- `SP` : `0x4` (stack pointer)
	- `CYC` : `0x5` (cycle counter, read-only)
	- `EM` : `0x6` (extension mode register, defaults to `0x0000`)

- Ports (symbolic immediate encodings):
	- `PORT0` = `0x0000`
	- `PORT1` = `0x0001`
	- `PORT2` = `0x0002`
	- `PORT3` = `0x0003`
	- `PORT4` = `0x0004`
	- `PORT5` = `0x0005`
	- `PORT6` = `0x0006`
	- `PORT7` = `0x0007`

These names can be used in instruction operands where appropriate (for example `IN PORT1 X` in extended mode). Port-to-device mappings remain implementation-defined and are assigned by the emulator.
