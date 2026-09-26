using System.Collections.ObjectModel;
using System.Globalization;

namespace AdCodicem.Pdf.Validation;

/// <summary>What a validation profile found in a document: the findings in the order they were found, and
/// how many there were of each severity.</summary>
/// <remarks>
/// <para>
/// The report is bounded: it keeps at most <see cref="PdfValidatorOptions.FindingCapacity"/> findings, so a
/// hostile file with a fault in every object cannot make it grow without limit. The counts by severity, and
/// <see cref="Contains"/>, cover every finding, kept or not; <see cref="SuppressedCount"/> says how many were
/// not kept.
/// </para>
/// <para>
/// Two validations of the same document under the same profile give the same report: rules run in the
/// profile's order and report in the order they find, and nothing in a report depends on the time or on the
/// machine.
/// </para>
/// </remarks>
public sealed class PdfValidationReport
{
    private readonly HashSet<string> _ruleIds;

    internal PdfValidationReport(
        ValidationProfile profile,
        List<PdfValidationFinding> findings,
        HashSet<string> ruleIds,
        int errorCount,
        int warningCount,
        int informationCount)
    {
        ProfileName = profile.Name;
        ProfileVersion = profile.Version;
        Findings = new ReadOnlyCollection<PdfValidationFinding>(findings);
        _ruleIds = ruleIds;
        ErrorCount = errorCount;
        WarningCount = warningCount;
        InformationCount = informationCount;
    }

    /// <summary>Gets the name of the profile the document was validated against, such as <c>structural</c>.</summary>
    public string ProfileName { get; }

    /// <summary>Gets the version of that profile.</summary>
    public int ProfileVersion { get; }

    /// <summary>Gets the findings kept, in the order they were found.</summary>
    public IReadOnlyList<PdfValidationFinding> Findings { get; }

    /// <summary>Gets the number of error findings, kept or not.</summary>
    public int ErrorCount { get; }

    /// <summary>Gets the number of warning findings, kept or not.</summary>
    public int WarningCount { get; }

    /// <summary>Gets the number of information findings, kept or not.</summary>
    public int InformationCount { get; }

    /// <summary>Gets the number of findings not kept because <see cref="PdfValidatorOptions.FindingCapacity"/> was reached.</summary>
    public int SuppressedCount => ErrorCount + WarningCount + InformationCount - Findings.Count;

    /// <summary>Gets a value indicating whether any finding is an error: readers will disagree about the document.</summary>
    public bool HasErrors => ErrorCount > 0;

    /// <summary>Gets a value indicating whether any finding is a warning.</summary>
    public bool HasWarnings => WarningCount > 0;

    /// <summary>Determines whether a rule reported anything, whether or not its findings were kept.</summary>
    /// <param name="ruleId">One of <see cref="PdfValidationRuleIds"/>.</param>
    public bool Contains(string ruleId)
    {
        ArgumentNullException.ThrowIfNull(ruleId);
        return _ruleIds.Contains(ruleId);
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{ProfileName} {ProfileVersion}: errors {ErrorCount}, warnings {WarningCount}, information {InformationCount}");
}
