using System.Globalization;
using System.Runtime.ExceptionServices;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO;

internal sealed partial class PdfFileReader
{
    private bool HasUsableRoot()
    {
        var root = Trailer.GetDictionary(PdfName.Root);
        return root is not null && (root.IsOfType(PdfName.Catalog) || root.ContainsKey(PdfName.Pages));
    }

    private PdfObject LoadObject(PdfObjectId id)
    {
        if (!_xref.TryGet(id.Number, out var entry))
        {
            // A reference to an object the file does not define is null (ISO 32000-1, 7.3.10): only an index damage
            // may have lost entries from is rebuilt to look for it, once. One a guard cut or filled reads it as null until
            // the guard is lifted (ADR 34): a rebuild would scan the file for what the guard's report already names.
            if (!_repaired && _indexIncomplete)
            {
                Repair();

                if (_xref.TryGet(id.Number, out entry))
                {
                    return LoadFromEntry(id, entry);
                }
            }

            // While a rebuilt index takes in what its object streams hold, the object may be in one not
            // expanded yet: that null is not the object's, and is not kept.
            if (_expandingObjectStreams)
            {
                _reentries++;
            }

            return NotIndexed();
        }

        return LoadFromEntry(id, entry);
    }

    /// <summary>
    /// Answers null for an object the index holds no entry for; or, for a document opened to throw on a guard, throws each
    /// time a guard that stopped the index short of the file leaves one out — <see cref="PdfReaderLimits.MaxIndexEntries"/>
    /// filled it, or <see cref="PdfReaderLimits.MaxRebuildReadPasses"/> kept the scan of the rebuild that made it from
    /// reading data that holds a header —, as an object read again past its guard throws again, where the guard was
    /// reported. The exception is a new one each time, which no load or decoding keeps (<see cref="IsKeptByNoTable"/>).
    /// </summary>
    /// <exception cref="PdfLimitExceededException">
    /// The index is full, or a rebuild's scan left data that holds a header unread, and the document was opened to throw on
    /// a guard.
    /// </exception>
    private PdfNull NotIndexed()
    {
        if (!_guard.ThrowsOnLimit)
        {
            return PdfNull.Instance;
        }

        try
        {
            if (_indexFull)
            {
                _guard.Reach(PdfLimit.IndexEntries, null, IndexFullMessage(), _indexFullAt);
            }
            else if (_rebuildReadsBoundedAt >= 0)
            {
                _guard.Reach(PdfLimit.RebuildReads, null, RebuildReadsMessage(), _rebuildReadsReportedAt);
            }
        }
        catch (PdfLimitExceededException exception)
        {
            // Thrown anew at each lookup, which reads nothing: neither the load of the number nor any load or decoding it
            // passes through keeps it (IsKeptByNoTable).
            _lookupThrown = exception;
            throw;
        }

        return PdfNull.Instance;
    }

    private PdfObject LoadFromEntry(PdfObjectId id, XRefEntry entry)
    {
        var loaded = entry.Kind switch
        {
            XRefEntryKind.Regular => LoadRegularObject(id, entry),
            XRefEntryKind.Compressed => LoadCompressedObject(id, entry),
            _ => PdfNull.Instance,
        };

        // Null for an object the index holds in use means the reader gave up on it, unless the object is a
        // literal null: the loads say which.
        if (loaded is { } value)
        {
            _unproduced.Remove(id.Number);
            return value;
        }

        _unproduced.Add(id.Number);
        return PdfNull.Instance;
    }

    /// <summary>
    /// Loads an object <paramref name="entry"/> places, in at most three attempts: where the index says, in the
    /// neighborhood — in the order <see cref="TryParseWhereEntryPlaces"/> takes them —, and wherever a rebuilt index says.
    /// </summary>
    /// <remarks>
    /// The attempts are counted rather than chained. An earlier version let relocation call back into
    /// loading, and a fuzzed file drove the two into each other until the stack ran out — a file killing
    /// the process is the exact outcome the reader exists to prevent.
    /// </remarks>
    private PdfObject? LoadRegularObject(PdfObjectId id, XRefEntry entry)
    {
        var offset = PositionOf(entry);

        if (!IsInFile(offset))
        {
            // No position in the file names the entry, which the index does not keep where it was read. Only a row of the
            // file places an object there: one the reader found is in the file.
            _diagnostics.Warn(
                PdfDiagnosticCodes.XRefEntryOutOfRange,
                string.Create(
                    CultureInfo.InvariantCulture, $"The entry of object {id.Number} places it at offset {DescribeOffset(entry.Offset)}, outside the file."));
        }

        if (TryParseWhereEntryPlaces(id.Number, entry, RecordedAnywhere, out var found, out var nearby, out var generation, out _))
        {
            if (nearby < 0)
            {
                return found;
            }

            _diagnostics.Repair(
                PdfDiagnosticCodes.XRefOffsetAdjusted,
                string.Create(CultureInfo.InvariantCulture, $"Object {id.Number} was found {CountText.Distance(nearby - offset, "where the index said")}."),
                nearby);

            // The entry records the header found, as a rebuilt one does: not the generation of the reference that asked
            // first, which would make the index depend on the order objects were asked for (#118).
            PreserveChainIndex();
            _xref.Set(id.Number, XRefEntry.Found(nearby - _headerOffset, generation));
            return found;
        }

        if (_repaired)
        {
            return null;
        }

        Repair();

        return _xref.TryGet(id.Number, out var rebuilt) &&
               rebuilt.Kind == XRefEntryKind.Regular &&
               TryParseObjectAt(id.Number, PositionOf(rebuilt), out var afterRebuild)
            ? afterRebuild
            : null;
    }

    /// <summary>
    /// Parses object <paramref name="number"/> at an exact offset, growing the window while the object runs
    /// past its end. Returns false when nothing with that number is there.
    /// </summary>
    private bool TryParseObjectAt(int number, long offset, out PdfObject value, long recordedAt = RecordedAnywhere) =>
        TryParseNumberedAt(offset, number, PdfLimit.Object, out value, out _, out _, out _, out _, recordedAt: recordedAt);

    /// <summary>
    /// Parses object <paramref name="number"/> where <paramref name="entry"/> places it, as a load looks for it: at the offset
    /// it gives, unless that is a row the file gives that cuts a number (<see cref="RowCutsNumber"/>); else at the header the
    /// search near the offset chooses (<see cref="TryFindObjectHeader"/>), which <paramref name="nearby"/> gives; else, where
    /// the row cuts a number and that search finds no header of the object, at the row as written.
    /// </summary>
    /// <remarks>
    /// Offsets are commonly off by a few bytes in files from careless tools, so the neighborhood is searched before the index
    /// is given up on entirely. A row that cuts a number may land inside the number of another object's header, the object's
    /// own lying near it; or on a header glued to the digits before it — an update appended with no end of line to a file
    /// cut after a digit —, which the bytes do not tell from the first. Where the search finds no other header of the
    /// object, the row is read as written, as a reader that lexes from it reads it, and the validator says the reader cannot
    /// vouch for it (<see cref="Validation.PdfValidationRuleIds.XRefEntryUnconfirmed"/>).
    /// </remarks>
    /// <param name="number">The object's number.</param>
    /// <param name="entry">The regular entry that places it.</param>
    /// <param name="recordedAt">Where the object's header must start for what the reading notes by its number to be noted.</param>
    /// <param name="value">The object read.</param>
    /// <param name="nearby">Where the header the search near the offset chose starts; -1 where it was read at the offset.</param>
    /// <param name="generation">The generation the header the search chose gives.</param>
    /// <param name="headerAt">
    /// Where the header of the object read starts, past the white space and comments before it: the place a load takes the
    /// entry to give, which a row a few bytes early does not.
    /// </param>
    private bool TryParseWhereEntryPlaces(
        int number, XRefEntry entry, long recordedAt, out PdfObject value, out long nearby, out int generation, out long headerAt)
    {
        var offset = PositionOf(entry);
        nearby = -1;

        if (TryParseNumberedAt(
            offset, number, PdfLimit.Object, out value, out var found, out headerAt, out _, out _, recordedAt: recordedAt, atRow: !entry.FoundByReader))
        {
            generation = 0;
            return true;
        }

        if (TryFindObjectHeader(number, entry, reportsBound: true, out var header, out generation, out _))
        {
            nearby = header;
            headerAt = header;
            return TryParseObjectAt(number, header, out value, recordedAt);
        }

        headerAt = offset;
        return found == HeaderCutsNumber && TryParseObjectAt(number, offset, out value, recordedAt);
    }

