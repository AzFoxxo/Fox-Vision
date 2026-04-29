# FoxC Tools

FoxC Tools is a small VS Code extension for FoxC source files (`.fc`). It provides:

- Syntax highlighting for language keywords, types, built-ins, operators, and comments
- Basic completion items for common keywords, types, built-ins, and snippets
- Lightweight validation for common mistakes such as missing semicolons, unmatched braces, duplicate declarations, unknown identifiers, and incorrect call shapes

## Install locally

1. Open the Extensions view in VS Code.
2. Use `Developer: Install Extension from Location...`.
3. Select the `vscode-foxc` folder in this repository.
4. Reload VS Code.

## Notes

The extension is intentionally lightweight. It mirrors the FoxC language subset documented in `FoxC/grammar.ebnf` and the compiler in `FoxC/internal/foxc`.