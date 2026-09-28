namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.PageTreeParentWrong"/>: each node and page below the root names, as <c>/Parent</c>, the node that lists it, and the root names none.
/// </summary>
/// <remarks>
/// ISO 32000-1 (Tables 29 and 30) requires <c>/Parent</c> of every node and page but the root, and forbids it in the
/// root. The reader walks the tree from the root through <c>/Kids</c> and reads a page's inherited attributes along
/// that path, not along <c>/Parent</c>; qpdf does the same. The file is read as <c>/Kids</c> says: a warning (ADR 45).
/// A kid written in the array, which has no number to be named by, is not judged.
/// </remarks>
internal sealed class PageTreeParentWrongRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.PageTreeParentWrong;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var finding in context.PageTree.ParentFaults)
        {
            context.Report(this, finding.Location, finding.Message, "Point /Parent at the node that lists it, as an indirect reference; remove it from the root.");
        }
    }
}
