using System.Runtime.ExceptionServices;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO.XRef;

namespace AdCodicem.Pdf.IO;

internal sealed partial class PdfFileReader
{
    /// <summary>
    /// Gets what the rebuild's scan for objects takes of the file (<see cref="ScanForObjects"/>): the memo a rebuild made, or,
    /// before any rebuild, one made now by scanning the file from its start, kept for the document's life.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The memo depends on nothing read before it: the scan starts from the file's start, with its own budget for reads past
    /// declared lengths, and indexes nothing. Making it reads the whole file once, the first time it is asked for; it is kept
    /// only once that scan has run to its end, and the source failing the scan leaves none, for the next call to scan again.
    /// </para>
    /// <para>
    /// It may be asked for while the caller holds a window of the source, and within a load: the scan reads through windows
    /// of its own, each given back before it returns, and no window of a source invalidates another — a source in memory
    /// lends slices of its bytes, one on a file rents each window a buffer of its own (<see cref="PdfFileSource.GetWindow"/>)
    /// —; it loads no object, touches neither the index nor what was read, and reports nothing. A memo past its bound is
    /// not whole (<see cref="ScanMemo.WholeUntil"/>): a caller that has to go without its word at or past there reports the
    /// guard where it does (<see cref="ReachScanMemoBound"/>).
    /// </para>
    /// </remarks>
    internal ScanMemo GetScanMemo()
    {
        if (_scanMemo is { } memo)
        {
            return memo;
        }

        var builder = new ScanMemo.Builder(_guard.Bound(PdfLimit.IndexEntries));
        var trailers = new ScannedTrailers();
        ScanForObjects(builder, redefinitions: null, trailers);
        return _scanMemo = builder.Complete(trailers);
    }

    /// <summary>
    /// Reports that the memo of the scan for objects is not whole, for a caller that has to go without its word at or past
    /// <see cref="ScanMemo.WholeUntil"/>: <see cref="PdfReaderLimits.MaxIndexEntries"/> reached where the first place the memo
    /// left out starts (<see cref="ScanMemo.FullAt"/>), once a document; a document opened to throw on a guard throws each time
    /// a caller has to go without it. Nothing where the memo is whole, or not made.
    /// </summary>
    /// <remarks>
    /// Nothing else reports the memo's bound: a rebuild scans the file again where the memo is not whole, and serves what it
    /// would have served, so that the bound changes nothing until a caller has to go without the memo's word. The report
    /// counts among no read's guards (<see cref="_guardsReached"/>): the memo tells where places lie, not what they hold, and a
    /// read the caller weighs by the guards it reached is not told that a guard cut its value. Among the guards reported
    /// (<see cref="_limitsReached"/>) it is held under a place no byte of the file has, so that the index's own report of the
    /// bound is made wherever it lies, and a rebuild that withdraws what it reported withdraws this report too, for it to be
    /// made again.
    /// </remarks>
    /// <exception cref="PdfLimitExceededException">The memo is not whole, and the document was opened to throw on a guard.</exception>
    internal void ReachScanMemoBound()
    {
        if (_scanMemo is not { IsWhole: false } memo || _limitsReached.Contains(ScanMemoBoundReported))
        {
            return;
        }

        // Thrown before it is held as reported: a document that throws on a guard throws each time.
        _guard.Reach(
            PdfLimit.IndexEntries,
            _diagnostics,
            $"The scan for objects recorded {CountText.Grouped(_guard.Bound(PdfLimit.IndexEntries), "object header or keyword", "object headers and keywords")}; those past them were not recorded.",
            memo.FullAt);
        _limitsReached.Add(ScanMemoBoundReported);
    }

    /// <summary>
    /// Indexes, for a rebuild, the object headers the scan for objects takes (<see cref="ScanForObjects"/>), and gives where
    /// the trailers it meets lie: from the memo of the scan where it holds every header in the order the scan takes them, as
    /// that scan would; otherwise by scanning the file, the memo made on the way where there is none yet, and kept once that
    /// scan has run to its end.
    /// </summary>
    /// <param name="redefinitions">Receives the numbers defined again.</param>
    /// <param name="trailers">Receives where the trailers the scan meets lie.</param>
    /// <returns>The exception a document opened to throw asked for, held until the index is whole; or null.</returns>
    private ExceptionDispatchInfo? IndexScannedObjects(Redefinitions redefinitions, out ScannedTrailers trailers)
    {
        if (_scanMemo is { Trailers: { } noted, InScanOrder: true } memo)
        {
            ExceptionDispatchInfo? replayed = null;
            trailers = noted;

            foreach (var header in memo.HeadersWithin(0, long.MaxValue))
            {
                IndexScannedHeader(header.Position, header.Number, header.Generation, redefinitions, ref replayed);
            }

            return replayed;
        }

        // A memo that is not whole, or not in the scan's order, is left as it is: the scan runs as it ran before there was one.
        trailers = new ScannedTrailers();
        var making = _scanMemo is null ? new ScanMemo.Builder(_guard.Bound(PdfLimit.IndexEntries)) : null;
        var reached = ScanForObjects(making, redefinitions, trailers);

        if (making is not null)
        {
            _scanMemo = making.Complete(trailers);
        }

        return reached;
    }

    /// <summary>
    /// Takes into a rebuilt index the header the scan took at <paramref name="at"/>: the last definition of a number wins,
    /// under the generation its header gives.
    /// </summary>
    private void IndexScannedHeader(long at, int number, int generation, Redefinitions redefinitions, ref ExceptionDispatchInfo? reached)
    {
        // The last definition wins: that is what an incrementally updated file means. Its entry records the generation its
        // header gives, whatever an earlier definition gave (#118). A number the index lacks takes room in it, which it may
        // have no more of.
        var known = _xref.TryGet(number, out _);

        if (known)
        {
            redefinitions.Add(number);
        }

        if (known || HasRoomInRebuild(at, ref reached))
        {
            _xref.Set(number, XRefEntry.Found(at - _headerOffset, generation));
        }
    }

