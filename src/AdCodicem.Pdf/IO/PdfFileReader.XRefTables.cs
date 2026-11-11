using System.Globalization;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO;

internal sealed partial class PdfFileReader
{
    /// <summary>
    /// Reads the section at <paramref name="absolute"/> into the index, and what became of it into
    /// <paramref name="section"/>: <see cref="XRefSectionState.Read"/>, <see cref="XRefSectionState.NotFound"/> when
    /// nothing there is a section, or <see cref="XRefSectionState.Malformed"/> when one is and cannot be read.
    /// </summary>
    /// <param name="absolute">
    /// Where the section is looked for, a position in the file: one an offset the chain gives names, or one the reader found,
    /// before the header though it be.
    /// </param>
    /// <param name="section">
    /// Receives what became of the section, and the ordinal of the reading, which the numbers it refuses are kept with
    /// (<see cref="XRefSectionRecord.Ordinal"/>).
    /// </param>
    /// <param name="previous">The offset its <c>/Prev</c> gives, or -1.</param>
    /// <param name="hybrid">The offset its <c>/XRefStm</c> gives, or -1.</param>
    /// <param name="candidate">
    /// Whether the position is a place the section may have been moved to rather than one the chain names: an object there
    /// that is no cross-reference stream leaves nothing of its reading.
    /// </param>
    /// <param name="tablePrevious">
    /// For a place the search near the offset a hybrid table's <c>/Prev</c> names tries, where the table's <c>/XRefStm</c>
    /// read a cross-reference stream, or names again one a newer table's read, and the place starts no sooner than that
    /// stream: that offset, as written. A stream there that may be the table's own leaves nothing of its reading, and is
    /// passed over (<see cref="XRefSectionRecord.MayBeTablesOwn"/>). -1 otherwise.
    /// </param>
    private XRefSectionState TryReadXRefSection(
        long absolute, XRefSectionRecord section, out long previous, out long hybrid, bool candidate = false, long tablePrevious = -1)
    {
        section.Ordinal = ++_sectionReadings;
        previous = -1;
        hybrid = -1;
        _rowsLeftUnread = null;
        _previousPastGuard = -1;
        _streamPastGuard = -1;

        if (!IsInFile(absolute))
        {
            // Reported once, by what the chain makes of it: a missing section, or a rebuild for the first.
            section.Fault = "lies outside the file";
            section.State = XRefSectionState.NotFound;
            return section.State;
        }

        var padding = PaddingAt(absolute, out var table, remember: !candidate);

        if (padding < 0)
        {
            // White space and comments fill a trailer's window there and run on past it: the first token past them is the
            // guard's to hide, as whatever else it cuts at an offset the chain names is (ADR 34), and the section is not
            // read from there. It is looked for as one not there is: near the offset, where the search may find it past
            // them, or, for the one startxref names, by rebuilding the index. A place a search near an offset tries starts
            // at a token, and never gets here: the offset is one the chain names. What that search passes over, or what the
            // section names past the guard, is the guard's: the search may take a section other than the one past them,
            // which the reader cannot tell without reading past the guard, and no stream passed over anywhere in the
            // document is reported, even where the search takes the section past them and reads it whole
            // (ReportStreamsPassedOver).
            ReachLimit(
                PdfLimit.Trailer,
                $"White space and comments run past {PdfLimitGuard.FormatLength(_guard.Bound(PdfLimit.Trailer))} where a cross-reference section is named; the section was not read from there.",
                absolute);
            _guardMayHideLink = true;
            section.Fault = "holds neither the xref keyword nor an object";
            section.State = XRefSectionState.NotFound;
            return section.State;
        }

        section.Padding = padding;

        if (table)
        {
            section.Kind = XRefSectionKind.Table;
            section.State = TryReadClassicTable(absolute, section, out previous, out hybrid, candidate)
                ? XRefSectionState.Read
                : XRefSectionState.Malformed;
            return section.State;
        }

        section.State = TryReadXRefStream(absolute, section, out previous, candidate, tablePrevious: tablePrevious);
        return section.State;
    }

