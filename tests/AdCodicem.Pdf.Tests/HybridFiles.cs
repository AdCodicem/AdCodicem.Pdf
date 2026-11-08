using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Files whose hybrid tables name through <c>/XRefStm</c> what the chain reads anyway: a cross-reference stream two tables
/// name, as a writer that copies <c>/XRefStm</c> into the next trailer leaves it, or no cross-reference stream at all — a
/// classic table, the table itself among them, or a section the chain reads through <c>/Prev</c> or <c>startxref</c> (#156).
/// Offsets are counted from the header, which starts the file.
/// </summary>
internal static class HybridFiles
{
    /// <summary>The catalog, the page tree and its page, objects 1 to 3, after the header, in a <see cref="PdfTemplate"/>.</summary>
    private const string Objects = """
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

        """;

    /// <summary>
    /// A one-page document whose one table indexes the catalog and the page tree, and whose <c>/XRefStm</c> names the table
    /// itself, <c>xref:1</c>: the page, object 3, is indexed by no section.
    /// </summary>
    public static byte[] NamingItself() => PdfTemplate.Build(Objects + """
        xref
        0 3
        {free}
        {row:1}
        {row:2}
        trailer
        << /Size 4 /Root 1 0 R /XRefStm {xref:1} >>
        startxref
        {xref:1}
        %%EOF

        """);

    /// <summary>
    /// A one-page document saved three times, its tables at <c>xref:1</c>, <c>xref:2</c> and <c>xref:3</c>, each naming the
    /// one before through <c>/Prev</c>. The oldest indexes the catalog and the page tree, and its <c>/XRefStm</c> names
    /// <paramref name="named"/>: itself, <c>{xref:1}</c>; the table <c>/Prev</c> names, <c>{xref:2}</c>; or the one
    /// <c>startxref</c> names, <c>{xref:3}</c>. The others index objects 4 and 5, <c>(four)</c> and <c>(five)</c>: the page,
    /// object 3, is indexed by no section.
    /// </summary>
    public static byte[] OldestNaming(string named) => PdfTemplate.Build(Objects + """
        xref
        0 3
        {free}
        {row:1}
        {row:2}
        trailer
        << /Size 6 /Root 1 0 R /XRefStm {named} >>
        startxref
        {xref:1}
        %%EOF
        4 0 obj
        (four)
        endobj
        xref
        0 1
        {free}
        4 1
        {row:4}
        trailer
        << /Size 6 /Root 1 0 R /Prev {xref:1} >>
        startxref
        {xref:2}
        %%EOF
        5 0 obj
        (five)
        endobj
        xref
        0 1
        {free}
        5 1
        {row:5}
        trailer
        << /Size 6 /Root 1 0 R /Prev {xref:2} >>
        startxref
        {xref:3}
        %%EOF

        """.Replace("{named}", named, StringComparison.Ordinal));

    /// <summary>
    /// A one-page document, its first table at <c>xref:1</c> indexing it whole, updated by a table at <c>xref:3</c> that
    /// indexes object 9, <c>(nine)</c>, names the first through <c>/Prev</c> and, through <c>/XRefStm</c>, a classic table at
    /// <c>xref:2</c> no <c>/Prev</c> names, which indexes object 10, <c>(ten)</c>, with a trailer of its own: what follows its
    /// <c>xref</c> keyword is <paramref name="ten"/>, up to the trailer keyword. With <paramref name="firstNamesIt"/>, the first
    /// table names that table through <c>/XRefStm</c> too.
    /// </summary>
    public static byte[] NamingATableNeverRead(bool firstNamesIt = false, string ten = "10 1\n{row:10}\n") => PdfTemplate.Build(Objects + """
        xref
        0 4
        {free}
        {row:1}
        {row:2}
        {row:3}
        trailer
        << /Size 4 /Root 1 0 R{first} >>
        startxref
        {xref:1}
        %%EOF
        9 0 obj
        (nine)
        endobj
        10 0 obj
        (ten)
        endobj
        xref
        {ten}trailer
        << /Size 11 >>
        xref
        0 1
        {free}
        9 1
        {row:9}
        trailer
        << /Size 11 /Root 1 0 R /Prev {xref:1} /XRefStm {xref:2} >>
        startxref
        {xref:3}
        %%EOF

        """.Replace("{first}", firstNamesIt ? " /XRefStm {xref:2}" : string.Empty, StringComparison.Ordinal).Replace("{ten}", ten, StringComparison.Ordinal));

    /// <summary>
    /// A one-page document saved twice: the first table, at <c>xref:1</c>, indexes it whole and names the newest, at
    /// <c>xref:2</c>, through <c>/Prev</c> and through <c>/XRefStm</c> both; the newest indexes nothing and names the first
    /// through <c>/Prev</c>: the chain loops.
    /// </summary>
    public static byte[] FirstNamingTheNewestTwice() => PdfTemplate.Build(Objects + """
        xref
        0 4
        {free}
        {row:1}
        {row:2}
        {row:3}
        trailer
        << /Size 4 /Root 1 0 R /Prev {xref:2} /XRefStm {xref:2} >>
        xref
        0 1
        {free}
        trailer
        << /Size 4 /Root 1 0 R /Prev {xref:1} >>
        startxref
        {xref:2}
        %%EOF

        """);

    /// <summary>
    /// A one-page document, its first table at <c>xref:1</c> indexing it whole, updated by a table at <c>xref:2</c> that
    /// indexes object 4, <c>(four)</c>, and names the first through <c>/Prev</c>. A comment of 1,200 bytes lies between the
    /// page and the first table, and both tables name through <c>/XRefStm</c> <paramref name="hole"/>, which the test gives
    /// as the comment's start plus 600, in ten digits so that no offset moves with it: nothing lies there, nor within 512
    /// bytes of it.
    /// </summary>
    public static byte[] TablesNamingNothing(string hole) => PdfTemplate.Build((Objects + "%" + new string('-', 1200) + "\n" + """
        xref
        0 4
        {free}
        {row:1}
        {row:2}
        {row:3}
        trailer
        << /Size 4 /Root 1 0 R /XRefStm {hole} >>
        startxref
        {xref:1}
        %%EOF
        4 0 obj
        (four)
        endobj
        xref
        0 1
        {free}
        4 1
        {row:4}
        trailer
        << /Size 5 /Root 1 0 R /Prev {xref:1} /XRefStm {hole} >>
        startxref
        {xref:2}
        %%EOF

        """).Replace("{hole}", hole, StringComparison.Ordinal));

    /// <summary>
    /// A one-page document saved three times: the first table, at <c>xref:1</c>, alone indexes the page tree and its page;
    /// the second, at <c>xref:2</c>, indexes object 4, <c>(four)</c>, and names the first through <c>/Prev</c>; the newest,
    /// at <c>xref:3</c>, indexes object 5, <c>(five)</c>, and names the second through <c>/Prev</c> and through
    /// <c>/XRefStm</c> both.
    /// </summary>
    public static byte[] NamingThePreviousTable() => PdfTemplate.Build(Objects + """
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
        4 0 obj
        (four)
        endobj
        xref
        0 1
        {free}
        4 1
        {row:4}
        trailer
        << /Size 5 /Root 1 0 R /Prev {xref:1} >>
        startxref
        {xref:2}
        %%EOF
        5 0 obj
        (five)
        endobj
        xref
        0 1
        {free}
        5 1
        {row:5}
        trailer
        << /Size 6 /Root 1 0 R /Prev {xref:2} /XRefStm {xref:2} >>
        startxref
        {xref:3}
        %%EOF

        """);

