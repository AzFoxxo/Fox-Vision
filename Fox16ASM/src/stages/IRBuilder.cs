using System.Globalization;
using System.Text.RegularExpressions;

namespace Fox16ASM;

class IRBuilder
{
    private static readonly Regex LabelIdentifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex ConstantReference = new("^<([^<>]+)>$", RegexOptions.Compiled);

    public CompilationResult<AssemblerIR> Build(SourceLine[] lines, string sourceFile, DebugFlags? debugFlags = null)
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
            if (!TryParseOperandToken(valueToken, sourceLine, sourceFile, diagnostics, out var constantValue))
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
                if (!TryParseOperandToken(parts[i], sourceLine, sourceFile, diagnostics, out var operand))
                    continue;

                operands.Add(operand);
            }

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
