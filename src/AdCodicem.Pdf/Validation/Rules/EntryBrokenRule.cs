namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefEntryBroken"/>: each in-use entry of the file's index places its object where
/// it is — at its offset, or in the object stream and at the index it gives, written there as an object stream can
/// hold it.
/// </summary>
/// <remarks>
/// An object that is neither at its offset nor within 512 bytes of it — or that its object stream does not hold —
/// is found, if at all, by rebuilding the index, after which the reader cannot vouch that it reads what was written:
/// an error (ADR 45); one an object stream holds written as a stream reads as null (#161). The entries probed are those
/// the file's chain gave, as the reader first read them; a chain that gave none is
/// <see cref="PdfValidationRuleIds.FileStartXRefWrong"/>'s or <see cref="PdfValidationRuleIds.FileStartXRefMissing"/>'s,
/// and none of its entries is probed. Of an index the reader rebuilt, chain or none, the objects written as a stream that
/// it places where the chain's index places nothing are judged too: no entry the reader read would say they are lost.
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
            context.Report(this, finding.Location, finding.Message, finding.Remedy ?? "Correct the entry, or rewrite the file's cross-reference index.");
        }
    }
}