    /// <summary>
    /// Gives how many bytes of white space and comments lie at <paramref name="absolute"/>, a position in the file, before
    /// the first token there — where a section read there starts, and where the chain reached it (<see cref="StartOf"/>) —,
    /// and whether that token is the <c>xref</c> keyword, a classic table's; -1 where they run on past a trailer's window,
    /// which bounds them, the first token past them the guard's.
    /// </summary>
    /// <remarks>
    /// <see cref="PdfReaderLimits.MaxTrailerLength"/> bounds the white space and comments before a section, and a stream's
    /// dictionary, read through windows that open at its object's header (<see cref="TryReadXRefStream"/>), apart; a classic
    /// table's windows open at the position, so that the white space before its keyword counts toward
    /// <see cref="PdfReaderLimits.MaxXRefSectionLength"/> as well (#289). The section starts where the first token does,
    /// however much white space the offset falls short by, so that what the chain reads there is what it reached there, and
    /// what a check of the place reads (<see cref="KeepsItsPlace"/>). A probe that holds white space and comments alone, or a
    /// first token its edge may cut, is read on as far as a trailer's window reaches and <see cref="XRefProbeLength"/> bytes
    /// past it: a first token that starts within that window, or where it ends, is read whole enough to tell the keyword
    /// from an object's header. White space and comments that fill the window and run on past it are the guard's (ADR 34),
    /// as whatever else it cuts there is: the section is not read from the position, and the reading reports the guard
    /// (<see cref="TryReadXRefSection"/>), while a search near the position may find it past them. The windows grow
    /// eightfold from <see cref="XRefProbeLength"/> bytes, and only where the first token, or the white space and comments
    /// before it, reach the last one's edge, so that the bytes read come to one probe where that token ends within the
    /// first, short of its edge, and otherwise to less than ten times those from the position to that token's end — white
    /// space, comments and the token alike, a token the probe's edge cuts counted as far as the probe reaches.
    /// </remarks>
    /// <param name="absolute">The position, in the file.</param>
    /// <param name="table">Receives whether the first token is the <c>xref</c> keyword; false where there is none to read.</param>
    /// <param name="remember">
    /// Whether the probe is kept, to answer the position when it is asked of again: not for a place a search near an offset
    /// tries (<see cref="_paddingProbes"/>).
    /// </param>
    private int PaddingAt(long absolute, out bool table, bool remember = true)
    {
        // A position asked of again is answered as it was, read once (_paddingProbes), and the two are kept in the order they
        // were read: a hybrid table's /Prev, probed after its /XRefStm, outlasts that /XRefStm's reading and the places its
        // search near the offset tries, however many, which are not kept, and is known when the chain comes to it.
        var (last, before) = _paddingProbes;

        if (last.Position == absolute || before.Position == absolute)
        {
            var known = last.Position == absolute ? last : before;
            table = known.Table;
            return known.Padding;
        }

        // The white space a trailer's window may hold, and as many bytes past it as tell what starts at its end: the keyword
        // whole, or the first digit of an object's header.
        var bound = _guard.Bound(PdfLimit.Trailer);
        var reach = (int)Math.Min((long)bound + XRefProbeLength, Array.MaxLength);
        var probeLength = XRefProbeLength;

        while (true)
        {
            using var probe = _source.GetWindow(absolute, probeLength);
            var lexer = new PdfLexer(probe.Memory.Span);
            var first = lexer.Read();

            // A probe that holds white space and comments alone gives the end-of-input token, which starts and ends where the
            // probe does: it is grown as one whose first token its edge may cut is, as far as the probe reaches.
            if (first.End >= probe.Length && probe.Length == probeLength && probeLength < reach)
            {
                probeLength = (int)Math.Min(reach, (long)probeLength * 8);
                continue;
            }

            // A first token past a trailer's window, or none before the probe's end, lies past what the guard lets the reader
            // cross: the run is the guard's, whatever follows it.
            var padding = first.Start > bound ? -1 : first.Start;
            table = padding >= 0 && first.IsKeyword("xref"u8);

            if (remember)
            {
                _paddingProbes = (new PaddingProbe(absolute, padding, table), last);
            }

            return padding;
        }
    }

