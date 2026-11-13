using System.Runtime.ExceptionServices;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO.XRef;

namespace AdCodicem.Pdf.IO;

internal sealed partial class PdfFileReader
{
    /// <summary>
    /// Indexes every object header the file holds outside another object, read once each, to its end: the last definition
    /// of a number wins, under the generation its header gives. Notes, for the trailers the rebuild merges, where each
    /// <c>trailer</c> keyword and each cross-reference stream the scan takes lies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scan reads the file forward, a piece of <see cref="ScanChunkSize"/> bytes at a time, the last byte of a piece
    /// read only to tell an <c>obj</c> or a <c>trailer</c> that ends the bytes searched from a longer word. A header is read
    /// back from its <c>obj</c> as a load lexes it (<see cref="ReadHeaderBackwards"/>); one whose walk reaches the start of
    /// the piece undecided is read again from further back, as far as it takes: where a piece starts changes nothing of
    /// what the scan takes (#171).
    /// </para>
    /// <para>
    /// Each object the scan takes is delimited as a load reads it (<see cref="DelimitObject"/>): its value, the strings and
    /// comments it holds among it, and, for a stream whose direct <c>/Length</c> an <c>endstream</c> confirms, its data.
    /// What lies within is the object's, and the scan goes on past it: a header or a <c>trailer</c> keyword there — an
    /// attached file's own objects written as they are, a string that quotes a header — is none (#171). A stream whose
    /// length the scan cannot confirm, an indirect <c>/Length</c> or a wrong one, has its data end where a load searching for
    /// its end would stop: at the first header after it, or at its <c>endstream</c>; the headers and keywords before that
    /// <c>endstream</c> are taken as the bytes give them, comments unread. Between objects, a comment runs to the end of its
    /// line, and a header or a keyword that starts inside one is none; the scan does not follow strings there, where no
    /// object holds one.
    /// </para>
    /// <para>
    /// The index takes object numbers as the file gives them, as many as <see cref="PdfReaderLimits.MaxIndexEntries"/>
    /// lets it; past that, a header for a number it holds still replaces its entry, so that the newest definition wins
    /// whatever came before it, and one for a number it lacks is left out. A number defined again takes no room, so a file
    /// that repeats one header millions of times hides nothing behind them. The scan's time follows the file's length, and
    /// what it keeps the index's bound and the keywords it notes.
    /// </para>
    /// </remarks>
    /// <param name="redefinitions">Receives the numbers defined again.</param>
    /// <param name="trailers">Receives where the trailers the scan meets lie.</param>
    /// <returns>The exception a document opened to throw asked for, held until the index is whole; or null.</returns>
    private ExceptionDispatchInfo? ScanForObjects(Redefinitions redefinitions, ScannedTrailers trailers)
    {
        ExceptionDispatchInfo? reached = null;
        var scan = new ScanState();

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

            // The next trailer keyword in the bytes searched, found once and kept until the search passes it: -1 when none
            // lies past where its search stands, -2 before the first search.
            var trailerAt = -2;

            while (true)
            {
                var objAt = objFrom < searchEnd ? span[objFrom..searchEnd].IndexOf(ObjKeyword) : -1;
                objAt = objAt < 0 ? -1 : objFrom + objAt;

                if (trailerFrom < objFrom)
                {
                    trailerFrom = objFrom;
                }

                if (trailerAt != -1 && trailerAt < trailerFrom)
                {
                    trailerAt = trailerFrom < searchEnd ? span[trailerFrom..searchEnd].IndexOf(TrailerKeyword) : -1;
                    trailerAt = trailerAt < 0 ? -1 : trailerFrom + trailerAt;
                }

                if (trailerAt >= 0 && (objAt < 0 || trailerAt < objAt))
                {
                    NoteTrailerKeyword(span, windowStart, trailerAt, ref scan, trailers);
                    trailerFrom = trailerAt + TrailerKeyword.Length;
                    continue;
                }

                if (objAt < 0)
                {
                    break;
                }

                objFrom = objAt + ObjKeyword.Length;
                trailerFrom = Math.Max(trailerFrom, objFrom);

                var objAbsolute = windowStart + objAt;
                var walk = ReadHeaderBackwards(
                    span, objAt, startsData: windowStart == 0, signsBeforeUnknown: false, out var number, out var generation, out var headerStart, out _);
                var at = windowStart + headerStart;

                if (walk == HeaderWalk.Undecided)
                {
                    walk = ReadHeaderFurtherBack(objAbsolute, out number, out generation, out at);
                }

                if (walk != HeaderWalk.Header ||
                    (at >= scan.SweptTo ? IsHidden(span, windowStart, at, ref scan) : IsHiddenOnItsLine(at, scan.GapStart)))
                {
                    continue;
                }

                // The last definition wins: that is what an incrementally updated file means. Its entry records the
                // generation its header gives, whatever an earlier definition gave (#118). A number the index lacks takes
                // room in it, which it may have no more of.
                var known = _xref.TryGet(number, out _);

                if (known)
                {
                    redefinitions.Add(number);
                }

                if (known || HasRoomInRebuild(at, ref reached))
                {
                    _xref.Set(number, XRefEntry.Found(at - _headerOffset, generation));
                }

                var extent = at >= windowStart
                    ? DelimitObject(at, span[(int)(at - windowStart)..], endsData)
                    : DelimitObject(at, [], buffersToEnd: false);

                if (extent.CrossReferenceStream)
                {
                    trailers.Streams.Add(at);
                }

                if (!scan.Passed(extent, this))
                {
                    continue;
                }

                // The scan goes on past the object: within this window where its end lies within the bytes searched, from
                // the next window otherwise.
                if (scan.ObjFrom - windowStart <= searchEnd)
                {
                    objFrom = (int)(scan.ObjFrom - windowStart);
                    trailerFrom = objFrom;
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
                // A keyword that starts within the bytes searched but runs past them is the next window's to find. The comments
                // are swept as far as the next window starts, so that a header found in it starts past where the sweep stands.
                var objNext = windowStart + Math.Max(objFrom, searchEnd - (ObjKeyword.Length - 1));
                var trailerNext = windowStart + Math.Max(trailerFrom, searchEnd - (TrailerKeyword.Length - 1));
                Sweep(span, windowStart, Math.Max(windowStart, Math.Min(objNext, trailerNext) - ScanLead), ref scan);
                scan.ObjFrom = objNext;
                scan.TrailerFrom = trailerNext;
            }

            scan.Resume = Math.Min(scan.ObjFrom, scan.TrailerFrom);
        }

        return reached;
    }

