# FoxC Tools

FoxC Tools is a VS Code extension for FoxC source files (`.fc`). It provides syntax highlighting, completions, hover documentation, snippets, and validation.

Supports legacy, extended (V1.10), and V2.0 (`em2_am1`) language features.

## Features

- **Syntax Highlighting**:
  - Keywords: `if`, `else`, `while`, `return`
  - Types: `u8`, `u16`, `u32`, `void`, and pointers (`u8 *`, `u16 *`, `u32 *`)
  - Built-ins:
    - Core: `poke(addr, val)`, `peek(addr)`, `wait(cycles)`, `cyc()`, `vblank()`
    - FoxOS: `os_start(address)`, `os_return()`
    - Extended I/O: `in_port(port)`, `out_port(port, value)` (ports 0–65535 in `em2_am1` mode)
    - V2.0 addressing & mode control: `am()`, `set_am(val)`, `em()`, `set_em(val)`
  - Operators: Arithmetic, bitwise, comparison, logical, assignment, address-of (`&`), and dereference (`*`)
  - Literals: Single-byte character literals (`'A'`), string literals (`"Hello"`), and decimal integers
  - Comments: `//` line comments and `/* */` block comments
- **IntelliSense & Autocomplete**:
  - Type completions (`u8`, `u16`, `u32`, `void`) with descriptions
  - Built-in functions with parameter signatures and return types
  - Document symbol completions for declared variables and functions
  - Snippets: `if`, `ifelse`, `while`, `main`, `return`, `u32var`
- **Hover Documentation**:
  - Detailed hover tooltips for all built-in functions including parameters and mode requirements
  - Hover information for data types explaining bit widths, storage, and mode constraints
  - Hover tooltips for user-defined functions
- **Lightweight Validation**:
  - Missing semicolons, unmatched braces, duplicate declarations
  - Signature and argument count checking on built-in calls
  - Validation of `main()` entry point signature

## Installation

Run the workspace reinstall script:

```fish
./reinstall-vscode-extensions.fish
```

Or manually package and install:

```bash
cd vscode-foxc
npx @vscode/vsce package
code --install-extension foxc-tools-*.vsix
```
