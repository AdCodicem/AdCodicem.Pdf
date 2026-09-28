using System.Globalization;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation;

/// <summary>
/// Words the rules' messages share: counts of bytes, objects and pages, distances either way, and what kind of value
/// a file wrote where another was due.
/// </summary>
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

    /// <summary>Says a number of pages: <c>1 page</c>, <c>3 pages</c>.</summary>
    public static string Pages(long count) =>
        count == 1 ? "1 page" : string.Create(CultureInfo.InvariantCulture, $"{count} pages");

    /// <summary>Says what kind of value a file wrote: <c>a number</c>, <c>an array</c>, <c>null</c>.</summary>
    public static string Kind(PdfObject value) => value switch
    {
        PdfInteger or PdfReal => "a number",
        PdfName => "a name",
        PdfString => "a string",
        PdfArray => "an array",
        PdfStream => "a stream",
        PdfDictionary => "a dictionary",
        PdfBoolean => "a boolean",
        PdfReference => "a reference",
        _ => "null",
    };

    /// <summary>
    /// Writes a name as the file would, with its solidus, and a null character as <c>#00</c> so that the text stays
    /// readable.
    /// </summary>
    public static string Name(PdfName name) =>
        "/" + (name.Value.Contains('\0', StringComparison.Ordinal) ? name.Value.Replace("\0", "#00", StringComparison.Ordinal) : name.Value);
}
