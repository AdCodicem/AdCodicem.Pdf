using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;

namespace AdCodicem.Pdf.Validation;

/// <summary>
/// What the rules of one validation share: the document, its bytes, and the findings reported so far.
/// </summary>
/// <remarks>
/// It lives for one validation. The shared cache of resolved objects the object-graph rules will need — so
/// that twenty rules that all walk the page tree resolve it once — arrives with the first of them.
/// </remarks>
internal sealed class ValidationContext
{
    private readonly int _capacity;
    private readonly List<PdfValidationFinding> _findings = [];
    private readonly HashSet<string> _ruleIds = new(StringComparer.Ordinal);
    private int _errors;
    private int _warnings;
    private int _information;

    public ValidationContext(PdfDocument document, int capacity)
    {
        Document = document;
        _capacity = capacity;
    }

    /// <summary>Gets the document being validated.</summary>
    public PdfDocument Document { get; }

    /// <summary>Gets the bytes of the file, for the rules that look at the file itself rather than its objects.</summary>
    public PdfFileSource Source => Document.Source;

    /// <summary>Records a finding of <paramref name="rule"/>, under its identifier and at its severity.</summary>
    public void Report(IValidationRule rule, PdfValidationLocation location, string message, string? remedy)
    {
        switch (rule.Severity)
        {
            case PdfValidationSeverity.Error:
                _errors++;
                break;
            case PdfValidationSeverity.Warning:
                _warnings++;
                break;
            default:
                _information++;
                break;
        }

        _ruleIds.Add(rule.Id);

        if (_findings.Count < _capacity)
        {
            _findings.Add(new PdfValidationFinding(rule.Id, rule.Severity, location, message, remedy));
        }
    }

    /// <summary>Closes the validation, handing what was found to a report.</summary>
    public PdfValidationReport ToReport(ValidationProfile profile) =>
        new(profile, _findings, _ruleIds, _errors, _warnings, _information);
}