    /// <summary>
    /// Parses what starts at an exact offset — object <paramref name="number"/>, any object for
    /// <see cref="AnyObject"/>, or a direct object for <see cref="DirectObject"/> —, growing the window while it
    /// runs past its end, up to the bound of <paramref name="limit"/>, and gives the number of the object found there —
    /// 0 for a direct object. Returns false when nothing with the expected number is there.
    /// </summary>
    /// <remarks>
    /// What the parser notices is held back until an attempt is kept. An attempt that ran out of window
    /// sees a cut token, a missing <c>endstream</c> or an object that stops mid-way: artifacts of the window,
    /// not of the file, and reporting them would put a syntax error the file does not have in the document's
    /// diagnostics — with the real ones reported once per attempt. What nested loads report, such as a
    /// relocated <c>/Length</c> object, goes straight to the document's diagnostics and stays there. An object
    /// that still runs past the window once the guard allows no larger one is kept as far as it was read, and
    /// the guard is reported in place of what the cut made the parser notice. One whose value the window holds whole, only what
    /// follows it past the edge, is not read again where the window holds a header an index places past it
    /// (<see cref="WindowHoldsPlacedHeader"/>).
    /// </remarks>
    /// <param name="offset">Where the object starts in the file.</param>
    /// <param name="number">The object's number, <see cref="AnyObject"/> or <see cref="DirectObject"/>.</param>
    /// <param name="limit">The guard that bounds the window.</param>
    /// <param name="value">The object read.</param>
    /// <param name="foundNumber">
    /// The number of the object found, even when <paramref name="keep"/> refuses it; <see cref="HeaderCutsNumber"/> where
    /// <paramref name="atRow"/> and the row cuts a number, where nothing is read.
    /// </param>
    /// <param name="headerAt">Where in the file the object read starts, past the white space and comments before it.</param>
    /// <param name="digitsAt">
    /// Where in the file the digits of the object's number start, past the run of signs it is written with, where it is
    /// (<see cref="PdfObjectParser.HeaderDigits"/>); <paramref name="headerAt"/> otherwise, and for a direct object.
    /// </param>
    /// <param name="syntaxFault">
    /// Whether the reading kept met a fault of the syntax, or may have, the pending diagnostics full: reported now or
    /// already, by an earlier reading of the same bytes.
    /// </param>
    /// <param name="initialWindow">The window the first attempt reads through.</param>
    /// <param name="keep">
    /// Whether what was read is what was looked for: when it says no, nothing the reading met is reported or recorded —
    /// a search for a stream's <c>endstream</c> aside, which is kept as every search is (<see cref="IPdfStreamDataProvider.FindEndStream"/>)
    /// —, a guard its window reached included, and the method returns false.
    /// </param>
    /// <param name="recordedAt">
    /// Where the object's header must start for what the reading notes of it by its number — its <c>endobj</c>, its stream's
    /// length, a cut — to be noted: <see cref="RecordedAnywhere"/>, <see cref="RecordedNowhere"/>, or the place of the
    /// definition the reader serves, which a reading where the chain places the object may not be (#255).
    /// </param>
    /// <param name="atRow">
    /// Whether the offset is a row the file gives, which is not read where it cuts a number (<see cref="RowCutsNumber"/>):
    /// the byte before it is read with the first window, and a cut row gives <see cref="HeaderCutsNumber"/>, for its caller to
    /// search near it first (<see cref="TryParseWhereEntryPlaces"/>).
    /// </param>
    private bool TryParseNumberedAt(
        long offset,
        int number,
        PdfLimit limit,
        out PdfObject value,
        out int foundNumber,
        out long headerAt,
        out long digitsAt,
        out bool syntaxFault,
        int initialWindow = InitialObjectWindow,
        Func<PdfObject, bool>? keep = null,
        long recordedAt = RecordedAnywhere,
        bool atRow = false)
    {
        value = PdfNull.Instance;
        foundNumber = 0;
        headerAt = offset;
        digitsAt = offset;
        syntaxFault = false;

        if (offset < 0 || offset >= _source.Length)
        {
            return false;
        }

        var maxWindow = _guard.Bound(limit);
        var windowSize = Math.Min(initialWindow, maxWindow);
        var mark = _pending.GetMark();
        var lead = atRow && offset > 0 ? ByteBeforeRow : 0;

        try
        {
            while (true)
            {
                using var window = _source.GetWindow(offset - lead, windowSize + lead);
                var bytes = window.Memory;

                if (lead > 0)
                {
                    if (RowCutsNumber(bytes.Span))
                    {
                        foundNumber = (int)HeaderCutsNumber;
                        return false;
                    }

                    bytes = bytes[Math.Min(lead, bytes.Length)..];
                    lead = 0;
                }

                // A window that ends where the file does cut nothing, however long it is: what it holds is all there is. The
                // largest the guard allows drops a token its edge parts (#262, #279).
                var cut = bytes.Length == windowSize && offset + bytes.Length < _source.Length;
                var parser = new PdfObjectParser(
                    bytes, offset, this, _pending, this, endsData: !cut, maxDepth: MaxNestingDepth, guard: _guard, guardEdge: windowSize >= maxWindow);
                PdfObject parsed;

                if (number == DirectObject)
                {
                    parsed = parser.ParseObject();
                }
                else if (!parser.TryReadIndirectObject(out var found, out parsed))
                {
                    // An object header the window's edge cut is read again in a larger one; anything else
                    // that is not an object header is not the object.
                    if (!parser.IsTruncated || !cut)
                    {
                        return false;
                    }

                    _pending.RollBackToRetry(mark);

                    if (windowSize >= maxWindow)
                    {
                        ReachLimit(limit, LimitSubject(number, maxWindow), offset);
                        return false;
                    }

                    windowSize = (int)Math.Min((long)windowSize * 8, maxWindow);
                    continue;
                }
                else if (number != AnyObject && found.Number != number)
                {
                    return false;
                }
                else
                {
                    foundNumber = found.Number;
                }

                var lengthFault = parser.LengthFault;

                // Each object the index places in a long run of comment lines would otherwise read every line after it for what
                // follows its value: a rebuild, which loads them all, or a validation, would take time in the square of the
                // lines (#362). At the largest window the guard allows, the guard is reached and reported, as without the rule.
                var truncated = parser.IsTruncated && cut &&
                    (windowSize >= maxWindow || !WindowHoldsPlacedHeader(foundNumber, offset, bytes.Span, parser.WholeValueEnd));

                if (truncated)
                {
                    if (windowSize < maxWindow)
                    {
                        _pending.RollBackToRetry(mark);
                        windowSize = (int)Math.Min((long)windowSize * 8, maxWindow);
                        continue;
                    }

                    if (keep?.Invoke(parsed) == false)
                    {
                        return false;
                    }

                    // The guard is reported first, so that what the parse met before the cut cannot crowd it out of full
                    // diagnostics: it is the one report that names the property to raise. What the parser met before the cut
                    // is the file's, and is kept after it; it reported nothing of the token the window's edge cut, which is
                    // the guard's (ADR 34), and what it found of a stream is dropped with the cut.
                    syntaxFault = SyntaxFaultSince(_pending, mark);
                    ReachLimit(limit, LimitSubject(number, maxWindow), offset);
                    MarkCutByGuard(parsed, offset + bytes.Length);

                    // What the chain reads of a cross-reference stream is the section's, which records its cut: the object of that
                    // number a load reads is noted when it is read (#175).
                    if (foundNumber > 0 && number != AnyObject && Records(recordedAt, offset + parser.HeaderStart))
                    {
                        _cutAtLimit.Add(foundNumber);
                    }

                    KeepPending(mark, syntaxOnly: true);

                    // A stream the guard cut ran into the window's edge, not the file's end: what the parser found of it
                    // is the limit's, and was dropped with what it reported.
                    lengthFault = null;
                }
                else if (keep?.Invoke(parsed) == false)
                {
                    return false;
                }

                syntaxFault |= SyntaxFaultSince(_pending, mark);
                KeepPending(mark);
                CountNestingCut(NestingCut.Of(parser), number != AnyObject && Records(recordedAt, offset + parser.HeaderStart) ? foundNumber : 0);

                // A reading of a definition the reader no longer serves notes nothing of the number (#255).
                var noted = Records(recordedAt, offset + parser.HeaderStart);
                RecordEndObj(noted ? number : DirectObject, foundNumber, parser.EndObj, cut, offset);
                RecordStreamLength(noted ? number : DirectObject, foundNumber, parsed, lengthFault);
                value = parsed;
                headerAt = offset + parser.HeaderStart;
                digitsAt = offset + parser.HeaderDigits;
                return true;
            }
        }
        finally
        {
            // Whatever an attempt that was not kept noticed is dropped with it, however it ended.
            _pending.RollBack(mark);
        }
    }

    /// <summary>
    /// Determines whether <paramref name="bytes"/>, read from <paramref name="offset"/> for object <paramref name="number"/>,
    /// hold a header an index places past where the object's value ends, at <paramref name="valueEnd"/>, the parse having
    /// found that value whole (<see cref="PdfObjectParser.WholeValueEnd"/>): the first row past that end — of the chain's
    /// index, and of the rebuilt one after a rebuild that followed the chain, each index's entry of the object stepped over
    /// — from which a header lexes forward as a load reads one (<see cref="RowLexer"/>), rows from which none does stepped
    /// over. False where the value is not whole, where no such row lies within <paramref name="bytes"/>, and where the
    /// lexing from a row reaches their edge before it can say.
    /// </summary>
    /// <remarks>
    /// Where they do, the load keeps the object as read rather than read it again in a larger window
    /// (<see cref="TryParseNumberedAt"/>): what lies past the value is that header's object's, and a larger window would read
    /// the value alike. The value is kept whole; its <c>endobj</c> is unseen, recorded neither found nor missing; a
    /// dictionary whose <c>stream</c> keyword lies past that header is read as a dictionary, and an integer whose generation
    /// and <c>R</c> lie past it as an integer; and no guard is reached for it. Without it, every object a long run of
    /// comment lines places read every line after it, its window grown until what follows its value showed, and a rebuild,
    /// which loads every object it places, or a validation, which loads them all, took time in the square of the lines
    /// (#362). No object of a valid file is read so: the chain's index of one places no header past an object's value
    /// and before its <c>endobj</c>, and a rebuilt index places one there only among the objects the scan takes in stream
    /// data it cannot confirm, an attached file's, which are not the file's own. At the largest window the guard allows, the rule is not asked, and the guard is reached and reported
    /// where it cuts, so that a lower <see cref="PdfReaderLimits.MaxObjectLength"/> never changes what is served without a
    /// report (ADR 34). The rows asked are those within <paramref name="bytes"/> alone, each a logarithm of an
    /// index, in the order of their positions, and the lexing from each shares what the ones before learned of the white
    /// space and comments it crosses: the bytes are read a bounded number of times, whatever the gaps between a header's
    /// tokens.
    /// </remarks>
    private bool WindowHoldsPlacedHeader(int number, long offset, ReadOnlySpan<byte> bytes, int valueEnd)
    {
        if (valueEnd < 0)
        {
            return false;
        }

        var index = SearchIndex;
        var rebuilt = _repaired && !ReferenceEquals(index, _xref) ? _xref : null;
        var own = OwnOffset(index, number);
        var ownRebuilt = rebuilt is null ? long.MinValue : OwnOffset(rebuilt, number);
        var start = offset - _headerOffset;
        var lexer = new RowLexer(bytes);

        for (var from = valueEnd; from < bytes.Length;)
        {
            var row = Math.Min(index.FirstOffsetAfter(start + from - 1), rebuilt?.FirstOffsetAfter(start + from - 1) ?? long.MaxValue);

            if (row == long.MaxValue || row - start >= bytes.Length)
            {
                return false;
            }

            var at = (int)(row - start);
            from = at + 1;

            // The object's own row may lie past its value, where the search near it found its header.
            if (row == own || row == ownRebuilt)
            {
                continue;
            }

            var reading = lexer.Read(at);

            if (reading != RowReading.None)
            {
                return reading == RowReading.Header;
            }
        }

        return false;
    }

    /// <summary>Gives the offset <paramref name="index"/>'s entry of object <paramref name="number"/> places it at, where it places it.</summary>
    private static long OwnOffset(PdfXRefTable index, int number) =>
        index.TryGet(number, out var own) && own.Kind == XRefEntryKind.Regular ? own.Offset : long.MinValue;

    /// <summary>What lexing forward from a row says of it (<see cref="RowLexer.Read"/>).</summary>
    private enum RowReading
    {
        /// <summary>No header starts at the row.</summary>
        None,

        /// <summary>A header starts at the row.</summary>
        Header,

        /// <summary>The lexing reached the edge of the bytes before it could say.</summary>
        Undecided,
    }

    /// <summary>What a token read where a header's number may start is (<see cref="RowLexer"/>).</summary>
    private enum RowToken
    {
        /// <summary>Anything but an integer.</summary>
        Other,

        /// <summary>An integer.</summary>
        Integer,

        /// <summary>A token the edge of the bytes cut, which may go on past it.</summary>
        Cut,
    }

