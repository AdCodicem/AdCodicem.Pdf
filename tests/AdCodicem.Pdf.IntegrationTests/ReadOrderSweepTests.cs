using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.IntegrationTests;

/// <summary>
/// Holds each corpus document to one validation report, whatever was read before validating it: nothing, a validation, or
/// every object it holds, each stream whose data is not an image's decoded (#237).
/// </summary>
/// <remarks>
/// This suite asserts what an independent tool says, and this class is its one exception, by the maintainer's choice when
/// #237 was settled: it runs no tool, and validates every document three times over. The unit suite holds the same property
/// over the files crafted while measuring #128 and #237, and checks over the corpus that no read rebuilds the index once a
/// validation has settled it. No committed document has an index a read rebuilds after opening, so that over them this
/// guards against a regression; eight documents of the remote corpus do, three of them Quartz files whose damage no rule
/// reaches, and the nightly run holds it on them.
/// </remarks>
public class ReadOrderSweepTests
{
    public static TheoryData<string> AllDocuments
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var path in Corpus.Paths)
            {
                data.Add(path);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllDocuments))]
    public void A_corpus_document_validates_the_same_whatever_was_read_before(string file)
    {
        var entry = Corpus.Get(file);
        var fresh = Findings(entry, _ => { });

        Findings(entry, document => new PdfValidator().Validate(document)).Should().Equal(
            fresh, $"{entry.Name} validated a second time finds what it did the first");
        Findings(entry, ReadEverything).Should().Equal(fresh, $"{entry.Name} validated after every object was read finds what it did fresh");
    }

    private static List<string> Findings(CorpusDocument entry, Action<PdfDocument> before)
    {
        using var document = PdfDocument.Open(Corpus.Read(entry.File), CorpusOptions.For(entry));
        before(document);
        return [.. new PdfValidator().Validate(document).Findings.Select(finding => finding.ToString())];
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