    /// <summary>
    /// A one-page document saved three times: the first table, at <c>Oldest</c>, alone indexes the page tree and its page;
    /// the second, at <c>Middle</c>, indexes object 4, <c>(four)</c>, and names the first through <c>/Prev</c>; the newest,
    /// at <c>Newest</c>, indexes object 5, <c>(five)</c>, and names the second through <c>/Prev</c>. Both name through
    /// <c>/XRefStm</c>, as a writer that copies it into the next trailer leaves it, the place 7 bytes before the second
    /// table, in the <c>endobj</c> of object 4, where no section starts: the second table is the nearest one, and the first
    /// lies within 512 bytes of it.
    /// </summary>
    public static (byte[] File, long Oldest, long Middle, long Newest) CopyingAnXRefStmOffThePreviousTable()
    {
        var file = new HandWrittenFile();
        var (catalog, pages, page) = Page(file);
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        var four = file.Object(4, "(four)");
        var middle = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n4 1\n{Row(four)}trailer\n<< /Size 5 /Root 1 0 R /Prev {oldest} /XRefStm {middle - 7} >>\nstartxref\n{middle}\n%%EOF\n"));
        var five = file.Object(5, "(five)");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n5 1\n{Row(five)}trailer\n<< /Size 6 /Root 1 0 R /Prev {middle} /XRefStm {middle - 7} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, middle, newest);
    }

    /// <summary>
    /// A one-page document saved three times: the first table, at <c>Oldest</c>, alone indexes the page tree and its page;
    /// cross-reference stream 5, at <c>Stream</c>, indexes object 4, <c>(four)</c>, and itself, and names the first table
    /// through <c>/Prev</c>; the newest table, at <c>Newest</c>, indexes object 6, <c>(six)</c>, and names the stream through
    /// <c>/Prev</c> and through <c>/XRefStm</c> both.
    /// </summary>
    public static (byte[] File, long Oldest, long Stream, long Newest) NamingThePreviousStream()
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        var four = file.Object(4, "(four)");
        var stream = file.Position;
        var rows = StreamRows((1, four, 0), (1, stream, 0));
        file.Object(5, Invariant($"<< /Type /XRef /Size 6 /W [1 4 2] /Index [4 2] /Root 1 0 R /Prev {oldest} /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        file.Raw(Invariant($"startxref\n{stream}\n%%EOF\n"));
        var six = file.Object(6, "(six)");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n6 1\n{Row(six)}trailer\n<< /Size 7 /Root 1 0 R /Prev {stream} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, stream, newest);
    }

    /// <summary>
    /// A one-page document saved twice: the first table, at <c>Table</c>, indexes the catalog and the page tree and names,
    /// through <c>/XRefStm</c>, cross-reference stream 6 at <c>Stream</c>, the newest section, which <c>startxref</c> names,
    /// which indexes object 5, <c>(five)</c>, and itself, and names the table through <c>/Prev</c>: the page, object 3, is
    /// indexed by no section.
    /// </summary>
    public static (byte[] File, long Table, long Stream) NamingANewerStream()
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, _) = Page(file);
        var table = file.Position;

        // The table names the stream that follows it, whose offset is the table's length past it: written once with a
        // stand-in of the same length, then again with the offset found.
        var text = Invariant($"xref\n0 3\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}trailer\n<< /Size 7 /Root 1 0 R /XRefStm 0000000000 >>\nstartxref\n{table}\n%%EOF\n");
        var five = table + text.Length;
        var stream = five + "5 0 obj\n(five)\nendobj\n".Length;
        file.Raw(text.Replace("0000000000 >>", Invariant($"{stream:D10} >>"), StringComparison.Ordinal));
        file.Object(5, "(five)");
        var rows = StreamRows((1, five, 0), (1, stream, 0));
        file.Object(6, Invariant($"<< /Type /XRef /Size 7 /W [1 4 2] /Index [5 2] /Root 1 0 R /Prev {table} /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        file.Raw(Invariant($"startxref\n{stream}\n%%EOF\n"));
        return (file.Bytes, table, stream);
    }

    /// <summary>
    /// #156's shape: a one-page document whose first table, at <c>Oldest</c>, indexes objects 1 to 3, <c>/Size 4</c>; an
    /// update whose table, at <c>Older</c>, indexes object 4, <c>(four)</c>, and cross-reference stream 5, and gives
    /// <c>/Size</c> <paramref name="olderSize"/>, its <c>/XRefStm</c> naming that stream, at <c>Stream</c>, which indexes
    /// object 6, <paramref name="six"/>, alone and gives <c>/Size</c> <paramref name="streamSize"/>; and the newest table, at
    /// <c>Newest</c>, which indexes object 7, <c>(seven)</c>, <c>/Size 8</c>, and, when <paramref name="copied"/>, copies
    /// the update's <c>/XRefStm</c>. With <paramref name="between"/>, a table indexing object 8, <c>(eight)</c>, lies
    /// between the update and the newest, which is two revisions late with its copy; with <paramref name="olderEight"/>, the
    /// update's table indexes that object 8 itself, above what the stream numbers. Either way the newest gives
    /// <c>/Size 9</c>.
    /// </summary>
    public static (byte[] File, long Oldest, long Older, long Stream, long Newest) SharedStream(
        int olderSize, int streamSize = 7, bool copied = true, bool between = false, bool olderEight = false, string six = "(six)")
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        var four = file.Object(4, "(four)");
        var sixth = file.Object(6, six);
        var eight = between || olderEight ? file.Object(8, "(eight)") : -1;
        var stream = file.Position;
        var rows = StreamRows((1, sixth, 0));
        file.Object(5, Invariant($"<< /Type /XRef /Size {streamSize} /W [1 4 2] /Index [6 1] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var older = file.Position;
        var olderRows = olderEight ? "8 1\n" + Row(eight) : string.Empty;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n4 2\n{Row(four)}{Row(stream)}{olderRows}trailer\n<< /Size {olderSize} /Root 1 0 R /Prev {oldest} /XRefStm {stream} >>\nstartxref\n{older}\n%%EOF\n"));
        var previous = older;

        if (between)
        {
            previous = file.Position;
            file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n8 1\n{Row(eight)}trailer\n<< /Size 9 /Root 1 0 R /Prev {older} >>\nstartxref\n{previous}\n%%EOF\n"));
        }

        var seven = file.Object(7, "(seven)");
        var newest = file.Position;
        var copy = copied ? Invariant($" /XRefStm {stream}") : string.Empty;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n7 1\n{Row(seven)}trailer\n<< /Size {(between || olderEight ? 9 : 8)} /Root 1 0 R /Prev {previous}{copy} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, older, stream, newest);
    }

    /// <summary>
    /// A file as <see cref="SharedStream"/> writes it, of the right sizes, but for the offset the newest table gives the
    /// stream through <c>/XRefStm</c>: two bytes short of it, where two spaces precede its header, while the update's names
    /// the header itself.
    /// </summary>
    public static (byte[] File, long Stream) SharedStreamNamedShort()
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        var four = file.Object(4, "(four)");
        var six = file.Object(6, "(six)");
        file.Raw("  ");
        var stream = file.Position;
        var rows = StreamRows((1, six, 0));
        file.Object(5, Invariant($"<< /Type /XRef /Size 7 /W [1 4 2] /Index [6 1] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var older = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n4 2\n{Row(four)}{Row(stream)}trailer\n<< /Size 7 /Root 1 0 R /Prev {oldest} /XRefStm {stream} >>\nstartxref\n{older}\n%%EOF\n"));
        var seven = file.Object(7, "(seven)");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n7 1\n{Row(seven)}trailer\n<< /Size 8 /Root 1 0 R /Prev {older} /XRefStm {stream - 2} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, stream);
    }

    /// <summary>
    /// #156's shape, as a writer that copies <c>/XRefStm</c> into the next trailer and names a stream short of it leaves it:
    /// a one-page document whose first table, at <c>Oldest</c>, indexes objects 1 to 3; an update that writes objects 4, 6
    /// <c>(six)</c> and 8 <c>(eight)</c>, two spaces, and cross-reference stream 5 at <c>Stream</c>, of <c>/W</c>
    /// <paramref name="widths"/> and <c>/Size</c> <paramref name="size"/>, whose rows place object 6 and give object 8 type
    /// <paramref name="eightType"/>, at the offset of <c>(eight)</c> where that type is 1; the update's table, which indexes
    /// objects 4 and 5 and names the stream through <c>/XRefStm</c>; and the newest table, which indexes object 7,
    /// <c>(seven)</c>, and names the stream through <c>/XRefStm</c> too. Where <paramref name="headerFirst"/>, the newest
    /// table, read first, names the stream's header, and the update two bytes short of it, in the spaces; the other way
    /// round otherwise. Both tables give <c>/Size 9</c>.
    /// </summary>
    public static (byte[] File, long Stream) SharedStreamNamedTwice(bool headerFirst, string widths = "[1 4 2]", int size = 9, int eightType = 1)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        var four = file.Object(4, "(four)");
        var six = file.Object(6, "(six)");
        var eight = file.Object(8, "(eight)");
        file.Raw("  ");
        var stream = file.Position;
        var rows = StreamRows((1, six, 0), (eightType, eightType == 1 ? eight : 0, 0));
        file.Object(5, Invariant($"<< /Type /XRef /Size {size} /W {widths} /Index [6 1 8 1] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var older = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n4 2\n{Row(four)}{Row(stream)}trailer\n<< /Size 9 /Root 1 0 R /Prev {oldest} /XRefStm {(headerFirst ? stream - 2 : stream)} >>\nstartxref\n{older}\n%%EOF\n"));
        var seven = file.Object(7, "(seven)");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n7 1\n{Row(seven)}trailer\n<< /Size 9 /Root 1 0 R /Prev {older} /XRefStm {(headerFirst ? stream : stream - 2)} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, stream);
    }

    /// <summary>
    /// An empty document whose <paramref name="tables"/> tables, each naming the one before through <c>/Prev</c>, index the
    /// catalog and the page tree, and whose <c>/XRefStm</c> names, in every table when <paramref name="every"/> and in the
    /// newest alone otherwise, cross-reference stream 3, which declares <paramref name="rows"/> free rows from object 100,
    /// past <paramref name="spaces"/> spaces. The older tables that do not name it give its offset under <c>/XRefStx</c>, a
    /// key of the same length that names nothing, and every offset is written in ten digits: either file is as long as the
    /// other, each table where it is in both. Where <paramref name="shifted"/>, each table names the stream short of its
    /// header, in the spaces, by as many bytes as tables were written after it, as far as the spaces reach: the newest names
    /// the header itself.
    /// </summary>
    public static byte[] TablesNamingOneStream(int tables, int rows, bool every, int spaces = 0, bool shifted = false)
    {
        var text = new StringBuilder("%PDF-1.7\n");
        var catalog = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = text.Length;
        text.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n").Append(' ', spaces);
        var stream = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"3 0 obj\n<< /Type /XRef /Size {100 + rows} /W [1 4 2] /Index [100 {rows}] /Length {7 * rows} >>\nstream\n")
            .Append('\0', 7 * rows)
            .Append("\nendstream\nendobj\n");
        var previous = -1;

        for (var table = 0; table < tables; table++)
        {
            var xref = text.Length;
            text.Append(CultureInfo.InvariantCulture, $"xref\n0 3\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}trailer\n<< /Size {100 + rows} /Root 1 0 R");
            var named = shifted ? stream - ((tables - 1 - table) % (spaces + 1)) : stream;
            text.Append(CultureInfo.InvariantCulture, $" /{(every || table == tables - 1 ? "XRefStm" : "XRefStx")} {named:D10}");
            text.Append(previous < 0 ? string.Empty : Invariant($" /Prev {previous}"));
            text.Append(" >>\n");
            previous = xref;
        }

        text.Append(CultureInfo.InvariantCulture, $"startxref\n{previous}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.ToString());
    }

    /// <summary>
    /// A one-page document saved twice, whose newest table names both earlier sections a few hundred bytes off: the first
    /// table, at <c>Oldest</c>, indexes objects 1 to 3 and object 7, <c>(old seven)</c>; cross-reference stream 9, at
    /// <c>Stream</c>, right after it, indexes object 5, <c>(five)</c>, and object 7 again, <c>(new seven)</c>; object 8, a
    /// string, lies 360 bytes past the first table, and object 4, <paramref name="four"/>, 410 bytes past it, at <c>Four</c>;
    /// the newest table, at <c>Newest</c>, indexes object 6, <c>(six)</c>, and names through <c>/Prev</c> the place
    /// <paramref name="prevShortBy"/> bytes before object 4, in the spaces before it, and through <c>/XRefStm</c> the place
    /// <paramref name="xrefStmInto"/> bytes into object 8: 20 bytes in, inside its string, object 8 is the nearest place to
    /// it, then object 4, then the stream; 28 bytes in, still inside its string, object 4, 22 bytes off, is nearer than
    /// object 8. A long comment keeps the newest table more than 512 bytes from what each link names, whatever object 4's
    /// length.
    /// </summary>
    public static (byte[] File, long Oldest, long Four, long Stream, long Newest) NamingAnObjectBesideTheStream(
        string four, int prevShortBy = 0, int xrefStmInto = 20)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var oldSeven = file.Object(7, "(old seven)");
        var five = file.Object(5, "(five)");
        var newSeven = file.Object(7, "(new seven)");
        file.Raw("%" + new string('-', 1200) + "\n");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}7 1\n{Row(oldSeven)}trailer\n<< /Size 8 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        var stream = file.Position;
        var rows = StreamRows((1, five, 0), (1, newSeven, 0));
        file.Object(9, Invariant($"<< /Type /XRef /Size 10 /W [1 4 2] /Index [5 1 7 1] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        file.Raw(new string(' ', (int)(oldest + 360 - file.Position)));
        var eight = file.Object(8, "(" + new string('x', 20) + ")");
        file.Raw(new string(' ', (int)(oldest + 410 - file.Position)));
        var fourAt = file.Object(4, four);
        file.Raw("%" + new string('-', 1200) + "\n");
        var six = file.Object(6, "(six)");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n6 1\n{Row(six)}trailer\n<< /Size 10 /Root 1 0 R /Prev {fourAt - prevShortBy} /XRefStm {eight + xrefStmInto} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, fourAt, stream, newest);
    }

    /// <summary>
    /// A one-page document saved twice, whose newest table names through <c>/Prev</c> a cross-reference stream that reads,
    /// and through <c>/XRefStm</c> a place beside it, nearer it than the oldest table: the oldest table, at <c>Oldest</c>,
    /// indexes objects 1 to 3 and object 7, <c>(old seven)</c>; object 8, a string, follows it, and object 4, at
    /// <c>Four</c>, a stream whose dictionary gives <paramref name="type"/> — written whole, a space after it, or nothing —,
    /// the <c>/W</c> <paramref name="widths"/> and an <c>/Index</c> whose one row places object 7's new revision,
    /// <c>(new seven)</c>, its data <paramref name="padding"/> dashes longer, written Flate-encoded where
    /// <paramref name="deflated"/> is set, names the oldest table through <c>/Prev</c>, gives the <c>/Filter</c>
    /// <paramref name="filter"/>, none when empty, and the <c>/Length</c> <paramref name="length"/>, the length of the data
    /// as written where none is given; the newest table, past a long comment, indexes object
    /// 6, <c>(six)</c>, and names object 4 through <c>/Prev</c> and, through <c>/XRefStm</c>, the place 20 bytes into object
    /// 8, inside its string, at <c>Named</c>, 28 bytes before object 4: object 8 is the nearest place to it, then object 4,
    /// then the oldest table. Where <paramref name="cut"/> names object 4's <c>/Size</c>, <c>/W</c> or <c>/Index</c>, that
    /// value is written as the reference <c>9 0 R</c> — the <c>/Size</c> with no <c>/Index</c> beside it, its one row then
    /// object 0's —, and object 9, past object 6, holds it, followed by 1,100 spaces: the newest table indexes it, and
    /// <see cref="Documents.PdfReaderLimits.MaxObjectLength"/> at 1 KB cuts it.
    /// </summary>
    public static (byte[] File, long Oldest, long Four, long Named) PrevNamingAStreamBesideTheOldestTable(
        string type, string widths = "[1 4 2]", int padding = 0, string filter = "", int? length = null, string? cut = null, bool deflated = false)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var oldSeven = file.Object(7, "(old seven)");
        var newSeven = file.Object(7, "(new seven)");
        file.Raw("%" + new string('-', 1200) + "\n");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}7 1\n{Row(oldSeven)}trailer\n<< /Size 8 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        var eight = file.Object(8, "(" + new string('x', 30) + ")");
        var rows = StreamRows((1, newSeven, 0)) + new string('-', padding);
        var data = deflated ? Deflate(rows) : rows;
        var filtered = filter.Length == 0 ? string.Empty : " /Filter " + filter;
        (string numbering, string? value) = cut switch
        {
            null => (Invariant($"/Size 10 /W {widths} /Index [7 1]"), null),
            "/Size" => (Invariant($"/Size 9 0 R /W {widths}"), "10"),
            "/W" => ("/Size 10 /W 9 0 R /Index [7 1]", widths),
            "/Index" => (Invariant($"/Size 10 /W {widths} /Index 9 0 R"), "[7 1]"),
            _ => throw new ArgumentOutOfRangeException(nameof(cut), cut, "No such key."),
        };
        var four = file.Object(4, Invariant($"<< {type}{numbering} /Prev {oldest}{filtered} /Length {length ?? data.Length} >>\nstream\n{data}\nendstream"));
        file.Raw("%" + new string('-', 1200) + "\n");
        var six = file.Object(6, "(six)");
        var nine = value is null ? string.Empty : "9 1\n" + Row(file.Object(9, value + new string(' ', 1100)));
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n6 1\n{Row(six)}{nine}trailer\n<< /Size 10 /Root 1 0 R /Prev {four} /XRefStm {eight + 20} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, four, eight + 20);
    }

    /// <summary>
    /// A one-page document saved twice, whose newest table writes each offset <paramref name="shift"/> bytes too large: the
    /// first table, at <c>Oldest</c>, indexes objects 1 to 3 and object 7, <c>(old seven)</c>; cross-reference stream 9, at
    /// <c>Stream</c>, right after it, indexes object 5, <c>(five)</c>, and object 7 again, <c>(new seven)</c>; object 6,
    /// <c>(six)</c>, lies where the table's <c>/Prev</c> names, <paramref name="shift"/> bytes past the first table; the
    /// newest table, at <c>Newest</c>, indexes it and names the stream through <c>/XRefStm</c> <paramref name="shift"/> bytes
    /// past it.
    /// </summary>
    public static (byte[] File, long Oldest, long Stream, long Newest) WritingEveryOffsetTooLarge(int shift)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var oldSeven = file.Object(7, "(old seven)");
        var five = file.Object(5, "(five)");
        var newSeven = file.Object(7, "(new seven)");
        file.Raw("%" + new string('-', 1200) + "\n");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}7 1\n{Row(oldSeven)}trailer\n<< /Size 8 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        var stream = file.Position;
        var rows = StreamRows((1, five, 0), (1, newSeven, 0));
        file.Object(9, Invariant($"<< /Type /XRef /Size 10 /W [1 4 2] /Index [5 1 7 1] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        file.Raw(new string(' ', (int)(oldest + shift - file.Position)));
        var six = file.Object(6, "(six)");
        file.Raw("%" + new string('-', 1200) + "\n");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n6 1\n{Row(six)}trailer\n<< /Size 10 /Root 1 0 R /Prev {oldest + shift} /XRefStm {stream + shift} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, stream, newest);
    }

    /// <summary>
    /// A one-page document saved three or four times, whose newest table names through <c>/Prev</c> the <c>endobj</c> 7
    /// bytes before the table before it, at <c>Previous</c>, and through <c>/XRefStm</c> the place
    /// <paramref name="xrefStmShortBy"/> bytes before that table: 1, the end of line before it; 3, inside the
    /// <c>endobj</c>, where the search near it finds the table. The table before the newest indexes object 4, <c>(four)</c>,
    /// and names through <c>/Prev</c> the oldest table, at <c>Oldest</c>, which indexes objects 1 to 3, the page among them
    /// as <c>(three as the oldest has it)</c> — or, with <paramref name="middle"/>, a middle table, at <c>Middle</c>, which
    /// indexes object 3 as <c>(three as the middle revision has it)</c>, names the oldest through <c>/Prev</c> and, where
    /// <paramref name="middleXRefStm"/> is given, the place that many bytes before the table before the newest through
    /// <c>/XRefStm</c> — and, where <paramref name="middlePrev"/> is given, names the place that many bytes before that table
    /// through <c>/Prev</c> in place of the oldest. The middle table is written before the oldest, which lies nearer the
    /// table before the newest, both within reach of a search near it; without a middle table, the oldest lies 1,200 bytes
    /// before it, out of reach. The newest table indexes object 5, <c>(five)</c>.
    /// </summary>
    public static (byte[] File, long Oldest, long Middle, long Previous, long Newest) PrevFallingShortOfTheTableBefore(
        int xrefStmShortBy, bool middle, int? middleXRefStm = null, int? middlePrev = null)
    {
        var file = new HandWrittenFile();
        var (catalog, pages, _) = Page(file);
        var middleAt = -1L;

        // The middle table names the oldest, written after it, and the table before the newest through stand-ins of ten
        // characters, replaced once their places are known.
        if (middle)
        {
            var middleThree = file.Object(3, "(three as the middle revision has it)");
            middleAt = file.Position;
            file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n3 1\n{Row(middleThree)}trailer\n<< /Size 4 /Root 1 0 R /Prev @@OLDEST@@{(middleXRefStm is null ? string.Empty : " /XRefStm @@BEFORE@@")} >>\nstartxref\n{middleAt}\n%%EOF\n"));
        }

        var oldThree = file.Object(3, "(three as the oldest has it)");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(oldThree)}trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));

        if (!middle)
        {
            file.Raw("%" + new string('-', 1200) + "\n");
        }

        var four = file.Object(4, "(four)");
        var previous = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n4 1\n{Row(four)}trailer\n<< /Size 5 /Root 1 0 R /Prev {(middle ? middleAt : oldest)} >>\nstartxref\n{previous}\n%%EOF\n"));
        var five = file.Object(5, "(five)");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n5 1\n{Row(five)}trailer\n<< /Size 6 /Root 1 0 R /Prev {previous - 7} /XRefStm {previous - xrefStmShortBy} >>\nstartxref\n{newest}\n%%EOF\n"));
        var text = Encoding.Latin1.GetString(file.Bytes)
            .Replace("@@OLDEST@@", Invariant($"{(middlePrev is { } before ? previous - before : oldest):D10}"), StringComparison.Ordinal)
            .Replace("@@BEFORE@@", Invariant($"{previous - middleXRefStm.GetValueOrDefault():D10}"), StringComparison.Ordinal);
        return (Encoding.Latin1.GetBytes(text), oldest, middleAt, previous, newest);
    }

    /// <summary>
    /// A document saved three times, whose newest table names through <c>/Prev</c> the <c>endobj</c> 7 bytes before
    /// cross-reference stream 6, at <c>Stream</c>, and through <c>/XRefStm</c> the stream itself. The stream indexes object 4,
    /// <c>(four)</c>, and itself, then gives object 7 <paramref name="seven"/>: <c>"no row"</c>; <c>"a reserved row"</c>, a row
    /// of type 3; <c>"a reserved row, one row short"</c>, that row, its <c>/Index</c> declaring object 8 after it, whose row
    /// its data does not hold; <c>"a reserved row after a refused one"</c>, a row of type 2 naming object stream 0, which
    /// no entry holds, then, in a subsection of its own, that row of type 3; or <c>"no row, its /Index holding a
    /// reference"</c>, its <c>/Index</c> declaring object 7 all the same through <c>[4 1 6 9 0 R]</c>, object 9 holding 2,
    /// which the newest table places, so that the stream holds 2 of the 3 rows it declares. It names through <c>/Prev</c>
    /// <paramref name="prev"/>: <c>"the oldest table"</c>, at <c>Oldest</c>, 1,200 bytes before it, out of reach of a search
    /// near it, which indexes objects 1 to 3, the page among them as <c>(three as the oldest has it)</c>, and, for
    /// <c>"a reserved row, one row short"</c> and <c>"a reserved row after a refused one"</c>, object 7,
    /// <c>(seven as the oldest has it)</c>; <c>"the stream itself"</c>; <c>"the table's /Prev"</c>, the <c>endobj</c> 7 bytes
    /// before it, which the newest table's <c>/Prev</c> names; <c>"outside the file"</c>, offset 99,999,999;
    /// <c>"a string"</c>, <c>(x)</c>; or <c>"null"</c>. The newest table indexes object 5, <c>(five)</c>, object 9 where the
    /// stream's <c>/Index</c> refers to it, and, where <paramref name="fourRefused"/> says so, gives object 4 generation
    /// 65536 before them, which no entry holds, so that the stream's row for object 4 does not stand.
    /// </summary>
    public static (byte[] File, long Oldest, long Stream, long Newest) PrevFallingShortOfTheStreamBefore(
        string seven = "no row", string prev = "the oldest table", bool fourRefused = false)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, _) = Page(file);
        var oldThree = file.Object(3, "(three as the oldest has it)");
        var olderSeven = seven is "a reserved row, one row short" or "a reserved row after a refused one";
        var oldSeven = olderSeven ? Invariant($"7 1\n{Row(file.Object(7, "(seven as the oldest has it)"))}") : string.Empty;
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(oldThree)}{oldSeven}trailer\n<< /Size {(olderSeven ? 8 : 4)} /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        file.Raw("%" + new string('-', 1200) + "\n");
        var four = file.Object(4, "(four)");
        var stream = file.Position;
        var (rows, index, size) = seven switch
        {
            "a reserved row" => (StreamRows((1, four, 0), (1, stream, 0), (3, 0, 0)), "[4 1 6 2]", 8),
            "a reserved row, one row short" => (StreamRows((1, four, 0), (1, stream, 0), (3, 0, 0)), "[4 1 6 3]", 9),
            "a reserved row after a refused one" => (StreamRows((1, four, 0), (1, stream, 0), (2, 0, 0), (3, 0, 0)), "[4 1 6 2 7 1]", 8),
            "no row, its /Index holding a reference" => (StreamRows((1, four, 0), (1, stream, 0)), "[4 1 6 9 0 R]", 10),
            _ => (StreamRows((1, four, 0), (1, stream, 0)), "[4 1 6 1]", 8),
        };
        var named = prev switch
        {
            "the stream itself" => Invariant($"{stream}"),
            "the table's /Prev" => Invariant($"{stream - 7}"),
            "outside the file" => "99999999",
            "a string" => "(x)",
            "null" => "null",
            _ => Invariant($"{oldest}"),
        };
        file.Object(6, Invariant($"<< /Type /XRef /Size {size} /W [1 4 2] /Index {index} /Root 1 0 R /Prev {named} /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var five = file.Object(5, "(five)");
        var nine = index.Contains("9 0 R", StringComparison.Ordinal) ? Invariant($"9 1\n{Row(file.Object(9, "2"))}") : string.Empty;
        var newest = file.Position;
        var newestRows = fourRefused ? Invariant($"4 2\n{four:D10} 65536 n \n") : "5 1\n";
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n{newestRows}{Row(five)}{nine}trailer\n<< /Size {size} /Root 1 0 R /Prev {stream - 7} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, stream, newest);
    }

    /// <summary>
    /// The document <see cref="PrevFallingShortOfTheStreamBefore"/> writes, object 7 given no row, saved twice more:
    /// cross-reference stream 20, at <c>Lost</c>, whose <c>/Index</c> is no array, which holds no row and names through
    /// <c>/Prev</c> that document's newest table, the hybrid one, at <c>Hybrid</c>; then a classic table, at <c>Newest</c>,
    /// which indexes nothing and names stream 20 through <c>/Prev</c>.
    /// </summary>
    public static (byte[] File, long Oldest, long Stream, long Hybrid, long Lost, long Newest) PrevFallingShortOfTheStreamBeforeALostStream()
    {
        var (bytes, oldest, stream, hybrid) = PrevFallingShortOfTheStreamBefore();
        var text = new StringBuilder(Encoding.Latin1.GetString(bytes));
        var lost = text.Length;
        text.Append(Invariant($"20 0 obj\n<< /Type /XRef /Size 21 /Index /X /W [1 4 2] /Root 1 0 R /Prev {hybrid} /Length 0 >>\nstream\n\nendstream\nendobj\n"));
        var newest = text.Length;
        text.Append(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 21 /Root 1 0 R /Prev {lost} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (Encoding.Latin1.GetBytes(text.ToString()), oldest, stream, hybrid, lost, newest);
    }

    /// <summary>
    /// The document <see cref="PrevFallingShortOfTheStreamBefore"/> writes, saved once more: its newest table, at
    /// <c>Middle</c>, names the <c>endobj</c> 7 bytes before stream 6 through <c>/Prev</c> as it did, and no stream; a newer
    /// table, at <c>Newest</c>, names it through <c>/Prev</c>, and stream 6 through <c>/XRefStm</c>, and indexes nothing.
    /// </summary>
    public static (byte[] File, long Stream, long Middle, long Newest) OlderPrevFallingShortOfTheStreamBefore()
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, _) = Page(file);
        var oldThree = file.Object(3, "(three as the oldest has it)");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(oldThree)}trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        file.Raw("%" + new string('-', 1200) + "\n");
        var four = file.Object(4, "(four)");
        var stream = file.Position;
        var rows = StreamRows((1, four, 0), (1, stream, 0));
        file.Object(6, Invariant($"<< /Type /XRef /Size 8 /W [1 4 2] /Index [4 1 6 1] /Root 1 0 R /Prev {oldest} /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var five = file.Object(5, "(five)");
        var middle = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n5 1\n{Row(five)}trailer\n<< /Size 8 /Root 1 0 R /Prev {stream - 7} >>\nstartxref\n{middle}\n%%EOF\n"));
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 8 /Root 1 0 R /Prev {middle} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, stream, middle, newest);
    }

    /// <summary>
    /// The document <see cref="PrevFallingShortOfTheStreamBefore"/> writes, object 7 given no row, with object 8,
    /// <c>(eight, which stream 6 frees)</c>, which the oldest table, at <c>Oldest</c>, indexes and stream 6, at
    /// <c>Stream</c>, gives a free row, and with the cross-reference stream of the newest revision's own, which nothing names:
    /// stream 11, at <c>Own</c>, written after object 5 and object 10, <c>(ten)</c>, which only it indexes. Stream 11 gives
    /// through <c>/Prev</c> <paramref name="ownPrev"/>, and stream 6 <paramref name="streamPrev"/>: <c>"nothing"</c>;
    /// <c>"the table's /Prev"</c>, the offset the newest table's <c>/Prev</c> names, written in ten digits in stream 6; or
    /// <c>"the oldest table"</c>, 1,200 bytes before stream 6, out of reach of a search near either. The newest table, at
    /// <c>Newest</c>, past a comment of 600 dashes, indexes object 5 and stream 11, and names through <c>/XRefStm</c> stream 6
    /// and through <c>/Prev</c> the <c>endobj</c> 7 bytes before stream 6 — or, where <paramref name="shortOfOwn"/> says so,
    /// the one 7 bytes before stream 11 —: both streams lie within reach of a search near it, stream 11 the last place there.
    /// The oldest table names through <paramref name="oldestNames"/>: <c>"nothing"</c>; <c>"/XRefStm"</c> or
    /// <c>"/Prev"</c>, stream 11, at its header, or <paramref name="into"/> bytes past its start, inside its object number;
    /// <c>"/Prev, 5 bytes short"</c>, the place 5 bytes before stream 11, inside the <c>endobj</c> before it, stream 11 the
    /// nearest place to it; or <c>"/Prev, 1 byte short"</c>, the end of line after that <c>endobj</c>, which a reading
    /// there crosses to stream 11. Stream 11 is written as object <paramref name="ownNumber"/>, 11 unless it says
    /// otherwise, and its dictionary and the newest table's trailer give a <c>/Size</c> one past the highest object
    /// number. Its dictionary starts with <paramref name="ownEntries"/>, <c>/Type /XRef</c> unless it says otherwise, and gives
    /// <paramref name="ownWidths"/> as its <c>/W</c>; where that is <c>12 0 R</c>, object 12, <c>[1 4 2]</c> and 1,100
    /// spaces, follows the comment of 600 dashes, and the newest table indexes it.
    /// </summary>
    public static (byte[] File, long Oldest, long Stream, long Own, long Newest) PrevFallingShortOfTheStreamBeforeTheTablesOwn(
        string ownPrev,
        bool shortOfOwn,
        string streamPrev = "the oldest table",
        string oldestNames = "nothing",
        string ownWidths = "[1 4 2]",
        string ownEntries = "/Type /XRef ",
        int into = 0,
        int ownNumber = 11)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, _) = Page(file);
        var oldThree = file.Object(3, "(three as the oldest has it)");
        var eight = file.Object(8, "(eight, which stream 6 frees)");
        var oldest = file.Position;
        var oldestLinks = oldestNames switch
        {
            "/XRefStm" => "/XRefStm @@@@OWN@@@ ",
            "/Prev" => "/Prev @@@@OWN@@@ ",
            "/Prev, 5 bytes short" or "/Prev, 1 byte short" => "/Prev @@@SHORT@@ ",
            _ => string.Empty,
        };
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(oldThree)}8 1\n{Row(eight)}trailer\n<< /Size 9 /Root 1 0 R {oldestLinks}>>\nstartxref\n{oldest}\n%%EOF\n"));
        file.Raw("%" + new string('-', 1200) + "\n");
        var four = file.Object(4, "(four)");
        var stream = file.Position;
        var rows = StreamRows((1, four, 0), (1, stream, 0), (0, 0, 1));

        // The offsets the newest table's /Prev and stream 11 lie at are known once stream 11's place is: stream 6, and the
        // oldest table, give stand-ins of ten characters for them, replaced then.
        var streamNames = streamPrev switch
        {
            "the table's /Prev" => "/Prev @@@NAMED@@ ",
            "the oldest table" => Invariant($"/Prev {oldest} "),
            _ => string.Empty,
        };
        file.Object(6, Invariant($"<< /Type /XRef /Size 9 /W [1 4 2] /Index [4 1 6 1 8 1] /Root 1 0 R {streamNames}/Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var five = file.Object(5, "(five)");
        var ten = file.Object(10, "(ten)");
        var own = file.Position;
        var named = (shortOfOwn ? own : stream) - 7;
        var ownRows = StreamRows((1, ten, 0));
        var ownNames = ownPrev switch
        {
            "the table's /Prev" => Invariant($"/Prev {named} "),
            "the oldest table" => Invariant($"/Prev {oldest} "),
            _ => string.Empty,
        };
        var twelve = ownWidths == "12 0 R";
        var size = Math.Max(ownNumber, twelve ? 12 : 11) + 1;
        file.Object(ownNumber, Invariant($"<< {ownEntries}/Size {size} /W {ownWidths} /Index [10 1] /Root 1 0 R {ownNames}/Length {ownRows.Length} >>\nstream\n{ownRows}\nendstream"));
        file.Raw("%" + new string('-', 600) + "\n");
        var twelveRow = twelve ? Invariant($"12 1\n{Row(file.Object(12, "[1 4 2]" + new string(' ', 1100)))}") : string.Empty;
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n5 1\n{Row(five)}{ownNumber} 1\n{Row(own)}{twelveRow}trailer\n<< /Size {size} /Root 1 0 R /Prev {named} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        var text = Encoding.Latin1.GetString(file.Bytes)
            .Replace("@@@NAMED@@", Invariant($"{named:D10}"), StringComparison.Ordinal)
            .Replace("@@@@OWN@@@", Invariant($"{own + into:D10}"), StringComparison.Ordinal)
            .Replace("@@@SHORT@@", Invariant($"{own - (oldestNames == "/Prev, 1 byte short" ? 1 : 5):D10}"), StringComparison.Ordinal);
        return (Encoding.Latin1.GetBytes(text), oldest, stream, own, newest);
    }

    /// <summary>
    /// The document <see cref="PrevFallingShortOfTheStreamBeforeTheTablesOwn"/> writes, its newest table's <c>/Prev</c>
    /// falling 7 bytes short of stream 11, which gives no <c>/Prev</c>, saved once more: a newer table, at <c>Newer</c>,
    /// indexes nothing, and names through <c>/Prev</c> the table before it, at <c>Newest</c>, and through
    /// <c>/XRefStm</c> stream 11, at its header, or <paramref name="into"/> bytes past its start, inside its object number:
    /// a reading of stream 11 there comes before the search near the place the table before names through <c>/Prev</c>.
    /// </summary>
    public static (byte[] File, long Oldest, long Stream, long Own, long Newest, long Newer) NamingTheTablesOwnStreamFromANewerTable(int into)
    {
        var (bytes, oldest, stream, own, newest) = PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", shortOfOwn: true);
        var text = new StringBuilder(Encoding.Latin1.GetString(bytes));
        var newer = text.Length;
        text.Append(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 12 /Root 1 0 R /Prev {newest} /XRefStm {own + into} >>\nstartxref\n{newer}\n%%EOF\n"));
        return (Encoding.Latin1.GetBytes(text.ToString()), oldest, stream, own, newest, newer);
    }

    /// <summary>
    /// A document saved twice. The oldest table, at <c>Oldest</c>, indexes the catalog and the page tree; 1,200 bytes
    /// past it, cross-reference stream 6, at <c>Stream</c>, indexes object 4, <c>(four)</c>, and itself, and names the
    /// oldest table through <c>/Prev</c>. Object 10, <c>(ten)</c>, follows, then, at <c>Near</c>,
    /// <paramref name="near"/>: <c>"stream 11"</c>, a cross-reference stream which nothing names and which indexes
    /// object 10, giving through <c>/Prev</c> <paramref name="streamPrev"/> — <c>"nothing"</c>, or
    /// <c>"the oldest table"</c> — after <paramref name="extra"/> in its dictionary, or before it where
    /// <paramref name="prevFirst"/> says so; or <c>"an outline item"</c>, object 12, whose dictionary gives
    /// <c>/Prev 13 0 R</c>, then <paramref name="extra"/>. The newest table, at <c>Newest</c>, past a comment of 600
    /// dashes, indexes what lies at <c>Near</c>, and names through <c>/XRefStm</c> stream 6 and through <c>/Prev</c>
    /// the <c>endobj</c> 7 bytes before <c>Near</c>: a search near it comes to what lies there before stream 6.
    /// </summary>
    public static (byte[] File, long Oldest, long Stream, long Near, long Newest) PrevFallingShortOfAPlaceBesideTheStream(
        string near, string extra, string streamPrev = "nothing", bool prevFirst = false)
    {
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 3\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}trailer\n<< /Size 3 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        file.Raw("%" + new string('-', 1200) + "\n");
        var four = file.Object(4, "(four)");
        var stream = file.Position;
        var rows = StreamRows((1, four, 0), (1, stream, 0));
        file.Object(6, Invariant($"<< /Type /XRef /Size 7 /W [1 4 2] /Index [4 1 6 1] /Root 1 0 R /Prev {oldest} /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var ten = file.Object(10, "(ten)");
        var ownRows = StreamRows((1, ten, 0));
        var number = near == "stream 11" ? 11 : 12;
        var prev = streamPrev == "the oldest table" ? Invariant($"/Prev {oldest} ") : string.Empty;
        var entries = prevFirst ? prev + extra : extra + prev;
        var at = near == "stream 11"
            ? file.Object(11, Invariant($"<< /Type /XRef /Size 12 /W [1 4 2] /Index [10 1] {entries}/Length {ownRows.Length} >>\nstream\n{ownRows}\nendstream"))
            : file.Object(12, Invariant($"<< /Title (a heading) /Parent 9 0 R /Prev 13 0 R {extra}>>"));
        file.Raw("%" + new string('-', 600) + "\n");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n{number} 1\n{Row(at)}trailer\n<< /Size 14 /Root 1 0 R /Prev {at - 7} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, stream, at, newest);
    }

    /// <summary>
    /// A one-page document saved twice. The oldest table, at <c>Oldest</c>, indexes the first three objects and object 5,
    /// <c>(old five)</c>. The update writes object 4, <c>(four)</c>, and its stream, stream 21, at <c>Stream</c>, which
    /// indexes it and itself and gives no <c>/Prev</c>; then, at <c>Place</c>, <paramref name="place"/>, whose object 9
    /// gives <c>/W [1 1 1]</c>: <c>"a header in a comment"</c>, a comment line that writes the header of a stream of
    /// <c>/Type /XRef</c> and no <c>/Size</c>, its data running to the end of the file; <c>"a header in a string"</c>, the
    /// same in a string that object 8 opens on that line; <c>"a stream of no type"</c>, a stream that gives only that
    /// <c>/W</c>, its header beginning its line, and no <c>/Size</c>; <c>"a header on its line"</c>, a stream of
    /// <c>/Type /XRef</c> and no <c>/Size</c>, its header beginning its line; or, where it reads, <c>"a readable header in a
    /// comment"</c>, <c>"a readable stream of no type"</c> and <c>"a readable header on its line"</c>, which give
    /// <c>/Index [9 1]</c>, <c>/Size 10</c> and the one row of 3 bytes their data holds, freeing object 9. Then comes the
    /// update's table, at <c>Newest</c>, which indexes stream 21, and names it through <c>/XRefStm</c>, and through
    /// <c>/Prev</c> the place 20 bytes before <c>Place</c>: a search near it comes to the place first, then to stream 21,
    /// then to the oldest table — but where <paramref name="alone"/> says so, a comment of 700 dashes before the update
    /// leaves the oldest table out of its reach.
    /// </summary>
    public static (byte[] File, long Oldest, long Stream, long Place, long Newest) PrevFallingShortOfAPlacePastTheStream(string place, bool alone = false)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var oldFive = file.Object(5, "(old five)");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}5 1\n{Row(oldFive)}trailer\n<< /Size 22 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));

        if (alone)
        {
            file.Raw("%" + new string('-', 700) + "\n");
        }

        var four = file.Object(4, "(four)");
        var stream = file.Position;
        var rows = StreamRows((1, four, 0), (1, stream, 0));
        file.Object(21, Invariant($"<< /Type /XRef /Size 22 /W [1 4 2] /Index [4 1 21 1] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        const string Row9 = "/Size 10 /Index [9 1] /W [1 1 1] /Length 3";
        const string Data9 = "\n\u0000\u0000\u0000\nendstream\nendobj\n";
        var line = file.Position;
        var (at, written) = place switch
        {
            "a header in a comment" => (line + 2, "% 9 0 obj << /Type /XRef /W [1 1 1] >> stream\n"),
            "a header in a string" => (line + 9, "8 0 obj\n(9 0 obj << /Type /XRef /W [1 1 1] >> stream)\nendobj\n"),
            "a stream of no type" => (line, "9 0 obj << /W [1 1 1] >> stream\n"),
            "a header on its line" => (line, "9 0 obj << /Type /XRef /W [1 1 1] >> stream\n"),
            "a readable header in a comment" => (line + 2, "% 9 0 obj << /Type /XRef " + Row9 + " >> stream" + Data9),
            "a readable stream of no type" => (line, "9 0 obj\n<< " + Row9 + " >>\nstream" + Data9),
            _ => (line, "9 0 obj\n<< /Type /XRef " + Row9 + " >>\nstream" + Data9),
        };
        file.Raw(written);
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n21 1\n{Row(stream)}trailer\n<< /Size 22 /Root 1 0 R /Prev {at - 20} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, stream, at, newest);
    }

    /// <summary>
    /// A one-page document saved twice. The update writes object 4, <c>(four)</c>, and its stream, stream 21, at
    /// <c>Stream</c>, which indexes it and itself and gives no <c>/Prev</c>; then a line that starts with object 8, a
    /// string of <paramref name="lead"/> <c>x</c>, followed by a space — or by an end of line, where
    /// <paramref name="lineBroken"/> says so, the next line starting at object 9 —, and holds the header of object 9, at
    /// <c>Place</c>, a stream of <c>/Type /XRef</c> that gives <c>/W [1 1 1]</c> and no <c>/Size</c>, its data three bytes;
    /// then a comment, object 5, <c>(old five)</c>, and the oldest table, at <c>Oldest</c>, which indexes the first three
    /// objects and object 5, within 512 bytes of object 9. The update's table, at <c>Newest</c>, indexes stream 21, and
    /// names it through <c>/XRefStm</c>, and through <c>/Prev</c> the place 20 bytes past <c>Place</c>: a search near it
    /// comes to object 9, which lies past stream 21 and may be the table's own, then to the oldest table. Where
    /// <paramref name="lead"/> is 600 or more, and the line is not broken, the line starts before the bytes that search
    /// reads; where it is 5,000, more than 4,096 bytes before them.
    /// </summary>
    public static (byte[] File, long Oldest, long Stream, long Place, long Newest) PrevFallingInsideAStreamOnALongLine(int lead, bool lineBroken)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var four = file.Object(4, "(four)");
        var stream = file.Position;
        var rows = StreamRows((1, four, 0), (1, stream, 0));
        file.Object(21, Invariant($"<< /Type /XRef /Size 22 /W [1 4 2] /Index [4 1 21 1] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var line = "8 0 obj (" + new string('x', lead) + ") endobj" + (lineBroken ? "\n" : " ");
        var at = file.Position + line.Length;
        file.Raw(line + "9 0 obj << /Type /XRef /W [1 1 1] /Length 3 >> stream\n\u0000\u0000\u0000\nendstream\nendobj\n");
        file.Raw("%" + new string('-', 40) + "\n");
        var oldFive = file.Object(5, "(old five)");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}5 1\n{Row(oldFive)}trailer\n<< /Size 22 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n21 1\n{Row(stream)}trailer\n<< /Size 22 /Root 1 0 R /Prev {at + 20} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, stream, at, newest);
    }

    /// <summary>
    /// A document saved four times, each revision but the last a hybrid file's. The first writes the catalog, an empty page
    /// tree and object 7, <c>(seven)</c>, its stream, stream 9, at <c>First</c>, which indexes object 7 and itself, and
    /// its table, which indexes the catalog and the page tree and names stream 9 through <c>/XRefStm</c>. Past a comment of
    /// 1,200 dashes, the second writes object 4, <c>(four)</c>, its stream, stream 10, at <c>Second</c>, which indexes it
    /// and itself and gives no <c>/Prev</c>, and its table, at <c>Table</c>, which indexes the catalog and the page tree
    /// again, and names stream 10 through <c>/XRefStm</c> and the first table through <c>/Prev</c>. The third writes object
    /// 5, <c>(five)</c>, its stream, stream 11, which indexes it and itself, and its table, which indexes nothing, and names
    /// stream 11 and the second table. Past a comment of 600 dashes, the fourth writes object 6, <c>(six)</c>, and a table,
    /// at <c>Newest</c>, which indexes it, and names through <c>/XRefStm</c> stream 9, the first revision's, and through
    /// <c>/Prev</c> the place 5 bytes before stream 10: a search near it comes to stream 10 first, which lies past stream
    /// 9, then to the second revision's table, just past it.
    /// </summary>
    public static (byte[] File, long First, long Second, long Table, long Newest) NamingTheFirstStreamAndFallingShortOfTheSecond()
    {
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var seven = file.Object(7, "(seven)");
        var first = file.Position;
        var firstRows = StreamRows((1, seven, 0), (1, first, 0));
        file.Object(9, Invariant($"<< /Type /XRef /Size 10 /W [1 4 2] /Index [7 1 9 1] /Length {firstRows.Length} >>\nstream\n{firstRows}\nendstream"));
        var firstTable = file.Position;
        file.Raw(Invariant($"xref\n0 3\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}trailer\n<< /Size 10 /Root 1 0 R /XRefStm {first} >>\nstartxref\n{firstTable}\n%%EOF\n"));
        file.Raw("%" + new string('-', 1200) + "\n");
        var four = file.Object(4, "(four)");
        var second = file.Position;
        var secondRows = StreamRows((1, four, 0), (1, second, 0));
        file.Object(10, Invariant($"<< /Type /XRef /Size 11 /W [1 4 2] /Index [4 1 10 1] /Length {secondRows.Length} >>\nstream\n{secondRows}\nendstream"));
        var table = file.Position;
        file.Raw(Invariant($"xref\n0 3\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}trailer\n<< /Size 11 /Root 1 0 R /Prev {firstTable} /XRefStm {second} >>\nstartxref\n{table}\n%%EOF\n"));
        var five = file.Object(5, "(five)");
        var third = file.Position;
        var thirdRows = StreamRows((1, five, 0), (1, third, 0));
        file.Object(11, Invariant($"<< /Type /XRef /Size 12 /W [1 4 2] /Index [5 1 11 1] /Length {thirdRows.Length} >>\nstream\n{thirdRows}\nendstream"));
        var thirdTable = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 12 /Root 1 0 R /Prev {table} /XRefStm {third} >>\nstartxref\n{thirdTable}\n%%EOF\n"));
        file.Raw("%" + new string('-', 600) + "\n");
        var six = file.Object(6, "(six)");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n6 1\n{Row(six)}trailer\n<< /Size 12 /Root 1 0 R /Prev {second - 5} /XRefStm {first} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, first, second, table, newest);
    }

    /// <summary>
    /// A document saved twice whose sections index nothing. Past a comment of 1,200 dashes after the catalog and an empty
    /// page tree, the oldest table, at <c>Oldest</c>, gives the subsection <c>0 0</c>, or, where
    /// <paramref name="oldestFreeRow"/> says so, the row that heads the free list. The update writes object 4,
    /// <c>(four)</c>, cross-reference stream 6, at <c>Stream</c>, of <c>/Index [4 0]</c> and no data, object 10,
    /// <c>(ten)</c>, and the cross-reference stream of its own revision, stream 11, at <c>Own</c>, which nothing names,
    /// gives no <c>/Prev</c>, and has <c>/Index [10 0]</c> and no data; then, past a comment of 600 dashes, its table, at
    /// <c>Newest</c>, which gives the subsection <c>0 0</c>, and names stream 6 through <c>/XRefStm</c> and, through
    /// <c>/Prev</c>, the place 7 bytes before stream 11: a search near it comes to stream 11 first, then to stream 6 and
    /// the oldest table.
    /// </summary>
    public static (byte[] File, long Oldest, long Own, long Newest) PrevFallingShortOfTheTablesOwnStreamInAChainOfEmptySections(bool oldestFreeRow)
    {
        var file = new HandWrittenFile(version: "1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Raw("%" + new string('-', 1200) + "\n");
        var oldest = file.Position;
        file.Raw(oldestFreeRow ? "xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 3 /Root 1 0 R >>\n" : "xref\n0 0\ntrailer\n<< /Size 3 /Root 1 0 R >>\n");
        file.Object(4, "(four)");
        var stream = file.Position;
        file.Object(6, "<< /Type /XRef /Size 7 /W [1 4 2] /Index [4 0] /Root 1 0 R /Length 0 >>\nstream\n\nendstream");
        file.Object(10, "(ten)");
        var own = file.Position;
        file.Object(11, "<< /Type /XRef /Size 12 /W [1 4 2] /Index [10 0] /Root 1 0 R /Length 0 >>\nstream\n\nendstream");
        file.Raw("%" + new string('-', 600) + "\n");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 0\ntrailer\n<< /Size 12 /Root 1 0 R /Prev {own - 7} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, own, newest);
    }

    /// <summary>
    /// A one-page document saved twice, whose newest table, at <c>Newest</c>, indexes nothing, names through <c>/Prev</c>
    /// the place 3 bytes before the oldest table, at <c>Oldest</c>, inside the <c>endobj</c> before it, and through
    /// <c>/XRefStm</c> the place 12 bytes into object 6, inside its string, at <c>Named</c>. Objects 6 and 7, strings, lie
    /// past a comment of 1,200 dashes after the oldest table, and another such comment after them: a search near the place
    /// the <c>/XRefStm</c> names finds their headers alone, and one near the place the <c>/Prev</c> names the oldest table.
    /// </summary>
    public static (byte[] File, long Oldest, long Named, long Newest) NamingAStringAndFallingShortOfTheTableBefore()
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}trailer\n<< /Size 8 /Root 1 0 R >>\n"));
        file.Raw("%" + new string('-', 1200) + "\n");
        var six = file.Object(6, "(six, a string)");
        file.Object(7, "(seven, a string)");
        file.Raw("%" + new string('-', 1200) + "\n");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 8 /Root 1 0 R /Prev {oldest - 3} /XRefStm {six + 12} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, six + 12, newest);
    }

    /// <summary>
    /// A one-page document whose newest table, at <c>Newest</c>, past a comment of 600 dashes, indexes the page tree
    /// and the page, and names through <c>/Prev</c> offset 0, the header, past which the catalog lies, and through
    /// <c>/XRefStm</c> cross-reference stream 6, at <c>Stream</c>, which indexes object 4, <c>(four)</c>, and itself,
    /// within reach of a search near offset 0. Stream 6 gives through <c>/Prev</c> <paramref name="streamPrev"/>:
    /// <c>"nothing"</c>, or <c>"offset 0"</c>, the offset the newest table's <c>/Prev</c> names, the newest table
    /// indexing the catalog too; or <c>"the first revision's stream"</c>, the offset of stream 11, at <c>First</c>, -1
    /// otherwise: the document is then saved twice, its first revision writing the catalog, object 10, <c>(ten)</c>,
    /// and right after them, nearer offset 0 than stream 6, cross-reference stream 11, which indexes them and itself
    /// and gives no <c>/Prev</c>, as a first revision's does.
    /// </summary>
    public static (byte[] File, long First, long Stream, long Newest) PrevOfZeroNearTheStream(string streamPrev)
    {
        var firstRevision = streamPrev == "the first revision's stream";
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var first = -1L;

        if (firstRevision)
        {
            var ten = file.Object(10, "(ten)");
            first = file.Position;
            var firstRows = StreamRows((1, catalog, 0), (1, ten, 0), (1, first, 0));
            file.Object(11, Invariant($"<< /Type /XRef /Size 12 /W [1 4 2] /Index [1 1 10 2] /Root 1 0 R /Length {firstRows.Length} >>\nstream\n{firstRows}\nendstream"));
            file.Raw(Invariant($"startxref\n{first}\n%%EOF\n"));
        }

        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>");
        var four = file.Object(4, "(four)");
        var stream = file.Position;
        var rows = StreamRows((1, four, 0), (1, stream, 0));
        var prev = streamPrev switch
        {
            "the first revision's stream" => Invariant($"/Prev {first} "),
            "offset 0" => "/Prev 0 ",
            _ => string.Empty,
        };
        file.Object(6, Invariant($"<< /Type /XRef /Size 12 /W [1 4 2] /Index [4 1 6 1] /Root 1 0 R {prev}/Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        file.Raw("%" + new string('-', 600) + "\n");
        var newest = file.Position;
        var catalogRow = firstRevision ? "0 1\n0000000000 65535 f \n2 2\n" : Invariant($"0 4\n0000000000 65535 f \n{Row(catalog)}");
        file.Raw(Invariant($"xref\n{catalogRow}{Row(pages)}{Row(page)}trailer\n<< /Size 12 /Root 1 0 R /Prev 0 /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, first, stream, newest);
    }

    /// <summary>
    /// A one-page document saved three times, whose newest table, at <c>Newest</c>, names through <c>/XRefStm</c> the
    /// cross-reference stream of its own revision, stream 6, at <c>Stream</c>, which indexes object 4, <c>(four)</c>,
    /// and itself, and through <c>/Prev</c> the place 12 bytes into that stream's object, inside its dictionary. The
    /// stream names through <c>/Prev</c> <paramref name="ownPrev"/>: <c>"nothing"</c>; <c>"the table's /Prev"</c>, the
    /// place the table's names; <c>"the table's /Prev, as a real"</c>, that place written as a real with no fractional
    /// part; <c>"the table's /Prev, through a reference"</c>, object 20, which holds that place and which the newest
    /// table indexes, written after the stream; <c>"the table's /Prev, through two references"</c>, object 20, which
    /// holds <c>21 0 R</c>, object 21 holding that place, both indexed by the newest table and written after the
    /// stream; <c>"the table's /Prev, through a chain of 32 objects"</c> or <c>"... 33 objects"</c>, object 20, each of
    /// objects 20 to 50, or to 51, holding a reference to the next, and the last, 51 or 52, that place, all indexed by
    /// the newest table and written after the stream — as many objects as a section's offset is read through, or one
    /// more, past which it reads as no offset —; <c>"outside the file"</c>, offset 99,999,999,999;
    /// <c>"a negative offset"</c>, -1; <c>"a name"</c>, <c>/Nowhere</c>; or <c>"the table before"</c>, the table before
    /// the newest, at <c>Previous</c>, written before object 4, within reach of a search near that place, which indexes
    /// object 3 as
    /// <c>(three as the middle revision has it)</c> and names through <c>/Prev</c> the oldest table, at <c>Oldest</c>,
    /// 1,200 bytes before it, which indexes objects 1 to 3, the page among them as <c>(three as the oldest has it)</c>.
    /// </summary>
    public static (byte[] File, long Oldest, long Previous, long Stream, long Newest) PrevFallingInsideTheTablesOwnStream(string ownPrev)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, _) = Page(file);
        var oldThree = file.Object(3, "(three as the oldest has it)");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(oldThree)}trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        file.Raw("%" + new string('-', 1200) + "\n");
        var middleThree = file.Object(3, "(three as the middle revision has it)");
        var previous = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n3 1\n{Row(middleThree)}trailer\n<< /Size 4 /Root 1 0 R /Prev {oldest} >>\nstartxref\n{previous}\n%%EOF\n"));
        var four = file.Object(4, "(four)");
        var stream = file.Position;
        var rows = StreamRows((1, four, 0), (1, stream, 0));
        var prev = ownPrev switch
        {
            "the table's /Prev" => Invariant($"/Prev {stream + 12} "),
            "the table's /Prev, as a real" => Invariant($"/Prev {stream + 12}.0 "),
            "the table's /Prev, through a reference" or "the table's /Prev, through two references" or
                "the table's /Prev, through a chain of 32 objects" or "the table's /Prev, through a chain of 33 objects" => "/Prev 20 0 R ",
            "outside the file" => "/Prev 99999999999 ",
            "a negative offset" => "/Prev -1 ",
            "a name" => "/Prev /Nowhere ",
            "the table before" => Invariant($"/Prev {previous} "),
            _ => string.Empty,
        };
        var chain = ownPrev switch
        {
            "the table's /Prev, through a reference" => 1,
            "the table's /Prev, through two references" => 2,
            "the table's /Prev, through a chain of 32 objects" => 32,
            "the table's /Prev, through a chain of 33 objects" => 33,
            _ => 0,
        };
        var size = chain > 0 ? 20 + chain : 7;
        file.Object(6, Invariant($"<< /Type /XRef /Size {size} /W [1 4 2] /Index [4 1 6 1] /Root 1 0 R {prev}/Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var twenty = new StringBuilder(chain > 0 ? Invariant($"20 {chain}\n") : string.Empty);

        for (var link = 0; link < chain; link++)
        {
            twenty.Append(Row(file.Object(20 + link, link < chain - 1 ? Invariant($"{21 + link} 0 R") : Invariant($"{stream + 12}"))));
        }

        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n6 1\n{Row(stream)}{twenty}trailer\n<< /Size {size} /Root 1 0 R /Prev {stream + 12} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, previous, stream, newest);
    }

    /// <summary>
    /// A one-page document saved twice. Its first revision writes object 5, <c>(old five)</c>, and object 7,
    /// <c>(seven, which the update frees)</c>, and, at <c>First</c>, cross-reference stream 8, which indexes them, the
    /// first three objects and itself, and gives no <c>/Prev</c>, as a first revision's does. The update writes object
    /// 5 again, <c>(new five)</c>, and the cross-reference stream of its own revision, stream 9, at <c>Stream</c>,
    /// which indexes it and itself and gives through <c>/Prev</c> <paramref name="streamPrev"/>:
    /// <c>"the first revision's stream"</c>, the offset of stream 8; <c>"a reference"</c>, <c>99 0 R</c>, which no
    /// section places; <c>"another offset"</c>, offset 9, where the catalog's object starts; or
    /// <c>"the table's /Prev, as a real"</c>, the offset the update's table names, written as a real with no fractional
    /// part. The update's table, at <c>Newest</c>, frees object 7, names stream 9 through <c>/XRefStm</c>, and through
    /// <c>/Prev</c> the place 12 bytes into stream 8's object: a search near it comes to stream 8, and to stream 9
    /// farther off.
    /// </summary>
    public static (byte[] File, long First, long Stream, long Newest) PrevFallingShortOfTheFirstRevisionsStream(string streamPrev)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var oldFive = file.Object(5, "(old five)");
        var seven = file.Object(7, "(seven, which the update frees)");
        var first = file.Position;
        var rows = StreamRows((0, 0, 65535), (1, catalog, 0), (1, pages, 0), (1, page, 0), (0, 0, 1), (1, oldFive, 0), (0, 0, 1), (1, seven, 0), (1, first, 0));
        file.Object(8, Invariant($"<< /Type /XRef /Size 9 /W [1 4 2] /Root 1 0 R /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var five = file.Object(5, "(new five)");
        var stream = file.Position;
        var named = first + 12;
        var prev = streamPrev switch
        {
            "the first revision's stream" => Invariant($"/Prev {first} "),
            "a reference" => "/Prev 99 0 R ",
            "another offset" => "/Prev 9 ",
            _ => Invariant($"/Prev {named}.0 "),
        };
        var ownRows = StreamRows((1, five, 0), (1, stream, 0));
        file.Object(9, Invariant($"<< /Type /XRef /Size 10 /W [1 4 2] /Index [5 1 9 1] {prev}/Length {ownRows.Length} >>\nstream\n{ownRows}\nendstream"));
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n7 1\n0000000000 00001 f \n9 1\n{Row(stream)}trailer\n<< /Size 10 /Root 1 0 R /Prev {named} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, first, stream, newest);
    }

    /// <summary>
    /// A one-page document saved three times. The oldest table, at <c>Oldest</c>, indexes the first three objects and
    /// object 5, <c>(five)</c>. The middle revision writes object 7, <c>(seven, as the middle revision has it)</c>,
    /// and, at <c>Middle</c>, cross-reference stream 13, which indexes it and itself and gives no <c>/Prev</c>, though
    /// an older section stands before it, within reach of a search near it. The newest revision, past a comment of
    /// 1,300 dashes, writes object 4, <c>(four)</c>, and the cross-reference stream of its own revision, stream 11, at
    /// <c>Stream</c>, which indexes it and itself and gives no <c>/Prev</c>, then its table, at <c>Newest</c>, which
    /// indexes nothing and names stream 11 through <c>/XRefStm</c> and, through <c>/Prev</c>, the place 11 bytes before
    /// stream 13: a search near it comes to stream 13, and to the oldest table farther off.
    /// </summary>
    public static (byte[] File, long Oldest, long Middle, long Stream, long Newest) PrevFallingShortOfAMiddleRevisionsStream()
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var five = file.Object(5, "(five)");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}5 1\n{Row(five)}trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        var seven = file.Object(7, "(seven, as the middle revision has it)");
        var middle = file.Position;
        var middleRows = StreamRows((1, seven, 0), (1, middle, 0));
        file.Object(13, Invariant($"<< /Type /XRef /Size 14 /W [1 4 2] /Index [7 1 13 1] /Root 1 0 R /Length {middleRows.Length} >>\nstream\n{middleRows}\nendstream"));
        file.Raw(Invariant($"startxref\n{middle}\n%%EOF\n"));
        file.Raw("%" + new string('-', 1300) + "\n");
        var four = file.Object(4, "(four)");
        var stream = file.Position;
        var rows = StreamRows((1, four, 0), (1, stream, 0));
        file.Object(11, Invariant($"<< /Type /XRef /Size 14 /W [1 4 2] /Index [4 1 11 1] /Root 1 0 R /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 14 /Root 1 0 R /Prev {middle - 11} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, middle, stream, newest);
    }

    /// <summary>
    /// A one-page document saved three times, each update a hybrid file's whose table names its own revision's stream
    /// through <c>/XRefStm</c>. The oldest table, at <c>Oldest</c>, indexes the first three objects and object 5,
    /// <c>(old five)</c>. Past a comment of 1,200 dashes, the middle revision writes object 4, <c>(four)</c>, its stream,
    /// stream 10, at <c>Older</c>, which indexes it and itself, and its table, at <c>Previous</c>, which names the oldest
    /// table through <c>/Prev</c>. Where <paramref name="olderGivesPrev"/>, the middle revision also writes object 7,
    /// <c>(seven, only in the middle revision's table)</c>, before its stream, which its table alone indexes, and its
    /// stream names the oldest table through <c>/Prev</c> too; otherwise its table indexes nothing, and no stream gives a
    /// <c>/Prev</c>. The newest revision writes object 5 again, <c>(new five)</c>, its stream, stream 11, at
    /// <c>Stream</c>, which indexes it and itself, and its table, at <c>Newest</c>, which indexes nothing and names
    /// through <c>/Prev</c> the place 12 bytes into stream 10's object: a search near it comes to stream 10, and to the
    /// middle table farther off.
    /// </summary>
    public static (byte[] File, long Oldest, long Older, long Previous, long Stream, long Newest) PrevFallingShortOfAnOlderRevisionsStream(
        bool olderGivesPrev = false)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var oldFive = file.Object(5, "(old five)");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}5 1\n{Row(oldFive)}trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        file.Raw("%" + new string('-', 1200) + "\n");
        var four = file.Object(4, "(four)");
        var seven = olderGivesPrev ? Invariant($"7 1\n{Row(file.Object(7, "(seven, only in the middle revision's table)"))}") : string.Empty;
        var older = file.Position;
        var olderRows = StreamRows((1, four, 0), (1, older, 0));
        var olderPrev = olderGivesPrev ? Invariant($"/Prev {oldest} ") : string.Empty;
        file.Object(10, Invariant($"<< /Type /XRef /Size 11 /W [1 4 2] /Index [4 1 10 1] {olderPrev}/Length {olderRows.Length} >>\nstream\n{olderRows}\nendstream"));
        var previous = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n{seven}trailer\n<< /Size 11 /Root 1 0 R /Prev {oldest} /XRefStm {older} >>\nstartxref\n{previous}\n%%EOF\n"));
        var five = file.Object(5, "(new five)");
        var stream = file.Position;
        var rows = StreamRows((1, five, 0), (1, stream, 0));
        file.Object(11, Invariant($"<< /Type /XRef /Size 12 /W [1 4 2] /Index [5 1 11 1] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 12 /Root 1 0 R /Prev {older + 12} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, older, previous, stream, newest);
    }

    /// <summary>
    /// A one-page document saved twice, its first revision a hybrid file's. The first revision writes the first three
    /// objects, object 4, <c>(four)</c>, and object 5, <c>(five, only in the first revision's table)</c>; its stream,
    /// stream 20, at <c>First</c>, which indexes the first four objects and itself and gives no <c>/Prev</c>, as a first
    /// revision's does; and its table, at <c>FirstTable</c>, which indexes object 5 alone and names stream 20 through
    /// <c>/XRefStm</c>. Past a comment of 600 dashes, the update writes object 6, <c>(six)</c>, its stream, stream 21, at
    /// <c>Stream</c>, which indexes it and itself and gives no <c>/Prev</c>, and its table, at <c>Newest</c>, which
    /// indexes nothing, names stream 21 through <c>/XRefStm</c>, and through <c>/Prev</c> the place 12 bytes into stream
    /// 20's object: a search near it comes to stream 20, and to the first table farther off.
    /// </summary>
    public static (byte[] File, long First, long FirstTable, long Stream, long Newest) PrevFallingShortOfAFirstHybridRevisionsStream()
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var four = file.Object(4, "(four)");
        var five = file.Object(5, "(five, only in the first revision's table)");
        var first = file.Position;
        var firstRows = StreamRows((1, catalog, 0), (1, pages, 0), (1, page, 0), (1, four, 0), (1, first, 0));
        file.Object(20, Invariant($"<< /Type /XRef /Size 21 /W [1 4 2] /Index [1 4 20 1] /Root 1 0 R /Length {firstRows.Length} >>\nstream\n{firstRows}\nendstream"));
        var firstTable = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n5 1\n{Row(five)}trailer\n<< /Size 21 /Root 1 0 R /XRefStm {first} >>\nstartxref\n{firstTable}\n%%EOF\n"));
        file.Raw("%" + new string('-', 600) + "\n");
        var six = file.Object(6, "(six)");
        var stream = file.Position;
        var rows = StreamRows((1, six, 0), (1, stream, 0));
        file.Object(21, Invariant($"<< /Type /XRef /Size 22 /W [1 4 2] /Index [6 1 21 1] /Root 1 0 R /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 22 /Root 1 0 R /Prev {first + 12} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, first, firstTable, stream, newest);
    }

    /// <summary>
    /// A one-page document saved three or four times, whose writer copies <c>/XRefStm</c> into each later trailer. The
    /// oldest table, at <c>Oldest</c>, indexes the first three objects, object 5, <c>(old five)</c>, and object 7,
    /// <c>(seven, which the third revision frees)</c>. Past a comment of 1,200 dashes, the second revision writes
    /// object 4, <c>(four)</c>, its stream, stream 10, at <c>Stream</c>, which indexes it and itself and gives no
    /// <c>/Prev</c>, and its table, at <c>Previous</c>, which indexes stream 10 and names it through <c>/XRefStm</c>,
    /// and the oldest table through <c>/Prev</c>. The third writes object 5 again, <c>(new five)</c>, and the
    /// cross-reference stream of its own revision, stream 11, at <c>Own</c>, which nothing names, indexes it and
    /// itself, frees object 7, and gives through <c>/Prev</c> <paramref name="ownPrev"/> — <c>"nothing"</c>, or
    /// <c>"the table's /Prev"</c>, the offset its table names —; then, past a comment of 600 dashes, its table, at
    /// <c>Hybrid</c>, which indexes nothing, names stream 10 through <c>/XRefStm</c>, copied, and through <c>/Prev</c>
    /// the place 40 bytes before the end of the second revision's trailer: a search near it comes to stream 11, and to
    /// the second revision's table farther off. Where <paramref name="again"/> says so, a fourth revision, past a
    /// comment of 1,200 dashes, writes object 6, <c>(six)</c>, and a table, at <c>Newest</c>, which indexes it, names
    /// the third revision's table through <c>/Prev</c> and stream 10 through <c>/XRefStm</c>, copied again; otherwise
    /// <c>Newest</c> is the third revision's table. Each <c>/XRefStm</c> names stream 10 <paramref name="shortBy"/>
    /// bytes short of it, inside the <c>endobj</c> before it where that is 7. Where <paramref name="streamLost"/> says
    /// so, stream 10's <c>/W</c> gives a field a negative width: its rows cannot be read.
    /// </summary>
    public static (byte[] File, long Oldest, long Stream, long Previous, long Own, long Hybrid, long Newest) CopyingTheXRefStmIntoLaterTrailers(
        bool again, string ownPrev, int shortBy = 0, bool streamLost = false)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var oldFive = file.Object(5, "(old five)");
        var seven = file.Object(7, "(seven, which the third revision frees)");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}5 1\n{Row(oldFive)}7 1\n{Row(seven)}trailer\n<< /Size 8 /Root 1 0 R >>\n"));
        file.Raw("%" + new string('-', 1200) + "\n");
        var four = file.Object(4, "(four)");
        var stream = file.Position;
        var rows = StreamRows((1, four, 0), (1, stream, 0));
        var widths = streamLost ? "[1 -4 2]" : "[1 4 2]";
        file.Object(10, Invariant($"<< /Type /XRef /Size 11 /W {widths} /Index [4 1 10 1] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var previous = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n10 1\n{Row(stream)}trailer\n<< /Size 11 /Root 1 0 R /Prev {oldest} /XRefStm {stream - shortBy} >>\n"));
        var named = file.Position - 40;
        var five = file.Object(5, "(new five)");
        var own = file.Position;
        var ownRows = StreamRows((1, five, 0), (0, 0, 1), (1, own, 0));
        var prev = ownPrev == "the table's /Prev" ? Invariant($"/Prev {named} ") : string.Empty;
        file.Object(11, Invariant($"<< /Type /XRef /Size 12 /W [1 4 2] /Index [5 1 7 1 11 1] /Root 1 0 R {prev}/Length {ownRows.Length} >>\nstream\n{ownRows}\nendstream"));
        file.Raw("%" + new string('-', 600) + "\n");
        var hybrid = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 12 /Root 1 0 R /Prev {named} /XRefStm {stream - shortBy} >>\n"));
        var newest = hybrid;

        if (again)
        {
            file.Raw("%" + new string('-', 1200) + "\n");
            var six = file.Object(6, "(six)");
            newest = file.Position;
            file.Raw(Invariant($"xref\n6 1\n{Row(six)}trailer\n<< /Size 12 /Root 1 0 R /Prev {hybrid} /XRefStm {stream - shortBy} >>\n"));
        }

        file.Raw(Invariant($"startxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, stream, previous, own, hybrid, newest);
    }

    /// <summary>
    /// A one-page document saved twice or three times. The oldest table indexes the first three objects and object 8,
    /// <c>(eight, which stream 6 frees)</c>. Past a comment of 1,200 dashes, the update writes object 4, <c>(four)</c>;
    /// cross-reference stream 6, at <c>Stream</c>, which indexes it and itself, frees object 8 and gives no
    /// <c>/Prev</c>; objects 5, <c>(five)</c>, and 10, <c>(ten)</c>; <paramref name="objects"/> objects of one null
    /// each, numbered from 40, then as many again; between them, at <c>Own</c>, the cross-reference stream of its own
    /// revision, stream 11, which nothing names, gives no <c>/Prev</c>, and indexes the first three objects, object 10
    /// and itself; and, past a comment of 600 dashes, its table, at <c>Hybrid</c>, which indexes objects 5 and 11, and
    /// names stream 6 through <c>/XRefStm</c> and, through <c>/Prev</c>, the place 5 bytes before stream 11: a search
    /// near it comes to stream 11 first, then to the objects around it. Where <paramref name="spent"/> is not 0, the
    /// document is saved once more as <see cref="UnderPlacesSpent"/> saves it: a search near the newest table's
    /// <c>/Prev</c> tries <paramref name="spent"/> places, and takes the update's table. Otherwise <c>Newest</c> is the
    /// update's table.
    /// </summary>
    public static (byte[] File, long Stream, long Own, long Hybrid, long Newest) PrevFallingShortOfTheTablesOwnStreamAmongObjects(int objects, int spent)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var eight = file.Object(8, "(eight, which stream 6 frees)");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}8 1\n{Row(eight)}trailer\n<< /Size 9 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        file.Raw("%" + new string('-', 1200) + "\n");
        var four = file.Object(4, "(four)");
        var stream = file.Position;
        var rows = StreamRows((1, four, 0), (1, stream, 0), (0, 0, 1));
        file.Object(6, Invariant($"<< /Type /XRef /Size 100 /W [1 4 2] /Index [4 1 6 1 8 1] /Root 1 0 R /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var five = file.Object(5, "(five)");
        var ten = file.Object(10, "(ten)");

        for (var i = 0; i < objects; i++)
        {
            file.Object(40 + i, "null");
        }

        var own = file.Position;
        var ownRows = StreamRows((1, catalog, 0), (1, pages, 0), (1, page, 0), (1, ten, 0), (1, own, 0));
        file.Object(11, Invariant($"<< /Type /XRef /Size 100 /W [1 4 2] /Index [1 3 10 2] /Root 1 0 R /Length {ownRows.Length} >>\nstream\n{ownRows}\nendstream"));

        for (var i = 0; i < objects; i++)
        {
            file.Object(40 + objects + i, "null");
        }

        file.Raw("%" + new string('-', 600) + "\n");
        var hybrid = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n5 1\n{Row(five)}11 1\n{Row(own)}trailer\n<< /Size 100 /Root 1 0 R /Prev {own - 5} /XRefStm {stream} >>\nstartxref\n{hybrid}\n%%EOF\n"));

        if (spent == 0)
        {
            return (file.Bytes, stream, own, hybrid, hybrid);
        }

        var (spending, newest) = UnderPlacesSpent(file.Bytes, hybrid, spent);
        return (spending, stream, own, hybrid, newest);
    }

    /// <summary>
    /// A one-page document saved four times, whose writer copies <c>/XRefStm</c> into each later trailer. The oldest
    /// table, at <c>Oldest</c>, indexes the first three objects. Past a comment of 1,200 dashes, the second revision
    /// writes object 4, <c>(four)</c>, its stream, stream 10, at <c>Stream</c>, which indexes it and itself and gives
    /// no <c>/Prev</c>, and its table, at <c>Older</c>, which names stream 10 through <c>/XRefStm</c> and the oldest
    /// table through <c>/Prev</c>. The third writes object 5, <c>(five)</c>, and the cross-reference stream of its own
    /// revision, stream 11, at <c>Own</c>, which nothing names, indexes it and itself and gives no <c>/Prev</c>, then
    /// its table, at <c>Middle</c>, which names stream 10 through <c>/XRefStm</c> and, through <c>/Prev</c>, the place
    /// 5 bytes before stream 11. The fourth writes a table, at <c>Newest</c>, which names stream 10 through
    /// <c>/XRefStm</c> and, through <c>/Prev</c>, the place 40 bytes into stream 11's object: a search near either
    /// place comes to stream 11 first, and to the third revision's table, or the second's, farther off.
    /// </summary>
    public static (byte[] File, long Oldest, long Stream, long Older, long Own, long Middle, long Newest) PassingOverOneStreamTwice()
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}trailer\n<< /Size 4 /Root 1 0 R >>\n"));
        file.Raw("%" + new string('-', 1200) + "\n");
        var four = file.Object(4, "(four)");
        var stream = file.Position;
        var rows = StreamRows((1, four, 0), (1, stream, 0));
        file.Object(10, Invariant($"<< /Type /XRef /Size 11 /W [1 4 2] /Index [4 1 10 1] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var older = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 11 /Root 1 0 R /Prev {oldest} /XRefStm {stream} >>\n"));
        var five = file.Object(5, "(five)");
        var own = file.Position;
        var ownRows = StreamRows((1, five, 0), (1, own, 0));
        file.Object(11, Invariant($"<< /Type /XRef /Size 12 /W [1 4 2] /Index [5 1 11 1] /Length {ownRows.Length} >>\nstream\n{ownRows}\nendstream"));
        var middle = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 12 /Root 1 0 R /Prev {own - 5} /XRefStm {stream} >>\n"));
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 12 /Root 1 0 R /Prev {own + 40} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, stream, older, own, middle, newest);
    }

    /// <summary>
    /// A one-page document saved five times. The oldest table indexes the first three objects. Past a comment of 1,200
    /// dashes, the second revision writes object 4, <c>(four)</c>, its stream, stream 10, which indexes it and itself
    /// and gives no <c>/Prev</c>, and a classic table, at <c>Previous</c>, which names the oldest through <c>/Prev</c>
    /// and no stream. The third writes object 5, <c>(five)</c>, and stream 11, at <c>Own</c>, which nothing names,
    /// indexes it and itself and gives no <c>/Prev</c>; then a classic table, at <c>Middle</c>, which indexes the first
    /// three objects again, names no stream, and names through <c>/Prev</c> the place 5 bytes before stream 11: a
    /// search near it comes to stream 11 first, and to the second revision's table farther off. Past a comment of 600
    /// dashes, the fourth writes a table which names the third's through <c>/Prev</c>, and stream 10 through
    /// <c>/XRefStm</c>; the fifth a table, at <c>Newest</c>, which names the fourth's through <c>/Prev</c>, and stream
    /// 10 through <c>/XRefStm</c>.
    /// </summary>
    public static (byte[] File, long Previous, long Own, long Middle, long Newest) NamingAStreamAgainAboveATableThatNamesNone()
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}trailer\n<< /Size 4 /Root 1 0 R >>\n"));
        file.Raw("%" + new string('-', 1200) + "\n");
        var four = file.Object(4, "(four)");
        var stream = file.Position;
        var rows = StreamRows((1, four, 0), (1, stream, 0));
        file.Object(10, Invariant($"<< /Type /XRef /Size 11 /W [1 4 2] /Index [4 1 10 1] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var previous = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 11 /Root 1 0 R /Prev {oldest} >>\n"));
        var five = file.Object(5, "(five)");
        var own = file.Position;
        var ownRows = StreamRows((1, five, 0), (1, own, 0));
        file.Object(11, Invariant($"<< /Type /XRef /Size 12 /W [1 4 2] /Index [5 1 11 1] /Length {ownRows.Length} >>\nstream\n{ownRows}\nendstream"));
        var middle = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}trailer\n<< /Size 12 /Root 1 0 R /Prev {own - 5} >>\n"));
        file.Raw("%" + new string('-', 600) + "\n");
        var hybrid = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 12 /Root 1 0 R /Prev {middle} /XRefStm {stream} >>\n"));
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 12 /Root 1 0 R /Prev {hybrid} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, previous, own, middle, newest);
    }

    /// <summary>
    /// A one-page document saved twice. The oldest table indexes the first three objects. Past a comment of 1,200 dashes,
    /// the update writes object 4, <c>(four)</c>, and the cross-reference stream of its own revision, stream 13, at
    /// <c>Stream</c>, which indexes the first four objects and itself and gives no <c>/Prev</c>; then its table, at
    /// <c>Newest</c>, which indexes nothing, and names through <c>/XRefStm</c> the place 1 byte into the stream's object
    /// number, its digit 3, and through <c>/Prev</c> the place 7 bytes before the stream, inside the <c>endobj</c> before
    /// it: read from that digit, the stream is object 3, and a search near the place the <c>/Prev</c> names comes to the
    /// stream's header, 1 byte before where that reading started.
    /// </summary>
    public static (byte[] File, long Stream, long Newest) NamingAStreamOneByteIntoItsNumber()
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        file.Raw("%" + new string('-', 1200) + "\n");
        var four = file.Object(4, "(four)");
        var stream = file.Position;
        var rows = StreamRows((1, catalog, 0), (1, pages, 0), (1, page, 0), (1, four, 0), (1, stream, 0));
        file.Object(13, Invariant($"<< /Type /XRef /Size 14 /W [1 4 2] /Index [1 4 13 1] /Root 1 0 R /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 14 /Root 1 0 R /Prev {stream - 7} /XRefStm {stream + 1} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, stream, newest);
    }

    /// <summary>
    /// A one-page document saved twice. The oldest table, at <c>Oldest</c>, indexes the first three objects and object 5,
    /// <c>(old five)</c>. The update writes object 4, <c>(four)</c>, and the cross-reference stream of its own revision,
    /// stream 13, at <c>Stream</c>, right after it, which indexes object 4 and itself — and the first three objects too
    /// where <paramref name="catalogInStream"/> — and gives through <c>/Prev</c>
    /// <paramref name="streamPrev"/>: <c>"nothing"</c>; <c>"the table's /Prev"</c>, the place the table's names; or
    /// <c>"the oldest table"</c>. The stream's header is written <paramref name="header"/>: <c>13 0 obj</c> unless it says
    /// otherwise — <c>+13 0 obj</c>, its number written with a sign, or <c>+13 0 %c</c> and <c>obj</c> on the next line, a
    /// comment parting its tokens as well. Past a comment of 600 dashes, its table, at <c>Newest</c>, indexes nothing, and
    /// names through <c>/Prev</c> the place 7 bytes before the stream, inside the <c>endobj</c> before it, and through
    /// <c>/XRefStm</c> the place <paramref name="into"/> bytes past the start of the stream's header: the header's first
    /// byte, at 0, a digit 1 or a sign; or a place past it, inside the object number, from which the stream reads as object
    /// 13 or 3. A search near the place the <c>/Prev</c> names comes to the stream's header — at the digit past its sign,
    /// where no comment parts its tokens —, and to the oldest table farther off, within reach.
    /// </summary>
    public static (byte[] File, long Oldest, long Stream, long Newest) NamingTheUpdatesStreamAtOrInsideItsNumber(
        int into, string streamPrev, bool catalogInStream, string header = "13 0 obj")
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var five = file.Object(5, "(old five)");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}5 1\n{Row(five)}trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        var four = file.Object(4, "(four)");
        var stream = file.Position;
        var rows = catalogInStream
            ? StreamRows((1, catalog, 0), (1, pages, 0), (1, page, 0), (1, four, 0), (1, stream, 0))
            : StreamRows((1, four, 0), (1, stream, 0));
        var prev = streamPrev switch
        {
            "the table's /Prev" => Invariant($"/Prev {stream - 7} "),
            "the oldest table" => Invariant($"/Prev {oldest} "),
            _ => string.Empty,
        };
        file.Raw(Invariant($"{header}\n<< /Type /XRef /Size 14 /W [1 4 2] /Index {(catalogInStream ? "[1 4 13 1]" : "[4 1 13 1]")} /Root 1 0 R {prev}/Length {rows.Length} >>\nstream\n{rows}\nendstream\nendobj\n"));
        file.Raw("%" + new string('-', 600) + "\n");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 14 /Root 1 0 R /Prev {stream - 7} /XRefStm {stream + into} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, stream, newest);
    }

    /// <summary>
    /// A one-page document saved three times, whose writer copies <c>/XRefStm</c> into the next trailer. The oldest table,
    /// at <c>Oldest</c>, indexes the first three objects and object 5, <c>(old five)</c>. The second revision writes
    /// object 4, <c>(four)</c>, and the cross-reference stream of its own revision, stream 13, at <c>Stream</c>, right
    /// after it, which indexes the first four objects and itself and gives no <c>/Prev</c>, then its table, at
    /// <c>Middle</c>, which indexes nothing, and names through <c>/Prev</c> the place 7 bytes before the stream, inside the
    /// <c>endobj</c> before it, and through <c>/XRefStm</c> the stream's header where not
    /// <paramref name="insideItsNumber"/>, or the place 1 byte into the stream's object number, its digit 3, where the
    /// stream reads as object 3. Past a comment of 600 dashes, the newest table, at <c>Newest</c>, indexes nothing, names
    /// the middle table through <c>/Prev</c>, and through <c>/XRefStm</c> the place the middle table's names: the newest
    /// table's <c>/XRefStm</c> reads the stream, and the middle table's names it again. A search near the place the middle
    /// table's <c>/Prev</c> names comes to the stream's header, and to the oldest table farther off, within reach.
    /// </summary>
    public static (byte[] File, long Oldest, long Stream, long Middle, long Newest) NamingTheUpdatesStreamAgainAtOrInsideItsNumber(
        bool insideItsNumber)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var five = file.Object(5, "(old five)");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}5 1\n{Row(five)}trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        var four = file.Object(4, "(four)");
        var stream = file.Position;
        var rows = StreamRows((1, catalog, 0), (1, pages, 0), (1, page, 0), (1, four, 0), (1, stream, 0));
        file.Object(13, Invariant($"<< /Type /XRef /Size 14 /W [1 4 2] /Index [1 4 13 1] /Root 1 0 R /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var named = insideItsNumber ? stream + 1 : stream;
        var middle = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 14 /Root 1 0 R /Prev {stream - 7} /XRefStm {named} >>\nstartxref\n{middle}\n%%EOF\n"));
        file.Raw("%" + new string('-', 600) + "\n");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 14 /Root 1 0 R /Prev {middle} /XRefStm {named} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, stream, middle, newest);
    }

    /// <summary>
    /// A document saved twice. The first revision writes the catalog, an empty page tree and object 3,
    /// <c>(three as the oldest has it)</c>, and a classic table, at <c>Oldest</c>, that indexes them; then, where not
    /// <paramref name="oldestWithinReach"/>, a comment of 700 bytes. The update writes object 4, <c>(four)</c>, and the
    /// cross-reference stream of its own revision, stream 6, at <c>Stream</c>, which indexes object 4, itself and objects
    /// 20 and 21, and gives <c>/Prev 20 0 R</c>; object 20, which holds <c>21 0 R</c> and a comment of 1,100 bytes, which
    /// <c>MaxObjectLength</c> 1,024 cuts after the reference, whole; and object 21, which holds the place 7 bytes before
    /// the stream, where <paramref name="streamPrev"/> is <c>"the table's /Prev"</c>, or the oldest table's offset,
    /// where it is <c>"the oldest table"</c>. Its table, at <c>Newest</c>, indexes nothing, and names the stream through
    /// <c>/XRefStm</c> and the place 7 bytes before it, inside the <c>endobj</c> before it, through <c>/Prev</c>: a
    /// search near that place comes to the stream, and to the oldest table farther off where it lies within reach.
    /// </summary>
    public static (byte[] File, long Oldest, long Stream, long Newest) PrevFallingShortOfAStreamWhosePrevLeadsThroughALongObject(
        bool oldestWithinReach, string streamPrev)
    {
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var three = file.Object(3, "(three as the oldest has it)");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(three)}trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        file.Raw(oldestWithinReach ? string.Empty : "%" + new string('-', 698) + "\n");
        var four = file.Object(4, "(four)");
        var stream = file.Position;

        // Where objects 20 and 21 start is known before the stream is written: its rows have a fixed width.
        var twenty = "21 0 R\n%" + new string('=', 1100);
        string Body(long twentyAt, long twentyOneAt)
        {
            var rows = StreamRows((1, four, 0), (1, stream, 0), (1, twentyAt, 0), (1, twentyOneAt, 0));
            return Invariant($"<< /Type /XRef /Size 22 /W [1 4 2] /Index [4 1 6 1 20 2] /Root 1 0 R /Prev 20 0 R /Length {rows.Length} >>\nstream\n{rows}\nendstream");
        }

        var twentyStart = stream + Invariant($"6 0 obj\n{Body(0, 0)}\nendobj\n").Length;
        var twentyOneStart = twentyStart + Invariant($"20 0 obj\n{twenty}\nendobj\n").Length;
        file.Object(6, Body(twentyStart, twentyOneStart));
        file.Object(20, twenty);
        file.Object(21, Invariant($"{(streamPrev == "the oldest table" ? oldest : stream - 7)}"));
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 22 /Root 1 0 R /Prev {stream - 7} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, stream, newest);
    }

    /// <summary>
    /// <paramref name="file"/>, whose newest table lies at <paramref name="newest"/>, saved once more:
    /// <paramref name="spent"/> less one objects of one null each, numbered from 70, past it, and a table, at
    /// <c>Newest</c>, which indexes nothing and names through <c>/Prev</c> the place among them halfway, within reach
    /// of that table: a search near it tries each of them, nearer than that table, then takes that table,
    /// <paramref name="spent"/> places in all.
    /// </summary>
    public static (byte[] File, long Newest) UnderPlacesSpent(byte[] file, long newest, int spent)
    {
        var text = new StringBuilder(Encoding.Latin1.GetString(file));
        var first = (long)text.Length;

        for (var i = 0; i < spent - 1; i++)
        {
            text.Append(Invariant($"{70 + i} 0 obj\nnull\nendobj\n"));
        }

        var halfway = (first + text.Length) / 2;
        var spender = (long)text.Length;
        text.Append(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 100 /Root 1 0 R /Prev {halfway} >>\nstartxref\n{spender}\n%%EOF\n"));
        return (Encoding.Latin1.GetBytes(text.ToString()), spender);
    }

    /// <summary>
    /// A document saved three times. The first revision writes the catalog, an empty page tree and object 6,
    /// <c>(old six)</c>, and a classic table at <c>Oldest</c> that indexes them. The second writes object 6 again,
    /// <c>(new six)</c>; a classic table at <c>Table</c>, which no <c>/Prev</c> names, whose subsection <c>6 2</c> holds the
    /// row of the newer revision, then <paramref name="rest"/>; and a hybrid file's table at <c>Hybrid</c> that indexes
    /// nothing and names that table through <c>/XRefStm</c> and the oldest through <c>/Prev</c>. The third writes
    /// cross-reference stream 9, whose rows cannot be read — its <c>/W</c> gives a field a negative width —, of
    /// <c>/Index <paramref name="index"/></c>, whose <c>/Prev</c> names the hybrid table, and a table, which <c>startxref</c>
    /// names, that indexes the catalog and the page tree and names the stream through <c>/Prev</c>.
    /// </summary>
    public static (byte[] File, long Table, long Hybrid) PastALostStream(string index, string rest)
    {
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var oldSix = file.Object(6, "(old six)");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 3\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}6 1\n{Row(oldSix)}trailer\n<< /Size 7 >>\n"));
        var six = file.Object(6, "(new six)");
        var table = file.Position;
        file.Raw(Invariant($"xref\n6 2\n{Row(six)}{rest}"));
        var hybrid = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 9 /XRefStm {table} /Prev {oldest} >>\n"));
        var lost = file.Object(9, Invariant($"<< /Type /XRef /Size 12 /Index {index} /W [1 -4 2] /Prev {hybrid} /Length 0 >>\nstream\n\nendstream"));
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 3\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}trailer\n<< /Size 12 /Root 1 0 R /Prev {lost} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, table, hybrid);
    }

    /// <summary>
    /// A one-page document saved twice, whose newest table names through <c>/Prev</c> the first of <paramref name="run"/>
    /// line feeds before cross-reference stream 4: the oldest table, at <c>Oldest</c>, indexes objects 1 to 3 and object 7,
    /// <c>(old seven)</c>; the run starts at <c>Run</c>; stream 4, at <c>Four</c>, gives no type, the <c>/W</c>
    /// <c>[1 4 2]</c>, an <c>/Index</c> whose one row places object 7's new revision, <c>(new seven)</c>, a <c>/Pad</c> string
    /// of <paramref name="pad"/> dots before them where it is not 0, and names the oldest table through <c>/Prev</c>;
    /// cross-reference stream 9, of <c>/Type /XRef</c>, at <c>Stream</c>, 150 bytes past object 8, a string, indexes object 5,
    /// <c>(five)</c>; the newest table, past a long comment, indexes object 6, <c>(six)</c>, and names through
    /// <c>/XRefStm</c>, at <c>Named</c>, <paramref name="named"/>: <c>"inside a string past object 4"</c>, 25 bytes into
    /// object 8, written right after object 4, which is then the nearest place to it, the stream next;
    /// <c>"inside a string before the run"</c>, 25 bytes into object 8, written right before the run, 600 spaces past the
    /// oldest table, object 4 the nearest place to it and the oldest table and the stream out of reach; <c>"in the run"</c>,
    /// 7 bytes before object 4; or <c>"the stream"</c>, stream 9's header. But for the second, the oldest table lies within
    /// reach of a search near the run's start.
    /// </summary>
    public static (byte[] File, long Oldest, long Run, long Four, long Stream, long Named) PrevNamingARunBeforeAStream(
        int run, int pad, string named)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var oldSeven = file.Object(7, "(old seven)");
        var five = file.Object(5, "(five)");
        var newSeven = file.Object(7, "(new seven)");
        file.Raw("%" + new string('-', 1200) + "\n");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}7 1\n{Row(oldSeven)}trailer\n<< /Size 8 /Root 1 0 R >>\n"));
        var before = named == "inside a string before the run";

        if (before)
        {
            file.Raw(new string(' ', 600));
        }

        var eight = before ? file.Object(8, "(" + new string('y', 30) + ")") : -1;
        var runAt = file.Position;
        file.Raw(new string('\n', run));
        var row = StreamRows((1, newSeven, 0));
        var padding = pad > 0 ? "/Pad (" + new string('.', pad) + ") " : string.Empty;
        var four = file.Object(4, Invariant($"<< {padding}/Size 10 /W [1 4 2] /Index [7 1] /Prev {oldest} /Length {row.Length} >>\nstream\n{row}\nendstream"));
        eight = before ? eight : file.Object(8, "(" + new string('y', 30) + ")");
        file.Raw(new string(' ', 150));
        var stream = file.Position;
        var rows = StreamRows((1, five, 0));
        file.Object(9, Invariant($"<< /Type /XRef /Size 10 /W [1 4 2] /Index [5 1] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        file.Raw("%" + new string('-', 1200) + "\n");
        var six = file.Object(6, "(six)");
        var newest = file.Position;
        var at = named switch
        {
            "in the run" => four - 7,
            "the stream" => stream,
            _ => eight + 25,
        };
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n6 1\n{Row(six)}trailer\n<< /Size 10 /Root 1 0 R /Prev {runAt} /XRefStm {at} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, runAt, four, stream, at);
    }

    /// <summary>
    /// A one-page document saved twice, whose newest table names through <c>/XRefStm</c>, at <c>Bracket</c>, the
    /// <c>]</c> that closes the array of object 8, which <paramref name="run"/> line feeds follow before its <c>endobj</c>,
    /// and through <c>/Prev</c> the first of them, at <c>Bracket + 1</c>: cross-reference stream 9, of <c>/Type /XRef</c>,
    /// at <c>Stream</c>, which indexes object 7, <c>(seven)</c>, and gives no <c>/Prev</c>, lies just before object 8, within
    /// reach of a search near either. The newest table, past a long comment, indexes the catalog, the page tree and its
    /// page.
    /// </summary>
    public static (byte[] File, long Stream, long Bracket) NamingTheTokenBeforeARunOfWhiteSpace(int run)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var seven = file.Object(7, "(seven)");
        var stream = file.Position;
        var rows = StreamRows((1, seven, 0));
        file.Object(9, Invariant($"<< /Type /XRef /Size 10 /W [1 4 2] /Index [7 1] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var eight = file.Object(8, "[1 2]" + new string('\n', run));
        var bracket = eight + "8 0 obj\n[1 2".Length;
        file.Raw("%" + new string('-', 1200) + "\n");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}trailer\n<< /Size 10 /Root 1 0 R /Prev {bracket + 1} /XRefStm {bracket} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, stream, bracket);
    }

    /// <summary>
    /// A one-page document saved twice, whose newest table names through <c>/Prev</c> the first of <paramref name="run"/>
    /// spaces before the oldest table — the table itself where it is 0. The update writes object 4, <c>(four)</c>, and
    /// two cross-reference streams that index it and themselves and give no <c>/Prev</c>, each of <c>/Type /XRef</c>, its
    /// header beginning its line: stream 11, at <c>Stream</c>, which the newest table names through <c>/XRefStm</c>, and
    /// stream 12, at <c>Own</c>, right after it, which may be that table's own. The run follows, at <c>Run</c>, then the
    /// oldest table, at <c>Oldest</c>, which indexes objects 1 to 3, and the newest table, at <c>Newest</c>, which indexes
    /// nothing. A search near the run's first space comes to stream 12, past stream 11, and to the oldest table, where the
    /// run is shorter than 512 bytes.
    /// </summary>
    public static (byte[] File, long Run, long Oldest, long Stream, long Own, long Newest) PrevNamingWhiteSpaceBeforeTheOldestTable(int run)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var four = file.Object(4, "(four)");
        var stream = file.Position;
        var rows = StreamRows((1, four, 0), (1, stream, 0));
        file.Object(11, Invariant($"<< /Type /XRef /Size 13 /W [1 4 2] /Index [4 1 11 1] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var own = file.Position;
        var ownRows = StreamRows((1, four, 0), (1, own, 0));
        file.Object(12, Invariant($"<< /Type /XRef /Size 13 /W [1 4 2] /Index [4 1 12 1] /Length {ownRows.Length} >>\nstream\n{ownRows}\nendstream"));
        var runAt = file.Position;
        file.Raw(new string(' ', run));
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}trailer\n<< /Size 13 /Root 1 0 R >>\n"));
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 13 /Root 1 0 R /Prev {runAt} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, runAt, oldest, stream, own, newest);
    }

    /// <summary>
    /// A one-page document saved three times. Objects 4, <c>(four)</c>, and 5, <c>(five)</c>, follow the first three, then
    /// two cross-reference streams that index object 4 and themselves and give no <c>/Prev</c>, each of <c>/Type /XRef</c>,
    /// its header beginning its line: stream 11, at <c>Stream</c>, and stream 12, at <c>Own</c> — past
    /// <paramref name="run"/> spaces, at <c>Run</c>, where <paramref name="runBefore"/> is <c>"the stream"</c> —, which a
    /// comment of dashes follows. The table past the comment, at <c>Older</c>, indexes objects 1 to 3, and names through
    /// <c>/Prev</c> the oldest table, at <c>Oldest</c> — or, where <paramref name="runBefore"/> is
    /// <c>"the oldest table"</c>, the first of <paramref name="run"/> spaces, at <c>Run</c>, written between the two —.
    /// The oldest table indexes object 5, and names through <c>/XRefStm</c> stream 12, or the first space of the run before
    /// it, where <paramref name="oldestNamesOwn"/> says so, and nothing otherwise. The newest table, at <c>Newest</c>,
    /// indexes nothing, and names through <c>/XRefStm</c> stream 11 and through <c>/Prev</c> the place 200 bytes past the
    /// start of stream 12, inside the comment, 240 bytes before the table past it: a search near that place comes to
    /// stream 12, which lies past stream 11 and may be the newest table's own, and to the table past the comment.
    /// </summary>
    public static (byte[] File, long Stream, long Own, long Run, long Older, long Oldest, long Newest) PassingOverAStreamAnOlderTableNamesPastWhiteSpace(
        string runBefore, int run, bool oldestNamesOwn = true)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var four = file.Object(4, "(four)");
        var five = file.Object(5, "(five)");
        var stream = file.Position;
        var rows = StreamRows((1, four, 0), (1, stream, 0));
        file.Object(11, Invariant($"<< /Type /XRef /Size 13 /W [1 4 2] /Index [4 1 11 1] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var runAt = file.Position;

        if (runBefore == "the stream")
        {
            file.Raw(new string(' ', run));
        }

        var own = file.Position;
        var ownRows = StreamRows((1, four, 0), (1, own, 0));
        file.Object(12, Invariant($"<< /Type /XRef /Size 13 /W [1 4 2] /Index [4 1 12 1] /Length {ownRows.Length} >>\nstream\n{ownRows}\nendstream"));
        var named = own + 200;
        file.Raw("%" + new string('-', (int)(named + 240 - file.Position - 2)) + "\n");

        // The older table's /Prev, written in ten digits, names what follows the table: the run, or the oldest table.
        var older = file.Position;
        string Older(long previous) => Invariant(
            $"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}trailer\n<< /Size 13 /Root 1 0 R /Prev {previous:D10} >>\n");
        var past = older + Older(0).Length;
        file.Raw(Older(past));

        if (runBefore == "the oldest table")
        {
            runAt = past;
            file.Raw(new string(' ', run));
        }

        var oldest = file.Position;
        var link = oldestNamesOwn ? Invariant($"/XRefStm {(runBefore == "the stream" ? runAt : own)} ") : string.Empty;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n5 1\n{Row(five)}trailer\n<< /Size 13 {link}>>\n"));
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 13 /Root 1 0 R /Prev {named} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, stream, own, runAt, older, oldest, newest);
    }

    /// <summary>
    /// A one-page document saved twice, whose middle table's <c>/Prev</c> names what the newest table's <c>/Prev</c> named
    /// and found moved: object 12, <c>4</c>, and object 5, <c>(five)</c>, lie before cross-reference stream 9, of
    /// <c>/Type /XRef</c>, at <c>Stream</c>, which indexes object 5; object 8, a string, lies 150 bytes past it, and
    /// object 4, at <c>Four</c>, right after it — <paramref name="inWhiteSpace"/>, 6 spaces before it —, a stream of
    /// <paramref name="type"/> — written whole, a space after it, or nothing — whose <c>/W</c> gives its second width as the
    /// reference <c>12 0 R</c>, which only the middle table places, and whose one row places nothing. The middle table, at
    /// <c>Table</c>, right after object 4, indexes objects 1 to 3 and object 12, and names object 4 through <c>/Prev</c> and,
    /// through <c>/XRefStm</c>, at <c>Named</c>, the place 25 bytes into object 8, inside its string — or the first of the 6
    /// spaces before object 4 —, nearer object 4 than the stream. The newest table, past a long comment, at <c>Newest</c>,
    /// indexes object 6, <c>(six)</c>, and names object 4 through <c>/Prev</c>, where nothing places object 12 yet: object 4
    /// reads as no section there, and the middle table is the nearest one.
    /// </summary>
    public static (byte[] File, long Stream, long Named, long Four, long Table, long Newest) PrevLoopingBesideTheTablesStream(
        string type, bool inWhiteSpace)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var five = file.Object(5, "(five)");
        var twelve = file.Object(12, "4");
        file.Raw("%" + new string('-', 900) + "\n");
        var stream = file.Position;
        var rows = StreamRows((1, five, 0));
        file.Object(9, Invariant($"<< /Type /XRef /Size 13 /W [1 4 2] /Index [5 1] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        file.Raw(new string(' ', 150));
        var eight = file.Object(8, "(" + new string('y', 30) + ")");
        var named = eight + 25;

        if (inWhiteSpace)
        {
            named = file.Position;
            file.Raw("      ");
        }

        var four = file.Object(4, Invariant($"<< {type}/Size 13 /W [1 12 0 R 2] /Index [6 1] /Length 7 >>\nstream\n{StreamRows((0, 0, 0))}\nendstream"));
        var table = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}12 1\n{Row(twelve)}trailer\n<< /Size 13 /Root 1 0 R /Prev {four} /XRefStm {named} >>\nstartxref\n{table}\n%%EOF\n"));
        file.Raw("%" + new string('-', 900) + "\n");
        var six = file.Object(6, "(six)");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n6 1\n{Row(six)}trailer\n<< /Size 13 /Root 1 0 R /Prev {four} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, stream, named, four, table, newest);
    }

    /// <summary>
    /// The document <see cref="NamingAnObjectBesideTheStream"/> writes, with object 4 written as a stream that gives no type,
    /// whose <c>/W</c> gives its second width as the reference <c>12 0 R</c>, the <c>/Size</c> 10, the <c>/Length</c> 6 and
    /// <paramref name="filter"/>, nothing when empty, and whose <c>stream</c> keyword ends <paramref name="keywordEnd"/> bytes
    /// past its header, a <c>/Pad</c> string making up the length, followed by <paramref name="after"/> and 6 bytes of data;
    /// the newest table also indexes object 12, <c>4</c> after a comment of 3,000 dashes, which
    /// <see cref="Documents.PdfReaderLimits.MaxObjectLength"/> lowered below that cuts before its value. Object 4 lies at
    /// <c>Four</c>, the place the <c>/XRefStm</c> names at <c>Named</c>.
    /// </summary>
    public static (byte[] File, long Four, long Named) NamingAStreamWhoseKeywordEndsAt(int keywordEnd, string after, string filter = "")
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, page) = Page(file);
        var oldSeven = file.Object(7, "(old seven)");
        var five = file.Object(5, "(five)");
        var newSeven = file.Object(7, "(new seven)");
        file.Raw("%" + new string('-', 1200) + "\n");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(page)}7 1\n{Row(oldSeven)}trailer\n<< /Size 8 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        var stream = file.Position;
        var rows = StreamRows((1, five, 0), (1, newSeven, 0));
        file.Object(9, Invariant($"<< /Type /XRef /Size 13 /W [1 4 2] /Index [5 1 7 1] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        file.Raw(new string(' ', (int)(stream + 400 - file.Position)));
        var eight = file.Object(8, "(" + new string('x', 20) + ")");
        var head = "4 0 obj\n<< /W [1 12 0 R 2] /Size 10 /Length 6 " + filter + "/Pad (";
        const string Tail = ") >>\nstream";
        var four = file.Position;
        file.Raw(head + new string('-', keywordEnd - head.Length - Tail.Length) + Tail + after + new string('\u0001', 6) + "\nendstream\nendobj\n");
        file.Raw("%" + new string('-', 1200) + "\n");
        var six = file.Object(6, "(six)");
        var twelve = file.Object(12, "%" + new string('-', 3000) + "\n4");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n6 1\n{Row(six)}12 1\n{Row(twelve)}trailer\n<< /Size 13 /Root 1 0 R /Prev {four} /XRefStm {eight + 20} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, four, eight + 20);
    }

    /// <summary>Writes the catalog, the page tree and its page, objects 1 to 3, and gives where each starts.</summary>
    private static (long Catalog, long Pages, long Page) Page(HandWrittenFile file) => (
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>"));

    /// <summary>Writes a classic table's row placing an object in use at <paramref name="offset"/>.</summary>
    private static string Row(long offset) => Invariant($"{offset:D10} 00000 n \n");

    /// <summary>Compresses <paramref name="text"/>, one byte to a character, into Flate data, one byte to a character.</summary>
    private static string Deflate(string text)
    {
        using var output = new MemoryStream();

        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(Encoding.Latin1.GetBytes(text));
        }

        return Encoding.Latin1.GetString(output.ToArray());
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

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
