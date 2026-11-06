using System.Buffers;

namespace AdCodicem.Pdf.IO;

/// <summary>
/// Lexes one line of a stream's data from its start, as the lexer a load reads with reads it, through the reads a search for
/// <c>endstream</c> splits the data into, and keeps the line's last two tokens: those <see cref="HeaderText"/> takes for an
/// object header's numbers where the line holds a <c>%</c>, as the scan that rebuilds an index takes them (#261).
/// </summary>
/// <remarks>
/// The tokens are <see cref="PdfLexer"/>'s, read from the line's start, and told apart only as far as a header asks: a run
/// of digits alone, with its value — or <see cref="HeaderText.Beyond"/>, past any object number and generation, for a larger
/// one —, and any other token. A <c>%</c> between tokens starts a comment, which runs to the end of the line; a string runs
/// to the parenthesis that closes it, or to the end of the line, and so does a hexadecimal string to its <c>&gt;</c>. What
/// a read leaves undone — a token, a string or a comment it cuts, a <c>&lt;</c> or a <c>&gt;</c> the next byte may double —
/// is carried into the next in a few fields, whatever the line's length: each byte is read once, and what a token or a
/// string runs on through, or the white space between tokens, is passed at once rather than a byte at a time, so that a
/// line of one long token costs what searching it costs.
/// </remarks>
internal struct LineTokens
{
    /// <summary>The bytes that end a run of regular characters or a name: white space and delimiters.</summary>
    private static readonly SearchValues<byte> RunEnds = SearchValues.Create("\0\t\n\f\r ()<>[]{}/%"u8);

    /// <summary>The bytes of a literal string that matter: those that nest it, close it or escape the next.</summary>
    private static readonly SearchValues<byte> StringMarks = SearchValues.Create("()\\"u8);

    private Mode _mode;
    private int _depth;
    private bool _escaped;
    private bool _digits;
    private long _value;
    private long _start;

    /// <summary>What a token of the line is, as far as an object header asks.</summary>
    internal enum Kind : byte
    {
        /// <summary>No token: the line holds fewer.</summary>
        None,

        /// <summary>A run of digits alone, no sign: a number a header may hold.</summary>
        Digits,

        /// <summary>Any other token.</summary>
        Other,
    }

    /// <summary>Where the lexer stands between two bytes of the line.</summary>
    private enum Mode : byte
    {
        /// <summary>Between tokens.</summary>
        Space,

        /// <summary>In a run of regular characters: a number or a keyword.</summary>
        Regular,

        /// <summary>In a name, past its solidus.</summary>
        Name,

        /// <summary>In a literal string.</summary>
        Literal,

        /// <summary>In a hexadecimal string.</summary>
        Hex,

        /// <summary>Past a <c>&lt;</c>, which the next byte makes a dictionary's start or a hexadecimal string's.</summary>
        Less,

        /// <summary>Past a <c>&gt;</c>, which the next byte may make a dictionary's end.</summary>
        Greater,

        /// <summary>In a comment, which runs to the end of the line.</summary>
        Comment,
    }

    /// <summary>Gets the line's last token so far.</summary>
    public Token Last { get; private set; }

    /// <summary>Gets the token before <see cref="Last"/>.</summary>
    public Token Before { get; private set; }

    /// <summary>Gets a value indicating whether the line holds a <c>%</c> so far, in a comment or in a string.</summary>
    public bool HoldsPercent { get; private set; }

    /// <summary>Gets a value indicating whether the bytes read so far end inside a comment, which runs to the end of the line.</summary>
    public readonly bool InComment => _mode == Mode.Comment;

    /// <summary>Lexes <paramref name="bytes"/>, which start at <paramref name="at"/> in the file, as the line's next bytes.</summary>
    public void Read(ReadOnlySpan<byte> bytes, long at)
    {
        HoldsPercent |= bytes.Contains((byte)'%');
        var index = 0;

        while (index < bytes.Length && _mode != Mode.Comment)
        {
            index += Pass(bytes[index..]);

            if (index < bytes.Length)
            {
                Step(bytes[index], at + index);
                index++;
            }
        }
    }

    /// <summary>Ends the line: a token its end cuts — a run, a name, a string left open — is its last.</summary>
    public void Finish()
    {
        switch (_mode)
        {
            case Mode.Regular:
                Complete(_digits ? Kind.Digits : Kind.Other);
                break;

            case Mode.Name or Mode.Literal or Mode.Hex or Mode.Less or Mode.Greater:
                Complete(Kind.Other);
                break;
        }
    }

