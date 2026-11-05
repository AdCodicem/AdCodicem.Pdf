using System.Globalization;
using System.Text;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Files whose index places an object, or whose chain names a section, some way from where it lies, for the search near the
/// offset named, which reaches 512 bytes either side of it wherever the offset falls (#197); and files whose chain names a
/// section near others the search must not take for it — one the chain has read, or any near a section that is there and
/// cannot be read (#188, #281). What the test is about is written where a comment pads the bytes before it to; offsets are
/// counted from the header, which starts the file unless the file is said to start with other bytes.
/// </summary>
internal static class NearbyFiles
{
    /// <summary>
    /// A one-page document indexed by one classic table, whose row of the page, object 3, gives <paramref name="row"/>
    /// while the page's header starts at <paramref name="at"/>.
    /// </summary>
    public static byte[] PageNamedAt(long row, long at)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
        };
        PadTo(file, at);
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>");
        rows[3] = row;
        file.Table(4, rows, "/Size 4 /Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// An empty document whose classic table's row of object 4 gives <paramref name="row"/>. Object
    /// <paramref name="number"/>, <c>(near)</c>, starts at <paramref name="at"/>, and a comment of 1,200 bytes follows it.
    /// Unless it is object 4 itself, its own row places it there, and object 4, <c>(four)</c>, follows the comment, out of
    /// reach of the search near the row.
    /// </summary>
    public static byte[] HeaderNear(int number, long at, long row)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        PadTo(file, at);
        rows[number] = file.Object(number, "(near)");
        file.Raw("%" + new string('q', 1200) + "\n");

        if (number != 4)
        {
            file.Object(4, "(four)");
        }

        rows[4] = row;
        file.Table(Math.Max(number, 4) + 1, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {Math.Max(number, 4) + 1} /Root 1 0 R"));
        return file.Bytes;
    }

    /// <summary>
    /// A one-page document indexed by one classic table, whose object 4, <c>(four)</c>, follows the page after
    /// <paramref name="before"/>, its header written <paramref name="header"/>: object 4's row gives where that header starts,
    /// <paramref name="shift"/> bytes on. Where <paramref name="startxrefLost"/>, startxref gives offset 9, where the catalog's
    /// header starts, and the index is rebuilt as the document opens.
    /// </summary>
    public static byte[] FourWrittenAs(string header, long shift = 0, bool startxrefLost = false, string before = "")
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>"),
        };
        file.Raw(before);
        rows[4] = file.Position + shift;
        file.Raw(header + "\n(four)\nendobj\n");
        var table = file.Table(5, rows, "/Size 5 /Root 1 0 R");

        if (!startxrefLost)
        {
            return file.Bytes;
        }

        var text = Encoding.Latin1.GetString(file.Bytes);
        return Encoding.Latin1.GetBytes(
            text.Replace(string.Create(CultureInfo.InvariantCulture, $"startxref\n{table}\n"), "startxref\n9\n", StringComparison.Ordinal));
    }

    /// <summary>
    /// A file that starts with <paramref name="junk"/>, before its header, then holds the catalog, an empty page tree and, past
    /// 1,200 bytes of comment, object 4, <c>(four)</c>, whose row gives the offset of the file's byte at
    /// <paramref name="position"/>, counted from the file's first byte.
    /// </summary>
    public static byte[] FourNamedPastJunk(string junk, long position)
    {
        var file = new HandWrittenFile(prefix: junk);
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        file.Raw("%" + new string('q', 1200) + "\n");
        file.Object(4, "(four)");
        rows[4] = position - junk.Length;
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// A file of two revisions, after <paramref name="junk"/> when one is given, before its header. The first holds the
    /// catalog, the page tree, the page, object 4, <c>(original)</c>, and object 5, <c>(five)</c>, indexed by a classic table
    /// that starts at <paramref name="section"/> — or, when <paramref name="stream"/> is given, by the cross-reference
    /// stream of that number. The second, past 1,200 bytes of comment, redefines the catalog and object 4,
    /// <c>(updated)</c>, in a classic table whose <c>/Prev</c> gives <paramref name="prev"/>: only the first indexes the page
    /// tree, the page and object 5. Offsets are counted from the header, as the file's own are. The stream's header is
    /// written <paramref name="streamHeader"/> where one is given.
    /// </summary>
    public static byte[] UpdateNamingItsOriginal(long section, long prev, int? stream = null, string junk = "", string? streamHeader = null)
    {
        var file = new HandWrittenFile(prefix: junk, version: stream is null ? "1.7" : "1.5");
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>"),
            [4] = file.Object(4, "(original)"),
            [5] = file.Object(5, "(five)"),
        };
        PadTo(file, section);

        if (stream is { } number)
        {
            var streamRows = new Dictionary<int, (int Type, long Field2, int Field3)>();

            foreach (var (row, offset) in rows)
            {
                streamRows[row] = (1, offset, 0);
            }

            file.CrossReferenceStream(number, number + 1, streamRows, "/Root 1 0 R", header: streamHeader);
        }
        else
        {
            file.Table(6, rows, "/Size 6 /Root 1 0 R");
        }

        file.Raw("%" + new string('u', 1200) + "\n");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var four = file.Object(4, "(updated)");
        var update = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n1 1\n{catalog:D10} 00000 n \n4 1\n{four:D10} 00000 n \ntrailer\n<< /Size {Math.Max(stream ?? 5, 5) + 1} /Root 1 0 R /Prev {prev} >>\nstartxref\n{update}\n%%EOF\n"));
        return file.Bytes;
    }

    /// <summary>
    /// An empty document saved twice. The first table, at <c>Malformed</c>, holds the catalog's and the page tree's rows,
    /// then a stray name, <c>/Bad 1</c>, where its trailer should start. The update's table, past <paramref name="gap"/>
    /// bytes of comment, indexes nothing and names the first through <c>/Prev</c>: with no gap it lies within 512 bytes of
    /// it. <paramref name="junk"/> comes before the header; the positions given are in the file, the junk counted.
    /// </summary>
    public static (byte[] File, long Malformed, long Update) MalformedTableUnderAnUpdate(int gap, string junk = "")
    {
        var file = new HandWrittenFile(prefix: junk);
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var malformed = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 3\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \n/Bad 1\ntrailer\n<< /Size 3 /Root 1 0 R >>\n"));
        Gap(file, gap);
        var update = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 3 /Root 1 0 R /Prev {malformed} >>\nstartxref\n{update}\n%%EOF\n"));
        return (file.Bytes, malformed + junk.Length, update + junk.Length);
    }

    /// <summary>
    /// A one-page document whose classic table, past object 4, <c>&lt;&lt; /Type /Foo &gt;&gt;</c>, and
    /// <paramref name="gap"/> bytes of comment, indexes the catalog and the page tree and names object 4 through
    /// <c>/XRefStm</c>: no cross-reference stream is there, and the page, object 3, is indexed by nothing. With no gap the
    /// table lies within 512 bytes of object 4. <paramref name="junk"/> comes before the header; the positions given are in
    /// the file, the junk counted.
    /// </summary>
    public static (byte[] File, long Four, long Table) HybridNamingAnotherObject(int gap, string junk = "")
    {
        var file = new HandWrittenFile(prefix: junk);
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
        };
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>");
        var four = file.Object(4, "<< /Type /Foo >>");
        Gap(file, gap);
        var table = file.Table(3, rows, string.Create(CultureInfo.InvariantCulture, $"/Size 5 /Root 1 0 R /XRefStm {four}"));
        return (file.Bytes, four + junk.Length, table + junk.Length);
    }

    /// <summary>
    /// A one-page document whose one table, past <paramref name="gap"/> bytes of comment, indexes every object and names
    /// through <c>/Prev</c> a place inside the page tree's dictionary, where no section is. With no gap the table lies within
    /// 512 bytes of that place.
    /// </summary>
    public static (byte[] File, long Named, long Table) TableNamingNoSection(int gap)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>"),
        };
        var named = rows[2] + "2 0 obj\n<< /Type ".Length;
        Gap(file, gap);
        var table = file.Table(4, rows, string.Create(CultureInfo.InvariantCulture, $"/Size 4 /Root 1 0 R /Prev {named}"));
        return (file.Bytes, named, table);
    }

    /// <summary>
    /// A one-page document saved twice. The first table indexes the catalog, the page tree and the page. The update's,
    /// which indexes object 4, names through <c>/Prev</c> the place <paramref name="before"/> bytes past the first table's
    /// keyword, and starts <paramref name="after"/> bytes past that place, object 4 filling what lies between.
    /// </summary>
    public static (byte[] File, long Original, long Named, long Update) UpdateNamingBetweenItsTables(int before, int after)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>"),
        };
        var original = file.Table(4, rows, "/Size 4 /Root 1 0 R");
        var named = original + before;
        var filler = named + after - file.Position - "4 0 obj\n()\nendobj\n".Length;
        filler.Should().BeGreaterThanOrEqualTo(0, "object 4 fills the bytes up to the update's table");
        var four = file.Object(4, "(" + new string('u', (int)filler) + ")");
        var update = file.Position;
        update.Should().Be(named + after);
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n4 1\n{four:D10} 00000 n \ntrailer\n<< /Size 5 /Root 1 0 R /Prev {named} >>\nstartxref\n{update}\n%%EOF\n"));
        return (file.Bytes, original, named, update);
    }

    /// <summary>
    /// A one-page document saved three times. The first section, a classic table or, with <paramref name="streams"/>,
    /// cross-reference stream 5, indexes the catalog, the page tree and the page. The second, past object 4,
    /// <c>(four)</c>, and <paramref name="gap"/> bytes of comment, alone indexes object 4 and names the first through
    /// <c>/Prev</c>, and cannot be read: a classic table holding a stray name, <c>/Bad 1</c>, before its first subsection,
    /// or cross-reference stream 6, whose <c>/W</c> gives a name for a width, and which gives <c>/Type /XRef</c> unless
    /// <paramref name="untyped"/>. With no gap it lies within 512 bytes of the first. The third, a classic table past
    /// <paramref name="after"/> bytes of comment, indexes nothing and names the second through <c>/Prev</c>,
    /// <paramref name="shortBy"/> bytes short of it: 7 bytes short lies in the <c>endobj</c> of object 4, or in the comment
    /// before the second, where no section starts. With no comment after it, the third lies within 512 bytes of the second.
    /// </summary>
    public static (byte[] File, long First, long Second) MalformedUpdateNearItsOriginal(
        bool streams, int gap, int shortBy = 0, int after = 1200, bool untyped = false)
    {
        var file = new HandWrittenFile(version: streams ? "1.5" : "1.7");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>");
        var first = file.Position;

        if (streams)
        {
            var rows = StreamRows((0, 0, 65535), (1, catalog, 0), (1, pages, 0), (1, page, 0), (1, first, 0));
            file.Object(5, string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Type /XRef /Size 6 /Index [0 4 5 1] /W [1 4 2] /Root 1 0 R /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        }
        else
        {
            file.Raw(string.Create(
                CultureInfo.InvariantCulture,
                $"xref\n0 4\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \n{page:D10} 00000 n \ntrailer\n<< /Size 4 /Root 1 0 R >>\n"));
        }

        var four = file.Object(4, "(four)");
        Gap(file, gap);
        var second = file.Position;

        if (streams)
        {
            var rows = StreamRows((1, four, 0), (1, second, 0));
            file.Object(6, string.Create(
                CultureInfo.InvariantCulture,
                $"<< {(untyped ? string.Empty : "/Type /XRef ")}/Size 7 /Index [4 1 6 1] /W [1 4 /] /Root 1 0 R /Prev {first} /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        }
        else
        {
            file.Raw(string.Create(
                CultureInfo.InvariantCulture,
                $"xref\n/Bad 1\n4 1\n{four:D10} 00000 n \ntrailer\n<< /Size 5 /Root 1 0 R /Prev {first} >>\n"));
        }

        Gap(file, after);
        var third = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 7 /Root 1 0 R /Prev {second - shortBy} >>\nstartxref\n{third}\n%%EOF\n"));
        return (file.Bytes, first, second);
    }

    /// <summary>
    /// A document saved twice. The first revision writes the catalog, an empty page tree, object 3, <c>(three)</c>, object 9,
    /// <c>(nine)</c>, and, at <c>Section</c>, what <paramref name="kind"/> names, which indexes object 3 and cannot be read
    /// whole: <c>table read in part</c>, a classic table of objects 0 to 3 whose row for object 3 is no row;
    /// <c>table holding no subsection</c>, one whose trailer keyword follows its xref keyword; <c>table that cannot be
    /// read</c>, one holding a stray name, <c>/Bad 1</c>, before its first subsection; <c>stream read in part</c>,
    /// cross-reference stream 5 of <c>/Index [0 5]</c> that holds the rows of objects 0 to 3 alone, the last of the
    /// reserved type 7; or <c>stream that cannot be read</c>, stream 5 of <c>/Type /XRef</c> whose <c>/W</c> gives a
    /// negative width. The update, past 1,200 bytes of comment, indexes the catalog and the page tree, and names the section
    /// through <paramref name="link"/>, <c>/Prev</c> or <c>/XRefStm</c>, 7 bytes short of it, in the <c>endobj</c> of
    /// object 9, where no section starts: the search near that offset finds the section, the nearest place to it.
    /// </summary>
    public static (byte[] File, long Section, long Update) SectionNamedShort(string kind, string link)
    {
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var three = file.Object(3, "(three)");
        file.Object(9, "(nine)");
        var section = file.Position;

        if (kind.StartsWith("table", StringComparison.Ordinal))
        {
            var rows = string.Create(CultureInfo.InvariantCulture, $"0 4\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \n");
            file.Raw(kind switch
            {
                "table read in part" => string.Create(CultureInfo.InvariantCulture, $"xref\n{rows}{three:D10} 00000 x \ntrailer\n<< /Size 10 >>\n"),
                "table holding no subsection" => "xref\ntrailer\n<< /Size 10 >>\n",
                _ => string.Create(CultureInfo.InvariantCulture, $"xref\n/Bad 1\n{rows}{three:D10} 00000 n \ntrailer\n<< /Size 10 >>\n"),
            });
        }
        else
        {
            var unreadable = kind == "stream that cannot be read";
            var rows = unreadable
                ? StreamRows((1, three, 0))
                : StreamRows((0, 0, 65535), (1, catalog, 0), (1, pages, 0), (7, three, 0));
            file.Object(5, string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Type /XRef /Size 10 {(unreadable ? "/Index [3 1] /W [1 -4 2]" : "/Index [0 5] /W [1 4 2]")} /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        }

        Gap(file, 1200);
        var update = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 3\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \ntrailer\n<< /Size 10 /Root 1 0 R {link} {section - 7} >>\nstartxref\n{update}\n%%EOF\n"));
        return (file.Bytes, section, update);
    }

    /// <summary>
    /// A one-page document saved twice. The first table, at <c>Original</c>, indexes the catalog, the page tree and the
    /// page, and names through <c>/Prev</c> the offset <paramref name="originalPrev"/> bytes from its own <c>xref</c>
    /// keyword — none when it is null —. The update's table, past 1,200 bytes of comment, indexes nothing and names through
    /// <c>/Prev</c> the offset <paramref name="updatePrev"/> bytes from that keyword: 40 bytes in lies among its first rows,
    /// where no section starts, and one byte before it is the end of line the keyword follows. <paramref name="junk"/> comes
    /// before the header; the positions given are in the file, the junk counted.
    /// </summary>
    public static (byte[] File, long Original, long Update) UpdateNamingAroundItsOriginal(long updatePrev, long? originalPrev, string junk = "")
    {
        var file = new HandWrittenFile(prefix: junk);
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>");
        var original = file.Position;
        var prev = originalPrev is { } shift ? string.Create(CultureInfo.InvariantCulture, $" /Prev {original + shift}") : string.Empty;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 4\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \n{page:D10} 00000 n \ntrailer\n<< /Size 4 /Root 1 0 R{prev} >>\n"));
        Gap(file, 1200);
        var update = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 4 /Root 1 0 R /Prev {original + updatePrev} >>\nstartxref\n{update}\n%%EOF\n"));
        return (file.Bytes, original + junk.Length, update + junk.Length);
    }

    /// <summary>
    /// A one-page document whose one table, past object 6, written <paramref name="six"/>, and 1,200 bytes of comment,
    /// indexes the catalog, the page tree and the page and names object 6 through <c>/Prev</c>: no section is near it.
    /// </summary>
    public static (byte[] File, long Six) TableNamingAnObject(string six)
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>"),
        };
        var at = file.Object(6, six);
        Gap(file, 1200);
        file.Table(4, rows, string.Create(CultureInfo.InvariantCulture, $"/Size 7 /Root 1 0 R /Prev {at}"));
        return (file.Bytes, at);
    }

    /// <summary>
    /// An empty document saved three times. The oldest table, at <c>Oldest</c>, indexes the catalog, the page tree, object
    /// 3, <c>(three)</c>, and object 4, <c>(old)</c>, then holds a stray name, <c>/Bad 1</c>, where its trailer should
    /// start. The next, at <c>Next</c>, indexes object 4 again, <c>(new)</c>, and names the oldest through <c>/Prev</c>.
    /// The newest, past 1,200 bytes of comment, indexes nothing and names through <c>/Prev</c> the place 30 bytes into the
    /// oldest table's rows, at <c>Named</c>, where no section starts: the oldest table is the nearest place to it, the next
    /// one farther on.
    /// </summary>
    public static (byte[] File, long Oldest, long Next, long Named) UnreadableTableNearerThanTheNextOne()
    {
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var three = file.Object(3, "(three)");
        var old = file.Object(4, "(old)");
        var oldest = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 5\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \n{three:D10} 00000 n \n{old:D10} 00000 n \n/Bad 1\ntrailer\n<< /Size 5 /Root 1 0 R >>\n"));
        var four = file.Object(4, "(new)");
        var next = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n4 1\n{four:D10} 00000 n \ntrailer\n<< /Size 5 /Root 1 0 R /Prev {oldest} >>\n"));
        Gap(file, 1200);
        var newest = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 5 /Root 1 0 R /Prev {oldest + 30} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, oldest, next, oldest + 30);
    }

    /// <summary>
    /// A one-page document whose <c>startxref</c> names object 6, written <paramref name="six"/>, which follows the page and
    /// is the last object; no section is in the file.
    /// </summary>
    public static (byte[] File, long Six) StartxrefNamingAnObject(string six)
    {
        var file = new HandWrittenFile(version: "1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>");
        var at = file.Object(6, six);
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"startxref\n{at}\n%%EOF\n"));
        return (file.Bytes, at);
    }

    /// <summary>
    /// A one-page document saved twice. The first table indexes the catalog, the page tree and the page. The update writes
    /// object 5, <c>(only in the stream)</c>, past 600 bytes of comment, and cross-reference stream 6, which indexes it; its
    /// classic table, past 1,200 bytes of comment more, indexes nothing, names stream 6 through <c>/XRefStm</c> and,
    /// through <c>/Prev</c>, a place 30 bytes into the stream's dictionary, more than 512 bytes from the first table.
    /// </summary>
    public static (byte[] File, long Stream, long Named) HybridNamingInsideItsStream()
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>"),
        };
        file.Table(4, rows, "/Size 4 /Root 1 0 R");
        Gap(file, 600);
        var five = file.Object(5, "(only in the stream)");
        var stream = file.Position;
        var data = StreamRows((1, five, 0), (1, stream, 0));
        file.Object(6, string.Create(
            CultureInfo.InvariantCulture,
            $"<< /Type /XRef /Size 7 /Index [5 2] /W [1 4 2] /Length {data.Length} >>\nstream\n{data}\nendstream"));
        Gap(file, 1200);
        var table = file.Position;
        var named = stream + 30;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 7 /Root 1 0 R /XRefStm {stream} /Prev {named} >>\nstartxref\n{table}\n%%EOF\n"));
        return (file.Bytes, stream, named);
    }

    /// <summary>
    /// A document whose two tables name through <c>/XRefStm</c> object 9 at <c>Object</c>, which gives a <c>/W</c> of a
    /// negative width and no <c>/Type</c>: the newest at its header, the older 600 bytes short of it, in the spaces before
    /// it. Cross-reference stream 7, at <c>Stream</c>, 1,000 bytes before the object, indexes object 6, <c>(six)</c>, and
    /// itself; a comment of 1,200 bytes follows the object, then the oldest table, which indexes the catalog and an empty
    /// page tree, and names nothing.
    /// </summary>
    public static (byte[] File, long Stream, long Object) ObjectGivingOnlyAWidthNamedTwice()
    {
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var six = file.Object(6, "(six)");
        var stream = file.Position;
        var rows = StreamRows((1, six, 0), (1, stream, 0));
        file.Object(7, string.Create(
            CultureInfo.InvariantCulture,
            $"<< /Type /XRef /Size 8 /W [1 4 2] /Index [6 2] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        file.Raw(new string(' ', (int)(stream + 1000 - file.Position)));
        var named = file.Position;
        file.Object(9, "<< /W [1 -4 2] /Size 10 /Length 7 >>\nstream\n\u0001\u0001\u0001\u0001\u0001\u0001\u0001\nendstream");
        Gap(file, 1200);
        var oldest = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 3\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \ntrailer\n<< /Size 10 /Root 1 0 R >>\n"));
        var older = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 10 /Root 1 0 R /Prev {oldest} /XRefStm {named - 600} >>\n"));
        var newest = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 10 /Root 1 0 R /Prev {older} /XRefStm {named} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, stream, named);
    }

    /// <summary>
    /// An empty document indexed by one cross-reference stream, object 3 at <c>Stream</c>, after two spaces: <c>startxref</c>
    /// names it at the first of them, and its own <c>/Prev</c> at the second.
    /// </summary>
    public static (byte[] File, long Stream) StreamNamedShortTwice()
    {
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Raw("  ");
        var stream = file.Position;
        var rows = StreamRows((0, 0, 65535), (1, catalog, 0), (1, pages, 0), (1, stream, 0));
        file.Object(3, string.Create(
            CultureInfo.InvariantCulture,
            $"<< /Type /XRef /Size 4 /W [1 4 2] /Root 1 0 R /Prev {stream - 1} /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"startxref\n{stream - 2}\n%%EOF\n"));
        return (file.Bytes, stream);
    }

    /// <summary>
    /// A one-page document saved twice, whose newest table names through <c>/Prev</c> a place 2 bytes into the
    /// <c>endobj</c> that ends object 3's new revision, past which <paramref name="between"/> stands before the update's
    /// table, at <c>Update</c>. The first table, 1,200 bytes before, indexes objects 1 to 3, object 3 as <c>(three old)</c>;
    /// the update's indexes object 4, <c>(four)</c>, and object 3 again, <c>(three new)</c> — or, with
    /// <paramref name="deleted"/>, frees it, writing object 6 where object 3 would be —, and names the first through
    /// <c>/Prev</c>; the newest indexes object 5, <c>(five)</c>.
    /// </summary>
    public static (byte[] File, long Update, long Named) UpdateBehindAStrayWord(string between, bool deleted = false)
    {
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var three = file.Object(3, "(three old)");
        var oldest = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 4\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \n{three:D10} 00000 n \ntrailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        Gap(file, 1200);
        var four = file.Object(4, "(four)");
        var newThree = deleted ? file.Object(6, "(six)") : file.Object(3, "(three new)");
        var named = file.Position - "endobj\n".Length + 2;
        file.Raw(between);
        var update = file.Position;
        var threeRow = deleted ? "0000000000 00001 f " : string.Create(CultureInfo.InvariantCulture, $"{newThree:D10} 00000 n ");
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \n3 2\n{threeRow}\n{four:D10} 00000 n \ntrailer\n<< /Size 5 /Root 1 0 R /Prev {oldest} >>\nstartxref\n{update}\n%%EOF\n"));
        var five = file.Object(5, "(five)");
        var newest = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \n5 1\n{five:D10} 00000 n \ntrailer\n<< /Size 6 /Root 1 0 R /Prev {named} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, update, named);
    }

    /// <summary>
    /// A one-page document saved twice: the first table indexes objects 1 to 3 and object 7, <c>(old seven)</c>; past
    /// 1,200 bytes of comment, object 7's new revision, <c>(new seven)</c>, then <paramref name="between"/>, then
    /// cross-reference stream 9, at <c>Stream</c>, which indexes it; past 1,200 bytes more, the update's classic table
    /// indexes object 6, <c>(six)</c>, and names the first table through <c>/Prev</c> and, through <c>/XRefStm</c>, the
    /// place 2 bytes into the <c>endobj</c> of object 7's new revision, at <c>Named</c>.
    /// </summary>
    public static (byte[] File, long Stream, long Named) HybridBehindAStrayWord(string between)
    {
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var three = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>");
        var seven = file.Object(7, "(old seven)");
        var oldest = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 4\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \n{three:D10} 00000 n \n7 1\n{seven:D10} 00000 n \ntrailer\n<< /Size 8 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        Gap(file, 1200);
        var newSeven = file.Object(7, "(new seven)");
        var named = file.Position - "endobj\n".Length + 2;
        file.Raw(between);
        var stream = file.Position;
        var rows = StreamRows((1, newSeven, 0));
        file.Object(9, string.Create(
            CultureInfo.InvariantCulture,
            $"<< /Type /XRef /Size 10 /W [1 4 2] /Index [7 1] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        Gap(file, 1200);
        var six = file.Object(6, "(six)");
        var update = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \n6 1\n{six:D10} 00000 n \ntrailer\n<< /Size 10 /Root 1 0 R /Prev {oldest} /XRefStm {named} >>\nstartxref\n{update}\n%%EOF\n"));
        return (file.Bytes, stream, named);
    }

    /// <summary>Writes a comment of <paramref name="length"/> bytes, its end of line counted, when it is more than none.</summary>
    private static void Gap(HandWrittenFile file, int length)
    {
        if (length > 0)
        {
            file.Raw("%" + new string('-', length - 2) + "\n");
        }
    }

    /// <summary>Writes cross-reference stream rows of <c>/W [1 4 2]</c>, one byte to a character.</summary>
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

    /// <summary>Writes a comment that brings the next byte written to <paramref name="at"/>.</summary>
    private static void PadTo(HandWrittenFile file, long at)
    {
        var gap = at - file.Position;
        gap.Should().BeGreaterThanOrEqualTo(2, "a comment of one byte and its end of line at least comes before the target");
        file.Raw("%" + new string('p', (int)gap - 2) + "\n");
    }
}
