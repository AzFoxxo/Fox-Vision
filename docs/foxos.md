# FoxOS

**Version:** `v0.0.1`

FoxOS is a small 32-bit operating system for the Fox Vision machine. It is the
`foxos/` project, built from `kernel/kernel.fc`, `foxfs/foxfs.fc`,
`foxsh/foxsh.fc`, and `apps/graphics_demo/graphics_demo.fc`.

FoxOS targets the V2.2 ISA in `--mode em2_am1`:

- `EM = 2` — 32-bit registers, 32-bit data width, 32-bit memory address bus.
- `AM = 0` — word addressing, so one address unit is one 16-bit memory word.
- Storage — a 4 GiB **VF32HardDisk** on **port 6**, driven over the 32-bit bus.
- Traps — `SYS` for syscalls, `SYSRET` for return, and a periodic timer trap
  for preemptive scheduling.

User programs see a Unix-like process ABI with `fork`, `exec`, `waitpid`,
`exit`, `getpid`, file descriptors, `sbrk` and `malloc`/`free`. See
[abi.md](abi.md) for the syscall table and memory layout.

The resident kernel (`kernel` + `foxfs`) and the interactive shell (`foxsh`)
are separate ROM images. `build/foxos.bin` never contains shell code; the
bootable shell is `build/foxsh.bin`, and `build/foxos-with-demo.bin` is that
image with a foreground program packed in.

FoxOS runs foreground programs as processes in the `0x00100000` code window.
`run PATH` block-copies a file from the disk into the current process, and
`install` copies packed ROM programs into `/bin`. Each process gets a private
data window (`$A000`–`$EC77`), heap window (`0x20000000`–`0x203FFFFF`) and code
window. Programs are compiled with a `0x100000` load address and include only
`<foxos.fh>`; they finish with `exit()`/`os_return()`, which emits a `SYS`
exit syscall. The timer shares CPU time between runnable processes.

## Building

From the repository root:

```sh
./build-foxos-new.sh
```

This builds the native host toolchain, then delegates to `foxos/Makefile` to
produce:

| Artifact                          | Description                                   |
| --------------------------------- | --------------------------------------------- |
| `foxos/build/foxos.bin`           | Resident kernel + filesystem                  |
| `foxos/build/foxsh.bin`           | Bootable shell image                          |
| `foxos/build/graphics_demo.bin`   | Foreground demo at `0x100000`                 |
| `foxos/build/edit.bin`            | Foreground text editor at `0x100000`          |
| `foxos/build/brainfuck.bin`       | Foreground Brainfuck interpreter              |
| `foxos/build/foxos-with-demo.bin` | Shell image with the demo and editor embedded |

If the tools are already installed, use the Makefile directly:

```sh
cd foxos
make TOOL_DIR=~/vf16/tools
make check TOOL_DIR=~/vf16/tools
make run TOOL_DIR=~/vf16/tools
```

`make run` sets `FOXVISION_PORT4_DEVICE=VF16TTY` and
`FOXVISION_PORT6_DEVICE=VF32HardDisk` before starting FoxVision. A direct
launch is equivalent to:

```sh
FOXVISION_PORT4_DEVICE=VF16TTY \
FOXVISION_PORT6_DEVICE=VF32HardDisk \
foxvision --rom foxos/build/foxos-with-demo.bin
```

The disk image defaults to `~/vf16/ports/vf32harddisk/port6.img`. Override it
with `FOXVISION_PORT6_HARDDISK_PATH=/path/to/disk.img`.

The resident images include the kernel, filesystem and syscall ABI. `boot.fc`
is the single entry point; its includes pull in the public and internal halves
of each module. Resident text now extends past `$A000`, so globals are linked
at `$D000`:

```sh
foxc -i foxos/kernel/boot.fc -o foxos/build/foxos.bin \
  --mode em2_am1 --mapper 2 --rom-version 3 --strict-format \
  -I foxos --load-address 0 --global-base 0xD000
```

To build a foreground graphics program for the OS user area:

```sh
foxc -i foxos/apps/graphics_demo/graphics_demo.fc \
  -o foxos/build/graphics_demo.bin \
  --mode em2_am1 --load-address 0x100000 --strict-format
```

