using System.Collections;

namespace AdCodicem.Pdf.Diagnostics;

/// <summary>
/// Collects what the library noticed while working on a document.
/// </summary>
/// <remarks>
/// Diagnostics are returned, not logged: they are part of the result, so they can be inspected,
/// serialized and asserted on in tests. A malformed file that the reader repaired is a normal outcome,
/// not an exception — but it is never a silent one.
/// </remarks>
public sealed class PdfDiagnostics : IReadOnlyList<PdfDiagnostic>
{
    private readonly List<PdfDiagnostic> _entries = [];
    private int _suppressed;
    private int _limits;

    /// <summary>
    /// The reports decoding a stream moved into this instance, each with how many copies of it this instance holds, so that a
    /// stream decoded again adds none it holds already (#144); null until a decoding reports. They are entries held, so
    /// they are at most <see cref="Capacity"/>.
    /// </summary>
    private Dictionary<PdfDiagnostic, int>? _decodingReports;

    /// <summary>Where a decoding of a stream reports before its reports are moved here; null until a stream is decoded.</summary>
    private PdfDiagnostics? _decodings;

    /// <summary>
    /// Gets the largest number of entries kept. A pathological file can produce an unbounded
    /// number of observations; beyond this limit they are counted but not stored.
    /// </summary>
    public int Capacity { get; init; } = 1000;

    /// <inheritdoc/>
    public int Count => _entries.Count;

    /// <summary>
    /// Gets the number of entries dropped because <see cref="Capacity"/> was reached. A fault the reader meets again, as it
    /// parses an object again, is counted again once the capacity is reached, whether or not it was kept before.
    /// </summary>
    public int SuppressedCount => _suppressed;

