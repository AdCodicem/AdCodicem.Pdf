namespace AdCodicem.Pdf.IO;

/// <summary>
/// Compares numbers as numbers, and hashes them through <see cref="HashCode"/>, whose seed .NET draws once a process,
/// as it draws another for strings: a table keyed by numbers a file chose spreads them as it would random keys.
/// </summary>
/// <remarks>
/// An <see cref="int"/> is its own hash, and .NET picks a key's bucket as its hash modulo a prime from a table anyone can
/// read: object numbers a multiple of that prime apart all fall in one bucket, and every lookup walks the lot (#155). A
/// <see cref="long"/> hashes as its two halves exclusive-ored, which lets a file give distinct offsets one hash outright,
/// and a tuple hashes as its items do: each is hashed here with both halves of a <see cref="long"/> mixed in apart.
/// Every hash table keyed by a number, or by a tuple of numbers — whatever they number, wherever they come from —
/// takes this comparer, handed to it last and straight, which two tests hold, one reading the compiled library and the
/// other its sources.
/// </remarks>
internal sealed class SeededComparer :
    IEqualityComparer<int>,
    IEqualityComparer<long>,
    IEqualityComparer<(long, int)>,
    IEqualityComparer<(long, bool)>,
    IEqualityComparer<(long, EndObjState)>,
    IEqualityComparer<(PdfLimit, long)>
{
    /// <summary>Gets the comparer.</summary>
    public static SeededComparer Instance { get; } = new();

    private SeededComparer()
    {
    }

    /// <inheritdoc/>
    public bool Equals(int x, int y) => x == y;

    /// <inheritdoc/>
    public int GetHashCode(int obj) => HashCode.Combine(obj);

    /// <inheritdoc/>
    public bool Equals(long x, long y) => x == y;

    /// <inheritdoc/>
    public int GetHashCode(long obj) => HashCode.Combine((int)obj, (int)(obj >> 32));

    /// <inheritdoc/>
    public bool Equals((long, int) x, (long, int) y) => x == y;

    /// <inheritdoc/>
    public int GetHashCode((long, int) obj) => HashCode.Combine((int)obj.Item1, (int)(obj.Item1 >> 32), obj.Item2);

    /// <inheritdoc/>
    public bool Equals((long, bool) x, (long, bool) y) => x == y;

    /// <inheritdoc/>
    public int GetHashCode((long, bool) obj) => HashCode.Combine((int)obj.Item1, (int)(obj.Item1 >> 32), obj.Item2);

    /// <inheritdoc/>
    public bool Equals((long, EndObjState) x, (long, EndObjState) y) => x == y;

    /// <inheritdoc/>
    public int GetHashCode((long, EndObjState) obj) =>
        HashCode.Combine((int)obj.Item1, (int)(obj.Item1 >> 32), (int)obj.Item2);

    /// <inheritdoc/>
    public bool Equals((PdfLimit, long) x, (PdfLimit, long) y) => x == y;

    /// <inheritdoc/>
    public int GetHashCode((PdfLimit, long) obj) =>
        HashCode.Combine((int)obj.Item1, (int)obj.Item2, (int)(obj.Item2 >> 32));
}
