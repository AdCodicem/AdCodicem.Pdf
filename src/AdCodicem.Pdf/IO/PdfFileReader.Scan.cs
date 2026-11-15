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
    /// The memo depends on nothing read before it: the scan starts from the file's start, with its own budget for reads past
    /// declared lengths, and indexes nothing. Making it reads the whole file once, the first time it is asked for, a rebuild
    /// run before or not, since a rebuild makes none (<see cref="IndexScannedObjects"/>); it is kept only once that scan has
    /// run to its end, and the source failing the scan leaves none, for the next call to scan again.
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
    /// would make every rebuild pay for a use that rarely comes.
    /// </remarks>
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
    /// piece starts changes nothing of what the scan takes (#171), but for three hand-made shapes (#358) and for the reads
    /// that tell a stream from an intact one, which count only what they read past the bytes the scan holds
    /// (<see cref="StartsStreamWithin"/>): where a piece ends changes what they spend of the scan's reads past declared
    /// lengths, and, once those near their bound (<see cref="EndStreamSearchPasses"/>), which streams after them the scan
    /// confirms or ends.
    /// </para>
    /// <para>
    /// Each object the scan takes is delimited as a load reads it (<see cref="DelimitObject"/>): its value, the strings and
    /// comments it holds among it, and, for a stream whose direct <c>/Length</c> an <c>endstream</c> confirms, its data.
    /// What lies within is the object's, and the scan goes on past it: a header or a keyword there — an attached file's own
    /// objects written as they are, a string that quotes a header — is none (#171). A string that runs past its object's
    /// end — an unbalanced parenthesis left unescaped, which runs it into the next objects, or the end of a cut file —
    /// delimits nothing (<see cref="IsRunaway"/>). A stream whose length the scan does not confirm — it confirms only a
    /// direct <c>/Length</c> an <c>endstream</c> follows, as far as the bytes tell (<see cref="ConfirmStream"/>) — has its
    /// data read as the bytes give it, comments unread, the headers and keywords there taken
    /// (<see cref="ScannedPlaceKind.Raw"/>): up to its first <c>endstream</c>; or, where no <c>endstream</c> follows a
    /// direct <c>/Length</c>, or an indirect one whose object the scan reads in the file's bytes
    /// (<see cref="EndDataAtLengthObject"/>), up to where a load ends the data, the scan reading on between objects from
    /// where <see cref="DataEndWithoutEndStream"/> says, and holding as taken between objects what it took past that end
    /// before it learned it (<see cref="EndDataWithLength"/>) — or, for an indirect one whose object the scan never reads,
    /// what it took past where a load ends the data without a length (<see cref="EndAwaitedData"/>). An object the scan
    /// cannot delimit has what follows its header read as the bytes give it, and the headers within what its delimiting
    /// read are taken undelimited, so that no byte is lexed for one object after another. Between objects, a comment or a
    /// string runs to the end of its line, and a header or a keyword that starts inside one is none
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
        var scan = new ScanState(memo);
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
                else if (!memo.TryAddHeader(at, number, generation, at < scan.RawUntil ? ScannedPlaceKind.Raw : ScannedPlaceKind.Clean, scan.TokensFrom))
                {
                    return reached;
                }

                // A header within what the delimiting of an object it could not delimit read is not delimited again.
                var extent = default(ObjectExtent);

                if (at >= scan.UndelimitedUntil)
                {
                    extent = DelimitObject(at, at >= windowStart ? span[(int)(at - windowStart)..] : [], endsData, ref scan);
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
        var raw = position < scan.RawUntil;

        if (!raw && IsHiddenAhead(span, windowStart, position, ref scan))
        {
            return true;
        }

        return memo.TryAddXRefKeyword(position, raw ? ScannedPlaceKind.Raw : ScannedPlaceKind.Clean, scan.TokensFrom);
    }

    /// <summary>
    /// Determines whether <paramref name="position"/> lies in a comment or a string between objects, as a load lexes its
    /// line (<see cref="LineState"/>), the scan sweeping the bytes between where it last stood and there. Within the data
    /// of a stream whose length the scan could not confirm, nothing is: where the scan awaits the object of that stream's
    /// indirect <c>/Length</c>, the data ends first where that object says (<see cref="EndDataAtLengthObject"/>).
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
    /// Ends the data of a stream whose indirect <c>/Length</c> names an object the file's bytes give
    /// (<see cref="TryReadLengthObject"/>), at the header or <c>trailer</c> keyword at <paramref name="position"/>, where they
    /// show that no <c>endstream</c> follows the length it holds (<see cref="EndDataWithLength"/>). At the first header or
    /// keyword past the data's start of the stream the scan last delimited (<see cref="ScanState.LengthObject"/>), the
    /// object is one of the last values the scan passed (<see cref="ScanState.Values"/>), the newest first, or else the
    /// header there. Finding neither there, the scan awaits it (<see cref="ScanState.PendingFrom"/>) at each header and
    /// keyword it meets after — the one where a stream after it reads its own length too —, between objects, or in what it
    /// reads as that stream's data before the data of the first stream it meets there (<see cref="ScanState.PendingUntil"/>),
    /// and takes the first that gives it; a stream awaited in its turn, or the end of the file, ends the wait without it
    /// (<see cref="EndAwaitedData"/>). Data that starts with a PDF file's header, an attached file's, whose objects are its
    /// own, is read as intact where neither gives it, nothing awaited for it (<see cref="StartsPdfFile"/>); a stream the
    /// scan awaits already goes on trying the headers past that data's first <c>endstream</c>, and may take one of the
    /// attached file's objects there for its length's object.
    /// </summary>
    /// <remarks>
    /// The object is read from the file's bytes alone, never through an index, so that a scan for the memo and a rebuild's
    /// take the same, whatever was read before. It is read once at each header or keyword the scan meets while it awaits
    /// one, from the window the scan holds, and one stream is awaited at a time. One the scan never meets while it awaits
    /// it — lost with the <c>endstream</c>, more than <see cref="ScanState.ValueCount"/> values before the stream, or past
    /// a stream awaited in its turn —, or that holds anything but a non-negative integer, leaves the data to end where a
    /// load ends it without a length.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EndDataAtLengthObject(ReadOnlySpan<byte> span, long windowStart, long position, ref ScanState scan)
    {
        long length;

        if (scan.LengthObject >= 0)
        {
            var lengthObject = scan.LengthObject;
            scan.LengthObject = -1;

            // No object was passed since the stream: its data starts where the bytes are read anew.
            if (position < scan.RawUntil)
            {
                var inData = !TryReadLastValue(span, windowStart, lengthObject, ref scan, out length);

                if (inData && !TryReadLengthObject(span, windowStart, position, lengthObject, out length))
                {
                    if (!StartsPdfFile(span, windowStart, scan.TokensFrom))
                    {
                        EndAwaitedData(span, windowStart, ref scan);
                        (scan.PendingObject, scan.PendingFrom, scan.PendingUntil) = (lengthObject, scan.TokensFrom, scan.RawUntil);
                        scan.Memo?.AwaitedFrom = scan.TokensFrom;
                    }

                    return;
                }

                EndDataWithLength(span, windowStart, scan.TokensFrom, length, position, position, ref scan);
            }
        }

        if (scan.PendingFrom >= 0 && (position < scan.PendingUntil || position >= scan.RawUntil) &&
            TryReadLengthObject(span, windowStart, position, scan.PendingObject, out length))
        {
            var dataStart = scan.PendingFrom;
            scan.PendingFrom = scan.PendingObject = -1;
            scan.Memo?.AwaitedFrom = long.MaxValue;
            EndDataWithLength(span, windowStart, dataStart, length, position, Math.Min(position, scan.PendingUntil), ref scan);
        }
    }

    /// <summary>
    /// Ends the wait for the length's object of the stream the scan awaits it for, if any
    /// (<see cref="ScanState.PendingFrom"/>), none of the headers and keywords it met giving it: the data ends where a load
    /// ends the data of a stream whose length's object it cannot read, as far as the file's bytes tell
    /// (<see cref="DataEndWithoutEndStream"/>), and the places the memo took from there to where what the scan read as that
    /// data stops being that stream's (<see cref="ScanState.PendingUntil"/>) are held as taken between objects
    /// (<see cref="ScanMemo.Builder.TakeClean"/>) — unless what it read so stops short of the end of the file with no
    /// <c>stream</c> keyword starting in it from the data's end, within <see cref="ScanChunkSize"/> bytes of there, a
    /// longer stretch taken as holding one (<see cref="StartsStreamWithin"/>): the data may hold an attached file's
    /// objects, and the stream is read as intact.
    /// </summary>
    private void EndAwaitedData(ReadOnlySpan<byte> span, long windowStart, ref ScanState scan)
    {
        var dataStart = scan.PendingFrom;
        scan.PendingFrom = scan.PendingObject = -1;

        if (dataStart >= 0 && DataEndWithoutEndStream(dataStart, long.MaxValue, ref scan, out _) is var end and >= 0 && end < scan.PendingUntil &&
            (scan.PendingUntil >= _source.Length || StartsStreamWithin(span, windowStart, end, scan.PendingUntil)))
        {
            scan.Memo?.TakeClean(end, scan.PendingUntil);
        }
    }

    /// <summary>
    /// Determines whether the data that starts at <paramref name="dataStart"/> starts with a PDF file's header, <c>%PDF-</c>,
    /// from the window the scan holds where it holds it: the data of an attached file, which holds objects of its own.
    /// </summary>
    private bool StartsPdfFile(ReadOnlySpan<byte> span, long windowStart, long dataStart)
    {
        Span<byte> bytes = stackalloc byte[5];
        var wanted = (int)Math.Min(bytes.Length, _source.Length - dataStart);
        ReadOnlySpan<byte> held = dataStart >= windowStart && dataStart - windowStart + wanted <= span.Length
            ? span.Slice((int)(dataStart - windowStart), wanted)
            : bytes[.._source.Read(dataStart, bytes[..wanted])];
        return held.SequenceEqual("%PDF-"u8);
    }

    /// <summary>
    /// Ends the data that starts at <paramref name="dataStart"/> where a load with <paramref name="length"/> ends it, where
    /// the bytes show that no <c>endstream</c> follows that length, a length past the end of the file too: the scan reads on
    /// between objects from where the load says (<see cref="DataEndWithoutEndStream"/>), as for a direct <c>/Length</c>,
    /// where it still reads that data at <paramref name="position"/>; and the places the memo took from that end to
    /// <paramref name="revisedTo"/> are held as taken between objects (<see cref="ScanMemo.Builder.TakeClean"/>). An
    /// <c>endstream</c> after the length, first bytes past it that do not tell — white space or comments that run past
    /// them among them (<see cref="SignificantPastLength"/>) —, or the scan's reads past declared lengths at their bound,
    /// leaves the data as it is; and so does, where the scan still reads that data at <paramref name="position"/>, a first
    /// <c>endstream</c> of the data that ends the stream as far as the bytes tell — an <c>endobj</c> follows it
    /// (<see cref="IsEndObjAt"/>), and no <c>stream</c> keyword starts between <paramref name="position"/> and it, within
    /// <see cref="ScanChunkSize"/> bytes of there, a longer stretch taken as holding one (<see cref="StartsStreamWithin"/>)
    /// —, wherever the length ends the data: read from the file's bytes, it may be no length of the stream's — the last
    /// values the scan passed may hold an older revision's object of its number, or an attached file's, and the data a
    /// header of that number, an attached file's —, and the stream is read as intact.
    /// </summary>
    /// <remarks>
    /// The memo took those places as the bytes give them, in what the scan read as data: a header or a keyword that a
    /// comment or a string between objects would hide there is held, not left out, and the scan read no object anew from
    /// there. A rebuild's scan, which keeps no kind, takes the same headers either way.
    /// </remarks>
    private void EndDataWithLength(ReadOnlySpan<byte> span, long windowStart, long dataStart, long length, long position, long revisedTo, ref ScanState scan)
    {
        var declaredEnd = length <= _source.Length - dataStart ? dataStart + length : _source.Length + 1;
        var next = SignificantPastLength(span, windowStart, declaredEnd);

        if (next < 0 ||
            DataEndWithoutEndStream(dataStart, declaredEnd, ref scan, out var resume) is not (var end and >= 0) ||
            (position < scan.RawUntil && scan.RawUntil < _source.Length && !StartsStreamWithin(span, windowStart, position, scan.RawUntil) &&
             IsEndObjAt(scan.RawUntil + StreamEnds.EndStreamKeyword.Length, ref _scanGapBytes)))
        {
            return;
        }

        scan.Memo?.TakeClean(end, revisedTo);

        if (position < scan.RawUntil)
        {
            scan.RawUntil = end;
            scan.SweptTo = Math.Max(resume, scan.TokensFrom);
        }
    }

    /// <summary>
    /// Gives where the first byte at or past <paramref name="declaredEnd"/>, where a length the scan read from the file's
    /// bytes ends a stream's data, lies that is neither white space nor part of a comment, a comment only after white space
    /// (<see cref="StreamEnds.SkipGap"/>), where the <see cref="PdfObjectParser.EndObjLookahead"/> bytes there show that no
    /// <c>endstream</c> starts at it — from the window the scan holds where it holds them —; the length of the file where
    /// the file ends first, a declared end past it too; -1 where an <c>endstream</c> starts there, or where those bytes do
    /// not tell: white space or comments that run past them, or past a read the source gives short, a keyword they cut.
    /// </summary>
    /// <remarks>
    /// Those first bytes alone are read, and not counted with the scan's reads past declared lengths
    /// (<see cref="_scanGapBytes"/>), as at a length's object: that length may be no length of the stream's — a valid
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
    /// searching the <see cref="ScanChunkSize"/> bytes at most from the byte before <paramref name="from"/>: a stretch that
    /// runs past them is taken as holding one, though they hold none. They are searched up to the first keyword that
    /// answers: those the scan holds (<paramref name="span"/>) as they are, then the file's, in reads that grow from
    /// <see cref="PdfObjectParser.EndObjLookahead"/> to <see cref="EndStreamSearchChunk"/> bytes, each twice the one
    /// before, counted with the scan's reads past declared lengths (<see cref="_scanGapBytes"/>). False where the bytes
    /// searched reach <paramref name="to"/> with no keyword starting before it, where the source gives a read shorter
    /// than asked, or where those reads have read as much as they may: the stream is then read as intact.
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
            if (_scanGapBytes >= EndStreamSearchPasses * _source.Length)
            {
                return false;
            }

            // Each read repeats a keyword the bytes before it cut, with the byte before it and the one after.
            var pieceStart = Math.Max(start, position - ("stream"u8.Length + 1));
            var asked = (int)Math.Min(size, end - pieceStart);
            using var window = _source.GetWindow(pieceStart, asked);
            _scanGapBytes += window.Length;

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

        return end < to;
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
    /// <param name="buffered">The bytes from <paramref name="at"/> the scan holds already.</param>
    /// <param name="buffersToEnd">Whether <paramref name="buffered"/> runs to the end of the file.</param>
    /// <param name="scan">Where the scan stands.</param>
    private ObjectExtent DelimitObject(long at, ReadOnlySpan<byte> buffered, bool buffersToEnd, ref ScanState scan)
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
            BoundsKind.Stream when bounds.DataStart <= bound => ConfirmStream(at, bounds, buffered, ref scan),
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
    /// direct <c>/Length</c>, white space and comments between, as many as there are, read as a load reads them (#174); or,
    /// its length not confirmed, where a load ends its data, as far as the file's bytes tell, where they show that no
    /// <c>endstream</c> follows the length, a length past the end of the file too (<see cref="DataEndWithoutEndStream"/>),
    /// or nowhere they tell yet: a read the source gives short of the keyword leaves the stream unconfirmed
    /// (<see cref="EndStreamUndecided"/>), and so does a missing <c>/Length</c>, or a direct one that is no length; an
    /// indirect one is read from its object, which the scan looks for from the first header or <c>trailer</c> keyword past
    /// the data's start on (<see cref="EndDataAtLengthObject"/>), where no stretch it reads as the bytes give them reaches
    /// past that start.
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
    /// <param name="scan">Where the scan stands.</param>
    private ObjectExtent ConfirmStream(long at, ObjectBounds bounds, ReadOnlySpan<byte> buffered, ref ScanState scan)
    {
        var dataStart = at + bounds.DataStart;

        // A length that runs past the end of the file declares an end past it, which no endstream follows.
        var dataEnd = bounds.Length <= _source.Length - dataStart ? dataStart + bounds.Length : _source.Length + 1;
        var keywordEnd = -1L;
        scan.LengthObject = bounds.Length < 0 && scan.RawUntil <= dataStart ? bounds.LengthObject : -1;
        scan.PendingUntil = Math.Min(scan.PendingUntil, dataStart);

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
                var next = NextSignificant(dataEnd, ref _scanGapBytes, PdfObjectParser.EndObjLookahead);
                keywordEnd = next >= 0 ? EndStreamTokenEnd(next) : EndStreamUndecided;
            }
        }

        if (keywordEnd >= 0)
        {
            return new ObjectExtent(ExtentKind.Stream, keywordEnd, dataStart, bounds.CrossReferenceStream);
        }

        if (bounds.Length < 0 || keywordEnd == EndStreamUndecided)
        {
            return new ObjectExtent(ExtentKind.UnconfirmedStream, -1, dataStart, bounds.CrossReferenceStream);
        }

        var rawUntil = DataEndWithoutEndStream(dataStart, dataEnd, ref scan, out var resume);
        scan.SweptTo = Math.Max(scan.SweptTo, resume);
        return new ObjectExtent(ExtentKind.UnconfirmedStream, rawUntil, dataStart, bounds.CrossReferenceStream);
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
    /// scan's reads past declared lengths at their bound, before the keyword or within the run of regular characters it
    /// starts.
    /// </summary>
    private const long EndStreamUndecided = -2;

    /// <summary>
    /// Gives where the token that starts at <paramref name="position"/> ends, the <c>endstream</c> keyword a stream's data
    /// ends at, whole as a load lexes it — "endstreamx" too —; -1 where the bytes show that none starts; or
    /// <see cref="EndStreamUndecided"/> where they do not tell (<see cref="RegularRunEnd"/>): a short read is not taken
    /// for a stream that lost its <c>endstream</c>, whose data the scan would end elsewhere (#52).
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
            StreamEndKind.Undecided => EndStreamUndecided,
            _ => -1,
        };
    }

    /// <summary>
    /// Gives where the run of regular characters at <paramref name="position"/> ends, read with the scan's reads past
    /// declared lengths (<see cref="_scanGapBytes"/>); <see cref="EndStreamUndecided"/> once those have read as much as they
    /// may, or where a window the source gives empty ends the run's reading.
    /// </summary>
    private long RegularRunEnd(long position)
    {
        var size = PdfObjectParser.EndObjLookahead;

        while (position < _source.Length)
        {
            if (_scanGapBytes >= EndStreamSearchPasses * _source.Length)
            {
                return EndStreamUndecided;
            }

            using var window = _source.GetWindow(position, (int)Math.Min(size, _source.Length - position));
            var span = window.Memory.Span;

            if (span.IsEmpty)
            {
                return EndStreamUndecided;
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
        var lengthObject = -1L;
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
                    // A reference: a /Length whose value it is, is indirect, held by the object it names.
                    if (lengthToken == count - 2)
                    {
                        lengthObject = PackObject(length, new PdfLexer(bytes, lastIntegerStart).Read().Integer);
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

        return new ObjectBounds(BoundsKind.Stream, valueEnd, lexer.Position, length, crossReference, lengthObject);
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
        /// (<c>ObjectScanBenchmarks</c>).
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
        /// length it could not confirm, where a load ends it as far as the file's bytes tell, a header it ends at taken in
        /// it (<see cref="DataEndWithoutEndStream"/>), or at its first <c>endstream</c>, or a string that runs past its
        /// object's end.
        /// </summary>
        public long RawUntil;

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
        /// The object that holds the indirect <c>/Length</c> of the stream whose data the scan reads as the bytes give them, no
        /// stretch read so before it reaching past the data's start, until the scan meets the first header or <c>trailer</c>
        /// keyword past that start (<see cref="EndDataAtLengthObject"/>); -1 otherwise.
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
        /// Where what the scan read as the data of the stream it awaits the length's object of stops being that stream's:
        /// the first <c>endstream</c> past the data's start, or where the data of a stream it meets there starts, or where
        /// the object before one it meets there whose string runs past its object's end ends.
        /// </summary>
        public long PendingUntil;

        /// <summary>Receives what a scan for a memo takes; null for a rebuild's scan.</summary>
        public ScanMemo.Builder? Memo;

        public ScanState(ScanMemo.Builder? memo)
        {
            EndStreamAt = LengthObject = PendingObject = PendingFrom = -1;
            Memo = memo;
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
                    // hides them, and what they hold is not the data of a stream whose length's object the scan awaits.
                    PendingUntil = Math.Min(PendingUntil, TokensFrom);
                    RawUntil = Math.Max(RawUntil, extent.End);
                    SweptTo = Math.Max(SweptTo, RawUntil);
                    Line = LineState.Tokens;
                    return false;

                case ExtentKind.UnconfirmedStream:
                    var buffered = extent.DataStart >= windowStart && extent.DataStart - windowStart < span.Length
                        ? span[(int)(extent.DataStart - windowStart)..]
                        : [];
                    RawUntil = Math.Max(RawUntil, extent.End >= 0 ? extent.End : reader.FindEndStream(extent.DataStart, buffered, ref EndStreamAt));
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
    /// at taken in it (<see cref="DataEndWithoutEndStream"/>), -1 where they do not.</param>
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
    /// <param name="LengthObject">The object an indirect <c>/Length</c> names (<see cref="PackObject"/>), or -1.</param>
    private readonly record struct ObjectBounds(
        BoundsKind Kind, int End, int DataStart, long Length, bool CrossReferenceStream, long LengthObject = -1)
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
