using System.Globalization;
using System.Runtime.CompilerServices;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO;

/// <summary>
/// Builds objects from PDF syntax.
/// </summary>
/// <remarks>
/// The parser assumes nothing about the file being correct. Every loop terminates on end of input, nesting
/// is bounded, and a construct it cannot make sense of becomes a null object plus a diagnostic — never an
/// exception, and never an unbounded amount of work.
/// </remarks>
internal ref struct PdfObjectParser
{
    /// <summary>
    /// Deepest nesting followed. Real documents rarely pass ten; a file that passes this is either broken
    /// or trying to exhaust the stack.
    /// </summary>
    /// <remarks>
    /// The bound holds within one object. An object loaded while another is parsed — an indirect <c>/Length</c> —
    /// starts again at zero, so the stack is also asked, at each container, whether it has room for another level:
    /// what the bound alone would allow, times the loads the reader nests, is more than a small thread holds.
    /// </remarks>
    private const int MaxDepth = 128;

    /// <summary>
    /// How many bytes after a stream's data the reader reads first to decide whether <c>endstream</c> follows it, and
    /// whether <c>endobj</c> follows that: the few bytes of white space or comment files put before and between the two
    /// keywords, and the keywords with the byte after each.
    /// </summary>
    /// <remarks>
    /// No bound (ADR 34): a first read, not what the reader reads. White space and comments that run past it are read on as
    /// far as they go (#174), and what follows them decides.
    /// </remarks>
    internal const int EndObjLookahead = 64;

    private static ReadOnlySpan<byte> EndStreamKeyword => StreamEnds.EndStreamKeyword;

    private readonly ReadOnlyMemory<byte> _memory;
    private readonly IPdfObjectSource? _source;
    private readonly PdfDiagnostics? _diagnostics;
    private readonly IPdfStreamDataProvider? _streamData;
    private readonly IPdfFileEdge? _fileEdge;
    private readonly long _baseOffset;

    /// <summary>
    /// Whether the buffer ends where the data does — the file, or an object stream's decoded data —, rather than at the
    /// edge of a window the reader may grow: only then is what the end of the buffer cuts short the data's fault.
    /// </summary>
    private readonly bool _endsData;

    private PdfLexer _lexer;

    /// <summary>Whether the parse met the end of its buffer, where it may have cut an object short.</summary>
    private bool _truncated;

    /// <summary>
    /// Whether the end of the data was reported already: a cut leaves every construct around it open, and is reported
    /// once, for the innermost.
    /// </summary>
    private bool _cutReported;

    /// <summary>
    /// Where the header of the object after this one starts, in the buffer, when one ended the containers this object left
    /// open, where a value or a key was expected (#230); -1 when none did.
    /// </summary>
    private int _headerAt = -1;

    /// <summary>
    /// Whether the header <see cref="_headerAt"/> says is one a window's edge left undecided, which a larger window may show
    /// is none: what the edge made of it, which ends the containers as a header does, but reports nothing and gives the key
    /// before it no value (#230).
    /// </summary>
    private bool _headerUndecided;

    /// <summary>
    /// Whether a window's edge left the look-ahead of an integer undecided — "12 0" that may be what the edge left of
    /// "12 0 R", or "12 0 R" that may be what it left of "12 0 RG" —: what the parse reads after the integer is what the
    /// edge made of the reference, not what follows the object (#224).
    /// </summary>
    private bool _lookAheadCut;

    /// <summary>
    /// Whether the last stream read kept a declared length that neither <c>endstream</c> nor <c>endobj</c> follows: where
    /// its object ends is unknown, and its <c>endobj</c> is not judged.
    /// </summary>
    private bool _endObjUnknown;

    private EndObjState _endObj;

    /// <summary>What was found of the last stream read, when the file did not confirm its length.</summary>
    private StreamLengthFault? _lengthFault;

    /// <summary>
    /// What follows the data of the last stream read, when the provider was asked: what follows its <c>endstream</c>, or,
    /// when it has none, whether its object's <c>endobj</c> ends it; null otherwise.
    /// </summary>
    private EndObjState? _endObjAfterStream;

    /// <summary>
    /// The number of the indirect object being read, whose own entries in the indexes do not end a search for its
    /// stream's <c>endstream</c>; 0 for a value read alone.
    /// </summary>
    private int _objectNumber;

    /// <summary>
    /// Whether the value is an indirect object's, read after its header: a report of the header that ended a container left
    /// open names the object it ended, which a trailer, or a value read alone, is not.
    /// </summary>
    private bool _inObject;

    /// <summary>
    /// The object stream member the buffer holds, when it is an object stream's decoded data rather than bytes of the
    /// file; null otherwise.
    /// </summary>
    private ObjectStreamMember? _member;

    /// <summary>Where the <c>stream</c> keyword a member is written with starts, or -1 when none was met.</summary>
    private int _memberStream = -1;

    /// <summary>Where the header of the last indirect object <see cref="TryReadIndirectObject"/> read starts, in the buffer.</summary>
    private int _headerStart;

    /// <summary>Creates a parser over <paramref name="memory"/>.</summary>
    /// <param name="memory">The bytes to parse.</param>
    /// <param name="baseOffset">Where the bytes start in the file.</param>
    /// <param name="source">Resolves what the objects read refer to.</param>
    /// <param name="diagnostics">Receives what parsing met.</param>
    /// <param name="streamData">Serves the data of a stream from the file, past the bytes given.</param>
    /// <param name="endsData">
    /// Whether the bytes end where the file does; when they end at a window's edge instead, what that edge cuts short is
    /// not reported, and <see cref="IsTruncated"/> asks for a larger window.
    /// </param>
    /// <param name="fileEdge">
    /// Says what the file holds right past the bytes given, for a parser no <paramref name="streamData"/> backs; one that
    /// serves stream data says it too.
    /// </param>
    public PdfObjectParser(
        ReadOnlyMemory<byte> memory,
        long baseOffset = 0,
        IPdfObjectSource? source = null,
        PdfDiagnostics? diagnostics = null,
        IPdfStreamDataProvider? streamData = null,
        bool endsData = true,
        IPdfFileEdge? fileEdge = null)
    {
        _memory = memory;
        _lexer = new PdfLexer(memory.Span);
        _baseOffset = baseOffset;
        _source = source;
        _diagnostics = diagnostics;
        _streamData = streamData;
        _fileEdge = fileEdge ?? streamData;
        _endsData = endsData;
    }

    /// <summary>
    /// Creates a parser over an object stream's decoded data, to read member <paramref name="objectNumber"/>. A byte of
    /// decoded data is no position in the file: what the parser meets is placed where the stream's data starts, and the
    /// member and the byte are given in the message.
    /// </summary>
    /// <param name="data">The object stream's decoded data.</param>
    /// <param name="streamNumber">The object stream's number.</param>
    /// <param name="dataStart">Where the object stream's data starts in the file.</param>
    /// <param name="objectNumber">The member being read.</param>
    /// <param name="source">Resolves what the member refers to.</param>
    /// <param name="diagnostics">Receives what parsing met.</param>
    /// <param name="endsData">
    /// Whether <paramref name="data"/> ends where the member must — at the next member's start, or at the end of the decoded
    /// data —, rather than where a guard cut it: only then is what its end cuts short the data's fault.
    /// </param>
    /// <param name="endsAtNextMember">Whether <paramref name="data"/> ends at the next member's start, which its reports then name.</param>
    /// <param name="dataEdge">
    /// Says what the decoded data holds past <paramref name="data"/>'s end, where a window's edge leaves a keyword undecided:
    /// the data is in memory, so a member a window ends exactly with reads whole, as under a larger window.
    /// </param>
    public static PdfObjectParser ForObjectStreamMember(
        ReadOnlyMemory<byte> data,
        int streamNumber,
        long dataStart,
        int objectNumber,
        IPdfObjectSource source,
        PdfDiagnostics diagnostics,
        bool endsData = true,
        bool endsAtNextMember = false,
        IPdfFileEdge? dataEdge = null)
    {
        var parser = new PdfObjectParser(data, 0, source, diagnostics, endsData: endsData, fileEdge: dataEdge);
        parser._member = new ObjectStreamMember(streamNumber, dataStart, objectNumber, diagnostics, endsAtNextMember);
        return parser;
    }

    /// <summary>
    /// Gets a value indicating whether the input ended where it may have cut an object short — a larger
    /// buffer could read differently. A caller that can offer one should; a caller whose buffer is the
    /// whole input keeps what was read.
    /// </summary>
    public readonly bool IsTruncated => _truncated;

    /// <summary>
    /// Gets a value indicating whether the object stream member being read is written as a stream, or holds one: a
    /// dictionary followed by <c>stream</c>, which an object stream cannot hold (ISO 32000-1 7.5.7). The parse stops at the
    /// keyword, and what it read of the member is no value (#161).
    /// </summary>
    public readonly bool IsMemberStream => _memberStream >= 0;

    /// <summary>Gets where the header of the last indirect object <see cref="TryReadIndirectObject"/> read starts, in the buffer.</summary>
    public readonly int HeaderStart => _headerStart;

    /// <summary>Gets what followed the value of the last indirect object <see cref="TryReadIndirectObject"/> read.</summary>
    public readonly EndObjState EndObj => _endObj;

    /// <summary>
    /// Gets what the parser found of the last stream it read, when the file did not confirm its length; null when it
    /// did. An indirect object whose value is a stream ends with it, so this is that stream's.
    /// </summary>
    public readonly StreamLengthFault? LengthFault => _lengthFault;

    /// <summary>Gets or sets the position in the buffer.</summary>
    public int Position
    {
        readonly get => _lexer.Position;
        set => _lexer.Position = value;
    }

    /// <summary>Parses the object at the current position.</summary>
    public PdfObject ParseObject()
    {
        _headerAt = -1;
        _headerUndecided = false;
        _lookAheadCut = false;
        return ParseValue(_lexer.Read(), 0);
    }

    /// <summary>
    /// Determines whether an object header may still begin with <paramref name="number"/> and <paramref name="generation"/>,
    /// the first two tokens read from bytes <paramref name="length"/> long whose third reached their end: neither is a whole
    /// token no header begins with — a number that is no object number, a generation that is none.
    /// </summary>
    /// <remarks>
    /// A token the bytes' end reached may go on past it, and rules nothing out: <c>0000</c> may be the start of
    /// <c>00004</c>. The reader's check of the header each entry places decides alike, token by token, as it reads on past
    /// a window's edge.
    /// </remarks>
    private static bool MayBeginHeader(PdfToken number, PdfToken generation, int length)
    {
        if (number.Kind == PdfTokenKind.EndOfInput || number.End >= length)
        {
            return true;
        }

        if (number.Kind != PdfTokenKind.Integer || number.Integer is <= 0 or > PdfObjectId.MaxNumber)
        {
            return false;
        }

        return generation.Kind == PdfTokenKind.EndOfInput || generation.End >= length ||
            (generation.Kind == PdfTokenKind.Integer && generation.Integer is >= 0 and <= PdfObjectId.MaxGeneration);
    }

    /// <summary>
    /// Reads an indirect object definition — <c>N G obj … endobj</c> — at the current position.
    /// </summary>
    public bool TryReadIndirectObject(out PdfObjectId id, out PdfObject value)
    {
        id = default;
        value = PdfNull.Instance;

        var start = _lexer.Position;
        var number = _lexer.Read();
        var generation = _lexer.Read();
        var keyword = _lexer.Read();

        if (number.Kind != PdfTokenKind.Integer ||
            generation.Kind != PdfTokenKind.Integer ||
            !keyword.IsKeyword("obj"u8) ||
            number.Integer is <= 0 or > PdfObjectId.MaxNumber ||
            generation.Integer is < 0 or > PdfObjectId.MaxGeneration)
        {
            // A header the end of the buffer reached — white space up to it, "5 0 o" — may be whole in a
            // larger one; one a whole token already rules out is no header however large the buffer, and no larger one
            // is asked for (#175).
            if ((keyword.Kind == PdfTokenKind.EndOfInput || keyword.End >= _memory.Length) &&
                MayBeginHeader(number, generation, _memory.Length))
            {
                _truncated = true;
            }

            _lexer.Position = start;
            return false;
        }

        id = new PdfObjectId((int)number.Integer, (int)generation.Integer);
        _objectNumber = id.Number;
        _headerStart = number.Start;
        _headerAt = -1;
        _headerUndecided = false;
        _lookAheadCut = false;

        // An empty object, "2 0 obj endobj", reads its endobj as its value, and says so; that endobj is its own.
        var beforeValue = _lexer.Position;
        var empty = _lexer.Read().IsKeyword("endobj"u8);
        _lexer.Position = beforeValue;

        _inObject = true;
        value = ParseObject();

        if (empty)
        {
            _endObj = EndObjState.Present;
            return true;
        }

        var afterValue = _lexer.Position;
        var next = _lexer.Read();

        // A header a window's edge left undecided ended the value, or a reference the edge cut after its generation: what
        // follows it is the edge's, not seen (#224, #230).
        var unseen = next.Kind == PdfTokenKind.EndOfInput || next.End >= _memory.Length || _headerUndecided || _lookAheadCut;

        // What follows a stream whose data runs past the buffer lies past it too: the file was asked at the stream's
        // end, and its answer stands where the buffer has none (#55).
        var endObj = IsEndObj(next);
        _endObj = endObj
            ? EndObjState.Present
            : _endObjUnknown ? EndObjState.Unknown
            : !unseen ? EndObjState.Absent
            : value is PdfStream && _endObjAfterStream is { } afterStream ? afterStream
            : EndObjState.Unseen;

        if (!endObj)
        {
            // A value followed by the end of the buffer, or by a token the end of the buffer may have cut,
            // may itself have been cut there: a string that lost its closing parenthesis, a dictionary whose
            // "stream" keyword lies beyond. Containers say so on their own; a lone value cannot, so the
            // caller is told to look further. A stream is exempt: its data is expected to run past the
            // buffer, and the reader serves it from the file.
            if (value is not PdfStream && unseen)
            {
                _truncated = true;
            }

            _lexer.Position = afterValue;
        }

        return true;
    }

    private PdfObject ParseValue(PdfToken token, int depth)
    {
        // A value that reaches the end of the buffer may go on past it: "12" of "1234", a string without
        // its closing delimiter, the "<" of a "<<". An endobj the file says ends there does not: an empty object's.
        if (token.Kind != PdfTokenKind.EndOfInput && token.End >= _memory.Length && !IsEndObj(token))
        {
            _truncated = true;
        }

        switch (token.Kind)
        {
            case PdfTokenKind.EndOfInput:
                // Inside an array or a dictionary, the end of the data is the container's to report; here, no construct is
                // open, and the value is missing.
                _truncated = true;

                if (_endsData && !_cutReported)
                {
                    _cutReported = true;
                    Report(
                        PdfDiagnosticCodes.SyntaxTruncatedObject,
                        $"{End} in the middle of an object.",
                        token.Start);
                }

                return PdfNull.Instance;

            case PdfTokenKind.Integer:
                return ParseIntegerOrReference(token, depth);

            // A token a window's edge cut is the guard's, or the next window's, to judge: nothing is reported of it.
            case PdfTokenKind.Real when !double.IsFinite(token.Real) && AtWindowEdge(token):
                return PdfNull.Instance;

            case PdfTokenKind.Real when !double.IsFinite(token.Real):
                // A real is a double. One past it reads as null, not as an infinity the file did not write: no valid
                // file writes one — ISO 32000-1's Annex C advises reals within 3.403 × 10^38 — and no non-finite real
                // reaches the document.
                // Hostile data can hold one at every token: the number is quoted only for a report that is kept.
                Report(
                    PdfDiagnosticCodes.SyntaxNumberOutOfRange,
                    KeepsReports
                        ? $"The number {FileQuote.Keyword(_memory.Span[token.Start..token.End])} is beyond what a real can hold; it was read as null."
                        : "A number is beyond what a real can hold; it was read as null.",
                    token.Start);
                return PdfNull.Instance;

            case PdfTokenKind.Real:
                return new PdfReal(token.Real);

            case PdfTokenKind.Name:
                return ReadName(token);

            case PdfTokenKind.LiteralString:
                ReportIfUnterminated(token, "a literal string", depth);
                return new PdfString(PdfStringDecoder.DecodeLiteral(token.Text));

            case PdfTokenKind.HexString:
                ReportIfUnterminated(token, "a hexadecimal string", depth);
                return ReadHexString(token);

            case PdfTokenKind.ArrayStart:
                return ParseArray(depth, token.Start);

            case PdfTokenKind.DictionaryStart:
                return ParseDictionaryOrStream(depth, token.Start);

            case PdfTokenKind.Keyword when token.Text.SequenceEqual("true"u8):
                return PdfBoolean.True;

            case PdfTokenKind.Keyword when token.Text.SequenceEqual("false"u8):
                return PdfBoolean.False;

            case PdfTokenKind.Keyword when token.Text.SequenceEqual("null"u8):
                return PdfNull.Instance;

            default:
                if (!AtWindowEdge(token) || IsEndObj(token))
                {
                    Report(PdfDiagnosticCodes.SyntaxUnexpectedToken, "A token was found where a value was expected.", token.Start);
                }

                return PdfNull.Instance;
        }
    }

    /// <summary>
    /// Parses an integer, or the reference it starts, inside <paramref name="depth"/> containers: inside one, the header of
    /// the next object it starts ends them (#230), and reads as null where <see cref="_headerAt"/> says.
    /// </summary>
    private PdfObject ParseIntegerOrReference(PdfToken token, int depth)
    {
        // "12 0 R" is only distinguishable from "12" by looking two tokens ahead. "0 0 R" is a reference too: object 0
        // heads the free list and is never in use (ISO 32000-1, 7.5.4), so it reads as null, as a reference to any
        // object the file lacks does (7.3.10), and keeps its place in its array or dictionary.
        var saved = _lexer.Position;
        var second = _lexer.Read();
        var third = second.Kind == PdfTokenKind.Integer ? _lexer.Read() : second;

        // An R a window's edge reaches is one only where the file says it ends there: "12 0 Rx" is no reference.
        var referenced = second.Kind == PdfTokenKind.Integer && IsWholeKeyword(third, "R"u8);

        if (referenced && token.Integer is >= 0 and <= PdfObjectId.MaxNumber && second.Integer is >= 0 and <= PdfObjectId.MaxGeneration)
        {
            return new PdfReference(new PdfObjectId((int)token.Integer, (int)second.Integer), _source);
        }

        // The next object's header where an element or a value should be: this object lost both its closing delimiter and
        // its endobj, and its containers end where the next object starts (#230). A value alone is no container.
        if (depth > 0 && IsHeader(token, second, third))
        {
            _headerAt = token.Start;
            _lexer.Position = saved;
            return PdfNull.Instance;
        }

        // Inside a container, a window's edge may have cut a keyword that obj starts with after two integers, "12 0 ob": no
        // reference, and maybe the next object's header, which a larger window shows. The containers end there, as at a
        // header, and the number is no value of the file's. "12 0" alone may be what the edge left of a reference, and is
        // kept as far as it was read, as any value the edge cuts.
        if (depth > 0 && third.Kind == PdfTokenKind.Keyword && HeaderUndecided(token, second, third))
        {
            _truncated = true;
            _headerAt = token.Start;
            _headerUndecided = true;
            _lexer.Position = saved;
            return PdfNull.Instance;
        }

        // The buffer ended before the look-ahead could tell: "12 0" may be all there is, or what a window's edge left of
        // "12 0 R" — or "12 0 R" what it left of "12 0 RG", which a larger window shows (#174).
        if (third.Kind == PdfTokenKind.EndOfInput || (second.Kind == PdfTokenKind.Integer && third.IsKeyword("R"u8) && !referenced))
        {
            _truncated = true;
            _lookAheadCut = !_endsData;
        }

        _lexer.Position = saved;
        return PdfInteger.Create(token.Integer);
    }

    /// <summary>Parses an array whose <c>[</c> starts at <paramref name="openedAt"/>, inside <paramref name="depth"/> containers.</summary>
    private PdfObject ParseArray(int depth, int openedAt)
    {
        if (depth >= MaxDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            Report(PdfDiagnosticCodes.SyntaxDepthExceeded, "Nesting is deeper than the reader will follow.", _lexer.Position);
            SkipContainer(PdfTokenKind.ArrayEnd, openedAt, depth);
            return PdfNull.Instance;
        }

        var array = new PdfArray();

        while (true)
        {
            var token = _lexer.Read();

            switch (token.Kind)
            {
                case PdfTokenKind.ArrayEnd:
                    return array;

                case PdfTokenKind.EndOfInput:
                    _truncated = true;
                    ReportCut("an array", openedAt, depth);
                    return array;

                // A dictionary end inside an array means the file is confused; stopping here keeps the
                // damage local instead of swallowing the rest of the document into this array.
                case PdfTokenKind.DictionaryEnd:
                    if (!PastWindowEdge)
                    {
                        Report(PdfDiagnosticCodes.SyntaxUnexpectedToken, "An array was closed by a dictionary end.", token.Start);
                    }

                    return array;

                // An endobj where an element should be ends the object, the array with it, as PDFBox and pdfium read it:
                // the array takes nothing past it. An object that lost its own endobj meets the next object's header
                // first, which ends it there (#230).
                case PdfTokenKind.Keyword when IsEndObj(token):
                    _lexer.Position = token.Start;
                    ReportCut("an array", openedAt, depth, atEndObj: true);
                    return array;

                default:
                    var element = ParseValue(token, depth + 1);

                    if (_headerAt >= 0)
                    {
                        // The next object's header ends the array: where an element should be, reported here; inside an
                        // array or a dictionary the element opened, reported there, and that element kept as it was read.
                        if (_headerAt == token.Start)
                        {
                            if (!_headerUndecided)
                            {
                                ReportCut("an array", openedAt, depth, atHeader: true);
                            }
                        }
                        else
                        {
                            array.Add(element);
                        }

                        _lexer.Position = _headerAt;
                        return array;
                    }

                    array.Add(element);

                    if (IsMemberStream)
                    {
                        return array;
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Parses a dictionary whose <c>&lt;&lt;</c> starts at <paramref name="openedAt"/>, inside <paramref name="depth"/>
    /// containers, and the stream it introduces if <c>stream</c> follows it.
    /// </summary>
    private PdfObject ParseDictionaryOrStream(int depth, int openedAt)
    {
        if (depth >= MaxDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            Report(PdfDiagnosticCodes.SyntaxDepthExceeded, "Nesting is deeper than the reader will follow.", _lexer.Position);
            SkipContainer(PdfTokenKind.DictionaryEnd, openedAt, depth);
            return PdfNull.Instance;
        }

        var dictionary = new PdfDictionary();
        var nullKeys = default(NullKeys);

        while (true)
        {
            var keyToken = _lexer.Read();

            if (keyToken.Kind is PdfTokenKind.DictionaryEnd)
            {
                break;
            }

            if (keyToken.Kind is PdfTokenKind.EndOfInput)
            {
                _truncated = true;
                ReportCut("a dictionary", openedAt, depth);
                return dictionary;
            }

            // An endobj where a key should be ends the object, the dictionary with it: the dictionary takes nothing past it.
            // An object that lost its own endobj meets the next object's header first, which ends it there (#230).
            if (IsEndObj(keyToken))
            {
                _lexer.Position = keyToken.Start;
                ReportCut("a dictionary", openedAt, depth, atEndObj: true);
                return dictionary;
            }

            if (keyToken.Kind != PdfTokenKind.Name)
            {
                if (StartsHeader(keyToken))
                {
                    _headerAt = keyToken.Start;
                    _lexer.Position = keyToken.Start;
                    ReportCut("a dictionary", openedAt, depth, atHeader: true);
                    return dictionary;
                }

                // A string where a key should be is a string all the same, which the end of the data can leave open inside
                // the dictionary: it is the innermost construct then.
                if (keyToken.Kind is PdfTokenKind.LiteralString)
                {
                    ReportIfUnterminated(keyToken, "a literal string", depth + 1);
                }
                else if (keyToken.Kind is PdfTokenKind.HexString)
                {
                    ReportIfUnterminated(keyToken, "a hexadecimal string", depth + 1);
                }

                if (!AtWindowEdge(keyToken))
                {
                    Report(PdfDiagnosticCodes.SyntaxUnexpectedToken, "A dictionary key was not a name.", keyToken.Start);
                }

                continue;
            }

            var key = ReadName(keyToken);
            var keyAtEdge = AtWindowEdge(keyToken);
            var valueToken = _lexer.Read();

            if (valueToken.Kind is PdfTokenKind.DictionaryEnd)
            {
                // A key with no value: the specification says an absent value is null, which removes what the key held.
                if (!PastWindowEdge)
                {
                    Report(PdfDiagnosticCodes.SyntaxUnexpectedToken, "A dictionary key had no value.", valueToken.Start);
                }

                Give(dictionary, ref nullKeys, keyToken, key, PdfNull.Instance, keyAtEdge, valueCut: false);
                break;
            }

            if (valueToken.Kind is PdfTokenKind.EndOfInput)
            {
                _truncated = true;
                ReportCut("a dictionary", openedAt, depth, valueMissing: true);
                return dictionary;
            }

            if (IsEndObj(valueToken))
            {
                _lexer.Position = valueToken.Start;
                ReportCut("a dictionary", openedAt, depth, valueMissing: true, atEndObj: true);
                Give(dictionary, ref nullKeys, keyToken, key, PdfNull.Instance, keyAtEdge, valueCut: false);
                return dictionary;
            }

            var value = ParseValue(valueToken, depth + 1);

            if (IsMemberStream)
            {
                return dictionary;
            }

            if (_headerAt >= 0)
            {
                // The next object's header ends the dictionary: where the value should be, the key given none and reported
                // here; inside an array or a dictionary the value opened, reported there, and that value kept.
                if (_headerAt == valueToken.Start && _headerUndecided)
                {
                    // One a window's edge left undecided is the edge's: the key keeps what it held, and nothing is said.
                    Give(dictionary, ref nullKeys, keyToken, key, PdfNull.Instance, keyAtEdge: true, valueCut: true);
                }
                else if (_headerAt == valueToken.Start)
                {
                    ReportCut("a dictionary", openedAt, depth, valueMissing: true, atHeader: true);
                    Give(dictionary, ref nullKeys, keyToken, key, PdfNull.Instance, keyAtEdge, valueCut: false);
                }
                else
                {
                    Give(dictionary, ref nullKeys, keyToken, key, value, keyAtEdge, valueCut: false);
                }

                _lexer.Position = _headerAt;
                return dictionary;
            }

            // A value the parse met a window's edge in may be what the edge made of it — a keyword cut short reads as null —:
            // it is kept as far as it was read, but removes nothing.
            Give(dictionary, ref nullKeys, keyToken, key, value, keyAtEdge, valueCut: PastWindowEdge);
        }

        var afterDictionary = _lexer.Position;
        var next = _lexer.Read();

        // A member's window that a guard ended may have left a stream keyword after the dictionary past it, or cut one — a
        // keyword it reaches that begins "stream", or is "stream" itself, which may be a longer keyword —: the member is cut
        // there, as an object written in the file whose window ends after its value is. What else follows is whole
        // wherever it lies, or cannot be a stream keyword however it goes on.
        if (_member is not null && !_endsData &&
            (next.Kind is PdfTokenKind.EndOfInput ||
             (next.Kind is PdfTokenKind.Keyword && next.End >= _memory.Length && "stream"u8.StartsWith(next.Text))))
        {
            _truncated = true;
            _lexer.Position = afterDictionary;
            return dictionary;
        }

        if (next.IsKeyword("stream"u8))
        {
            // A member's stream is not read: the member is no value, and the parse stops here (#161).
            if (_member is not null)
            {
                _memberStream = next.Start;
                return dictionary;
            }

            return ReadStream(dictionary);
        }

        _lexer.Position = afterDictionary;
        return dictionary;
    }

    /// <summary>
    /// Reads a stream's data, whose <c>stream</c> keyword the lexer just read, and finds where it ends (#55, #174).
    /// </summary>
    /// <remarks>
    /// The declared length is confirmed by an <c>endstream</c> after it, white space and comments between, however many.
    /// Unconfirmed, or with no length to confirm, the data is searched for its first <c>endstream</c>, which ends it: in the
    /// bytes the parser holds, then in the file, up to the nearer of the first object header the bytes hold and the next
    /// object the index places, or to the end of the data. Where a guard hid the length, a header the data spells that
    /// stops the search leaves the data up to it to the guard. Stream data never counts toward the bytes the parser is given
    /// to read an object through: a window is never grown to find an <c>endstream</c>. With none before the stop, the
    /// object's <c>endobj</c> plays its part: it ends the data where it follows the declared length, or where it ends the
    /// object before the stop; without it, a declared length that ends before the stop is kept, and the data otherwise
    /// runs to the stop.
    /// </remarks>
    private PdfStream ReadStream(PdfDictionary dictionary)
    {
        var span = _memory.Span;
        var keywordEnd = _lexer.Position;

        _lexer.SkipStreamEndOfLine();

        var dataStart = _lexer.Position;
        _lengthFault = null;
        _endObjAfterStream = null;
        _endObjUnknown = false;

        // The end-of-line after "stream" is not data. A buffer that ends on the keyword, or on the carriage
        // return of a CR LF, cannot say where the data starts: the file says, as it says what follows the data, since the
        // data counts toward no window — not even one a guard ended there (#174) —; a parser the reader does not back
        // needs a larger buffer, as does a keyword a regular character goes on, which is no stream keyword. One that ends
        // the data says: the stream's data, which starts there, holds nothing.
        if (!_endsData && dataStart >= span.Length && (dataStart == keywordEnd || span[dataStart - 1] != (byte)'\n'))
        {
            var start = _streamData?.DataStart(_baseOffset + keywordEnd) ?? -1;

            if (start < 0)
            {
                _truncated = true;
            }
            else
            {
                dataStart = (int)(start - _baseOffset);
            }
        }

        var declared = ReadLength(dictionary);
        var length = declared.Form == StreamLengthForm.Integer ? (int)declared.Value.GetValueOrDefault() : -1;

        if (_truncated)
        {
            return Finish(dictionary, dataStart, Math.Max(length, 0), span.Length);
        }

        // An integer read from a /Length a guard cut is a place to check, never a length kept when nothing confirms it.
        var checkedAt = declared.Hint ?? length;
        var confirmed = ConfirmsLength(span, dataStart, checkedAt);

        if (confirmed is null)
        {
            // Undecided, the buffer a window the parser alone was given: a larger one says.
            _truncated = true;
            return Finish(dictionary, dataStart, length, span.Length);
        }

        if (confirmed == true)
        {
            return Finish(dictionary, dataStart, checkedAt, span.Length);
        }

        // A cross-reference stream whose /Length the chain could not read keeps a carriage return before the line feed as
        // data, which it may be — rows can end in 0x0D, and the length, once read, is checked against what was taken —,
        // and is not stopped by rows that spell an object header (#182, #220).
        var deferred = declared.Form == StreamLengthForm.Deferred;
        var declaredEnd = length < 0 ? -1 : _baseOffset + dataStart + length;
        var search = _streamData is not null
            ? _streamData.FindEndStream(_objectNumber, _baseOffset + dataStart, declaredEnd, span[Math.Min(dataStart, span.Length)..], deferred)
            : SearchBuffer(span, dataStart, declaredEnd, deferred);

        if (search is not { } settled)
        {
            _truncated = true;
            return Finish(dictionary, dataStart, Math.Max(length, 0), span.Length);
        }

        if (settled.Length is { } found)
        {
            // A length read once the chain is, or one a guard left out of the index or cut, is not judged: nothing is said
            // of it, nor a message made for it.
            var hidden = declared.Form is StreamLengthForm.ObjectUnindexed or StreamLengthForm.ObjectCut;

            if (length != found && declared.Form != StreamLengthForm.Deferred && !hidden &&
                KeepLengthFault(Fault(declared, dataStart, found, found, EndStreamState.Found, null)))
            {
                WarnLengthFault(PdfDiagnosticCodes.StreamLengthInvalid, DescribeRecovered(declared, found), dataStart);
            }

            // Where a guard cut the /Length or left it out of the index, an endstream the data spells, as a valid stream's may —
            // in a string, followed by endobj or not —, ends the search as the stream's own would: the length that tells them
            // apart is the guard's. One what follows proves its own — the next object the index places, the end of the file,
            // a cross-reference section's keyword, after the object's endobj or in its place — ends the data, the file's.
            // Any other's data is the guard's, cut by it, the object's endobj unjudged, and nothing is said of the file, as
            // at a header the data spells (#224): one past which the searches could read nothing more, too.
            if (hidden && _streamData is { } file &&
                (settled.EndObj is not (EndObjState.Absent or EndObjState.Present) ||
                    !file.IsOwnEndStream(_baseOffset + dataStart + found, settled.EndObj, settled.NextObject)))
            {
                _endObjUnknown = true;
                return Finish(dictionary, dataStart, found, span.Length, cutByGuard: true);
            }

            _endObjAfterStream = _streamData is null ? null : settled.EndObj;
            return Finish(dictionary, dataStart, found, span.Length);
        }

        return FinishWithoutEndStream(dictionary, declared, dataStart, length, settled);
    }

    /// <summary>
    /// Ends the data of a stream whose search found no <c>endstream</c> before it stopped (#174): at the declared length,
    /// when its object's <c>endobj</c> follows it; at that <c>endobj</c>, when it ends the object before the stop; at the
    /// declared length, when it ends before the stop; at the stop otherwise — the next object, or the end of the data.
    /// </summary>
    private PdfStream FinishWithoutEndStream(
        PdfDictionary dictionary, DeclaredLength declared, int dataStart, int length, StreamEndSearch search)
    {
        var span = _memory.Span;
        var endOfData = _streamData is null ? span.Length : _streamData.SourceLength - _baseOffset;
        var stop = search.NextObject is { } next ? next - _baseOffset : endOfData;
        var fits = length >= 0 && dataStart + (long)length <= stop;
        var upToStop = (int)Math.Min(stop - dataStart, int.MaxValue);
        var missing = search.NextObject is null ? EndStreamState.MissingBeforeEndOfFile : EndStreamState.MissingBeforeNextObject;

        if (!search.Searched)
        {
            // The document's searches read as much of the file as they may: what lies past the bytes held is not looked at.
            // Where a /Length a guard cut or left out of the index would say how far the data runs, where it ends is the
            // guard's, as where an endstream found is not proven the stream's own: the data up to the stop is cut by it, the
            // object's endobj unjudged, and nothing is said of the file (#224).
            if (declared.Form is StreamLengthForm.ObjectCut or StreamLengthForm.ObjectUnindexed)
            {
                _endObjUnknown = true;
                return Finish(dictionary, dataStart, upToStop, span.Length, cutByGuard: true);
            }

            var kept = fits ? length : upToStop;

            if (KeepLengthFault(Fault(declared, dataStart, kept, null, EndStreamState.NotSearched, null)))
            {
                WarnLengthFault(
                    PdfDiagnosticCodes.StreamLengthInvalid,
                    fits
                        ? string.Create(
                            CultureInfo.InvariantCulture,
                            $"The stream declared {length} bytes, and no endstream follows them; the declared length is kept without a search, the document's searches for endstream having read as much of the file as they may.")
                        : string.Create(
                            CultureInfo.InvariantCulture,
                            $"{Describe(declared)}, and no endstream follows its data in the bytes read with its object; the {kept} bytes {(search.NextObject is null ? "to the end of the file" : "up to the next object")} are taken as its data without a search, the document's searches for endstream having read as much of the file as they may."),
                    dataStart);
            }

            _endObjUnknown = true;
            return Finish(dictionary, dataStart, kept, span.Length);
        }

        if (fits && search.EndObjAfterDeclared)
        {
            if (KeepLengthFault(Fault(declared, dataStart, length, null, EndStreamState.MissingBeforeEndObj, null)))
            {
                WarnLengthFault(
                    PdfDiagnosticCodes.StreamTruncated,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The stream has no endstream: its object's endobj follows the {length} bytes it declared, which are taken as its data."),
                    dataStart);
            }

            _endObjAfterStream = EndObjState.Present;
            return Finish(dictionary, dataStart, length, span.Length);
        }

        if (search.ObjectEnd >= 0)
        {
            var taken = (int)Math.Min(search.ObjectEnd - _baseOffset - dataStart, int.MaxValue);

            if (KeepLengthFault(Fault(declared, dataStart, taken, null, EndStreamState.MissingBeforeEndObj, search.NextObject)))
            {
                WarnLengthFault(
                    PdfDiagnosticCodes.StreamTruncated,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{Describe(declared)}, and no endstream follows its data {Where(search.NextObject)}: its object's endobj ends it, after {taken} bytes."),
                    dataStart);
            }

            _endObjAfterStream = EndObjState.Present;
            return Finish(dictionary, dataStart, taken, span.Length);
        }

        if (fits)
        {
            if (KeepLengthFault(Fault(declared, dataStart, length, null, missing, search.NextObject)))
            {
                WarnLengthFault(
                    PdfDiagnosticCodes.StreamLengthInvalid,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The stream declared {length} bytes, and no endstream follows them {Where(search.NextObject)}; the declared length is kept."),
                    dataStart);
            }

            // Where the object ends is unknown, and its endobj is not judged.
            _endObjUnknown = true;
            return Finish(dictionary, dataStart, length, span.Length);
        }

        // A /Length a guard cut or left out of the index hides where the data ends, which a header the data spells stops the
        // search at, as a valid stream's may — an embedded file's does —, no endobj of the object's own before it: the data
        // up to it is the guard's, cut by it, the object's endobj unjudged, and nothing is said of the file (#224).
        if (declared.Form is StreamLengthForm.ObjectCut or StreamLengthForm.ObjectUnindexed && search.HeaderStopped)
        {
            _endObjUnknown = true;
            return Finish(dictionary, dataStart, upToStop, span.Length, cutByGuard: true);
        }

        // The data runs to the next object, which no declared length passes, or to the end of the file. The object lacks its
        // endobj, unless what is taken as its data holds one, which may be the object's own: where it ends is then unknown.
        if (KeepLengthFault(Fault(declared, dataStart, upToStop, null, missing, search.NextObject)))
        {
            WarnLengthFault(
                search.NextObject is null ? PdfDiagnosticCodes.StreamTruncated : PdfDiagnosticCodes.StreamLengthInvalid,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Describe(declared)}, and no endstream follows its data {Where(search.NextObject)}; the {upToStop} bytes {(search.NextObject is null ? "to the end of the file" : "up to that object")} are taken as its data."),
                dataStart);
        }

        _endObjUnknown = search.EndObjSeen;
        _endObjAfterStream = search.EndObjSeen ? EndObjState.Unknown : EndObjState.Absent;
        return Finish(dictionary, dataStart, upToStop, span.Length);

        // Where the search stopped, as a report says it.
        static string Where(long? nextObject) => nextObject is { } at
            ? string.Create(CultureInfo.InvariantCulture, $"before the next object, at {at}")
            : "before the end of the file";
    }

    /// <summary>
    /// Determines whether the declared length is confirmed by an <c>endstream</c> after it, white space and comments
    /// between, however many: in the buffer, or, where the buffer cannot say, in the file, which the provider reads on as
    /// far as they run (#174). Null when neither can say: the buffer is a window given to the parser alone.
    /// </summary>
    private bool? ConfirmsLength(ReadOnlySpan<byte> span, int dataStart, int length)
    {
        if (length < 0)
        {
            return false;
        }

        var dataEnd = dataStart + (long)length;

        if (dataEnd <= span.Length)
        {
            var end = StreamEnds.ReadEnd(span[(int)dataEnd..], reachesEndOfFile: _endsData);

            if (end.Kind != StreamEndKind.Undecided)
            {
                // What follows an endstream the buffer holds, when the buffer ends before it shows, is read on in the file,
                // as it is past the buffer: where the window ends decides nothing of the object's endobj (#174).
                if (end is { Kind: StreamEndKind.EndStream, EndObj: EndObjState.Unseen } && _streamData is not null)
                {
                    _endObjAfterStream = _streamData.CheckEndStream(_baseOffset + dataEnd);
                }

                return end.Kind == StreamEndKind.EndStream;
            }
        }
        else if (_endsData)
        {
            return false;
        }

        if (_streamData is null)
        {
            return null;
        }

        if (_baseOffset + dataEnd > _streamData.SourceLength)
        {
            return false;
        }

        _endObjAfterStream = _streamData.CheckEndStream(_baseOffset + dataEnd);
        return _endObjAfterStream is not null;
    }

    /// <summary>
    /// Searches the buffer of a parser the reader does not back for the first <c>endstream</c> after the start of the data,
    /// up to the first object header it holds, as the reader searches the file, and says what the object's <c>endobj</c>
    /// does when it finds none; null when the buffer holds neither, and is a window a larger one may show more of.
    /// </summary>
    private readonly StreamEndSearch? SearchBuffer(ReadOnlySpan<byte> span, int dataStart, long declaredEnd, bool deferred)
    {
        Span<byte> carried = stackalloc byte[HeaderText.CarriedLength];
        Span<long> carriedAt = stackalloc long[HeaderText.CarriedLength];
        var start = _baseOffset + dataStart;
        var text = new HeaderText(carried, carriedAt, start);
        text.Read(span[dataStart..], start);

        var dataEnd = StreamEnds.SearchStretch(text, endsRange: _endsData, deferred, out var header);

        if (dataEnd >= 0)
        {
            return new StreamEndSearch((int)(dataEnd - start), null, EndObjState.Unseen);
        }

        if (header < 0 && !_endsData)
        {
            return null;
        }

        var stop = header < 0 ? span.Length : (int)(header - _baseOffset);
        var stretch = span[dataStart..stop];
        var tail = new EndObjTail(start);
        tail.Read(stretch, start, _baseOffset + stop);

        return new StreamEndSearch(null, header < 0 ? null : header, EndObjState.Unknown)
        {
            HeaderStopped = header >= 0,
            EndObjSeen = StreamEnds.HoldsEndObj(stretch, stretch.Length),
            EndObjAfterDeclared = declaredEnd >= start && declaredEnd - _baseOffset <= stop &&
                StreamEnds.IsEndObjAt(span[..stop], (int)(declaredEnd - _baseOffset), reachesEndOfFile: true) == true,
            ObjectEnd = tail.DataEnd(_baseOffset + stop),
        };
    }

    /// <summary>
    /// Reads a stream's <c>/Length</c>: the integer it gives, and, when it gives none the parser can take as a length,
    /// how it was written — so that a report says what the file did rather than a length it never declared (#120).
    /// </summary>
    private readonly DeclaredLength ReadLength(PdfDictionary dictionary)
    {
        var written = dictionary.GetRaw(PdfName.Length);

        if (written is null)
        {
            return new DeclaredLength(StreamLengthForm.Absent);
        }

        var value = written;
        PdfObjectId? reference = null;

        if (written is PdfReference named)
        {
            reference = named.Id;
            ObjectPresence presence;

            if (_streamData is not null)
            {
                value = _streamData.ResolveLength(named, out presence).Resolve();
            }
            else
            {
                // Parsed alone, a reference that resolves to nothing could not be read: whether the file holds the
                // object, only the reader can say.
                value = named.Resolve();
                presence = value is PdfNull ? ObjectPresence.Unproduced : ObjectPresence.Defined;
            }

            // An integer the cut left, whole or parted, is only a place to look for endstream at, and no declared length; the
            // object the guard cut is the one a chain of references from the /Length leads to (#224).
            if (presence == ObjectPresence.Cut)
            {
                return new DeclaredLength(
                    StreamLengthForm.ObjectCut,
                    Reference: named.Target(PdfReference.MaxChainLength),
                    Hint: value.AsInteger() is { } hint and >= 0 and <= int.MaxValue ? (int)hint : null);
            }

            // The object a guard left out of the index, as the one it cut, is the one the chain of references leads to.
            if (presence == ObjectPresence.Unindexed)
            {
                return new DeclaredLength(StreamLengthForm.ObjectUnindexed, Reference: named.Target(PdfReference.MaxChainLength));
            }

            if (presence != ObjectPresence.Defined)
            {
                return new DeclaredLength(
                    presence switch
                    {
                        ObjectPresence.Missing => StreamLengthForm.ObjectMissing,
                        ObjectPresence.Deferred => StreamLengthForm.Deferred,
                        _ => StreamLengthForm.ObjectUnreadable,
                    },
                    Reference: reference);
            }
        }

        if (value.AsInteger() is not { } integer)
        {
            return new DeclaredLength(
                StreamLengthForm.NotAnInteger,
                Reference: reference,
                Kind: KindOf(value),
                Written: value is PdfReal or PdfName or PdfBoolean ? value : null);
        }

        return new DeclaredLength(
            integer is >= 0 and <= int.MaxValue ? StreamLengthForm.Integer : StreamLengthForm.OutOfRange, integer, reference);
    }

    /// <summary>
    /// Says what kind of value <paramref name="value"/> is, in words that quote nothing of the file: <c>an integer</c>,
    /// <c>a real number</c>, <c>a dictionary</c>; <c>null</c> for null, or for a value that resolves to none.
    /// </summary>
    internal static string KindOf(PdfObject? value) => value switch
    {
        PdfInteger => "an integer",
        PdfReal => "a real number",
        PdfName => "a name",
        PdfBoolean => "a boolean",
        PdfString => "a string",
        PdfArray => "an array",
        PdfStream => "a stream",
        PdfDictionary => "a dictionary",
        _ => "null",
    };

    /// <summary>Says what was wrong with a stream's <c>/Length</c>, its <c>endstream</c> found after <paramref name="recovered"/> bytes.</summary>
    private static string DescribeRecovered(DeclaredLength declared, int recovered) => declared.Form == StreamLengthForm.Integer
        ? string.Create(CultureInfo.InvariantCulture, $"The stream declared {declared.Value} bytes but ended after {recovered}.")
        : string.Create(CultureInfo.InvariantCulture, $"{Describe(declared)}; its data ends after {recovered} bytes.");

    /// <summary>
    /// Says how a stream's <c>/Length</c> was written, as a report starts: the length it declared, or why it gives none.
    /// </summary>
    private static string Describe(DeclaredLength declared)
    {
        if (declared.Form == StreamLengthForm.Integer)
        {
            return string.Create(CultureInfo.InvariantCulture, $"The stream declared {declared.Value} bytes");
        }

        if (declared.Form == StreamLengthForm.Absent)
        {
            return "The stream has no /Length";
        }

        var written = declared.Reference is not { } reference
            ? "is " + Value(declared)
            : declared.Form switch
            {
                StreamLengthForm.ObjectMissing => Names(reference, "which the file lacks"),
                StreamLengthForm.ObjectUnreadable => Names(reference, "which could not be read"),
                StreamLengthForm.ObjectCut => Names(reference, "which one of the reader's limits cut"),
                StreamLengthForm.ObjectUnindexed => Names(reference, "which one of the reader's limits left out of the index"),
                _ => Names(reference, "which holds " + Value(declared)),
            };

        return "The stream's /Length " + written;

        static string Names(PdfObjectId reference, string what) =>
            string.Create(CultureInfo.InvariantCulture, $"names object {reference.Number} {reference.Generation}, {what}");

        static string Value(DeclaredLength declared) => declared.Form == StreamLengthForm.NotAnInteger
            ? $"{Holds(declared)}, not a non-negative integer"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{declared.Value}, {(declared.Value < 0 ? "a length no stream can have" : "more than the reader can take as a length")}");

        // A string's or a container's content is not quoted: what it is is enough to say why it is no length.
        static string Holds(DeclaredLength declared) => declared.Written switch
        {
            PdfName name => declared.Kind + ", " + FileQuote.Name(name),
            PdfBoolean boolean => declared.Kind + (boolean.Value ? ", true" : ", false"),
            { } real => declared.Kind + ", " + real,
            _ => declared.Kind!,
        };
    }

    /// <summary>Describes what was found of the stream being read, whose data starts at <paramref name="dataStart"/>.</summary>
    private readonly StreamLengthFault Fault(
        DeclaredLength declared, int dataStart, int taken, int? found, EndStreamState endStream, long? nextObject) => new()
        {
            Form = declared.Form,
            Reference = declared.Reference,
            Declared = declared.Value,
            Kind = declared.Kind,
            Value = declared.Written,
            Taken = taken,
            Found = found,
            EndStream = endStream,
            NextObject = nextObject,
            DataStart = _baseOffset + dataStart,
        };

    /// <summary>
    /// Keeps what was found of the stream being read, and says whether to report it: unless a reading of the same stream the
    /// reader kept already has — a stream parsed again, after the cache let it go or the index was rebuilt, is reported
    /// once —, or no diagnostics are kept. The message is made only then.
    /// </summary>
    private bool KeepLengthFault(StreamLengthFault fault)
    {
        // A /Length read once the cross-reference chain is: whether the data ends where it should is known then (#182). One a
        // guard left out of the index, or cut, is unknown, and the guard's report says why; a missing endstream is still the
        // file's (#224), and the only fault reported of it, where no header the data spells, nor an endstream its endobj
        // does not follow, stops the search.
        if (fault.Form == StreamLengthForm.Deferred)
        {
            return false;
        }

        _lengthFault = fault;
        return _diagnostics is not null && _streamData?.IsLengthFaultReported(fault.DataStart) != true;
    }

    /// <summary>Reports what was found of the stream being read, whose data starts at <paramref name="dataStart"/>.</summary>
    private readonly void WarnLengthFault(string code, string message, int dataStart) =>
        _diagnostics!.Warn(code, message, _baseOffset + dataStart);

    private PdfStream Finish(PdfDictionary dictionary, int dataStart, int length, int bufferLength, bool cutByGuard = false)
    {
        length = Math.Max(length, 0);

        var data = _streamData is not null
            ? _streamData.Create(_baseOffset + dataStart, length, cutByGuard)
            : PdfStreamData.FromMemory(_memory.Slice(dataStart, Math.Min(length, bufferLength - dataStart)));

        var afterData = (int)Math.Min((long)dataStart + length, bufferLength);
        _lexer.Position = afterData;

        var afterStream = _lexer.Position;
        if (!_lexer.Read().IsKeyword(EndStreamKeyword))
        {
            _lexer.Position = afterStream;
        }

        return new PdfStream(dictionary, data);
    }

    /// <summary>
    /// Consumes a container whose contents are being discarded, keeping nesting balanced: the one that opens at
    /// <paramref name="openedAt"/>, inside <paramref name="enclosing"/> containers.
    /// </summary>
    private void SkipContainer(PdfTokenKind endKind, int openedAt, int enclosing)
    {
        var depth = 1;

        while (depth > 0)
        {
            var token = _lexer.Read();

            switch (token.Kind)
            {
                case PdfTokenKind.EndOfInput:
                    _truncated = true;
                    ReportCut(endKind == PdfTokenKind.ArrayEnd ? "an array" : "a dictionary", openedAt, enclosing);
                    return;

                case PdfTokenKind.Keyword when IsEndObj(token):
                    _lexer.Position = token.Start;
                    ReportCut(endKind == PdfTokenKind.ArrayEnd ? "an array" : "a dictionary", openedAt, enclosing, atEndObj: true);
                    return;

                case PdfTokenKind.Integer when StartsHeader(token):
                    _headerAt = token.Start;
                    _lexer.Position = token.Start;
                    ReportCut(endKind == PdfTokenKind.ArrayEnd ? "an array" : "a dictionary", openedAt, enclosing, atHeader: true);
                    return;

                case PdfTokenKind.ArrayStart when endKind == PdfTokenKind.ArrayEnd:
                case PdfTokenKind.DictionaryStart when endKind == PdfTokenKind.DictionaryEnd:
                    depth++;
                    break;

                default:
                    if (token.Kind == endKind)
                    {
                        depth--;
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Gives <paramref name="dictionary"/> the value read for <paramref name="key"/>, and reports a key given again.
    /// </summary>
    /// <remarks>
    /// The specification says an entry whose value is null is the same as no entry at all, so the parser does not create
    /// one: every later stage is spared a null it would have to ignore. A key given again keeps its last value, as qpdf,
    /// pdf.js, PDFBox, MuPDF and pdfium read it, a null given last removing it (#172) — but for a null a window's edge
    /// made, which is no value of the file's. The repeat lies at the key, and is reported unless the edge reaches the key.
    /// </remarks>
    /// <param name="dictionary">The dictionary being read.</param>
    /// <param name="nullKeys">The keys it gave null so far.</param>
    /// <param name="keyToken">The key, as the file wrote it.</param>
    /// <param name="key">The key, as it reads.</param>
    /// <param name="value">The value read for it, null when it has none.</param>
    /// <param name="keyAtEdge">Whether the key reaches a window's edge, or comes after the parse met one.</param>
    /// <param name="valueCut">Whether the parse met a window's edge reading the value.</param>
    private readonly void Give(
        PdfDictionary dictionary, ref NullKeys nullKeys, PdfToken keyToken, PdfName key, PdfObject value, bool keyAtEdge, bool valueCut)
    {
        var isNull = ReferenceEquals(value, PdfNull.Instance);
        bool repeated;
        RepeatOutcome outcome;

        if (isNull && valueCut)
        {
            repeated = dictionary.ContainsKey(key) || nullKeys.Contains(key);
            outcome = RepeatOutcome.EarlierKept;
        }
        else if (isNull)
        {
            repeated = dictionary.Remove(key) | nullKeys.Add(key);
            outcome = RepeatOutcome.LeftOut;
        }
        else if (dictionary.TryAdd(key, value))
        {
            repeated = nullKeys.Remove(key);
            outcome = RepeatOutcome.LastKept;
        }
        else
        {
            dictionary.Set(key, value);
            repeated = true;
            outcome = RepeatOutcome.LastKept;
        }

        if (repeated && !keyAtEdge)
        {
            ReportRepeatedKey(keyToken, key, outcome);
        }
    }

    /// <summary>Reports a key the dictionary gives again, at <paramref name="token"/>.</summary>
    /// <param name="token">The key given again, as the file wrote it.</param>
    /// <param name="key">The key as it reads.</param>
    /// <param name="outcome">What became of the key.</param>
    private readonly void ReportRepeatedKey(PdfToken token, PdfName key, RepeatOutcome outcome)
    {
        if (!KeepsReports)
        {
            Report(PdfDiagnosticCodes.SyntaxKeyRepeated, "A dictionary gives a key more than once.", token.Start);
            return;
        }

        // A #xx escape makes the key shorter than the token that writes it; a number sign that is no escape does not.
        var written = token.Text.Length != key.Value.Length ? ", written here with #xx escapes" : string.Empty;
        var kept = outcome switch
        {
            RepeatOutcome.LeftOut => "given null last, the key is left out",
            RepeatOutcome.EarlierKept => "its last value runs past what was read, and the value given before is kept",
            _ => "the last value given is kept",
        };

        Report(PdfDiagnosticCodes.SyntaxKeyRepeated, $"The dictionary gives the key {FileQuote.Name(key)} more than once{written}; {kept}.", token.Start);
    }

    /// <summary>Reads a name token, and reports a number sign in it that two hexadecimal digits do not follow.</summary>
    private readonly PdfName ReadName(PdfToken token)
    {
        var name = PdfName.Get(PdfStringDecoder.DecodeName(token.Text, out var badEscape));

        if (badEscape >= 0 && !AtWindowEdge(token))
        {
            Report(
                PdfDiagnosticCodes.SyntaxNameEscapeInvalid,
                KeepsReports
                    ? $"A name holds a number sign that two hexadecimal digits do not follow, kept as the byte it is: the name reads as {FileQuote.Name(name)}."
                    : "A name holds a number sign that two hexadecimal digits do not follow, kept as the byte it is.",
                token.Start + 1 + badEscape);
        }

        return name;
    }

    /// <summary>Reads a hexadecimal string token, and reports the bytes in it that are neither hexadecimal digits nor white space.</summary>
    private readonly PdfString ReadHexString(PdfToken token)
    {
        var bytes = PdfStringDecoder.DecodeHex(token.Text, out var firstStray, out var strays);

        if (strays > 0 && !AtWindowEdge(token))
        {
            Report(
                PdfDiagnosticCodes.SyntaxHexStringInvalid,
                strays == 1
                    ? "A hexadecimal string holds a byte that is neither a hexadecimal digit nor white space; it was skipped."
                    : KeepsReports
                        ? string.Create(
                            CultureInfo.InvariantCulture,
                            $"A hexadecimal string holds {strays:N0} bytes that are neither hexadecimal digits nor white space, the first here; they were skipped.")
                        : "A hexadecimal string holds bytes that are neither hexadecimal digits nor white space; they were skipped.",
                token.Start + 1 + firstStray);
        }

        return new PdfString(bytes, hexadecimal: true);
    }

    /// <summary>
    /// Reports a string the end of the data left open — the lexer took it to that end — unless the end was a window's edge,
    /// or reported already.
    /// </summary>
    /// <param name="token">The string, whose closing delimiter, when present, is the token's last byte.</param>
    /// <param name="what">The kind of string, as the message names it.</param>
    /// <param name="depth">How many arrays and dictionaries enclose it.</param>
    private void ReportIfUnterminated(PdfToken token, string what, int depth)
    {
        // A closed string's token holds both delimiters around its text; one the data ended inside holds its opening only.
        if (token.End >= _memory.Length && token.End - token.Start - token.Text.Length == 1)
        {
            ReportCut(what, token.Start, depth, token.End - token.Start);
        }
    }

    /// <summary>
    /// Reports, once for the innermost, the construct the end of the data, or an <c>endobj</c>, cut short — at
    /// <paramref name="openedAt"/>, where it opens —, unless the end of the buffer is a window's edge rather than the end of
    /// the data.
    /// </summary>
    /// <param name="what">The construct, as the message names it.</param>
    /// <param name="openedAt">Where the construct opens, in the buffer.</param>
    /// <param name="enclosing">How many arrays and dictionaries enclose it, each left open with it.</param>
    /// <param name="swallowed">The bytes a string took to the end of the data, or 0 for an array or a dictionary.</param>
    /// <param name="valueMissing">Whether the cut came after a dictionary's key, before its value.</param>
    /// <param name="atEndObj">Whether an <c>endobj</c> made the cut, rather than the end of the data.</param>
    /// <param name="atHeader">Whether the next object's header made the cut, rather than the end of the data (#230).</param>
    private void ReportCut(
        string what, int openedAt, int enclosing, int swallowed = 0, bool valueMissing = false, bool atEndObj = false, bool atHeader = false)
    {
        if ((!_endsData && !atEndObj && !atHeader) || _cutReported)
        {
            return;
        }

        _cutReported = true;

        if (!KeepsReports)
        {
            Report(PdfDiagnosticCodes.SyntaxTruncatedObject, "The object ended inside a construct it left open.", openedAt);
            return;
        }

        // A trailer, or a value read alone, is no object a header can end: the header came inside the construct left open.
        var data = atEndObj ? "An endobj ended the object"
            : atHeader ? (_inObject ? "An object header ended the object" : "An object header came")
            : End;
        var content = swallowed > 0
            ? string.Create(CultureInfo.InvariantCulture, $", which takes the {swallowed:N0} {(swallowed == 1 ? "byte" : "bytes")} from where it opens to that end")
            : valueMissing ? ", which was never closed, before the value of its last key" : ", which was never closed";
        var around = enclosing switch
        {
            0 => string.Empty,
            1 => "; the array or dictionary around it was never closed either",
            _ => string.Create(CultureInfo.InvariantCulture, $"; the {enclosing} arrays or dictionaries around it were never closed either"),
        };

        Report(PdfDiagnosticCodes.SyntaxTruncatedObject, $"{data} inside {what}{content}{around}.", openedAt);
    }

    /// <summary>
    /// Determines whether <paramref name="token"/> reaches the edge of a window that is not the end of the data, which may
    /// have cut it, or comes after the parse met that edge — a look-ahead the edge cut reads the tokens before it again,
    /// for what they are not —: what it seems to be is no fault of the file's.
    /// </summary>
    private readonly bool AtWindowEdge(PdfToken token) => PastWindowEdge || (!_endsData && token.End >= _memory.Length);

    /// <summary>
    /// Gets a value indicating whether the parse met the edge of a window that is not the end of the data: what it reads
    /// from then on may be what a look-ahead the edge cut reads again. A <c>&gt;&gt;</c> or a <c>]</c> read before is whole
    /// wherever it lies, the lexer telling it from its own bytes, and what it says is the file's.
    /// </summary>
    private readonly bool PastWindowEdge => !_endsData && _truncated;

    /// <summary>
    /// Determines whether <paramref name="token"/> is <c>endobj</c>, and whole: one that reaches the end of a buffer that is
    /// a window's edge may be the start of a longer keyword, which the file says when it backs the parser, so that an object
    /// a window ends exactly with reads whole, as under a larger window, wherever its endobj comes — after its value, where a
    /// container it left open is, or as all it holds (#174); a parser no file backs asks for a larger buffer.
    /// </summary>
    private readonly bool IsEndObj(PdfToken token) => IsWholeKeyword(token, "endobj"u8);

    /// <summary>
    /// Determines whether <paramref name="token"/> is the keyword <paramref name="keyword"/>, and whole: one that reaches the
    /// end of a buffer that is a window's edge may be the start of a longer keyword — <c>endobjX</c>, <c>RG</c> —, which the
    /// file says when it backs the parser; a parser no file backs asks for a larger buffer.
    /// </summary>
    private readonly bool IsWholeKeyword(PdfToken token, ReadOnlySpan<byte> keyword) =>
        token.IsKeyword(keyword) &&
        (!AtWindowEdge(token) || (!PastWindowEdge && _fileEdge is { } file && file.TokenEndsAt(_baseOffset + token.End)));

    /// <summary>
    /// Determines whether <paramref name="number"/>, read where a key is expected, starts the next object's header
    /// (<see cref="IsHeader"/>): the two tokens after it are read, and the lexer left where it was.
    /// </summary>
    private bool StartsHeader(PdfToken number)
    {
        if (number.Kind != PdfTokenKind.Integer || _member is not null)
        {
            return false;
        }

        var saved = _lexer.Position;
        var generation = _lexer.Read();
        var keyword = generation.Kind == PdfTokenKind.Integer ? _lexer.Read() : generation;
        _lexer.Position = saved;

        // Tokens a window's edge leaves undecided may be a header a larger window shows: the parse met the edge, and what it
        // reads from here — the number and the generation, as keys — is no fault of the file's.
        if (HeaderUndecided(number, generation, keyword))
        {
            _truncated = true;
        }

        return IsHeader(number, generation, keyword);
    }

    /// <summary>
    /// Determines whether a window's edge leaves undecided whether a number and the two tokens after it are the next
    /// object's header: the edge comes before a generation, before a keyword, or inside one that <c>obj</c> starts with,
    /// and no whole token read rules a header out — a number that is no object number, a generation that is none
    /// (<see cref="MayBeginHeader"/>). An object stream's member is followed by no header.
    /// </summary>
    private readonly bool HeaderUndecided(PdfToken number, PdfToken generation, PdfToken keyword) =>
        !_endsData && _member is null && MayBeginHeader(number, generation, _memory.Length) &&
        MayBeHeaderBytes(number, generation, keyword) &&
        (generation.Kind == PdfTokenKind.EndOfInput ||
            (generation.Kind == PdfTokenKind.Integer &&
                (keyword.Kind == PdfTokenKind.EndOfInput ||
                    (keyword.Kind == PdfTokenKind.Keyword && keyword.End >= _memory.Length && "obj"u8.StartsWith(keyword.Text)))));

    /// <summary>
    /// Determines whether three tokens are an object header as the bytes hold one, the rule the search for a stream's
    /// <c>endstream</c> stops at (<see cref="HeaderText"/>): <c>N G obj</c>, digits alone for a number from 1 to
    /// 2,147,483,647 and a generation from 0 to 65,535, white space alone between them — no comment —, and <c>obj</c> a
    /// whole keyword, which one a window's edge reaches may not be. An object stream's member is never followed by a
    /// header: its members have none (#230).
    /// </summary>
    private readonly bool IsHeader(PdfToken number, PdfToken generation, PdfToken keyword)
    {
        if (_member is not null || number.Kind != PdfTokenKind.Integer || generation.Kind != PdfTokenKind.Integer ||
            !keyword.IsKeyword("obj"u8) || AtWindowEdge(keyword) ||
            number.Integer is < 1 or > PdfObjectId.MaxNumber || generation.Integer is < 0 or > PdfObjectId.MaxGeneration)
        {
            return false;
        }

        var span = _memory.Span;
        return !span[number.Start..number.End].ContainsAnyExceptInRange((byte)'0', (byte)'9') &&
            !span[generation.Start..generation.End].ContainsAnyExceptInRange((byte)'0', (byte)'9') &&
            !span[number.End..generation.Start].ContainsAnyExcept(PdfCharacters.Whitespace) &&
            !span[generation.End..keyword.Start].ContainsAnyExcept(PdfCharacters.Whitespace);
    }

    /// <summary>
    /// Determines whether the bytes read of what may be a header, up to the window's edge, keep to the header's byte rule
    /// (<see cref="IsHeader"/>): digits alone, white space alone between the tokens — no sign, no comment.
    /// </summary>
    private readonly bool MayBeHeaderBytes(PdfToken number, PdfToken generation, PdfToken keyword)
    {
        var span = _memory.Span;

        if (span[number.Start..number.End].ContainsAnyExceptInRange((byte)'0', (byte)'9'))
        {
            return false;
        }

        if (generation.Kind != PdfTokenKind.Integer)
        {
            return !span[number.End..].ContainsAnyExcept(PdfCharacters.Whitespace);
        }

        var upTo = keyword.Kind == PdfTokenKind.EndOfInput ? span.Length : keyword.Start;
        return !span[generation.Start..generation.End].ContainsAnyExceptInRange((byte)'0', (byte)'9') &&
            !span[number.End..generation.Start].ContainsAnyExcept(PdfCharacters.Whitespace) &&
            !span[generation.End..upTo].ContainsAnyExcept(PdfCharacters.Whitespace);
    }

    /// <summary>Gets what ended the buffer, as a report of what it cut short names it.</summary>
    private readonly string End => _member switch
    {
        null => "The file ended",
        { EndsAtNextMember: true } => "The next member of the object stream started",
        _ => "The object stream's decoded data ended",
    };

    /// <summary>Gets a value indicating whether a report made now is kept, rather than counted and dropped.</summary>
    private readonly bool KeepsReports => _member is { } member ? !member.Diagnostics.IsFull : _diagnostics is { IsFull: false };

    private readonly void Report(string code, string message, long position)
    {
        if (_member is { } member)
        {
            member.Report(code, message, position);
            return;
        }

        _diagnostics?.Warn(code, message, _baseOffset + position);
    }

    /// <summary>What became of a key a dictionary gives again, as its report says.</summary>
    private enum RepeatOutcome
    {
        /// <summary>The value given last is the key's.</summary>
        LastKept,

        /// <summary>A null given last removed the key.</summary>
        LeftOut,

        /// <summary>The value given last runs past what was read, and the value given before is kept.</summary>
        EarlierKept,
    }

    /// <summary>
    /// The keys a dictionary gave null so far, which make no entry: one given again is a repeat all the same. The first is
    /// held alone, so that a dictionary's one null costs nothing, and a set is made for the others, which files seldom
    /// give: a set rather than a list, so that a dictionary of a million nulls costs as much again as it reads, not its
    /// square.
    /// </summary>
    private struct NullKeys
    {
        private PdfName? _first;
        private HashSet<PdfName>? _others;

        /// <summary>Remembers <paramref name="key"/>, and says whether it was given null before.</summary>
        public bool Add(PdfName key)
        {
            if (Contains(key))
            {
                return true;
            }

            if (_first is null)
            {
                _first = key;
            }
            else
            {
                (_others ??= []).Add(key);
            }

            return false;
        }

        /// <summary>Forgets <paramref name="key"/>, and says whether it was given null before.</summary>
        public bool Remove(PdfName key)
        {
            if (_first is not null && _first.Equals(key))
            {
                _first = null;
                return true;
            }

            return _others?.Remove(key) == true;
        }

        /// <summary>Determines whether <paramref name="key"/> was given null.</summary>
        public readonly bool Contains(PdfName key) => (_first is not null && _first.Equals(key)) || _others?.Contains(key) == true;
    }

    /// <summary>The object stream member a parser reads, and where what it meets is placed.</summary>
    /// <param name="StreamNumber">The object stream's number.</param>
    /// <param name="DataStart">Where the object stream's data starts in the file.</param>
    /// <param name="ObjectNumber">The member's number.</param>
    /// <param name="Diagnostics">Receives what the parser meets in the member.</param>
    /// <param name="EndsAtNextMember">Whether the buffer ends at the next member's start rather than at the end of the data.</param>
    private readonly record struct ObjectStreamMember(
        int StreamNumber, long DataStart, int ObjectNumber, PdfDiagnostics Diagnostics, bool EndsAtNextMember)
    {
        /// <summary>
        /// Reports, where the object stream's data starts, what was met at byte <paramref name="position"/> of its
        /// decoded data, the member and the byte added to <paramref name="message"/>. A report the diagnostics drop,
        /// being full, is counted with its message as it stands: hostile data can meet a fault at every token, and none
        /// of those is formatted for nothing.
        /// </summary>
        public void Report(string code, string message, long position) =>
            Diagnostics.Warn(
                code,
                Diagnostics.IsFull
                    ? message
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"{message} It was met in object {ObjectNumber}, at byte {position} of object stream {StreamNumber}'s decoded data."),
                DataStart);
    }

    /// <summary>A stream's <c>/Length</c>, as <see cref="ReadLength"/> read it.</summary>
    /// <param name="Form">How it was written, or what the object it names holds.</param>
    /// <param name="Value">The integer it gives, in range or not; null when it gives none.</param>
    /// <param name="Reference">The object it names, when it is a reference.</param>
    /// <param name="Kind">What kind of value it holds, when that is not an integer.</param>
    /// <param name="Written">The real number, name or boolean it holds, as the file wrote it; null for any other value.</param>
    /// <param name="Hint">
    /// The integer an object a guard cut held before the cut, when it is one a stream can have: where to look for an
    /// <c>endstream</c> first, and no declared length (#224).
    /// </param>
    private readonly record struct DeclaredLength(
        StreamLengthForm Form,
        long? Value = null,
        PdfObjectId? Reference = null,
        string? Kind = null,
        PdfObject? Written = null,
        int? Hint = null);
}
