using System.Globalization;
using AdCodicem.Pdf.IO.XRef;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefSectionShifted"/>: each cross-reference section a <c>/Prev</c> or an
/// <c>/XRefStm</c> names starts where it is named.
/// </summary>
/// <remarks>
/// A writer's arithmetic a few bytes off — IBM's QMF manual names its main table 12 bytes past it — is found by
/// readers that look nearby, as the reader does within 512 bytes, and by those that rebuild the index: they agree
/// about the file, which is wrong all the same. Only white space between the offset and the section is
/// <see cref="PdfValidationRuleIds.XRefOffsetImprecise"/>'s.
/// </remarks>
internal sealed class SectionShiftedRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefSectionShifted;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var section in context.Structure.Sections)
        {
            if (section.State != XRefSectionState.Relocated)
            {
                continue;
            }

            context.Report(
                this,
                PdfValidationLocation.AtPosition(section.Offset),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The cross-reference section {section.NamedBy} names at offset {section.NamedOffset} starts {section.Offset - section.NamedOffset} bytes from there, at offset {section.Offset}."),
                "Correct the offset that names the section.");
        }
    }
}
