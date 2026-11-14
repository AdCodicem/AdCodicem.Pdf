using System.Globalization;
using AdCodicem.Pdf.IO;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefSectionUnconfirmed"/>: each cross-reference section a <c>/Prev</c> or an
/// <c>/XRefStm</c> names, found near where it is named, is found where the file confirms that a section starts.
/// </summary>
/// <remarks>
/// The cases are <see cref="PdfValidationRuleIds.XRefSectionUnconfirmed"/>'s. Where the section the reader takes near the
/// offset lies in bytes its scan for objects takes as they come, they may be another object's — an attached file's own
/// table, a stream the data quotes —, and past where what the scan keeps is whole the reader goes without its word: the
/// choice is the reader's, not the file's, and the reader cannot vouch that it reads what was written, an error (ADR 45).
/// A section found where the scan takes it between objects, before there, is
/// <see cref="PdfValidationRuleIds.XRefSectionShifted"/>'s.
/// </remarks>
internal sealed class SectionUnconfirmedRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefSectionUnconfirmed;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Error;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var section in context.Structure.Sections)
        {
            // Past the report's room, counted, its finding not written (#168).
            if (!section.WasRelocated || !section.Unconfirmed || !context.HasRoomFor(this))
            {
                continue;
            }

            context.Report(
                this,
                PdfValidationLocation.AtPosition(section.Offset),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The cross-reference section {section.NamedBy} names at offset {section.NamedOffset} is not there; the reader took the one {CountText.Distance(section.Offset - section.NamedOffset, "it")}, at offset {section.Offset}, which may lie inside another object."),
                "Correct the offset that names the section.");
        }
    }
}
