using Antlr4.Runtime;

namespace UniversalFoxAssembler
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            // Check arguments are valid
            if (args.Length != 1)
            {
                Console.WriteLine("Usage:\n\tufox16asm my-assembly-file.ufox16");
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
            if (Path.GetExtension(filePath) != ".ufox16")
            {
                Console.WriteLine("Invalid file extension: " + filePath);
                return;
            }

            // Load the test file
            var input = File.ReadAllText(filePath);

            // Create a lexer and parser
            AntlrInputStream inputStream = new(input);
            UFox16ASMLexer lexer = new(inputStream);
            CommonTokenStream commonTokenStream = new(lexer);
            UFox16ASMParser parser = new(commonTokenStream);

            var context = parser.assembly(); // Parse the input

            // Print the tree to the console
            for (var i = 0; i < context.ChildCount; i++)
            {
                // Print the tree to the console
                var child = context.GetChild(i);
                
                // Print the line
                Console.WriteLine(child.ToStringTree());
            }
        }
    }
}