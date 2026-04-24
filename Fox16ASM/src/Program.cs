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
        static void Main(string[] args)
        {
            string? inputFile = null;
            string outputFile = "vfox16.bin";
            bool showHelp = false;
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
                            return;
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
                            return;
                        }
                        break;
                    case "--tokens":
                        debugFlags.ShowTokens = true;
                        break;
                    case "--labels":
                        debugFlags.ShowLabels = true;
                        break;
                    default:
                        Console.WriteLine($"Error: Unknown argument '{args[i]}'");
                        return;
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
                Console.WriteLine("  --tokens              Show token debug output");
                Console.WriteLine("  --labels              Show label resolution debug output");
                Console.WriteLine("  -h, --help            Show this help message");
                return;
            }

            // Check input file was provided
            if (inputFile is null)
            {
                Console.WriteLine("Error: Input file required");
                Console.WriteLine("Usage: fox16asm -i <file> [-o <rom>]");
                return;
            }

            // Check file exists
            if (!File.Exists(inputFile))
            {
                Console.WriteLine("File not found: " + inputFile);
                return;
            }

            // Check file has valid extension
            if (Path.GetExtension(inputFile) != ".f16")
            {
                Console.WriteLine("Invalid file extension: " + inputFile);
                Console.WriteLine("Expected .f16 extension");
                return;
            }

            // Invoke the preprocessor
            Preprocessor preprocessor = new();
            (string[] lines, Label[] labels) = preprocessor.Process(inputFile, debugFlags);

            // Tokeniser the cleaned and processed lines
            var tokens = Tokeniser.ConvertLinesToTokens(lines, debugFlags);

            // Lexical validation

            // Generation
            Generator.Generate(tokens, labels, outputFile, debugFlags);
        }
    }
}