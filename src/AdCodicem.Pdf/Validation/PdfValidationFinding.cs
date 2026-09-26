namespace AdCodicem.Pdf.Validation;

/// <summary>One thing a validation rule found wrong with a document, or worth knowing about it.</summary>
/// <remarks>
/// A finding is the validator's verdict on the file, where a <see cref="Diagnostics.PdfDiagnostic"/> is the
/// reader's account of what it did to read it. Its <see cref="RuleId"/> is the stable part: callers filter on
/// it, and repair keys its remedies off it. The message and the remedy are for people, and may be reworded.
/// </remarks>
public sealed record PdfValidationFinding
{
    internal PdfValidationFinding(
        string ruleId, PdfValidationSeverity severity, PdfValidationLocation location, string message, string? remedy)
    {
        RuleId = ruleId;
        Severity = severity;
        Location = location;
        Message = message;
        Remedy = remedy;
    }

    /// <summary>
    /// Gets the identifier of the rule that found it, one of <see cref="PdfValidationRuleIds"/>: stable, and
    /// part of the public contract.
    /// </summary>
    public string RuleId { get; }

    /// <summary>Gets how wrong the finding says the document is. A rule always reports at the same severity.</summary>
    public PdfValidationSeverity Severity { get; }

    /// <summary>Gets where in the document the finding applies.</summary>
    public PdfValidationLocation Location { get; }

    /// <summary>Gets a human-readable explanation, in English.</summary>
    public string Message { get; }

    /// <summary>
    /// Gets what would put it right, as a hint rather than an action — so that a report reads as a plan — or
    /// null when there is nothing to put right.
    /// </summary>
    public string? Remedy { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{Severity} {RuleId} at {Location}: {Message}";
}
