using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation;

/// <summary>
/// Words the rules' messages share: counts of bytes, objects and pages, distances either way, what kind of value a file
/// wrote where another was due, and the path to a value. A name the file wrote is quoted through
/// <see cref="IO.FileQuote"/>.
/// </summary>
internal static class RuleText
{
    /// <summary>The steps a path keeps at either end: the first four and the last four, those between counted.</summary>
    /// <remarks>
    /// No guard (ADR 34): it bounds what a message repeats, not what is read. A key quotes at most
    /// <see cref="IO.FileQuote.MaxBytes"/> bytes, but a path can hold as many keys as values nest; the steps at either
    /// end are the ones that locate a value, from its object and to it.
    /// </remarks>
    internal const int PathEnds = 4;

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
    /// Writes a path of keys and indexes, <c>/Resources/Font/F1</c> or <c>[2]/Next</c>, whole up to
    /// <c>2 × <see cref="PathEnds"/></c> steps, and past that its first and last <see cref="PathEnds"/> steps, with how
    /// many lie between: <c>/A/B/C/D (7 steps) /W/X/Y/Z</c>.
    /// </summary>
    /// <remarks>
    /// A step starts at a solidus or a left bracket: a quoted key holds neither, since a quote writes both as
    /// <c>#xx</c>, and the space of the count cannot be read as part of a key.
    /// </remarks>
    public static string Path(StringBuilder path)
    {
        var text = path.ToString();
        var steps = 0;
        var head = -1;
        var tail = -1;
        var total = Steps(text);

        if (total <= 2 * PathEnds)
        {
            return text;
        }

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('/' or '['))
            {
                continue;
            }

            if (steps == PathEnds)
            {
                head = index;
            }

            if (steps == total - PathEnds)
            {
                tail = index;
            }

            steps++;
        }

        var between = total - (2 * PathEnds);
        return string.Create(
            CultureInfo.InvariantCulture, $"{text.AsSpan(0, head)} ({between} {(between == 1 ? "step" : "steps")}) {text.AsSpan(tail)}");

        static int Steps(string text)
        {
            var count = 0;

            foreach (var character in text)
            {
                count += character is '/' or '[' ? 1 : 0;
            }

            return count;
        }
    }
}
