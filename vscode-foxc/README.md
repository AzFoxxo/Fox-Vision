# FoxC Tools

FoxC Tools is a small VS Code extension for FoxC source files (`.fc`). It provides:

- Syntax highlighting for language keywords, types, built-ins, operators, and comments
- Syntax highlighting for character and string literals, including escaped characters
- Basic completion items for common keywords, types, built-ins, and snippets
- FoxOS built-ins: `os_start(address)` and `os_return()`
- Lightweight validation for common mistakes such as missing semicolons, unmatched braces, duplicate declarations, unknown identifiers, and incorrect call shapes

FoxC literals follow the compiler's byte-oriented rules:

- Character literals use single quotes and produce one `u8`, for example `'A'` or `'\n'`.
- String literals use double quotes, support escaped characters, and produce a NUL-terminated `u8 *`.
- String literals can initialize inferred or explicitly sized `u8` arrays, for example
  `u8 message[] = "Hello";` or `u8 message[6] = "Hello";`.
- An explicitly sized array must include space for the terminating NUL. `u8` array
  storage is packed, while string data used as a pointer is emitted as read-only
  byte data.

## Install locally

1. Open the Extensions view in VS Code.
2. Use `Developer: Install Extension from Location...`.
3. Select the `vscode-foxc` folder in this repository.
4. Reload VS Code.

## Notes

The extension is intentionally lightweight. It mirrors the FoxC language subset documented in `FoxC/grammar.ebnf` and the compiler in `FoxC/internal/foxc`.