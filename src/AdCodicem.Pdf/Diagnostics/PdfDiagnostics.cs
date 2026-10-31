using System.Collections;

namespace AdCodicem.Pdf.Diagnostics;

/// <summary>
/// Collects what the library noticed while working on a document.
/// </summary>
/// <remarks>
/// <para>
/// Diagnostics are returned, not logged: they are part of the result, so they can be inspected,
/// serialized and asserted on in tests. A malformed file that the reader repaired is a normal outcome,
/// not an exception — but it is never a silent one.
/// </para>
/// <para>
/// A document's record grows as the document is read, after opening too: an enumeration gives the entries recorded when
/// it began, and the next one those recorded since, while <see cref="Count"/>, the indexer, <see cref="SuppressedCount"/>,
/// <see cref="HasRepairs"/>, <see cref="HasWarnings"/>, <see cref="HasConformanceLoss"/> and <see cref="Contains(string)"/>
/// answer for every entry recorded so far (#167).
/// </para>
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

    /// <summary>
    /// Records where the entries stand, so that what follows can be kept or dropped as one. Rolled back to, this instance
    /// still holds as moved in the reports decodings moved in since: a mark of an instance decodings report into is taken
    /// by <see cref="GetMarkWithDecodingReports"/>.
    /// </summary>
    internal PdfDiagnosticsMark GetMark() => new(_entries.Count, _suppressed, _limits);

    /// <summary>
    /// Records where the entries stand, and which of them decodings of streams moved in: rolled back to, this instance
    /// forgets the decodings' reports it drops, so that a decoding that meets one again moves it in again. It copies what
    /// it records, so it is taken where it is rare, not in a loop.
    /// </summary>
    internal PdfDiagnosticsMark GetMarkWithDecodingReports() =>
        new(_entries.Count, _suppressed, _limits, new Dictionary<PdfDiagnostic, int>(_decodingReports ?? []));

    /// <summary>Drops every entry recorded since <paramref name="mark"/>, suppressed ones and guards reached included.</summary>
    internal void RollBack(PdfDiagnosticsMark mark)
    {
        _entries.RemoveRange(mark.Count, _entries.Count - mark.Count);
        _suppressed = mark.Suppressed;
        _limits = mark.Limits;

        // Copied: the mark keeps what it recorded, whatever this instance holds next.
        if (mark.DecodingReports is { } held)
        {
            _decodingReports = new Dictionary<PdfDiagnostic, int>(held);
        }
    }

    /// <summary>
    /// Drops every entry recorded since <paramref name="mark"/> but the guards reached, their <c>limit.*</c> reports and
    /// their count standing, and those the capacity left out counted as left out still: what a reading met beside a guard
    /// it is to make again is met again, and the guard stands. The reports decodings moved in that the mark recorded are
    /// held again, with the guards' kept, so that a decoding that meets one again does not report it twice.
    /// </summary>
    internal void RollBackBesideLimits(PdfDiagnosticsMark mark)
    {
        var kept = mark.Count;
        var held = mark.DecodingReports is { } recorded ? new Dictionary<PdfDiagnostic, int>(recorded) : null;

        for (var i = mark.Count; i < _entries.Count; i++)
        {
            var entry = _entries[i];

            if (!entry.Code.StartsWith("limit.", StringComparison.Ordinal))
            {
                continue;
            }

            _entries[kept++] = entry;

            if (held is not null && _decodingReports?.ContainsKey(entry) == true)
            {
                held[entry] = held.GetValueOrDefault(entry) + 1;
            }
        }

        // Every guard reached since the mark was reported once, kept as an entry or left out by the capacity.
        var limitsLeftOut = Math.Max(0, _limits - mark.Limits - (kept - mark.Count));
        _entries.RemoveRange(kept, _entries.Count - kept);
        _suppressed = mark.Suppressed + limitsLeftOut;

        if (held is not null)
        {
            _decodingReports = held;
        }
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

    /// <summary>
    /// Returns a non-allocating enumerator over the entries recorded when it is made, in the order they were recorded.
    /// </summary>
    /// <remarks>
    /// What is recorded meanwhile — by a read of the document whose record this is, or by a validation of it, which the
    /// enumeration goes on through — is counted by <see cref="Count"/> at once, and given by the next enumeration, not this
    /// one: the enumeration never throws for it (#167).
    /// </remarks>
    public Enumerator GetEnumerator() => new(_entries);

    IEnumerator<PdfDiagnostic> IEnumerable<PdfDiagnostic>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Enumerates the entries a <see cref="PdfDiagnostics"/> held when the enumeration was made.</summary>
    /// <remarks>
    /// It reads them where they are, without a copy: between two calls a caller makes, a document's record only grows, so
    /// that the entries an enumeration began with stay where it reads them, whatever is recorded after them.
    /// </remarks>
    public struct Enumerator : IEnumerator<PdfDiagnostic>
    {
        private readonly List<PdfDiagnostic> _entries;
        private readonly int _count;
        private int _at;

        /// <summary>
        /// The entry the last step advanced to, read as it advanced: the place it was read at may be withdrawn, or hold
        /// another entry, by the time <see cref="Current"/> is asked.
        /// </summary>
        private PdfDiagnostic _current;

        internal Enumerator(List<PdfDiagnostic> entries)
        {
            _entries = entries;
            _count = entries.Count;
            _at = -1;
        }

        /// <summary>Gets the entry the last call to <see cref="MoveNext"/> advanced to.</summary>
        /// <remarks>
        /// It is read as the enumeration advances, as a list's enumerator reads it: what a read withdraws or records at that
        /// place afterward does not change it (#167). Before the first step, and once the enumeration has ended, it is the
        /// default entry.
        /// </remarks>
        public readonly PdfDiagnostic Current => _current;

        readonly object IEnumerator.Current => Current;

        /// <summary>Advances to the next of the entries the enumeration began with.</summary>
        /// <returns>
        /// <see langword="true"/> if it advanced to an entry; <see langword="false"/> if the enumeration has ended, as it
        /// stays until <see cref="Reset"/>.
        /// </returns>
        /// <remarks>
        /// A read may withdraw what it recorded and record again before it returns — a rebuild finished again from its scan
        /// does —, which only a caller's own <see cref="IO.PdfFileSource"/>, stepping an enumeration of the record from
        /// inside the read, can see: such an enumeration reads, up to the count it began with, the entries held at those
        /// places, which may be those recorded again. Once it has ended, it stays ended, whatever is recorded after (#167).
        /// </remarks>
        public bool MoveNext()
        {
            if (_at + 1 < Math.Min(_count, _entries.Count))
            {
                _at++;
                _current = _entries[_at];
                return true;
            }

            // Past every place it can read: a record that a withdrawal shrank and that grows again does not bring it back.
            _at = _count;
            _current = default;
            return false;
        }

        /// <summary>
        /// Starts the enumeration again from its first place, up to the count it began with: it reads the entries held at
        /// those places, as <see cref="MoveNext"/> says.
        /// </summary>
        public void Reset()
        {
            _at = -1;
            _current = default;
        }

        /// <inheritdoc/>
        public readonly void Dispose()
        {
        }
    }
}
