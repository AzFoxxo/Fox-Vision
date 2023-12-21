# Specification of the Fox Vision architecture

## Processor
The CPU is a single threaded 8MHz RISC chip. It uses the FoxVision16 architecture.
The CPU contains several registers:
- X - 16 bit reg (general purpose register #1)
- Y - 16 bit reg (general purpose register #2)
- PC - 16 bit reg (program counter register, points the current location in RAM the CPU is executing)
- OSR - 8 bit register (bit 1: Equality? bit 2: active general purpose register)

### Instruction breakdown
8 bits: opcode
9-24 bits: addresses/values

## Memory
The device has a total of 65,536 bytes (16kb) of addressable space.
This memory is broken up into several sections, the first 4kb is reserved for ROM and with the rest for general purpose use