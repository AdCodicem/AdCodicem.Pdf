using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Files whose chain a guard ends at a section it stops — <see cref="PdfReaderLimits.MaxXRefSectionLength"/> cuts a table's
/// rows, <see cref="PdfReaderLimits.MaxDecodedStreamLength"/> a stream's, <see cref="PdfReaderLimits.MaxIndexEntries"/>
/// fills at either —, which names through <c>/Prev</c> or <c>/XRefStm</c> what is no section whatever the guard hid, or what
/// may be one (#264). Each guard is named as the theories name it (<see cref="Guard"/>). Offsets are counted from the
/// header, which starts the file, but for <see cref="AtTheFirstByte"/>'s, which has none.
/// </summary>
internal static class StoppedSectionFiles
{
    /// <summary>
    /// The guards that stop a section's rows, by the name the theories give them: <c>table rows</c> and <c>stream rows</c>
    /// cut a table's rows or a stream's, <c>table index</c> and <c>stream index</c> fill the index at them.
    /// </summary>
    public static PdfReaderOptions Guard(string guard) => new()
    {
        Limits = guard switch
        {
            "table rows" => PdfReaderLimits.Default with { MaxXRefSectionLength = 128 },
            "stream rows" => PdfReaderLimits.Default with { MaxDecodedStreamLength = 35 },
            _ => PdfReaderLimits.Default with { MaxIndexEntries = 4 },
        },
    };

    /// <summary>The code of the report <see cref="Guard"/>'s guard gives once it stops a section.</summary>
    public static string GuardCode(string guard) => guard switch
    {
        "table rows" => PdfDiagnosticCodes.LimitXRefSectionLength,
        "stream rows" => PdfDiagnosticCodes.LimitDecodedStream,
        _ => PdfDiagnosticCodes.LimitIndexEntries,
    };

    /// <summary>
    /// A file of two revisions, or three with <paramref name="newer"/>. The first, a classic table at <c>{older}</c>, holds
    /// the catalog, the page tree and the page, objects 1 to 3, and gives <c>/Root</c>. The second updates objects 2 and 3,
    /// and numbers its rows <c>0 1</c>, <c>21 1</c> — a free row, or object 21 written as <paramref name="twentyOne"/> —,
    /// <c>2 2</c> and <c>4 40</c> — free rows —, giving <c>/Size 44</c>, no <c>/Root</c>, and <paramref name="links"/>: a
    /// classic table at <c>{self}</c>, or, with <paramref name="stream"/>, cross-reference stream 44, whose ASCIIHex-encoded
    /// rows of 7 bytes are numbered so through its <c>/Index</c>, its own row last. <c>MaxXRefSectionLength = 128</c> cuts
    /// the table's rows in their fifth, <c>MaxDecodedStreamLength = 35</c> the stream's after it, and
    /// <c>MaxIndexEntries = 4</c> fills the index at it, in either. The third, at <c>{newer}</c>, indexes object 0 alone,
    /// names the second through <paramref name="newer"/> and is what <c>startxref</c> names.
    /// </summary>
    /// <param name="links">
    /// The second revision's <c>/Prev</c> and <c>/XRefStm</c>, as written, <c>{self}</c>, <c>{older}</c> and <c>{newer}</c>
    /// standing for those offsets.
    /// </param>
    /// <param name="stream">Whether the second revision is a cross-reference stream rather than a classic table.</param>
    /// <param name="newer">The key through which a third revision names the second, <c>/Prev</c> or <c>/XRefStm</c>; none when null.</param>
    /// <param name="twentyOne">
    /// The body of object 21, which the second revision places, <c>{older}</c> standing for that offset; a free row when null.
    /// </param>
    /// <param name="lost">
    /// The <c>/Index</c> of a cross-reference stream of <c>/Type /XRef</c> whose <c>/W</c> gives a negative width, object 45,
    /// written after the second revision and naming it through <c>/Prev</c>, which the third revision then names through
    /// <c>/Prev</c>, whatever <paramref name="newer"/> says: its rows lost, the stream refuses the numbers it declares to the
    /// second revision's rows (#208). None when null.
    /// </param>
    public static (byte[] File, long Section) Stopped(
        string links, bool stream = false, string? newer = null, string? twentyOne = null, string? lost = null)
    {
        var file = new HandWrittenFile();
        var (older, two, three) = Older(file);
        var placed = twentyOne is null ? 0 : file.Object(21, Fill(twentyOne, 0, older, 0));
        var section = file.Position;
        var text = Section(links, stream, section, two, three, placed);

        // The newer revision's offset is written in ten digits, so that the text's length does not change with it.
        var tail = Invariant($"startxref\n{section}\n%%EOF\n");
        var next = section + Fill(text, section, older, 0).Length + tail.Length;
        file.Raw(Fill(text, section, older, next) + tail);

        if (lost is not null)
        {
            var at = file.Object(45, Invariant($"<< /Type /XRef /Size 46 /Index {lost} /W [1 -4 2] /Prev {section} /Length 0 >>\nstream\n\nendstream"));
            file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 46 /Prev {at} >>\nstartxref\n{file.Position}\n%%EOF\n"));
        }
        else if (newer is not null)
        {
            file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 44 {newer} {section} >>\nstartxref\n{next}\n%%EOF\n"));
        }

        return (file.Bytes, section);
    }

