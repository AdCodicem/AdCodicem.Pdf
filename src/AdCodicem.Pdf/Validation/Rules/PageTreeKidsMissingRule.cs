namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.PageTreeKidsMissing"/>: each node of the page tree has a <c>/Kids</c> array.
/// </summary>
/// <remarks>
/// ISO 32000-1 (Table 29) requires <c>/Kids</c> of every node. Without one, a node of <c>/Type /Pages</c> lists no
/// page, and the reader reads none below it, as the file is written: a warning (ADR 45). Pages its <c>/Count</c> or
/// their <c>/Parent</c> say belong there, and no node lists, are <see cref="PdfValidationRuleIds.PageTreePageOrphaned"/>'s,
/// and the <c>/Count</c> of the nodes above it is not judged. A <c>/Kids</c> naming an object the file lacks is
/// <see cref="PdfValidationRuleIds.ObjectReferenceMissing"/>'s.
/// </remarks>
internal sealed class PageTreeKidsMissingRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.PageTreeKidsMissing;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var findings = context.PageTree.NodesWithoutKids;

        foreach (var finding in findings)
        {
            context.Report(this, finding.Location, finding.Message, "Give the node a /Kids array listing its pages, or remove it from the tree.");
        }

        // What the analysis counted past the room the report had when it was made, which it did not write (#168).
        context.Count(this, findings.Unkept);
    }
}
