using System.Globalization;
using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;
using AdCodicem.Pdf.Validation.Rules;

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
    public void A_deferred_length_a_guard_cut_leaves_a_stream_with_no_endstream_the_guard_s(bool chained)
    {
        // Stream 7 lost its endstream, and its /Length, object 5 — or object 6, which object 5 refers to —, which the newer
        // table places, runs past MaxObjectLength: the section was called malformed because no section placed the length,
        // and the index rebuilt. Without the guard the length is read, and the section with it: the section is the guard's.
        var padded = new string(' ', 3000) + "56";
        var file = ChainFiles.UnderAnUpdate(
            "/Type /XRef /Size 8 /W [1 4 2] /Length 5 0 R",
            chained ? "6 0 R" : padded,
            compressed: false,
            predicted: false,
            placed: true,
            placedToo: chained ? new Dictionary<int, string> { [6] = padded } : null);
        var text = Encoding.Latin1.GetString(file);
        var at = text.IndexOf("endstream", StringComparison.Ordinal);
        Encoding.ASCII.GetBytes("endstrXam").CopyTo(file, at);
        using var document = PdfDocument.Open(file, ObjectGuard);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        document.Reader.Structure.Sections[1].CutByLimit.Should().BeTrue();
        document.Reader.Structure.Sections[1].Fault.Should().EndWith(
            string.Create(CultureInfo.InvariantCulture, $"the reference {(chained ? 6 : 5)} 0 R, which one of the reader's limits cut"));
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().BeFalse();

        var throwing = () => PdfDocument.Open(file, ObjectGuard with { ThrowOnLimit = true });
        throwing.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));
    }

    [Theory]
    [InlineData("21 0 R")]
    [InlineData("20 0 R")]
    public void A_missing_endstream_is_the_file_s_fault_when_a_guard_left_the_length_out_of_the_index(string length)
    {
        // Object 4's /Length leads to object 21, which only an older section places, which MaxXRefSectionCount leaves unread:
        // the length is unknown, and not judged, but the endstream the stream lacks was not reported either (#224).
        var file = new HandWrittenFile();
        var older = new Dictionary<int, long> { [21] = file.Object(21, "5") };
        var first = file.Table(22, older, "/Size 22");
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [4] = file.Object(4, "<< /Length " + length + " >>\nstream\n0 0 m"),
            [20] = file.Object(20, "21 0 R"),
        };
        file.Table(21, rows, string.Create(CultureInfo.InvariantCulture, $"/Size 22 /Root 1 0 R /Prev {first}"));
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 1 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        document.GetObject(new PdfObjectId(4)).AsStream().Required().GetRawBytes().ToArray().Should().Equal("0 0 m"u8.ToArray());
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.StreamTruncated).Which.Message.Should().StartWith(
            "The stream's /Length names object 21 0, which one of the reader's limits left out of the index, and no endstream follows its data");
        document.Reader.TryGetStreamLengthFault(4, out var fault).Should().BeTrue();
        fault.Form.Should().Be(StreamLengthForm.ObjectUnindexed);
        fault.Reference.Should().Be(new PdfObjectId(21));
    }

    [Theory]
    [InlineData("cut", "endstream")]
    [InlineData("left out", "endstream")]
    [InlineData("cut", "inner stream")]
    [InlineData("left out", "inner stream")]
    [InlineData("cut", "none")]
    [InlineData("left out", "none")]
    public void Data_a_header_stops_where_a_guard_hid_the_length_is_the_guard_s(string guard, string end)
    {
        // Object 4 embeds a PDF file, whose data spells "1 0 obj" — and holds a stream of its own, whose endstream comes
        // after —, and its /Length, object 10, is one MaxObjectLength cuts or MaxIndexEntries leaves out of the index. The
        // search for its endstream stops at the header the data spells: the file was said to lack the endstream it holds,
        // or the data was taken to the embedded stream's. What decides is the length the guard hid: the data up to the
        // header is the guard's, cut by it, the object's endobj unjudged, and nothing said of the file, whatever follows
        // (#224).
        var embedded = "%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\n" +
            (end == "inner stream" ? "2 0 obj\n<< /Length 5 >>\nstream\nhello\nendstream\nendobj\n" : string.Empty) +
            "trailer\n<< /Root 1 0 R >>\n%%EOF";
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
            [4] = file.Object(4, "<< /Type /EmbeddedFile /Length 10 0 R >>\nstream\n" + embedded + (end == "none" ? string.Empty : "\nendstream")),
            [6] = file.Object(6, "(six)"),
            [10] = file.Object(10, (guard == "cut" ? new string(' ', 2000) : string.Empty) + embedded.Length.ToString(CultureInfo.InvariantCulture)),
        };
        file.Table(11, rows, "/Size 11 /Root 1 0 R");
        var limits = guard == "cut"
            ? PdfReaderLimits.Default with { MaxObjectLength = 1024 }
            : PdfReaderLimits.Default with { MaxIndexEntries = 8 };
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = limits });

        var stream = document.GetObject(new PdfObjectId(4)).AsStream().Required();

        stream.GetRawBytes().ToArray().Should().Equal("%PDF-1.4\n"u8.ToArray());
        stream.Data.CutByGuard.Should().BeTrue();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(
            guard == "cut" ? PdfDiagnosticCodes.LimitObject : PdfDiagnosticCodes.LimitIndexEntries);
        document.Reader.TryGetStreamLengthFault(4, out _).Should().BeFalse();
        document.Reader.IsEndObjMissing(4, out _).Should().BeFalse();
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.ObjectEndObjMissing).Should().BeFalse();
    }

    [Theory]
    [InlineData("cut")]
    [InlineData("left out")]
    public void A_stream_a_guard_hid_the_length_of_ends_at_its_own_endobj_before_a_header(string guard)
    {
        // Object 4, whose /Length a guard hid, lost its endstream; its own endobj, then an object no row places, a stream
        // with an endstream, follow: the header that object starts with is no header the data spells, and the endobj before
        // it ends the data, the missing endstream the file's (#224).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
            [4] = file.Object(4, "<< /Length 10 0 R >>\nstream\nABC"),
        };
        file.Object(30, "<< /Length 3 >>\nstream\nxyz\nendstream");
        rows[6] = file.Object(6, "(six)");
        rows[10] = file.Object(10, (guard == "cut" ? new string(' ', 2000) : string.Empty) + "3");
        file.Table(11, rows, "/Size 11 /Root 1 0 R");
        var limits = guard == "cut"
            ? PdfReaderLimits.Default with { MaxObjectLength = 1024 }
            : PdfReaderLimits.Default with { MaxIndexEntries = 8 };
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = limits });

        var stream = document.GetObject(new PdfObjectId(4)).AsStream().Required();

        stream.GetRawBytes().ToArray().Should().Equal("ABC"u8.ToArray());
        stream.Data.CutByGuard.Should().BeFalse();
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.StreamTruncated);
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

    /// <summary>The entries of object stream 10 that lead to object 20, and what object 20 holds.</summary>
    private static readonly (string Entries, string Twenty)[] Values =
    [
        ("/Filter 20 0 R", "/FlateDecode"),
        ("/Filter [20 0 R]", "/FlateDecode"),
        ("/Filter /FlateDecode /DecodeParms 20 0 R", "<< /Predictor 1 >>"),
        ("/Filter /FlateDecode /DecodeParms [20 0 R]", "<< /Predictor 1 >>"),
        ("/Filter /FlateDecode /DecodeParms << /Predictor 20 0 R >>", "1"),
    ];

    public static TheoryData<string, string, string> Cuts
    {
        get
        {
            var data = new TheoryData<string, string, string>();

            foreach (var (entries, twenty) in Values)
            {
                foreach (var cut in new[] { "before the value", "inside the value", "member past the data", "left out of the index" })
                {
                    data.Add(entries, twenty, cut);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cuts))]
    public void An_object_stream_whose_filter_values_a_guard_cut_is_hidden_and_its_members_taken_as_read(string entries, string twenty, string cut)
    {
        // The catalog is packed in object stream 10, whose /Filter, /DecodeParms or a value of its parameters leads to object
        // 20, which a guard cut — before its value, or inside it: "/Flate" of "/FlateDecode" —, left past the data of an
        // object stream it decoded, or out of the index. The stream decoded as no filter, or as "/Flate", unsupported, and
        // its header read as stray bytes: the catalog was looked for elsewhere, and a sound file judged broken (#263). The
        // filters end at the guard's value, the stream is left encoded and hidden, and its members are the guard's.
        var (file, options) = PackedCatalog(entries, twenty, cut);
        using var document = PdfDocument.Open(file, options);

        document.Reader.Structure.RootCutByGuard.Should().BeTrue();
        document.Diagnostics.Should().NotContain(entry =>
            entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable || entry.Code == PdfDiagnosticCodes.FilterUnsupported ||
            entry.Code == PdfDiagnosticCodes.TrailerRootRecovered);
        var report = new PdfValidator().Validate(document);
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse();
        report.Contains(PdfValidationRuleIds.XRefObjectStreamBroken).Should().BeFalse();
        report.Contains(PdfValidationRuleIds.XRefCheckedInPart).Should().BeTrue();

        using var control = PdfDocument.Open(file);
        control.Catalog.Should().NotBeNull();
        control.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void A_content_stream_whose_filter_a_guard_cut_comes_back_as_the_filters_before_it_decode_it()
    {
        // The page's content stream decodes through /ASCIIHexDecode, then object 20, which MaxObjectLength cuts before
        // "/ASCIIHexDecode": its data comes back as the first filter decodes it, as past MaxFilterChainLength, and nothing is
        // said but the guard — once, it decoded as if the chain ended there, in silence (#263).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [4] = file.Object(4, "<< /Filter [/ASCIIHexDecode 20 0 R] /Length 23 >>\nstream\n333032303330323036443E>\nendstream"),
            [20] = file.Object(20, new string(' ', 3000) + "/ASCIIHexDecode"),
        };
        file.Table(21, rows, "/Size 21 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes, ObjectGuard);

        var stream = document.GetObject(new PdfObjectId(4)).AsStream().Required();

        Encoding.ASCII.GetString(stream.Decode().Span).Should().Be("302030206D>");
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);

        using var control = PdfDocument.Open(file.Bytes);
        Encoding.ASCII.GetString(control.GetObject(new PdfObjectId(4)).AsStream().Required().Decode().Span).Should().Be("0 0 m");
        control.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void A_guard_a_filter_value_reaches_as_it_loads_is_not_the_stream_s_own_cut()
    {
        // Object stream 10's /Filter is object 20, read whole from object stream 11, whose own decoding reaches
        // MaxDecodedStreamLength on a later member; 10's /First lies past its data. The guard 20's load reached was taken for
        // 10's: its fault went unreported, and its catalog was taken as cut, though the validator, reading 20 from the cache,
        // judged it broken (#263). The guard is 11's: 10's fault is the file's, reported by both.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"), 0),
            [3] = (1, file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>"), 0),
            [11] = (1, FlateObjectStream(file, 11, [(20, "/FlateDecode"), (21, "(" + new string('x', 300) + ")")]), 0),
            [20] = (2, 11, 0),
            [21] = (2, 11, 1),
            [10] = (1, FlateObjectStream(file, 10, [(1, "<< /Type /Catalog /Pages 2 0 R >>")], "/First 999 ", filter: "20 0 R"), 0),
            [1] = (2, 10, 0),
        };
        file.CrossReferenceStream(12, 22, rows, "/Root 1 0 R");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 100 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        document.Reader.Structure.RootCutByGuard.Should().BeFalse();
        document.Diagnostics.Should().Contain(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable);
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.XRefObjectStreamBroken).Should().BeTrue();
    }

    [Theory]
    [InlineData("Filter")]
    [InlineData("DecodeParms")]
    [InlineData("Columns")]
    [InlineData("W")]
    [InlineData("W element")]
    [InlineData("Index")]
    [InlineData("Index element")]
    [InlineData("Size")]
    public void A_cross_reference_stream_value_a_guard_cut_ends_the_chain_as_the_guard_s(string key)
    {
        // Stream 7's /Filter, /DecodeParms, /W or /Index is object 5, which the newer table places and MaxObjectLength cuts
        // after its value. The chain said no section placed it, rebuilt the index and judged the section malformed. The
        // section is the guard's: the chain ends there, nothing is rebuilt, and the guard is reached once the chain is read
        // — thrown at opening when the caller asks for it (#263).
        var file = ChainFiles.UnderAnUpdate(key, placed: true, padding: 3000);
        using var document = PdfDocument.Open(file, ObjectGuard);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        document.Reader.Structure.Sections[1].CutByLimit.Should().BeTrue();
        document.Reader.Structure.Sections[1].Fault.Should().EndWith("the reference 5 0 R, which one of the reader's limits cut");
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().BeFalse();

        var throwing = () => PdfDocument.Open(file, ObjectGuard with { ThrowOnLimit = true });
        throwing.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));
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
    /// The catalog, packed in Flate object stream 10, whose dictionary holds <paramref name="entries"/>, which lead to object
    /// 20, holding <paramref name="twenty"/>; and the reader's options under which a guard cuts object 20 as
    /// <paramref name="cut"/> says.
    /// </summary>
    private static (byte[] File, PdfReaderOptions Options) PackedCatalog(string entries, string twenty, string cut)
    {
        var file = new HandWrittenFile(version: "1.5");
        var older = -1L;

        if (cut == "left out of the index")
        {
            older = file.Table(23, new Dictionary<int, long> { [20] = file.Object(20, twenty) }, "/Size 23");
        }

        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"), 0),
            [3] = (1, file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>"), 0),
            [1] = (2, 10, 0),
        };
        var catalog = "<< /Type /Catalog /Pages 2 0 R >>";
        var data = HostileInputTests.Compress(Encoding.ASCII.GetBytes("1 0 " + catalog));
        rows[10] = (1, file.Object(
            10,
            string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First 4 {entries} /Length {data.Length} >>\nstream\n{Encoding.Latin1.GetString(data)}\nendstream")),
            0);
        PdfReaderLimits limits;

        switch (cut)
        {
            case "before the value":
                rows[20] = (1, file.Object(20, new string(' ', 3000) + twenty), 0);
                limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 };
                break;

            case "inside the value":
                rows[20] = (1, file.Object(20, new string(' ', 1024 - "20 0 obj\n".Length - (twenty.Length / 2)) + twenty + new string(' ', 100)), 0);
                limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 };
                break;

            case "member past the data":
                rows[11] = (1, FlateObjectStream(file, 11, [(21, "(" + new string('x', 300) + ")"), (20, twenty)]), 0);
                rows[21] = (2, 11, 0);
                rows[20] = (2, 11, 1);
                limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 100 };
                break;

            default:
                limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 1 };
                break;
        }

        // Left out of the index, object 20 is numbered by no row of the newest section, which writes a row for every number
        // below its /Size.
        var size = older < 0 ? 23 : 13;
        file.CrossReferenceStream(12, size, rows, "/Root 1 0 R" + (older < 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" /Prev {older}")));
        return (file.Bytes, new PdfReaderOptions { Limits = limits });
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

    [Fact]
    public void A_missing_endstream_names_the_object_a_guard_cut_at_the_end_of_the_length_s_chain_and_keeps_no_length()
    {
        // Object 4's /Length, object 20, holds a reference to object 21, which MaxObjectLength cuts inside "15": the report
        // of its missing endstream named object 20, read whole, and the fault kept the parted 1 as the declared length.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [4] = file.Object(4, "<< /Length 20 0 R >>\nstream\n0 0 m 10 10 l S"),
            [20] = file.Object(20, "21 0 R"),
            [21] = file.Object(21, new string(' ', 1024 - "21 0 obj\n".Length - 1) + "15" + new string(' ', 100)),
        };
        file.Table(22, rows, "/Size 22 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes, ObjectGuard);

        document.GetObject(new PdfObjectId(4)).AsStream().Required().GetRawBytes().ToArray().Should().Equal("0 0 m 10 10 l S"u8.ToArray());
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.StreamTruncated)
            .Which.Message.Should().StartWith("The stream's /Length names object 21 0, which one of the reader's limits cut");
        document.Reader.TryGetStreamLengthFault(4, out var fault).Should().BeTrue();
        fault.Form.Should().Be(StreamLengthForm.ObjectCut);
        fault.Reference.Should().Be(new PdfObjectId(21));
        fault.Declared.Should().BeNull();
    }

    [Fact]
    public void A_deferred_length_whose_reference_leads_to_an_object_a_guard_cut_is_not_judged_once_the_chain_is_read()
    {
        // The cross-reference stream's /Length, object 5, holds a reference to object 6, which the newer table places and
        // MaxObjectLength cuts: the chain read object 5 alone, judged the length as holding null, and never reached the guard
        // (#224).
        var file = ChainFiles.UnderAnUpdate(
            "/Type /XRef /Size 8 /W [1 4 2] /Length 5 0 R",
            "6 0 R",
            compressed: false,
            predicted: false,
            placed: true,
            placedToo: new Dictionary<int, string> { [6] = new string(' ', 3000) + "56" });
        using var document = PdfDocument.Open(file, ObjectGuard);

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        document.WasRepaired.Should().BeFalse();
        document.Reader.Structure.Sections.Should().OnlyContain(section => section.State == IO.XRef.XRefSectionState.Read);

        var throwing = () => PdfDocument.Open(file, ObjectGuard with { ThrowOnLimit = true });
        throwing.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));
    }

    [Fact]
    public void The_integer_a_guard_cut_a_deferred_length_after_is_where_rows_that_spell_an_endstream_are_taken_again_to()
    {
        // The update's stream indexes its /Length, object 4, and object 8 after rows that spell an endstream, and
        // MaxObjectLength cuts object 4 after its 35: the section was said to lack the rows past the endstream, and object 8
        // read as the first revision wrote it. The 35 the cut left is where to look for the endstream, which confirms it
        // (#224).
        var file = ChainFiles.UpdateWithIndirectLength(spellingEndStream: true, four: "{length}" + new string(' ', 3000));
        using var document = PdfDocument.Open(file, ObjectGuard);

        document.GetObject(new PdfObjectId(8)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("new");
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        document.WasRepaired.Should().BeFalse();
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void The_integer_a_guard_cut_a_length_a_newer_section_places_after_is_where_spelled_rows_are_taken_again_to(
        bool chained, int lengthOff)
    {
        // Stream 7's /Length leads to an object the newer table places — object 5 itself, or object 9 through object 4,
        // which only stream 7's own row places — and MaxObjectLength cuts it after its value. The value the cut left is where
        // to look for the endstream, as for an object the section's own row places: confirmed, the data is taken again to it
        // and every row read; otherwise the rows past the endstream they spell are the guard's (#224).
        var file = ChainFiles.UpdatePlacingAPaddedLength(chained, lengthOff: lengthOff, lengthFirst: true);
        using var document = PdfDocument.Open(file, ObjectGuard);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        document.Reader.Structure.Sections[1].CutByLimit.Should().Be(lengthOff != 0);
        document.Reader.Structure.Sections[1].Incomplete.Should().BeFalse();
        (document.GetObject(new PdfObjectId(3)).AsDictionary() is not null).Should().Be(lengthOff == 0);
    }

    [Fact]
    public void Rows_data_taken_again_to_an_integer_a_guard_parted_still_lacks_are_the_guard_s()
    {
        // MaxObjectLength parts stream 7's /Length, 210, after its 21, which the rows' second spelled endstream confirms: the
        // data taken again to it holds 3 rows of the 30 the section declares, and the 27 it lacks were the file's, the
        // section incomplete and the index rebuilt. The integer is only a place to look at, the length that decides the
        // guard's: what it leaves unread is the guard's (#224).
        var file = ChainFiles.UpdatePlacingAPartedLength();

        using (var whole = PdfDocument.Open(file))
        {
            whole.Diagnostics.Should().BeEmpty();
            whole.GetObject(new PdfObjectId(3)).AsDictionary().Should().NotBeNull();
        }

        using var document = PdfDocument.Open(file, ObjectGuard);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        document.Reader.Structure.Sections[1].CutByLimit.Should().BeTrue();
        document.Reader.Structure.Sections[1].Incomplete.Should().BeFalse();
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().BeFalse();
    }

    [Theory]
    [InlineData("cut before the value")]
    [InlineData("cut inside the value")]
    [InlineData("left out")]
    public void An_endstream_the_data_spells_where_a_guard_hid_the_length_ends_data_that_is_the_guard_s(string guard)
    {
        // The content stream's data spells endstream inside a string, which its object's endobj does not follow; its own
        // endstream and endobj come after the 40 bytes. Read whole, the length passes the spelled one. Where a guard cut the
        // /Length, or left it out of the index, the search stops there, and the object was said to lack its endobj: the
        // length that tells the two apart is the guard's, and so is the data up to it (#224).
        const string Data = "BT /F1 12 Tf 72 712 Td (endstream) Tj ET";
        var (twenty, options) = guard switch
        {
            "cut before the value" => (new string(' ', 2000) + "40", ObjectGuard),
            "cut inside the value" => (new string(' ', 1024 - "20 0 obj\n".Length - 1) + "40" + new string(' ', 100), ObjectGuard),
            _ => ("40", new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 20 } }),
        };
        var file = ContentLengthIn(twenty, Data);

        using (var whole = PdfDocument.Open(file))
        {
            whole.GetObject(new PdfObjectId(4)).AsStream().Required().GetRawBytes().ToArray().Should().Equal(Encoding.ASCII.GetBytes(Data));
            whole.Diagnostics.Should().BeEmpty();
        }

        using var document = PdfDocument.Open(file, options);
        var stream = document.GetObject(new PdfObjectId(4)).AsStream().Required();

        stream.GetRawBytes().ToArray().Should().Equal(Encoding.ASCII.GetBytes("BT /F1 12 Tf 72 712 Td ("));
        stream.Data.CutByGuard.Should().BeTrue();
        document.Reader.IsEndObjMissing(4, out _).Should().BeFalse();
        document.Reader.TryGetStreamLengthFault(4, out _).Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(
            guard == "left out" ? PdfDiagnosticCodes.LimitIndexEntries : PdfDiagnosticCodes.LimitObject);
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.ObjectEndObjMissing).Should().BeFalse();
    }

    [Theory]
    [InlineData("Rx", false)]
    [InlineData("RG", false)]
    [InlineData("R\n", true)]
    public void An_R_a_window_s_edge_reaches_makes_a_reference_only_where_the_file_ends_it_there(string tail, bool reference)
    {
        // Object 4, the stream's /Filter, holds "6 0 " and the tail after 100 spaces, and MaxObjectLength ends its window
        // right after the R; object 6 holds /ASCIIHexDecode. An R a regular byte goes on was taken whole: the cut object
        // held a reference the file never wrote, which the /Filter followed to object 6, past the guard's cut, and the data
        // was decoded. The edge cuts what may be a reference, as under a bound one byte smaller, and the data is left as it
        // is, the guard's (#224, #263).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
            [4] = file.Object(4, new string(' ', 100) + "6 0 " + tail),
            [6] = file.Object(6, "/ASCIIHexDecode"),
            [7] = file.Object(7, "<< /Length 7 /Filter 4 0 R >>\nstream\n414243>\nendstream"),
        };
        file.Table(8, rows, "/Size 8 /Root 1 0 R");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = "4 0 obj\n".Length + 105 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        var data = document.GetObject(new PdfObjectId(7)).AsStream().Required().Decode().ToArray();

        Encoding.ASCII.GetString(data).Should().Be(reference ? "ABC" : "414243>");
        document.Reader.IsCutAtLimit(4).Should().BeTrue();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
    }

    [Theory]
    [InlineData(29)]
    [InlineData(20)]
    public void A_filter_run_out_where_a_guard_s_cut_of_the_length_leaves_the_data_says_nothing_of_the_file(int free)
    {
        // As above, the rows compressed in a stored block. With 29 free rows the data is 284 bytes, which MaxObjectLength
        // parts after its 28, where the rows' second spelled endstream confirms it: the data is taken again to it; with 20,
        // 221, parted after its 22, which nothing confirms: the data stays at the first spelled endstream. Either way it ends
        // where the guard hides the length, and the Flate run out there was reported as the file's fault (#224).
        var file = ChainFiles.UpdatePlacingAPartedLength(free, compressed: true);

        using (var whole = PdfDocument.Open(file))
        {
            whole.Diagnostics.Should().BeEmpty();
        }

        using var document = PdfDocument.Open(file, ObjectGuard);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        document.Reader.Structure.Sections[1].CutByLimit.Should().BeTrue();
    }

    [Fact]
    public void Rows_data_taken_again_to_an_integer_a_guard_parted_in_the_own_row_s_object_still_lacks_are_the_guard_s()
    {
        // As above, but the stream's own row places object 5, whose 420 MaxObjectLength parts after its 42, which the rows'
        // second spelled endstream confirms: the 54 rows the data taken again lacks are the guard's, not the file's (#224).
        var file = ChainFiles.OwnRowPlacingAPaddedLength(throughReference: false);

        using (var whole = PdfDocument.Open(file))
        {
            whole.Diagnostics.Should().BeEmpty();
            whole.GetObject(new PdfObjectId(3)).AsDictionary().Should().NotBeNull();
        }

        using var document = PdfDocument.Open(file, ObjectGuard);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        document.Reader.Structure.Sections[0].CutByLimit.Should().BeTrue();
        document.Reader.Structure.Sections[0].Incomplete.Should().BeFalse();
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_whole_reference_a_guard_cut_after_leads_the_own_row_s_length_on_as_a_whole_read_follows_it(bool lackingRow)
    {
        // The stream's own row places object 5, which MaxObjectLength cuts after "6 0 R", whole; object 6, which a newer
        // section places, holds the length. The data is taken again to it, as read whole: the guard hid nothing the length
        // needs, and the rows the data lacks — one, where the /Index declares one more — are the file's, as read whole. They
        // went to the guard (#224).
        var file = ChainFiles.OwnRowPlacingAPaddedLength(throughReference: true, lackingRow);
        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, ObjectGuard);

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        whole.Diagnostics.Should().BeEmpty();

        foreach (var read in (ReadOnlySpan<PdfDocument>)[whole, document])
        {
            var stream = read.Reader.Structure.Sections[1];
            stream.CutByLimit.Should().BeFalse();
            stream.Incomplete.Should().Be(lackingRow);
            read.GetObject(new PdfObjectId(3)).AsDictionary().Should().NotBeNull();
            new PdfValidator().Validate(read).Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().Be(lackingRow);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void A_whole_reference_a_guard_cut_after_leads_a_chain_on_whichever_section_reads_it_first(bool bothNameIt, bool lackingRow)
    {
        // Object 5 holds "6 0 R", which MaxObjectLength cuts after; object 6, which the newer stream places, holds the length
        // the older stream's /Length gives through 5. With the newer stream's /Length naming 5 too, the chain reads 5 before
        // any section places 6, and what the cut left of it was kept as that read judged it: the older stream's data could
        // not be taken again, its rows past the spelled endstream went to the guard and the file was said to give a length
        // no endstream confirms; with a row lacking, the section was cut where it is the file's to lack (#224). The guard hides
        // nothing either length needs: each reads as read whole.
        var file = ChainFiles.UpdatesReachingALengthThroughACutReference(bothNameIt, lackingRow);
        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, ObjectGuard);

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        whole.Diagnostics.Should().BeEmpty();

        foreach (var read in (ReadOnlySpan<PdfDocument>)[whole, document])
        {
            var sections = read.Reader.Structure.Sections;
            sections.Should().HaveCount(3);
            sections.Should().AllSatisfy(section => section.CutByLimit.Should().BeFalse());
            sections[2].Incomplete.Should().Be(lackingRow);
            read.GetObject(new PdfObjectId(3)).AsDictionary().Should().NotBeNull();
            new PdfValidator().Validate(read).Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().Be(lackingRow);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_value_the_chain_reads_through_a_whole_reference_a_guard_cut_after_is_read_and_reaches_the_guard(bool throwOnLimit)
    {
        // Stream 7's /W names object 5, which MaxObjectLength cuts after "9 0 R", whole; object 9, which the newer table
        // places, holds [1 4 2]. The chain ended at object 5 for the guard, the section cut before its first row, though the
        // guard hid nothing the value needs. It reads through, as a load reads through it, and reaches the guard once the
        // chain is read: reported, or thrown at opening when the caller asks for it (#263).
        var file = ChainFiles.UnderAnUpdate(
            "/Type /XRef /Size 8 /W 5 0 R /Length {length}",
            "9 0 R" + new string(' ', 1100),
            compressed: false,
            predicted: false,
            placed: true,
            placedToo: new Dictionary<int, string> { [9] = "[1 4 2]" });

        if (throwOnLimit)
        {
            var opening = () => PdfDocument.Open(file, ObjectGuard with { ThrowOnLimit = true });
            opening.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));
            return;
        }

        using var document = PdfDocument.Open(file, ObjectGuard);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        document.Reader.Structure.Sections.Should().HaveCount(2).And.OnlyContain(section => !section.CutByLimit);
        document.GetObject(new PdfObjectId(3)).AsDictionary().Should().NotBeNull();
    }

    [Fact]
    public void Rows_past_an_endstream_they_spell_are_the_guard_s_when_it_cut_the_deferred_length_before_its_value()
    {
        // As above, object 4's value lying past the cut: how far the data runs is unknown, and the rows past the endstream are
        // the guard's. Object 8 read as the first revision wrote it, its section said to lack them (#224).
        var file = ChainFiles.UpdateWithIndirectLength(spellingEndStream: true, four: new string(' ', 3000) + "{length}");
        using var document = PdfDocument.Open(file, ObjectGuard);

        document.GetObject(new PdfObjectId(8)).Should().BeOfType<PdfNull>();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        document.WasRepaired.Should().BeFalse();
        document.Reader.Structure.Sections[0].CutByLimit.Should().BeTrue();
        document.Reader.Structure.Sections[0].Incomplete.Should().BeFalse();
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().BeFalse();
    }

    [Fact]
    public void Rows_past_a_spelled_endstream_are_the_file_s_when_the_length_s_chain_gives_a_wrong_length()
    {
        // As above, object 9 read whole, with no guard, and 1,000 more than the data's length: nothing confirms it, and the
        // rows past the endstream the rows spell are the file's to lack — the section incomplete, the index rebuilt (#220).
        var file = ChainFiles.UpdatePlacingAPaddedLength(chained: true, padding: 0, lengthOff: 1000);
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(3));

        var section = document.Reader.Structure.Sections[1];
        section.Incomplete.Should().BeTrue();
        section.CutByLimit.Should().BeFalse();
        document.Diagnostics.Should().NotContain(entry => entry.Code.StartsWith("limit.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(32)]
    public void A_length_whose_chain_ends_at_its_last_object_a_guard_cut_is_the_guard_s(int objects)
    {
        // Content stream 4's /Length names object 100, which names the next, the last, after 3,000 spaces, holding 4: the
        // parse resolves a /Length through 32 objects, one more than a reference, and the last it reads, cut, is the guard's
        // — the 31st no longer taken for the target unread, nor the 32nd left past it (#224).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
            [4] = file.Object(4, "<< /Length 100 0 R >>\nstream\n0 0 m\nendstream"),
        };
        var last = 100 + objects - 1;

        for (var number = 100; number <= last; number++)
        {
            rows[number] = file.Object(number, number < last ? Invariant($"{number + 1} 0 R") : new string(' ', 3000) + "4");
        }

        file.Table(last + 1, rows, Invariant($"/Size {last + 1} /Root 1 0 R"));

        using (var whole = PdfDocument.Open(file.Bytes))
        {
            whole.GetObject(new PdfObjectId(4)).AsStream().Required().GetRawBytes().Length.Should().Be(5);
            whole.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.StreamLengthInvalid);
        }

        using var document = PdfDocument.Open(file.Bytes, ObjectGuard);
        document.GetObject(new PdfObjectId(4)).AsStream().Required().GetRawBytes().Length.Should().Be(5);

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        document.Reader.TryGetStreamLengthFault(4, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(31, "cut")]
    [InlineData(32, "cut")]
    [InlineData(31, "left out of the index")]
    [InlineData(32, "left out of the index")]
    public void A_missing_endstream_names_the_last_object_of_a_length_s_chain_a_guard_cut_or_left_out_of_the_index(int objects, string guard)
    {
        // As above, content stream 4 having lost its endstream, and the last object of its /Length's chain cut after 3,000
        // spaces — or placed only by an older table, which MaxXRefSectionCount leaves unread: the fault and the report of the
        // missing endstream name that object, the 32nd as the 31st, and not the one before it, which no guard cut and the
        // index holds (#224).
        var file = new HandWrittenFile();
        var last = 100 + objects - 1;
        var unindexed = guard == "left out of the index";
        var rows = new Dictionary<int, long>();
        var trailer = Invariant($"/Size {last + 1} /Root 1 0 R");

        if (unindexed)
        {
            var older = file.Table(last + 1, new Dictionary<int, long> { [last] = file.Object(last, "5") }, Invariant($"/Size {last + 1}"));
            trailer += Invariant($" /Prev {older}");
        }
        else
        {
            rows[last] = file.Object(last, new string(' ', 3000) + "5");
        }

        rows[1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        rows[2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        rows[4] = file.Object(4, "<< /Length 100 0 R >>\nstream\n0 0 m");

        for (var number = 100; number < last; number++)
        {
            rows[number] = file.Object(number, Invariant($"{number + 1} 0 R"));
        }

        // The newer table numbers no row for the last object when only the older one places it.
        file.Table(unindexed ? last : last + 1, rows, trailer);
        var options = unindexed ? new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 1 } } : ObjectGuard;
        using var document = PdfDocument.Open(file.Bytes, options);

        document.GetObject(new PdfObjectId(4)).AsStream().Required().GetRawBytes().ToArray().Should().Equal("0 0 m"u8.ToArray());
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.StreamTruncated).Which.Message.Should().StartWith(
            Invariant($"The stream's /Length names object {last} 0, which one of the reader's limits {guard}, and no endstream follows its data"));
        document.Reader.TryGetStreamLengthFault(4, out var fault).Should().BeTrue();
        fault.Form.Should().Be(unindexed ? StreamLengthForm.ObjectUnindexed : StreamLengthForm.ObjectCut);
        fault.Reference.Should().Be(new PdfObjectId(last));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(32)]
    public void A_cross_reference_stream_s_length_whose_chain_ends_at_its_last_object_a_guard_cut_waits_for_the_chain(int objects)
    {
        // Stream 7's /Length names object 5, which the newer table places, and which names object 100, the next, and so on;
        // the last, after 3,000 spaces, holds the length. The chain read follows as many objects as the parse resolves a
        // /Length through, so the cut last one makes it wait for the chain, which then leaves it to the guard (#220, #224).
        var placed = new Dictionary<int, string>();
        var last = 100 + objects - 2;

        for (var number = 100; number <= last; number++)
        {
            placed[number] = number < last ? Invariant($"{number + 1} 0 R") : new string(' ', 3000) + "56";
        }

        var file = ChainFiles.UnderAnUpdate(
            "/Type /XRef /Size 8 /W [1 4 2] /Length 5 0 R", "100 0 R", compressed: false, predicted: false, placed: true, placedToo: placed);

        using (var whole = PdfDocument.Open(file))
        {
            whole.Diagnostics.Should().BeEmpty();
        }

        using var document = PdfDocument.Open(file, ObjectGuard);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        document.GetObject(new PdfObjectId(3)).AsDictionary().Should().NotBeNull();

        var throwing = () => PdfDocument.Open(file, ObjectGuard with { ThrowOnLimit = true });
        throwing.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(32)]
    public void A_cross_reference_stream_with_no_endstream_whose_length_s_chain_ends_at_its_last_object_a_guard_cut_is_the_guard_s(int objects)
    {
        // As above, stream 7 having lost its endstream: the fault that says its data ends nowhere follows the /Length as far
        // as the parse resolves one, so the 32nd object, cut, leaves the section to the guard — not a chain of references
        // that reaches no value, which rebuilt the index (#220, #224).
        var placed = new Dictionary<int, string>();
        var last = 100 + objects - 2;

        for (var number = 100; number <= last; number++)
        {
            placed[number] = number < last ? Invariant($"{number + 1} 0 R") : new string(' ', 3000) + "56";
        }

        var file = ChainFiles.UnderAnUpdate(
            "/Type /XRef /Size 8 /W [1 4 2] /Length 5 0 R", "100 0 R", compressed: false, predicted: false, placed: true, placedToo: placed);
        var at = Encoding.Latin1.GetString(file).IndexOf("endstream", StringComparison.Ordinal);
        Encoding.ASCII.GetBytes("endstrXam").CopyTo(file, at);
        using var document = PdfDocument.Open(file, ObjectGuard);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        document.Reader.Structure.Sections[1].CutByLimit.Should().BeTrue();
        document.Reader.Structure.Sections[1].Fault.Should().EndWith(Invariant($"the reference {last} 0 R, which one of the reader's limits cut"));

        var throwing = () => PdfDocument.Open(file, ObjectGuard with { ThrowOnLimit = true });
        throwing.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));
    }

    [Theory]
    [InlineData(31, false)]
    [InlineData(32, false)]
    [InlineData(31, true)]
    [InlineData(32, true)]
    public void Rows_past_a_spelled_endstream_are_the_guard_s_when_the_length_s_chain_ends_at_its_last_object_a_guard_cut(int objects, bool ownRow)
    {
        // Stream 7's /Length names object 5, which the newer table places — or object 4, which only stream 7's own row
        // places —, which names object 100, the next, and so on; the last, after 3,000 spaces, holds the length, and the rows
        // spell an endstream. Whether the length leads to a cut follows it as far as the parse resolves one, from the object
        // the newer table places or from what the own row's object holds: the 32nd, cut, leaves the rows past the endstream
        // to the guard — the page, whose row is one, reads as null —, where a shorter walk gave them to the file, the section
        // incomplete and the index rebuilt (#220, #224).
        var file = ChainFiles.UpdatePlacingAPaddedLengthChain(objects, ownRow);

        using (var whole = PdfDocument.Open(file))
        {
            whole.GetObject(new PdfObjectId(3)).AsDictionary().Should().NotBeNull();
            whole.Diagnostics.Should().BeEmpty();
        }

        using var document = PdfDocument.Open(file, ObjectGuard);

        document.GetObject(new PdfObjectId(3)).Should().BeOfType<PdfNull>();
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        var section = document.Reader.Structure.Sections[1];
        section.CutByLimit.Should().BeTrue();
        section.Incomplete.Should().BeFalse();

        // Loaded, the stream ends at the endstream its rows spell, which its object's endobj does not follow: the data is the
        // guard's, and the object was said to lack its endobj.
        document.GetObject(new PdfObjectId(7)).AsStream().Required().Data.CutByGuard.Should().BeTrue();
        document.Reader.IsEndObjMissing(7, out _).Should().BeFalse();

        var throwing = () => PdfDocument.Open(file, ObjectGuard with { ThrowOnLimit = true });
        throwing.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(32)]
    public void A_prev_whose_chain_ends_at_its_last_object_a_guard_cut_ends_the_chain_as_the_guard_s(int objects)
    {
        // The newer table's /Prev names object 21, which names the next, the last, after 3,000 spaces, holding the older
        // table's offset: a /Prev reads through 32 objects, as a whole read does, and the last, cut, ends the chain for the
        // guard, which the 32nd did not: it was no offset, and the index was rebuilt (#263).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>"),
        };
        var older = file.Table(4, new Dictionary<int, long>(rows), "/Size 4 /Root 1 0 R");
        var newer = new Dictionary<int, long>();
        var last = 21 + objects - 1;

        for (var number = 21; number <= last; number++)
        {
            newer[number] = file.Object(
                number, number < last ? Invariant($"{number + 1} 0 R") : new string(' ', 3000) + older.ToString(CultureInfo.InvariantCulture));
        }

        var table = file.Position;
        var text = new StringBuilder(Invariant($"xref\n0 1\n0000000000 65535 f \n21 {objects}\n"));

        foreach (var number in newer.Keys)
        {
            text.Append(CultureInfo.InvariantCulture, $"{newer[number]:D10} 00000 n \n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {last + 1} /Root 1 0 R /Prev 21 0 R >>\nstartxref\n{table}\n%%EOF\n");
        file.Raw(text.ToString());

        using (var whole = PdfDocument.Open(file.Bytes))
        {
            whole.WasRepaired.Should().BeFalse();
            whole.Reader.Structure.Sections.Should().HaveCount(2);
        }

        using var document = PdfDocument.Open(file.Bytes, ObjectGuard);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        document.Reader.Structure.Sections.Should().ContainSingle().Which.ChainHiddenByGuard.Should().NotBeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Rows_past_a_spelled_endstream_are_the_guard_s_when_the_length_leads_to_an_object_a_newer_section_places_and_it_cut(
        bool chained, bool spelledFirst)
    {
        // Stream 7's /Length leads to an object the newer table places — object 5 itself, or object 9 through object 4,
        // which only stream 7's own row places — and MaxObjectLength cuts before its value: the rows past the endstream they
        // spell are the guard's, as when the cut object is the one the section's own row places (#224) — the data taken to it
        // holding no row whole too, the spelled rows first. Read whole, the file is clean.
        var file = ChainFiles.UpdatePlacingAPaddedLength(chained, spelledFirst: spelledFirst);

        using (var whole = PdfDocument.Open(file))
        {
            whole.GetObject(new PdfObjectId(3)).AsDictionary().Should().NotBeNull();
            whole.Diagnostics.Should().BeEmpty();
        }

        using var document = PdfDocument.Open(file, ObjectGuard);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        var section = document.Reader.Structure.Sections[1];
        section.CutByLimit.Should().BeTrue();
        section.Incomplete.Should().BeFalse();
        new PdfValidator().Validate(document).Findings.Should().NotContain(finding => finding.Severity == PdfValidationSeverity.Error);

        var throwing = () => PdfDocument.Open(file, ObjectGuard with { ThrowOnLimit = true });
        throwing.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));
    }

    [Theory]
    [InlineData("Prev")]
    [InlineData("XRefStm")]
    public void A_section_offset_a_guard_cut_ends_the_chain_as_the_guard_s(string key)
    {
        // The newer section's /Prev, object 9, or its /XRefStm, object 6, is one that section places and MaxObjectLength cuts
        // before its value: the section it names was said to be missing, and the index rebuilt. The chain ends there for the
        // guard, which is reached once the chain is read — thrown at opening when the caller asks for it (#263). The
        // validation says what it names was not checked — the older sections, or the stream of the hybrid's revision —, and
        // judges nothing the chain they end would hold.
        var reference = key == "Prev" ? "9 0 R" : "6 0 R";
        var file = key == "Prev"
            ? ChainFiles.UpdateNamingItsPrevious(reference, padding: 3000)
            : ChainFiles.HybridNamingItsStream(reference, padding: 3000);
        using var document = PdfDocument.Open(file, ObjectGuard);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        document.Reader.Structure.Sections.Should().ContainSingle();
        var report = new PdfValidator().Validate(document);
        report.Contains(PdfValidationRuleIds.XRefSectionNotFound).Should().BeFalse();
        report.Contains(PdfValidationRuleIds.FileSizeWrong).Should().BeFalse();
        var section = document.Reader.Structure.Sections[0].Offset;
        report.Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.XRefCheckedInPart).Select(finding => finding.Message).Should().Equal(
            key == "Prev"
                ? $"The cross-reference section at offset {section} names older sections, but its /Prev is the reference {reference}, which one of the reader's limits cut: they were not checked."
                : $"The cross-reference section at offset {section} names the cross-reference stream of its revision, but its /XRefStm is the reference {reference}, which one of the reader's limits cut: that stream, and any section older than it, were not checked.");

        var throwing = () => PdfDocument.Open(file, ObjectGuard with { ThrowOnLimit = true });
        throwing.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));
    }

    [Fact]
    public void A_cross_reference_stream_s_prev_a_guard_cut_ends_the_chain_as_the_guard_s()
    {
        // Cross-reference stream 10's /Prev, object 9, is one its own rows place, and MaxObjectLength cuts before its value:
        // the chain ends there for the guard, as at a table's /Prev, thrown at opening when the caller asks for it (#263).
        // Read whole, the stream's /Size 13 is one more than object 12, which only the older table indexes, and object 4 is
        // a page the tree does not list: the validation says the table was not checked, and judges neither on the stream.
        var file = ChainFiles.StreamUpdateHidingItsHighestNumber(padding: 3000);

        using (var whole = PdfDocument.Open(file))
        {
            whole.Reader.Structure.Sections.Should().HaveCount(2);
            var report = new PdfValidator().Validate(whole);
            report.Contains(PdfValidationRuleIds.FileSizeWrong).Should().BeFalse();
            report.Contains(PdfValidationRuleIds.PageTreePageOrphaned).Should().BeTrue();
        }

        using var document = PdfDocument.Open(file, ObjectGuard);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        var section = document.Reader.Structure.Sections.Should().ContainSingle().Which;
        section.Kind.Should().Be(XRefSectionKind.Stream);
        section.ChainHiddenByGuard.Should().NotBeNull();
        var guarded = new PdfValidator().Validate(document);
        guarded.Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.XRefCheckedInPart).Select(finding => finding.Message).Should().Equal(
            $"The cross-reference section at offset {section.Offset} names older sections, but its /Prev is the reference 9 0 R, which one of the reader's limits cut: they were not checked.");
        guarded.Contains(PdfValidationRuleIds.FileSizeWrong).Should().BeFalse();
        guarded.Contains(PdfValidationRuleIds.PageTreePageOrphaned).Should().BeFalse();

        var throwing = () => PdfDocument.Open(file, ObjectGuard with { ThrowOnLimit = true });
        throwing.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));
    }

    [Theory]
    [InlineData("9 0 R", true)]
    [InlineData("9 0 R", false)]
    [InlineData("12 0 R", false)]
    [InlineData("4.0", false)]
    public void A_hybrid_stream_s_own_prev_is_not_followed_the_chain_going_on_through_the_table_s(string prev, bool guarded)
    {
        // A hybrid file's stream gives a /Prev of its own, which the chain does not follow: it goes on through the table's,
        // as ISO 32000-1 (7.5.8.4) describes. Read anyway, one that leads to an object MaxObjectLength cuts ended the chain
        // for the guard, the older table lost, and one no section places was said to name a missing section (#263). The
        // rule on trailer values judges it as the file wrote it, a reference or a real where Table 17 asks for a direct
        // integer, without following it: following it reached the guard the reading never met.
        var file = ChainFiles.HybridWithStreamPrev(prev);
        using var document = PdfDocument.Open(file, guarded ? ObjectGuard : null);

        document.WasRepaired.Should().BeFalse();
        document.Reader.Structure.Sections.Should().HaveCount(3);
        document.GetObject(new PdfObjectId(3)).AsDictionary().Should().NotBeNull();
        document.Diagnostics.Should().BeEmpty();
        var report = new PdfValidator().Validate(document);
        report.Contains(PdfValidationRuleIds.XRefSectionNotFound).Should().BeFalse();
        report.Contains(PdfValidationRuleIds.XRefCheckedInPart).Should().BeFalse();

        using var judged = PdfDocument.Open(file, guarded ? ObjectGuard : null);
        var trailers = new ValidationProfile("trailers", 1, [new TrailerValueWrongRule()]);
        new PdfValidator(new PdfValidatorOptions { Profile = trailers }).Validate(judged).Findings.Select(finding => finding.Message)
            .Should().ContainSingle().Which.Should().Contain(prev == "4.0" ? "gives its /Prev as a real number" : $"writes its /Prev as the reference {prev}");
        judged.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Prev", "9 0 R", true)]
    [InlineData("XRefStm", "9 0 R", true)]
    [InlineData("Prev", "12 0 R", false)]
    [InlineData("XRefStm", "12 0 R", false)]
    public void A_table_an_xrefstm_names_in_a_stream_s_place_has_its_own_links_left_unread(string key, string value, bool guarded)
    {
        // The update's /XRefStm names a classic table, where ISO 32000-1 names a cross-reference stream (Table 19): its rows
        // are read, and the chain goes on through the update's /Prev, as past a stream. The table's own /Prev or /XRefStm,
        // which a stream's is not since #263, was read all the same: one that leads to an object MaxObjectLength cuts ended
        // the chain for the guard, the original table and the catalog lost, and one no section places was said to name a
        // missing section. The rule on trailer values judges it as the file wrote it.
        var file = ChainFiles.HybridNamingATable(key, value);
        using var document = PdfDocument.Open(file, guarded ? ObjectGuard : null);

        document.WasRepaired.Should().BeFalse();
        document.Reader.Structure.Sections.Should().HaveCount(3);
        document.Diagnostics.Should().BeEmpty();
        document.GetObject(new PdfObjectId(3)).AsDictionary().Should().NotBeNull();
        var report = new PdfValidator().Validate(document);
        report.Contains(PdfValidationRuleIds.XRefSectionNotFound).Should().BeFalse();
        report.Contains(PdfValidationRuleIds.XRefCheckedInPart).Should().BeFalse();

        using var judged = PdfDocument.Open(file, guarded ? ObjectGuard : null);
        var trailers = new ValidationProfile("trailers", 1, [new TrailerValueWrongRule()]);
        var findings = new PdfValidator(new PdfValidatorOptions { Profile = trailers }).Validate(judged).Findings.Select(finding => finding.Message);

        // A table's /XRefStm may be a reference (Table 19); its /Prev is direct (Table 15).
        findings.Should().HaveCount(key == "Prev" ? 1 : 0).And.OnlyContain(message => message.Contains($"writes its /Prev as the reference {value}", StringComparison.Ordinal));
        judged.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Prev")]
    [InlineData("XRefStm")]
    public void A_section_whose_prev_a_guard_cut_keeps_the_chain_it_hides_when_it_is_found_where_it_was_moved(string key)
    {
        // A third table's /Prev names the table whose /Prev, object 9, or whose /XRefStm, object 6, MaxObjectLength cuts, 3
        // bytes before it: the section is found where it lies, and what that value hides is said of it there, as where it is
        // named (#263).
        var two = key == "Prev"
            ? ChainFiles.UpdateNamingItsPrevious("9 0 R", padding: 3000)
            : ChainFiles.HybridNamingItsStream("6 0 R", padding: 3000);
        var text = Encoding.Latin1.GetString(two);
        var named = long.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + "startxref\n".Length)..].Split('\n')[0], CultureInfo.InvariantCulture);
        var five = two.Length;
        var appended = Invariant($"5 0 obj\n(five)\nendobj\n");
        var third = five + appended.Length;
        appended += Invariant(
            $"xref\n0 1\n0000000000 65535 f\r\n5 1\n{five:D10} 00000 n\r\ntrailer\n<< /Size 10 /Root 1 0 R /Prev {named - 3} >>\nstartxref\n{third}\n%%EOF\n");
        var file = two.Concat(Encoding.Latin1.GetBytes(appended)).ToArray();

        using var document = PdfDocument.Open(file, ObjectGuard);

        var moved = document.Reader.Structure.Sections.Should().ContainSingle(section => section.State == XRefSectionState.Relocated).Which;
        moved.ChainHiddenByGuard.Should().NotBeNull();
        new PdfValidator().Validate(document).Findings
            .Where(finding => finding.RuleId == PdfValidationRuleIds.XRefCheckedInPart)
            .Should().ContainSingle(finding => finding.Message.Contains(
                key == "Prev" ? "names older sections" : "names the cross-reference stream of its revision", StringComparison.Ordinal));
    }

    [Fact]
    public void A_chain_a_guard_ends_at_a_hidden_prev_leaves_unjudged_what_needs_the_whole_chain()
    {
        // Read whole, the update's /Size 13 is one more than object 12, which only the older table indexes, and object 4 is
        // a page the tree does not list. With the older table hidden by the guard, object 12 is unknown: /Size is not judged
        // against the 9 the update alone gives, nor is a page judged left out of the tree (#263).
        var file = ChainFiles.UpdateHidingItsHighestNumber(padding: 3000);

        using (var whole = PdfDocument.Open(file))
        {
            var report = new PdfValidator().Validate(whole);
            report.Contains(PdfValidationRuleIds.FileSizeWrong).Should().BeFalse();
            report.Contains(PdfValidationRuleIds.PageTreePageOrphaned).Should().BeTrue();
        }

        using var guarded = PdfDocument.Open(file, ObjectGuard);
        var findings = new PdfValidator().Validate(guarded).Findings.Select(finding => finding.RuleId).ToArray();

        findings.Should().Contain(PdfValidationRuleIds.XRefCheckedInPart);
        findings.Should().NotContain(PdfValidationRuleIds.FileSizeWrong);
        findings.Should().NotContain(PdfValidationRuleIds.PageTreePageOrphaned);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_parameter_a_guard_cut_before_an_image_filter_leaves_no_encoded_image(bool guarded)
    {
        // Object 4's Flate step, before its DCT, reads its parameters in object 20, which MaxObjectLength cuts: decoding ends
        // there and gives the Flate data back, which HasImageFilter took for the JPEG (#263).
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0xFF, 0xD9 };
        using var output = new MemoryStream();

        using (var zlib = new ZLibStream(output, CompressionLevel.NoCompression, leaveOpen: true))
        {
            zlib.Write(jpeg);
        }

        var flate = output.ToArray();
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
            [4] = file.Object(
                4,
                string.Create(CultureInfo.InvariantCulture, $"<< /Filter [/FlateDecode /DCTDecode] /DecodeParms [20 0 R null] /Length {flate.Length} >>\nstream\n") +
                    Encoding.Latin1.GetString(flate) + "\nendstream"),
            [20] = file.Object(20, new string(' ', 3000) + "<< /Predictor 1 >>"),
        };
        file.Table(21, rows, "/Size 21 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes, guarded ? ObjectGuard : null);
        var stream = document.GetObject(new PdfObjectId(4)).AsStream().Required();

        stream.HasImageFilter().Should().Be(!guarded);
        stream.Decode().ToArray().Should().Equal(guarded ? flate : jpeg);

        // Loading the parameter reaches the guard, as decoding would: a caller who asked for it is told so.
        if (guarded)
        {
            using var throwing = PdfDocument.Open(file.Bytes, ObjectGuard with { ThrowOnLimit = true });
            var image = throwing.GetObject(new PdfObjectId(4)).AsStream().Required();
            image.Invoking(value => value.HasImageFilter()).Should().Throw<PdfLimitExceededException>()
                .Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));
        }
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
