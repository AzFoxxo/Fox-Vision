# Specification of the Fox Vision architecture

## Processor
The CPU is a single threaded 8MHz RISC chip. It uses the FoxVision16 architecture.

### Registers
The CPU contains several registers:
- X - 16 bit reg (general purpose register #1)
- Y - 16 bit reg (general purpose register #2)
- PC - 16 bit reg (program counter register, points the current location in RAM the CPU is executing)
- OSR - 8 bit register
  - Equality register (`0x0` - inequality, `0x1` - equality)
  - Active register (`0x0` - `X` register, `0x1` - `Y` register)
  - Illegal division register (`0x0` - `X` division good, `0x1` - `Y` illegal divide by zero operation)
  - Halt register (`0x0` - continue after cycle, `0x1` - halt after cycle)

## CPU opcodes
- `0000 0000` `0000 0000` - `NOP` - Waste clock cycle
- `0000 0000` `0000 0001` - `LFM` - Load 2 byte value from memory in active register
- `0000 0000` `0000 0010` - `WTM` - Write to memory the value of the active register
- `0000 0000` `0000 0011` - `SRA` - Set register active (X - 0, Y - 1)
- `0000 0000` `0000 0100` - `AXY` - Add X and Y and store result in active register
- `0000 0000` `0000 0101` - `SXY` - Subtract X from Y and store result in active register
- `0000 0000` `0000 0110` - `MXY` - Multiply X by Y and store result in active register
- `0000 0000` `0000 0111` - `DXY` - Divide X by Y and store result in active register
- `0000 0000` `0000 1000` - `EQU` - Check if X and Y registers are equal and store result in 
- `0000 0000` `0000 1001` - `LEQ` - Check if X register is less than Y register.
- `0000 0000` `0000 1010` - `JPZ` - Jump if zero to 2 byte wide address
- `0000 0000` `0000 1011` - `JNZ` - Jump if not zero to 2 byte wide address
- `0000 0000` `0000 1100` - `JMP` - Jump to 2 byte wide address.
- `0000 0000` `0000 1101` - `CLR` - Clear all OSR registers (set to zero)
- `0000 0000` `0000 1110` - `HLT` - Halt program execution (quit/power-off)
- `0000 0000` `0000 1111` - `BSL` - Bitshift to left value in active register
- `0000 0000` `0001 0000` - `BSR` - Bitshift to right value in active register
- `0000 0000` `0001 0001` - `AND` - AND bitwise value in active register by value in non-active register
- `0000 0000` `0001 0010` - `ORA` - OR bitwise value in active register by value in non-active register
- `0000 0000` `0001 0011` - `XOR` - XOR bitwise value in active register by value in non-active register
- `0000 0000` `0001 0100` - `DWR` - Direct write (to) register sets the value given (16 bit decimal) to the active register
### Extension debug opcodes
**Note:** These are instructions which are only for us for testing the virtual machine, they allow console I/O, printing memory, etc.
**Note:** All extension debug instruction start with `11` so the first instruction is `1100 0000` `0000 0000`. `11` is not used by the spec so is safe to use for debug commands.
- `1100 0000` `0000 0000` - `DBG_LGC` - Log a character to the console (see [Extension debug character encoding](#extension-debug-character-encoding))
- `1100 0000` `0000 0001` - `DGB_MEM` - Log the memory in hex to the console
- `1100 0000` `0000 0010` - `DGB_INP` - Prompt the user for input which is then converted to an unsigned uint16 (active register used)

## Extension debug character encoding
**Note:** Unknown displays `?` when outputting and unknown reading in, is converted to `40`
- For `#` use value: `0`
- For `A` use value: `1`
- For `B` use value: `2`
- For `C` use value: `3`
- For `D` use value: `4`
- For `E` use value: `5`
- For `F` use value: `6`
- For `G` use value: `7`
- For `H` use value: `8`
- For `I` use value: `9`
- For `J` use value: `10`
- For `K` use value: `11`
- For `L` use value: `12`
- For `M` use value: `13`
- For `N` use value: `14`
- For `O` use value: `15`
- For `P` use value: `16`
- For `Q` use value: `17`
- For `R` use value: `18`
- For `S` use value: `19`
- For `T` use value: `20`
- For `U` use value: `21`
- For `V` use value: `22`
- For `W` use value: `23`
- For `X` use value: `24`
- For `Y` use value: `25`
- For `Z` use value: `26`
- For `-` use value: `27`
- For `0` use value: `28`
- For `1` use value: `29`
- For `2` use value: `30`
- For `3` use value: `31`
- For `4` use value: `32`
- For `5` use value: `33`
- For `6` use value: `34`
- For `7` use value: `35`
- For `8` use value: `36`
- For `9` use value: `37`
- For `\n` (newline) use value: `38`
- For ` ` (space) use value: `39`

### Instruction breakdown
first 16 bits: opcode
second 16 bits: addresses/values (if applicable)

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
**Note:** all PPU instructions start with `1--- ----` so the first instruction starts at `1000 0000` followed by the opcode.
**Note**: PPU instructions can potentially be misinterpreted as CPU instructions so it is on the programmer to ensure they are outside of execution space of the CPU before the programmer loads the instructions into VRAM using `LFM` and `WTM` to load the initial PPU program.
- `1000 0000` `0000 0000` - `PPU_NOP` - waste a clock cycle
- `1000 0000` `0000 0001` - `PPU_DRW` `line_to_draw (0-134)` - draw a line using the pixel data in VRAM (variable time to execute)
- `1000 0000` `0000 0010` - `PPU_LFM` `memory_address_of_two_bytes_to_load_into_ppu_register` - Load value to general purpose register from RAM
- `1000 0000` `0000 0011` - `PPU_WTV` `memory_address_of_ram_to_write_to_or_ram` - Write value from general purpose register to RAM
- `1000 0000` `0000 0100` - `PPU_BRR` - bitshift value in register to right by 1, storing result in the register
- `1000 0000` `0000 0101` - `PPU_BLR` - bitshift value in register to right by 1, storing result in the register
- `1000 0000` `0000 0110` - `PPU_ORA` - bitwise OR between a provided value and that in the register, storing result in the register
- `1000 0000` `0000 0111` - `PPU_XOR` - bitwise XOR between a provided value and that in the register, storing result in the register
- `1000 0000` `0000 1000` - `PPU_AND` - bitwise AND between a provided value and that in the register, storing result in the register
- `1000 0000` `0000 1001` - `PPU_DEC` - decrement one to the current value in the register
- `1000 0000` `0000 1010` - `PPU_INC` - increment one to the current value in the register

## Memory
The device has a total of 65,536 bytes (16kb) of addressable space.
This memory is broken up into several sections, the first 4kb is reserved for ROM and with the rest for general purpose use