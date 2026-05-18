# FoxC

FoxC is a compiler written in Go for a practical C-like subset that targets Fox16ASM source (`.f16`).

## Implemented language subset

- Types: `u8`, `u16`, `void`, pointers (`u8 *`, `u16 *`)
- Variables: global and local declarations with optional initialization, including fixed-size arrays and pointer variables
- Functions: user-defined functions, parameters, `return`
- Control flow: `if`/`else`, `while`
- Operators: 
  - Arithmetic: `+`, `-`, `*`, `/`
  - Bitwise: `&`, `|`, `^`, `<<`, `>>`, unary `~`
  - Logical: `&&`, `||` (short-circuit evaluation)
  - Comparison: `==`, `!=`, `<`, `>`, `<=`, `>=`
  - Pointer: `&` (address-of), `*` (dereference)
- Pointer operations:
  - Pointer variables: `u16 *ptr`, `u8 *ptr`
  - Address-of: `&variable`, `&array[index]`
  - Dereference: `*ptr` (load), `*ptr = value` (store)
  - Pointer arithmetic: `ptr + offset`, `ptr - offset`
  - **Implementation status**: Fully implemented for `u16` pointers; `u8` pointers supported for reads but have limitations with arithmetic (see notes below)
- Built-ins:
    - `poke(addr, value)` write a word to memory
    - `peek(addr)` read a word from memory
    - `wait(cycles)` block execution for `cycles` using CPU `WAIT`
    - `cyc()` read the CPU cycle counter (`CYC` register)
    - `vblank()` block until the next frame VBlank
    - `in_port(port)` read from a port (V1.10 extended mode, port 0-7)
    - `out_port(port, value)` write to a port (V1.10 extended mode, port 0-7)

## Pointer syntax (spec)

FoxC pointer syntax follows standard C notation.

```c
u16 *wordPtr;
u8 *bytePtr;
u16 **indirect;
```

- Pointer declarations use `*` next to the declarator, as in C.
- Pointer values are word addresses and occupy one 16-bit word.
- `&expr` yields the address of an lvalue.
- `*expr` dereferences a pointer.
- Pointer arithmetic is type-scaled: `u16 *` advances by one 16-bit word, while `u8 *` advances by one packed byte, which is half a word in storage.
- `u8` storage remains packed two elements per word, so a `u8 *` refers to a byte within packed word storage rather than a standalone byte address.

## Pointer semantics (spec)

Pointer lowering in FoxC is word-oriented.

- All pointers are 16-bit word addresses.
- `u16 *` lowers to direct word access.
- `u16 *` addresses must be word-aligned; the low bit is required to be `0` (or is implicitly enforced by lowering).
- `u8 *` lowers to packed byte access within a 16-bit word.
- A `u8 *` pointer still stores a word address, but the low bit selects the byte inside the word.
- `*p`, `p[i]`, and pointer-based assignment follow the same packed `u8` rules when the pointee type is `u8`.
- For `u8` element access, the effective word index is `index >> 1` and the byte offset is `index & 1`; `p + 1` for a `u8 *` advances to the next packed byte.
- Loading a `u8` value reads the containing word and extracts the selected byte with shift/mask operations.
- Storing a `u8` value merges the updated byte back into the containing word.
- FoxC does not introduce byte-addressable pointers; the address value is still stored as a 16-bit word.

## Formal grammar

See [grammar.ebnf](grammar.ebnf).

## Build

```bash
go build ./cmd/foxc
```

## Usage

```bash
./foxc -i input.fc [-o output.f16] [options]
```

### Options

- `-i <file>` - Input FoxC source file (required)
- `-o <file>` - Output path (default: `out.f16` for assembly, `out.bin` for ROM)
- `--mode legacy|extended` - Target machine mode (default: `legacy`)
- `--strict-format` - Enforce ROM size compliance for the selected mode
- `-a, --asm` - Assemble only: output Fox16ASM instead of binary ROM (skips assembler invocation)

## Example

```c
u16 counter = 0;

u16 addOne(u16 x) {
    return x + 1;
}

void main() {
    while (counter < 10) {
        counter = addOne(counter);
    }
    poke(4096, counter);
}
```

Note: FoxC currently accepts decimal integer literals in source. Address constants can be represented as decimal values.

Type note: implicit widening (`u8 -> u16`) is allowed; implicit narrowing (`u16 -> u8`) is rejected to avoid unintended truncation.

Pointer note: the pointer model is word-based, so pointer-sized values are 16-bit words even when the pointed-to type is `u8`.

Implementation note (pointers): 
- `u16 *` pointers are fully implemented and tested: address-of operations, dereference reads/writes, and pointer arithmetic all work correctly.
- `u8 *` pointers have known limitations due to packed storage (two `u8` values per word). Simple reads work; however, pointer arithmetic with `u8 *` is not yet fully supported. Workaround: use `u16` arrays for applications requiring pointer operations, or use direct array indexing for `u8` arrays.

## Fixed arrays

FoxC supports fixed-size arrays for `u8` and `u16` element types.

`u8` arrays are packed into 16-bit words in generated memory layout (two `u8` elements per word).

### Declaration syntax

```c
u16 samples[16];
u8 flags[8];
```

### Supported operations

- Indexing for read/write: `samples[i]`, `samples[i] = value`
- Constant and variable indices are both supported
- Arrays can be declared globally or locally

### Constraints

- Array length must be a compile-time constant
- Array length is fixed (no resize)
- Array assignment/copy is not supported (`a = b` for arrays)
- Passing arrays by value is not supported
- For `u8` arrays, storage is packed into 16-bit words; codegen handles byte selection/masking for indexed access

### Example

```c
u16 table[4];

void main() {
    table[0] = 10;
    table[1] = 20;
    u16 x = table[0] + table[1];
    poke(4096, x);
}
```

## Pointers

FoxC supports pointers with word-based addressing. Both `u16 *` and `u8 *` pointer types are available.

### Basic pointer operations

```c
u16 data = 42;
u16 *ptr = &data;      // Address-of operator
u16 value = *ptr;      // Dereference operator (read)
*ptr = 100;            // Dereference operator (write)
```

### Pointers to array elements

```c
u16 array[3];
u16 *pa = &array[0];   // Pointer to first element
u16 *pb = &array[1];   // Pointer to second element
u16 x = *pa;           // Load value via pointer
*pb = 50;              // Store value via pointer
```

### Pointer arithmetic

```c
u16 array[10];
u16 *ptr = &array[0];
u16 *next = ptr + 1;   // Advances by one u16 (one word)
u16 value = *(ptr + 5); // Access array[5] via pointer
```

### Example: pointer-based data manipulation

```c
void main() {
    u16 x = 10;
    u16 y = 20;
    u16 *px = &x;
    u16 *py = &y;
    
    // Swap via pointers
    u16 temp = *px;
    *px = *py;
    *py = temp;
    
    // x is now 20, y is now 10
    out_port(4, x);
}
```

## Codegen notes

- FoxC emits Fox16ASM MOI instructions (`MOV`, `LOD`, `STR`, `ADD`, `CMP`, etc.).
- Codegen avoids active-register (`SRA`) dependent instruction forms.
 - Generated assembly uses typed register operands (`X`, `Y`, `STATUS`, `SP`, `CYC`) and multi-operand MOI forms.
 - Legacy active-register instructions (`SRA`, `AXY`, `BSL`, etc.) are not emitted by the compiler.
- User functions are emitted as callable labels and return using a return-id dispatch sequence.
- Function locals/params/return metadata are frame-addressed in a software call stack, allowing recursive calls.
