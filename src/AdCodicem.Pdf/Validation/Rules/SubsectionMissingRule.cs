using System.Globalization;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefSubsectionMissing"/>: each classic cross-reference table of the chain holds one or
/// more subsections (ISO 32000-1, 7.5.4).
/// </summary>
/// <remarks>
/// A table whose <c>trailer</c> keyword follows its <c>xref</c> keyword is read as indexing nothing, as a table whose one
/// subsection is <c>0 0</c> is, which ISO 32000-1 allows: the reader reads the file as written and rebuilds nothing for it,
/// so the fault is a warning (ADR 45), though an update that indexes nothing may have lost what it meant to index (#222).
/// Other readers part: qpdf calls the table's syntax invalid and reconstructs the file, PyMuPDF and PDFBox read it.
/// </remarks>
internal sealed class SubsectionMissingRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefSubsectionMissing;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var section in context.Structure.Sections)
        {
            // A table that holds no subsection is read whole, its trailer reached; past the report's room, counted, its finding
            // not written (#168).
            if (!section.NoSubsection || !context.HasRoomFor(this))
            {
                continue;
            }

            context.Report(
                this,
                PdfValidationLocation.AtPosition(section.Offset),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The cross-reference table at offset {section.Offset} holds no subsection: its trailer keyword follows its xref keyword."),
                "Write a subsection after the xref keyword: 0 0 for a table that indexes nothing.");
        }
    }
}
