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
            // Placed where the chain goes on, a position in the file; one named outside the file — past its end, or past
            // what a long holds (#125) — is placed at the section that names it, as a section not found is.
            var inFile = structure.ChainCutAt < context.Source.Length;
            var location = inFile
                ? PdfValidationLocation.AtPosition(structure.ChainCutAt)
                : structure.ChainCutNamedFrom >= 0 ? PdfValidationLocation.AtPosition(structure.ChainCutNamedFrom) : default;

            context.Report(
                this,
                location,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The cross-reference chain goes on at offset {context.Document.Reader.DescribeOffset(structure.ChainCutWrittenOffset)}{(inFile ? string.Empty : ", outside the file")}, past the sections PdfReaderLimits.MaxXRefSectionCount lets the reader read: the older sections were not checked."),
                remedy: null);
        }

        // The reader's, not the structure's: the index may have filled in a rebuild run after the document opened.
        var indexFullAt = context.Document.Reader.IndexFullAt;

        if (indexFullAt >= 0)
        {
            context.Report(
                this,
                PdfValidationLocation.AtPosition(indexFullAt),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"At offset {indexFullAt} the index reached the most entries PdfReaderLimits.MaxIndexEntries lets the reader hold: the objects past them were not indexed or checked."),
                remedy: null);
        }

        // A section's finding is written while the report has room for it, and counted past it (#168): there are as many as
        // the chain has sections.
        foreach (var section in structure.Sections)
        {
            // The section the index filled in is said so above, once.
            if (section.CutByLimit && section.Offset != indexFullAt && context.HasRoomFor(this))
            {
                context.Report(
                    this,
                    PdfValidationLocation.AtPosition(section.Offset),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"One of the reader's limits stopped it reading the cross-reference section at offset {section.Offset} whole: what lies past the limit was not checked."),
                    remedy: null);
            }

            // A hybrid file's /XRefStm names the cross-reference stream of the section's own revision, which a /Prev may
            // not follow: what the guard hides is that stream, and whatever is older.
            if (section.ChainHiddenByGuard is { } hidden && context.HasRoomFor(this))
            {
                context.Report(
                    this,
                    PdfValidationLocation.AtPosition(section.Offset),
                    section.ChainHiddenAtStream
                        ? string.Create(
                            CultureInfo.InvariantCulture,
                            $"The cross-reference section at offset {section.Offset} names the cross-reference stream of its revision, but {hidden}: that stream, and any section older than it, were not checked.")
                        : string.Create(
                            CultureInfo.InvariantCulture,
                            $"The cross-reference section at offset {section.Offset} names older sections, but {hidden}: they were not checked."),
                    remedy: null);
            }
        }

        var findings = context.Probe.NotChecked;

        foreach (var finding in findings)
        {
            context.Report(this, finding.Location, finding.Message, remedy: null);
        }

        // What the analysis counted past the room the report had when it was made, which it did not write (#168).
        context.Count(this, findings.Unkept);
    }
}
