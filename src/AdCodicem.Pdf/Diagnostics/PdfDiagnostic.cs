namespace AdCodicem.Pdf.Diagnostics;

/// <summary>How much a diagnostic entry should worry the caller.</summary>
public enum PdfDiagnosticSeverity
{
    /// <summary>Something worth knowing that changes nothing.</summary>
    Information,

    /// <summary>The file was not conforming and the reader worked around it.</summary>
    Repair,

    /// <summary>Something is wrong and the result may not be what the caller expects.</summary>
    Warning,

    /// <summary>A conformance guarantee such as PDF/A or PDF/UA was lost by an operation.</summary>
    ConformanceLoss,
}

/// <summary>A single observation made while reading, writing or transforming a document.</summary>
/// <param name="Severity">How much the entry should worry the caller.</param>
/// <param name="Code">Stable machine-readable code; part of the public contract.</param>
/// <param name="Message">Human-readable explanation, in English.</param>
/// <param name="Position">Byte offset the observation relates to, or -1 when it relates to no position.</param>
public readonly record struct PdfDiagnostic(
    PdfDiagnosticSeverity Severity,
    string Code,
    string Message,
    long Position = -1)
{
    /// <inheritdoc/>
    public override string ToString() =>
        Position >= 0 ? $"{Severity} {Code} at {Position}: {Message}" : $"{Severity} {Code}: {Message}";
}

/// <summary>
/// The diagnostic codes the library emits. They are part of the public contract: renaming one is a
/// breaking change, because callers filter and assert on them.
/// </summary>
public static class PdfDiagnosticCodes
{
    /// <summary>The cross-reference table was rebuilt by scanning the whole file.</summary>
    public const string XRefRebuilt = "xref.rebuilt";

    /// <summary>
    /// An object, or a cross-reference section the chain names, was not at the offset the file claimed, and was
    /// found nearby.
    /// </summary>
    public const string XRefOffsetAdjusted = "xref.offset-adjusted";

    /// <summary>
    /// A cross-reference section the chain names, through <c>/Prev</c> or <c>/XRefStm</c>, is neither where
    /// it is named nor near it. The objects only it indexes are missing from the index, which is rebuilt by
    /// scanning the file when one of them is asked for.
    /// </summary>
    public const string XRefSectionMissing = "xref.section-missing";

    /// <summary>The chain of previous cross-reference sections looped back on itself.</summary>
    public const string XRefChainCycle = "xref.chain-cycle";

    /// <summary>A cross-reference entry pointed outside the file.</summary>
    public const string XRefEntryOutOfRange = "xref.entry-out-of-range";

    /// <summary>
    /// A stream's <c>/Length</c> is not where its data ends: its <c>endstream</c> lies elsewhere and ends the data, the
    /// <c>/Length</c> gives no length the reader can take and the <c>endstream</c> ends the data, or no <c>endstream</c>
    /// follows the declared length — none before the next object or the end of the file, or none looked for once the
    /// document's searches read as much of the file as they may — and that length is kept.
    /// </summary>
    public const string StreamLengthInvalid = "stream.length-invalid";

    /// <summary>
    /// A stream has no <c>endstream</c> before the end of the file, or before the <c>endobj</c> that follows its data:
    /// its data runs to the end of the file.
    /// </summary>
    public const string StreamTruncated = "stream.truncated";

    /// <summary>
    /// An object stream's dictionary names an object the stream holds — as its <c>/Length</c>, <c>/N</c>,
    /// <c>/First</c>, a filter or one of its parameters —, which cannot be read before the stream is: the stream was
    /// decoded without it.
    /// </summary>
    public const string StreamSelfReference = "stream.self-reference";

    /// <summary>A token could not be understood and was skipped.</summary>
    public const string SyntaxUnexpectedToken = "syntax.unexpected-token";

    /// <summary>The file ended in the middle of an object.</summary>
    public const string SyntaxTruncatedObject = "syntax.truncated-object";

    /// <summary>Nesting exceeded the depth the reader is willing to follow.</summary>
    public const string SyntaxDepthExceeded = "syntax.depth-exceeded";

    /// <summary>An object was defined more than once; the last definition won.</summary>
    public const string ObjectRedefined = "object.redefined";

    /// <summary>
    /// The trailer's <c>/Root</c> did not lead to a document catalog, and the reader found the catalog among the
    /// file's objects: among those the file indexes when its index is sound, in a rebuilt index otherwise.
    /// </summary>
    public const string TrailerRootRecovered = "trailer.root-recovered";

    /// <summary>
    /// A filter's data is not what the filter says. Data that nothing could decode was left encoded; data that
    /// decoded in part — a Flate stream that lost its tail or turned corrupt, an LZW stream that uses a code it has
    /// not defined — was kept as far as it went; data read despite a fault that lost nothing, such as a missing
    /// zlib header or checksum, is reported as a repair. The message says which, and where a Flate stream's data
    /// turned corrupt.
    /// </summary>
    public const string FilterFailed = "filter.failed";

    /// <summary>
    /// A Flate stream's data decoded to its end, but the zlib checksum that follows it disagrees with what it
    /// decoded to: all of it was kept, as other readers keep it, and some of it may be wrong.
    /// </summary>
    public const string FilterChecksumMismatch = "filter.checksum-mismatch";

    /// <summary>A filter named by the file is not supported.</summary>
    public const string FilterUnsupported = "filter.unsupported";

    /// <summary>
    /// A stream decodes to more than <see cref="Documents.PdfReaderLimits.MaxDecodedStreamLength"/>, and only
    /// the part within it was kept. Like every <c>limit.*</c> code, the guard is the reader's, not damage in
    /// the file.
    /// </summary>
    public const string LimitDecodedStream = "limit.decoded-stream";

    /// <summary>
    /// An object is longer than <see cref="Documents.PdfReaderLimits.MaxObjectLength"/>, and only the part
    /// within it was parsed.
    /// </summary>
    public const string LimitObject = "limit.object";

    /// <summary>
    /// A classic cross-reference section is longer than
    /// <see cref="Documents.PdfReaderLimits.MaxXRefSectionLength"/>, and only the entries within it were read.
    /// </summary>
    public const string LimitXRefSectionLength = "limit.xref-section-length";

    /// <summary>
    /// The chain of cross-reference sections is longer than
    /// <see cref="Documents.PdfReaderLimits.MaxXRefSectionCount"/>, and the older sections were not read.
    /// </summary>
    public const string LimitXRefSectionCount = "limit.xref-section-count";

    /// <summary>
    /// A trailer, or a cross-reference stream's dictionary, is longer than
    /// <see cref="Documents.PdfReaderLimits.MaxTrailerLength"/>, and only the part within it was parsed.
    /// </summary>
    public const string LimitTrailer = "limit.trailer";
}
