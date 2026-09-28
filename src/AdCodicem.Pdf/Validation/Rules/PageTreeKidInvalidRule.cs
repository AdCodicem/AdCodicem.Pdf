namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.PageTreeKidInvalid"/>: each kid of the page tree is an indirect reference to a page or a node.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-1 (Table 29) makes <c>/Kids</c> an array of indirect references to pages and nodes. A kid that is null,
/// that names an object the file lacks, or that is neither a dictionary nor a stream stands for a page the file does
/// not hold: the reader reads it as null, as the specification says, and counts a page with nothing on it in its
/// place, as qpdf, poppler and PDFium do — M06's page API is to materialize it. A kid that is a stream is read through
/// its dictionary; one written in the array rather than referred to, as it is. Each is read as it was evidently meant,
/// nothing rebuilt: a warning (ADR 45).
/// </para>
/// <para>
/// A kid the index holds in use and the reader could not produce is the index's fault, which
/// <see cref="PdfValidationRuleIds.XRefEntryBroken"/> or <see cref="PdfValidationRuleIds.XRefObjectStreamBroken"/>
/// reports; it takes the place of a page all the same.
/// </para>
/// </remarks>
internal sealed class PageTreeKidInvalidRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.PageTreeKidInvalid;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var finding in context.PageTree.InvalidKids)
        {
            context.Report(this, finding.Location, finding.Message, "Replace the kid with an indirect reference to the page or node it stands for, or remove it and correct /Count.");
        }
    }
}
