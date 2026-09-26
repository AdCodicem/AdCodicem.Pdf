using System.Globalization;
using System.Text.RegularExpressions;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IntegrationTests;

/// <summary>
/// Checks, against qpdf running in a container, which Flate streams of the corpus the reader finds cut short.
/// </summary>
/// <remarks>
/// <para>
/// The framework's inflater takes the end of its input for the end of the data, so the reader has to notice a
/// lost tail on its own (T32). qpdf's inflater is zlib's, which says so: it warns "input stream is complete but
/// output may still be valid" at the offset where the stream's data starts, which is where the reader reports
/// it too. Both decode every stream whose filters they can undo whole, unreferenced objects included.
/// </para>
/// <para>
/// The two may differ only where they did not read the same bytes. qpdf recovers the length of a stream whose
/// <c>/Length</c> it does not trust, and says so at the same offset; its recovered length may take in an
/// end-of-line the reader leaves out, or keep what the reader keeps of a stream the file cuts short. Nothing
/// else excuses a difference. qpdf does not check zlib's checksum at all, so a stream whose checksum is wrong
/// is not compared: the reader's report of one is not the report this test is about.
/// </para>
/// </remarks>
[Collection(RefereeCollection.Name)]
public partial class FlateRefereeTests(RefereeContainer referee)
{
    [Theory]
    [MemberData(nameof(ReadableDocuments))]
    public async Task The_reader_finds_cut_short_exactly_the_flate_streams_qpdf_finds_cut_short(string file)
    {
        Assert.SkipWhen(referee.Unavailable is not null, referee.Unavailable ?? string.Empty);

        var ours = StreamsFoundCutShort(file);
        var (_, _, stderr) = await referee.RunAsync(
            "qpdf",
            "--decode-level=generalized",
            "--stream-data=uncompress",
            "--preserve-unreferenced",
            RefereeContainer.PathInContainer(file),
            "/dev/null");
        var theirs = Offsets(CutShort(), stderr);
        var recovered = Offsets(LengthRecovered(), stderr);

        theirs.Except(ours).Should().BeEmpty(
            $"qpdf finds these streams of {file} cut short and the reader must too:\n{stderr}");
        ours.Except(theirs).Except(recovered).Should().BeEmpty(
            $"the reader alone finds these streams of {file} cut short, where qpdf read the length the file gave:\n{stderr}");
    }

    /// <summary>Every document the reader can open without a password, whatever else is wrong with it.</summary>
    public static TheoryData<string> ReadableDocuments
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var path in Corpus.PathsWhere(document => !document.Expect.Encrypted))
            {
                data.Add(path);
            }

            return data;
        }
    }

    /// <summary>
    /// Reads every object and decodes every stream the pipeline decodes whole, and returns the offsets at which
    /// a Flate stream was found to end before its data or its checksum did.
    /// </summary>
    /// <remarks>
    /// The reader reads here as qpdf does: without its limits, since qpdf has none, and the corpus holds no
    /// document that asks for them other than to be read whole.
    /// </remarks>
    private static HashSet<long> StreamsFoundCutShort(string file)
    {
        using var document = PdfDocument.Open(
            Corpus.Read(file), PdfReaderOptions.Default with { Limits = PdfReaderLimits.Unbounded });

        foreach (var number in document.ObjectNumbers.ToList())
        {
            if (document.GetObject(new PdfObjectId(number)) is PdfStream stream && !stream.HasImageFilter())
            {
                stream.Decode(document.Diagnostics);
            }
        }

        // The reader's two reports of a Flate stream that ran out: its tail lost, or only its checksum.
        return
        [
            .. document.Diagnostics
                .Where(entry => entry.Message.StartsWith("A Flate stream ends before", StringComparison.Ordinal))
                .Select(entry => entry.Position),
        ];
    }

    private static HashSet<long> Offsets(Regex warning, string stderr) =>
        [.. warning.Matches(stderr).Select(match => long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))];

    [GeneratedRegex(@"offset (\d+)\): input stream is complete but output may still be valid")]
    private static partial Regex CutShort();

    [GeneratedRegex(@"offset (\d+)\): attempting to recover stream length")]
    private static partial Regex LengthRecovered();
}
