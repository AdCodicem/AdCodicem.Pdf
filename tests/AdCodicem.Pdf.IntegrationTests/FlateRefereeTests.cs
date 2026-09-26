using System.Globalization;
using System.Text.RegularExpressions;
using AdCodicem.Pdf.Diagnostics;
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
/// output may still be valid" at the offset where the stream's data starts, counted from the <c>%PDF-</c>
/// header, where the reader counts from the start of the file. qpdf decodes at the specialized level, which
/// undoes every filter the reader decodes and leaves the image filters, as the reader does; both decode
/// unreferenced objects too.
/// </para>
/// <para>
/// Every stream qpdf finds cut short, the reader must report. A stream only the reader reports must be one whose
/// bytes the reader knew were not what the file declared — a stream the file cuts short, which qpdf treats as
/// empty, or one whose <c>/Length</c> the reader found wrong, where qpdf may recover a different length —, or
/// raw deflate, which qpdf does not read at all. A document qpdf gives up on is checked in the first direction
/// only, since its silence then means nothing. Qpdf does not check zlib's checksum, so a stream whose checksum is
/// wrong is not compared: the reader's report of one is another report. The one known exception is named below,
/// with the debt it waits on.
/// </para>
/// </remarks>
[Collection(RefereeCollection.Name)]
public partial class FlateRefereeTests(RefereeContainer referee)
{
    /// <summary>
    /// Streams the reader alone finds cut short, for a reason of its own: T39 takes the <c>/Length</c> of a stream
    /// longer than the parser's window as it is, and SAMHSA's object 27 declares 26 bytes too few. qpdf recovers
    /// the length and decodes the stream whole. The test fails when T39 is fixed, so that the entry goes with it.
    /// </summary>
    private static readonly Dictionary<string, long[]> KnownReaderOnly = new(StringComparer.Ordinal)
    {
        ["remote/opf-format-corpus/quartz-word-samhsa-prevention-pathways-fact-sheet.pdf"] = [3278],
    };

    [Theory]
    [MemberData(nameof(ReadableDocuments))]
    public async Task The_reader_finds_cut_short_exactly_the_flate_streams_qpdf_finds_cut_short(string file)
    {
        Assert.SkipWhen(referee.Unavailable is not null, referee.Unavailable ?? string.Empty);

        var bytes = Corpus.Read(file);
        var (ours, excused) = StreamsFoundCutShort(bytes);
        var (exitCode, _, stderr) = await referee.RunAsync(
            "qpdf",
            "--decode-level=specialized",
            "--stream-data=uncompress",
            "--preserve-unreferenced",
            RefereeContainer.PathInContainer(file),
            "/dev/null");
        var header = HeaderOffset(bytes);
        var theirs = CutShort().Matches(stderr)
            .Select(match => header + long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToHashSet();

        theirs.Except(ours).Should().BeEmpty(
            $"qpdf finds these streams of {file} cut short and the reader must too:\n{stderr}");

        if (exitCode == 2)
        {
            return;
        }

        var known = KnownReaderOnly.GetValueOrDefault(file, []);
        var readerOnly = ours.Except(theirs).Except(excused).ToHashSet();

        readerOnly.Except(known).Should().BeEmpty(
            $"the reader alone finds these streams of {file} cut short, though it read them as the file declared:\n{stderr}");
        known.Except(readerOnly).Should().BeEmpty(
            $"these streams of {file} were known to be found cut short by the reader alone; remove them from {nameof(KnownReaderOnly)}");
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
    /// Reads every object and decodes every stream the pipeline decodes, and returns the offsets at which a Flate
    /// stream was found to end before its data or its checksum did, and those at which the reader knew the bytes
    /// it decoded were not the stream the file declared.
    /// </summary>
    /// <remarks>
    /// The reader reads here as qpdf does: without its limits, since qpdf has none, and the corpus holds no
    /// document that asks for them other than to be read whole.
    /// </remarks>
    private static (HashSet<long> CutShort, HashSet<long> Excused) StreamsFoundCutShort(byte[] bytes)
    {
        using var document = PdfDocument.Open(bytes, PdfReaderOptions.Default with { Limits = PdfReaderLimits.Unbounded });

        foreach (var number in document.ObjectNumbers.ToList())
        {
            if (document.GetObject(new PdfObjectId(number)) is PdfStream stream && !stream.HasImageFilter())
            {
                stream.Decode(document.Diagnostics);
            }
        }

        // The reader's two reports of a Flate stream that ran out: its tail lost, or only its checksum.
        var cutShort = document.Diagnostics
            .Where(entry => entry.Message.StartsWith("A Flate stream ends before", StringComparison.Ordinal))
            .Select(entry => entry.Position)
            .ToHashSet();
        var excused = document.Diagnostics
            .Where(entry => entry.Code is PdfDiagnosticCodes.StreamTruncated or PdfDiagnosticCodes.StreamLengthInvalid
                || entry.Message.StartsWith("A Flate stream was not valid zlib data", StringComparison.Ordinal))
            .Select(entry => entry.Position)
            .ToHashSet();

        return (cutShort, excused);
    }

    /// <summary>Where <c>%PDF-</c> starts, in the first kilobyte as readers look for it: qpdf's offsets count from there.</summary>
    private static long HeaderOffset(byte[] bytes)
    {
        var header = bytes.AsSpan(0, Math.Min(bytes.Length, 1024)).IndexOf("%PDF-"u8);
        return Math.Max(header, 0);
    }

    [GeneratedRegex(@"offset (\d+)\): input stream is complete but output may still be valid")]
    private static partial Regex CutShort();
}
