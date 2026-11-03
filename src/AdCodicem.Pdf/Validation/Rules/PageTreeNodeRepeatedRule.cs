namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.PageTreeNodeRepeated"/>: each node and each page is listed once in the page tree.
/// </summary>
/// <remarks>
/// A tree lists each of its nodes once (ISO 32000-1, 7.7.3). A node or a page listed a second time, away from its
/// own path, is counted each time it is listed, as qpdf does — the file is read as it was evidently meant, the same
/// page shown twice: a warning (ADR 45). One that loops back to a node above it is
/// <see cref="PdfValidationRuleIds.PageTreeCycle"/>'s.
/// </remarks>
internal sealed class PageTreeNodeRepeatedRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.PageTreeNodeRepeated;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var findings = context.PageTree.RepeatedNodes;

        foreach (var finding in findings)
        {
            context.Report(this, finding.Location, finding.Message, "List each node and page once, giving a page shown twice a page object of its own.");
        }

        // What the analysis counted past the room the report had when it was made, which it did not write (#168).
        context.Count(this, findings.Unkept);
    }
}
