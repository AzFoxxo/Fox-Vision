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

## CPU opcodes (V1.0)
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
### CPU usability additions (V1.1)
- `0000 0000` `0001 0101` - `ILM` - Indirect load from memory - load address stored in active register
- `0000 0000` `0001 0110` - `IWR` - Indirect write register to memory - write value in active register to address stored in inactive register
- ### CPU shorthand additions (V1.2)
- `0000 0000` `0001 0111` - `INC` - Increase the value in the active register by one
- `0000 0000` `0001 1000` - `DEC` - Decrease the value in the active register by one
### Extension debug opcodes (EDO)
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
Fox Vision supports a display size of 100x100 with four bits used to represent each colour (colours are predefined) and retrieves this data from RAM 60 times a second to display it.

The total memory size for an uncompressed frame is as follows:
`4bits * (100 * 100) = 40000 bits (5000 bytes or 5kb approx)`

VRAM starts at address `FFFF` and descents the next 5000bytes.

## List of supported colours
<table>
<tr>
  <th>Colour Name</th>
  <th>Hex (RGB)</th>
  <th>Hex (4bit representation)</th>
</tr>
<tr>
  <td>Black</td>
  <td>#1a1c2c</td>
  <td>0x0</td>
</tr>
<tr>
  <td>Purple</td>
  <td>#5d275d</td>
  <td>0x1</td>
</tr>
<tr>
  <td>Red</td>
  <td>#b13e53</td>
  <td>0x2</td>
</tr>
<tr>
  <td>Orange</td>
  <td>#ef7d57</td>
  <td>0x3</td>
</tr>
<tr>
  <td>Yellow</td>
  <td>#ffcd75</td>
  <td>0x4</td>
</tr>
<tr>
  <td>Light Green</td>
  <td>#a7f070</td>
  <td>0x5</td>
</tr>
<tr>
  <td>Green</td>
  <td>#38b764</td>
  <td>0x6</td>
</tr>
<tr>
  <td>Dark Green</td>
  <td>#257179</td>
  <td>0x7</td>
</tr>
<tr>
  <td>Dark Blue</td>
  <td>#29366f</td>
  <td>0x8</td>
</tr>
<tr>
  <td>Blue</td>
  <td>#3b5dc9</td>
  <td>0x9</td>
</tr>
<tr>
  <td>Light Blue</td>
  <td>#41a6f6</td>
  <td>0xA</td>
</tr>
<tr>
  <td>Turquoise</td>
  <td>#73eff7</td>
  <td>0xB</td>
</tr>
<tr>
  <td>White</td>
  <td>#f4f4f4</td>
  <td>0xC</td>
</tr>
<tr>
  <td>Light Grey</td>
  <td>#94b0c2</td>
  <td>0xD</td>
</tr>
<tr>
  <td>Grey</td>
  <td>#566c86</td>
  <td>0xE</td>
</tr>
<tr>
  <td>Dark Grey</td>
  <td>#333c57</td>
  <td>0xF</td>
</tr>
</table> 


## Memory
The device has a total of 65,536 bytes (16kb) of addressable space.
This memory is broken up into several sections, the first 4kb is reserved for ROM and with the rest for general purpose use