    /// <summary>
    /// Scans the file for every object header it holds outside another object, read once each, to its end, and records what
    /// it takes: for a rebuild, the headers into the index, the last definition of a number winning, under the generation its
    /// header gives (<see cref="IndexScannedHeader"/>); into a memo, each header and each <c>xref</c> keyword with how it took
    /// them; and, for the trailers a rebuild merges, where each <c>trailer</c> keyword and each cross-reference stream it takes
    /// lies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scan reads the file forward, a piece of <see cref="ScanChunkSize"/> bytes at a time, the last byte of a piece
    /// read only to tell an <c>obj</c>, an <c>xref</c> or a <c>trailer</c> that ends the bytes searched from a longer word. A
    /// header is read back from its <c>obj</c> as a load lexes it (<see cref="ReadHeaderBackwards"/>); one whose walk reaches
    /// the start of the piece undecided is read again from further back, as far as it takes: where a piece starts changes
    /// nothing of what the scan takes (#171).
    /// </para>
    /// <para>
    /// Each object the scan takes is delimited as a load reads it (<see cref="DelimitObject"/>): its value, the strings and
    /// comments it holds among it, and, for a stream whose direct <c>/Length</c> an <c>endstream</c> confirms, its data.
    /// What lies within is the object's, and the scan goes on past it: a header or a keyword there — an attached file's own
    /// objects written as they are, a string that quotes a header — is none (#171). A string that runs past its object's
    /// end — an unbalanced parenthesis left unescaped, which runs it into the next objects, or the end of a cut file —
    /// delimits nothing (<see cref="IsRunaway"/>). A stream whose length the scan cannot confirm, an indirect <c>/Length</c>
    /// or a wrong one, has its data end where a load searching for its end would stop: at the first header after it, or at
    /// its <c>endstream</c>; the headers and keywords before that <c>endstream</c> are taken as the bytes give them, comments
    /// unread (<see cref="ScannedPlaceKind.Raw"/>). An object the scan cannot delimit has what follows its header read as the
    /// bytes give it, and the headers within what its delimiting read are taken undelimited, so that no byte is lexed for
    /// one object after another. Between objects, a comment or a string runs to the end of its line, and a header or a
    /// keyword that starts inside one is none (<see cref="SweepLine"/>). A keyword counts as a whole token
    /// (<see cref="NoteTrailerKeyword"/>, <see cref="NoteXRefKeyword"/>).
    /// </para>
    /// <para>
    /// The index takes object numbers as the file gives them, as many as <see cref="PdfReaderLimits.MaxIndexEntries"/>
    /// lets it; past that, a header for a number it holds still replaces its entry, so that the newest definition wins
    /// whatever came before it, and one for a number it lacks is left out. A number defined again takes no room, so a file
    /// that repeats one header millions of times hides nothing behind them. The scan's time follows the file's length: each
    /// byte is searched, swept and walked back over a bounded number of times, and what it keeps the index's bound and the
    /// memo's (<see cref="ScanMemo"/>). Once the memo can hold no more, a scan that only makes it stops, and a rebuild's scans
    /// on as it does with no memo to make.
    /// </para>
    /// </remarks>
    /// <param name="memo">Receives what the scan takes; null for a rebuild that has a memo already, which it cannot use.</param>
    /// <param name="redefinitions">Receives the numbers defined again; null for a scan that only makes a memo, and indexes
    /// nothing.</param>
    /// <param name="trailers">Receives where the trailers the scan meets lie.</param>
    /// <returns>The exception a document opened to throw asked for, held until the index is whole; or null.</returns>
    private ExceptionDispatchInfo? ScanForObjects(ScanMemo.Builder? memo, Redefinitions? redefinitions, ScannedTrailers trailers)
    {
        ExceptionDispatchInfo? reached = null;
        var scan = new ScanState();
        _scanGapBytes = 0;

        while (scan.Resume < _source.Length)
        {
            var windowStart = Math.Max(0, scan.Resume - ScanLead);
            var asked = (int)Math.Min(ScanChunkSize, _source.Length - windowStart);
            using var window = _source.GetWindow(windowStart, asked);
            var span = window.Memory.Span;

            // A window shorter than asked holds the end of the data, as every window does. Elsewhere its last byte is read
            // only as the byte after a keyword that ends the bytes searched.
            var endsData = span.Length < asked || windowStart + span.Length >= _source.Length;
            var searchEnd = endsData ? span.Length : span.Length - 1;
            var objFrom = (int)(scan.ObjFrom - windowStart);
            var trailerFrom = (int)(scan.TrailerFrom - windowStart);
            var xrefFrom = (int)(scan.XRefFrom - windowStart);
            var jumped = false;

            // The next obj, trailer and xref keyword in the bytes searched, each found once and kept until the search passes
            // it: -1 when none lies past where its search stands, -2 before the first search. The xref keywords are looked
            // for only for a memo.
            var objAt = -2;
            var trailerAt = -2;
            var xrefAt = memo is null ? -1 : -2;

            while (true)
            {
                // Past its bound the memo takes nothing more: a scan that only makes it is done, and a rebuild's no longer
                // looks for what only the memo takes.
                if (memo is { IsWhole: false })
                {
                    if (redefinitions is null)
                    {
                        return reached;
                    }

                    memo = null;
                    xrefAt = -1;
                }

                if (objAt != -1 && objAt < objFrom)
                {
                    objAt = objFrom < searchEnd ? span[objFrom..searchEnd].IndexOf(ObjKeyword) : -1;
                    objAt = objAt < 0 ? -1 : objFrom + objAt;
                }

                if (trailerFrom < objFrom)
                {
                    trailerFrom = objFrom;
                }

                if (trailerAt != -1 && trailerAt < trailerFrom)
                {
                    trailerAt = trailerFrom < searchEnd ? span[trailerFrom..searchEnd].IndexOf(TrailerKeyword) : -1;
                    trailerAt = trailerAt < 0 ? -1 : trailerFrom + trailerAt;
                }

                if (xrefAt != -1 && xrefAt < xrefFrom)
                {
                    xrefAt = xrefFrom < searchEnd ? span[xrefFrom..searchEnd].IndexOf(XRefKeyword) : -1;
                    xrefAt = xrefAt < 0 ? -1 : xrefFrom + xrefAt;
                }

                if (xrefAt >= 0 && (objAt < 0 || xrefAt < objAt) && (trailerAt < 0 || xrefAt < trailerAt))
                {
                    NoteXRefKeyword(span, windowStart, xrefAt, ref scan, memo!);
                    xrefFrom = xrefAt + XRefKeyword.Length;
                    continue;
                }

                if (trailerAt >= 0 && (objAt < 0 || trailerAt < objAt))
                {
                    NoteTrailerKeyword(span, windowStart, trailerAt, ref scan, trailers, memo);
                    trailerFrom = trailerAt + TrailerKeyword.Length;
                    continue;
                }

                if (objAt < 0)
                {
                    break;
                }

                objFrom = objAt + ObjKeyword.Length;
                trailerFrom = Math.Max(trailerFrom, objFrom);

                // A header starts no further back than where the object before it ends, from where a load's lexer reads
                // tokens anew, as from the start of the file: the window is walked from there when it holds it.
                var floor = (int)Math.Clamp(scan.TokensFrom - windowStart, 0, objAt);
                var walk = ReadHeaderBackwards(
                    span[floor..], objAt - floor, startsData: windowStart + floor == scan.TokensFrom, signsBeforeUnknown: false, out var number, out var generation, out var headerStart, out _);
                var at = windowStart + floor + headerStart;

                if (walk == HeaderWalk.Undecided)
                {
                    walk = ReadHeaderFurtherBack(windowStart + objAt, scan.TokensFrom, out number, out generation, out at);
                }

                if (walk != HeaderWalk.Header || IsHidden(span, windowStart, at, ref scan))
                {
                    continue;
                }

                if (redefinitions is not null)
                {
                    IndexScannedHeader(at, number, generation, redefinitions, ref reached);
                }

                memo?.TryAddHeader(at, number, generation, at < scan.RawUntil ? ScannedPlaceKind.Raw : ScannedPlaceKind.Clean, scan.TokensFrom);

                // A header within what the delimiting of an object it could not delimit read is not delimited again.
                var extent = default(ObjectExtent);

                if (at >= scan.UndelimitedUntil)
                {
                    extent = DelimitObject(at, at >= windowStart ? span[(int)(at - windowStart)..] : [], endsData, out var lexedTo);

                    if (extent.Kind is ExtentKind.Unknown or ExtentKind.RunawayString)
                    {
                        scan.UndelimitedUntil = lexedTo;
                    }
                }

                if (extent.CrossReferenceStream)
                {
                    trailers.Streams.Add(at);
                }

                if (!scan.Passed(extent, this, span, windowStart))
                {
                    continue;
                }

                // The scan goes on past the object: within this window where its end lies within the bytes searched, from
                // the next window otherwise.
                if (scan.ObjFrom - windowStart <= searchEnd)
                {
                    objFrom = (int)(scan.ObjFrom - windowStart);
                    trailerFrom = xrefFrom = objFrom;
                    continue;
                }

                jumped = true;
                break;
            }

            if (endsData && !jumped)
            {
                break;
            }

            if (!jumped)
            {
                // A keyword that starts within the bytes searched but runs past them is the next window's to find.
                scan.ObjFrom = windowStart + Math.Max(objFrom, searchEnd - (ObjKeyword.Length - 1));
                scan.TrailerFrom = windowStart + Math.Max(trailerFrom, searchEnd - (TrailerKeyword.Length - 1));

                // The search for xref follows the search for obj only past the objects the scan passes, so that a keyword the
                // end of the bytes searched cuts is found by the next piece, which reads it whole: it resumes at most three
                // bytes before where that piece's search for obj does, within the bytes it reads before it.
                scan.XRefFrom = windowStart + Math.Max(xrefFrom, searchEnd - (XRefKeyword.Length - 1));
            }

            scan.Resume = Math.Min(scan.ObjFrom, scan.TrailerFrom);
        }

        return reached;
    }

