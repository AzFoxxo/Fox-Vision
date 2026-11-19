# FoxOS

**Version:** `v0.0.1`

FoxOS is a small 32-bit operating system for the Fox Vision machine. It is the
`foxos/` project, built from `kernel/kernel.fc`, `foxfs/foxfs.fc`,
`foxsh/foxsh.fc`, and `apps/graphics_demo/graphics_demo.fc`.

FoxOS targets the V2.0 ISA in `--mode em2_am1`:

- `EM = 2` — 32-bit registers, 32-bit data width, 32-bit memory address bus.
- `AM = 0` — word addressing, so one address unit is one 16-bit memory word.
- Storage — a 4 GiB **VF32HardDisk** on **port 6**, driven over the 32-bit bus.

The resident kernel (`kernel` + `foxfs`) and the interactive shell (`foxsh`)
are separate ROM images. `build/foxos.bin` never contains shell code; the
bootable shell is `build/foxsh.bin`, and `build/foxos-with-demo.bin` is that
image with a foreground program packed in.

FoxOS runs a single foreground program at a time. `run PATH` block-copies a
file from the disk to the `0x100000` program area and transfers control to it.
Program files contain raw 16-bit machine words. Programs must be assembled or
compiled with a `0x100000` load address, and can return control to the resident
kernel with the `OSRET` instruction.

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

For separate compiler stages, the resident kernel is:

```sh
cat foxos/kernel/kernel.fc foxos/foxfs/foxfs.fc foxos/kernel/boot.fc \
  > /tmp/foxos-image.fc
foxc -i /tmp/foxos-image.fc -o foxos/build/foxos.bin \
  --mode em2_am1 --strict-format --load-address 0
```

To build a foreground graphics program for the OS user area:

```sh
foxc -i foxos/apps/graphics_demo/graphics_demo.fc \
  -o foxos/build/graphics_demo.bin \
  --mode em2_am1 --load-address 0x100000 --strict-format
```

`tools/foxos_pack.py` embeds a foreground program into the OS ROM at word
address `0x10000`:

```sh
python3 tools/foxos_pack.py foxos/build/foxsh.bin \
  --embed-program foxos/build/graphics_demo.bin \
  --embed-name graphics \
  --embed-address 0x10000 \
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

FoxOS addresses memory in 16-bit words (`AM=0`):

| Range               | Contents                                               |
| ------------------- | ------------------------------------------------------ |
| `0x0000` – `0x97FF` | Kernel and shell image, constant data                  |
| `0x9800` – `0x9FFF` | FoxC software stack (frames grow upward from `0x9800`) |
| `0xA000` – `0xEC77` | FoxC globals                                           |
| `0xEC78` – `0xFFFF` | VRAM                                                   |
| `0x00010000`        | Embedded foreground programs placed by `foxos_pack.py` |
| `0x00100000`        | Foreground program load and execution area             |

The 32-bit (EM=2) layout reserves code space up to `0x9800`, about 39 000
words. The shell image is roughly 34 000 words, which is why the embedded
programs were moved up to `0x10000` and foreground programs run from
`0x100000`.

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

An entry record uses one 32-bit word per field: type (`0` free, `1` file,
`2` directory), parent slot (`512` means the root), name length, and size in
32-bit words. Packing an entry into a single four-word record lets a lookup
read one slot with one four-word disk transaction instead of three separate
table accesses.

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
Programs return to the resident kernel with `OSRET`.

## Regression test

`make check` builds `tests/foxfs32_test.fc` against the kernel and filesystem,
formats a fresh sparse image, then exercises:

- the v8 header and four-word entry records;
- directory creation and child lookup;
- sixteen-character names, duplicate rejection, and reserved `.`/`..` names;
- 32-bit single-word metadata and data accesses;
- a multi-word block write and read over the port-6 bus;
- absolute-path resolution, trailing slashes, and `..` handling;
- directory, current-directory, and slot reuse rules.

The test prints `PASS` on success and the Makefile target fails if it does not.
