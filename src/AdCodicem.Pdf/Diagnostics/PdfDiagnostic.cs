using System.Globalization;

namespace AdCodicem.Pdf.Diagnostics;

/// <summary>A single observation made while reading, writing or transforming a document.</summary>
/// <param name="Severity">How much the entry should worry the caller.</param>
/// <param name="Code">Stable machine-readable code; part of the public contract.</param>
/// <param name="Message">Human-readable explanation, in English.</param>
/// <param name="Position">
/// Byte offset in the file the observation relates to, or -1 when it relates to no position. An offset equal to the
/// file's length designates its end, where the file stops short of what it should hold. An offset a section's trailer
/// or an object's entry names outside the file — past its end, before its start, or past what a long holds once the
/// header's offset is added — is no position: the entry is placed at the section that named it, or has none, and the
/// offset is in the message. The <c>object-stream.*</c> entries, and a fault met inside an object an object stream
/// holds, are placed where the stream's data starts in the file, the byte of decoded data in the message.
/// </param>
public readonly record struct PdfDiagnostic(
    PdfDiagnosticSeverity Severity,
    string Code,
    string Message,
    long Position = -1)
{
    /// <inheritdoc/>
    public override string ToString() =>
        Position >= 0
            ? string.Create(CultureInfo.InvariantCulture, $"{Severity} {Code} at {Position}: {Message}")
            : $"{Severity} {Code}: {Message}";
}
