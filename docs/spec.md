# Specification of the Fox Vision architecture

## Processor
The CPU is a single threaded 8MHz RISC chip. It uses the FoxVision16 architecture.
The CPU contains several registers:
- X - 16 bit reg (general purpose register #1)
- Y - 16 bit reg (general purpose register #2)
- PC - 16 bit reg (program counter register, points the current location in RAM the CPU is executing)
- OSR - 8 bit register (bit 1: Equality? bit 2: active general purpose register)

## CPU opcodes
- `0000 0000` - `NOP` - Waste clock cycle
- `0000 0001` - `LFM` - Load 2 byte value from memory in active register
- `0000 0010` - `WTM` - Write to memory the value of the active register
- `0000 0011` - `SRA` - Set register active (X - 0, Y - 1)
- `0000 0100` - `AXY` - Add X and Y and store result in active register
- `0000 0101` - `SXY` - Subtract X from Y and store result in active register
- `0000 0110` - `MXY` - Multiply X by Y and store result in active register
- `0000 0111` - `DXY` - Divide X by Y and store result in active register
- `0000 1000` - `EQU` - Check if X and Y registers are equal and store result in 
- `0000 1001` - `LEQ` - Check if active register is more than non-active register (e.g. X (active) < Y (non-active))
- `0000 1010` - `JPZ` - Jump if zero to 2 byte wide address
- `0000 1011` - `JNZ` - Jump if not zero to 2 byte wide address
- `0000 1100` - `JPL` - Jump if less than to 2 byte wide address
- `0000 1101` - `CLR` - Clear equality flag
- `0000 1110` - `HLT` - Halt program execution (quit/power-off)
- `0000 1111` - `BSL` - Bitshift to left value in active register
- `0001 0000` - `BSR` - Bitshift to right value in active register
- `0001 0001` - `AND` - AND bitwise value in active register by value in non-active register
- `0001 0010` - `ORA` - OR bitwise value in active register by value in non-active register
- `0001 0011` - `XOR` - XOR bitwise value in active register by value in non-active register
- `0001 0100` - `DWR` - Direct write (to) register sets the value given (16 bit decimal) to the active register

### Instruction breakdown
8 bits: opcode
9-24 bits: addresses/values

# Graphics
Fox Vision supports a display size of 240x135.

The VRAM is the least 8kb of RAM, it split into the following sections
- 80 bytes are reserved for graphics commands
- 12 bytes are used to store RGB colours for each of the 4 colours supported.
- 8100 bytes are used to store colours, two bits per pixel (going horizontal from left to right and then from top to bottom)

## PPU instructions
The PPU has 80 bytes of addressable space for instructions. The clock cycle on the PPU is 4MHZ however certain instructions like drawing to the screen take significantly more cycles.

The PPU has a total of two registers, `PPU_PC` for program counter which loops back to the start once it reaches the end of the eighty bytes of executable VRAM and `PPU_GPR` general purpose register. Both registers are 16 bits wide.

The PPU will initially idle executing `NOP`s until VRAM has instructions to instruct the PPU to draw its data.

### List of support instructions
**Note:** all PPU instructions start with `1--- ----` so the first instruction starts at `1000 0000`.
**Note**: PPU instructions can potentially be misinterpreted as CPU instructions so it is on the programmer to ensure they are outside of execution space of the CPU before the programmer loads the instructions into VRAM using `LFM` and `WTM` to load the initial PPU program.
- `1000 0000` - `PPU_NOP` - waste a clock cycle
- `1000 0001` - `PPU_DRW` `line_to_draw (0-134)` - draw a line using the pixel data in VRAM (variable time to execute)
- `1000 0010` - `PPU_LFM` `memory_address_of_two_bytes_to_load_into_ppu_register` - Load value to general purpose register from RAM
- `1000 0011` - `PPU_WTV` `memory_address_of_ram_to_write_to_or_ram` - Write value from general purpose register to RAM
- `1000 0100` - `PPU_BRR` - bitshift value in register to right by 1, storing result in the register
- `1000 0101` - `PPU_BLR` - bitshift value in register to right by 1, storing result in the register
- `1000 0111` - `PPU_ORA` - bitwise OR between a provided value and that in the register, storing result in the register
- `1000 1000` - `PPU_XOR` - bitwise XOR between a provided value and that in the register, storing result in the register
- `1000 1001` - `PPU_AND` - bitwise AND between a provided value and that in the register, storing result in the register
- `1000 1010` - `PPU_DEC` - decrement one to the current value in the register
- `1000 1011` - `PPU_INC` - increment one to the current value in the register

## Memory
The device has a total of 65,536 bytes (16kb) of addressable space.
This memory is broken up into several sections, the first 4kb is reserved for ROM and with the rest for general purpose use