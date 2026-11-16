# Fox16ASM Highlighter

Rich VS Code syntax highlighting, autocomplete, hover documentation, and validation for Fox Vision assembly files (`.f16`).

Supports Fox Vision ISA legacy mode, extended mode (V1.10), and V2.0 (`em2_am1` mode with 32-bit address space and byte addressing).

## Features

- **Syntax Highlighting**:
  - Line comments starting with `;`
  - Label declarations like `:main` and `:loop`
  - Preprocessor directives: `@const`, `@include`, `@org`, `@align`, `@db`, `@dw`
  - Opcodes: All legacy and modern multi-operand MOI instructions (`MOV`, `STR`, `LOD`, `CMP`, `ADD`, `SUB`, `MUL`, `DIV`, `SHL`, `SHR`, `AND`, `OR`, `XOR`, `PUSH`, `POP`, `IN`, `OUT`, `WAIT`, `VBLANK`, `OSRET`, `NOP`, `HLT`, etc.)
  - Core registers: `X`, `Y`, `STATUS`
  - Extended registers: `PC`, `SP`, `CYC`, `EM`, `R0`–`R7`
  - V2.0 registers and sub-registers: `AM`, `XL`/`XH`, `YL`/`YH`, `PCL`/`PCH`, `SPL`/`SPH`, `CYCL`/`CYCH`, `R0L`/`R0H` through `R7L`/`R7H`
  - Port operands: `PORT0`–`PORT65535`
  - Numbers: Hexadecimal (`$FFFF`), decimal immediates (`%123`), and raw decimal
  - Named constants: `<NAME>`
- **IntelliSense & Autocomplete**:
  - Context-aware completions for opcodes, core/extended/V2 registers, ports, directives, labels, and constants
  - Snippets for `@const`
- **Hover Documentation**:
  - Detailed instruction docs with operand syntax and examples
  - Register descriptions indicating register width, sub-register pairs, and mode requirements
  - Port operand information
- **Diagnostics & Validation**:
  - Checks for duplicate labels and constants
  - Validates opcode operand counts
  - Identifies undefined labels or constants

## Installation

Run the workspace reinstall script:

```fish
./reinstall-vscode-extensions.fish
```

Or manually package and install:

```bash
cd vscode-fox16asm-highlighter
npx @vscode/vsce package
code --install-extension fox16asm-highlighter-*.vsix
```