`tools/foxos_pack.py` embeds a foreground program into the OS ROM at word
address `0x20000`:

```sh
python3 tools/foxos_pack.py foxos/build/foxsh.bin \
  --embed-program foxos/build/graphics_demo.bin \
  --embed-name graphics \
  --embed-address 0x20000 \
  --output foxos/build/foxos-with-demo.bin
```

The embedded image does not modify disk storage. When the shell receives the
`install` command, it ensures the standard root directories (`/bin`, `/home`,
`/temp`, `/etc`) exist and copies the embedded programs into `/bin/<name>`.
Filesystem names are limited to sixteen ASCII characters; the names stored in
embedded program records are limited to eight.

At the FoxOS prompt:

```text
install
run /bin/graphics
```

## Memory map

FoxOS addresses memory in 16-bit words (`AM=0`). FoxC emits logical addresses;
`DB`, `HB` and `CB` relocate data, heap and code references before they reach
RAM:

| Logical range               | Contents / physical placement                              |
| --------------------------- | ---------------------------------------------------------- |
| `0x0000` – `0xCFFF`         | Early resident kernel, shell and syscall ABI text           |
| `0xD000` – `0xEC77`         | Resident globals and kernel stack, physically `0x43000`–`0x47C77` |
| `0xEC78` – `0xFFFF`         | VRAM                                                       |
| `0x00020000`                | Embedded foreground programs placed by `foxos_pack.py`      |
| `0x00048000` – `0x0006FC77` | Process data windows (`DB`), one per task slot               |
| `0x00070000`                | Trap frame (`VEC`) and saved process frames                 |
| `0x00100000` – `0x008FFFFF` | Process code windows (`CB`)                                 |
| `0x20000000` – `0x203FFFFF` | Process heap windows (`HB`)                                 |

Resident instruction fetches are physical, so the kernel text can extend past
`$CFFF`. Resident data accesses in `$A000`–`$EC77` are relocated by `DB`; the
emulator initializes kernel `DB = 0x00043000`, placing globals emitted at
`$D000` and the kernel stack in physical memory at `0x43000`–`0x47C77`, above
the text. Foreground processes instead see `0xA000`–`0xEC77` as their private
data window, with their own `DB`, `HB` and `CB` bases, so `fork` can copy a
process without copying the resident OS.

The resident shell is about 75 000 words with the ABI included, so its globals
are emitted at `$D000` and relocated above the text. Embedded programs start at
`0x20000`; foreground programs are linked at `0x100000`. Task data windows are
placed at physical `0x48000 + slot * 0x5000`, above the resident text and the
embedded records, and end below the trap frame at `0x70000`.

## VF32HardDisk protocol

Port 6 is a true 32-bit port device. In `EM=2` a full-width transfer to a
primary register is a single 32-bit bus transaction. A transaction is:

1. **Address** — one 32-bit word containing the byte offset in the 4 GiB image.
2. **Command** — one 32-bit word:
   - `bit 0` — `1` writes, `0` reads;
   - `bits 1–31` — transfer count minus one.
3. **Data** — one 32-bit word per transfer. The device advances its address by
   4 bytes per word.

FoxOS stores disk positions as 32-bit *word* indices and multiplies by four at
the device boundary. `disk_read_begin` / `disk_write_begin` start a block
transaction, and `disk_read` / `disk_write` perform single-word metadata
accesses.

A fresh image is sparse: `fs_format()` writes only the 10244-word metadata
region, so creating the 4 GiB filesystem is fast and does not touch the data
area. Formatting also installs the standard root hierarchy (`/bin`, `/home`,
`/temp`, `/etc`), so a newly installed filesystem is immediately usable.

## Filesystem layout

FoxFS version `8` uses 32-bit words throughout. Metadata occupies words
`0`–`10243`, and file data starts at word `10244`:

|    Word(s) | Contents                                                             |
| ---------: | -------------------------------------------------------------------- |
|          0 | Magic `0x464F5833` (`FOX3`)                                          |
|          1 | Format version (`8`)                                                 |
|          2 | Entry count (`512`)                                                  |
|          3 | First data word (`10244`)                                            |
|     4–2051 | Entry records: four words per slot (type, parent, name length, size) |
| 2052–10243 | Entry names: sixteen 32-bit words per slot, one ASCII character each |
|     10244– | File data, one `0x1FF000`-word chunk per slot                        |

