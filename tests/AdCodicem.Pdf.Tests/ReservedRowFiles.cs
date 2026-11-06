using System.Globalization;
using System.Text;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Files whose cross-reference streams give rows of a reserved type: a type ISO 32000-1 (7.5.8.3) does not define, which
/// it reads as a reference to the null object (#221). Offsets are counted from the header, which starts the file.
/// </summary>
internal static class ReservedRowFiles
{
    /// <summary>
    /// A one-page document saved twice, each revision indexed by a cross-reference stream. The first writes the catalog,
    /// the page tree, object 3 <c>(three)</c> when <paramref name="older"/> says so, the page, object 4, and stream 5 at
    /// <c>Older</c>, whose rows place them — its row for object 3 free when it writes none, or of type
    /// <paramref name="olderType"/> when one is given. The second writes object 3 again, <c>(new three)</c>, when
    /// <paramref name="newer"/> says so, and stream 6 at <c>Newer</c>, which <c>startxref</c> names and whose <c>/Prev</c>
    /// names the first, <paramref name="prevShift"/> bytes past it: its <c>/Index</c> numbers object 3 — unless
    /// <paramref name="type"/> is null —, itself, and objects 7 on in <paramref name="more"/> rows of type 3 and
    /// <paramref name="missing"/> rows its data does not hold, in fields of <paramref name="width"/>, 4 and 2 bytes. Its row
    /// for object 3 is of type <paramref name="type"/>, its second field the offset of <c>(new three)</c> where there is one,
    /// and its type field's first byte 1 where <paramref name="past64Bits"/> says so, in a field wider than 8 bytes.
    /// </summary>
    public static (byte[] File, long Older, long Newer) Updated(
        ulong? type,
        bool older = true,
        bool newer = false,
        int width = 1,
        bool past64Bits = false,
        int more = 0,
        int missing = 0,
        ulong? olderType = null,
        int prevShift = 0)
    {
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [4 0 R] /Count 1 >>");
        var three = older ? file.Object(3, "(three)") : 0;
        var page = file.Object(4, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        var olderAt = file.Position;
        var threeRow = olderType is { } reserved ? (reserved, 0UL, 0UL) : older ? (1UL, (ulong)three, 0UL) : (0UL, 0UL, 0UL);
        var olderRows = Rows(1, (0, 0, 65535), (1, (ulong)catalog, 0), (1, (ulong)pages, 0), threeRow, (1, (ulong)page, 0), (1, (ulong)olderAt, 0));
        file.Object(5, Invariant($"<< /Type /XRef /Size 6 /W [1 4 2] /Root 1 0 R /Length {olderRows.Length} >>\nstream\n{olderRows}\nendstream"));
        file.Raw(Invariant($"startxref\n{olderAt}\n%%EOF\n"));

        var newThree = newer ? file.Object(3, "(new three)") : 0;
        var newerAt = file.Position;
        var rows = new List<(ulong, ulong, ulong)>();
        var index = new StringBuilder();

        if (type is { } given)
        {
            rows.Add((given, (ulong)newThree, 0));
            index.Append("3 1 ");
        }

        rows.Add((1, (ulong)newerAt, 0));
        index.Append("6 1");

        for (var row = 0; row < more; row++)
        {
            rows.Add((3, 0, 0));
        }

        if (more + missing > 0)
        {
            index.Append(CultureInfo.InvariantCulture, $" 7 {more + missing}");
        }

        var data = Rows(width, [.. rows]);

        if (past64Bits && type is not null)
        {
            // The first byte of object 3's type field, the first row's, is not zero: the type takes more than 64 bits.
            data = "\u0001" + data[1..];
        }

        file.Object(6, Invariant(
            $"<< /Type /XRef /Size {7 + more + missing} /Index [{index}] /W [{width} 4 2] /Root 1 0 R /Prev {olderAt + prevShift} /Length {data.Length} >>\nstream\n{data}\nendstream"));
        file.Raw(Invariant($"startxref\n{newerAt}\n%%EOF\n"));
        return (file.Bytes, olderAt, newerAt);
    }

    /// <summary>
    /// A one-page document saved twice, its update a hybrid file's: the first revision writes the catalog, the page tree,
    /// object 3 <c>(three)</c> and the page, object 4, and a classic table that indexes them; the second, cross-reference
    /// stream 5 at <c>Stream</c>, whose row for object 3 is of type 3, and a classic table at <c>Table</c> that indexes no
    /// object, names the first through <c>/Prev</c> and the stream through <c>/XRefStm</c>.
    /// </summary>
    public static (byte[] File, long Stream, long Table) Hybrid()
    {
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [4 0 R] /Count 1 >>");
        var three = file.Object(3, "(three)");
        var page = file.Object(4, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        var oldest = file.Position;
        file.Raw(Invariant(
            $"xref\n0 5\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(three)}{Row(page)}trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{oldest}\n%%EOF\n"));
        var stream = file.Position;
        var rows = Rows(1, (3, 0, 0), (1, (ulong)stream, 0));
        file.Object(5, Invariant($"<< /Type /XRef /Size 6 /Index [3 1 5 1] /W [1 4 2] /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        var table = file.Position;
        file.Raw(Invariant(
            $"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 6 /Root 1 0 R /Prev {oldest} /XRefStm {stream} >>\nstartxref\n{table}\n%%EOF\n"));
        return (file.Bytes, stream, table);
    }

    /// <summary>
    /// A document saved three or four times. The first revision writes the catalog, an empty page tree, objects 3 and 4,
    /// <c>(old three)</c> and <c>(old four)</c>, and a classic table that indexes them. The second writes them again,
    /// <c>(new three)</c> and <c>(new four)</c>, and cross-reference stream 7 at <c>Middle</c>, whose <c>/Prev</c> names the
    /// oldest table and whose rows give objects 3 on, <paramref name="reserved"/> of them, type 7. Where
    /// <paramref name="lost"/> is given, the third writes cross-reference stream 9, of <c>/Index</c> <paramref name="lost"/>,
    /// whose rows cannot be read — its <c>/W</c> gives a field a negative width — and whose <c>/Prev</c> names stream 7: it
    /// refuses to the older sections' rows the numbers its <c>/Index</c> declares, or every number where it is no array
    /// (#208). The last writes a table, which <c>startxref</c> names, that indexes the catalog and the page tree, then gives
    /// <paramref name="newest"/>, <c>{three}</c> standing for the offset of <c>(new three)</c>, and names the section before
    /// it through <c>/Prev</c>. The <c>/Prev</c> that names stream 7 names it <paramref name="shift"/> bytes past its header.
    /// Where <paramref name="refusedRowFirst"/> is given, stream 7 gives object 3 two rows instead, each in a subsection of its
    /// own: one of type 2 naming object stream 0, which no entry holds, and which the stream refuses itself — the first where
    /// <paramref name="refusedRowFirst"/> says so —, and one of type 7.
    /// </summary>
    public static (byte[] File, long Middle) PastARefusal(
        string? lost, int reserved = 1, string newest = "", int shift = 0, bool? refusedRowFirst = null)
    {
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var oldThree = file.Object(3, "(old three)");
        var oldFour = file.Object(4, "(old four)");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 5\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{Row(oldThree)}{Row(oldFour)}trailer\n<< /Size 5 >>\n"));
        var three = file.Object(3, "(new three)");
        file.Object(4, "(new four)");
        var (data, index) = refusedRowFirst switch
        {
            true => (Rows(1, (2, 0, 0), (7, 0, 0)), "3 1 3 1"),
            false => (Rows(1, (7, 0, 0), (2, 0, 0)), "3 1 3 1"),
            null => (Rows(1, [.. Enumerable.Repeat((7UL, 0UL, 0UL), reserved)]), Invariant($"3 {reserved}")),
        };
        var middle = file.Object(7, Invariant(
            $"<< /Type /XRef /Size 8 /Index [{index}] /W [1 4 2] /Prev {oldest} /Length {data.Length} >>\nstream\n{data}\nendstream"));
        var previous = lost is null
            ? middle + shift
            : file.Object(9, Invariant($"<< /Type /XRef /Size 12 /Index {lost} /W [1 -4 2] /Prev {middle + shift} /Length 0 >>\nstream\n\nendstream"));
        var at = file.Position;
        file.Raw(Invariant(
            $"xref\n0 3\n0000000000 65535 f \n{Row(catalog)}{Row(pages)}{newest.Replace("{three}", Invariant($"{three:D10}"), StringComparison.Ordinal)}trailer\n<< /Size 12 /Root 1 0 R /Prev {previous} >>\nstartxref\n{at}\n%%EOF\n"));
        return (file.Bytes, middle);
    }

    /// <summary>
    /// A document indexed by one cross-reference stream, stream 5 at <c>Stream</c>, which <c>startxref</c> names: its row
    /// for object 3 <c>(three)</c> is of type 3, and its row for object 4 <c>(four)</c> places it 900 bytes into the string
    /// of 1,500 letters object 9 holds, which no row indexes — neither where the entry says nor within 512 bytes of it.
    /// </summary>
    public static (byte[] File, long Stream) Astray()
    {
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var three = file.Object(3, "(three)");
        file.Object(4, "(four)");
        var letters = file.Object(9, "(" + new string('x', 1500) + ")");
        var stream = file.Position;
        var rows = Rows(1, (0, 0, 65535), (1, (ulong)catalog, 0), (1, (ulong)pages, 0), (3, (ulong)three, 0), (1, (ulong)letters + 900, 0), (1, (ulong)stream, 0));
        file.Object(5, Invariant($"<< /Type /XRef /Size 6 /W [1 4 2] /Root 1 0 R /Length {rows.Length} >>\nstream\n{rows}\nendstream"));
        file.Raw(Invariant($"startxref\n{stream}\n%%EOF\n"));
        return (file.Bytes, stream);
    }

    /// <summary>
    /// Writes cross-reference stream rows in fields of <paramref name="width"/>, 4 and 2 bytes, one byte to a character: a
    /// type field wider than 8 bytes is written with leading zeros.
    /// </summary>
    private static string Rows(int width, params (ulong Type, ulong Field2, ulong Field3)[] rows)
    {
        var data = new StringBuilder();

        foreach (var (type, field2, field3) in rows)
        {
            for (var shift = (width - 1) * 8; shift >= 0; shift -= 8)
            {
                data.Append((char)(shift >= 64 ? 0 : (byte)(type >> shift)));
            }

            data.Append((char)((field2 >> 24) & 0xFF)).Append((char)((field2 >> 16) & 0xFF))
                .Append((char)((field2 >> 8) & 0xFF)).Append((char)(field2 & 0xFF))
                .Append((char)((field3 >> 8) & 0xFF)).Append((char)(field3 & 0xFF));
        }

        return data.ToString();
    }

    /// <summary>Writes a classic table's row placing an object in use at <paramref name="offset"/>.</summary>
    private static string Row(long offset) => Invariant($"{offset:D10} 00000 n \n");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
