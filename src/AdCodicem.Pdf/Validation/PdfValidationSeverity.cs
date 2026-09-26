namespace AdCodicem.Pdf.Validation;

/// <summary>How wrong a validation finding says the document is.</summary>
/// <remarks>
/// A scale of its own, not the reader's: <see cref="Diagnostics.PdfDiagnosticSeverity"/> says what the reader
/// did to read a file, this says how wrong the file is. The members are ordered, so a severity can be compared
/// with another.
/// </remarks>
public enum PdfValidationSeverity
{
    /// <summary>Worth knowing; nothing is wrong with the document.</summary>
    Information,

    /// <summary>The document works, but it is wrong: readers accept it, and a stricter one may not.</summary>
    Warning,

    /// <summary>The document is broken: readers will disagree about what it contains.</summary>
    Error,
}
