# Fox Vision (Viso Fox)
Fox vision (FoxVision16) is a 16 bit RISC system designed to be simple to write code for and easy to implement.

This project is based on the failures and what worked from both [SEBIS](https://github.com/AzFoxxo/SEBIS) and [QKVT](https://github.com/AzFoxxo/Quirky-Virty). Unlike those projects, this project is a pure 16 bit machine with 16 bit only addressing to simplify the machine and overcome some of the technical limitations of those

## Project
The project is composed of three different parts:
- `Fox16ASM` - An assembler which converts .fox16 assembly files into machine code
- `FoxVision` - A virtual machine that executes ROM files 
- `Fox16Shared` - Shared code between the assembler and virtual machine

## Features
- [X] Custom assembly language
- [X] Assembler
- [X] Virtual machine

## Todo
- [ ] - Finalise graphics drawing spec
- [ ] - Implement graphics renderer

## Technical specification
For a full list of technical speciations, see [Spec](docs/spec.md).
