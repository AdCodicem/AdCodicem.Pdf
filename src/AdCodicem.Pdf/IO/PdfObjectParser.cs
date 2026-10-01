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

    /// <summary>The most white space accepted between a stream's data and its <c>endstream</c> keyword.</summary>
    private const int MaxEndStreamGap = 4;

    /// <summary>How many bytes after a stream's data can decide whether <c>endstream</c> follows it.</summary>
    internal const int EndStreamLookahead = MaxEndStreamGap + 9;

    /// <summary>
    /// How many bytes after a stream's data can decide, besides whether <c>endstream</c> follows it, whether
    /// <c>endobj</c> follows that: the white space and keyword <see cref="EndStreamLookahead"/> allows, then the few
    /// bytes of white space or comment files put between the two keywords, and <c>endobj</c> with the byte after it.
    /// </summary>
    /// <remarks>
    /// No guard (ADR 34): it bounds what the reader sees of an object's end, not what it reads. A file that puts more
    /// between the keywords reads the same, and whether its object ends with <c>endobj</c> goes unseen and unjudged, as
    /// past a window's edge.
    /// </remarks>
    internal const int EndObjLookahead = 64;

    private static ReadOnlySpan<byte> EndStreamKeyword => "endstream"u8;

    private readonly ReadOnlyMemory<byte> _memory;
    private readonly IPdfObjectSource? _source;
    private readonly PdfDiagnostics? _diagnostics;
    private readonly IPdfStreamDataProvider? _streamData;
    private readonly long _baseOffset;
    private PdfLexer _lexer;
    private bool _truncated;
    private bool _endStreamMissing;
    private EndObjState _endObj;

    /// <summary>What was found of the last stream read, when the file did not confirm its length.</summary>
    private StreamLengthFault? _lengthFault;

    /// <summary>
    /// What follows the <c>endstream</c> of the last stream read, when its data ran past the buffer and the provider
    /// was asked; null otherwise.
    /// </summary>
    private EndObjState? _endObjAfterStream;

    /// <summary>
    /// The number of the indirect object being read, whose own entries in the indexes do not end a search for its
    /// stream's <c>endstream</c>; 0 for a value read alone.
    /// </summary>
    private int _objectNumber;

    /// <summary>
    /// The object stream member the buffer holds, when it is an object stream's decoded data rather than bytes of the
    /// file; null otherwise.
    /// </summary>
    private ObjectStreamMember? _member;

    public PdfObjectParser(
        ReadOnlyMemory<byte> memory,
        long baseOffset = 0,
        IPdfObjectSource? source = null,
        PdfDiagnostics? diagnostics = null,
        IPdfStreamDataProvider? streamData = null)
    {
        _memory = memory;
        _lexer = new PdfLexer(memory.Span);
        _baseOffset = baseOffset;
        _source = source;
        _diagnostics = diagnostics;
        _streamData = streamData;
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
    public static PdfObjectParser ForObjectStreamMember(
        ReadOnlyMemory<byte> data, int streamNumber, long dataStart, int objectNumber, IPdfObjectSource source, PdfDiagnostics diagnostics)
    {
        var parser = new PdfObjectParser(data, 0, source, diagnostics);
        parser._member = new ObjectStreamMember(streamNumber, dataStart, objectNumber);
        return parser;
    }

    /// <summary>
    /// Gets a value indicating whether the input ended where it may have cut an object short — a larger
    /// buffer could read differently. A caller that can offer one should; a caller whose buffer is the
    /// whole input keeps what was read.
    /// </summary>
    public readonly bool IsTruncated => _truncated;

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
    public PdfObject ParseObject() => ParseValue(_lexer.Read(), 0);

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
            number.Integer is <= 0 or > int.MaxValue ||
            generation.Integer is < 0 or > ushort.MaxValue)
        {
            // A header the end of the buffer reached — white space up to it, "5 0 o" — may be whole in a
            // larger one.
            if (keyword.Kind == PdfTokenKind.EndOfInput || keyword.End >= _memory.Length)
            {
                _truncated = true;
            }

            _lexer.Position = start;
            return false;
        }

        id = new PdfObjectId((int)number.Integer, (int)generation.Integer);
        _objectNumber = id.Number;

        // An empty object, "2 0 obj endobj", reads its endobj as its value, and says so; that endobj is its own.
        var beforeValue = _lexer.Position;
        var empty = _lexer.Read().IsKeyword("endobj"u8);
        _lexer.Position = beforeValue;

        value = ParseObject();

        if (empty)
        {
            _endObj = EndObjState.Present;
            return true;
        }

        var afterValue = _lexer.Position;
        var next = _lexer.Read();
        var unseen = next.Kind == PdfTokenKind.EndOfInput || next.End >= _memory.Length;

        // What follows a stream whose data runs past the buffer lies past it too: the file was asked at the stream's
        // end, and its answer stands where the buffer has none (#55).
        _endObj = next.IsKeyword("endobj"u8)
            ? EndObjState.Present
            : _endStreamMissing ? EndObjState.Unknown
            : !unseen ? EndObjState.Absent
            : value is PdfStream && _endObjAfterStream is { } afterStream ? afterStream
            : EndObjState.Unseen;

        if (!next.IsKeyword("endobj"u8))
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
        // its closing delimiter, the "<" of a "<<".
        if (token.Kind != PdfTokenKind.EndOfInput && token.End >= _memory.Length)
        {
            _truncated = true;
        }

        switch (token.Kind)
        {
            case PdfTokenKind.EndOfInput:
                _truncated = true;
                Report(
                    PdfDiagnosticCodes.SyntaxTruncatedObject,
                    _member is null ? "The file ended in the middle of an object." : "The object stream's decoded data ended in the middle of an object.",
                    token.Start);
                return PdfNull.Instance;

            case PdfTokenKind.Integer:
                return ParseIntegerOrReference(token);

            case PdfTokenKind.Real:
                return new PdfReal(token.Real);

            case PdfTokenKind.Name:
                return PdfName.Get(PdfStringDecoder.DecodeName(token.Text));

            case PdfTokenKind.LiteralString:
                return new PdfString(PdfStringDecoder.DecodeLiteral(token.Text));

            case PdfTokenKind.HexString:
                return new PdfString(PdfStringDecoder.DecodeHex(token.Text), hexadecimal: true);

            case PdfTokenKind.ArrayStart:
                return ParseArray(depth);

            case PdfTokenKind.DictionaryStart:
                return ParseDictionaryOrStream(depth);

            case PdfTokenKind.Keyword when token.Text.SequenceEqual("true"u8):
                return PdfBoolean.True;

            case PdfTokenKind.Keyword when token.Text.SequenceEqual("false"u8):
                return PdfBoolean.False;

            case PdfTokenKind.Keyword when token.Text.SequenceEqual("null"u8):
                return PdfNull.Instance;

            default:
                Report(PdfDiagnosticCodes.SyntaxUnexpectedToken, "A token was found where a value was expected.", token.Start);
                return PdfNull.Instance;
        }
    }

    private PdfObject ParseIntegerOrReference(PdfToken token)
    {
        // "12 0 R" is only distinguishable from "12" by looking two tokens ahead. "0 0 R" is a reference too: object 0
        // heads the free list and is never in use (ISO 32000-1, 7.5.4), so it reads as null, as a reference to any
        // object the file lacks does (7.3.10), and keeps its place in its array or dictionary.
        var saved = _lexer.Position;
        var second = _lexer.Read();
        var third = second.Kind == PdfTokenKind.Integer ? _lexer.Read() : second;

        if (second.Kind == PdfTokenKind.Integer && third.IsKeyword("R"u8) &&
            token.Integer is >= 0 and <= int.MaxValue &&
            second.Integer is >= 0 and <= ushort.MaxValue)
        {
            return new PdfReference(new PdfObjectId((int)token.Integer, (int)second.Integer), _source);
        }

        // The buffer ended before the look-ahead could tell: "12 0" may be all there is, or what a
        // window's edge left of "12 0 R".
        if (third.Kind == PdfTokenKind.EndOfInput)
        {
            _truncated = true;
        }

        _lexer.Position = saved;
        return PdfInteger.Create(token.Integer);
    }

    private PdfObject ParseArray(int depth)
    {
        if (depth >= MaxDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            Report(PdfDiagnosticCodes.SyntaxDepthExceeded, "Nesting is deeper than the reader will follow.", _lexer.Position);
            SkipContainer(PdfTokenKind.ArrayEnd);
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
                    return array;

                // A dictionary end inside an array means the file is confused; stopping here keeps the
                // damage local instead of swallowing the rest of the document into this array.
                case PdfTokenKind.DictionaryEnd:
                    Report(PdfDiagnosticCodes.SyntaxUnexpectedToken, "An array was closed by a dictionary end.", token.Start);
                    return array;

                default:
                    array.Add(ParseValue(token, depth + 1));
                    break;
            }
        }
    }

    private PdfObject ParseDictionaryOrStream(int depth)
    {
        if (depth >= MaxDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            Report(PdfDiagnosticCodes.SyntaxDepthExceeded, "Nesting is deeper than the reader will follow.", _lexer.Position);
            SkipContainer(PdfTokenKind.DictionaryEnd);
            return PdfNull.Instance;
        }

        var dictionary = new PdfDictionary();

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
                return dictionary;
            }

            if (keyToken.Kind != PdfTokenKind.Name)
            {
                Report(PdfDiagnosticCodes.SyntaxUnexpectedToken, "A dictionary key was not a name.", keyToken.Start);
                continue;
            }

            var key = PdfName.Get(PdfStringDecoder.DecodeName(keyToken.Text));
            var valueToken = _lexer.Read();

            if (valueToken.Kind is PdfTokenKind.DictionaryEnd)
            {
                // A key with no value: the specification says an absent value is null.
                Report(PdfDiagnosticCodes.SyntaxUnexpectedToken, "A dictionary key had no value.", valueToken.Start);
                break;
            }

            var value = ParseValue(valueToken, depth + 1);

            // The specification says an entry whose value is null is the same as no entry at all, so the
            // parser does not create one: every later stage is spared a null it would have to ignore.
            if (!ReferenceEquals(value, PdfNull.Instance))
            {
                dictionary.Set(key, value);
            }
        }

        var afterDictionary = _lexer.Position;

        if (_lexer.Read().IsKeyword("stream"u8))
        {
            return ReadStream(dictionary);
        }

        _lexer.Position = afterDictionary;
        return dictionary;
    }

    private PdfStream ReadStream(PdfDictionary dictionary)
    {
        var span = _memory.Span;
        var keywordEnd = _lexer.Position;

        _lexer.SkipStreamEndOfLine();

        var dataStart = _lexer.Position;
        _lengthFault = null;
        _endObjAfterStream = null;

        // The end-of-line after "stream" is not data. A buffer that ends on the keyword, or on the carriage
        // return of a CR LF, cannot say where the data starts; a larger one can.
        if (dataStart >= span.Length && (dataStart == keywordEnd || span[dataStart - 1] != (byte)'\n'))
        {
            _truncated = true;
        }

        var declared = ReadLength(dictionary);
        var length = declared.Form == StreamLengthForm.Integer ? (int)declared.Value.GetValueOrDefault() : -1;

        var beyondBuffer = length >= 0 && dataStart + (long)length > span.Length;

        if (beyondBuffer && _streamData is not null)
        {
            // The data lives past the window the reader gave us, into what the file holds: the file is asked
            // whether endstream follows the declared length (#55). An attempt the reader makes again, its buffer
            // too short to say where the data starts, asks nothing.
            if (_baseOffset + dataStart + (long)length <= _streamData.SourceLength)
            {
                return _truncated
                    ? Finish(dictionary, dataStart, length, span.Length)
                    : ReadPastBuffer(_streamData, dictionary, declared, dataStart, length);
            }

            // Unless the file cannot hold it. Then only a window that reaches the end of the file can say
            // whether the data stops at an "endstream" — the length is wrong — or at the end of the file —
            // the stream is cut —, and the search below says which once the reader has offered one.
            _truncated = true;
        }

        if (length < 0 || beyondBuffer || !ConfirmsLength(span, dataStart + length))
        {
            var recovered = FindEndStream(span, dataStart);

            if (recovered < 0)
            {
                _truncated = true;

                // An endobj after the data says the object ends there, without its endstream, and where it ends is
                // then the reader's guess; with none, the file ends inside the object, and so without its endobj.
                _endStreamMissing = span[dataStart..].IndexOf("endobj"u8) >= 0;
                var rest = span.Length - dataStart;

                ReportLengthFault(
                    PdfDiagnosticCodes.StreamTruncated,
                    _endStreamMissing
                        ? string.Create(
                            CultureInfo.InvariantCulture,
                            $"The stream has no endstream before the endobj that follows its data; the {rest} bytes to the end of the file are taken as its data.")
                        : "A stream ran past the end of the file.",
                    Fault(declared, dataStart, rest, null, _endStreamMissing ? EndStreamState.MissingBeforeEndObj : EndStreamState.MissingBeforeEndOfFile, null));

                return Finish(dictionary, dataStart, rest, span.Length);
            }

            if (length != recovered)
            {
                ReportLengthFault(
                    PdfDiagnosticCodes.StreamLengthInvalid,
                    DescribeRecovered(declared, recovered),
                    Fault(declared, dataStart, recovered, recovered, EndStreamState.Found, null));
            }

            length = recovered;
        }

        return Finish(dictionary, dataStart, length, span.Length);
    }

    /// <summary>
    /// Reads a stream whose declared length runs past the buffer, into what the file holds (#55). The file is asked
    /// whether <c>endstream</c> follows the declared length, and when it does not, searched for the first one after the
    /// start of the data, before the next object or the end of the file: found, it ends the data; not found, or not
    /// searched for once the document's searches have read as much as they may, the declared length is kept. Either is
    /// reported, and the file answers what follows the <c>endstream</c> too.
    /// </summary>
    private PdfStream ReadPastBuffer(
        IPdfStreamDataProvider provider, PdfDictionary dictionary, DeclaredLength declared, int dataStart, int length)
    {
        var span = _memory.Span;

        if (provider.CheckEndStream(_baseOffset + dataStart + (long)length) is { } afterEndStream)
        {
            _endObjAfterStream = afterEndStream;
            return Finish(dictionary, dataStart, length, span.Length);
        }

        var search = provider.FindEndStream(_objectNumber, _baseOffset + dataStart, span[dataStart..]);
        _endObjAfterStream = search.EndObj;

        if (search.Length is { } found)
        {
            ReportLengthFault(
                PdfDiagnosticCodes.StreamLengthInvalid,
                string.Create(CultureInfo.InvariantCulture, $"The stream declared {length} bytes but ended after {found}."),
                Fault(declared, dataStart, found, found, EndStreamState.Found, null));

            return Finish(dictionary, dataStart, found, span.Length);
        }

        if (!search.Searched)
        {
            ReportLengthFault(
                PdfDiagnosticCodes.StreamLengthInvalid,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The stream declared {length} bytes, and no endstream follows them; the declared length is kept without a search, the document's searches for endstream having read as much of the file as they may."),
                Fault(declared, dataStart, length, null, EndStreamState.NotSearched, null));

            return Finish(dictionary, dataStart, length, span.Length);
        }

        var before = search.NextObject is { } next
            ? string.Create(CultureInfo.InvariantCulture, $"before the next object, at {next}")
            : "before the end of the file";

        ReportLengthFault(
            PdfDiagnosticCodes.StreamLengthInvalid,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The stream declared {length} bytes, and no endstream follows them {before}; the declared length is kept."),
            Fault(
                declared,
                dataStart,
                length,
                null,
                search.NextObject is null ? EndStreamState.MissingBeforeEndOfFile : EndStreamState.MissingBeforeNextObject,
                search.NextObject));

        return Finish(dictionary, dataStart, length, span.Length);
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
                value = _streamData.ResolveLength(named.Id, out presence).Resolve();
            }
            else
            {
                // Parsed alone, a reference that resolves to nothing could not be read: whether the file holds the
                // object, only the reader can say.
                value = named.Resolve();
                presence = value is PdfNull ? ObjectPresence.Unproduced : ObjectPresence.Defined;
            }

            if (presence != ObjectPresence.Defined)
            {
                return new DeclaredLength(
                    presence == ObjectPresence.Missing ? StreamLengthForm.ObjectMissing : StreamLengthForm.ObjectUnreadable,
                    Reference: reference);
            }
        }

        if (value.AsInteger() is not { } integer)
        {
            return new DeclaredLength(StreamLengthForm.NotAnInteger, Reference: reference, Kind: DescribeKind(value));
        }

        return new DeclaredLength(
            integer is >= 0 and <= int.MaxValue ? StreamLengthForm.Integer : StreamLengthForm.OutOfRange, integer, reference);
    }

    /// <summary>Says what a <c>/Length</c> that is not an integer holds: <c>a real number, 61.5</c>, <c>a dictionary</c>.</summary>
    private static string DescribeKind(PdfObject value) => value switch
    {
        PdfReal real => $"a real number, {real}",
        PdfName name => $"a name, /{name.Value}",
        PdfBoolean boolean => boolean.Value ? "a boolean, true" : "a boolean, false",
        PdfString => "a string",
        PdfArray => "an array",
        PdfStream => "a stream",
        PdfDictionary => "a dictionary",
        _ => "null",
    };

    /// <summary>Says what was wrong with a stream's <c>/Length</c>, its <c>endstream</c> found after <paramref name="recovered"/> bytes.</summary>
    private static string DescribeRecovered(DeclaredLength declared, int recovered)
    {
        if (declared.Form == StreamLengthForm.Integer)
        {
            return string.Create(CultureInfo.InvariantCulture, $"The stream declared {declared.Value} bytes but ended after {recovered}.");
        }

        if (declared.Form == StreamLengthForm.Absent)
        {
            return string.Create(CultureInfo.InvariantCulture, $"The stream has no /Length; its data ends after {recovered} bytes.");
        }

        var written = declared.Reference is not { } reference
            ? "is " + Value(declared)
            : declared.Form switch
            {
                StreamLengthForm.ObjectMissing => Names(reference, "which the file lacks"),
                StreamLengthForm.ObjectUnreadable => Names(reference, "which could not be read"),
                _ => Names(reference, "which holds " + Value(declared)),
            };

        return string.Create(CultureInfo.InvariantCulture, $"The stream's /Length {written}; its data ends after {recovered} bytes.");

        static string Names(PdfObjectId reference, string what) =>
            string.Create(CultureInfo.InvariantCulture, $"names object {reference.Number} {reference.Generation}, {what}");

        static string Value(DeclaredLength declared) => declared.Form == StreamLengthForm.NotAnInteger
            ? $"{declared.Kind}, not a non-negative integer"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{declared.Value}, {(declared.Value < 0 ? "a length no stream can have" : "more than the reader can take as a length")}");
    }

    /// <summary>Describes what was found of the stream being read, whose data starts at <paramref name="dataStart"/>.</summary>
    private readonly StreamLengthFault Fault(
        DeclaredLength declared, int dataStart, int taken, int? found, EndStreamState endStream, long? nextObject) => new()
        {
            Form = declared.Form,
            Reference = declared.Reference,
            Declared = declared.Value,
            Kind = declared.Kind,
            Taken = taken,
            Found = found,
            EndStream = endStream,
            NextObject = nextObject,
            DataStart = _baseOffset + dataStart,
        };

    /// <summary>
    /// Keeps what was found of the stream being read, and reports it unless a reading of the same stream the reader
    /// kept already has: a stream parsed again — after the cache let it go, or the index was rebuilt — is reported once.
    /// </summary>
    private void ReportLengthFault(string code, string message, StreamLengthFault fault)
    {
        _lengthFault = fault;

        if (_member is { } member)
        {
            _diagnostics?.Warn(code, member.Locate(message, fault.DataStart), member.DataStart);
        }
        else if (_streamData?.IsLengthFaultReported(fault.DataStart) != true)
        {
            _diagnostics?.Warn(code, message, fault.DataStart);
        }
    }

    private PdfStream Finish(PdfDictionary dictionary, int dataStart, int length, int bufferLength)
    {
        length = Math.Max(length, 0);

        var data = _streamData is not null
            ? _streamData.Create(_baseOffset + dataStart, length)
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
    /// Determines whether the declared length is confirmed by an <c>endstream</c> keyword where the data
    /// ends. When the keyword may lie across the end of the buffer, the file is asked rather than the buffer:
    /// a stream whose data ends a few bytes short of the window's edge is not a truncated stream.
    /// </summary>
    private readonly bool ConfirmsLength(ReadOnlySpan<byte> span, int dataEnd)
    {
        if (IsEndStreamAt(span, dataEnd))
        {
            return true;
        }

        return _streamData is not null &&
               dataEnd + (long)EndStreamLookahead > span.Length &&
               _streamData.IsEndStreamAt(_baseOffset + dataEnd);
    }

    /// <summary>
    /// Determines whether <c>endstream</c> starts at <paramref name="position"/>, after at most
    /// <see cref="MaxEndStreamGap"/> bytes of white space.
    /// </summary>
    internal static bool IsEndStreamAt(ReadOnlySpan<byte> span, long position)
    {
        if (position < 0 || position > span.Length)
        {
            return false;
        }

        var index = (int)position;

        // Conforming files put an end-of-line before "endstream"; some put nothing, some put spaces.
        var limit = Math.Min(index + MaxEndStreamGap, span.Length);
        while (index < limit && PdfCharacters.IsWhitespace(span[index]))
        {
            index++;
        }

        return span[index..].StartsWith(EndStreamKeyword);
    }

    /// <summary>
    /// Determines whether <c>endstream</c> starts at the start of <paramref name="bytes"/>, after at most
    /// <see cref="MaxEndStreamGap"/> bytes of white space, and what follows the keyword: <c>endobj</c>, another token,
    /// or nothing the bytes show — unless they reach the end of the file, which then follows it.
    /// </summary>
    /// <returns>Null when no <c>endstream</c> starts there.</returns>
    internal static EndObjState? ReadStreamEnd(ReadOnlySpan<byte> bytes, bool reachesEndOfFile)
    {
        if (!IsEndStreamAt(bytes, 0))
        {
            return null;
        }

        // Read as the parser reads what follows a stream: "endstreamx" is no keyword, and is what follows the data.
        var lexer = new PdfLexer(bytes);
        var keyword = lexer.Read();
        var next = keyword.IsKeyword(EndStreamKeyword) ? lexer.Read() : keyword;

        if (!reachesEndOfFile && (next.Kind == PdfTokenKind.EndOfInput || next.End >= bytes.Length))
        {
            return EndObjState.Unseen;
        }

        return next.IsKeyword("endobj"u8) ? EndObjState.Present : EndObjState.Absent;
    }

    /// <summary>
    /// Finds where a stream really ends when its declared length is wrong, and returns its true length.
    /// </summary>
    private static int FindEndStream(ReadOnlySpan<byte> span, int dataStart)
    {
        var index = span[dataStart..].IndexOf(EndStreamKeyword);
        if (index < 0)
        {
            return -1;
        }

        var end = dataStart + index;

        // The end-of-line that precedes the keyword belongs to the syntax, not to the data.
        if (end > dataStart && span[end - 1] == (byte)'\n')
        {
            end--;
        }

        if (end > dataStart && span[end - 1] == (byte)'\r')
        {
            end--;
        }

        return end - dataStart;
    }

    /// <summary>Consumes a container whose contents are being discarded, keeping nesting balanced.</summary>
    private void SkipContainer(PdfTokenKind endKind)
    {
        var depth = 1;

        while (depth > 0)
        {
            var token = _lexer.Read();

            switch (token.Kind)
            {
                case PdfTokenKind.EndOfInput:
                    _truncated = true;
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

    private readonly void Report(string code, string message, long position)
    {
        if (_member is { } member)
        {
            _diagnostics?.Warn(code, member.Locate(message, position), member.DataStart);
            return;
        }

        _diagnostics?.Warn(code, message, _baseOffset + position);
    }

    /// <summary>The object stream member a parser reads, and where what it meets is placed.</summary>
    /// <param name="StreamNumber">The object stream's number.</param>
    /// <param name="DataStart">Where the object stream's data starts in the file.</param>
    /// <param name="ObjectNumber">The member's number.</param>
    private readonly record struct ObjectStreamMember(int StreamNumber, long DataStart, int ObjectNumber)
    {
        /// <summary>Adds to <paramref name="message"/> the member, and the byte of decoded data it is about.</summary>
        public string Locate(string message, long position) => string.Create(
            CultureInfo.InvariantCulture,
            $"{message} It was met in object {ObjectNumber}, at byte {position} of object stream {StreamNumber}'s decoded data.");
    }

    /// <summary>A stream's <c>/Length</c>, as <see cref="ReadLength"/> read it.</summary>
    /// <param name="Form">How it was written, or what the object it names holds.</param>
    /// <param name="Value">The integer it gives, in range or not; null when it gives none.</param>
    /// <param name="Reference">The object it names, when it is a reference.</param>
    /// <param name="Kind">What it holds, in words, when that is not an integer.</param>
    private readonly record struct DeclaredLength(
        StreamLengthForm Form, long? Value = null, PdfObjectId? Reference = null, string? Kind = null);
}
