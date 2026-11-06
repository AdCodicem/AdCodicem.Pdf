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
    /// Gets the first row of a reserved type the cross-reference stream gives, in words — "the row for object 3 gives it
    /// type 3" —; null when it gives none. ISO 32000-1 (7.5.8.3) defines types 0, 1 and 2, and reads any other as a
    /// reference to the null object, as the reader does: the row stands for its object as a free row does, and no older
    /// section's row stands for it (#221), but for one whose number was refused before it was read, which stands for nothing,
    /// its object looked for by rebuilding the index (<see cref="ReservedRowsRefused"/>). It is no cause of the section's
    /// being unreadable.
    /// </summary>
    public string? ReservedRow { get; set; }

    /// <summary>Gets how many rows of a reserved type the cross-reference stream gives (<see cref="ReservedRow"/>).</summary>
    public long ReservedRows { get; set; }

    /// <summary>
    /// Gets how many of the rows of a reserved type the cross-reference stream gives stand for nothing, their numbers refused
    /// before they were read: by a newer section — a row refused alone, a range a stream whose rows were lost declares,
    /// every number —, or by a row of the stream's own for the same number (#221). Each such object is looked for by
    /// rebuilding the index, rather than read as the null object.
    /// </summary>
    public long ReservedRowsRefused { get; set; }

    /// <summary>
    /// Gets a value indicating whether the section's own reading lost rows through the file's fault, the rows before the
    /// fault kept: a classic table's row that is no row, which ends its subsection; a classic table the end of the file cuts
    /// before its trailer, once it read a row; a cross-reference stream that holds fewer rows than it declares — where a
    /// value they are read with is one a guard cut too, beside which the data holds too few whatever the value holds. The
    /// section was read only in part (#207, #210).
    /// </summary>
    public bool Incomplete { get; set; }

    /// <summary>
    /// Gets a value indicating whether what a classic table holds past where its reading stopped cannot be known: a
    /// subsection header that gives no first object number and count of rows, or numbers its rows past the last object
    /// number, a token where a subsection or the trailer should start, or the end of the file before its trailer — within
    /// its <c>trailer</c> keyword too (#207). A row for any number may lie there, and the reader refuses every number to the
    /// rows of the sections the chain reads after the table: for a table an <c>/XRefStm</c> names, whose trailer's links are
    /// not followed, the chain goes on through the table that names it (#156, #210). Rows that run into a dictionary no
    /// trailer keyword introduces end where the trailer starts, which is known.
    /// </summary>
    public bool SubsectionsUnknown { get; set; }

    /// <summary>
    /// Gets how many rows a cross-reference stream holds, read, where its data holds fewer than its <c>/Index</c> and
    /// <c>/Size</c> declare (#210); -1 for every other section, and for a stream whose rows were left to a guard
    /// (<see cref="RowsLeftToGuard"/>).
    /// </summary>
    public long RowsHeld { get; set; } = -1;

    /// <summary>
    /// Gets a value indicating whether the rows a cross-reference stream's data may hold were left unread: a value they are
    /// read with leads to an object one of the reader's guards cut, beside which the data holds fewer rows than the stream
    /// declares whatever that value holds. The section was read only in part, and the chain ends at it (#210, #263).
    /// </summary>
    public bool RowsLeftToGuard { get; set; }

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
    /// Gets a value indicating whether the chain read the <c>/Prev</c> the dictionary of the section gives, a cross-reference
    /// stream whose rows it could not read, to go on past it (#208): the one value of that dictionary the reader read, and
    /// the one the validation rules judge, whether it names an offset the chain went on to or no offset at all.
    /// </summary>
    public bool PreviousHandedOn { get; set; }

    /// <summary>
    /// Gets a value indicating whether the object numbers the section lost, that cannot be read, were refused to the rows of
    /// the sections the chain read after it: those a cross-reference stream declares, which declares itself one and whose
    /// dictionary was read whole, whether the chain went on through its <c>/Prev</c> (#208) or it is a hybrid file's stream
    /// (#210); and those a classic table an <c>/XRefStm</c> names lost — the rest of the subsection its fault ended, or every
    /// number, where what follows the fault cannot be known (<see cref="SubsectionsUnknown"/>) —, the chain going on past it
    /// through the table that names it (#156).
    /// </summary>
    public bool NumbersRefused { get; set; }

    /// <summary>
    /// Gets a value indicating whether a section the chain read before this one had refused every object number to the rows
    /// of the sections read after it when the chain came to this one — a cross-reference stream whose rows were lost and whose
    /// numbering cannot be read, more ranges lost than are kept apart, a classic table whose rest cannot be known (#208,
    /// #210) —: none of the rows this section reads stands, and its report says so rather than that they were kept. What
    /// the section refuses once it is read is its own, and does not count, whatever order the reports take.
    /// </summary>
    public bool EveryNumberRefusedBefore { get; init; }

    /// <summary>
    /// Gets a value indicating whether a row the section read was not indexed, its number refused before the row was read
    /// by a section the chain read before this one — a range of numbers a stream whose rows were lost declares, the rest of a
    /// subsection a table lost, a row refused alone (#208, #210). Its report says the rows read were kept but for those,
    /// rather than all of them. Where the section gives a number twice, its own row refused first, the other row does not
    /// stand either, which is the section's own refusal (<see cref="RefusedRow"/>), not a newer section's.
    /// </summary>
    public bool RowsRefusedBefore { get; set; }

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
    /// <c>/XRefStm</c> that names what its own table's <c>/Prev</c> names, or whose search near the offset it names stopped
    /// where a guard cut what that <c>/Prev</c> names, the record of the section that <c>/Prev</c> reads (#156); null for
    /// every other section, and while that <c>/Prev</c> has read nothing — as past a guard that stopped the table, where it
    /// reads nothing in the file (#264).
    /// </summary>
    public XRefSectionRecord? ReadAs { get; set; }

    /// <summary>
    /// Gets what a table's <c>/XRefStm</c> names in place of a cross-reference stream of the table's revision, in words, as
    /// the reader reported it — "the table itself", "a cross-reference table, whose rows were read in the stream's place", "a
    /// cross-reference table, which one of the reader's limits cut before its first row", "a cross-reference table, which
    /// cannot be read" —, or, where <see cref="Documents.PdfReaderLimits.MaxXRefSectionCount"/>
    /// ended the chain at what the table's <c>/Prev</c> names too, as the guard hides it; null where it names a stream, or
    /// what the chain could not read but a classic table, and where its search near the offset it names stopped only where a
    /// guard cut what the table's <c>/Prev</c> names, which is the guard's (#156, #264).
    /// </summary>
    public string? NamesNoStream { get; set; }

    /// <summary>
    /// Gets a value indicating whether the <c>/XRefStm</c> names, in the file, what its own table's <c>/Prev</c> reaches too,
    /// which that <c>/Prev</c> reads (<see cref="ReadAs"/>): what the <c>/Prev</c> names, or what its search near the offset
    /// it names came to, which the <c>/XRefStm</c> read in the stream's place first (#156). The reader reported the naming
    /// as it knew it then — before the <c>/Prev</c> read what is there, as what the chain looks for there; as what the
    /// <c>/XRefStm</c>'s own reading found, a table whose rows were read in the stream's place; or, a stream that reading
    /// took for the table's own, once the <c>/Prev</c> read it —, and what the <c>/Prev</c> found is known from
    /// <see cref="ReadAs"/> once the chain is read. False for every other naming, and where a guard kept the <c>/Prev</c>
    /// from being read — one that stopped the table, or <see cref="Documents.PdfReaderLimits.MaxXRefSectionCount"/> ending
    /// the chain at what the <c>/Prev</c> names —, or that offset lies outside the file, or where the search near the offset
    /// the <c>/XRefStm</c> names stopped only where a guard cut what the <c>/Prev</c> names, before it said what it is or in a
    /// value its rows are read with.
    /// </summary>
    public bool NamesWhatPreviousReads { get; set; }

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
    /// the <c>xref</c> keyword, or the stream's object header —, where the offset should have named that token. For an
    /// <c>/XRefStm</c> that names what the chain reaches anyway (<see cref="XRefSectionState.NamedAgain"/>), how many lie
    /// between where it names and the first token of the section <see cref="ReadAs"/> records, which it reached through
    /// them: none where it names that token, or the offset the chain reached the section by (#156).
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
        ReservedRow = attempt.ReservedRow;
        ReservedRows = attempt.ReservedRows;
        ReservedRowsRefused = attempt.ReservedRowsRefused;
        Incomplete = attempt.Incomplete;
        SubsectionsUnknown = attempt.SubsectionsUnknown;
        RowsHeld = attempt.RowsHeld;
        RowsLeftToGuard = attempt.RowsLeftToGuard;
        RowsRefusedBefore = attempt.RowsRefusedBefore;
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
