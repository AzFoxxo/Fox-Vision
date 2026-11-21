# Mount a VF32HardDisk in Dolphin

`foxfs_mount` is a tiny writable FUSE3 filesystem for the FoxOS `VF32HardDisk`
image. It turns the raw `port6.img` cartridge into a normal folder, so KDE's
Dolphin (or any other file manager) can browse and edit the FoxFS files inside
it.

## Build

Requires libfuse3 development files (`pkg-config fuse3`):

```bash
cd foxos/mountfoxfs
make
```

## Use

The wrapper builds the tool and mounts the default port6 image at `~/vf16/hd`:

```bash
./mount-port6.sh               # mount (detached)
./unmount-port6.sh             # unmount
dolphin ~/vf16/hd
```

`./mount-port6.sh --unmount` is an alias for the unmount script. Both honour
`FOXFS_MOUNT` for the mount point; the mount script also honours
`FOXVISION_PORT6_HARDDISK_PATH` for the image path. Extra arguments are passed
to FUSE.

To mount somewhere else by hand:

```bash
mkdir -p ~/foxfs
./foxfs_mount ~/vf16/ports/vf32harddisk/port6.img ~/foxfs -f
```

Keep it running in that terminal, then open the folder in Dolphin:

```bash
dolphin ~/foxfs
```

Unmount with `Ctrl+C` in the terminal, or from another shell:

```bash
fusermount3 -u ~/foxfs
```

Extra arguments after the mount point are passed to FUSE, e.g. `-d` for debug
output or `-o allow_other` to let other users see the mount.

The utility validates the FoxFS v8 header and walks the 512-entry table for
directories and names. You can create, rename, delete, and edit files and
directories in Dolphin; changes are written straight back to the image.

FoxFS v8 has two file types:

- **type 1 (program)** — legacy word-packed data, size in 32-bit words. This is
  what compiled Fox16/FoxC ROMs under `/bin` use.
- **type 3 (text)** — four ASCII bytes packed into each 32-bit word, size in
  bytes. This is what the FoxOS `edit` app now writes, so Kate and Dolphin show
  ordinary text with no conversion.

New text files created through the mount are type 3. Names are limited to 16
ASCII characters, and a file is limited to one slot's data chunk (just under
8 MiB). Pass `-o ro` as an extra FUSE option if you want a read-only mount.

## Installing rebuilt programs

Do **not** copy `build/*.bin` directly into `/bin`: those files include a ROM
header and pack two 16-bit instructions per 32-bit disk word. Use the installer,
which strips the header and stores one instruction per FoxFS word:

```bash
./mount-port6.sh
./install-rom.py ../build/edit.bin ~/vf16/hd/bin/edit
./install-rom.py ../build/basic.bin ~/vf16/hd/bin/basic
./unmount-port6.sh
```

The FUSE mount keeps these entries as type 1 so FoxOS can execute them.

## Old word-packed text files

Files saved by the previous `edit` build are still type 1 and will look full of
NUL bytes in Kate (`48 00 00 00 65 00 00 00` = "He"). They still load correctly
in FoxOS; saving them once with the new `edit` converts them. To convert them
from the host without opening FoxOS, unmount the FUSE mount and run:

```bash
./unmount-port6.sh
./foxfs_migrate_text.py            # preview candidates
./foxfs_migrate_text.py --apply    # rewrite in place
./mount-port6.sh
```

The migration tool touches type-1 files whose data looks like text (either
word-packed or byte-packed); compiled ROMs are skipped. `foxfs_text.py` remains
as a one-off legacy decoder; do **not** use its `encode` mode on a type-3 file.

Files under `/bin` are compiled Fox16/FoxC ROMs, not text; edit the `.fc`
sources in `apps/` instead.
