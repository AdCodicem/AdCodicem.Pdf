namespace AdCodicem.Pdf.Diagnostics;

/// <summary>Thrown when an operation on a PDF document cannot be carried out at all.</summary>
/// <remarks>
/// Reserved for what makes the operation impossible. Anything the reader can work around belongs in
/// <see cref="PdfDiagnostics"/> instead.
/// </remarks>
public class PdfException : Exception
{
    /// <summary>Initialises a new instance.</summary>
    public PdfException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises a new instance.</summary>
    public PdfException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Thrown when the input is not a PDF file, or is damaged beyond repair.</summary>
public sealed class PdfFormatException : PdfException
{
    /// <summary>Initialises a new instance.</summary>
    public PdfFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises a new instance.</summary>
    public PdfFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Thrown when a document is encrypted and cannot be opened with the credentials supplied.</summary>
public sealed class PdfEncryptedException : PdfException
{
    /// <summary>Initialises a new instance.</summary>
    public PdfEncryptedException(string message)
        : base(message)
    {
    }
}
