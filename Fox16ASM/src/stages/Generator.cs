using System.IO;
using MiscUtil.IO;
using MiscUtil.Conversion;

namespace Fox16ASM
{
    class Generator
    {
        public void Generate(Token[] tokens, string outputFile)
        {
            // Delete the file if it exists
            if (File.Exists(outputFile)) File.Delete(outputFile);

            // Write the header to the file
            // WriteHeader(tokens, outputFile);

            // Write the ROM
            WriteROM(tokens, outputFile);

            // Write the footer to the file
            // WriteFooter(tokens, outputFile);
        }

        private void WriteROM(Token[] tokens, string outputFile)
        {
            // Write the tokens to the file
            using (var stream = File.Open(outputFile, FileMode.Create))
            {
                using (var writer = new EndianBinaryWriter(EndianBitConverter.Big, stream))
                {
                    foreach (var token in tokens)
                    {
                        // Skip all terminating tokens
                        if (token.type == TokenType.Terminator) continue;
                        if (token.type == TokenType.Decimal)
                        {
                            // Write the decimal value as a ushort
                            writer.Write(Convert.ToUInt16(token.value));
                        }
                        if (token.type == TokenType.Hexadecimal)
                        {
                            // Write the hexadecimal value as a ushort
                            Console.WriteLine(token.value);
                            writer.Write(Convert.ToUInt16(token.value));
                        }
                        if (token.type == TokenType.Opcode)
                        {
                            // Get the value of the opcode from Opcodes.instructions
                            ushort value;
                            if (Opcodes.instructions.TryGetValue(Convert.ToString(token.value), out value))
                            {
                                Console.WriteLine(value);
                                writer.Write(Convert.ToUInt16(value));
                            }
                            // TODO: Error handling
                        }
                    }
                }
            }
        }
    }
}