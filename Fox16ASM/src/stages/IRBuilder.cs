using System.Globalization;
using System.Text.RegularExpressions;

namespace Fox16ASM;

class IRBuilder
{
    private static readonly Regex LabelIdentifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex ConstantReference = new("^<([^<>]+)>$", RegexOptions.Compiled);

    public CompilationResult<AssemblerIR> Build(SourceLine[] lines, string sourceFile, AssemblerMode mode, DebugFlags? debugFlags = null)
    {
        debugFlags ??= new DebugFlags();
        var diagnostics = new List<Diagnostic>();
        var labels = new List<IRLabel>();
        var instructions = new List<IRInstruction>();
        var constants = new Dictionary<string, IROperand>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceLine in lines)
        {
            var parts = sourceLine.Text.Replace(",", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].StartsWith('@'))
                continue;

            var directive = parts[0];
            if (!directive.Equals("@const", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Diagnostic.Error(
                    $"Unknown directive '{directive}'.",
                    sourceFile,
                    sourceLine.LineNumber,
                    1,
                    sourceLine.Text));
                continue;
            }

            if (parts.Length != 3)
            {
                diagnostics.Add(Diagnostic.Error(
                    "Invalid @const directive. Expected '@const <name> <value>'.",
                    sourceFile,
                    sourceLine.LineNumber,
                    1,
                    sourceLine.Text));
                continue;
            }

            var name = parts[1];
            if (string.IsNullOrWhiteSpace(name) || name.Contains('<') || name.Contains('>'))
            {
                diagnostics.Add(Diagnostic.Error(
                    $"Invalid constant name '{name}'.",
                    sourceFile,
                    sourceLine.LineNumber,
                    sourceLine.Text.IndexOf(name, StringComparison.Ordinal) + 1,
                    sourceLine.Text));
                continue;
            }

            var valueToken = parts[2];
            if (!TryParseOperandToken(valueToken, sourceLine, sourceFile, mode, diagnostics, out var constantValue))
                continue;

