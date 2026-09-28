using System.Globalization;

namespace AdCodicem.Pdf.Validation;

/// <summary>Words the rules' messages share: counts of bytes and of objects, and distances either way.</summary>
internal static class RuleText
{
    /// <summary>Says a number of bytes: <c>1 byte</c>, <c>12 bytes</c>.</summary>
    public static string Bytes(long count) =>
        count == 1 ? "1 byte" : string.Create(CultureInfo.InvariantCulture, $"{count} bytes");

    /// <summary>Says how far, and which way, something lies from where it was named: <c>12 bytes after it</c>.</summary>
    public static string Distance(long delta) =>
        delta < 0 ? $"{Bytes(-delta)} before it" : $"{Bytes(delta)} after it";

    /// <summary>Says a number of objects: <c>1 object</c>, <c>3 objects</c>.</summary>
    public static string Objects(long count) =>
        count == 1 ? "1 object" : string.Create(CultureInfo.InvariantCulture, $"{count} objects");
}
