using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IntegrationTests;

/// <summary>
/// Checks, against qpdf running in a container, which Flate streams of the corpus the reader finds cut short, and
/// what it keeps of those whose checksum is wrong or that turn corrupt partway.
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
/// only, since its silence then means nothing. A known exception is named below, with the debt it waits on; there
/// is none since issue #55.
/// </para>
/// <para>
/// Two other reports are checked stream by stream, against what qpdf serves of the same object (issue #56). qpdf
/// ignores a zlib checksum that disagrees with the data, as other readers do, and keeps all of it: the reader, which
/// reports such a stream as <c>filter.checksum-mismatch</c> rather than cut short, must keep the same bytes, and qpdf
/// must say nothing of the stream's data. Of data that turns corrupt partway, qpdf keeps what its inflater had passed
/// on before the fault, 64 KB at a time — nothing, for most such streams —, and warns that it could not decode it: the
/// reader, which keeps what decoded before the byte the fault lies in, must keep at least those bytes. It could keep
/// fewer only if the few bytes that one byte decodes ahead of the fault, which it loses, took qpdf's inflater past
/// one of its 64 KB; on the corpus, none does. Keeping nothing of most, qpdf cannot say how much the reader keeps of
/// them: <c>CorpusReadingTests</c> holds the committed ones to libz's own figures.
/// </para>
/// </remarks>
[Collection(RefereeCollection.Name)]
public partial class FlateRefereeTests(RefereeContainer referee)
{
    /// <summary>
    /// Streams the reader alone finds cut short, for a reason of its own, by document: none since issue #55, which made
    /// the reader find the endstream of SAMHSA's object 27 as qpdf does. An entry fails the test once its reason is
    /// fixed, so that it goes with it.
    /// </summary>
    private static readonly Dictionary<string, long[]> KnownReaderOnly = new(StringComparer.Ordinal);

    /// <summary>
    /// Streams of the committed corpus the reader must find corrupt, by document. They make sure the check of corrupt
    /// streams runs at all: a report it no longer recognized would leave it comparing nothing, and passing.
    /// </summary>
    private static readonly Dictionary<string, int[]> KnownCorrupt = new(StringComparer.Ordinal)
    {
        ["vendor/opf-format-corpus/distiller7-pscript5-census-housing-units-2005.pdf"] = [14],
        ["vendor/opf-format-corpus/groff-distiller405-mac-usgs-gps-noise-spectra.pdf"] = [211],
        ["vendor/opf-format-corpus/word9-distiller405-usgs-nwql-volatile-organics-methods.pdf"] = [237],
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
        var header = QpdfIndex.HeaderOffset(bytes);
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

    [Theory]
    [MemberData(nameof(ReadableDocuments))]
    public async Task The_reader_keeps_what_qpdf_keeps_of_a_flate_stream_whose_checksum_is_wrong_and_at_least_as_much_of_a_corrupt_one(string file)
    {
        Assert.SkipWhen(referee.Unavailable is not null, referee.Unavailable ?? string.Empty);

        var bytes = Corpus.Read(file);
        var streams = StreamsFoundDamaged(bytes);

        KnownCorrupt.GetValueOrDefault(file, []).Except(streams.Where(stream => stream.Corrupt).Select(stream => stream.Number))
            .Should().BeEmpty($"the reader must find these streams of {file} corrupt");

        if (streams.Count == 0)
        {
            return;
        }

        // qpdf shows an object by number and generation, and a rebuilt index can hold one number under two
        // generations: the one asked for is that of the entry placing the copy the reader decoded, which is also the
        // check that both read the same stream. Exit code 2 is an index qpdf could not build — a file it gives up on,
        // or one the container cannot see —, where no copy of any stream would be found, and what qpdf said is why.
        var path = RefereeContainer.PathInContainer(file);
        var (indexExitCode, xref, indexStderr) = await referee.RunAsync("qpdf", "--show-xref", path);
        indexExitCode.Should().NotBe(2, $"qpdf must index {file} to referee its damaged Flate streams:\n{indexStderr}");
        var entries = QpdfIndex.Entries(xref, bytes);
        var disagreements = new List<string>();

        foreach (var (number, dataStart, decoded, corrupt) in streams)
        {
            var copy = entries.FindIndex(entry => entry.Number == number && QpdfIndex.DataStartsAt(bytes, entry.Offset, dataStart));

            if (copy < 0)
            {
                disagreements.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"object {number}: qpdf's index places no copy of it whose data starts at {dataStart}, where the reader's does"));
                continue;
            }

            var generation = entries[copy].Generation;

            // The data goes to a file, and only its size and SHA-256 come back: standard output is text.
            var (exitCode, output, stderr) = await referee.RunAsync(
                "/bin/sh",
                "-c",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"qpdf --show-object={number},{generation} --filtered-stream-data --decode-level=specialized '{path}' > /tmp/flate-data; status=$?; wc -c < /tmp/flate-data; sha256sum < /tmp/flate-data | cut -d ' ' -f 1; exit $status"));
            var stream = string.Create(CultureInfo.InvariantCulture, $"object {number} {generation}");

            // Exit code 3 is data served with warnings, which is still data; exit code 2 is none.
            if (exitCode is not (0 or 3))
            {
                disagreements.Add($"{stream}: qpdf could not show it:\n{stderr}");
                continue;
            }

            var (length, hash) = Served(output);
            var aboutData = WarningsAboutData(stderr, path);

