namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefEntryUnconfirmed"/>: each in-use entry leads to a header of its object the reader can
/// vouch for.
/// </summary>
/// <remarks>
/// The cases are <see cref="PdfValidationRuleIds.XRefEntryUnconfirmed"/>'s. The reader serves the object from the header it
/// takes all the same, as it does under <see cref="PdfValidationRuleIds.XRefEntryShifted"/>, but the choice is the reader's,
/// not the file's: it cannot vouch that it reads the object as it was written, an error (ADR 45).
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
