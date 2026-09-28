namespace AdCodicem.Pdf.Validation.Arlington;

/// <summary>
/// What a row's <c>Link</c> column says for one of its types: the objects a value of that type may be, one or
/// several candidates, and for several, the discriminator plan the generator worked out.
/// </summary>
internal readonly struct ArlingtonLink : IEquatable<ArlingtonLink>
{
    private readonly ushort _index;

    internal ArlingtonLink(int index) => _index = (ushort)index;

    /// <summary>Gets the type whose values the candidates are.</summary>
    public ArlingtonType Type => (ArlingtonType)Record[ArlingtonLayout.LinkType];

    /// <summary>Gets whether the link names several candidates, the walk choosing among them.</summary>
    public bool IsCandidateSet => (Target & ArlingtonLayout.CandidateSetFlag) != 0;

    /// <summary>Gets the number of the candidate set the link names, shared by every link naming the same list; -1 for one candidate.</summary>
    public int CandidateSet => IsCandidateSet ? Target & ~ArlingtonLayout.CandidateSetFlag : -1;

    /// <summary>Gets how many candidates the link names, of every kind: the walk keeps those of the value's kind.</summary>
    public int CandidateCount => IsCandidateSet ? SetRecord[ArlingtonLayout.SetMemberCount] : 1;

    private int Target => ArlingtonModel.U16(Record, ArlingtonLayout.LinkTarget);

    private ReadOnlySpan<byte> Record => ArlingtonModel.LinkRecord(_index);

    private ReadOnlySpan<byte> SetRecord => ArlingtonModel.CandidateSetRecord(CandidateSet);

    /// <summary>Gets the candidate at <paramref name="position"/>, in the model's order.</summary>
    public ArlingtonObject GetCandidate(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, CandidateCount);

        return IsCandidateSet
            ? new ArlingtonObject(ArlingtonModel.SetMember(ArlingtonModel.U16(SetRecord, ArlingtonLayout.SetFirstMember) + position))
            : new ArlingtonObject(Target);
    }

    /// <summary>
    /// Finds the discriminator plan of the candidates of one kind, arrays or not: the key (a dictionary's key, or an
    /// array's element <c>0</c> or <c>1</c>) whose plain values tell every candidate of that kind apart, no two sharing
    /// one. False when fewer than two candidates are of that kind, or no single key tells them apart: the walk then
    /// scores them.
    /// </summary>
    public bool TryGetPlan(bool arrays, out ArlingtonName key)
    {
        if (IsCandidateSet)
        {
            var id = ArlingtonModel.U16(SetRecord, arrays ? ArlingtonLayout.SetArrayPlan : ArlingtonLayout.SetOtherPlan);

            if (id != ArlingtonLayout.NoPlan)
            {
                key = new ArlingtonName(id);
                return true;
            }
        }

        key = default;
        return false;
    }

    /// <inheritdoc/>
    public bool Equals(ArlingtonLink other) => _index == other._index;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ArlingtonLink other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _index;

    /// <summary>Compares two links.</summary>
    public static bool operator ==(ArlingtonLink left, ArlingtonLink right) => left.Equals(right);

    /// <summary>Compares two links.</summary>
    public static bool operator !=(ArlingtonLink left, ArlingtonLink right) => !left.Equals(right);
}
