using System.IO;

namespace Fox16ASM
{
    class Preprocessor
    {
        /// <summary>
        /// Run the preprocessor
        /// </summary>
        /// <param name="filename">File to process</param>
        /// <returns>string representation of the file as a string array</return>
        public string[] Process(string filename)
        {
            // Remove whitespace and comments from the file
            var lines = RemoveCommentsAndWhitespace(filename);

            // Find all labels in the file
            List<Label> labels = new();
            (labels, lines) = FindLabels(lines);

            // Resolve the labels
            LabelResolution(lines, labels); // TODO: Possible null value?

            foreach (var line in lines)
            {
                Console.WriteLine(line);
            }

            return lines;
        }

        /// <summary>
        /// Strip all comments and whitespace from file
        /// </summary>
        /// <param name="filename">File to process</param>
        /// <returns>string representation of cleaned file</returns>
        private string[] RemoveCommentsAndWhitespace(string filename)
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
        /// Finds all labels in the lines provided and returns a list of Labels and cleaned up lines without label declarations
        /// </summary>
        /// <param name="lines"></param>
        /// <returns></returns>
        private static Tuple<List<Label>, string[]> FindLabels(string[] lines)
        {
            List<Label> labels = new();
            for (int i = 0; i < lines.Length - 1; i++)
            {
                if (lines[i].Trim().StartsWith(":"))
                {
                    // Store the name and discard the character letter `:`
                    var name = lines[i].Trim()[1..];

                    // Get the current line number (factoring in labels)
                    byte lineNumber = ((byte)(i - labels.Count));

                    // Print label in red
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write(name);
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.Write(" " + lineNumber);
                    Console.WriteLine();

                    // Append the label to the list
                    labels.Add(new Label(lineNumber, name));
                }
            };

            // Delete label declarations
            lines = lines.Where(line => !line.Trim().StartsWith(":")).ToArray();

            return new Tuple<List<Label>, string[]>(labels, lines);
        }

        /// <summary>
        /// Label resolution: replaces all labels with the address
        /// </summary>
        /// <param name="lines"></param>
        /// <param name="labels"></param>
        /// <returns>All lines with labels replaced</returns>
        private static string[]? LabelResolution(string[] lines, List<Label> labels)
        {
            // Loop through all instructions
            for (int i = 0; i < lines.Length; i++)
            {
                // If line begins with a jump instruction find and replace the label with the direct address
                if (lines[i].Trim().StartsWith("JPZ") || lines[i].Trim().StartsWith("JNZ") || lines[i].Trim().StartsWith("JPL"))
                {
                    // Print until the space in yellow
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write(lines[i].Split(' ')[0] + " ");
                    Console.ResetColor();

                    // Check if there is a label after the instruction
                    string[] parts = lines[i].Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1)
                    {
                        // Print the label in blue
                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.WriteLine(parts[1]);
                        Console.ResetColor();

                        // Replace the label with the direct address in the line
                        Label label = labels.Find(l => l.name == parts[1]);

                        // Check the label isn't null
                        if (label.name == null)
                        {
                            // Invalid label name
                            Console.WriteLine("Error: Invalid label name");
                            return null;
                        }

                        // Replace label
                        lines[i] = lines[i].Replace(parts[1], "$" + label.address.ToString("X"));
                        Console.WriteLine($"Now: {lines[i]}");
                    }
                }
            }
            
            return lines;
        }
    }
}