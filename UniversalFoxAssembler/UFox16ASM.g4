grammar UFox16ASM;

assembly      : (line | label)* EOF ;
line          : (instruction | directive) ;
label         : ID ':' ;
instruction   : jump | cmp | mov | arithmetic | bitshift | logic | basic ;
directive     : '#define' ID (HEX | DEC_NUM | BIN) ;
comment       : COMMENT ;

jump          : (JMP | JZ | JNZ | JL | JLE | JG | JGE) ID ;
cmp           : 'CMP' operand ',' operand ;
mov           : 'MOV' operand ',' operand ;
arithmetic    : (ADD | SUB | MUL | DIV) operand ',' (operand | constant)
              | (INC | DEC) operand ;
bitshift      : (SHL | SHR) operand ',' (operand | constant) ;
logic         : (AND | OR | XOR) operand ',' (operand | constant)
              | 'NOT' operand ;
basic         : NOP | HLT ;

operand       : ID | constant | '[' ID ']' ;
constant      : HEX | DEC_NUM | BIN ;

JMP           : 'JMP' ;
JZ            : 'JZ' ;
JNZ           : 'JNZ' ;
JL            : 'JL' ;
JLE           : 'JLE' ;
JG            : 'JG' ;
JGE           : 'JGE' ;
ADD           : 'ADD' ;
SUB           : 'SUB' ;
MUL           : 'MUL' ;
DIV           : 'DIV' ;
INC           : 'INC' ;
DEC           : 'DEC' ;
SHL           : 'SHL' ;
SHR           : 'SHR' ;
AND           : 'AND' ;
OR            : 'OR' ;
XOR           : 'XOR' ;
NOT           : 'NOT' ;
NOP           : 'NOP' ;
HLT           : 'HLT' ;

ID            : [a-zA-Z_][a-zA-Z_0-9]* ;
HEX           : '0x' [0-9a-fA-F]+ ;
DEC_NUM       : [0-9]+ ;
BIN           : '0b' [01]+ ;
COMMENT       : ';' ~[\r\n]* -> skip ;

WHITESPACE    : [ \t\r\n]+ -> skip ;
