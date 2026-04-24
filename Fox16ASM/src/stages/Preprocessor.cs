using System.IO;

namespace Fox16ASM
{
    class Preprocessor
    {
        List<Label> labels = [];

        /// <summary>
        /// Run the preprocessor
        /// </summary>
        /// <param name="filename">File to process</param>
        /// <param name="debugFlags">Debug output flags</param>
        /// <returns>Returns a tuple of the cleaned lines and labels</return>
        public Tuple<string[], Label[]> Process(string filename, DebugFlags debugFlags = null)
        {
            debugFlags = debugFlags ?? new DebugFlags();
            // Remove whitespace and comments from the file
            var lines = RemoveCommentsAndWhitespace(filename);

            // Apply EFox16 preprocessor (now standard for all .f16 files)
            // Check if any line has @const directive
            if (lines.Any(line => line.StartsWith("@const")))
            {
                // Find all @const directives
                var constLines = lines.Where(line => line.StartsWith("@const")).ToArray();

                // Split at space and error handling
                foreach (var line in constLines)
                {
                    var parts = line.Split(' ');
                    if (parts.Length != 3)
                    {
                        Console.WriteLine("Invalid @const directive: " + line);
                        continue;
                    }

                    // Get the name and value
                    var name = parts[1];
                    var value = parts[2];

                    // Replace all instances of name with value
                    lines = lines.Select(line => line.Replace($"<{name}>", value)).ToArray();
                }
            }

            // Delete all preprocessor lines
            lines = lines.Where(line => !line.StartsWith('@')).ToArray();

            // Print all lines (if debug flag is enabled)
            if (debugFlags.ShowTokens)
            {
                foreach (var line in lines)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(line);
                }
            }

            // Find all labels in the file
            labels = FindLabels(lines, debugFlags);

            return new Tuple<string[], Label[]>(lines, [.. labels]);
        }

        /// <summary>
        /// Strip all comments and whitespace from file
        /// </summary>
        /// <param name="filename">File to process</param>
        /// <returns>string representation of cleaned file</returns>
        private static string[] RemoveCommentsAndWhitespace(string filename)
        {
            // Read the file
            var lines = File.ReadAllLines(filename);

            // Discard any empty lines
            lines = lines.Where(line => !string.IsNullOrEmpty(line)).ToArray();

            // Discard any lines starting with ; even if space before
            lines = lines.Where(line => !line.Trim().StartsWith(";")).ToArray();

            // Clear post instruction comments
            lines = lines.Select(line => line.Split(';')[0].Trim()).ToArray();

            return lines;
        }

        /// <summary>
        /// Return all label declarations in the lines provided
        /// </summary>
        /// <param name="lines">source code lines</param>
        /// <param name="debugFlags">debug flags</param>
        /// <returns>list of label declarations found</returns>
        private static List<Label> FindLabels(string[] lines, DebugFlags debugFlags)
        {
            List<Label> labels = [];
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().StartsWith(':'))
                {
                    // Store the name and discard the character letter `:`
                    var name = lines[i].Trim()[1..];

                    // Get the current line number (factoring in labels)
                    ushort lineNumber = (ushort)(i - labels.Count);

                    // Print label in red (if debug flag is enabled)
                    if (debugFlags.ShowLabels)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Write(name);
                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.Write(" " + lineNumber);
                        Console.WriteLine();
                    }

                    // Append the label to the list
                    labels.Add(new Label(lineNumber, name));
                }
            }
            ;

            return labels;
        }
    }
}