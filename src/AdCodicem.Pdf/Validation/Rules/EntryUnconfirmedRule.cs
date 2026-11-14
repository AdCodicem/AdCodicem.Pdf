namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefEntryUnconfirmed"/>: an in-use entry whose object is not at its offset leads, within
/// 512 bytes of it, to no header of the object the reader can vouch for.
/// </summary>
/// <remarks>
/// The reader looks for the object near the offset, as it does for <see cref="PdfValidationRuleIds.XRefEntryShifted"/>, and
/// serves the header it chooses there. Where the object is defined more than once within reach, and no entry next to it is
/// shifted alike, or where every header within reach may lie inside another object's data — that of a stream whose length is
/// not confirmed, or a string that runs past its object —, the choice is the reader's, not the file's: the reader cannot vouch
/// that it reads the object as it was written, an error (ADR 45).
/// </remarks>
internal sealed class EntryUnconfirmedRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefEntryUnconfirmed;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Error;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var findings = context.Probe.Unconfirmed;

        foreach (var finding in findings)
        {
            context.Report(this, finding.Location, finding.Message, "Correct the entry's offset.");
        }

        // What the analysis counted past the room the report had when it was made, which it did not write (#168).
        context.Count(this, findings.Unkept);
    }
}
