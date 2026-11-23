using System.Globalization;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;

namespace AdCodicem.Pdf.IO;

/// <summary>
/// The guards one document is read under, and what reaching one does: a warning that names the property
/// to raise, or, when the document was opened with <see cref="PdfReaderOptions.ThrowOnLimit"/>, an exception.
/// </summary>
/// <remarks>
/// Streams read from the document carry it, so that one decoded long after opening keeps its document's
/// settings; a stream built in memory decodes under <see cref="Default"/>.
/// </remarks>
/// <param name="limits">The bounds.</param>
/// <param name="throwOnLimit">Whether reaching one throws rather than warns.</param>
/// <param name="documentDiagnostics">
/// Where an operation reports when it was given nowhere to report: a stream read from the document and decoded
/// without diagnostics reports there what decoding met — a guard reached, damaged data.
/// </param>
internal sealed class PdfLimitGuard(PdfReaderLimits limits, bool throwOnLimit, PdfDiagnostics? documentDiagnostics = null)
{
    /// <summary>The guards in force when a document supplies none: the default bounds, reported as warnings.</summary>
    public static PdfLimitGuard Default { get; } = new(PdfReaderLimits.Default, throwOnLimit: false);

    /// <summary>
    /// Gets where what an operation meets is reported when it was given nowhere to report: the diagnostics of
    /// the document the guard belongs to, or null for a stream built in memory.
    /// </summary>
    public PdfDiagnostics? DocumentDiagnostics => documentDiagnostics;

    /// <summary>Gets a value indicating whether reaching a guard throws rather than warns.</summary>
    public bool ThrowsOnLimit => throwOnLimit;

    /// <summary>Gets the bound <paramref name="limit"/> stands at.</summary>
    public int Bound(PdfLimit limit) => limit switch
    {
        PdfLimit.DecodedStream => limits.MaxDecodedStreamLength,
        PdfLimit.StreamLength => limits.MaxStreamLength,
        PdfLimit.Object => limits.MaxObjectLength,
        PdfLimit.XRefSectionLength => limits.MaxXRefSectionLength,
        PdfLimit.XRefSectionCount => limits.MaxXRefSectionCount,
        PdfLimit.IndexEntries => limits.MaxIndexEntries,
        PdfLimit.FilterChain => limits.MaxFilterChainLength,
        PdfLimit.RebuildReads => limits.MaxRebuildReadPasses,
        PdfLimit.ObjectStreamDecodes => limits.MaxObjectStreamDecodePasses,
        PdfLimit.NestingDepth => limits.MaxNestingDepth,
        PdfLimit.NestedLoads => limits.MaxNestedLoads,
        _ => limits.MaxTrailerLength,
    };

    /// <summary>
    /// Reports that <paramref name="limit"/> was reached: a warning under its code, whose message is
    /// <paramref name="what"/> followed by the property that lifts it, or the exception the document asked for.
    /// The warning goes to <paramref name="diagnostics"/>, or to the document's when none are given.
    /// </summary>
    /// <exception cref="PdfLimitExceededException">The document was opened with <see cref="PdfReaderOptions.ThrowOnLimit"/>.</exception>
    public void Reach(PdfLimit limit, PdfDiagnostics? diagnostics, string what, long position)
    {
        var message = Describe(limit, what);
        ThrowIfAsked(limit, message, position);
        (diagnostics ?? documentDiagnostics)?.ReachLimit(Code(limit), message, position);
    }

    /// <summary>
    /// Gives the message that <paramref name="limit"/>'s report makes: <paramref name="what"/> followed by the property that
    /// lifts it, or by its standing at the most the reader can hold.
    /// </summary>
    public string Describe(PdfLimit limit, string what)
    {
        var name = Name(limit);
        var ceiling = limit is PdfLimit.XRefSectionCount or PdfLimit.IndexEntries or PdfLimit.FilterChain or PdfLimit.RebuildReads
            or PdfLimit.ObjectStreamDecodes or PdfLimit.NestingDepth or PdfLimit.NestedLoads
            ? int.MaxValue
            : Array.MaxLength;
        return Bound(limit) >= ceiling
            ? $"{what} PdfReaderLimits.{name} is already at the most the reader can hold."
            : $"{what} Raise PdfReaderLimits.{name} to read past it.";
    }

    /// <summary>
    /// Gives the message that the report of the thread's stack, cutting what <paramref name="what"/> says before a nesting
    /// guard did, makes: the remedy is a thread with a larger stack.
    /// </summary>
    public static string DescribeStack(string what) =>
        $"{what} The thread's stack had no room for more; read the document from a thread with a larger stack.";

