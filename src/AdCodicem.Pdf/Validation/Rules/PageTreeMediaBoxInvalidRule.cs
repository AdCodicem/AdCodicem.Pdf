namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.PageTreeMediaBoxInvalid"/>: each page has a <c>/MediaBox</c>, its own or inherited, that is a rectangle of four numbers enclosing an area.
/// </summary>
/// <remarks>
/// ISO 32000-1 (Table 30) requires a media box of every page, inheritable from the nodes above it. Without one, or
/// with one that is not four numbers or encloses no area, the page's size is unknown; qpdf gives such a page the size
/// of a US Letter sheet and warns. The reader has chosen no size yet — M06's page API is to —: a warning (ADR 45).
/// Any two opposite corners make a rectangle (7.9.5), so a box written from its upper corner is sound. A missing box
/// is reported for each page; a malformed one where it is written, once. A <c>/MediaBox</c> naming an object the file
/// lacks is <see cref="PdfValidationRuleIds.ObjectReferenceMissing"/>'s.
/// </remarks>
internal sealed class PageTreeMediaBoxInvalidRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.PageTreeMediaBoxInvalid;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var finding in context.PageTree.MediaBoxFaults)
        {
            context.Report(this, finding.Location, finding.Message, "Give the page, or a node above it, a /MediaBox of four numbers enclosing the page's area.");
        }
    }
}
