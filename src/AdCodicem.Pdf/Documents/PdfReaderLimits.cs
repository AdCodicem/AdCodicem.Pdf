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
/// Bounds that only an invalid file can reach — how deeply containers nest, how far references to references are
/// followed — are not options: lifting them would let nothing more be read.
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
        MaxStreamLength = int.MaxValue,
        MaxObjectLength = int.MaxValue,
        MaxXRefSectionLength = int.MaxValue,
        MaxXRefSectionCount = int.MaxValue,
        MaxTrailerLength = int.MaxValue,
        MaxIndexEntries = int.MaxValue,
        MaxFilterChainLength = int.MaxValue,
    };

    /// <summary>
    /// Gets the most one stream may decode to, in bytes. Defaults to 256 MB: a compressed stream of a few
    /// kilobytes can claim gigabytes, and a scanned map can hold an image of 300 MB.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A decoded stream is held in one piece, so a value above <see cref="Array.MaxLength"/> is taken as that
    /// length. Reaching it reports <see cref="Diagnostics.PdfDiagnosticCodes.LimitDecodedStream"/>.
    /// </para>
    /// <para>
    /// Of a stream whose data passes through several filters, the last filter's output is bounded alone, and the filters
    /// before it share the bound for what they pass along together: a stream of one or two filters decodes as each filter
    /// alone would, and a chain however long holds two bounds at most. A <c>/Crypt</c> filter, which passes its data along
    /// as it is, is neither charged nor the last. Under a bound of <see cref="Array.MaxLength"/>, the filters before the
    /// last share none.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxDecodedStreamLength { get; init => field = Length(value); } = 256 * 1024 * 1024;

    /// <summary>
    /// Gets the most of one stream's encoded data the reader reads at once, in bytes. Defaults to 256 MB: a <c>/Length</c>
    /// of a few digits can claim gigabytes, and a scanned map can hold an uncompressed image of 300 MB.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The encoded data is read in one piece each time it is asked for — <see cref="Objects.PdfStream.GetRawBytes"/>, or
    /// decoding it —, so a value above <see cref="Array.MaxLength"/> is taken as that length, and a <c>/Length</c> past it
    /// is cut there. Data past the bound is cut at it: what lies within it is read, and decoded as data the reader cut,
    /// whose lost tail is no damage. Reaching it reports <see cref="Diagnostics.PdfDiagnosticCodes.LimitStreamLength"/>,
    /// once for each stream. <see cref="Objects.PdfStreamData.Length"/> stays the length the file holds.
    /// </para>
    /// <para>
    /// Data that decoding hands back as it is — no filter, an image filter, a filter the library does not know, one that
    /// could not decode it — is bounded by this guard, not by <see cref="MaxDecodedStreamLength"/>, which bounds what the
    /// filters produce: a large uncompressed image needs this bound raised alone (#170).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxStreamLength { get; init => field = Length(value); } = 256 * 1024 * 1024;

    /// <summary>
    /// Gets the longest object the reader parses, in bytes, the data of a stream aside, whether the file writes it
    /// directly or an object stream holds it. Defaults to 16 MB, which a direct array of a million references would need.
    /// </summary>
    /// <remarks>
    /// A stream's data never counts toward it, however its end is found: a <c>/Length</c> that is wrong, missing or past
    /// the file has the data searched for its <c>endstream</c> past the bytes the object was parsed through, not read
    /// through a larger window (#174); what the reader reads of the data at once is
    /// <see cref="MaxStreamLength"/>'s. A value above <see cref="Array.MaxLength"/> is taken as that length. Reaching it reports
    /// <see cref="Diagnostics.PdfDiagnosticCodes.LimitObject"/>, once for each object, and for each object an object
    /// stream holds, once for each definition of that stream.
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
    /// A classic trailer that ends within the window its cross-reference table was read through is read whole,
    /// whatever its length: the guard bounds how far the reader follows one past that window. A cross-reference stream's
    /// data is not a trailer: one whose <c>/Length</c> the reader cannot read while it reads the chain is searched for its
    /// <c>endstream</c> in the file, up to the next object or the end of the file. A value above
    /// <see cref="Array.MaxLength"/> is taken as that length. Reaching it reports
    /// <see cref="Diagnostics.PdfDiagnosticCodes.LimitTrailer"/>, and ends the cross-reference chain at the section cut: an
    /// older section, or a key, past the cut is unknown, and what only older sections index reads as null until the
    /// limit is raised.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxTrailerLength { get; init => field = Length(value); } = 64 * 1024;

    /// <summary>
    /// Gets the most object numbers the reader indexes, free ones and those whose row it refused included. Defaults to
    /// 8,388,607, the most ISO 32000-1 advises a document to hold (annex C): a cross-reference stream of a few hundred
    /// bytes can claim hundreds of millions of objects, and the index holds 52 to 108 bytes for each.
    /// </summary>
    /// <remarks>
    /// The index takes object numbers in the order the reader meets them — the newest section of the chain first, a
    /// rebuild in the file's order —, and once it holds this many takes no more: an object it holds no entry for reads
    /// as null until the bound is raised, and the index is not rebuilt for it. Reaching it reports
    /// <see cref="Diagnostics.PdfDiagnosticCodes.LimitIndexEntries"/>, once. The bound also sets what validating a
    /// document can cost, which follows its index: about 24 seconds and 3 GB near the default.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxIndexEntries
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, nameof(MaxIndexEntries));
            field = value;
        }
    } = 8_388_607;

    /// <summary>
    /// Gets the most filters a stream's data passes through. Defaults to 64: real streams name one to three, a
    /// <c>/Crypt</c> filter included, and ISO 32000 sets no bound, while each filter a <c>/Filter</c> array of millions
    /// names costs decoding a few microseconds, and some of them a table of tens of kilobytes, whatever data it is given.
    /// </summary>
    /// <remarks>
    /// The filters are counted as the <c>/Filter</c> array names them, from its first element. Reaching the bound ends
    /// decoding before the element past it, as a filter the library does not know does: the data comes back as the
    /// filters before it decoded it, and a filter past it is not read, an image filter included, so that
    /// <see cref="Objects.PdfStreamDecodingExtensions.HasImageFilter"/> does not see one. Reaching it reports
    /// <see cref="Diagnostics.PdfDiagnosticCodes.LimitFilterChain"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxFilterChainLength
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, nameof(MaxFilterChainLength));
            field = value;
        }
    } = 64;

    private static int Length(int value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, property);
        return Math.Min(value, Array.MaxLength);
    }
}
