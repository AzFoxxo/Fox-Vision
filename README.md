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

## Technical Specification

For a full list of technical specifications, see [ISA](docs/isa.md).
