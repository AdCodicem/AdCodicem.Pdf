using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

public sealed class ZzTrackProbe2
{
    private static string Out => Environment.GetEnvironmentVariable("ZZ_TRACK_OUT")
        ?? "/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/track-probe/out2";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private const string CutUpdate = """
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
        3 1
        {row:3}

        """;

    private const string CutFirst = """
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

    private static void Dump(string name, byte[] file, string run, params int[] objects)
    {
        Directory.CreateDirectory(Out);
        var text = new StringBuilder();
        text.AppendLine(Invariant($"== {name} ({file.Length} bytes)"));
        using (var fresh = PdfDocument.Open(file))
        {
            foreach (var number in objects)
            {
                text.AppendLine(Invariant($"GetObject({number}) = {fresh.GetObject(new PdfObjectId(number, 0))}"));
            }

            text.AppendLine(Invariant($"Catalog is a catalog: {fresh.Catalog?.IsOfType(PdfName.Catalog)}; WasRepaired {fresh.WasRepaired}"));
            foreach (var d in fresh.Diagnostics)
            {
                text.AppendLine(d.ToString());
            }
        }

        using var document = PdfDocument.Open(file);
        var report = new PdfValidator().Validate(document);
        text.AppendLine("-- findings");
        foreach (var f in report.Findings)
        {
            text.AppendLine(f.ToString());
        }

        File.AppendAllText(Path.Combine(Out, "run-" + run + ".txt"), text.ToString());
    }

    private static byte[] OnlyRows(int type)
    {
        var file = new HandWrittenFile(version: "1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var at = file.Position;
        var data = StreamRows((type, 0, 0), (type, 0, 0), (type, 0, 0), (type, 0, 0));
        file.Object(3, Invariant($"<< /Type /XRef /W [1 4 2] /Size 4 /Root 1 0 R /Length {data.Length} >>\nstream\n{data}\nendstream"));
        file.Raw(Invariant($"startxref\n{at}\n%%EOF\n"));
        return file.Bytes;
    }

    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    public void ZzTrack2(string run)
    {
        Dump("only-reserved-rows", OnlyRows(3), run);
        Dump("only-free-rows", OnlyRows(0), run);
        Dump("prev-eof-after-row", PdfTemplate.Build(CutUpdate), run, 3, 4);
        Dump("prev-dictionary-no-keyword", PdfTemplate.Build(CutUpdate + "<< /Size 5 /Root 1 0 R >>\n"), run, 3, 4);
        Dump("first-eof-after-row", PdfTemplate.Build(CutFirst), run, 3, 4);
        Dump("first-dictionary-no-keyword", PdfTemplate.Build(CutFirst + "<< /Size 5 /Root 1 0 R >>\n"), run, 3, 4);
    }
}
