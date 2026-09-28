namespace AdCodicem.Pdf.Validation.Arlington;

/// <summary>
/// One plain value of a row's <c>PossibleValues</c>, and the type it is a value of: a name (<c>Catalog</c>), an
/// integer (<c>1</c>) or a boolean (<c>true</c>), as the model writes it.
/// </summary>
internal readonly struct ArlingtonValue : IEquatable<ArlingtonValue>
{
    private readonly ushort _index;

    internal ArlingtonValue(int index) => _index = (ushort)index;

    /// <summary>Gets the type the value is a value of.</summary>
    public ArlingtonType Type => (ArlingtonType)Record[ArlingtonLayout.ValueType];

    /// <summary>Gets the value as the model writes it.</summary>
    public ArlingtonName Text => new(ArlingtonModel.U16(Record, ArlingtonLayout.ValueName));

    private ReadOnlySpan<byte> Record => ArlingtonModel.ValueRecord(_index);

    /// <inheritdoc/>
    public bool Equals(ArlingtonValue other) => _index == other._index;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ArlingtonValue other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _index;

    /// <summary>Compares two values.</summary>
    public static bool operator ==(ArlingtonValue left, ArlingtonValue right) => left.Equals(right);

    /// <summary>Compares two values.</summary>
    public static bool operator !=(ArlingtonValue left, ArlingtonValue right) => !left.Equals(right);
}
