namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.ObjectKeyMissing"/>: every object reachable from the trailer has the keys the
/// Arlington PDF Model requires of its type, in the version the file declares.
/// </summary>
/// <remarks>
/// <para>
/// The model's <c>Required</c> column, reduced to what a lookup can tell: <c>TRUE</c> from the key's ISO version on
/// — an extension's key never —, and the predicates that test the version alone. Any other predicate is not
/// evaluated, and its key never reported. A key the object inherits (<c>Inheritable</c>) is looked for in its
/// ancestors through <c>/Parent</c>, and a field's <c>/DA</c> also in the interactive form's (ISO 32000-1, 12.7.2).
/// An array shorter than the elements its type requires lacks each required element it does not hold. A key given as
/// <c>null</c> is absent (7.3.7); one that names an object the file lacks, or that the reader could not produce, is
/// present, the fault being <see cref="PdfValidationRuleIds.ObjectReferenceMissing"/>'s or the index's.
/// </para>
/// <para>
/// A reader goes on without the key — it takes a default, or does without what the key gives —, so the file is read
/// as it was evidently meant: a warning (ADR 45). One finding per type and key, at the first object in walk order,
/// with how many objects lack it. What a hand-written rule already reports — a page's media box or resources, a
/// node's kids, count or parent — is left to it where the page tree's walk judged the object, as
/// <c>tools/AdCodicem.Pdf.Arlington/overrides.tsv</c> records.
/// </para>
/// </remarks>
internal sealed class ObjectKeyMissingRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.ObjectKeyMissing;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var finding in context.Arlington.KeysMissing)
        {
            context.Report(this, finding.Location, finding.Message, ArlingtonText.Remedy(ArlingtonWalk.Rule.KeyMissing));
        }
    }
}
