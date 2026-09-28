namespace AdCodicem.Pdf.Validation.Arlington;

/// <summary>One object of the Arlington model (one of its TSV files): a dictionary, a stream or an array, and its rows.</summary>
internal readonly struct ArlingtonObject : IEquatable<ArlingtonObject>
{
    private readonly ushort _index;

    internal ArlingtonObject(int index) => _index = (ushort)index;

    /// <summary>Gets the object's number, in ordinal order of the objects' names.</summary>
    public int Index => _index;

    /// <summary>Gets the object's name as the model gives it, <c>PageObject</c>, in ASCII bytes.</summary>
    public ReadOnlySpan<byte> Name => ArlingtonModel.NameAt(_index);

    /// <summary>Gets whether the object is an array, rather than a dictionary or a stream.</summary>
    public bool IsArray => (Record[ArlingtonLayout.ObjectFlags] & ArlingtonLayout.ObjectIsArray) != 0;

    /// <summary>Gets how many rows the object has: 64 at most.</summary>
    public int RowCount => Record[ArlingtonLayout.ObjectRowCount];

    /// <summary>
    /// Gets how many fixed elements an array's rows describe (elements 0, 1, 2…, the first rows); 0 for a dictionary.
    /// </summary>
    public int FixedElementCount
    {
        get
        {
            if (!IsArray)
            {
                return 0;
            }

            var count = 0;

            while (count < RowCount && (GetRow(count).Flags & (ArlingtonRowFlags.Wildcard | ArlingtonRowFlags.Repeating)) == 0)
            {
                count++;
            }

            return count;
        }
    }

    /// <summary>
    /// Gets how many members an array's repeating group has (rows <c>N*</c>, which follow the fixed elements); 0 when
    /// it has none.
    /// </summary>
    public int RepeatingCount
    {
        get
        {
            var count = 0;

            for (var i = FixedElementCount; i < RowCount && (GetRow(i).Flags & ArlingtonRowFlags.Repeating) != 0; i++)
            {
                count++;
            }

            return count;
        }
    }

    /// <summary>Gets the number of the object's first row.</summary>
    internal int FirstRow => ArlingtonModel.U16(Record, ArlingtonLayout.ObjectFirstRow);

    /// <summary>Gets the number of the object's first link group.</summary>
    internal int FirstLink => ArlingtonModel.U16(Record, ArlingtonLayout.ObjectFirstLink);

    /// <summary>Gets the number of the object's first plain value.</summary>
    internal int FirstValue => ArlingtonModel.U16(Record, ArlingtonLayout.ObjectFirstValue);

    private ReadOnlySpan<byte> Record => ArlingtonModel.ObjectRecord(_index);

    /// <summary>
    /// Gets the row at <paramref name="position"/>: a dictionary's rows are in ordinal order of their keys; an
    /// array's fixed elements come first, then its repeating group, then its wildcard.
    /// </summary>
    public ArlingtonRow GetRow(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, RowCount);
        return new ArlingtonRow(_index, FirstRow + position);
    }

    /// <summary>Finds the row whose key is exactly <paramref name="key"/>: a wildcard row answers only for <c>*</c>.</summary>
    public bool TryFindRow(ReadOnlySpan<char> key, out ArlingtonRow row)
    {
        var first = FirstRow;
        var count = RowCount;

        if (IsArray)
        {
            for (var i = 0; i < count; i++)
            {
                if (ArlingtonModel.Compare(KeyAt(first + i), key) == 0)
                {
                    row = new ArlingtonRow(_index, first + i);
                    return true;
                }
            }

            row = default;
            return false;
        }

        int low = first, high = first + count - 1;

        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var comparison = ArlingtonModel.Compare(KeyAt(middle), key);

            if (comparison == 0)
            {
                row = new ArlingtonRow(_index, middle);
                return true;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        row = default;
        return false;
    }

    /// <summary>
    /// Finds the row of <paramref name="key"/>, or else the wildcard row that stands for any other key; false when the
    /// object has neither, and so says nothing of the key.
    /// </summary>
    public bool TryFindRowOrWildcard(ReadOnlySpan<char> key, out ArlingtonRow row) =>
        TryFindRow(key, out row) || TryGetWildcard(out row);

    /// <summary>Finds the wildcard row, <c>*</c>: any other key of a dictionary, any other element of an array.</summary>
    public bool TryGetWildcard(out ArlingtonRow row) => TryFindRow("*", out row);

    /// <summary>Whether the object's name is <paramref name="name"/>.</summary>
    public bool NameIs(ReadOnlySpan<char> name) => ArlingtonModel.Compare(Name, name) == 0;

    /// <inheritdoc/>
    public bool Equals(ArlingtonObject other) => _index == other._index;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ArlingtonObject other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _index;

    /// <summary>Returns the object's name. It allocates: for a message, once a finding needs it.</summary>
    public override string ToString() => System.Text.Encoding.ASCII.GetString(Name);

    /// <summary>Compares two objects.</summary>
    public static bool operator ==(ArlingtonObject left, ArlingtonObject right) => left.Equals(right);

    /// <summary>Compares two objects.</summary>
    public static bool operator !=(ArlingtonObject left, ArlingtonObject right) => !left.Equals(right);

    private static ReadOnlySpan<byte> KeyAt(int row) =>
        ArlingtonModel.NameAt(ArlingtonModel.U16(ArlingtonModel.RowRecord(row), ArlingtonLayout.RowKey));
}
