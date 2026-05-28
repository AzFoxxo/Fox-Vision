namespace Fox16ASM;

enum IROperandKind
{
    Immediate,
    Register,
    Label,
    Constant,
}

readonly record struct IROperand(IROperandKind Kind, ushort Value, string? Symbol, int Line, int Column)
{
    public static IROperand Immediate(ushort value, int line, int column)
        => new(IROperandKind.Immediate, value, null, line, column);

    public static IROperand Register(ushort value, int line, int column)
        => new(IROperandKind.Register, value, null, line, column);

    public static IROperand Label(string symbol, int line, int column)
        => new(IROperandKind.Label, 0, symbol, line, column);

    public static IROperand Constant(string symbol, int line, int column)
        => new(IROperandKind.Constant, 0, symbol, line, column);
}

readonly record struct IRInstruction(string Opcode, IReadOnlyList<IROperand> Operands, int Line, int Column, string SourceLine);
readonly record struct IRLabel(string Name, int Line, int Column, string SourceLine);

readonly record struct IRDataWord(IROperand Operand, int Line, int Column, string SourceLine);

enum IRSegmentKind
{
    Code,
    Data,
}

enum IRProgramItemKind
{
    Instruction,
    DataWord,
}

readonly record struct IRProgramItem(IRProgramItemKind Kind, IRSegmentKind SegmentKind, IRInstruction Instruction, IRDataWord DataWord)
{
    public static IRProgramItem InstructionItem(IRInstruction instruction, IRSegmentKind segmentKind)
        => new(IRProgramItemKind.Instruction, segmentKind, instruction, default);

    public static IRProgramItem DataWordItem(IRDataWord dataWord, IRSegmentKind segmentKind)
        => new(IRProgramItemKind.DataWord, segmentKind, default, dataWord);

    public int Line => Kind == IRProgramItemKind.Instruction ? Instruction.Line : DataWord.Line;
    public int Column => Kind == IRProgramItemKind.Instruction ? Instruction.Column : DataWord.Column;
    public string SourceLine => Kind == IRProgramItemKind.Instruction ? Instruction.SourceLine : DataWord.SourceLine;
    public int WordCount => Kind == IRProgramItemKind.Instruction ? 1 + Instruction.Operands.Count : 1;
}

readonly record struct AssemblerIR(
    IReadOnlyList<IRLabel> Labels,
    IReadOnlyList<IRProgramItem> Items,
    IReadOnlyDictionary<string, IROperand> Constants);
