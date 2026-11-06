using System.Globalization;
using AdCodicem.Pdf.IO.XRef;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefHybridStreamWrong"/>: each hybrid file's table names through <c>/XRefStm</c> the
/// cross-reference stream of its revision.
/// </summary>
/// <remarks>
/// ISO 32000-1 makes <c>/XRefStm</c> the offset of a cross-reference stream (Table 19), which holds the rows a hybrid file's
/// table leaves out for readers of PDF 1.4 (7.5.8.4). One that names a classic table — the table itself, another the chain
/// reads, or one the reader reads in the stream's place —, a cross-reference stream the chain reads through <c>/Prev</c>
/// or <c>startxref</c>, or what the table's own <c>/Prev</c> names too, leaves that stream unknown: what only it indexed is
/// found, if at all, by rebuilding the index, after which the reader cannot vouch that it reads what was written — an error
/// (ADR 45), as a section that cannot be found is (#156). So does one that names a classic table that cannot be read, whose
/// own fault is <see cref="PdfValidationRuleIds.XRefSectionMalformed"/>'s, at each naming. What the table's own <c>/Prev</c>
/// names too is said as the chain found it once it read on: the section it read, or what held no section it could read — or,
/// where a limit cut what is there before the reading could tell, or the section count guard ended the chain there, as the
/// limit hides it. An <c>/XRefStm</c> whose search near the offset it names stopped only where a limit cut what the
/// <c>/Prev</c> names is the limit's, which the reader left unreported, and is not judged here (#264). A stream that more
/// than one table names, as a writer that copies <c>/XRefStm</c> into the next trailer leaves it, is a stream all the same,
/// and not judged here.
/// </remarks>
internal sealed class HybridStreamWrongRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefHybridStreamWrong;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Error;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var section in context.Structure.Sections)
        {
            // Past the report's room, the naming is counted, its finding not written (#168).
            if (section.NamesNoStream is not { } what || !context.HasRoomFor(this))
            {
                continue;
            }

            // What the /Prev names too was looked for after the reader reported the naming, and is said as the chain found it:
            // what a limit cut there, before the reading could tell, is hidden by it, not found to hold no section.
            if (section is { NamesWhatPreviousReads: true, ReadAs: { } previous })
            {
                what = previous.State is XRefSectionState.Read or XRefSectionState.Relocated
                    ? "the section its /Prev names too, which the chain read through that /Prev"
                    : previous.CutByLimit
                        ? "what its /Prev names too, past the limit that cut it there"
                        : "what its /Prev names too, which holds no section the chain could read";
            }

            context.Report(
                this,
                PdfValidationLocation.AtPosition(section.NamedFrom),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The /XRefStm of the cross-reference table at offset {section.NamedFrom} names offset {context.Document.Reader.DescribeOffset(section.WrittenOffset)}, {what}, where ISO 32000-1 names the cross-reference stream of the table's revision (Table 19): what only that stream indexes is lost."),
                "Point /XRefStm at the cross-reference stream of the table's revision, or remove it when the table indexes every object of its revision.");
        }
    }
}
