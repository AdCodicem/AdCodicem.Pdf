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

    /// <summary>
    /// Gets the largest number of entries kept. A pathological file can produce an unbounded
    /// number of observations; beyond this limit they are counted but not stored.
    /// </summary>
    public int Capacity { get; init; } = 1000;

    /// <inheritdoc/>
    public int Count => _entries.Count;

    /// <summary>Gets the number of entries dropped because <see cref="Capacity"/> was reached.</summary>
    public int SuppressedCount => _suppressed;

    /// <summary>
    /// Gets the diagnostics what this instance holds is moved into once kept, whose entries count against what this one can
    /// keep; null when it is kept as it is.
    /// </summary>
    internal PdfDiagnostics? KeptIn { get; init; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Capacity"/> is reached — by this instance's entries and, when it is kept
    /// in another, by that one's —: what is added now is counted and dropped, here or once kept, so a message built only
    /// for it need not be.
    /// </summary>
    internal bool IsFull => _entries.Count + (KeptIn?.Count ?? 0) >= Capacity;

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

    /// <summary>Records where the entries stand, so that what follows can be kept or dropped as one.</summary>
    internal PdfDiagnosticsMark GetMark() => new(_entries.Count, _suppressed);

    /// <summary>Drops every entry recorded since <paramref name="mark"/>, suppressed ones included.</summary>
    internal void RollBack(PdfDiagnosticsMark mark)
    {
        _entries.RemoveRange(mark.Count, _entries.Count - mark.Count);
        _suppressed = mark.Suppressed;
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
