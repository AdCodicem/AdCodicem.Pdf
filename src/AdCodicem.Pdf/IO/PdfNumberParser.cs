namespace AdCodicem.Pdf.IO;

/// <summary>Parses PDF numbers from their textual form.</summary>
/// <remarks>
/// Hand-written rather than delegated to <see cref="double"/> parsing, for two reasons: it must accept
/// forms the framework rejects — <c>4.</c>, <c>-.5</c>, and the doubled signs some generators emit — and
/// it must never allocate, being on the hottest path of the reader.
/// </remarks>
internal static class PdfNumberParser
{
    /// <summary>Largest integer magnitude accumulated before switching to floating point.</summary>
    private const long IntegerLimit = long.MaxValue / 10;

    public static bool TryParse(ReadOnlySpan<byte> text, out long integer, out double real, out bool isReal)
    {
        integer = 0;
        real = 0;
        isReal = false;

        if (text.IsEmpty)
        {
            return false;
        }

        var index = 0;
        var negative = false;
        var signSeen = false;

        while (index < text.Length && text[index] is (byte)'+' or (byte)'-')
        {
            if (!signSeen)
            {
                negative = text[index] == (byte)'-';
                signSeen = true;
            }

            index++;
        }

        long whole = 0;
        var digits = 0;
        var overflowed = false;
        double wholeAsReal = 0;

        while (index < text.Length && PdfCharacters.IsDigit(text[index]))
        {
            var digit = text[index] - '0';
            digits++;

            if (!overflowed && whole <= IntegerLimit)
            {
                whole = (whole * 10) + digit;
            }
            else
            {
                if (!overflowed)
                {
                    overflowed = true;
                    wholeAsReal = whole;
                }

                wholeAsReal = (wholeAsReal * 10) + digit;
            }

            index++;
        }

        var fraction = 0d;

        if (index < text.Length && text[index] == (byte)'.')
        {
            isReal = true;
            index++;

            var scale = 0.1d;
            while (index < text.Length && PdfCharacters.IsDigit(text[index]))
            {
                fraction += (text[index] - '0') * scale;
                scale *= 0.1d;
                digits++;
                index++;
            }
        }

        // Anything left over means this was never a number: "12abc" is a keyword, not twelve.
        if (index != text.Length || digits == 0)
        {
            return false;
        }

        if (overflowed)
        {
            isReal = true;
            real = negative ? -(wholeAsReal + fraction) : wholeAsReal + fraction;
            integer = negative ? long.MinValue : long.MaxValue;
            return true;
        }

        if (isReal)
        {
            real = negative ? -(whole + fraction) : whole + fraction;
            integer = negative ? -whole : whole;
            return true;
        }

        integer = negative ? -whole : whole;
        real = integer;
        return true;
    }
}
