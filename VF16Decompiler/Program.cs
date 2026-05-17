using System;
using System.IO;
using System.Buffers.Binary;
using Fox16Shared;

namespace VF16Decompiler
{
    internal class Program
    {
        private const int LegacyRomHeaderLength = 10;
        private const int ExtendedRomHeaderLength = 17;
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

                // Disassemble
                for (int i = 0; i < words.Length; i++)
                {
                    int consumed = WriteInstructionLine(i, idx => (idx < words.Length) ? (ushort?)words[idx] : null);
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

        private static bool TryDecodeRom(byte[] rawRom, out ushort[] image)
        {
            image = Array.Empty<ushort>();
            if (rawRom.Length < LegacyRomHeaderLength)
                return false;

            string magic = System.Text.Encoding.ASCII.GetString(rawRom, 0, LegacyRomHeaderLength);
            if (magic == ExtendedRomMagic)
            {
                if (rawRom.Length < ExtendedRomHeaderLength)
                    return false;

                byte version = rawRom[10];
                if (version != 1) return false;

                ushort expectedWords = BinaryPrimitives.ReadUInt16BigEndian(rawRom.AsSpan(15, 2));
                var payloadLengthBytes = rawRom.Length - ExtendedRomHeaderLength;
                if ((payloadLengthBytes & 1) != 0) payloadLengthBytes++;
                if (payloadLengthBytes / 2 != expectedWords) return false;

                var payload = new byte[payloadLengthBytes];
                Array.Copy(rawRom, ExtendedRomHeaderLength, payload, 0, rawRom.Length - ExtendedRomHeaderLength);
                ushort[] rom = new ushort[payload.Length / 2];
                for (int i = 0; i < rom.Length; i++)
                {
                    rom[i] = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(i * 2, 2));
                }

                image = rom;
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

            image = legacyRom;
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
}
