using System.Globalization;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.FileHeaderOffset"/>: the header is the file's first line, with nothing before it.
/// </summary>
/// <remarks>
/// A transfer that prepends bytes — a MacBinary header, a mail gateway's line — leaves every offset of the file
/// counted from the header rather than from the file's start. Readers find the header in the first 1,024 bytes and
/// count from there, so they agree about the file; it is wrong all the same. A header further in is
/// <see cref="PdfValidationRuleIds.FileHeaderMissing"/>'s.
/// </remarks>
internal sealed class FileHeaderOffsetRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.FileHeaderOffset;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var header = context.Structure.HeaderPosition;

        if (header is > 0 and < FileHeaderMissingRule.SearchLength)
        {
            context.Report(
                this,
                PdfValidationLocation.AtPosition(0),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The PDF header starts {header} bytes into the file instead of at its first byte."),
                "Remove the bytes before the header; a rewrite of the file puts every offset right.");
        }
    }
}
