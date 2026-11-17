namespace AdCodicem.Pdf.Documents;

/// <summary>Controls how a document is opened.</summary>
public sealed record PdfReaderOptions
{
    /// <summary>The options used when none are supplied.</summary>
    public static PdfReaderOptions Default { get; } = new();

    /// <summary>
    /// Gets the number of parsed objects kept in memory. Objects beyond this are re-read from the file
    /// when they are needed again, which is the trade the reader makes to keep memory predictable. Defaults to 8,192; the
    /// reader keeps at least 64, a smaller value reading as 64.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int ObjectCacheCapacity
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 8192;

    /// <summary>
    /// Gets the largest number of diagnostic entries kept for one document (<see cref="Diagnostics.PdfDiagnostics.Capacity"/>):
    /// past it, entries are counted, and still answer <see cref="Diagnostics.PdfDiagnostics.Contains(string)"/> and the
    /// <c>Has</c> properties, but are not kept. Defaults to 1,000; zero keeps none.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int DiagnosticCapacity
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 1000;

    /// <summary>
    /// Gets a value indicating whether opening an encrypted document fails. Decryption arrives with the
    /// security milestone; until then, failing loudly beats returning unreadable content.
    /// </summary>
    public bool ThrowOnEncrypted { get; init; } = true;

    /// <summary>
    /// Gets the reader's guards: bounds a valid document may exceed, set against hostile input. A stream
    /// read from the document decodes under them too, however long after opening.
    /// </summary>
    public PdfReaderLimits Limits
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = PdfReaderLimits.Default;

    /// <summary>
    /// Gets a value indicating whether reaching one of the <see cref="Limits"/> throws a
    /// <see cref="Diagnostics.PdfLimitExceededException"/> rather than keeping what fits and reporting it.
    /// </summary>
    /// <remarks>
    /// Reading is lazy, so the exception can come from any operation that reads: <see cref="PdfDocument.Open(string, PdfReaderOptions?)"/>,
    /// resolving an object, decoding a stream.
    /// </remarks>
    public bool ThrowOnLimit { get; init; }
}
