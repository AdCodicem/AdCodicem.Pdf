namespace AdCodicem.Pdf.Validation;

/// <summary>One check of a validation profile.</summary>
/// <remarks>
/// A rule is stateless: everything it learns about a document goes through the <see cref="ValidationContext"/>
/// it is given, so one instance serves every validation, on any thread. It reports under its own
/// <see cref="Id"/> and at its own <see cref="Severity"/>, always.
/// </remarks>
internal interface IValidationRule
{
    /// <summary>Gets the rule's identifier, one of <see cref="PdfValidationRuleIds"/>.</summary>
    string Id { get; }

    /// <summary>Gets the severity the rule reports at.</summary>
    PdfValidationSeverity Severity { get; }

    /// <summary>Checks the document, reporting what it finds into <paramref name="context"/>.</summary>
    void Check(ValidationContext context);
}