    /// <summary>
    /// Reads again, from further back, the header of the <c>obj</c> keyword at <paramref name="objPosition"/> whose walk back
    /// reached the start of the piece the scan read it in undecided: the bytes before it are read in windows eight times
    /// larger each time, until the walk decides or reaches <paramref name="tokensFrom"/>. Only a walk across white space,
    /// digits and lines that hold nothing but a comment reaches that far, which ends at the keyword before it.
    /// </summary>
    /// <remarks>
    /// A header starts no further back than <paramref name="tokensFrom"/>, where the last object the scan took ends and a
    /// load's lexer reads tokens anew, as from the start of the data: before it lies that object. One whose tokens run back
    /// past <see cref="PdfReaderLimits.MaxObjectLength"/> from its <c>obj</c>, or past what one window can hold, is none: a
    /// load reads no further. A walk stops at the first byte that is none of a header's, the keyword before it at the
    /// furthest, and its windows, each eight times the one before, read at most about nine times the bytes it crosses, or
    /// the stretch back to <paramref name="tokensFrom"/>: walks that start at different keywords cross different bytes, and
    /// at most a few start in each piece.
    /// </remarks>
    private HeaderWalk ReadHeaderFurtherBack(long objPosition, long tokensFrom, out int number, out int generation, out long at)
    {
        var bound = Math.Min(Math.Min(_guard.Bound(PdfLimit.Object), Array.MaxLength - ObjKeyword.Length - 1L), objPosition - tokensFrom);
        var before = (long)ScanLead;

        while (true)
        {
            before = Math.Min(bound, before * 8);
            var start = objPosition - before;
            var end = Math.Min(_source.Length, objPosition + ObjKeyword.Length + 1);
            using var window = _source.GetWindow(start, (int)(end - start));
            var walk = ReadHeaderBackwards(
                window.Memory.Span, (int)before, startsData: start == tokensFrom, signsBeforeUnknown: false, out number, out generation, out var headerStart, out _);
            at = start + headerStart;

            if (walk != HeaderWalk.Undecided)
            {
                return walk;
            }

            if (before >= bound)
            {
                return HeaderWalk.None;
            }
        }
    }

    /// <summary>
    /// Notes the <c>trailer</c> keyword at <paramref name="keyword"/> in the window, for the rebuild to merge the dictionary
    /// after it, and counts it in the memo: a keyword a load lexes as one, which neither a regular character nor a comment
    /// around it makes part of a longer word or of a comment.
    /// </summary>
    private void NoteTrailerKeyword(
        ReadOnlySpan<byte> span, long windowStart, int keyword, ref ScanState scan, ScannedTrailers trailers, ScanMemo.Builder? memo)
    {
        var end = keyword + TrailerKeyword.Length;

        if ((keyword > 0 && PdfCharacters.IsRegular(span[keyword - 1])) ||
            (end < span.Length && PdfCharacters.IsRegular(span[end])) ||
            IsHidden(span, windowStart, windowStart + keyword, ref scan))
        {
            return;
        }

        trailers.Keywords.Add(windowStart + end);
        memo?.TryAddTrailer(windowStart + keyword, scan.TokensFrom);
    }

    /// <summary>
    /// Records in the memo the <c>xref</c> keyword at <paramref name="keyword"/> in the window: a token a load lexes from
    /// there, outside a comment or a string between objects, or taken as the bytes give it (<see cref="ScannedPlaceKind.Raw"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A token starts at the keyword where the byte before it ends one, as a header's number starts
    /// (<see cref="PdfCharacters.EndsToken"/>) — white space, or a delimiter that ends a token or stands alone —, or ends the
    /// end-of-file marker a writer glues an update to, and no regular character follows it: the end of <c>startxref</c>, a
    /// name <c>/xref</c> and the word <c>xrefs</c> are none, as the search near an offset reads a place's token from its
    /// line (<see cref="IsTokenOnItsLine"/>).
    /// </para>
    /// <para>
    /// Whether a comment or a string holds it is swept apart from the sweep the headers and the <c>trailer</c> keywords are
    /// told by (<see cref="IsHiddenAhead"/>): a header whose comment between its tokens holds the keyword is told as it was
    /// before the scan looked for it.
    /// </para>
    /// </remarks>
    private void NoteXRefKeyword(ReadOnlySpan<byte> span, long windowStart, int keyword, ref ScanState scan, ScanMemo.Builder memo)
    {
        var end = keyword + XRefKeyword.Length;

        // A keyword at the window's first byte starts the data: elsewhere the window holds the bytes before it, read from
        // before where the search resumes.
        if ((keyword > 0 && !PdfCharacters.EndsToken(span[keyword - 1]) && !EndsEndOfFileMarker(span, keyword - 1)) ||
            (end < span.Length && PdfCharacters.IsRegular(span[end])))
        {
            return;
        }

        var position = windowStart + keyword;
        var raw = position < scan.RawUntil;

        if (!raw && IsHiddenAhead(span, windowStart, position, ref scan))
        {
            return;
        }

        memo.TryAddXRefKeyword(position, raw ? ScannedPlaceKind.Raw : ScannedPlaceKind.Clean, scan.TokensFrom);
    }

    /// <summary>
    /// Determines whether <paramref name="position"/> lies in a comment or a string between objects, as a load lexes its
    /// line (<see cref="LineState"/>), the scan sweeping the bytes between where it last stood and there. Within the data
    /// of a stream whose length the scan could not confirm, nothing is.
    /// </summary>
    private bool IsHidden(ReadOnlySpan<byte> span, long windowStart, long position, ref ScanState scan)
    {
        if (position < scan.RawUntil)
        {
            return false;
        }

        // Where the sweep ahead stopped short of the position, it stands where the scan's own would once swept that far: the
        // bytes it swept are not swept again.
        if (scan.AheadTo > scan.SweptTo && scan.AheadTo <= position)
        {
            scan.SweptTo = scan.AheadTo;
            scan.Line = scan.AheadLine;
            scan.StringDepth = scan.AheadDepth;
        }

        Sweep(span, windowStart, position, scan.RawUntil, ref scan.SweptTo, ref scan.Line, ref scan.StringDepth);
        return scan.Line != LineState.Tokens;
    }