    /// <summary>
    /// What a probe <see cref="PaddingAt"/> read found at a position: how many bytes of white space and comments lie there
    /// before the first token, and whether that token is the <c>xref</c> keyword.
    /// </summary>
    /// <param name="Position">The position probed, in the file; -1 for no probe.</param>
    /// <param name="Padding">The bytes before the first token; -1 where they run on past a trailer's window.</param>
    /// <param name="Table">Whether the first token is the <c>xref</c> keyword.</param>
    private readonly record struct PaddingProbe(long Position, int Padding, bool Table)
    {
        /// <summary>Gets no probe: one that answers no position.</summary>
        public static PaddingProbe None => new(-1, 0, false);
    }

    private bool TryReadClassicTable(long absolute, XRefSectionRecord section, out long previous, out long hybrid, bool candidate)
    {
        previous = -1;
        hybrid = -1;
        var maxWindow = _guard.Bound(PdfLimit.XRefSectionLength);
        var windowSize = Math.Min(XRefWindow, maxWindow);
        var reserved = false;

        while (true)
        {
            using var window = _source.GetWindow(absolute, windowSize);
            var span = window.Memory.Span;
            var lexer = new PdfLexer(span);

            // A window shorter than asked holds the end of the file: what it holds is all there is, as it is for a full one
            // that ends where the file does. Any other full one may have cut the table, and is grown until the guard allows
            // no more.
            var windowFull = window.Length == windowSize && absolute + window.Length < _source.Length;
            var canGrow = windowFull && windowSize < maxWindow;
            var keyword = lexer.Read();
            var truncated = windowFull && keyword.End >= span.Length;

            // Each window reads the rows from the keyword on: what one lost to its own edge, a larger one reads.
            var subsections = 0;
            _rowsLost?.Clear();
            _rowsLostPast = false;

            if (!truncated && !keyword.IsKeyword("xref"u8))
            {
                section.Fault = "does not start with the xref keyword";
                return false;
            }

            if (!truncated && !reserved)
            {
                // The rows are read in the window that holds their end, rather than read again in each larger one: until
                // a window holds it, or more rows than the index has room for, the window grows. The index makes room for
                // as many rows as their bytes hold before they are read, once: a subsection's count is the file's claim,
                // and room made for each subsection in turn, or for rows read again, would double the index each time.
                var rows = span[keyword.End..];
                var rowsEnd = rows.IndexOfAnyExcept(TableBytes);
                var held = RowsHeld(rowsEnd < 0 ? rows : rows[..rowsEnd]);

                if (rowsEnd < 0 && canGrow && held <= IndexRoomLeft)
                {
                    windowSize = (int)Math.Min((long)windowSize * 4, maxWindow);
                    continue;
                }

                ReserveIndex(held);
                reserved = true;
            }

            while (!truncated)
            {
                var token = lexer.Read();

                // A token that reaches the window's edge may have been cut by it — "trai" of "trailer", "32"
                // of "3270" — so it is read again in a larger window rather than taken for what it seems.
                if (token.Kind == PdfTokenKind.EndOfInput || (windowFull && token.End >= span.Length))
                {
                    break;
                }

                if (token.IsKeyword(TrailerKeyword))
                {
                    section.TrailerPosition = absolute + token.Start;

                    // A trailer keyword that follows the xref keyword ends a table of no subsection, which indexes nothing,
                    // as one whose one subsection is 0 0 does: read as written, and reported (#222).
                    section.NoSubsection = subsections == 0;

                    var trailer = ReadTrailer(window.Memory, absolute, lexer.Position, windowFull, section);

                    if (trailer is not null)
                    {
                        MergeSectionTrailer(trailer, section);
                    }

                    // A table an /XRefStm names, where ISO 32000-1 names a cross-reference stream (Table 19), stands in its
                    // place: as a hybrid file's stream's own /Prev, what its trailer names is not read, to name a section
                    // missing or end the chain for a guard, the chain going on through the table that names it.
                    if (trailer is not null && section.NamedBy != "/XRefStm")
                    {
                        previous = SectionOffset(trailer, PdfName.Prev, absolute, out var previousHidden);
                        hybrid = SectionOffset(trailer, PdfName.XRefStm, absolute, out var streamHidden);
                        section.ChainHiddenByGuard = streamHidden ?? previousHidden;
                        section.ChainHiddenAtStream = streamHidden is not null;

                        // A stream a guard hid holds rows newer than the older sections': the chain ends with it, as with a
                        // stream a guard cut, since their rows would stand for the stream's.
                        previous = streamHidden is null ? previous : -1;
                    }

                    return true;
                }

                if (token.Kind != PdfTokenKind.Integer)
                {
                    // The end of the file within the trailer keyword — "trai" — cuts the table before its trailer, as where it
                    // ends before the keyword (#207): a token that runs to the end of a window that holds the end of the file
                    // runs to the end of the file, a window whose edge may cut a token having been grown.
                    if (token.End == span.Length && CutTrailerKeyword(token))
                    {
                        break;
                    }

                    if (token.Kind == PdfTokenKind.DictionaryStart)
                    {
                        // The rows run into a dictionary no trailer keyword introduces: the trailer is missing,
                        // whatever that dictionary holds.
                        section.TrailerFault = XRefTrailerFault.Missing;
                        section.TrailerPosition = absolute + token.Start;
                    }
                    else
                    {
                        // What follows a token that starts no subsection cannot be known: rows misread from it on, or none.
                        section.SubsectionsUnknown = true;
                        section.Fault ??= string.Create(
                            CultureInfo.InvariantCulture,
                            $"it holds {DescribeToken(token)} at offset {absolute + token.Start}, where a subsection or the trailer should start");
                    }

                    return false;
                }

                var first = token.Integer;
                var countToken = lexer.Read();

                if (countToken.Kind == PdfTokenKind.EndOfInput || (windowFull && countToken.End >= span.Length))
                {
                    break;
                }

                // What follows a header that numbers no rows cannot be known: the rows after it are read by it.
                if (countToken.Kind != PdfTokenKind.Integer || countToken.Integer < 0 || first is < 0 or > PdfObjectId.MaxNumber)
                {
                    section.SubsectionsUnknown = true;
                    section.Fault ??= string.Create(
                        CultureInfo.InvariantCulture,
                        $"the subsection header at offset {absolute + token.Start} does not give a first object number and a count of rows");
                    return false;
                }

                // Compared by subtraction: a count as large as a long would carry first + count - 1 past what one holds.
                if (countToken.Integer > PdfObjectId.MaxNumber - first + 1)
                {
                    section.SubsectionsUnknown = true;
                    section.Fault ??= string.Create(
                        CultureInfo.InvariantCulture,
                        $"the subsection header at offset {absolute + token.Start} numbers its rows past object {PdfObjectId.MaxNumber}");
                    return false;
                }

                // A count is bounded by what it numbers, and its rows by the window, which MaxXRefSectionLength bounds: the
                // most rows an int counts lie far past any window, and a count past them describes rows that are not read.
                subsections++;
                truncated = !ReadSubsection(ref lexer, (int)first, (int)Math.Min(countToken.Integer, int.MaxValue), section, absolute);

                if (_indexFullAt >= 0)
                {
                    // The index takes no more numbers: the rows left can only repeat numbers it holds, and are not read.
                    // The trailer past them still names the catalog, the encryption and the identity of the document.
                    if (!candidate)
                    {
                        ReadTrailerPastRows(absolute + lexer.Position, section);
                    }

                    return true;
                }
            }

            if (!canGrow)
            {
                if (windowFull)
                {
                    _cutByGuard = true;
                    section.CutByLimit = true;
                    ReachLimit(
                        PdfLimit.XRefSectionLength,
                        $"The cross-reference table runs past {PdfLimitGuard.FormatLength(maxWindow)}; only the entries within it were read.",
                        absolute);

                    // The keyword may start within the window's last bytes, which it cut, though not before the table's
                    // own. A place the section may have been moved to is a guess, and its trailer is not looked for past
                    // the guard: each guess would read the file to its end.
                    if (!candidate)
                    {
                        // From the start of the line the cut fell in, outside any comment: a line holds a row, a header, a
                        // comment or the trailer keyword, which the cut may have split. Not before the table's own keyword.
                        var line = span.LastIndexOfAny((byte)'\r', (byte)'\n');
                        ReadTrailerPastRows(
                            Math.Max(absolute + (line >= 0 ? line + 1 : windowSize - TrailerKeyword.Length), absolute + section.Padding + "xref".Length),
                            section);
                    }
                }
                else
                {
                    // The file ends before the table's trailer. A table that read a row — free, in use or refused — is
                    // kept, read only in part, which the chain reports; one that read none is no section to keep, but one
                    // that is there and cannot be read. What decides is the table's own rows, not the index's, which a
                    // newer section may have filled (#207). What the rest of the table held cannot be known.
                    section.TrailerFault = XRefTrailerFault.Missing;
                    section.TrailerPosition = _source.Length;
                    section.SubsectionsUnknown = true;

                    if (section.HighestNumber < 0)
                    {
                        section.Fault ??= "the file ends before its trailer";
                        return false;
                    }

                    section.Incomplete = true;
                    return true;
                }

                // A table the guard cut is kept, its rows read as far as the guard let them, the guard's, whatever the index
                // holds (ADR 34), and what its trailer names is judged as a whole read judges it (#264). At the offset a link
                // names, its keyword is the table's own; a place the search near an offset tries is read so too, and taken as
                // any place that reads, whole or in part, is, whatever its line — its keyword in a comment or a string there
                // among them, which declares nothing (#171).
                return true;
            }

            windowSize = (int)Math.Min((long)windowSize * 4, maxWindow);
        }
    }