    /// <summary>
    /// A file whose only revision, or older one with <paramref name="updated"/>, writes the catalog, an empty page tree and
    /// object 3, and a classic table at <c>Table</c> whose <c>xref</c> keyword 300 spaces follow, so that
    /// <c>MaxXRefSectionLength = 150</c> cuts it before its first row, then the rows of those objects and a trailer that gives
    /// <c>/Root</c> and <paramref name="prev"/>. With <paramref name="updated"/>, an update's table of the one subsection
    /// <c>0 0</c>, which indexes nothing, names it through <c>/Prev</c>, and is what <c>startxref</c> names.
    /// </summary>
    /// <param name="prev">The table's <c>/Prev</c>, as written, <c>{self}</c> standing for its offset.</param>
    /// <param name="updated">Whether an update names the table, rather than <c>startxref</c>.</param>
    public static (byte[] File, long Table) CutBeforeItsRows(string prev, bool updated = false)
    {
        var file = new HandWrittenFile();
        var one = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var two = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var three = file.Object(3, "(three)");
        var table = file.Position;
        var link = prev.Replace("{self}", table.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        file.Raw("xref" + new string(' ', 300) + Invariant($"\n0 4\n0000000000 65535 f \n{one:D10} 00000 n \n{two:D10} 00000 n \n{three:D10} 00000 n \ntrailer\n<< /Size 4 /Root 1 0 R /Prev {link} >>\nstartxref\n{table}\n%%EOF\n"));

        if (updated)
        {
            var update = file.Position;
            file.Raw(Invariant($"xref\n0 0\ntrailer\n<< /Size 4 /Root 1 0 R /Prev {table} >>\nstartxref\n{update}\n%%EOF\n"));
        }

        return (file.Bytes, table);
    }

    /// <summary>
    /// A file with no header, whose first byte starts the section <see cref="Stopped"/> writes second, giving
    /// <paramref name="links"/>, which <c>startxref</c> names: offset 0 names it. Objects 1 to 3 follow, then a classic
    /// table, at <c>{older}</c>, that indexes them and gives <c>/Root</c>.
    /// </summary>
    /// <param name="links">The section's <c>/Prev</c> and <c>/XRefStm</c>, as written, <c>{older}</c> standing for that offset.</param>
    /// <param name="stream">Whether the section is a cross-reference stream rather than a classic table.</param>
    public static byte[] AtTheFirstByte(string links, bool stream = false)
    {
        // The section places objects that follow it, and names the table after them: written first with every offset in ten
        // digits, its length does not change once they are known.
        var length = Fill(Section(links, stream, 0, 0, 0, 0), 0, 0, 0).Length;
        var objects = new StringBuilder();
        var one = length + Append(objects, 1, "<< /Type /Catalog /Pages 2 0 R >>");
        var two = length + Append(objects, 2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var three = length + Append(objects, 3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>");
        var older = length + objects.Length;
        objects.Append(Invariant($"xref\n0 4\n0000000000 65535 f \n{one:D10} 00000 n \n{two:D10} 00000 n \n{three:D10} 00000 n \ntrailer\n<< /Size 4 /Root 1 0 R >>\n"));
        var text = Fill(Section(links, stream, 0, two, three, 0), 0, older, 0) + objects + "startxref\n0\n%%EOF\n";
        return Encoding.Latin1.GetBytes(text);

        static long Append(StringBuilder objects, int number, string body)
        {
            var at = objects.Length;
            objects.Append(Invariant($"{number} 0 obj\n{body}\nendobj\n"));
            return at;
        }
    }

    /// <summary>
    /// A file of two revisions, the first as <see cref="Stopped"/> writes it, the second a hybrid section: cross-reference
    /// stream 44, at <c>{stream}</c>, numbered as <see cref="Stopped"/> numbers it, with no <c>/Prev</c> of its own, then a
    /// classic table, at <c>{self}</c>, of object 0 alone, whose trailer gives no <c>/Root</c>, names the stream through
    /// <c>/XRefStm</c>, <paramref name="shift"/> bytes off, and gives <paramref name="prev"/>. A guard that stops the stream
    /// ends the chain at it, the table read whole.
    /// </summary>
    /// <param name="prev">
    /// The table's <c>/Prev</c>, as written, <c>{self}</c>, <c>{stream}</c> and <c>{older}</c> standing for those offsets.
    /// </param>
    /// <param name="shift">
    /// How far past the stream's header the <c>/XRefStm</c> names it, a place before the header being in the end of the
    /// first revision's <c>%%EOF</c>, where the stream is found moved.
    /// </param>
    public static (byte[] File, long Table) HybridWhoseStreamIsStopped(string prev, int shift = 0)
    {
        var file = new HandWrittenFile();
        var (older, two, three) = Older(file);
        var stream = file.Position;
        file.Raw(Section(string.Empty, stream: true, stream, two, three, 0));
        var table = file.Position;
        var link = prev.Replace("{stream}", stream.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 45 /XRefStm {stream + shift} /Prev {Fill(link, table, older, 0)} >>\nstartxref\n{table}\n%%EOF\n"));
        return (file.Bytes, table);
    }

    /// <summary>
    /// A file of three revisions: the first as <see cref="Stopped"/> writes it; the second cross-reference stream 44, at
    /// <c>{self}</c>, numbered as <see cref="Stopped"/> numbers it and giving <paramref name="prev"/>; the third a classic
    /// table of object 0 alone, which gives <c>/Root 1 0 R</c> and names the stream through <c>/Prev</c> 4 bytes before its
    /// header, in the end of the first revision's <c>%%EOF</c>: the stream is found moved.
    /// </summary>
    /// <param name="prev">
    /// The stream's <c>/Prev</c>, as written, <c>{self}</c> — where the stream is, not where it is named — and
    /// <c>{older}</c> standing for those offsets.
    /// </param>
    public static byte[] StreamFoundMoved(string prev)
    {
        var file = new HandWrittenFile();
        var (older, two, three) = Older(file);
        var stream = file.Position;
        file.Raw(Fill(Section("/Prev " + prev, stream: true, stream, two, three, 0), stream, older, 0));
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 45 /Prev {stream - 4} /Root 1 0 R >>\nstartxref\n{newest}\n%%EOF\n"));
        return file.Bytes;
    }

    /// <summary>
    /// Writes the first revision: the catalog, the page tree and the page, objects 1 to 3, a classic table that indexes them
    /// and gives <c>/Root</c>; then objects 2 and 3 again, which the second revision indexes.
    /// </summary>
    private static (long Older, long Two, long Three) Older(HandWrittenFile file)
    {
        var one = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var two = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var three = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>");
        var older = file.Table(4, new Dictionary<int, long> { [1] = one, [2] = two, [3] = three }, "/Size 4 /Root 1 0 R");
        two = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        three = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 2 2] /Resources << >> >>");
        return (older, two, three);
    }

    /// <summary>
    /// Writes the section a guard stops, at <paramref name="at"/>, placing objects 2 and 3 and object 21 where it places one
    /// — at <paramref name="twentyOne"/>, a free row when 0 —, and giving <paramref name="links"/>, its placeholders left.
    /// </summary>
    private static string Section(string links, bool stream, long at, long two, long three, long twentyOne)
    {
        if (stream)
        {
            var data = Row(0, 0, 65535) + Row(twentyOne > 0 ? 1 : 0, twentyOne, 0) + Row(1, two, 0) + Row(1, three, 0) +
                string.Concat(Enumerable.Repeat(Row(0, 0, 0), 40)) + Row(1, at, 0) + ">";
            return Invariant($"44 0 obj\n<< /Type /XRef /Size 45 /Index [0 1 21 1 2 2 4 40 44 1] /W [1 4 2] /Filter /ASCIIHexDecode {links} /Length {data.Length} >>\nstream\n{data}\nendstream\nendobj\n");
        }

        var free = string.Concat(Enumerable.Repeat("0000000000 00000 f \n", 40));
        var row = twentyOne > 0 ? Invariant($"{twentyOne:D10} 00000 n \n") : "0000000000 00000 f \n";
        return Invariant($"xref\n0 1\n0000000000 65535 f \n21 1\n{row}2 2\n{two:D10} 00000 n \n{three:D10} 00000 n \n4 40\n{free}trailer\n<< /Size 44 {links} >>\n");
    }

    /// <summary>Puts the offsets <paramref name="text"/> names by placeholder in their place, the newer one in ten digits.</summary>
    private static string Fill(string text, long self, long older, long newer) => text
        .Replace("{self}", self.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{older}", older.ToString("D10", CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{newer}", newer.ToString("D10", CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>Writes a cross-reference stream's row in fields of 1, 4 and 2 bytes, as ASCIIHex digits.</summary>
    private static string Row(int type, long offset, int generation) => Invariant($"{type:X2}{offset:X8}{generation:X4}");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
