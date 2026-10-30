namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.ObjectEndObjMissing"/>: every object reachable from the trailer ends with
/// <c>endobj</c>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-1 (7.3.10) ends an indirect object with <c>endobj</c>. Without it the reader reads the value as far as it
/// goes and stops where the next token begins, as qpdf does while it reports "expected endobj": the file is read as it
/// was evidently meant, a warning (ADR 45). An empty object, <c>2 0 obj endobj</c>, has its <c>endobj</c>.
/// </para>
/// <para>
/// The objects checked are those the reader read, reached from the trailer; what follows an object one of the reader's
/// limits cut is not seen, nor what follows a stream whose declared length no <c>endstream</c> nor <c>endobj</c> follows,
/// kept with its object's end unknown. An object stream's members have no <c>endobj</c> of their own.
/// </para>
/// </remarks>
internal sealed class EndObjMissingRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.ObjectEndObjMissing;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var findings = context.Graph.MissingEndObj;

        foreach (var finding in findings)
        {
            context.Report(this, finding.Location, finding.Message, "End the object with endobj, on a line of its own.");
        }

        // What the analysis counted past the room the report had when it was made, which it did not write (#168).
        context.Count(this, findings.Unkept);
    }
}
