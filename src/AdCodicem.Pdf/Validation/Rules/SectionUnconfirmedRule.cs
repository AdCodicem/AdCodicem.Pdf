using System.Globalization;
using AdCodicem.Pdf.IO;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefSectionUnconfirmed"/>: each cross-reference section a <c>/Prev</c> or an
/// <c>/XRefStm</c> names, found near where it is named, is found where the file confirms that a section starts.
/// </summary>
/// <remarks>
/// The reader looks for such a section within 512 bytes of where it is named, and tries only the places its scan for
/// objects takes. Where the one it takes lies in the data of a stream whose length is not confirmed, or in what a string
/// that runs past its object runs into, the bytes may be another object's — an attached file's own table, a stream the data
/// quotes —, or the reader went without the scan's word, past what it records: the choice is the reader's, not the file's,
/// and the reader cannot vouch that it reads what was written, an error (ADR 45). A section found where the scan takes it
/// between objects is <see cref="PdfValidationRuleIds.XRefSectionShifted"/>'s.
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
