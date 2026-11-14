using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO;

internal sealed partial class PdfFileReader
{
    /// <summary>
    /// Brings the index to where no later load rebuilds it: rebuilds it now, once, when a load could still rebuild it, and
    /// otherwise leaves it as it is. The validator calls it before its first rule, so that what every rule reads was
    /// read after any rebuild, however much or little was read before (#237).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A load rebuilds the index in two cases: the index is known to be incomplete through damage and lacks the object; or
    /// the object is neither where its entry places it, nor near it. The first is known without reading anything. The
    /// second is decided here without loading anything: for each entry in use that places an object in the file, the header
    /// a load would parse there is read alone, as a load reads it — after white space and comments, in a window of
    /// <see cref="HeaderCheckWindow"/> bytes, then in windows eight times larger while the header runs past it, no further
    /// than a load reads —, and the 512 bytes either side are searched as a load searches them. An object a load
    /// would find so is one it finds, whatever its body holds; an entry that places an object in an object stream rebuilds
    /// nothing of its own, the stream's entry aside, which is one of the others. The checks stop at the first entry a load
    /// would rebuild for, and at the first header whose reading would take what settling consumed past twice the file's
    /// length, past the first bytes of each header — what a valid file's headers never take —: the index is then rebuilt,
    /// in one pass, rather than read on.
    /// </para>
    /// <para>
    /// A header that still runs past <see cref="PdfReaderLimits.MaxObjectLength"/> is where a load reaches that guard: one
    /// reports it and looks on, as here; a document opened to throw on a guard throws there instead, and rebuilds nothing,
    /// so that settling rebuilds nothing for it either, and leaves the guard to a load that asks for the object.
    /// </para>
    /// <para>
    /// Once settled, the index changes only where a load relocates an object found near its entry, which changes neither
    /// the entry's kind nor any other entry. A rebuild that reaches a guard, the document opened to throw on one, throws
    /// here once, with the index whole: the next call finds it settled. A rebuild a load set off and left unfinished, the
    /// load having failed, is finished first, and so is one the source failed, which is run again (#167). The
    /// source failing the checks leaves the index unsettled, for the next call to check again; failing the rebuild they set
    /// off, it leaves that rebuild for the next call to run again.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The document was disposed.</exception>
    /// <exception cref="PdfLimitExceededException">
    /// The rebuild reached one of the reader's guards, and the document was opened to throw on one.
    /// </exception>
    internal void SettleIndex()
    {
        ObjectDisposedException.ThrowIf(_closed, typeof(PdfDocument));

        // A rebuild a load set off and could not finish, the load having thrown, is finished first; and so is one the source
        // failed, which is run again.
        if (_unfinishedRebuild is not null)
        {
            FinishUnfinishedRebuild();
        }

        if (_settled)
        {
            return;
        }

        _settled = true;
        _settlingReading = new HeaderReading(HeaderReadingAllowance);
        bool rebuilds;

        try
        {
            rebuilds = !_repaired && (_indexIncomplete || AnyLoadWouldRebuild());
        }
        catch
        {
            // The source failed a check — the checks reach no guard that throws —: what the others would have found is
            // unknown, and the next call checks again.
            _settled = false;
            throw;
        }

        if (rebuilds)
        {
            Repair();
        }
    }

