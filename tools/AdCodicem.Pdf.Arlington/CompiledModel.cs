using System.Text;
using AdCodicem.Pdf.Validation.Arlington;

namespace AdCodicem.Pdf.Arlington;

/// <summary>The model reduced to what the tables hold, overrides applied: what the generator encodes.</summary>
internal sealed class CompiledModel
{
    public CompiledModel(string commit, IReadOnlyList<CompiledObject> objects, IReadOnlyList<CandidateSet> candidateSets)
    {
        Commit = commit;
        Objects = objects;
        CandidateSets = candidateSets;
    }

    /// <summary>Gets the commit of the model the tables come from.</summary>
    public string Commit { get; }

    /// <summary>Gets the objects, in ordinal order of their names: an object's position is its number.</summary>
    public IReadOnlyList<CompiledObject> Objects { get; }

    /// <summary>Gets the distinct lists of more than one candidate the links name, in the order they first appear.</summary>
    public IReadOnlyList<CandidateSet> CandidateSets { get; }
}

/// <summary>One object of the model.</summary>
internal sealed class CompiledObject
{
    public CompiledObject(string name, bool isArray, IReadOnlyList<CompiledRow> rows)
    {
        Name = name;
        IsArray = isArray;
        Rows = rows;
    }

    /// <summary>Gets the object's name.</summary>
    public string Name { get; }

    /// <summary>Gets whether the object is an array, rather than a dictionary or a stream.</summary>
    public bool IsArray { get; }

    /// <summary>
    /// Gets the rows: a dictionary's in ordinal order of their keys, an array's in the model's order (fixed elements,
    /// then the repeating group, then the wildcard).
    /// </summary>
    public IReadOnlyList<CompiledRow> Rows { get; }
}

/// <summary>One row, reduced.</summary>
internal sealed class CompiledRow
{
    /// <summary>Gets the object the row belongs to.</summary>
    public required string ObjectName { get; init; }

    /// <summary>Gets the key: a name, <c>*</c>, an array index, or <c>N*</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the types, wrappers stripped.</summary>
    public required ArlingtonTypes Types { get; init; }

    /// <summary>Gets the ISO version that introduced the key, or <see cref="ArlingtonVersion.ExtensionOnly"/>.</summary>
    public required byte Since { get; init; }

    /// <summary>Gets the version that deprecated the key, or <see cref="ArlingtonVersion.None"/>.</summary>
    public required byte Deprecated { get; init; }

    /// <summary>Gets whether the key is required.</summary>
    public required ArlingtonRequirement Requirement { get; init; }

    /// <summary>Gets the first version a version-only requirement holds in, or <see cref="ArlingtonVersion.None"/>.</summary>
    public required byte RequiredFrom { get; init; }

    /// <summary>Gets the version it stops holding in, or <see cref="ArlingtonVersion.Unbounded"/>.</summary>
    public required byte RequiredBefore { get; init; }

    /// <summary>Gets the row's flags, the requirement bits aside.</summary>
    public required ArlingtonRowFlags Flags { get; init; }

    /// <summary>Gets, per type that links objects, its candidates, in the order of the types.</summary>
    public required IReadOnlyList<CompiledLink> Links { get; init; }

    /// <summary>Gets the plain values, in the order of the types, then ordinal.</summary>
    public required IReadOnlyList<CompiledValue> Values { get; init; }

    /// <summary>Gets what the overrides ask of the walk for this row.</summary>
    public ArlingtonOverride Overrides { get; init; }

    /// <summary>Gets whether an override edited one of the row's cells.</summary>
    public bool Edited { get; init; }

    /// <summary>Gets whether the row goes into the extras table.</summary>
    public bool HasExtras => Requirement == ArlingtonRequirement.InVersions || Overrides != ArlingtonOverride.None;

    /// <summary>Gets the whole flags byte the table holds.</summary>
    public ArlingtonRowFlags EncodedFlags =>
        Flags | (ArlingtonRowFlags)(byte)Requirement | (HasExtras ? ArlingtonRowFlags.HasExtras : ArlingtonRowFlags.None);

    /// <summary>Says what the row holds, for comparing two reductions of it: an edit that changes nothing is stale.</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        text.Append(Key).Append(' ').Append((uint)Types).Append(' ').Append(Since).Append(' ').Append(Deprecated)
            .Append(' ').Append(Requirement).Append(' ').Append(RequiredFrom).Append(' ').Append(RequiredBefore)
            .Append(' ').Append(Flags);

        foreach (var link in Links)
        {
            text.Append(" L").Append(link.Type).Append('=').AppendJoin(',', link.Candidates);
        }

        foreach (var value in Values)
        {
            text.Append(" V").Append(value.Type).Append('=').Append(value.Text);
        }

        return text.ToString();
    }
}

/// <summary>The objects a value of one type may be.</summary>
internal sealed record CompiledLink(ArlingtonType Type, IReadOnlyList<string> Candidates);

/// <summary>A plain value, and the type it is a value of.</summary>
internal sealed record CompiledValue(ArlingtonType Type, string Text);

/// <summary>
/// A list of more than one candidate a link names, and the discriminator plan of its array candidates and of its
/// other candidates: the key (an array's element <c>0</c> or <c>1</c>, or a dictionary's key) whose plain values
/// tell those candidates apart, or null when no single key does.
/// </summary>
internal sealed record CandidateSet(IReadOnlyList<string> Members, string? ArrayPlan, string? OtherPlan);
