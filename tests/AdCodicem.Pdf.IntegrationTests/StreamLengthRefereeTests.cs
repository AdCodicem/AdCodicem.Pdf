using System.Globalization;
using System.Text.RegularExpressions;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IntegrationTests;

/// <summary>
/// Checks, against qpdf running in a container, the length the reader takes for each stream of the corpus whose declared
/// length it found wrong, or could not take (issues #55 and #120).
/// </summary>
/// <remarks>
/// <para>
/// qpdf recovers such a stream's length as the reader does, from the first <c>endstream</c> after the data, and serves
/// the data it recovered: <c>qpdf --show-object=N --raw-stream-data</c>. Its recovered length runs up to the keyword,
/// the end-of-line before it included, which the reader leaves out as the syntax's rather than the data's (ISO 32000-1,
/// 7.3.8.1): one CR LF, LF or CR is taken off qpdf's before the two are compared.
/// </para>
/// <para>
/// qpdf reads the copy of an object its own index places, which is the reader's only when both indexes place the same
/// one: a stream qpdf's index lacks — tiff2pdf's scans, whose chain qpdf does not follow —, or places elsewhere — the
/// Atypon article's object 49, which the reader reads where its rebuilt index places it —, is another stream, and is not
/// compared. qpdf's search for <c>endstream</c> is not bounded by the next object, as the reader's past its window is;
/// where the two disagree for that reason, the stream is named below. Inside the window the reader does not bound its
/// search either, and the two agree even where both are wrong: PDFBox's zeroed object stream, object 417, takes object
/// 441's endstream, a zeroed stretch having erased its own.
/// </para>
/// </remarks>
[Collection(RefereeCollection.Name)]
public partial class StreamLengthRefereeTests(RefereeContainer referee)
{
    /// <summary>
    /// Streams whose length the reader and qpdf take differently, for a reason of qpdf's, by document. PDFium's urban
    /// planning report: a block of zeros erased the end of object 695's data, its <c>endstream</c> and the objects after
    /// it; qpdf takes the first <c>endstream</c> past the block, object 778's, and reads 3,292,376 bytes, where the reader
    /// stops at object 696, which the index as written places 21 bytes past the declared length, and keeps that length.
    /// </summary>
    private static readonly Dictionary<string, int[]> KnownDisagreements = new(StringComparer.Ordinal)
    {
        ["remote/opf-format-corpus/jhove-hul-35-pdfium-urban-planning-report.pdf"] = [695],
    };