    /// <summary>
    /// Determines whether <paramref name="position"/>, past the data of a stream whose length the scan could not confirm,
    /// lies in a comment or a string between objects, as <see cref="IsHidden"/> does, with a sweep of its own that runs
    /// ahead of the scan's own sweep and leaves it where it stood.
    /// </summary>
    /// <remarks>
    /// The scan's sweep moves forward only, and a header is told by where it stands once swept to the header's start: swept
    /// past a keyword that lies in a comment between a header's tokens, it would tell that header otherwise. The sweep ahead
    /// starts where the scan's own stands whenever that one has caught up with it or been moved past it, and from where it
    /// stopped otherwise, each byte swept so by it once; the scan's own sweep, asked for a position the sweep ahead has
    /// reached, takes up where it stopped (<see cref="IsHidden"/>): a byte is swept twice only where a header starts before a
    /// keyword the sweep ahead reached, and twice at most.
    /// </remarks>
    private bool IsHiddenAhead(ReadOnlySpan<byte> span, long windowStart, long position, ref ScanState scan)
    {
        if (scan.AheadTo <= scan.SweptTo)
        {
            scan.AheadTo = scan.SweptTo;
            scan.AheadLine = scan.Line;
            scan.AheadDepth = scan.StringDepth;
        }

        Sweep(span, windowStart, position, scan.RawUntil, ref scan.AheadTo, ref scan.AheadLine, ref scan.AheadDepth);
        return scan.AheadLine != LineState.Tokens;
    }

    /// <summary>
    /// Sweeps the bytes from where the sweep last stood, <paramref name="sweptTo"/>, to <paramref name="to"/>: where a comment
    /// or a string opens, and where it closes. Each byte is swept once: from the window where it holds them, from the file
    /// before it — a header found further back than its piece, or bytes no header nor keyword asked for while the piece
    /// before held them —, a piece at a time. The data of a stream whose length the scan could not confirm, before
    /// <paramref name="rawUntil"/>, is not swept.
    /// </summary>
    private void Sweep(
        ReadOnlySpan<byte> span, long windowStart, long to, long rawUntil, ref long sweptTo, ref LineState line, ref int depth)
    {
        var from = Math.Max(sweptTo, rawUntil);

        while (from < to)
        {
            if (from >= windowStart)
            {
                var start = (int)Math.Min(from - windowStart, span.Length);
                from += SweepLine(span[start..], (int)Math.Min(to - from, span.Length - start), ref line, ref depth);
                break;
            }

            // The bytes after a piece's are read with it, as far as they tell an end-of-file marker that starts within it.
            var length = (int)Math.Min(ScanChunkSize - (EndOfFileMarker.Length - 1), Math.Min(to, windowStart) - from);
            using var window = _source.GetWindow(from, (int)Math.Min(length + EndOfFileMarker.Length - 1L, _source.Length - from));

            if (window.Length == 0)
            {
                break;
            }

            from += SweepLine(window.Memory.Span, Math.Min(length, window.Length), ref line, ref depth);
        }

        sweptTo = Math.Max(sweptTo, from);
    }

    /// <summary>
    /// Runs the state a load's lexer is in over the first <paramref name="length"/> bytes of <paramref name="bytes"/>, from
    /// <paramref name="state"/>: a comment runs to the end of its line, and a string, its parentheses balanced and a
    /// backslash escaping the byte after it, to its closing parenthesis. An end of line ends both: no object holds the
    /// bytes between objects, where a string has no business, and a line is read as its tokens, as an object's header is
    /// (<see cref="ReadHeaderBackwards"/>). The end-of-file marker <c>%%EOF</c> is a comment that ends with it, a writer
    /// gluing the next update to it; the bytes past <paramref name="length"/> are read only to tell it.
    /// </summary>
    /// <returns>How far the bytes were read: <paramref name="length"/>, or past it, by the byte an escape at its end takes
    /// or the rest of an end-of-file marker that starts before it.</returns>
    private static int SweepLine(ReadOnlySpan<byte> bytes, int length, ref LineState state, ref int depth)
    {
        var at = 0;

        while (at < length)
        {
            var rest = bytes[at..length];
            int found;

            switch (state)
            {
                case LineState.Comment:
                    found = rest.IndexOfAny((byte)'\r', (byte)'\n');

                    if (found < 0)
                    {
                        return length;
                    }

                    state = LineState.Tokens;
                    break;

                case LineState.String:
                    found = rest.IndexOfAny(StringBytes);

                    if (found < 0)
                    {
                        return length;
                    }

                    switch (rest[found])
                    {
                        case (byte)'\\':
                            found++;
                            break;
                        case (byte)'(':
                            depth++;
                            break;
                        case (byte)')':
                            state = --depth == 0 ? LineState.Tokens : state;
                            break;
                        default:
                            state = LineState.Tokens;
                            break;
                    }

                    break;

                default:
                    found = rest.IndexOfAny((byte)'%', (byte)'(');

                    if (found < 0)
                    {
                        return length;
                    }

                    if (bytes[(at + found)..].StartsWith(EndOfFileMarker))
                    {
                        found += EndOfFileMarker.Length - 1;
                        break;
                    }

                    state = rest[found] == (byte)'%' ? LineState.Comment : LineState.String;
                    depth = 1;
                    break;
            }

            at += found + 1;
        }

        return at;
    }

    /// <summary>The bytes that change a string's state as a sweep reads it: an escape, a parenthesis, an end of line.</summary>
    private static ReadOnlySpan<byte> StringBytes => "\\()\r\n"u8;

