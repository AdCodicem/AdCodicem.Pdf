using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Documents;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures the rebuild's scan for objects, which reads an object header back from each <c>obj</c> keyword of a file it
/// rebuilds the index of, and delimits each object it takes as a load lexes its value, and the same scan made for the memo
/// of what it takes (#171).
/// </summary>
/// <remarks>
/// <para>
/// Each file's <c>startxref</c> names nothing, so opening it rebuilds its index. The scan searches the file a megabyte at a
/// time for <c>obj</c>; it reads a header back from each, over digits, signs, white space and comments, from no further back
/// than where the object before it ends; it sweeps each line once, from where it last stood, to tell whether a comment or a
/// string holds the header; and it lexes the value of each object it takes, so that a header another object holds is none.
/// A sound file or plain headers cost what reading the headers and their values costs; a comment between a header's
/// tokens, a long comment line before each <c>obj</c>, or a string holding a <c>%</c> on the line before it, is swept once.
/// Every shape costs time linear in the file, and allocates nothing for it but the index.
/// </para>
/// <para>
/// The scan for the memo, which the searches near a row and near an offset ask for, runs on a document opened outside the
/// measurement, its index rebuilt already, since a rebuild makes no memo: it indexes nothing, also looks for each
/// <c>xref</c> keyword, and allocates what the reader keeps of what it takes, 16 bytes at most for each header and each
/// keyword, in an array grown by doubling and trimmed once the scan ends (<c>ScanMemo</c>). A file of lines of <c>xref</c>
/// keywords holds no header, and costs the scan for the memo noting each keyword.
/// </para>
/// <para>
/// The <c>trailer</c> keyword the generated files end with names the catalog, which the rebuild merges; the scan for
/// trailers the rebuild also runs is <see cref="TrailerScanBenchmarks"/>' to measure.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class ObjectScanBenchmarks
{
    /// <summary>How many objects the files of headers hold, and how many bytes about the files of lines hold.</summary>
    private const int Objects = 100_000;

    private const int LinesSize = 4 * 1024 * 1024;

    private byte[] _document = [];

    private PdfDocument? _opened;

    /// <summary>
    /// Gets or sets what the file holds: a sound document of 1,000 pages; objects whose headers are plain; objects whose
    /// headers hold a comment between the number and the generation; lines of a 60-byte comment each, each followed by a
    /// line <c>0 obj</c>; lines of a string holding a <c>%</c> before a number and a comment, each followed by a line
    /// <c>0 obj</c>; or lines of an <c>xref</c> keyword, at two distances in turn, so that no two keywords are held as one.
    /// </summary>
    [Params("sound", "plain-headers", "comment-headers", "comment-lines", "percent-strings", "xref-lines")]
    public string Shape { get; set; } = "sound";

    [GlobalSetup]
    public void Setup() =>
        _document = Shape switch
        {
            "sound" => WithoutIndex(SyntheticDocument.Create(pageCount: 1000, contentBytes: 4096)),
            "plain-headers" => Headers(" 0 obj\n(x)\nendobj\n"),
            "comment-headers" => Headers(" % a comment\n0 obj\n(x)\nendobj\n"),
            "comment-lines" => Lines("%" + new string('c', 60) + "\n0 obj\n"),
            "percent-strings" => Lines("(50%) 4 % x\n0 obj\n"),
            "xref-lines" => Lines("xref\nxref 0\n"),
            _ => throw new InvalidOperationException($"No shape {Shape}."),
        };

    [Benchmark(Description = "Rebuild an index by scanning the file for objects")]
    public int Open()
    {
        using var document = PdfDocument.Open(_document);
        return document.ObjectCount;
    }

    [IterationSetup(Target = nameof(ScanForMemo))]
    public void OpenForMemo() => _opened = PdfDocument.Open(_document);

    [IterationCleanup(Target = nameof(ScanForMemo))]
    public void CloseAfterMemo() => _opened?.Dispose();

    [Benchmark(Description = "Scan the file for the memo of what the rebuild's scan takes")]
    public int ScanForMemo() => _opened!.Reader.GetScanMemo().Count;

    /// <summary>Gives <paramref name="file"/> with its <c>startxref</c> naming nothing, so that opening it rebuilds its index.</summary>
    private static byte[] WithoutIndex(byte[] file)
    {
        var text = Encoding.Latin1.GetString(file);
        var at = text.LastIndexOf("startxref", StringComparison.Ordinal);
        var end = text.IndexOf("%%EOF", at, StringComparison.Ordinal);
        return Encoding.Latin1.GetBytes(text[..at] + "startxref\n9\n" + text[end..]);
    }

    /// <summary>Writes a catalog, an empty page tree and <see cref="Objects"/> objects numbered from 3, each its number and then <paramref name="rest"/>.</summary>
    private static byte[] Headers(string rest)
    {
        var text = Start();

        for (var number = 3; number < Objects + 3; number++)
        {
            text.Append(number.ToString(CultureInfo.InvariantCulture)).Append(rest);
        }

        return End(text);
    }

    /// <summary>Writes a catalog, an empty page tree and <paramref name="line"/> again and again, about <see cref="LinesSize"/> bytes of it.</summary>
    private static byte[] Lines(string line)
    {
        var text = Start();

        while (text.Length < LinesSize)
        {
            text.Append(line);
        }

        return End(text);
    }

    private static StringBuilder Start() =>
        new StringBuilder("%PDF-1.7\n")
            .Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
            .Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

    private static byte[] End(StringBuilder text) =>
        Encoding.Latin1.GetBytes(text.Append("trailer\n<< /Root 1 0 R >>\n%%EOF\n").ToString());
}
