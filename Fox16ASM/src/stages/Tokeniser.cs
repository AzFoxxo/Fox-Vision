using System.IO;

namespace Fox16ASM
{
    class Tokeniser
    {
        /// <summary>
        /// Convert lines to tokens
        /// </summary>
        /// <returns>Returns a list of tokens which can be fed to the next stage of compilation</returns>
        public static Token[] ConvertLinesToTokens(string[] lines)
        {
            List<Token> tokens = [];

            foreach (var line in lines)
            {
                // Consume the line and spit out tokens with a terminator
                tokens.AddRange(ConsumeLine(line));
            }

            // Print all tokens
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

            return [.. tokens];
        }

        private static Token[] ConsumeLine(string line)
        {
            // Slice the lines at spaces
            var parts = line.Split(' ');

            Token lastToken = new(0, TokenType.Null);

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
                    // If previous instruction was JMP, JNZ or JPZ, TokenType should be Label
                    if (lastToken.type == TokenType.Opcode && (
                    (string)lastToken.value == "JMP" ||
                    (string)lastToken.value == "JNZ" ||
                    (string)lastToken.value == "JPZ"))
                        tokens.Add(new Token(part, TokenType.Label));
                    else
                        tokens.Add(new Token(part, TokenType.Opcode));
                }
                // Invalid token found
                else
                {
                    // TODO: Error handling
                    Console.WriteLine($"Invalid token found {part}");
                    Environment.Exit(1);
                }

                // Update the last token
                if (tokens.Count > 0)
                    lastToken = tokens[^1];
            }

            // Add line terminator
            tokens.Add(new Token("", TokenType.Terminator));

            return [.. tokens];
        }
    }
}