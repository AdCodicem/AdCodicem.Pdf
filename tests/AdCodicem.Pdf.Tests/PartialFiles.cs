using System.Globalization;
using System.Text;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Files whose chain holds a cross-reference section the reader keeps though it reads it only in part — a table the end of
/// the file cuts before its trailer (#207), a table whose row that is no row ends a subsection, a stream that holds fewer
/// rows than it declares (#210) — or one it cannot read, beside older revisions whose rows must not stand for what the
/// section lost; and tables that hold no subsection (#222). Offsets are counted from the header, which starts the file.
/// </summary>
internal static class PartialFiles
{
    /// <summary>
    /// #207's shape: the catalog, an empty page tree and object 4, <c>(object 4)</c>, then a table that indexes object 4
    /// alone, which nothing names; object 3, <c>(object 3)</c>; an update whose table indexes the catalog and the page tree
    /// and names through <c>/Prev</c> the table that ends the file, <c>xref:3</c>, of which the end of the file leaves
    /// <paramref name="rows"/>: its trailer, which would have named the older table, is lost.
    /// </summary>
    public static string CutUpdate(string rows = "3 1\n{row:3}\n") => """
        %PDF-1.7
        1 0 obj
        << /Type /Catalog /Pages 2 0 R >>
        endobj
        2 0 obj
        << /Type /Pages /Kids [] /Count 0 >>
        endobj
        4 0 obj
        (object 4)
        endobj
        xref
        4 1
        {row:4}
        trailer
        << /Size 5 >>
        3 0 obj
        (object 3)
        endobj
        xref
        0 3
        {free}
        {row:1}
        {row:2}
        trailer
        << /Size 5 /Root 1 0 R /Prev {xref:3} >>
        startxref
        {xref:2}
        %%EOF
        xref

        """ + rows;

    /// <summary>
    /// #207's shape where <c>startxref</c> names the table the end of the file cuts, <c>xref:2</c>, after the rows of objects
    /// 0 to 3: an older table, which nothing names, indexes the catalog, the page tree and object 4, and its trailer gives
    /// <c>/Root</c>; the cut table's trailer is lost.
    /// </summary>
    public const string CutFirst = """
        %PDF-1.7
        1 0 obj
        << /Type /Catalog /Pages 2 0 R >>
        endobj
        2 0 obj
        << /Type /Pages /Kids [] /Count 0 >>
        endobj
        4 0 obj
        (object 4)
        endobj
        xref
        0 3
        {free}
        {row:1}
        {row:2}
        4 1
        {row:4}
        trailer
        << /Size 5 /Root 1 0 R >>
        3 0 obj
        (object 3)
        endobj
        startxref
        {xref:2}
        %%EOF
        xref
        0 4
        {free}
        {row:1}
        {row:2}
        {row:3}

        """;

    /// <summary>
    /// The catalog, an empty page tree, objects 3 and 4, and the only table, which <c>startxref</c> names and the end of the
    /// file cuts after the rows of objects 0 to 3: no trailer the file holds gives <c>/Root</c>.
    /// </summary>
    public const string CutAlone = """
        %PDF-1.7
        1 0 obj
        << /Type /Catalog /Pages 2 0 R >>
        endobj
        2 0 obj
        << /Type /Pages /Kids [] /Count 0 >>
        endobj
        3 0 obj
        (object 3)
        endobj
        4 0 obj
        (object 4)
        endobj
        startxref
        {xref:1}
        %%EOF
        xref
        0 4
        {free}
        {row:1}
        {row:2}
        {row:3}

        """;

    /// <summary>
    /// #210's shape: the catalog, an empty page tree, objects 3 and 4, and a table of their rows whose last, object 4's,
    /// ends in <c>x</c>, followed by its trailer; when <paramref name="updated"/>, an update's table that indexes nothing
    /// names it through <c>/Prev</c>, and <c>startxref</c> names it otherwise.
    /// </summary>
    public static string RowEndingInX(bool updated) => """
        %PDF-1.7
        1 0 obj
        << /Type /Catalog /Pages 2 0 R >>
        endobj
        2 0 obj
        << /Type /Pages /Kids [] /Count 0 >>
        endobj
        3 0 obj
        (object 3)
        endobj
        4 0 obj
        (object 4)
        endobj
        xref
        0 5
        {free}
        {row:1}
        {row:2}
        {row:3}
        {off:4} 00000 x
        trailer
        << /Size 5 /Root 1 0 R >>
        startxref
        {xref:1}
        %%EOF

        """ + (updated
            ? """
                xref
                0 1
                {free}
                trailer
                << /Size 5 /Root 1 0 R /Prev {xref:1} >>
                startxref
                {xref:2}
                %%EOF

                """
            : string.Empty);

