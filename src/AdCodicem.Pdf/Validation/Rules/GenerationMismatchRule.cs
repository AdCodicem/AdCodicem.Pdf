namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefGenerationMismatch"/>: each in-use entry gives the generation its object is
/// written with.
/// </summary>
/// <remarks>
/// A reference names an object by number and generation. The references to such an object and its own header agree
/// against its entry, and the reader reads the object as they designate it: a warning (ADR 45). A stricter reader
/// does not — qpdf reads a reference to it as a reference to nothing, and in iPRES t03-010, where that object is the
/// page tree, finds no page.
/// </remarks>
internal sealed class GenerationMismatchRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefGenerationMismatch;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var finding in context.Probe.GenerationMismatches)
        {
            context.Report(
                this,
                finding.Location,
                finding.Message,
                "Give the entry the generation the object is written with, or write the object with the entry's.");
        }
    }
}
