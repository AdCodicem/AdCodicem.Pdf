using System.Globalization;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.FileHeaderMissing"/>: a file starts with its header, <c>%PDF-</c> and a version,
/// where readers look for it.
/// </summary>
/// <remarks>
/// ISO 32000-1 (7.5.2) puts the header on the file's first line. Acrobat looks for it in the first 1,024 bytes and
/// refuses a file without one there; qpdf and the reader look further, or do without it, and read the file as it
/// was written. A warning (ADR 45): the file is read all the same, and a stricter reader may refuse it.
/// </remarks>
internal sealed class FileHeaderMissingRule : IValidationRule
{
    /// <summary>
    /// How far from the start the header may lie. Not a reader guard (invariant 12): a valid file's header is on
    /// its first line; this is the tolerance Acrobat extends to bytes a transfer put before it.
    /// </summary>
    internal const int SearchLength = 1024;

    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.FileHeaderMissing;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var header = context.Structure.HeaderPosition;

        if (header is >= 0 and < SearchLength)
        {
            return;
        }

        if (header < 0)
        {
            context.Report(
                this,
                PdfValidationLocation.AtPosition(0),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The file does not begin with a PDF header: no %PDF- in its first {Math.Min(SearchLength, context.Source.Length)} bytes."),
                "Start the file with %PDF- and the version it conforms to, such as %PDF-1.7, as its first line.");
            return;
        }

        context.Report(
            this,
            PdfValidationLocation.AtPosition(header),
            string.Create(
                CultureInfo.InvariantCulture,
                $"The PDF header starts {header} bytes into the file, past the first {SearchLength} bytes where readers look for it."),
            "Remove what precedes the header, so that the file starts with it.");
    }
}
