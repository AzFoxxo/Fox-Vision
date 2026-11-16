using System.IO;

namespace Fox16ASM
{
    class DebugFlags
    {
        public bool ShowTokens { get; set; } = false;
        public bool ShowLabels { get; set; } = false;
    }

    class Program
    {
        static int Main(string[] args)
        {
            string? inputFile = null;
            string outputFile = "vfox16.bin";
            bool showHelp = false;
            bool strictFormat = false;
            uint loadAddress = 0;
            byte? romVersion = null;
            ushort? mapper = null;
            var mode = AssemblerMode.Legacy;
            var debugFlags = new DebugFlags();

            // Parse command-line arguments
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-h":
                    case "--help":
                        showHelp = true;
                        break;
                    case "-i":
                    case "--input":
                        if (i + 1 < args.Length)
                        {
                            inputFile = args[++i];
                        }
                        else
                        {
                            Console.WriteLine("Error: -i/--input requires a file argument");
                            return 1;
                        }
                        break;
                    case "-o":
                    case "--output":
                        if (i + 1 < args.Length)
                        {
                            outputFile = args[++i];
                        }
                        else
                        {
                            Console.WriteLine("Error: -o/--output requires a file argument");
                            return 1;
                        }
                        break;
                    case "--tokens":
                        debugFlags.ShowTokens = true;
                        break;
                    case "--obj":
                        // recognized here; actual handling occurs after generation
                        break;
                    case "--labels":
                        debugFlags.ShowLabels = true;
                        break;
                    case "--strict-format":
                        strictFormat = true;
                        break;
                    case "--load-address":
                        if (i + 1 >= args.Length || !TryParseAddress(args[++i], out loadAddress))
                        {
                            Console.WriteLine("Error: --load-address requires a decimal or hexadecimal address");
                            return 1;
                        }
                        break;
                    case "--rom-version":
                        if (i + 1 >= args.Length || !byte.TryParse(args[++i], out var parsedVer) || parsedVer < 1 || parsedVer > 3)
                        {
                            Console.WriteLine("Error: --rom-version requires 1, 2, or 3");
                            return 1;
                        }
                        romVersion = parsedVer;
                        break;
                    case "--mapper":
                        if (i + 1 >= args.Length)
                        {
                            Console.WriteLine("Error: --mapper requires 0 (ROM4K), 1 (ROM32K), or 2 (ROMByte8_32)");
                            return 1;
                        }
                        var mapperArg = args[++i];
                        if (mapperArg.Equals("rom4k", StringComparison.OrdinalIgnoreCase) || mapperArg == "0")
                            mapper = 0;
                        else if (mapperArg.Equals("rom32k", StringComparison.OrdinalIgnoreCase) || mapperArg == "1")
                            mapper = 1;
                        else if (mapperArg.Equals("rombyte8_32", StringComparison.OrdinalIgnoreCase) || mapperArg.Equals("byte8_32", StringComparison.OrdinalIgnoreCase) || mapperArg == "2")
                            mapper = 2;
                        else
                        {
                            Console.WriteLine($"Error: Unknown mapper '{mapperArg}'. Expected rom4k (0), rom32k (1), or rombyte8_32 (2)");
                            return 1;
                        }
                        break;
                    case "--mode":
                        if (i + 1 >= args.Length)
                        {
                            Console.WriteLine("Error: --mode requires a value of 'legacy', 'extended', or 'em2_am1'");
                            return 1;
                        }

                        var modeValue = args[++i];
                        if (modeValue.Equals("legacy", StringComparison.OrdinalIgnoreCase))
                        {
                            mode = AssemblerMode.Legacy;
                        }
                        else if (modeValue.Equals("extended", StringComparison.OrdinalIgnoreCase))
                        {
                            mode = AssemblerMode.Extended;
                        }
                        else if (modeValue.Equals("extended2", StringComparison.OrdinalIgnoreCase)
                            || modeValue.Equals("em2_am1", StringComparison.OrdinalIgnoreCase)
                            || modeValue.Equals("am1_em2", StringComparison.OrdinalIgnoreCase)
                            || modeValue.Equals("em2", StringComparison.OrdinalIgnoreCase)
                            || modeValue.Equals("v2", StringComparison.OrdinalIgnoreCase))
                        {
                            mode = AssemblerMode.Extended2;
                        }
                        else
                        {
                            Console.WriteLine($"Error: Unknown assembler mode '{modeValue}'");
                            return 1;
                        }

                        break;
                    default:
                        Console.WriteLine($"Error: Unknown argument '{args[i]}'");
                        return 1;
                }
            }

