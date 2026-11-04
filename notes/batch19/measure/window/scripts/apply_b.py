import sys, re
p = sys.argv[1]
s = open(p, encoding='utf-8').read()

start = s.index('    private static bool TryReadHeaderBackwards(')
end = s.index('    private PdfObject? LoadCompressedObject(PdfObjectId id, XRefEntry entry)')

new = '''    private static bool TryReadHeaderBackwards(
        ReadOnlySpan<byte> span, int objPosition, out int number, out int generation, out int headerStart)
    {
        number = 0;
        generation = 0;
        headerStart = 0;

        var index = objPosition - 1;

        if (index < 0 || !PdfCharacters.IsWhitespace(span[index]))
        {
            return false;
        }

        // The header's two numbers, the generation first, each read backwards as a run of digits, or forwards with the lexer
        // from the start of a line that holds a comment (#261).
        long generationValue = 0;
        long numberValue = 0;
        var numberStart = 0;
        var taken = 0;

        while (taken < 2)
        {
            var crossedLine = false;

            while (index >= 0 && PdfCharacters.IsWhitespace(span[index]))
            {
                crossedLine |= span[index] is (byte)'\\r' or (byte)'\\n';
                index--;
            }

            if (index < 0)
            {
                return false;
            }

            if (crossedLine)
            {
                var lineStart = span[..(index + 1)].LastIndexOfAny((byte)'\\r', (byte)'\\n') + 1;

                if (span[lineStart..(index + 1)].IndexOf((byte)'%') >= 0)
                {
                    // A line that holds a % may end with a comment, which starts where the lexer, reading the line from its
                    // start, meets a % outside a string: its tokens are what precedes the comment, the nearest taken first.
                    if (!TakeLineTokens(span, lineStart, index + 1, ref taken, ref generationValue, ref numberValue, ref numberStart))
                    {
                        return false;
                    }

                    if (taken == 2)
                    {
                        break;
                    }

                    index = lineStart - 1;

                    if (index < 0)
                    {
                        return false;
                    }

                    continue;
                }
            }

            var runEnd = index + 1;
            while (index >= 0 && PdfCharacters.IsDigit(span[index]))
            {
                index--;
            }

            var runStart = index + 1;
            if (runStart == runEnd)
            {
                return false;
            }

            // Each run is judged by its value, however many zeros lead it, as the parser judges it: a header the parser would
            // refuse, found here, would be an object no read can serve. A run of digits always parses, as an integer within a
            // long or as a real past one.
            _ = PdfNumberParser.TryParse(span[runStart..runEnd], out var parsed, out _, out var pastLong);

            if (pastLong)
            {
                return false;
            }

            if (taken == 0)
            {
                generationValue = parsed;
            }
            else
            {
                numberValue = parsed;
                numberStart = runStart;
            }

            taken++;

            if (taken == 1 && (index < 0 || !PdfCharacters.IsWhitespace(span[index])))
            {
                return false;
            }
        }

        if (numberValue is <= 0 or > PdfObjectId.MaxNumber || generationValue is < 0 or > PdfObjectId.MaxGeneration)
        {
            return false;
        }

        number = (int)numberValue;
        generation = (int)generationValue;
        headerStart = numberStart;
        return true;
    }

    /// <summary>
    /// Lexes the line <c>[lineStart, lineEnd)</c> of <paramref name="span"/> forward, from its start, and takes its last tokens
    /// as the header's numbers still wanting, the nearest first: each must be an integer. False where one is not, or where a
    /// string runs past the line's end, which would hold what follows.
    /// </summary>
    private static bool TakeLineTokens(
        ReadOnlySpan<byte> span, int lineStart, int lineEnd, ref int taken, ref long generationValue, ref long numberValue, ref int numberStart)
    {
        var lexer = new PdfLexer(span[..lineEnd], lineStart);
        var lastKind = PdfTokenKind.EndOfInput;
        var lastValue = 0L;
        var lastStart = 0;
        var beforeKind = PdfTokenKind.EndOfInput;
        var beforeValue = 0L;
        var beforeStart = 0;
        var count = 0;

        while (true)
        {
            var token = lexer.Read();

            if (token.Kind == PdfTokenKind.EndOfInput)
            {
                break;
            }

            if ((token.Kind == PdfTokenKind.LiteralString && token.End == lineEnd && span[lineEnd - 1] != (byte)')') ||
                (token.Kind == PdfTokenKind.HexString && token.End == lineEnd && span[lineEnd - 1] != (byte)'>'))
            {
                return false;
            }

            beforeKind = lastKind;
            beforeValue = lastValue;
            beforeStart = lastStart;
            lastKind = token.Kind;
            lastValue = token.Integer;
            lastStart = token.Start;
            count++;
        }

        if (count > 0)
        {
            if (lastKind != PdfTokenKind.Integer)
            {
                return false;
            }

            if (taken == 0)
            {
                generationValue = lastValue;
            }
            else
            {
                numberValue = lastValue;
                numberStart = lastStart;
            }

            taken++;
        }

        if (count > 1 && taken < 2)
        {
            if (beforeKind != PdfTokenKind.Integer)
            {
                return false;
            }

            numberValue = beforeValue;
            numberStart = beforeStart;
            taken++;
        }

        return true;
    }

'''
s = s[:start] + new + s[end:]
open(p, 'w', encoding='utf-8').write(s)
print("B applied")
