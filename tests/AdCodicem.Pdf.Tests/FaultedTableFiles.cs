using System.Globalization;
using System.Text;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Files whose chain names, through <c>/Prev</c>, classic tables whose fault lies among their rows, each read on past it to
/// its trailer, whose <c>/Prev</c> the chain goes on through (#324). Offsets are counted from the header, which starts the
/// file.
/// </summary>
internal static class FaultedTableFiles
{
    /// <summary>
    /// A document saved three times, or more. The first revision writes object 4 <c>(old four)</c>, object 5
    /// <c>(old five)</c>, object 7 <c>(old seven)</c> and object 9 <c>(nine)</c>, and a classic table at <c>Oldest</c>,
    /// <c>/Size 10</c>, that indexes them in four subsections. Each of the <paramref name="middles"/> next writes object 4
    /// <c>(four)</c>, object 5 <c>(five)</c> and object 7 <c>(seven)</c>, and a classic table that gives
    /// <paramref name="lead"/> free rows from object 0, then the rows <paramref name="fault"/> writes, the row for object 7
    /// past them, then <paramref name="gap"/> bytes of comment, its trailer <paramref name="size"/>, the <c>/Prev</c>
    /// <paramref name="prev"/> writes, an <c>/ID</c> and <paramref name="trailer"/>; the last of them lies at <c>Middle</c>.
    /// Past <paramref name="tail"/> bytes of comment, the last revision writes the catalog, its page tree and page, and object
    /// 6 <c>(six)</c>, and a classic table at <c>Newest</c>, <c>/Size 10</c>, naming the table before it through
    /// <c>/Prev</c>.
    /// </summary>
    /// <param name="fault">
    /// The rows of each middle table: "sound", rows for objects 4 and 5; "row", a subsection of three rows whose second, for
    /// object 5, ends in <c>x</c>, the third read as a subsection header and an <c>f</c>; "row then count", as "row", but the
    /// third row <c>2147483647 00002 f</c>, read as a header that numbers its rows past the last object number; "header", the
    /// row for object 4, then the header <c>5 x</c> and the row for object 5; "count", the row for object 4, then the header
    /// <c>5 2147483647</c> and the row for object 5; "stray", the rows for objects 4 and 5, then <c>/Bad 1</c>; "header
    /// first", the header <c>4 x</c>, then the rows for objects 4 and 5, so that no row is read before the fault.
    /// </param>
    /// <param name="prev">
    /// What each middle table's <c>/Prev</c> is written as, <c>{previous}</c> and <c>{self}</c> standing for the offset of
    /// the table before it and its own: the table before it when null.
    /// </param>
    /// <param name="trailer">What each middle table's trailer gives besides its <c>/Size</c>, its <c>/Prev</c> and an <c>/ID</c>.</param>
    /// <param name="middles">How many middle revisions there are.</param>
    /// <param name="tail">The bytes of comment between the last middle table and the last revision.</param>
    /// <param name="lead">The free rows each middle table gives before its fault's rows, in a subsection from object 0.</param>
    /// <param name="gap">The bytes of comment between each middle table's rows and its trailer.</param>
    /// <param name="shift">How many bytes past object 9's header the oldest table's row for it places it.</param>
    /// <param name="eight">
    /// What object 8 holds, which the last revision writes and the newest table indexes, <c>{oldest}</c> standing for the
    /// oldest table's offset; none when null.
    /// </param>
    /// <param name="size">The <c>/Size</c> entry of each middle table's trailer, or none.</param>
    /// <param name="oldestRows">What the oldest table gives past its row for object 9, before its trailer.</param>
    public static (byte[] File, long Oldest, long Middle, long Newest) Revisions(
        string fault,
        string? prev = null,
        string trailer = "",
        int middles = 1,
        int tail = 1200,
        int lead = 0,
        int gap = 0,
        int shift = 0,
        string? eight = null,
        string size = "/Size 8",
        string oldestRows = "")
    {
        var file = new HandWrittenFile();
        var oldFour = file.Object(4, "(old four)");
        var oldFive = file.Object(5, "(old five)");
        var oldSeven = file.Object(7, "(old seven)");
        var nine = file.Object(9, "(nine)");
        var oldest = file.Position;
        file.Raw(Invariant(
            $"xref\n0 1\n0000000000 65535 f \n4 2\n{Row(oldFour)}{Row(oldFive)}7 1\n{Row(oldSeven)}9 1\n{Row(nine + shift)}{oldestRows}trailer\n<< /Size 10 >>\nstartxref\n{oldest}\n%%EOF\n"));
        var previous = oldest;
        var free = lead == 0 ? string.Empty : Invariant($"0 {lead}\n") + string.Concat(Enumerable.Repeat("0000000000 00000 f \n", lead));
        var comment = gap == 0 ? string.Empty : "%" + new string('-', gap - 2) + "\n";

        for (var middle = 0; middle < middles; middle++)
        {
            var four = Row(file.Object(4, "(four)"));
            var five = file.Object(5, "(five)");
            var seven = Row(file.Object(7, "(seven)"));
            var at = file.Position;
            var body = fault switch
            {
                "sound" => Invariant($"4 2\n{four}{Row(five)}"),
                "row" => Invariant($"4 3\n{four}{five:D10} 00000 x \n0000000000 00000 f \n"),
                "row then count" => Invariant($"4 3\n{four}{five:D10} 00000 x \n2147483647 00002 f \n"),
                "header" => Invariant($"4 1\n{four}5 x\n{Row(five)}"),
                "count" => Invariant($"4 1\n{four}5 2147483647\n{Row(five)}"),
                "stray" => Invariant($"4 2\n{four}{Row(five)}/Bad 1\n"),
                "header first" => Invariant($"4 x\n{four}{Row(five)}"),
                _ => throw new ArgumentOutOfRangeException(nameof(fault), fault, null),
            };
            var named = (prev ?? "{previous}")
                .Replace("{previous}", previous.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{self}", at.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            file.Raw(Invariant(
                $"xref\n{free}{body}7 1\n{seven}{comment}trailer\n<< {size} /Prev {named} /ID [<0102> <0304>]{trailer} >>\nstartxref\n{at}\n%%EOF\n"));
            previous = at;
        }