    /// <summary>Determines whether loading some object the index holds in use, where it places it, would rebuild the index.</summary>
    private bool AnyLoadWouldRebuild()
    {
        foreach (var (number, entry) in _xref)
        {
            if (number > 0 && entry.Kind == XRefEntryKind.Regular && !LoadFindsWhereEntryPlaces(number, entry))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a load of object <paramref name="number"/> finds it where <paramref name="entry"/> places it, or
    /// near it, as <see cref="LoadRegularObject"/> looks — what it would parse there is an object of that number —, or would
    /// not rebuild the index for it all the same, a guard thrown first. A guard a load reaches on the way and reports is
    /// reported here as it would be.
    /// </summary>
    private bool LoadFindsWhereEntryPlaces(int number, XRefEntry entry)
    {
        var placement = PlaceHeader(number, entry, _settlingReading!, reportsBound: !_guard.ThrowsOnLimit);

        // A load that reaches the guard on a header reports it and looks on; one opened to throw on a guard throws there,
        // and rebuilds nothing, which settling leaves to a load that asks for the object.
        if (!_guard.ThrowsOnLimit)
        {
            var offset = PositionOf(entry);

            // The guard's message is built only where the guard was reached: settling allocates nothing an entry.
            if (placement.GuardAtOffset)
            {
                ReachLimit(PdfLimit.Object, LimitSubject(number, _guard.Bound(PdfLimit.Object)), offset);
            }

            if (placement.Place == HeaderPlace.PastGuard && placement.Position != offset)
            {
                ReachLimit(PdfLimit.Object, LimitSubject(number, _guard.Bound(PdfLimit.Object)), placement.Position);
            }
        }

        return placement.Place switch
        {
            HeaderPlace.AtOffset or HeaderPlace.Nearby => true,
            HeaderPlace.PastGuard => _guard.ThrowsOnLimit,
            _ => false,
        };
    }

    /// <summary>Gets how much a pass that reads the header of every entry of an index may consume past the first window of each.</summary>
    internal long HeaderReadingAllowance => HeaderReadingFactor * _source.Length;

    /// <summary>
    /// Says what a load of object <paramref name="number"/> finds where <paramref name="entry"/> places it, as
    /// <see cref="LoadRegularObject"/> looks: its header at the offset the entry gives, read as <see cref="TryParseNumberedAt"/>
    /// reads it, unless the offset cuts a number (<see cref="RowCutsNumber"/>); else the header of that number the search near
    /// the offset chooses (<see cref="TryFindObjectHeader"/>), which what a load parses there must confirm, and whether that
    /// search vouches for it; else none. Nothing is parsed past a header, reported or recorded: the guard a load would reach
    /// on the way is said, not reported, but the bound of the scan's memo, which the search reports where
    /// <paramref name="reportsBound"/> asks. Settling and the validator's probe of the cross-reference entries decide each
    /// entry through it, alike.
    /// </summary>
    /// <param name="number">The object's number.</param>
    /// <param name="entry">The regular entry that places it.</param>
    /// <param name="reading">
    /// What the pass may still consume past the first window of each header, which what they consume is taken from — a
    /// header that would need more is <see cref="HeaderPlace.Undecided"/> —, and the run of white space it crossed.
    /// </param>
    /// <param name="reportsBound">Whether the search near the offset reports the bound of the scan's memo, as a load does.</param>
    internal HeaderPlacement PlaceHeader(int number, XRefEntry entry, HeaderReading reading, bool reportsBound = false)
    {
        var offset = PositionOf(entry);
        long atOffset = 0;
        var guardAtOffset = false;

        if (IsInFile(offset))
        {
            var found = HeaderAt(offset, number, reading, out var generation, out var start, atRow: !entry.FoundByReader);

            if (found == HeaderReadingSpent)
            {
                return new HeaderPlacement(HeaderPlace.Undecided, offset, -1, 0, GuardAtOffset: false);
            }

            if (found == number)
            {
                return new HeaderPlacement(HeaderPlace.AtOffset, start, generation, number, GuardAtOffset: false);
            }

            if (found == HeaderPastGuard)
            {
                // A document opened to throw on a guard throws there, and looks no further.
                if (_guard.ThrowsOnLimit)
                {
                    return new HeaderPlacement(HeaderPlace.PastGuard, offset, -1, 0, GuardAtOffset: true);
                }

                guardAtOffset = true;
            }
            else
            {
                atOffset = found;
            }
        }

        var nowhere = new HeaderPlacement(
            guardAtOffset ? HeaderPlace.PastGuard : HeaderPlace.NotFound, offset, -1, atOffset, guardAtOffset);

        if (!TryFindObjectHeader(number, entry, reportsBound, out var nearby, out _, out var confirmation))
        {
            return nowhere;
        }

        // The search reads a header backwards from an "obj"; a load parses forwards from where it starts, and refuses what
        // the search took — "4 0 objection" —, or reaches the guard on it.
        var near = HeaderAt(nearby, number, reading, out var nearGeneration, out _);

        return near switch
        {
            HeaderReadingSpent => new HeaderPlacement(HeaderPlace.Undecided, offset, -1, atOffset, guardAtOffset),
            HeaderPastGuard => new HeaderPlacement(HeaderPlace.PastGuard, nearby, -1, atOffset, guardAtOffset),
            _ when near == number => new HeaderPlacement(HeaderPlace.Nearby, nearby, nearGeneration, atOffset, guardAtOffset, confirmation),
            _ => nowhere,
        };
    }

    /// <summary>
    /// Gives the number of the object whose header starts at <paramref name="offset"/>, read as <see cref="TryParseNumberedAt"/>
    /// reads it for object <paramref name="expected"/> — after white space and comments, in windows read on while the header
    /// runs past them or may have been cut at their edge, up to <see cref="PdfReaderLimits.MaxObjectLength"/> —, with its
    /// generation and where it starts; 0 where no header is; or <see cref="HeaderPastGuard"/> where the header still runs
    /// past the largest window, where a load reaches that guard; or <see cref="HeaderReadingSpent"/> where reading it further
    /// would consume more than <paramref name="reading"/> has left; or, where <paramref name="atRow"/>,
    /// <see cref="HeaderCutsNumber"/> where a header read at the offset starts on a number the offset cuts, the byte before it
    /// read with the first window. Nothing is parsed past the header, reported or recorded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It decides as a load does, and reads no more: a first window of <see cref="HeaderCheckWindow"/> bytes, which holds the
    /// headers of real files; then, while the header runs past the last or may have been cut at its edge, windows eight
    /// times larger each, the one a load starts with, <see cref="InitialObjectWindow"/>, taken on the way, then each the
    /// rest of the window a load reads from the offset; past a run of white space it skips, the windows start again from the
    /// first. Each reads on from where the last stopped — the white space it
    /// crossed, the tokens it lexed whole, a comment running on — and reads again only a token its edge may have cut, from
    /// its start; each ends no further than the window a load reads its first bytes in, but one that reads such a token
    /// again. A header is so read in no window reaching past the one a load reads it in, and with no more read than a load
    /// reads to decide it, the small windows aside, and one crossing window more where a crossing stopped at a run another
    /// reading crossed finds a string that runs through it, which is read again from its start. What decides is the first whole token that rules a header out, or the
    /// header's three, which any window long enough reads alike — or, as a load reads it, an <c>obj</c> a load's window ends
    /// on, after the number of another object than <paramref name="expected"/>, which a load takes as it is.
    /// </para>
    /// <para>
    /// What it consumes past the first window is taken from <paramref name="reading"/>: the bytes it read up to its decision,
    /// past the next window where the first does not decide, which consume what lies up to the decision;
    /// past a run it skips, every window it reads, whole. What it skipped it did not
    /// consume, and a window as large as a load's from the offset may lie far past it: so no reading reads much more than it
    /// consumes. A valid file's entries place their headers exactly, and cross no run to remember. The runs of white space crossed before the header's first token are remembered:
    /// an offset inside one, a crossing that reaches one, or a reading between two tokens whose window ends in one, reads on
    /// from its end, where a load would find what it finds, and what it holds is consumed once; a window that lexes reads
    /// through a run, which a string may hold. Inside a comment, which runs to the end of its line, a run is read as any
    /// other bytes: it may hold no line end.
    /// </para>
    /// </remarks>
    private long HeaderAt(long offset, int expected, HeaderReading reading, out long generation, out long start, bool atRow = false)
    {
        generation = -1;
        start = offset;
        var byteBefore = atRow && offset > 0 ? ByteBeforeRow : 0;
        var cutsNumber = false;

        // What a load reads from the offset at most: its largest window, or the rest of the file.
        var reach = Math.Min(offset + _guard.Bound(PdfLimit.Object), _source.Length);

        // How far the reading has consumed: the first window is read free of the pass's allowance.
        var consumed = offset + HeaderCheckWindow;
        var origin = offset;
        var windowSize = (long)HeaderCheckWindow;
        var crossing = true;

        // Whether the reading skipped a run another reading crossed: past it, every window it reads is consumed whole, since
        // what it skipped was not, and a window as large as a load's from the row may lie far past what it read.
        var skipped = false;

        // What the windows before lexed of the header: the tokens read whole — its number, where it starts, its generation —,
        // whether they stopped inside a comment, and whether the window starts at a token its edge may have cut.
        var lexed = 0;
        long number = 0;
        long numberStart = 0;
        long written = 0;
        var inComment = false;
        var readingAgain = false;

        while (true)
        {
            // White space a reading crossed already is not read again, from the offset or on the way: what lies past it
            // decides, read from a first window again, and what it holds was consumed then. Inside a comment, it is the
            // comment's up to a line end it may not hold.
            if (!inComment)
            {
                var past = reading.PastBlanks(origin);

                if (past != origin)
                {
                    origin = past;
                    consumed = Math.Max(consumed, origin);
                    windowSize = HeaderCheckWindow;
                    skipped = true;
                }
            }

            if (origin >= reach)
            {
                // White space up to where a load stops reading: the file's end, or the guard's.
                return reach < _source.Length ? HeaderPastGuard : 0;
            }

            var length = (int)Math.Min(windowSize, reach - origin);

            // A window ends no further than the one a load reads its first bytes in, from the offset, but one that reads
            // again from its start a token the last window's edge may have cut, which that window reached.
            if (!readingAgain)
            {
                var loadEnd = offset + LoadWindowPast(origin - offset);

                if (origin + length > loadEnd)
                {
                    length = (int)(loadEnd - origin);
                }
            }

            // While white space is crossed, a window stops where a run remembered starts: the crossing joins it there. A
            // window that lexes reads on through it: a string may hold it.
            if (crossing && reading.NextRunAfter(origin) is var next && origin + length > next)
            {
                length = (int)(next - origin);
            }

            // A pass that has spent its allowance reads no more than the first window of each header: what is left of it past
            // a run skipped into it, or past white space crossed, decides, or leaves the entry undecided; a token an edge cut
            // is not read again, since no window that ends in the first one reads it further.
            if (origin + length > consumed && reading.Remaining == 0)
            {
                var free = offset + HeaderCheckWindow;

                if (origin >= free || readingAgain)
                {
                    return HeaderReadingSpent;
                }

                length = (int)(free - origin);
            }

            // Only a first window that starts at the offset reads the byte before it: past white space, none starts on a number.
            byteBefore = origin == offset ? byteBefore : 0;
            using var window = _source.GetWindow(origin - byteBefore, length + byteBefore);
            var span = window.Memory.Span;

            if (byteBefore > 0)
            {
                cutsNumber = RowCutsNumber(span);
                span = span[Math.Min(byteBefore, span.Length)..];
                byteBefore = 0;
            }

            // A source that gives nothing where its length says bytes lie ends there, as a load reads it: no header.
            if (span.IsEmpty)
            {
                return 0;
            }

            if (crossing)
            {
                // A window of nothing but white space is crossed whole: what decides lies past it, and so does what a load
                // reads in the larger windows it grows to. It never reaches into the run remembered, which it stops at or
                // reads on from.
                var firstToken = span.IndexOfAnyExcept(PdfCharacters.Whitespace);

                if (firstToken < 0)
                {
                    var crossed = origin + span.Length;

                    if (!Consume(reading, ref consumed, crossed))
                    {
                        return HeaderReadingSpent;
                    }

                    // From the offset to here is white space: crossed, or a run an earlier reading crossed. The windows
                    // grow as a lexing window does, up to the most a crossing reads at once.
                    reading.Crossed(offset, crossed);
                    origin = crossed;
                    windowSize = Math.Min(Grown(windowSize), BlankCrossingWindow);
                    continue;
                }

                // The white space the window starts with ends the run crossed: it is consumed, and remembered with the run;
                // the window is lexed from the first token on, as the lexer reads it from the offset.
                // Taken however little remains: the window it lies in may decide, and a deciding window is charged whole.
                var lead = origin + firstToken;
                Consume(reading, ref consumed, lead);
                reading.Crossed(offset, lead);
                crossing = false;
                span = span[firstToken..];
                length -= firstToken;
                origin = lead;
                windowSize = StepAtMost(length);
            }

            // The window ends where the file does, or where a load's largest does: past it a load reads nothing.
            var end = origin + span.Length;
            var cut = span.Length == length && end < _source.Length;
            var atReach = origin + length >= reach;

            // A token the window's edge reaches may go on past it — "0000" may be the start of "00004", "obj" that of
            // "objx" —, but not where white space a reading crossed lies, which ends any token but a string still open,
            // which runs through it; at a load's largest window, a load reaches the guard on it whatever follows.
            var blankPast = !atReach && reading.PastBlanks(end) != end;
            var lineEnd = inComment ? LineEndIn(span) : 0;
            var commentRunsOn = inComment && lineEnd == span.Length;
            var lexer = new PdfLexer(span, lineEnd);
            long resume;

            while (true)
            {
                var before = lexer.Position;
                var token = lexer.Read();

                var open = token.Kind is PdfTokenKind.LiteralString or PdfTokenKind.HexString && token.End - token.Start < token.Text.Length + 2;
                var edge = blankPast && !open ? span.Length + 1 : span.Length;

                if (cut && (token.Kind == PdfTokenKind.EndOfInput || token.End >= edge))
                {
                    // The header may go on past the window. At a load's largest, a load keeps a header it read whole —
                    // "5 0 obj" up to the edge —, and reaches the guard on one it did not; at the end of a smaller one, it
                    // takes such a header of another object than the one it reads as it is, and reads no further.
                    if (atReach || (lexed == 2 && number != expected && token.IsKeyword("obj"u8) && EndsLoadWindow(offset, end)))
                    {
                        Consume(reading, ref consumed, end);

                        if (lexed == 2 && token.IsKeyword("obj"u8))
                        {
                            generation = written;
                            start = numberStart;
                            return cutsNumber ? HeaderCutsNumber : number;
                        }

                        return HeaderPastGuard;
                    }

                    if (!Consume(reading, ref consumed, end))
                    {
                        return HeaderReadingSpent;
                    }

                    // Read on from the edge, past the white space and comments up to it, the tokens lexed whole kept; or from
                    // the start of the token the edge may have cut.
                    if (token.Kind == PdfTokenKind.EndOfInput)
                    {
                        resume = end;
                        inComment = commentRunsOn || EndsInComment(span[before..]);
                        readingAgain = false;
                    }
                    else
                    {
                        resume = origin + token.Start;
                        inComment = false;
                        readingAgain = true;
                    }

                    break;
                }

                // A whole token: one no header begins with decides, as the header's three do.
                var whole = lexed switch
                {
                    0 => token.Kind == PdfTokenKind.Integer && token.Integer is > 0 and <= PdfObjectId.MaxNumber,
                    1 => token.Kind == PdfTokenKind.Integer && token.Integer is >= 0 and <= PdfObjectId.MaxGeneration,
                    _ => token.IsKeyword("obj"u8),
                };

                if (!whole || lexed == 2)
                {
                    // Decided: what the reading crossed up to its decision is consumed, however little the pass has left — the
                    // whole window past a run it skipped.
                    Consume(reading, ref consumed, skipped ? end : origin + Math.Min(token.End, span.Length));

                    if (!whole)
                    {
                        return 0;
                    }

                    generation = written;
                    start = numberStart;
                    return cutsNumber ? HeaderCutsNumber : number;
                }

                if (lexed == 0)
                {
                    number = token.Integer;
                    numberStart = origin + token.Start;
                }
                else
                {
                    written = token.Integer;
                }

                lexed++;
            }

            // The next window grows from this one — twice what it reads again at least — while it is one of the small ones
            // and ends short of the window a load reads past this one's edge, from the offset; past them, it is the rest of
            // that window, as a load reads none between: after a crossing, a window starts past the offset, and one as
            // large as a load's would read past it.
            var toLoadEnd = offset + LoadWindowPast(end - offset) - resume;
            windowSize = Grown(windowSize);

            if (windowSize < 2L * (end - resume))
            {
                windowSize = Grown(windowSize);
            }

            if (windowSize >= InitialObjectWindow || 2 * windowSize > toLoadEnd)
            {
                windowSize = toLoadEnd;
            }

            origin = resume;
        }
    }

    /// <summary>
    /// Determines whether <paramref name="end"/> is where a window a load reads from <paramref name="offset"/> ends, one of
    /// <see cref="InitialObjectWindow"/> grown eightfold.
    /// </summary>
    private bool EndsLoadWindow(long offset, long end) => LoadWindowPast(end - offset - 1) == end - offset;

    /// <summary>
    /// Gives where the line ends in <paramref name="span"/>, which starts inside a comment: the first carriage return or line
    /// feed, which the lexer reads on from, or the span's length when the comment runs past it.
    /// </summary>
    private static int LineEndIn(ReadOnlySpan<byte> span)
    {
        var lineEnd = span.IndexOfAny((byte)'\r', (byte)'\n');
        return lineEnd < 0 ? span.Length : lineEnd;
    }

    /// <summary>
    /// Determines whether <paramref name="span"/>, white space and comments the lexer skipped to their end, ends inside a
    /// comment: its last byte that is no white space is a comment's, and no line ends after it.
    /// </summary>
    private static bool EndsInComment(ReadOnlySpan<byte> span)
    {
        var last = span.LastIndexOfAnyExcept(PdfCharacters.Whitespace);
        return last >= 0 && span[(last + 1)..].IndexOfAny((byte)'\r', (byte)'\n') < 0;
    }

    /// <summary>
    /// Gives the window <see cref="HeaderAt"/> reads after one of <paramref name="size"/> bytes: eight times larger, the window
    /// a load starts with, <see cref="InitialObjectWindow"/>, taken on the way, so that no window is larger than the one a
    /// load reads the same bytes in.
    /// </summary>
    private static long Grown(long size) => size < InitialObjectWindow ? Math.Min(size * 8, InitialObjectWindow) : size * 8;

    /// <summary>
    /// Gives the window a load reads from an entry's offset that ends past the first <paramref name="length"/> bytes: the
    /// smallest of its windows, <see cref="InitialObjectWindow"/> grown eightfold up to its guard, that is larger. A header
    /// a window ending there cuts is one a load reads in that window at least.
    /// </summary>
    private long LoadWindowPast(long length)
    {
        var bound = _guard.Bound(PdfLimit.Object);
        var window = (long)InitialObjectWindow;

        while (window <= length && window < bound)
        {
            window *= 8;
        }

        return Math.Min(window, bound);
    }

    /// <summary>
    /// Gives the largest window <see cref="Grown"/> steps through, from <see cref="HeaderCheckWindow"/>, that is no larger
    /// than <paramref name="length"/>, or the first: the one after it is larger than <paramref name="length"/>.
    /// </summary>
    private static long StepAtMost(long length)
    {
        var step = (long)HeaderCheckWindow;

        while (Grown(step) <= length)
        {
            step = Grown(step);
        }

        return step;
    }

    /// <summary>
    /// Takes from <paramref name="reading"/> what a header's reading consumed from <paramref name="consumed"/> to
    /// <paramref name="end"/>, and moves <paramref name="consumed"/> there.
    /// </summary>
    /// <returns><see langword="false"/> when the pass had less left, and is now spent.</returns>
    private static bool Consume(HeaderReading reading, ref long consumed, long end)
    {
        if (end <= consumed)
        {
            return true;
        }

        var taken = reading.Consume(end - consumed);
        consumed = end;
        return taken;
    }
}
