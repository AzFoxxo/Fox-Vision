# Fox Vision (FoxVision16)

FoxVision16 is a 16-bit RISC architecture designed to be simple to program and straightforward to implement.

This project was inspired by lessons learned from SEBIS and QKVT. Unlike those systems, FoxVision16 is a pure 16-bit machine with 16-bit addressing only, simplifying the architecture and avoiding several limitations encountered in those earlier designs.

## Project Overview

The project is composed of four main components:

- `Fox16ASM` — An assembler that converts `.f16` assembly files into machine code
- `FoxC` — A compiler that translates FoxC source code into FoxVision16 assembly or machine code
- `FoxLink` — A linker that combines compiled objects into executable ROM images
- `FoxDecompiler` — A tool for analyzing and decompiling FoxVision16 ROMs back into a readable form
- `FoxVision` — A virtual machine that executes compiled ROM files
- `Fox16Shared` — Shared code used across the toolchain (assembler, compiler, linker, and VM)

## Features

- [X] Custom 16-bit assembly language
- [X] Assembler
- [X] C-like high-level language (FoxC)
- [X] Compiler toolchain
- [X] Linker
- [X] Decompiler
- [X] Virtual machine
- [X] FoxOS syscall ABI with preemptive processes (`fork`/`exec`/`wait`), `malloc`/`free` and Unix-like I/O

FoxOS applications include only `foxos/foxos.fh`. The syscall table, memory
windows and process model are documented in [docs/abi.md](docs/abi.md); the OS
itself is described in [docs/foxos.md](docs/foxos.md). The ISA V2.2 trap and
window extensions are in [docs/isa.md](docs/isa.md).

## Configuring Emulator Memory

The virtual machine exposes up to 4 GB of RAM. The amount of memory can be configured at startup with `--memory`, or with the `FOXVISION_MEMORY_SIZE` environment variable, using sizes between 128 KB and 4 GB. Sizes are given in bytes and accept `K`, `M`, or `G` suffixes:

```sh
foxvision --rom program.bin --memory 64M
FOXVISION_MEMORY_SIZE=512M foxvision --rom program.bin
```

The memory size can also be changed at runtime from **Settings → Memory size** in the GUI. Shrinking memory discards data above the new limit.

Guest programs in extended modes can read the configured size from the read-only `MS` register (introduced in ISA V2.1, in KiB units), or from FoxC with `get_memory_size()`.

## Technical Specification

For a full list of technical specifications, see [ISA](docs/isa.md).
