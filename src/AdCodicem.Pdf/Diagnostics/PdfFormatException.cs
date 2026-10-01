namespace AdCodicem.Pdf.Diagnostics;

/// <summary>Thrown when the input is not a PDF file, or is damaged beyond repair.</summary>
public sealed class PdfFormatException : PdfException
{
    /// <summary>Initializes a new instance.</summary>
    public PdfFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance.</summary>
    public PdfFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
