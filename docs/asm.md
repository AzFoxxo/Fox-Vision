# Fox 16ASM Language
Fox 16ASM is a simple assembly language which compiles to native ROM files used by Viso Fox. The assembly language always implements the most current spec the machine specification details.

## Basic structure
Each program must start with a `:main` definition, this must be the first piece of code within the program as address zero is used as the start of execution.

The language supports a few features:
- Comments
- Instructions
- Labels

### Comments
All comments begin with `;` this may be on its own line or come after the final part of an existing instruction e.g.
`; my comment`
and
`NOP ; my comment`
are both valid comments


### Instructions
; Instructions (opcodes) are always spelt in capitals, basic instructions use three character codes such as `NOP` and `AXY`. These instructions can take a singular operand or no operand `NOR`.

To denote the type of data, the following are used:
Aa-Zz + _ to use a label (this is resoled to the label's address within rom) e.g. `main`
% - This denotes a decimal value e.g. `%1`
$ - This denotes a hexadecimal value and is used for address representation e.g. `$FFFF`

# Labels
Labels are defined by writing `:` followed by name using Aa-Zz + _ e.g.
```assembly
:main
; Code here
```

These labels can then be jumped to using jump instructions e.g. `JMP main` or an equivalent conditional jump.