    /// <summary>
    /// Delimits the object whose header starts at <paramref name="at"/>, as a load reads it: its value, then, for a stream,
    /// its data where its direct <c>/Length</c> is confirmed by an <c>endstream</c> after it.
    /// </summary>
    /// <remarks>
    /// The header and value are lexed, nothing built: a token at a time, the containers counted, the keys of the dictionary
    /// a stream's is read for <c>/Length</c> and <c>/Type</c>. A value that runs past the bytes given is read again in a window
    /// from its header, eight times larger each time, as long as it stays within
    /// <see cref="PdfReaderLimits.MaxObjectLength"/>, the bound a load reads it within; past it, or where the header or
    /// value is not one a load reads, the object is not delimited, and what follows its header is read as the bytes give it.
    /// </remarks>
    /// <param name="at">Where the header starts in the file.</param>
    /// <param name="buffered">The bytes from <paramref name="at"/> the scan holds already.</param>
    /// <param name="buffersToEnd">Whether <paramref name="buffered"/> runs to the end of the file.</param>
    /// <param name="lexedTo">Where, in the file, the lexing of an object not delimited stopped.</param>
    private ObjectExtent DelimitObject(long at, ReadOnlySpan<byte> buffered, bool buffersToEnd, out long lexedTo)
    {
        var bound = (long)_guard.Bound(PdfLimit.Object);

        // No more is lexed than the bound, however much the piece holds: where an object past it is let go does not depend
        // on where its piece ends.
        if (buffered.Length > bound)
        {
            buffered = buffered[..(int)bound];
            buffersToEnd = false;
        }

        var bounds = buffered.IsEmpty ? ObjectBounds.Cut : DelimitWithin(buffered, buffersToEnd);
        var size = Math.Max(XRefWindow, 2L * buffered.Length);
        var lexed = (long)buffered.Length;

        // A value the bytes lexed cut, which already reach the bound, runs past it.
        while (bounds.Kind == BoundsKind.Cut)
        {
            if (lexed >= bound)
            {
                lexedTo = at + lexed;
                return default;
            }

            var asked = (int)Math.Min(Math.Min(size, bound), _source.Length - at);
            using var window = _source.GetWindow(at, asked);
            var toEnd = window.Length < asked || at + window.Length >= _source.Length;
            lexed = window.Length;
            bounds = DelimitWithin(window.Memory.Span, toEnd);
            size *= 8;
        }

        // What a load reads of the object, its header and value or its stream's dictionary, lies within the guard's bound.
        lexedTo = at + Math.Max(bounds.End, bounds.DataStart);
        return bounds.Kind switch
        {
            BoundsKind.Value when bounds.End <= bound => new ObjectExtent(ExtentKind.Value, at + bounds.End, -1, false),
            BoundsKind.Stream when bounds.DataStart <= bound => ConfirmStream(at, bounds, buffered),
            BoundsKind.Runaway when bounds.End <= bound => new ObjectExtent(ExtentKind.RunawayString, at + bounds.End, -1, false),
            _ => default,
        };
    }

    /// <summary>
    /// Says where the stream whose header starts at <paramref name="at"/> ends: past the <c>endstream</c> that follows its
    /// direct <c>/Length</c>, white space and comments between, as many as there are, read as a load reads them (#174); or,
    /// its length not confirmed, unknown.
    /// </summary>
    /// <remarks>
    /// The white space a first read cannot see past is read on with the scan's own reads past declared lengths, which
    /// <see cref="EndStreamSearchPasses"/> bounds apart from the loads' searches, and nothing is kept of it: a file of a
    /// million stream headers holds the scan to the time of a few readings of it, and no more memory than one. Once those
    /// reads have read as much as they may — crafted headers whose lengths end in long white space —, a length a first read
    /// cannot confirm is not, and its stream's data is read as an unconfirmed stream's: which ones depends on the order the
    /// scan meets them in, as for the loads' searches.
    /// </remarks>
    /// <param name="at">Where the header starts in the file.</param>
    /// <param name="bounds">What the lexing of the header and dictionary found.</param>
    /// <param name="buffered">The bytes from <paramref name="at"/> the scan holds already.</param>
    private ObjectExtent ConfirmStream(long at, ObjectBounds bounds, ReadOnlySpan<byte> buffered)
    {
        var dataStart = at + bounds.DataStart;
        var dataEnd = dataStart + bounds.Length;
        var keywordEnd = -1L;

        if (bounds.Length >= 0 && bounds.Length < _source.Length - dataStart)
        {
            Span<byte> tail = stackalloc byte[PdfObjectParser.EndObjLookahead];
            var length = (int)Math.Min(tail.Length, _source.Length - dataEnd);
            int read;

            if (dataEnd - at + length <= buffered.Length)
            {
                buffered.Slice((int)(dataEnd - at), length).CopyTo(tail);
                read = length;
            }
            else
            {
                read = _source.Read(dataEnd, tail[..length]);
            }

            var end = StreamEnds.ReadEnd(tail[..read], dataEnd + read >= _source.Length);

            if (end.Kind == StreamEndKind.EndStream)
            {
                keywordEnd = dataEnd + end.KeywordEnd;
            }
            else if (end.Kind == StreamEndKind.Undecided)
            {
                var next = NextSignificant(dataEnd, ref _scanGapBytes, PdfObjectParser.EndObjLookahead);
                keywordEnd = next >= 0 ? EndStreamTokenEnd(next) : -1;
            }
        }

        return keywordEnd >= 0
            ? new ObjectExtent(ExtentKind.Stream, keywordEnd, dataStart, bounds.CrossReferenceStream)
            : new ObjectExtent(ExtentKind.UnconfirmedStream, dataStart, dataStart, bounds.CrossReferenceStream);
    }

    /// <summary>
    /// Gives where the token that starts at <paramref name="position"/> ends, the <c>endstream</c> keyword a stream's data
    /// ends at, whole as a load lexes it — "endstreamx" too —; or -1 where none starts, or where its token runs past a first
    /// read once the scan's reads past declared lengths have read as much as they may (<see cref="RegularRunEnd"/>).
    /// </summary>
    private long EndStreamTokenEnd(long position)
    {
        if (position >= _source.Length)
        {
            return -1;
        }

        Span<byte> bytes = stackalloc byte[PdfObjectParser.EndObjLookahead];
        var read = _source.Read(position, bytes[..(int)Math.Min(bytes.Length, _source.Length - position)]);
        var end = StreamEnds.ReadEnd(bytes[..read], position + read >= _source.Length);

        // The keyword glued to a run of regular characters longer than the read ends where the run does, read on with the
        // scan's reads past declared lengths.
        return end.Kind switch
        {
            StreamEndKind.EndStream => position + end.KeywordEnd,
            StreamEndKind.Undecided when bytes[..read].StartsWith(StreamEnds.EndStreamKeyword) => RegularRunEnd(position + read),
            _ => -1,
        };
    }

    /// <summary>
    /// Gives where the run of regular characters at <paramref name="position"/> ends, read with the scan's reads past
    /// declared lengths (<see cref="_scanGapBytes"/>); -1 once those have read as much as they may.
    /// </summary>
    private long RegularRunEnd(long position)
    {
        var size = PdfObjectParser.EndObjLookahead;

        while (position < _source.Length)
        {
            if (_scanGapBytes >= EndStreamSearchPasses * _source.Length)
            {
                return -1;
            }

            using var window = _source.GetWindow(position, (int)Math.Min(size, _source.Length - position));
            var span = window.Memory.Span;

            if (span.IsEmpty)
            {
                return -1;
            }

            _scanGapBytes += span.Length;

            for (var i = 0; i < span.Length; i++)
            {
                if (!PdfCharacters.IsRegular(span[i]))
                {
                    return position + i;
                }
            }

            position += span.Length;
            size = Math.Min(2 * size, EndStreamSearchChunk);
        }

        return _source.Length;
    }

