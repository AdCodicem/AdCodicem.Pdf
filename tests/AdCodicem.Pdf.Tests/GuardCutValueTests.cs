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

    [Theory]
    [InlineData("Prev")]
    [InlineData("XRefStm")]
    public void A_section_offset_a_guard_cut_ends_the_chain_as_the_guard_s(string key)
    {
        // The newer section's /Prev, object 9, or its /XRefStm, object 6, is one that section places and MaxObjectLength cuts
        // before its value: the section it names was said to be missing, and the index rebuilt. The chain ends there for the
        // guard, which is reached once the chain is read — thrown at opening when the caller asks for it (#263).
        var file = key == "Prev"
            ? ChainFiles.UpdateNamingItsPrevious("9 0 R", padding: 3000)
            : ChainFiles.HybridNamingItsStream("6 0 R", padding: 3000);
        using var document = PdfDocument.Open(file, ObjectGuard);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        document.Reader.Structure.Sections.Should().ContainSingle();
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.XRefSectionNotFound).Should().BeFalse();

        var throwing = () => PdfDocument.Open(file, ObjectGuard with { ThrowOnLimit = true });
        throwing.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));
    }
}
