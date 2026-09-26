namespace AdCodicem.Pdf.Diagnostics;

/// <summary>
/// Thrown when reading reaches one of the reader's guards and
/// <see cref="Documents.PdfReaderOptions.ThrowOnLimit"/> asks for an exception rather than a warning.
/// </summary>
/// <remarks>
/// The guard is the reader's, not a fault of the file: the document may be valid. Raising the property
/// <see cref="LimitName"/> names on <see cref="Documents.PdfReaderLimits"/> reads it further.
/// </remarks>
public sealed class PdfLimitExceededException : PdfException
{
    /// <summary>Initialises a new instance.</summary>
    /// <param name="code">The <c>limit.*</c> code of <see cref="PdfDiagnosticCodes"/> the guard reports under.</param>
    /// <param name="limitName">The <see cref="Documents.PdfReaderLimits"/> property that sets the guard.</param>
    /// <param name="limit">The value of that property when the guard was reached.</param>
    /// <param name="message">What was reached, and where.</param>
    /// <param name="position">The byte offset the guard was reached at, or -1 when it relates to no position.</param>
    public PdfLimitExceededException(string code, string limitName, int limit, string message, long position = -1)
        : base(message)
    {
        Code = code;
        LimitName = limitName;
        Limit = limit;
        Position = position;
    }

    /// <summary>Gets the <c>limit.*</c> code of <see cref="PdfDiagnosticCodes"/> the guard reports under.</summary>
    public string Code { get; }

    /// <summary>
    /// Gets the name of the <see cref="Documents.PdfReaderLimits"/> property that sets the guard, such as
    /// <c>MaxDecodedStreamLength</c>.
    /// </summary>
    public string LimitName { get; }

    /// <summary>Gets the value of that property when the guard was reached.</summary>
    public int Limit { get; }

    /// <summary>Gets the byte offset the guard was reached at, or -1 when it relates to no position.</summary>
    public long Position { get; }
}
