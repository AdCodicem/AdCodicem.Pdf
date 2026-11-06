namespace AdCodicem.Pdf.IO;

/// <summary>
/// The first <c>endstream</c> a search for one found inside a comment, on a line whose tokens before the comment may begin
/// an object header — a run of digits last, or none —, while what follows that line is read to tell whether such a header
/// ends past the keyword (#261): the data ends at the keyword, unless a header that starts before it does.
/// </summary>
/// <remarks>
/// <para>
/// A header's number and its <c>obj</c> may have comment lines between them, as a load and the scan that rebuilds an index
/// read them, and a comment may hold <c>endstream</c>. Taken where it lies, it would end the data inside the header: the
/// data would run into it, and the object's own <c>endobj</c> before the header would be judged missing, while a search
/// whose next object is that header — the index placing it — stops there. Between the keyword and such a header's
/// <c>obj</c> there can only be the rest of the comment, white space and comments, and the header's generation where the
/// keyword's line does not hold it: the first token past that line outside a comment is <c>obj</c>, or a run of digits
/// that <c>obj</c> follows. Anything else — a delimiter, another keyword, a second run of digits — ends every header that
/// starts before the keyword, which then ends the data; and an <c>obj</c> where one may end is decided by the rule for
/// object headers (<see cref="HeaderText.HeaderAt"/>), which reads back across the keyword's line as across any line that
/// holds a comment. A header that rule takes there starts before the keyword: its tokens past the keyword are those two
/// at most, and its number lies before them.
/// </para>
/// <para>
/// Those tokens are read through the reads the search parts the data into, each from the furthest the reads before it
/// reached: runs of white space, of a comment and of regular characters are passed at once, a few fields carry where the
/// reading stands, whatever the lines hold, and whether an <c>obj</c> would end a header is read where the token's first
/// byte is, from the bytes before it, rather than in the read that ends the token, which may no longer hold them. So the
/// decision does not depend on where the reads part the data, however short a source cuts them: a read that gives fewer
/// bytes than asked (#52) may end before the one before it, which reads nothing again, and makes the next start a byte
/// past its own start, which may be past the token's. What is read is bounded by what the search would read without the
/// keyword, up to its next object or the end of the file, each byte once; a search that reads no further — past its
/// budget, or at a source with nothing more to give — takes the keyword. None of this is a guard (ADR 34): it reads only
/// a stream whose <c>endstream</c> does not follow its declared length, and stops where the search does.
/// </para>
/// </remarks>
internal struct PendingEndStream
{
    /// <summary>The keyword that ends an object header.</summary>
    private static ReadOnlySpan<byte> ObjKeyword => "obj"u8;

    private Mode _mode;
    private bool _numbered;
    private bool _digits;
    private int _matched;
    private long _header;
    private long _readTo;

    /// <summary>Where the reading of what follows the keyword's line stands.</summary>
    private enum Mode : byte
    {
        /// <summary>No keyword waits: the search found none in a comment, or what follows it decided.</summary>
        None,

        /// <summary>In a comment, the keyword's or a later one, which runs to the end of its line.</summary>
        Comment,

        /// <summary>Between tokens.</summary>
        Space,

        /// <summary>In a run of regular characters: a run of digits, <c>obj</c>, or any other token.</summary>
        Token,
    }

    /// <summary>Gets a value indicating whether a keyword waits for what follows its line to decide.</summary>
    public readonly bool IsPending => _mode != Mode.None;

    /// <summary>Gets where the data ends at the keyword, the end-of-line before it left out, as the search found it.</summary>
    public long DataEnd { get; private set; }

    /// <summary>
    /// Takes the keyword, found in a comment, which ends the data at <paramref name="dataEnd"/> unless a header that starts
    /// before it ends past it, and reads on from <paramref name="after"/>, where the keyword ends, its comment's rest first.
    /// Called once in a search, on the state it starts with: a search that decided ends there.
    /// </summary>
    public void Start(long dataEnd, long after)
    {
        DataEnd = dataEnd;
        _readTo = after;
        _mode = Mode.Comment;
    }

