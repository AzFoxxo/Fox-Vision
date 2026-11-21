# FoxC

FoxC is a compiler written in Go for a practical C-like subset that targets Fox16ASM source (`.f16`).

## Implemented language subset

- Types: `u8`, `u16`, `u32`, `void`, pointers (`u8 *`, `u16 *`, `u32 *`)
- Variables: global and local declarations with optional initialization, including fixed-size arrays and pointer variables
- Literals: decimal numbers, character literals (`'A'`), and string literals for NUL-terminated `u8` array initialization
- Functions: user-defined functions, parameters, `return`, and function prototypes (signatures)
- Headers: `#include "file.fh"` / `#include <file.fh>` with `-I` search paths; headers may contain signatures or complete definitions
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
    - `get_memory_size()` read the RAM size in KiB (`MS` register; ISA V2.1; `em2_am1` mode only)
    - `vblank()` block until the next frame VBlank
    - `in_port(port)` read from a port (extended mode; port 0–65535 in `em2_am1` mode, 0–7 in `extended`)
    - `out_port(port, value)` write to a port (extended mode; same port range)
    - `in_port32(port)` / `out_port32(port, value)` 32-bit port transfers (`em2_am1` mode only)
    - `in_port_v(port)` / `out_port_v(port, value)` variable-port forms; the port operand is patched at run time (extended mode)
    - `in_port32_v(port)` / `out_port32_v(port, value)` variable-port 32-bit forms (`em2_am1` mode only)
    - `am()` read the `AM` register (addressing-mode; `em2_am1` mode only)
    - `set_am(value)` write the `AM` register (`em2_am1` mode only)
    - `em()` read the `EM` register (extended-memory mode; `em2_am1` mode only)
    - `set_em(value)` write the `EM` register (`em2_am1` mode only)
    - `os_start(address)` start a FoxOS foreground program at a constant address
    - `os_return()` emit the `OSRET` instruction to return to FoxOS

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
- `--mode legacy|extended|em2_am1` - Target machine mode (default: `legacy`).
  - `legacy` — 16-bit address space, no extended registers
  - `extended` — 16-bit address space, `R0`–`R7` registers, ports 0–7
  - `em2_am1` — ISA V2.0: 32-bit address space, 32-bit registers, ports 0–65535; enables the `u32` type and `am()`/`em()` builtins (aliases: `extended2`, `v2`, `em2`). The compiler starts with `EM=2` and word addressing (`AM=0`) because FoxC's labels, globals, stack slots and pointers are word-based; call `set_am(1)` at run time to switch to byte addressing.
- `--load-address <addr>` - Load the ROM at a non-zero address, such as
  `0x4000` for a FoxOS foreground program
- `-I <dir>`, `--include-dir <dir>` - Add a header search directory (repeatable)
- `--strict-format` - Enforce ROM size compliance for the selected mode
- `-a, --asm` - Assemble only: output Fox16ASM instead of binary ROM (skips assembler invocation)

## Headers

FoxC supports C-style include directives. They are expanded before lexing, so a
header may contain either **function signatures** or **whole code** (globals and
complete function definitions).

```c
#include "local_header.fh"   // searched relative to the including file first
#include <shared_header.fh>  // searched in -I directories first
```

Headers are included at most once per translation unit, so a header containing
definitions can safely be included from multiple files. `#pragma once` is
accepted for compatibility. Recursive includes are reported as errors.

### Signature headers

A declaration ending in `;` is a prototype. Parameter names are optional in
prototypes; a definition must still name every parameter. `f(void)` is accepted
as an explicit empty parameter list.

```c
// math.fh
u16 add(u16, u16);
u16 scale(u16 value, u16 factor);
```

```c
// main.fc
#include "math.fh"

void main() {
    u16 x = add(2, 3);
}

u16 add(u16 a, u16 b) { return a + b; }
u16 scale(u16 value, u16 factor) { return value * factor; }
```

FoxC validates that each definition matches its prototype, reports conflicting
prototypes, and still requires exactly one `void main()` definition.

### Whole-code headers

A header can also define functions or globals directly:

```c
// util.fh
#pragma once

u16 twice(u16 value) {
    return value * 2;
}
```

Including that header pastes the complete definition into the translation unit.
Use `-I DIR` (or `--include-dir DIR`) to add search directories for angle-bracket
includes.

A runnable example of both header styles is in
[`examples/headers_demo/`](examples/headers_demo/).

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

## FoxOS example

`examples/foxos.fc` is a complete small operating system example. It enables
extended mode, exposes a CLI through the VF16TTY port, and stores four fixed
text files in port-mapped VF16Flash storage. See [docs/foxos.md](../docs/foxos.md)
for the flash layout, port protocol, build command, and supported shell
commands.

Type note: implicit widening (`u8 -> u16 -> u32`) is allowed; narrowing (`u32 -> u16`, `u16 -> u8`) is rejected to avoid unintended truncation.

## u32 (32-bit unsigned integers)

`u32` is available in `--mode em2_am1` (and `extended2` / `v2`). It requires the Fox Vision V2.0 ISA with 32-bit registers.

> [!IMPORTANT]
> `u32` is **not** available in `legacy` or `extended` modes. The compiler will
> reject any `u32` variable, parameter, or return type outside `em2_am1` mode.

### Storage

- A `u32` local or global occupies **two consecutive 16-bit words** in the software stack/data area. The low word is stored first (little-endian word order), followed by the high word.
- In register-allocated paths the compiler uses full 32-bit registers (`R0`–`R7`, `X`, `Y`) and their `H`/`L` sub-registers to load wide immediates.
- Arrays of `u32` elements allocate 2 words per element, and indexing scales by two.

