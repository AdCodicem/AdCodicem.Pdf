import sys
p = sys.argv[1]
s = open(p, encoding='utf-8').read()

old = '''        var index = objPosition - 1;

        if (index < 0 || !PdfCharacters.IsWhitespace(span[index]))
        {
            return false;
        }

        while (index >= 0 && PdfCharacters.IsWhitespace(span[index]))
        {
            index--;
        }

        var generationEnd = index + 1;'''
new = '''        var index = objPosition - 1;

        if (index < 0 || !PdfCharacters.IsWhitespace(span[index]))
        {
            return false;
        }

        index = SkipBackOverBlanksAndComments(span, index);

        var generationEnd = index + 1;'''
assert s.count(old) == 1
s = s.replace(old, new)

old = '''        if (index < 0 || !PdfCharacters.IsWhitespace(span[index]))
        {
            return false;
        }

        while (index >= 0 && PdfCharacters.IsWhitespace(span[index]))
        {
            index--;
        }

        var numberEnd = index + 1;'''
new = '''        if (index < 0 || !PdfCharacters.IsWhitespace(span[index]))
        {
            return false;
        }

        index = SkipBackOverBlanksAndComments(span, index);

        var numberEnd = index + 1;'''
assert s.count(old) == 1
s = s.replace(old, new)

old = '''    private PdfObject? LoadCompressedObject(PdfObjectId id, XRefEntry entry)
    {'''
new = '''    /// <summary>
    /// Steps back from <paramref name="index"/>, on white space, over the white space and the comments between two tokens of
    /// a header, and gives where the token before them ends; below 0 when the span starts first (#261).
    /// </summary>
    /// <remarks>
    /// A comment runs from a <c>%</c> to the end of its line (ISO 32000-1, 7.2.3): only where the white space crossed holds
    /// an end of line can a comment end before it, on the line that end of line closes. That line is taken to end with a
    /// comment when it holds a <c>%</c>, which starts at its first - unless a parenthesis precedes that <c>%</c> on the line,
    /// which may open or close a string that holds it, read backwards: the line is then taken as it is, and no comment is
    /// recognized on it. A line is looked for back to the span's start, which is taken as a line's start.
    /// </remarks>
    private static int SkipBackOverBlanksAndComments(ReadOnlySpan<byte> span, int index)
    {
        while (true)
        {
            var crossedLine = false;

            while (index >= 0 && PdfCharacters.IsWhitespace(span[index]))
            {
                crossedLine |= span[index] is (byte)'\\r' or (byte)'\\n';
                index--;
            }

            if (!crossedLine || index < 0)
            {
                return index;
            }

            var lineStart = span[..(index + 1)].LastIndexOfAny((byte)'\\r', (byte)'\\n') + 1;
            var line = span[lineStart..(index + 1)];
            var percent = line.IndexOf((byte)'%');

            if (percent < 0 || line[..percent].IndexOfAny((byte)'(', (byte)')') >= 0)
            {
                return index;
            }

            index = lineStart + percent - 1;

            if (index < 0 || !PdfCharacters.IsWhitespace(span[index]))
            {
                return index;
            }
        }
    }

    private PdfObject? LoadCompressedObject(PdfObjectId id, XRefEntry entry)
    {'''
assert s.count(old) == 1
s = s.replace(old, new)
open(p, 'w', encoding='utf-8').write(s)
print("A applied")