    /// <summary>
    /// Finds the trailer of a classic table past the rows the reader left unread from <paramref name="from"/> on — those
    /// <see cref="PdfReaderLimits.MaxXRefSectionLength"/> cut, or those past an index
    /// <see cref="PdfReaderLimits.MaxIndexEntries"/> filled —, and merges it into the document's: the rows are not
    /// indexed, but the trailer names the catalog, the encryption and the identity of the document. The chain goes no
    /// further: an older section's rows would stand for the objects the rows left unread define, or find no room. What the
    /// trailer names through <c>/Prev</c> and <c>/XRefStm</c> is judged all the same, without reading past the guard
    /// (<see cref="JudgeLinksPastGuard"/>, #264).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rows are searched a piece at a time for the first byte no table holds, so that reaching the trailer costs a
    /// read of them, not memory for them. A comment is skipped, as the lexer skips it. Anything else that is no part of a
    /// table — a damaged row, a stray token — is the section's fault, and the search goes on past it to the trailer
    /// keyword, which a reader that read the rows would reach as well: the trailer names the encryption, which must not be
    /// lost to damage the guard hid. <paramref name="from"/> starts a line, outside any comment.
    /// </para>
    /// <para>
    /// The search ends at the trailer keyword, at a dictionary no keyword introduces, or at the end of the file; the last
    /// two are a missing trailer, as they are where no guard stopped the reader, unless something no table holds came
    /// first, which is then all the section is judged by. The file is read a megabyte at a time, each piece once and every
    /// byte of it looked at before the next is read, however many comments or stray bytes it holds; what starts within a
    /// piece's last bytes, which may run past its end, starts the next. The search is made once a document: the chain
    /// ends at the section it is made for.
    /// </para>
    /// </remarks>
    private void ReadTrailerPastRows(long from, XRefSectionRecord section)
    {
        var position = from;
        var stray = -1L;
        var found = -1L;
        var dictionary = -1L;
        var comment = false;

        while (position < _source.Length && found < 0 && dictionary < 0)
        {
            var start = position;
            var length = (int)Math.Min(ScanChunkSize, _source.Length - start);
            using var window = _source.GetWindow(start, length);
            var span = window.Memory.Span;

            // What starts within the window's last bytes may run past its end: it is looked at again from a window that
            // starts with it, so that each piece of the file is read once.
            var edge = start + length < _source.Length ? span.Length - XRefProbeLength : span.Length;
            var at = 0;

            while (at < span.Length)
            {
                var rest = span[at..];
                var next = comment ? rest.IndexOfAny((byte)'\r', (byte)'\n')
                    : stray < 0 ? rest.IndexOfAnyExcept(TableBytes)
                    : rest.IndexOfAny(TrailerSearchBytes);

                if (next < 0)
                {
                    at = span.Length;
                    break;
                }

                at += next;

                if (comment)
                {
                    comment = false;
                    continue;
                }

                if (at >= edge)
                {
                    break;
                }

                var head = span[at..];

                if (head[0] == (byte)'%')
                {
                    comment = true;
                    at++;
                }
                else if (head.StartsWith("<<"u8))
                {
                    dictionary = start + at;
                    break;
                }
                else if (head[0] == (byte)'t' &&
                    (at > 0 ? !PdfCharacters.IsRegular(span[at - 1]) : StartsToken(start)) &&
                    new PdfLexer(head).Read() is var word &&
                    (word.IsKeyword(TrailerKeyword) || (start + at + word.End == _source.Length && CutTrailerKeyword(word))))
                {
                    // The trailer keyword; or the start of it, which the end of the file cuts: the rows run to the end of the
                    // file, which ends the search, as where it ends before the keyword (#207).
                    if (word.IsKeyword(TrailerKeyword))
                    {
                        found = start + at;
                    }
                    else
                    {
                        at = span.Length;
                    }

                    break;
                }
                else
                {
                    if (stray < 0)
                    {
                        stray = start + at;
                        section.Fault ??= string.Create(
                            CultureInfo.InvariantCulture,
                            $"it holds {DescribeToken(new PdfLexer(head).Read())} at offset {stray}, where a subsection or the trailer should start");
                    }

                    at++;
                }
            }

            position = start + at;
        }

        if (found < 0)
        {
            // Rows that end in the file's end, or in a dictionary no keyword introduces, lack their trailer; rows that end in
            // something else are judged by it alone.
            if (stray < 0)
            {
                section.TrailerFault = XRefTrailerFault.Missing;
            }

            section.TrailerPosition = stray >= 0 ? stray : dictionary >= 0 ? dictionary : _source.Length;
            return;
        }

        section.TrailerPosition = found;
        var guards = _guardsReached;
        _ = TryParseNumberedAt(found + TrailerKeyword.Length, DirectObject, PdfLimit.Trailer, out var value, out _, out _, out _, out var syntaxFault);

        if (_guardsReached != guards)
        {
            section.CutByLimit = true;
            section.TrailerCutByLimit = true;
        }

        if (JudgeTrailer(value, syntaxFault, section) is { } trailer)
        {
            MergeSectionTrailer(trailer, section);
            JudgeLinksPastGuard(trailer, section);
        }
    }

