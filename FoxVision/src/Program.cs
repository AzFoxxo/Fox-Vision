using System.Text.RegularExpressions;
using Fox16Shared;

namespace FoxVision
{
    public class Program()
    {
        private static VirtualMachine? vm;

        public static void Main()
        {
            var RawROM = LoadROMFile("fox16.bin");

            // Print the first 10 bytes of the ROM as ASCII
            Console.Write("10 byte identifier: ");
            Console.ForegroundColor = ConsoleColor.Blue;
            for (int i = 0; i < 10; i++)
            {
                Console.Write((char)RawROM[i]);
            }
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine();

            // Delete the first 10 bytes of the ROM
            RawROM = RawROM.Skip(10).ToArray();

            // Convert the ROM to a ushort array (reversing array order to factor in big-endian)
            ushort[] ROM = new ushort[RawROM.Length / 2];
            for (int i = 0; i < ROM.Length; i++)
            {
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(RawROM, i * 2, 2);
                }
                ROM[i] = BitConverter.ToUInt16(RawROM, i * 2);
            }

            DebugLogROMAsData(ROM);

            vm = new VirtualMachine(ROM);
        }

        /// <summary>
        /// Load the ROM file
        /// </summary>
        /// <param name="ROMfile">The ROM file to load</param>
        /// <returns>The ROM file as a byte array</returns>
        private static byte[] LoadROMFile(string ROMfile) => File.ReadAllBytes(ROMfile);

        private static void DebugLogROMAsData(ushort[] ROM)
        {
            Console.WriteLine("ROM contents: ");
            for (var i = 0; i < ROM.Length; i++)
            {
                // Look up the opcode corresponding to the value
                var opcodes = Opcodes.GetKey(ROM[i]);

                // Convert string array to string with spaces
                string opcode = string.Join(" ", opcodes);

                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"${i:X4}");
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.Write($" {ROM[i]:X2}");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"→");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"{opcode} ");

                // If instruction takes address or value, skip
                Console.ForegroundColor = ConsoleColor.Green;
                switch (opcode)
                {
                    case "LFM":
                    case "WTM":
                    case "JNZ":
                    case "JMP":
                    case "JPZ":
                        // Convert byte array to ushort
                        Console.Write($"{ROM[i + 1]:x4} ({ROM[i + 1]})");
                        i += 1;

                        break;

                    case "SRA":
                    case "DWR":
                    case "DBG_LGC":
                        // Convert byte array to ushort
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write($"({ROM[i + 1]})");
                        i += 1;
                        break;

                    default:
                        Console.ForegroundColor = ConsoleColor.DarkGreen;
                        Console.Write("NOR");
                        break;
                }

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.White;
            }
        }
    }
}