    /// <summary>
    /// Lexes forward from the rows <see cref="WindowHoldsPlacedHeader"/> asks about, in the order of their positions, each as a
    /// load reads a header from it: an integer whose token starts at the row — the byte before it ends a token, as for the walk
    /// back from an <c>obj</c> keyword (<see cref="ReadHeaderBackwards"/>) —, an integer, and <c>obj</c>, as much white space
    /// and as many comments between them as there are.
    /// </summary>
    /// <remarks>
    /// Lexed apart, the rows of a comment line of headers would each cross the rest of the line, and the comment lines after
    /// it a gap holds: time in the square of the lines. What a lexing learns is kept for the rows after it. A comment that
    /// starts within the stretch the last one crossed ends where it did. A lexing that reaches, between tokens, an end of line
    /// within the last gap that crossed one goes on as that gap did, to the token after it. And the second token last reached
    /// past such a gap is read once, with the gap after it, for every row whose gap reaches it. A gap that crosses an end of
    /// line ends at a token no earlier than the one the gaps of the rows before reached, but for a token within the comment
    /// a gap before crossed, which only its own line reaches: what is kept is replaced only by what lies past it, but for
    /// once each such token. A digit run is read only by the row at its start and the two before it, the white space after
    /// it by two rows, and no other token is read but where its first byte may start an integer or <c>obj</c>: each byte is
    /// read a bounded number of times.
    /// </remarks>
    private ref struct RowLexer
    {
        private readonly ReadOnlySpan<byte> _bytes;

        /// <summary>Where the stretch the last comment crossed starts: no end of line lies from there to <see cref="_commentEnd"/>.</summary>
        private int _commentFrom;

        /// <summary>Where the last comment ends, at an end of line or the end of the bytes; -1 before any.</summary>
        private int _commentEnd;

        /// <summary>Where the first end of line the last gap that crossed one crossed lies.</summary>
        private int _gapFrom;

        /// <summary>Where the token after that gap starts, or the end of the bytes; -1 before any.</summary>
        private int _gapTo;

        /// <summary>The second token last reached past a gap that crossed an end of line; -1 before any.</summary>
        private int _secondAt;

        /// <summary>What that token is.</summary>
        private RowToken _secondToken;

        /// <summary>Its value, where it is an integer.</summary>
        private long _secondValue;

        /// <summary>Where the token after it starts, or the end of the bytes, where it is an integer.</summary>
        private int _secondNext;

        /// <summary>Lexes the rows of <paramref name="bytes"/>, a window whose edge the file goes on past.</summary>
        public RowLexer(ReadOnlySpan<byte> bytes)
        {
            _bytes = bytes;
            _commentEnd = _gapTo = _secondAt = -1;
        }

        /// <summary>Says whether a header starts at <paramref name="at"/>, a row past those asked about before.</summary>
        public RowReading Read(int at)
        {
            // A row lies past where the object's value starts, never at the first byte.
            if (!PdfCharacters.EndsToken(_bytes[at - 1]) && !EndsEndOfFileMarker(_bytes, at - 1))
            {
                return RowReading.None;
            }

            var first = ReadInteger(at, out var number, out var end);

            if (first != RowToken.Integer || number is <= 0 or > PdfObjectId.MaxNumber)
            {
                return first == RowToken.Cut ? RowReading.Undecided : RowReading.None;
            }

            var second = Skip(end, out var crossed);

            if (second >= _bytes.Length)
            {
                return RowReading.Undecided;
            }

            RowToken token;
            long generation;
            int third;

            if (crossed && second == _secondAt)
            {
                (token, generation, third) = (_secondToken, _secondValue, _secondNext);
            }
            else
            {
                token = ReadInteger(second, out generation, out var secondEnd);
                third = token == RowToken.Integer ? Skip(secondEnd, out _) : secondEnd;

                // Kept only for a token past a gap that crossed an end of line, which other rows may reach: one on the row's
                // own line is no other row's second.
                if (crossed)
                {
                    (_secondAt, _secondToken, _secondValue, _secondNext) = (second, token, generation, third);
                }
            }

            if (token != RowToken.Integer || generation is < 0 or > PdfObjectId.MaxGeneration)
            {
                return token == RowToken.Cut ? RowReading.Undecided : RowReading.None;
            }

            return third >= _bytes.Length ? RowReading.Undecided : ReadsObj(third);
        }

        /// <summary>Reads the token at <paramref name="at"/> where its first byte may start an integer.</summary>
        private readonly RowToken ReadInteger(int at, out long value, out int end)
        {
            value = 0;
            end = at;

            if (!PdfCharacters.IsDigit(_bytes[at]) && _bytes[at] is not ((byte)'+' or (byte)'-'))
            {
                return RowToken.Other;
            }

            var token = new PdfLexer(_bytes, at).Read();
            end = token.End;
            value = token.Integer;
            return token.End >= _bytes.Length ? RowToken.Cut
                : token.Kind == PdfTokenKind.Integer ? RowToken.Integer
                : RowToken.Other;
        }

        /// <summary>Says whether the token at <paramref name="at"/> is the keyword <c>obj</c>.</summary>
        private readonly RowReading ReadsObj(int at)
        {
            var keyword = ObjKeyword;
            var held = _bytes[at..];

            // The byte after the keyword tells obj from a longer word.
            if (held.Length <= keyword.Length)
            {
                return keyword.StartsWith(held) ? RowReading.Undecided : RowReading.None;
            }

            return held.StartsWith(keyword) && !PdfCharacters.IsRegular(held[keyword.Length]) ? RowReading.Header : RowReading.None;
        }

        /// <summary>
        /// Skips the white space and comments from <paramref name="at"/>, right after a token, and gives where the next token
        /// starts, or the end of the bytes; <paramref name="crossed"/> says whether they cross an end of line.
        /// </summary>
        private int Skip(int at, out bool crossed)
        {
            var firstEndOfLine = -1;

            while (at < _bytes.Length)
            {
                var current = _bytes[at];

                if (current is (byte)'\r' or (byte)'\n')
                {
                    if (firstEndOfLine < 0)
                    {
                        firstEndOfLine = at;
                    }

                    if (at >= _gapFrom && at < _gapTo)
                    {
                        // The last gap that crossed an end of line went on from this one, between tokens as this lexing is.
                        at = _gapTo;
                        break;
                    }

                    at++;
                }
                else if (PdfCharacters.IsWhitespace(current))
                {
                    at++;
                }
                else if (current == (byte)'%')
                {
                    at = CommentEnd(at);
                }
                else
                {
                    break;
                }
            }

            crossed = firstEndOfLine >= 0;

            if (crossed)
            {
                // The stretch from that end of line to the next token is crossed between tokens, as from any end of line in it.
                _gapFrom = _gapTo == at ? Math.Min(_gapFrom, firstEndOfLine) : firstEndOfLine;
                _gapTo = at;
            }

            return at;
        }

        /// <summary>Gives where the comment that starts at <paramref name="at"/> ends: its end of line, or the end of the bytes.</summary>
        private int CommentEnd(int at)
        {
            if (_commentEnd >= 0 && at >= _commentFrom && at < _commentEnd)
            {
                return _commentEnd;
            }

            // A comment before the stretch the last crossed ends where it did, when no end of line lies before that stretch.
            var searched = _commentEnd >= 0 && at < _commentFrom ? _bytes[at.._commentFrom] : _bytes[at..];
            var found = searched.IndexOfAny((byte)'\r', (byte)'\n');
            _commentEnd = found >= 0 ? at + found : searched.Length < _bytes.Length - at ? _commentEnd : _bytes.Length;
            _commentFrom = at;
            return _commentEnd;
        }
    }

    /// <summary>
    /// Parses object <paramref name="id"/> at an exact offset, as <see cref="TryParseNumberedAt"/> does, but keeps nothing of the
    /// reading: what the parser notices is dropped, nothing is recorded, and an object that still runs past the window
    /// once <see cref="PdfReaderLimits.MaxObjectLength"/> allows no larger one is not read rather than reported, which
    /// <paramref name="pastGuard"/> says, <paramref name="value"/> then giving what the cut left of it. A stream is not
    /// read: whatever its data, the method returns false.
    /// </summary>
    private bool TryReadUnrecorded(PdfObjectId id, long offset, out PdfObject value, out bool pastGuard)
    {
        value = PdfNull.Instance;
        pastGuard = false;

        if (offset < 0 || offset >= _source.Length)
        {
            return false;
        }

        var maxWindow = _guard.Bound(PdfLimit.Object);
        var windowSize = Math.Min(InitialObjectWindow, maxWindow);
        var mark = _pending.GetMark();

        try
        {
            while (true)
            {
                using var window = _source.GetWindow(offset, windowSize);
                var cut = window.Length == windowSize && offset + window.Length < _source.Length;
                // The file says where a token the window's edge reaches ends, as it does to a load; no stream's data is read.
                var parser = new PdfObjectParser(
                    window.Memory, offset, this, _pending, endsData: !cut, fileEdge: this, maxDepth: MaxNestingDepth, guard: _guard, guardEdge: windowSize >= maxWindow);
                var read = parser.TryReadIndirectObject(out var found, out var parsed);

                // A header read whole that names another object, or another generation, is not this object, however far
                // its body runs, the chain reading each object under its reference's own number and generation: no guard
                // hides it. A stream is not read either, wherever its data ends.
                if (read && (found != id || parsed is PdfStream))
                {
                    return false;
                }

                if (parser.IsTruncated && cut)
                {
                    if (windowSize >= maxWindow)
                    {
                        // What the cut left of the object, which a caller may take as a place to look at, never as its value: a
                        // header read whole that names another object, or a stream, returned above.
                        pastGuard = true;
                        value = read ? parsed : PdfNull.Instance;
                        return false;
                    }

                    _pending.RollBackToRetry(mark);
                    windowSize = (int)Math.Min((long)windowSize * 8, maxWindow);
                    continue;
                }

                if (!read || found != id || parsed is PdfStream)
                {
                    return false;
                }

                value = parsed;
                return true;
            }
        }
        finally
        {
            _pending.RollBack(mark);
        }
    }

    /// <summary>
    /// Determines whether a reading of an object whose header starts at <paramref name="headerStart"/> notes what it found
    /// by the object's number, as <paramref name="recordedAt"/> asks.
    /// </summary>
    private static bool Records(long recordedAt, long headerStart) =>
        recordedAt == RecordedAnywhere || recordedAt == headerStart;

    /// <summary>
    /// Gives where a reading of object <paramref name="number"/> where the chain's <paramref name="entry"/> places it notes
    /// what it found by the number: anywhere, when the reader serves that entry; at the header of the definition it serves
    /// instead, read from the file, which may be the same one; nowhere, when it serves the number from an object stream or
    /// not at all (#255).
    /// </summary>
    private long RecordedAtFor(int number, XRefEntry entry)
    {
        if (!_xref.TryGet(number, out var served) || served.Kind != XRefEntryKind.Regular)
        {
            return RecordedNowhere;
        }

        return served == entry ? RecordedAnywhere : PositionOf(served);
    }

    /// <summary>
    /// Records whether <c>endobj</c> follows an object read at <paramref name="offset"/>: it does not when another
    /// token follows it, or when the file ends first — not when the window the reader offered ended first, where
    /// what follows was never seen. A stream whose data runs past the window is seen to its end all the same: the
    /// file is asked what follows its <c>endstream</c> (#55). A reading that finds it, or cannot tell where the object
    /// ends — a stream whose end a guard hides —, takes back what an earlier one recorded: the chain reads a
    /// cross-reference stream whose <c>/Length</c> it cannot read up to the first <c>endstream</c> it holds, which its rows
    /// may spell (#224).
    /// </summary>
    private void RecordEndObj(int number, int found, EndObjState state, bool cut, long offset)
    {
        if (number == DirectObject || found <= 0)
        {
            return;
        }

        if (state == EndObjState.Absent || (state == EndObjState.Unseen && !cut))
        {
            _endObjMissing[found] = offset;
        }
        else if (state is EndObjState.Present or EndObjState.Unknown)
        {
            _endObjMissing.Remove(found);
        }
    }

    /// <summary>
    /// Records what was found of the stream a kept reading of object <paramref name="found"/> ended with, when the file
    /// did not confirm its length, and that it was reported: the stream is not reported again when it is parsed again, but
    /// for data kept short of a length taken unread the first time (<see cref="_keptShortReported"/>).
    /// A kept reading that shows the stream sound, or the object no stream, clears what an earlier one recorded.
    /// </summary>
    private void RecordStreamLength(int number, int found, PdfObject parsed, StreamLengthFault? fault)
    {
        if (fault is { } reported)
        {
            _lengthFaultsReported.Add(reported.DataStart);

            if (reported.KeptShort)
            {
                _keptShortReported.Add(reported.DataStart);
            }
        }

        if (number == DirectObject || found <= 0)
        {
            return;
        }

        if (fault is { } kept && parsed is PdfStream)
        {
            _streamLengthFaults[found] = kept;
        }
        else
        {
            _streamLengthFaults.Remove(found);
        }
    }

    /// <summary>
    /// Marks a stream whose data reaches the edge of the window a guard stopped at — its dictionary ran to that edge, the
    /// data no search can be anchored at past it —: the reader cut it there, and its lost tail is the guard's, not the
    /// file's. A stream whose data starts within the window is found by its <c>endstream</c>, which no window bounds.
    /// </summary>
    /// <remarks>
    /// A defensive branch: a <c>stream</c> keyword the edge reaches is one only where the file says no regular byte follows
    /// it (#294), and the data then starts past its end of line, which the file gives. Only a source whose bytes change
    /// between those two reads leaves the data's start unknown at the edge.
    /// </remarks>
    private static void MarkCutByGuard(PdfObject parsed, long windowEnd)
    {
        if (parsed is PdfStream { Data: FileStreamData data } && data.Position + data.Length >= windowEnd)
        {
            data.MarkCutByGuard();
        }
    }

    private static string LimitSubject(int number, int bound) => number switch
    {
        DirectObject => $"A trailer runs past {PdfLimitGuard.FormatLength(bound)}; only what lies within it was read.",
        AnyObject => $"The dictionary of the object read as a cross-reference stream runs past {PdfLimitGuard.FormatLength(bound)}; only what lies within it was read.",
        _ => string.Create(
            CultureInfo.InvariantCulture,
            $"Object {number} runs past {PdfLimitGuard.FormatLength(bound)}, its stream data aside; only what lies within it was read."),
    };

    /// <summary>
    /// Reports a guard reached at <paramref name="position"/>, once: an object read again, after the cache
    /// let it go, reaches it again. A document that throws on a guard throws each time.
    /// </summary>
    private void ReachLimit(PdfLimit limit, string what, long position)
    {
        _guardsReached++;

        if (!_limitsReached.Contains((limit, position)))
        {
            _guard.Reach(limit, _diagnostics, what, position);
            _limitsReached.Add((limit, position));
        }
    }

    /// <summary>
    /// Reaches <see cref="PdfReaderLimits.MaxStreamLength"/> for the stream whose data starts at <paramref name="position"/>,
    /// as <see cref="ReachLimit"/> does: its message is made once, as the stream is reported once, however often its data is
    /// read.
    /// </summary>
    private void ReachStreamLength(long position)
    {
        _guardsReached++;

        if (!_limitsReached.Contains((PdfLimit.StreamLength, position)))
        {
            _guard.Reach(PdfLimit.StreamLength, _diagnostics, _guard.DescribeStreamLengthCut(), position);
            _limitsReached.Add((PdfLimit.StreamLength, position));
        }
    }

