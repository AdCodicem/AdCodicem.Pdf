using System.Globalization;
using AdCodicem.Pdf.IO.XRef;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.FileTrailerMissing"/>: a cross-reference table is followed by its trailer, the
/// <c>trailer</c> keyword and a dictionary.
/// </summary>
/// <remarks>
/// Without the keyword a table's rows run into whatever follows: qpdf finds no trailer and rebuilds the index, as the
/// reader does, and neither can vouch that it then reads what was written: an error (ADR 45). A cross-reference stream carries its
/// trailer in its own dictionary, and cannot lose it this way.
/// </remarks>
internal sealed class TrailerMissingRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.FileTrailerMissing;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Error;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var length = context.Source.Length;

        foreach (var section in context.Structure.Sections)
        {
            if (section.TrailerFault != XRefTrailerFault.Missing)
            {
                continue;
            }

            var what = section.TrailerPosition >= length
                ? "the file ends first"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"a dictionary follows its rows at offset {section.TrailerPosition} without the trailer keyword");

            context.Report(
                this,
                PdfValidationLocation.AtPosition(Math.Min(section.TrailerPosition, length)),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The cross-reference table at offset {section.Offset} is not followed by a trailer: {what}."),
                "Write the trailer keyword, then the trailer dictionary, after the table's last row.");
        }
    }
}
