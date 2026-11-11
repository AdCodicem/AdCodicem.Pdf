using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Files whose cross-reference streams give data their filters decode nothing of: a filter the library does not know,
/// or Flate data corrupt before its first byte decodes. Such data holds none of the section's rows, whatever its bytes
/// would read as (#221). The same files, with a stream under no filter or with sound zlib data, set them against.
/// Offsets are counted from the header, which starts the file.
/// </summary>
internal static class UndecodedStreamFiles
{
    /// <summary>
    /// A document saved twice. The first revision writes the catalog, an empty page tree and object 3,
    /// <c>(three, as the oldest table gives it)</c>, and a classic table at <c>Oldest</c> that indexes them. The second writes
    /// cross-reference stream 5 at <c>Stream</c>, which <c>startxref</c> names: its <c>/Index</c> numbers object 3 alone, in
    /// fields of 1, 4 and 2 bytes, its <c>/Prev</c> names the oldest table, its <c>/Filter</c> is <paramref name="filter"/>,
    /// none when empty, and its data <paramref name="data"/>, one byte to a character.
    /// </summary>
    public static (byte[] File, long Oldest, long Stream) OverAnOlderTable(string filter, string data)
    {
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var three = file.Object(3, "(three, as the oldest table gives it)");
        var oldest = file.Position;
        file.Raw(Invariant(
            $"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(three)}trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        var stream = file.Position;
        file.Object(5, Invariant(
            $"<< /Type /XRef /Size 6 /Index [3 1] /W [1 4 2] /Root 1 0 R /Prev {oldest}{Filter(filter)} /Length {data.Length} >>\nstream\n{data}\nendstream"));
        file.Raw(Invariant($"startxref\n{stream}\n%%EOF\n"));
        return (file.Bytes, oldest, stream);
    }

    /// <summary>
    /// A document saved twice, as a writer saves it. The first revision writes the catalog, an empty page tree and object 4,
    /// <c>(old four)</c>, and a classic table at <c>Oldest</c> that indexes them. The second writes object 4 again,
    /// <c>(new four)</c>, and cross-reference stream 6 at <c>Stream</c>, which <c>startxref</c> names, whose <c>/Prev</c>
    /// names the oldest table and whose rows, zlib data of <c>/Filter /FlateDecode</c>, place object 4's new revision and
    /// the stream itself. Where <paramref name="damagedAt"/> is given, the byte there of the zlib data is turned to its
    /// complement — the third, the first of the deflate data past the zlib header, makes it corrupt before any row decodes.
    /// </summary>
    public static (byte[] File, long Oldest, long Stream) UpdateInZlibData(int? damagedAt)
    {
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var oldFour = file.Object(4, "(old four)");
        var oldest = file.Position;
        file.Raw(Invariant(
            $"xref\n0 3\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}4 1\n{Row(oldFour)}trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        var newFour = file.Object(4, "(new four)");
        var stream = file.Position;
        var data = Zlib(StreamRows((1, newFour, 0), (1, stream, 0)), damagedAt);
        file.Object(6, Invariant(
            $"<< /Type /XRef /Size 7 /Index [4 1 6 1] /W [1 4 2] /Root 1 0 R /Prev {oldest} /Filter /FlateDecode /Length {data.Length} >>\nstream\n{data}\nendstream"));
        file.Raw(Invariant($"startxref\n{stream}\n%%EOF\n"));
        return (file.Bytes, oldest, stream);
    }

    /// <summary>
    /// A document indexed by one cross-reference stream, stream 3 at <c>Stream</c>, which <c>startxref</c> names: its rows,
    /// zlib data of <c>/Filter /FlateDecode</c>, place the catalog, an empty page tree and the stream itself, and its third
    /// byte, the first of the deflate data past the zlib header, is turned to its complement, which makes the data corrupt
    /// before any row decodes. No trailer keyword follows: the stream's dictionary alone gives <c>/Root</c>.
    /// </summary>
    public static (byte[] File, long Stream) AloneInDamagedZlibData()
    {
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var stream = file.Position;
        var data = Zlib(StreamRows((0, 0, 65535), (1, catalog, 0), (1, pages, 0), (1, stream, 0)), damagedAt: 2);
        file.Object(3, Invariant(
            $"<< /Type /XRef /Size 4 /W [1 4 2] /Root 1 0 R /Filter /FlateDecode /Length {data.Length} >>\nstream\n{data}\nendstream"));
        file.Raw(Invariant($"startxref\n{stream}\n%%EOF\n"));
        return (file.Bytes, stream);
    }

    /// <summary>
    /// A document saved twice, its update a hybrid file's. The first revision writes the catalog, an empty page tree and
    /// object 3, <c>(old three)</c>, and a classic table at <c>Oldest</c> that indexes them. Past a comment of 1,200 bytes,
    /// the second writes object 3 again, <c>(new three)</c>, and cross-reference stream 5 at <c>Stream</c>, whose
    /// <c>/Index</c> numbers object 3 alone and whose data, <c>xxxxxxx</c> under <c>/Filter /FlateDecode</c>, decodes to
    /// nothing; the table that names it depends on <paramref name="naming"/>:
    /// <list type="bullet">
    /// <item><c>/XRefStm</c>: a table, which <c>startxref</c> names, indexes no object and names the oldest table through
    /// <c>/Prev</c> and the stream through <c>/XRefStm</c>;</item>
    /// <item><c>/Prev and /XRefStm</c>: the stream names the oldest table through <c>/Prev</c>, and past another comment, a
    /// table that indexes object 6, <c>(six)</c>, names the stream through <c>/Prev</c> and through <c>/XRefStm</c> both;</item>
    /// <item><c>/XRefStm then /Prev</c>: the stream names the oldest table through <c>/Prev</c>; past another comment, a
    /// table that indexes object 6 names it through <c>/Prev</c>; and past a third, the newest table, which indexes object 7,
    /// <c>(seven)</c>, names that table through <c>/Prev</c> and the stream through <c>/XRefStm</c>.</item>
    /// </list>
    /// </summary>
    public static (byte[] File, long Stream) NamedByTables(string naming)
    {
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var oldThree = file.Object(3, "(old three)");
        var oldest = file.Position;
        file.Raw(Invariant(
            $"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(oldThree)}trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        Comment(file);
        file.Object(3, "(new three)");
        var stream = file.Position;
        var previous = naming == "/XRefStm" ? string.Empty : Invariant($" /Prev {oldest}");
        file.Object(5, Invariant(
            $"<< /Type /XRef /Size 6 /Index [3 1] /W [1 4 2] /Root 1 0 R{previous} /Filter /FlateDecode /Length 7 >>\nstream\nxxxxxxx\nendstream"));

        if (naming == "/XRefStm")
        {
            var table = file.Position;
            file.Raw(Invariant(
                $"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 6 /Root 1 0 R /Prev {oldest} /XRefStm {stream} >>\nstartxref\n{table}\n%%EOF\n"));
            return (file.Bytes, stream);
        }

        Comment(file);
        var six = file.Object(6, "(six)");
        var sixTable = file.Position;
        var hybrid = naming == "/Prev and /XRefStm" ? Invariant($" /XRefStm {stream}") : string.Empty;
        file.Raw(Invariant(
            $"xref\n0 1\n0000000000 65535 f \n6 1\n{Row(six)}trailer\n<< /Size 7 /Root 1 0 R /Prev {stream}{hybrid} >>\nstartxref\n{sixTable}\n%%EOF\n"));

        if (naming == "/Prev and /XRefStm")
        {
            return (file.Bytes, stream);
        }

        Comment(file);
        var seven = file.Object(7, "(seven)");
        var newest = file.Position;
        file.Raw(Invariant(
            $"xref\n0 1\n0000000000 65535 f \n7 1\n{Row(seven)}trailer\n<< /Size 8 /Root 1 0 R /Prev {sixTable} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, stream);
    }

    /// <summary>
    /// Writes <paramref name="rows"/>, one byte to a character, as zlib data, the byte at <paramref name="damagedAt"/>, when
    /// one is given, turned to its complement.
    /// </summary>
    public static string Zlib(string rows, int? damagedAt = null)
    {
        using var packed = new MemoryStream();

        using (var zlib = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(Encoding.Latin1.GetBytes(rows));
        }

        var data = packed.ToArray();

        if (damagedAt is { } at)
        {
            data[at] = (byte)~data[at];
        }

        return Encoding.Latin1.GetString(data);
    }

    /// <summary>Writes cross-reference stream rows in fields of 1, 4 and 2 bytes, one byte to a character.</summary>
    public static string StreamRows(params (int Type, long Field2, int Field3)[] rows)
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

    private static string Filter(string filter) => filter.Length == 0 ? string.Empty : " /Filter " + filter;

    /// <summary>Writes a comment of 1,200 bytes, which keeps what follows more than 512 bytes from what precedes it.</summary>
    private static void Comment(HandWrittenFile file) => file.Raw("%" + new string('-', 1198) + "\n");

    /// <summary>Writes a classic table's row placing an object in use at <paramref name="offset"/>.</summary>
    private static string Row(long offset) => Invariant($"{offset:D10} 00000 n \n");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