    [Theory]
    [MemberData(nameof(DamagedDocuments))]
    public async Task The_reader_takes_the_length_qpdf_recovers_for_a_stream_whose_declared_length_is_wrong(string file)
    {
        Assert.SkipWhen(referee.Unavailable is not null, referee.Unavailable ?? string.Empty);

        var bytes = Corpus.Read(file);
        var streams = StreamsWhoseLengthIsWrong(bytes);
        var known = KnownDisagreements.GetValueOrDefault(file, []);

        if (streams.Count == 0)
        {
            known.Should().BeEmpty($"{file} has no stream whose length the reader found wrong");
            return;
        }

        var path = RefereeContainer.PathInContainer(file);
        var header = HeaderOffset(bytes);
        var (_, xref, _) = await referee.RunAsync("qpdf", "--show-xref", path);
        var offsets = UncompressedEntry().Matches(xref).ToDictionary(
            match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            match => long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
        var disagreements = new List<string>();
        var agreements = new List<int>();

        foreach (var (number, dataStart, length) in streams)
        {
            if (!offsets.TryGetValue(number, out var offset) || !DataStartsAt(bytes, header + offset, dataStart))
            {
                continue;
            }

            // The data goes to a file, and only its size and last two bytes come back: standard output is text.
            var (_, output, _) = await referee.RunAsync(
                "/bin/sh",
                "-c",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"qpdf --show-object={number} --raw-stream-data '{path}' > /tmp/stream-data 2>/dev/null; wc -c < /tmp/stream-data; tail -c 2 /tmp/stream-data | od -An -tu1"));
            var theirs = RecoveredLength(output);

            if (theirs == length)
            {
                agreements.Add(number);
            }
            else if (!known.Contains(number))
            {
                disagreements.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"object {number}, whose data starts at {dataStart}: the reader takes {length} bytes, qpdf {theirs}"));
            }
        }

        disagreements.Should().BeEmpty($"the reader must take the length qpdf recovers for the streams of {file}");
        known.Intersect(agreements).Should().BeEmpty(
            $"these streams of {file} were known to be read differently by qpdf; remove them from {nameof(KnownDisagreements)}");
    }

    /// <summary>Every document the reader can open without a password whose manifest entry says it is damaged.</summary>
    public static TheoryData<string> DamagedDocuments
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var path in Corpus.PathsWhere(document => !document.Expect.Encrypted && !document.Expect.Clean))
            {
                data.Add(path);
            }

            return data;
        }
    }

    /// <summary>
    /// Reads every object as qpdf does, without the reader's limits, and returns each stream reported as
    /// <c>stream.length-invalid</c> — its object number, where its data starts, and the length the reader took. Every
    /// diagnostic is kept: IBM's QMF manual reports thousands of relocated objects before two of its streams.
    /// </summary>
    private static List<(int Number, long DataStart, int Length)> StreamsWhoseLengthIsWrong(byte[] bytes)
    {
        using var document = PdfDocument.Open(
            bytes, PdfReaderOptions.Default with { Limits = PdfReaderLimits.Unbounded, DiagnosticCapacity = int.MaxValue });
        var streams = new Dictionary<long, (int Number, int Length)>();

        foreach (var number in document.ObjectNumbers.ToList())
        {
            if (document.GetObject(new PdfObjectId(number)) is PdfStream stream)
            {
                streams[stream.Data.Position] = (number, stream.Data.Length);
            }
        }

        var found = new List<(int, long, int)>();

        foreach (var diagnostic in document.Diagnostics)
        {
            if (diagnostic.Code == PdfDiagnosticCodes.StreamLengthInvalid && streams.TryGetValue(diagnostic.Position, out var stream))
            {
                found.Add((stream.Number, diagnostic.Position, stream.Length));
            }
        }

        return found;
    }

    /// <summary>
    /// Determines whether the object whose header starts at <paramref name="objectOffset"/> is the stream whose data
    /// starts at <paramref name="dataStart"/>: the first <c>stream</c> keyword after its header, and the end-of-line after
    /// it, end there.
    /// </summary>
    private static bool DataStartsAt(byte[] bytes, long objectOffset, long dataStart)
    {
        if (objectOffset < 0 || objectOffset >= dataStart || dataStart > bytes.Length)
        {
            return false;
        }

        var span = bytes.AsSpan((int)objectOffset, (int)(dataStart - objectOffset));
        var searchFrom = 0;

        while (searchFrom < span.Length)
        {
            var index = span[searchFrom..].IndexOf("stream"u8);

            if (index < 0)
            {
                return false;
            }

            var afterKeyword = searchFrom + index + "stream".Length;

            if (afterKeyword < span.Length && span[afterKeyword] is (byte)'\r' or (byte)'\n')
            {
                return span.Length - afterKeyword is 1 or 2;
            }

            searchFrom = afterKeyword;
        }

        return false;
    }

    /// <summary>
    /// Reads what the shell printed — the size of the data qpdf served, then its last two bytes in decimal — and gives
    /// the length qpdf recovered without the end-of-line before <c>endstream</c>.
    /// </summary>
    private static long RecoveredLength(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var length = long.Parse(lines[0], CultureInfo.InvariantCulture);
        var tail = lines.Length > 1
            ? lines[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToList()
            : [];

        if (tail.Count > 0 && tail[^1] == '\n')
        {
            length--;
            tail.RemoveAt(tail.Count - 1);
        }

        if (tail.Count > 0 && tail[^1] == '\r')
        {
            length--;
        }

        return length;
    }

    /// <summary>Where <c>%PDF-</c> starts, in the first kilobyte as readers look for it: qpdf's offsets count from there.</summary>
    private static long HeaderOffset(byte[] bytes)
    {
        var header = bytes.AsSpan(0, Math.Min(bytes.Length, 1024)).IndexOf("%PDF-"u8);
        return Math.Max(header, 0);
    }

    [GeneratedRegex(@"^(\d+)/\d+: uncompressed; offset = (\d+)", RegexOptions.Multiline)]
    private static partial Regex UncompressedEntry();
}