### Arithmetic

All the same operators that work on `u16` work on `u32`:

```c
u32 a = 100000;
u32 b = 200000;
u32 c = a + b;    // 300000
u32 d = c - a;    // 200000
u32 e = a * 3;
u32 f = c / 7;
u32 g = a & 0xFFFF;
u32 h = a | 0x10000;
u32 i = a ^ 0xAAAA;
u32 j = a << 1;
u32 k = a >> 1;
```

### Widening and narrowing

Widening is implicit:

```c
u16 w = 1000;
u32 big = w;      // OK: u16 widens to u32
u8  b2  = 42;
u32 b32 = b2;     // OK: u8 widens to u32
```

Narrowing is **rejected**:

```c
u32 big = 100000;
u16 small = big;  // compile error: cannot assign u32 to u16
```

### Pointers (`u32 *`)

`u32 *` is a pointer to a 32-bit value. Because each `u32` occupies two words, pointer arithmetic on `u32 *` advances the word address by **2** per step.

### am() / em() builtins (em2_am1 mode)

```c
u16 cur_em = em();     // read EM register (should be 2 in em2_am1 mode)
u16 cur_am = am();     // read AM register (0 = word addressing, the compiler default)
set_em(2);             // write EM register
set_am(1);             // switch to byte addressing
set_am(0);             // return to word addressing
```

### get_memory_size() builtin (ISA V2.1, em2_am1 mode)

```c
u32 ram_kib = get_memory_size();  // MS register: installed RAM in 1 KiB units
```

The value is the configured FoxVision RAM size divided by 1024 — for example,
`0x00000080` for 128 KiB or `0x00400000` for 4 GiB. `MS` is read-only in both
assembly and FoxC.



Pointer note: the pointer model is word-based, so pointer-sized values are 16-bit words even when the pointed-to type is `u8`.

Implementation note (pointers): 
- `u16 *` pointers are fully implemented and tested: address-of operations, dereference reads/writes, and pointer arithmetic all work correctly.
- `u8 *` pointers have known limitations due to packed storage (two `u8` values per word). Simple reads work; however, pointer arithmetic with `u8 *` is not yet fully supported. Workaround: use `u16` arrays for applications requiring pointer operations, or use direct array indexing for `u8` arrays.

## Fixed arrays

FoxC supports fixed-size arrays for `u8` and `u16` element types.

`u8` arrays are packed into 16-bit words in generated memory layout (two `u8` elements per word).

### Character and string literals

Character literals represent one byte and have type `u8`:

```c
u8 newline = '\n';
u8 letter = 'A';
```

Character and string literals accept the escaped forms understood by FoxC,
including `\n`, `\r`, `\t`, `\\`, `\'`, `\"`, octal escapes, and hexadecimal
or Unicode escapes. A character literal must decode to exactly one byte.

String literals are NUL-terminated and can initialize `u8` arrays or be passed
to functions expecting a `u8 *`. The array length can be inferred or specified
explicitly:

```c
u8 greeting[] = "Hello";
u8 prompt[8] = "Ready>";
```

The explicit array must have room for the terminating NUL. Strings use the
same packed `u8` storage and indexing rules as other `u8` arrays. String
literals used as pointer expressions are emitted as read-only word data, with
one byte per word, so `u8 *` pointer arithmetic can advance through them.

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
- In `--mode extended`, codegen uses the V1.11 `R0`-`R7` registers to keep values out of memory:
  - `R0` and `R1` are reserved as expression temporaries, so ordinary binary,
    comparison, array, and built-in operations do not bounce values through the
    software stack.
  - Up to six scalar locals/parameters per function are cached in `R7`-`R2`.
    Parameters are loaded once at function entry; subsequent reads and writes use
    the register directly.
  - Variables whose address is taken (`&x`) are excluded from register caching so
    pointer writes still target their memory slot. Arrays and globals stay
    memory-backed.
  - Live cached/scalar temporaries are spilled to the hardware stack around real
    function calls, because callees may use any `R` register.
  - Extended mode also uses the V1.11 two-operand `CMP SRC DST` form.
- In `--mode em2_am1` (V2.0), codegen extends the `extended` strategy:
  - Startup emits `MOV %2 EM` / `MOV %0 AM` to enable the 32-bit address space while keeping word addressing, which matches FoxC's word-based label, global and pointer model. Programs can call `set_am(1)` when they need byte accesses.
  - `u32` variables use full 32-bit registers (`R0`–`R7`) and their H/L sub-registers for wide immediate loads.
  - `u32` values, globals, frame slots and array elements are stored as two words (low word first); `u32 *` pointer arithmetic scales by two.
  - Existing `u8`/`u16` arithmetic is masked back to 16 bits after ALU operations, while `u32` arithmetic keeps all 32 bits.
  - Port operands accept the full 16-bit range (0–65535) instead of 0–7.
  - `am()`, `set_am()`, `em()`, `set_em()` builtins are available.
  - The 32-bit memory layout leaves `0x0000`–`0x97FF` for code and constant
    data, starts the software stack at `0x9800`, and places globals at
    `0xA000` (VRAM begins at `0xEC78`). This gives large OS images about
    39K words of code space.
- In `--mode legacy` the compiler keeps the original `X`/`Y` plus stack
  evaluation strategy and does not emit `R` registers.
- User functions are emitted as callable labels and return using a return-id dispatch sequence.
- Function locals/params/return metadata are frame-addressed in a software call stack, allowing recursive calls.
