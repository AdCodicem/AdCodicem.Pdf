using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO;

internal sealed partial class PdfFileReader
{
    /// <inheritdoc/>
    long IPdfStreamDataProvider.SourceLength => _source.Length;

    /// <inheritdoc/>
    PdfStreamData IPdfStreamDataProvider.Create(long absoluteOffset, int length, bool cutByGuard, bool endHidden)
    {
        // A declared length is a claim made by the file, so it is clamped to what the file can hold.
        // Without this, "/Length 2147483647" would ask for two gigabytes of memory.
        var available = (int)Math.Clamp(_source.Length - absoluteOffset, 0, int.MaxValue);
        var data = new FileStreamData(this, absoluteOffset, Math.Clamp(length, 0, available), _guard);

        if (cutByGuard)
        {
            data.MarkCutByGuard();
        }

        if (endHidden)
        {
            data.MarkEndHidden();
        }

        return data;
    }

    /// <inheritdoc/>
    bool IPdfFileEdge.TokenEndsAt(long absoluteOffset)
    {
        Span<byte> next = stackalloc byte[1];
        return absoluteOffset < 0 || absoluteOffset >= _source.Length || _source.Read(absoluteOffset, next) == 0 ||
            !PdfCharacters.IsRegular(next[0]);
    }

    /// <inheritdoc/>
    long IPdfStreamDataProvider.DataStart(long absoluteKeywordEnd)
    {
        // The end-of-line after "stream" is a carriage return and a line feed, or either alone, as the lexer skips it.
        Span<byte> endOfLine = stackalloc byte[2];
        var read = absoluteKeywordEnd < 0 || absoluteKeywordEnd >= _source.Length
            ? 0
            : _source.Read(absoluteKeywordEnd, endOfLine[..(int)Math.Min(endOfLine.Length, _source.Length - absoluteKeywordEnd)]);
        if (read > 0 && PdfCharacters.IsRegular(endOfLine[0]))
        {
            return -1;
        }

        var skipped = read > 0 && endOfLine[0] == (byte)'\r' ? 1 : 0;

        if (skipped < read && endOfLine[skipped] == (byte)'\n')
        {
            skipped++;
        }

        return absoluteKeywordEnd + skipped;
    }

    /// <inheritdoc/>
    EndObjState? IPdfStreamDataProvider.CheckEndStream(long absoluteOffset)
    {
        // A source is only asked for bytes it has, as GetWindow does: a subclass need not accept an offset
        // or a length that runs past its end.
        if (absoluteOffset < 0 || absoluteOffset >= _source.Length)
        {
            return null;
        }

        // One read decides for all but a stream whose white space runs past it: a few bytes, on the stack.
        Span<byte> tail = stackalloc byte[PdfObjectParser.EndObjLookahead];
        var read = _source.Read(absoluteOffset, tail[..(int)Math.Min(tail.Length, _source.Length - absoluteOffset)]);
        var end = StreamEnds.ReadEnd(tail[..read], absoluteOffset + read >= _source.Length);

        return end.Kind switch
        {
            StreamEndKind.None => null,
            StreamEndKind.EndStream when end.EndObj != EndObjState.Unseen => end.EndObj,
            _ => CheckEndStreamOn(absoluteOffset, end),
        };
    }

    /// <summary>
    /// Reads on, past what one read of <see cref="IPdfStreamDataProvider.CheckEndStream"/> holds, through the white space
    /// and comments that may lead to an <c>endstream</c> at <paramref name="absoluteOffset"/>, or to what follows it: once
    /// for each place, however often its stream is parsed.
    /// </summary>
    /// <param name="absoluteOffset">Where the data ends.</param>
    /// <param name="first">What the first read said: undecided, or an <c>endstream</c> after which it saw nothing.</param>
    private EndObjState? CheckEndStreamOn(long absoluteOffset, StreamEnd first)
    {
        if (_endStreamChecks.TryGetValue(absoluteOffset, out var known))
        {
            return known;
        }

        EndObjState? state;
        var keywordEnd = absoluteOffset + first.KeywordEnd;

        if (first.Kind == StreamEndKind.Undecided)
        {
            // White space and comments may run to the file's end, where no keyword lies and nothing is left to read: the
            // source is not asked for an empty read there, as EndStreamAfter does not ask it for one (#167).
            var at = NextSignificant(absoluteOffset);
            Span<byte> keyword = stackalloc byte[StreamEnds.EndStreamKeyword.Length + 1];
            var read = at < 0 || at >= _source.Length ? 0 : _source.Read(at, keyword[..(int)Math.Min(keyword.Length, _source.Length - at)]);

            if (!keyword[..read].StartsWith(StreamEnds.EndStreamKeyword))
            {
                // Once the searches have read as much as they may, a run the first read could not see past confirms nothing.
                _endStreamChecks[absoluteOffset] = null;
                return null;
            }

            // "endstreamx" is no keyword, and is what follows the data.
            if (read == keyword.Length && PdfCharacters.IsRegular(keyword[^1]))
            {
                _endStreamChecks[absoluteOffset] = EndObjState.Absent;
                return EndObjState.Absent;
            }

            keywordEnd = at + StreamEnds.EndStreamKeyword.Length;
        }

        // Two places may lead to one keyword — a cross-reference stream's data, whose carriage return is kept, and the
        // length that ends before it —: what follows the keyword is read once.
        if (!_endObjStarts.TryGetValue(keywordEnd, out var next))
        {
            next = NextSignificant(keywordEnd);

            if (next >= 0)
            {
                _endObjStarts[keywordEnd] = next;
            }
        }

        state = next < 0 ? EndObjState.Unseen : IsEndObjKeywordAt(next) ? EndObjState.Present : EndObjState.Absent;
        _endStreamChecks[absoluteOffset] = state;
        return state;
    }

    /// <summary>
    /// Determines whether an <c>endobj</c> keyword, whole, follows the declared length of a stream whose data starts at
    /// <paramref name="dataStart"/>, white space and comments between, a comment only after white space: in the bytes the
    /// parser holds when they say, in the file otherwise.
    /// </summary>
    private bool EndObjFollows(long dataStart, long declaredEnd, ReadOnlySpan<byte> buffered) =>
        (declaredEnd - dataStart <= buffered.Length
            ? StreamEnds.IsEndObjAt(buffered, (int)(declaredEnd - dataStart), reachesEndOfFile: dataStart + buffered.Length >= _source.Length)
            : null) ?? IsEndObjAt(declaredEnd, ref _endStreamSearchBytes, EndStreamSearchPasses * _source.Length);

    /// <summary>
    /// Determines whether an <c>endobj</c> keyword, whole, follows <paramref name="absoluteOffset"/> after white space and
    /// comments, a comment only after white space (<see cref="StreamEnds.SkipGap"/>): where a stream's declared length ends,
    /// the end of a stream's object that has no <c>endstream</c>; past an <c>endstream</c>, the end of the object it ends.
    /// White space that runs past a first read is read on with the reads <paramref name="spent"/> counts, as long as they
    /// are short of <paramref name="bound"/>.
    /// </summary>
    private bool IsEndObjAt(long absoluteOffset, ref long spent, long bound)
    {
        if (absoluteOffset < 0 || absoluteOffset >= _source.Length)
        {
            return false;
        }

        Span<byte> tail = stackalloc byte[PdfObjectParser.EndObjLookahead];
        var read = _source.Read(absoluteOffset, tail[..(int)Math.Min(tail.Length, _source.Length - absoluteOffset)]);

        if (StreamEnds.IsEndObjAt(tail[..read], 0, absoluteOffset + read >= _source.Length) is { } decided)
        {
            return decided;
        }

        var next = NextSignificant(absoluteOffset, ref spent, bound, PdfObjectParser.EndObjLookahead);
        return next >= 0 && IsEndObjKeywordAt(next);
    }

