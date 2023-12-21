using System.IO;

namespace Fox16ASM
{
    class Tokeniser
    {
        /// <summary>
        /// Convert lines to tokens
        /// </summary>
        /// <returns>Returns a list of tokens which can be fed to the next stage of compilation</returns>
        public Token[] ConvertLinesToTokens(string[] lines)
        {
            List<Token> tokens = new();

            foreach (var line in lines)
            {
                // Consume the line and spit out tokens with a terminator
                tokens.AddRange(ConsumeLine(line));
            }

            // Print all tokens
            foreach (var token in tokens)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(token.value);
                
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(": ");

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(token.type);
            }

            return tokens.ToArray();
        }

        private Token[] ConsumeLine(string line)
        {
            // Slice the lines at spaces
            var parts = line.Split(' ');

            List<Token> tokens = new();
            foreach (var part in parts)
            {
                // TODO: Error handling on type conversion (plus bounds checks)
                // If part starts with a %, convert to decimal value
                if (part.StartsWith("%"))
                {
                    tokens.Add(new Token(int.Parse(part.Remove(0, 1)), TokenType.Decimal));
                }
                // If part starts with a $, convert to hex value
                else if (part.StartsWith("$"))
                {
                    tokens.Add(new Token(ushort.Parse(part.Remove(0, 1), System.Globalization.NumberStyles.HexNumber), TokenType.Hexadecimal));
                }
                // If part starts with letter A-Z, convert to opcode
                else if (Char.IsLetter(part[0]))
                {
                    tokens.Add(new Token(part, TokenType.Opcode));
                }
                // Invalid token found
                else 
                {
                    // TODO: Error handling
                    Console.WriteLine($"Invalid token found {part}");
                }
            }

            // Add line terminator
            tokens.Add(new Token("", TokenType.Terminator));

            return tokens.ToArray();
        }
    }
}