namespace AdCodicem.Pdf.IO;

/// <summary>
/// Follows, through the reads of a search for <c>endstream</c> in their order, where the data of a stream that has no
/// <c>endstream</c> ends when its object's own <c>endobj</c> ends it: at the last <c>endobj</c>, a whole keyword, after
/// which only white space and comments run to where the search stops, the end-of-line before it left out (#174).
/// </summary>
/// <remarks>
/// <para>
/// A comment after the keyword is white space, as ISO 32000-1 (7.2.3) makes one outside a stream's data: it runs to the end
/// of its line, and an <c>endobj</c> it holds is no keyword. Each byte is read once, however much the reads repeat of the
/// ones before; the last few bytes read are kept, so that a keyword that runs from one read into the next is found, and
/// what comes before it known.
/// </para>
/// <para>
/// Where the search stops is known only once the read that holds the header stopping it is searched, and that header may
/// start in a read before it, which was followed past it. What lies between is the header's runs of digits and white
/// space, which make no keyword: the place where what follows the last <c>endobj</c> stopped being white space and
/// comments says, alone, whether that run reached the header.
/// </para>
/// </remarks>
internal struct EndObjTail
{
    /// <summary>
    /// The bytes a read keeps of the reads before it: the part of an <c>endobj</c> they hold, five bytes at most, and the two
    /// before it that say whether it starts a token and where its end-of-line starts — seven, rounded up.
    /// </summary>
    private const int Kept = 8;

    private readonly long _dataStart;
    private long _upTo;
    private Mode _mode;
    private long _keywordEnd;
    private long _dataEnd;
    private long _brokenAt;
    private ulong _recent;
    private int _recentCount;

    /// <summary>Initializes a new instance of the <see cref="EndObjTail"/> struct, for data that starts at <paramref name="dataStart"/>.</summary>
    public EndObjTail(long dataStart)
    {
        _dataStart = dataStart;
        _upTo = dataStart;
        _keywordEnd = -1;
        _dataEnd = -1;
        _brokenAt = long.MaxValue;
    }

    private enum Mode : byte
    {
        /// <summary>No <c>endobj</c> that may end the data is being followed.</summary>
        Data,

        /// <summary>An <c>endobj</c> was read whose next byte says whether it is a whole keyword.</summary>
        AfterKeyword,

        /// <summary>White space follows the last whole <c>endobj</c>.</summary>
        Tail,

        /// <summary>A comment after the last whole <c>endobj</c> runs on.</summary>
        Comment,
    }

    /// <summary>
    /// Reads the bytes a read of the search holds, from where the reads before stopped to <paramref name="until"/>.
    /// </summary>
    /// <param name="bytes">The bytes of the read.</param>
    /// <param name="at">Where they start in the file.</param>
    /// <param name="until">Where the search stops in them, or where they end.</param>
    public void Read(ReadOnlySpan<byte> bytes, long at, long until)
    {
        var start = (int)Math.Max(0, _upTo - at);
        var end = (int)(Math.Min(until, at + bytes.Length) - at);

        if (start >= end)
        {
            return;
        }

        var i = _mode == Mode.Data ? Straddling(bytes, at, start, end) : start;

        while (i < end)
        {
            switch (_mode)
            {
                case Mode.AfterKeyword:
                    // "endobjx" is no keyword; anything else that follows it may be the tail, and is read as such.
                    if (PdfCharacters.IsRegular(bytes[i]))
                    {
                        _brokenAt = at + i;
                        _mode = Mode.Data;
                    }
                    else
                    {
                        _mode = Mode.Tail;
                    }

                    break;

                case Mode.Tail:
                    var next = bytes[i..end].IndexOfAnyExcept(PdfCharacters.Whitespace);

                    if (next < 0)
                    {
                        i = end;
                    }
                    else if (bytes[i + next] == (byte)'%')
                    {
                        i += next + 1;
                        _mode = Mode.Comment;
                    }
                    else
                    {
                        i += next;
                        _brokenAt = at + i;
                        _mode = Mode.Data;
                    }

                    break;

                case Mode.Comment:
                    // A comment runs to the end of the line; both line endings occur in the wild.
                    var lineEnd = bytes[i..end].IndexOfAny((byte)'\r', (byte)'\n');
                    i = lineEnd < 0 ? end : i + lineEnd + 1;
                    _mode = lineEnd < 0 ? Mode.Comment : Mode.Tail;
                    break;

                default:
                    var hit = bytes[i..end].IndexOf(StreamEnds.EndObjKeyword);

                    if (hit < 0)
                    {
                        i = end;
                    }
                    else
                    {
                        i = Found(bytes, at, start, at + i + hit) ? i + hit + StreamEnds.EndObjKeyword.Length : i + hit + 1;
                    }

                    break;
            }
        }

        Keep(bytes[start..end]);
        _upTo = at + end;
    }

