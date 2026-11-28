using System.Globalization;
using System.Runtime.CompilerServices;
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
            // Reported once, by what the chain makes of it: a missing section, the first among them (#323).
            section.Fault = "lies outside the file";
            section.State = XRefSectionState.NotFound;
            return section.State;
        }

        var padding = PaddingAt(absolute, out var table, remember: !candidate);

        if (padding < 0)
        {
            // White space and comments fill a trailer's window there and run on past it: the first token past them is the
            // guard's to hide, and so is the section, as whatever else the guard cuts at an offset the chain names is (ADR
            // 34) — cut where it is named and not looked for nearby, where a search would read past the guard and may take
            // a section other than the one past them, the chain ending there. For the one startxref names, the index is
            // rebuilt, as for any guard that leaves the chain no section. A place a search near an offset tries starts at a
            // token, and never gets here: the offset is one the chain names.
            ReachLimit(
                PdfLimit.Trailer,
                $"White space and comments run past {PdfLimitGuard.FormatLength(_guard.Bound(PdfLimit.Trailer))} where a cross-reference section is named; the section was not read from there.",
                absolute);
            section.CutByLimit = true;
            section.Fault = "lies past white space and comments one of the reader's limits cut";
            section.State = XRefSectionState.Malformed;
            return section.State;
        }

        section.Padding = padding;

        if (table)
        {
            section.Kind = XRefSectionKind.Table;
            section.State = TryReadClassicTable(absolute, section, out previous, out hybrid, candidate);
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
    /// <see cref="PdfReaderLimits.MaxTrailerLength"/> bounds the white space and comments before a section apart from
    /// the section, whose windows open at its first token: a stream's at its object's header, its dictionary bounded by
    /// the same property (<see cref="TryReadXRefStream"/>), a classic table's at its keyword, bounded by
    /// <see cref="PdfReaderLimits.MaxXRefSectionLength"/> (<see cref="TryReadClassicTable"/>). The section starts where
    /// the first token does, however much white space the offset falls short by, so that what the chain reads there is
    /// what it reached there, and what a check of the place reads (<see cref="KeepsItsPlace"/>). A probe that holds
    /// white space and comments alone, or a first token its edge may cut, is read on as far as a trailer's window
    /// reaches and <see cref="XRefProbeLength"/> bytes past it: a first token that starts within that window, or where
    /// it ends, is read whole enough to tell the keyword from an object's header. White space and comments that fill
    /// the window and run on past it are the guard's (ADR 34), and so is the section past them, as whatever else the
    /// guard cuts there is: it is not read from the position, nor looked for near it, the chain ending there, and the
    /// reading reports the guard (<see cref="TryReadXRefSection"/>). The windows grow eightfold from
    /// <see cref="XRefProbeLength"/> bytes, and only where the first token, or the white space and comments before it,
    /// reach the last one's edge, so that the bytes read come to one probe where that token ends within the first,
    /// short of its edge, and otherwise to less than ten times those from the position to that token's end — white
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
                // Where the object number ends that holds the first token's first byte is told from the same bytes, where they
                // hold it: the chain asks it of the place each link reaches once it has reached a stream (NumberEndHolding).
                var numberEnd = padding < 0 || table
                    ? -1
                    : ProbedNumberEnd(probe.Memory.Span, padding, absolute + padding, endOfData: probe.Length < probeLength);
                _paddingProbes = (new PaddingProbe(absolute, padding, table, numberEnd), last);
            }

            return padding;
        }
    }

    /// <summary>
    /// Gives where the object number ends whose bytes hold <paramref name="start"/>, the first token's first byte at
    /// <paramref name="at"/> in a probe <see cref="PaddingAt"/> read, as <see cref="NumberEndHolding"/> gives it from its
    /// own window, where the probe's bytes tell: a run of signs, then digits, ending at the first other byte within
    /// <see cref="HeaderNumberReach"/> bytes; -1 where no digit follows the signs, where they or the digits run on past that
    /// reach or to the end of the data, and for a <c>-</c> past white space or a comment, which starts its run.
    /// <see cref="UnknownNumberEnd"/> where they run to the probe's edge short of both, and for a <c>-</c> at the position
    /// probed, whose run of signs may start before it, which the probe does not hold.
    /// </summary>
    /// <param name="span">The bytes the probe read, from the position probed.</param>
    /// <param name="at">Where the first token starts in <paramref name="span"/>.</param>
    /// <param name="start">Where the first token starts in the file.</param>
    /// <param name="endOfData">Whether the probe holds the end of the data, the source giving fewer bytes than asked.</param>
    private static long ProbedNumberEnd(ReadOnlySpan<byte> span, int at, long start, bool endOfData)
    {
        if (at >= span.Length)
        {
            return -1;
        }

        if (span[at] == (byte)'-')
        {
            return at > 0 ? -1 : UnknownNumberEnd;
        }

        var limit = Math.Min(span.Length, at + HeaderNumberReach);
        var rest = span[at..limit];
        var signs = rest.IndexOfAnyExcept((byte)'+', (byte)'-');
        var digits = signs < 0 ? -1 : rest[signs..].IndexOfAnyExceptInRange((byte)'0', (byte)'9');

        if (digits < 0)
        {
            return limit == at + HeaderNumberReach || endOfData ? -1 : UnknownNumberEnd;
        }

        return digits == 0 ? -1 : start + signs + digits;
    }

    /// <summary>
    /// What a probe <see cref="PaddingAt"/> read found at a position: how many bytes of white space and comments lie there
    /// before the first token, whether that token is the <c>xref</c> keyword, and where the object number ends that holds
    /// the token's first byte.
    /// </summary>
    /// <param name="Position">The position probed, in the file; -1 for no probe.</param>
    /// <param name="Padding">The bytes before the first token; -1 where they run on past a trailer's window.</param>
    /// <param name="Table">Whether the first token is the <c>xref</c> keyword.</param>
    /// <param name="NumberEnd">
    /// Where the object number ends that holds the first token's first byte (<see cref="ProbedNumberEnd"/>): -1 for none,
    /// <see cref="UnknownNumberEnd"/> where the probe's bytes do not tell.
    /// </param>
    private readonly record struct PaddingProbe(long Position, int Padding, bool Table, long NumberEnd)
    {
        /// <summary>Gets no probe: one that answers no position.</summary>
        public static PaddingProbe None => new(-1, 0, false, -1);

        /// <summary>
        /// Gives where the object number ends that holds <paramref name="start"/>, where it is the first token this probe
        /// found; <see cref="UnknownNumberEnd"/> for any other position, and where the probe's bytes do not tell.
        /// </summary>
        public long NumberEndAt(long start) => Position >= 0 && Padding >= 0 && Position + Padding == start ? NumberEnd : UnknownNumberEnd;
    }

    /// <summary>
    /// Reads the classic table whose <c>xref</c> keyword the probe found at <paramref name="absolute"/>, past the
    /// <see cref="XRefSectionRecord.Padding"/> bytes of white space and comments the position falls short by:
    /// <see cref="XRefSectionState.Read"/>, whole or in part; <see cref="XRefSectionState.Malformed"/> where it cannot be
    /// read; or <see cref="XRefSectionState.NotFound"/> where its own window reads a first token that is not the keyword.
    /// </summary>
    /// <remarks>
    /// The windows open at the keyword, as a stream's open at its object's header: the white space before it, which a
    /// trailer's window bounds apart (<see cref="PaddingAt"/>), counts toward no
    /// <see cref="PdfReaderLimits.MaxXRefSectionLength"/>, and is crossed once, by the probe, however far the windows grow.
    /// The guard's report, and the links the trailer gives, are placed at the position named, as the section is.
    /// </remarks>
    private XRefSectionState TryReadClassicTable(
        long absolute, XRefSectionRecord section, out long previous, out long hybrid, bool candidate)
    {
        previous = -1;
        hybrid = -1;
        var maxWindow = _guard.Bound(PdfLimit.XRefSectionLength);
        var windowSize = Math.Min(XRefWindow, maxWindow);
        var reserved = false;
        var at = absolute + section.Padding;
        var rowsNotTaken = _rowsNotTakenHeld?.Count ?? 0;

        while (true)
        {
            // A larger window reads the rows again: those the last one noted as not taken are given back first (#287).
            GiveBackRowsNotTaken(rowsNotTaken);
            using var window = _source.GetWindow(at, windowSize);
            var span = window.Memory.Span;
            var lexer = new PdfLexer(span);

            // A window shorter than asked holds the end of the file: what it holds is all there is, as it is for a full one
            // that ends where the file does. Any other full one may have cut the table, and is grown until the guard allows
            // no more.
            var windowFull = window.Length == windowSize && at + window.Length < _source.Length;
            var canGrow = windowFull && windowSize < maxWindow;
            var keyword = lexer.Read();
            var truncated = windowFull && keyword.End >= span.Length;

            // Each window reads the rows from the keyword on: what one lost to its own edge, a larger one reads.
            var subsections = 0;
            _rowsLost?.Clear();
            _rowsLostPast = false;
            section.WidthWrongRows = 0;
            section.WidthWrongRow = null;

            // Where the first row that is no row starts in the window: a reading on past the fault starts there, not where the
            // misreading of the rows after it stops, which may have taken the table's own trailer keyword for a row's token.
            var firstFault = -1;

            if (!truncated && !keyword.IsKeyword("xref"u8))
            {
                // A first token the window reads whole that is not the keyword, where the probe took the start of it for the
                // keyword — a source gave the probe's read back short, which the reader takes for the end of the data (#52) —,
                // is no table, as the probe would have said reading it whole: nothing there is a section.
                section.Kind = XRefSectionKind.Unknown;
                section.Fault = "holds neither the xref keyword nor an object";
                return XRefSectionState.NotFound;
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
                    section.TrailerPosition = at + token.Start;

                    // A trailer keyword that follows the xref keyword ends a table of no subsection, which indexes nothing,
                    // as one whose one subsection is 0 0 does: read as written, and reported (#222).
                    section.NoSubsection = subsections == 0;

                    var trailer = ReadTrailer(window.Memory, at, lexer.Position, windowFull, section);

                    if (trailer is not null)
                    {
                        TakeTrailer(trailer, section, absolute, out previous, out hybrid);
                    }
                    else if (section.TrailerFault == XRefTrailerFault.NotADictionary && !section.TrailerCutByLimit &&
                        section.NamedBy != "/XRefStm")
                    {
                        // The keyword followed by no dictionary loses the trailer, and with it the older sections its /Prev
                        // would name: the table is read in part, as one the end of the file cuts after a row, its rows kept
                        // and what only those sections index looked for by rebuilding the index — the section startxref names
                        // as one a /Prev names, whatever catalog its rows hold (#322). A table an /XRefStm names loses no
                        // link that way: its trailer's are not followed.
                        section.Incomplete = true;
                    }

                    return XRefSectionState.Read;
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
                        // The rows run into a dictionary no trailer keyword introduces: the trailer is missing, and the
                        // dictionary, where the trailer would be, is read as it, its /Prev and /XRefStm followed and its /Root,
                        // /Encrypt and /ID kept, which a rebuild, finding trailers by their keyword, would lose. Whether rows
                        // were lost before it with the keyword cannot be known — a damaged block may end where a subsection
                        // would start —: the table is read in part, as one whose rest is unknown, no older section's row
                        // standing for a number it does not give (#322).
                        var found = ReadTrailer(window.Memory, at, token.Start, windowFull, section);
                        section.TrailerFault = XRefTrailerFault.Missing;
                        section.TrailerPosition = at + token.Start;
                        section.Incomplete = true;
                        section.SubsectionsUnknown = true;

                        if (found is not null)
                        {
                            TakeTrailer(found, section, absolute, out previous, out hybrid);
                        }

                        return XRefSectionState.Read;
                    }

                    // What follows a token that starts no subsection cannot be known: rows misread from it on, or none.
                    section.SubsectionsUnknown = true;
                    section.Fault ??= string.Create(
                        CultureInfo.InvariantCulture,
                        $"it holds {DescribeToken(token)} at offset {at + token.Start}, where a subsection or the trailer should start");
                    ReadOnPastFault(window.Memory, at, firstFault < 0 ? token.Start : firstFault, section, candidate);
                    return XRefSectionState.Malformed;
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
                        $"the subsection header at offset {at + token.Start} does not give a first object number and a count of rows");
                    ReadOnPastFault(window.Memory, at, firstFault < 0 ? token.Start : firstFault, section, candidate);
                    return XRefSectionState.Malformed;
                }

                // Compared by subtraction: a count as large as a long would carry first + count - 1 past what one holds.
                if (countToken.Integer > PdfObjectId.MaxNumber - first + 1)
                {
                    section.SubsectionsUnknown = true;
                    section.Fault ??= string.Create(
                        CultureInfo.InvariantCulture,
                        $"the subsection header at offset {at + token.Start} numbers its rows past object {PdfObjectId.MaxNumber}");
                    ReadOnPastFault(window.Memory, at, firstFault < 0 ? token.Start : firstFault, section, candidate);
                    return XRefSectionState.Malformed;
                }

                // A count is bounded by what it numbers, and its rows by the window, which MaxXRefSectionLength bounds: the
                // most rows an int counts lie far past any window, and a count past them describes rows that are not read.
                subsections++;
                truncated = !ReadSubsection(ref lexer, span, (int)first, (int)Math.Min(countToken.Integer, int.MaxValue), section, at, ref firstFault);

                if (_indexFullAt >= 0)
                {
                    // The index takes no more numbers: the rows left can only repeat numbers it holds, and are not read.
                    // The trailer past them still names the catalog, the encryption and the identity of the document.
                    if (!candidate && ReadTrailerPastRows(at + lexer.Position, section) is { } past)
                    {
                        JudgeLinksPastGuard(past, section);
                    }

                    return XRefSectionState.Read;
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
                        section.HighestNumber < 0
                            ? $"The cross-reference table runs past {PdfLimitGuard.FormatLength(maxWindow)} before its first entry; none of its entries was read."
                            : $"The cross-reference table runs past {PdfLimitGuard.FormatLength(maxWindow)}; only the entries within it were read.",
                        absolute);

                    // The keyword may start within the window's last bytes, which it cut, though not before the table's
                    // own. A place the section may have been moved to is a guess, and its trailer is not looked for past
                    // the guard: each guess would read the file to its end.
                    if (!candidate)
                    {
                        // From the start of the line the cut fell in, outside any comment: a line holds a row, a header, a
                        // comment or the trailer keyword, which the cut may have split. Not before the table's own keyword.
                        var line = span.LastIndexOfAny((byte)'\r', (byte)'\n');

                        if (ReadTrailerPastRows(
                            Math.Max(at + (line >= 0 ? line + 1 : windowSize - TrailerKeyword.Length), at + "xref".Length),
                            section) is { } past)
                        {
                            JudgeLinksPastGuard(past, section);
                        }
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

                    // Read before no row, the table lacks its trailer, which is the trailer's fault, not one of its own: the
                    // validator says it once (#325).
                    if (section.HighestNumber < 0)
                    {
                        return XRefSectionState.Malformed;
                    }

                    section.Incomplete = true;
                    return XRefSectionState.Read;
                }

                // A table the guard cut is kept, its rows read as far as the guard let them, the guard's, whatever the index
                // holds (ADR 34), and what its trailer names is judged as a whole read judges it (#264). At the offset a link
                // names, its keyword is the table's own; a place the search near an offset tries is read so too, and taken as
                // any place that reads, whole or in part, is.
                return XRefSectionState.Read;
            }

            windowSize = (int)Math.Min((long)windowSize * 4, maxWindow);
        }
    }

    /// <summary>
    /// Finds the trailer of a classic table past the rows the reader left unread from <paramref name="from"/> on — those
    /// <see cref="PdfReaderLimits.MaxXRefSectionLength"/> cut, those past an index
    /// <see cref="PdfReaderLimits.MaxIndexEntries"/> filled, or those past a fault among them (<see cref="ReadOnPastFault"/>)
    /// —, merges it into the document's, and gives it: the rows are not indexed, but the trailer names the catalog, the
    /// encryption and the identity of the document. Past a guard, the chain goes no further: an older section's rows would
    /// stand for the objects the rows left unread define, or find no room, and what the trailer names through
    /// <c>/Prev</c> and <c>/XRefStm</c> is judged all the same, without reading past the guard
    /// (<see cref="JudgeLinksPastGuard"/>, #264).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rows are searched a piece at a time for the first byte no table holds, so that reaching the trailer costs a
    /// read of them, not memory for them. A comment is skipped, as the lexer skips it. Anything else that is no part of a
    /// table — a damaged row, a stray token — is the section's fault, and the search goes on past it to the trailer
    /// keyword, which a reader that read the rows would reach as well: the trailer names the encryption, which must not be
    /// lost to damage the guard hid. <paramref name="from"/> starts a line, or a token, outside any comment.
    /// </para>
    /// <para>
    /// The search ends at the trailer keyword, at a dictionary no keyword introduces, at the end of the table's
    /// revision, or at the end of the file. The revision ends at a <c>startxref</c> keyword, or another table's
    /// <c>xref</c>, outside any comment: a token no table holds, as a reader that read the rows would take it, so that
    /// the search never takes another revision's trailer for the table's, and ends there with none. A dictionary or the
    /// end of the file is a missing trailer, as where no guard stopped the reader, unless something no table holds came
    /// first, which is then all the section is judged by; a dictionary the rows run into is read as the trailer, as
    /// where no guard stopped the reader (#322). The bytes <paramref name="held"/> gives, the rest of the table's own
    /// window, are searched first, read already; then the file, from <see cref="XRefWindow"/> bytes, each piece eight
    /// times the last up to a megabyte, each read once and every byte of it looked at before the next is read, however
    /// many comments or stray bytes it holds; what starts within a piece's last bytes, which may run past its end,
    /// starts the next. A search reads up to the trailer, and past it the rest of the piece it ends in: less than
    /// <see cref="XRefWindow"/> bytes, or eight times what it read before. Past a guard, it is made once a document, the
    /// chain ending at the section it is made for; past a fault, once a table, from the table's first fault
    /// (<see cref="ReadOnPastFault"/>), and the next table's keyword ends it. Two tables whose searches still reach one
    /// trailer — a keyword glued to the token before it starts no token — hand on one <c>/Prev</c>, the second a loop,
    /// so that the searches of a chain reach over the file about twice at most.
    /// </para>
    /// </remarks>
    /// <param name="from">Where the search starts, in the file.</param>
    /// <param name="section">The record of the table.</param>
    /// <param name="held">The bytes from <paramref name="from"/> on that the caller holds already, or none.</param>
    /// <returns>The trailer, merged; null where none was read.</returns>
    private PdfDictionary? ReadTrailerPastRows(long from, XRefSectionRecord section, ReadOnlyMemory<byte> held = default)
    {
        var position = from;
        var stray = -1L;
        var found = -1L;
        var dictionary = -1L;
        var comment = false;
        var revisionEnded = false;
        var piece = XRefWindow;

        while (position < _source.Length && found < 0 && dictionary < 0 && !revisionEnded)
        {
            var start = position;

            // The bytes the caller holds are looked at first, read already; then the file, each piece eight times the last
            // up to a megabyte, so that a trailer a few rows on costs a read of those rows, not of a megabyte.
            var length = held.IsEmpty ? (int)Math.Min(piece, _source.Length - start) : held.Length;
            using var window = held.IsEmpty ? _source.GetWindow(start, length) : new PdfWindow(held, start, rented: null);
            piece = held.IsEmpty ? Math.Min(piece * 8, ScanChunkSize) : piece;
            held = default;
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
                else if (head[0] is (byte)'t' or (byte)'x' or (byte)'s' &&
                    (at > 0 ? !PdfCharacters.IsRegular(span[at - 1]) : StartsToken(start)) &&
                    new PdfLexer(head).Read() is var word &&
                    (word.IsKeyword(TrailerKeyword) || word.IsKeyword(XRefKeyword) || word.IsKeyword(StartXRefKeyword) ||
                        (start + at + word.End == _source.Length && CutTrailerKeyword(word))))
                {
                    // The trailer keyword; the end of the table's revision, a startxref keyword or another table's xref, which
                    // ends the search with no trailer, a token no table holds, so that no other revision's trailer is taken
                    // for the table's; or the start of the trailer keyword, which the end of the file cuts: the rows run to
                    // the end of the file, which ends the search, as where it ends before the keyword (#207).
                    if (word.IsKeyword(TrailerKeyword))
                    {
                        found = start + at;
                    }
                    else if (word.IsKeyword(XRefKeyword) || word.IsKeyword(StartXRefKeyword))
                    {
                        NoteStray(head, start + at, ref stray, section);
                        revisionEnded = true;
                    }
                    else
                    {
                        at = span.Length;
                    }

                    break;
                }
                else
                {
                    NoteStray(head, start + at, ref stray, section);
                    at++;
                }
            }

            position = start + at;
        }

        // Rows that end in the file's end lack their trailer; rows that end in something but a dictionary are judged by it
        // alone.
        if (found < 0 && (stray >= 0 || dictionary < 0))
        {
            if (stray < 0)
            {
                section.TrailerFault = XRefTrailerFault.Missing;
            }

            section.TrailerPosition = stray >= 0 ? stray : _source.Length;
            return null;
        }

        // A dictionary the rows run into with no keyword before it lacks the keyword, and is read as the trailer, as where the
        // rows are read whole (#322).
        section.TrailerPosition = found >= 0 ? found : dictionary;
        var guards = _guardsReached;
        _ = TryParseNumberedAt(
            found >= 0 ? found + TrailerKeyword.Length : dictionary, DirectObject, PdfLimit.Trailer, out var value, out _, out _, out _, out _, out var syntaxFault, out _);

        if (_guardsReached != guards)
        {
            section.CutByLimit = true;
            section.TrailerCutByLimit = true;
        }

        var trailer = JudgeTrailer(value, syntaxFault, section);

        if (found < 0)
        {
            section.TrailerFault = XRefTrailerFault.Missing;
        }

        if (trailer is not null)
        {
            MergeSectionTrailer(trailer, section);
        }

        return trailer;
    }

    /// <summary>
    /// Notes the first thing no table holds that the search for a trailer meets past the rows (<see cref="ReadTrailerPastRows"/>),
    /// which the section is judged by where no trailer follows, and names it as the section's fault where none came before.
    /// </summary>
    /// <param name="head">The bytes from that thing on.</param>
    /// <param name="position">Where it starts, in the file.</param>
    /// <param name="stray">Where the first such thing starts, in the file; -1 until one is met.</param>
    /// <param name="section">The record of the table.</param>
    private static void NoteStray(ReadOnlySpan<byte> head, long position, ref long stray, XRefSectionRecord section)
    {
        if (stray >= 0)
        {
            return;
        }

        stray = position;
        section.Fault ??= string.Create(
            CultureInfo.InvariantCulture,
            $"it holds {DescribeToken(new PdfLexer(head).Read())} at offset {position}, where a subsection or the trailer should start");
    }

    /// <summary>
    /// Reads a classic table a <c>/Prev</c> names on past a fault among its rows — a row that is no row and what follows it,
    /// a subsection header that gives no count, a stray token — to its trailer, which it merges into the document's
    /// (<see cref="ReadTrailerPastRows"/>, #324): the trailer names the catalog, the encryption and the identity of the
    /// document, and, through its <c>/Prev</c>, the older sections, which the chain reads and judges past the table
    /// (<see cref="PreviousToHandOn"/>), no older section's row standing for a number below the trailer's <c>/Size</c>
    /// (<see cref="RefuseBelowSize"/>). The table stays one that cannot be read, for its fault.
    /// </summary>
    /// <remarks>
    /// The search starts at the first fault, not where the reading stopped: past a row that is no row, the rows after it are
    /// misread, and the misreading may take the table's own trailer keyword for a row's token, stopping past it. It never
    /// crosses the end of the table's revision (<see cref="ReadTrailerPastRows"/>). A trailer
    /// <see cref="PdfReaderLimits.MaxTrailerLength"/> cuts is not followed: its <c>/Prev</c>, written whole before the cut,
    /// is judged as a whole read of the table judges it, once the table is reported (#328), an older trailer standing for the
    /// keys the cut hid, and the fault met before the cut stays the file's. The table <c>startxref</c> names is not read on,
    /// the whole index rebuilt for it; nor is a place a section may have been moved to, a guess whose search each guess would
    /// make; nor a table an <c>/XRefStm</c> names, whose links the chain does not follow (#263).
    /// </remarks>
    /// <param name="window">The table's window, read already, whose rest from the first fault on the search looks at first.</param>
    /// <param name="at">Where the window starts, in the file.</param>
    /// <param name="fault">Where the first fault starts in the window: a row that is no row, a header or a stray token.</param>
    /// <param name="section">The record of the table.</param>
    /// <param name="candidate">Whether the table is read at a place a section may have been moved to.</param>
    private void ReadOnPastFault(ReadOnlyMemory<byte> window, long at, int fault, XRefSectionRecord section, bool candidate)
    {
        if (candidate || section.NamedBy != "/Prev")
        {
            return;
        }

        _ = ReadTrailerPastRows(at + fault, section, window[fault..]);
    }

    /// <summary>
    /// Merges the trailer of the classic table <paramref name="section"/> into the document's, and gives the offsets its
    /// <c>/Prev</c> and its <c>/XRefStm</c> name for the chain to follow, or -1.
    /// </summary>
    /// <remarks>
    /// A table an <c>/XRefStm</c> names, where ISO 32000-1 names a cross-reference stream (Table 19), stands in its place: as
    /// a hybrid file's stream's own <c>/Prev</c>, what its trailer names is not read, to name a section missing or end the
    /// chain for a guard, the chain going on through the table that names it.
    /// </remarks>
    private void TakeTrailer(PdfDictionary trailer, XRefSectionRecord section, long absolute, out long previous, out long hybrid)
    {
        previous = -1;
        hybrid = -1;
        MergeSectionTrailer(trailer, section);

        if (section.NamedBy == "/XRefStm")
        {
            return;
        }

        previous = SectionOffset(trailer, PdfName.Prev, absolute, out var previousHidden);
        hybrid = SectionOffset(trailer, PdfName.XRefStm, absolute, out var streamHidden);
        section.ChainHiddenByGuard = streamHidden ?? previousHidden;
        section.ChainHiddenAtStream = streamHidden is not null;

        // A stream a guard hid holds rows newer than the older sections': the chain ends with it, as with a stream a guard
        // cut, since their rows would stand for the stream's.
        previous = streamHidden is null ? previous : -1;
    }

    /// <summary>
    /// Reads the <c>/Prev</c> and the <c>/XRefStm</c> of a classic table whose rows a guard stopped, its trailer found past
    /// them, for the chain to judge without following them (<see cref="_previousPastGuard"/>, #264): a value that names no
    /// offset is reported as a whole read reports it, but for a reference that leads to a row the guard left unread, or meets
    /// an object a guard cut on the way, which names what is unknown (<see cref="LeadsPastIndex"/>), and an offset is left for
    /// the chain to judge. In a trailer a guard cut, each is the value written whole before the cut, which is the trailer's
    /// (#328). Neither is read in a table an <c>/XRefStm</c> names, whose links a whole read does not follow either.
    /// </summary>
    /// <remarks>
    /// A reference that meets an object a guard cut is the guard's here before it is a link the object guard hid: the
    /// section's own cut says that what lies past it was not checked, and it names no
    /// <see cref="XRefSectionRecord.ChainHiddenByGuard"/>.
    /// </remarks>
    private void JudgeLinksPastGuard(PdfDictionary trailer, XRefSectionRecord section)
    {
        if (section.NamedBy == "/XRefStm")
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
            var parser = new PdfObjectParser(window, absolute, this, _pending, this, endsData: !windowFull, maxDepth: MaxNestingDepth, guard: _guard);
            parser.Position = position;
            var parsed = parser.ParseObject();

            if (!parser.IsTruncated || !windowFull)
            {
                // Cut short where the window holds the end of the file, the dictionary runs to it unclosed.
                var malformed = parser.IsTruncated || SyntaxFaultSince(_pending, mark);
                KeepPending(mark);
                CountNestingCut(NestingCut.Of(parser), 0);
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
        _ = TryParseNumberedAt(absolute + position, DirectObject, PdfLimit.Trailer, out var value, out _, out _, out _, out _, out var syntaxFault, out _);

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
        _pending.MoveTo(
            _diagnostics,
            mark,
            (this, syntaxOnly),
            static (entry, state) => state.Item1.IsKept(entry, state.syntaxOnly),
            static (code, state) => !state.syntaxOnly || IsParseReport(code));

    /// <summary>
    /// Determines whether a report under <paramref name="code"/> is one an object's parse makes of what it holds, kept once
    /// however often the object is parsed, and kept for an object a guard cut too: a fault of the syntax, or a container
    /// nested past <see cref="PdfReaderLimits.MaxNestingDepth"/>.
    /// </summary>
    private static bool IsParseReport(string code) =>
        code.StartsWith(SyntaxCodePrefix, StringComparison.Ordinal) || code == PdfDiagnosticCodes.LimitNestingDepth;

    /// <summary>Determines whether <paramref name="entry"/> goes into the document's diagnostics.</summary>
    private bool IsKept(PdfDiagnostic entry, bool syntaxOnly)
    {
        if (!IsParseReport(entry.Code))
        {
            return !syntaxOnly;
        }

        // Once the diagnostics are full, what is added is counted and dropped, each time it is met, and need not be
        // remembered: the set holds no more than the diagnostics do. A report met again then reads as one the diagnostics
        // drop — its message not built — and is counted again, kept before or not.
        return _diagnostics.IsFull || _syntaxReported.Add(entry);
    }

    /// <summary>
    /// Determines whether a syntax error was recorded since <paramref name="mark"/>, kept or dropped: an entry the capacity
    /// dropped is one only under a syntax code, so that a full record does not make another report a fault of the syntax (#169).
    /// </summary>
    private static bool SyntaxFaultSince(PdfDiagnostics diagnostics, PdfDiagnosticsMark mark)
    {
        if (diagnostics.DroppedSince(mark, SyntaxCodePrefix))
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

    /// <summary>
    /// Reads the <paramref name="count"/> rows of a subsection from object <paramref name="first"/> into the index, and
    /// gives false where the window ends among them, which a larger one reads again or the end of the file cut.
    /// </summary>
    /// <param name="lexer">The lexer over the table's window, past the subsection's header.</param>
    /// <param name="window">The table's window, which the lexer reads.</param>
    /// <param name="first">The first object number the header gives.</param>
    /// <param name="count">The count of rows the header gives.</param>
    /// <param name="section">The record of the table.</param>
    /// <param name="absolute">Where the window starts, in the file.</param>
    /// <param name="firstFault">
    /// Where the first row that is no row starts in the window, -1 until one is met: set by the first, kept past it.
    /// </param>
    private bool ReadSubsection(
        ref PdfLexer lexer, ReadOnlySpan<byte> window, int first, int count, XRefSectionRecord section, long absolute, ref int firstFault)
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
                firstFault = firstFault < 0 ? offsetToken.Start : firstFault;
                section.Fault ??= string.Create(
                    CultureInfo.InvariantCulture,
                    $"the row for object {number}, at offset {absolute + offsetToken.Start}, is not an offset, a generation and n or f");
                return true;
            }

            section.HighestNumber = Math.Max(section.HighestNumber, number);

            // A row read by its tokens reads as it was meant, whatever its width, but a reader that seeks rows by their fixed
            // width misreads every row after it: the first is described, the others counted.
            if (!IsInTwentyBytes(window, offsetToken.Start, offsetToken.End, generationToken.Start, generationToken.End, kindToken.End))
            {
                NoteRowForm(window, offsetToken.Start, i + 1 < count, section, number, absolute);
            }

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

    /// <summary>
    /// Determines, at a glance, whether the row whose offset starts at <paramref name="start"/> is in its 20 bytes, as most
    /// rows are: its offset ending at <paramref name="offsetEnd"/>, its generation running from
    /// <paramref name="generationStart"/> to <paramref name="generationEnd"/> and its keyword ending at <paramref name="end"/>
    /// where those 20 bytes place them, each number starting with a digit — so all digits, being an integer —, the end of
    /// line one of the three pairs ISO 32000-1 allows, and no third byte of it the window holds. False for any other row,
    /// which <see cref="NoteRowForm"/> then judges.
    /// </summary>
    private static bool IsInTwentyBytes(ReadOnlySpan<byte> window, int start, int offsetEnd, int generationStart, int generationEnd, int end)
    {
        return offsetEnd == start + 10 && generationStart == start + 11 && generationEnd == start + 16 && end == start + 18 &&
            end + 2 < window.Length && window[start + 10] == (byte)' ' && window[start + 16] == (byte)' ' &&
            PdfCharacters.IsDigit(window[start]) && PdfCharacters.IsDigit(window[start + 11]) &&
            window[end + 2] is not ((byte)' ' or (byte)'\r' or (byte)'\n') &&
            (window[end], window[end + 1]) is ((byte)' ', (byte)'\r') or ((byte)' ', (byte)'\n') or ((byte)'\r', (byte)'\n');
    }

    /// <summary>
    /// Counts in <paramref name="section"/> the row for object <paramref name="number"/>, whose offset starts at
    /// <paramref name="start"/> in the window, where it departs from the 20 bytes ISO 32000-1 writes it in
    /// (<see cref="FormOf"/>), and describes the first such row of the table's reading.
    /// </summary>
    /// <remarks>
    /// Called for the rows <see cref="IsInTwentyBytes"/> does not tell, out of the loop that reads the rows, from where the
    /// row starts: it lexes the row's tokens again rather than take them. Passed by value from that loop, they kept its
    /// tokens out of registers through every row, and the description compiled there grew its frame by about 800 bytes —
    /// a table of 300,000 rows opened a seventh slower, one of a row a subsection a fifth (<c>IndexBenchmarks</c>,
    /// <c>Table</c> and <c>Spread</c>); passed their places, a few hundredths at most.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void NoteRowForm(ReadOnlySpan<byte> window, int start, bool followed, XRefSectionRecord section, int number, long absolute)
    {
        // The row's tokens lexed again from its first, as the loop lexed them.
        var lexer = new PdfLexer(window, start);
        var offset = lexer.Read();
        var generation = lexer.Read();
        var kind = lexer.Read();
        var form = FormOf(window, offset, generation, kind, followed);

        if (form is RowForm.Twenty or RowForm.Unjudged)
        {
            return;
        }

        section.WidthWrongRows++;
        section.WidthWrongRow ??= string.Create(
            CultureInfo.InvariantCulture,
            $"the row for object {number}, at offset {absolute + offset.Start}, {Describe(form, window, offset, generation)}");
    }

    /// <summary>
    /// Gives how the row whose tokens are <paramref name="offset"/>, <paramref name="generation"/> and <paramref name="kind"/>
    /// departs from the 20 bytes ISO 32000-1 (7.5.4) writes each row in: a ten-digit offset, a space, a five-digit generation,
    /// a space, <c>n</c> or <c>f</c>, and an end of line of a space and a carriage return, a space and a line feed, or a
    /// carriage return and a line feed, with no third byte of it where another row of the subsection follows the row
    /// (<paramref name="followed"/>).
    /// </summary>
    /// <remarks>
    /// A row whose end of line the window does not hold is not judged: a larger window reads it again, or a guard cut it,
    /// and what lies past the cut is the guard's.
    /// </remarks>
    private static RowForm FormOf(ReadOnlySpan<byte> window, PdfToken offset, PdfToken generation, PdfToken kind, bool followed)
    {
        var end = kind.End;

        if (end + 2 > window.Length)
        {
            return RowForm.Unjudged;
        }

        if (!IsDigits(window[offset.Start..offset.End], 10))
        {
            return RowForm.OffsetDigits;
        }

        if (generation.Start != offset.End + 1 || kind.Start != generation.End + 1 || window[offset.End] != (byte)' ' ||
            window[generation.End] != (byte)' ')
        {
            return RowForm.Separators;
        }

        if (!IsDigits(window[generation.Start..generation.End], 5))
        {
            return RowForm.GenerationDigits;
        }

        // The run of spaces, carriage returns and line feeds past the keyword is the row's end of line, which takes its two
        // bytes: a run of one or of three makes the row 19 bytes or 21 or more — a reader seeking rows by width misreads the
        // next —, and one of two is one of the three pairs ISO 32000-1 allows. Past a subsection's last row, which no row
        // follows for a reader to misread, the run goes on as many writers write it, a blank line before the trailer: only
        // the row's two bytes are judged, as where the window does not hold a third.
        var (first, second) = (window[end], window[end + 1]);

        if (!IsEndOfLineByte(first))
        {
            return RowForm.EndOfLine;
        }

        if (!IsEndOfLineByte(second))
        {
            return first == (byte)' ' ? RowForm.EndOfLine : RowForm.OneEndOfLineByte;
        }

        if (followed && end + 2 < window.Length && IsEndOfLineByte(window[end + 2]))
        {
            return RowForm.MoreEndOfLineBytes;
        }

        return (first, second) is ((byte)' ', (byte)'\r') or ((byte)' ', (byte)'\n') or ((byte)'\r', (byte)'\n') ? RowForm.Twenty : RowForm.EndOfLine;

        static bool IsDigits(ReadOnlySpan<byte> field, int length) =>
            field.Length == length && field.IndexOfAnyExceptInRange((byte)'0', (byte)'9') < 0;

        static bool IsEndOfLineByte(byte value) => value is (byte)' ' or (byte)'\r' or (byte)'\n';
    }

    /// <summary>Describes <paramref name="form"/>, how a row departs from the 20 bytes ISO 32000-1 writes it in, for a finding.</summary>
    private static string Describe(RowForm form, ReadOnlySpan<byte> window, PdfToken offset, PdfToken generation) => form switch
    {
        RowForm.OffsetDigits => $"whose offset {FileQuote.Keyword(window[offset.Start..offset.End])} is not ten digits",
        RowForm.Separators => "whose fields are not parted by single spaces",
        RowForm.GenerationDigits => $"whose generation {FileQuote.Keyword(window[generation.Start..generation.End])} is not five digits",
        RowForm.OneEndOfLineByte => "which ends with one end-of-line byte, not two",
        RowForm.MoreEndOfLineBytes => "which ends with more than two end-of-line bytes",
        _ => "whose end of line is neither a space and a carriage return or a line feed, nor a carriage return and a line feed",
    };

    /// <summary>How a row read by its tokens departs from the 20 bytes ISO 32000-1 (7.5.4) writes each row in.</summary>
    private enum RowForm
    {
        /// <summary>The row is written in its 20 bytes.</summary>
        Twenty,

        /// <summary>The window does not hold the row's end of line.</summary>
        Unjudged,

        /// <summary>The offset is not ten digits.</summary>
        OffsetDigits,

        /// <summary>The offset, the generation and the keyword are not parted by one space each.</summary>
        Separators,

        /// <summary>The generation is not five digits.</summary>
        GenerationDigits,

        /// <summary>The row ends with one end-of-line byte, a carriage return or a line feed no other follows.</summary>
        OneEndOfLineByte,

        /// <summary>The row ends with more than two spaces, carriage returns and line feeds, a row of its subsection following.</summary>
        MoreEndOfLineBytes,

        /// <summary>The keyword is followed by no end of line ISO 32000-1 allows a row, of two bytes or of one.</summary>
        EndOfLine,
    }
}
