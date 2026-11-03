namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefObjectStreamBroken"/>: each object stream the index places objects in is an
/// object stream whose header can be read.
/// </summary>
/// <remarks>
/// An object stream the index lacks, that is no object stream, or whose data does not decode to the header its
/// <c>/N</c> and <c>/First</c> describe, loses every object placed in it: reported once for the stream, not for each
/// of its objects. A header that breaks after listing some objects loses only those the index places in the stream that
/// it does not list before the break, which the finding counts; none lost, there is no finding (#275). An object stream whose own entry is broken is <see cref="PdfValidationRuleIds.XRefEntryBroken"/>'s,
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
        foreach (var broken in context.Probe.BrokenObjectStreams)
        {
            // The probe keeps a record of every broken stream, for this rule to pass over the circular ones; the others'
            // findings are written while the report has room for them, and counted past it (#168).
            if (context.ObjectStreams.CircularStreams.Contains(broken.Stream) || !context.HasRoomFor(this))
            {
                continue;
            }

            context.Report(
                this,
                broken.Location,
                broken.Describe(),
                "Rewrite the object stream, or store the objects it should hold outside it.");
        }
    }
}
