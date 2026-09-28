namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefObjectStreamBroken"/>: each object stream the index places objects in is an
/// object stream whose header can be read.
/// </summary>
/// <remarks>
/// An object stream the index lacks, that is no object stream, or whose data does not decode to the header its
/// <c>/N</c> and <c>/First</c> describe, loses every object placed in it: reported once for the stream, not for each
/// of its objects. An object stream whose own entry is broken is <see cref="PdfValidationRuleIds.XRefEntryBroken"/>'s,
/// one a limit stopped the reader decoding is <see cref="PdfValidationRuleIds.XRefCheckedInPart"/>'s, and one that
/// needs an object it holds to be read is <see cref="PdfValidationRuleIds.XRefObjectStreamCircular"/>'s.
/// </remarks>
internal sealed class ObjectStreamBrokenRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefObjectStreamBroken;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Error;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var finding in context.Probe.BrokenObjectStreams)
        {
            if (finding.Location.Object is { } stream && context.ObjectStreams.CircularStreams.Contains(stream.Number))
            {
                continue;
            }

            context.Report(
                this,
                finding.Location,
                finding.Message,
                "Rewrite the object stream, or store the objects it should hold outside it.");
        }
    }
}
