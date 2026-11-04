using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO.XRef;

/// <summary>
/// One cross-reference section the chain named, as the reader found it: where it was named and by what, where it
/// was read, its trailer as the file wrote it, and what was wrong with it.
/// </summary>
/// <remarks>
/// Every offset here is a position in the file, the header's own offset added: what the validation rules report
/// and a caller can seek to. One the file names outside it is no position to seek to: past its end, or, when a long
/// cannot hold its sum with the header's offset, <see cref="long.MaxValue"/> (#125); <see cref="WrittenOffset"/> keeps
/// the offset as written.
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
    /// Gets the offset what named the section gives, as the file writes it, counted from its header; -1 when what named it
    /// was not an offset, or the section was not named by one.
    /// </summary>
    public long WrittenOffset { get; init; } = -1;

    /// <summary>
    /// Gets where what named the section is written: the <c>startxref</c> keyword, or the section whose trailer
    /// holds the <c>/Prev</c> or <c>/XRefStm</c>; -1 when it is not known.
    /// </summary>
    public long NamedFrom { get; }

    /// <summary>Gets where the section was read, which is where it was named unless it was relocated.</summary>
    public long Offset { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the section was found away from where it was named, whatever became of it there:
    /// read, or kept from being read by a guard, which leaves its state <see cref="XRefSectionState.Malformed"/>.
    /// </summary>
    public bool WasRelocated => Offset != NamedOffset;

    /// <summary>Gets what became of the section.</summary>
    public XRefSectionState State { get; set; }

    /// <summary>Gets the form of the section, once the reader saw enough of it to tell.</summary>
    public XRefSectionKind Kind { get; set; }

    /// <summary>Gets what made the section unreadable, or incomplete, in words; null when nothing did.</summary>
    public string? Fault { get; set; }

    /// <summary>
    /// Gets the first row the section refused, in words — one that gives an object in use what no entry can hold —; null
    /// when it refused none. The rows after it were read: it is no cause of the section's being unreadable.
    /// </summary>
    public string? RefusedRow { get; set; }

    /// <summary>
    /// Gets a value indicating whether the section's own reading lost rows through the file's fault, the rows before the
    /// fault kept: a classic table's row that is no row, which ends its subsection; a classic table the end of the file cuts
    /// before its trailer, once it read a row; a cross-reference stream that holds fewer rows than it declares — where a
    /// value they are read with is one a guard cut too, beside which the data holds too few whatever the value holds. The
    /// section was read only in part (#207, #210).
    /// </summary>
    public bool Incomplete { get; set; }

    /// <summary>
    /// Gets a value indicating whether a classic table holds no subsection: its <c>trailer</c> keyword follows its
    /// <c>xref</c> keyword, where ISO 32000-1 (7.5.4) has one or more subsections follow it. It is read as indexing nothing,
    /// as a table whose one subsection is <c>0 0</c> is (#222).
    /// </summary>
    public bool NoSubsection { get; set; }

    /// <summary>
    /// Gets a value indicating whether one of the reader's limits stopped it reading the section, or its trailer,
    /// whole (ADR 34): what lies past the limit was not read, and is not the file's fault.
    /// </summary>
    public bool CutByLimit { get; set; }

    /// <summary>
    /// Gets a value indicating whether what cut the section is a value it is read with — one its rows are read with, or its
    /// <c>/Length</c> — that leads to an object one of the reader's guards cut: the section was found whole, and is the
    /// guard's to keep from being read (#263). A guard that cut the section's own dictionary leaves it unknown, but for
    /// what declared itself a section before the cut (<see cref="TypedXRef"/>, or a table's <c>xref</c> keyword).
    /// </summary>
    public bool CutByValue { get; set; }

    /// <summary>
    /// Gets a value indicating whether the cross-reference stream's dictionary, as far as it was read, gives
    /// <c>/Type /XRef</c>: what is there declared itself a section before any guard's cut.
    /// </summary>
    public bool TypedXRef { get; set; }

    /// <summary>
    /// Gets a value indicating whether the stream's dictionary gives a <c>/Type</c> that names another type than
    /// <c>/XRef</c> — a CIDFont's, whose <c>/W</c> gives its glyphs' widths —: what is there said it is something else, and
    /// is the section only where the chain names it and it reads as one, or a guard cut a value it is read with (#263):
    /// otherwise it holds no section (#281).
    /// </summary>
    public bool TypedOther { get; set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Documents.PdfReaderLimits.MaxTrailerLength"/> cut the section's
    /// trailer: what lay past the cut — an older section's <c>/Prev</c>, a key a newer trailer gives — is unknown.
    /// </summary>
    public bool TrailerCutByLimit { get; set; }

    /// <summary>
    /// Gets what the section's <c>/Prev</c> or <c>/XRefStm</c> is, when it leads to an object one of the reader's guards cut
    /// — "its /Prev is the reference 9 0 R, which one of the reader's limits cut" —, which ends the chain here: the sections
    /// it names are the guard's to hide (#263). Null when nothing did.
    /// </summary>
    public string? ChainHiddenByGuard { get; set; }

    /// <summary>
    /// Gets a value indicating whether what <see cref="ChainHiddenByGuard"/> says is the section's <c>/XRefStm</c>: what
    /// the guard hides is then the cross-reference stream of the section's revision, and any section older than it.
    /// </summary>
    public bool ChainHiddenAtStream { get; set; }

    /// <summary>
    /// Gets a value indicating whether the chain went on past the section, a cross-reference stream whose rows it could not
    /// read, through the <c>/Prev</c> its dictionary gives (#208): the one value of that dictionary the reader read, and the
    /// one the validation rules judge.
    /// </summary>
    public bool PreviousHandedOn { get; set; }

    /// <summary>
    /// Gets a value indicating whether the object numbers the cross-reference stream declares, its rows lost, were refused
    /// to the rows of the sections the chain read after it: the stream declares itself one, and its dictionary was read
    /// whole, whether the chain went on through its <c>/Prev</c> (#208) or it is a hybrid file's stream (#210).
    /// </summary>
    public bool NumbersRefused { get; set; }

    /// <summary>Gets the section's own trailer — a cross-reference stream's dictionary —, or null when none was read.</summary>
    public PdfDictionary? Trailer { get; set; }

    /// <summary>Gets what is wrong with the trailer.</summary>
    public XRefTrailerFault TrailerFault { get; set; }

    /// <summary>
    /// Gets where the trailer lies — a table's <c>trailer</c> keyword, or the dictionary its rows run into without one; a
    /// cross-reference stream's object —, or where it was expected, the file's length when the file ends first; -1 when
    /// unknown.
    /// </summary>
    public long TrailerPosition { get; set; } = -1;

    /// <summary>Gets where a finding about the section's trailer points: the trailer, or the section itself while no trailer was found.</summary>
    public long TrailerLocation => TrailerPosition >= 0 ? TrailerPosition : Offset;

    /// <summary>
    /// Gets the trailer of the classic table whose <c>/XRefStm</c> named the section, a hybrid file's stream; null
    /// for every other section.
    /// </summary>
    public PdfDictionary? NamingTrailer { get; init; }

    /// <summary>
    /// Gets the record of what the chain read, or looked for, where an <c>/XRefStm</c> names a position it had reached
    /// before (<see cref="XRefSectionState.NamedAgain"/>) — the first record named or read there —, or, for an
    /// <c>/XRefStm</c> that names what its own table's <c>/Prev</c> names, the record of the section that <c>/Prev</c> reads
    /// (#156); null for every other section, and while that <c>/Prev</c> has read nothing — as past a guard that stopped
    /// the table, where it reads nothing in the file (#264).
    /// </summary>
    public XRefSectionRecord? ReadAs { get; set; }

    /// <summary>
    /// Gets what a table's <c>/XRefStm</c> names in place of a cross-reference stream of the table's revision, in words —
    /// "the table itself", "a cross-reference table, whose rows were read in the stream's place" —; null where it names a
    /// stream, or what the chain could not read (#156).
    /// </summary>
    public string? NamesNoStream { get; set; }

    /// <summary>
    /// Gets a value indicating whether the section is a hybrid file's cross-reference stream: one a table's <c>/XRefStm</c>
    /// named, which the chain found to be a stream. A record of a naming again is none: what it names is
    /// <see cref="ReadAs"/>.
    /// </summary>
    public bool IsHybridStream => NamedBy == "/XRefStm" && Kind == XRefSectionKind.Stream;

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
        RefusedRow = attempt.RefusedRow;
        Incomplete = attempt.Incomplete;
        NoSubsection = attempt.NoSubsection;
        CutByLimit = attempt.CutByLimit;
        CutByValue = attempt.CutByValue;
        TypedXRef = attempt.TypedXRef;
        TypedOther = attempt.TypedOther;
        TrailerCutByLimit = attempt.TrailerCutByLimit;
        ChainHiddenByGuard = attempt.ChainHiddenByGuard;
        ChainHiddenAtStream = attempt.ChainHiddenAtStream;
        Trailer = attempt.Trailer;
        TrailerFault = attempt.TrailerFault;
        TrailerPosition = attempt.TrailerPosition;
        StreamObjectNumber = attempt.StreamObjectNumber;
        HighestNumber = attempt.HighestNumber;
        Padding = attempt.Padding;
    }
}
