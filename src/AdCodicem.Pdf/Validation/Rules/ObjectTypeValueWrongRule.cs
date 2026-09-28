namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.ObjectTypeValueWrong"/>: the <c>/Type</c> and <c>/Subtype</c> of every object
/// reachable from the trailer are names the Arlington PDF Model lists for its type.
/// </summary>
/// <remarks>
/// <para>
/// The object's type is the one its context gives it — a page tree node's kid, a page's resources —, weighed with
/// its other keys, so a root of <c>/Type /Pagez</c> is still the root, and a page of <c>/Type /Font</c> still a page.
/// Only <c>/Type</c> and <c>/Subtype</c> are judged, against the model's plain values; a value its predicates alone
/// allow, and every other key's values, are not.
/// </para>
/// <para>
/// A reader that goes by the context reads the object as it was evidently meant; one that goes by <c>/Type</c> may
/// not: a warning (ADR 45). One finding per type and key, at the first occurrence in walk order.
/// </para>
/// </remarks>
internal sealed class ObjectTypeValueWrongRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.ObjectTypeValueWrong;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var finding in context.Arlington.TypeValuesWrong)
        {
            context.Report(this, finding.Location, finding.Message, ArlingtonText.Remedy(ArlingtonWalk.Rule.TypeValueWrong));
        }
    }
}
