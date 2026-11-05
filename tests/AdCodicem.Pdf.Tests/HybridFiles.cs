using System.Globalization;
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
    /// <paramref name="prevShortBy"/> bytes before object 4, in the spaces before it, and through <c>/XRefStm</c> the place 20
    /// bytes into object 8, inside its string: object 8 is the nearest place to it, then object 4, then the stream. A long
    /// comment keeps the newest table more than 512 bytes from what each link names, whatever object 4's length.
    /// </summary>
    public static (byte[] File, long Oldest, long Four, long Stream, long Newest) NamingAnObjectBesideTheStream(string four, int prevShortBy = 0)
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
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n6 1\n{Row(six)}trailer\n<< /Size 10 /Root 1 0 R /Prev {fourAt - prevShortBy} /XRefStm {eight + 20} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, fourAt, stream, newest);
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
    /// <c>(four)</c>, and itself — and, with <paramref name="reservedRow"/>, object 7 in a row of type 3 — and names through
    /// <c>/Prev</c> the oldest table, at <c>Oldest</c>, 1,200 bytes before it, out of reach of a search near it, which
    /// indexes objects 1 to 3, the page among them as <c>(three as the oldest has it)</c>. The newest table indexes object 5,
    /// <c>(five)</c>.
    /// </summary>
    public static (byte[] File, long Oldest, long Stream, long Newest) PrevFallingShortOfTheStreamBefore(bool reservedRow = false)
    {
        var file = new HandWrittenFile(version: "1.5");
        var (catalog, pages, _) = Page(file);
        var oldThree = file.Object(3, "(three as the oldest has it)");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 4\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(oldThree)}trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        file.Raw("%" + new string('-', 1200) + "\n");
        var four = file.Object(4, "(four)");
        var stream = file.Position;
        var rows = reservedRow ? StreamRows((1, four, 0), (1, stream, 0), (3, 0, 0)) : StreamRows((1, four, 0), (1, stream, 0));
        var index = reservedRow ? "[4 1 6 2]" : "[4 1 6 1]";
        file.Object(6, Invariant($"<< /Type /XRef /Size 8 /W [1 4 2] /Index {index} /Root 1 0 R /Prev {oldest} /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var five = file.Object(5, "(five)");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n5 1\n{Row(five)}trailer\n<< /Size 8 /Root 1 0 R /Prev {stream - 7} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, stream, newest);
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

    /// <summary>Writes the catalog, the page tree and its page, objects 1 to 3, and gives where each starts.</summary>
    private static (long Catalog, long Pages, long Page) Page(HandWrittenFile file) => (
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>"));

    /// <summary>Writes a classic table's row placing an object in use at <paramref name="offset"/>.</summary>
    private static string Row(long offset) => Invariant($"{offset:D10} 00000 n \n");

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