    /// <summary>
    /// Gives where the first byte at or after <paramref name="position"/> lies that is neither white space nor part of a
    /// comment, reading the file on as far as they run, or the length of the file when they run to its end; -1 when the
    /// searches for <c>endstream</c> have read as much as they may (<see cref="EndStreamSearchPasses"/>), which these reads
    /// count with, or the source gives less than it holds. The first read is <paramref name="firstRead"/> bytes, and each
    /// after it twice the one before, up to <see cref="EndStreamSearchChunk"/>.
    /// </summary>
    private long NextSignificant(long position, int firstRead = PdfObjectParser.EndObjLookahead) =>
        NextSignificant(position, ref _endStreamSearchBytes, EndStreamSearchPasses * _source.Length, firstRead);

    /// <summary>
    /// Gives where the first byte at or after <paramref name="position"/> lies that is neither white space nor part of a
    /// comment, as <see cref="NextSignificant(long, int)"/> does, its reads counted in <paramref name="spent"/>, which
    /// <paramref name="bound"/> bounds.
    /// </summary>
    private long NextSignificant(long position, ref long spent, long bound, int firstRead)
    {
        var size = firstRead;
        var inComment = false;

        while (position < _source.Length)
        {
            if (spent >= bound)
            {
                return -1;
            }

            using var window = _source.GetWindow(position, (int)Math.Min(size, _source.Length - position));
            var span = window.Memory.Span;
            spent += span.Length;

            if (span.IsEmpty)
            {
                return -1;
            }

            var significant = FirstSignificant(span, ref inComment);

            if (significant >= 0)
            {
                return position + significant;
            }

            position += span.Length;
            size = (int)Math.Min(2L * size, EndStreamSearchChunk);
        }

        return _source.Length;
    }

    /// <summary>
    /// Gives the index of the first byte of <paramref name="span"/> that is neither white space nor in a comment, or -1,
    /// <paramref name="inComment"/> then saying whether its last line's comment runs on past it.
    /// </summary>
    /// <remarks>
    /// A loop of its own, whose state the JIT keeps in registers: inside <see cref="NextSignificant(long, ref long, long, int)"/>,
    /// whose bound is a parameter, it held the comment's state in memory, a read and a write each byte, and the search past
    /// a comment that holds <c>endstream</c> over 4 MB of white space took about a quarter longer
    /// (<c>StreamEndSearchBenchmarks</c>).
    /// </remarks>
    private static int FirstSignificant(ReadOnlySpan<byte> span, ref bool inComment)
    {
        var comment = inComment;

        for (var i = 0; i < span.Length; i++)
        {
            var current = span[i];

            if (comment)
            {
                // A comment runs to the end of the line; both line endings occur in the wild.
                comment = current is not ((byte)'\r' or (byte)'\n');
            }
            else if (current == (byte)'%')
            {
                comment = true;
            }
            else if (!PdfCharacters.IsWhitespace(current))
            {
                return i;
            }
        }

        inComment = comment;
        return -1;
    }