    /// <summary>
    /// Throws, where the document asked for it, the exception of <paramref name="limit"/> reached with
    /// <paramref name="message"/>, a message <see cref="Describe"/> or <see cref="DescribeStack"/> made; does nothing
    /// otherwise.
    /// </summary>
    /// <exception cref="PdfLimitExceededException">The document was opened with <see cref="PdfReaderOptions.ThrowOnLimit"/>.</exception>
    public void ThrowIfAsked(PdfLimit limit, string message, long position)
    {
        if (throwOnLimit)
        {
            throw new PdfLimitExceededException(Code(limit), Name(limit), Bound(limit), message, position);
        }
    }

    /// <summary>
    /// Reports that the thread's stack stopped a nesting <paramref name="limit"/> bounds before the bound did: a warning under
    /// the guard's code, whose message is <paramref name="what"/> followed by the remedy, a thread with a larger stack, or the
    /// exception the document asked for.
    /// </summary>
    /// <exception cref="PdfLimitExceededException">The document was opened with <see cref="PdfReaderOptions.ThrowOnLimit"/>.</exception>
    public void ReachStack(PdfLimit limit, PdfDiagnostics? diagnostics, string what, long position)
    {
        var message = DescribeStack(what);
        ThrowIfAsked(limit, message, position);
        (diagnostics ?? documentDiagnostics)?.ReachLimit(Code(limit), message, position);
    }

    /// <summary>
    /// Says what <see cref="PdfReaderLimits.MaxStreamLength"/> did to a stream's data it cut, as its report starts: the
    /// reader's report, once for the stream, and decoding's, in diagnostics a caller passes of its own.
    /// </summary>
    public string DescribeStreamLengthCut() =>
        $"A stream's data runs past {FormatLength(Bound(PdfLimit.StreamLength))}; only what lies within it was read.";

    /// <summary>
    /// Writes a length the way a message states a bound: a whole number of megabytes or kilobytes in that unit, any other
    /// length in bytes, its digits ungrouped as every count's are (<see cref="CountText"/>): <c>256 MB</c>, <c>64 KB</c>,
    /// <c>1000 bytes</c>, <c>1 byte</c>.
    /// </summary>
    public static string FormatLength(int length) => length switch
    {
        > 0 when length % (1024 * 1024) == 0 => string.Create(CultureInfo.InvariantCulture, $"{length / (1024 * 1024)} MB"),
        > 0 when length % 1024 == 0 => string.Create(CultureInfo.InvariantCulture, $"{length / 1024} KB"),
        _ => CountText.Bytes(length),
    };

    private static string Code(PdfLimit limit) => limit switch
    {
        PdfLimit.DecodedStream => PdfDiagnosticCodes.LimitDecodedStream,
        PdfLimit.StreamLength => PdfDiagnosticCodes.LimitStreamLength,
        PdfLimit.Object => PdfDiagnosticCodes.LimitObject,
        PdfLimit.XRefSectionLength => PdfDiagnosticCodes.LimitXRefSectionLength,
        PdfLimit.XRefSectionCount => PdfDiagnosticCodes.LimitXRefSectionCount,
        PdfLimit.IndexEntries => PdfDiagnosticCodes.LimitIndexEntries,
        PdfLimit.FilterChain => PdfDiagnosticCodes.LimitFilterChain,
        PdfLimit.RebuildReads => PdfDiagnosticCodes.LimitRebuildReads,
        PdfLimit.ObjectStreamDecodes => PdfDiagnosticCodes.LimitObjectStreamDecodes,
        PdfLimit.NestingDepth => PdfDiagnosticCodes.LimitNestingDepth,
        PdfLimit.NestedLoads => PdfDiagnosticCodes.LimitNestedLoads,
        _ => PdfDiagnosticCodes.LimitTrailer,
    };

    private static string Name(PdfLimit limit) => limit switch
    {
        PdfLimit.DecodedStream => nameof(PdfReaderLimits.MaxDecodedStreamLength),
        PdfLimit.StreamLength => nameof(PdfReaderLimits.MaxStreamLength),
        PdfLimit.Object => nameof(PdfReaderLimits.MaxObjectLength),
        PdfLimit.XRefSectionLength => nameof(PdfReaderLimits.MaxXRefSectionLength),
        PdfLimit.XRefSectionCount => nameof(PdfReaderLimits.MaxXRefSectionCount),
        PdfLimit.IndexEntries => nameof(PdfReaderLimits.MaxIndexEntries),
        PdfLimit.FilterChain => nameof(PdfReaderLimits.MaxFilterChainLength),
        PdfLimit.RebuildReads => nameof(PdfReaderLimits.MaxRebuildReadPasses),
        PdfLimit.ObjectStreamDecodes => nameof(PdfReaderLimits.MaxObjectStreamDecodePasses),
        PdfLimit.NestingDepth => nameof(PdfReaderLimits.MaxNestingDepth),
        PdfLimit.NestedLoads => nameof(PdfReaderLimits.MaxNestedLoads),
        _ => nameof(PdfReaderLimits.MaxTrailerLength),
    };
}
