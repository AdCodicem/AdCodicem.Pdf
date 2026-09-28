namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.ObjectNameNullCharacter"/>: no name of an object reachable from the trailer contains
/// a null character.
/// </summary>
/// <remarks>
/// ISO 32000-1 (7.3.5) lets a name hold any character but the null one, character code 0, which <c>#00</c> writes. The
/// reader keeps the name as written, and reads the file as it was evidently meant: a warning (ADR 45). qpdf refuses the
/// name, and reads different dictionaries, in which the keys so named are missing. One finding per object that holds
/// such names, naming the first of them.
/// </remarks>
internal sealed class NameNullCharacterRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.ObjectNameNullCharacter;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var finding in context.Graph.NullCharacterNames)
        {
            context.Report(
                this,
                finding.Location,
                finding.Message,
                "Rename the name without its null character, here and wherever the file uses it.");
        }
    }
}
