using System.Globalization;

namespace AdCodicem.Pdf.Objects;

/// <summary>Represents a PDF integer.</summary>
public sealed class PdfInteger : PdfObject
{
    private const int CacheMin = -128;
    private const int CacheMax = 1024;

    private static readonly PdfInteger[] Cache = CreateCache();

    /// <summary>The integer zero.</summary>
    public static readonly PdfInteger Zero = Create(0);

    private PdfInteger(long value) => Value = value;

    /// <summary>Gets the integer value.</summary>
    public long Value { get; }

    /// <summary>Returns an instance for <paramref name="value"/>, reusing a cached one for small values.</summary>
    public static PdfInteger Create(long value) =>
        value is >= CacheMin and <= CacheMax ? Cache[(int)value - CacheMin] : new PdfInteger(value);

    private static PdfInteger[] CreateCache()
    {
        var cache = new PdfInteger[CacheMax - CacheMin + 1];
        for (var i = 0; i < cache.Length; i++)
        {
            cache[i] = new PdfInteger(i + CacheMin);
        }

        return cache;
    }

    /// <inheritdoc/>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
