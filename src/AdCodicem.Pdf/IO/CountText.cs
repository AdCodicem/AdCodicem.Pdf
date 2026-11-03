using System.Globalization;

namespace AdCodicem.Pdf.IO;

/// <summary>
/// Says in a message how many, and how far: a count agreed with its noun — <c>1 byte</c>, <c>3 bytes</c> —, and a
/// distance with the way it runs — <c>1 byte before it</c>, <c>3 bytes after it</c> —, for the reader's diagnostics and
/// the validator's findings alike (#241).
/// </summary>
/// <remarks>
/// A count is written in digits and in the invariant culture, as every number of a message is: <c>1 byte</c>, never
/// <c>one byte</c>, nor a culture's own minus sign or separators. A message that groups a count's digits by thousands
/// writes it through <see cref="Grouped"/>, one that does not through <see cref="Of"/>; the noun agrees with it either
/// way. A distance is never negative: the way it runs is said in words.
/// </remarks>
internal static class CountText
{
    /// <summary>Says a number of bytes: <c>1 byte</c>, <c>12 bytes</c>.</summary>
    public static string Bytes(long count) => Of(count, "byte", "bytes");

    /// <summary>Says a number of objects: <c>1 object</c>, <c>3 objects</c>.</summary>
    public static string Objects(long count) => Of(count, "object", "objects");

    /// <summary>Says a number of pages: <c>1 page</c>, <c>3 pages</c>.</summary>
    public static string Pages(long count) => Of(count, "page", "pages");

    /// <summary>
    /// Says <paramref name="count"/>, its digits as they are, with the noun that agrees with it: <paramref name="one"/>
    /// for 1, <paramref name="many"/> for any other count.
    /// </summary>
    public static string Of(long count, string one, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? one : many)}");

    /// <summary>
    /// Says <paramref name="count"/> as <see cref="Of"/> does, its digits grouped by thousands: <c>1 section</c>,
    /// <c>1,024 sections</c>.
    /// </summary>
    public static string Grouped(long count, string one, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{count:N0} {(count == 1 ? one : many)}");

    /// <summary>
    /// Says how far, and which way, something was found from where <paramref name="from"/> says it was named, the
    /// distance <paramref name="delta"/> counted from there: <c>1 byte before where the index said</c>,
    /// <c>12 bytes after it</c>.
    /// </summary>
    /// <remarks>
    /// Every caller states a move, so none gives a distance of zero; one would be said to run after.
    /// </remarks>
    public static string Distance(long delta, string from) =>
        delta < 0 ? $"{Bytes(-delta)} before {from}" : $"{Bytes(delta)} after {from}";
}
