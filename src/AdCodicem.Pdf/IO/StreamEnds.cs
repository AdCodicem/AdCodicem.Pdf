namespace AdCodicem.Pdf.IO;

/// <summary>
/// The rules that say where a stream's data ends, read over bytes: whether <c>endstream</c> follows the data, where the
/// first one after its start lies, and whether an <c>endobj</c> ends the object. The parser applies them to the bytes it
/// holds, and the reader to the file past them (<see cref="IPdfStreamDataProvider"/>), so that a stream reads the same
/// wherever its window ends (#174).
/// </summary>
internal static class StreamEnds
{
    /// <summary>The keyword that ends a stream's data.</summary>
    public static ReadOnlySpan<byte> EndStreamKeyword => "endstream"u8;

    /// <summary>The keyword that ends an object.</summary>
    public static ReadOnlySpan<byte> EndObjKeyword => "endobj"u8;

    /// <summary>
    /// Gives where the first byte at or after <paramref name="index"/> lies that is neither white space nor part of a
    /// comment, or the length of <paramref name="bytes"/> when they run to its end. ISO 32000-1 (7.2.3) makes a comment
    /// outside a string or a stream's data count as white space.
    /// </summary>
    public static int SkipBlanks(ReadOnlySpan<byte> bytes, int index)
    {
        var lexer = new PdfLexer(bytes, index);
        lexer.SkipWhitespaceAndComments();
        return lexer.Position;
    }

    /// <summary>
    /// Reads, at the start of <paramref name="bytes"/>, whether <c>endstream</c> follows a stream's data — after white
    /// space and comments, as many as there are (#174) — and what follows the keyword: <c>endobj</c>, another token, or
    /// nothing the bytes show.
    /// </summary>
    /// <param name="bytes">The bytes after the data.</param>
    /// <param name="reachesEndOfFile">Whether the bytes end where the file does, which then follows what they hold.</param>
    public static StreamEnd ReadEnd(ReadOnlySpan<byte> bytes, bool reachesEndOfFile)
    {
        var lexer = new PdfLexer(bytes);
        lexer.SkipWhitespaceAndComments();
        var at = lexer.Position;
        var rest = bytes[at..];

        if (!rest.StartsWith(EndStreamKeyword))
        {
            // White space, a comment or the start of the keyword that runs to the end of the bytes may lead to it past them.
            return !reachesEndOfFile && EndStreamKeyword.StartsWith(rest) ? StreamEnd.Undecided : StreamEnd.None;
        }

        // Read as the parser reads what follows a stream: "endstreamx" is no keyword, and is what follows the data.
        var keyword = lexer.Read();

        if (keyword.End >= bytes.Length && !reachesEndOfFile)
        {
            return StreamEnd.Undecided;
        }

        if (!keyword.IsKeyword(EndStreamKeyword))
        {
            return new StreamEnd(StreamEndKind.EndStream, EndObjState.Absent, keyword.End);
        }

        var next = lexer.Read();

        if (!reachesEndOfFile && (next.Kind == PdfTokenKind.EndOfInput || next.End >= bytes.Length))
        {
            return new StreamEnd(StreamEndKind.EndStream, EndObjState.Unseen, keyword.End);
        }

        return new StreamEnd(
            StreamEndKind.EndStream, next.IsKeyword(EndObjKeyword) ? EndObjState.Present : EndObjState.Absent, keyword.End);
    }

    /// <summary>
    /// Determines whether <c>endobj</c>, a whole keyword, starts at <paramref name="index"/> once white space and comments are
    /// skipped; null when they, or the keyword, run to the end of <paramref name="bytes"/>, which do not end the file.
    /// </summary>
    public static bool? IsEndObjAt(ReadOnlySpan<byte> bytes, int index, bool reachesEndOfFile)
    {
        var at = SkipBlanks(bytes, index);
        var rest = bytes[at..];

        if (rest.Length <= EndObjKeyword.Length)
        {
            if (!reachesEndOfFile && EndObjKeyword.StartsWith(rest))
            {
                return null;
            }

            return rest.SequenceEqual(EndObjKeyword);
        }

        return rest.StartsWith(EndObjKeyword) && !PdfCharacters.IsRegular(rest[EndObjKeyword.Length]);
    }

