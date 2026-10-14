# FoxOS

`FoxC/examples/foxos.fc` is a small operating system for the Fox Vision
machine. It boots in extended mode, initializes a persistent filesystem when
needed, and provides a line-oriented shell over the VF16TTY device.

FoxOS supports a single foreground program at a time. The `run PATH` command
loads a file from flash into the reserved `$4000` program area and transfers
control to it. Program files contain raw 16-bit machine words, with a maximum
size of 1000 words. Programs must be assembled or compiled with a `$4000` load
address and must terminate with the `OSRET` instruction, which returns control
to the FoxOS boot shell.

## Building

From the repository root:

```sh
./build-foxos.sh
```

This builds the native host toolchain, produces `build/foxxo-os.bin`, builds the
foreground lifecycle test at `$4000`, and packs it into the emulator's flash
image. It can then be loaded directly by FoxVision:

```sh
FOXVISION_PORT4_DEVICE=VF16TTY \
FOXVISION_PORT5_DEVICE=VF16Flash \
foxvision --rom build/foxxo-os.bin
```

The default foreground program is
`FoxC/examples/graphics_demo.fc`. To use the lifecycle test instead:

```sh
./build-foxos.sh \
  --program FoxC/examples/foxos_program_test.fc
```

The ROM build stage also accepts any `.fc` or `.f16` program. Use
`--mode legacy` for a legacy image, or set `INSTALL_DIR`/`--install-dir` when
the host tools should be installed somewhere other than `$HOME/vf16/tools`.

Keep the generated image below 32768 words when loading it in extended mode.
The shell source is intentionally compact so string data is not executed as
instructions after the image boundary.

For separate compiler stages, the equivalent commands are:

```sh
foxc -i FoxC/examples/foxos.fc -o /tmp/foxos.f16 --mode extended --asm
fox16asm -i /tmp/foxos.f16 -o build/foxos.bin --mode extended --strict-format
```

To build a foreground graphics program for the OS user area:

```sh
foxc -i FoxC/examples/graphics_demo.fc -o build/graphics.bin \
  --mode extended --load-address 0x4000 --strict-format
```

The repository includes a small lifecycle test at
`FoxC/examples/foxos_program_test.fc`. It draws an animated VRAM pattern,
prints status through the TTY, and returns to FoxOS when Escape or Enter is
held:

```sh
foxc -i FoxC/examples/foxos_program_test.fc -o build/ostest.bin \
  --mode extended --load-address 0x4000 --strict-format
```

The build script embeds the foreground program into the OS ROM at `$7000`.
The embedded image does not modify flash storage. When FoxOS receives the
`install` command, it formats the filesystem, creates the standard
directories, creates the embedded program's named file in `/bin`, and copies
the program into it. The default name is `graphics`; use
`--program-name <name>` to select another name of up to eight ASCII
characters.

```sh
./build-foxos.sh \
  --program FoxC/examples/graphics_demo.fc
```

Then start the combined OS ROM:

```sh
FOXVISION_PORT4_DEVICE=VF16TTY \
FOXVISION_PORT5_DEVICE=VF16Flash \
foxvision --rom build/foxxo-os.bin
```

At the FoxOS prompt, run `install`, then run the embedded program:

```text
install
run /bin/graphics
```

`install` intentionally formats flash. Run it again only when you want to
recreate the filesystem from the program embedded in the current OS ROM.

FoxC places global variables at `$8000`, above the maximum `$0000-$7FFF`
extended ROM address range, and starts its software call stack at `$7800`.
The embedded program metadata occupies `$7000` and the following words, so
the stack cannot overwrite it during `install`.

The shell reads and writes ASCII through port 4 and treats Enter as carriage
return (`0x0D`). It emits CRLF (`0x0D`, `0x0A`) for terminal line endings. It uses the command/value
protocol on port 5:

- read: write `(address * 2)`, then read the result;
- write: write `(address * 2) + 1`, then write the value.

## Filesystem layout

The flash device is word-addressed. FoxOS uses the following fixed layout:

| Address | Contents |
| ---: | --- |
| 0 | Magic `18008` |
| 1 | Format version (`6`) |
| 2-33 | Length of entries `0-31` |
| 34-65 | Entry type: `0` free, `1` file, `2` directory |
| 66-97 | Entry name lengths |
| 98-129 | Parent slot for each entry (`32` means root) |
| 130-385 | Thirty-two eight-word entry names |
| 386-32767 | File data area, allocated in 1000-word chunks |

Entries are stored in thirty-two fixed slots and have names up to eight ASCII
characters. The first 386 words are the metadata area, which is reserved as
filesystem capacity but is counted as used only as entries occupy it. Each
active entry uses 12 words (four metadata words and eight name words). Files
additionally reserve a 16-word data chunk, while directories do not. The
remaining cart capacity is available for file chunks. `format` erases all
32,768 flash words before recreating the metadata header. The format version
is `6`; formatting is required when opening a disk created by the older
four-entry format.
Directories can contain files and other directories. The thirty-two-entry
limit is global, and a directory cannot be removed while it contains entries.

## Commands

| Command | Action |
| --- | --- |
| `help` | Show command help |
| `clear` | Clear the terminal screen |
| `format` | Erase the full cart and reset the filesystem metadata |
| `install` | Format the filesystem and create `/bin`, `/etc`, `/home`, and `/tmp` |
| `ls` | List entries in the current directory |
| `df` | Show free and used words across the full 32K-word cart |
| `cd PATH` | Change directory using an absolute or relative path |
| `touch PATH` | Create an empty file at a path |
| `mkdir PATH` | Create an empty directory at a path |
| `rm PATH` | Remove a file or empty directory at a path |
| `run PATH` | Load and run a raw Fox16 program from flash |

Paths use `/` separators. `/` selects the root, and `.` and `..` select the
current and parent directories. The prompt displays the current path.

FoxOS uses FoxC string literals for static shell text and the existing integer,
loop, pointer, port, and flash primitives. There is no separate runtime library
in the ROM.
