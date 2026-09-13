namespace AdCodicem.Pdf.Documents;

/// <summary>Controls how a document is opened.</summary>
public sealed record PdfReaderOptions
{
    /// <summary>The options used when none are supplied.</summary>
    public static PdfReaderOptions Default { get; } = new();

    /// <summary>
    /// Gets the number of parsed objects kept in memory. Objects beyond this are re-read from the file
    /// when they are needed again, which is the trade the reader makes to keep memory predictable.
    /// </summary>
    public int ObjectCacheCapacity { get; init; } = 8192;

    /// <summary>Gets the largest number of diagnostic entries kept for one document.</summary>
    public int DiagnosticCapacity { get; init; } = 1000;

    /// <summary>
    /// Gets a value indicating whether opening an encrypted document fails. Decryption arrives with the
    /// security milestone; until then, failing loudly beats returning unreadable content.
    /// </summary>
    public bool ThrowOnEncrypted { get; init; } = true;
}
