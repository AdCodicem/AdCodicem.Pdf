namespace AdCodicem.Pdf.Diagnostics;

/// <summary>Thrown when a document is encrypted and cannot be opened with the credentials supplied.</summary>
public sealed class PdfEncryptedException : PdfException
{
    /// <summary>Initializes a new instance.</summary>
    public PdfEncryptedException(string message)
        : base(message)
    {
    }
}
