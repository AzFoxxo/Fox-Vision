using System.Globalization;
using System.Text.RegularExpressions;

namespace Fox16ASM;

class IRBuilder
{
    private static readonly Regex LabelIdentifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public CompilationResult<AssemblerIR> Build(SourceLine[] lines, string sourceFile, DebugFlags? debugFlags = null)
    {
        debugFlags ??= new DebugFlags();
        var diagnostics = new List<Diagnostic>();
        var labels = new List<IRLabel>();
        var instructions = new List<IRInstruction>();

        foreach (var sourceLine in lines)
        {
            var parts = sourceLine.Text.Replace(",", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
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
                var part = parts[i];
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
                        continue;
                    }

                    operands.Add(IROperand.Immediate(value, sourceLine.LineNumber, column));
                    continue;
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
                        continue;
                    }

                    operands.Add(IROperand.Immediate(value, sourceLine.LineNumber, column));
                    continue;
                }

                if (!char.IsLetter(part[0]) && part[0] != '_')
                {
                    diagnostics.Add(Diagnostic.Error(
                        $"Invalid operand '{part}'.",
                        sourceFile,
                        sourceLine.LineNumber,
                        column,
                        sourceLine.Text));
                    continue;
                }

                var symbol = part.ToUpperInvariant();
                if (RegisterOperands.TryParseSymbol(symbol, out var register))
                {
                    operands.Add(IROperand.Register((ushort)register, sourceLine.LineNumber, column));
                }
                else
                {
                    operands.Add(IROperand.Label(part, sourceLine.LineNumber, column));
                }
            }

            RewriteOverloadedOpcode(opcode, operands, out var rewrittenOpcode);
            instructions.Add(new IRInstruction(rewrittenOpcode, operands, sourceLine.LineNumber, 1, sourceLine.Text));
        }

        if (debugFlags.ShowTokens)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            foreach (var label in labels)
                Console.WriteLine($"IR LABEL: :{label.Name}");

            foreach (var instruction in instructions)
            {
                var renderedOperands = string.Join(' ', instruction.Operands.Select(RenderOperand));
                Console.WriteLine($"IR INST: {instruction.Opcode}{(renderedOperands.Length > 0 ? " " + renderedOperands : string.Empty)}");
            }

            Console.ForegroundColor = ConsoleColor.White;
        }

        return diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
            ? CompilationResult<AssemblerIR>.Failed(diagnostics)
            : new CompilationResult<AssemblerIR>(new AssemblerIR(labels, instructions), diagnostics);
    }

    private static string RenderOperand(IROperand operand)
    {
        return operand.Kind switch
        {
            IROperandKind.Immediate => $"${operand.Value:X4}",
            IROperandKind.Register => $"REG({operand.Value:X4})",
            IROperandKind.Label => operand.Symbol ?? string.Empty,
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
