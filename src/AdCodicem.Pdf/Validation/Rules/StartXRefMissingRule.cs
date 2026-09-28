using System.Globalization;
using AdCodicem.Pdf.IO;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.FileStartXRefMissing"/>: the file ends with <c>startxref</c> and the offset of
/// its cross-reference section.
/// </summary>
/// <remarks>
/// Without it the file gives no way into its index, and the reader rebuilds one by scanning the file. It then cannot
/// vouch that it reads what was written — which copy of an object an incremental update left wins, whether a deleted
/// object comes back —, and readers that rebuild differently read the file differently: an error (ADR 45). The
/// offset is looked for in the file's last 4,096 bytes, as the reader looks for it.
/// </remarks>
internal sealed class StartXRefMissingRule : IValidationRule
{
    private const string Remedy =
        "Write startxref and the offset of the file's last cross-reference section before %%EOF; if the index itself is lost, rewrite the file.";

    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.FileStartXRefMissing;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Error;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var structure = context.Structure;

        if (structure.StartXRef >= 0)
        {
            return;
        }

        var length = context.Source.Length;

        if (structure.StartXRefPosition < 0)
        {
            context.Report(
                this,
                PdfValidationLocation.AtPosition(length),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The file does not give the offset of its cross-reference section: no startxref in its last {Math.Min(PdfFileReader.TailSearchLength, length)} bytes."),
                Remedy);
            return;
        }

        context.Report(
            this,
            PdfValidationLocation.AtPosition(structure.StartXRefPosition),
            string.Create(
                CultureInfo.InvariantCulture,
                $"The startxref at offset {structure.StartXRefPosition} is not followed by an offset."),
            Remedy);
    }
}