    /// <summary>
    /// Lexes the object header at the start of <paramref name="bytes"/> and the value after it, and says where the value
    /// ends, or, for a dictionary that <c>stream</c> follows, where its data starts, with its direct <c>/Length</c> and
    /// whether it gives <c>/Type /XRef</c>.
    /// </summary>
    /// <remarks>
    /// A value is read as the parser reads one: a reference is three tokens, and an integer that a second integer and
    /// <c>obj</c> follow is the next object's header, which ends this one where it starts, inside a container too (#230); a
    /// <c>/Length</c> that a generation and <c>R</c> follow is indirect. A keyword that ends an object or a stream inside a
    /// container, or a string that runs past its object's end (<see cref="IsRunaway"/>), leaves the value undelimited, and so
    /// does a comment that holds the word <c>obj</c> before the value or inside it (<see cref="SkipGap"/>).
    /// </remarks>
    private static ObjectBounds DelimitWithin(ReadOnlySpan<byte> bytes, bool endsData)
    {
        var lexer = new PdfLexer(bytes);
        var number = lexer.Read();
        var generation = lexer.Read();
        var keyword = lexer.Read();

        if (!endsData && keyword.End >= bytes.Length)
        {
            return ObjectBounds.Cut;
        }

        if (number.Kind != PdfTokenKind.Integer || generation.Kind != PdfTokenKind.Integer || !keyword.IsKeyword("obj"u8))
        {
            return ObjectBounds.Undelimited(keyword.End);
        }

        if (!SkipGap(ref lexer, bytes))
        {
            return ObjectBounds.Undelimited(lexer.Position);
        }

        var first = lexer.Read();

        if (first.Kind == PdfTokenKind.EndOfInput || (!endsData && first.End >= bytes.Length))
        {
            return endsData ? ObjectBounds.Undelimited(bytes.Length) : ObjectBounds.Cut;
        }

        switch (first.Kind)
        {
            case PdfTokenKind.DictionaryStart or PdfTokenKind.ArrayStart:
                break;

            case PdfTokenKind.Integer:
                return DelimitInteger(ref lexer, bytes, endsData, first);

            case PdfTokenKind.Keyword when IsObjectEnd(first):
                // An empty object: the keyword ends it.
                return new ObjectBounds(BoundsKind.Value, first.Start, -1, -1, false);

            case PdfTokenKind.Unknown:
                return ObjectBounds.Undelimited(first.End);

            default:
                return IsRunaway(first) ? ObjectBounds.Runaway(first.End) : new ObjectBounds(BoundsKind.Value, first.End, -1, -1, false);
        }

        var topIsDictionary = first.Kind == PdfTokenKind.DictionaryStart;
        var depth = 1;
        var expectKey = true;
        var key = DictionaryKey.Other;
        var length = -1L;
        var lengthToken = -1;
        var crossReference = false;
        var count = 0;
        var lastIsInteger = false;
        var beforeIsInteger = false;
        var lastIntegerStart = 0;
        var pairStart = 0;
        var valueEnd = -1;

        while (valueEnd < 0)
        {
            if (!SkipGap(ref lexer, bytes))
            {
                return ObjectBounds.Undelimited(lexer.Position);
            }

            var token = lexer.Read();
            count++;

            if (token.Kind == PdfTokenKind.EndOfInput || (!endsData && token.End >= bytes.Length))
            {
                return endsData ? ObjectBounds.Undelimited(bytes.Length) : ObjectBounds.Cut;
            }

            if (IsRunaway(token))
            {
                return ObjectBounds.Runaway(token.End);
            }

            var inTopDictionary = depth == 1 && topIsDictionary;

            switch (token.Kind)
            {
                case PdfTokenKind.DictionaryStart or PdfTokenKind.ArrayStart:
                    depth++;
                    break;

                case PdfTokenKind.DictionaryEnd or PdfTokenKind.ArrayEnd:
                    depth--;
                    valueEnd = depth == 0 ? token.End : -1;
                    expectKey |= depth == 1;
                    break;

                case PdfTokenKind.Keyword when lastIsInteger && beforeIsInteger && token.IsKeyword("obj"u8):
                    // The next object's header, inside a container this one left open: it ends this one where it starts.
                    valueEnd = pairStart;
                    break;

                case PdfTokenKind.Keyword when IsObjectEnd(token):
                    return ObjectBounds.Undelimited(token.End);

                case PdfTokenKind.Keyword when inTopDictionary && lastIsInteger && beforeIsInteger && token.IsKeyword("R"u8):
                    // A reference: a /Length whose value it is, is indirect.
                    length = lengthToken == count - 2 ? -1 : length;
                    break;

                case PdfTokenKind.Name when inTopDictionary && expectKey:
                    key = token.Text.SequenceEqual("Length"u8) ? DictionaryKey.Length
                        : token.Text.SequenceEqual("Type"u8) ? DictionaryKey.Type
                        : DictionaryKey.Other;
                    expectKey = false;
                    break;

                case PdfTokenKind.Name when inTopDictionary:
                    crossReference |= key == DictionaryKey.Type && token.Text.SequenceEqual("XRef"u8);
                    expectKey = true;
                    break;

                case PdfTokenKind.Integer when inTopDictionary && !expectKey && key == DictionaryKey.Length:
                    length = token.Integer;
                    lengthToken = count;
                    expectKey = true;
                    break;

                case not PdfTokenKind.Unknown when inTopDictionary && !expectKey:
                    expectKey = true;
                    break;
            }

            var isInteger = token.Kind == PdfTokenKind.Integer;

            if (isInteger)
            {
                pairStart = lastIsInteger ? lastIntegerStart : token.Start;
                lastIntegerStart = token.Start;
            }

            beforeIsInteger = lastIsInteger;
            lastIsInteger = isInteger;
        }

        if (!topIsDictionary || !IsDictionaryEnd(bytes, valueEnd))
        {
            return new ObjectBounds(BoundsKind.Value, valueEnd, -1, -1, false);
        }

        // A dictionary that the stream keyword follows is a stream's, whose data starts past the end of line after it.
        lexer.Position = valueEnd;

        switch (ReadRegularToken(ref lexer, bytes, endsData, out var next))
        {
            case LookAhead.Cut:
                return ObjectBounds.Cut;
            case LookAhead.Token when next.IsKeyword("stream"u8):
                break;
            default:
                return new ObjectBounds(BoundsKind.Value, valueEnd, -1, -1, false);
        }

        lexer.SkipStreamEndOfLine();

        if (!endsData && lexer.Position >= bytes.Length)
        {
            return ObjectBounds.Cut;
        }

        return new ObjectBounds(BoundsKind.Stream, valueEnd, lexer.Position, length, crossReference);
    }

    /// <summary>
    /// Delimits a value that starts with the integer <paramref name="first"/>: the integer alone, a reference, or nothing,
    /// where it starts the next object's header.
    /// </summary>
    private static ObjectBounds DelimitInteger(ref PdfLexer lexer, ReadOnlySpan<byte> bytes, bool endsData, PdfToken first)
    {
        switch (ReadRegularToken(ref lexer, bytes, endsData, out var second))
        {
            case LookAhead.Cut:
                return ObjectBounds.Cut;

            case LookAhead.Token when second.Kind == PdfTokenKind.Integer:
                switch (ReadRegularToken(ref lexer, bytes, endsData, out var third))
                {
                    case LookAhead.Cut:
                        return ObjectBounds.Cut;
                    case LookAhead.Token when third.IsKeyword("R"u8):
                        return new ObjectBounds(BoundsKind.Value, third.End, -1, -1, false);
                    case LookAhead.Token when third.IsKeyword("obj"u8):
                        return new ObjectBounds(BoundsKind.Value, first.Start, -1, -1, false);
                }

                break;
        }

        return new ObjectBounds(BoundsKind.Value, first.End, -1, -1, false);
    }

