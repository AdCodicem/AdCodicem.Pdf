namespace AdCodicem.Pdf.Diagnostics;

/// <summary>
/// The diagnostic codes the library emits. They are part of the public contract: renaming one is a
/// breaking change, because callers filter and assert on them.
/// </summary>
public static class PdfDiagnosticCodes
{
    /// <summary>
    /// The file has no <c>%PDF-</c> header within the bytes the reader searches at its start (ISO 32000-1, 7.5.2): the
    /// file was read all the same, its offsets counted from its first byte.
    /// </summary>
    public const string HeaderMissing = "header.missing";

    /// <summary>The cross-reference table was rebuilt by scanning the whole file.</summary>
    public const string XRefRebuilt = "xref.rebuilt";

    /// <summary>
    /// An object, or a cross-reference section the chain names, was not at the offset the file claimed, and was
    /// found nearby.
    /// </summary>
    public const string XRefOffsetAdjusted = "xref.offset-adjusted";

    /// <summary>
    /// A cross-reference section the chain names, through <c>/Prev</c> or <c>/XRefStm</c>, is neither where
    /// it is named nor near it — nothing there reads as a section, which <see cref="XRefSectionUnreadable"/> reports
    /// otherwise —, lies outside the file, or is named by a value that is not an offset. The objects only it
    /// indexes are missing from the index, which is rebuilt by scanning the file when one of them is asked for. The report
    /// is placed at the offset the section was named at, or, when that offset lies outside the file or the value is not an
    /// offset, at the section whose trailer named it.
    /// </summary>
    public const string XRefSectionMissing = "xref.section-missing";

    /// <summary>
    /// A cross-reference section the chain names, through <c>/Prev</c> or <c>/XRefStm</c>, is where it is named but
    /// cannot be read — a stray token among a table's rows or in place of its trailer, a subsection numbering objects
    /// past 2,147,483,647, a cross-reference stream whose <c>/W</c>, <c>/Index</c>, <c>/Size</c> or data cannot give
    /// rows —, and nothing near it can be read in its place. The rows read before the
    /// fault, if any, were kept; the objects only the rest indexes are missing from the index, which is rebuilt by
    /// scanning the file when one of them is asked for. The report is placed at the section, the fault in its message.
    /// </summary>
    public const string XRefSectionUnreadable = "xref.section-unreadable";

    /// <summary>
    /// The chain of previous cross-reference sections looped back on itself: a <c>/Prev</c> or <c>/XRefStm</c> names an
    /// offset the chain has already reached, read or, outside the file, named. The report is placed at the section the
    /// chain looped back to, or at the section whose trailer named it when that offset lies outside the file.
    /// </summary>
    public const string XRefChainCycle = "xref.chain-cycle";

    /// <summary>
    /// An object's cross-reference entry places it outside the file. The report carries no position, since the index
    /// does not keep where the entry was read; its message gives the offset. A section named outside the file is
    /// <see cref="XRefSectionMissing"/>'s, or the rebuild's when <c>startxref</c> names it.
    /// </summary>
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

    /// <summary>
    /// An object is in the object stream its entry names, at another index than the entry gives: the object stream's
    /// header lists it elsewhere, and it was read from there. The report is placed where the object stream's data
    /// starts, and names the object, the object stream and both indexes.
    /// </summary>
    public const string ObjectStreamMemberMoved = "object-stream.member-moved";

    /// <summary>
    /// What an object stream — a stream an entry of the index names as one — says of itself cannot be believed: its
    /// <c>/N</c> or its <c>/First</c> is absent or no non-negative integer, its <c>/N</c> declares more objects than
    /// its header can list, its <c>/First</c> lies past its decoded data, or its header ends or breaks before listing as
    /// many objects as its <c>/N</c> declares — a pair that is not two integers, or that gives a number no object can
    /// have or an offset no member can start at. The objects listed before the fault, if any, are read; the others cannot
    /// be read from it. The report is placed where the object
    /// stream's data starts, and names the object stream and its fault.
    /// </summary>
    public const string ObjectStreamUnreadable = "object-stream.unreadable";

