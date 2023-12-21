using System.IO;

namespace Fox16ASM
{
    class Program
    {
        static void Main(string[] args)
        {
            // Check arguments are valid
            if (args.Length != 1)
            {
                Console.WriteLine("Usage:\n\tfox16asm my-assembly-file.fox16");
                return;
            }
            string filePath = args[0];

            // Check file exists
            if (!File.Exists(filePath))
            {
                Console.WriteLine("File not found: " + filePath);
                return;
            }

            // Check file has valid extension
            if (Path.GetExtension(filePath) != ".fox16")
            {
                Console.WriteLine("Invalid file extension: " + filePath);
                return;
            }

            // Invoke the preprocessor
            Preprocessor preprocessor = new Preprocessor();
            string[] lines = preprocessor.Process(filePath);

            // Tokeniser the cleaned and processed lines
            Tokeniser tokeniser = new Tokeniser();
            var tokens = tokeniser.ConvertLinesToTokens(lines);

            // Lexical validation
            // Remove terminating tokens

            // Generation
            Generator generator = new Generator();
            generator.Generate(tokens, "fox16.bin");
        }
    }
}