using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.XRef;

namespace AdCodicem.Pdf.Validation;

/// <summary>
/// What the rules of one validation share: the document, its bytes, and the findings reported so far.
/// </summary>
/// <remarks>
/// It lives for one validation. What several rules need is worked out once and shared: the probe of every entry
/// of the file's index, which four cross-reference rules read; the walk of the page tree, which the page tree rules
/// and the object rules read; the walk of the objects reachable from the trailer; the walk that checks their shapes
/// against the Arlington PDF Model; and which object streams need themselves to be read. None of them keeps the
/// objects it resolved: those stay in the reader's cache, bounded by <see cref="PdfReaderOptions.ObjectCacheCapacity"/>,
/// and each analysis keeps what it found wrong.
/// </remarks>
internal sealed class ValidationContext
{
    private readonly int _capacity;
    private CrossReferenceProbe? _probe;
    private PageTreeWalk? _pageTree;
    private ObjectGraph? _graph;
    private ObjectStreamDependencies? _objectStreams;
    private ArlingtonWalk? _arlington;
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

    /// <summary>Gets what the file's own structure looked like as the document opened.</summary>
    public FileStructure Structure => Document.Reader.Structure;

    /// <summary>
    /// Gets what probing every entry of the file's own index found, worked out the first time a rule asks for it.
    /// </summary>
    public CrossReferenceProbe Probe => _probe ??= CrossReferenceProbe.Run(Document);

    /// <summary>Gets the walk of the document's page tree, made the first time a rule asks for it.</summary>
    public PageTreeWalk PageTree => _pageTree ??= PageTreeWalk.Run(Document);

    /// <summary>Gets the walk of the objects reachable from the trailer, made the first time a rule asks for it.</summary>
    public ObjectGraph Graph => _graph ??= ObjectGraph.Run(Document, PageTree);

    /// <summary>
    /// Gets the walk that types the objects reachable from the trailer in the Arlington PDF Model and checks them, made
    /// the first time a rule asks for it.
    /// </summary>
    public ArlingtonWalk Arlington => _arlington ??= ArlingtonWalk.Run(Document, PageTree);

    /// <summary>Gets which object streams need themselves to be read, worked out the first time a rule asks for it.</summary>
    public ObjectStreamDependencies ObjectStreams => _objectStreams ??= ObjectStreamDependencies.Run(Document);

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