    /// <summary>
    /// A token stood where the syntax does not allow it: where a value or a dictionary key was expected, after a key
    /// that has no value, or closing an array with a dictionary's end. It was read as null, skipped, or it ended what
    /// it stood in.
    /// </summary>
    public const string SyntaxUnexpectedToken = "syntax.unexpected-token";

    /// <summary>
    /// An object ended before it was whole: the file, or an object stream's decoded data, ended in the middle of it — a value
    /// missing, or an array, a dictionary or a string it opened never closed —, or an <c>endobj</c> stood where a value or a
    /// key should be, inside an array or a dictionary it opened. What was read is kept: a string takes the bytes to the end of
    /// the data, and an <c>endobj</c> ends every container it finds open, which takes nothing of the objects after it.
    /// Reported once, for the innermost construct left open, where it opens — where the value would start, when none is open
    /// —, the constructs around it counted in the message; inside an object an object stream holds, where the stream's data
    /// starts, the member and the byte in the message. The edge of a window the reader grows is no end of the data, and a cut
    /// a guard made is the guard's.
    /// </summary>
    public const string SyntaxTruncatedObject = "syntax.truncated-object";

    /// <summary>Nesting exceeded the depth the reader is willing to follow.</summary>
    public const string SyntaxDepthExceeded = "syntax.depth-exceeded";

    /// <summary>
    /// A number is beyond what a real can hold — its magnitude rounds past the largest a double holds, about 1.8 × 10^308
    /// —: it was read as null, so a dictionary holds no entry for its key. The report quotes the number, and is placed
    /// where it starts, or, for a number inside an object an object stream holds, where the stream's data starts, the
    /// member and the byte in its message. An integer past the range of a <see langword="long"/> is no fault of its own:
    /// it reads as a real, the one nearest to it.
    /// </summary>
    public const string SyntaxNumberOutOfRange = "syntax.number-out-of-range";

    /// <summary>
    /// A hexadecimal string holds bytes that are neither hexadecimal digits nor white space, the only bytes ISO 32000-1
    /// (7.3.4.3) lets it hold — a <c>%</c> among them, which is no comment there —: they were skipped, as pdf.js and pdfium
    /// skip them, where other readers stop at the first or read the string as null. Reported once for each string, where
    /// its first such byte lies, how many there are in the message; inside an object an object stream holds, where the
    /// stream's data starts, the member and the byte in the message.
    /// </summary>
    public const string SyntaxHexStringInvalid = "syntax.hex-string-invalid";

    /// <summary>
    /// A name holds a number sign that two hexadecimal digits do not follow: it was kept as the byte it is, as PDF 1.1 read
    /// it and most readers still do, where ISO 32000-1 (7.3.5) has a writer write <c>#23</c>. Reported once for each name,
    /// where its first such number sign lies, the name read quoted in the message; inside an object an object stream holds,
    /// where the stream's data starts, the member and the byte in the message. A <c>#00</c> is no such fault: it is an
    /// escape, of a byte no name may hold, which the validator reports.
    /// </summary>
    public const string SyntaxNameEscapeInvalid = "syntax.name-escape-invalid";

    /// <summary>
    /// A dictionary gives a key more than once, which ISO 32000-1 (7.3.7) forbids: keys compare as they read, so a key written
    /// with <c>#xx</c> escapes repeats the one written without. The last value given is kept, and a null given last removes
    /// the key, as qpdf, pdf.js, PDFBox, MuPDF and pdfium read it. Reported for each key given again, where it is given, the
    /// key quoted in the message; inside an object an object stream holds, where the stream's data starts, the member and the
    /// byte in the message.
    /// </summary>
    public const string SyntaxKeyRepeated = "syntax.key-repeated";

    /// <summary>
    /// Rebuilding the index met more than one definition of an object number: the file was updated, or copies an
    /// object. It is reported once for each rebuild, as information with no position: how many definitions met a number
    /// already found, the first ten of those numbers, and which definition was kept — the last written directly in the
    /// file, or, for a number written only inside object streams, the first listed in the object stream read first.
    /// </summary>
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

    /// <summary>
    /// A filter named by the file is not supported, and decoding stopped there: the data is what the filters before
    /// it decoded, or the stream's own when it is the first. A <c>/Filter</c> entry that is neither a name nor an
    /// array leaves the data as the stream holds it.
    /// </summary>
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
