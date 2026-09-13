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
    private const int MaxDepth = 128;

    private static ReadOnlySpan<byte> EndStreamKeyword => "endstream"u8;

    private readonly ReadOnlyMemory<byte> _memory;
    private readonly IPdfObjectSource? _source;
    private readonly PdfDiagnostics? _diagnostics;
    private readonly IPdfStreamDataProvider? _streamData;
    private readonly long _baseOffset;
    private PdfLexer _lexer;
    private bool _truncated;

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

    /// <summary>Gets a value indicating whether the input ended in the middle of an object.</summary>
    public readonly bool IsTruncated => _truncated;

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
            _lexer.Position = start;
            return false;
        }

        id = new PdfObjectId((int)number.Integer, (int)generation.Integer);
        value = ParseObject();

        var afterValue = _lexer.Position;
        if (!_lexer.Read().IsKeyword("endobj"u8))
        {
            _lexer.Position = afterValue;
        }

        return true;
    }

    private PdfObject ParseValue(PdfToken token, int depth)
    {
        switch (token.Kind)
        {
            case PdfTokenKind.EndOfInput:
                _truncated = true;
                Report(PdfDiagnosticCodes.SyntaxTruncatedObject, "The file ended in the middle of an object.", token.Start);
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
        // "12 0 R" is only distinguishable from "12" by looking two tokens ahead.
        var saved = _lexer.Position;
        var second = _lexer.Read();

        if (second.Kind == PdfTokenKind.Integer && _lexer.Read().IsKeyword("R"u8) &&
            token.Integer is > 0 and <= int.MaxValue &&
            second.Integer is >= 0 and <= ushort.MaxValue)
        {
            return new PdfReference(new PdfObjectId((int)token.Integer, (int)second.Integer), _source);
        }

        _lexer.Position = saved;
        return PdfInteger.Create(token.Integer);
    }

    private PdfObject ParseArray(int depth)
    {
        if (depth >= MaxDepth)
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
        if (depth >= MaxDepth)
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
        _lexer.SkipStreamEndOfLine();

        var span = _memory.Span;
        var dataStart = _lexer.Position;
        var declared = dictionary.GetInteger(PdfName.Length);
        var length = declared is >= 0 and <= int.MaxValue ? (int)declared.Value : -1;

        var beyondBuffer = length >= 0 && dataStart + (long)length > span.Length;

        if (beyondBuffer && _streamData is not null)
        {
            // The data lives past the window the reader gave us; the declared length is all we have, and
            // the reader will notice if it is wrong when the bytes are eventually read.
            return Finish(dictionary, dataStart, length, span.Length);
        }

        if (length < 0 || beyondBuffer || !IsEndStreamAt(span, dataStart + length))
        {
            var recovered = FindEndStream(span, dataStart);

            if (recovered < 0)
            {
                _truncated = true;
                Report(PdfDiagnosticCodes.StreamTruncated, "A stream ran past the end of the file.", dataStart);
                return Finish(dictionary, dataStart, span.Length - dataStart, span.Length);
            }

            if (length != recovered)
            {
                Report(
                    PdfDiagnosticCodes.StreamLengthInvalid,
                    $"The stream declared {length} bytes but ended after {recovered}.",
                    dataStart);
            }

            length = recovered;
        }

        return Finish(dictionary, dataStart, length, span.Length);
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

    private static bool IsEndStreamAt(ReadOnlySpan<byte> span, long position)
    {
        if (position < 0 || position > span.Length)
        {
            return false;
        }

        var index = (int)position;

        // Conforming files put an end-of-line before "endstream"; some put nothing, some put spaces.
        var limit = Math.Min(index + 4, span.Length);
        while (index < limit && PdfCharacters.IsWhitespace(span[index]))
        {
            index++;
        }

        return span[index..].StartsWith(EndStreamKeyword);
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

    private readonly void Report(string code, string message, long position) =>
        _diagnostics?.Warn(code, message, _baseOffset + position);
}