    /// <summary>
    /// Reads again, from further back, the header of the <c>obj</c> keyword at <paramref name="objPosition"/> whose walk back
    /// reached the start of the piece the scan read it in undecided: the bytes before it are read in windows eight times
    /// larger each time, until the walk decides or reaches the start of the file. Only a walk across white space, digits
    /// and lines that hold nothing but a comment reaches that far, which ends at the keyword before it.
    /// </summary>
    /// <remarks>
    /// A header whose tokens run back past <see cref="PdfReaderLimits.MaxObjectLength"/> from its <c>obj</c> is none: a load
    /// reads no further.
    /// </remarks>
    private HeaderWalk ReadHeaderFurtherBack(long objPosition, out int number, out int generation, out long at)
    {
        var bound = Math.Min((long)_guard.Bound(PdfLimit.Object), objPosition);
        var before = (long)ScanLead;

        while (true)
        {
            before = Math.Min(bound, before * 8);
            var start = objPosition - before;
            var end = Math.Min(_source.Length, objPosition + ObjKeyword.Length + 1);
            using var window = _source.GetWindow(start, (int)(end - start));
            var walk = ReadHeaderBackwards(
                window.Memory.Span, (int)before, startsData: start == 0, signsBeforeUnknown: false, out number, out generation, out var headerStart, out _);
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
    /// after it: a keyword a load lexes as one, which neither a regular character nor a comment around it makes part of a
    /// longer word or of a comment.
    /// </summary>
    private static void NoteTrailerKeyword(
        ReadOnlySpan<byte> span, long windowStart, int keyword, ref ScanState scan, ScannedTrailers trailers)
    {
        var end = keyword + TrailerKeyword.Length;

        if ((keyword > 0 && PdfCharacters.IsRegular(span[keyword - 1])) ||
            (end < span.Length && PdfCharacters.IsRegular(span[end])) ||
            IsHidden(span, windowStart, windowStart + keyword, ref scan))
        {
            return;
        }

        trailers.Keywords.Add(windowStart + end);
    }

    /// <summary>
    /// Determines whether <paramref name="position"/>, in the window, lies in a comment or a string between objects, as a
    /// load lexes its line (<see cref="LineState"/>), the scan having swept the bytes between where it last stood and there.
    /// Within the data of a stream whose length the scan could not confirm, nothing is.
    /// </summary>
    private static bool IsHidden(ReadOnlySpan<byte> span, long windowStart, long position, ref ScanState scan)
    {
        if (position < scan.RawUntil)
        {
            return false;
        }

        Sweep(span, windowStart, position, ref scan);
        return scan.Line != LineState.Tokens;
    }

    /// <summary>
    /// Sweeps the bytes of the window from where the scan last stood to <paramref name="to"/>: where a comment or a string
    /// opens, and where it closes. Linear in the bytes swept, each once.
    /// </summary>
    private static void Sweep(ReadOnlySpan<byte> span, long windowStart, long to, ref ScanState scan)
    {
        var from = (int)(Math.Max(scan.SweptTo, Math.Max(scan.RawUntil, windowStart)) - windowStart);
        var end = (int)(to - windowStart);

        if (from < end)
        {
            scan.Line = SweepLine(span[from..], end - from, scan.Line, ref scan.StringDepth);
        }

        scan.SweptTo = Math.Max(scan.SweptTo, to);
    }

    /// <summary>
    /// Runs the state a load's lexer is in over the first <paramref name="length"/> bytes of <paramref name="bytes"/>, from
    /// <paramref name="state"/>: a comment runs to the end of its line, and a string, its parentheses balanced and a
    /// backslash escaping the byte after it, to its closing parenthesis. An end of line ends both: no object holds the
    /// bytes between objects, where a string has no business, and a line is read as its tokens, as an object's header is
    /// (<see cref="ReadHeaderBackwards"/>). The end-of-file marker <c>%%EOF</c> is a comment that ends with it, a writer
    /// gluing the next update to it; the bytes past <paramref name="length"/> are read only to tell it.
    /// </summary>
    private static LineState SweepLine(ReadOnlySpan<byte> bytes, int length, LineState state, ref int depth)
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
                        return state;
                    }

                    state = LineState.Tokens;
                    break;

                case LineState.String:
                    found = rest.IndexOfAny(StringBytes);

                    if (found < 0)
                    {
                        return state;
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
                        return state;
                    }

                    if (bytes[(at + found)..].StartsWith("%%EOF"u8))
                    {
                        found += "%%EOF".Length - 1;
                        break;
                    }

                    state = rest[found] == (byte)'%' ? LineState.Comment : LineState.String;
                    depth = 1;
                    break;
            }

