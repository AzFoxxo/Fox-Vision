# Fox 16 Assembly Language

Fox 16ASM is a simple assembly language which compiles to native ROM files used by Viso Fox. The assembly language always implements the most current spec the machine specification details.

## Basic structure

Each program must start with a `:main` definition, this must be the first piece of code within the program as address zero is used as the start of execution.

The language supports a few features:

- Comments
- Instructions
- Labels

## Comments

All comments begin with `;` this may be on its own line or come after the final part of an existing instruction e.g.
`; my comment`
and
`NOP ; my comment`
are both valid comments


## Instructions

; Instructions (opcodes) are always spelt in capitals, basic instructions use three character codes such as `NOP` and `AXY`. These instructions can take a singular operand or no operand `NOR`.

To denote the type of data, the following are used:
Aa-Zz + _ to use a label (this is resoled to the label's address within rom) e.g. `main`
% - This denotes a decimal value e.g. `%1`
$ - This denotes a hexadecimal value and is used for address representation e.g. `$FFFF`

## Location specifiers

With the introduction of the `MOV` macro-instruction, there is a need to denote what register or if a memory address is being used for the source and destination.

Example:

```assembly
MOV #A #B ; Move the value in A to B
```

### Register specifiers

- \#A - Denotes the A register
- \#B - Denotes the B register

### Literals

- !% - Denotes a decimal literal e.g. `!%1`
- !$ - Denotes a hexadecimal literal e.g. `!$FFFF`

### Constants (EFox16 only)

- \#<const_name> - denotes a constant being used to reference a pointer
- !<const_name> - denotes a constant being used as a literal
- $<const_name> - denotes a constant being used as a hexadecimal literal

## Labels

Labels are defined by writing `:` followed by name using Aa-Zz + _ e.g.

```assembly
:main
; Code here
```

These labels can then be jumped to using jump instructions e.g. `JMP main` or an equivalent conditional jump.

## EFox16 - Extended language

EFox16 offers a few extensions to the base Fox16 language.

To use EFox16,.efox16 file extension is required and to use the functionality of the extended language, the `EFox16` directive must be used at the start of the file.

### Directives

The extended language supports directives for code generation.

#### List of directives

- `@efox16_required` - Tells the assembler that the EFox16 language features are required.
- `const <name> <value>` - Defines a constant.