    /// <summary>
    /// Reads on through the read <paramref name="text"/> holds, from the furthest the reads before reached, and gives where
    /// the data ends once that is decided: at the keyword, or, where a header that starts before it ends past it, -1, the
    /// header's start in <paramref name="header"/>. -1 with no header while the read ends before a decision, which the next
    /// decides.
    /// </summary>
    /// <param name="text">The read.</param>
    /// <param name="endsRange">Whether the read ends where the search does, which ends any token it cuts and decides.</param>
    /// <param name="header">Where the header that ends past the keyword starts in the file, or -1.</param>
    public long Decide(in HeaderText text, bool endsRange, out long header)
    {
        header = -1;
        var bytes = text.Bytes;
        var at = text.PositionOf(0);

        // A read starts at or before where the one before it ended, which the reading reached: what the reads repeat is
        // not read again. One a source cut short may end before where the reads before it reached, and reads nothing (#52).
        var index = (int)(_readTo - at);

        while (index < bytes.Length)
        {
            switch (_mode)
            {
                case Mode.Comment:
                    var lineEnd = bytes[index..].IndexOfAny((byte)'\r', (byte)'\n');
                    index = lineEnd < 0 ? bytes.Length : index + lineEnd + 1;
                    _mode = lineEnd < 0 ? Mode.Comment : Mode.Space;
                    break;

                case Mode.Space:
                    var next = bytes[index..].IndexOfAnyExcept(PdfCharacters.Whitespace);

                    if (next < 0)
                    {
                        index = bytes.Length;
                    }
                    else if (bytes[index + next] == (byte)'%')
                    {
                        index += next + 1;
                        _mode = Mode.Comment;
                    }
                    else if (PdfCharacters.IsRegular(bytes[index + next]))
                    {
                        index += next;

                        // Whether an obj would end a header is read at its first byte, from the bytes before it: the read
                        // that ends the token may start past them, after a read a source cut short (#52).
                        _header = bytes[index] == ObjKeyword[0] ? text.HeaderAt(index) : -1;
                        _digits = true;
                        _matched = 0;
                        _mode = Mode.Token;
                    }
                    else
                    {
                        // A delimiter starts no token of a header.
                        return Decided();
                    }

                    break;

                default:
                    // The token's regular characters, each read once; the byte that ends it is white space or a delimiter.
                    while (index < bytes.Length && PdfCharacters.IsRegular(bytes[index]))
                    {
                        Take(bytes[index]);
                        index++;
                    }

                    if (index < bytes.Length && TokenEnds(out var dataEnd, out header))
                    {
                        return dataEnd;
                    }

                    break;
            }
        }

        // A read a source cut short leaves the reading where the reads before reached, rather than taking it back.
        _readTo = Math.Max(_readTo, at + bytes.Length);

        if (!endsRange)
        {
            return -1;
        }

        // The end of the search ends the token it cuts, and no header follows past it.
        return _mode == Mode.Token && TokenEnds(out var last, out header) ? last : Decided();
    }

    /// <summary>
    /// Takes the token just ended: a first run of digits, a header's generation, lets the reading go on, and says so by
    /// returning false; <c>obj</c> or anything else decides, and gives where the data ends in <paramref name="dataEnd"/>,
    /// or -1 with the start of the header the rule takes in <paramref name="header"/>.
    /// </summary>
    private bool TokenEnds(out long dataEnd, out long header)
    {
        header = -1;

        if (_digits && !_numbered)
        {
            _numbered = true;
            _mode = Mode.Space;
            dataEnd = -1;
            return false;
        }

        // An obj three bytes long, whose header was read at its first byte.
        header = _matched == ObjKeyword.Length ? _header : -1;
        dataEnd = header >= 0 ? -1 : DataEnd;
        _mode = Mode.None;
        return true;
    }

    /// <summary>Takes the next regular character of the token: whether it keeps the token a run of digits, or <c>obj</c>.</summary>
    private void Take(byte value)
    {
        _digits &= PdfCharacters.IsDigit(value);
        _matched = _matched >= 0 && _matched < ObjKeyword.Length && ObjKeyword[_matched] == value ? _matched + 1 : -1;
    }

    /// <summary>Ends the wait: nothing past the keyword's line ends a header that starts before it, and the keyword ends the data.</summary>
    private long Decided()
    {
        _mode = Mode.None;
        return DataEnd;
    }
}
