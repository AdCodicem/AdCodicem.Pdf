namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefEntryBroken"/>: each in-use entry of the file's index places its object where
/// it is — at its offset, or in the object stream and at the index it gives.
/// </summary>
/// <remarks>
/// An object that is neither at its offset nor within 512 bytes of it — or that its object stream does not hold —
/// is found, if at all, by rebuilding the index, after which the reader cannot vouch that it reads what was written:
/// an error (ADR 45). The entries
/// probed are those the file's chain gave, as the reader first read them; a chain that gave none is
/// <see cref="PdfValidationRuleIds.FileStartXRefWrong"/>'s or <see cref="PdfValidationRuleIds.FileStartXRefMissing"/>'s,
/// and no entry is probed.
/// </remarks>
internal sealed class EntryBrokenRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefEntryBroken;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Error;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var finding in context.Probe.Broken)
        {
            context.Report(this, finding.Location, finding.Message, "Correct the entry, or rewrite the file's cross-reference index.");
        }
    }
}
