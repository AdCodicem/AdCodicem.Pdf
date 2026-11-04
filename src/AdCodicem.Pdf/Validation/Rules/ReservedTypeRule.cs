using System.Globalization;
using AdCodicem.Pdf.IO;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefEntryTypeReserved"/>: no cross-reference stream the chain reads gives a row of a
/// reserved type — a type past 2, or a type field wider than 8 bytes whose leading bytes are not all zero. ISO 32000-1
/// (7.5.8.3) defines types 0, 1 and 2, and reads any other as a reference to the null object, "thus permitting new entry
/// types to be defined in the future".
/// </summary>
/// <remarks>
/// The reader reads such a row as the specification does, its object indexed as a free one's is, so that no older
/// section's row stands for it (#221): the file breaks the specification and is read as the specification says, which is
/// a warning (ADR 45), as a reference to an object the file lacks is
/// (<see cref="PdfValidationRuleIds.ObjectReferenceMissing"/>). What a later version may define the type to mean is
/// unknown to this one. Each section that gives such rows is reported once, the first named and the others counted,
/// whatever became of the section once its rows were read: the rows it read are the file's, before a guard's cut too.
/// </remarks>
internal sealed class ReservedTypeRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefEntryTypeReserved;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var section in context.Structure.Sections)
        {
            // Past the report's room, the section is counted, its finding not written (#168).
            if (section.ReservedRow is not { } first || !context.HasRoomFor(this))
            {
                continue;
            }

            var others = section.ReservedRows > 1 ? ", the first of them" : string.Empty;

            context.Report(
                this,
                PdfValidationLocation.AtPosition(section.Offset),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The cross-reference stream at offset {section.Offset} gives {CountText.Grouped(section.ReservedRows, "row", "rows")} a reserved type: {first}{others}. ISO 32000-1 defines types 0, 1 and 2, and reads any other as a reference to the null object (7.5.8.3), as the reader does."),
                "Rewrite the cross-reference stream with a row of type 0, 1 or 2 for each object it numbers: type 0 for one that is free.");
        }
    }
}
