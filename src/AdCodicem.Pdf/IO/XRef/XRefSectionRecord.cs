using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO.XRef;

/// <summary>
/// One cross-reference section the chain named, as the reader found it: where it was named and by what, where it
/// was read, its trailer as the file wrote it, and what was wrong with it.
/// </summary>
/// <remarks>
/// Every offset here is a position in the file, the header's own offset added: what the validation rules report
/// and a caller can seek to.
/// </remarks>
internal sealed class XRefSectionRecord
{
    public XRefSectionRecord(string namedBy, long namedOffset, long namedFrom)
    {
        NamedBy = namedBy;
        NamedOffset = namedOffset;
        NamedFrom = namedFrom;
        Offset = namedOffset;
    }

    /// <summary>Gets what named the section: <c>startxref</c>, <c>/Prev</c> or <c>/XRefStm</c>.</summary>
    public string NamedBy { get; }

    /// <summary>Gets the position the section was named at, or -1 when what named it was not an offset.</summary>
    public long NamedOffset { get; }

    /// <summary>
    /// Gets where what named the section is written: the <c>startxref</c> keyword, or the section whose trailer
    /// holds the <c>/Prev</c> or <c>/XRefStm</c>; -1 when it is not known.
    /// </summary>
    public long NamedFrom { get; }

    /// <summary>Gets where the section was read, which is where it was named unless it was relocated.</summary>
    public long Offset { get; private set; }

    /// <summary>Gets what became of the section.</summary>
    public XRefSectionState State { get; set; }

    /// <summary>Gets the form of the section, once the reader saw enough of it to tell.</summary>
    public XRefSectionKind Kind { get; set; }

    /// <summary>Gets what made the section unreadable, or incomplete, in words; null when nothing did.</summary>
    public string? Fault { get; set; }

    /// <summary>Gets a value indicating whether a cross-reference stream holds fewer rows than it declares.</summary>
    public bool Incomplete { get; set; }

    /// <summary>
    /// Gets a value indicating whether one of the reader's limits stopped it reading the section, or its trailer,
    /// whole (ADR 34): what lies past the limit was not read, and is not the file's fault.
    /// </summary>
    public bool CutByLimit { get; set; }

    /// <summary>Gets the section's own trailer — a cross-reference stream's dictionary —, or null when none was read.</summary>
    public PdfDictionary? Trailer { get; set; }

    /// <summary>Gets what is wrong with the trailer.</summary>
    public XRefTrailerFault TrailerFault { get; set; }

    /// <summary>Gets where the trailer's dictionary starts, or where the trailer was expected; -1 when unknown.</summary>
    public long TrailerPosition { get; set; } = -1;

    /// <summary>Gets the object number of a cross-reference stream, or 0 for a classic table.</summary>
    public int StreamObjectNumber { get; set; }

    /// <summary>Gets the highest object number the section's rows give an entry, free or not; -1 when it gave none.</summary>
    public int HighestNumber { get; set; } = -1;

    /// <summary>
    /// Gets how many bytes of white space or comments lie between where the section was read and its first token —
    /// the <c>xref</c> keyword, or the stream's object header —, where the offset should have named that token.
    /// </summary>
    public int Padding { get; set; }

    /// <summary>Takes what a successful attempt at another offset read, the section having been relocated there.</summary>
    public void RelocateTo(XRefSectionRecord attempt)
    {
        Offset = attempt.Offset;
        State = XRefSectionState.Relocated;
        Kind = attempt.Kind;
        Fault = attempt.Fault;
        Incomplete = attempt.Incomplete;
        CutByLimit = attempt.CutByLimit;
        Trailer = attempt.Trailer;
        TrailerFault = attempt.TrailerFault;
        TrailerPosition = attempt.TrailerPosition;
        StreamObjectNumber = attempt.StreamObjectNumber;
        HighestNumber = attempt.HighestNumber;
        Padding = attempt.Padding;
    }
}
