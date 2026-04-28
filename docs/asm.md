# Fox 16ASM Language
Fox 16ASM is a simple assembly language which compiles to native ROM files used by Viso Fox. The assembly language always implements the most current spec the machine specification details.

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
- `-h, --help` - Display help information

### Examples
```bash
# Compile with default output name
fox16asm -i myprogram.f16

# Compile with custom output name
fox16asm -i myprogram.f16 -o custom.bin

# Compile with debug output
fox16asm -i myprogram.f16 --tokens --labels

# Compile forcing ROM size compliance (4kb word limit)
fox16asm -i myprogram.f16 --strict-format
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