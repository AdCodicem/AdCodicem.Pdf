using System.Globalization;
using System.Text;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Files whose chain names, through <c>/Prev</c>, cross-reference streams of <c>/Type /XRef</c> whose dictionary reads
/// whole and whose rows cannot be read, each naming an older section through a <c>/Prev</c> of its own, which the chain goes
/// on through (#208). Offsets are counted from the header, which starts the file.
/// </summary>
internal static class LostStreamFiles
{
    /// <summary>The dictionary of a stream whose rows cannot be read, as <see cref="ThreeRevisions"/> writes it by default.</summary>
    public const string Unreadable = "/Type /XRef /Size 9 /Index [4 2 7 1] /W [1 -4 2]";

    /// <summary>
    /// A document saved three times. The first revision writes the catalog, an empty page tree, object 3
    /// <c>(only in the oldest)</c>, object 7 <c>(old seven)</c> and object 8 <c>(old eight)</c>, and a classic table at
    /// <c>Oldest</c> that indexes them in two subsections, 0 to 3 and 7 to 8. Past <paramref name="before"/> bytes of
    /// comment, the second writes object 4 <c>(only in the stream)</c>, object 7 again, <c>(new seven)</c>, and
    /// cross-reference stream 5 at <c>Stream</c>, its header written <paramref name="header"/> where one is given and
    /// <c>5 0 obj</c> otherwise, whose dictionary holds <paramref name="entries"/>,
    /// <c>/Root 1 0 R</c>, the <c>/Prev</c> <paramref name="prev"/> writes and the <c>/Length</c> of its data: the rows
    /// of objects 4, 5 and 7, in fields of 1, 4 and 2 bytes, whatever <c>/W</c> the entries give, or
    /// <paramref name="written"/> when it is given. Past <paramref name="after"/> bytes of comment, the third writes
    /// object 6 <c>(only in the newest)</c>, and object 9 as <paramref name="nine"/> when one is given, and a classic
    /// table at <c>Newest</c> that indexes them and names the stream through <c>/Prev</c>.
    /// </summary>
    /// <param name="entries">The stream's dictionary, but for its <c>/Root</c>, <c>/Prev</c> and <c>/Length</c>.</param>
    /// <param name="prev">
    /// What the stream's <c>/Prev</c> is written as, <c>{oldest}</c> and <c>{stream}</c> standing for those offsets: the
    /// oldest table's offset when null, and no <c>/Prev</c> at all when empty.
    /// </param>
    /// <param name="before">The bytes of comment between the oldest table and the second revision.</param>
    /// <param name="after">The bytes of comment between the stream and the third revision.</param>
    /// <param name="nine">What object 9 holds, which the newest table indexes; none when null.</param>
    /// <param name="data">Whether stream data follows the stream's dictionary: object 5 is the dictionary alone otherwise.</param>
    /// <param name="shift">How many bytes past object 3's header the oldest table's row for it places it.</param>
    /// <param name="namedShortBy">
    /// How many bytes short of the stream's header the newest table's <c>/Prev</c> names it — 1 for the end of line before
    /// it —, or, where it is negative, how many bytes past the header's first byte.
    /// </param>
    /// <param name="oldestTrailer">What the oldest table's trailer gives besides its <c>/Size</c> and <c>/Root</c>.</param>
    /// <param name="written">The stream's data, one byte to a character, in place of its rows; the rows when null.</param>
    /// <param name="header">The stream's header, <c>5 0 obj</c> when null.</param>
    public static (byte[] File, long Oldest, long Stream, long Newest) ThreeRevisions(
        string entries = Unreadable,
        string? prev = null,
        int before = 1200,
        int after = 1200,
        string? nine = null,
        bool data = true,
        int shift = 0,
        int namedShortBy = 0,
        string oldestTrailer = "",
        string? written = null,
        string? header = null)
    {
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var three = file.Object(3, "(only in the oldest)");
        var oldSeven = file.Object(7, "(old seven)");
        var eight = file.Object(8, "(old eight)");
        var oldest = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(three + shift)}7 2\n{Row(oldSeven)}{Row(eight)}trailer\n<< /Size 9 /Root 1 0 R{oldestTrailer} >>\nstartxref\n{oldest}\n%%EOF\n"));
        Gap(file, before);