    /// <summary>
    /// Reports, once for each member, that <see cref="PdfReaderLimits.MaxObjectLength"/> cut member <paramref name="number"/>
    /// of the object stream whose data starts at <paramref name="position"/>: its members share that place, and each is
    /// reported. A document that throws on a guard throws each time.
    /// </summary>
    private void ReachMemberLimit(int number, string what, long position)
    {
        _guardsReached++;

        if (!_memberLimitsReached.Contains((position, number)))
        {
            _guard.Reach(PdfLimit.Object, _diagnostics, what, position);
            _memberLimitsReached.Add((position, number));
        }
    }

    /// <summary>
    /// Looks for the header of object <paramref name="number"/> near the offset <paramref name="entry"/> gives, where careless
    /// writers leave an object their index misplaced, and chooses one, with the generation it gives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bytes searched are those within <see cref="NearbySearchRadius"/> either side of the offset, however near the
    /// file's start it falls (#197); a header counts where it lies whole within them, as the radius says. The
    /// candidates are the headers of the number among them that the rebuild's scan takes (<see cref="GetScanMemo"/>),
    /// clean or raw (<see cref="ScannedPlaceKind"/>): none inside a string, a comment or the confirmed data of a
    /// stream, nor one whose number the bytes' start cuts, but one glued to an end-of-file marker before them (#171).
    /// The memo is asked for only where the bytes hold a header of the number at all, read back from its <c>obj</c> as
    /// the scan reads one: an offset with none near it costs no scan. A header past where the memo is whole
    /// (<see cref="ScanMemo.WholeUntil"/>) is taken as the bytes give it, raw; where the bytes hold one there, the
    /// memo's bound is reported if <paramref name="reportsBound"/> (<see cref="ReachScanMemoBound"/>).
    /// </para>
    /// <para>
    /// A clean candidate ranks before a raw one. Within the best rank, a candidate is supported where the row just before
    /// the entry's in the chain's index as written, or the row just after it, has the header of its own object as far from it
    /// as the candidate lies from the offset — rows a writer miscounted, or bytes inserted before them, shifted alike —, as
    /// the memo holds it: support reads nothing. The nearest supported candidate is chosen, or the nearest of all where none
    /// is, the one before the offset on a tie. The chosen candidate is vouched for where it is clean, and either the only
    /// clean one or supported; otherwise the file does not designate it, and it is chosen all the same. The rows and the memo
    /// do not depend on what was read before, and neither does the choice.
    /// </para>
    /// </remarks>
    /// <param name="number">The object's number.</param>
    /// <param name="entry">The regular entry that places it.</param>
    /// <param name="reportsBound">Whether the memo's bound is reported where a header of the number lies past it.</param>
    /// <param name="actualOffset">Where the header chosen starts: its number, at the first sign of a run of signs (#327).</param>
    /// <param name="generation">The generation the header chosen gives.</param>
    /// <param name="confirmation">Whether the choice is vouched for, and why not where it is not.</param>
    /// <exception cref="PdfLimitExceededException">
    /// A header of the number lies past where the memo is whole, the bound is reported, and the document was opened to throw
    /// on a guard.
    /// </exception>
    private bool TryFindObjectHeader(
        int number, XRefEntry entry, bool reportsBound, out long actualOffset, out int generation, out HeaderConfirmation confirmation)
    {
        actualOffset = -1;
        generation = 0;
        confirmation = HeaderConfirmation.Raw;
        var approximateOffset = PositionOf(entry);

        if (approximateOffset < -NearbySearchRadius)
        {
            // Nothing of the file lies within reach of an offset that far before its start, as a negative row gives; the
            // window is not worked out from it, which would wrap (#125).
            return false;
        }

        // The window ends the radius past the offset, or at the file's end, however near the file's start the offset falls,
        // where a window clamped there once reached up to twice the radius past it (#197). The end is worked out from what
        // lies between the offset and the file's end, which wraps for no offset the test above lets through: long.MaxValue,
        // which an entry past what a long holds gives, ends it at the file's end, and the window is empty.
        var start = Math.Max(0, approximateOffset - NearbySearchRadius);
        var length = approximateOffset + Math.Min(NearbySearchRadius, _source.Length - approximateOffset) - start;

        if (length <= 0)
        {
            return false;
        }

        Span<long> positions = stackalloc long[MaxNearbyHeaders];
        Span<int> generations = stackalloc int[MaxNearbyHeaders];
        var count = 0;

        // The bytes before the window are read as well, up to an end-of-file marker's length: a header glued to the marker
        // the window's start cuts is told from digits a longer token ends with. So is the byte after it, where there is one: it
        // tells an obj that ends the window from a longer word (#189). A window shorter than asked holds the end of the data.
        var lead = (int)Math.Min(start, EndOfFileMarker.Length);
        var trail = start + length < _source.Length ? 1 : 0;

        using (var window = _source.GetWindow(start - lead, (int)length + lead + trail))
        {
            var span = window.Memory.Span;
            var searched = Math.Min(span.Length, (int)length + lead);

            for (var from = 0; from < searched && count < MaxNearbyHeaders;)
            {
                var index = span[from..searched].IndexOf(ObjKeyword);

                if (index < 0)
                {
                    break;
                }

                var position = from + index;

                if (TryReadHeaderBackwards(span, position, out var found, out var foundGeneration, out var headerStart) &&
                    headerStart >= lead && found == number)
                {
                    positions[count] = start - lead + headerStart;
                    generations[count++] = foundGeneration;
                }

                from = position + ObjKeyword.Length;
            }
        }

        if (count == 0)
        {
            return false;
        }

        var memo = GetScanMemo();
        Span<ScannedPlaceKind> kinds = stackalloc ScannedPlaceKind[MaxNearbyHeaders];
        var candidates = 0;
        var clean = 0;
        var pastMemo = false;

        for (var i = 0; i < count; i++)
        {
            if (positions[i] >= memo.WholeUntil)
            {
                kinds[i] = ScannedPlaceKind.Raw;
                pastMemo = true;
            }
            else if (memo.TryGetHeaderAt(positions[i], out var header) && header.Number == number)
            {
                kinds[i] = header.Kind;
                generations[i] = header.Generation;
            }
            else
            {
                positions[i] = -1;
                continue;
            }

            candidates++;
            clean += kinds[i] == ScannedPlaceKind.Clean ? 1 : 0;
        }

        // A guard is reached where it changes what is read (ADR 34): here, where a header lies past where the memo is whole,
        // taken without its word. A window that reaches past there but holds no header there has the memo's word for each.
        if (reportsBound && pastMemo)
        {
            ReachScanMemoBound(positions[..count]);
        }

        var best = -1;
        var bestSupported = false;

        for (var i = 0; i < count; i++)
        {
            if (positions[i] < 0)
            {
                continue;
            }

            var supported = candidates > 1 && SharesShift(memo, entry, positions[i] - approximateOffset);

            // Positions rise: of two candidates as near, the one kept first lies before the offset.
            if (best < 0 || (kinds[i] != kinds[best] ? kinds[i] == ScannedPlaceKind.Clean
                : supported != bestSupported ? supported
                : Math.Abs(positions[i] - approximateOffset) < Math.Abs(positions[best] - approximateOffset)))
            {
                best = i;
                bestSupported = supported;
            }
        }

        if (best < 0)
        {
            return false;
        }

        actualOffset = positions[best];
        generation = generations[best];
        confirmation = kinds[best] == ScannedPlaceKind.Raw
            ? actualOffset >= memo.WholeUntil ? HeaderConfirmation.PastScan : HeaderConfirmation.Raw
            : clean == 1 || bestSupported ? HeaderConfirmation.Vouched : HeaderConfirmation.Ambiguous;
        return true;
    }

    /// <summary>
    /// Determines whether the row just before <paramref name="entry"/>'s in the chain's index as written, or the row just
    /// after it, has the header of its own object <paramref name="shift"/> bytes from it, as the scan's memo holds it.
    /// </summary>
    private bool SharesShift(ScanMemo memo, XRefEntry entry, long shift)
    {
        var index = _chainIndex ?? _xref;
        return HoldsOwnHeader(index, memo, index.LastOffsetBefore(entry.Offset), shift) ||
            HoldsOwnHeader(index, memo, index.FirstOffsetAfter(entry.Offset), shift);
    }

    /// <summary>
    /// Determines whether the memo holds, <paramref name="shift"/> bytes from where the offset <paramref name="row"/> names, the
    /// header of an object <paramref name="index"/> places at that offset; false for no row, -1 or <see cref="long.MaxValue"/>.
    /// </summary>
    private bool HoldsOwnHeader(PdfXRefTable index, ScanMemo memo, long row, long shift) =>
        row >= 0 && row < _source.Length &&
        memo.TryGetHeaderAt(PositionOf(row) + shift, out var header) &&
        index.TryGet(header.Number, out var entry) && entry.Kind == XRefEntryKind.Regular && entry.Offset == row;

    /// <summary>
    /// Determines whether a row the file gives cuts a number, <paramref name="bytes"/> starting with the byte before it: a digit
    /// before it, and a digit or a sign at it — <c>14 0 obj</c> read from its <c>4</c>. A load lexes from the row, and would
    /// take the number's end for an object's; a valid file has white space or a delimiter before every header, as a token
    /// starts. Such a row is read only once the search near it has found no header of the object, for a load, settling and
    /// the validator's probe alike (<see cref="TryParseWhereEntryPlaces"/>). A place the reader found holds a header read
    /// with what precedes it, and is not asked.
    /// </summary>
    private static bool RowCutsNumber(ReadOnlySpan<byte> bytes) =>
        bytes.Length > 1 && PdfCharacters.IsDigit(bytes[0]) && (PdfCharacters.IsDigit(bytes[1]) || bytes[1] is (byte)'+' or (byte)'-');

    /// <summary>
    /// Reads object <paramref name="number"/> where an entry of the chain's index places it — at the offset it gives, or
    /// near it —, neither through the index the reader reads with now nor from its cache: what the file's own index gives,
    /// whatever a rebuild made of the number since. Null when nothing of that number is there.
    /// </summary>
    /// <remarks>
    /// It parses as a load parses, and notes what a load notes of the object, as <see cref="ReadObjectStreamHeader"/> does;
    /// it neither relocates the entry nor rebuilds the index. A stream's indirect <c>/Length</c> is still loaded as any
    /// object is.
    /// </remarks>
    /// <param name="number">The object's number.</param>
    /// <param name="entry">The regular entry of the chain's index that places it.</param>
    internal PdfObject? ReadWhereChainPlaces(int number, XRefEntry entry) =>
        TryParseWhereEntryPlaces(number, entry, RecordedAtFor(number, entry), out var value, out _, out _, out _) ? value : null;

    /// <summary>
    /// Reads which objects the object stream <paramref name="number"/> holds, in order, from the header its data
    /// starts with — without keeping the stream, nor what it decodes to: the validation rules read each once, or once more
    /// where the chain's index places some of its objects and a rebuilt index alone others, and
    /// memory follows the largest of them rather than their sum.
    /// </summary>
    /// <param name="number">The object stream's number.</param>
    /// <param name="entry">
    /// The regular entry that places it: the chain's index's, or that of the index the reader serves, for the members only a
    /// rebuild places.
    /// </param>
    /// <param name="numbers">
    /// The object numbers its header lists, in order: all of them when it was read, those listed before the fault when its
    /// header could be read only in part — the objects the reader serves from it —, none otherwise.
    /// </param>
    /// <param name="fault">
    /// What is wrong with it, when it could not be read whole and is there: the predicate of a sentence whose subject is
    /// the stream, as the reader's own report words it (<see cref="ObjectStreamHeader.NoCount"/>).
    /// </param>
    /// <param name="streamMembers">
    /// The places in the header, among those of <paramref name="numbers"/>, of the objects written as a stream, which the
    /// reader reads as null (#161): each member is read as the reader reads it, in its window, unless the window holds no
    /// <c>stream</c> at all.
    /// </param>
    /// <param name="headerAt">
    /// Where the header of the definition read starts, past the white space before it: the place a load takes the entry to
    /// give, which a row a few bytes early does not.
    /// </param>
    /// <param name="holdersReadAsNull">
    /// The object streams whose objects the header's values read as null, wherever a value leads: those the walk of the
    /// streams' dependencies finds not to decode alike in every read order, so that the header reads the same whatever was
    /// read before (#313); null to read them as the reader serves them.
    /// </param>
    internal ObjectStreamHeaderResult ReadObjectStreamHeader(
        int number, XRefEntry entry, out int[] numbers, out string? fault, out int[] streamMembers, out long headerAt,
        IReadOnlySet<int>? holdersReadAsNull = null)
    {
        var outer = _holdersReadAsNull;
        _holdersReadAsNull = holdersReadAsNull;

        try
        {
            return ReadObjectStreamHeaderCore(number, entry, out numbers, out fault, out streamMembers, out headerAt);
        }
        finally
        {
            _holdersReadAsNull = outer;
        }
    }

