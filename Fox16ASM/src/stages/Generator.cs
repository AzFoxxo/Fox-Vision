using System.IO;
using MiscUtil.IO;
using MiscUtil.Conversion;
using Fox16Shared;

namespace Fox16ASM
{
    class Generator
    {
        /// <summary>
        /// Generate the ROM file using the tokens
        /// Directly generate machine code from tokens
        /// </summary>
        /// <param name="tokens">tokens</param>
        /// <param name="outputFile">ROM filename</param>
        public void Generate(Token[] tokens, string outputFile)
        {
            // Delete the file if it exists
            if (File.Exists(outputFile)) File.Delete(outputFile);

            // Write the header to the file
            WriteHeader(outputFile);

            // Write the ROM
            WriteROM(tokens, outputFile);

            // Write the footer to the file
            WriteFooter(outputFile);

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Code generation complete!");
            Console.WriteLine($"ROM file written to {outputFile}");
        }

        /// <summary>
        /// Write the footer to the file
        /// </summary>
        /// <param name="outputFile">ROM filename</param>
        private void WriteFooter(string outputFile)
        {
            // Pad the file with a NOP and a HLT instruction
            using (var stream = File.Open(outputFile, FileMode.Append))
            {
                using (var writer = new EndianBinaryWriter(EndianBitConverter.Big, stream))
                {
                    
                    writer.Write(Convert.ToUInt16(Opcodes.instructions["NOP"]));
                    writer.Write(Convert.ToUInt16(Opcodes.instructions["HLT"]));
                }
            }
        }

        /// <summary>
        /// Write the header to the file
        /// </summary>
        /// <param name="outputFile">ROM filename</param>
        private void WriteHeader(string outputFile)
        {
            using (var stream = File.Open(outputFile, FileMode.OpenOrCreate, FileAccess.Write))
            {
                using (var writer = new BinaryWriter(stream))
                {
                    // Write magic number to the file ".VISOFOX16"
                    string magic = ".VISOFOX16";
                    byte[] magicBytes = System.Text.Encoding.ASCII.GetBytes(magic);
                    writer.Write(magicBytes, 0, magicBytes.Length);
                }
            }
        }


        /// <summary>
        /// Write instructions to ROM
        /// </summary>
        /// <param name="tokens">tokens to write</param>
        /// <param name="outputFile">ROM filename</param>
        private void WriteROM(Token[] tokens, string outputFile)
        {
            // Write the tokens to the file
            using (var stream = File.Open(outputFile, FileMode.Append))
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
                            if (Opcodes.instructions.TryGetValue(Convert.ToString(token.value), out byte value))
                            {
                                Console.WriteLine(value);
                                writer.Write(Convert.ToByte(value));
                            }
                            // TODO: Error handling
                        }
                    }
                }
            }
        }
    }
}