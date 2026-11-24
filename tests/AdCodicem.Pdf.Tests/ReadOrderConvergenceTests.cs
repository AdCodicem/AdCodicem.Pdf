using System.Globalization;
using System.Text;
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
    [InlineData("/Type")]
    [InlineData("/Filter")]
    public void An_object_a_validation_s_probe_reads_through_a_holder_it_reads_as_null_reads_after_it_as_a_fresh_read_gives_it(string key)
    {
        // #313's loop: object streams 6 and 21 need each other, and 21 holds 7, 15, which stream 40's /Length names; 19's header
        // takes its /Type, or its /Filter, from 40. The validation's probe reads 7 as null, as the walk does, and 40 through it,
        // its data ending at the keyword endstream it holds: what it read through 7 is kept for that read alone, and 40 reads
        // after the validation, all 15 bytes of its data, as a fresh read gives it (#163, #313).
        var (members, first) = ConvergenceShapes.ObjectStreamData((10, "(ten)"), (11, "(eleven)"));
        var (loop, loopFirst) = ConvergenceShapes.ObjectStreamData((7, "15"), (20, "0"));
        var nineteen = key == "/Type" ? "/Type 40 0 R" : "/Type /ObjStm /Filter 40 0 R";
        var objects = new SortedDictionary<int, (string Entries, byte[]? Data)>
        {
            [6] = ("/Type /ObjStm /N 1 /First 5 /Length 20 0 R", Encoding.Latin1.GetBytes(string.Create(CultureInfo.InvariantCulture, $"31 0 {loopFirst} "))),
            [21] = ("/Type /ObjStm /N 2 /First 31 0 R /Length @L", Encoding.Latin1.GetBytes(loop)),
            [19] = (string.Create(CultureInfo.InvariantCulture, $"{nineteen} /N 2 /First {first} /Length @L"), Encoding.Latin1.GetBytes(members)),
            [40] = ("<< /Length 7 0 R >>\nstream\nxx endstream yy\nendstream", null),
        };
        var file = ConvergenceShapes.StreamsAndMembers(objects, new() { [31] = (6, 0), [7] = (21, 0), [20] = (21, 1), [10] = (19, 0), [11] = (19, 1) });
        using var document = PdfDocument.Open(file);

        new PdfValidator().Validate(document);

        document.GetObject(new PdfObjectId(40)).AsStream().Required().Decode().Length.Should().Be(15);
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
    public static TheoryData<int> Seeds => [.. Enumerable.Range(0, 64), 338, 1758, 3519, 3838, 4635, 12800];

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
        // at 64 objects lets go of a stream a decoding met before meeting it again. The last six seeds gave two verdicts
        // before the reader kept what its nesting cut for reads as deep alone (#163).
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
            // The walks' guard on decoding again, whose cut depends on what the reader kept before a walk, as its documentation
            // says, is lifted: the streams drawn here weigh a few bytes, and what a walk may decode again of them, with them.
            using var document = PdfDocument.Open(file, new PdfReaderOptions { ObjectCacheCapacity = capacity, Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = int.MaxValue } });

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
