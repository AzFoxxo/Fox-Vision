# FoxOS

A small 32-bit operating system for the **Fox16** virtual machine, structured
as three clean sub-projects.

**Version:** `v0.0.1`

FoxOS targets the V2.0 ISA in `--mode em2_am1`: `EM=2` (32-bit registers and
address bus) with word addressing (`AM=0`). Storage is a 4 GiB **VF32HardDisk**
cartridge on **port 6**, used as an array of 32-bit words.

The resident kernel (`kernel` + `foxfs`) and the interactive shell (`foxsh`)
are built as **separate images**. `build/foxos.bin` never contains shell code.
Applications get the useful OS services through the small public header
`foxos.fh`; the kernel, shell and tests include the full modules (which also
contain OS-only helpers such as formatting and program loading).

```
foxos/
├── Makefile                  ← top-level build system
├── foxos.fh                  ← public application header (API subsets only)
├── kernel/
│   ├── kernel.fc             ← full module (public API + OS internals)
│   ├── kernel_api.fc         ← public: terminal, disk I/O, line[] buffer
│   ├── kernel_internal.fc    ← OS-only: loader, embedded images, banner
│   └── boot.fc               ← resident-kernel entry point (main)
├── foxfs/
│   ├── foxfs.fc              ← full module (public API + OS internals)
│   ├── foxfs_api.fc          ← public: paths, entries, create/remove
│   └── foxfs_internal.fc     ← OS-only: format, validate, list, root dirs
├── foxsh/
│   └── foxsh.fc             ← interactive shell entry (full modules)
├── tests/
│   └── foxfs32_test.fc       ← FoxFS regression test (`make check`)
└── apps/
    ├── graphics_demo/
    │   └── graphics_demo.fc  ← 16-colour scrolling-gradient demo
    ├── edit/
    │   └── edit.fc           ← full-screen text editor
    └── brainfuck/
        └── brainfuck.fc      ← Brainfuck interpreter
```

## Quick Start

```bash
# Build everything from the repo root (builds toolchain first):
./build-foxos-new.sh

# Or, if the VF16 tools are already installed:
cd foxos
make TOOL_DIR=~/vf16/tools

# Run the filesystem regression test:
make check TOOL_DIR=~/vf16/tools

# Launch FoxVision with the combined ROM:
make run
```

`make run` starts FoxVision with the TTY on port 4 and the 4 GiB disk on
port 6. The disk image defaults to `~/vf16/ports/vf32harddisk/port6.img`; set
`FOXVISION_PORT6_HARDDISK_PATH` to use a different file.

## Application header (`foxos.fh`)

`foxos.fh` is the small public header for FoxOS programs. Add one include to a
foreground application to get the OS services an application needs:

```c
#include <foxos.fh>
```

It expands only the public API subsets of the kernel and FoxFS into the
translation unit, providing:

- terminal I/O: `tty_putc`, `tty_putc32`, `tty_puts`, `tty_getc`, `tty_newline`,
  `tty_putu32`
- 32-bit disk I/O: `disk_read_begin`, `disk_write_begin`, `disk_read`, `disk_write`
- FoxFS: `fs_resolve_path`, `fs_resolve_dir`, `fs_find_child`, `fs_create`,
  `fs_remove`, `fs_read_entry`, `fs_write_entry`, `fs_data_base`, `fs_name_*`,
  `fs_entry_base`, `fs_split` and the path helpers
- shared state and constants: `line[]` / `line_len`, `fs_current_dir`,
  `fs_e_*`, `ROOT_SLOT`, `NOT_FOUND`, `FILE_CHUNK_WORDS`, `ENTRY_COUNT`, port
  numbers

OS/shell internals are deliberately not exported: program loading,
`kernel_banner`, embedded-image constants, `fs_format`, `fs_valid`, `fs_list`,
`fs_df` and root-directory setup. The kernel, shell and test images include the
full `kernel.fc` / `foxfs.fc` modules to get those.

The Makefile passes `-I <foxos directory>` to `foxc`, so the include works from
any application source. When compiling by hand, pass that directory with `-I`
yourself. Because FoxC has no separate link step, `foxos.fh` is a whole-code
header: just the API support code is compiled into each application ROM.
`edit` and `bf` use it; the graphics demo only needs FoxC built-ins.

## Sub-projects

### `kernel/kernel.fc`
Low-level hardware interface:
- `disk_read_begin` / `disk_write_begin` — start a 32-bit block transaction on port 6
- `disk_read` / `disk_write` — single-word metadata helpers
- `tty_putc` / `tty_puts` / `tty_getc` — port 4 (VF16TTY) terminal I/O
- `kernel_exec(disk_word, word_count)` — block-copy a program from the disk to
  `0x100000` and jump to it
