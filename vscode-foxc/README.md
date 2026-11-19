# FoxC Tools

FoxC Tools is a VS Code extension for FoxC source files (`.fc`). It provides syntax highlighting, completions, hover documentation, snippets, and validation.

Supports legacy, extended (V1.10), V2.0 (`em2_am1`) language features, and the
header system (`#include`, function prototypes, whole-code headers).

## Features

- **Syntax Highlighting**:
  - Preprocessor directives: `#include`, `#pragma once`
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
  - Document symbol completions for declared variables and functions, **including symbols from included headers**
  - Snippets: `if`, `ifelse`, `while`, `main`, `return`, `u32var`, `include`, `prototype`
- **Hover Documentation**:
  - Detailed hover tooltips for all built-in functions including parameters and mode requirements
  - Hover information for data types explaining bit widths, storage, and mode constraints
  - Hover tooltips for user-defined functions and globals, with signature details and the originating header file
- **Header Support**:
  - Follows `#include "file.fh"` and `#include <file.fh>` recursively; headers are visited once
  - Recognizes function prototypes (declarations ending in `;`), including nameless parameters and `f(void)`
  - Reports missing/unreadable includes
- **Lightweight Validation**:
  - Missing semicolons, unmatched braces, duplicate declarations
  - Signature and argument count checking on built-in and user-defined calls (including prototype declarations from headers)
  - Validation of `main()` entry point signature (skipped for `.fh`/`.h` header files)

## Settings

- `foxc.includePaths` — array of directories searched for `#include <...>`
  directives. Relative paths resolve from the workspace folder. Quoted includes
  always check the including file's directory first, then these directories.

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
