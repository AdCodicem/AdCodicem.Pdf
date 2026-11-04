using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

#pragma warning disable

public class ZzR1cProbe
{
    private static readonly string Out = Environment.GetEnvironmentVariable("ZZ_OUT") ?? "/tmp/zz-r1c.txt";

    private static string Row(long offset) => string.Create(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");

    private static byte[] XRefStmTable(string rows)
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
        var newSix = file.Object(6, "(new six)");
        var newSeven = file.Object(7, "(new seven)");
        var table = file.Position;
        string Hybrid(long cut) => string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 8 /Root 1 0 R /Prev {oldest} /XRefStm {cut:D10} >>\nstartxref\n{table}\n%%EOF\n");
        var cutAt = table + Hybrid(0).Length;
        file.Raw(Hybrid(cutAt));
        file.Raw("xref\n" + rows.Replace("{six}", Row(newSix), StringComparison.Ordinal).Replace("{seven}", Row(newSeven), StringComparison.Ordinal));
        return file.Bytes;
    }

    private static byte[] Q8(int n, string index)
    {
        // Told <- T0 (40 rows; /Prev N 0 R; object N holds Told's offset) <- S (lost; /Index; /Prev T0) <- T1.
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var told = file.Table(3, new Dictionary<int, long> { [1] = catalog, [2] = pages }, "/Size 3 /Root 1 0 R");
        var holder = file.Object(n, told.ToString(CultureInfo.InvariantCulture));
        var t0 = file.Position;
        var rows = new StringBuilder("xref\n0 40\n0000000000 65535 f \n");
        for (var i = 1; i < 40; i++)
        {
            rows.Append(i == n ? Row(holder) : "0000000000 00000 f \n");
        }

        rows.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size 40 /Prev {n} 0 R >>\n");
        file.Raw(rows.ToString());
        var s = file.Position;
        file.Object(50, string.Create(CultureInfo.InvariantCulture, $"<< /Type /XRef /Size 51 /Index {index} /W [1 -4 2] /Prev {t0} /Length 0 >>\nstream\n\nendstream"));
        var t1 = file.Position;
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 51 /Root 1 0 R /Prev {s} >>\nstartxref\n{t1}\n%%EOF\n"));
        return file.Bytes;
    }

    private static byte[] LpiEdge()
    {
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var older = file.Table(3, new Dictionary<int, long> { [1] = catalog, [2] = pages }, "/Size 3 /Root 1 0 R");
        var ten = file.Object(10, older.ToString(CultureInfo.InvariantCulture));
        var table = file.Position;
        var free = string.Concat(Enumerable.Repeat("0000000000 00000 f \n", 20));
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n0 1\n0000000000 65535 f \n10 1\nabcdefghij 00000 n \n11 20\n{free}10 1\n{ten:D10} 00000 n \ntrailer\n<< /Size 31 /Prev 10 0 R >>\nstartxref\n{table}\n%%EOF\n"));
        return file.Bytes;
    }

    private static byte[] Spaced(string prev, string newer)
    {
        // A table with 300 spaces after xref, /Prev given, cut at 150; newer: "" (startxref names it), or the subsections of a newer table.
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var three = file.Object(3, "(three)");
        var t = file.Position;
        file.Raw("xref" + new string(' ', 300) + "\n0 4\n0000000000 65535 f \n" + Row(catalog) + Row(pages) + Row(three) +
            string.Create(CultureInfo.InvariantCulture, $"trailer\n<< /Size 4 /Root 1 0 R /Prev {prev.Replace("{self}", t.ToString(CultureInfo.InvariantCulture))} >>\nstartxref\n{t}\n%%EOF\n"));
        if (newer.Length > 0)
        {
            var n = file.Position;
            file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n{newer}trailer\n<< /Size 4 /Root 1 0 R /Prev {t} >>\nstartxref\n{n}\n%%EOF\n"));
        }

        return file.Bytes;
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var (name, rows) in new[]
        {
            ("A-badrow-stray", "6 2\n{six}junk junk junk\nmore\n"),
            ("A2-badrow-name-trailer", "6 2\n{six}junk junk junk\n/Bad\ntrailer\n<< /Size 8 >>\n"),
            ("A3-good-then-stray", "6 2\n{six}{seven}/Bad\ntrailer\n<< /Size 8 >>\n"),
            ("A4-bad-header", "6 1\n{six}7 x\n{seven}trailer\n<< /Size 8 >>\n"),
            ("B-badrow-trailer", "6 2\n{six}junk junk junk\ntrailer\n<< /Size 8 >>\n"),
            ("C-eof-after-whole", "6 1\n{six}"),
            ("C2-eof-inside", "6 2\n{six}"),
            ("D-sound", "6 2\n{six}{seven}trailer\n<< /Size 8 >>\n"),
            ("E-dict-no-keyword", "6 2\n{six}{seven}<< /Size 8 >>\n"),
            ("E2-badrow-dict", "6 2\n{six}junk junk junk\n<< /Size 8 >>\n"),
            ("F-eof-no-row", "6 1\n"),
            ("G-trai-at-eof", "6 2\n{six}{seven}trai"),
        })
        {
            yield return new object[] { "xrefstm " + name, rows, "" };
        }

