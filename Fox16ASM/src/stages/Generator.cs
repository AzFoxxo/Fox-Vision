using System;
using System.IO;
using MiscUtil.IO;
using MiscUtil.Conversion;
using Fox16Shared;

namespace Fox16ASM;

class Generator
{
    private const byte OperandTypeRegister = 0b00;
    private const byte OperandTypeImmediate = 0b01;
    private const byte OperandTypeDirectMemory = 0b10;
    private const byte OperandTypeIndirectMemory = 0b11;
    private const int LegacyStrictFormatMaxPayloadWords = 4 * 1024;
    private const int ExtendedStrictFormatMaxPayloadWords = 32 * 1024;

    private readonly record struct ResolvedOperand(ushort Value, IROperandKind Kind);

    public static CompilationResult<byte[]> Generate(
        AssemblerIR ir,
        string outputFile,
        string sourceFile,
        AssemblerMode mode,
        DebugFlags debugFlags,
        bool strictFormat = false)
    {
        var diagnostics = new List<Diagnostic>();
        var labelTable = ResolveLabelAddresses(ir, sourceFile, debugFlags, diagnostics);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return CompilationResult<byte[]>.Failed(diagnostics);

        var payloadWordCount = GetPayloadWordCount(ir);
        if (strictFormat)
        {
            var maximumPayloadWords = GetStrictFormatMaxPayloadWords(mode);
            if (payloadWordCount > maximumPayloadWords)
            {
                diagnostics.Add(Diagnostic.Error(
                    $"ROM payload size is {payloadWordCount} words, which exceeds strict format limit of {maximumPayloadWords} words for {mode.ToString().ToLowerInvariant()} mode.",
                    sourceFile));
                return CompilationResult<byte[]>.Failed(diagnostics);
            }
        }

        using var memory = new MemoryStream();
        using var writer = new EndianBinaryWriter(EndianBitConverter.Big, memory);

        WriteHeader(memory, mode, payloadWordCount);

        foreach (var instruction in ir.Instructions)
        {
            if (!Opcodes.instructions.TryGetValue(instruction.Opcode, out var opcodeValue))
            {
                diagnostics.Add(Diagnostic.Error(
                    $"Unknown opcode '{instruction.Opcode}'.",
                    sourceFile,
                    instruction.Line,
                    instruction.Column,
                    instruction.SourceLine));
                continue;
            }

            var resolvedOperands = new List<ResolvedOperand>(instruction.Operands.Count);
            foreach (var operand in instruction.Operands)
            {
                if (!TryResolveOperand(operand, labelTable, ir.Constants, sourceFile, instruction, diagnostics, out var resolvedOperand))
                    continue;

                resolvedOperands.Add(resolvedOperand);
            }

            if (!ValidateInstructionOperands(instruction, resolvedOperands, sourceFile, diagnostics))
                continue;

            var encodedOpcode = EncodeOpcode(instruction, opcodeValue, resolvedOperands);
            writer.Write(encodedOpcode);
            if (debugFlags.ShowTokens)
                Console.WriteLine($"OPCODE: {instruction.Opcode} => {encodedOpcode:X4}");

            foreach (var operand in resolvedOperands)
            {
                writer.Write(operand.Value);
                if (debugFlags.ShowTokens)
                    Console.WriteLine($"OPERAND: {operand.Value:X4}");
            }
        }

        WriteFooter(writer);

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return CompilationResult<byte[]>.Failed(diagnostics);

        var bytes = memory.ToArray();
        File.WriteAllBytes(outputFile, bytes);

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("Code generation complete!");
        Console.WriteLine($"ROM file written to {outputFile}");

        return new CompilationResult<byte[]>(bytes, diagnostics);
    }

    private static int GetPayloadWordCount(AssemblerIR ir)
        => ir.Instructions.Sum(instruction => 1 + instruction.Operands.Count) + 2;

    private static int GetStrictFormatMaxPayloadWords(AssemblerMode mode)
        => mode == AssemblerMode.Extended ? ExtendedStrictFormatMaxPayloadWords : LegacyStrictFormatMaxPayloadWords;

    private static bool ValidateInstructionOperands(
        IRInstruction instruction,
        IReadOnlyList<ResolvedOperand> resolvedOperands,
        string sourceFile,
        List<Diagnostic> diagnostics)
    {
        return instruction.Opcode switch
        {
            "IN" => ValidatePortTransfer(instruction, resolvedOperands, sourceFile, diagnostics, portOperandIndex: 0, registerOperandIndex: 1),
            "OUT" => ValidatePortTransfer(instruction, resolvedOperands, sourceFile, diagnostics, portOperandIndex: 1, registerOperandIndex: 0),
            _ => true,
        };
    }

