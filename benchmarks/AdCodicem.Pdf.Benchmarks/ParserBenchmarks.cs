using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures what parsing one object costs, for shapes dense in dictionary entries, strings and names, and for the hostile
/// shapes the parser reports (#119, #172).
/// </summary>
/// <remarks>
/// The sound shapes are what files hold: a page dictionary as Word writes one; ten thousand distinct keys; a thousand
/// dictionaries of literal and hexadecimal strings, spaced as producers space them; a thousand font dictionaries whose names
/// carry <c>#xx</c> escapes. They hold no fault, so they show what the checks the parser makes on every entry, string and
/// name cost when nothing is wrong. The hostile shapes are what a file can make the parser report: ten thousand repeats of
/// ten keys, ten thousand distinct keys given null, a thousand hexadecimal strings holding stray bytes, a thousand names
/// whose number sign is no escape, and an object the end of the data leaves open. Their reports are bounded by the
/// diagnostics' capacity: past it, they are counted and not formatted.
/// </remarks>
[MemoryDiagnoser]
public class ParserBenchmarks
{
    /// <summary>A page dictionary as Word's PDF export writes one.</summary>
    private const string Page =
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.32 841.92] /CropBox [0 0 595.32 841.92] /Rotate 0 " +
        "/Resources << /Font << /F1 5 0 R /F2 7 0 R /F3 9 0 R >> /ExtGState << /GS7 11 0 R /GS8 12 0 R >> " +
        "/XObject << /Image13 13 0 R >> /ProcSet [/PDF /Text /ImageB /ImageC /ImageI] >> /Contents 4 0 R " +
        "/Annots [20 0 R 21 0 R] /Group << /Type /Group /S /Transparency /CS /DeviceRGB >> /Tabs /S /StructParents 0 >>";

    private byte[] _data = [];

    [Params(
        "page",
        "wide-10000",
        "strings-1000",
        "names-1000",
        "hostile-repeats-10000",
        "hostile-nulls-10000",
        "hostile-hex-1000",
        "hostile-names-1000",
        "hostile-unclosed-1000")]
    public string Shape { get; set; } = string.Empty;

    [GlobalSetup]
    public void Setup() => _data = Encoding.Latin1.GetBytes(Build(Shape));

    [Benchmark(Description = "Parse one object")]
    public int Parse()
    {
        var diagnostics = new PdfDiagnostics();
        var parser = new PdfObjectParser(_data, diagnostics: diagnostics);
        var value = parser.ParseObject();

        return diagnostics.Count + diagnostics.SuppressedCount + value switch
        {
            PdfDictionary dictionary => dictionary.Count,
            PdfArray array => array.Count,
            _ => 0,
        };
    }

    private static string Build(string shape)
    {
        var text = new StringBuilder();

        switch (shape)
        {
            case "page":
                text.Append(Page);
                break;

            case "wide-10000":
                text.Append("<<");
                for (var i = 0; i < 10_000; i++)
                {
                    text.Append(" /K").Append(i).Append(' ').Append(i);
                }

                text.Append(" >>");
                break;

            case "strings-1000":
                text.Append('[');
                for (var i = 0; i < 1000; i++)
                {
                    text.Append("<< /Title (Quarterly report, part ").Append(i)
                        .Append(") /Author (Jane Doe \\(Finance\\)) /Subject <FEFF00520065007000610072007400200074007200690061006C>")
                        .Append(" /ID [<9F8E7D6C5B4A39281706F5E4D3C2B1A0> <9F8E 7D6C 5B4A 3928 1706 F5E4 D3C2 B1A0>] >>\n");
                }

                text.Append(']');
                break;

            case "names-1000":
                text.Append('[');
                for (var i = 0; i < 1000; i++)
                {
                    text.Append("<< /Type /Font /Subtype /TrueType /BaseFont /ABCDEF#2B#82l#82r#20#96#BE#92#A9 /Name /F#20").Append(i)
                        .Append(" /Font#20Family /MS#20Mincho /Encoding /WinAnsiEncoding /FirstChar 32 /LastChar 255 >>\n");
                }

                text.Append(']');
                break;

            case "hostile-repeats-10000":
                text.Append("<<");
                for (var i = 0; i < 10_000; i++)
                {
                    text.Append(" /K").Append(i % 10).Append(' ').Append(i);
                }

                text.Append(" >>");
                break;

            case "hostile-nulls-10000":
                text.Append("<<");
                for (var i = 0; i < 10_000; i++)
                {
                    text.Append(" /K").Append(i).Append(" null");
                }

                text.Append(" >>");
                break;

            case "hostile-hex-1000":
                text.Append('[');
                text.Insert(1, "<FFxyz00GG1> ", 1000);
                text.Append(']');
                break;

            case "hostile-names-1000":
                text.Append('[');
                text.Insert(1, "/A#zzB#4 ", 1000);
                text.Append(']');
                break;

            case "hostile-unclosed-1000":
                text.Insert(0, "[<< /A (", 1000);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, "Not a shape this benchmark parses.");
        }

        return text.ToString();
    }
}
