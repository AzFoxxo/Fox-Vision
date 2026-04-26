namespace Fox16ASM;

static class DiagnosticPrinter
{
    public static void Print(IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            Console.ForegroundColor = diagnostic.Severity == DiagnosticSeverity.Error
                ? ConsoleColor.Red
                : ConsoleColor.Yellow;
            Console.Write(diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($": {diagnostic}");

            if (string.IsNullOrWhiteSpace(diagnostic.SourceLine))
                continue;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  {diagnostic.SourceLine}");
            if (diagnostic.Column > 0)
            {
                Console.WriteLine($"  {new string(' ', diagnostic.Column - 1)}^");
            }
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}