    /// <summary>
    /// Reads the <c>/Prev</c> and the <c>/XRefStm</c> of a classic table whose rows a guard stopped, its trailer found past
    /// them, for the chain to judge without following them (<see cref="_previousPastGuard"/>, #264): a value that names no
    /// offset is reported as a whole read reports it, but for a reference that leads to a row the guard left unread, or meets
    /// an object a guard cut on the way, which names what is unknown (<see cref="LeadsPastIndex"/>), and an offset is left for
    /// the chain to judge. Neither is read in a trailer a guard cut — what it named may lie past the cut —, nor in a table an
    /// <c>/XRefStm</c> names, whose links a whole read does not follow either.
    /// </summary>
    /// <remarks>
    /// A reference that meets an object a guard cut is the guard's here before it is a link the object guard hid: the
    /// section's own cut says that what lies past it was not checked, and it names no
    /// <see cref="XRefSectionRecord.ChainHiddenByGuard"/>.
    /// </remarks>
    private void JudgeLinksPastGuard(PdfDictionary trailer, XRefSectionRecord section)
    {
        if (section.TrailerCutByLimit || section.NamedBy == "/XRefStm")
        {
            return;
        }

        _previousPastGuard = SectionOffset(trailer, PdfName.Prev, section.Offset, out _, rowsHidden: true);
        _streamPastGuard = SectionOffset(trailer, PdfName.XRefStm, section.Offset, out _, rowsHidden: true);
    }