- `kernel_exec_embedded(rom_offset, word_count)` — copy the program embedded
  by `foxos_pack.py` to the same area
- `kernel_banner()` — print the kernel startup string

The VF32HardDisk protocol is:
1. one 32-bit address word (byte offset),
2. one 32-bit command word (`bit 0` = write, `bits 1–31` = count − 1),
3. one 32-bit data word per transfer, with the address auto-incrementing by 4.

FoxOS keeps a disk address in 32-bit words and multiplies by four when talking
to the device.

### `kernel/boot.fc`
Entry point for the resident kernel image (`build/foxos.bin`). It validates or
formats the filesystem and, if `foxos_pack.py` embedded a program, hands
control to it. It does not include the shell.

### `foxfs/foxfs.fc`
FoxFS v8, a 512-slot filesystem with hierarchical paths. All metadata fields,
slot indices and data units are 32-bit words packed into the first 10244 words
of the cartridge:
- `fs_format()` — rewrite the metadata header and entry records, then create
  the standard root directories (`/bin`, `/home`, `/temp`, `/etc`)
- `fs_valid()` — check the magic/version header
- `fs_list(parent, long_format)` — list a directory, optionally with types and sizes
- `fs_df()` — print entry usage and the per-file size limit
- `fs_create(start, end, type)` — create a file (1) or directory (2)
- `fs_remove(start, end)` — remove an entry
- `fs_resolve_path` / `fs_resolve_dir` — walk a path to a slot
- `fs_current_dir` — persistent working-directory state

Each slot has a four-word record (type, parent, name length, size) followed by
a sixteen-word name field, so entries support names up to sixteen ASCII
characters. File data starts at word `10244`, with one `0x1FF000`-word
(just under 8 MiB) chunk per slot.

A fresh 4 GiB image is sparse. `fs_format()` only rewrites the 10244-word
metadata region and never erases the data area. It also installs the standard
root hierarchy, so a new filesystem starts with `/bin`, `/home`, `/temp` and
`/etc` ready to use.

### `foxsh/foxsh.fc`
Interactive shell, built as its own image (`build/foxsh.bin`). It uses the full
kernel and filesystem modules (including OS-only commands such as `format` and
`ls`), so the shell is never linked into the resident kernel
(`build/foxos.bin`).
- Commands: `help`, `version`, `clear`, `pwd`, `ls`, `ls -l`, `df`, `cd`, `mkdir`, `touch`, `rm`, `run`, `install`, `format`, `logo`
- `install` ensures the standard root directories exist and copies the embedded
  ROM programs (packed by `foxos_pack.py`) into `/bin/<name>`
- `run PATH` loads and executes a file from the disk filesystem

### `apps/graphics_demo/graphics_demo.fc`
Foreground application loaded at **0x100000**:
- Paints 5 000 pixels with a cycling 16-colour palette, synced to `vblank()`
- Installed with: `install` → then `run /bin/graphics`

## Memory map

| Range (word addresses) | Use |
| --- | --- |
| `0x0000` – `0x9FFF` | Kernel and shell image, constant data |
| `0xA000` – *(end of globals)* | FoxC globals, growing upward |
| *(end of globals)* – `0xEC77` | FoxC software stack, growing upward |
| `0xEC78` – `0xFFFF` | VRAM |
| `0x00010000` | Programs embedded by `foxos_pack.py` |
| `0x00100000` | Foreground program load and execution area |

In 32-bit (`EM=2`) builds FoxC places globals at `0xA000`. The software call
stack starts at the first free word after the globals, so an application with
large global arrays (such as the editor's text buffer) cannot have its frames
overwrite its globals. The hardware stack starts just below the VRAM window
and grows downward; at least 1024 words must remain between the globals and
VRAM or compilation fails. The embedded programs sit at `0x10000`, and
foreground programs run from `0x100000`, so `foxos_pack.py` requires
foreground images built with `--load-address 0x100000`.

## Build targets

| Target   | Description                                              |
|----------|----------------------------------------------------------|
| `make`   | Build kernel + shell + demo and pack the combined ROM    |
| `make os`| Resident kernel ROM only (`build/foxos.bin`, no shell)   |
| `make shell` | Bootable shell image (`build/foxsh.bin`)             |
| `make demo` | Build graphics demo ROM only (`build/graphics_demo.bin`) |
| `make edit` | Build editor ROM only (`build/edit.bin`) |
| `make pack` | Embed demo into shell image → `build/foxos-with-demo.bin` |
| `make check` | Run the 32-bit FoxFS regression test                |
| `make run`  | Launch FoxVision with the combined ROM                |
| `make clean`| Remove `build/`                                      |
