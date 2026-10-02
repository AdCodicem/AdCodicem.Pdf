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

    /// <inheritdoc/>
    public override string ToString() => Value.ToString("0.######", CultureInfo.InvariantCulture);
}