    private static bool ValidatePortTransfer(
        IRInstruction instruction,
        IReadOnlyList<ResolvedOperand> resolvedOperands,
        string sourceFile,
        List<Diagnostic> diagnostics,
        int portOperandIndex,
        int registerOperandIndex)
    {
        if (resolvedOperands.Count != 2)
        {
            diagnostics.Add(Diagnostic.Error(
                $"Opcode '{instruction.Opcode}' expects exactly two operands.",
                sourceFile,
                instruction.Line,
                instruction.Column,
                instruction.SourceLine));
            return false;
        }

        if (resolvedOperands[registerOperandIndex].Kind != IROperandKind.Register)
        {
            diagnostics.Add(Diagnostic.Error(
                $"Opcode '{instruction.Opcode}' requires a register operand in position {registerOperandIndex + 1}.",
                sourceFile,
                instruction.Line,
                instruction.Column,
                instruction.SourceLine));
            return false;
        }

        var portOperand = resolvedOperands[portOperandIndex];
        if (portOperand.Kind == IROperandKind.Register)
        {
            diagnostics.Add(Diagnostic.Error(
                $"Opcode '{instruction.Opcode}' requires a port immediate in position {portOperandIndex + 1}.",
                sourceFile,
                instruction.Line,
                instruction.Column,
                instruction.SourceLine));
            return false;
        }

        if (portOperand.Value > 0x0007)
        {
            diagnostics.Add(Diagnostic.Error(
                $"Port operand for opcode '{instruction.Opcode}' must be in the range 0x0000 to 0x0007.",
                sourceFile,
                instruction.Line,
                instruction.Column,
                instruction.SourceLine));
            return false;
        }

        return true;
    }

    private static Dictionary<string, ushort> ResolveLabelAddresses(
        AssemblerIR ir,
        string sourceFile,
        DebugFlags debugFlags,
        List<Diagnostic> diagnostics)
    {
        var labelsByName = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        var instructionAddresses = new int[ir.Instructions.Count];
        var eofAddress = 0;

        for (var i = 0; i < ir.Instructions.Count; i++)
        {
            instructionAddresses[i] = eofAddress;
            eofAddress += 1 + ir.Instructions[i].Operands.Count;
            if (eofAddress > ushort.MaxValue)
            {
                diagnostics.Add(Diagnostic.Error(
                    "Program exceeds addressable 16-bit ROM space.",
                    sourceFile,
                    ir.Instructions[i].Line,
                    ir.Instructions[i].Column,
                    ir.Instructions[i].SourceLine));
                break;
            }
        }

        foreach (var label in ir.Labels.OrderBy(l => l.Line))
        {
            var targetAddress = eofAddress;
            for (var i = 0; i < ir.Instructions.Count; i++)
            {
                if (ir.Instructions[i].Line < label.Line)
                    continue;

                targetAddress = instructionAddresses[i];
                break;
            }

            if (!labelsByName.TryAdd(label.Name, (ushort)targetAddress))
            {
                diagnostics.Add(Diagnostic.Error(
                    $"Duplicate label declaration '{label.Name}'.",
                    sourceFile,
                    label.Line,
                    label.Column,
                    label.SourceLine));
                continue;
            }

            if (debugFlags.ShowLabels)
                Console.WriteLine($"LABEL DECL: {label.Name} => {targetAddress:X4}");
        }

        return labelsByName;
    }