            at += found + 1;
        }

        return state;
    }

    /// <summary>The bytes that change a string's state as a sweep reads it: an escape, a parenthesis, an end of line.</summary>
    private static ReadOnlySpan<byte> StringBytes => "\\()\r\n"u8;

    /// <summary>
    /// Determines whether <paramref name="position"/>, which the sweep has passed, lies in a comment or a string between
    /// objects: its line is read back to its start, no further than <paramref name="from"/>, where the scan last stood
    /// outside an object, and swept to <paramref name="position"/>. Only a header found further back than the piece it ends
    /// in asks, which a piece starts at most once.
    /// </summary>
    private bool IsHiddenOnItsLine(long position, long from)
    {
        var lineStart = position;

        while (lineStart > from)
        {
            var start = Math.Max(from, lineStart - ScanLead);
            using var window = _source.GetWindow(start, (int)(lineStart - start));
            var end = window.Memory.Span.LastIndexOfAny((byte)'\r', (byte)'\n');

            if (end >= 0)
            {
                lineStart = start + end + 1;
                break;
            }

            lineStart = start;
        }

        using var line = _source.GetWindow(lineStart, (int)(position - lineStart));
        var depth = 0;
        return SweepLine(line.Memory.Span, line.Length, LineState.Tokens, ref depth) != LineState.Tokens;
    }

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
    private ObjectExtent DelimitObject(long at, ReadOnlySpan<byte> buffered, bool buffersToEnd)
    {
        var bound = (long)_guard.Bound(PdfLimit.Object);
        var bounds = buffered.IsEmpty ? ObjectBounds.Cut : DelimitWithin(buffered, buffersToEnd);
        var size = Math.Max(XRefWindow, 2L * buffered.Length);

        while (bounds.Kind == BoundsKind.Cut)
        {
            if (size / 8 >= bound)
            {
                return default;
            }

            var asked = (int)Math.Min(Math.Min(size, bound), _source.Length - at);
            using var window = _source.GetWindow(at, asked);
            var toEnd = window.Length < asked || at + window.Length >= _source.Length;
            bounds = DelimitWithin(window.Memory.Span, toEnd);
            size *= 8;
        }

        // What a load reads of the object, its header and value or its stream's dictionary, lies within the guard's bound.
        return bounds.Kind switch
        {
            BoundsKind.Value when bounds.End <= bound => new ObjectExtent(ExtentKind.Value, at + bounds.End, -1, false),
            BoundsKind.Stream when bounds.DataStart <= bound => ConfirmStream(at, bounds),
            _ => default,
        };
    }

    /// <summary>
    /// Says where the data of the stream whose header starts at <paramref name="at"/> ends: past the <c>endstream</c> that
    /// follows its direct <c>/Length</c>, as a load reads it, or, its length not confirmed, unknown.
    /// </summary>
    private ObjectExtent ConfirmStream(long at, ObjectBounds bounds)
    {
        var dataStart = at + bounds.DataStart;

        if (bounds.Length >= 0 && bounds.Length <= _source.Length - dataStart)
        {
            var dataEnd = dataStart + bounds.Length;
            var size = (long)PdfObjectParser.EndObjLookahead;

            // White space and comments before endstream run as far as they go, as a load reads them (#174).
            while (true)
            {
                var asked = (int)Math.Min(size, _source.Length - dataEnd);
                using var window = _source.GetWindow(dataEnd, asked);
                var toEnd = window.Length < asked || dataEnd + window.Length >= _source.Length;
                var end = StreamEnds.ReadEnd(window.Memory.Span, toEnd);

                if (end.Kind == StreamEndKind.EndStream)
                {
                    return new ObjectExtent(ExtentKind.Stream, dataEnd + end.KeywordEnd, dataStart, bounds.CrossReferenceStream);
                }

                if (end.Kind != StreamEndKind.Undecided || toEnd)
                {
                    break;
                }

                size *= 8;
            }
        }

        return new ObjectExtent(ExtentKind.UnconfirmedStream, dataStart, dataStart, bounds.CrossReferenceStream);
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
    /// container leaves the value undelimited.
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
            return default;
        }

        var first = lexer.Read();

        if (first.Kind == PdfTokenKind.EndOfInput || (!endsData && first.End >= bytes.Length))
        {
            return endsData ? default : ObjectBounds.Cut;
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
                return default;

            default:
                return new ObjectBounds(BoundsKind.Value, first.End, -1, -1, false);
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
            var token = lexer.Read();
            count++;

            if (token.Kind == PdfTokenKind.EndOfInput || (!endsData && token.End >= bytes.Length))
            {
                return endsData ? default : ObjectBounds.Cut;
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
                    return default;

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
        var next = lexer.Read();

        if (!endsData && next.End >= bytes.Length)
        {
            return ObjectBounds.Cut;
        }

        if (!next.IsKeyword("stream"u8))
        {
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
        var second = lexer.Read();

        if (!endsData && second.End >= bytes.Length)
        {
            return ObjectBounds.Cut;
        }

        if (second.Kind == PdfTokenKind.Integer)
        {
            var third = lexer.Read();

            if (!endsData && third.End >= bytes.Length)
            {
                return ObjectBounds.Cut;
            }

            if (third.IsKeyword("R"u8))
            {
                return new ObjectBounds(BoundsKind.Value, third.End, -1, -1, false);
            }

            if (third.IsKeyword("obj"u8))
            {
                return new ObjectBounds(BoundsKind.Value, first.Start, -1, -1, false);
            }
        }

        return new ObjectBounds(BoundsKind.Value, first.End, -1, -1, false);
    }

    /// <summary>Determines whether <paramref name="token"/> is a keyword that ends an object or a stream, or starts one.</summary>
    private static bool IsObjectEnd(PdfToken token) =>
        token.IsKeyword("obj"u8) || token.IsKeyword("endobj"u8) || token.IsKeyword("stream"u8) || token.IsKeyword("endstream"u8);

    /// <summary>Determines whether the value that ends at <paramref name="end"/> in <paramref name="bytes"/> ends with <c>&gt;&gt;</c>.</summary>
    private static bool IsDictionaryEnd(ReadOnlySpan<byte> bytes, int end) =>
        end >= 2 && bytes[end - 1] == (byte)'>' && bytes[end - 2] == (byte)'>';

    /// <summary>Where the scan stands, between the pieces it reads.</summary>
    private struct ScanState
    {
        /// <summary>Where the next piece's search starts: the earlier of the two below.</summary>
        public long Resume;

        /// <summary>Where the search for the next <c>obj</c> keyword starts.</summary>
        public long ObjFrom;

        /// <summary>Where the search for the next <c>trailer</c> keyword starts.</summary>
        public long TrailerFrom;

        /// <summary>Where the sweep for comments and strings has reached.</summary>
        public long SweptTo;

        /// <summary>What the sweep stands in.</summary>
        public LineState Line;

        /// <summary>How many parentheses the string the sweep stands in has open.</summary>
        public int StringDepth;

        /// <summary>Where the scan last stood outside an object: past the last object it delimited, or the file's start.</summary>
        public long GapStart;

        /// <summary>
        /// Where the data of a stream whose length the scan could not confirm ends for it: at its first <c>endstream</c>,
        /// before which the scan reads no comment.
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
        public bool Passed(ObjectExtent extent, PdfFileReader reader)
        {
            switch (extent.Kind)
            {
                case ExtentKind.Value or ExtentKind.Stream:
                    ObjFrom = TrailerFrom = SweptTo = GapStart = extent.End;
                    Line = LineState.Tokens;
                    return true;

                case ExtentKind.UnconfirmedStream:
                    RawUntil = Math.Max(RawUntil, reader.FindEndStream(extent.DataStart, ref EndStreamAt));
                    ObjFrom = TrailerFrom = extent.DataStart;
                    SweptTo = GapStart = Math.Max(SweptTo, RawUntil);
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
    /// past where they start: each byte is searched once.
    /// </summary>
    private long FindEndStream(long from, ref long known)
    {
        if (known >= from)
        {
            return known;
        }

        var position = from;

        while (position < _source.Length)
        {
            var asked = (int)Math.Min(ScanChunkSize, _source.Length - position);
            using var window = _source.GetWindow(position, asked);
            var found = window.Memory.Span.IndexOf(StreamEnds.EndStreamKeyword);

            if (found >= 0)
            {
                return known = position + found;
            }

            if (window.Length < asked || position + window.Length >= _source.Length)
            {
                break;
            }

            position += window.Length - (StreamEnds.EndStreamKeyword.Length - 1);
        }

        return known = _source.Length;
    }

    /// <summary>Where the trailers a rebuild merges lie, as the scan for objects meets them.</summary>
    private sealed class ScannedTrailers
    {
        /// <summary>Gets where the dictionary after each <c>trailer</c> keyword starts: right past the keyword.</summary>
        public List<long> Keywords { get; } = [];

        /// <summary>Gets where the header of each cross-reference stream starts.</summary>
        public List<long> Streams { get; } = [];
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
    }

    /// <summary>What the lexing of an object at the start of some bytes found (<see cref="DelimitWithin"/>).</summary>
    /// <param name="Kind">What the bytes held.</param>
    /// <param name="End">Where the value ends, in the bytes.</param>
    /// <param name="DataStart">Where a stream's data starts, in the bytes.</param>
    /// <param name="Length">A stream's direct <c>/Length</c>, or -1.</param>
    /// <param name="CrossReferenceStream">Whether the dictionary gives <c>/Type /XRef</c>.</param>
    private readonly record struct ObjectBounds(BoundsKind Kind, int End, int DataStart, long Length, bool CrossReferenceStream)
    {
        /// <summary>Gets bounds that say the bytes end before the value does.</summary>
        public static ObjectBounds Cut => new(BoundsKind.Cut, -1, -1, -1, false);
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
