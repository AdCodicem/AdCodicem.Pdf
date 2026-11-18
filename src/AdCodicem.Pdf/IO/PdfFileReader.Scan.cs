using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO;

internal sealed partial class PdfFileReader
{
    /// <summary>
    /// Gets what the rebuild's scan for objects takes of the file (<see cref="ScanForObjects"/>): a memo made the first time it
    /// is asked for, by scanning the file from its start, and kept for the document's life.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The memo depends on nothing read before it: the scan starts from the file's start, with its own budgets for the
    /// reads that confirm streams' lengths, and indexes nothing. Making it reads the whole file once, the first time it is
    /// asked for, a rebuild run before or not, since a rebuild makes none (<see cref="IndexScannedObjects"/>); it is kept
    /// only once that scan has run to its end, and the source failing the scan leaves none, for the next call to scan
    /// again.
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
    /// Reports that the memo of the scan for objects is not whole, for a caller that has to go without its word at one of
    /// <paramref name="positions"/>, where its bound alone keeps it from answering (<see cref="ScanMemo.IsPastBound"/>):
    /// <see cref="PdfReaderLimits.MaxIndexEntries"/> reached where the first place the memo left out starts
    /// (<see cref="ScanMemo.FullAt"/>), once a document; a document opened to throw on a guard throws each time a caller has
    /// to go without it there. Nothing where the memo is whole, or not made, nor for a place past where a window the source
    /// gave shorter than asked left it whole, which raising the bound would not let it answer for.
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
    /// <param name="positions">Where the places the caller goes without the memo's word at start; -1 for none.</param>
    /// <exception cref="PdfLimitExceededException">
    /// The bound keeps the memo from answering at one of <paramref name="positions"/>, and the document was opened to throw on
    /// a guard.
    /// </exception>
    internal void ReachScanMemoBound(ReadOnlySpan<long> positions)
    {
        if (_scanMemo is not { IsWhole: false } memo || _limitsReached.Contains(ScanMemoBoundReported) || !IsAnyPastBound(memo, positions))
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

        static bool IsAnyPastBound(ScanMemo memo, ReadOnlySpan<long> positions)
        {
            foreach (var position in positions)
            {
                if (memo.IsPastBound(position))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Indexes, for a rebuild, the object headers the scan for objects takes (<see cref="ScanForObjects"/>), and gives where
    /// the trailers it meets lie: from the memo of the scan where one was made that holds every header in the order the scan
    /// takes them, as that scan would; otherwise by scanning the file, which makes no memo.
    /// </summary>
    /// <remarks>
    /// The memo serves the searches near a row and near an offset, which run before a rebuild; once the index is rebuilt it
    /// comes from the scan, and what still asks for the memo makes it (<see cref="GetScanMemo"/>). A rebuild that made one
    /// would make every rebuild pay for a use that rarely comes. A memo whose scan reached
    /// <see cref="PdfReaderLimits.MaxRebuildReadPasses"/> is not taken from: the memo reports nothing, and the rebuild scans
    /// the file again, which reports the guard where its scan reaches it.
    /// </remarks>
    /// <param name="redefinitions">Receives the numbers defined again.</param>
    /// <param name="trailers">Receives where the trailers the scan meets lie.</param>
    /// <returns>The exception a document opened to throw asked for, held until the index is whole; or null.</returns>
    private ExceptionDispatchInfo? IndexScannedObjects(Redefinitions redefinitions, out ScannedTrailers trailers)
    {
        if (_scanMemo is { Trailers: { } noted, InScanOrder: true, ReadsBounded: false } memo)
        {
            ExceptionDispatchInfo? replayed = null;
            trailers = noted;

            foreach (var header in memo.HeadersWithin(0, long.MaxValue))
            {
                IndexScannedHeader(header.Position, header.Number, header.Generation, redefinitions, ref replayed);
            }

            return replayed;
        }

        trailers = new ScannedTrailers();
        return ScanForObjects(memo: null, redefinitions, trailers);
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
    /// header gives (<see cref="IndexScannedHeader"/>); or, for a memo, each header and each <c>xref</c> keyword with how it
    /// took them; and, for the trailers a rebuild merges, where each <c>trailer</c> keyword and each cross-reference stream it
    /// takes lies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scan reads the file forward, a piece of <see cref="ScanChunkSize"/> bytes at a time, the last byte of a piece
    /// read only to tell an <c>obj</c>, an <c>xref</c> or a <c>trailer</c> that ends the bytes searched from a longer word; a
    /// keyword that runs past the bytes searched is the next piece's to find whole, each search resuming as far back as such
    /// a keyword starts. A header is read back from its <c>obj</c> as a load lexes it (<see cref="ReadHeaderBackwards"/>);
    /// one whose walk reaches the start of the piece undecided is read again from further back, as far as it takes: where a
    /// piece starts changes nothing of what the scan takes (#171), but for three hand-made shapes (#358). The reads that
    /// tell a stream from an intact one count only what they read past the bytes the scan holds
    /// (<see cref="StartsStreamWithin"/>): where a piece ends changes what they spend of their own budget
    /// (<see cref="MemoKindReadPasses"/>), and, once it nears its bound, how the memo counts the places in the data of the
    /// streams after them, never what the scan takes (#361). The reads that confirm an indirect length count every byte they
    /// search, from the piece or from the file (<see cref="FindLengthHeader"/>), and so do the sweeps for the memo's kinds
    /// (<see cref="SweepForKinds"/>): where a piece ends changes nothing of what they spend.
    /// </para>
    /// <para>
    /// Each object the scan takes is delimited as a load reads it (<see cref="DelimitObject"/>): its value, the strings
    /// and comments it holds among it, and, for a stream whose length an <c>endstream</c> confirms, its data — a direct
    /// <c>/Length</c>, or an indirect one whose object the file's bytes give, one of the last values the scan passed or a
    /// header past the data's first <c>endstream</c> (<see cref="ConfirmIndirectLength"/>, #363). What lies within is the
    /// object's, and the scan goes on past it: a header or a keyword there — an attached file's own objects written as
    /// they are, a trailer its text spells, a string that quotes a header — is none (#171). A string that runs past its
    /// object's end — an unbalanced parenthesis left unescaped, which runs it into the next objects, or the end of a cut
    /// file — delimits nothing (<see cref="IsRunaway"/>). A stream whose length the scan does not confirm, as far as the
    /// bytes tell (<see cref="ConfirmStream"/>) — no length, a direct one no <c>endstream</c> follows, an indirect one
    /// whose object it does not find or whose length no <c>endstream</c> follows — has its data read as the bytes give it
    /// up to its first <c>endstream</c>, comments unread, the headers and keywords there taken
    /// (<see cref="ScannedPlaceKind.Raw"/>), whatever its length says: a length the scan cannot confirm may be no length
    /// of the stream's — a direct one in the bytes an attached file spells, an indirect one whose object the scan reads in
    /// the file's bytes, an older revision's or an attached file's —, and changes only how the memo counts the places the
    /// scan takes. Once the reads that confirm direct lengths, or those that confirm indirect ones, each counted apart,
    /// have read as much as <see cref="PdfReaderLimits.MaxRebuildReadPasses"/> lets them, the scan reads such data as an
    /// intact stream's instead, up to its first <c>endstream</c>, nothing taken there (#361): the streams an attached
    /// file's bytes spell there would otherwise go on spending them. Where the bytes show that no <c>endstream</c>
    /// follows such a length, the memo holds what the scan takes past where a load ends the data as taken between
    /// objects, as a scan between objects would take it, a header or a keyword that a comment or a string between objects
    /// holds there, swept from that end, held as taken in the data (<see cref="KindAt"/>, <see cref="TakeCleanSwept"/>):
    /// past where <see cref="DataEndWithoutEndStream"/> says, for a direct <c>/Length</c>; for an indirect one, once its
    /// object, read in the file's bytes, shows it (<see cref="EndDataAtLengthObject"/>, <see cref="EndDataWithLength"/>),
    /// or, for one whose object the scan never reads, past where a load ends the data without a length, or past an
    /// <c>xref</c> or a <c>trailer</c> keyword before it (<see cref="EndAwaitedData"/>). The data of an attached file — a
    /// stream of <c>/Type /EmbeddedFile</c>, or data that starts with a file's header (<see cref="StartsAttachedFile"/>) —
    /// holds objects of its own: no length's object is read inside it, it ends the wait for another stream's, and its
    /// places stay held as taken in it but where its own length's object, met outside it, shows where it ends
    /// (<see cref="EndAttachedData"/>). An object the scan
    /// cannot delimit has what follows its header read as the bytes give it, and the headers within what its delimiting
    /// read are taken undelimited, so that no byte is lexed for one object after another. Between objects, a comment or
    /// a string runs to the end of its line, and a header or a keyword that starts inside one is none
    /// (<see cref="SweepLine"/>). A keyword counts as a whole token (<see cref="NoteTrailerKeyword"/>,
    /// <see cref="NoteXRefKeyword"/>).
    /// </para>
    /// <para>
    /// The index takes object numbers as the file gives them, as many as <see cref="PdfReaderLimits.MaxIndexEntries"/>
    /// lets it; past that, a header for a number it holds still replaces its entry, so that the newest definition wins
    /// whatever came before it, and one for a number it lacks is left out. A number defined again takes no room, so a file
    /// that repeats one header millions of times hides nothing behind them. The scan's time follows the file's length: each
    /// byte is searched, swept and walked back over a bounded number of times, and what it keeps the index's bound and the
    /// memo's (<see cref="ScanMemo"/>). A scan for a memo stops at the first place the memo can no longer hold.
    /// </para>
    /// </remarks>
    /// <param name="memo">Receives what the scan takes; null for a rebuild's scan, which looks for no <c>xref</c> keyword.</param>
    /// <param name="redefinitions">Receives the numbers defined again, for a rebuild's scan; null for a scan for a memo, which
    /// indexes nothing.</param>
    /// <param name="trailers">Receives where the trailers the scan meets lie.</param>
    /// <returns>The exception a document opened to throw asked for, held until the index is whole; or null.</returns>
    private ExceptionDispatchInfo? ScanForObjects(ScanMemo.Builder? memo, Redefinitions? redefinitions, ScannedTrailers trailers)
    {
        ExceptionDispatchInfo? reached = null;
        var scan = new ScanState(memo, trailers);
        _scanGapBytes = _scanLengthBytes = _scanKindBytes = 0;

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
            var jumped = false;

            // The next obj, trailer and xref keyword in the bytes searched, each found once and kept until the search passes
            // it: -1 when none lies past where its search stands, -2 before the first search. The xref keywords are looked
            // for only for a memo, and their search keeps where it stands in the scan's state.
            var objAt = -2;
            var trailerAt = -2;
            scan.StartPiece(windowStart, span.Length, asked);

            while (true)
            {
                if (objAt != -1 && objAt < objFrom)
                {
                    objAt = objFrom < searchEnd ? span[objFrom..searchEnd].IndexOf(ObjKeyword) : -1;
                    objAt = objAt < 0 ? -1 : objFrom + objAt;
                }

                if (trailerAt != -1 && trailerAt < trailerFrom)
                {
                    trailerAt = trailerFrom < searchEnd ? span[trailerFrom..searchEnd].IndexOf(TrailerKeyword) : -1;
                    trailerAt = trailerAt < 0 ? -1 : trailerFrom + trailerAt;
                }

                if (scan.PieceXRefAt != -1)
                {
                    if (scan.PieceXRefAt < scan.PieceXRefFrom)
                    {
                        scan.PieceXRefAt = scan.PieceXRefFrom < searchEnd ? span[scan.PieceXRefFrom..searchEnd].IndexOf(XRefKeyword) : -1;
                        scan.PieceXRefAt = scan.PieceXRefAt < 0 ? -1 : scan.PieceXRefFrom + scan.PieceXRefAt;
                    }

                    if (scan.PieceXRefAt >= 0 && (objAt < 0 || scan.PieceXRefAt < objAt) && (trailerAt < 0 || scan.PieceXRefAt < trailerAt))
                    {
                        if (!NoteXRefKeyword(span, windowStart, scan.PieceXRefAt, ref scan, memo!))
                        {
                            return reached;
                        }

                        scan.PieceXRefFrom = scan.PieceXRefAt + XRefKeyword.Length;
                        continue;
                    }
                }

                if (trailerAt >= 0 && (objAt < 0 || trailerAt < objAt))
                {
                    if (!NoteTrailerKeyword(span, windowStart, trailerAt, ref scan, trailers, memo))
                    {
                        return reached;
                    }

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
                    walk = ReadHeaderFurtherBack(windowStart + objAt, scan.TokensFrom, memo, out number, out generation, out at);
                }

                if (walk != HeaderWalk.Header || IsHidden(span, windowStart, at, ref scan))
                {
                    continue;
                }

                if (memo is null)
                {
                    IndexScannedHeader(at, number, generation, redefinitions!, ref reached);
                }
                else if (!memo.TryAddHeader(at, number, generation, KindAt(span, windowStart, at, ref scan, ahead: false), scan.TokensFrom))
                {
                    return reached;
                }

                // A header within what the delimiting of an object it could not delimit read is not delimited again.
                var extent = default(ObjectExtent);

                if (at >= scan.UndelimitedUntil)
                {
                    extent = DelimitObject(at, span, windowStart, endsData, ref scan);
                }

                if (extent.CrossReferenceStream)
                {
                    trailers.Streams.Add(at);
                }

                // Once the reads that confirm direct lengths, or those that confirm indirect ones, have read all they may, the
                // data of each stream the scan cannot confirm is read as an intact stream's, up to its first endstream (#361):
                // the guard is reported at the first such stream, once a scan, whatever its data holds.
                var intact = extent.Kind == ExtentKind.UnconfirmedStream && (_scanGapBytes >= ScanReadBound || _scanLengthBytes >= ScanReadBound);

                if (intact && !scan.ReadsBounded)
                {
                    scan.ReadsBounded = true;
                    ReachScanReadBound(extent.DataStart, memo, ref reached);
                }

                if (!scan.Passed(extent, intact, this, span, windowStart))
                {
                    continue;
                }

                // The index stops short of the file only where data the scan left unread holds a header it would have taken:
                // at the first such stream's, up to the endstream its passing found.
                if (intact && memo is null && _rebuildReadsBoundedAt < 0 && HoldsHeader(extent.DataStart, scan.EndStreamAt))
                {
                    _rebuildReadsBoundedAt = extent.DataStart;
                }

                // An object the sweep for the memo's kinds took between objects is passed by it, as the scan's own sweep
                // passes one.
                if (scan.KindCleanAt == at && extent.Kind is ExtentKind.Value or ExtentKind.Stream && extent.End > scan.KindSweptTo)
                {
                    (scan.KindSweptTo, scan.KindLine, scan.KindDepth) = (extent.End, LineState.Tokens, 0);
                }

                // The scan goes on past the object: within this window where its end lies within the bytes searched, from
                // the next window otherwise.
                if (scan.ObjFrom - windowStart <= searchEnd)
                {
                    objFrom = (int)(scan.ObjFrom - windowStart);
                    trailerFrom = scan.PieceXRefFrom = objFrom;
                    continue;
                }

                jumped = true;
                break;
            }

            if (endsData && !jumped)
            {
                if (windowStart + span.Length < _source.Length)
                {
                    memo?.EndAt(windowStart + span.Length, scan.TokensFrom);
                }

                break;
            }

            if (!jumped)
            {
                // A keyword that starts within the bytes searched but runs past them is the next window's to find, which reads it
                // whole. The searches for trailer and xref follow the search for obj only past the objects the scan passes, so
                // that the next piece does not move them past such a keyword: each resumes at most a few bytes before where
                // that piece's search for obj does, within the bytes it reads before it.
                scan.ObjFrom = windowStart + Math.Max(objFrom, searchEnd - (ObjKeyword.Length - 1));
                scan.TrailerFrom = windowStart + Math.Max(trailerFrom, searchEnd - (TrailerKeyword.Length - 1));
                scan.XRefFrom = windowStart + Math.Max(scan.PieceXRefFrom, searchEnd - (XRefKeyword.Length - 1));
            }

            scan.Resume = Math.Min(scan.ObjFrom, scan.TrailerFrom);
        }

        // Once the file is read, only the kinds of what a memo took change: a rebuild's scan has taken its headers.
        EndAwaitedData([], 0, ref scan);
        return reached;
    }

    /// <summary>
    /// Gets how many bytes the scan may read to confirm streams' direct lengths (<see cref="_scanGapBytes"/>):
    /// <see cref="PdfReaderLimits.MaxRebuildReadPasses"/> times the length of the file.
    /// </summary>
    private long ScanReadBound => Passes(_guard.Bound(PdfLimit.RebuildReads), _source.Length);

    /// <summary>
    /// Gets how many bytes the scan may read to decide only the memo's kinds (<see cref="_scanKindBytes"/>):
    /// <see cref="MemoKindReadPasses"/> times the length of the file, as the loads' searches bound theirs — a source that
    /// says it holds more than a quarter of what a position can name wraps the product into a bound already spent, which
    /// leaves the kinds as they are.
    /// </summary>
    private long MemoKindReadBound => MemoKindReadPasses * _source.Length;

    /// <summary>
    /// Gives <paramref name="passes"/> times <paramref name="length"/>, held at <see cref="long.MaxValue"/>: a source may say
    /// it holds as many bytes as a position can name, which a product would overflow into a bound already spent.
    /// </summary>
    private static long Passes(int passes, long length) => length > long.MaxValue / passes ? long.MaxValue : passes * length;

    /// <summary>
    /// Reports, for a rebuild's scan, that its reads that confirm streams' lengths — past direct ones, or, counted apart,
    /// for the objects of indirect ones — reached <see cref="PdfReaderLimits.MaxRebuildReadPasses"/> where the data of the
    /// stream that starts at <paramref name="position"/> is read as an intact stream's for it, and holds the exception a
    /// document opened to throw asked for in <paramref name="reached"/>, unless one is held already: the rebuild finishes
    /// first. A scan for the memo, which reports nothing, notes it in the memo instead
    /// (<see cref="ScanMemo.Builder.ReadsBounded"/>).
    /// </summary>
    /// <remarks>
    /// The report counts among no read's guards (<see cref="_guardsReached"/>): the scan's reads decide where places lie,
    /// not what a value holds, and a load that set the rebuild off is not told that a guard cut its value. The index stops
    /// short of the file only once data left unread holds a header the scan would have taken
    /// (<see cref="_rebuildReadsBoundedAt"/>): what it lacks is then the guard's, as where it filled, whether the file holds
    /// such an object unknown (<see cref="ObjectPresence.Unindexed"/>), and a read of one throws again where the guard was
    /// reported (<see cref="_rebuildReadsReportedAt"/>). A rebuild the source fails withdraws the report with the rest of
    /// what it reported, and the rebuild run again scans, and reports, again.
    /// </remarks>
    private void ReachScanReadBound(long position, ScanMemo.Builder? memo, ref ExceptionDispatchInfo? reached)
    {
        if (memo is not null)
        {
            memo.ReadsBounded = true;
            return;
        }

        _rebuildReadsReportedAt = position;

        try
        {
            _guard.Reach(PdfLimit.RebuildReads, _diagnostics, RebuildReadsMessage(), position);
        }
        catch (PdfLimitExceededException exception)
        {
            reached ??= ExceptionDispatchInfo.Capture(exception);
        }
    }

    /// <summary>Says that the rebuild's scan reached <see cref="PdfReaderLimits.MaxRebuildReadPasses"/>.</summary>
    private string RebuildReadsMessage()
    {
        var passes = _guard.Bound(PdfLimit.RebuildReads);
        var read = passes == 1 ? "the length of the file" : string.Create(CultureInfo.InvariantCulture, $"{passes:N0} times the length of the file");
        return $"To confirm streams' lengths, the scan for objects read {read}; the data of each stream it could not confirm after that was read up to its first endstream, the object headers there not taken.";
    }

    /// <summary>
    /// Determines whether the file holds, from <paramref name="from"/> up to <paramref name="to"/>, an object header the scan
    /// would take were it to read that data as the bytes give it: an <c>obj</c> keyword from which it would read a header
    /// back (<see cref="ReadHeaderBackwards"/>), from no further back than <paramref name="from"/>, where it would read
    /// tokens anew. An <c>obj</c> no header precedes — a word in a comment or a string — holds none.
    /// </summary>
    /// <remarks>
    /// Read in windows of <see cref="ScanChunkSize"/> bytes, from <paramref name="from"/> to the byte at
    /// <paramref name="to"/>, each overlapping the one before by a keyword and the byte after it; a walk back goes no
    /// further than the window it lies in, and one that reaches the start of a window past <paramref name="from"/> undecided
    /// is taken as a header, which may start before it. The scan asks only until one answers, once a rebuild, of the data
    /// of streams it reads as intact, which their passing has already searched for their <c>endstream</c>: no more than
    /// that data is read again, and the walks in a window read it a bounded number of times.
    /// </remarks>
    private bool HoldsHeader(long from, long to)
    {
        var position = from;
        var stop = Math.Min(to + 1, _source.Length);

        while (position < stop)
        {
            var asked = (int)Math.Min(ScanChunkSize, stop - position);
            using var window = _source.GetWindow(position, asked);
            var bytes = window.Memory.Span;
            var index = 0;

            while (index < bytes.Length && bytes[index..].IndexOf(ObjKeyword) is var found and >= 0)
            {
                index += found;
                var after = index + ObjKeyword.Length;

                // A keyword whose next byte the window does not hold is the next window's to read, but at the file's end.
                if (position + after <= to && (after < bytes.Length || position + after >= _source.Length) &&
                    ReadHeaderBackwards(bytes, index, startsData: position == from, signsBeforeUnknown: false, out _, out _, out _, out _) != HeaderWalk.None)
                {
                    return true;
                }

                index++;
            }

            if (bytes.Length < asked || position + bytes.Length >= stop)
            {
                return false;
            }

            position += bytes.Length - (ObjKeyword.Length + 1);
        }

        return false;
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
    /// at most a few start in each piece. A window the source gives shorter than asked holds the end of the data (#52), which
    /// the keyword then lies past: the walk ends there with no header, and the scan goes on past the keyword, a memo whole
    /// only before <paramref name="tokensFrom"/>, where the header it did not read may start
    /// (<see cref="ScanMemo.Builder.EndAt"/>).
    /// </remarks>
    /// <param name="objPosition">Where the keyword starts in the file.</param>
    /// <param name="tokensFrom">Where the bytes the scan's last object left to read anew start.</param>
    /// <param name="memo">Receives where the data a short window holds ends; null for a rebuild's scan.</param>
    /// <param name="number">The object number the header gives.</param>
    /// <param name="generation">The generation the header gives.</param>
    /// <param name="at">Where the header starts in the file.</param>
    private HeaderWalk ReadHeaderFurtherBack(
        long objPosition, long tokensFrom, ScanMemo.Builder? memo, out int number, out int generation, out long at)
    {
        var bound = Math.Min(Math.Min(_guard.Bound(PdfLimit.Object), Array.MaxLength - ObjKeyword.Length - 1L), objPosition - tokensFrom);
        var before = (long)ScanLead;

        while (true)
        {
            before = Math.Min(bound, before * 8);
            var start = objPosition - before;
            var end = Math.Min(_source.Length, objPosition + ObjKeyword.Length + 1);
            using var window = _source.GetWindow(start, (int)(end - start));

            if (window.Length < end - start)
            {
                memo?.EndAt(start + window.Length, tokensFrom);
                number = generation = 0;
                at = -1;
                return HeaderWalk.None;
            }

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
    /// <returns>False where the memo can hold the keyword no more; true otherwise.</returns>
    private bool NoteTrailerKeyword(
        ReadOnlySpan<byte> span, long windowStart, int keyword, ref ScanState scan, ScannedTrailers trailers, ScanMemo.Builder? memo)
    {
        var end = keyword + TrailerKeyword.Length;

        if ((keyword > 0 && PdfCharacters.IsRegular(span[keyword - 1])) ||
            (end < span.Length && PdfCharacters.IsRegular(span[end])) ||
            IsHidden(span, windowStart, windowStart + keyword, ref scan))
        {
            return true;
        }

        trailers.Keywords.Add(windowStart + end);
        return memo?.TryAddTrailer(windowStart + keyword, scan.TokensFrom) ?? true;
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
    /// name <c>/xref</c> and the word <c>xrefs</c> are none: the search near an offset tries no place the memo does not
    /// hold (<see cref="CheckPlace"/>).
    /// </para>
    /// <para>
    /// Whether a comment or a string holds it is swept apart from the sweep the headers and the <c>trailer</c> keywords are
    /// told by (<see cref="IsHiddenAhead"/>): a header whose comment between its tokens holds the keyword is told as it was
    /// before the scan looked for it.
    /// </para>
    /// <para>
    /// Kept out of the scan's loop, which only a scan for a memo calls it from, for the reason the search for these keywords
    /// keeps where it stands in the scan's state (<see cref="ScanState.PieceXRefAt"/>).
    /// </para>
    /// </remarks>
    /// <returns>False where the memo can hold the keyword no more; true otherwise.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool NoteXRefKeyword(ReadOnlySpan<byte> span, long windowStart, int keyword, ref ScanState scan, ScanMemo.Builder memo)
    {
        var end = keyword + XRefKeyword.Length;

        // A keyword at the window's first byte starts the data: elsewhere the window holds the bytes before it, read from
        // before where the search resumes.
        if ((keyword > 0 && !PdfCharacters.EndsToken(span[keyword - 1]) && !EndsEndOfFileMarker(span, keyword - 1)) ||
            (end < span.Length && PdfCharacters.IsRegular(span[end])))
        {
            return true;
        }

        var position = windowStart + keyword;

        if (position >= scan.RawUntil && IsHiddenAhead(span, windowStart, position, ref scan))
        {
            return true;
        }

        return memo.TryAddXRefKeyword(position, KindAt(span, windowStart, position, ref scan, ahead: true), scan.TokensFrom);
    }

    /// <summary>
    /// Determines whether <paramref name="position"/> lies in a comment or a string between objects, as a load lexes its
    /// line (<see cref="LineState"/>), the scan sweeping the bytes between where it last stood and there. Within the data
    /// of a stream whose length the scan could not confirm, up to its first <c>endstream</c>
    /// (<see cref="ScanState.RawUntil"/>), nothing is, whatever that length says: a direct one, or an indirect one whose
    /// object a scan for a memo reads in the file's bytes, which it looks for here first
    /// (<see cref="EndDataAtLengthObject"/>), changes only how the memo holds what the scan takes
    /// (<see cref="ScanState.KindUntil"/>).
    /// </summary>
    private bool IsHidden(ReadOnlySpan<byte> span, long windowStart, long position, ref ScanState scan)
    {
        if (scan.LengthObject >= 0 || scan.PendingFrom >= 0)
        {
            EndDataAtLengthObject(span, windowStart, position, ref scan);
        }

        if (position < scan.RawUntil)
        {
            return false;
        }

        Sweep(span, windowStart, position, scan.RawUntil, ref scan.SweptTo, ref scan.Line, ref scan.StringDepth, scan.Memo, scan.TokensFrom);
        return scan.Line != LineState.Tokens;
    }

    /// <summary>
    /// Learns, for the memo's kinds alone, where a load ends the data of a stream whose indirect <c>/Length</c> names an
    /// object the file's bytes give (<see cref="TryReadLengthObject"/>), at the header or <c>trailer</c> keyword at
    /// <paramref name="position"/>, where they show that no <c>endstream</c> follows the length it holds
    /// (<see cref="EndDataWithLength"/>). At the first header or keyword past the data's start of the stream the scan last
    /// delimited (<see cref="ScanState.LengthObject"/>), within that data, the object is one of the last values the scan
    /// passed (<see cref="ScanState.Values"/>), the newest first, or else the header there. Finding neither there, the scan
    /// awaits it (<see cref="ScanState.PendingFrom"/>) at each header and keyword it meets after — the one where a stream
    /// after it reads its own length too —, past where the memo counts places as taken in data
    /// (<see cref="ScanState.KindUntil"/>), or in what it reads as that stream's data before the data of the first stream
    /// it meets there (<see cref="ScanState.PendingUntil"/>), and takes the first that gives it; a stream awaited in its
    /// turn — one whose data starts in the data of the stream awaited, in its place —, the data of an attached file met
    /// there (<see cref="ConfirmStream"/>), or the end of the file, ends the wait without it (<see cref="EndAwaitedData"/>).
    /// The data of an attached file (<see cref="StartsAttachedFile"/>), whose objects are its own, gives no length's object
    /// from inside it: its own is one of the last values, or one the scan awaits, either taken only where the bytes where
    /// its length ends the data show where the stream ends (<see cref="EndAttachedData"/>).
    /// </summary>
    /// <remarks>
    /// The object is read from the file's bytes alone, never through an index, so that a scan for the memo and a
    /// rebuild's do the same, whatever was read before; and what it gives changes nothing of what either takes
    /// (<see cref="ScanState.KindUntil"/>). It is read once at each header or keyword the scan meets while it awaits
    /// one, from the window the scan holds, and one stream is awaited at a time. One the scan never meets while it
    /// awaits it — lost with the <c>endstream</c>, more than <see cref="ScanState.ValueCount"/> values before the
    /// stream, or past a stream awaited in its turn —, or that holds anything but a non-negative integer, leaves the
    /// memo's kinds to turn where a load ends the data without a length, or, for an attached file's data, as they are.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EndDataAtLengthObject(ReadOnlySpan<byte> span, long windowStart, long position, ref ScanState scan)
    {
        // Only a scan for a memo reads a length's object, or awaits one (ConfirmStream).
        long length;

        if (scan.LengthObject >= 0)
        {
            var lengthObject = scan.LengthObject;
            var attached = scan.LengthAttached;
            scan.LengthObject = -1;

            // No object was passed since the stream: its data starts where the bytes are read anew.
            if (position < scan.KindUntil)
            {
                var dataStart = scan.TokensFrom;
                var found = TryReadLastValue(span, windowStart, lengthObject, ref scan, out length);

                if (!attached && (found || TryReadLengthObject(span, windowStart, position, lengthObject, out length)))
                {
                    EndDataWithLength(span, windowStart, dataStart, length, position, position, ref scan);
                }
                else if (!found || !EndAttachedData(span, windowStart, dataStart, length, position, after: false, ref scan))
                {
                    // The place was tried: the wait starts at the next one.
                    EndAwaitedData(span, windowStart, ref scan);
                    (scan.PendingObject, scan.PendingFrom, scan.PendingUntil, scan.PendingAttached) = (lengthObject, dataStart, scan.KindUntil, attached);
                    scan.Memo!.AwaitedFrom = dataStart;
                    return;
                }
            }
        }

        if (scan.PendingFrom >= 0 && (position < scan.PendingUntil || position >= scan.KindUntil) &&
            TryReadLengthObject(span, windowStart, position, scan.PendingObject, out length))
        {
            var dataStart = scan.PendingFrom;

            if (scan.PendingAttached)
            {
                if (EndAttachedData(span, windowStart, dataStart, length, position, after: true, ref scan))
                {
                    (scan.PendingFrom, scan.PendingObject, scan.PendingAttached) = (-1, -1, false);
                    scan.Memo!.AwaitedFrom = long.MaxValue;
                }

                return;
            }

            scan.PendingFrom = scan.PendingObject = -1;
            scan.Memo!.AwaitedFrom = long.MaxValue;
            EndDataWithLength(span, windowStart, dataStart, length, position, Math.Min(position, scan.PendingUntil), ref scan);
        }
    }

    /// <summary>
    /// Ends the wait for the length's object of the stream the scan awaits it for, if any
    /// (<see cref="ScanState.PendingFrom"/>), none of the headers and keywords it met giving it: the places the memo took
    /// from where a load ends the data of a stream whose length's object it cannot read, as far as the file's bytes tell
    /// (<see cref="DataEndWithoutEndStream"/>) — or, where sooner, from the first <c>xref</c> or <c>trailer</c> keyword the
    /// scan took past the data's start (<see cref="FirstKeyword"/>) —, to where what the scan read as that data stops being
    /// that stream's (<see cref="ScanState.PendingUntil"/>) are held as taken between objects but where a comment or a
    /// string between objects holds them (<see cref="TakeCleanSwept"/>) — unless what it read so stops short of the end of
    /// the file with no <c>stream</c> keyword starting in it from that end, within <see cref="ScanChunkSize"/> bytes of
    /// there, a longer stretch not told (<see cref="StartsStreamWithin"/>): the data may hold an attached file's objects,
    /// which the memo then keeps as taken in it. The data of an attached file keeps its places so whatever the wait ends
    /// with. What the scan takes, it took as the bytes give it, whatever the wait ends with.
    /// </summary>
    /// <remarks>
    /// A load ends the data at the next object the index places, where the scan, which reads no index, ends it at the next
    /// header: a section between the two — an update's table that the damage left between the data and that header — would
    /// be held as taken in the data. The keywords are those the scan took, the memo holding the <c>xref</c> ones and the scan's own
    /// list the <c>trailer</c> ones: no byte is read for them.
    /// </remarks>
    private void EndAwaitedData(ReadOnlySpan<byte> span, long windowStart, ref ScanState scan)
    {
        var dataStart = scan.PendingFrom;
        var attached = scan.PendingAttached;
        (scan.PendingFrom, scan.PendingObject, scan.PendingAttached) = (-1, -1, false);

        if (dataStart < 0 || attached || DataEndWithoutEndStream(dataStart, long.MaxValue, ref scan) is not (var end and >= 0))
        {
            return;
        }

        // Only a scan for a memo awaits a length's object.
        var from = FirstKeyword(scan.Memo!, scan.Trailers.Keywords, dataStart, Math.Min(end, scan.PendingUntil));

        if (from < scan.PendingUntil &&
            (scan.PendingUntil >= _source.Length || (end < scan.PendingUntil && StartsStreamWithin(span, windowStart, end, scan.PendingUntil))))
        {
            TakeCleanSwept(span, windowStart, from, scan.PendingUntil, scan.Memo!, scan.TokensFrom);
        }
    }

    /// <summary>
    /// Gives where the first <c>xref</c> or <c>trailer</c> keyword the scan took at <paramref name="from"/> or past it and
    /// before <paramref name="to"/> starts, as <paramref name="memo"/> and <paramref name="trailers"/> hold them;
    /// <paramref name="to"/> where none does.
    /// </summary>
    /// <param name="memo">Holds the <c>xref</c> keywords.</param>
    /// <param name="trailers">Where the dictionary after each <c>trailer</c> keyword starts, right past it, in the order of the
    /// positions, as the scan meets them.</param>
    /// <param name="from">Where the stretch searched starts.</param>
    /// <param name="to">Where it ends.</param>
    private static long FirstKeyword(ScanMemo.Builder memo, List<long> trailers, long from, long to)
    {
        var first = memo.FirstXRefKeyword(from, to);
        var index = trailers.BinarySearch(from + TrailerKeyword.Length);
        index = index < 0 ? ~index : index;
        return index < trailers.Count ? Math.Min(first, trailers[index] - TrailerKeyword.Length) : first;
    }

    /// <summary>
    /// Determines whether the stream data that starts at <paramref name="dataStart"/> starts with a PDF or an FDF file's
    /// header, <c>%PDF-</c> or <c>%FDF-</c>, after the white space the first <see cref="PdfObjectParser.EndObjLookahead"/>
    /// bytes hold — from the window the scan holds where it holds them —: the data of an attached file, which holds objects
    /// of its own.
    /// </summary>
    /// <remarks>
    /// Those first bytes alone are read, as at a length's object, and not counted with the reads made for the memo's kinds:
    /// one read for each stream the scan does not confirm. A header after longer white space is not told, and the data is
    /// read as another stream's: only how the memo counts its places hangs on it.
    /// </remarks>
    private bool StartsAttachedFile(ReadOnlySpan<byte> span, long windowStart, long dataStart)
    {
        Span<byte> bytes = stackalloc byte[PdfObjectParser.EndObjLookahead];
        var wanted = (int)Math.Min(bytes.Length, _source.Length - dataStart);
        ReadOnlySpan<byte> held = dataStart >= windowStart && dataStart - windowStart + wanted <= span.Length
            ? span.Slice((int)(dataStart - windowStart), wanted)
            : bytes[.._source.Read(dataStart, bytes[..wanted])];
        var start = 0;

        while (start < held.Length && PdfCharacters.IsWhitespace(held[start]))
        {
            start++;
        }

        held = held[start..];
        return held.StartsWith("%PDF-"u8) || held.StartsWith("%FDF-"u8);
    }

    /// <summary>
    /// Holds as taken between objects what the scan takes past where a load with <paramref name="length"/> ends the data
    /// that starts at <paramref name="dataStart"/>, where the bytes show that no <c>endstream</c> follows that length, a
    /// length past the end of the file too (<see cref="DataEndWithoutEndStream"/>): the places the memo took from that end
    /// to <paramref name="revisedTo"/> (<see cref="TakeCleanSwept"/>), and, where the scan still reads that data at
    /// <paramref name="position"/>, those it takes from that end on, up to where it next reads data as the bytes give them
    /// (<see cref="ScanState.KindUntil"/>, which that end lowers and never raises) — but, either way, a place a comment or a
    /// string between objects holds there, swept from that end as a scan between objects sweeps the bytes, which stays
    /// held as taken in the data (<see cref="KindAt"/>). The scan itself reads on as
    /// before, the data as the bytes give it up to its first <c>endstream</c>: read from the file's bytes, the length may
    /// be no length of the stream's — the last values the scan passed may hold an older revision's object of its number,
    /// or an attached file's, and the data a header of that number, an attached file's —, and a scan that ended the data
    /// there would read the rest of it between objects, where a comment or a string in it would hide the header of the
    /// object after it. An <c>endstream</c> after the length, first bytes past it that do not tell — white space or
    /// comments that run past them among them (<see cref="SignificantPastLength"/>) —, or the reads made for the memo's
    /// kinds at their bound (<see cref="MemoKindReadPasses"/>), leaves the kinds as they are; and so does, where the scan
    /// still reads that data at <paramref name="position"/>, a first <c>endstream</c> of the data that ends the stream as far as the bytes tell — an
    /// <c>endobj</c> follows it (<see cref="IsEndObjAt"/>), and no <c>stream</c> keyword starts between
    /// <paramref name="position"/> and it, within <see cref="ScanChunkSize"/> bytes of there, a longer stretch not told
    /// (<see cref="StartsStreamWithin"/>) —: the data may hold an attached file's objects, which the memo then
    /// holds as taken in it.
    /// </summary>
    /// <remarks>
    /// A rebuild's scan, which keeps no kind, reads no length's object for them, and never comes here
    /// (<see cref="ConfirmStream"/>).
    /// </remarks>
    private void EndDataWithLength(ReadOnlySpan<byte> span, long windowStart, long dataStart, long length, long position, long revisedTo, ref ScanState scan)
    {
        var declaredEnd = DeclaredEnd(dataStart, length);
        var next = SignificantPastLength(span, windowStart, declaredEnd);

        if (next < 0 ||
            DataEndWithoutEndStream(dataStart, declaredEnd, ref scan) is not (var end and >= 0) ||
            (position < scan.KindUntil && scan.KindUntil < _source.Length && !StartsStreamWithin(span, windowStart, position, scan.KindUntil) &&
             IsEndObjAt(scan.KindUntil + StreamEnds.EndStreamKeyword.Length, ref _scanKindBytes, MemoKindReadBound)))
        {
            return;
        }

        TakeCleanSwept(span, windowStart, end, revisedTo, scan.Memo!, scan.TokensFrom);

        // As where the scan passes a stream: that end lies past the data's first endstream only where a source reads short.
        if (position < scan.KindUntil)
        {
            LowerKindUntil(end, ref scan);
        }
    }

    /// <summary>
    /// Learns, for the memo's kinds alone, where the data of an attached file that starts at <paramref name="dataStart"/>
    /// ends (<see cref="StartsAttachedFile"/>), from <paramref name="length"/>, read in the file's bytes at the object its
    /// indirect <c>/Length</c> names, only where the bytes where that length ends the data end the stream: an
    /// <c>endstream</c>, the data intact, every place the memo took within it held as taken there — those past its first
    /// <c>endstream</c>, which the scan's own sweep told, too (<see cref="ScanMemo.Builder.TakeRaw"/>) —, and those the scan
    /// takes in it after held so (<see cref="ScanState.KindUntil"/>, raised to there); or the end of the stream's object,
    /// the <c>endstream</c> lost (<see cref="EndsObjectAt"/>), the places past it held as taken between objects, as past
    /// where a load ends a stream's data (<see cref="EndDataWithLength"/>). A length's object met past the data's start (<paramref name="after"/>), which the
    /// scan awaited, is taken only where it lies past where its length ends the data: one inside it is the attached file's.
    /// False where the length is not taken.
    /// </summary>
    /// <remarks>
    /// The bytes where the length ends the data are read once, a first read as past a length (<see cref="SignificantPastLength"/>),
    /// and an <c>endobj</c> behind white space longer than it read on with the reads made for the memo's kinds
    /// (<see cref="IsEndObjAt"/>).
    /// </remarks>
    private bool EndAttachedData(ReadOnlySpan<byte> span, long windowStart, long dataStart, long length, long position, bool after, ref ScanState scan)
    {
        var declaredEnd = DeclaredEnd(dataStart, length);

        if (declaredEnd >= _source.Length || (after && position < declaredEnd))
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[PdfObjectParser.EndObjLookahead];
        var wanted = (int)Math.Min(bytes.Length, _source.Length - declaredEnd);
        ReadOnlySpan<byte> held = declaredEnd >= windowStart && declaredEnd - windowStart + wanted <= span.Length
            ? span.Slice((int)(declaredEnd - windowStart), wanted)
            : bytes[.._source.Read(declaredEnd, bytes[..wanted])];

        if (StreamEnds.ReadEnd(held, declaredEnd + held.Length >= _source.Length).Kind == StreamEndKind.EndStream)
        {
            scan.Memo!.TakeRaw(dataStart, declaredEnd);
            scan.KindUntil = position < declaredEnd ? Math.Max(scan.KindUntil, declaredEnd) : scan.KindUntil;
            return true;
        }

        if (!EndsObjectAt(span, windowStart, declaredEnd))
        {
            return false;
        }

        TakeCleanSwept(span, windowStart, declaredEnd, after ? Math.Min(position, scan.PendingUntil) : position, scan.Memo!, scan.TokensFrom);

        if (position < scan.KindUntil)
        {
            LowerKindUntil(declaredEnd, ref scan);
        }

        return true;
    }

    /// <summary>
    /// Determines whether the bytes at <paramref name="declaredEnd"/>, where a length ends an attached file's data, end the
    /// stream's object, its <c>endstream</c> lost: an <c>endobj</c> follows (<see cref="IsEndObjAt"/>), or, the
    /// <c>endobj</c> lost too, the next object's header, after white space and comments, within the first
    /// <see cref="PdfObjectParser.EndObjLookahead"/> bytes — from the window the scan holds where it holds them —, as a load
    /// with that length ends the data where the next header follows it (<see cref="DataEndWithoutEndStream"/>).
    /// </summary>
    private bool EndsObjectAt(ReadOnlySpan<byte> span, long windowStart, long declaredEnd)
    {
        if (declaredEnd >= _source.Length)
        {
            return false;
        }

        if (IsEndObjAt(declaredEnd, ref _scanKindBytes, MemoKindReadBound))
        {
            return true;
        }

        Span<byte> bytes = stackalloc byte[PdfObjectParser.EndObjLookahead];
        var wanted = (int)Math.Min(bytes.Length, _source.Length - declaredEnd);
        ReadOnlySpan<byte> held = declaredEnd >= windowStart && declaredEnd - windowStart + wanted <= span.Length
            ? span.Slice((int)(declaredEnd - windowStart), wanted)
            : bytes[.._source.Read(declaredEnd, bytes[..wanted])];
        var lexer = new PdfLexer(held, StreamEnds.SkipGap(held, 0));
        var number = lexer.Read();
        var generation = lexer.Read();
        var keyword = lexer.Read();
        return number.Kind == PdfTokenKind.Integer && generation.Kind == PdfTokenKind.Integer && keyword.IsKeyword("obj"u8) &&
            (keyword.End < held.Length || declaredEnd + held.Length >= _source.Length);
    }

    /// <summary>
    /// Lowers where the memo stops counting places as taken in the data (<see cref="ScanState.KindUntil"/>) to
    /// <paramref name="end"/>, where a load ends a stream's data, the sweeps for the memo's kinds starting again from there.
    /// </summary>
    private static void LowerKindUntil(long end, ref ScanState scan)
    {
        if (end < scan.KindUntil)
        {
            scan.KindUntil = end;
            scan.KindSweptTo = scan.KindAheadTo = -1;
        }
    }

    /// <summary>
    /// Gives where a declared length ends a stream's data that starts at <paramref name="dataStart"/>: past the end of the
    /// file for a length that runs past it, which no <c>endstream</c> follows — a byte past it, or, for a source that says
    /// it holds as many bytes as a position can name, that last position, which no sum overflows.
    /// </summary>
    /// <param name="dataStart">Where the data starts in the file.</param>
    /// <param name="length">The declared length; a negative one gives a position before the data's start.</param>
    private long DeclaredEnd(long dataStart, long length) =>
        length <= _source.Length - dataStart ? dataStart + length : _source.Length < long.MaxValue ? _source.Length + 1 : long.MaxValue;

    /// <summary>
    /// Gives where the first byte at or past <paramref name="declaredEnd"/>, where a length the scan read from the file's
    /// bytes ends a stream's data, lies that is neither white space nor part of a comment, a comment only after white space
    /// (<see cref="StreamEnds.SkipGap"/>), where the <see cref="PdfObjectParser.EndObjLookahead"/> bytes there show that no
    /// <c>endstream</c> starts at it — from the window the scan holds where it holds them —; the length of the file where
    /// the file ends first, a declared end past it too; -1 where an <c>endstream</c> starts there, or where those bytes do
    /// not tell: white space or comments that run past them, or past a read the source gives short, a keyword they cut.
    /// </summary>
    /// <remarks>
    /// Those first bytes alone are read, and not counted with the reads made for the memo's kinds
    /// (<see cref="_scanKindBytes"/>), as at a length's object: that length may be no length of the stream's — a valid
    /// file's stream may hold, in its data, an object of its length's number, an attached file's —, and may end the data
    /// anywhere in the file, many streams' data in one run of white space, which reading on would cross once for each.
    /// </remarks>
    private long SignificantPastLength(ReadOnlySpan<byte> span, long windowStart, long declaredEnd)
    {
        if (declaredEnd >= _source.Length)
        {
            return _source.Length;
        }

        Span<byte> bytes = stackalloc byte[PdfObjectParser.EndObjLookahead];
        var wanted = (int)Math.Min(bytes.Length, _source.Length - declaredEnd);
        ReadOnlySpan<byte> held = declaredEnd >= windowStart && declaredEnd - windowStart + wanted <= span.Length
            ? span.Slice((int)(declaredEnd - windowStart), wanted)
            : bytes[.._source.Read(declaredEnd, bytes[..wanted])];
        return StreamEnds.ReadEnd(held, declaredEnd + held.Length >= _source.Length).Kind == StreamEndKind.None
            ? declaredEnd + StreamEnds.SkipGap(held, 0)
            : -1;
    }

    /// <summary>
    /// Reads the object an indirect <c>/Length</c> names (<paramref name="lengthObject"/>) at one of the last values the scan
    /// passed (<see cref="ScanState.Values"/>), the newest first, as <see cref="TryReadLengthObject"/> does.
    /// </summary>
    private bool TryReadLastValue(ReadOnlySpan<byte> span, long windowStart, long lengthObject, ref ScanState scan, out long length)
    {
        length = -1;

        for (var back = 1; back <= ScanState.ValueCount && back <= scan.ValuesPassed; back++)
        {
            if (TryReadLengthObject(span, windowStart, scan.Values[(int)((scan.ValuesPassed - back) & (ScanState.ValueCount - 1))], lengthObject, out length))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a <c>stream</c> keyword, a whole token, starts at <paramref name="from"/> or past it and before
    /// <paramref name="to"/> — the stream of another object, whose <c>endstream</c> the data's first one may be —,
    /// searching the <see cref="ScanChunkSize"/> bytes at most from the byte before <paramref name="from"/>. They are
    /// searched up to the first keyword that answers: those the scan holds (<paramref name="span"/>) as they are, then the
    /// file's, in reads that grow from <see cref="PdfObjectParser.EndObjLookahead"/> to <see cref="EndStreamSearchChunk"/>
    /// bytes, each twice the one before, counted with the reads made for the memo's kinds (<see cref="_scanKindBytes"/>).
    /// False where the bytes searched hold no keyword starting before <paramref name="to"/> — a stretch that runs past them
    /// is not told —, where the source gives a read shorter than asked, or where those reads have read as much as they
    /// may: the data may then be held as an intact stream's, an attached file's whose length's object the scan never meets
    /// among them, the places the memo took in it kept as taken there (<see cref="EndDataWithLength"/>,
    /// <see cref="EndAwaitedData"/>). Only the memo's kinds hang on the answer: what the scan takes does not.
    /// </summary>
    private bool StartsStreamWithin(ReadOnlySpan<byte> span, long windowStart, long from, long to)
    {
        var start = Math.Max(0, from - 1);
        var end = start + Math.Min(ScanChunkSize, Math.Min(_source.Length, to + "stream"u8.Length) - start);
        var held = start >= windowStart && start - windowStart < span.Length ? span[(int)(start - windowStart)..] : [];
        held = held[..(int)Math.Min(held.Length, end - start)];

        if (HoldsStreamKeyword(held, start, from, to))
        {
            return true;
        }

        var position = start + held.Length;

        for (var size = PdfObjectParser.EndObjLookahead; position < end; size = Math.Min(2 * size, EndStreamSearchChunk))
        {
            if (_scanKindBytes >= MemoKindReadBound)
            {
                return false;
            }

            // Each read repeats a keyword the bytes before it cut, with the byte before it and the one after.
            var pieceStart = Math.Max(start, position - ("stream"u8.Length + 1));
            var asked = (int)Math.Min(size, end - pieceStart);
            using var window = _source.GetWindow(pieceStart, asked);
            _scanKindBytes += window.Length;

            if (HoldsStreamKeyword(window.Memory.Span, pieceStart, from, to))
            {
                return true;
            }

            if (window.Length < asked)
            {
                return false;
            }

            position = pieceStart + asked;
        }

        return false;
    }

    /// <summary>
    /// Determines whether <paramref name="piece"/>, the file's bytes from <paramref name="pieceStart"/>, holds a
    /// <c>stream</c> keyword that starts at <paramref name="from"/> or past it and before <paramref name="to"/>, a whole
    /// token: one that ends the piece is whole only where the file ends with it, and one the piece starts with is not told.
    /// </summary>
    private bool HoldsStreamKeyword(ReadOnlySpan<byte> piece, long pieceStart, long from, long to)
    {
        var keyword = "stream"u8;

        for (var at = 0; at < piece.Length; at++)
        {
            var found = piece[at..].IndexOf(keyword);

            if (found < 0)
            {
                break;
            }

            at += found;
            var end = at + keyword.Length;

            if (pieceStart + at >= from && pieceStart + at < to && at > 0 && !PdfCharacters.IsRegular(piece[at - 1]) &&
                (end < piece.Length ? !PdfCharacters.IsRegular(piece[end]) : pieceStart + end >= _source.Length))
            {
                return true;
            }
        }

        return false;
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
    /// stopped otherwise, so that each sweep crosses a byte once at most. The scan's own sweep does not take up where the
    /// sweep ahead stopped: a scan that looks for no keyword, a rebuild's, then sweeps as it did before any scan looked for
    /// one, with no test of the sweep ahead for each header.
    /// </remarks>
    private bool IsHiddenAhead(ReadOnlySpan<byte> span, long windowStart, long position, ref ScanState scan)
    {
        if (scan.AheadTo <= scan.SweptTo)
        {
            scan.AheadTo = scan.SweptTo;
            scan.AheadLine = scan.Line;
            scan.AheadDepth = scan.StringDepth;
        }

        Sweep(span, windowStart, position, scan.RawUntil, ref scan.AheadTo, ref scan.AheadLine, ref scan.AheadDepth, scan.Memo, scan.TokensFrom);
        return scan.AheadLine != LineState.Tokens;
    }

    /// <summary>
    /// Gives how the memo holds the place the scan takes at <paramref name="at"/> (<see cref="ScannedPlaceKind"/>): as taken
    /// in the data before <see cref="ScanState.KindUntil"/>; past <see cref="ScanState.RawUntil"/>, where the scan's own
    /// sweep told it, as taken between objects; between the two — past where a load ends the data of a stream whose length
    /// the scan cannot confirm, in what the scan still reads as the bytes give them —, as taken between objects only where a
    /// sweep of its own from where that load ends the data shows no comment nor string between objects there holding it
    /// (<see cref="SweepForKind"/>), as a scan between objects would take it.
    /// </summary>
    /// <param name="span">The window the scan holds.</param>
    /// <param name="windowStart">Where the window starts in the file.</param>
    /// <param name="at">Where the place starts in the file.</param>
    /// <param name="scan">Where the scan stands.</param>
    /// <param name="ahead">Whether the place is an <c>xref</c> keyword, told by a sweep ahead, as <see cref="IsHiddenAhead"/>
    /// tells one.</param>
    private ScannedPlaceKind KindAt(ReadOnlySpan<byte> span, long windowStart, long at, ref ScanState scan, bool ahead) =>
        at < scan.KindUntil ? ScannedPlaceKind.Raw
        : at >= scan.RawUntil ? ScannedPlaceKind.Clean
        : SweepForKind(span, windowStart, at, ref scan, ahead);

    /// <summary>
    /// Tells, for <see cref="KindAt"/>, how the memo holds the place at <paramref name="at"/>, past where a load ends a stream's
    /// data (<see cref="ScanState.KindUntil"/>) and short of where the scan stops reading that data as the bytes give it: a
    /// sweep from there, as the scan's own sweep runs between objects, which passes an object whose header it holds as taken
    /// between objects as the scan's passes one, and starts again wherever <see cref="ScanState.KindUntil"/> moves; an
    /// <c>xref</c> keyword told by a sweep ahead of it, which leaves it where it stood (<see cref="IsHiddenAhead"/>). The
    /// sweeps read what <see cref="MemoKindReadPasses"/> lets them (<see cref="SweepForKinds"/>); past that, the place is held
    /// as taken in the data.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private ScannedPlaceKind SweepForKind(ReadOnlySpan<byte> span, long windowStart, long at, ref ScanState scan, bool ahead)
    {
        if (scan.KindSweptTo < scan.KindUntil)
        {
            (scan.KindSweptTo, scan.KindLine, scan.KindDepth) = (scan.KindUntil, LineState.Tokens, 0);
        }

        if (!ahead)
        {
            if (!SweepForKinds(span, windowStart, at, ref scan.KindSweptTo, ref scan.KindLine, ref scan.KindDepth, scan.Memo, scan.TokensFrom) ||
                scan.KindLine != LineState.Tokens)
            {
                return ScannedPlaceKind.Raw;
            }

            scan.KindCleanAt = at;
            return ScannedPlaceKind.Clean;
        }

        if (scan.KindAheadTo <= scan.KindSweptTo)
        {
            (scan.KindAheadTo, scan.KindAheadLine, scan.KindAheadDepth) = (scan.KindSweptTo, scan.KindLine, scan.KindDepth);
        }

        return SweepForKinds(span, windowStart, at, ref scan.KindAheadTo, ref scan.KindAheadLine, ref scan.KindAheadDepth, scan.Memo, scan.TokensFrom) &&
            scan.KindAheadLine == LineState.Tokens
            ? ScannedPlaceKind.Clean
            : ScannedPlaceKind.Raw;
    }

    /// <summary>
    /// Sweeps for the memo's kinds alone, as <see cref="Sweep"/> does, the bytes from <paramref name="sweptTo"/> to
    /// <paramref name="to"/>, each counted with the reads made for the memo's kinds (<see cref="_scanKindBytes"/>), from the
    /// window or the file alike, so that where a piece ends changes nothing of what they spend. False, nothing swept, once
    /// those reads have read as much as <see cref="MemoKindReadPasses"/> lets them: the places past there are held as taken
    /// in the data. A sweep is a bounded number of passes over the bytes it crosses: from where a load ends a stream's data
    /// to the places the scan takes before the next such end, once forward, once more where the scan learns that end past
    /// them (<see cref="TakeCleanSwept"/>), and once ahead for the <c>xref</c> keywords.
    /// </summary>
    private bool SweepForKinds(
        ReadOnlySpan<byte> span, long windowStart, long to, ref long sweptTo, ref LineState line, ref int depth, ScanMemo.Builder? memo, long floor)
    {
        if (to <= sweptTo)
        {
            return true;
        }

        if (_scanKindBytes >= MemoKindReadBound)
        {
            return false;
        }

        _scanKindBytes += to - sweptTo;
        Sweep(span, windowStart, to, 0, ref sweptTo, ref line, ref depth, memo, floor);
        return true;
    }

    /// <summary>
    /// Holds as taken between objects the places the memo took at <paramref name="from"/>, where a load ends a stream's data,
    /// or past it, and before <paramref name="to"/>, the scan learning that end only past them: those a sweep from there shows
    /// no comment nor string between objects holding (<see cref="SweepForKinds"/>), a run whose first and last places it so
    /// shows, a run across either bound keeping its kind; the others stay held as taken in the data, as do those past where
    /// the reads made for the memo's kinds reach their bound.
    /// </summary>
    /// <remarks>
    /// One sweep, forward, crosses the bytes up to the last place held once; unlike the sweep of the places the scan takes past
    /// that end (<see cref="SweepForKind"/>), it passes no object: a string of an object there that runs over its line, as a
    /// load reads it, is read as one the end of its line ends, which may hold a place after it as taken in the data.
    /// </remarks>
    /// <param name="span">The window the scan holds; empty for none.</param>
    /// <param name="windowStart">Where the window starts in the file.</param>
    /// <param name="from">Where a load ends the data.</param>
    /// <param name="to">Where the places held so end.</param>
    /// <param name="memo">Holds the places.</param>
    /// <param name="floor">Where the bytes the scan's last object left to read anew start (<see cref="Sweep"/>).</param>
    private void TakeCleanSwept(ReadOnlySpan<byte> span, long windowStart, long from, long to, ScanMemo.Builder memo, long floor)
    {
        var sweptTo = from;
        var line = LineState.Tokens;
        var depth = 0;

        // No window held: every byte is read from the file.
        windowStart = span.IsEmpty ? long.MaxValue : windowStart;

        for (var index = memo.IndexAtOrAfter(from); memo.TryGetPlaces(index, to, out var first, out var last, out var slots); index += slots)
        {
            if (first < 0)
            {
                continue;
            }

            if (!SweepForKinds(span, windowStart, first, ref sweptTo, ref line, ref depth, memo, floor) ||
                (line == LineState.Tokens && last != first && last < to && !SweepForKinds(span, windowStart, last, ref sweptTo, ref line, ref depth, memo, floor)))
            {
                return;
            }

            if (line == LineState.Tokens && last < to)
            {
                memo.ClearRaw(index);
            }
        }
    }

    /// <summary>
    /// Sweeps the bytes from where the sweep last stood, <paramref name="sweptTo"/>, to <paramref name="to"/>: where a comment
    /// or a string opens, and where it closes. Each byte is swept once: from the window where it holds them, from the file
    /// before it — a header found further back than its piece, or bytes no header nor keyword asked for while the piece
    /// before held them —, a piece at a time. The data of a stream whose length the scan could not confirm, before
    /// <paramref name="rawUntil"/>, is not swept. A window the source gives shorter than asked holds the end of the data
    /// (#52): what the sweep tells past it is not what the file holds, and a memo is whole only before there, or before
    /// <paramref name="floor"/> where sooner (<see cref="ScanMemo.Builder.EndAt"/>).
    /// </summary>
    private void Sweep(
        ReadOnlySpan<byte> span,
        long windowStart,
        long to,
        long rawUntil,
        ref long sweptTo,
        ref LineState line,
        ref int depth,
        ScanMemo.Builder? memo,
        long floor)
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
            var asked = (int)Math.Min(length + EndOfFileMarker.Length - 1L, _source.Length - from);
            using var window = _source.GetWindow(from, asked);

            if (window.Length < asked)
            {
                memo?.EndAt(from + window.Length, floor);
            }

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
    /// value is not one a load reads, the object is not delimited, and what follows its header is read as the bytes give it,
    /// the headers within what its lexing read taken undelimited (<see cref="ScanState.UndelimitedUntil"/>). A window the
    /// source gives shorter than asked holds the end of the data (#52), and ends a memo there
    /// (<see cref="ScanMemo.Builder.EndAt"/>).
    /// </remarks>
    /// <param name="at">Where the header starts in the file.</param>
    /// <param name="span">The piece the scan holds.</param>
    /// <param name="windowStart">Where the piece starts in the file.</param>
    /// <param name="endsData">Whether <paramref name="span"/> runs to the end of the file.</param>
    /// <param name="scan">Where the scan stands.</param>
    private ObjectExtent DelimitObject(long at, ReadOnlySpan<byte> span, long windowStart, bool endsData, ref ScanState scan)
    {
        var bound = (long)_guard.Bound(PdfLimit.Object);
        var buffered = at >= windowStart ? span[(int)(at - windowStart)..] : [];
        var buffersToEnd = endsData;

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
                scan.UndelimitedUntil = at + lexed;
                return default;
            }

            var asked = (int)Math.Min(Math.Min(size, bound), _source.Length - at);
            using var window = _source.GetWindow(at, asked);
            var toEnd = window.Length < asked || at + window.Length >= _source.Length;
            lexed = window.Length;

            if (window.Length < asked)
            {
                scan.Memo?.EndAt(at + window.Length, scan.TokensFrom);
            }

            bounds = DelimitWithin(window.Memory.Span, toEnd);
            size *= 8;
        }

        // What a load reads of the object, its header and value or its stream's dictionary, lies within the guard's bound.
        var extent = bounds.Kind switch
        {
            BoundsKind.Value when bounds.End <= bound => new ObjectExtent(ExtentKind.Value, at + bounds.End, -1, false),
            BoundsKind.Stream when bounds.DataStart <= bound => ConfirmStream(at, bounds, buffered, span, windowStart, ref scan),
            BoundsKind.Runaway when bounds.End <= bound => new ObjectExtent(ExtentKind.RunawayString, at + bounds.End, -1, false),
            _ => default,
        };

        if (extent.Kind is ExtentKind.Unknown or ExtentKind.RunawayString)
        {
            scan.UndelimitedUntil = at + Math.Max(bounds.End, bounds.DataStart);
        }

        if (extent.Kind == ExtentKind.Value)
        {
            scan.Values[(int)(scan.ValuesPassed++ & (ScanState.ValueCount - 1))] = at;
        }

        return extent;
    }

    /// <summary>
    /// Says where the stream whose header starts at <paramref name="at"/> ends: past the <c>endstream</c> that follows its
    /// direct <c>/Length</c>, white space and comments between, as many as there are, read as a load reads them (#174), or
    /// that follows the length its indirect one's object holds, where the file's bytes give that object
    /// (<see cref="ConfirmIndirectLength"/>, #363); or, its length not confirmed, where the memo stops counting the places
    /// the scan takes in its data as taken there (<see cref="ScanState.KindUntil"/>): where a load ends its data, as far as
    /// the file's bytes tell, where they show that no <c>endstream</c> follows the length, a length past the end of the
    /// file too (<see cref="DataEndWithoutEndStream"/>), or nowhere they tell yet — nowhere, for a rebuild's scan, which
    /// keeps no memo and makes no read for its kinds. The scan itself reads an unconfirmed stream's data as the bytes give
    /// it, up to its first <c>endstream</c>, wherever a load ends it (<see cref="ScanState.Passed"/>). A read the source
    /// gives short of the keyword leaves the stream unconfirmed (<see cref="EndStreamUndecided"/>), and so does a missing
    /// <c>/Length</c>, or a direct one that is no length; for the memo's kinds, an indirect one the scan does not confirm
    /// is read from its object, which the scan looks for from the first header or <c>trailer</c> keyword past the data's
    /// start on (<see cref="EndDataAtLengthObject"/>), where no stretch whose places the memo counts as taken in data
    /// reaches past that start, or where it lies in the data of the stream the scan awaits the length's object of. The data
    /// of an attached file (<see cref="StartsAttachedFile"/>) ends that wait, and its direct length ends it only where its
    /// object's end follows (<see cref="EndsObjectAt"/>).
    /// </summary>
    /// <remarks>
    /// The white space a first read cannot see past is read on with the scan's own reads past direct lengths, which
    /// <see cref="PdfReaderLimits.MaxRebuildReadPasses"/> bounds (<see cref="_scanGapBytes"/>), apart from the reads that
    /// confirm indirect lengths, from the loads' searches and from the reads made for the memo's kinds, and nothing is
    /// kept of it: a file of a million stream
    /// headers holds the scan to the time of a few readings of it, and no more memory than one. Once those reads have
    /// read as much as they may — crafted headers whose lengths end in long white space, or the streams an attached
    /// file's bytes spell in data the scan does not confirm —, a length a first read cannot confirm is not, and the data
    /// of every stream the scan cannot confirm from then on is read as an intact stream's (<see cref="ScanState.Passed"/>):
    /// which ones depends on the order the scan meets them in.
    /// </remarks>
    /// <param name="at">Where the header starts in the file.</param>
    /// <param name="bounds">What the lexing of the header and dictionary found.</param>
    /// <param name="buffered">The bytes from <paramref name="at"/> the scan holds already.</param>
    /// <param name="span">The piece the scan holds.</param>
    /// <param name="windowStart">Where the piece starts in the file.</param>
    /// <param name="scan">Where the scan stands.</param>
    private ObjectExtent ConfirmStream(
        long at, ObjectBounds bounds, ReadOnlySpan<byte> buffered, ReadOnlySpan<byte> span, long windowStart, ref ScanState scan)
    {
        var dataStart = at + bounds.DataStart;
        var inAwaitedData = scan.PendingFrom >= 0 && !scan.PendingAttached && dataStart < scan.PendingUntil;
        scan.PendingUntil = Math.Min(scan.PendingUntil, dataStart);

        if (bounds.Length < 0 && ConfirmIndirectLength(dataStart, bounds.LengthObject, span, windowStart, ref scan) is var confirmed and >= 0)
        {
            scan.LengthObject = -1;
            return new ObjectExtent(ExtentKind.Stream, confirmed, dataStart, bounds.CrossReferenceStream);
        }

        var dataEnd = DeclaredEnd(dataStart, bounds.Length);
        var keywordEnd = -1L;

        // What a length the scan cannot confirm says decides only the memo's kinds: a rebuild's scan, which keeps none,
        // reads neither the length's object nor where a load ends the data (#361). A stream whose data starts in the data
        // of the stream the scan awaits, but an attached file's, reads its own, in that stream's place.
        var awaitsLength = scan.Memo is not null && bounds.Length < 0 && (scan.KindUntil <= dataStart || inAwaitedData);
        scan.LengthObject = awaitsLength ? bounds.LengthObject : -1;

        if (bounds.Length >= 0 && dataEnd < _source.Length)
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
                var next = NextSignificant(dataEnd, ref _scanGapBytes, ScanReadBound, PdfObjectParser.EndObjLookahead);
                keywordEnd = next >= 0 ? EndStreamTokenEnd(next, ref _scanGapBytes) : EndStreamUndecided;
            }
        }

        if (keywordEnd >= 0)
        {
            return new ObjectExtent(ExtentKind.Stream, keywordEnd, dataStart, bounds.CrossReferenceStream);
        }

        if (scan.Memo is null)
        {
            return new ObjectExtent(ExtentKind.UnconfirmedStream, -1, dataStart, bounds.CrossReferenceStream);
        }

        // The data of an attached file holds objects of its own, which no stream the scan awaits the length's object of
        // takes for its own: it ends that wait.
        var attached = bounds.EmbeddedFile || StartsAttachedFile(span, windowStart, dataStart);
        scan.LengthAttached = attached;

        if (attached)
        {
            EndAwaitedData(span, windowStart, ref scan);
        }

        if (bounds.Length < 0 || keywordEnd == EndStreamUndecided)
        {
            return new ObjectExtent(ExtentKind.UnconfirmedStream, -1, dataStart, bounds.CrossReferenceStream);
        }

        // An attached file's data ends where its direct length does only where its object's end follows, its endstream lost.
        var loadEnd = attached
            ? EndsObjectAt(span, windowStart, dataEnd) ? dataEnd : -1
            : DataEndWithoutEndStream(dataStart, dataEnd, ref scan);
        return new ObjectExtent(ExtentKind.UnconfirmedStream, loadEnd, dataStart, bounds.CrossReferenceStream);
    }

    /// <summary>
    /// Gives where the <c>endstream</c> ends that follows the data starting at <paramref name="dataStart"/> of a stream
    /// whose indirect <c>/Length</c> names <paramref name="lengthObject"/>, read from an object of that number the
    /// file's bytes give, where an <c>endstream</c> follows the length it holds, white space and comments between, as
    /// one follows a direct length the scan confirms (<see cref="EndAfterLength"/>): the first of the last values the
    /// scan passed (<see cref="ScanState.Values"/>), the newest first, that gives such a length, or else the first
    /// header of that number from the data's first <c>endstream</c> on whose object holds a length at all (<see
    /// cref="FindLengthHeader"/>). -1 where none does, where <paramref name="lengthObject"/> is -1, and once the reads
    /// that confirm indirect lengths have read as much as <see cref="PdfReaderLimits.MaxRebuildReadPasses"/> lets them
    /// (<see cref="_scanLengthBytes"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stream so confirmed is passed as one whose direct length an <c>endstream</c> confirms: its data is the stream's, up to
    /// that <c>endstream</c>, and nothing in it is taken — an attached file's text, its own object headers and a trailer it
    /// spells in a comment are its bytes, not the file's (#363). A writer puts a stream's length object a few objects before
    /// the stream or right after it, where the search finds it.
    /// </para>
    /// <para>
    /// The object is read from the file's bytes alone, never through an index, so that a scan for the memo and a rebuild's
    /// confirm the same streams whatever was read before, and from the piece the scan holds where it holds them, which gives
    /// the same bytes. It may be another object of that number than the one a load reads — an older revision's, one an
    /// attached file spells —: its length confirms nothing unless an <c>endstream</c> follows where it ends the data, as a
    /// direct length an attached file spells confirms nothing unless one does. A length nothing confirms leaves the stream
    /// unconfirmed, its data read as before (<see cref="ScanState.Passed"/>).
    /// </para>
    /// <para>
    /// Its bounds — the last <see cref="ScanState.ValueCount"/> values, the <see
    /// cref="PdfObjectParser.EndObjLookahead"/> bytes read of an object, the <see cref="LengthHeaderSearchLength"/>
    /// bytes searched, the <see cref="LengthHeaderLookBehind"/> bytes each read repeats — are not guards (ADR 34): a
    /// valid file reaches this search only through a rebuild, which a reported guard sent it to and whose raising reads
    /// it through its index, and past a bound the scan reads the stream as before the search existed, never worse.
    /// </para>
    /// </remarks>
    private long ConfirmIndirectLength(long dataStart, long lengthObject, ReadOnlySpan<byte> span, long windowStart, ref ScanState scan)
    {
        if (lengthObject < 0 || _scanLengthBytes >= ScanReadBound)
        {
            return -1;
        }

        long confirmed;

        // Not a guard, as the remarks say: a length object more values back, or holding its integer past the bytes read
        // of it, leaves the stream read as before.
        for (var back = 1; back <= ScanState.ValueCount && back <= scan.ValuesPassed; back++)
        {
            if (TryReadLengthObject(span, windowStart, scan.Values[(int)((scan.ValuesPassed - back) & (ScanState.ValueCount - 1))], lengthObject, out var length) &&
                (confirmed = EndAfterLength(dataStart, length, span, windowStart)) >= 0)
            {
                return confirmed;
            }
        }

        var held = dataStart >= windowStart && dataStart - windowStart < span.Length ? span[(int)(dataStart - windowStart)..] : [];
        return FindLengthHeader(dataStart, FindEndStream(dataStart, held, ref scan.EndStreamAt), lengthObject, span, windowStart);
    }

    /// <summary>
    /// Searches the file from <paramref name="from"/>, the first <c>endstream</c> of the data that starts at <paramref
    /// name="dataStart"/>, for a header of <paramref name="lengthObject"/>, read back from its <c>obj</c> as the scan
    /// reads one (<see cref="ReadHeaderBackwards"/>) — the byte before its number ending a token, white space or
    /// comments between its tokens —, whose object holds a length (<see cref="TryReadLengthObject"/>), and stops at the
    /// first: gives where the <c>endstream</c> that follows that length ends (<see cref="EndAfterLength"/>), or -1
    /// where none does; a header whose object holds no length is passed. -1 too where no such header lies within <see
    /// cref="LengthHeaderSearchLength"/> bytes of <paramref name="from"/>, where a read the source gives short ends the
    /// search, or once the reads that confirm indirect lengths have read as much as they may.
    /// </summary>
    /// <remarks>
    /// The bytes are searched in reads that grow from <see cref="PdfObjectParser.EndObjLookahead"/> to
    /// <see cref="EndStreamSearchChunk"/> bytes, each twice the one before and repeating the last
    /// <see cref="LengthHeaderLookBehind"/> bytes of the one before, which a walk back from a keyword the one before cut
    /// reads; a walk that runs back past the start of a read is none. They come from the piece the scan holds where it
    /// holds a read whole, from the source otherwise: every byte searched is counted (<see cref="_scanLengthBytes"/>), so
    /// that where the piece ends changes neither what the search spends nor what it finds (#171). A header is read as the
    /// scan reads one, not looked for as the text <c>N G obj</c>: a writer that puts each token on a line of its own,
    /// <c>9</c>, <c>0</c> and <c>obj</c>, wrote 30 streams of a corpus file so, whose length objects a search for the
    /// text missed, each search reading a mebibyte past its stream, so that a rebuild reached the guard.
    /// </remarks>
    private long FindLengthHeader(long dataStart, long from, long lengthObject, ReadOnlySpan<byte> span, long windowStart)
    {
        var limit = Math.Min(_source.Length, from + LengthHeaderSearchLength);
        var position = from;
        var next = from;
        var size = PdfObjectParser.EndObjLookahead;

        while (position < limit && _scanLengthBytes < ScanReadBound)
        {
            var asked = (int)Math.Min(size, limit - position);
            size = Math.Min(2 * size, EndStreamSearchChunk);
            var held = position >= windowStart && position - windowStart + asked <= span.Length;
            using var window = held ? PdfWindow.Empty : _source.GetWindow(position, asked);
            var bytes = held ? span.Slice((int)(position - windowStart), asked) : window.Memory.Span;
            _scanLengthBytes += bytes.Length;

            // A keyword the read before held whole was tried there.
            var index = (int)(next - position);

            while (index < bytes.Length && bytes[index..].IndexOf(ObjKeyword) is var found and >= 0)
            {
                index += found;

                // The first length that header gives decides: a stream whose length a damage made wrong is searched no
                // further, which would read a mebibyte past each such stream.
                if (ReadHeaderBackwards(bytes, index, startsData: false, signsBeforeUnknown: false, out var number, out var generation, out var headerStart, out _) == HeaderWalk.Header &&
                    PackObject(number, generation) == lengthObject &&
                    TryReadLengthObject(bytes, position, position + headerStart, lengthObject, out var length))
                {
                    return EndAfterLength(dataStart, length, span, windowStart);
                }

                index++;
            }

            if (bytes.Length < asked || position + bytes.Length >= limit)
            {
                break;
            }

            next = position + bytes.Length - (ObjKeyword.Length - 1);
            position += bytes.Length - LengthHeaderLookBehind;
        }

        return -1;
    }

    /// <summary>
    /// How many bytes each read of the search for a length's object (<see cref="FindLengthHeader"/>) repeats of the one
    /// before: those a walk back from a keyword the read before cut reads, the two numbers of a header and the white
    /// space between them, as long as a header has, but for comments between its tokens. Less than the first read, so
    /// that each read moves the search on. Not a guard (<see cref="ConfirmIndirectLength"/>): a header whose walk back
    /// crosses a read by more leaves the stream read as before.
    /// </summary>
    private const int LengthHeaderLookBehind = 32;

    /// <summary>
    /// How many bytes past a stream's data's first <c>endstream</c> the search for its length's object reads (<see
    /// cref="FindLengthHeader"/>): a piece of the scan, as far as writers put a length object after its stream. Not a
    /// guard (<see cref="ConfirmIndirectLength"/>): a length object farther leaves the stream read as before.
    /// </summary>
    private const int LengthHeaderSearchLength = ScanChunkSize;

    /// <summary>
    /// Gives where the <c>endstream</c> ends that follows <paramref name="length"/> bytes of data from
    /// <paramref name="dataStart"/>, white space and comments between, as many as there are (#174) — the first bytes from
    /// the piece the scan holds where it holds them, those past them read on —; -1 where none does, or where the bytes do
    /// not tell: a read the source gives short, or the reads that confirm indirect lengths at their bound, every byte read
    /// counted with them (<see cref="_scanLengthBytes"/>).
    /// </summary>
    private long EndAfterLength(long dataStart, long length, ReadOnlySpan<byte> span, long windowStart)
    {
        var dataEnd = DeclaredEnd(dataStart, length);

        if (dataEnd >= _source.Length)
        {
            return -1;
        }

        Span<byte> bytes = stackalloc byte[PdfObjectParser.EndObjLookahead];
        var wanted = (int)Math.Min(bytes.Length, _source.Length - dataEnd);
        ReadOnlySpan<byte> held = dataEnd >= windowStart && dataEnd - windowStart + wanted <= span.Length
            ? span.Slice((int)(dataEnd - windowStart), wanted)
            : bytes[.._source.Read(dataEnd, bytes[..wanted])];
        _scanLengthBytes += wanted;
        var end = StreamEnds.ReadEnd(held, dataEnd + held.Length >= _source.Length);

        if (end.Kind == StreamEndKind.EndStream)
        {
            return dataEnd + end.KeywordEnd;
        }

        var next = end.Kind == StreamEndKind.Undecided ? NextSignificant(dataEnd, ref _scanLengthBytes, ScanReadBound, PdfObjectParser.EndObjLookahead) : -1;
        return next >= 0 && EndStreamTokenEnd(next, ref _scanLengthBytes) is var keywordEnd and >= 0 ? keywordEnd : -1;
    }

    /// <summary>
    /// Reads, at <paramref name="position"/>, the object that holds an indirect <c>/Length</c> naming
    /// <paramref name="lengthObject"/> as the file's bytes give it: its header, a non-negative integer and <c>endobj</c>,
    /// white space and comments between, within <see cref="PdfObjectParser.EndObjLookahead"/> bytes — from the window the
    /// scan holds where it holds them —; false for anything else, a read the source gives short of them included.
    /// </summary>
    /// <param name="span">The window the scan holds.</param>
    /// <param name="windowStart">Where the window starts in the file.</param>
    /// <param name="position">Where the header starts in the file.</param>
    /// <param name="lengthObject">The object the <c>/Length</c> names (<see cref="PackObject"/>), or -1 for none.</param>
    /// <param name="length">The integer the object holds, where it is one.</param>
    private bool TryReadLengthObject(ReadOnlySpan<byte> span, long windowStart, long position, long lengthObject, out long length)
    {
        length = -1;

        if (lengthObject < 0 || position < 0 || position >= _source.Length)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[PdfObjectParser.EndObjLookahead];
        var wanted = (int)Math.Min(bytes.Length, _source.Length - position);
        ReadOnlySpan<byte> held = position >= windowStart && position - windowStart + wanted <= span.Length
            ? span.Slice((int)(position - windowStart), wanted)
            : bytes[.._source.Read(position, bytes[..wanted])];
        var lexer = new PdfLexer(held);
        var number = lexer.Read();
        var generation = lexer.Read();

        if (number.Kind != PdfTokenKind.Integer || generation.Kind != PdfTokenKind.Integer || PackObject(number.Integer, generation.Integer) != lengthObject)
        {
            return false;
        }

        var keyword = lexer.Read();
        var value = lexer.Read();
        var close = lexer.Read();
        length = value.Integer;
        return keyword.IsKeyword("obj"u8) && value.Kind == PdfTokenKind.Integer && length >= 0 && close.IsKeyword("endobj"u8) &&
            (close.End < held.Length || position + held.Length >= _source.Length);
    }

    /// <summary>
    /// Gives an object number and generation as one value, the number times 65,536 plus the generation; or -1 for a pair no
    /// reference may name (<see cref="PdfObjectId.MaxNumber"/>, <see cref="PdfObjectId.MaxGeneration"/>). Number 0, which
    /// a reference may name, the head of the free list, packs as any other: it matches no header the scan takes, whose
    /// number is 1 at least, so that the scan never meets such a length's object.
    /// </summary>
    private static long PackObject(long number, long generation) =>
        number is >= 0 and <= PdfObjectId.MaxNumber && generation is >= 0 and <= PdfObjectId.MaxGeneration ? (number << 16) | generation : -1;

    /// <summary>
    /// Where the bytes do not tell whether an <c>endstream</c> follows a declared length (<see cref="EndStreamTokenEnd"/>,
    /// <see cref="ConfirmStream"/>): a read the source gave shorter than asked, before the keyword or within it, or the
    /// scan's reads that confirm streams' lengths at their bound, before the keyword or within the run of regular
    /// characters it starts.
    /// </summary>
    private const long EndStreamUndecided = -2;

    /// <summary>
    /// Gives where the token that starts at <paramref name="position"/> ends, the <c>endstream</c> keyword a stream's data
    /// ends at, whole as a load lexes it — "endstreamx" too —; -1 where the bytes show that none starts; or
    /// <see cref="EndStreamUndecided"/> where they do not tell (<see cref="RegularRunEnd"/>, its reads counted in
    /// <paramref name="spent"/>): a short read is not taken for a stream that lost its <c>endstream</c>, past whose data a
    /// load ends elsewhere the memo would count places as taken between objects (#52).
    /// </summary>
    private long EndStreamTokenEnd(long position, ref long spent)
    {
        if (position >= _source.Length)
        {
            return -1;
        }

        Span<byte> bytes = stackalloc byte[PdfObjectParser.EndObjLookahead];
        var read = _source.Read(position, bytes[..(int)Math.Min(bytes.Length, _source.Length - position)]);
        var end = StreamEnds.ReadEnd(bytes[..read], position + read >= _source.Length);

        // The keyword glued to a run of regular characters longer than the read ends where the run does, read on with the
        // scan's reads that confirm streams' lengths.
        return end.Kind switch
        {
            StreamEndKind.EndStream => position + end.KeywordEnd,
            StreamEndKind.Undecided when bytes[..read].StartsWith(StreamEnds.EndStreamKeyword) => RegularRunEnd(position + read, ref spent),
            StreamEndKind.Undecided => EndStreamUndecided,
            _ => -1,
        };
    }

    /// <summary>
    /// Gives where the run of regular characters at <paramref name="position"/> ends, read with the scan's reads that
    /// confirm streams' lengths, the kind <paramref name="spent"/> counts (<see cref="_scanGapBytes"/>,
    /// <see cref="_scanLengthBytes"/>);
    /// <see cref="EndStreamUndecided"/> once those have read as much as they may, or where a window the source gives empty
    /// ends the run's reading.
    /// </summary>
    private long RegularRunEnd(long position, ref long spent)
    {
        var size = PdfObjectParser.EndObjLookahead;

        while (position < _source.Length)
        {
            if (spent >= ScanReadBound)
            {
                return EndStreamUndecided;
            }

            using var window = _source.GetWindow(position, (int)Math.Min(size, _source.Length - position));
            var span = window.Memory.Span;

            if (span.IsEmpty)
            {
                return EndStreamUndecided;
            }

            spent += span.Length;

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
    /// whether it gives <c>/Type /XRef</c> or <c>/Type /EmbeddedFile</c>.
    /// </summary>
    /// <remarks>
    /// A value is read as the parser reads one: a reference is three tokens, and an integer that a second integer and
    /// <c>obj</c> follow is the next object's header, which ends this one where it starts, inside a container too (#230); a
    /// <c>/Length</c> that a generation and <c>R</c> follow is indirect. A keyword that ends an object or a stream inside a
    /// container, or a string that runs past its object's end (<see cref="IsRunaway"/>), leaves the value undelimited, and so
    /// does a comment that holds the word <c>obj</c> before the value or inside it (<see cref="SkipGap"/>). A <c>/Length</c>
    /// whose generation and <c>R</c> follow a pair no reference may name (<see cref="PackObject"/>) is direct, as a load
    /// reads it: the number, the generation and <c>R</c> skipped as tokens no key names.
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
        var lengthObject = -1L;
        var lengthToken = -1;
        var crossReference = false;
        var embedded = false;
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
                    // A reference: a /Length whose value it is, is indirect, held by the object it names. A pair no reference
                    // may name is none: a load reads the number as a direct length, and the rest as tokens it skips.
                    if (lengthToken == count - 2 && PackObject(length, new PdfLexer(bytes, lastIntegerStart).Read().Integer) is var named and >= 0)
                    {
                        lengthObject = named;
                        length = -1;
                    }

                    break;

                case PdfTokenKind.Name when inTopDictionary && expectKey:
                    key = token.Text.SequenceEqual("Length"u8) ? DictionaryKey.Length
                        : token.Text.SequenceEqual("Type"u8) ? DictionaryKey.Type
                        : DictionaryKey.Other;
                    expectKey = false;
                    break;

                case PdfTokenKind.Name when inTopDictionary:
                    crossReference |= key == DictionaryKey.Type && token.Text.SequenceEqual("XRef"u8);
                    embedded |= key == DictionaryKey.Type && token.Text.SequenceEqual("EmbeddedFile"u8);
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

        return new ObjectBounds(BoundsKind.Stream, valueEnd, lexer.Position, length, crossReference, lengthObject, embedded);
    }

    /// <summary>
    /// Delimits a value that starts with the integer <paramref name="first"/>: the integer alone, a reference, or nothing,
    /// where it starts the next object's header — or may, the end of the data reached within the tokens after it: bytes
    /// a source gave short of may hold the rest of that header, which the scan cannot take, and whose start then bounds
    /// where its memo is whole (<see cref="ScanMemo.Builder.EndAt"/>).
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

        return new ObjectBounds(BoundsKind.Value, lexer.Position >= bytes.Length ? first.Start : first.End, -1, -1, false);
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

        /// <summary>Where the search for the next <c>xref</c> keyword stands in the piece the scan reads, for a memo.</summary>
        public int PieceXRefFrom;

        /// <summary>
        /// Where the next <c>xref</c> keyword lies in the piece the scan reads: -1 when none lies past where its search stands,
        /// -2 before the first search; -1 throughout a rebuild's scan, which looks for none.
        /// </summary>
        /// <remarks>
        /// Kept here with <see cref="PieceXRefFrom"/> rather than in locals of the scan's loop: the compiler inlines less in a
        /// method the more locals it holds, and with these two, and <see cref="NoteXRefKeyword"/> inlined, it left out of the
        /// loop the lexer that reads back the line of a header whose tokens a comment parts
        /// (<see cref="HeaderNumbers.TakeLine"/>); the rebuild of a file of such lines ran about a tenth slower
        /// (<c>ObjectScanBenchmarks</c>). The method has grown since, and its optimized compiles leave that lexer out of the
        /// loop all the same, at no cost the benchmark tells (#374).
        /// </remarks>
        public int PieceXRefAt;

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
        /// Where the sweep that tells the memo's kinds past where a load ends a stream's data has reached
        /// (<see cref="KindAt"/>): from <see cref="KindUntil"/>, up to <see cref="RawUntil"/>; -1 before it starts.
        /// </summary>
        public long KindSweptTo;

        /// <summary>What the sweep for the memo's kinds stands in.</summary>
        public LineState KindLine;

        /// <summary>How many parentheses the string the sweep for the memo's kinds stands in has open.</summary>
        public int KindDepth;

        /// <summary>
        /// Where the sweep for the memo's kinds ahead, which tells the <c>xref</c> keywords, has reached: it starts where the
        /// sweep for the kinds stands, as <see cref="AheadTo"/> starts where <see cref="SweptTo"/> stands.
        /// </summary>
        public long KindAheadTo;

        /// <summary>What the sweep for the memo's kinds ahead stands in.</summary>
        public LineState KindAheadLine;

        /// <summary>How many parentheses the string the sweep for the memo's kinds ahead stands in has open.</summary>
        public int KindAheadDepth;

        /// <summary>
        /// Where the last header the sweep for the memo's kinds held as taken between objects starts, whose object, once
        /// passed, it passes as the scan's own sweep passes one; -1 for none.
        /// </summary>
        public long KindCleanAt;

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
        /// length it could not confirm, at its first <c>endstream</c>, or at the end of the file where none follows
        /// (<see cref="FindEndStream"/>), whatever that length says; or a string that runs past its object's end. Only ever
        /// moved forward.
        /// </summary>
        public long RawUntil;

        /// <summary>
        /// Where the places the memo holds stop being counted as taken as the bytes give them
        /// (<see cref="ScannedPlaceKind.Raw"/>), past which it holds them as taken between objects where no comment nor
        /// string between objects holds them (<see cref="KindAt"/>): <see cref="RawUntil"/>, but where the bytes show that no
        /// <c>endstream</c> follows a length the scan cannot confirm, where a load with that length ends the stream's data
        /// before it — a direct one (<see cref="ConfirmStream"/>), or an indirect one whose object the scan read in the
        /// file's bytes (<see cref="EndDataWithLength"/>, <see cref="EndDataAtLengthObject"/>). The scan itself takes and
        /// hides by <see cref="RawUntil"/> alone, so that such a length, which may be another object's than the stream's,
        /// changes nothing of what it takes; a rebuild's scan, which keeps no memo, does not learn where a load ends the data
        /// (#361). Past <see cref="RawUntil"/> only where the length of an attached file's data, read from its object, ends
        /// it intact past there (<see cref="EndAttachedData"/>); otherwise, where a load ends the data sets it below where
        /// <see cref="RawUntil"/> stands — a direct length's end raises it there where the scan passes the stream
        /// (<see cref="Passed"/>), never lowering it, and an indirect one's only lowers it.
        /// </summary>
        public long KindUntil;

        /// <summary>Where the last <c>endstream</c> the scan searched for lies, or -1 before any search.</summary>
        public long EndStreamAt;

        /// <summary>How many of the last values the scan passed it keeps (<see cref="Values"/>): a power of two.</summary>
        public const int ValueCount = 8;

        /// <summary>
        /// Where the headers of the last <see cref="ValueCount"/> objects the scan passed that are values, not streams, start,
        /// each at its count among the values passed, from 0, modulo <see cref="ValueCount"/> (<see cref="ValuesPassed"/>):
        /// where a stream's indirect <c>/Length</c> may name an object a writer put a few objects before it
        /// (<see cref="EndDataAtLengthObject"/>).
        /// </summary>
        public LastValues Values;

        /// <summary>How many values, not streams, the scan passed.</summary>
        public long ValuesPassed;

        /// <summary>
        /// The object that holds the indirect <c>/Length</c> of the stream whose data the scan reads as the bytes give them,
        /// where no stretch whose places the memo counts as taken in data reaches past the data's start
        /// (<see cref="KindUntil"/>, <see cref="ConfirmStream"/>), or where that data starts in the data of the stream the
        /// scan awaits, but an attached file's (<see cref="PendingFrom"/>), until the scan meets the first header or
        /// <c>trailer</c> keyword past that start (<see cref="EndDataAtLengthObject"/>); -1 otherwise.
        /// </summary>
        public long LengthObject;

        /// <summary>
        /// The object that holds the indirect <c>/Length</c> of the stream the scan awaits it for past that first header or
        /// keyword (<see cref="PendingFrom"/>); -1 for none.
        /// </summary>
        public long PendingObject;

        /// <summary>Where the data of the stream the scan awaits the length's object of starts; -1 for none.</summary>
        public long PendingFrom;

        /// <summary>
        /// Whether the stream the scan awaits the length's object of holds an attached file (<see cref="StartsAttachedFile"/>),
        /// whose places stay held as taken in it but where that object shows where its data ends (<see cref="EndAttachedData"/>).
        /// </summary>
        public bool PendingAttached;

        /// <summary>Whether the stream whose length's object <see cref="LengthObject"/> names holds an attached file.</summary>
        public bool LengthAttached;

        /// <summary>
        /// Where what the scan read as the data of the stream it awaits the length's object of stops being that stream's:
        /// the first <c>endstream</c> past the data's start, or where the data of a stream it meets there starts, or where
        /// the object before one it meets there whose string runs past its object's end ends.
        /// </summary>
        public long PendingUntil;

        /// <summary>
        /// Whether the scan read the data of a stream as an intact stream's for its reads that confirm streams' lengths at
        /// their bound (<see cref="ReachScanReadBound"/>): the guard is reported once a scan.
        /// </summary>
        public bool ReadsBounded;

        /// <summary>Receives what a scan for a memo takes; null for a rebuild's scan.</summary>
        public ScanMemo.Builder? Memo;

        /// <summary>Receives where the trailers the scan meets lie (<see cref="FirstKeyword"/>).</summary>
        public ScannedTrailers Trailers;

        public ScanState(ScanMemo.Builder? memo, ScannedTrailers trailers)
        {
            EndStreamAt = LengthObject = PendingObject = PendingFrom = KindSweptTo = KindAheadTo = KindCleanAt = -1;
            Memo = memo;
            Trailers = trailers;
        }

        /// <summary>
        /// Starts the searches of the piece that starts at <paramref name="windowStart"/> and holds <paramref name="length"/>
        /// of the <paramref name="asked"/> bytes the scan asked for: the search for <c>xref</c> keywords, for a memo, from
        /// where it stands; and the memo learns where a piece shorter than asked ends (<see cref="ScanMemo.Builder.ReadPiece"/>).
        /// </summary>
        public void StartPiece(long windowStart, int length, int asked)
        {
            PieceXRefFrom = (int)(XRefFrom - windowStart);
            PieceXRefAt = Memo is null ? -1 : -2;
            Memo?.ReadPiece(length < asked ? windowStart + length : long.MaxValue);
        }

        /// <summary>
        /// Takes in the extent of an object the scan took, and says whether the scan goes on past it: where its value or data
        /// ends, where its data starts when its length is not confirmed, the comments before it unread.
        /// </summary>
        /// <param name="extent">What the delimiting of the object found.</param>
        /// <param name="intact">Whether the data of a stream whose length is not confirmed is read as an intact stream's, up
        /// to its first <c>endstream</c>, nothing taken there, rather than as the bytes give it (#361).</param>
        /// <param name="reader">The reader, which searches the data of a stream whose length is not confirmed.</param>
        /// <param name="span">The window the scan holds.</param>
        /// <param name="windowStart">Where the window starts in the file.</param>
        public bool Passed(ObjectExtent extent, bool intact, PdfFileReader reader, ReadOnlySpan<byte> span, long windowStart)
        {
            switch (extent.Kind)
            {
                case ExtentKind.Value or ExtentKind.Stream:
                    ObjFrom = TrailerFrom = XRefFrom = SweptTo = TokensFrom = extent.End;
                    Line = LineState.Tokens;
                    return true;

                case ExtentKind.RunawayString:
                    // The objects a runaway string runs into are the file's: neither the string nor one it opens on its line
                    // hides them, and what they hold is not the data of a stream whose length's object the scan awaits.
                    PendingUntil = Math.Min(PendingUntil, TokensFrom);
                    RawUntil = Math.Max(RawUntil, extent.End);
                    KindUntil = Math.Max(KindUntil, extent.End);
                    SweptTo = Math.Max(SweptTo, RawUntil);
                    Line = LineState.Tokens;
                    return false;

                case ExtentKind.UnconfirmedStream:
                    var buffered = extent.DataStart >= windowStart && extent.DataStart - windowStart < span.Length
                        ? span[(int)(extent.DataStart - windowStart)..]
                        : [];
                    // Where a load ends the data lies before its first endstream, but where a source gives the load's search
                    // reads too short to hold that keyword whole, which the scan's piece holds (#52).
                    var end = reader.FindEndStream(extent.DataStart, buffered, ref EndStreamAt);

                    if (intact)
                    {
                        // No length's object is awaited for data the scan does not read, nor the memo's kinds turned in it. The
                        // end lies at the file's length at most, which a source may give as the most a position can name.
                        LengthObject = -1;
                        ObjFrom = TrailerFrom = XRefFrom = SweptTo = TokensFrom = reader._source.Length - end <= StreamEnds.EndStreamKeyword.Length
                            ? reader._source.Length
                            : end + StreamEnds.EndStreamKeyword.Length;
                        Line = LineState.Tokens;
                        return true;
                    }

                    RawUntil = Math.Max(RawUntil, end);
                    KindUntil = Math.Max(KindUntil, extent.End >= 0 ? Math.Min(extent.End, end) : end);
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

    /// <summary>Where the headers of the last values the scan passed start (<see cref="ScanState.Values"/>).</summary>
    [InlineArray(ScanState.ValueCount)]
    private struct LastValues
    {
        private long _first;
    }

    /// <summary>What a scan's delimiting of an object found (<see cref="DelimitObject"/>).</summary>
    /// <param name="Kind">How far the object was delimited.</param>
    /// <param name="End">Where the object's value or data ends in the file: past the value, past <c>endstream</c>, or, for a
    /// stream whose length is not confirmed, where a load ends its data as far as the file's bytes tell, a header it ends
    /// at counted in it (<see cref="DataEndWithoutEndStream"/>) — an attached file's, where its direct length does, its
    /// object's end following (<see cref="EndsObjectAt"/>) —, -1 where they do not: where the memo's places turn from taken in the data to taken
    /// between objects, the scan reading that data up to its first <c>endstream</c> whatever this says.</param>
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

        /// <summary>
        /// A stream whose length an <c>endstream</c> confirms: its direct <c>/Length</c>, or the one the object of its
        /// indirect one gives (<see cref="ConfirmIndirectLength"/>).
        /// </summary>
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
    /// <param name="LengthObject">The object an indirect <c>/Length</c> names (<see cref="PackObject"/>), or -1.</param>
    /// <param name="EmbeddedFile">Whether the dictionary gives <c>/Type /EmbeddedFile</c>: the stream holds an attached file.</param>
    private readonly record struct ObjectBounds(
        BoundsKind Kind, int End, int DataStart, long Length, bool CrossReferenceStream, long LengthObject = -1, bool EmbeddedFile = false)
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