    /// <summary>
    /// Gets the diagnostics what this instance holds is moved into once kept, which can be full before this one is; null
    /// when it is kept as it is.
    /// </summary>
    internal PdfDiagnostics? KeptIn { get; init; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Capacity"/> is reached, by this instance's entries or, when it is kept in
    /// another, by that one's: what is added now is counted and dropped, here or once kept, so a message built only for it
    /// need not be. The two are not summed: what is held here may be dropped on its way, as a report already kept is, and a
    /// report must read the same whenever it is made, for a repeat of it to be known.
    /// </summary>
    internal bool IsFull => _entries.Count >= Capacity || KeptIn is { IsFull: true };

    /// <summary>
    /// Gets the number of guards reached (<see cref="ReachLimit"/>), whether their reports were kept or dropped: what a guard
    /// cut is the guard's, not the file's, though the capacity left no room to say so.
    /// </summary>
    internal int LimitsReached => _limits;

    /// <inheritdoc/>
    public PdfDiagnostic this[int index] => _entries[index];

    /// <summary>Gets a value indicating whether the file needed repairing.</summary>
    public bool HasRepairs => Contains(PdfDiagnosticSeverity.Repair);

    /// <summary>Gets a value indicating whether anything worrying was observed.</summary>
    public bool HasWarnings => Contains(PdfDiagnosticSeverity.Warning);

    /// <summary>Gets a value indicating whether a conformance guarantee was lost.</summary>
    public bool HasConformanceLoss => Contains(PdfDiagnosticSeverity.ConformanceLoss);

    /// <summary>Records an observation.</summary>
    public void Add(PdfDiagnosticSeverity severity, string code, string message, long position = -1)
    {
        if (_entries.Count >= Capacity)
        {
            _suppressed++;
            return;
        }

        _entries.Add(new PdfDiagnostic(severity, code, message, position));
    }

    /// <summary>Records a repair the reader performed.</summary>
    public void Repair(string code, string message, long position = -1) =>
        Add(PdfDiagnosticSeverity.Repair, code, message, position);

    /// <summary>Records a warning.</summary>
    public void Warn(string code, string message, long position = -1) =>
        Add(PdfDiagnosticSeverity.Warning, code, message, position);

    /// <summary>Records that a guard was reached, as a warning under its <c>limit.*</c> code, and counts it.</summary>
    internal void ReachLimit(string code, string message, long position)
    {
        _limits++;
        Warn(code, message, position);
    }

    /// <summary>Records where the entries stand, so that what follows can be kept or dropped as one.</summary>
    internal PdfDiagnosticsMark GetMark() => new(_entries.Count, _suppressed, _limits);

    /// <summary>Drops every entry recorded since <paramref name="mark"/>, suppressed ones and guards reached included.</summary>
    internal void RollBack(PdfDiagnosticsMark mark)
    {
        _entries.RemoveRange(mark.Count, _entries.Count - mark.Count);
        _suppressed = mark.Suppressed;
        _limits = mark.Limits;
    }

    /// <summary>
    /// Moves every entry recorded since <paramref name="mark"/> into <paramref name="target"/>, which applies
    /// its own capacity, and drops them from this instance.
    /// </summary>
    internal void MoveTo(PdfDiagnostics target, PdfDiagnosticsMark mark)
    {
        for (var i = mark.Count; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            target.Add(entry.Severity, entry.Code, entry.Message, entry.Position);
        }

        target._suppressed += _suppressed - mark.Suppressed;
        target._limits += _limits - mark.Limits;
        RollBack(mark);
    }

    /// <summary>
    /// Moves the entries recorded since <paramref name="mark"/> that <paramref name="keep"/> keeps into
    /// <paramref name="target"/>, which applies its own capacity, counts there those this instance dropped, and drops the
    /// rest.
    /// </summary>
    /// <param name="target">The diagnostics the entries kept go to.</param>
    /// <param name="mark">Where the entries to move start.</param>
    /// <param name="state">What <paramref name="keep"/> needs besides the entry, so that it captures nothing.</param>
    /// <param name="keep">Whether an entry is kept.</param>
    internal void MoveTo<TState>(PdfDiagnostics target, PdfDiagnosticsMark mark, TState state, Func<PdfDiagnostic, TState, bool> keep)
    {
        for (var i = mark.Count; i < _entries.Count; i++)
        {
            var entry = _entries[i];

            if (keep(entry, state))
            {
                target.Add(entry.Severity, entry.Code, entry.Message, entry.Position);
            }
        }

        target._suppressed += _suppressed - mark.Suppressed;
        target._limits += _limits - mark.Limits;
        RollBack(mark);
    }

    /// <summary>
    /// Gets where a decoding of a stream reports first, its reports then moved into this instance by
    /// <see cref="MoveDecodingReportsTo"/>: a report a decoding moved there already takes no room here while it is
    /// dropped. It holds what one decoding reports, within the same capacity.
    /// </summary>
    internal PdfDiagnostics Decodings => _decodings ??= new PdfDiagnostics { Capacity = Capacity, KeptIn = this };

    /// <summary>
    /// Moves the entries a decoding of a stream recorded since <paramref name="mark"/> into <paramref name="target"/>, which
    /// applies its own capacity, but those a decoding moved there already: <paramref name="target"/> holds each once,
    /// however often the stream is decoded, and two filters of one decoding that report alike are each held (#144). A guard
    /// reached stays counted in <see cref="LimitsReached"/>, its report moved or not, so that what it cut is still known as
    /// its; an entry the capacity dropped was counted, and a repeat of it is counted again, as any report is.
    /// </summary>
    internal void MoveDecodingReportsTo(PdfDiagnostics target, PdfDiagnosticsMark mark)
    {
        Dictionary<PdfDiagnostic, int>? met = null;

        for (var i = mark.Count; i < _entries.Count; i++)
        {
            // The copies of a report up to as many as the target holds are repeats; one past them is new, held if the target
            // has room for it, and counted among those it left out otherwise, whichever decoding meets it.
            var entry = _entries[i];
            var held = target._decodingReports ??= [];
            met ??= [];
            var copy = met[entry] = met.GetValueOrDefault(entry) + 1;

            if (copy <= held.GetValueOrDefault(entry))
            {
                continue;
            }

            var count = target._entries.Count;
            target.Add(entry.Severity, entry.Code, entry.Message, entry.Position);

            if (target._entries.Count > count)
            {
                held[entry] = held.GetValueOrDefault(entry) + 1;
            }
        }

        target._suppressed += _suppressed - mark.Suppressed;
        target._limits += _limits - mark.Limits;
        RollBack(mark);
    }

    /// <summary>Determines whether any entry carries the given code.</summary>
    public bool Contains(string code)
    {
        foreach (var entry in _entries)
        {
            if (string.Equals(entry.Code, code, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool Contains(PdfDiagnosticSeverity severity)
    {
        foreach (var entry in _entries)
        {
            if (entry.Severity == severity)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns a non-allocating enumerator over the entries.</summary>
    public List<PdfDiagnostic>.Enumerator GetEnumerator() => _entries.GetEnumerator();

    IEnumerator<PdfDiagnostic> IEnumerable<PdfDiagnostic>.GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _entries.GetEnumerator();
}
