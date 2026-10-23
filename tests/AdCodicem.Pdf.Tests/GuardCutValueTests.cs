using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// A stream's <c>/Length</c>, <c>/Filter</c> or <c>/DecodeParms</c> that leads to an object one of the reader's guards cut,
/// or left out of the index: what the cut left of it — null, a token it parted, a whole value it may yet go on past — is
/// the guard's, which reported it, and not a value the file gave (#224, #263).
/// </summary>
public class GuardCutValueTests
{
    private const string Page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << >> >>";

    private static readonly PdfReaderOptions ObjectGuard = new() { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } };

    [Theory]
    [InlineData("before the value", "0 0 m")]
    [InlineData("after the value", "0 0 m")]
    [InlineData("after a wrong value", "0 0 m")]
    [InlineData("inside the value", "0 0 m 10 10 l S")]
    public void A_length_a_guard_cut_is_not_judged_and_the_data_is_found_by_its_endstream(string cut, string data)
    {
        // Object 20, the /Length, runs past MaxObjectLength: before its value, which read as null; after it, or after a
        // wrong one; or inside "15", which read as 1. Each was reported as the file's fault but the one after the right
        // value — "holds null", "declared 4 bytes", "declared 1 bytes" (#224). The data is found by its endstream, and only
        // the guard is reported.
        var twenty = cut switch
        {
            "before the value" => new string(' ', 3000) + "5",
            "after the value" => "5" + new string(' ', 3000),
            "after a wrong value" => "4" + new string(' ', 3000),
            _ => new string(' ', 1024 - "20 0 obj\n".Length - 1) + "15" + new string(' ', 100),
        };
        var file = ContentLengthIn(twenty, data);
        using var document = PdfDocument.Open(file, ObjectGuard);

        var stream = document.GetObject(new PdfObjectId(4)).AsStream().Required();

        stream.GetRawBytes().ToArray().Should().Equal(Encoding.ASCII.GetBytes(data));
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        document.Reader.TryGetStreamLengthFault(4, out _).Should().BeFalse();
        document.Reader.IsCutAtLimit(20).Should().BeTrue();

        // Read whole, a wrong value is the file's.
        using var control = PdfDocument.Open(file);
        control.GetObject(new PdfObjectId(4)).AsStream().Required().GetRawBytes().ToArray().Should().Equal(Encoding.ASCII.GetBytes(data));
        control.Diagnostics.Select(entry => entry.Message).Should().Equal(
            cut == "after a wrong value" ? ["The stream declared 4 bytes but ended after 5."] : []);
    }

    [Fact]
    public void A_length_a_guard_cut_leaves_a_missing_endstream_the_file_s_fault()
    {
        // The stream lost its endstream: its own endobj ends its data, which is the file's to answer for, whatever its
        // length — the guard cut that, and is not judged.
        var file = ContentLengthIn(new string(' ', 3000) + "5", "0 0 m", endStream: false);
        using var document = PdfDocument.Open(file, ObjectGuard);

        document.GetObject(new PdfObjectId(4)).AsStream().Required().GetRawBytes().ToArray().Should().Equal("0 0 m"u8.ToArray());
        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.StreamTruncated).Which;
        report.Message.Should().StartWith(
            "The stream's /Length names object 20 0, which one of the reader's limits cut, and no endstream follows its data");
        report.Message.Should().EndWith("its object's endobj ends it, after 5 bytes.");
    }

    [Fact]
    public void A_length_an_object_stream_s_guard_left_unread_is_not_judged()
    {
        // Object 20, the /Length, is a member of object stream 11 past what MaxDecodedStreamLength lets it decode to: it
        // read as null, and was reported as one that could not be read (#263). It is the guard's.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"), 0),
            [3] = (1, file.Object(3, Page), 0),
            [4] = (1, file.Object(4, "<< /Length 20 0 R >>\nstream\n0 0 m\nendstream"), 0),
            [11] = (1, FlateObjectStream(file, 11, [(21, "(" + new string('x', 300) + ")"), (20, "5")]), 0),
            [21] = (2, 11, 0),
            [20] = (2, 11, 1),
        };
        file.CrossReferenceStream(12, 22, rows, "/Root 1 0 R");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 100 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        document.GetObject(new PdfObjectId(4)).AsStream().Required().GetRawBytes().ToArray().Should().Equal("0 0 m"u8.ToArray());
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
        document.Reader.TryGetStreamLengthFault(4, out _).Should().BeFalse();
    }

    [Fact]
    public void A_length_whose_reference_leads_to_an_object_a_guard_left_out_of_the_index_is_not_judged()
    {
        // Object 20, the /Length, holds a reference to object 21, which only an older section places, which
        // MaxXRefSectionCount leaves unread — the newer table numbers no row 21: 21 is unknown, and 20 led to null,
        // reported as no length.
        var file = new HandWrittenFile();
        var older = new Dictionary<int, long> { [21] = file.Object(21, "5") };
        var first = file.Table(22, older, "/Size 22");
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [4] = file.Object(4, "<< /Length 20 0 R >>\nstream\n0 0 m\nendstream"),
            [20] = file.Object(20, "21 0 R"),
        };
        file.Table(21, rows, string.Create(CultureInfo.InvariantCulture, $"/Size 22 /Root 1 0 R /Prev {first}"));
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 1 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        document.GetObject(new PdfObjectId(4)).AsStream().Required().GetRawBytes().ToArray().Should().Equal("0 0 m"u8.ToArray());
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitXRefSectionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_deferred_length_a_guard_cut_is_not_judged_once_the_chain_is_read(bool inside)
    {
        // The cross-reference stream's /Length, object 5, which the newer table places, runs past MaxObjectLength before its
        // value, or inside "56": read once the chain is, it was said to hold null, or to declare 5 bytes (#224).
        var five = inside ? new string(' ', 1024 - "5 0 obj\n".Length - 1) + "56" + new string(' ', 100) : new string(' ', 3000) + "56";
        var file = ChainFiles.UnderAnUpdate("/Type /XRef /Size 8 /W [1 4 2] /Length 5 0 R", five, compressed: false, predicted: false, placed: true);
        using var document = PdfDocument.Open(file, ObjectGuard);

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        document.WasRepaired.Should().BeFalse();
        document.Reader.Structure.Sections.Should().OnlyContain(section => section.State == IO.XRef.XRefSectionState.Read);
    }

    /// <summary>
    /// A page whose content stream, object 4, holds <paramref name="data"/>, its <c>/Length</c> object 20, which holds
    /// <paramref name="twenty"/>; with no <c>endstream</c> unless <paramref name="endStream"/>.
    /// </summary>
    private static byte[] ContentLengthIn(string twenty, string data, bool endStream = true)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [4] = file.Object(4, "<< /Length 20 0 R >>\nstream\n" + data + (endStream ? "\nendstream" : string.Empty)),
            [20] = file.Object(20, twenty),
        };
        file.Table(21, rows, "/Size 21 /Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// Writes object stream <paramref name="number"/>, holding <paramref name="members"/> compressed with Flate, or with the
    /// filter <paramref name="filter"/> names, its dictionary holding <paramref name="entries"/> before its own.
    /// </summary>
    private static long FlateObjectStream(
        HandWrittenFile file, int number, (int Number, string Body)[] members, string entries = "", string filter = "/FlateDecode")
    {
        var header = new StringBuilder();
        var contents = new StringBuilder();

        foreach (var (member, body) in members)
        {
            header.Append(CultureInfo.InvariantCulture, $"{member} {contents.Length} ");
            contents.Append(body).Append(' ');
        }

        var data = HostileInputTests.Compress(Encoding.ASCII.GetBytes(header.ToString() + contents));
        var first = entries.Contains("/First", StringComparison.Ordinal) ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" /First {header.Length}");
        return file.Object(
            number,
            string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Type /ObjStm /N {members.Length}{first} {entries}/Filter {filter} /Length {data.Length} >>\nstream\n{Encoding.Latin1.GetString(data)}\nendstream"));
    }
}
