# Fox Vision (Viso Fox)
Fox vision (FoxVision16) is a 16 bit RISC system designed to be simple to write code for and easy to implement.

This project is based on the failures and what worked from both [SEBIS](https://github.com/AzFoxxo/SEBIS) and [QKVT](https://github.com/AzFoxxo/Quirky-Virty). Unlike those projects, this project is a pure 16 bit machine with 16 bit only addressing to simplify the machine and overcome some of the technical limitations of those.

## Project
The project is composed of three different parts:
- `Fox16ASM` - An assembler which converts .f16 assembly files into machine code
- `FoxVision` - A virtual machine that executes ROM files 
- `Fox16Shared` - Shared code between the assembler and virtual machine
- `FoxC` - A Go compiler for a C-like subset language targeting `.f16` assembly

## FoxC (new high-level language)

`FoxC` is the preferred high-level language path for this repository and replaces the older Python transpiler workflow.

Build from the repo root:

```bash
go build -o foxc ./FoxC
```

Compile FoxC to `.f16`:

```bash
./foxc -i FoxC/examples/sample.foxc -o test/sample.f16
```

Then assemble to ROM:

```bash
dotnet run --project Fox16ASM/Fox16ASM.csproj -- -i test/sample.f16 -o vfox16.bin
```

## Features
- [X] Custom assembly language
- [X] Assembler
- [X] Virtual machine

## Todo
- [X] - Finalise graphics drawing spec
- [ ] - Implement graphics renderer

## Technical specification
For a full list of technical speciations, see [Spec](docs/spec.md).
