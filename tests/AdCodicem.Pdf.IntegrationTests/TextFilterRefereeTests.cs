using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IntegrationTests;

/// <summary>
/// Checks, against qpdf running in a container, what the reader decodes of ASCII85, ASCIIHex, RunLength and LZW data
/// that breaks its format, and that it reports the data qpdf cannot decode past (issue #141).
/// </summary>
/// <remarks>
/// <para>
/// No corpus document but one holds such data — an ASCII85 thumbnail without its end marker —, so each case is a page
/// whose content stream holds one fault of one filter. qpdf stops at a byte the format does not allow, keeps what decoded
/// before it and fails: every such stream, the reader must report, though it passes over the byte and decodes the rest.
/// Data that only lacks its end marker, ends inside a unit, or holds a group past four bytes, qpdf decodes to its end in
/// silence: the reader must decode it to the same bytes, whatever it reports of it.
/// </para>
/// <para>
/// qpdf decodes past RunLength's end-of-data byte, which the reader, as MuPDF, takes for the end: no case puts data after
/// it.
/// </para>
/// </remarks>
[Collection(RefereeCollection.Name)]
public class TextFilterRefereeTests(RefereeContainer referee)
{
    /// <summary>Data qpdf cannot decode past: a name for the case, the filter, and the data, as text or as <c>hex:</c> bytes.</summary>
    public static TheoryData<string, string, string> Refused => new()
    {
        { "a85-byte-outside", "ASCII85Decode", "87cURD]{i,\"Ebo7~>" },
        { "a85-z-in-group", "ASCII85Decode", "87cUzRD]i,\"Ebo7~>" },
        { "a85-broken-marker", "ASCII85Decode", "87cUR~xD]i,\"Ebo7~>" },
        { "a85-introducer", "ASCII85Decode", "<~87cURD]i,\"Ebo7~>" },
        { "hex-byte-outside", "ASCIIHexDecode", "4865G6C6C6F>" },
    };

    /// <summary>Data qpdf decodes to its end: a name for the case, the filter, and the data, as text or as <c>hex:</c> bytes.</summary>
    public static TheoryData<string, string, string> DecodedToTheEnd => new()
    {
        { "a85-sound", "ASCII85Decode", "87cURD]i,\"Ebo7~>" },
        { "a85-unmarked", "ASCII85Decode", "87cURD]i,\"" },
        { "a85-unmarked-inside-group", "ASCII85Decode", "87cURD]i,\"Ebo7" },
        { "a85-unmarked-lone-digit", "ASCII85Decode", "87cURD]i,\"E" },
        { "a85-lone-digit", "ASCII85Decode", "87cURD]i,\"E~>" },
        { "a85-unfinished-marker", "ASCII85Decode", "87cURD]i,\"Ebo7~" },
        { "a85-overflow", "ASCII85Decode", "87cURuuuuuD]i,\"Ebo7~>" },
        { "hex-sound", "ASCIIHexDecode", "48656C6C6F>" },
        { "hex-unmarked", "ASCIIHexDecode", "48656C6C6F" },
        { "hex-unmarked-odd", "ASCIIHexDecode", "48656C6C6" },
        { "rl-sound", "RunLengthDecode", "hex:014849FC4180" },
        { "rl-unmarked", "RunLengthDecode", "hex:014849FC41" },
        { "rl-unmarked-inside-run", "RunLengthDecode", "hex:014849FC4104414243" },
        { "rl-unmarked-before-repeated-byte", "RunLengthDecode", "hex:014849FE" },
        { "lzw-sound", "LZWDecode", "hex:801048502808" },
        { "lzw-unmarked", "LZWDecode", "hex:8010485020" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Every_stream_qpdf_cannot_decode_past_the_reader_reports(string name, string filter, string data)
    {
        Assert.SkipWhen(referee.Unavailable is not null, referee.Unavailable ?? string.Empty);

        var file = FileWith(filter, Bytes(data));
        var (exitCode, _, stderr) = await Qpdf(name, file);
        var (_, reports) = Read(file);

        exitCode.Should().NotBe(0, $"qpdf refuses {name}");
        stderr.Should().Contain("error decoding stream data for object 4 0", $"qpdf says why it refuses {name}");
        reports.Should().Contain(report => report.Code == PdfDiagnosticCodes.FilterFailed, $"the reader reports {name}");
    }

    [Theory]
    [MemberData(nameof(DecodedToTheEnd))]
    public async Task The_reader_decodes_what_qpdf_decodes_to_its_end_to_the_same_bytes(string name, string filter, string data)
    {
        Assert.SkipWhen(referee.Unavailable is not null, referee.Unavailable ?? string.Empty);

        var file = FileWith(filter, Bytes(data));
        var (exitCode, decoded, stderr) = await Qpdf(name, file);
        var (ours, _) = Read(file);

        exitCode.Should().Be(0, $"qpdf decodes {name} in silence: {stderr}");
        Convert.ToHexString(ours).Should().Be(decoded, $"the reader keeps what qpdf keeps of {name}");
    }

    /// <summary>Runs qpdf on <paramref name="file"/>, written into the container, and gives what it decoded of object 4, in hex.</summary>
    private async Task<(long? ExitCode, string Decoded, string Stderr)> Qpdf(string name, byte[] file)
    {
        var path = $"/tmp/text-filter-{name}.pdf";
        var (exitCode, stdout, stderr) = await referee.RunAsync(
            "sh",
            "-c",
            $"echo '{Convert.ToBase64String(file)}' | base64 -d > {path}; qpdf --show-object=4 --filtered-stream-data {path} > {path}.out; status=$?; od -An -v -tx1 {path}.out | tr -d ' \\n'; exit $status");
        return (exitCode, stdout.Trim().ToUpperInvariant(), stderr);
    }

    /// <summary>Decodes object 4 of <paramref name="file"/> as the reader does, with what it reports.</summary>
    private static (byte[] Decoded, PdfDiagnostics Reports) Read(byte[] file)
    {
        using var document = PdfDocument.Open(file);
        var reports = new PdfDiagnostics();
        var decoded = document.GetObject(new PdfObjectId(4)).AsStream()!.Decode(reports).ToArray();
        return (decoded, reports);
    }

    private static byte[] Bytes(string data) =>
        data.StartsWith("hex:", StringComparison.Ordinal) ? Convert.FromHexString(data[4..]) : Encoding.ASCII.GetBytes(data);

    /// <summary>Writes a one-page file whose content stream, object 4, holds <paramref name="data"/> under <paramref name="filter"/>.</summary>
    private static byte[] FileWith(string filter, byte[] data)
    {
        using var file = new MemoryStream();
        var offsets = new List<long>();

        void Write(string text) => file.Write(Encoding.ASCII.GetBytes(text));

        Write("%PDF-1.7\n");
        string[] bodies =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>",
        ];

        for (var number = 1; number <= 3; number++)
        {
            offsets.Add(file.Position);
            Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{bodies[number - 1]}\nendobj\n"));
        }

        offsets.Add(file.Position);
        Write(string.Create(CultureInfo.InvariantCulture, $"4 0 obj\n<< /Filter /{filter} /Length {data.Length} >>\nstream\n"));
        file.Write(data);
        Write("\nendstream\nendobj\n");

        var xref = file.Position;
        Write("xref\n0 5\n0000000000 65535 f \n");

        foreach (var offset in offsets)
        {
            Write(string.Create(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n"));
        }

        Write(string.Create(CultureInfo.InvariantCulture, $"trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        return file.ToArray();
    }
}
