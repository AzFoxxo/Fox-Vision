namespace Fox16ASM
{
    class Tokeniser
    {
        /// <summary>
        /// Convert lines to tokens
        /// </summary>
        /// <returns>Returns a list of tokens which can be fed to the next stage of compilation</returns>
        public static Token[] ConvertLinesToTokens(string[] lines, DebugFlags? debugFlags = null)
        {
            debugFlags ??= new DebugFlags();
            List<Token> tokens = [];

            foreach (var line in lines)
            {
                // Consume the line and spit out tokens with a terminator
                tokens.AddRange(ConsumeLine(line));
            }

            // Print all tokens (if debug flag is enabled)
            if (debugFlags.ShowTokens)
            {
                foreach (var token in tokens)
                {
                    var type = token.type;

                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write(type);

                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write(": ");

                    Console.ForegroundColor = ConsoleColor.Green;
                    if (type == TokenType.Hexadecimal)
                        Console.Write($"{token.value:X4} ({token.value})");
                    else if (type == TokenType.Decimal)
                        Console.Write($"{token.value}");
                    else if (type == TokenType.Opcode)
                        Console.Write($"{token.value}");
                    else if (type == TokenType.LabelDeclaration)
                        Console.Write($"{token.value}");
                    else if (type == TokenType.Label)
                        Console.Write($"{token.value}");
                    else
                        Console.Write("N\\A");


                    Console.WriteLine();

                }
            }

            return [.. tokens];
        }

        private static Token[] ConsumeLine(string line)
        {
            // Allow commas as operand separators and split on whitespace.
            var parts = line.Replace(",", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var lineHasOpcode = false;

            List<Token> tokens = [];
            foreach (var part in parts)
            {
                // TODO: Error handling on type conversion (plus bounds checks)
                // If part starts with a %, convert to decimal value
                if (part.StartsWith('%'))
                {
                    tokens.Add(new Token(int.Parse(part.Remove(0, 1)), TokenType.Decimal));
                }
                // If part starts with a $, convert to hex value
                else if (part.StartsWith('$'))
                {
                    tokens.Add(new Token(ushort.Parse(part.Remove(0, 1), System.Globalization.NumberStyles.HexNumber), TokenType.Hexadecimal));
                }
                // If part starts with a :, treat it as a label
                else if (part.StartsWith(':'))
                {
                    tokens.Add(new Token(part.Remove(0, 1), TokenType.LabelDeclaration));
                }
                // If part starts with letter A-Z, convert to opcode
                else if (Char.IsLetter(part[0]))
                {
                    var symbol = part.ToUpperInvariant();

                    // The first alpha token is the instruction mnemonic.
                    if (!lineHasOpcode)
                    {
                        tokens.Add(new Token(symbol, TokenType.Opcode));
                        lineHasOpcode = true;
                    }
                    // Register operands for multi-operand instructions.
                    else if (RegisterOperands.TryParseSymbol(symbol, out var register))
                    {
                        tokens.Add(new Token((ushort)register, TokenType.Hexadecimal));
                    }
                    else
                    {
                        // Remaining alpha tokens are label/symbol operands.
                        tokens.Add(new Token(part, TokenType.Label));
                    }
                }
                // Invalid token found
                else
                {
                    // TODO: Error handling
                    Console.WriteLine($"Invalid token found {part}");
                    Environment.Exit(1);
                }
            }

            RewriteOverloadedOpcode(tokens);

            // Add line terminator
            tokens.Add(new Token("", TokenType.Terminator));

            return [.. tokens];
        }

        private static void RewriteOverloadedOpcode(List<Token> tokens)
        {
            if (tokens.Count == 0 || tokens[0].type != TokenType.Opcode)
                return;

            // Overloaded ALU instructions use V1.6 opcodes only when two operands are present.
            if (tokens.Count != 3)
                return;

            var opcode = (string)tokens[0].value;
            var rewritten = opcode switch
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
                _ => opcode
            };

            if (rewritten != opcode)
                tokens[0] = new Token(rewritten, TokenType.Opcode);
        }
    }
}