    /// <summary>Reads object stream <paramref name="number"/>'s header, as <see cref="ReadObjectStreamHeader"/> does.</summary>
    private ObjectStreamHeaderResult ReadObjectStreamHeaderCore(
        int number, XRefEntry entry, out int[] numbers, out string? fault, out int[] streamMembers, out long headerAt)
    {
        numbers = [];
        fault = null;
        streamMembers = [];

        var guards = _guardsReached;
        var found = TryParseWhereEntryPlaces(number, entry, RecordedAtFor(number, entry), out var value, out _, out _, out headerAt);

        // A dictionary one of the reader's limits cut is the limit's, not the file's: what it would have said is unknown. A
        // stream's dictionary was read whole, and is the file's; what a limit cut of its data is weighed as it is decoded.
        if (_guardsReached != guards && value is not PdfStream)
        {
            return ObjectStreamHeaderResult.CutByLimit;
        }

        if (!found)
        {
            return ObjectStreamHeaderResult.NotFound;
        }

        if (value is not PdfStream stream || !stream.Dictionary.IsOfType(PdfName.ObjStm))
        {
            if (value is PdfStream typed && HeaderValueHidden(typed.Dictionary, PdfName.Type))
            {
                return ObjectStreamHeaderResult.CutByLimit;
            }

            fault = value is PdfStream ? "is a stream that is not of /Type /ObjStm" : "is not a stream";
            return ObjectStreamHeaderResult.NotAnObjectStream;
        }

        var count = stream.Dictionary.GetInteger(PdfName.N, -1);
        var first = stream.Dictionary.GetInteger(PdfName.First, -1);

        // A stream that declares no object is empty, as the reader reads it: neither its /First nor its data is read.
        if (count == 0)
        {
            return ObjectStreamHeaderResult.Read;
        }

        // A /N or a /First the reader cannot read for one of its guards is the guard's: what it would say is unknown (#395).
        if ((count < 0 && HeaderValueHidden(stream.Dictionary, PdfName.N)) || (count > 0 && first < 0 && HeaderValueHidden(stream.Dictionary, PdfName.First)))
        {
            return ObjectStreamHeaderResult.CutByLimit;
        }

        if (count < 0 || first < 0)
        {
            fault = count < 0 ? ObjectStreamHeader.NoCount : ObjectStreamHeader.NoFirst;
            return ObjectStreamHeaderResult.Unreadable;
        }

        var data = Filters.PdfFilterPipeline.Decode(stream, _diagnostics, out var leftEncoded, out var cut);

        // Data the guard on filters left encoded holds none of the stream's objects: what its header says is unknown.
        if (leftEncoded)
        {
            return ObjectStreamHeaderResult.CutByLimit;
        }

        if (first > data.Length)
        {
            fault = ObjectStreamHeader.FirstPastData(first, data.Length);
            return cut ? ObjectStreamHeaderResult.CutByLimit : ObjectStreamHeaderResult.Unreadable;
        }

        // The reader's own reading of the header: the objects it serves are those the pairs read list (#275).
        var header = ObjectStreamHeader.Read(data.Span, count, (int)first, cut);
        numbers = header.Numbers;
        streamMembers = StreamMembers(number, stream.Data.Position, data, header, (int)first, cut);

        if (header.Fault == ObjectStreamHeaderFault.None)
        {
            return ObjectStreamHeaderResult.Read;
        }

        fault = header.Describe(count, data.Length);
        return ObjectStreamHeaderResult.Unreadable;
    }

    /// <summary>
    /// Determines whether the value <paramref name="dictionary"/>, an object stream's, gives under <paramref name="key"/> is
    /// a reference whose object a guard cut or left out of the index, the walk in progress's guard among them
    /// (<see cref="IsCutAtLimit"/>): what it holds is the guard's, not the file's (#395). Call it once the value is resolved.
    /// </summary>
    private bool HeaderValueHidden(PdfDictionary dictionary, PdfName key) =>
        dictionary.GetRaw(key) is PdfReference reference && LeftByGuard(reference);

    /// <summary>
    /// Gives the places in <paramref name="header"/> of the members of object stream <paramref name="number"/> written as a
    /// stream, each read as a load reads it, in its window: one whose window holds no <c>stream</c> is none. What the
    /// parses meet is dropped.
    /// </summary>
    private int[] StreamMembers(int number, long dataStart, ReadOnlyMemory<byte> data, ObjectStreamHeader header, int first, bool cut)
    {
        List<int>? found = null;
        var bound = _guard.Bound(PdfLimit.Object);
        var mark = _pending.GetMark();

        // The decoded data says what lies past a member's window, as it does to the load, so that both take a stream keyword
        // the window ends on alike (#282).
        var edge = new DecodedDataEdge(data);

        try
        {
            for (var index = 0; index < header.Count; index++)
            {
                // A member past data a guard cut is unread.
                if ((long)first + header.Offsets[index] >= data.Length)
                {
                    continue;
                }

                var window = ObjectStreamMemberWindow.Of(header.Offsets, index, first, data.Length, cut, bound);

                if (data.Span[window.Start..window.End].IndexOf("stream"u8) < 0)
                {
                    continue;
                }

                var parser = PdfObjectParser.ForObjectStreamMember(
                    data[..window.End], number, dataStart, header.Numbers[index], this, _pending, window.EndsData, window.EndsAtNextMember, edge, MaxNestingDepth, _guard);
                parser.Position = window.Start;
                _ = parser.ParseObject();

                if (parser.StoppedAtStream)
                {
                    (found ??= []).Add(index);
                }
            }
        }
        finally
        {
            _pending.RollBack(mark);
        }

        return found is null ? [] : [.. found];
    }

    /// <summary>
    /// Reads the <c>N G</c> that precedes an <c>obj</c> keyword, the bytes before <paramref name="span"/> standing for the
    /// start of the data. Walking backwards is what distinguishes a real object header from the <c>obj</c> inside
    /// <c>endobj</c>.
    /// </summary>
    /// <param name="span">The bytes the keyword lies in, from the start of the data or of what a caller searches.</param>
    /// <param name="objPosition">Where the keyword starts in <paramref name="span"/>.</param>
    /// <param name="number">The object number the header gives.</param>
    /// <param name="generation">The generation the header gives, as the parser reads it: what an entry recording the
    /// header records (#118).</param>
    /// <param name="headerStart">Where the header starts in <paramref name="span"/>: where the lexer starts its number's
    /// token, at the first sign of a run of signs the number is written with (#327).</param>
    internal static bool TryReadHeaderBackwards(
        ReadOnlySpan<byte> span, int objPosition, out int number, out int generation, out int headerStart) =>
        ReadHeaderBackwards(
            span, objPosition, startsData: true, signsBeforeUnknown: false, out number, out generation, out headerStart, out _) ==
            HeaderWalk.Header;

    /// <summary>
    /// Reads the <c>N G</c> that precedes an <c>obj</c> keyword, as a load lexes those tokens. Walking backwards is what
    /// distinguishes a real object header from the <c>obj</c> inside <c>endobj</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The keyword ends where white space, a delimiter or the end of <paramref name="span"/> follows it, as a load lexes it:
    /// <c>10 0 objx</c> is no header (#189). The end of <paramref name="span"/> stands for the end of the data, so a caller
    /// gives a span that holds the byte after every <c>obj</c> it asks about, where the file holds one.
    /// </para>
    /// <para>
    /// Each number, the generation first, is an integer token read backwards over the white space after it, as a load reads
    /// it where only white space lies between the tokens: a run of digits, after the run of signs it may be written with,
    /// which the lexer reads with it as one token, the first sign deciding (ISO 32000-1, 7.3.3) — <c>4 +0 obj</c> and
    /// <c>+4 0 obj</c> are object 4's header, <c>-4 0 obj</c> none, its number below 1 (#327). The token starts where the
    /// lexer would start it: the byte before it is white space or a delimiter that ends a token, not a regular character, nor
    /// a delimiter that starts a token the digits would belong to — <c>(</c>, <c>&lt;</c>, <c>/</c>, <c>%</c> —; so
    /// <c>x4 0 obj</c> is no header, and no window's start cuts a number short (#171). The end-of-file marker <c>%%EOF</c>
    /// is the one comment that ends where it does, a writer gluing the next update's first header to it: <c>%%EOF1 0 obj</c>
    /// is object 1's header. A comment may lie between the tokens
    /// too (ISO 32000-1, 7.2.3), and runs to the end of its line: one ends before a token only where the white space crossed
    /// holds an end of line. There, a line that holds a <c>%</c> is read as a load reads it, lexed forward from its start,
    /// which tells a comment from a <c>%</c> a string holds: its last tokens are the numbers still wanted, the nearest
    /// first, and a line that holds nothing but a comment is passed for the line before it (#261). A line that holds no
    /// <c>%</c> is read by its tokens, as is the line of <c>obj</c> itself.
    /// </para>
    /// <para>
    /// A walk lexes a line once at most, and enters it only through the white space right after it, which one walk at most
    /// crosses — that of the <c>obj</c> the tokens after it lead to, through comments and a generation —: the work stays
    /// linear in the bytes searched, and nothing is allocated. Those bytes bound a walk: the window of a search near an
    /// offset, or what the rebuild's scan reads. Where <paramref name="startsData"/> is false, a walk that reaches the start
    /// of <paramref name="span"/> undecided — in white space, a token, or a line holding a <c>%</c> that starts there — says
    /// so, for the caller to read further back; where it is true, that start is the start of the data. A line whose start lies
    /// inside a string a line before it opened is lexed from its start all the same.
    /// </para>
    /// </remarks>
    /// <param name="span">The bytes the keyword lies in.</param>
    /// <param name="objPosition">Where the keyword starts in <paramref name="span"/>.</param>
    /// <param name="startsData">
    /// Whether the start of <paramref name="span"/> is taken for the start of the data; a walk that reaches it undecided
    /// says so otherwise.
    /// </param>
    /// <param name="signsBeforeUnknown">
    /// Whether a run of signs that reaches the start of <paramref name="span"/>, taken for the start of the data, is left
    /// out of the number, its first sign perhaps before the bytes given: the search near an offset reads such a header by
    /// its digits.
    /// </param>
    /// <param name="number">The object number the header gives.</param>
    /// <param name="generation">The generation the header gives, as the parser reads it.</param>
    /// <param name="headerStart">Where the header starts in <paramref name="span"/>: its number's token, a sign included.</param>
    /// <param name="found">
    /// Where the walk met the object number in <paramref name="span"/>: its digits, past its signs, where it read the number
    /// back by them, and its token's start where it lexed the number's line — where the search near an offset places a
    /// header (<see cref="FindSectionCandidates"/>).
    /// </param>
    private static HeaderWalk ReadHeaderBackwards(
        ReadOnlySpan<byte> span,
        int objPosition,
        bool startsData,
        bool signsBeforeUnknown,
        out int number,
        out int generation,
        out int headerStart,
        out int found)
    {
        number = 0;
        generation = 0;
        headerStart = 0;
        found = 0;

        var index = objPosition - 1;
        var after = objPosition + ObjKeyword.Length;

        if (index < 0)
        {
            return startsData ? HeaderWalk.None : HeaderWalk.Undecided;
        }

        if (!PdfCharacters.IsWhitespace(span[index]) || (after < span.Length && PdfCharacters.IsRegular(span[after])))
        {
            return HeaderWalk.None;
        }

        var numbers = default(HeaderNumbers);

        while (numbers.Taken < 2)
        {
            // The white space after the token, and whether a comment may end where it starts: only an end of line ends one.
            var endOfLine = false;

            while (index >= 0 && PdfCharacters.IsWhitespace(span[index]))
            {
                endOfLine |= span[index] is (byte)'\r' or (byte)'\n';
                index--;
            }

            if (index < 0)
            {
                return startsData ? HeaderWalk.None : HeaderWalk.Undecided;
            }

            if (endOfLine)
            {
                var lineStart = span[..(index + 1)].LastIndexOfAny((byte)'\r', (byte)'\n') + 1;

                // A line that starts the bytes may start before them, where a % they do not hold may make it a comment.
                if (lineStart == 0 && !startsData)
                {
                    return HeaderWalk.Undecided;
                }

                if (span[lineStart..(index + 1)].Contains((byte)'%'))
                {
                    if (!numbers.TakeLine(span[..(index + 1)], lineStart))
                    {
                        return HeaderWalk.None;
                    }

                    index = lineStart - 1;
                    continue;
                }
            }

            // A run of digits, which ends the walk where it is empty, then the run of signs before it: the lexer reads both as
            // one token, which starts where neither a regular character nor a delimiter that starts a token comes before it.
            var runEnd = index + 1;

            while (index >= 0 && PdfCharacters.IsDigit(span[index]))
            {
                index--;
            }

            var runStart = index + 1;

            if (runStart == runEnd)
            {
                return HeaderWalk.None;
            }

            while (index >= 0 && span[index] is (byte)'+' or (byte)'-')
            {
                index--;
            }

            if (index < 0 ? !startsData : !PdfCharacters.EndsToken(span[index]) && !EndsEndOfFileMarker(span, index))
            {
                return index < 0 || (!startsData && index < EndOfFileMarker.Length - 1 &&
                    EndOfFileMarker.EndsWith(span[..(index + 1)])) ? HeaderWalk.Undecided : HeaderWalk.None;
            }

            // Each token is judged by its value, however many zeros lead it, as the parser judges it: a header the parser
            // would refuse, found here, would be an object no read can serve. A run of digits after signs always parses, as an
            // integer within a long or as a real past one. Signs that run back to the start of bytes that do not start the
            // data may follow a first sign they do not hold: they are not read, and the digits decide.
            var tokenStart = index < 0 && signsBeforeUnknown ? runStart : index + 1;
            _ = PdfNumberParser.TryParse(span[tokenStart..runEnd], out var value, out _, out var pastLong);

            if (pastLong)
            {
                return HeaderWalk.None;
            }

            numbers.Take(value, tokenStart, runStart);
        }

        if (numbers.Number is <= 0 or > PdfObjectId.MaxNumber || numbers.Generation is < 0 or > PdfObjectId.MaxGeneration)
        {
            return HeaderWalk.None;
        }

        number = (int)numbers.Number;
        generation = (int)numbers.Generation;
        headerStart = numbers.NumberStart;
        found = numbers.NumberFound;
        return HeaderWalk.Header;
    }

