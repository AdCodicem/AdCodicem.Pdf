using System.Globalization;

namespace AdCodicem.Pdf.Objects;

/// <summary>Represents a PDF real number.</summary>
/// <remarks>
/// PDF reals admit no exponent notation, which is why formatting for output never goes through the
/// default <see cref="double"/> conversion. A real read from a file is the double nearest to the decimal it wrote, and
/// is finite: a number beyond what a double holds reads as null, reported as
/// <see cref="Diagnostics.PdfDiagnosticCodes.SyntaxNumberOutOfRange"/>.
/// </remarks>
public sealed class PdfReal : PdfObject
{
    /// <summary>The real zero.</summary>
    public static readonly PdfReal Zero = new(0d);

    /// <summary>Initializes a new real with the given value.</summary>
    public PdfReal(double value) => Value = value;

    /// <summary>Gets the value.</summary>
    public double Value { get; }

    /// <summary>
    /// Returns the real as PDF writes one: the shortest decimal that reads back to <see cref="Value"/>, with no exponent
    /// and always with a decimal point — <c>0.0000001</c> for 1E-07, <c>4.0</c> for four, which tells it from the
    /// integer.
    /// </summary>
    /// <remarks>
    /// Nothing is rounded: a value read from a file reads as the decimal the file wrote, up to the 17 significant digits a
    /// double holds, and a computed one shows its binary remainder (0.1 + 0.2 gives <c>0.30000000000000004</c>). The text
    /// is at most 327 characters, whatever the value. A value that is not finite, which only a caller can build, gives
    /// <c>NaN</c>, <c>Infinity</c> or <c>-Infinity</c>.
    /// </remarks>
    public override string ToString()
    {
        var shortest = Value.ToString("R", CultureInfo.InvariantCulture);

        if (!double.IsFinite(Value))
        {
            return shortest;
        }

        var expanded = Expanded(shortest);
        return expanded.Contains('.', StringComparison.Ordinal) ? expanded : expanded + ".0";
    }

    /// <summary>Writes the shortest round-trip text of a finite double with no exponent: 1E-07 is written 0.0000001.</summary>
    private static string Expanded(string shortest)
    {
        var e = shortest.IndexOf('E', StringComparison.Ordinal);

        if (e < 0)
        {
            return shortest;
        }

        var exponent = int.Parse(shortest.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var sign = shortest[0] == '-' ? "-" : string.Empty;
        var mantissa = shortest[sign.Length..e];
        var point = mantissa.IndexOf('.', StringComparison.Ordinal);
        var digits = point < 0 ? mantissa : mantissa.Remove(point, 1);
        var whole = (point < 0 ? mantissa.Length : point) + exponent;

        // "R" writes an exponent only below 10^-4, where the whole part is none, and from 10^17, where it is longer than
        // the 17 digits a double needs at most: the last arm stands for a runtime that writes one sooner.
        return sign + (whole <= 0
            ? "0." + new string('0', -whole) + digits
            : whole >= digits.Length ? digits + new string('0', whole - digits.Length) : digits[..whole] + "." + digits[whole..]);
    }
}
