using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.XRef;

namespace AdCodicem.Pdf.Validation;

/// <summary>
/// What the rules of one validation share: the document, its bytes, and the findings reported so far.
/// </summary>
/// <remarks>
/// <para>
/// It lives for one validation. What several rules need is worked out once and shared: the probe of every entry
/// of the file's index, which four cross-reference rules read; the walk of the page tree, which the page tree rules
/// and the object rules read; the walk of the objects reachable from the trailer; the walk that checks their shapes
/// against the Arlington PDF Model; and which object streams need themselves to be read. None of them keeps the
/// objects it resolved: those stay in the reader's cache, bounded by <see cref="PdfReaderOptions.ObjectCacheCapacity"/>.
/// Of what they have still to visit, the object graph keeps the numbers of the objects it claimed and has not read
/// (#168), and the page tree walk the arrays of kids along its path, not the nodes that hold them (#201). The exception is
/// the Arlington walk's queue, which holds the values written inside the objects it has still to check: filed as debt
/// (#309).
/// </para>
/// <para>
/// Each analysis is made with the <see cref="Room"/> the report has when a rule first asks for it, and writes, of each kind
/// of fault it finds, the first that many, counting the rest (<see cref="ProbeFindings"/>): the report could keep no more of
/// them, its room only shrinking as the rules run. Each rule reports what was written for it and adds what was counted
/// (<see cref="Count"/>), so that the report is the one every finding written would have made, and the findings an
/// analysis writes of a hostile file's faults are bounded by <see cref="PdfValidatorOptions.FindingCapacity"/> rather than
/// by the file (#168). Four analyses keep more than the room. Three keep an entry for each fault, as many as the index
/// names objects or object streams: the probe, a record of each object stream it found broken, which their rule filters by
/// what a later analysis finds; the object graph, a count for each object that holds a fault of each kind — a reference to
/// nothing, a name with a null character —, about 48 bytes an object and kind, by which it counts one finding an object;
/// and the walk of the object streams' dependencies, the set of the circular streams, which that filter reads. The fourth
/// is the Arlington walk, a tally per rule and row of the model, which the model bounds, whose messages it writes once its
/// walk is over.
/// </para>
/// <para>
/// A rule that writes its findings itself, one for each section, value or object it judges, asks
/// <see cref="HasRoomFor"/> before writing each, so that a finding the report cannot keep is counted without being
/// written; a rule that writes one finding for the file, or two, writes it whatever the room. A count past what an
/// <see langword="int"/> holds stays at its greatest, the count of findings not kept included, which is kept apart rather
/// than worked out from the counts by severity: one of those stopped at its greatest would make it fall short.
/// </para>
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
    private int _suppressed;

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
    public CrossReferenceProbe Probe => _probe ??= CrossReferenceProbe.Run(Document, Room);

    /// <summary>Gets the walk of the document's page tree, made the first time a rule asks for it.</summary>
    public PageTreeWalk PageTree => _pageTree ??= PageTreeWalk.Run(Document, Room);

    /// <summary>Gets the walk of the objects reachable from the trailer, made the first time a rule asks for it.</summary>
    public ObjectGraph Graph => _graph ??= ObjectGraph.Run(Document, PageTree, Room);

    /// <summary>
    /// Gets the walk that types the objects reachable from the trailer in the Arlington PDF Model and checks them, made
    /// the first time a rule asks for it.
    /// </summary>
    public ArlingtonWalk Arlington => _arlington ??= ArlingtonWalk.Run(Document, PageTree);

    /// <summary>Gets which object streams need themselves to be read, worked out the first time a rule asks for it.</summary>
    public ObjectStreamDependencies ObjectStreams => _objectStreams ??= ObjectStreamDependencies.Run(Document, Room);

    /// <summary>
    /// Gets how many findings the report can still keep: its capacity less those it keeps, none once it is full. It only
    /// shrinks as the rules report, so that an analysis made now need write no more findings of a kind than this.
    /// </summary>
    public int Room => _capacity - _findings.Count;

    /// <summary>
    /// Records a finding of <paramref name="rule"/>, under its identifier and at its severity: kept while the report has
    /// room for it, and counted as not kept past it.
    /// </summary>
    public void Report(IValidationRule rule, PdfValidationLocation location, string message, string? remedy)
    {
        Tally(rule, 1);

        if (_findings.Count < _capacity)
        {
            _findings.Add(new PdfValidationFinding(rule.Id, rule.Severity, location, message, remedy));
        }
        else
        {
            _suppressed = Add(_suppressed, 1);
        }
    }

    /// <summary>
    /// Counts <paramref name="findings"/> findings of <paramref name="rule"/> that the report cannot keep, under its
    /// identifier and at its severity: those an analysis counted past the room the report had when it was made, which it
    /// did not write. None counts nothing, and leaves the rule unnamed.
    /// </summary>
    public void Count(IValidationRule rule, int findings)
    {
        if (findings == 0)
        {
            return;
        }

        Tally(rule, findings);
        _suppressed = Add(_suppressed, findings);
    }

    /// <summary>
    /// Determines whether the report has room for another finding, which a rule that writes its findings itself asks before
    /// writing one; when it has none, counts a finding of <paramref name="rule"/>, which the rule then neither writes nor
    /// reports (#168).
    /// </summary>
    public bool HasRoomFor(IValidationRule rule)
    {
        if (_findings.Count < _capacity)
        {
            return true;
        }

        Count(rule, 1);
        return false;
    }

    /// <summary>Closes the validation, handing what was found to a report.</summary>
    public PdfValidationReport ToReport(ValidationProfile profile) =>
        new(profile, _findings, _ruleIds, _errors, _warnings, _information, _suppressed);

    /// <summary>
    /// Counts <paramref name="findings"/> findings of <paramref name="rule"/>, kept or not, under its identifier and at its
    /// severity.
    /// </summary>
    private void Tally(IValidationRule rule, int findings)
    {
        switch (rule.Severity)
        {
            case PdfValidationSeverity.Error:
                _errors = Add(_errors, findings);
                break;
            case PdfValidationSeverity.Warning:
                _warnings = Add(_warnings, findings);
                break;
            default:
                _information = Add(_information, findings);
                break;
        }

        _ruleIds.Add(rule.Id);
    }

    /// <summary>Adds <paramref name="findings"/> to <paramref name="count"/>, a sum past what an <see langword="int"/> holds staying at its greatest.</summary>
    private static int Add(int count, int findings) => (int)Math.Min((long)count + findings, int.MaxValue);
}
