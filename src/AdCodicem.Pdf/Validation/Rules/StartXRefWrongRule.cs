using System.Globalization;
using AdCodicem.Pdf.IO.XRef;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.FileStartXRefWrong"/>: the offset <c>startxref</c> gives holds a cross-reference
/// section — the <c>xref</c> keyword, or a cross-reference stream.
/// </summary>
/// <remarks>
/// Pointing anywhere else, it leaves the file's index out of reach, and the reader rebuilds one by scanning: an
/// error, as <see cref="PdfValidationRuleIds.FileStartXRefMissing"/> is (ADR 45). White space before the
/// section is not this rule's but <see cref="PdfValidationRuleIds.XRefOffsetImprecise"/>'s; a section that is
/// there and cannot be read is <see cref="PdfValidationRuleIds.XRefSectionMalformed"/>'s.
/// </remarks>
internal sealed class StartXRefWrongRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.FileStartXRefWrong;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Error;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var structure = context.Structure;

        if (structure.StartXRef < 0 || structure.Sections.Count == 0 || structure.Sections[0].State != XRefSectionState.NotFound)
        {
            return;
        }

        context.Report(
            this,
            PdfValidationLocation.AtPosition(structure.StartXRefPosition),
            string.Create(
                CultureInfo.InvariantCulture,
                $"startxref gives offset {structure.StartXRef}, which {structure.Sections[0].Fault ?? "holds no cross-reference section"}."),
            "Correct the offset startxref gives; if no section is left to point at, rewrite the file's cross-reference index.");
    }
}
