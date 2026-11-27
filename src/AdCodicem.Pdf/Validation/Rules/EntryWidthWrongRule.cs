using System.Globalization;
using AdCodicem.Pdf.IO;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefEntryWidthWrong"/>: each row of a classic cross-reference table of the chain is
/// written in the 20-byte form ISO 32000-1 (7.5.4) gives it.
/// </summary>
/// <remarks>
/// The width is fixed so that a reader may seek a row by its number: one that does misreads every row after a row of
/// another width. The reader reads rows by their tokens, as pdf.js does, and qpdf accepts them with a warning, so the
/// file reads as written: a warning (ADR 45), once a table, naming the first such row and counting them. An end of line
/// runs over the spaces, carriage returns and line feeds past the keyword: one of more than two bytes — a blank line
/// after the row, or a carriage return a text-mode write doubled — makes the row longer than 20 bytes, where another
/// row of its subsection follows it. Past a subsection's last row, which no row follows for a reader to misread, a
/// blank line before the trailer is as many writers write it, and only the row's two bytes are judged. A row is judged
/// where the table's window holds its end of line: past a guard's cut, the rows are the guard's. Rows the reader could
/// not read as rows are <see cref="PdfValidationRuleIds.XRefSectionMalformed"/>'s.
/// </remarks>
internal sealed class EntryWidthWrongRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefEntryWidthWrong;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var section in context.Structure.Sections)
        {
            if (section.WidthWrongRow is not { } first || !context.HasRoomFor(this))
            {
                continue;
            }

            context.Report(
                this,
                PdfValidationLocation.AtPosition(section.Offset),
                section.WidthWrongRows == 1
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"The cross-reference table at offset {section.Offset} holds a row not in the 20-byte form: {first}.")
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"The cross-reference table at offset {section.Offset} holds {CountText.Of(section.WidthWrongRows, "row", "rows")} not in the 20-byte form; the first is {first}."),
                "Write each row in 20 bytes: a ten-digit offset, a space, a five-digit generation, a space, n or f, and a two-byte end of line.");
        }
    }
}
