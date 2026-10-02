namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefObjectStreamValueWrong"/>: an object stream's <c>/N</c>, <c>/First</c> and
/// <c>/Length</c> are integers.
/// </summary>
/// <remarks>
/// ISO 32000-1 makes them integers (Tables 5 and 16). One written as a real with no fractional part is read as the
/// integer it equals, as MuPDF, PDFBox and pdf.js read it — qpdf and poppler lose the stream's objects —: a warning
/// (ADR 45). One with a fractional part leaves the stream unreadable, which
/// <see cref="PdfValidationRuleIds.XRefObjectStreamBroken"/> reports. Each object stream the index places an object in
/// is judged, as it is for <see cref="PdfValidationRuleIds.XRefObjectStreamCircular"/>.
/// </remarks>
internal sealed class ObjectStreamValueWrongRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefObjectStreamValueWrong;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var finding in context.ObjectStreams.IntegersWrittenAsReals)
        {
            context.Report(this, finding.Location, finding.Message, "Write the value as an integer.");
        }
    }
}
