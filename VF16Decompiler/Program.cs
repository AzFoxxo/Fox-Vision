using System;
using System.IO;
using System.Buffers.Binary;
using Fox16Shared;

namespace VF16Decompiler
{
    internal class Program
    {
        private const int LegacyRomHeaderLength = 10;
        private const int ExtendedRomVersionFieldLength = 1;
        private const int ExtendedRomMapperFieldLength = 2;
        private const int ExtendedRomStartFieldLength = 2;
        private const int ExtendedRomResetVectorFieldLength = 2;
        private const int ExtendedRomSizeFieldLength = 2;
        private const int LegacyRomLimitWords = 0x1000;
        private const int ExtendedRomLimitWords = 0x8000;
        private const string LegacyRomMagic = ".VISOFOX16";
        private const string ExtendedRomMagic = ".VFOX16EXT";

        public static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("Usage: VF16Decompiler <romfile>");
                return 1;
            }

            var path = args[0];
            if (!File.Exists(path))
            {
                Console.WriteLine("File not found: " + path);
                return 2;
            }

            try
            {
                var raw = File.ReadAllBytes(path);
                if (!TryDecodeRom(raw, out var words))
                {
                    Console.WriteLine("Failed to decode ROM file.");
                    return 3;
                }

                Console.WriteLine($"; ROM start ${words.StartAddress:X4} reset ${words.ResetAddress:X4} size {words.Words.Length} words");

                int codeStartIndex = Math.Max(0, words.ResetAddress - words.StartAddress);

                // Disassemble from the reset vector so leading data segments do not appear as opcodes.
                for (int i = codeStartIndex; i < words.Words.Length; i++)
                {
                    int currentIndex = i;
                    ushort currentAddress = (ushort)(words.StartAddress + currentIndex);
                    int consumed = WriteInstructionLine(currentAddress, offset =>
                    {
                        int relativeIndex = currentIndex + offset;
                        return relativeIndex >= 0 && relativeIndex < words.Words.Length ? (ushort?)words.Words[relativeIndex] : null;
                    });
                    i += consumed;
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                return 4;
            }
        }

        private static bool TryDecodeRom(byte[] rawRom, out RomImage image)
        {
            image = default;
            if (rawRom.Length < LegacyRomHeaderLength)
                return false;

            string magic = System.Text.Encoding.ASCII.GetString(rawRom, 0, LegacyRomHeaderLength);
            if (magic == ExtendedRomMagic)
            {
                byte version = rawRom[10];
                if (version != 1 && version != 2)
                    return false;

                int headerLength = LegacyRomHeaderLength
                    + ExtendedRomVersionFieldLength
                    + ExtendedRomMapperFieldLength
                    + ExtendedRomStartFieldLength
                    + (version == 1 ? 0 : ExtendedRomResetVectorFieldLength)
                    + ExtendedRomSizeFieldLength;

                if (rawRom.Length < headerLength)
                    return false;

                int mapperOffset = LegacyRomHeaderLength + ExtendedRomVersionFieldLength;
                int romStartOffset = mapperOffset + ExtendedRomMapperFieldLength;
                int resetVectorOffset = romStartOffset + ExtendedRomStartFieldLength;
                int romSizeOffset = resetVectorOffset + (version == 1 ? 0 : ExtendedRomResetVectorFieldLength);

                ushort mapper = BinaryPrimitives.ReadUInt16BigEndian(rawRom.AsSpan(mapperOffset, ExtendedRomMapperFieldLength));
                ushort romStart = BinaryPrimitives.ReadUInt16BigEndian(rawRom.AsSpan(romStartOffset, ExtendedRomStartFieldLength));
                ushort resetVector = version == 1
                    ? romStart
                    : BinaryPrimitives.ReadUInt16BigEndian(rawRom.AsSpan(resetVectorOffset, ExtendedRomResetVectorFieldLength));
                ushort expectedWords = BinaryPrimitives.ReadUInt16BigEndian(rawRom.AsSpan(romSizeOffset, ExtendedRomSizeFieldLength));
                int romLimitWords = mapper switch
                {
                    0 => LegacyRomLimitWords,
                    1 => ExtendedRomLimitWords,
                    _ => LegacyRomLimitWords
                };

                var payloadLengthBytes = rawRom.Length - headerLength;
                if ((payloadLengthBytes & 1) != 0) payloadLengthBytes++;
                if (payloadLengthBytes / 2 != expectedWords) return false;

                var payload = new byte[payloadLengthBytes];
                Array.Copy(rawRom, headerLength, payload, 0, rawRom.Length - headerLength);
                ushort[] rom = new ushort[payload.Length / 2];
                for (int i = 0; i < rom.Length; i++)
                {
                    rom[i] = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(i * 2, 2));
                }

                image = new RomImage(rom, romStart, resetVector, romLimitWords);
                return true;
            }

