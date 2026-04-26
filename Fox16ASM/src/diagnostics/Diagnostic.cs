namespace Fox16ASM;

enum DiagnosticSeverity
{
    Error,
    Warning,
}

readonly record struct Diagnostic(
    DiagnosticSeverity Severity,
    string Message,
    string? File = null,
    int Line = 0,
    int Column = 0,
    string? SourceLine = null
)
{
    public static Diagnostic Error(string message, string? file = null, int line = 0, int column = 0, string? sourceLine = null)
        => new(DiagnosticSeverity.Error, message, file, line, column, sourceLine);

    public override string ToString()
    {
        var location = string.Empty;
        if (!string.IsNullOrWhiteSpace(File))
        {
            location = Line > 0
                ? $"{File}:{Line}:{Math.Max(1, Column)}: "
                : $"{File}: ";
        }
        else if (Line > 0)
        {
            location = $"line {Line}:{Math.Max(1, Column)}: ";
        }

        return $"{location}{Message}";
    }
}
