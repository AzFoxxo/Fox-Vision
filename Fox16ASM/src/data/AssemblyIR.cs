namespace Fox16ASM;

enum IROperandKind
{
    Immediate,
    Register,
    Label,
}

readonly record struct IROperand(IROperandKind Kind, ushort Value, string? Symbol, int Line, int Column)
{
    public static IROperand Immediate(ushort value, int line, int column)
        => new(IROperandKind.Immediate, value, null, line, column);

    public static IROperand Register(ushort value, int line, int column)
        => new(IROperandKind.Register, value, null, line, column);

    public static IROperand Label(string symbol, int line, int column)
        => new(IROperandKind.Label, 0, symbol, line, column);
}

readonly record struct IRInstruction(string Opcode, IReadOnlyList<IROperand> Operands, int Line, int Column, string SourceLine);
readonly record struct IRLabel(string Name, int Line, int Column, string SourceLine);

readonly record struct AssemblerIR(IReadOnlyList<IRLabel> Labels, IReadOnlyList<IRInstruction> Instructions);