    /// <summary>
    /// Determines whether the byte at <paramref name="index"/> ends the end-of-file marker <c>%%EOF</c>: a writer that
    /// appends an update with no end of line after the marker glues the update's first header to it, and a token starts
    /// right after it.
    /// </summary>
    private static bool EndsEndOfFileMarker(ReadOnlySpan<byte> span, int index) =>
        index >= EndOfFileMarker.Length - 1 && span[(index - EndOfFileMarker.Length + 1)..(index + 1)].SequenceEqual(EndOfFileMarker);

    /// <summary>The end-of-file marker, a comment a writer may glue the next update to (ISO 32000-1, 7.5.5).</summary>
    private static ReadOnlySpan<byte> EndOfFileMarker => "%%EOF"u8;

    /// <summary>What a walk back from an <c>obj</c> keyword decides (<see cref="ReadHeaderBackwards"/>).</summary>
    private enum HeaderWalk
    {
        /// <summary>No header ends at the keyword.</summary>
        None,

        /// <summary>A header ends at the keyword.</summary>
        Header,

        /// <summary>The walk reached the start of the bytes it was given before it could decide.</summary>
        Undecided,
    }

    /// <summary>
    /// The two numbers of an object header as a walk back from its <c>obj</c> takes them (<see cref="TryReadHeaderBackwards"/>):
    /// the generation first, then the number, with where it starts.
    /// </summary>
    private struct HeaderNumbers
    {
        /// <summary>Gets how many of the two were taken.</summary>
        public int Taken { get; private set; }

        /// <summary>Gets the generation, once taken.</summary>
        public long Generation { get; private set; }

        /// <summary>Gets the object number, once taken.</summary>
        public long Number { get; private set; }

        /// <summary>Gets where the object number's token starts, a sign included, once taken.</summary>
        public int NumberStart { get; private set; }

        /// <summary>
        /// Gets where the walk met the object number, once taken: its digits, past its signs, where it read the number back
        /// by them, and its token's start where it lexed the number's line.
        /// </summary>
        public int NumberFound { get; private set; }

        /// <summary>
        /// Takes <paramref name="value"/>, whose token starts at <paramref name="start"/> and which the walk met at
        /// <paramref name="found"/>, as the next number wanted.
        /// </summary>
        public void Take(long value, int start, int found)
        {
            if (Taken == 0)
            {
                Generation = value;
            }
            else
            {
                Number = value;
                NumberStart = start;
                NumberFound = found;
            }

            Taken++;
        }

        /// <summary>
        /// Lexes the line from <paramref name="lineStart"/> to the end of <paramref name="line"/> as a load reads it, and takes
        /// its last tokens as the numbers still wanted, the nearest first; a line that holds nothing but a comment gives none.
        /// False where one is no integer: a string the line does not close is its last token, and holds what follows it.
        /// </summary>
        public bool TakeLine(ReadOnlySpan<byte> line, int lineStart)
        {
            var lexer = new PdfLexer(line, lineStart);
            var last = default(PdfToken);
            var before = default(PdfToken);

            // One call to the lexer: the rebuild's scan inlines this walk in its loop, where the compiler weighs each call
            // apart, and took in the first of two while it left out the other, the one each token after the first goes through.
            while (lexer.Read() is { Kind: not PdfTokenKind.EndOfInput } token)
            {
                before = last;
                last = token;
            }

            if (last.Kind == PdfTokenKind.EndOfInput)
            {
                return true;
            }

            return TakeInteger(last) && (Taken == 2 || before.Kind == PdfTokenKind.EndOfInput || TakeInteger(before));
        }

        /// <summary>Takes <paramref name="token"/> as the next number wanted where it is an integer, and says whether it is.</summary>
        private bool TakeInteger(PdfToken token)
        {
            if (token.Kind != PdfTokenKind.Integer)
            {
                return false;
            }

            Take(token.Integer, token.Start, token.Start);
            return true;
        }
    }

    private PdfObject? LoadCompressedObject(PdfObjectId id, XRefEntry entry)
    {
        var contents = GetObjectStream(entry.ObjectStreamNumber, id.Number, out var notYet);

        if (contents is null)
        {
            // A stream that cannot be read yet — its own dictionary being read, or a walk having decoded again as much as
            // it may — says nothing of the object: it is read again when asked again.
            if (notYet)
            {
                return PdfNull.Instance;
            }

            // One a guard left unable to serve its objects leaves the object unread for the guard's reasons, which its
            // report names: the object is one a guard cut, read as far as it was — not at all (#259).
            if (_objectStreamsHiddenByGuard.Contains(entry.ObjectStreamNumber))
            {
                _cutAtLimit.Add(id.Number);
            }

            return null;
        }

        var mark = _pending.GetMark();

        try
        {
            var bound = _guard.Bound(PdfLimit.Object);
            var value = contents.Parse(
                entry.IndexInObjectStream, id.Number, this, _diagnostics, _pending, bound, MaxNestingDepth, _guard, out var cut, out var pastLength, out var streamAt, out var nesting);

            // A member written as a stream reads as null, what parsing met of it dropped, and is reported once (#161).
            if (streamAt >= 0)
            {
                if (_objectStreamReports.IsFirstStreamMember(contents.DataStart, id.Number))
                {
                    _diagnostics.Warn(
                        PdfDiagnosticCodes.ObjectStreamMemberStream,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Object {id.Number}, at byte {streamAt} of object stream {entry.ObjectStreamNumber}'s decoded data, is written as a stream, which an object stream cannot hold; it reads as null."),
                        contents.DataStart);
                }

                return null;
            }

            if (pastLength >= 0)
            {
                ReachMemberLimit(
                    id.Number,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Object {id.Number}, at byte {pastLength} of object stream {entry.ObjectStreamNumber}'s decoded data, runs past {PdfLimitGuard.FormatLength(bound)}; only what lies within it was read."),
                    contents.DataStart);
            }

            if (cut)
            {
                // A member that runs into the end of data a guard cut is kept as far as it was read, like a regular
                // object cut at MaxObjectLength; what the parser met at the cut is the guard's, not the file's, and was
                // not reported, while what it met before the cut is the file's (ADR 34).
                _cutAtLimit.Add(id.Number);
                KeepPending(mark, syntaxOnly: true);
            }
            else
            {
                KeepPending(mark);
            }

            CountNestingCut(nesting, id.Number);
            return value;
        }
        finally
        {
            _pending.RollBack(mark);
        }
    }

    /// <summary>
    /// Gives what object stream <paramref name="number"/> holds, decoding it the first time, or null when it cannot
    /// serve its objects — for good, or, when <paramref name="notYet"/> says so, only while its own dictionary is
    /// being read, or while a walk of the library's own that may not decode it again is in progress (#181).
    /// </summary>
    private ObjectStreamContents? GetObjectStream(int number, int wanted, out bool notYet)
    {
        notYet = false;

        // The stream is decoded with the loads in flight: what an earlier decoding kept serves only where a decoding from here
        // would go as deep (#163).
        var depthBase = _loading.Count;

        if ((_objectStreams.TryGet(DepthKey(number, -1), out var cached) && ServesAt(cached.Depth, depthBase)) ||
            (_objectStreams.TryGet(DepthKey(number, depthBase), out cached) && ServesAt(cached.Depth, depthBase)))
        {
            return cached.Contents;
        }

        // The stream's own dictionary is being read, and names an object the stream holds — its /Length —: the
        // stream cannot be read yet, and will be once its dictionary is (#51). Nothing is recorded.
        if (_loading.Contains(number))
        {
            NullWhileDecoding(wanted);
            notYet = true;
            ReportObjectStreamNeedsItself(number, wanted);
            return null;
        }

        // Under ThrowOnLimit, a decoding that reached a guard throws again, without decoding (#181).
        if (_decodingsThrown is not null && ((_decodingsThrown.TryGet(number, -1, out var thrown, out var thrownDepth) && ServesAt(thrownDepth, depthBase)) ||
            (_decodingsThrown.TryGet(number, depthBase, out thrown, out thrownDepth) && ServesAt(thrownDepth, depthBase))))
        {
            thrown.Throw();
        }

        // A walk of the library's own decodes again what it let go within its budget: past it, the object reads as null,
        // which says nothing of it, and is not kept — nor is anything read through it, beyond the walk (#181).
        if (WalkSpent(number))
        {
            CountWalkCut();
            (_walkCut ??= new(SeededComparer.Instance)).Add(wanted);
            notYet = true;
            ReachWalkDecodes(number);
            return null;
        }

        // Marked while it is decoded: an object it holds, asked for meanwhile, reads as null rather than
        // recursing, and is not kept as null.
        _objectStreamsLoading.Add(number);
        var before = (Reentries: _reentries, Repaired: _repaired, WalkCuts: _walkCuts, NestingCuts: _nestingCuts, TransientCuts: _transientCuts, Deepest: _deepest);
        _deepest = depthBase;

        // What a guard's exception is kept as, by the filter below, where it sets out: the stream's own load, its decoding,
        // or neither.
        var keeping = DecodingStep.None;

        try
        {
            PdfObject loaded;

            // The stream's own load reaching a guard — its dictionary, its /Length — is kept as its decoding's is, so that each
            // object asked of it throws again without loading it again (#397).
            keeping = DecodingStep.Load;
            loaded = GetObject(new PdfObjectId(number));
            keeping = DecodingStep.None;

            // The stream itself could not be loaded from here — too deep, or needed by what is being loaded —: that
            // says nothing of it, and it is read again when one of its objects is asked for again.
            if (loaded is PdfNull && before.Reentries != _reentries)
            {
                notYet = true;
                return null;
            }

            // What decoding without an object the stream holds gives is all the stream can give, and is kept; decoded
            // again once the budget let it go, the stream reads the same objects as null, and gives the same.
            ObjectStreamContents? contents = null;
            bool hidden;

            // A stream whose dictionary was read whole is one a guard hid only as its decoding says; one a guard cut short
            // of its stream, before or inside its dictionary, is hidden by that guard.
            if (loaded is PdfStream stream)
            {
                keeping = DecodingStep.Decoding;
                contents = ObjectStreamContents.TryCreate(this, number, stream, _diagnostics, _objectStreamReports, out hidden);
                keeping = DecodingStep.None;
            }
            else
            {
                hidden = IsCutAtLimit(number);
            }

            // What was read through an object a walk's guard read as null is the walk's: the stream is hidden by no guard,
            // and is decoded again, whole, when asked for once the walk ends — not before, counted as decoded
            // (CountWalkDecoding).
            var whole = before.WalkCuts == _walkCuts;

            if (contents is null && hidden && whole)
            {
                _objectStreamsHiddenByGuard.Add(number);
            }

            // A stream read through an index its own load had rebuilt is decoded again when next asked for, from the
            // rebuilt index's definition.
            if (before.Repaired == _repaired && whole)
            {
                KeepObjectStream(number, contents, DepthSince(depthBase, before.NestingCuts, before.TransientCuts));
            }

            CountWalkDecoding(number, contents, whole);

            // Nor does the walk read the object from what it gave, which may lack what the guard read as null: the object
            // is one the guard cut, until the walk ends.
            if (!whole)
            {
                (_walkCut ??= new(SeededComparer.Instance)).Add(wanted);
                notYet = true;
                return null;
            }

            return contents;
        }
        catch (Exception exception) when (KeepDecodingThrown(number, exception, keeping, depthBase, before))
        {
            // Never reached: the filter keeps the exception as it sets out, and catches nothing (KeepThrown's remarks).
            throw;
        }
        finally
        {
            _objectStreamsLoading.Remove(number);
            _deepest = Math.Max(before.Deepest, _deepest);
        }
    }

