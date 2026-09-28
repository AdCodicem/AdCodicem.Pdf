namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefObjectStreamCircular"/>: an object stream can be read without first reading
/// an object it holds.
/// </summary>
/// <remarks>
/// A stream's <c>/Length</c>, <c>/Filter</c>, <c>/DecodeParms</c>, <c>/N</c> and <c>/First</c> must be known before its
/// data can be read (ISO 32000-1, 7.5.7); an object the stream holds cannot be, nor one another stream holds whose
/// reading needs this one in turn. The reader reads the stream without that value — qpdf reports a loop — and cannot
/// vouch that what it decoded is what was written: an error (ADR 45). A stream this makes unreadable is reported here
/// alone, not by <see cref="PdfValidationRuleIds.XRefObjectStreamBroken"/>.
/// </remarks>
internal sealed class ObjectStreamCircularRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefObjectStreamCircular;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Error;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var finding in context.ObjectStreams.Circular)
        {
            context.Report(
                this,
                finding.Location,
                finding.Message,
                "Write the value the stream needs directly in its dictionary, or store the object it names outside the stream.");
        }
    }
}
