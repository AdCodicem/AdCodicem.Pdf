namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefEntryShifted"/>: each in-use entry places its object exactly — its offset at
/// the object's first byte, its index at the object's place in its object stream.
/// </summary>
/// <remarks>
/// An object a few bytes from its offset, within 512 of it, is found by readers that look nearby, as the reader
/// does, and by those that rebuild the index; one listed at another index of its object stream is found by readers
/// that trust the stream's header over the entry, as qpdf and the reader do. They agree about the file, which is
/// wrong all the same. A header found near the offset is the file's only where the reader vouches for it: one its scan for
/// objects takes between objects, and the only such definition of the object there or the one the entries next to it are
/// shifted alike to. Any other it takes — a supported one the scan takes only as the bytes give it among them — is
/// <see cref="PdfValidationRuleIds.XRefEntryUnconfirmed"/>'s, and white space between the offset and the object's header
/// <see cref="PdfValidationRuleIds.XRefOffsetImprecise"/>'s.
/// </remarks>
internal sealed class EntryShiftedRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefEntryShifted;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var findings = context.Probe.Shifted;

        foreach (var finding in findings)
        {
            context.Report(this, finding.Location, finding.Message, "Correct the entry's offset, or its index.");
        }

        // What the analysis counted past the room the report had when it was made, which it did not write (#168).
        context.Count(this, findings.Unkept);
    }
}
