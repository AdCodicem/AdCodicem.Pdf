namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.PageTreeResourcesMissing"/>: each page has a <c>/Resources</c> dictionary, its own or inherited.
/// </summary>
/// <remarks>
/// ISO 32000-1 (Table 30) requires resources of every page, inheritable from the nodes above it — an empty dictionary
/// when the page uses none. Without any, what the page's content names — a font, an image — cannot be found; the
/// reader reads the page as written: a warning (ADR 45). A <c>/Resources</c> naming an object the file lacks is
/// <see cref="PdfValidationRuleIds.ObjectReferenceMissing"/>'s, and one that is not a dictionary a type fault, not this
/// rule's.
/// </remarks>
internal sealed class PageTreeResourcesMissingRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.PageTreeResourcesMissing;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var finding in context.PageTree.ResourcesMissing)
        {
            context.Report(this, finding.Location, finding.Message, "Give the page, or a node above it, a /Resources dictionary, an empty one if its content uses no resource.");
        }
    }
}
