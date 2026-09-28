namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.ObjectKeyDeprecated"/>: no object reachable from the trailer has a key the
/// Arlington PDF Model says the version the file declares deprecates.
/// </summary>
/// <remarks>
/// <para>
/// The version is the header's, or the catalog's <c>/Version</c> when that is later (ISO 32000-1, 7.2.2 and 7.7.2);
/// a file whose header names no version is not judged, whatever its catalog says. A key deprecated in or before that version is reported;
/// deprecated values are not.
/// </para>
/// <para>
/// Deprecated means a writer should no longer write it; a file that does still conforms, and readers read it:
/// information (ADR 45). One finding per type and key, at the first occurrence in walk order.
/// </para>
/// </remarks>
internal sealed class ObjectKeyDeprecatedRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.ObjectKeyDeprecated;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Information;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var finding in context.Arlington.KeysDeprecated)
        {
            context.Report(this, finding.Location, finding.Message, ArlingtonText.Remedy(ArlingtonWalk.Rule.KeyDeprecated));
        }
    }
}
