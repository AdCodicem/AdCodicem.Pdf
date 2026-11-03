namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefObjectStreamCircular"/>: an object stream can be read without first reading
/// an object it holds.
/// </summary>
/// <remarks>
/// A stream's <c>/Length</c>, <c>/Type</c>, <c>/Filter</c>, <c>/DecodeParms</c>, <c>/N</c> and <c>/First</c> must be known
/// before its data can be read (ISO 32000-1, 7.5.7) — of one not of <c>/Type /ObjStm</c>, which the reader reads nothing
/// from, its <c>/Type</c>, which makes it no object stream (#274) —; an object the stream holds cannot be, nor one another
/// stream holds whose reading needs this one in turn. The reader reads the stream without that value — qpdf reports a loop — and cannot
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
        var findings = context.ObjectStreams.Circular;

        foreach (var finding in findings)
        {
            context.Report(
                this,
                finding.Location,
                finding.Message,
                "Write the value the stream needs directly in its dictionary, or store the object it names outside the stream.");
        }

        // What the analysis counted past the room the report had when it was made, which it did not write (#168).
        context.Count(this, findings.Unkept);
    }
}
