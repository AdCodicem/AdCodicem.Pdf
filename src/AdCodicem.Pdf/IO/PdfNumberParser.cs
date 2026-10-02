using System.Globalization;

namespace AdCodicem.Pdf.IO;

/// <summary>Parses PDF numbers from their textual form.</summary>
/// <remarks>
/// <para>
/// A real reads as the double nearest to the decimal written, as the framework's own parse gives it. The parser
/// does not hand every number to the framework, for two reasons: it must accept the doubled signs some generators
/// emit — <c>--0.7071</c>, where the first sign decides — the one form the framework rejects; and it must stay cheap,
/// being on the hottest path of the reader. So it checks the shape itself, reads an integer within a
/// <see cref="long"/> exactly, and divides a mantissa by a power of ten when both are exact doubles — the framework's
/// own first step, whose one rounding is the division's. Everything else, past 2^53 or past 22 decimals, goes to the
/// framework. Nothing allocates.
/// </para>
/// <para>
/// An integer past a <see cref="long"/> reads as a real, never wrapped; a value too large for a <see cref="double"/> —
/// one that rounds past <see cref="double.MaxValue"/> — reads as an infinity, which the caller reports
/// (<see cref="PdfToken.Real"/>).
/// </para>
/// </remarks>
internal static class PdfNumberParser
{
    /// <summary>The largest mantissa below which every further digit still fits a <see cref="ulong"/>.</summary>
    private const ulong LastSafeTenth = ulong.MaxValue / 10;

    /// <summary>The largest digit that may follow <see cref="LastSafeTenth"/> and still fit a <see cref="ulong"/>.</summary>
    private const ulong LastSafeDigit = ulong.MaxValue % 10;

    /// <summary>The largest magnitude a positive integer may have and still be a <see cref="long"/>.</summary>
    private const ulong LargestPositive = long.MaxValue;

    /// <summary>The magnitude of <see cref="long.MinValue"/>, the one negative integer with no positive counterpart.</summary>
    private const ulong LargestNegative = 1UL << 63;

    /// <summary>The largest mantissa up to which every integer is an exact double.</summary>
    private const ulong ExactMantissa = 1UL << 53;

    /// <summary>The powers of ten a double holds exactly: 10^0 to 10^22.</summary>
    private static ReadOnlySpan<double> ExactPowersOfTen =>
    [
        1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11,
        1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
    ];

    /// <summary>Parses a number: an optional run of signs, then digits with at most one period among them.</summary>
    /// <param name="text">The number's bytes, and nothing else.</param>
    /// <param name="integer">The value of an integer within a <see cref="long"/>; otherwise 0.</param>
    /// <param name="real">
    /// The value as a double: the integer itself, or the double nearest to the decimal written — an infinity when the
    /// decimal rounds past <see cref="double.MaxValue"/>.
    /// </param>
    /// <param name="isReal">Whether the number is a real: written with a period, or an integer past a <see cref="long"/>.</param>
    /// <returns>Whether the text is a number.</returns>
    public static bool TryParse(ReadOnlySpan<byte> text, out long integer, out double real, out bool isReal)
    {
        integer = 0;
        real = 0;
        isReal = false;

        var index = 0;
        var negative = false;

        if (!text.IsEmpty && text[0] is (byte)'+' or (byte)'-')
        {
            negative = text[0] == (byte)'-';
            index = 1;

            while (index < text.Length && text[index] is (byte)'+' or (byte)'-')
            {
                index++;
            }
        }

        var unsigned = index;
        ulong mantissa = 0;
        var fits = true;
        var digits = 0;
        var fractionDigits = 0;
        var period = false;

        for (; index < text.Length; index++)
        {
            var current = text[index];

            if (PdfCharacters.IsDigit(current))
            {
                var digit = (ulong)(current - '0');

                if (fits && (mantissa < LastSafeTenth || (mantissa == LastSafeTenth && digit <= LastSafeDigit)))
                {
                    mantissa = (mantissa * 10) + digit;
                }
                else
                {
                    fits = false;
                }

                digits++;

                if (period)
                {
                    fractionDigits++;
                }
            }
            else if (current == (byte)'.' && !period)
            {
                period = true;
            }
            else
            {
                // Anything else means this was never a number: "12abc" is a keyword, not twelve.
                return false;
            }
        }

        if (digits == 0)
        {
            return false;
        }

        if (!period && fits && mantissa <= (negative ? LargestNegative : LargestPositive))
        {
            integer = negative ? unchecked((long)(0 - mantissa)) : (long)mantissa;
            real = integer;
            return true;
        }

        double magnitude;

        if (fits && mantissa <= ExactMantissa && fractionDigits < ExactPowersOfTen.Length)
        {
            // Both operands are exact doubles, so the one rounding is the division's: the nearest double.
            magnitude = (long)mantissa / ExactPowersOfTen[fractionDigits];
        }
        else
        {
            // The shape is checked: digits with at most one period, and at least one digit, every one of which the
            // framework reads — so it cannot refuse, and its result is ignored. It returns an infinity, not false,
            // past double.MaxValue.
            _ = double.TryParse(text[unsigned..], NumberStyles.AllowDecimalPoint, NumberFormatInfo.InvariantInfo, out magnitude);
        }

        isReal = true;
        real = negative ? -magnitude : magnitude;
        return true;
    }
}
