using System.Text;

namespace AdCodicem.Pdf.Validation.Arlington;

/// <summary>
/// A name of the tables: a key, a plain value, or the key of a discriminator plan, as ASCII bytes compared ordinally
/// with text.
/// </summary>
internal readonly struct ArlingtonName : IEquatable<ArlingtonName>
{
    private readonly ushort _id;

    internal ArlingtonName(int id) => _id = (ushort)id;

    /// <summary>Gets the name's number in the tables.</summary>
    public int Id => _id;

    /// <summary>Gets the name's ASCII bytes.</summary>
    public ReadOnlySpan<byte> Bytes => ArlingtonModel.NameAt(_id);

    /// <summary>Whether the name is <paramref name="text"/>, compared ordinally.</summary>
    public bool Is(ReadOnlySpan<char> text) => ArlingtonModel.Compare(Bytes, text) == 0;

    /// <summary>
    /// Reads the name as an array index (<c>0</c>, <c>12</c>) or as a repeating group's (<c>1*</c>); false for any
    /// other name.
    /// </summary>
    public bool TryGetIndex(out int index)
    {
        var bytes = Bytes;

        if (!bytes.IsEmpty && bytes[^1] == (byte)'*')
        {
            bytes = bytes[..^1];
        }

        index = 0;

        if (bytes.IsEmpty || bytes.Length > 4)
        {
            return false;
        }

        foreach (var b in bytes)
        {
            if (b is < (byte)'0' or > (byte)'9')
            {
                index = 0;
                return false;
            }

            index = (index * 10) + (b - '0');
        }

        return true;
    }

    /// <inheritdoc/>
    public bool Equals(ArlingtonName other) => _id == other._id;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ArlingtonName other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _id;

    /// <summary>Returns the name as text. It allocates: for a message, once a finding needs it.</summary>
    public override string ToString() => Encoding.ASCII.GetString(Bytes);

    /// <summary>Compares two names.</summary>
    public static bool operator ==(ArlingtonName left, ArlingtonName right) => left.Equals(right);

    /// <summary>Compares two names.</summary>
    public static bool operator !=(ArlingtonName left, ArlingtonName right) => !left.Equals(right);
}
