namespace AdCodicem.Pdf.Documents;

/// <summary>
/// The reader's guards: bounds on what a file may make the reader hold, set against hostile input, which a
/// document valid under the specification can nonetheless exceed.
/// </summary>
/// <remarks>
/// <para>
/// Every guard is active by default, so an application that opens files it did not produce is protected
/// without configuring anything. One that reads a sound but exceptional document — a large-format scan, a
/// file saved incrementally a thousand times — raises the bound it reached, or chooses <see cref="Unbounded"/>.
/// </para>
/// <para>
/// Reaching a guard keeps what fits within it and reports a warning under one of the <c>limit.*</c> codes of
/// <see cref="Diagnostics.PdfDiagnosticCodes"/>, whose message names the property that lifts it. With
/// <see cref="PdfReaderOptions.ThrowOnLimit"/> it throws <see cref="Diagnostics.PdfLimitExceededException"/>
/// instead.
/// </para>
/// <para>
/// Bounds that only an invalid file can reach — how deeply containers nest, how many objects a rebuilt index
/// holds — are not options: lifting them would let nothing more be read.
/// </para>
/// </remarks>
public sealed record PdfReaderLimits
{
    /// <summary>The guards in force when none are supplied.</summary>
    public static PdfReaderLimits Default { get; } = new();

    /// <summary>
    /// Every guard at the most the implementation can hold: <see cref="Array.MaxLength"/> for a length,
    /// <see cref="int.MaxValue"/> for a count. For documents the application trusts.
    /// </summary>
    public static PdfReaderLimits Unbounded { get; } = new()
    {
        MaxDecodedStreamLength = int.MaxValue,
        MaxObjectLength = int.MaxValue,
        MaxXRefSectionLength = int.MaxValue,
        MaxXRefSectionCount = int.MaxValue,
        MaxTrailerLength = int.MaxValue,
    };

    /// <summary>
    /// Gets the most one stream may decode to, in bytes. Defaults to 256 MB: a compressed stream of a few
    /// kilobytes can claim gigabytes, and a scanned map can hold an image of 300 MB.
    /// </summary>
    /// <remarks>
    /// A decoded stream is held in one piece, so a value above <see cref="Array.MaxLength"/> is taken as that
    /// length. Reaching it reports <see cref="Diagnostics.PdfDiagnosticCodes.LimitDecodedStream"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxDecodedStreamLength { get; init => field = Length(value); } = 256 * 1024 * 1024;

    /// <summary>
    /// Gets the longest object the reader parses, in bytes, the data of a stream aside. Defaults to 16 MB,
    /// which a direct array of a million references would need.
    /// </summary>
    /// <remarks>
    /// A value above <see cref="Array.MaxLength"/> is taken as that length. Reaching it reports
    /// <see cref="Diagnostics.PdfDiagnosticCodes.LimitObject"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxObjectLength { get; init => field = Length(value); } = 16 * 1024 * 1024;

    /// <summary>
    /// Gets the longest classic cross-reference section the reader reads, in bytes. Defaults to 64 MB, about
    /// 3.3 million entries.
    /// </summary>
    /// <remarks>
    /// A value above <see cref="Array.MaxLength"/> is taken as that length. Reaching it keeps the entries read
    /// and reports <see cref="Diagnostics.PdfDiagnosticCodes.LimitXRefSectionLength"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxXRefSectionLength { get; init => field = Length(value); } = 64 * 1024 * 1024;

    /// <summary>
    /// Gets the most cross-reference sections the reader follows through a chain of updates. Defaults to
    /// 1,024; each incremental save adds one.
    /// </summary>
    /// <remarks>
    /// Reaching it keeps the newest sections and reports
    /// <see cref="Diagnostics.PdfDiagnosticCodes.LimitXRefSectionCount"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxXRefSectionCount
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, nameof(MaxXRefSectionCount));
            field = value;
        }
    } = 1024;

    /// <summary>
    /// Gets the longest trailer the reader parses, in bytes — a cross-reference stream's dictionary is its
    /// trailer. Defaults to 64 KB; real ones are a few hundred bytes.
    /// </summary>
    /// <remarks>
    /// A value above <see cref="Array.MaxLength"/> is taken as that length. Reaching it reports
    /// <see cref="Diagnostics.PdfDiagnosticCodes.LimitTrailer"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxTrailerLength { get; init => field = Length(value); } = 64 * 1024;

    private static int Length(int value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, property);
        return Math.Min(value, Array.MaxLength);
    }
}
