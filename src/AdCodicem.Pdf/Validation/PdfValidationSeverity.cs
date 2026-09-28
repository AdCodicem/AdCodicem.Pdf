namespace AdCodicem.Pdf.Validation;

/// <summary>How wrong a validation finding says the document is.</summary>
/// <remarks>
/// A scale of its own, not the reader's: <see cref="Diagnostics.PdfDiagnosticSeverity"/> says what the reader
/// did to read a file, this says how wrong the file is. What separates a warning from an error is whether the
/// file can still be read as it was written (ADR 45). The members are ordered, so a severity can be compared with
/// another.
/// </remarks>
public enum PdfValidationSeverity
{
    /// <summary>Worth knowing; nothing is wrong with the document.</summary>
    Information,

    /// <summary>
    /// The document breaks the specification, and is read all the same as it was evidently meant, the rest of the
    /// file confirming that reading: the reader reads it, and a stricter reader may not.
    /// </summary>
    Warning,

    /// <summary>
    /// The document is broken: the reader cannot vouch that it reads what was written — it rebuilt the index by
    /// scanning the file, lost part of what the file holds, or chose what the file does not designate.
    /// </summary>
    Error,
}
