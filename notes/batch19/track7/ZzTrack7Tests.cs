using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

#pragma warning disable

public class ZzTrack7Tests
{
    private const string Dir = "/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/track7/out/";
    private static string Tree => Environment.GetEnvironmentVariable("ZZTREE") ?? "unknown";

    private static (byte[] File, long Word, long Named, long Update, long Oldest, long Four) Build(string data, string word, long delta)
    {
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var three = file.Object(3, "(three)");
        var six = file.Object(6, "(six, freed by the update)");
        var oldest = file.Table(7, new() { [1] = catalog, [2] = pages, [3] = three, [6] = six }, "/Size 7 /Root 1 0 R");
        file.Raw("%" + new string('=', 1200) + "\n");
        var body = string.Create(CultureInfo.InvariantCulture, $"<< /Type /EmbeddedFile /Length {data.Length} >>\nstream\n{data}\nendstream");
        var four = file.Object(4, body);
        var word0 = four + "4 0 obj\n".Length + body.IndexOf(word, StringComparison.Ordinal);
        var update = file.Position;
        file.Raw(string.Create(CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \n4 1\n{four:D10} 00000 n \n6 1\n0000000000 00001 f \ntrailer\n<< /Size 7 /Root 1 0 R /Prev {oldest} >>\nstartxref\n{update}\n%%EOF\n"));
        var five = file.Object(5, "(five)");
        var newest = file.Position;
        file.Raw(string.Create(CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \n5 1\n{five:D10} 00000 n \ntrailer\n<< /Size 7 /Root 1 0 R /Prev {word0 + delta} >>\nstartxref\n{newest}\n%%EOF\n"));
        return (file.Bytes, word0, word0 + delta, update, oldest, four);
    }

    private static string Obj(PdfDocument d, int n) => d.GetObject(new PdfObjectId(n)) switch { PdfStream => "stream", var o => o.ToString() ?? "?" };

    private static void Row(StringBuilder sb, string name, string data, string word, long delta, int[] order)
    {
        var (bytes, w, named, update, oldest, four) = Build(data, word, delta);
        File.WriteAllBytes(Dir + $"{name}.pdf", bytes);
        using var document = PdfDocument.Open(bytes);
        var repaired = document.WasRepaired;
        sb.AppendLine($"ROW {name}: oldest={oldest} four={four} word={w} named={named} update={update} len={bytes.Length} repairedAtOpen={repaired}");
        foreach (var s in document.Reader.Structure.Sections)
            sb.AppendLine($"  S {s.NamedBy} named={s.NamedOffset} off={s.Offset} {s.State} {s.Kind}");
        var objs = new StringBuilder();
        foreach (var n in order) objs.Append($" {n}={Obj(document, n)}");
        sb.AppendLine("  O" + objs + $" repairedAfter={document.WasRepaired}");
        foreach (var d in document.Diagnostics)
            sb.AppendLine($"  D {d.Severity} {d.Code} @{d.Position}: {d.Message}");
        foreach (var f in new AdCodicem.Pdf.Validation.PdfValidator().Validate(document).Findings)
            sb.AppendLine($"  F {f.Severity} {f.RuleId}: {f.Message}");
    }

    [Fact]
    public void ZzTrack7()
    {
        Directory.CreateDirectory(Dir);
        var sb = new StringBuilder();
        var tail = "\n%" + new string('z', 60) + "\n";
        var table = "%PDF-1.3\n%attached original\nxref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 1 >>\nstartxref\n28\n%%EOF";
        Row(sb, "attached", table + tail, "xref", 2, [6, 4, 3, 5]);
        Row(sb, "attached-text", table.Replace("xref", "text") + tail, "text", 2, [6, 4, 3, 5]);
        var prev = "%PDF-1.3\nxref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 1 /Prev 12 >>\n%%EOF";
        Row(sb, "attached-prev", prev + tail, "xref", 2, [6, 4, 3, 5]);
        Row(sb, "attached-prev-4first", prev + tail, "xref", 2, [4, 6, 3, 5]);
        File.WriteAllText(Dir + $"t7-{Tree}.txt", sb.ToString());
    }
}