            if (corrupt)
            {
                if (length > decoded.Length || hash != Sha256(decoded.AsSpan(0, length)))
                {
                    disagreements.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{stream}, which the reader finds corrupt: qpdf keeps {length} bytes, which are not the first of the {decoded.Length} the reader keeps"));
                }

                if (!aboutData.Exists(line => line.Contains($"error decoding stream data for {stream}: ", StringComparison.Ordinal)))
                {
                    disagreements.Add($"{stream}, which the reader finds corrupt: qpdf decodes it without an error:\n{stderr}");
                }
            }
            else
            {
                if (length != decoded.Length || hash != Sha256(decoded))
                {
                    disagreements.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{stream}, whose checksum the reader finds wrong: qpdf keeps {length} bytes, which are not the {decoded.Length} the reader keeps"));
                }

                // Silence is what qpdf says of the stream's data. Showing one object of a damaged file, it may still
                // warn of the file's structure — an index it rebuilt, the length of this very stream, recovered from
                // its endstream — which the reader reports apart, and the other referees check: on the corpus, every
                // warning it gives beside a stream whose checksum is wrong is one of those.
                if (aboutData.Count > 0)
                {
                    disagreements.Add($"{stream}, whose checksum the reader finds wrong: qpdf says of it:\n{string.Join('\n', aboutData)}");
                }
            }
        }

        disagreements.Should().BeEmpty($"the reader must keep what qpdf keeps of the damaged Flate streams of {file}");
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
    /// document that asks for them other than to be read whole. Every diagnostic is kept (issue #134): IBM's QMF
    /// manual reports thousands of relocated objects, and the default capacity would drop the reports of its later
    /// streams.
    /// </remarks>
    private static (HashSet<long> CutShort, HashSet<long> Excused) StreamsFoundCutShort(byte[] bytes)
    {
        using var document = PdfDocument.Open(
            bytes, PdfReaderOptions.Default with { Limits = PdfReaderLimits.Unbounded, DiagnosticCapacity = int.MaxValue });

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

    /// <summary>
    /// Reads every object as qpdf does, without the reader's limits, decodes every stream the pipeline decodes, and
    /// returns each one the reader reports with a checksum that disagrees with its data, or corrupt partway: its object
    /// number, where its data starts, what it decoded to, and whether it turned corrupt.
    /// </summary>
    /// <remarks>
    /// Each stream reports into diagnostics of its own, so that a report is known to be that stream's, whatever the
    /// document reported before it.
    /// </remarks>
    private static List<(int Number, long DataStart, byte[] Decoded, bool Corrupt)> StreamsFoundDamaged(byte[] bytes)
    {
        using var document = PdfDocument.Open(bytes, PdfReaderOptions.Default with { Limits = PdfReaderLimits.Unbounded });
        var found = new List<(int, long, byte[], bool)>();

        foreach (var number in document.ObjectNumbers.ToList())
        {
            if (document.GetObject(new PdfObjectId(number)) is not PdfStream stream || stream.HasImageFilter())
            {
                continue;
            }

            var diagnostics = new PdfDiagnostics();
            var decoded = stream.Decode(diagnostics);

            // A fault anywhere in a chain leaves qpdf at best a part of the data, even beside a checksum found wrong.
            if (diagnostics.Any(entry => entry.Message.StartsWith("A Flate stream is corrupt at byte", StringComparison.Ordinal)))
            {
                found.Add((number, stream.Data.Position, decoded.ToArray(), true));
            }
            else if (diagnostics.Contains(PdfDiagnosticCodes.FilterChecksumMismatch))
            {
                found.Add((number, stream.Data.Position, decoded.ToArray(), false));
            }
        }

        return found;
    }

    /// <summary>
    /// Returns the lines of what qpdf printed on standard error, showing one object of the file at
    /// <paramref name="path"/>, that are neither its closing summary nor about the file's structure.
    /// </summary>
    private static List<string> WarningsAboutData(string stderr, string path)
    {
        var prefix = $"WARNING: {path}";

        return stderr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line != "qpdf: operation succeeded with warnings"
                && !(line.StartsWith(prefix, StringComparison.Ordinal) && StructuralWarning().IsMatch(line[prefix.Length..])))
            .ToList();
    }

    /// <summary>Reads what the shell printed — the size of the data qpdf served, then its SHA-256 — as a length and a hash.</summary>
    private static (int Length, string Hash) Served(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (int.Parse(lines[0], CultureInfo.InvariantCulture), lines[1]);
    }

    private static string Sha256(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));

    [GeneratedRegex(@"offset (\d+)\): input stream is complete but output may still be valid")]
    private static partial Regex CutShort();

    /// <summary>
    /// What qpdf says of a file's structure, after the file's name: that it rebuilt the index, and that it recovered the
    /// length of a stream whose <c>/Length</c> is wrong from the stream's endstream — which the reader reports as
    /// <c>stream.length-invalid</c>. The object such a warning names is the one qpdf was parsing: for a stream whose
    /// <c>/Length</c> is an indirect reference, SAMHSA's object 27, that reference's.
    /// </summary>
    [GeneratedRegex(@"^(?: \((?:object \d+ \d+, )?offset \d+\))?: (?:file is damaged|xref not found|Attempting to reconstruct cross-reference table|expected endstream|attempting to recover stream length|recovered stream length: \d+)$")]
    private static partial Regex StructuralWarning();
}