    /// <summary>
    /// Lexes the token after a value only where it can change where the value ends: a run of regular characters — an
    /// integer, <c>R</c>, <c>obj</c>, <c>stream</c> —, which no string, name or container is. A token a delimiter opens is
    /// left unlexed: a string after a value is not the value's, and lexing it for each header it holds would read it again
    /// and again.
    /// </summary>
    private static LookAhead ReadRegularToken(ref PdfLexer lexer, ReadOnlySpan<byte> bytes, bool endsData, out PdfToken token)
    {
        token = default;

        if (!SkipGap(ref lexer, bytes))
        {
            return LookAhead.Other;
        }

        if (lexer.Position >= bytes.Length)
        {
            return endsData ? LookAhead.Other : LookAhead.Cut;
        }

        if (!PdfCharacters.IsRegular(bytes[lexer.Position]))
        {
            return LookAhead.Other;
        }

        token = lexer.Read();
        return !endsData && token.End >= bytes.Length ? LookAhead.Cut : LookAhead.Token;
    }

    /// <summary>Determines whether <paramref name="token"/> is a keyword that ends an object or a stream, or starts one.</summary>
    private static bool IsObjectEnd(PdfToken token) =>
        token.IsKeyword("obj"u8) || token.IsKeyword("endobj"u8) || token.IsKeyword("stream"u8) || token.IsKeyword("endstream"u8);

    /// <summary>Determines whether the value that ends at <paramref name="end"/> in <paramref name="bytes"/> ends with <c>&gt;&gt;</c>.</summary>
    private static bool IsDictionaryEnd(ReadOnlySpan<byte> bytes, int end) =>
        end >= 2 && bytes[end - 1] == (byte)'>' && bytes[end - 2] == (byte)'>';

    /// <summary>
    /// Determines whether <paramref name="token"/> is a string that runs past its object's end, which then delimits nothing:
    /// one that holds the keyword <c>endobj</c> — a parenthesis some writers leave unescaped and unbalanced runs a string
    /// into the objects after it, as far as a later stray one closes it, which a load reads as one string too —, or one the
    /// end of the data leaves open.
    /// </summary>
    private static bool IsRunaway(PdfToken token) =>
        token.Kind is PdfTokenKind.LiteralString or PdfTokenKind.HexString &&
        (token.Text.Length != token.End - token.Start - 2 || HoldsKeyword(token.Text, StreamEnds.EndObjKeyword));

    /// <summary>
    /// Determines whether <paramref name="keyword"/> lies in <paramref name="bytes"/> as a word of its own, which no regular
    /// character before or after it makes part of a longer one.
    /// </summary>
    private static bool HoldsKeyword(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> keyword)
    {
        var from = 0;

        while (true)
        {
            var found = bytes[from..].IndexOf(keyword);

            if (found < 0)
            {
                return false;
            }

            var start = from + found;
            var end = start + keyword.Length;

            if ((start == 0 || !PdfCharacters.IsRegular(bytes[start - 1])) && (end == bytes.Length || !PdfCharacters.IsRegular(bytes[end])))
            {
                return true;
            }

            from = start + 1;
        }
    }

    /// <summary>Where the scan stands, between the pieces it reads.</summary>
    private struct ScanState
    {
        /// <summary>Where the next piece's search starts: the earlier of the two below.</summary>
        public long Resume;

        /// <summary>Where the search for the next <c>obj</c> keyword starts.</summary>
        public long ObjFrom;

        /// <summary>Where the search for the next <c>trailer</c> keyword starts.</summary>
        public long TrailerFrom;

        /// <summary>Where the search for the next <c>xref</c> keyword starts, for a memo.</summary>
        public long XRefFrom;

        /// <summary>Where the sweep for comments and strings has reached.</summary>
        public long SweptTo;

        /// <summary>What the sweep stands in.</summary>
        public LineState Line;

        /// <summary>How many parentheses the string the sweep stands in has open.</summary>
        public int StringDepth;

        /// <summary>Where the sweep ahead, which tells the <c>xref</c> keywords, has reached (<see cref="IsHiddenAhead"/>).</summary>
        public long AheadTo;

        /// <summary>What the sweep ahead stands in.</summary>
        public LineState AheadLine;

        /// <summary>How many parentheses the string the sweep ahead stands in has open.</summary>
        public int AheadDepth;

        /// <summary>
        /// Where the last object the scan took ends, or where the data of a stream whose length it could not confirm starts:
        /// a load's lexer reads tokens anew from there, as from the start of the file.
        /// </summary>
        public long TokensFrom;

        /// <summary>
        /// Where the lexing of the last object the scan could not delimit stopped: a header before it is taken undelimited,
        /// so that no byte is lexed for one object after another.
        /// </summary>
        public long UndelimitedUntil;

        /// <summary>
        /// Where the bytes the scan reads as they give them, no comment nor string swept, end: the data of a stream whose
        /// length it could not confirm, at its first <c>endstream</c>, or a string that runs past its object's end.
        /// </summary>
        public long RawUntil;

        /// <summary>Where the last <c>endstream</c> the scan searched for lies, or -1 before any search.</summary>
        public long EndStreamAt;

        public ScanState()
        {
            EndStreamAt = -1;
        }

