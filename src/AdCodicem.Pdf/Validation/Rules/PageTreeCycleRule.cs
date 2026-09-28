namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.PageTreeCycle"/>: no kid of the page tree loops back to the node listing it or to a node above it.
/// </summary>
/// <remarks>
/// ISO 32000-1 (7.7.3) makes the page tree a tree. A kid naming the node that lists it, or a node above it, loops:
/// the reader stops there and counts no page for it, qpdf reports the loop, and what the tree should have listed in
/// its place is unknown — an error (ADR 45), as <see cref="PdfValidationRuleIds.XRefChainLoop"/> is for the
/// cross-reference chain. The <c>/Count</c> of the nodes above the loop is not judged: it cannot be known.
/// </remarks>
internal sealed class PageTreeCycleRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.PageTreeCycle;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Error;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var finding in context.PageTree.Cycles)
        {
            context.Report(this, finding.Location, finding.Message, "Replace the kid that loops back with the page or node the tree should list there, or remove it and correct /Count.");
        }
    }
}