    /// <summary>
    /// Gives how many of the bytes <paramref name="rest"/> starts with the lexer reads past where it stands, none of which
    /// ends what it is in: white space between tokens; the rest of a name, of a keyword or of a hexadecimal string; the bytes
    /// of a literal string that neither nest, close nor escape; the digits of a number, which its value takes in.
    /// </summary>
    private int Pass(ReadOnlySpan<byte> rest)
    {
        int length;

        switch (_mode)
        {
            case Mode.Space:
                length = rest.IndexOfAnyExcept(PdfCharacters.Whitespace);
                break;

            case Mode.Regular when _digits:
                length = rest.IndexOfAnyExceptInRange((byte)'0', (byte)'9');
                TakeDigits(length < 0 ? rest : rest[..length]);
                break;

            case Mode.Regular or Mode.Name:
                length = rest.IndexOfAny(RunEnds);
                break;

            case Mode.Literal when !_escaped:
                length = rest.IndexOfAny(StringMarks);
                break;

            case Mode.Hex:
                length = rest.IndexOf((byte)'>');
                break;

            default:
                return 0;
        }

        return length < 0 ? rest.Length : length;
    }

    /// <summary>
    /// Takes <paramref name="digits"/> into the value of the number they go on: past the zeros that lead it, a number reaches
    /// <see cref="HeaderText.Beyond"/> within ten more digits and stays there, which are all its value reads.
    /// </summary>
    private void TakeDigits(ReadOnlySpan<byte> digits)
    {
        if (_value == 0)
        {
            var first = digits.IndexOfAnyExcept((byte)'0');
            digits = first < 0 ? [] : digits[first..];
        }

        foreach (var digit in digits)
        {
            if (_value == HeaderText.Beyond)
            {
                return;
            }

            _value = Math.Min((_value * 10) + (digit - '0'), HeaderText.Beyond);
        }
    }

    /// <summary>
    /// Reads the byte at <paramref name="position"/>, one that <see cref="Pass"/> does not pass: it ends a token or starts
    /// one, nests or closes a string, escapes the byte after it, or decides a <c>&lt;</c> or a <c>&gt;</c>.
    /// </summary>
    private void Step(byte value, long position)
    {
        switch (_mode)
        {
            case Mode.Regular when PdfCharacters.IsRegular(value):
                // A regular character that is no digit: the run is a keyword, read on to its end.
                _digits = false;
                return;

            case Mode.Regular:
                Complete(_digits ? Kind.Digits : Kind.Other);
                break;

            case Mode.Name:
                Complete(Kind.Other);
                break;

            case Mode.Literal:
                // Whatever follows a backslash is the string's, a parenthesis included.
                if (_escaped)
                {
                    _escaped = false;
                }
                else if (value == (byte)'\\')
                {
                    _escaped = true;
                }
                else if (value == (byte)'(')
                {
                    _depth++;
                }
                else if (value == (byte)')' && --_depth == 0)
                {
                    Complete(Kind.Other);
                }

                return;

            case Mode.Less when value == (byte)'<':
            case Mode.Greater when value == (byte)'>':
                Complete(Kind.Other);
                return;

            case Mode.Greater:
                Complete(Kind.Other);
                break;

            case Mode.Less or Mode.Hex:
                _mode = Mode.Hex;

                if (value == (byte)'>')
                {
                    Complete(Kind.Other);
                }

                return;
        }

        // Between tokens: white space, the start of a comment, or the start of a token.
        if (PdfCharacters.IsWhitespace(value))
        {
            return;
        }

        _start = position;

        switch (value)
        {
            case (byte)'%':
                _mode = Mode.Comment;
                break;

            case (byte)'(':
                _mode = Mode.Literal;
                _depth = 1;
                break;

            case (byte)'<':
                _mode = Mode.Less;
                break;

            case (byte)'/':
                _mode = Mode.Name;
                break;

            case (byte)'>':
                _mode = Mode.Greater;
                break;

            case (byte)')' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}':
                Complete(Kind.Other);
                break;

            default:
                _mode = Mode.Regular;
                _digits = PdfCharacters.IsDigit(value);
                _value = _digits ? value - '0' : 0;
                break;
        }
    }

    /// <summary>Takes the token that started at <see cref="_start"/> as the line's last, of <paramref name="kind"/>.</summary>
    private void Complete(Kind kind)
    {
        Before = Last;
        Last = new Token(kind, kind == Kind.Digits ? _value : 0, _start);
        _mode = Mode.Space;
    }

    /// <summary>A token of the line: what it is, the value of a run of digits, and where it starts in the file.</summary>
    /// <param name="Kind">What the token is.</param>
    /// <param name="Value">The value of a run of digits, at most <see cref="HeaderText.Beyond"/>; 0 otherwise.</param>
    /// <param name="Start">Where the token starts in the file.</param>
    internal readonly record struct Token(Kind Kind, long Value, long Start);
}
