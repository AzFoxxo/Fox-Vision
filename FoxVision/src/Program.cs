using System.Diagnostics;
using System.Reflection;
using Fox16Shared;

namespace FoxVision
{
    public class Program()
    {
        private const int RomHeaderLength = 10;
        private const byte OperandTypeRegister = 0b00;
        private const byte OperandTypeImmediate = 0b01;
        private const byte OperandTypeDirectMemory = 0b10;
        private const byte OperandTypeIndirectMemory = 0b11;
        private static VirtualMachine? vm;

        public static void Main(string[] args)
        {
            var options = new EmulatorOptions();
            if (!TryApplyCommandLineArguments(args, options))
                return;

            ushort[] ROM;

            if (!string.IsNullOrWhiteSpace(options.RomPath))
            {
                if (!TryLoadROMFile(options.RomPath, out var rawRom))
                {
                    return;
                }

                if (!TryDecodeRom(rawRom, out ROM))
                {
                    return;
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No ROM selected at startup. Use File -> Load ROM.");
                Console.ForegroundColor = ConsoleColor.White;
                ROM = [0x0000, 0x000E];
            }

            if (ROM.Length <= 512)
            {
                DebugLogROMAsData(ROM);
            }
            else
            {
                Console.WriteLine($"ROM size: {ROM.Length} words (disassembly preview skipped for performance)");
            }

            vm = new VirtualMachine(ROM, options);
        }

        internal static bool TryLaunchRomProcess(string romPath, EmulatorOptions options)
        {
            if (string.IsNullOrWhiteSpace(romPath))
            {
                return false;
            }

            try
            {
                var processPath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(processPath))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Unable to determine the current process path.");
                    Console.ForegroundColor = ConsoleColor.White;
                    return false;
                }

                string? entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
                bool runningUnderDotnetHost = Path.GetFileName(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);

                var startInfo = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    WorkingDirectory = Environment.CurrentDirectory,
                    FileName = processPath
                };

                if (runningUnderDotnetHost && !string.IsNullOrWhiteSpace(entryAssemblyPath))
                {
                    startInfo.ArgumentList.Add(entryAssemblyPath);
                }

                startInfo.ArgumentList.Add("--rom");
                startInfo.ArgumentList.Add(romPath);