        var four = file.Object(4, "(only in the stream)");
        var newSeven = file.Object(7, "(new seven)");
        var stream = file.Position;
        var rows = written ?? StreamRows((1, four, 0), (1, stream, 0), (1, newSeven, 0));
        var named = prev switch
        {
            null => string.Create(CultureInfo.InvariantCulture, $" /Prev {oldest}"),
            "" => string.Empty,
            _ => " /Prev " + prev
                .Replace("{oldest}", oldest.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{stream}", stream.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
        };
        var body = data
            ? string.Create(CultureInfo.InvariantCulture, $"<< {entries} /Root 1 0 R{named} /Length {rows.Length} >>\nstream\n{rows}\nendstream")
            : string.Create(CultureInfo.InvariantCulture, $"<< {entries} /Root 1 0 R{named} >>");

        if (header is null)
        {
            file.Object(5, body);
        }
        else
        {
            file.Raw(header + "\n" + body + "\nendobj\n");
        }
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"startxref\n{stream}\n%%EOF\n"));
        Gap(file, after);

        var six = file.Object(6, "(only in the newest)");
        var nineRow = nine is null ? string.Empty : "9 1\n" + Row(file.Object(9, nine));
        var newest = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \n6 1\n{Row(six)}{nineRow}trailer\n<< /Size {(nine is null ? 9 : 10)} /Root 1 0 R /Prev {stream - namedShortBy} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, stream, newest);
    }

    /// <summary>
    /// A document whose newest table indexes the catalog and an empty page tree, and names through <c>/Prev</c> the first of
    /// <paramref name="streams"/> cross-reference streams of <c>/Type /XRef</c> whose <c>/W</c> gives a negative width, each
    /// naming the next through <c>/Prev</c>, the last naming the oldest section: a classic table of the numbers below
    /// <paramref name="rows"/>, which indexes object 3 <c>(only in the oldest)</c> and each of <paramref name="objects"/>,
    /// <c>(old n)</c>, all its other rows free. Stream <c>s</c>, counted from the newest, declares through its <c>/Index</c>
    /// the subsections <paramref name="subsections"/> gives it: pairs of a first object number and a count.
    /// </summary>
    public static byte[] Chain(int streams, int rows, IReadOnlyCollection<int> objects, Func<int, IEnumerable<(long First, long Count)>> subsections)
    {
        var text = new StringBuilder("%PDF-1.5\n");
        var placed = new Dictionary<int, long>();

        foreach (var number in objects.Append(3))
        {
            placed[number] = text.Length;
            text.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n{(number == 3 ? "(only in the oldest)" : $"(old {number})")}\nendobj\n");
        }

        var previous = (long)text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 {rows}\n0000000000 65535 f \n");

        for (var number = 1; number < rows; number++)
        {
            text.Append(placed.TryGetValue(number, out var at) ? Row(at) : "0000000000 00000 f \n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {rows} >>\n");

        // Written from the oldest, so that each names the one before it; the newest is stream 0.
        for (var s = streams - 1; s >= 0; s--)
        {
            var at = (long)text.Length;
            text.Append(CultureInfo.InvariantCulture, $"{1_000_000 + s} 0 obj\n<< /Type /XRef /Size {rows} /W [1 -4 2] /Index [");

            foreach (var (first, count) in subsections(s))
            {
                text.Append(CultureInfo.InvariantCulture, $"{first} {count} ");
            }

            text.Append(CultureInfo.InvariantCulture, $"] /Prev {previous} /Length 0 >>\nstream\n\nendstream\nendobj\n");
            previous = at;
        }

        var catalog = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = text.Length;
        text.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        var newest = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 3\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}");
        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {rows} /Root 1 0 R /Prev {previous} >>\nstartxref\n{newest}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.ToString());
    }

    /// <summary>
    /// The <see cref="Documents.PdfReaderLimits.MaxTrailerLength"/> an <see cref="OwnNumbers"/> file is read under: its
    /// streams' dictionaries, of 700 subsections, hold some 7 KB of <c>/Index</c>, near what the limit lets one hold.
    /// </summary>
    public const int TrailerLength = 8 * 1024;

    /// <summary>
    /// A file as <see cref="Chain"/> writes it, of <paramref name="streams"/> streams, each declaring <paramref name="each"/>
    /// one-row subsections of its own, every other number from where its run starts, so that no range meets another, none of
    /// them object 3, over an older table of <paramref name="rows"/> rows.
    /// </summary>
    public static byte[] OwnNumbers(int streams, int each, int rows) =>
        Chain(streams, rows, objects: [], s => Enumerable.Range(0, each).Select(i => (10L + (2L * each * s) + (2L * i), 1L)));

    /// <summary>
    /// A file as <see cref="Chain"/> writes it, of one stream declaring 4,096 one-row subsections from object 10, every other
    /// number when <paramref name="apart"/>, so that they are 4,096 ranges, or one after the other otherwise, so that they
    /// meet in one, over an older table of <paramref name="rows"/> rows.
    /// </summary>
    public static byte[] RangesOverOlderRows(bool apart, int rows) =>
        Chain(1, rows, objects: [], _ => Enumerable.Range(0, 4096).Select(i => (10L + ((apart ? 2L : 1L) * i), 1L)));

    /// <summary>Writes a classic table's row placing an object in use at <paramref name="offset"/>.</summary>
    private static string Row(long offset) => string.Create(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");

    /// <summary>Writes a comment of <paramref name="length"/> bytes, its end of line counted, when it is more than none.</summary>
    private static void Gap(HandWrittenFile file, int length)
    {
        if (length > 0)
        {
            file.Raw("%" + new string('-', length - 2) + "\n");
        }
    }

    /// <summary>Writes cross-reference stream rows in fields of 1, 4 and 2 bytes, one byte to a character.</summary>
    private static string StreamRows(params (int Type, long Field2, int Field3)[] rows)
    {
        var data = new StringBuilder();

        foreach (var (type, field2, field3) in rows)
        {
            data.Append((char)type)
                .Append((char)((field2 >> 24) & 0xFF)).Append((char)((field2 >> 16) & 0xFF))
                .Append((char)((field2 >> 8) & 0xFF)).Append((char)(field2 & 0xFF))
                .Append((char)((field3 >> 8) & 0xFF)).Append((char)(field3 & 0xFF));
        }

        return data.ToString();
    }
}