    /// <summary>
    /// Determines whether a token can start at <paramref name="position"/>: the byte before it is white space or a
    /// delimiter, so that it is not the end of a longer one. The search for a trailer asks where one of its windows
    /// starts, past the table's own keyword: never the file's first byte.
    /// </summary>
    private bool StartsToken(long position)
    {
        using var window = _source.GetWindow(position - 1, 1);
        return !PdfCharacters.IsRegular(window.Memory.Span[0]);
    }

    /// <summary>
    /// Parses the trailer dictionary that starts at <paramref name="position"/> in a classic table's window, and
    /// records in <paramref name="section"/> what was wrong with it.
    /// </summary>
    /// <remarks>
    /// A trailer cut by the window's edge would lose its /Root or its /Prev. It is parsed again where it
    /// starts, through a window of its own that stops at <see cref="PdfReaderLimits.MaxTrailerLength"/> — not
    /// by growing the table's, which a dictionary that never closes would grow to the size of the file, for
    /// every section of a chain. Only a dictionary written as one is a trailer: a reference is not followed to one. The
    /// values it refers to are read later, but for a <c>/Prev</c> or an <c>/XRefStm</c> written as one, which is read
    /// without loading anything while the index is still being read (<see cref="SectionOffset"/>).
    /// </remarks>
    private PdfDictionary? ReadTrailer(
        ReadOnlyMemory<byte> window, long absolute, int position, bool windowFull, XRefSectionRecord section)
    {
        var mark = _pending.GetMark();

        try
        {
            // The table's window ends the data unless it is full and the file goes on, as the table's reading took it.
            var parser = new PdfObjectParser(window, absolute, this, _pending, this, endsData: !windowFull);
            parser.Position = position;
            var parsed = parser.ParseObject();

            if (!parser.IsTruncated || !windowFull)
            {
                // Cut short where the window holds the end of the file, the dictionary runs to it unclosed.
                var malformed = parser.IsTruncated || SyntaxFaultSince(_pending, mark);
                KeepPending(mark);
                return JudgeTrailer(parsed, malformed, section);
            }
        }
        finally
        {
            _pending.RollBack(mark);
        }

        // The trailer starts inside the window, so inside the file as long as the source's length holds;
        // where nothing can be parsed the value is null, and anything that is not a dictionary is no trailer.
        var guards = _guardsReached;
        _ = TryParseNumberedAt(absolute + position, DirectObject, PdfLimit.Trailer, out var value, out _, out _, out _, out var syntaxFault);

        if (_guardsReached != guards)
        {
            section.CutByLimit = true;
            section.TrailerCutByLimit = true;
        }

        return JudgeTrailer(value, syntaxFault, section);
    }

