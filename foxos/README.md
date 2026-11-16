# FoxOS

A small operating system for the **Fox16** virtual machine, structured as three clean sub-projects.

The resident kernel (`kernel` + `foxfs`) and the interactive shell (`foxsh`) are
built as **separate images**. `build/foxos.bin` never contains shell code.

```
foxos/
├── Makefile                  ← top-level build system
├── kernel/
│   ├── kernel.fc             ← hardware init, flash I/O, program loader
│   └── boot.fc               ← resident-kernel entry point (main)
├── foxfs/
│   └── foxfs.fc              ← flash-backed flat filesystem
├── foxsh/
│   └── foxsh.fc              ← interactive shell image (kernel + foxfs + foxsh)
└── apps/
    └── graphics_demo/
        └── graphics_demo.fc  ← 16-colour scrolling-gradient demo
```

## Quick Start

```bash
# Build everything from the repo root (builds toolchain first):
./build-foxos-new.sh

# Or, if the VF16 tools are already installed:
cd foxos
make TOOL_DIR=~/vf16/tools

# Launch FoxVision with the combined ROM:
make run
```

## Sub-projects

### `kernel/kernel.fc`
Low-level hardware interface:
- `flash_read` / `flash_write` — Port 5 (VF16Flash) word-level I/O
- `tty_putc` / `tty_puts` / `tty_getc` — Port 4 (VF16TTY) terminal I/O
- `kernel_exec(flash_offset, word_count)` — copy program from flash → RAM @ 0x4000 and jump
- `kernel_banner()` — print the kernel startup string

### `kernel/boot.fc`
Entry point for the resident kernel image (`build/foxos.bin`). It validates or
formats the flash filesystem and, if `foxos_pack.py` embedded a program, hands
control to it. It does not include the shell.

### `foxfs/foxfs.fc`
Flash filesystem (32-slot flat directory, hierarchical paths):
- `fs_format()` — erase and initialise the cartridge
- `fs_valid()` — check the magic/version header
- `fs_list(parent)` — list a directory
- `fs_df()` — print slot usage
- `fs_create(start, end, type)` — create a file (1) or directory (2)
- `fs_remove(start, end)` — remove an entry
- `fs_resolve_path / fs_resolve_dir` — walk a path string to a slot
- `fs_current_dir` — persistent working-directory state

### `foxsh/foxsh.fc`
Interactive shell, built as its own image (`build/foxsh.bin`). The kernel and
filesystem sources are compiled in front of it as support code, so the shell is
never linked into the resident kernel (`build/foxos.bin`).
- Commands: `help`, `clear`, `pwd`, `ls`, `df`, `cd`, `mkdir`, `touch`, `rm`, `run`, `install`, `format`, `logo`
- `install` copies the embedded ROM program (packed by `foxos_pack.py`) into `/bin/<name>`
- `run PATH` loads and executes a file from the flash filesystem

### `apps/graphics_demo/graphics_demo.fc`
Foreground application loaded at **0x4000**:
- Paints 5 000 pixels with a cycling 16-colour palette, synced to `vblank()`
- Installed with: `install` → then `run /bin/graphics`

## Build targets

| Target   | Description                                              |
|----------|----------------------------------------------------------|
| `make`   | Build kernel + shell + demo and pack the combined ROM    |
| `make os`| Resident kernel ROM only (`build/foxos.bin`, no shell)   |
| `make shell` | Bootable shell image (`build/foxsh.bin`)             |
| `make demo` | Build graphics demo ROM only (`build/graphics_demo.bin`) |
| `make pack` | Embed demo into shell image → `build/foxos-with-demo.bin` |
| `make run`  | Launch FoxVision with the combined ROM                |
| `make clean`| Remove `build/`                                      |
