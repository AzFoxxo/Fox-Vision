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

    private readonly record struct ResolvedOperand(uint Value, IROperandKind Kind);

    public class GenerationOutput
    {
        public byte[] Bytes = Array.Empty<byte>();
        public Dictionary<string, uint> Labels = new();
    }

    public static CompilationResult<GenerationOutput> Generate(
        AssemblerIR ir,
        string outputFile,
        string sourceFile,
        AssemblerMode mode,
        DebugFlags debugFlags,
        bool strictFormat = false,
        uint loadAddress = 0,
        byte? romVersion = null,
        ushort? mapper = null)
    {
        byte effectiveVersion = romVersion ?? (mode == AssemblerMode.Extended2 || loadAddress > ushort.MaxValue ? (byte)3 : (byte)2);
        ushort effectiveMapper = mapper ?? (effectiveVersion == 3 || mode == AssemblerMode.Extended2 ? (ushort)2 : (ushort)1);
        if (effectiveMapper == 2 && !romVersion.HasValue)
        {
            effectiveVersion = 3;
        }

        var diagnostics = new List<Diagnostic>();
        var is32BitLimit = effectiveMapper == 2 || effectiveVersion == 3 || mode == AssemblerMode.Extended2;
        var wideJumps = DetermineWideJumps(ir, loadAddress, is32BitLimit);
        var labelTable = ResolveLabelAddresses(ir, sourceFile, debugFlags, diagnostics, loadAddress, is32BitLimit, wideJumps);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return CompilationResult<GenerationOutput>.Failed(diagnostics);

        var payloadWordCount = GetPayloadWordCount(ir, wideJumps);
        if (strictFormat)
        {
            var maximumPayloadWords = GetStrictFormatMaxPayloadWords(mode, effectiveMapper);
            if (payloadWordCount > maximumPayloadWords)
            {
                diagnostics.Add(Diagnostic.Error(
                    $"ROM payload size is {payloadWordCount} words, which exceeds strict format limit of {maximumPayloadWords} words for {mode.ToString().ToLowerInvariant()} mode.",
                    sourceFile));
                return CompilationResult<GenerationOutput>.Failed(diagnostics);
            }
        }

        using var memory = new MemoryStream();
        using var writer = new EndianBinaryWriter(EndianBitConverter.Big, memory);

        var resetVector = ResolveResetVectorAddress(ir, loadAddress, wideJumps);
        WriteHeader(memory, mode, payloadWordCount, loadAddress, resetVector, effectiveVersion, effectiveMapper);

        for (var itemIndex = 0; itemIndex < ir.Items.Count; itemIndex++)
        {
            var item = ir.Items[itemIndex];
            if (item.Kind == IRProgramItemKind.Instruction)
            {
                var instruction = item.Instruction;
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

                if (!ValidateInstructionOperands(instruction, resolvedOperands, mode, sourceFile, diagnostics))
                    continue;

                var wideJump = wideJumps.Contains(itemIndex);

                // A single operand word cannot hold an address above 0xFFFF.
                // Jumps have the wide form; everything else must use @lo/@hi
                // or an indirect register instead of silently truncating.
                var operandTooLarge = false;
                if (!wideJump)
                {
                    foreach (var resolvedOperand in resolvedOperands)
                    {
                        if (resolvedOperand.Kind != IROperandKind.Register &&
                            resolvedOperand.Value > ushort.MaxValue)
                        {
                            diagnostics.Add(Diagnostic.Error(
                                $"Operand value 0x{resolvedOperand.Value:X} does not fit in 16 bits; " +
                                "use @lo(label)/@hi(label) for 32-bit addresses.",
                                sourceFile,
                                instruction.Line,
                                instruction.Column,
                                instruction.SourceLine));
                            operandTooLarge = true;
                        }
                    }
                }
                if (operandTooLarge)
                    continue;

                var encodedOpcode = EncodeOpcode(instruction, opcodeValue, resolvedOperands, wideJump);
                writer.Write(encodedOpcode);
                if (debugFlags.ShowTokens)
                    Console.WriteLine($"OPCODE: {instruction.Opcode} => {encodedOpcode:X4}");

                foreach (var operand in resolvedOperands)
                {
                    writer.Write((ushort)(operand.Value & 0xFFFF));
                    if (wideJump)
                        writer.Write((ushort)((operand.Value >> 16) & 0xFFFF));
                    if (debugFlags.ShowTokens)
                        Console.WriteLine($"OPERAND: {operand.Value:X8}");
                }
            }
            else
            {
                var dataWord = item.DataWord;
                var contextInstruction = new IRInstruction("@word", Array.Empty<IROperand>(), dataWord.Line, dataWord.Column, dataWord.SourceLine);
                if (!TryResolveOperand(dataWord.Operand, labelTable, ir.Constants, sourceFile, contextInstruction, diagnostics, out var resolvedWord))
                    continue;

                writer.Write((ushort)(resolvedWord.Value & 0xFFFF));
                if (debugFlags.ShowTokens)
                    Console.WriteLine($"DATA: {resolvedWord.Value:X8}");
            }
        }

        WriteFooter(writer);

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return CompilationResult<GenerationOutput>.Failed(diagnostics);

        var bytes = memory.ToArray();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("Code generation complete!");

        var outp = new GenerationOutput { Bytes = bytes, Labels = new Dictionary<string, uint>(labelTable) };
        return new CompilationResult<GenerationOutput>(outp, diagnostics);
    }

    private static int GetPayloadWordCount(AssemblerIR ir, HashSet<int> wideJumps)
        => ir.Items.Select((item, index) => item.WordCount + (wideJumps.Contains(index) ? 1 : 0)).Sum() + 2;

    private static Dictionary<string, uint> BuildLabelTable(AssemblerIR ir, uint loadAddress, HashSet<int> wideJumps)
    {
        var itemAddresses = new long[ir.Items.Count];
        long cursor = loadAddress;
        for (var i = 0; i < ir.Items.Count; i++)
        {
            itemAddresses[i] = cursor;
            cursor += ir.Items[i].WordCount + (wideJumps.Contains(i) ? 1 : 0);
        }

        var labels = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var eofAddress = (uint)cursor;
        foreach (var label in ir.Labels.OrderBy(l => l.Line))
        {
            var targetAddress = eofAddress;
            for (var i = 0; i < ir.Items.Count; i++)
            {
                if (ir.Items[i].Line < label.Line)
                    continue;

                targetAddress = (uint)itemAddresses[i];
                break;
            }

            labels.TryAdd(label.Name, targetAddress);
        }

        return labels;
    }

    private static HashSet<int> DetermineWideJumps(AssemblerIR ir, uint loadAddress, bool is32BitLimit)
    {
        var wideJumps = new HashSet<int>();
        if (!is32BitLimit)
            return wideJumps;

        // A 32-bit jump carries its target in two operand words. Whether a jump
        // needs that form depends on the target address, which itself depends
        // on the size of earlier jumps. Mark jumps whose targets fall outside
        // the 16-bit range and repeat until the layout is stable.
        while (true)
        {
            var labels = BuildLabelTable(ir, loadAddress, wideJumps);
            var changed = false;

            for (var i = 0; i < ir.Items.Count; i++)
            {
                if (wideJumps.Contains(i))
                    continue;

                var item = ir.Items[i];
                if (item.Kind != IRProgramItemKind.Instruction)
                    continue;

                var instruction = item.Instruction;
                if (!IsJumpOpcode(instruction.Opcode) || instruction.Operands.Count != 1)
                    continue;

                var operand = instruction.Operands[0];
                if (operand.Kind == IROperandKind.Register)
                    continue;

                var scratchDiagnostics = new List<Diagnostic>();
                if (!TryResolveOperand(operand, labels, ir.Constants, string.Empty, instruction, scratchDiagnostics, out var resolved))
                    continue;

                if (resolved.Kind != IROperandKind.Register && resolved.Value > ushort.MaxValue)
                {
                    wideJumps.Add(i);
                    changed = true;
                }
            }

            if (!changed)
                return wideJumps;
        }
    }

    private static bool IsJumpOpcode(string opcode)
        => opcode switch
        {
            "JMP" or "JPZ" or "JNZ" or "JEQ" or "JNE" or "JLT" or "JGT" or "JLE" or "JGE" => true,
            _ => false,
        };

    private static uint ResolveResetVectorAddress(AssemblerIR ir, uint loadAddress, HashSet<int> wideJumps)
    {
        uint address = loadAddress;
        for (var i = 0; i < ir.Items.Count; i++)
        {
            if (ir.Items[i].SegmentKind == IRSegmentKind.Code)
                return address;

            address += (uint)(ir.Items[i].WordCount + (wideJumps.Contains(i) ? 1 : 0));
        }

        return 0;
    }

    private static long GetStrictFormatMaxPayloadWords(AssemblerMode mode, ushort mapper)
    {
        if (mapper == 2 || mode == AssemblerMode.Extended2)
            return int.MaxValue;
        return mode != AssemblerMode.Legacy ? ExtendedStrictFormatMaxPayloadWords : LegacyStrictFormatMaxPayloadWords;
    }

    private static bool ValidateInstructionOperands(
        IRInstruction instruction,
        IReadOnlyList<ResolvedOperand> resolvedOperands,
        AssemblerMode mode,
        string sourceFile,
        List<Diagnostic> diagnostics)
    {
        // In legacy mode, extended registers (R0-R7, AM, sub-registers) are not available.
        if (mode == AssemblerMode.Legacy)
        {
            for (int i = 0; i < resolvedOperands.Count; i++)
            {
                var ro = resolvedOperands[i];
                if (ro.Kind == IROperandKind.Register && ro.Value >= 0x0007)
                {
                    string regName = Enum.IsDefined(typeof(RegisterId), ro.Value)
                        ? ((RegisterId)ro.Value).ToString()
                        : $"0x{ro.Value:X4}";
                    diagnostics.Add(Diagnostic.Error(
                        $"Register operand '{regName}' is only available in extended mode.",
                        sourceFile,
                        instruction.Line,
                        instruction.Column,
                        instruction.SourceLine));
                    return false;
                }
            }
        }

        if (!ValidateReadOnlyRegisterDestinations(instruction, resolvedOperands, sourceFile, diagnostics))
            return false;

        return instruction.Opcode switch
        {
            "IN" => ValidatePortTransfer(instruction, resolvedOperands, sourceFile, diagnostics, portOperandIndex: 0, registerOperandIndex: 1),
            "OUT" => ValidatePortTransfer(instruction, resolvedOperands, sourceFile, diagnostics, portOperandIndex: 1, registerOperandIndex: 0),
            _ => true,
        };
    }

    private static bool ValidateReadOnlyRegisterDestinations(
        IRInstruction instruction,
        IReadOnlyList<ResolvedOperand> resolvedOperands,
        string sourceFile,
        List<Diagnostic> diagnostics)
    {
        // Destination operand positions for the instructions that can write a
        // register. MS is read-only, so it may appear as a source but never as
        // a destination (including LOD, POP and IN).
        int[] destinationIndices = instruction.Opcode switch
        {
            "MOV" or "ADD" or "SUB" or "MUL" or "DIV" or "AND2" or "OR"
                or "XOR2" or "SHL" or "SHR" => new[] { 1 },
            "LOD" or "POP" => new[] { 0 },
            "IN" => new[] { 1 },
            _ => Array.Empty<int>(),
        };

        foreach (int index in destinationIndices)
        {
            if (index >= resolvedOperands.Count)
                continue;

            var operand = resolvedOperands[index];
            if (operand.Kind == IROperandKind.Register && operand.Value == (ushort)RegisterId.MS)
            {
                diagnostics.Add(Diagnostic.Error(
                    "Register operand 'MS' is read-only.",
                    sourceFile,
                    instruction.Line,
                    instruction.Column,
                    instruction.SourceLine));
                return false;
            }
        }

        return true;
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

        return true;
    }

    private static Dictionary<string, uint> ResolveLabelAddresses(
        AssemblerIR ir,
        string sourceFile,
        DebugFlags debugFlags,
        List<Diagnostic> diagnostics,
        uint loadAddress,
        bool is32BitLimit,
        HashSet<int> wideJumps)
    {
        var labelsByName = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var itemAddresses = new long[ir.Items.Count];
        var eofAddress = (long)loadAddress;

        for (var i = 0; i < ir.Items.Count; i++)
        {
            itemAddresses[i] = eofAddress;
            eofAddress += ir.Items[i].WordCount + (wideJumps.Contains(i) ? 1 : 0);
            if (!is32BitLimit && eofAddress > ushort.MaxValue)
            {
                diagnostics.Add(Diagnostic.Error(
                    "Program exceeds addressable 16-bit ROM space.",
                    sourceFile,
                    ir.Items[i].Line,
                    ir.Items[i].Column,
                    ir.Items[i].SourceLine));
                break;
            }
        }

        foreach (var label in ir.Labels.OrderBy(l => l.Line))
        {
            var targetAddress = eofAddress;
            for (var i = 0; i < ir.Items.Count; i++)
            {
                if (ir.Items[i].Line < label.Line)
                    continue;

                targetAddress = itemAddresses[i];
                break;
            }

            if (!labelsByName.TryAdd(label.Name, (uint)targetAddress))
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
                Console.WriteLine($"LABEL DECL: {label.Name} => {targetAddress:X8}");
        }

        return labelsByName;
    }

    private static bool TryResolveOperand(
        IROperand operand,
        Dictionary<string, uint> labelTable,
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
            case IROperandKind.LabelLow:
            case IROperandKind.LabelHigh:
                if (operand.Symbol is not null && labelTable.TryGetValue(operand.Symbol, out var halfAddress))
                {
                    var halfValue = operand.Kind == IROperandKind.LabelLow
                        ? halfAddress & 0xFFFF
                        : (halfAddress >> 16) & 0xFFFF;
                    resolved = new ResolvedOperand(halfValue, IROperandKind.Immediate);
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

    private static ushort EncodeOpcode(IRInstruction instruction, ushort opcodeValue, IReadOnlyList<ResolvedOperand> resolvedOperands, bool wideJump)
    {
        if (opcodeValue > 0x00FF)
            return opcodeValue;

        byte operandCount = (byte)Math.Min(resolvedOperands.Count, 0b11);
        byte operandOneType = 0;
        byte operandTwoType = 0;

        if (resolvedOperands.Count > 0)
            operandOneType = ClassifyOperandType(instruction.Opcode, resolvedOperands[0], 0, wideJump && resolvedOperands.Count == 1);
        if (resolvedOperands.Count > 1)
            operandTwoType = ClassifyOperandType(instruction.Opcode, resolvedOperands[1], 1, false);

        var controlLowByte = (byte)((operandCount & 0b11) | ((operandOneType & 0b11) << 2) | ((operandTwoType & 0b11) << 4));
        return (ushort)((opcodeValue << 8) | controlLowByte);
    }

    private static byte ClassifyOperandType(string opcode, ResolvedOperand operand, int operandIndex, bool wideJump)
    {
        // A jump target above the 16-bit range is encoded as a two-word
        // direct-memory operand: low word first, then high word. The
        // direct-memory type marks the wide form for the processor.
        if (wideJump && operand.Kind != IROperandKind.Register)
            return OperandTypeDirectMemory;

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

    private static void WriteHeader(Stream stream, AssemblerMode mode, int payloadWordCount, uint loadAddress, uint resetVector, byte version, ushort mapper)
    {
        if (mode != AssemblerMode.Legacy)
        {
            var magicBytes = System.Text.Encoding.ASCII.GetBytes(".VFOX16EXT");
            stream.Write(magicBytes, 0, magicBytes.Length);
            stream.WriteByte(version);
            if (version == 3)
            {
                WriteBigEndianUInt16(stream, mapper);
                WriteBigEndianUInt32(stream, loadAddress);
                WriteBigEndianUInt32(stream, resetVector);
                WriteBigEndianUInt32(stream, (uint)payloadWordCount);
            }
            else if (version == 2)
            {
                WriteBigEndianUInt16(stream, mapper);
                WriteBigEndianUInt16(stream, (ushort)loadAddress);
                WriteBigEndianUInt16(stream, (ushort)resetVector);
                WriteBigEndianUInt16(stream, (ushort)payloadWordCount);
            }
            else
            {
                WriteBigEndianUInt16(stream, mapper);
                WriteBigEndianUInt16(stream, (ushort)loadAddress);
                WriteBigEndianUInt16(stream, (ushort)payloadWordCount);
            }
            return;
        }

        var legacyMagicBytes = System.Text.Encoding.ASCII.GetBytes(".VISOFOX16");
        stream.Write(legacyMagicBytes, 0, legacyMagicBytes.Length);
    }

    private static void WriteBigEndianUInt32(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)((value >> 16) & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.WriteByte((byte)(value & 0xFF));
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