    /// <summary>
    /// Gives where the data ends when the search stops at <paramref name="stop"/>, at an <c>endobj</c> that only white space
    /// and comments separate from it; -1 when none does, or the reads did not reach it.
    /// </summary>
    public readonly long DataEnd(long stop) =>
        stop <= _upTo && _keywordEnd >= 0 && _keywordEnd <= stop && stop <= _brokenAt ? _dataEnd : -1;

    /// <summary>
    /// Finds an <c>endobj</c> that starts in the bytes the reads before kept and ends in these, and gives where to go on
    /// reading these from.
    /// </summary>
    private int Straddling(ReadOnlySpan<byte> bytes, long at, int start, int end)
    {
        var keyword = StreamEnds.EndObjKeyword;

        // The keyword holds no part of itself again: one way at most of parting it fits.
        for (var held = 1; held < keyword.Length && held <= _recentCount; held++)
        {
            var rest = keyword.Length - held;

            if (start + rest > end || !bytes.Slice(start, rest).SequenceEqual(keyword[held..]))
            {
                continue;
            }

            var matches = true;

            for (var k = 0; k < held && matches; k++)
            {
                matches = Recent(held - 1 - k) == keyword[k];
            }

            if (matches)
            {
                return Found(bytes, at, start, at + start - held) ? start + rest : start;
            }
        }

        return start;
    }

    /// <summary>
    /// Takes the <c>endobj</c> that starts at <paramref name="keyword"/>, unless the byte before it makes it part of a longer
    /// token, as the one the data may end at; whether the byte after it does is read next.
    /// </summary>
    /// <remarks>
    /// A regular character right before it makes it part of the token that character is in; a solidus makes it a name,
    /// <c>/endobj</c>, and a percent sign a comment that starts there, <c>%endobj</c>. Only that byte is read: the bytes
    /// before the keyword are the data's, which no lexing reads, so a comment that starts further back on the line does not
    /// hide it — any other delimiter ends a token or stands alone, wherever a string, a dictionary or a comment it may open
    /// would end.
    /// </remarks>
    /// <returns>Whether the keyword starts a token.</returns>
    private bool Found(ReadOnlySpan<byte> bytes, long at, int start, long keyword)
    {
        if (ByteAt(bytes, at, start, keyword - 1) is var before and >= 0 &&
            (PdfCharacters.IsRegular((byte)before) || before is '/' or '%'))
        {
            return false;
        }

        var dataEnd = keyword;

        if (ByteAt(bytes, at, start, dataEnd - 1) == '\n')
        {
            dataEnd--;
        }

        if (ByteAt(bytes, at, start, dataEnd - 1) == '\r')
        {
            dataEnd--;
        }

        _keywordEnd = keyword + StreamEnds.EndObjKeyword.Length;
        _dataEnd = dataEnd;
        _brokenAt = long.MaxValue;
        _mode = Mode.AfterKeyword;
        return true;
    }

    /// <summary>
    /// Gives the byte at <paramref name="position"/> in the file: in these bytes from <paramref name="start"/> on, among those
    /// kept before it; -1 before the start of the data.
    /// </summary>
    private readonly int ByteAt(ReadOnlySpan<byte> bytes, long at, int start, long position) =>
        position < _dataStart ? -1
            : position >= at + start ? bytes[(int)(position - at)]
            : Recent((int)(at + start - 1 - position));

    /// <summary>Gives a byte kept, <paramref name="back"/> bytes before the last one read.</summary>
    private readonly byte Recent(int back) => (byte)(_recent >> (8 * back));

    /// <summary>Keeps the last bytes read, the latest in the lowest byte.</summary>
    private void Keep(ReadOnlySpan<byte> read)
    {
        foreach (var value in read[Math.Max(0, read.Length - Kept)..])
        {
            _recent = (_recent << 8) | value;
        }

        _recentCount = (int)Math.Min(Kept, _recentCount + (long)read.Length);
    }
}