        file.Raw("%" + new string('-', tail) + "\n");
        var newest = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>"),
            [6] = file.Object(6, "(six)"),
        };
        var eightAt = eight is null ? -1 : file.Object(8, eight.Replace("{oldest}", oldest.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        var last = file.Position;
        var text = new StringBuilder("xref\n0 4\n0000000000 65535 f \n")
            .Append(Row(newest[1])).Append(Row(newest[2])).Append(Row(newest[3]))
            .Append("6 1\n").Append(Row(newest[6]))
            .Append(eight is null ? string.Empty : "8 1\n" + Row(eightAt))
            .Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size 10 /Root 1 0 R /Prev {previous} >>\nstartxref\n{last}\n%%EOF\n");
        file.Raw(text.ToString());
        return (file.Bytes, oldest, previous, last);
    }

    /// <summary>
    /// A document saved three times whose middle table, at <c>Middle</c>, holds a subsection <c>4 3</c> whose row for object
    /// 4 is sound, then <paramref name="rest"/>. The first revision writes object 4 <c>(old four)</c> and object 5
    /// <c>(old five)</c>, and a classic table at <c>Oldest</c>, <c>/Size 7</c>, that indexes them. The second writes object
    /// 4 <c>(four)</c> and object 5 <c>(five)</c>, and the middle table. The last writes the catalog, its page tree and page,
    /// before the first revision, or past <paramref name="between"/> where <paramref name="objectsBetween"/> says so, and a
    /// classic table at <c>Newest</c>, <c>/Size 7</c>, that indexes them and names the middle table through <c>/Prev</c>,
    /// past <paramref name="between"/>, which follows the middle table at once.
    /// </summary>
    /// <param name="rest">
    /// The rest of the middle table: "head", a row for object 5 that ends in <c>x</c>, then the head of the free list as the
    /// last row, written just before the trailer keyword as Acrobat PDFMaker writes it, and a trailer that gives
    /// <c>/Size 7</c>, <c>/Prev</c> the oldest table and an <c>/ID</c>; "head without f", as "head", the head of the free
    /// list written without its <c>f</c>; "head without f, info first", as "head without f", the trailer's first key an
    /// <c>/Info</c> that refers to object 2147483647, generation 2; "lost", a sound row for object 5 and the head of the
    /// free list, then <c>/Bad 1</c>, and no trailer; "dictionary", a row for object 5 that ends in <c>x</c>, then a line
    /// <c>0000000000 00002</c>, which reads as a subsection header, a dictionary <c>&lt;&lt; /A 1 &gt;&gt;</c>, which its
    /// first row starts with, and <c>/Bad 1</c>, and no trailer.
    /// </param>
    /// <param name="between">What lies between the middle table and the newest table, before the last revision's objects.</param>
    /// <param name="objectsBetween">Whether the last revision's objects lie between the two tables, past <paramref name="between"/>.</param>
    public static (byte[] File, long Oldest, long Middle, long Newest) Pdfmaker(string rest, string between = "", bool objectsBetween = false)
    {
        var file = new HandWrittenFile();
        var newest = new Dictionary<int, long>();

        if (!objectsBetween)
        {
            WriteLastObjects(file, newest);
        }

        var oldFour = file.Object(4, "(old four)");
        var oldFive = file.Object(5, "(old five)");
        var oldest = file.Position;
        file.Raw(Invariant(
            $"xref\n0 1\n0000000000 65535 f \n4 2\n{Row(oldFour)}{Row(oldFive)}trailer\n<< /Size 7 >>\nstartxref\n{oldest}\n%%EOF\n"));
        var four = file.Object(4, "(four)");
        var five = file.Object(5, "(five)");
        var middle = file.Position;
        file.Raw("xref\n4 3\n" + Row(four) + rest switch
        {
            "head" => Invariant($"{five:D10} 00000 x \n0000000000 65535 f \ntrailer\n<< /Size 7 /Prev {oldest} /ID [<0102> <0304>] >>\n"),
            "head without f" => Invariant($"{five:D10} 00000 x \n0000000000 65535\ntrailer\n<< /Size 7 /Prev {oldest} /ID [<0102> <0304>] >>\n"),
            "head without f, info first" => Invariant(
                $"{five:D10} 00000 x \n0000000000 65535\ntrailer\n<< /Info 2147483647 2 R /Size 7 /Prev {oldest} /ID [<0102> <0304>] >>\n"),
            "lost" => Row(five) + "0000000000 65535 f \n/Bad 1\n",
            "dictionary" => Invariant($"{five:D10} 00000 x \n0000000000 00002\n<< /A 1 >>\n/Bad 1\n"),
            _ => throw new ArgumentOutOfRangeException(nameof(rest), rest, null),
        });
        file.Raw(between);

        if (objectsBetween)
        {
            WriteLastObjects(file, newest);
        }

        var last = file.Position;
        file.Raw(Invariant(
            $"xref\n0 4\n0000000000 65535 f \n{Row(newest[1])}{Row(newest[2])}{Row(newest[3])}trailer\n<< /Size 7 /Root 1 0 R /Prev {middle} >>\nstartxref\n{last}\n%%EOF\n"));
        return (file.Bytes, oldest, middle, last);
    }

    private static void WriteLastObjects(HandWrittenFile file, Dictionary<int, long> written)
    {
        written[1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        written[2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        written[3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>");
    }

    private static string Row(long offset) => Invariant($"{offset:D10} 00000 n \n");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
