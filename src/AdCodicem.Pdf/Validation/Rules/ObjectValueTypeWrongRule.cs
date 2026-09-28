namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.ObjectValueTypeWrong"/>: every value of an object reachable from the trailer is of
/// a type the Arlington PDF Model allows for its key.
/// </summary>
/// <remarks>
/// <para>
/// Judged by the value's class, never by a conversion: an integer is an <c>integer</c>, a <c>bitmask</c> or a
/// <c>number</c>, and a real only a <c>number</c>; a stream is a <c>stream</c> and never a <c>dictionary</c>, nor a
/// dictionary a stream; a string is any string or a <c>date</c>, an array an <c>array</c>, a <c>rectangle</c> or a
/// <c>matrix</c>. What lies inside — a string's encoding, a date's syntax, a rectangle's four numbers, a bitmask's
/// bits — is not judged. A value of the wrong type is not followed further: a stream where a page belongs is
/// reported on what holds it, and not walked. In an array, a <c>null</c> written there is judged; a reference to an
/// object that reads as null is not.
/// </para>
/// <para>
/// A reader skips the value or takes it for what it evidently means: a warning (ADR 45). One finding per type and
/// key, at the first occurrence in walk order. What a hand-written rule already reports — a page's media box, a node's
/// kids, a kid of the page tree — is left to it where the page tree's walk judged the object, as
/// <c>tools/AdCodicem.Pdf.Arlington/overrides.tsv</c> records.
/// </para>
/// </remarks>
internal sealed class ObjectValueTypeWrongRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.ObjectValueTypeWrong;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var finding in context.Arlington.ValueTypesWrong)
        {
            context.Report(this, finding.Location, finding.Message, ArlingtonText.Remedy(ArlingtonWalk.Rule.ValueTypeWrong));
        }
    }
}
