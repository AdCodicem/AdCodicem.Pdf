namespace AdCodicem.Pdf.Diagnostics;

/// <summary>Thrown when an operation on a PDF document cannot be carried out at all.</summary>
/// <remarks>
/// Reserved for what makes the operation impossible. Anything the reader can work around belongs in
/// <see cref="PdfDiagnostics"/> instead.
/// </remarks>
public class PdfException : Exception
{
    /// <summary>Initializes a new instance.</summary>
    public PdfException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance.</summary>
    public PdfException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
