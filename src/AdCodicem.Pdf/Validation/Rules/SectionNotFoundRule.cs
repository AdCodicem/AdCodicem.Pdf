using System.Globalization;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.XRef;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefSectionNotFound"/>: each cross-reference section a <c>/Prev</c> or an
/// <c>/XRefStm</c> names is where it is named.
/// </summary>
/// <remarks>
/// The reader looks for such a section within 512 bytes of where it is named; one that is nowhere near — or named by
/// something that is not an offset, as tiff2pdf's <c>/Prev 576066 0 R</c> — leaves the objects only it indexes out
/// of the index, to be found by rebuilding it, after which the reader cannot vouch that it reads what was written:
/// an error (ADR 45). The section
/// <c>startxref</c> names is <see cref="PdfValidationRuleIds.FileStartXRefWrong"/>'s.
/// </remarks>
internal sealed class SectionNotFoundRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefSectionNotFound;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Error;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var length = context.Source.Length;

        foreach (var section in context.Structure.Sections)
        {
            if (section.State != XRefSectionState.NotFound || section.NamedBy == "startxref")
            {
                continue;
            }

            var named = section.NamedOffset;
            var location = named >= 0 && named < length
                ? PdfValidationLocation.AtPosition(named)
                : section.NamedFrom >= 0 ? PdfValidationLocation.AtPosition(section.NamedFrom) : default;
            var message = named < 0
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"The {section.NamedBy} of the cross-reference section at offset {section.NamedFrom} {section.Fault}.")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"The cross-reference section {section.NamedBy} names at offset {named} is not there, nor within {PdfFileReader.NearbySearchRadius} bytes of it: the offset {section.Fault ?? "holds no section"}.");

            context.Report(
                this,
                location,
                message,
                "Correct the offset that names the section; if the section is lost, rewrite the file's cross-reference index.");
        }
    }
}