An entry record uses one 32-bit word per field: type (`0` free, `1` program,
`2` directory, `3` text), parent slot (`512` means the root), name length, and
size. Type 1 stores legacy word-packed program data, so its size is in 32-bit
words; type 3 stores four ASCII bytes per word, so its size is in bytes and host
file managers show the text directly. Packing an entry into a single four-word
record lets a lookup read one slot with one four-word disk transaction instead
of three separate table accesses.

Type 3 text files are understood by `edit`, BASIC `LOAD`, the shell `disp`
command, the Brainfuck interpreter, and the `open`/`read`/`write` syscalls (with
byte offsets). Type 1 remains word-addressed for compiled ROMs, so `run`/`exec`
still work unchanged.

Each slot has a fixed data chunk of `0x1FF000` words (just under 8 MiB). All
512 chunks plus the metadata still fit inside the 2³⁰ 32-bit words in the
4 GiB image. Entries have names up to sixteen ASCII characters; `.` and `..`
are reserved for path traversal and cannot be created. Directories can contain
files and other directories; a directory cannot be removed while it contains
entries, and the current working directory cannot be removed.

## Commands

| Command      | Action                                                                                    |
| ------------ | ----------------------------------------------------------------------------------------- |
| `help`       | Show command help                                                                         |
| `version`    | Print the FoxOS version                                                                   |
| `clear`      | Clear the terminal screen                                                                 |
| `pwd`        | Print the current directory                                                               |
| `ls`         | List the current directory                                                                |
| `ls -l`      | List the current directory with types and sizes                                           |
| `df`         | Print entry usage and the per-file size limit                                             |
| `cd PATH`    | Change directory using an absolute or relative path; bare `cd` returns to the root        |
| `touch PATH` | Create an empty file at a path                                                            |
| `disp PATH`  | Display the contents of a file                                                            |
| `mkdir PATH` | Create a directory at a path                                                              |
| `rm PATH`    | Remove a file or empty directory at a path                                                |
| `run PATH`   | Load and run a program from the disk at `0x100000`                                        |
| `install`    | Ensure the standard root directories and copy the embedded ROM program into `/bin/<name>` |
| `format`     | Format FoxFS v8 and create `/bin`, `/home`, `/temp`, `/etc`                               |
| `logo`       | Print the FoxOS logo                                                                      |

Paths use `/` separators. `/` selects the root, and `.` and `..` select the
current and parent directories. The prompt displays the current path.

The shell reads and writes ASCII through port 4 and treats Enter as carriage
return (`0x0D`). It emits CRLF (`0x0D`, `0x0A`) for terminal line endings.
Programs return to the kernel with `exit()` (a `SYS_EXIT` syscall); the shell
itself remains resident and is resumed by the scheduler when its children
exit. Pressing the host `Super+Q` shortcut while a guest program is running
raises trap cause `2`; the kernel terminates the foreground job (including any
forked children) and resumes FoxSH. The shortcut has no effect at the shell
prompt. Because VRAM overlaps the tail of the resident image, the emulator
restores that range from its boot-time copy before delivering the quit trap, so
a graphics program cannot leave corrupted shell code behind.

## Regression tests

`make check` builds four images and runs each headlessly with a fresh sparse
disk:

- `tests/foxfs32_test.fc` checks the v8 header, four-word entry records,
  directory creation and lookup, name rules, 32-bit disk I/O, path resolution
  and slot reuse. It prints `PASS`.
- `tests/abi_test.fc` embeds `tests/abi_user.fc` as a user process and checks
  syscalls, `malloc`/`free`, preempted children and `waitpid` status. It prints
  `PASS ABI`.
- `tests/editor_test.fc` embeds `tests/editor_test_user.fc`, which uses the
  editor core through `<foxos.fh>` to load a file, edit it, save it and verify
  the result on disk. It prints `PASS EDITOR`.
- `tests/exec_test.fc` stores `tests/exec_user.fc` in FoxFS and runs it through
  the kernel's external-program path, covering the shell's `<program>` command.
  It prints `PASS EXEC`.

The Makefile target fails if any image omits its success marker or prints
`FAIL`.
