# Fox16ASM Highlighter

Very simple VS Code syntax highlighting extension for Fox Vision assembly files (`.f16`).

## Features

- Line comments starting with `;`
- Label declarations like `:main`
- Directive highlighting for `@const`
- Instruction highlighting for Fox16 opcodes
- Register highlighting for `X`, `Y`, and `STATUS`
- Numeric literals (`%123`, `$FFFF`)
- Named constants (`<NAME>`)
- Autocomplete for opcodes, registers, directives, labels, and constants
- Context-aware suggestions (jump targets, `@const`, and `<CONST>`)

## Quick local install

1. Open VS Code Extensions view.
2. Click the `...` menu.
3. Choose `Install from VSIX...` if you packaged one, or use `Developer: Install Extension from Location...` from the command palette and select this folder.
4. Reload VS Code.

## Folder

This extension is located in `vscode-fox16asm-highlighter`.
