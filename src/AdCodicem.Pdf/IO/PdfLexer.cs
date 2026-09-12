namespace AdCodicem.Pdf.IO;

/// <summary>
/// Turns PDF syntax into tokens, without allocating.
/// </summary>
/// <remarks>
/// The lexer is deliberately forgiving: real files contain stray delimiters, unbalanced strings and
/// truncated tails. It never throws — it reports what it found and lets the parser decide what that means.
/// </remarks>
internal ref struct PdfLexer
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public PdfLexer(ReadOnlySpan<byte> data, int position = 0)
    {
        _data = data;
        _position = position;
    }

    /// <summary>Gets or sets the current offset in the buffer.</summary>
    public int Position
    {
        readonly get => _position;
        set => _position = value;
    }

    /// <summary>Gets the length of the buffer.</summary>
    public readonly int Length => _data.Length;

    /// <summary>Gets a value indicating whether the whole buffer has been consumed.</summary>
    public readonly bool IsAtEnd => _position >= _data.Length;

    /// <summary>Skips white space and comments, leaving the position on the next significant byte.</summary>
    public void SkipWhitespaceAndComments()
    {
        while (_position < _data.Length)
        {
            var current = _data[_position];

            if (PdfCharacters.IsWhitespace(current))
            {
                _position++;
                continue;
            }

            if (current != (byte)'%')
            {
                return;
            }

            // A comment runs to the end of the line; both line endings occur in the wild.
            while (_position < _data.Length && _data[_position] is not ((byte)'\r' or (byte)'\n'))
            {
                _position++;
            }
        }
    }

    /// <summary>Reads the next token and advances past it.</summary>
    public PdfToken Read()
    {
        SkipWhitespaceAndComments();

        if (_position >= _data.Length)
        {
            return new PdfToken(PdfTokenKind.EndOfInput, _position, _position);
        }

        var start = _position;
        var current = _data[_position];

        switch (current)
        {
            case (byte)'[':
                _position++;
                return new PdfToken(PdfTokenKind.ArrayStart, start, _position);

            case (byte)']':
                _position++;
                return new PdfToken(PdfTokenKind.ArrayEnd, start, _position);

            case (byte)'{':
                _position++;
                return new PdfToken(PdfTokenKind.BraceOpen, start, _position);

            case (byte)'}':
                _position++;
                return new PdfToken(PdfTokenKind.BraceClose, start, _position);

            case (byte)'<':
                if (_position + 1 < _data.Length && _data[_position + 1] == (byte)'<')
                {
                    _position += 2;
                    return new PdfToken(PdfTokenKind.DictionaryStart, start, _position);
                }

                return ReadHexString();

            case (byte)'>':
                if (_position + 1 < _data.Length && _data[_position + 1] == (byte)'>')
                {
                    _position += 2;
                    return new PdfToken(PdfTokenKind.DictionaryEnd, start, _position);
                }

                _position++;
                return new PdfToken(PdfTokenKind.Unknown, start, _position);

            case (byte)'(':
                return ReadLiteralString();

            case (byte)'/':
                return ReadName();

            case (byte)')':
                _position++;
                return new PdfToken(PdfTokenKind.Unknown, start, _position);

            default:
                return ReadRegularRun();
        }
    }

    /// <summary>Reads the next token without advancing.</summary>
    public PdfToken Peek()
    {
        var saved = _position;
        var token = Read();
        _position = saved;
        return token;
    }

    /// <summary>
    /// Skips the end-of-line sequence that must follow the <c>stream</c> keyword. A lone carriage return
    /// is not conforming, and is nevertheless produced by real tools.
    /// </summary>
    public void SkipStreamEndOfLine()
    {
        if (_position < _data.Length && _data[_position] == (byte)'\r')
        {
            _position++;
        }

        if (_position < _data.Length && _data[_position] == (byte)'\n')
        {
            _position++;
        }
    }

    private PdfToken ReadName()
    {
        var start = _position;
        _position++; // the solidus

        var valueStart = _position;
        while (_position < _data.Length && PdfCharacters.IsRegular(_data[_position]))
        {
            _position++;
        }

        return new PdfToken(PdfTokenKind.Name, start, _position, _data[valueStart.._position]);
    }

    private PdfToken ReadRegularRun()
    {
        var start = _position;
        while (_position < _data.Length && PdfCharacters.IsRegular(_data[_position]))
        {
            _position++;
        }

        if (_position == start)
        {
            // A delimiter the switch did not claim. Skip it so the caller cannot loop forever.
            _position++;
            return new PdfToken(PdfTokenKind.Unknown, start, _position);
        }

        var text = _data[start.._position];

        if (PdfNumberParser.TryParse(text, out var integer, out var real, out var isReal))
        {
            return isReal
                ? new PdfToken(PdfTokenKind.Real, start, _position, integer, real)
                : new PdfToken(PdfTokenKind.Integer, start, _position, integer, integer);
        }

        return new PdfToken(PdfTokenKind.Keyword, start, _position, text);
    }

    private PdfToken ReadLiteralString()
    {
        var start = _position;
        _position++; // the opening parenthesis

        var contentStart = _position;
        var depth = 1;

        while (_position < _data.Length)
        {
            var current = _data[_position];

            if (current == (byte)'\\')
            {
                // Whatever follows a backslash is part of the string, including a parenthesis.
                _position += 2;
                continue;
            }

            if (current == (byte)'(')
            {
                depth++;
            }
            else if (current == (byte)')')
            {
                depth--;
                if (depth == 0)
                {
                    var content = _data[contentStart.._position];
                    _position++;
                    return new PdfToken(PdfTokenKind.LiteralString, start, _position, content);
                }
            }

            _position++;
        }

        // Unterminated: take what there is rather than lose the object.
        _position = _data.Length;
        return new PdfToken(PdfTokenKind.LiteralString, start, _position, _data[contentStart..]);
    }

    private PdfToken ReadHexString()
    {
        var start = _position;
        _position++; // the opening angle bracket

        var contentStart = _position;
        while (_position < _data.Length && _data[_position] != (byte)'>')
        {
            _position++;
        }

        var content = _data[contentStart.._position];

        if (_position < _data.Length)
        {
            _position++; // the closing angle bracket
        }

        return new PdfToken(PdfTokenKind.HexString, start, _position, content);
    }
}
