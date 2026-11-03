using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Holds every file crafted while measuring #128, #237 and #312 to one report, whatever was read before validating it —
/// nothing, a validation, the objects whose reading once changed the report, or every object —, and whatever the reader's
/// cache.
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

    [Theory]
    [MemberData(nameof(Shapes))]
    public void A_crafted_file_validates_the_same_whatever_the_cache(string shape)
    {
        // The walk keeps what it follows for as many objects as the reader caches, and takes what it kept only where
        // following it again would find the same (#201, #312): the default cache, which keeps a run or a nest a smaller one
        // lets go of, finds what the smallest does.
        var crafted = ConvergenceShapes.All[shape];
        var fresh = Findings(crafted, _ => { });

        foreach (var capacity in (int[])[8192, ConvergenceShapes.SmallestCache])
        {
            Findings(crafted with { CacheCapacity = capacity }, _ => { }).Should().Equal(fresh, "a cache of {0} objects finds the same", capacity);
        }
    }

    /// <summary>Gets the seeds the files of <see cref="ConvergenceShapes.GeneratedHolders"/> are drawn from.</summary>
    public static TheoryData<int> Seeds => [.. Enumerable.Range(0, 64)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Object_streams_drawn_from_every_kind_of_holder_validate_the_same_whatever_was_read_before_and_whatever_the_cache(int seed)
    {
        // Each file draws the parts that make what an object stream's decoding gives depend on what the reader read before
        // and on its cache — a stream of predictor reading itself again through its /Length, directly, through a link or
        // through a nest at the reader's deepest load, holders taking their parameters from one another's objects — and lets
        // object stream 6 take its parameters from a holder's object, which leads to an object 6 holds, or not, as the
        // holder's decoding reads its predictor. The walk follows such an object only where its holder decodes alike in
        // every read order (#312): the circular streams are the same whatever was read before, and whatever the cache, which
        // at 64 objects lets go of a stream a decoding met before meeting it again. Of the first 5,000 seeds, 5 gave two
        // verdicts, each where the reader decoded a holder at its deepest load, which cut a value its decoding read, or not,
        // as a read before had left the holder in its cache, and kept what the cut gave (#163): none of the 64 drawn here.
        var file = ConvergenceShapes.GeneratedHolders(seed);
        var fresh = Circular(file, 8192, []);

        foreach (var capacity in (int[])[8192, ConvergenceShapes.SmallestCache])
        {
            Circular(file, capacity, null).Should().Equal(fresh, "a validation before changes nothing, under a cache of {0}", capacity);

            foreach (var reads in ConvergenceShapes.GeneratedReads)
            {
                Circular(file, capacity, reads).Should().Equal(fresh, "reading {0} first changes nothing, under a cache of {1}", string.Join(", ", reads), capacity);
            }
        }

        // Gives the circular object streams' findings, once the objects of `reads` are read, or a validation made.
        static List<string> Circular(byte[] file, int capacity, PdfObjectId[]? reads)
        {
            using var document = PdfDocument.Open(file, new PdfReaderOptions { ObjectCacheCapacity = capacity });

            if (reads is null)
            {
                new PdfValidator().Validate(document);
            }
            else
            {
                Read(document, reads);
            }

            return [.. new PdfValidator().Validate(document).Findings
                .Where(finding => finding.RuleId == PdfValidationRuleIds.XRefObjectStreamCircular).Select(finding => finding.ToString())];
        }
    }

    private static List<string> Findings(ConvergenceShape shape, Action<PdfDocument> before)
    {
        using var document = PdfDocument.Open(shape.File, new PdfReaderOptions { Limits = shape.Limits, ObjectCacheCapacity = shape.CacheCapacity });
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