            constants[name] = constantValue;
        }

        foreach (var sourceLine in lines)
        {
            var parts = sourceLine.Text.Replace(",", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            if (parts[0].StartsWith('@'))
                continue;

            var cursor = 0;
            if (parts[cursor].StartsWith(':'))
            {
                var name = parts[cursor][1..];
                if (string.IsNullOrWhiteSpace(name) || !LabelIdentifier.IsMatch(name))
                {
                    diagnostics.Add(Diagnostic.Error(
                        "Invalid label declaration.",
                        sourceFile,
                        sourceLine.LineNumber,
                        1,
                        sourceLine.Text));
                    continue;
                }

                labels.Add(new IRLabel(name, sourceLine.LineNumber, 1, sourceLine.Text));
                cursor++;
                if (cursor >= parts.Length)
                    continue;
            }

            var opcodeToken = parts[cursor];
            if (!char.IsLetter(opcodeToken[0]))
            {
                diagnostics.Add(Diagnostic.Error(
                    $"Expected opcode but found '{opcodeToken}'.",
                    sourceFile,
                    sourceLine.LineNumber,
                    sourceLine.Text.IndexOf(opcodeToken, StringComparison.Ordinal) + 1,
                    sourceLine.Text));
                continue;
            }

            var opcode = opcodeToken.ToUpperInvariant();
            var operands = new List<IROperand>();
            for (var i = cursor + 1; i < parts.Length; i++)
            {
                if (!TryParseOperandToken(parts[i], sourceLine, sourceFile, mode, diagnostics, out var operand))
                    continue;

                operands.Add(operand);
            }

            if (!ValidateOpcodeMode(opcode, operands.Count, mode, sourceLine, sourceFile, diagnostics))
                continue;

            RewriteOverloadedOpcode(opcode, operands, out var rewrittenOpcode);
            instructions.Add(new IRInstruction(rewrittenOpcode, operands, sourceLine.LineNumber, 1, sourceLine.Text));
        }

        if (debugFlags.ShowTokens)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            foreach (var label in labels)
                Console.WriteLine($"IR LABEL: :{label.Name}");

            foreach (var constant in constants)
                Console.WriteLine($"IR CONST: <{constant.Key}> = {RenderOperand(constant.Value)}");

            foreach (var instruction in instructions)
            {
                var renderedOperands = string.Join(' ', instruction.Operands.Select(RenderOperand));
                Console.WriteLine($"IR INST: {instruction.Opcode}{(renderedOperands.Length > 0 ? " " + renderedOperands : string.Empty)}");
            }

            Console.ForegroundColor = ConsoleColor.White;
        }

        return diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
            ? CompilationResult<AssemblerIR>.Failed(diagnostics)
            : new CompilationResult<AssemblerIR>(new AssemblerIR(labels, instructions, constants), diagnostics);
    }

    private static bool TryParseOperandToken(
        string part,
        SourceLine sourceLine,
        string sourceFile,
        AssemblerMode mode,
        List<Diagnostic> diagnostics,
        out IROperand operand)
    {
        var column = sourceLine.Text.IndexOf(part, StringComparison.Ordinal) + 1;

        if (part.StartsWith("%", StringComparison.Ordinal))
        {
            var valueText = part[1..];
            if (!ushort.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                diagnostics.Add(Diagnostic.Error(
                    $"Invalid decimal literal '{part}'.",
                    sourceFile,
                    sourceLine.LineNumber,
                    column,
                    sourceLine.Text));
                operand = default;
                return false;
            }

            operand = IROperand.Immediate(value, sourceLine.LineNumber, column);
            return true;
        }

        if (part.StartsWith("$", StringComparison.Ordinal))
        {
            var valueText = part[1..];
            if (!ushort.TryParse(valueText, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
            {
                diagnostics.Add(Diagnostic.Error(
                    $"Invalid hexadecimal literal '{part}'.",
                    sourceFile,
                    sourceLine.LineNumber,
                    column,
                    sourceLine.Text));
                operand = default;
                return false;
            }

            operand = IROperand.Immediate(value, sourceLine.LineNumber, column);
            return true;
        }

        var upperPart = part.ToUpperInvariant();
        if (upperPart.Length == 5 && upperPart.StartsWith("PORT", StringComparison.Ordinal) && upperPart[4] >= '0' && upperPart[4] <= '7')
        {
            if (mode != AssemblerMode.Extended)
            {
                diagnostics.Add(Diagnostic.Error(
                    $"Port operand '{part}' requires --mode extended.",
                    sourceFile,
                    sourceLine.LineNumber,
                    column,
                    sourceLine.Text));
                operand = default;
                return false;
            }

            var portIndex = upperPart[4] - '0';
            operand = IROperand.Immediate((ushort)portIndex, sourceLine.LineNumber, column);
            return true;
        }

        var constMatch = ConstantReference.Match(part);
        if (constMatch.Success)
        {
            var constName = constMatch.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(constName))
            {
                diagnostics.Add(Diagnostic.Error(
                    "Constant reference cannot be empty.",
                    sourceFile,
                    sourceLine.LineNumber,
                    column,
                    sourceLine.Text));
                operand = default;
                return false;
            }

            operand = IROperand.Constant(constName, sourceLine.LineNumber, column);
            return true;
        }

        if (!char.IsLetter(part[0]) && part[0] != '_')
        {
            diagnostics.Add(Diagnostic.Error(
                $"Invalid operand '{part}'.",
                sourceFile,
                sourceLine.LineNumber,
                column,
                sourceLine.Text));
            operand = default;
            return false;
        }

        var symbol = part.ToUpperInvariant();
        if (RegisterOperands.TryParseSymbol(symbol, out var register))
        {
            operand = IROperand.Register((ushort)register, sourceLine.LineNumber, column);
        }
        else
        {
            operand = IROperand.Label(part, sourceLine.LineNumber, column);
        }

        return true;
    }

    private static bool ValidateOpcodeMode(
        string opcode,
        int operandCount,
        AssemblerMode mode,
        SourceLine sourceLine,
        string sourceFile,
        List<Diagnostic> diagnostics)
    {
        if (mode == AssemblerMode.Legacy)
            return ValidateLegacyMode(opcode, sourceLine, sourceFile, diagnostics);

        return ValidateExtendedMode(opcode, operandCount, sourceLine, sourceFile, diagnostics);
    }

    private static bool ValidateLegacyMode(
        string opcode,
        SourceLine sourceLine,
        string sourceFile,
        List<Diagnostic> diagnostics)
    {
        if (opcode is "IN" or "OUT")
        {
            diagnostics.Add(Diagnostic.Error(
                $"Opcode '{opcode}' requires --mode extended.",
                sourceFile,
                sourceLine.LineNumber,
                1,
                sourceLine.Text));
            return false;
        }

        return true;
    }

    private static bool ValidateExtendedMode(
        string opcode,
        int operandCount,
        SourceLine sourceLine,
        string sourceFile,
        List<Diagnostic> diagnostics)
    {
        if (opcode is "IN" or "OUT")
            return true;

        if (opcode is "DBG_LGC" or "DBG_MEM" or "DBG_INP" or "DGB_MEM" or "DGB_INP")
        {
            diagnostics.Add(Diagnostic.Error(
                $"Opcode '{opcode}' is only available in legacy mode.",
                sourceFile,
                sourceLine.LineNumber,
                1,
                sourceLine.Text));
            return false;
        }

        if (opcode is "LFM" or "WTM" or "SRA" or "DWR" or "ILM" or "IWR" or "INC" or "DEC")
        {
            diagnostics.Add(Diagnostic.Error(
                $"Opcode '{opcode}' is disabled in extension mode.",
                sourceFile,
                sourceLine.LineNumber,
                1,
                sourceLine.Text));
            return false;
        }

        if (opcode is "AXY" or "SXY" or "MXY" or "DXY" or "AND" or "ORA" or "XOR" or "BSL" or "BSR")
        {
            if (operandCount == 2)
                return true;

            diagnostics.Add(Diagnostic.Error(
                $"Opcode '{opcode}' requires the two-operand form in extension mode.",
                sourceFile,
                sourceLine.LineNumber,
                1,
                sourceLine.Text));
            return false;
        }

        return true;
    }

    private static string RenderOperand(IROperand operand)
    {
        return operand.Kind switch
        {
            IROperandKind.Immediate => $"${operand.Value:X4}",
            IROperandKind.Register => $"REG({operand.Value:X4})",
            IROperandKind.Label => operand.Symbol ?? string.Empty,
            IROperandKind.Constant => $"<{operand.Symbol ?? string.Empty}>",
            _ => string.Empty,
        };
    }

    private static void RewriteOverloadedOpcode(string opcode, List<IROperand> operands, out string rewritten)
    {
        rewritten = opcode;
        if (operands.Count != 2)
            return;

        rewritten = opcode switch
        {
            "AXY" => "ADD",
            "SXY" => "SUB",
            "MXY" => "MUL",
            "DXY" => "DIV",
            "AND" => "AND2",
            "ORA" => "OR",
            "OR" => "OR",
            "XOR" => "XOR2",
            "BSL" => "SHL",
            "BSR" => "SHR",
            _ => opcode,
        };
    }
}
