using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation.Arlington;

/// <summary>
/// The Arlington PDF Model's objects, as the tables generated in <c>ArlingtonModel.g.cs</c> hold them: every object
/// the model describes, its rows, and for each row its types, versions, requirement, links and plain values.
/// </summary>
/// <remarks>
/// The tables are data the compiler stores in the assembly: nothing is built when they are first touched, and
/// nothing here allocates. Names are ASCII bytes, compared ordinally with the text of a <see cref="PdfName"/>, so
/// that no name read from a file is ever interned to look one up. The layout is <see cref="ArlingtonLayout"/>'s.
/// </remarks>
internal static partial class ArlingtonModel
{
    /// <summary>Gets the object numbered <paramref name="index"/>, in ordinal order of the objects' names.</summary>
    public static ArlingtonObject GetObject(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ObjectCount);
        return new ArlingtonObject(index);
    }

    /// <summary>Finds the object the model calls <paramref name="name"/>: <c>PageObject</c>.</summary>
    public static bool TryFindObject(ReadOnlySpan<char> name, out ArlingtonObject found)
    {
        int low = 0, high = ObjectCount - 1;

        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var comparison = Compare(NameAt(middle), name);

            if (comparison == 0)
            {
                found = new ArlingtonObject(middle);
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

        found = default;
        return false;
    }

    /// <summary>
    /// The types whose values <paramref name="value"/> can be, by its class alone: an integer is an <c>integer</c>, a
    /// <c>bitmask</c> or a <c>number</c>; a real only a <c>number</c>; a stream only a <c>stream</c>, never a
    /// <c>dictionary</c>; a string any of the string types and a <c>date</c>; an array an <c>array</c>, a
    /// <c>rectangle</c> or a <c>matrix</c>; a dictionary a <c>dictionary</c> or the root of a tree.
    /// </summary>
    /// <remarks>
    /// Class-based on purpose: <see cref="PdfObjectExtensions.AsDictionary"/> would take a stream for its dictionary,
    /// and <see cref="PdfObjectExtensions.AsInteger"/> a real with an integral value for an integer. A reference is
    /// none of the types: resolve it first.
    /// </remarks>
    public static ArlingtonTypes TypesAccepting(PdfObject value) => value switch
    {
        PdfInteger => ArlingtonTypes.Integer | ArlingtonTypes.Bitmask | ArlingtonTypes.Number,
        PdfReal => ArlingtonTypes.Number,
        PdfBoolean => ArlingtonTypes.Boolean,
        PdfName => ArlingtonTypes.Name,
        PdfString => ArlingtonTypes.String | ArlingtonTypes.StringAscii | ArlingtonTypes.StringByte | ArlingtonTypes.StringText | ArlingtonTypes.Date,
        PdfArray => ArlingtonTypes.Array | ArlingtonTypes.Rectangle | ArlingtonTypes.Matrix,
        PdfStream => ArlingtonTypes.Stream,
        PdfDictionary => ArlingtonTypes.Dictionary | ArlingtonTypes.NameTree | ArlingtonTypes.NumberTree,
        PdfNull => ArlingtonTypes.Null,
        _ => ArlingtonTypes.None,
    };

    /// <summary>Compares ASCII bytes of the tables with text ordinally, as <see cref="string.CompareOrdinal(string, string)"/> would.</summary>
    internal static int Compare(ReadOnlySpan<byte> ascii, ReadOnlySpan<char> text)
    {
        var length = Math.Min(ascii.Length, text.Length);

        for (var i = 0; i < length; i++)
        {
            var difference = ascii[i] - text[i];

            if (difference != 0)
            {
                return difference;
            }
        }

        return ascii.Length - text.Length;
    }

    /// <summary>Gets the name numbered <paramref name="id"/>, as ASCII bytes.</summary>
    internal static ReadOnlySpan<byte> NameAt(int id) => Names[NameStarts[id]..NameStarts[id + 1]];

    /// <summary>Reads a little-endian 16-bit number of a table.</summary>
    internal static int U16(ReadOnlySpan<byte> record, int offset) => record[offset] | (record[offset + 1] << 8);

    internal static ReadOnlySpan<byte> ObjectRecord(int index) =>
        Objects.Slice(index * ArlingtonLayout.ObjectSize, ArlingtonLayout.ObjectSize);

    internal static ReadOnlySpan<byte> RowRecord(int index) =>
        Rows.Slice(index * ArlingtonLayout.RowSize, ArlingtonLayout.RowSize);

    internal static ReadOnlySpan<byte> LinkRecord(int index) =>
        Links.Slice(index * ArlingtonLayout.LinkSize, ArlingtonLayout.LinkSize);

    internal static ReadOnlySpan<byte> CandidateSetRecord(int index) =>
        CandidateSets.Slice(index * ArlingtonLayout.SetSize, ArlingtonLayout.SetSize);

    internal static int SetMember(int index) => SetMembers[index];

    internal static ReadOnlySpan<byte> ValueRecord(int index) =>
        Values.Slice(index * ArlingtonLayout.ValueSize, ArlingtonLayout.ValueSize);

    /// <summary>Finds the extras of the row numbered <paramref name="row"/>, which the extras table holds in order of rows.</summary>
    internal static ReadOnlySpan<byte> ExtrasOf(int row)
    {
        var extras = Extras;
        int low = 0, high = (extras.Length / ArlingtonLayout.ExtraSize) - 1;

        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var record = extras.Slice(middle * ArlingtonLayout.ExtraSize, ArlingtonLayout.ExtraSize);
            var at = U16(record, ArlingtonLayout.ExtraRow);

            if (at == row)
            {
                return record;
            }

            if (at < row)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return default;
    }
}