    private static bool TryResolveOperand(
        IROperand operand,
        Dictionary<string, ushort> labelTable,
        IReadOnlyDictionary<string, IROperand> constants,
        string sourceFile,
        IRInstruction instruction,
        List<Diagnostic> diagnostics,
        out ResolvedOperand resolved,
        HashSet<string>? constantResolutionStack = null)
    {
        switch (operand.Kind)
        {
            case IROperandKind.Immediate:
                resolved = new ResolvedOperand(operand.Value, IROperandKind.Immediate);
                return true;
            case IROperandKind.Register:
                resolved = new ResolvedOperand(operand.Value, IROperandKind.Register);
                return true;
            case IROperandKind.Label:
                if (operand.Symbol is not null && labelTable.TryGetValue(operand.Symbol, out var labelAddress))
                {
                    resolved = new ResolvedOperand(labelAddress, IROperandKind.Label);
                    return true;
                }

                diagnostics.Add(Diagnostic.Error(
                    $"Unresolved label '{operand.Symbol}'.",
                    sourceFile,
                    operand.Line,
                    operand.Column,
                    instruction.SourceLine));
                resolved = default;
                return false;
            case IROperandKind.Constant:
                if (string.IsNullOrWhiteSpace(operand.Symbol))
                {
                    diagnostics.Add(Diagnostic.Error(
                        "Invalid constant reference.",
                        sourceFile,
                        operand.Line,
                        operand.Column,
                        instruction.SourceLine));
                    resolved = default;
                    return false;
                }

                if (!constants.TryGetValue(operand.Symbol, out var constantValue))
                {
                    diagnostics.Add(Diagnostic.Error(
                        $"Unresolved constant '{operand.Symbol}'.",
                        sourceFile,
                        operand.Line,
                        operand.Column,
                        instruction.SourceLine));
                    resolved = default;
                    return false;
                }

                constantResolutionStack ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!constantResolutionStack.Add(operand.Symbol))
                {
                    diagnostics.Add(Diagnostic.Error(
                        $"Cyclic constant reference detected for '{operand.Symbol}'.",
                        sourceFile,
                        operand.Line,
                        operand.Column,
                        instruction.SourceLine));
                    resolved = default;
                    return false;
                }

                var resolvedConstant = TryResolveOperand(
                    constantValue,
                    labelTable,
                    constants,
                    sourceFile,
                    instruction,
                    diagnostics,
                    out resolved,
                    constantResolutionStack);

                constantResolutionStack.Remove(operand.Symbol);
                return resolvedConstant;
            default:
                diagnostics.Add(Diagnostic.Error(
                    "Unsupported operand kind.",
                    sourceFile,
                    operand.Line,
                    operand.Column,
                    instruction.SourceLine));
                resolved = default;
                return false;
        }
    }

    private static ushort EncodeOpcode(IRInstruction instruction, ushort opcodeValue, IReadOnlyList<ResolvedOperand> resolvedOperands)
    {
        if (opcodeValue > 0x00FF)
            return opcodeValue;

        byte operandCount = (byte)Math.Min(resolvedOperands.Count, 0b11);
        byte operandOneType = 0;
        byte operandTwoType = 0;

        if (resolvedOperands.Count > 0)
            operandOneType = ClassifyOperandType(instruction.Opcode, resolvedOperands[0], 0);
        if (resolvedOperands.Count > 1)
            operandTwoType = ClassifyOperandType(instruction.Opcode, resolvedOperands[1], 1);

        var controlLowByte = (byte)((operandCount & 0b11) | ((operandOneType & 0b11) << 2) | ((operandTwoType & 0b11) << 4));
        return (ushort)((opcodeValue << 8) | controlLowByte);
    }

    private static byte ClassifyOperandType(string opcode, ResolvedOperand operand, int operandIndex)
    {
        var isMemoryAddressOperand = IsMemoryAddressOperand(opcode, operandIndex);

        if (operand.Kind == IROperandKind.Register)
            return isMemoryAddressOperand ? OperandTypeIndirectMemory : OperandTypeRegister;

        return isMemoryAddressOperand ? OperandTypeDirectMemory : OperandTypeImmediate;
    }

    private static bool IsMemoryAddressOperand(string opcode, int operandIndex)
    {
        return opcode switch
        {
            "LFM" or "WTM" => operandIndex == 0,
            "STR" or "LOD" => operandIndex == 1,
            _ => false,
        };
    }

    private static void WriteHeader(Stream stream, AssemblerMode mode, int payloadWordCount)
    {
        if (mode == AssemblerMode.Extended)
        {
            var magicBytes = System.Text.Encoding.ASCII.GetBytes(".VFOX16EXT");
            stream.Write(magicBytes, 0, magicBytes.Length);
            stream.WriteByte(1);
            WriteBigEndianUInt16(stream, 1);
            WriteBigEndianUInt16(stream, 0);
            WriteBigEndianUInt16(stream, (ushort)payloadWordCount);
            return;
        }

        var legacyMagicBytes = System.Text.Encoding.ASCII.GetBytes(".VISOFOX16");
        stream.Write(legacyMagicBytes, 0, legacyMagicBytes.Length);
    }

    private static void WriteBigEndianUInt16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteFooter(EndianBinaryWriter writer)
    {
        writer.Write(Convert.ToUInt16(Opcodes.instructions["NOP"]));
        writer.Write(Convert.ToUInt16(Opcodes.instructions["HLT"]));
    }
}