        /// <summary>
        /// Takes in the extent of an object the scan took, and says whether the scan goes on past it: where its value or data
        /// ends, where its data starts when its length is not confirmed, the comments before it unread.
        /// </summary>
        /// <param name="extent">What the delimiting of the object found.</param>
        /// <param name="reader">The reader, which searches the data of a stream whose length is not confirmed.</param>
        /// <param name="span">The window the scan holds.</param>
        /// <param name="windowStart">Where the window starts in the file.</param>
        public bool Passed(ObjectExtent extent, PdfFileReader reader, ReadOnlySpan<byte> span, long windowStart)
        {
            switch (extent.Kind)
            {
                case ExtentKind.Value or ExtentKind.Stream:
                    ObjFrom = TrailerFrom = XRefFrom = SweptTo = TokensFrom = extent.End;
                    Line = LineState.Tokens;
                    return true;

                case ExtentKind.RunawayString:
                    // The objects a runaway string runs into are the file's: neither the string nor one it opens on its line
                    // hides them.
                    RawUntil = Math.Max(RawUntil, extent.End);
                    SweptTo = Math.Max(SweptTo, RawUntil);
                    Line = LineState.Tokens;
                    return false;

                case ExtentKind.UnconfirmedStream:
                    var buffered = extent.DataStart >= windowStart && extent.DataStart - windowStart < span.Length
                        ? span[(int)(extent.DataStart - windowStart)..]
                        : [];
                    RawUntil = Math.Max(RawUntil, reader.FindEndStream(extent.DataStart, buffered, ref EndStreamAt));
                    ObjFrom = TrailerFrom = XRefFrom = TokensFrom = extent.DataStart;
                    SweptTo = Math.Max(SweptTo, RawUntil);
                    Line = LineState.Tokens;
                    return true;

                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Gives where the first <c>endstream</c> at or after <paramref name="from"/> starts, or the file's length when none
    /// does; <paramref name="known"/> keeps the last found, which later calls, made from further on, reuse while it lies
    /// past where they start: each byte is searched once. The bytes the scan holds are searched first, then the file, in
    /// reads that grow from <see cref="XRefWindow"/> to <see cref="ScanChunkSize"/>, each twice the one before, so that a
    /// search reads about twice the bytes it crosses.
    /// </summary>
    /// <param name="from">Where the search starts in the file.</param>
    /// <param name="buffered">The bytes from <paramref name="from"/> the scan holds.</param>
    /// <param name="known">Where the last <c>endstream</c> found lies, or -1.</param>
    private long FindEndStream(long from, ReadOnlySpan<byte> buffered, ref long known)
    {
        if (known >= from)
        {
            return known;
        }

        var found = buffered.IndexOf(StreamEnds.EndStreamKeyword);

        if (found >= 0)
        {
            return known = from + found;
        }

        var position = from + Math.Max(0, buffered.Length - (StreamEnds.EndStreamKeyword.Length - 1));
        var size = XRefWindow;

        while (position < _source.Length)
        {
            var asked = (int)Math.Min(size, _source.Length - position);
            using var window = _source.GetWindow(position, asked);
            found = window.Memory.Span.IndexOf(StreamEnds.EndStreamKeyword);

            if (found >= 0)
            {
                return known = position + found;
            }

            if (window.Length < asked || position + window.Length >= _source.Length)
            {
                break;
            }

            position += window.Length - (StreamEnds.EndStreamKeyword.Length - 1);
            size = Math.Min(2 * size, ScanChunkSize);
        }

        return known = _source.Length;
    }

    /// <summary>What a scan's delimiting of an object found (<see cref="DelimitObject"/>).</summary>
    /// <param name="Kind">How far the object was delimited.</param>
    /// <param name="End">Where the object's value or data ends in the file: past the value, past <c>endstream</c>, or, for a
    /// stream whose length is not confirmed, where its data starts.</param>
    /// <param name="DataStart">Where a stream's data starts in the file; -1 for another value.</param>
    /// <param name="CrossReferenceStream">Whether the object is a stream whose dictionary gives <c>/Type /XRef</c>.</param>
    private readonly record struct ObjectExtent(ExtentKind Kind, long End, long DataStart, bool CrossReferenceStream);

    /// <summary>How far a scan delimited an object.</summary>
    private enum ExtentKind
    {
        /// <summary>Not delimited: its header or value is not one a load reads, or runs past what a load reads.</summary>
        Unknown,

        /// <summary>A value, not a stream, read to its end.</summary>
        Value,

        /// <summary>A stream whose direct <c>/Length</c> an <c>endstream</c> confirms.</summary>
        Stream,

        /// <summary>A stream whose length the scan cannot confirm.</summary>
        UnconfirmedStream,

        /// <summary>
        /// Not delimited: a string its value holds runs past its object's end, and what the string holds is read as the bytes
        /// give it, no comment nor string swept there.
        /// </summary>
        RunawayString,
    }

    /// <summary>What the lexing of an object at the start of some bytes found (<see cref="DelimitWithin"/>).</summary>
    /// <param name="Kind">What the bytes held.</param>
    /// <param name="End">Where the value ends, in the bytes; for no header and value a load reads, where their lexing stopped.</param>
    /// <param name="DataStart">Where a stream's data starts, in the bytes.</param>
    /// <param name="Length">A stream's direct <c>/Length</c>, or -1.</param>
    /// <param name="CrossReferenceStream">Whether the dictionary gives <c>/Type /XRef</c>.</param>
    private readonly record struct ObjectBounds(BoundsKind Kind, int End, int DataStart, long Length, bool CrossReferenceStream)
    {
        /// <summary>Gets bounds that say the bytes end before the value does.</summary>
        public static ObjectBounds Cut => new(BoundsKind.Cut, -1, -1, -1, false);

        /// <summary>Gives bounds that say the bytes hold no header and value a load reads, lexed as far as <paramref name="reached"/>.</summary>
        public static ObjectBounds Undelimited(int reached) => new(BoundsKind.None, reached, -1, -1, false);

        /// <summary>Gives bounds that say the value holds a string that runs past its object's end, to <paramref name="end"/>.</summary>
        public static ObjectBounds Runaway(int end) => new(BoundsKind.Runaway, end, -1, -1, false);
    }

    /// <summary>What the lexing of an object found.</summary>
    private enum BoundsKind
    {
        /// <summary>No header and value a load reads.</summary>
        None,

        /// <summary>The bytes end before the value does.</summary>
        Cut,

        /// <summary>A value, not a stream.</summary>
        Value,

        /// <summary>A stream's dictionary and where its data starts.</summary>
        Stream,

        /// <summary>A value that holds a string running past its object's end (<see cref="IsRunaway"/>).</summary>
        Runaway,
    }

    /// <summary>
    /// Skips the white space and comments before the next token, as a load's lexer does, but stops at a comment that holds
    /// the word <c>obj</c>, leaving the position on its <c>%</c>: a header a comment holds, which the scan may take, is
    /// then lexed from its own header rather than for each object before it whose lexing would cross it.
    /// </summary>
    /// <returns>False where a comment that holds the word stopped it.</returns>
    private static bool SkipGap(ref PdfLexer lexer, ReadOnlySpan<byte> bytes)
    {
        var at = lexer.Position;

        while (at < bytes.Length)
        {
            if (PdfCharacters.IsWhitespace(bytes[at]))
            {
                at++;
                continue;
            }

            if (bytes[at] != (byte)'%')
            {
                break;
            }

            var comment = at++;

            // A comment runs to the end of its line, read up to it, or up to the word, whichever comes first.
            while (at < bytes.Length)
            {
                var found = bytes[at..].IndexOfAny(CommentStops);

                if (found < 0)
                {
                    at = bytes.Length;
                    break;
                }

                at += found;

                if (bytes[at] != (byte)'o')
                {
                    break;
                }

                if (bytes[at..].StartsWith(ObjKeyword) && !PdfCharacters.IsRegular(bytes[at - 1]) &&
                    (at + ObjKeyword.Length == bytes.Length || !PdfCharacters.IsRegular(bytes[at + ObjKeyword.Length])))
                {
                    lexer.Position = comment;
                    return false;
                }

                at++;
            }
        }

        lexer.Position = at;
        return true;
    }

    /// <summary>The bytes a comment's reading stops at: its end of line, and the first letter of <c>obj</c>.</summary>
    private static ReadOnlySpan<byte> CommentStops => "\r\no"u8;

    /// <summary>What the token after a value is, as far as it can change where the value ends (<see cref="ReadRegularToken"/>).</summary>
    private enum LookAhead
    {
        /// <summary>A token a delimiter opens, or none before the end of the data.</summary>
        Other,

        /// <summary>The bytes end before the token does.</summary>
        Cut,

        /// <summary>A run of regular characters, lexed.</summary>
        Token,
    }

    /// <summary>What a sweep of the bytes between objects stands in (<see cref="SweepLine"/>).</summary>
    private enum LineState
    {
        /// <summary>Tokens: a header or a keyword that starts here is read.</summary>
        Tokens,

        /// <summary>A comment, to the end of its line.</summary>
        Comment,

        /// <summary>A string, to its closing parenthesis or the end of its line.</summary>
        String,
    }

    /// <summary>The keys of a stream's dictionary the scan reads.</summary>
    private enum DictionaryKey
    {
        /// <summary>Another key.</summary>
        Other,

        /// <summary><c>/Length</c>.</summary>
        Length,

        /// <summary><c>/Type</c>.</summary>
        Type,
    }
}
