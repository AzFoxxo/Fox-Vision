using System.Diagnostics;
using System.Reflection;
using Fox16Shared;

namespace FoxVision
{
    public class Program()
    {
        private const int RomHeaderLength = 10;
        private static VirtualMachine? vm;

        public static void Main(string[] args)
        {
            var options = new EmulatorOptions();
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
                Console.WriteLine($"Assembly source file not found: {sourcePath}");
                Console.ForegroundColor = ConsoleColor.White;
                return false;
            }

            string extension = Path.GetExtension(sourcePath);
            if (!string.Equals(extension, ".f16", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Unsupported source extension: {extension}");
                Console.WriteLine("Expected .f16 assembly file");
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
                startInfo.ArgumentList.Add(sourcePath);

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
                ushort opcodeValue = ROM[i];
                string opcode = GetDisplayOpcode(opcodeValue);

                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"${i:X4}");
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.Write($" {ROM[i]:X4}");
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

                    case 0x19:
                    case 0x1A:
                    case 0x1B:
                        if (i + 2 < ROM.Length)
                        {
                            Console.Write($"{ROM[i + 1]:X4} {ROM[i + 2]:X4}");
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
                _ => opcodes[0]
            };
        }
    }
}