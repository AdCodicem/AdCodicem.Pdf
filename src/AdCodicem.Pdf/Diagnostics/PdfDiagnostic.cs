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

    /// <summary>An object was not at the offset the cross-reference table claimed.</summary>
    public const string XRefOffsetAdjusted = "xref.offset-adjusted";

    /// <summary>The chain of previous cross-reference sections looped back on itself.</summary>
    public const string XRefChainCycle = "xref.chain-cycle";

    /// <summary>A cross-reference entry pointed outside the file.</summary>
    public const string XRefEntryOutOfRange = "xref.entry-out-of-range";

    /// <summary>The declared length of a stream did not match where its data actually ended.</summary>
    public const string StreamLengthInvalid = "stream.length-invalid";

    /// <summary>A stream ran past the end of the file.</summary>
    public const string StreamTruncated = "stream.truncated";

    /// <summary>A token could not be understood and was skipped.</summary>
    public const string SyntaxUnexpectedToken = "syntax.unexpected-token";

    /// <summary>The file ended in the middle of an object.</summary>
    public const string SyntaxTruncatedObject = "syntax.truncated-object";

    /// <summary>Nesting exceeded the depth the reader is willing to follow.</summary>
    public const string SyntaxDepthExceeded = "syntax.depth-exceeded";

    /// <summary>An object was defined more than once; the last definition won.</summary>
    public const string ObjectRedefined = "object.redefined";

    /// <summary>A filter could not be applied and the data was left encoded.</summary>
    public const string FilterFailed = "filter.failed";

    /// <summary>A filter named by the file is not supported.</summary>
    public const string FilterUnsupported = "filter.unsupported";

    /// <summary>
    /// A stream decodes to more than the reader will decode, and only the part within that bound was kept:
    /// a limit of the reader's, set against decompression bombs, not damage in the file.
    /// </summary>
    public const string FilterLimitExceeded = "filter.limit-exceeded";
}
