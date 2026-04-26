namespace Fox16ASM;

readonly record struct CompilationResult<T>(T? Value, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);

    public static CompilationResult<T> Ok(T value)
        => new(value, Array.Empty<Diagnostic>());

    public static CompilationResult<T> Failed(params Diagnostic[] diagnostics)
        => new(default, diagnostics);

    public static CompilationResult<T> Failed(IEnumerable<Diagnostic> diagnostics)
        => new(default, diagnostics.ToArray());
}
