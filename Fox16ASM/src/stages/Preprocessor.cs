using System.IO;

namespace Fox16ASM;

class Preprocessor
{
    public CompilationResult<SourceLine[]> Process(string filename, DebugFlags? debugFlags = null)
    {
        debugFlags ??= new DebugFlags();
        var linesResult = RemoveCommentsAndWhitespace(filename);
        if (!linesResult.Success || linesResult.Value is null)
            return CompilationResult<SourceLine[]>.Failed(linesResult.Diagnostics);

        var processed = linesResult.Value;

        if (debugFlags.ShowTokens)
        {
            foreach (var line in processed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(line.Text);
            }

            Console.ForegroundColor = ConsoleColor.White;
        }

        return new CompilationResult<SourceLine[]>(processed, linesResult.Diagnostics);
    }

    private static CompilationResult<SourceLine[]> RemoveCommentsAndWhitespace(string filename)
    {
        try
        {
            var cleaned = new List<SourceLine>();
            var lines = File.ReadAllLines(filename);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var commentIndex = line.IndexOf(';');
                if (commentIndex >= 0)
                    line = line[..commentIndex];

                line = line.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                cleaned.Add(new SourceLine(i + 1, line));
            }

            return CompilationResult<SourceLine[]>.Ok(cleaned.ToArray());
        }
        catch (Exception ex)
        {
            return CompilationResult<SourceLine[]>.Failed(Diagnostic.Error($"Unable to read source file: {ex.Message}", filename));
        }
    }
}