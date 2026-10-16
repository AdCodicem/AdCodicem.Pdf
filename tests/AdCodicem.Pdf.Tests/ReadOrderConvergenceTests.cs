using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Holds every file crafted while measuring #128 and #237 to one report, whatever was read before validating it: nothing, a
/// validation, the objects whose reading once changed the report, or every object.
/// </summary>
public class ReadOrderConvergenceTests
{
    public static TheoryData<string> Shapes
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var name in ConvergenceShapes.All.Keys)
            {
                data.Add(name);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void A_crafted_file_validates_the_same_whatever_was_read_before(string shape)
    {
        var crafted = ConvergenceShapes.All[shape];
        var fresh = Findings(crafted, _ => { });

        Findings(crafted, document => new PdfValidator().Validate(document)).Should().Equal(fresh, "a second validation finds what the first did");

        foreach (var reads in crafted.Reads)
        {
            Findings(crafted, document => Read(document, reads)).Should().Equal(
                fresh,
                $"reading {string.Join(", ", reads)} first once changed what validating {shape} found");
        }

        Findings(crafted, ReadEverything).Should().Equal(fresh, "reading every object first leaves nothing to find otherwise");
    }

    private static List<string> Findings(ConvergenceShape shape, Action<PdfDocument> before)
    {
        using var document = PdfDocument.Open(shape.File, new PdfReaderOptions { Limits = shape.Limits });
        before(document);
        return [.. new PdfValidator().Validate(document).Findings.Select(finding => finding.ToString())];
    }

    private static void Read(PdfDocument document, PdfObjectId[] reads)
    {
        foreach (var id in reads)
        {
            document.GetObject(id);
        }
    }

    /// <summary>Reads every object the index holds, and decodes every stream whose data is not an image's.</summary>
    private static void ReadEverything(PdfDocument document)
    {
        foreach (var number in document.ObjectNumbers.ToList())
        {
            if (document.GetObject(new PdfObjectId(number)) is PdfStream stream && !stream.HasImageFilter())
            {
                stream.Decode(document.Diagnostics);
            }
        }
    }
}