    /// <summary>Records a section's trailer, or what is wrong with it, and returns it when it is a dictionary.</summary>
    private static PdfDictionary? JudgeTrailer(PdfObject parsed, bool malformed, XRefSectionRecord section)
    {
        if (parsed is not PdfDictionary dictionary)
        {
            section.TrailerFault = XRefTrailerFault.NotADictionary;
            return null;
        }

        section.Trailer = dictionary;

        if (malformed)
        {
            section.TrailerFault = XRefTrailerFault.Malformed;
        }

        return dictionary;
    }

    /// <summary>
    /// Keeps what an attempt recorded since <paramref name="mark"/>: moves it into the document's diagnostics, but for a
    /// fault of the syntax they keep already, met again as its object is parsed again.
    /// </summary>
    /// <param name="mark">Where the attempt's entries start in the pending diagnostics.</param>
    /// <param name="syntaxOnly">Whether only the faults of the syntax are kept, the rest dropped with a guard's cut.</param>
    private void KeepPending(PdfDiagnosticsMark mark, bool syntaxOnly = false) =>
        _pending.MoveTo(_diagnostics, mark, (this, syntaxOnly), static (entry, state) => state.Item1.IsKept(entry, state.syntaxOnly));

    /// <summary>Determines whether <paramref name="entry"/> goes into the document's diagnostics.</summary>
    private bool IsKept(PdfDiagnostic entry, bool syntaxOnly)
    {
        if (!entry.Code.StartsWith(SyntaxCodePrefix, StringComparison.Ordinal))
        {
            return !syntaxOnly;
        }

        // Once the diagnostics are full, what is added is counted and dropped, each time it is met, and need not be
        // remembered: the set holds no more than the diagnostics do. A report met again then reads as one the diagnostics
        // drop — its message not built — and is counted again, kept before or not.
        return _diagnostics.IsFull || _syntaxReported.Add(entry);
    }

