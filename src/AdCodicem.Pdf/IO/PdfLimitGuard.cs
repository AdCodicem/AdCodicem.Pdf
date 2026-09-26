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
/// Where a guard reached is reported when the operation that reached it was given nowhere to report: a stream
/// read from the document and decoded without diagnostics is still cut, and the cut is reported there.
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

    /// <summary>Gets the bound <paramref name="limit"/> stands at.</summary>
    public int Bound(PdfLimit limit) => limit switch
    {
        PdfLimit.DecodedStream => limits.MaxDecodedStreamLength,
        PdfLimit.Object => limits.MaxObjectLength,
        PdfLimit.XRefSectionLength => limits.MaxXRefSectionLength,
        PdfLimit.XRefSectionCount => limits.MaxXRefSectionCount,
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
        var name = Name(limit);
        var bound = Bound(limit);
        var ceiling = limit == PdfLimit.XRefSectionCount ? int.MaxValue : Array.MaxLength;
        var message = bound >= ceiling
            ? $"{what} PdfReaderLimits.{name} is already at the most the reader can hold."
            : $"{what} Raise PdfReaderLimits.{name} to read past it.";

        if (throwOnLimit)
        {
            throw new PdfLimitExceededException(Code(limit), name, bound, message, position);
        }

        (diagnostics ?? documentDiagnostics)?.Warn(Code(limit), message, position);
    }

    /// <summary>Writes a length the way a message states a bound: <c>256 MB</c>, <c>64 KB</c>, <c>1,000 bytes</c>.</summary>
    public static string FormatLength(int length) => length switch
    {
        > 0 when length % (1024 * 1024) == 0 => string.Create(CultureInfo.InvariantCulture, $"{length / (1024 * 1024):N0} MB"),
        > 0 when length % 1024 == 0 => string.Create(CultureInfo.InvariantCulture, $"{length / 1024:N0} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{length:N0} bytes"),
    };

    private static string Code(PdfLimit limit) => limit switch
    {
        PdfLimit.DecodedStream => PdfDiagnosticCodes.LimitDecodedStream,
        PdfLimit.Object => PdfDiagnosticCodes.LimitObject,
        PdfLimit.XRefSectionLength => PdfDiagnosticCodes.LimitXRefSectionLength,
        PdfLimit.XRefSectionCount => PdfDiagnosticCodes.LimitXRefSectionCount,
        _ => PdfDiagnosticCodes.LimitTrailer,
    };

    private static string Name(PdfLimit limit) => limit switch
    {
        PdfLimit.DecodedStream => nameof(PdfReaderLimits.MaxDecodedStreamLength),
        PdfLimit.Object => nameof(PdfReaderLimits.MaxObjectLength),
        PdfLimit.XRefSectionLength => nameof(PdfReaderLimits.MaxXRefSectionLength),
        PdfLimit.XRefSectionCount => nameof(PdfReaderLimits.MaxXRefSectionCount),
        _ => nameof(PdfReaderLimits.MaxTrailerLength),
    };
}