    /// <summary>Determines whether <c>endobj</c>, a whole keyword, starts at <paramref name="position"/>.</summary>
    private bool IsEndObjKeywordAt(long position)
    {
        if (position >= _source.Length)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[StreamEnds.EndObjKeyword.Length + 1];
        var read = _source.Read(position, bytes[..(int)Math.Min(bytes.Length, _source.Length - position)]);
        return bytes[..read].StartsWith(StreamEnds.EndObjKeyword) &&
            (read == StreamEnds.EndObjKeyword.Length ? position + read >= _source.Length : !PdfCharacters.IsRegular(bytes[^1]));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The search stops at the nearer of two places, or at the end of the file: the next object after the start of the data
    /// that <see cref="SearchIndex"/> places — the index the chain gave, as the file wrote it, or, for a document whose chain
    /// gave none, the one rebuilt as it opened, as it stood once opened —, and the first object header the file's bytes hold
    /// after the start of the data (<see cref="HeaderText"/>). Stopping there keeps an <c>endstream</c> that belongs to
    /// a later object from ending this one; when none lies before it, the declared length is kept. The stream's own entry is
    /// no next object: an entry that missed its object may place it inside its own data.
    /// <para>
    /// Neither bound changes once the document has opened, whatever is read: the bytes do not, nor does the index as written.
    /// The index the reader reads with is left out on purpose, though a rebuild of it places objects the file wrote no entry
    /// for: it changes as objects are read — rebuilt when a broken entry is met, corrected when an object is found near its
    /// entry —, and a stream it bounded would end where it does according to what was read before it. The headers the bytes
    /// hold stand in for it: they place what only a rebuilt index would — a copy of an object the file superseded, an object
    /// its entries mark free —, and a stream that lost its <c>endstream</c> stops at the object after it rather than running
    /// on to the next one the index as written places. That index places in turn what the bytes no longer show: the objects
    /// a damaged stretch of the file erased, as a block of zeros does. What a search keeps depends on the file's bytes and on
    /// the index as written, then. It can still depend on the number a stream is first read under — its own entry, stepped
    /// over, is that number's —, and, once the document's searches have read as much as they may
    /// (<see cref="EndStreamSearchPasses"/>), on which streams were searched first: crafted files make it do so, no document
    /// of the corpus does (#138). The rebuild's scan for trailers reads no stream (#171).
    /// </para>
    /// <para>
    /// This bound is no guard (ADR 34): a valid file's <c>endstream</c> follows its declared length (ISO 32000-1,
    /// 7.3.8.1) — so do those of the 35,716 streams of the corpus whose length is confirmed, a gap of 0 to 2 bytes
    /// between them, counted over the final index of each of its 401 documents —, and only an invalid stream is searched,
    /// but for a cross-reference stream whose <c>/Length</c> the chain could not read, which is searched with neither
    /// stop the rule for headers makes nor a carriage return trimmed, as <paramref name="deferred"/>: its rows may spell
    /// a header, and end in a carriage return; and for a stream whose <c>/Length</c> a guard cut or left out of the index,
    /// which a valid file may hold, searched as an invalid one is: a header its data spells — an embedded file's does —
    /// leaves the data up to it to the guard (#224). What it reads is the stretch between the stream and the next object,
    /// once for each stream: the result is kept, and a stream parsed again — after the cache let it go, or a rebuild of
    /// the index — is not searched again. What the searches read together is bounded by
    /// <see cref="EndStreamSearchPasses"/>. Another object whose data starts at the same place — one body that many headers
    /// name — takes the result as well, which was not read for it: <paramref name="reused"/> says so, and the length it
    /// gives is then weighed as one taken without reading where the data ends (<see cref="IPdfStreamDataProvider.TakeUnread"/>).
    /// </para>
    /// </remarks>
    StreamEndSearch IPdfStreamDataProvider.FindEndStream(
        int number, long absoluteDataStart, long declaredEnd, ReadOnlySpan<byte> buffered, bool deferred, out bool reused)
    {
        var found = _endStreamSearches.TryGetValue((absoluteDataStart, deferred), out var known);
        reused = found && known.SearchedFor != number;

        if (found && known.DeclaredEnd == declaredEnd)
        {
            return known.Search;
        }

        var search = found ? known.Search : SearchEndStreamOnce(number, absoluteDataStart, buffered, deferred);

        // Whether the object's endobj follows the declared length depends on the length the stream is read with now, which a
        // rebuild can change — the /Length an object another definition replaces —: it is read again when that length does,
        // and the search, whose result the bytes and the index as written alone decide, is not.
        if (search.Length is null && search.Searched)
        {
            search = search with
            {
                EndObjAfterDeclared = declaredEnd >= absoluteDataStart && declaredEnd <= (search.NextObject ?? _source.Length) &&
                    EndObjFollows(absoluteDataStart, declaredEnd, buffered),
            };
        }

        _endStreamSearches[(absoluteDataStart, deferred)] = (search, declaredEnd, found ? known.SearchedFor : number);
        return search;
    }

    /// <summary>
    /// Searches, once for each stream, the data that starts at <paramref name="absoluteDataStart"/> for its first
    /// <c>endstream</c>, and reads what its object's <c>endobj</c> does when none is found; what follows the declared length
    /// is left to <see cref="IPdfStreamDataProvider.FindEndStream"/>.
    /// </summary>
    private StreamEndSearch SearchEndStreamOnce(int number, long absoluteDataStart, ReadOnlySpan<byte> buffered, bool deferred)
    {
        var nextObject = NextObjectStart(number, absoluteDataStart);

        // Once the searches have read as much of the file as they may, the bytes the parser holds are searched alone: they
        // cost no read.
        var readsFile = _endStreamSearchBytes < EndStreamSearchPasses * _source.Length;
        var dataEnd = SearchEndStream(
            absoluteDataStart,
            buffered,
            nextObject ?? _source.Length,
            deferred,
            readsFile,
            ref _endStreamSearchBytes,
            out var header,
            out var searched,
            out var endObjSeen,
            out var objectEnd,
            out _,
            out var endObjHeld);

        // A header the bytes hold is looked for only before the next object the index places: found, it is the nearer.
        var stop = header >= 0 ? header : nextObject;

        // Past int.MaxValue bytes, no length the reader holds a stream by reaches the endstream found: only a file of more
        // than 2 GB can put one there, and the stream is read as one whose endstream is not found. With none, what the
        // object's endobj does is read once with the search, as long as the searches may read. What follows an endstream
        // found is read from the bytes that hold it where the file's first read after the data would decide it from them,
        // the file asked otherwise.
        return dataEnd >= 0 && dataEnd - absoluteDataStart <= int.MaxValue
            ? new StreamEndSearch(
                (int)(dataEnd - absoluteDataStart),
                stop,
                endObjHeld is EndObjState.Present or EndObjState.Absent
                    ? endObjHeld
                    : ((IPdfStreamDataProvider)this).CheckEndStream(dataEnd) ?? EndObjState.Unseen)
            : new StreamEndSearch(null, stop, EndObjState.Unknown, searched)
            {
                EndObjSeen = endObjSeen,
                ObjectEnd = objectEnd,
                HeaderStopped = header >= 0,
            };
    }

    /// <summary>
    /// How many times the file's length the lengths that streams are read with, taken without reading where their data ends,
    /// may total over every parse before the reader counts them (<see cref="IPdfStreamDataProvider.TakeUnread"/>).
    /// </summary>
    /// <remarks>
    /// Not a guard (ADR 34): no valid file's lengths taken unread total past the file's length, each counted once, so what the
    /// reader decides past it changes nothing a valid file serves. It spares a valid file the count — a record for each
    /// stream —, until its streams so read were parsed four times over: a valid file's streams lie apart, and parsed once
    /// each they total less than the file, which leaves room for streams parsed again after the cache let them go.
    /// </remarks>
    internal const int UncountedUnreadPasses = 4;

    /// <summary>
    /// How many times the file's length the lengths taken unread may total, once counted, before a stream's data is no longer
    /// kept whole (<see cref="IPdfStreamDataProvider.TakeUnread"/>).
    /// </summary>
    /// <remarks>
    /// Not a guard (ADR 34): no valid file's lengths taken unread total past the file's length. Each is counted once for each
    /// object and place its data starts at, so that streams whose data share no byte total at most the file's length, under
    /// this bound, which only data that overlaps reaches: streams each holding the next one's header, every length confirmed
    /// by the one <c>endstream</c> far on or kept without a search, or one body that many headers name, each number but the
    /// first taking what its search found. A damaged file's streams keep their data as the file wrote it, a row a shift or
    /// an update left stale inside the data, or a copy of an object the file superseded, included.
    /// </remarks>
    internal const int OverlapKeptPasses = 4;

    /// <summary>
    /// How many times the file's length what the streams past <see cref="OverlapKeptPasses"/> keep, up to a place an index
    /// gives inside their data, may total before such a stream's data reads empty
    /// (<see cref="IPdfStreamDataProvider.TakeUnread"/>).
    /// </summary>
    /// <remarks>
    /// Not a guard (ADR 34): only streams past <see cref="OverlapKeptPasses"/> spend it, which no valid file has. It bounds
    /// what the place a stream's data ends at keeps: one object placed near the far <c>endstream</c>, inside every stream's
    /// data, would otherwise let each keep nearly the whole stretch. With it, the data of the streams whose lengths are taken
    /// unread total at most eight times the file once counted, each object and place its data starts at once, and four times
    /// before, whatever the order they are read in and whatever the indexes place.
    /// </remarks>
    internal const int OverlapCutPasses = 4;

    /// <summary>
    /// How many of the places the indexes give inside the data of a stream past <see cref="OverlapKeptPasses"/> are read for a
    /// header to end the data at; past them, it ends at the first (<see cref="IPdfStreamDataProvider.TakeUnread"/>).
    /// </summary>
    /// <remarks>
    /// Not a guard (ADR 34): only data past <see cref="OverlapKeptPasses"/> is ended, which no valid file's is. It bounds what
    /// choosing where such a stream ends reads — a read of <see cref="PdfObjectParser.EndObjLookahead"/> bytes and a lookup in
    /// each index for each place, once for each object and place its data starts at —, which rows of numbers no object has,
    /// placed before each header, would otherwise make as long as the rows are many.
    /// </remarks>
    internal const int PlacedHeaderReads = 64;

    /// <summary>What a stream whose length was taken unread is recorded to keep where it keeps its data whole.</summary>
    private const int KeptWhole = -1;

    /// <summary>What a stream whose length was taken unread is recorded to keep where its data reads empty, at no place.</summary>
    private const int KeptNone = -2;

    /// <summary>
    /// How many bytes the lengths taken unread parsed so far total, which <see cref="UncountedUnreadPasses"/> bounds; it
    /// stops growing once past.
    /// </summary>
    private long _unreadParsed;

    /// <summary>
    /// How many bytes the streams whose lengths were taken unread kept whole once counted, each object and place its data
    /// starts at counted once, at the longest length taken for it, which <see cref="OverlapKeptPasses"/> bounds.
    /// </summary>
    private long _unreadKept;

    /// <summary>
    /// How many bytes the streams past <see cref="OverlapKeptPasses"/> kept up to a place an index gives inside their data,
    /// which <see cref="OverlapCutPasses"/> bounds.
    /// </summary>
    private long _unreadCut;

    /// <summary>
    /// What was decided of each length taken unread once counted, by where the data starts and the number of the object read
    /// there: the longest length counted for the pair, and what the stream keeps — all of it (<see cref="KeptWhole"/>),
    /// nothing, at no place (<see cref="KeptNone"/>), or the bytes up to a place an index gives inside the data. A stream
    /// parsed again reads the same.
    /// </summary>
    /// <remarks>
    /// Null until the count starts (<see cref="UncountedUnreadPasses"/>), so that a file whose streams are not parsed over
    /// and over allocates nothing. Each pair then costs about 40 bytes — its key, its decision and the table's own slots —,
    /// once however often its stream is parsed: as many as the streams read past the start, each from the few places the
    /// indexes give it.
    /// </remarks>
    private Dictionary<(long DataStart, int Number), (int Counted, int Kept)>? _unreadTaken;

    /// <summary>
    /// Where the data starts of each stream a kept reading reported its data kept short of a length taken unread
    /// (<see cref="StreamLengthFault.KeptShort"/>), apart from <see cref="_lengthFaultsReported"/>, which holds them too: a
    /// stream reported for another fault first — its length kept without a search, before the count began — and read short
    /// once parsed again says so, once.
    /// </summary>
    private readonly HashSet<long> _keptShortReported = new(SeededComparer.Instance);

    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-1 (7.3.8, 7.5.3) has each object begin after the one before ends. A file whose streams each hold the next
    /// one's header, every declared length confirmed by the one <c>endstream</c> a megabyte on, makes each stream's data the
    /// whole stretch: nothing keeps it (<see cref="FileStreamData"/>), but from a source not in memory each read of each
    /// stream's data reads it again, the file in its square (#280). A length taken without reading where the data ends is the
    /// one way data runs over the next object: a search for <c>endstream</c> stops at it. One body that many headers name —
    /// on one comment line, each number's load reading it — does the same with a search: it is searched once, for the number
    /// read first, whose search read where the data ends; for every other number, what the search found is a length taken
    /// unread too (<see cref="IPdfStreamDataProvider.FindEndStream"/>).
    /// <para>
    /// Nothing is counted until the lengths so taken, over every parse, total <see cref="UncountedUnreadPasses"/> times
    /// the file's length: until then a valid file pays nothing. Past it, each length is counted once for each object number
    /// and place its data starts at, at the longest length taken for that pair — a rebuild may lengthen it —, into one total;
    /// no index is consulted to count, so that neither an index rebuilt after the chain was read, nor headers that all lie
    /// before the first data start, nor one body that many headers name, lets a stream escape it. Streams whose data share no
    /// byte total at most the file's length, so that only data that overlaps, or that more than one number reads, passes
    /// four times it. The number a search was made for is not counted: it read where its data ends, and a file whose streams
    /// lost their <c>endstream</c> keeps the count it has.
    /// </para>
    /// <para>
    /// While the total is at most <see cref="OverlapKeptPasses"/> times the file's length, the data is kept whole. Past it,
    /// the data ends at the first of the places the indexes give inside it (<see cref="NextPlacedStart"/>), of the first
    /// <see cref="PlacedHeaderReads"/>, at which a header starts as a load reads one (<see cref="PlacedHeaderStartsAt"/>),
    /// or at the first where none does, as long as what it keeps fits <see cref="OverlapCutPasses"/> times the file's length,
    /// which it then spends; otherwise, or where nothing is placed inside the data, the data reads empty. Either is reported
    /// as <see cref="Diagnostics.PdfDiagnosticCodes.StreamLengthInvalid"/>. So the data of the streams whose lengths are
    /// taken unread total at most eight times the file once counted, each pair once, and four times before; the streams the
    /// searches were made for add what the searches read — <see cref="EndStreamSearchPasses"/> times the file, checked
    /// before each search starts, and the stretch of the one under way when they reach it, which reads on to its stop: about
    /// once the file more —, and the bytes each load held aside. A row a shift or an update left stale in a sound stream's
    /// data ends nothing under these bounds. Past both, which streams are ended, and where, depends on the order they are
    /// read in — one read whole before the count began may be read ended after, the cache having let it go —, as which
    /// streams the searches leave unsearched does (#138): crafted files make it so, and no stream of the corpus, nor of the
    /// copies of it measured truncated, rows shifted or updates stale, is ended.
    /// </para>
    /// <para>
    /// While the chain is read, nothing is counted: the index is being made, and the streams read then are its sections'.
    /// </para>
    /// </remarks>
    int IPdfStreamDataProvider.TakeUnread(int number, long absoluteDataStart, int length, out long placed)
    {
        placed = -1;

        if (_readingChain || length <= 0)
        {
            return length;
        }

        var countedFrom = UncountedUnreadPasses * _source.Length;

        if (_unreadParsed <= countedFrom)
        {
            _unreadParsed += length;

            if (_unreadParsed <= countedFrom)
            {
                return length;
            }
        }

        var taken = _unreadTaken ??= new(SeededComparer.Instance);
        var key = (absoluteDataStart, number);

        if (!taken.TryGetValue(key, out var decided))
        {
            decided = (0, KeptWhole);
        }

        if (length > decided.Counted)
        {
            // Counted once for each pair, at the longest length taken for it: a longer one adds what it runs past the last,
            // while the data is kept whole; once ended, the data keeps what was decided.
            if (decided.Kept == KeptWhole)
            {
                var more = length - decided.Counted;

                if (_unreadKept + more <= OverlapKeptPasses * _source.Length)
                {
                    _unreadKept += more;
                }
                else
                {
                    decided.Kept = KeptPastBound(number, absoluteDataStart, length);
                }
            }

            decided.Counted = length;
            taken[key] = decided;
        }

        // A shorter length parsed later, a rebuild's, keeps what it holds up to where the data was decided to end.
        var kept = decided.Kept;

        if (kept == KeptWhole || kept >= length)
        {
            return length;
        }

        placed = kept == KeptNone ? -1 : absoluteDataStart + kept;
        return Math.Max(kept, 0);
    }

    /// <summary>
    /// Decides what the data of object <paramref name="number"/>'s stream keeps, <paramref name="length"/> bytes from
    /// <paramref name="dataStart"/>, once the lengths taken unread have kept whole as much of the file as they may: the bytes
    /// up to where <see cref="OverlapEnd"/> ends it, at a place an index gives inside it, where they fit what
    /// <see cref="OverlapCutPasses"/> leaves, which they then spend; <see cref="KeptNone"/> otherwise, or where nothing is
    /// placed inside.
    /// </summary>
    private int KeptPastBound(int number, long dataStart, int length)
    {
        var dataEnd = dataStart + length;

        if (NextPlacedStart(number, dataStart - 1) is not { } first || first >= dataEnd)
        {
            return KeptNone;
        }

        var keeps = OverlapEnd(number, first, dataEnd) - dataStart;

        if (_unreadCut + keeps > OverlapCutPasses * _source.Length)
        {
            return KeptNone;
        }

        _unreadCut += keeps;
        return (int)keeps;
    }

    /// <summary>
    /// Gives where the data of object <paramref name="number"/>'s stream ends once the lengths taken unread have kept whole as
    /// much of the file as they may: at the first place the indexes give inside the data — from <paramref name="first"/>, the
    /// first of them, to <paramref name="dataEnd"/>, <see cref="PlacedHeaderReads"/> of them read at most — at which a header
    /// starts (<see cref="PlacedHeaderStartsAt"/>); at <paramref name="first"/> where none of those does.
    /// </summary>
    private long OverlapEnd(int number, long first, long dataEnd)
    {
        var at = first;

        for (var read = 0; read < PlacedHeaderReads && at < dataEnd; read++)
        {
            if (PlacedHeaderStartsAt(at))
            {
                return at;
            }

            at = NextPlacedStart(number, at) ?? long.MaxValue;
        }

        return first;
    }

    /// <summary>
    /// Gives the first place after <paramref name="position"/> that either index gives an object at — <see cref="SearchIndex"/>,
    /// and the index the reader reads with once they differ —, object <paramref name="number"/>'s own entry in each stepped
    /// over in both; null when neither places one before the end of the file.
    /// </summary>
    /// <remarks>
    /// The index the reader reads with may place what the other does not: one rebuilt after the chain was read, whose rows
    /// placed nothing inside any stream's data. It is sorted the first time it is asked (<see cref="PdfXRefTable.FirstOffsetAfter"/>),
    /// which only streams past <see cref="OverlapKeptPasses"/> do. The object's own entries are stepped over in both: the
    /// object read, relocated from an entry that missed it, may have that entry past the start of its data, and the index the
    /// reader reads with keeps the offset of an entry it replaced among those it sorted.
    /// </remarks>
    private long? NextPlacedStart(int number, long position)
    {
        var written = SearchIndex;
        var reading = ReferenceEquals(written, _xref) ? null : _xref;
        var own = OwnOffset(written, number);
        var stale = reading is null ? own : OwnOffset(reading, number);
        var next = NextOffsetIn(written, position, own, stale);

        if (reading is not null)
        {
            next = Math.Min(next, NextOffsetIn(reading, position, own, stale));
        }

        return next < _source.Length - _headerOffset ? next + _headerOffset : null;
    }

    /// <summary>
    /// Gives the first offset after <paramref name="position"/>, a place in the file, that <paramref name="index"/> gives an
    /// entry in use written directly at, counted from the header, <paramref name="own"/> and <paramref name="stale"/> stepped
    /// over; <see cref="long.MaxValue"/> when none is.
    /// </summary>
    private long NextOffsetIn(PdfXRefTable index, long position, long own, long stale)
    {
        var next = index.FirstOffsetAfter(position - _headerOffset);

        // Offsets come back each greater than the last, so the two stepped over are passed in two steps at most.
        for (var step = 0; step < 2 && (next == own || next == stale); step++)
        {
            next = index.FirstOffsetAfter(next);
        }

        return next;
    }

    /// <summary>
    /// Determines whether an object header starts at <paramref name="position"/>, a place an index gives inside a stream's
    /// data, as a load reads one from a row: past the white space and comments there, a number, a generation and
    /// <c>obj</c> (<see cref="RowLexer"/>), unless the place cuts a number (<see cref="RowCutsNumber"/>). It reads
    /// <see cref="PdfObjectParser.EndObjLookahead"/> bytes and the byte before once: where they, whole, cannot decide, it is
    /// taken for one.
    /// </summary>
    private bool PlacedHeaderStartsAt(long position)
    {
        // The place lies inside data, past the end-of-line after a stream keyword: there is a byte before it.
        Span<byte> bytes = stackalloc byte[PdfObjectParser.EndObjLookahead + 1];
        var from = position - 1;
        var read = _source.Read(from, bytes[..(int)Math.Min(bytes.Length, _source.Length - from)]);

        if (read < 2 || RowCutsNumber(bytes[..read]))
        {
            return false;
        }

        // Undecided where the read was short — the end of the file, or a source that gives less than it holds —, it is no
        // header: its tokens run into what could not be read.
        var inComment = false;
        var skipped = FirstSignificant(bytes[1..read], ref inComment);

        if (skipped < 0)
        {
            return read == bytes.Length;
        }

        // A load lexes from the place, whatever the byte before it: the token after the white space is read as one a row
        // starts, the byte before it taken for the end of a token.
        bytes[skipped] = (byte)' ';
        var reading = new RowLexer(bytes[..read]).Read(skipped + 1);
        return reading == RowReading.Header || (reading == RowReading.Undecided && read == bytes.Length);
    }

    /// <summary>
    /// Gives where a load ends the data of the stream that starts at <paramref name="dataStart"/>, whose declared length ends
    /// at <paramref name="declaredEnd"/> and no <c>endstream</c> follows, as far as the file's bytes tell
    /// (<see cref="PdfObjectParser.FinishWithoutEndStream"/>), where the load's search finds no <c>endstream</c> before the
    /// first object header they hold after the data's start, or before their end where they hold none — where the search
    /// stops (<see cref="SearchEndStream"/>): at the declared length, where it ends where the search stops or before, and the
    /// object's <c>endobj</c> follows it or ends the data nowhere else; at the object's <c>endobj</c>, which only white space
    /// and comments part from where the search stops; and, where the declared length runs past where the search stops and
    /// no <c>endobj</c> ends the data before it, a byte past the header there, which the data may hold — the value given
    /// lies a byte past where it starts —, or at the end of the file, where no header stops the search. With no length
    /// (<see cref="long.MaxValue"/>), at that <c>endobj</c> or else where the search stops, as a load ends the data of a
    /// stream whose length it cannot read, the header there not the data's. -1 where the search finds an <c>endstream</c>,
    /// or is not made; -1 too where a window the source gives empty ends the search, which holds the end of the data there
    /// (#52), and ends a memo there (<see cref="ScanMemo.Builder.EndAt"/>).
    /// </summary>
    /// <remarks>
    /// The scan for a memo asks it of a direct <c>/Length</c>, which it reads itself, and of an indirect one whose object it
    /// reads from the file's bytes (<see cref="EndDataAtLengthObject"/>), a length past the end of the file too, or never
    /// reads (<see cref="EndAwaitedData"/>): the next object an index places, which also stops a load's search, is not looked
    /// at, for the scan reads nothing of an index. What it gives decides only where the scan's memo counts the places the
    /// scan takes as taken between objects (<see cref="ScannedPlaceKind.Clean"/>) rather than in the data
    /// (<see cref="ScanState.KindUntil"/>), never what the scan takes: a length the scan cannot confirm may be no length of
    /// the stream's, and the scan reads the data as the bytes give it, up to its first <c>endstream</c>
    /// (<see cref="ScanState.RawUntil"/>). The data is read from the file as a load past its window reads it, with the
    /// reads made for the memo's kinds (<see cref="_scanKindBytes"/>); once those have read as much as
    /// <see cref="MemoKindReadPasses"/> lets them, no search is made. A rebuild's scan, which keeps no memo, asks nothing
    /// (<see cref="ConfirmStream"/>).
    /// </remarks>
    /// <param name="dataStart">Where the data starts in the file.</param>
    /// <param name="declaredEnd">Where the declared length ends in the file, past its end for a length that runs past it;
    /// <see cref="long.MaxValue"/> for no length.</param>
    /// <param name="scan">Where the scan stands.</param>
    private long DataEndWithoutEndStream(long dataStart, long declaredEnd, ref ScanState scan)
    {
        var found = SearchEndStream(
            dataStart,
            [],
            _source.Length,
            deferred: false,
            readsFile: _scanKindBytes < MemoKindReadBound,
            ref _scanKindBytes,
            out var header,
            out var searched,
            out _,
            out var objectEnd,
            out var emptied,
            out _);

        if (found >= 0 || !searched)
        {
            return -1;
        }

        if (emptied)
        {
            scan.Memo?.EndAt(dataStart, scan.TokensFrom);
            return -1;
        }

        var stop = header >= 0 ? header : _source.Length;

        if (declaredEnd > stop)
        {
            return objectEnd >= 0 ? objectEnd : Math.Min(declaredEnd == long.MaxValue ? stop : stop + 1, _source.Length);
        }

        var endObjFollows = IsEndObjAt(declaredEnd, ref _scanKindBytes, MemoKindReadBound);
        return endObjFollows || objectEnd < 0 ? declaredEnd : objectEnd;
    }

    /// <inheritdoc/>
    bool IPdfStreamDataProvider.IsOwnEndStream(long absoluteDataEnd, EndObjState endObj, long? nextObject)
    {
        var (position, boundary) = EndStreamFollower(absoluteDataEnd, endObj);
        return position >= 0 &&
            (boundary || IsPlacedAt(position, nextObject) || IsStreamSectionAt(position) || IsPlacedByRowNotTaken(position));
    }

    /// <summary>
    /// Determines whether a row the chain read and did not take places the object whose header starts at
    /// <paramref name="position"/> (<see cref="RowNotTakenAtOrBefore"/>), and a header does start there: such a row is an
    /// older revision's, which a rewrite of the file may have left naming bytes of no object's, and is trusted no further
    /// than the header the bytes hold (#287).
    /// </summary>
    private bool IsPlacedByRowNotTaken(long position) =>
        IsPlacedAt(position, RowNotTakenAtOrBefore(position)) && PlacedHeaderStartsAt(position);

    /// <summary>
    /// Gives the last place at or before <paramref name="position"/> a row the chain read and did not take gives an object
    /// at (<see cref="_rowsNotTaken"/>): one that names white space and comments before a header proves that header as one
    /// that names it does, and only the nearest can name the run the header ends. Null when none does.
    /// </summary>
    /// <remarks>
    /// The rows held since a proof last asked are sorted in first, but while a place a search near an offset tries is read,
    /// whose rows wait until it is kept or dropped.
    /// </remarks>
    private long? RowNotTakenAtOrBefore(long position)
    {
        if (_rowsNotTakenMark < 0 && _rowsNotTakenHeld is { Count: > 0 } held)
        {
            foreach (var place in held)
            {
                (_rowsNotTaken ??= new()).Add(place);
            }

            held.Clear();
        }

        return _rowsNotTaken?.LastBefore(position + 1) is >= 0 and var at ? at : null;
    }

    /// <inheritdoc/>
    bool IPdfStreamDataProvider.EndsBesideWhiteSpace(long absoluteDataEnd)
    {
        // The keyword is found as the proof found it, once for each place: past the data's end, white space or comments.
        if (absoluteDataEnd <= 0 || absoluteDataEnd > _source.Length)
        {
            return false;
        }

        Span<byte> last = stackalloc byte[1];
        return SignificantAfterData(absoluteDataEnd) != absoluteDataEnd ||
            (_source.Read(absoluteDataEnd - 1, last) == 1 && PdfCharacters.IsWhitespace(last[0]));
    }

    /// <summary>
    /// Gives where the first significant byte lies past <paramref name="dataEnd"/>, where a search ended data: the
    /// <c>endstream</c> keyword it found, past the white space and comments it left out; -1 where nothing could be read.
    /// Read once for each place, counted with the searches.
    /// </summary>
    private long SignificantAfterData(long dataEnd)
    {
        if (!_endStreamKeywords.TryGetValue(dataEnd, out var keyword))
        {
            keyword = NextSignificant(dataEnd, ProofRead);
            _endStreamKeywords[dataEnd] = keyword;
        }

        return keyword;
    }

    /// <summary>
    /// Determines whether the object of a cross-reference stream the chain read starts at <paramref name="position"/>: the
    /// section places it, though no row of its own does — PDFBox writes it so (#224).
    /// </summary>
    /// <remarks>
    /// The sections are as many as <see cref="PdfReaderLimits.MaxXRefSectionCount"/> lets the chain read, and each says where
    /// its object's header lies, past the white space and comments before it: nothing is read.
    /// </remarks>
    private bool IsStreamSectionAt(long position)
    {
        foreach (var section in _structure.Sections)
        {
            if (section.Kind == XRefSectionKind.Stream && section.StreamObjectNumber > 0 && section.Offset + section.Padding == position)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads, once for each place, what follows the end of the object whose data a search ended at
    /// <paramref name="dataEnd"/>: past the <c>endstream</c> keyword, whole, and the <c>endobj</c> after it where
    /// <paramref name="endObj"/> says one follows, white space and comments between, through first reads of
    /// <see cref="ProofRead"/> bytes counted with the searches (<see cref="EndStreamSearchPasses"/>).
    /// </summary>
    private (long Position, bool Boundary) EndStreamFollower(long dataEnd, EndObjState endObj)
    {
        if (_endStreamFollowers.TryGetValue((dataEnd, endObj), out var known))
        {
            return known;
        }

        // "endstreamxref" is no keyword, and ends no data: what follows it proves nothing.
        var keyword = SignificantAfterData(dataEnd);
        var end = keyword >= 0 && keyword < _source.Length && IsKeywordAt(keyword, StreamEnds.EndStreamKeyword)
            ? keyword + StreamEnds.EndStreamKeyword.Length
            : -1;

        if (end >= 0 && endObj == EndObjState.Present)
        {
            end = _endObjStarts.TryGetValue(end, out var endObjAt) ? endObjAt : NextSignificant(end, ProofRead);
            end = end < 0 || end >= _source.Length ? -1 : end + StreamEnds.EndObjKeyword.Length;
        }

        // What follows a keyword no endobj follows the reader read on to already, through the white space between.
        var next = end < 0 ? -1 : endObj != EndObjState.Present && _endObjStarts.TryGetValue(end, out var after)
            ? after
            : NextSignificant(end, ProofRead);
        var follower = (next, next >= 0 &&
            (next == _source.Length || IsKeywordAt(next, "xref"u8) || IsKeywordAt(next, StartXRefKeyword)));
        _endStreamFollowers[(dataEnd, endObj)] = follower;
        return follower;
    }

    /// <summary>
    /// Determines whether the object an index places at <paramref name="placed"/> starts at <paramref name="position"/>:
    /// there, or past the white space and comments its row names, as the reader reads an object at a row's offset.
    /// </summary>
    /// <remarks>
    /// What lies past an offset is read once, counted with the searches, however many times its stream is parsed.
    /// </remarks>
    private bool IsPlacedAt(long position, long? placed)
    {
        if (placed is not { } at || at > position)
        {
            return false;
        }

        if (at == position)
        {
            return true;
        }

        // The first read reaches the place weighed: white space before it is read once, not doubled through.
        if (!_placedStarts.TryGetValue(at, out var start))
        {
            start = NextSignificant(at, (int)Math.Clamp(position - at + 1, ProofRead, EndStreamSearchChunk));
            _placedStarts[at] = start;
        }

        return start == position;
    }

    /// <summary>
    /// Determines whether <paramref name="keyword"/>, whole, starts at <paramref name="position"/>: the file ends after it,
    /// or no regular character follows.
    /// </summary>
    private bool IsKeywordAt(long position, ReadOnlySpan<byte> keyword)
    {
        Span<byte> bytes = stackalloc byte[StartXRefKeyword.Length + 1];
        var length = Math.Min(keyword.Length + 1, _source.Length - position);
        var read = _source.Read(position, bytes[..(int)length]);
        return bytes[..read].StartsWith(keyword) && (read == keyword.Length ? position + read >= _source.Length : !PdfCharacters.IsRegular(bytes[keyword.Length]));
    }

    /// <inheritdoc/>
    PdfObject IPdfStreamDataProvider.ResolveLength(PdfReference reference, out ObjectPresence presence)
    {
        var id = reference.Id;

        if (_readingChain)
        {
            // A /Length no section read so far places — or one a chain of references from it leads to, or one a guard cut — is
            // read once the chain is read: the data is found by its endstream meanwhile, and the length checked then (#182).
            var read = ReadThroughWithoutLoading(id);
            presence = read is null ? ObjectPresence.Deferred : ObjectPresence.Defined;

            // A chain longer than the parse resolves a /Length through gives null, as it does once the chain is read.
            return read is null or PdfReference ? PdfNull.Instance : read;
        }

        var reentries = _reentries;
        var value = GetObject(id);

        // A null a guard against re-entry gave says nothing of the object: it could not be read from here.
        presence = value is PdfNull && reentries != _reentries ? ObjectPresence.Unproduced : GetPresence(id.Number);

        // What the object holds, or the one a chain of references from it leads to, is the guard's when a guard cut it or
        // left it out of the index: a value the cut left, null or an integer it left whole, is none the file gave (#224).
        if (presence != ObjectPresence.Unindexed)
        {
            // The object is not loaded again unless its value leads further: the chain is resolved first, as the parse
            // resolves it, so that the object it leads to is read when it is asked whether a guard cut it.
            if (value is PdfReference next)
            {
                next.Resolve();
            }

            var target = value is PdfReference ? reference.Target(PdfReference.MaxChainLength).Number : id.Number;
            presence = IsCutAtLimit(target) ? ObjectPresence.Cut
                : target != id.Number && GetPresence(target) == ObjectPresence.Unindexed ? ObjectPresence.Unindexed
                : presence;
        }

        return value;
    }

    /// <inheritdoc/>
    bool IPdfStreamDataProvider.IsLengthFaultReported(long absoluteDataStart, bool keptShort) =>
        (keptShort ? _keptShortReported : _lengthFaultsReported).Contains(absoluteDataStart);

    /// <summary>
    /// Gets the index whose offsets bound the searches for <c>endstream</c>: the one the chain gave, as the file wrote
    /// it, or, for a document whose chain gave none, the one rebuilt as it opened.
    /// </summary>
    /// <remarks>
    /// Neither changes once the document has opened, whatever is read. The reader changes the chain's index — an object
    /// found near its entry, a rebuild — only after <see cref="PreserveChainIndex"/> has copied it. An index rebuilt as
    /// the document opened is the one the reader keeps, and no copy of it is needed: the rebuild runs once, and loads
    /// every object it places directly as it takes in the object streams, so that an object found near its entry is
    /// found, and its entry changed, before the document has opened; object streams add entries that place no offset, and
    /// replace an entry written directly that a later object stream holds again, whose offset still bounds the searches
    /// where they were sorted before (<see cref="PdfXRefTable.FirstOffsetAfter"/>).
    /// While the chain is read, and while a document whose chain gave none is rebuilt, the index being made bounds the
    /// searches made meanwhile, which opening makes the same whatever is read after it.
    /// </remarks>
    private PdfXRefTable SearchIndex => ChainIndex ?? _xref;

    /// <summary>
    /// Gives where the next object after <paramref name="position"/> starts, as <see cref="SearchIndex"/> places it,
    /// object <paramref name="number"/>'s own entry aside; null when it places none before the end of the file.
    /// </summary>
    /// <remarks>
    /// Offsets in an index count from the header, which <see cref="_headerOffset"/> places in the file; an offset past
    /// the end of the file places nothing, nor does a row's negative one, which the index leaves out (#125). The index
    /// keeps its offsets sorted once asked, so that a lookup costs a logarithm of it, however many streams are searched.
    /// The object's own entry is stepped over: the object read, relocated from an entry that missed it, may have that
    /// entry past the start of its data. Its entry in the index the reader reads with is not looked at: that one changes
    /// as objects are read, and the bound would change with it.
    /// </remarks>
    private long? NextObjectStart(int number, long position)
    {
        var index = SearchIndex;
        var next = index.FirstOffsetAfter(position - _headerOffset);

        // Offsets come back each greater than the last, so the object's own is stepped over once at most.
        if (index.TryGet(number, out var own) && own.Kind == XRefEntryKind.Regular && next == own.Offset)
        {
            next = index.FirstOffsetAfter(next);
        }

        return next < _source.Length - _headerOffset ? next + _headerOffset : null;
    }

    /// <summary>
    /// Gives how much of the file the first read of a search for <c>endstream</c> past the parser's window reads: twice what
    /// the parser holds of the data, <paramref name="buffered"/> bytes — twice what each read repeats at least —, up to
    /// <see cref="EndStreamSearchChunk"/>. Each read after it reads twice as much as the one before, up to that too.
    /// </summary>
    /// <remarks>
    /// The data runs past the window, so the object spans at least what the parser holds of it: a search stopped by a
    /// header it could not know of before reading it reads at most about twice what its object spans, whatever window the
    /// object was parsed through — the default 8 KB, or less when <see cref="PdfReaderLimits.MaxObjectLength"/> is set
    /// lower —, and one whose <c>endstream</c> lies no further past the window than the parser holds of the data, as a
    /// length a few bytes off leaves it, reads the file once.
    /// </remarks>
    internal static int EndStreamSearchFirstRead(int buffered) =>
        (int)Math.Min(2L * Math.Max(buffered, EndStreamSearchOverlap), EndStreamSearchChunk);

    /// <summary>
    /// Finds the first <c>endstream</c> after <paramref name="dataStart"/> that ends before <paramref name="limit"/> and
    /// before the first object header the bytes hold — in the bytes the parser holds, then in the file past them; one a
    /// comment between that header's tokens holds is not before it —, and returns where the data ends, the end-of-line
    /// before the keyword left out; -1 when there is none, and then <paramref name="header"/> is where that header starts,
    /// when one stopped the search, or -1.
    /// </summary>
    /// <remarks>
    /// The file is read forward through windows the source lends, each repeating the last
    /// <see cref="EndStreamSearchOverlap"/> bytes of the one before, each twice as large as the one before from
    /// <see cref="EndStreamSearchFirstRead"/> up to <see cref="EndStreamSearchChunk"/>, and each starting further on than the
    /// last whatever the source returns: the search ends at <paramref name="limit"/>, at the end of the file, at a header,
    /// or at a source that has nothing more to give, and reads no byte past the end of the file. The file is read once, but
    /// for what each read repeats, and each read is searched for both: a header only before the <c>endstream</c> it holds,
    /// with what <see cref="HeaderText"/> carries into it of the reads before — but for a first <c>endstream</c> inside a
    /// comment, on a line whose tokens may begin a header, which waits for the tokens past its line, through the reads after
    /// it, to say whether a header that starts before it ends past it (#261): the search reads on as where no keyword was
    /// found, and one that reads no further, past its budget or at a source with nothing more to give, takes the keyword
    /// (<see cref="PendingEndStream"/>). The object's own <c>endobj</c> is followed
    /// through each read up to where the next starts, or to where the search stops (<see cref="EndObjTail"/>), a copy kept
    /// at each place a number carried into the next read starts: a header that a later read finds to start in a read
    /// before it — comment lines between its tokens, which may hold <c>endobj</c> (#261) — is judged with the tail as it
    /// stood at its start, so that what the data ends at, and whether an <c>endobj</c> was seen before it, does not depend
    /// on where the reads part the data (<see cref="EndObjTailCopies"/>). That holds of windows of more than
    /// <see cref="EndStreamSearchOverlap"/> bytes, short or not: one of no more, which a source may give short before the
    /// end of the file (#52), makes the next start a byte past its own start, repeating less of it than the overlap, so
    /// that a keyword or a header no window then holds whole, or the end of line before a keyword, may be missed, a keyword
    /// in a comment among them, and the search may end elsewhere; but once a window has held a keyword in a comment whole
    /// and it waits, it is decided as whole windows decide it, however short the reads after it.
    /// </remarks>
    /// <param name="dataStart">Where the data starts in the file.</param>
    /// <param name="buffered">The bytes of the data the parser holds, from its start.</param>
    /// <param name="limit">Where the next object the index places starts, or the end of the file.</param>
    /// <param name="deferred">Whether the stream's <c>/Length</c> is one the chain could not read, which no header stops.</param>
    /// <param name="readsFile">Whether the file past the bytes the parser holds may be read.</param>
    /// <param name="spent">Counts the bytes read from the file.</param>
    /// <param name="header">Where the header that stopped the search starts, or -1.</param>
    /// <param name="searched">Whether the search reached where it stops, rather than the end of what it might read.</param>
    /// <param name="endObjSeen">
    /// Whether the bytes searched, before a header that stopped the search, hold an <c>endobj</c>; false where an
    /// <c>endstream</c> found ends the data, before which none is looked for.
    /// </param>
    /// <param name="objectEnd">
    /// Where the data ends, when no <c>endstream</c> is found and the search reached where it stops, at the object's own
    /// <c>endobj</c> that only white space and comments separate from there (<see cref="EndObjTail"/>); -1 otherwise.
    /// </param>
    /// <param name="emptied">Whether a window the source gave empty ended the search.</param>
    /// <param name="endObjHeld">
    /// What follows the <c>endstream</c> found, as the file's first read after the data would show it from the bytes that hold
    /// it — Present or Absent —; Unknown where that read would leave it undecided, where they end before it, or where none
    /// was found (<see cref="EndObjAfter"/>).
    /// </param>
    private long SearchEndStream(
        long dataStart,
        ReadOnlySpan<byte> buffered,
        long limit,
        bool deferred,
        bool readsFile,
        ref long spent,
        out long header,
        out bool searched,
        out bool endObjSeen,
        out long objectEnd,
        out bool emptied,
        out EndObjState endObjHeld)
    {
        Span<byte> carried = stackalloc byte[HeaderText.CarriedLength];
        Span<long> carriedAt = stackalloc long[HeaderText.CarriedLength];
        Span<long> starts = stackalloc long[HeaderText.MostNumbersCarried];
        Span<EndObjTail> copies = stackalloc EndObjTail[HeaderText.MostNumbersCarried];
        Span<long> copiedAt = stackalloc long[HeaderText.MostNumbersCarried];
        var text = new HeaderText(carried, carriedAt, dataStart);
        var tail = new EndObjTail(dataStart);
        var held = new EndObjTailCopies(copies, copiedAt);
        var pending = default(PendingEndStream);
        searched = true;
        emptied = false;
        objectEnd = -1;
        endObjHeld = EndObjState.Unknown;

        var inBuffer = (int)Math.Clamp(limit - dataStart, 0, buffered.Length);
        text.Read(buffered[..inBuffer], dataStart);
        var found = StreamEnds.SearchStretch(text, endsRange: dataStart + inBuffer >= limit, deferred, ref pending, out header);
        var end = Math.Min(limit, _source.Length);

        // A keyword in a comment that waits for the reads after it ends the data where the search reads no further (#261).
        if (pending.IsPending && !readsFile)
        {
            found = pending.DataEnd;
        }

        if (found >= 0 || header >= 0 || dataStart + buffered.Length >= end || !readsFile)
        {
            // An endstream found ends the data: no endobj before it is asked for, and what follows it the bytes held show.
            if (found >= 0)
            {
                endObjSeen = false;
                endObjHeld = EndObjAfter(buffered, found - dataStart, found);
                return found;
            }

            tail.Read(buffered[..inBuffer], dataStart, header >= 0 ? header : dataStart + inBuffer);
            endObjSeen = tail.Seen;

            if (header >= 0)
            {
                objectEnd = tail.DataEnd(header);
                return -1;
            }

            if (dataStart + buffered.Length >= end)
            {
                objectEnd = tail.DataEnd(end);
                return -1;
            }

            searched = false;
            return -1;
        }

        // A read the search goes on past is followed only up to where the next starts, which reads the rest again, a copy of
        // the tail kept where a header the next may find starts before it (EndObjTailCopies).
        var position = dataStart + Math.Max(0, buffered.Length - EndStreamSearchOverlap);
        text.CarryTo(position);
        held.ReadTo(ref tail, buffered[..inBuffer], dataStart, position, starts[..text.CarriedStarts(starts)]);

        // Each window starts before the end, and the next one further on than it: the loop ends at one of the returns.
        var size = EndStreamSearchFirstRead(buffered.Length);

        while (true)
        {
            using var window = _source.GetWindow(position, (int)Math.Min(size, end - position));
            var span = window.Memory.Span;
            spent += span.Length;

            if (span.IsEmpty)
            {
                endObjSeen = tail.Seen;
                emptied = true;
                return pending.IsPending ? pending.DataEnd : -1;
            }

            text.Read(span, position);
            found = StreamEnds.SearchStretch(text, endsRange: position + span.Length >= end, deferred, ref pending, out header);

            if (found >= 0 || header >= 0 || position + span.Length >= end)
            {
                // An endstream found that a length can reach ends the data, as in the bytes held. One past int.MaxValue
                // bytes is read as none found, which the object's endobj, followed up to it, plays its part for.
                if (found >= 0 && found - dataStart <= int.MaxValue)
                {
                    endObjSeen = false;
                    endObjHeld = EndObjAfter(span, found - position, found);
                    return found;
                }

                // A header that starts before this read starts at a number the reads before carried into it, where a copy of
                // the tail was kept: it is judged as the tail stood there, not as the reads before read on past it.
                if (held.TryGet(header, out var atHeader))
                {
                    tail = atHeader;
                }
                else
                {
                    tail.Read(span, position, header >= 0 ? header : position + span.Length);
                }

                endObjSeen = tail.Seen;
                objectEnd = found < 0 ? tail.DataEnd(header >= 0 ? header : end) : -1;
                return found;
            }

            var next = position + Math.Max(1, span.Length - EndStreamSearchOverlap);
            text.CarryTo(next);
            held.ReadTo(ref tail, span, position, next, starts[..text.CarriedStarts(starts)]);
            position = next;
            size = Math.Min(2 * size, EndStreamSearchChunk);
        }
    }

    /// <summary>
    /// Reads what follows the <c>endstream</c> after data that ends at <paramref name="dataEnd"/>, <paramref name="at"/> bytes
    /// into <paramref name="bytes"/>, which hold it, as the first read of <see cref="IPdfStreamDataProvider.CheckEndStream"/>
    /// reads it from the file: Present or Absent where the bytes that read takes, which <paramref name="bytes"/> hold, show
    /// it; Unknown where they leave it undecided, or <paramref name="bytes"/> end before them, for the file to be asked.
    /// </summary>
    /// <remarks>
    /// Only what that read holds is judged, however far past it the bytes a search holds go: where it cannot decide, the file
    /// is read on through the white space and comments that may lead to what follows, with reads that count with the
    /// searches (<see cref="EndStreamSearchPasses"/>) and stop once those may read no more — what the bytes held past it show
    /// would be seen where those reads see nothing, and would spend nothing where they spend. So judged, what follows the
    /// keyword, and what the searches have read, are what the file's read makes them, which no caller tells apart.
    /// </remarks>
    private EndObjState EndObjAfter(ReadOnlySpan<byte> bytes, long at, long dataEnd)
    {
        // As many bytes as that read asks for, or up to the end of the file, and the same end of the file.
        var first = (int)Math.Min(PdfObjectParser.EndObjLookahead, _source.Length - dataEnd);

        if (at < 0 || first <= 0 || at + first > bytes.Length)
        {
            return EndObjState.Unknown;
        }

        var end = StreamEnds.ReadEnd(bytes.Slice((int)at, first), dataEnd + first >= _source.Length);
        return end is { Kind: StreamEndKind.EndStream, EndObj: EndObjState.Present or EndObjState.Absent } ? end.EndObj : EndObjState.Unknown;
    }

    /// <summary>
    /// A stream's data in the file: where it lies and how long it is, read each time it is asked for, kept by nothing
    /// (#170), and refused once the document is disposed.
    /// </summary>
    /// <remarks>
    /// Streams whose data share a stretch of the file — overlapping objects —, or the many streams of a document read once
    /// each, hold none of it: what a caller keeps is its own, and memory follows what is being decoded. A source in
    /// memory lends the bytes where they lie, without a copy, valid for as long as its memory is; any other reads them
    /// into an array of their length, and is not asked for data of no length. No more than
    /// <see cref="PdfReaderLimits.MaxStreamLength"/> bytes are read: past it, the data is cut there, marked cut by a guard,
    /// and the guard reported, once for each stream.
    /// </remarks>
    private sealed class FileStreamData(PdfFileReader reader, long offset, int length, PdfLimitGuard guard) : PdfStreamData
    {
        private bool _cutByGuard;

        private bool _endHidden;

        public override int Length => length;

        public override long Position => offset;

        internal override PdfLimitGuard LimitGuard => guard;

        internal override bool CutByGuard => _cutByGuard;

        internal override bool EndHidden => _endHidden;

        // The trailer as read so far: what a rebuild merges is added to it, never taken from it.
        internal override bool MayBeEncrypted => reader.Trailer.ContainsKey(PdfName.Encrypt);

        public void MarkCutByGuard() => _cutByGuard = true;

        public void MarkEndHidden() => _endHidden = true;

        /// <exception cref="ObjectDisposedException">The document was disposed.</exception>
        /// <exception cref="Diagnostics.PdfLimitExceededException">
        /// The data runs past <see cref="PdfReaderLimits.MaxStreamLength"/>, and the document was opened to throw when it does.
        /// </exception>
        public override ReadOnlyMemory<byte> GetBytes()
        {
            ObjectDisposedException.ThrowIf(reader._closed, typeof(PdfDocument));

            var count = length;
            var bound = guard.Bound(PdfLimit.StreamLength);

            if (count > bound)
            {
                _cutByGuard = true;
                count = bound;
                reader.ReachStreamLength(offset);
            }

            // Data of no length is read from no source: a source is asked only for bytes it holds, never for none (#167).
            if (count == 0)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            if (reader._source.TryLend(offset, count, out var lent))
            {
                return lent;
            }

            // A source that gives less than it holds leaves the array longer than what it gave: the bytes it gave are
            // handed back, not copied again.
            var buffer = new byte[count];
            var read = reader._source.Read(offset, buffer);
            return buffer.AsMemory(0, read);
        }
    }
}
