namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.FileHeaderVersionInvalid"/>: the header names a version of PDF — 1.0 to 1.7, or 2.0.
/// </summary>
/// <remarks>
/// Readers open a file whatever version its header names, and a catalog's <c>/Version</c> may raise it (ISO 32000-1,
/// 7.5.2); a version that does not exist says the file was not written by a tool that knew the format, or was
/// edited by hand. A header that cannot be found is <see cref="PdfValidationRuleIds.FileHeaderMissing"/>'s.
/// </remarks>
internal sealed class FileHeaderVersionRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.FileHeaderVersionInvalid;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var structure = context.Structure;

        if (structure.HeaderPosition is < 0 or >= FileHeaderMissingRule.SearchLength || IsVersion(structure.HeaderVersion))
        {
            return;
        }

        context.Report(
            this,
            PdfValidationLocation.AtPosition(structure.HeaderPosition),
            string.IsNullOrEmpty(structure.HeaderVersion)
                ? "The header names no version after %PDF-."
                : $"The header names version {structure.HeaderVersion}, which is not a version of PDF: 1.0 to 1.7, or 2.0.",
            "Write after %PDF- the version the file conforms to, such as 1.7.");
    }

    private static bool IsVersion(string? version) => version is
        "1.0" or "1.1" or "1.2" or "1.3" or "1.4" or "1.5" or "1.6" or "1.7" or "2.0";
}
