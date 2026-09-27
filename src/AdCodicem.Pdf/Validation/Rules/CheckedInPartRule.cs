using System.Globalization;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefCheckedInPart"/>: says what part of the file's index the cross-reference rules
/// could not check, and why.
/// </summary>
/// <remarks>
/// A section, a trailer or an object stream one of the reader's limits cut is not the file's fault — the limit is
/// the reader's, and the file may be valid (ADR 34) —, and the rules that would have judged it say nothing of it.
/// This says so, as information, so that a report never passes a part it did not read for a sound one; raising the
/// limit the reader reported lets the rest be checked. An encrypted document's object streams are encrypted with it,
/// and wait for decryption (M16).
/// </remarks>
internal sealed class CheckedInPartRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefCheckedInPart;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Information;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var structure = context.Structure;

        if (structure.ChainCutAt >= 0)
        {
            context.Report(
                this,
                PdfValidationLocation.AtPosition(structure.ChainCutAt),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The cross-reference chain goes on at offset {structure.ChainCutAt}, past the sections PdfReaderLimits.MaxXRefSectionCount lets the reader read: the older sections were not checked."),
                remedy: null);
        }

        foreach (var section in structure.Sections)
        {
            if (section.CutByLimit)
            {
                context.Report(
                    this,
                    PdfValidationLocation.AtPosition(section.Offset),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"One of the reader's limits stopped it reading the cross-reference section at offset {section.Offset} whole: what lies past the limit was not checked."),
                    remedy: null);
            }
        }

        foreach (var finding in context.Probe.NotChecked)
        {
            context.Report(this, finding.Location, finding.Message, remedy: null);
        }
    }
}
