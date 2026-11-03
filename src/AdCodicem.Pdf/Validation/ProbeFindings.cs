namespace AdCodicem.Pdf.Validation;

/// <summary>
/// The findings an analysis made for one rule, of one kind: how many it counted, and the first of them, as many as the
/// report had room for when the analysis was made, written.
/// </summary>
/// <remarks>
/// <para>
/// A report keeps the findings in the order the rules report them, up to <see cref="PdfValidatorOptions.FindingCapacity"/>,
/// and counts the rest. Its room only shrinks as the rules run, and each rule reports what an analysis kept for it in the
/// order the analysis found it, then counts the rest (<see cref="ValidationContext.Count"/>): of what an analysis made with a
/// room of <c>n</c> found, the report can keep the first <c>n</c> at most, whichever rules report before. The analysis writes
/// those, and counts the others without writing them, so that a fault in each of a million kids costs the walk a count
/// rather than a sentence, and the report is the one every finding written would have made (#168).
/// </para>
/// <para>
/// That holds while every rule reports all an analysis kept for it, in order, before it counts the rest, and reads
/// <see cref="Total"/>, not <see cref="Kept"/>, to know whether there was any: a rule that passes over some of them cannot
/// take its findings from here — <see cref="CrossReferenceProbe.BrokenObjectStreams"/> keeps a record of each for the rule
/// that does.
/// </para>
/// <para>
/// A count past what an <see langword="int"/> holds stays at its greatest, <see cref="Unkept"/> as well as
/// <see cref="Total"/>: billions of faulty values reach it, now that they are counted rather than kept, which a file of
/// tens of kilobytes whose object streams decode to gigabytes can give. The findings are counted in a
/// <see langword="long"/>, stopped at <see cref="int.MaxValue"/> only as a count is read, so that those not written stay
/// at their greatest whatever was written: worked out from a total already stopped, they fell short by the findings
/// written, a thousand at the default capacity (#168).
/// </para>
/// </remarks>
/// <param name="room">How many findings are written: the room the report had when the analysis was made.</param>
internal sealed class ProbeFindings(int room) : IEnumerable<ProbeFinding>
{
    private readonly List<ProbeFinding> _kept = [];

    // Every finding counted, written or not. No file brings a long near its greatest: each finding is a value a walk read,
    // and 2^63 of them would take centuries to read.
    private long _total;

    /// <summary>Gets how many findings were counted, written or not, at most <see cref="int.MaxValue"/>.</summary>
    public int Total => (int)Math.Min(_total, int.MaxValue);

    /// <summary>Gets how many findings were written, the first of those counted.</summary>
    public int Kept => _kept.Count;

    /// <summary>
    /// Gets how many findings were counted past those written, at most <see cref="int.MaxValue"/> whatever was written: what
    /// the rule counts without reporting.
    /// </summary>
    public int Unkept => (int)Math.Min(_total - _kept.Count, int.MaxValue);

    /// <summary>
    /// Counts a finding, and says whether it is to be written: the caller writes it, and hands it to <see cref="Keep"/>, only
    /// then. What changes the analysis's own state is done before, written or not.
    /// </summary>
    public bool Take()
    {
        _total++;
        return _kept.Count < room;
    }

    /// <summary>Keeps a finding <see cref="Take"/> said is to be written.</summary>
    public void Keep(ProbeFinding finding) => _kept.Add(finding);

    /// <summary>Gives an enumerator over the findings written, in the order they were found.</summary>
    public List<ProbeFinding>.Enumerator GetEnumerator() => _kept.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator<ProbeFinding> IEnumerable<ProbeFinding>.GetEnumerator() => _kept.GetEnumerator();

    /// <inheritdoc/>
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _kept.GetEnumerator();
}
