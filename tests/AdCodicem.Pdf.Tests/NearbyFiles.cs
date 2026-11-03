using System.Globalization;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Files whose index places an object, or whose chain names a section, some way from where it lies, for the search near the
/// offset named, which reaches 512 bytes either side of it wherever the offset falls (#197). What the test is about is
/// written where a comment pads the bytes before it to; offsets are counted from the header, which starts the file unless
/// the file is said to start with other bytes.
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
    /// tree, the page and object 5. Offsets are counted from the header, as the file's own are.
    /// </summary>
    public static byte[] UpdateNamingItsOriginal(long section, long prev, int? stream = null, string junk = "")
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

            file.CrossReferenceStream(number, number + 1, streamRows, "/Root 1 0 R");
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

    /// <summary>Writes a comment that brings the next byte written to <paramref name="at"/>.</summary>
    private static void PadTo(HandWrittenFile file, long at)
    {
        var gap = at - file.Position;
        gap.Should().BeGreaterThanOrEqualTo(2, "a comment of one byte and its end of line at least comes before the target");
        file.Raw("%" + new string('p', (int)gap - 2) + "\n");
    }
}
