using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Writes a PDF from a template whose offsets are placeholders, so that a test can damage one byte of a file's
/// structure and leave every other offset right.
/// </summary>
/// <remarks>
/// <para>
/// Placeholders: <c>{free}</c>, the free head of a table, <c>0000000000 65535 f </c>; <c>{row:N}</c> and
/// <c>{row:N:G}</c>, a row placing object <c>N</c> where its header <c>N G obj</c> starts, and <c>{row:N:G:D}</c>,
/// the same off by <c>D</c> bytes; <c>{off:N}</c>, the offset of object <c>N</c>'s header; <c>{xref:K}</c>, the offset
/// of the <c>K</c>-th <c>xref</c> keyword that starts a line. A row is twenty bytes with its line feed.
/// </para>
/// <para>
/// Lines end with a line feed whatever the source file's line endings; the placeholders are replaced until the
/// offsets they give no longer move.
/// </para>
/// </remarks>
internal static partial class PdfTemplate
{
    /// <summary>A one-page document, sound, with a classic table.</summary>
    public const string Sound = """
        %PDF-1.7
        1 0 obj
        << /Type /Catalog /Pages 2 0 R >>
        endobj
        2 0 obj
        << /Type /Pages /Kids [3 0 R] /Count 1 >>
        endobj
        3 0 obj
        << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>
        endobj
        xref
        0 4
        {free}
        {row:1}
        {row:2}
        {row:3}
        trailer
        << /Size 4 /Root 1 0 R >>
        startxref
        {xref:1}
        %%EOF

        """;

    /// <summary>Writes the template, its placeholders replaced.</summary>
    public static byte[] Build(string template)
    {
        var text = template.Replace("\r\n", "\n", StringComparison.Ordinal);
        var result = text;

        for (var pass = 0; pass < 8; pass++)
        {
            var current = result;
            var next = Placeholder().Replace(text, match => Replace(match, current));

            if (next == result)
            {
                break;
            }

            result = next;
        }

        return Encoding.Latin1.GetBytes(result);
    }

    /// <summary>Writes the sound template with one piece of text replaced by another, before offsets are worked out.</summary>
    public static byte[] SoundWith(string text, string replacement)
    {
        Sound.Should().Contain(text);
        return Build(Sound.Replace(text, replacement, StringComparison.Ordinal));
    }

    /// <summary>Gets where <paramref name="text"/> starts in <paramref name="file"/>.</summary>
    public static long OffsetOf(byte[] file, string text, int occurrence = 1)
    {
        var content = Encoding.Latin1.GetString(file);
        var index = -1;

        for (var found = 0; found < occurrence; found++)
        {
            index = content.IndexOf(text, index + 1, StringComparison.Ordinal);
            index.Should().BeGreaterThanOrEqualTo(0, $"'{text}' occurs {occurrence} times");
        }

        return index;
    }

    private static string Replace(Match match, string current)
    {
        var parts = match.Groups["arguments"].Value.Split(':', StringSplitOptions.RemoveEmptyEntries);

        switch (match.Groups["name"].Value)
        {
            case "free":
                return "0000000000 65535 f ";

            case "row":
                var number = int.Parse(parts[0], CultureInfo.InvariantCulture);
                var generation = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
                var error = parts.Length > 2 ? long.Parse(parts[2], CultureInfo.InvariantCulture) : 0;
                return string.Create(CultureInfo.InvariantCulture, $"{HeaderOffset(current, number) + error:D10} {generation:D5} n ");

            case "off":
                return HeaderOffset(current, int.Parse(parts[0], CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);

            case "xref":
                return KeywordOffset(current, int.Parse(parts[0], CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);

            default:
                throw new InvalidOperationException($"Unknown placeholder {match.Value}.");
        }
    }

    /// <summary>Where the header of object <paramref name="number"/> starts, at the start of a line; 0 while it is not written yet.</summary>
    private static long HeaderOffset(string text, int number)
    {
        var match = Regex.Match(text, string.Create(CultureInfo.InvariantCulture, $@"(?<=\n|^){number} \d+ obj\b"));
        return match.Success ? match.Index : 0;
    }

    private static long KeywordOffset(string text, int occurrence)
    {
        var matches = Regex.Matches(text, @"(?<=\n)xref\n");
        return matches.Count >= occurrence ? matches[occurrence - 1].Index : 0;
    }

    [GeneratedRegex(@"\{(?<name>free|row|off|xref)(?::(?<arguments>[-0-9:]+))?\}")]
    private static partial Regex Placeholder();
}
