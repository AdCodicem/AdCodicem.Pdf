namespace AdCodicem.Pdf.Validation;

/// <summary>
/// The identifiers of the validation rules. They are part of the public contract: callers filter on them and
/// repair keys its remedies off them, so renaming one is a breaking change once a stable release carries it.
/// </summary>
/// <remarks>
/// An identifier is <c>family.name</c>, both in lowercase kebab case. The family is never a profile's name,
/// since a rule runs in every profile that includes it. One identifier names one rule, which always reports at
/// the same severity, and no identifier equals one of <see cref="Diagnostics.PdfDiagnosticCodes"/>: findings
/// and diagnostics are different things. <c>docs/validation-rules.md</c> lists every identifier.
/// </remarks>
public static class PdfValidationRuleIds
{
    /// <summary>
    /// No <c>%%EOF</c> marker in the last 1,024 bytes of the file: the file was cut short, or something was
    /// appended after its end. <see cref="PdfValidationSeverity.Warning"/>.
    /// </summary>
    public const string FileEofMissing = "file.eof-missing";
}
