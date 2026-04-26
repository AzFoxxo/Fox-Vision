using System.IO;

namespace Fox16ASM;

class Preprocessor
{
    public CompilationResult<SourceLine[]> Process(string filename, DebugFlags? debugFlags = null)
    {
        debugFlags ??= new DebugFlags();
        var diagnostics = new List<Diagnostic>();

        var linesResult = RemoveCommentsAndWhitespace(filename);
        diagnostics.AddRange(linesResult.Diagnostics);
        if (!linesResult.Success || linesResult.Value is null)
            return CompilationResult<SourceLine[]>.Failed(diagnostics);

        var lines = linesResult.Value.ToList();
        var constants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceLine in lines)
        {
            if (!sourceLine.Text.StartsWith("@const", StringComparison.Ordinal))
                continue;

            var parts = sourceLine.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
            {
                diagnostics.Add(Diagnostic.Error(
                    "Invalid @const directive. Expected '@const <name> <value>'.",
                    filename,
                    sourceLine.LineNumber,
                    1,
                    sourceLine.Text));
                continue;
            }

            var name = parts[1];
            var value = parts[2];
            constants[name] = value;
        }

        var processed = new List<SourceLine>(lines.Count);
        foreach (var sourceLine in lines)
        {
            if (sourceLine.Text.StartsWith('@'))
                continue;

            var text = sourceLine.Text;
            foreach (var kvp in constants)
            {
                text = text.Replace($"<{kvp.Key}>", kvp.Value, StringComparison.Ordinal);
            }

            processed.Add(new SourceLine(sourceLine.LineNumber, text));
        }

        if (debugFlags.ShowTokens)
        {
            foreach (var line in processed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(line.Text);
            }

            Console.ForegroundColor = ConsoleColor.White;
        }

        return diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
            ? CompilationResult<SourceLine[]>.Failed(diagnostics)
            : new CompilationResult<SourceLine[]>(processed.ToArray(), diagnostics);
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