                startInfo.EnvironmentVariables["FOXVISION_ROM_PATH"] = romPath;
                startInfo.EnvironmentVariables["FOXVISION_WINDOW_SCALE"] = options.WindowScale.ToString();
                startInfo.EnvironmentVariables["FOXVISION_TARGET_FPS"] = options.TargetFps.ToString();
                startInfo.EnvironmentVariables["FOXVISION_EXECUTION_SPEED_HZ"] = options.ExecutionSpeedHz.ToString();
                startInfo.EnvironmentVariables["FOXVISION_LOG_INSTRUCTION"] = options.LogInstruction.ToString();
                startInfo.EnvironmentVariables["FOXVISION_ROM_PREVIEW_WORDS"] = options.RomPreviewWords.ToString();
                startInfo.EnvironmentVariables["FOXVISION_CONTROLLER_UP_KEY"] = options.ControllerUpKey.ToString();
                startInfo.EnvironmentVariables["FOXVISION_CONTROLLER_DOWN_KEY"] = options.ControllerDownKey.ToString();
                startInfo.EnvironmentVariables["FOXVISION_CONTROLLER_LEFT_KEY"] = options.ControllerLeftKey.ToString();
                startInfo.EnvironmentVariables["FOXVISION_CONTROLLER_RIGHT_KEY"] = options.ControllerRightKey.ToString();
                startInfo.EnvironmentVariables["FOXVISION_CONTROLLER_A_KEY"] = options.ControllerAKey.ToString();
                startInfo.EnvironmentVariables["FOXVISION_CONTROLLER_B_KEY"] = options.ControllerBKey.ToString();
                startInfo.EnvironmentVariables["FOXVISION_CONTROLLER_START_KEY"] = options.ControllerStartKey.ToString();
                startInfo.EnvironmentVariables["FOXVISION_CONTROLLER_SELECT_KEY"] = options.ControllerSelectKey.ToString();

                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to launch ROM '{romPath}': {ex.Message}");
                Console.ForegroundColor = ConsoleColor.White;
                return false;
            }
        }

        internal static bool TryBuildAndLaunchRomProcess(string sourcePath, EmulatorOptions options)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return false;
            }

            if (!File.Exists(sourcePath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Source file not found: {sourcePath}");
                Console.ForegroundColor = ConsoleColor.White;
                return false;
            }

            string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (!string.Equals(extension, ".f16", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".fc", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Unsupported source extension: {extension}");
                Console.WriteLine("Expected .f16 assembly or .fc FoxC source file");
                Console.ForegroundColor = ConsoleColor.White;
                return false;
            }

            var assemblerProjectPath = FindFileUpwards("Fox16ASM/Fox16ASM.csproj", Environment.CurrentDirectory);
            if (string.IsNullOrWhiteSpace(assemblerProjectPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Could not locate Fox16ASM/Fox16ASM.csproj from current directory.");
                Console.ForegroundColor = ConsoleColor.White;
                return false;
            }

            var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            if (string.IsNullOrWhiteSpace(sourceDirectory))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Could not determine source directory for: {sourcePath}");
                Console.ForegroundColor = ConsoleColor.White;
                return false;
            }

            try
            {
                var assemblySourcePath = sourcePath;
                if (string.Equals(extension, ".fc", StringComparison.OrdinalIgnoreCase))
                {
                    var foxCRoot = FindFileUpwards("FoxC/go.mod", Environment.CurrentDirectory);
                    if (string.IsNullOrWhiteSpace(foxCRoot))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Could not locate FoxC/go.mod from current directory.");
                        Console.ForegroundColor = ConsoleColor.White;
                        return false;
                    }

                    foxCRoot = Path.GetDirectoryName(foxCRoot);
                    if (string.IsNullOrWhiteSpace(foxCRoot))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Could not determine FoxC project directory.");
                        Console.ForegroundColor = ConsoleColor.White;
                        return false;
                    }

                    assemblySourcePath = Path.Combine(
                        sourceDirectory,
                        Path.GetFileNameWithoutExtension(sourcePath) + ".f16");

                    var foxcStartInfo = new ProcessStartInfo
                    {
                        FileName = "go",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = foxCRoot
                    };

                    foxcStartInfo.ArgumentList.Add("run");
                    foxcStartInfo.ArgumentList.Add("./cmd/foxc");
                    foxcStartInfo.ArgumentList.Add("-i");
                    foxcStartInfo.ArgumentList.Add(sourcePath);
                    foxcStartInfo.ArgumentList.Add("-o");
                    foxcStartInfo.ArgumentList.Add(assemblySourcePath);

                    using var foxcProcess = Process.Start(foxcStartInfo);
                    if (foxcProcess is null)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Failed to start FoxC compiler process.");
                        Console.ForegroundColor = ConsoleColor.White;
                        return false;
                    }

                    string foxcStdOut = foxcProcess.StandardOutput.ReadToEnd();
                    string foxcStdErr = foxcProcess.StandardError.ReadToEnd();
                    foxcProcess.WaitForExit();

                    if (!string.IsNullOrWhiteSpace(foxcStdOut))
                    {
                        Console.WriteLine(foxcStdOut);
                    }

                    if (foxcProcess.ExitCode != 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("FoxC compile failed.");
                        if (!string.IsNullOrWhiteSpace(foxcStdErr))
                        {
                            Console.WriteLine(foxcStdErr);
                        }
                        Console.ForegroundColor = ConsoleColor.White;
                        return false;
                    }

                    if (!File.Exists(assemblySourcePath))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"FoxC completed but assembly was not found: {assemblySourcePath}");
                        Console.ForegroundColor = ConsoleColor.White;
                        return false;
                    }
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = sourceDirectory
                };

                startInfo.ArgumentList.Add("run");
                startInfo.ArgumentList.Add("--project");
                startInfo.ArgumentList.Add(assemblerProjectPath);
                startInfo.ArgumentList.Add("--");
                startInfo.ArgumentList.Add("-i");
                startInfo.ArgumentList.Add(assemblySourcePath);

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Failed to start assembler process.");
                    Console.ForegroundColor = ConsoleColor.White;
                    return false;
                }

                string stdOut = process.StandardOutput.ReadToEnd();
                string stdErr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrWhiteSpace(stdOut))
                {
                    Console.WriteLine(stdOut);
                }

                if (process.ExitCode != 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("ROM build failed.");
                    if (!string.IsNullOrWhiteSpace(stdErr))
                    {
                        Console.WriteLine(stdErr);
                    }
                    Console.ForegroundColor = ConsoleColor.White;
                    return false;
                }

                var builtRomPath = Path.Combine(sourceDirectory, "vfox16.bin");
                if (!File.Exists(builtRomPath))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Assembler completed but ROM was not found: {builtRomPath}");
                    Console.ForegroundColor = ConsoleColor.White;
                    return false;
                }

                var updatedOptions = new EmulatorOptions
                {
                    RomPath = builtRomPath,
                    WindowScale = options.WindowScale,
                    TargetFps = options.TargetFps,
                    ExecutionSpeedHz = options.ExecutionSpeedHz,
                    LogInstruction = options.LogInstruction,
                    RomPreviewWords = options.RomPreviewWords,
                    ControllerUpKey = options.ControllerUpKey,
                    ControllerDownKey = options.ControllerDownKey,
                    ControllerLeftKey = options.ControllerLeftKey,
                    ControllerRightKey = options.ControllerRightKey,
                    ControllerAKey = options.ControllerAKey,
                    ControllerBKey = options.ControllerBKey,
                    ControllerStartKey = options.ControllerStartKey,
                    ControllerSelectKey = options.ControllerSelectKey
                };

                return TryLaunchRomProcess(updatedOptions.RomPath, updatedOptions);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to build ROM from source '{sourcePath}': {ex.Message}");
                Console.ForegroundColor = ConsoleColor.White;
                return false;
            }
        }

        private static string? FindFileUpwards(string relativePath, string startDirectory)
        {
            var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
            while (current is not null)
            {
                var candidate = Path.Combine(current.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }

            return null;
        }

        private static bool TryApplyCommandLineArguments(string[] args, EmulatorOptions options)
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-r":
                    case "--rom":
                        if (i + 1 >= args.Length)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("Missing value for --rom option.");
                            Console.ForegroundColor = ConsoleColor.White;
                            return false;
                        }

                        options.RomPath = args[++i];
                        break;

                    case "-h":
                    case "--help":
                        PrintUsage();
                        return false;

                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Unknown argument: {args[i]}");
                        Console.ForegroundColor = ConsoleColor.White;
                        PrintUsage();
                        return false;
                }
            }

            return true;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("FoxVision options:");
            Console.WriteLine("  -r, --rom <path>    Load ROM binary at startup");
            Console.WriteLine("  -h, --help          Show this help message");
        }

        private static bool TryDecodeRom(byte[] rawRom, out ushort[] rom)
        {
            rom = [];

            if (rawRom.Length < RomHeaderLength)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"ROM file is too small. Expected at least {RomHeaderLength} bytes, got {rawRom.Length}.");
                Console.ForegroundColor = ConsoleColor.White;
                return false;
            }

            Console.Write("10 byte identifier: ");
            Console.ForegroundColor = ConsoleColor.Blue;
            for (int i = 0; i < RomHeaderLength; i++)
            {
                Console.Write((char)rawRom[i]);
            }
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine();

            rawRom = rawRom.Skip(RomHeaderLength).ToArray();

            if (rawRom.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("ROM payload is empty after header.");
                Console.ForegroundColor = ConsoleColor.White;
                return false;
            }

            if (rawRom.Length % 2 != 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("ROM payload has odd byte count. Padding one trailing zero byte.");
                Console.ForegroundColor = ConsoleColor.White;
                Array.Resize(ref rawRom, rawRom.Length + 1);
            }

            rom = new ushort[rawRom.Length / 2];
            for (int i = 0; i < rom.Length; i++)
            {
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(rawRom, i * 2, 2);
                }
                rom[i] = BitConverter.ToUInt16(rawRom, i * 2);
            }

            return true;
        }

        /// <summary>
        /// Load the ROM file
        /// </summary>
        /// <param name="ROMfile">The ROM file to load</param>
        /// <returns>The ROM file as a byte array</returns>
        private static bool TryLoadROMFile(string ROMfile, out byte[] rom)
        {
            rom = [];

            if (!File.Exists(ROMfile))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"ROM file not found: {ROMfile}");
                Console.WriteLine("Run the assembler first, e.g. dotnet run --project Fox16ASM/Fox16ASM.csproj test/graphics_test.fox16");
                Console.ForegroundColor = ConsoleColor.White;
                return false;
            }

            try
            {
                rom = File.ReadAllBytes(ROMfile);
                return true;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to read ROM file: {ROMfile}");
                Console.WriteLine(ex.Message);
                Console.ForegroundColor = ConsoleColor.White;
                return false;
            }
        }



        internal static void DebugLogROMAsData(ushort[] ROM)
        {
            Console.WriteLine("ROM contents: ");
            for (var i = 0; i < ROM.Length; i++)
            {
                ushort opcodeWord = ROM[i];
                ushort opcodeValue = DecodeDisplayOpcodeValue(opcodeWord);
                string opcode = GetDisplayOpcode(opcodeValue);

                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"${i:X4}");
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.Write($" {opcodeWord:X4}");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"→");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"{opcode} ");

                // If instruction takes address or value, skip
                Console.ForegroundColor = ConsoleColor.Green;
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
                        // Convert byte array to ushort
                        if (i + 1 < ROM.Length)
                        {
                            Console.Write($"{ROM[i + 1]:X4} ({ROM[i + 1]})");
                            i += 1;
                        }
                        else
                        {
                            Console.Write("<missing operand>");
                        }

                        break;

                    case 0x3:
                    case 0x14:
                    case Opcodes.DEBUG_EXTENSION_OFFSET:
                        // Convert byte array to ushort
                        if (i + 1 < ROM.Length)
                        {
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.Write($"({ROM[i + 1]})");
                            i += 1;
                        }
                        else
                        {
                            Console.Write("<missing operand>");
                        }
                        break;

                    case 0x2C:
                    case 0x2D:
                        if (i + 1 < ROM.Length)
                        {
                            Console.Write($"[{BuildOperandControlSummary(opcodeWord)}] ");

                            string firstOperand = BuildTypedOperandDisplay(opcodeWord, ROM[i + 1], operandIndex: 0);
                            Console.Write($"OP1={firstOperand}");
                            i += 1;
                        }
                        else
                        {
                            Console.Write("<missing operand>");
                        }
                        break;

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
                        if (i + 2 < ROM.Length)
                        {
                            Console.Write($"[{BuildOperandControlSummary(opcodeWord)}] ");

                            string firstOperand = BuildTypedOperandDisplay(opcodeWord, ROM[i + 1], operandIndex: 0);
                            string secondOperand = BuildTypedOperandDisplay(opcodeWord, ROM[i + 2], operandIndex: 1);
                            Console.Write($"OP1={firstOperand} OP2={secondOperand}");
                            i += 2;
                        }
                        else
                        {
                            Console.Write("<missing operands>");
                        }
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
            string operandOneType = GetOperandTypeLabel(DecodeOperandType(opcodeWord, operandIndex: 0));
            string operandTwoType = GetOperandTypeLabel(DecodeOperandType(opcodeWord, operandIndex: 1));
            return $"CNT={operandCount} OP1={operandOneType} OP2={operandTwoType}";
        }

        private static string BuildTypedOperandDisplay(ushort opcodeWord, ushort operandValue, int operandIndex)
        {
            byte operandType = DecodeOperandType(opcodeWord, operandIndex);
            return operandType switch
            {
                OperandTypeRegister => $"REG:{FormatRegisterOrRaw(operandValue)}",
                OperandTypeImmediate => $"IMM:{operandValue:X4}({operandValue})",
                OperandTypeDirectMemory => $"MEM[{operandValue:X4}]",
                OperandTypeIndirectMemory => $"MEM[{FormatRegisterOrRaw(operandValue)}]",
                _ => $"RAW:{operandValue:X4}"
            };
        }

        private static string GetOperandTypeLabel(byte operandType)
            => operandType switch
            {
                OperandTypeRegister => "REG",
                OperandTypeImmediate => "IMM",
                OperandTypeDirectMemory => "MEM",
                OperandTypeIndirectMemory => "IND",
                _ => "UNK"
            };

        private static string FormatRegisterOrRaw(ushort value)
            => value switch
            {
                0x0000 => "X",
                0x0001 => "Y",
                0x0003 => "STATUS",
                0x0004 => "SP",
                _ => $"${value:X4}"
            };

        private static byte DecodeOperandType(ushort opcodeWord, int operandIndex)
        {
            if (opcodeWord <= 0x00FF || opcodeWord >= Opcodes.DEBUG_EXTENSION_OFFSET)
                return OperandTypeRegister;

            return operandIndex switch
            {
                0 => (byte)((opcodeWord >> 2) & 0b11),
                1 => (byte)((opcodeWord >> 4) & 0b11),
                _ => OperandTypeImmediate
            };
        }

        private static string GetDisplayOpcode(ushort opcodeValue)
        {
            var opcodes = Opcodes.GetKey(opcodeValue);
            if (opcodes.Length == 0)
            {
                return "???";
            }

            // Prefer canonical debug mnemonics over legacy aliases.
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