            if (magic != LegacyRomMagic) return false;

            var payloadBytes = new byte[rawRom.Length - LegacyRomHeaderLength];
            Array.Copy(rawRom, LegacyRomHeaderLength, payloadBytes, 0, payloadBytes.Length);
            if (payloadBytes.Length % 2 != 0)
            {
                Array.Resize(ref payloadBytes, payloadBytes.Length + 1);
            }

            ushort[] legacyRom = new ushort[payloadBytes.Length / 2];
            for (int i = 0; i < legacyRom.Length; i++)
            {
                // foxvision legacy encoding stores big-endian words in file; swap if host is little-endian
                if (BitConverter.IsLittleEndian)
                {
                    // read big-endian
                    legacyRom[i] = BinaryPrimitives.ReadUInt16BigEndian(payloadBytes.AsSpan(i * 2, 2));
                }
                else
                {
                    legacyRom[i] = BitConverter.ToUInt16(payloadBytes, i * 2);
                }
            }

            image = new RomImage(legacyRom, 0, 0, LegacyRomLimitWords);
            return true;
        }

        private static int WriteInstructionLine(int address, Func<int, ushort?> readWord)
        {
            ushort opcodeWord = readWord(0) ?? 0;
            ushort opcodeValue = DecodeDisplayOpcodeValue(opcodeWord);
            string opcode = GetDisplayOpcode(opcodeValue);

            Console.Write($"${address:X4} {opcodeWord:X4}→{opcode} ");

            switch (opcodeValue)
            {
                case 0x1:
                case 0x2:
                case 0xA:
                case 0xB:
                case 0xC:
                case 0x1D:
                case 0x1E:
                case 0x1F:
                case 0x20:
                case 0x21:
                case 0x22:
                    {
                        ushort? operand = readWord(1);
                        if (operand.HasValue)
                        {
                            Console.WriteLine($"{operand.Value:X4} ({operand.Value})");
                            return 1;
                        }

                        Console.WriteLine("<missing operand>");
                        return 0;
                    }

                case 0x3:
                case 0x14:
                case Opcodes.DEBUG_EXTENSION_OFFSET:
                    {
                        ushort? operand = readWord(1);
                        if (operand.HasValue)
                        {
                            Console.WriteLine($"({operand.Value})");
                            return 1;
                        }

                        Console.WriteLine("<missing operand>");
                        return 0;
                    }

                case 0x2C:
                case 0x2D:
                    {
                        ushort? operand = readWord(1);
                        if (operand.HasValue)
                        {
                            Console.WriteLine($"[{BuildOperandControlSummary(opcodeWord)}] OP1={BuildTypedOperandDisplay(opcodeWord, operand.Value, 0)}");
                            return 1;
                        }

                        Console.WriteLine("<missing operand>");
                        return 0;
                    }

                case 0x19:
                case 0x1A:
                case 0x1B:
                case 0x23:
                case 0x24:
                case 0x25:
                case 0x26:
                case 0x27:
                case 0x28:
                case 0x29:
                case 0x2A:
                case 0x2B:
                case 0x30:
                case 0x31:
                    {
                        ushort? op1 = readWord(1);
                        ushort? op2 = readWord(2);
                        if (op1.HasValue && op2.HasValue)
                        {
                            Console.WriteLine($"[{BuildOperandControlSummary(opcodeWord)}] OP1={BuildTypedOperandDisplay(opcodeWord, op1.Value, 0)} OP2={BuildTypedOperandDisplay(opcodeWord, op2.Value, 1)}");
                            return 2;
                        }

                        Console.WriteLine("<missing operands>");
                        return 0;
                    }

                default:
                    Console.WriteLine("NOR");
                    return 0;
            }
        }

        private static ushort DecodeDisplayOpcodeValue(ushort opcodeWord)
        {
            if (opcodeWord >= Opcodes.DEBUG_EXTENSION_OFFSET)
                return opcodeWord;

            if (opcodeWord <= 0x00FF)
                return opcodeWord;

            return (ushort)(opcodeWord >> 8);
        }

        private static string BuildOperandControlSummary(ushort opcodeWord)
        {
            var controlByte = (byte)(opcodeWord & 0x00FF);
            int operandCount = controlByte & 0b11;
            string operandOneType = GetOperandTypeLabel(DecodeOperandType(opcodeWord, 0));
            string operandTwoType = GetOperandTypeLabel(DecodeOperandType(opcodeWord, 1));
            return $"CNT={operandCount} OP1={operandOneType} OP2={operandTwoType}";
        }

        private static string BuildTypedOperandDisplay(ushort opcodeWord, ushort operandValue, int operandIndex)
        {
            byte operandType = DecodeOperandType(opcodeWord, operandIndex);
            return operandType switch
            {
                0b00 => $"REG:{FormatRegisterOrRaw(operandValue)}",
                0b01 => $"IMM:{operandValue:X4}({operandValue})",
                0b10 => $"MEM[{operandValue:X4}]",
                0b11 => $"MEM[{FormatRegisterOrRaw(operandValue)}]",
                _ => $"RAW:{operandValue:X4}"
            };
        }

        private static string GetOperandTypeLabel(byte operandType)
            => operandType switch
            {
                0b00 => "REG",
                0b01 => "IMM",
                0b10 => "MEM",
                0b11 => "IND",
                _ => "UNK"
            };

        private static string FormatRegisterOrRaw(ushort value)
            => value switch
            {
                0x0000 => "X",
                0x0001 => "Y",
                0x0007 => "R0",
                0x0008 => "R1",
                0x0009 => "R2",
                0x000A => "R3",
                0x000B => "R4",
                0x000C => "R5",
                0x000D => "R6",
                0x000E => "R7",
                0x0003 => "STATUS",
                0x0004 => "SP",
                0x0006 => "EM",
                _ => $"${value:X4}"
            };

        private static byte DecodeOperandType(ushort opcodeWord, int operandIndex)
        {
            if (opcodeWord <= 0x00FF || opcodeWord >= Opcodes.DEBUG_EXTENSION_OFFSET)
                return 0b00;

            return operandIndex switch
            {
                0 => (byte)((opcodeWord >> 2) & 0b11),
                1 => (byte)((opcodeWord >> 4) & 0b11),
                _ => 0b01
            };
        }

        private static string GetDisplayOpcode(ushort opcodeValue)
        {
            var opcodes = Opcodes.GetKey(opcodeValue);
            if (opcodes.Length == 0) return "???";

            return opcodeValue switch
            {
                Opcodes.DEBUG_EXTENSION_OFFSET => "DBG_LGC",
                Opcodes.DEBUG_EXTENSION_OFFSET + 1 => "DBG_MEM",
                Opcodes.DEBUG_EXTENSION_OFFSET + 2 => "DBG_INP",
                0x23 => "MOI_ADD",
                0x24 => "MOI_SUB",
                0x25 => "MOI_MUL",
                0x26 => "MOI_DIV",
                0x27 => "MOI_AND",
                0x28 => "MOI_OR",
                0x29 => "MOI_XOR",
                0x2A => "MOI_SHL",
                0x2B => "MOI_SHR",
                _ => opcodes[0]
            };
        }
    }

    internal readonly record struct RomImage(ushort[] Words, ushort StartAddress, ushort ResetAddress, int MaximumWords);
}
