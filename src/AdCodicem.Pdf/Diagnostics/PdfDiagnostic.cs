namespace AdCodicem.Pdf.Diagnostics;

/// <summary>A single observation made while reading, writing or transforming a document.</summary>
/// <param name="Severity">How much the entry should worry the caller.</param>
/// <param name="Code">Stable machine-readable code; part of the public contract.</param>
/// <param name="Message">Human-readable explanation, in English.</param>
/// <param name="Position">Byte offset the observation relates to, or -1 when it relates to no position.</param>
public readonly record struct PdfDiagnostic(
    PdfDiagnosticSeverity Severity,
    string Code,
    string Message,
    long Position = -1)
{
    /// <inheritdoc/>
    public override string ToString() =>
        Position >= 0 ? $"{Severity} {Code} at {Position}: {Message}" : $"{Severity} {Code}: {Message}";
}
