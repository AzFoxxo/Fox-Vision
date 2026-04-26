# FoxC

FoxC is a compiler written in Go for a practical C-like subset that targets Fox16ASM source (`.f16`).

## Implemented language subset

- Types: `u8`, `u16`, `void`
- Variables: global and local declarations with optional initialization
- Functions: user-defined functions, parameters, `return`
- Control flow: `if`/`else`, `while`
- Expressions: `+`, `-`, `*`, `/`, `&`, logical short-circuit (`&&`, `||`), comparisons (`==`, `!=`, `<`, `>`, `<=`, `>=`)
- Built-ins:
  - `poke(addr, value)` write a word to memory
  - `peak(addr)` read a word from memory
  - `peek(addr)` alias for `peak`

## Formal grammar

See [grammar.ebnf](grammar.ebnf).

## Build

```bash
go build ./cmd/foxc
```

## Usage

```bash
./foxc -i input.fc -o output.f16
```

If `-o` is omitted, output defaults to `out.f16`.

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

## Codegen notes

- FoxC emits Fox16ASM MOI instructions (`MOV`, `LOD`, `STR`, `ADD`, `CMP`, etc.).
- Codegen avoids active-register (`SRA`) dependent instruction forms.
- User functions are emitted as callable labels and return using a return-id dispatch sequence.
- Function locals/params/return metadata are frame-addressed in a software call stack, allowing recursive calls.