    /// <summary>Determines whether a syntax error was recorded since <paramref name="mark"/>, or may have been, the capacity reached.</summary>
    private static bool SyntaxFaultSince(PdfDiagnostics diagnostics, PdfDiagnosticsMark mark)
    {
        if (diagnostics.SuppressedCount > mark.Suppressed)
        {
            return true;
        }

        for (var i = mark.Count; i < diagnostics.Count; i++)
        {
            if (diagnostics[i].Code.StartsWith(SyntaxCodePrefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Says what a token is, for a message about what stood where something else should have.</summary>
    private static string DescribeToken(PdfToken token) => token.Kind switch
    {
        PdfTokenKind.Keyword => "the keyword " + FileQuote.Keyword(token.Text),
        PdfTokenKind.Name => "a name",
        PdfTokenKind.Real => "a real number",
        PdfTokenKind.LiteralString or PdfTokenKind.HexString => "a string",
        PdfTokenKind.ArrayStart or PdfTokenKind.ArrayEnd => "an array delimiter",
        PdfTokenKind.DictionaryEnd => "the end of a dictionary",
        _ => "a byte that starts no token",
    };

    /// <summary>
    /// Determines whether <paramref name="token"/>, which runs to the end of the file, is the start of the trailer keyword —
    /// <c>t</c> up to <c>traile</c> — the end of the file cut: a table's rows run to the end of the file, its trailer lost, as
    /// where the file ends before the keyword (#207). The whole keyword is the trailer's, which a reading takes first.
    /// </summary>
    private static bool CutTrailerKeyword(PdfToken token) => token.Kind == PdfTokenKind.Keyword && TrailerKeyword.StartsWith(token.Text);

    private bool ReadSubsection(ref PdfLexer lexer, int first, int count, XRefSectionRecord section, long absolute)
    {
        for (var i = 0; i < count && _indexFullAt < 0; i++)
        {
            var offsetToken = lexer.Read();
            var generationToken = lexer.Read();
            var kindToken = lexer.Read();

            if (offsetToken.Kind == PdfTokenKind.EndOfInput ||
                generationToken.Kind == PdfTokenKind.EndOfInput ||
                kindToken.Kind == PdfTokenKind.EndOfInput)
            {
                // The window ends among the rows: a larger one reads them again, or the end of the file cut them, and the
                // table, kept read only in part, lost the rest of the subsection.
                LoseRows(first + i, (long)first + count);
                return false;
            }

            var number = first + i;

            if (offsetToken.Kind != PdfTokenKind.Integer || generationToken.Kind != PdfTokenKind.Integer ||
                !(kindToken.IsKeyword("n"u8) || kindToken.IsKeyword("f"u8)))
            {
                // A malformed row: stop this subsection rather than misread every row after it. The rows it
                // leaves unread index nothing, and the objects they stood for are the index's to find: no section read
                // after this one stands for them (#210).
                _indexIncomplete = true;
                section.Incomplete = true;
                LoseRows(number, (long)first + count);
                section.Fault ??= string.Create(
                    CultureInfo.InvariantCulture,
                    $"the row for object {number}, at offset {absolute + offsetToken.Start}, is not an offset, a generation and n or f");
                return true;
            }

            section.HighestNumber = Math.Max(section.HighestNumber, number);

            if (kindToken.IsKeyword("f"u8))
            {
                // A free row's generation serves no object: some producers give the head of the free list 65,536.
                AddFromChain(number, XRefEntry.Free, section);
            }
            else if (generationToken.Integer is >= 0 and <= PdfObjectId.MaxGeneration)
            {
                AddFromChain(number, XRefEntry.Regular(offsetToken.Integer, (int)generationToken.Integer), section);
            }
            else
            {
                // The rows after it are still in step: only this one is refused, and its object is the index's to find.
                Refuse(number, section);
                section.RefusedRow ??= string.Create(
                    CultureInfo.InvariantCulture,
                    $"the row for object {number}, at offset {absolute + offsetToken.Start}, gives it generation {generationToken.Integer}, outside 0 to {PdfObjectId.MaxGeneration}");
            }
        }

        return true;
    }
}