    /// <summary>
    /// Keeps, under <see cref="PdfReaderOptions.ThrowOnLimit"/>, what the decoding of object stream <paramref name="number"/>
    /// from <paramref name="depthBase"/> loads in flight threw, as the filter of its frame, which runs as the exception sets
    /// out (<see cref="KeepThrown"/>'s remarks): thrown by the stream's own load — its dictionary, its <c>/Length</c> — where
    /// nothing on the way counted as a re-entry, or by its decoding, as its contents are kept, a null on the way for an object
    /// the stream holds, or one being loaded, read as null again by every decoding of the stream, which would throw alike
    /// (<see cref="KeepDecodingThrow"/>'s remarks); and raises the deepest load the read reached as the frame's
    /// <see langword="finally"/> does. Catches nothing.
    /// </summary>
    /// <param name="number">The object stream.</param>
    /// <param name="exception">What was thrown.</param>
    /// <param name="step">Where it was thrown.</param>
    /// <param name="depthBase">How many loads were in flight as the decoding started.</param>
    /// <param name="before">The reader's counts, and the deepest load the read reached, as the decoding started.</param>
    private bool KeepDecodingThrown(
        int number,
        Exception exception,
        DecodingStep step,
        int depthBase,
        (int Reentries, bool Repaired, int WalkCuts, int NestingCuts, int TransientCuts, int Deepest) before)
    {
        if (step != DecodingStep.None && exception is PdfLimitExceededException limit &&
            (step == DecodingStep.Decoding || before.Reentries == _reentries) &&
            before.Repaired == _repaired && before.WalkCuts == _walkCuts && !_expandingObjectStreams && !IsKeptByNoTable(limit))
        {
            KeepDecodingThrow(number, limit, DepthSince(depthBase, before.NestingCuts, before.TransientCuts));
        }

        _deepest = Math.Max(before.Deepest, _deepest);
        return false;
    }

    /// <summary>Where the decoding of an object stream is, for what a guard's exception it meets is kept as.</summary>
    private enum DecodingStep
    {
        /// <summary>Neither loading the stream nor decoding it: what is thrown is kept by the load it comes from.</summary>
        None,

        /// <summary>Loading the stream itself.</summary>
        Load,

        /// <summary>Decoding the stream's header and data.</summary>
        Decoding,
    }

    /// <summary>
    /// Determines whether the walk in progress may not decode object stream <paramref name="number"/> again: it decoded it
    /// before, the reader let it go, and decoding it again would take what the walk decoded again past
    /// <see cref="PdfReaderLimits.MaxObjectStreamDecodePasses"/> times what its first decodings weighed — or did already,
    /// past which nothing it let go is decoded again before it ends.
    /// </summary>
    /// <remarks>
    /// A rebuild's taking in of the object streams is not weighed: what it reads makes the index the reader keeps, which no
    /// walk may change. Within the budget, a decoding is counted once done (<see cref="CountWalkDecoding"/>); one that read a
    /// value the guard cut is counted too, though it gave nothing, so that the next object asked of the stream is cut here,
    /// the budget being spent, rather than decode the stream again.
    /// </remarks>
    private bool WalkSpent(int number)
    {
        if (_walkDepth == 0 || _expandingObjectStreams || _walkDecoded is null || !_walkDecoded.TryGetValue(number, out var weight))
        {
            return false;
        }

        // Weighed in 128 bits, where a bound of int.MaxValue times what the first decodings weighed cannot overflow.
        if (!_walkBudgetSpent &&
            (Int128)_walkAgainBytes + weight <= (Int128)_walkFirstBytes * _guard.Bound(PdfLimit.ObjectStreamDecodes))
        {
            return false;
        }

        _walkBudgetSpent = true;
        return true;
    }

    /// <summary>
    /// Counts against the walk in progress a decoding of object stream <paramref name="number"/> that gave
    /// <paramref name="contents"/>: its first in the walk, or one more of a stream the reader let go.
    /// </summary>
    /// <remarks>
    /// A decoding that gave nothing is counted only where it read a value the walk's guard cut — <paramref name="whole"/>
    /// false —, as weighing nothing: the reader does not keep what it gave, and, counted, the stream is not decoded again
    /// before the walk ends (<see cref="WalkSpent"/>), however much data it decodes and however many of its objects the
    /// walk asks for. One that gave nothing for the file's own fault, reading no such value, is kept as what any decoding
    /// gives is, and its objects read from there.
    /// </remarks>
    /// <param name="number">The object stream's number.</param>
    /// <param name="contents">What the decoding gave; null for nothing.</param>
    /// <param name="whole">Whether the decoding read no value the walk's guard cut.</param>
    private void CountWalkDecoding(int number, ObjectStreamContents? contents, bool whole)
    {
        if (_walkDepth == 0 || _expandingObjectStreams || (contents is null && whole))
        {
            return;
        }

        var weight = contents?.Weight ?? 0;

        if ((_walkDecoded ??= new(SeededComparer.Instance)).TryAdd(number, weight))
        {
            _walkFirstBytes += weight;
        }
        else
        {
            _walkAgainBytes += weight;
        }
    }

    /// <summary>
    /// Reports that the walk in progress left object stream <paramref name="number"/> undecoded for
    /// <see cref="PdfReaderLimits.MaxObjectStreamDecodePasses"/>: once a document, where the stream lies; for a document
    /// opened to throw on a guard, each time.
    /// </summary>
    /// <exception cref="PdfLimitExceededException">The document was opened to throw on a guard.</exception>
    private void ReachWalkDecodes(int number)
    {
        if (_limitsReached.Contains(WalkDecodesReported))
        {
            return;
        }

        var passes = _guard.Bound(PdfLimit.ObjectStreamDecodes);
        var weighed = passes == 1
            ? "what its first decodings of them weighed"
            : string.Create(CultureInfo.InvariantCulture, $"{passes} times what its first decodings of them weighed");
        _guard.Reach(
            PdfLimit.ObjectStreamDecodes,
            _diagnostics,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The {_walkName} decoded again the object streams the reader let go as much as {weighed}, {CountText.Bytes(_walkFirstBytes)}: object stream {number} was not decoded again, and the objects it holds read as null until the {_walkName} ends."),
            _xref.TryGet(number, out var entry) && entry.Kind == XRefEntryKind.Regular ? EntryPosition(entry) : -1);
        _limitsReached.Add(WalkDecodesReported);
    }

    /// <summary>
    /// Keeps an object stream's contents, with how deep its decoding went, letting go of the oldest ones kept while what they
    /// weigh and its own weight exceed <see cref="ObjectStreamBudget"/>.
    /// </summary>
    private void KeepObjectStream(int number, ObjectStreamContents? contents, ReadDepth depth)
    {
        _objectStreams.Keep(DepthKey(number, depth.CutBase), (contents, depth));
        KeepForRead(number, depth);
    }

    /// <summary>
    /// Keeps <paramref name="value"/>, which a read at <paramref name="depth"/> gave of object <paramref name="number"/>: in
    /// the cache, for a read nothing cut, or apart, by the base it was made from, for one the loads' nesting or a transient
    /// cut reached, noted for the read in progress (<see cref="_cutCache"/>, <see cref="_cutKeptForRead"/>).
    /// </summary>
    private void Cache(int number, PdfObject value, ReadDepth depth)
    {
        if (depth.CutBase >= 0)
        {
            var key = DepthKey(number, depth.CutBase);
            _cutCache.Keep(key, (value, depth));

            if ((_cutKeysForRead ??= new(SeededComparer.Instance)).Add(key))
            {
                (_cutKeptForRead ??= []).Add(key);
            }

            KeepForRead(number, depth);
            return;
        }

        // One kept from a read at another depth gives way to the read made now, in its place in the order.
        if (_cache.ContainsKey(number))
        {
            _cache[number] = (value, depth);
            return;
        }

        while (_cache.Count >= _cacheCapacity && _cacheOrder.TryDequeue(out var oldest))
        {
            // The queue can name a number the cache let go of already, a rebuild having replaced its definition
            // (ForgetObject): the next is let go in its place, so that the cache never holds more than its capacity. One the
            // walk alone kept is no longer the walk's to let go of as it ends: read again, it is kept again.
            if (_cache.Remove(oldest))
            {
                _walkLoads?.Remove(oldest);
                break;
            }
        }

        if (_cache.TryAdd(number, (value, depth)))
        {
            _cacheOrder.Enqueue(number);
        }
    }

    /// <summary>What the object streams' reports said already, so that each fault is reported once.</summary>
    private sealed class ObjectStreamReports
    {
        /// <summary>Where the data starts of each object stream whose own fault was reported.</summary>
        private readonly HashSet<long> _faults = new(SeededComparer.Instance);

        /// <summary>
        /// Each object reported at another index of an object stream than its entry gives, with where that stream's data
        /// starts: a stream defined again, which a rebuild may serve, is another place.
        /// </summary>
        private readonly HashSet<(long DataStart, int Number)> _moves = new(SeededComparer.Instance);

        /// <summary>Each object reported written as a stream in an object stream, with where that stream's data starts.</summary>
        private readonly HashSet<(long DataStart, int Number)> _streamMembers = new(SeededComparer.Instance);

        /// <summary>Records the fault of the object stream whose data starts at <paramref name="dataStart"/>, and tells whether it is the first.</summary>
        public bool IsFirstFault(long dataStart) => _faults.Add(dataStart);

        /// <summary>Records that object <paramref name="number"/> is elsewhere in the object stream whose data starts at <paramref name="dataStart"/>, and tells whether it is the first time.</summary>
        public bool IsFirstMove(long dataStart, int number) => _moves.Add((dataStart, number));

        /// <summary>Records that object <paramref name="number"/> is written as a stream in the object stream whose data starts at <paramref name="dataStart"/>, and tells whether it is the first time.</summary>
        public bool IsFirstStreamMember(long dataStart, int number) => _streamMembers.Add((dataStart, number));

        /// <summary>Gives a copy of what was reported, which <see cref="RestoreFrom"/> brings back.</summary>
        public ObjectStreamReports Copy()
        {
            var copy = new ObjectStreamReports();
            copy._faults.UnionWith(_faults);
            copy._moves.UnionWith(_moves);
            copy._streamMembers.UnionWith(_streamMembers);
            return copy;
        }

        /// <summary>Adds to this copy what <paramref name="reports"/> holds as reported, which only grew since it was copied.</summary>
        public void TakeIn(ObjectStreamReports reports)
        {
            _faults.UnionWith(reports._faults);
            _moves.UnionWith(reports._moves);
            _streamMembers.UnionWith(reports._streamMembers);
        }

