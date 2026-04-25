using System;
using System.IO;
using MiscUtil.IO;
using MiscUtil.Conversion;
using Fox16Shared;

namespace Fox16ASM
{
    class Generator
    {
        private static DebugFlags _debugFlags = new();
        /// <summary>
        /// Generate the ROM file using the tokens
        /// Directly generate machine code from tokens
        /// </summary>
        /// <param name="tokens">tokens</param>
        /// <param name="labels">label declarations to resolve jumps</param>
        /// <param name="outputFile">ROM filename</param>
        /// <param name="debugFlags">debug output flags</param>
        public static void Generate(Token[] tokens, Label[] labels, string outputFile, DebugFlags debugFlags)
        {
            _debugFlags = debugFlags;
            // Delete the file if it exists
            if (File.Exists(outputFile)) File.Delete(outputFile);

            // Write the header to the file
            WriteHeader(outputFile);

            // Write the ROM
            WriteROM(tokens, labels, outputFile);

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
        private static void WriteFooter(string outputFile)
        {
            // Pad the file with a NOP and a HLT instruction
            using var stream = File.Open(outputFile, FileMode.Append);
            using var writer = new EndianBinaryWriter(EndianBitConverter.Big, stream);

            writer.Write(Convert.ToUInt16(Opcodes.instructions["NOP"]));
            writer.Write(Convert.ToUInt16(Opcodes.instructions["HLT"]));
        }

        /// <summary>
        /// Write the header to the file
        /// </summary>
        /// <param name="outputFile">ROM filename</param>
        private static void WriteHeader(string outputFile)
        {
            using var stream = File.Open(outputFile, FileMode.OpenOrCreate, FileAccess.Write);
            using var writer = new BinaryWriter(stream);
            // Write magic number to the file ".VISOFOX16"
            string magic = ".VISOFOX16";
            byte[] magicBytes = System.Text.Encoding.ASCII.GetBytes(magic);
            writer.Write(magicBytes, 0, magicBytes.Length);
        }


        /// <summary>
        /// Write instructions to ROM
        /// </summary>
        /// <param name="tokens">tokens to write</param>
        /// <param name="labels">label declarations to resolve jumps</param>
        /// <param name="outputFile">ROM filename</param>
        private static void WriteROM(Token[] tokens, Label[] labels, string outputFile)
        {
            // Find the address of each label
            LabelResolveAddress(tokens, labels);

            var errorCount = 0;

            // Write the tokens to the file
            using var stream = File.Open(outputFile, FileMode.Append);
            using var writer = new EndianBinaryWriter(EndianBitConverter.Big, stream);
            foreach (var token in tokens)
            {
                // Skip all terminating tokens
                if (token.type == TokenType.Terminator) continue;
                else if (token.type == TokenType.Decimal)
                {
                    // Write the decimal value as a ushort
                    writer.Write(Convert.ToUInt16(token.value));
                }
                else if (token.type == TokenType.Hexadecimal)
                {
                    // Write the hexadecimal value as a ushort
                    if (_debugFlags.ShowTokens)
                        Console.WriteLine($"HEX: {token.value}");
                    writer.Write(Convert.ToUInt16(token.value));
                }
                else if (token.type == TokenType.Opcode)
                {
                    // Get the value of the opcode from Opcodes.instructions
                    if (token.value is string opcode && Opcodes.instructions.TryGetValue(opcode, out ushort value))
                    {
                        // Debug: Print the instruction opcode value being written
                        if (_debugFlags.ShowTokens)
                            Console.WriteLine($"OPCODE: {value}");
                        writer.Write(Convert.ToUInt16(value));
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Unknown opcode: {token.value}");
                        Console.ForegroundColor = ConsoleColor.White;
                        errorCount++;
                    }
                }
                else if (token.type == TokenType.Label)
                {
                    // Get the address of the label
                    var value = 0;
                    var resolved = false;
                    foreach (var label in labels)
                    {
                        // Check if the name of the label matches the token value
                        if (label.name == (string)token.value)
                        {
                            value = label.address;
                            resolved = true;
                            break;
                        }
                    }

                    if (!resolved)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Unresolved label/symbol: {token.value}");
                        Console.ForegroundColor = ConsoleColor.White;
                        errorCount++;
                        continue;
                    }

                    // Debug: Print the resolved label address (in hexadecimal)
                    if (_debugFlags.ShowLabels)
                        Console.WriteLine($"LABEL REFERENCE: {value:X4}");
                    writer.Write(Convert.ToUInt16(value));
                }
            }

            if (errorCount > 0)
            {
                throw new InvalidOperationException($"Code generation failed with {errorCount} error(s).");
            }
        }

        private static void LabelResolveAddress(Token[] tokens, Label[] labels)
        {
            var tokensToSkip = 0; // Number of tokens to skip when resolving labels (terminators and labels)
            var labelCount = 0;
            for (var i = 0; i < tokens.Length; i++)
            {
                if (tokens[i].type == TokenType.LabelDeclaration)
                {
                    // Resolve EOF label resolutions (skip)
                    if (labelCount > labels.Length - 1)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Attempted to resolve EOF label.");
                        Console.WriteLine("Skipping label, will not be written to ROM");
                        continue;
                    }
                    labels[labelCount].address = (ushort)(i - tokensToSkip);
                    // Debug: Print the label declaration and its assigned address
                    if (_debugFlags.ShowLabels)
                        Console.WriteLine($"LABEL DECL: {labels[labelCount].address} = {labels[labelCount].name}");
                    labelCount++;
                    tokensToSkip++;
                }
                else if (tokens[i].type == TokenType.Terminator)
                {
                    tokensToSkip++;
                }
            }
        }
    }
}