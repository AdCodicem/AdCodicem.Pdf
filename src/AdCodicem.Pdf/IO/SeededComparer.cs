namespace AdCodicem.Pdf.IO;

/// <summary>
/// Compares numbers as numbers, and hashes them through the seed .NET draws once a process, as it does strings: a table
/// keyed by numbers a file chose spreads them as it would random keys.
/// </summary>
/// <remarks>
/// An <see cref="int"/> is its own hash, and .NET picks a key's bucket as its hash modulo a prime from a table anyone can
/// read: object numbers a multiple of that prime apart all fall in one bucket, and every lookup walks the lot (#155).
/// Every hash table keyed by a number — whatever it numbers, wherever it comes from — takes this comparer, which a test
/// holds over the sources.
/// </remarks>
internal sealed class SeededComparer : IEqualityComparer<int>
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
}
