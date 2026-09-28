namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.PageTreeCountMismatch"/>: each node of the page tree gives, as <c>/Count</c>, the number of pages below it.
/// </summary>
/// <remarks>
/// ISO 32000-1 (Table 29) requires <c>/Count</c>, the number of leaves below the node. The reader counts the pages
/// the tree lists, each null or missing kid as one, and does not trust <c>/Count</c>, as qpdf's walk does not — the
/// file is read as its kids say: a warning (ADR 45). Readers that take the root's <c>/Count</c> for the number of
/// pages disagree with it. A <c>/Count</c> that is not an integer is a type fault, not this rule's; the nodes above a
/// loop, a node without <c>/Kids</c> or a kid the reader could not produce are not judged, their count being unknown.
/// </remarks>
internal sealed class PageTreeCountMismatchRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.PageTreeCountMismatch;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var finding in context.PageTree.CountMismatches)
        {
            context.Report(this, finding.Location, finding.Message, "Set /Count to the number of pages below the node.");
        }
    }
}