        /// <summary>Brings back what <paramref name="copy"/> held as reported, and forgets what was reported since.</summary>
        public void RestoreFrom(ObjectStreamReports copy)
        {
            Restore(_faults, copy._faults);
            Restore(_moves, copy._moves);
            Restore(_streamMembers, copy._streamMembers);
        }

        /// <summary>Forgets what was reported, for a reader that will report nothing more.</summary>
        public void Clear()
        {
            _faults.Clear();
            _faults.TrimExcess();
            _moves.Clear();
            _moves.TrimExcess();
            _streamMembers.Clear();
            _streamMembers.TrimExcess();
        }
    }

    /// <summary>Says what an object stream's decoded data holds past the edge of a member's window.</summary>
    /// <param name="data">The decoded data.</param>
    private sealed class DecodedDataEdge(ReadOnlyMemory<byte> data) : IPdfFileEdge
    {
        /// <inheritdoc/>
        bool IPdfFileEdge.TokenEndsAt(long absoluteOffset) => EndsAt(data.Span, absoluteOffset);

        /// <summary>
        /// Determines whether a token that reaches a member window's edge at byte <paramref name="offset"/> of the decoded
        /// <paramref name="data"/> ends there: no regular character follows. A window MaxObjectLength bounds ends inside the
        /// data; past the end of data a guard cut there is nothing to ask, and the token stays undecided.
        /// </summary>
        public static bool EndsAt(ReadOnlySpan<byte> data, long offset) => offset < data.Length && !PdfCharacters.IsRegular(data[(int)offset]);
    }

    /// <summary>The objects packed inside one object stream, and where each of them starts.</summary>
    private sealed class ObjectStreamContents : IPdfFileEdge
    {
        private readonly ReadOnlyMemory<byte> _data;
        private readonly int[] _numbers;
        private readonly int[] _offsets;
        private readonly int _first;

        /// <summary>The stream's object number, which its reports name.</summary>
        private readonly int _number;

        /// <summary>Where the stream's data starts in the file, where its reports are placed.</summary>
        private readonly long _position;

        /// <summary>What the object streams' reports said already.</summary>
        private readonly ObjectStreamReports _reports;

        /// <summary>Whether a guard cut the stream's data, raw or decoded: its end is then the limit's, not the file's.</summary>
        private readonly bool _cutByGuard;

        /// <summary>
        /// Each number of the header with its place, the number in the upper half, sorted, so that the first place of a
        /// number is found by a binary search; built the first time an entry's index is wrong, so that an index every entry
        /// of the file gets wrong costs a lookup, not a search. <see cref="Weight"/> counts it from the start.
        /// </summary>
        private long[]? _places;

        private ObjectStreamContents(
            int number,
            long position,
            ObjectStreamReports reports,
            ReadOnlyMemory<byte> data,
            int[] numbers,
            int[] offsets,
            int first,
            bool cutByGuard)
        {
            _number = number;
            _position = position;
            _reports = reports;
            _data = data;
            _numbers = numbers;
            _offsets = offsets;
            _first = first;
            _cutByGuard = cutByGuard;
        }

        public int Count => _numbers.Length;

        /// <summary>Gets the stream's decoded data, which the contents keep for as long as they are kept.</summary>
        public ReadOnlyMemory<byte> Data => _data;

        /// <summary>Asks the decoded data, as <see cref="DecodedDataEdge.EndsAt"/> does.</summary>
        bool IPdfFileEdge.TokenEndsAt(long absoluteOffset) => DecodedDataEdge.EndsAt(_data.Span, absoluteOffset);

        /// <summary>
        /// Gets what keeping the contents costs, in bytes: the decoded data, the header's two tables and the lookup of its
        /// numbers, whether built yet or not — four bytes for each table and eight for the lookup, for each member (#160).
        /// </summary>
        public long Weight => _data.Length + (16L * _numbers.Length);

        public int NumberAt(int index) => _numbers[index];

        /// <summary>Gets where the stream's data starts in the file, where its reports are placed.</summary>
        public long DataStart => _position;

        /// <summary>
        /// Reads the header of object stream <paramref name="number"/>, or gives null when it serves no object: it
        /// declares none, or what it says of itself cannot be believed, which is reported.
        /// </summary>
        /// <remarks>
        /// Each report is placed where the stream's data starts, and says which fault it met and how many of the
        /// stream's objects can still be read from it; it is made once for each stream, however often the stream is
        /// decoded. A <c>/First</c> past data a guard cut is not reported, nor data the guard on filters left encoded, nor a
        /// <c>/Type</c>, <c>/N</c> or <c>/First</c> whose object a guard cut: the guard was, under its own code.
        /// </remarks>
        /// <param name="reader">Says whether a guard cut what a value of the stream's dictionary leads to.</param>
        /// <param name="number">The object stream's number.</param>
        /// <param name="stream">The object stream.</param>
        /// <param name="diagnostics">Receives what is wrong with the stream.</param>
        /// <param name="reports">Says which streams were reported already.</param>
        /// <param name="hiddenByGuard">Whether a guard, not the file, left the stream unable to serve its objects.</param>
        public static ObjectStreamContents? TryCreate(
            PdfFileReader reader, int number, PdfStream stream, PdfDiagnostics diagnostics, ObjectStreamReports reports, out bool hiddenByGuard)
        {
            hiddenByGuard = false;
            var position = stream.Data.Position;

            // A stream of another type serves no member, as the validator and the rebuild read it (#274): nothing past its
            // type is read — its /Length, the parse resolved —, so nothing else it names is needed. A /Type, /N or /First the
            // reader cannot read for one of its guards is the guard's, as data a guard cut before /First is (#395).
            if (!stream.Dictionary.IsOfType(PdfName.ObjStm))
            {
                return HiddenOrUnreadable(PdfName.Type, "is a stream that is not of /Type /ObjStm", 0, out hiddenByGuard);
            }

            var count = stream.Dictionary.GetInteger(PdfName.N, -1);
            var first = stream.Dictionary.GetInteger(PdfName.First, -1);

            // A stream that declares no object is empty, not damaged.
            if (count == 0)
            {
                return null;
            }

            if (count < 0)
            {
                return HiddenOrUnreadable(PdfName.N, ObjectStreamHeader.NoCount, count, out hiddenByGuard);
            }

            if (first < 0)
            {
                return HiddenOrUnreadable(PdfName.First, ObjectStreamHeader.NoFirst, count, out hiddenByGuard);
            }

            var data = Filters.PdfFilterPipeline.Decode(stream, diagnostics, out var leftEncoded, out var cutByGuard);

            // Data the guard on filters left encoded holds none of the stream's objects, as data a guard cut before /First.
            if (leftEncoded)
            {
                hiddenByGuard = true;
                return null;
            }

            if (first > data.Length)
            {
                hiddenByGuard = cutByGuard;

                if (!cutByGuard)
                {
                    ReportUnreadable(
                        diagnostics, reports, number, position, ObjectStreamHeader.FirstPastData(first, data.Length), 0, count);
                }

                return null;
            }

            var header = ObjectStreamHeader.Read(data.Span, count, (int)first, cutByGuard);

            if (header.Fault != ObjectStreamHeaderFault.None)
            {
                ReportUnreadable(diagnostics, reports, number, position, header.Describe(count, data.Length), header.Count, count);
            }

            return header.Count > 0
                ? new ObjectStreamContents(number, position, reports, data, header.Numbers, header.Offsets, (int)first, cutByGuard)
                : null;

            // Says the stream hidden by a guard, where the value under key is the guard's, or reports it unreadable.
            ObjectStreamContents? HiddenOrUnreadable(PdfName key, string fault, long declared, out bool hidden)
            {
                hidden = reader.HeaderValueHidden(stream.Dictionary, key);

                if (!hidden)
                {
                    ReportUnreadable(diagnostics, reports, number, position, fault, 0, declared);
                }

                return null;
            }
        }

        /// <summary>Parses the object the stream holds under <paramref name="expectedNumber"/>, or gives null when it holds none.</summary>
        /// <param name="index">Where the index says the object is among the stream's.</param>
        /// <param name="expectedNumber">The object's number.</param>
        /// <param name="source">Resolves what the object refers to.</param>
        /// <param name="diagnostics">Receives what the index got wrong of the object's place.</param>
        /// <param name="parsing">Receives what parsing met, for the caller to keep or drop.</param>
        /// <param name="maxLength">The most of the data the member is read over, <see cref="PdfReaderLimits.MaxObjectLength"/>.</param>
        /// <param name="maxDepth">How deeply the member may nest its containers, <see cref="PdfReaderLimits.MaxNestingDepth"/>.</param>
        /// <param name="guard">The document's guards, which a container nested past <paramref name="maxDepth"/> reaches.</param>
        /// <param name="cut">
        /// Whether the object runs into the end of data a guard cut, or past <paramref name="maxLength"/>, and was read only
        /// as far as it, or starts past data a guard cut, and was not read.
        /// </param>
        /// <param name="pastLength">
        /// Where the member starts in the data, when it runs past <paramref name="maxLength"/>, which a guard then reports;
        /// -1 otherwise.
        /// </param>
        /// <param name="streamAt">
        /// Where the member starts in the data, when it is written as a stream, which an object stream cannot hold: it is then
        /// no value; -1 otherwise.
        /// </param>
        /// <param name="nesting">Whether a container's nesting cut the member, and whether the thread's stack did.</param>
        public PdfObject? Parse(
            int index,
            int expectedNumber,
            IPdfObjectSource source,
            PdfDiagnostics diagnostics,
            PdfDiagnostics parsing,
            int maxLength,
            int maxDepth,
            PdfLimitGuard guard,
            out bool cut,
            out long pastLength,
            out long streamAt,
            out NestingCut nesting)
        {
            cut = false;
            pastLength = -1;
            nesting = default;
            streamAt = -1;

            if (index < 0 || index >= _numbers.Length || _numbers[index] != expectedNumber)
            {
                // The index in the entry is a hint; the object number is the truth.
                var corrected = IndexOf(expectedNumber);
                if (corrected < 0)
                {
                    return null;
                }

                // The entry is left as it is: the object is found by its number each time it is parsed, and
                // reported the first.
                if (_reports.IsFirstMove(_position, expectedNumber))
                {
                    diagnostics.Repair(
                        PdfDiagnosticCodes.ObjectStreamMemberMoved,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Object {expectedNumber} is at index {corrected} of object stream {_number}, not at index {index}, where the cross-reference index places it."),
                        _position);
                }

                index = corrected;
            }

            // Past data a guard cut, the object is unread for the guard's reasons, not the file's (#259): the header keeps a
            // pair whose member starts past the data only then. Both are at most int.MaxValue, so their sum is a long.
            if ((long)_first + _offsets[index] >= _data.Length)
            {
                cut = true;
                return null;
            }

            var window = ObjectStreamMemberWindow.Of(_offsets, index, _first, _data.Length, _cutByGuard, maxLength);
            var parser = PdfObjectParser.ForObjectStreamMember(
                _data[..window.End], _number, _position, expectedNumber, source, parsing, window.EndsData, window.EndsAtNextMember, this, maxDepth, guard);
            parser.Position = window.Start;
            var value = parser.ParseObject();
            nesting = NestingCut.Of(parser);

            // A member written as a stream is no value (#161).
            if (parser.StoppedAtStream)
            {
                streamAt = window.Start;
                return null;
            }

            cut = !window.EndsData && parser.IsTruncated;
            pastLength = cut && window.Bounded ? window.Start : -1;
            return value;
        }

        /// <summary>
        /// Reports, unless it was already, that object stream <paramref name="number"/> serves <paramref name="kept"/> of
        /// its objects at most.
        /// </summary>
        private static void ReportUnreadable(
            PdfDiagnostics diagnostics, ObjectStreamReports reports, int number, long position, string fault, int kept, long count)
        {
            if (!reports.IsFirstFault(position))
            {
                return;
            }

            diagnostics.Warn(
                PdfDiagnosticCodes.ObjectStreamUnreadable,
                kept == 0
                    ? string.Create(CultureInfo.InvariantCulture, $"Object stream {number} {fault}; none of its objects can be read from it.")
                    : string.Create(CultureInfo.InvariantCulture, $"Object stream {number} {fault}; only the first {kept} of its {count} objects can be read from it."),
                position);
        }

        private int IndexOf(int number)
        {
            _places ??= ObjectStreamPlaces.Of(_numbers);
            return ObjectStreamPlaces.FirstIndexOf(_places, number);
        }
    }
}
