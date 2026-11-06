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
/// A rebuilt index (<see cref="Documents.PdfDocument.WasRepaired"/>) — at opening; by a read after it that asks for an
/// object the index lacks while the index may have lost entries, or that finds an object neither where its entry says
/// nor near it; or as the validation settles the index before its first rule — no longer holds what the chain's rows
/// gave: each object such a row numbers reads as the rebuild finds it, which is the object an older revision defines
/// when the file writes no newer one, and the finding says the reader read the rows so only until it rebuilt the index.
/// Settled before the first rule, whether the index was rebuilt is known here and stays so, so that the finding reads the
/// same whatever was read before the validation. A row whose number was refused before it was read — by a newer section, or
/// by a row of the stream's own for the same number — was never read so: it stood for nothing, its object looked for by
/// rebuilding the index, which the finding says rather than that the reader read it so
/// (<see cref="IO.XRef.XRefSectionRecord.ReservedRowsRefused"/>); every such refusal leaves the index to be rebuilt as the
/// validation settles it.
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

            var many = section.ReservedRows > 1;
            var others = many ? ", the first of them" : string.Empty;

            // A rebuild serves each object as the scan finds it, the row the chain read for it set aside; a row whose number
            // was refused before it was read was never read so, and its object is the rebuild's from the first.
            var reader = section.ReservedRowsRefused == section.ReservedRows
                ? (many
                    ? "but the reader did not read the rows so, their numbers refused before they were read: it reads each such object as the rebuild finds it"
                    : "but the reader did not read the row so, its number refused before it was read: it reads its object as the rebuild finds it")
                : context.Document.WasRepaired
                    ? (section.ReservedRowsRefused > 0
                        ? "as the reader did until it rebuilt the index by scanning the file, but for the rows whose numbers were refused before they were read; it now reads each object such a row numbers as the rebuild finds it"
                        : "as the reader did until it rebuilt the index by scanning the file; it now reads each object such a row numbers as the rebuild finds it")
                    : "as the reader does";

            context.Report(
                this,
                PdfValidationLocation.AtPosition(section.Offset),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The cross-reference stream at offset {section.Offset} gives {CountText.Grouped(section.ReservedRows, "row", "rows")} a reserved type: {first}{others}. ISO 32000-1 defines types 0, 1 and 2, and reads any other as a reference to the null object (7.5.8.3), {reader}."),
                "Rewrite the cross-reference stream with a row of type 0, 1 or 2 for each object it numbers: type 0 for one that is free.");
        }
    }
}