        foreach (var tail in new[] { "", "t", "trai", "traile", "x" })
        {
            yield return new object[] { "cutfirst+" + tail, "cutfirst", tail };
            yield return new object[] { "cutupdate+" + tail, "cutupdate", tail };
            yield return new object[] { "cutfirst-frees4+" + tail, "frees4", tail };
        }

        yield return new object[] { "q8-9", "q8", "9|[9 1]" };
        yield return new object[] { "q8-35", "q8", "35|[35 1]" };
        yield return new object[] { "q8-9-every", "q8", "9|/X" };
        yield return new object[] { "q8-control", "q8", "35|[45 1]" };
        yield return new object[] { "lpi-edge", "edge", "" };
        yield return new object[] { "spaced-outside", "spaced", "999999|" };
        yield return new object[] { "spaced-self", "spaced", "{self}|" };
        yield return new object[] { "spaced-outside-under-00", "spaced", "999999|0 0\n" };
        yield return new object[] { "spaced-outside-under-free", "spaced", "999999|0 1\n0000000000 65535 f \n" };
        yield return new object[] { "pasttrai-guard", "pasttrai", "trai" };
        yield return new object[] { "pasttrai-guard-none", "pasttrai", "" };
        yield return new object[] { "pasttrai-guard-t", "pasttrai", "t" };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ZzProbe(string name, string kind, string arg)
    {
        byte[] bytes;
        var optionSets = new List<(string, PdfReaderOptions?)> { ("whole", null) };

        switch (kind)
        {
            case "cutfirst":
                bytes = PdfTemplate.Build(PartialFiles.CutFirst + arg);
                break;
            case "cutupdate":
                bytes = PdfTemplate.Build(PartialFiles.CutUpdate("3 1\n{row:3}\n" + arg));
                break;
            case "frees4":
                bytes = PdfTemplate.Build(PartialFiles.CutFirst.Replace("0 4\n{free}\n{row:1}\n{row:2}\n{row:3}\n", "0 5\n{free}\n{row:1}\n{row:2}\n{row:3}\n0000000000 00001 f \n", StringComparison.Ordinal) + arg);
                break;
            case "q8":
                var parts = arg.Split('|');
                bytes = Q8(int.Parse(parts[0], CultureInfo.InvariantCulture), parts[1]);
                optionSets.Add(("rows300", new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 300 } }));
                optionSets.Add(("index5", new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 5 } }));
                break;
            case "edge":
                bytes = LpiEdge();
                optionSets.Add(("rows150", new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 150 } }));
                break;
            case "spaced":
                var sp = arg.Split('|');
                bytes = Spaced(sp[0], sp[1]);
                optionSets.Add(("rows150", new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 150 } }));
                break;
            case "pasttrai":
                // A table whose rows run to the end of the file, 40 free rows, ending in arg; MaxXRefSectionLength 128 cuts it.
                var free = string.Concat(Enumerable.Repeat("0000000000 00000 f \n", 40));
                bytes = PdfTemplate.Build(PdfTemplate.Sound + "xref\n0 1\n{free}\n4 40\n" + free + arg);
                bytes = PdfTemplate.Build((PdfTemplate.Sound + "xref\n0 1\n{free}\n4 40\n" + free + arg).Replace("startxref\n{xref:1}", "startxref\n{xref:2}", StringComparison.Ordinal));
                optionSets.Add(("rows128", new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 128 } }));
                break;
            default:
                bytes = XRefStmTable(kind);
                break;
        }

        var output = new StringBuilder();
        output.AppendLine("=== " + name + " len=" + bytes.Length);

        foreach (var (label, options) in optionSets)
        {
            using var document = options is null ? PdfDocument.Open(bytes) : PdfDocument.Open(bytes, options);
            output.AppendLine("-- " + label);

            foreach (var d in document.Diagnostics)
            {
                output.AppendLine("  D " + d);
            }

            var index = document.Reader.ChainIndex;
            var sb = new StringBuilder();
            for (var n = 1; n <= 10; n++)
            {
                sb.Append(n).Append('=').Append(index is not null && index.TryGet(n, out var e) ? $"{e.Kind}@{e.Offset}" : "-").Append(' ');
            }

            output.AppendLine("  index " + sb);
            foreach (var section in document.Reader.Structure.Sections)
            {
                output.AppendLine(CultureInfo.InvariantCulture, $"  S {section.NamedBy} @{section.Offset}: {section.State} {section.Kind} inc={section.Incomplete} cut={section.CutByLimit} tf={section.TrailerFault} tp={section.TrailerPosition} fault={section.Fault}");
            }

            output.AppendLine(CultureInfo.InvariantCulture, $"  hidden={document.Reader.Structure.TrailersHiddenByGuard} repaired={document.WasRepaired}");

            foreach (var n in new[] { 3, 4, 5, 6, 7 })
            {
                var value = document.GetObject(new PdfObjectId(n));
                output.AppendLine(CultureInfo.InvariantCulture, $"  {n}={(value is PdfString s ? s.ToText() : value.ToString())} repaired={document.WasRepaired}");
            }

            foreach (var f in new PdfValidator().Validate(document).Findings)
            {
                output.AppendLine("  F " + f);
            }
        }

        lock (typeof(ZzR1cProbe))
        {
            File.AppendAllText(Out, output.ToString());
        }
    }
}