    /// <summary>
    /// Determines whether the first <paramref name="before"/> bytes of <paramref name="bytes"/> hold an <c>endobj</c>
    /// keyword: a read of a search that may end inside its object.
    /// </summary>
    public static bool HoldsEndObj(ReadOnlySpan<byte> bytes, int before) =>
        bytes[..Math.Clamp(before, 0, bytes.Length)].IndexOf(EndObjKeyword) >= 0;

    /// <summary>
    /// Gives where the data of a stream that has no <c>endstream</c> ends when its object's own <c>endobj</c> ends it: the
    /// <c>endobj</c>, a whole keyword, that only white space separates from the end of <paramref name="stretch"/>, the
    /// end-of-line before it left out; -1 when none does.
    /// </summary>
    /// <param name="stretch">The bytes from the start of the data to where the search for its <c>endstream</c> stopped.</param>
    public static int EndObjBefore(ReadOnlySpan<byte> stretch)
    {
        var end = stretch.LastIndexOfAnyExcept(PdfCharacters.Whitespace) + 1;
        return EndObjEnding(stretch[..end], atDataStart: true);
    }

    /// <summary>
    /// Gives where the data ends when <paramref name="bytes"/> end with a whole <c>endobj</c>: where the keyword starts, the
    /// end-of-line before it left out; -1 when they do not.
    /// </summary>
    /// <param name="bytes">Bytes that end where the white space before the stop starts.</param>
    /// <param name="atDataStart">Whether the bytes start where the data does, before which no byte belongs to it.</param>
    public static int EndObjEnding(ReadOnlySpan<byte> bytes, bool atDataStart)
    {
        if (!bytes.EndsWith(EndObjKeyword))
        {
            return -1;
        }

        var keyword = bytes.Length - EndObjKeyword.Length;

        // The keyword is whole: what precedes it ends a token, or is the start of the data.
        if (keyword > 0 ? PdfCharacters.IsRegular(bytes[keyword - 1]) : !atDataStart)
        {
            return -1;
        }

        var end = keyword;

        if (end > 0 && bytes[end - 1] == (byte)'\n')
        {
            end--;
        }

        if (end > 0 && bytes[end - 1] == (byte)'\r')
        {
            end--;
        }

        return end;
    }

    /// <summary>
    /// Searches one read of a search for <c>endstream</c> — the parser's bytes, or a window of the file — for the first
    /// <c>endstream</c>, and for the first object header before it: returns where the data ends in the file, the
    /// end-of-line before the keyword left out, or -1 when the read holds no <c>endstream</c> or a header comes first,
    /// whose start in the file is then <paramref name="header"/>, -1 otherwise.
    /// </summary>
    /// <remarks>
    /// The overlap between reads puts the end-of-line before a keyword first seen in a read inside it, unless the keyword
    /// starts the data. A cross-reference stream whose <c>/Length</c> the chain could not read is searched as
    /// <paramref name="deferred"/>: its rows may spell a header, and may end in a carriage return, so neither stops nor
    /// trims it (#220).
    /// </remarks>
    /// <param name="text">The bytes read, after what the reads before carry into them.</param>
    /// <param name="endsRange">Whether they end where the search does: at the next object the index places, or the end of the file.</param>
    /// <param name="deferred">Whether the stream's <c>/Length</c> is one the chain could not read, which is no fault.</param>
    /// <param name="header">Where the header that stopped the search starts in the file, or -1.</param>
    public static long SearchStretch(in HeaderText text, bool endsRange, bool deferred, out long header)
    {
        var span = text.Bytes;
        var keyword = span.IndexOf(EndStreamKeyword);
        header = deferred ? -1 : text.FindHeader(keyword >= 0 ? keyword : span.Length, endsRange);

        if (keyword < 0 || header >= 0)
        {
            return -1;
        }

        if (keyword > 0 && span[keyword - 1] == (byte)'\n')
        {
            keyword--;
        }

        if (!deferred && keyword > 0 && span[keyword - 1] == (byte)'\r')
        {
            keyword--;
        }

        return text.PositionOf(keyword);
    }
}