    /// <summary>
    /// A one-page document, sound, saved once more by an update whose table holds <paramref name="subsections"/> between its
    /// <c>xref</c> keyword and its trailer, which names the first table through <c>/Prev</c>: none, as #222 found it, or
    /// <c>0 0</c>, which ISO 32000-1 allows.
    /// </summary>
    public static string EmptyUpdate(string subsections) => PdfTemplate.Sound + $"""
        xref
        {subsections}trailer
        << /Size 4 /Root 1 0 R /Prev {"{xref:1}"} >>
        startxref
        {"{xref:2}"}
        %%EOF

        """;

    /// <summary>
    /// A document saved twice. The first revision writes the catalog, an empty page tree, object 3
    /// <c>(only in the oldest)</c>, object 4 <c>(old four)</c> and object 5 <c>(old five)</c>, and a classic table at
    /// <c>Oldest</c> that indexes them. The second writes object 4 again, <c>(new four)</c>, and a table at <c>Middle</c>,
    /// which <c>startxref</c> names, of a subsection <c>4 <paramref name="count"/></c> holding one row, object 4's, which
    /// ends in <c>x</c>, then <paramref name="after"/>, and a trailer whose <c>/Prev</c> names the oldest table.
    /// </summary>
    public static (byte[] File, long Oldest, long Middle) LostRowOverOlderOnes(int count, string after = "")
    {
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var three = file.Object(3, "(only in the oldest)");
        var oldFour = file.Object(4, "(old four)");
        var oldFive = file.Object(5, "(old five)");
        var oldest = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 6\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(three)}{Row(oldFour)}{Row(oldFive)}trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        var newFour = file.Object(4, "(new four)");
        var middle = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \n4 {count}\n{newFour:D10} 00000 x\n{after}trailer\n<< /Size 6 /Root 1 0 R /Prev {oldest} >>\nstartxref\n{middle}\n%%EOF\n"));
        return (file.Bytes, oldest, middle);
    }

    /// <summary>
    /// A document saved twice. The first revision writes the catalog, an empty page tree, object 3
    /// <c>(only in the oldest)</c> and object 7 <c>(old seven)</c>, and a classic table at <c>Oldest</c> that indexes them.
    /// The second writes object 4 <c>(only in the stream)</c>, object 7 again, <c>(new seven)</c>, and cross-reference stream
    /// 5 at <c>Stream</c>, whose dictionary holds <paramref name="entries"/>, <c>/Size 8 /Root 1 0 R</c> and the
    /// <c>/Length</c> of its data, the first <paramref name="rows"/> of the rows of objects 4, 5 and 7 in fields of 1, 4 and
    /// 2 bytes. When <paramref name="hybrid"/>, a classic table at <c>Table</c> that indexes nothing names the stream through
    /// <c>/XRefStm</c> and the oldest table through <c>/Prev</c>, and <c>startxref</c> names it; otherwise the stream's
    /// dictionary names the oldest table through <c>/Prev</c>, and <c>startxref</c> names the stream.
    /// </summary>
    public static (byte[] File, long Oldest, long Stream, long Table) StreamOverOlderRows(
        string entries = "/Type /XRef /W [1 4 2] /Index [4 2 7 1]", int rows = 3, bool hybrid = false)
    {
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var three = file.Object(3, "(only in the oldest)");
        var oldSeven = file.Object(7, "(old seven)");
        var oldest = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(three)}7 1\n{Row(oldSeven)}trailer\n<< /Size 8 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));

        var four = file.Object(4, "(only in the stream)");
        var newSeven = file.Object(7, "(new seven)");
        var stream = file.Position;
        var data = StreamRows([(four, 0), (stream, 0), (newSeven, 0)], rows);
        var prev = hybrid ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" /Prev {oldest}");
        file.Object(5, string.Create(
            CultureInfo.InvariantCulture,
            $"<< {entries} /Size 8 /Root 1 0 R{prev} /Length {data.Length} >>\nstream\n{data}\nendstream"));

        if (!hybrid)
        {
            file.Raw(string.Create(CultureInfo.InvariantCulture, $"startxref\n{stream}\n%%EOF\n"));
            return (file.Bytes, oldest, stream, -1);
        }

        var table = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 8 /Root 1 0 R /Prev {oldest} /XRefStm {stream} >>\nstartxref\n{table}\n%%EOF\n"));
        return (file.Bytes, oldest, stream, table);
    }

    /// <summary>
    /// The catalog, an empty page tree and object 3 <c>(only in the oldest)</c> in a classic table at <c>Oldest</c>; then a
    /// table, which <c>startxref</c> names and whose <c>/Prev</c> names the oldest, of <paramref name="lost"/> one-row
    /// subsections every other number from 100, each row ending in <c>x</c>, and then a subsection of
    /// <paramref name="free"/> free rows from 20,000.
    /// </summary>
    public static byte[] LostSubsections(int lost, int free = 0)
    {
        var text = new StringBuilder("%PDF-1.7\n");
        var catalog = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = text.Length;
        text.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        var three = text.Length;
        text.Append("3 0 obj\n(only in the oldest)\nendobj\n");
        var oldest = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(three)}trailer\n<< /Size 4 /Root 1 0 R >>\n");
        var newest = text.Length;
        text.Append("xref\n");

        for (var i = 0; i < lost; i++)
        {
            text.Append(CultureInfo.InvariantCulture, $"{100 + (2 * i)} 1\n0 0 x\n");
        }

        if (free > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"20000 {free}\n");

            for (var i = 0; i < free; i++)
            {
                text.Append("0000000000 00000 f \n");
            }
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size 30000 /Root 1 0 R /Prev {oldest} >>\nstartxref\n{newest}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.ToString());
    }

    /// <summary>
    /// A document saved twice. The first revision writes the catalog, an empty page tree, object 3
    /// <c>(only in the oldest)</c>, object 5 <c>(old five)</c>, object 6 <c>(old six)</c> and object 7 <c>(old seven)</c>, and
    /// a classic table at <c>Oldest</c> that indexes them. The second writes object 6 again, <c>(new six)</c>, and a hybrid
    /// file's table that indexes nothing, names the oldest through <c>/Prev</c>, and through <c>/XRefStm</c> the table that
    /// ends the file, at <c>Cut</c>, whose subsections the end of the file leaves as <paramref name="rows"/> writes them: by
    /// default <c>5 2</c>, holding object 5's free row, before object 6's.
    /// </summary>
    public static (byte[] File, long Oldest, long Cut) CutTableAnXRefStmNames(string rows = "5 2\n0000000000 00000 f \n")
    {
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var three = file.Object(3, "(only in the oldest)");
        var five = file.Object(5, "(old five)");
        var oldSix = file.Object(6, "(old six)");
        var seven = file.Object(7, "(old seven)");
        var oldest = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(three)}5 3\n{Row(five)}{Row(oldSix)}{Row(seven)}trailer\n<< /Size 8 /Root 1 0 R >>\n"));
        file.Object(6, "(new six)");
        var table = file.Position;
        string Hybrid(long cut) => string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 8 /Root 1 0 R /Prev {oldest} /XRefStm {cut:D10} >>\nstartxref\n{table}\n%%EOF\n");
        var cutAt = table + Hybrid(0).Length;
        file.Raw(Hybrid(cutAt));
        file.Raw("xref\n" + rows);
        return (file.Bytes, oldest, cutAt);
    }

    /// <summary>Writes a classic table's row placing an object in use at <paramref name="offset"/>.</summary>
    private static string Row(long offset) => string.Create(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");

    /// <summary>Writes the first <paramref name="count"/> of <paramref name="rows"/> as rows of type 1 in fields of 1, 4 and 2 bytes, a byte to a character.</summary>
    private static string StreamRows((long Offset, int Generation)[] rows, int count)
    {
        var data = new StringBuilder();

        foreach (var (offset, generation) in rows.Take(count))
        {
            data.Append((char)1)
                .Append((char)((offset >> 24) & 0xFF)).Append((char)((offset >> 16) & 0xFF))
                .Append((char)((offset >> 8) & 0xFF)).Append((char)(offset & 0xFF))
                .Append((char)((generation >> 8) & 0xFF)).Append((char)(generation & 0xFF));
        }

        return data.ToString();
    }
}
