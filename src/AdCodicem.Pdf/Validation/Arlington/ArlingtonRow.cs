using System.Globalization;
using System.Numerics;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation.Arlington;

/// <summary>
/// One row of an Arlington object: a dictionary's key, or an array's element, with its types, versions, requirement,
/// the objects its values may be, and its plain values.
/// </summary>
internal readonly struct ArlingtonRow : IEquatable<ArlingtonRow>
{
    private readonly ushort _object;
    private readonly ushort _index;

    internal ArlingtonRow(int objectIndex, int index)
    {
        _object = (ushort)objectIndex;
        _index = (ushort)index;
    }

    /// <summary>Gets the object the row belongs to.</summary>
    public ArlingtonObject Object => new(_object);

    /// <summary>Gets the row's number among all the rows of the model.</summary>
    public int Index => _index;

    /// <summary>Gets the row's position in its object.</summary>
    public int Position => _index - Object.FirstRow;

    /// <summary>Gets the key: a name, an array index, <c>N*</c> for a member of a repeating group, <c>*</c> for the wildcard.</summary>
    public ArlingtonName Key => new(ArlingtonModel.U16(Record, ArlingtonLayout.RowKey));

    /// <summary>Gets the types a value may have, version and extension wrappers stripped.</summary>
    public ArlingtonTypes Types =>
        (ArlingtonTypes)(uint)(Record[ArlingtonLayout.RowTypes] | (Record[ArlingtonLayout.RowTypes + 1] << 8) | (Record[ArlingtonLayout.RowTypes + 2] << 16));

    /// <summary>Gets the ISO version that introduced the key, or <see cref="ArlingtonVersion.ExtensionOnly"/>.</summary>
    public byte Since => Record[ArlingtonLayout.RowSince];

    /// <summary>Gets whether the key belongs to an extension only, and to no ISO version.</summary>
    public bool IsExtensionOnly => Since == ArlingtonVersion.ExtensionOnly;

    /// <summary>Gets the version that deprecated the key, or <see cref="ArlingtonVersion.None"/>.</summary>
    public byte DeprecatedIn => Record[ArlingtonLayout.RowDeprecated];

    /// <summary>Gets the row's flags.</summary>
    public ArlingtonRowFlags Flags => (ArlingtonRowFlags)Record[ArlingtonLayout.RowFlags];

    /// <summary>Gets whether the key is required, as the model's <c>Required</c> column says once reduced.</summary>
    public ArlingtonRequirement Requirement => (ArlingtonRequirement)(byte)(Flags & ArlingtonRowFlags.RequirementMask);

    /// <summary>Gets the first version a <see cref="ArlingtonRequirement.InVersions"/> requirement holds in, or <see cref="ArlingtonVersion.None"/>.</summary>
    public byte RequiredFrom =>
        Requirement == ArlingtonRequirement.InVersions ? ArlingtonModel.ExtrasOf(_index)[ArlingtonLayout.ExtraRequiredFrom] : ArlingtonVersion.None;

    /// <summary>Gets the version it stops holding in, or <see cref="ArlingtonVersion.Unbounded"/>.</summary>
    public byte RequiredBefore =>
        Requirement == ArlingtonRequirement.InVersions ? ArlingtonModel.ExtrasOf(_index)[ArlingtonLayout.ExtraRequiredBefore] : ArlingtonVersion.Unbounded;

    /// <summary>Gets what <c>overrides.tsv</c> asks of the walk for this row, beyond the edits already in the tables.</summary>
    public ArlingtonOverride Overrides =>
        (Flags & ArlingtonRowFlags.HasExtras) != 0 ? (ArlingtonOverride)ArlingtonModel.ExtrasOf(_index)[ArlingtonLayout.ExtraOverride] : ArlingtonOverride.None;

    /// <summary>Gets whether the key is inheritable.</summary>
    public bool IsInheritable => (Flags & ArlingtonRowFlags.Inheritable) != 0;

    /// <summary>Gets whether the row is the wildcard, <c>*</c>.</summary>
    public bool IsWildcard => (Flags & ArlingtonRowFlags.Wildcard) != 0;

    /// <summary>Gets whether the row is a member of an array's repeating group, <c>N*</c>.</summary>
    public bool IsRepeating => (Flags & ArlingtonRowFlags.Repeating) != 0;

    /// <summary>Gets whether the key points back up the object graph, so that the walk types nothing through it.</summary>
    public bool IsBackLink => (Flags & ArlingtonRowFlags.BackLink) != 0;

    /// <summary>Gets whether the key of a dictionary is one of those that tell an object's type apart.</summary>
    public bool IsDiscriminator => (Flags & ArlingtonRowFlags.Discriminator) != 0;

    /// <summary>
    /// Gets whether the model requires the key whatever the version: <c>TRUE</c>, for an ISO key. What the walk's
    /// scoring counts, where the file's version does not enter.
    /// </summary>
    public bool IsUnconditionallyRequired => Requirement == ArlingtonRequirement.Yes && !IsExtensionOnly;

    /// <summary>
    /// Gets the element an array's row describes: its index for a fixed element, <c>N</c> for a member <c>N*</c> of
    /// the repeating group; -1 for the wildcard and for a dictionary's row.
    /// </summary>
    public int ElementIndex => Object.IsArray && !IsWildcard && Key.TryGetIndex(out var index) ? index : -1;

    /// <summary>Gets how many types of the row link objects.</summary>
    public int LinkCount => Record[ArlingtonLayout.RowLinkCount];

    /// <summary>Gets how many plain values the row has, all its types together.</summary>
    public int ValueCount => Record[ArlingtonLayout.RowValueCount];

    private ReadOnlySpan<byte> Record => ArlingtonModel.RowRecord(_index);

    /// <summary>
    /// Whether the key is required in <paramref name="version"/>: <c>TRUE</c> from the key's ISO version on, never for
    /// an extension-only key; a version-only predicate in its range. Never for the wildcard or a repeating group's
    /// member, which no single key stands for. The caller decides what an unknown version means.
    /// </summary>
    public bool IsRequiredIn(byte version)
    {
        if (IsWildcard || IsRepeating)
        {
            return false;
        }

        return Requirement switch
        {
            ArlingtonRequirement.Yes => !IsExtensionOnly && Since <= version,
            ArlingtonRequirement.InVersions => RequiredFrom <= version && version < RequiredBefore,
            _ => false,
        };
    }

    /// <summary>Whether the key is deprecated in <paramref name="version"/>: deprecated in it, or in an earlier one.</summary>
    public bool IsDeprecatedIn(byte version) => DeprecatedIn != ArlingtonVersion.None && DeprecatedIn <= version;

    /// <summary>
    /// Finds the first of the row's types among <paramref name="accepted"/> (<see cref="ArlingtonModel.TypesAccepting"/>
    /// of a value): the type the value is read as, whose links and plain values apply. False when none is: the value's
    /// type is not one the row allows.
    /// </summary>
    public bool TryMatch(ArlingtonTypes accepted, out ArlingtonType type)
    {
        var common = (uint)(Types & accepted);

        if (common == 0)
        {
            type = default;
            return false;
        }

        type = (ArlingtonType)BitOperations.TrailingZeroCount(common);
        return true;
    }

    /// <summary>Gets the row's link group at <paramref name="position"/>, in the order of the row's types.</summary>
    public ArlingtonLink GetLink(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, LinkCount);
        return new ArlingtonLink(FirstLink + position);
    }

    /// <summary>Finds the objects a value of <paramref name="type"/> may be; false when the row links none for it.</summary>
    public bool TryGetLink(ArlingtonType type, out ArlingtonLink link)
    {
        var first = FirstLink;

        for (var i = 0; i < LinkCount; i++)
        {
            link = new ArlingtonLink(first + i);

            if (link.Type == type)
            {
                return true;
            }
        }

        link = default;
        return false;
    }

    /// <summary>Gets the row's plain value at <paramref name="position"/>, in the order of the row's types, then ordinal.</summary>
    public ArlingtonValue GetValue(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, ValueCount);
        return new ArlingtonValue(FirstValue + position);
    }

    /// <summary>Whether <paramref name="type"/> has plain values in this row.</summary>
    public bool HasValuesFor(ArlingtonType type)
    {
        var first = FirstValue;

        for (var i = 0; i < ValueCount; i++)
        {
            if (new ArlingtonValue(first + i).Type == type)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="text"/> is a plain value of <paramref name="type"/> in this row.</summary>
    public bool HasValue(ArlingtonType type, ReadOnlySpan<char> text)
    {
        var first = FirstValue;

        for (var i = 0; i < ValueCount; i++)
        {
            var value = new ArlingtonValue(first + i);

            if (value.Type == type && value.Text.Is(text))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="value"/> is a plain value of <paramref name="type"/> in this row, read as the model
    /// writes values: a name by its text, an integer in decimal, a boolean as <c>true</c> or <c>false</c>. Any other
    /// value (a real, a string, an array) is never one: the model's plain values are tokens.
    /// </summary>
    public bool HasValue(ArlingtonType type, PdfObject value)
    {
        if (value is PdfName name)
        {
            return HasValue(type, name.Value);
        }

        Span<char> text = stackalloc char[20];
        return TryWriteToken(value, text, out var written) && HasValue(type, text[..written]);
    }

    /// <summary>Whether <paramref name="value"/>, read as <see cref="HasValue(ArlingtonType, PdfObject)"/> reads it, is a plain value of any of the row's types.</summary>
    public bool HasValueOfAnyType(PdfObject value)
    {
        if (value is PdfName name)
        {
            return HasValueOfAnyType(name.Value);
        }

        Span<char> text = stackalloc char[20];
        return TryWriteToken(value, text, out var written) && HasValueOfAnyType(text[..written]);
    }

    /// <summary>Whether <paramref name="text"/> is a plain value of any of the row's types: what a discriminator plan asks.</summary>
    public bool HasValueOfAnyType(ReadOnlySpan<char> text)
    {
        var first = FirstValue;

        for (var i = 0; i < ValueCount; i++)
        {
            if (new ArlingtonValue(first + i).Text.Is(text))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public bool Equals(ArlingtonRow other) => _index == other._index;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ArlingtonRow other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _index;

    /// <summary>Returns the row as <c>PageObject/MediaBox</c>. It allocates: for a message, once a finding needs it.</summary>
    public override string ToString() => $"{Object}/{Key}";

    /// <summary>Compares two rows.</summary>
    public static bool operator ==(ArlingtonRow left, ArlingtonRow right) => left.Equals(right);

    /// <summary>Compares two rows.</summary>
    public static bool operator !=(ArlingtonRow left, ArlingtonRow right) => !left.Equals(right);

    // Writes an integer or a boolean as the model writes a plain value; false for anything else.
    private static bool TryWriteToken(PdfObject value, Span<char> buffer, out int written)
    {
        switch (value)
        {
            case PdfInteger integer:
                return integer.Value.TryFormat(buffer, out written, provider: CultureInfo.InvariantCulture);
            case PdfBoolean boolean:
                written = boolean.Value ? 4 : 5;
                (boolean.Value ? "true" : "false").AsSpan().CopyTo(buffer);
                return true;
            default:
                written = 0;
                return false;
        }
    }

    // A row's links and values follow those of the rows before it in its object: the tables keep counts, not starts,
    // so that one row's change is one line of each table (ArlingtonLayout).
    private int FirstLink
    {
        get
        {
            var owner = Object;
            var start = owner.FirstLink;

            for (var row = owner.FirstRow; row < _index; row++)
            {
                start += ArlingtonModel.RowRecord(row)[ArlingtonLayout.RowLinkCount];
            }

            return start;
        }
    }

    private int FirstValue
    {
        get
        {
            var owner = Object;
            var start = owner.FirstValue;

            for (var row = owner.FirstRow; row < _index; row++)
            {
                start += ArlingtonModel.RowRecord(row)[ArlingtonLayout.RowValueCount];
            }

            return start;
        }
    }
}