            // Show help if requested
            if (showHelp)
            {
                Console.WriteLine("Fox16 Assembler");
                Console.WriteLine("Usage: fox16asm [options]");
                Console.WriteLine("Options:");
                Console.WriteLine("  -i, --input <file>    Input assembly file (.f16)");
                Console.WriteLine("  -o, --output <rom>    Output ROM file (default: vfox16.bin)");
                Console.WriteLine("  --mode <legacy|extended>  Select machine mode (default: legacy)");
                Console.WriteLine("  --tokens              Show token debug output");
                Console.WriteLine("  --labels              Show label resolution debug output");
                Console.WriteLine("  --strict-format       Enforce ROM payload limit for the selected mode");
                Console.WriteLine("  --load-address <addr> Load the image at a non-zero address");
                Console.WriteLine("  --rom-version <1|2|3> Select extended ROM container version (default: 2 for extended, 3 for extended2/em2_am1)");
                Console.WriteLine("  --mapper <name|id>    Select container mapper: rom4k (0), rom32k (1), rombyte8_32 (2)");
                Console.WriteLine("  -h, --help            Show this help message");
                return 0;
            }

            // Check input file was provided
            if (inputFile is null)
            {
                Console.WriteLine("Error: Input file required");
                Console.WriteLine("Usage: fox16asm -i <file> [-o <rom>]");
                return 1;
            }

            // Check file exists
            if (!File.Exists(inputFile))
            {
                Console.WriteLine("File not found: " + inputFile);
                return 1;
            }

            // Check file has valid extension
            if (Path.GetExtension(inputFile) != ".f16")
            {
                Console.WriteLine("Invalid file extension: " + inputFile);
                Console.WriteLine("Expected .f16 extension");
                return 1;
            }

            var preprocessor = new Preprocessor();
            var preprocessed = preprocessor.Process(inputFile, debugFlags);
            if (!preprocessed.Success || preprocessed.Value is null)
            {
                DiagnosticPrinter.Print(preprocessed.Diagnostics);
                return 1;
            }

            var irBuilder = new IRBuilder();
            var irResult = irBuilder.Build(preprocessed.Value, inputFile, mode, debugFlags);
            if (!irResult.Success)
            {
                DiagnosticPrinter.Print(irResult.Diagnostics);
                return 1;
            }

            bool emitObj = false;
            // detect --obj presence by scanning args (cheap)
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--obj") { emitObj = true; break; }
            }

            var generationResult = Generator.Generate(irResult.Value, outputFile, inputFile, mode, debugFlags, strictFormat, loadAddress, romVersion, mapper);
            if (!generationResult.Success)
            {
                DiagnosticPrinter.Print(generationResult.Diagnostics);
                return 1;
            }

            var genOut = generationResult.Value;

            if (emitObj)
            {
                // emit a VF16Obj file directly
                VF16ObjWriter.WriteObject(outputFile, genOut.Bytes, genOut.Labels);
                Console.WriteLine($"Object file written to {outputFile}");
                return 0;
            }

            // Otherwise, create an in-memory VF16Object and call the linker
            var vfObj = new VF16Linker.VF16Object();
            vfObj.Version = 1;
            var textSection = new VF16Linker.Section { Name = ".text", Flags = 1, Size = (uint)genOut.Bytes.Length, Data = genOut.Bytes };
            vfObj.Sections.Add(textSection);
            // add symbols from labels
            foreach (var kv in genOut.Labels)
            {
                var sym = new VF16Linker.Symbol { Name = kv.Key, Flags = 1, SectionIndex = 0, Offset = kv.Value };
                vfObj.Symbols.Add(sym);
            }
            // no relocations for flat output
            VF16Linker.Linker.Link(new System.Collections.Generic.List<VF16Linker.VF16Object> { vfObj }, outputFile);
            Console.WriteLine($"Wrote linked binary to {outputFile}");

            return 0;
        }

        private static bool TryParseAddress(string value, out uint address)
        {
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(value[2..], System.Globalization.NumberStyles.AllowHexSpecifier, null, out address);

            if (value.StartsWith("$", StringComparison.Ordinal))
                return uint.TryParse(value[1..], System.Globalization.NumberStyles.AllowHexSpecifier, null, out address);

            return uint.TryParse(value, out address);
        }
    }
}