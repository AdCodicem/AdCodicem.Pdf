using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The reader parses through windows — 8 KB for an object, 64 KB for a cross-reference table — and grows a
/// window when what it holds runs past its edge. Wherever that edge falls, the result must be the object
/// the file contains, and the diagnostics must be the file's, never the window's.
/// </summary>
/// <remarks>
/// T21 and T23 in <c>docs/status.md</c>: a stream whose <c>endstream</c> lay just past the window was
/// reported truncated, and an object longer than the window earned a syntax error where the window cut it.
/// Both were read correctly on the second, larger attempt; what the first attempt had reported stayed.
/// </remarks>
public class WindowEdgeTests
{
    private const int ObjectNumber = 5;

    private static readonly int Window = PdfFileReader.InitialObjectWindow;

    /// <summary>Every construct the parser reads, as dictionary entries: each one gets cut somewhere.</summary>
    private const string Entries =
        "/Name /Value /Int 12345 /Neg -42 /Real -3.25 /Ref 7 0 R /Str (a\\(b\\) c\\\\ \\n) /Nest (x (y) z) " +
        "/Hex <48 65 6C6C6F> /True true /False false /Null null /Arr [1 (x) /y <00> [2 3] << /K 3 >> 4 0 R] " +
        "/Dict << /A 1 /B [false] /C << >> >>";

    /// <summary>The same constructs as array elements.</summary>
    private const string Elements =
        "/Value 12345 -42 -3.25 7 0 R (a\\(b\\) c\\\\ \\n) (x (y) z) <48 65 6C6C6F> true false null " +
        "[1 (x) /y <00> [2 3]] << /K 3 >> 4 0 R";

    /// <summary>
    /// Objects longer than the window, each written as what comes before a run of filler, the filler, and
    /// what comes after it. The filler's length slides what comes after it across the window's edge.
    /// </summary>
    public static TheoryData<string, string, char, string> Shapes => new()
    {
        { "a dictionary", "<< /Pad (", 'x', ") " + Entries + " >>" },
        { "an array", "[(", 'x', ") " + Elements + "]" },
        { "a stream with CR LF", "<< /Pad (", 'x', ") " + Entries + " /Length 11 >>\r\nstream\r\nHello world\r\nendstream" },
        { "a stream with LF", "<< /Pad (", 'x', ") /Length 11 >>\nstream\nHello world\nendstream" },
        { "a stream with no end-of-line before endstream", "<< /Pad (", 'x', ") /Length 11 >>\nstream\nHello worldendstream" },
        { "a stream whose only endstream a comment between a header's tokens holds", "<< /Pad (", 'x', ") /Length 9999 >>\nstream\nHello\nendobj\n12 % endstream\n0 obj" },
        { "a literal string", "(", 'x', " \\(escaped\\) (nested (twice)) \\\\ \\r\\n\\t \\101 end)" },
        { "a hexadecimal string", "<", '0', "48 65 6c 6C 6F 2>" },
        { "a name", "/", 'n', "#20name" },
        { "a reference", "  ", ' ', "7 0 R" },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void An_object_reads_the_same_wherever_the_window_edge_cuts_it(
        string shape, string before, char filler, string after)
    {
        var failures = new List<string>();
        var header = Header(ObjectNumber);

        // From the edge a few bytes before what follows the filler, to past "endobj".
        var fixedLength = header.Length + before.Length;

        for (var cut = -4; cut <= after.Length + "\nendobj\n".Length + 2; cut++)
        {
            var fillerLength = Window - fixedLength - cut;
            var body = before + new string(filler, fillerLength) + after;

            var failure = ReadAndCompare(body);
            if (failure is not null)
            {
                failures.Add($"cut {cut} bytes into what follows the filler: {failure}");
            }
        }

        failures.Should().BeEmpty($"{shape} must read the same wherever the window's edge falls in it");
    }

    [Fact]
    public void A_stream_reads_whole_wherever_the_window_edge_falls_near_its_end()
    {
        // The data itself is what slides: it ends anywhere from twenty bytes before the edge to past it,
        // so the end-of-line, the "endstream" keyword and "endobj" each meet the edge at every byte.
        var failures = new List<string>();

        foreach (var endOfLine in new[] { "\n", "\r\n", "\r", " ", string.Empty })
        {
            for (var shortOfEdge = -20; shortOfEdge <= 24; shortOfEdge++)
            {
                var body = StreamEndingAt(Window - shortOfEdge, endOfLine);
                var failure = ReadAndCompare(body);

                if (failure is not null)
                {
                    failures.Add($"data ending {shortOfEdge} bytes before the edge, {Describe(endOfLine)}: {failure}");
                }
            }
        }

        failures.Should().BeEmpty();
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    [InlineData("    ")]
    public void A_stream_ending_just_before_the_window_edge_is_confirmed_without_a_larger_window(string endOfLine)
    {
        // T21's shape: the data ends 0 to 10 bytes short of the window's edge — the hospital-bed guidance's
        // objects 604 and 2053 end 2 bytes short and right on it, before a CR LF — and its "endstream" lies
        // across or past the edge. The reader asks the file for a few bytes after the data instead of
        // parsing the object again through a window eight times larger, which the file, padded past that
        // size, would otherwise serve.
        var failures = new List<string>();

        for (var shortOfEdge = 0; shortOfEdge <= 10; shortOfEdge++)
        {
            var bytes = new TestPdfBuilder()
                .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
                .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
                .WithObject(ObjectNumber, StreamEndingAt(Window - shortOfEdge, endOfLine))
                .WithObject(6, "(" + new string('p', 12 * Window) + ")")
                .BuildClassic(rootNumber: 1);

            using var source = new StrictCountingSource(bytes);
            using var document = PdfDocument.Open(source, options: null, ownsSource: false);
            var beforeLoading = source.BytesRead;

            var stream = document.GetObject(new PdfObjectId(ObjectNumber)).AsStream().Required();
            var read = source.BytesRead - beforeLoading;
            var length = stream.GetRawBytes().Length;

            if (read > Window + PdfObjectParser.EndObjLookahead || length != Window - shortOfEdge - DataStart(Window - shortOfEdge) ||
                document.Diagnostics.Count > 0)
            {
                failures.Add($"{shortOfEdge} bytes short: read {read} bytes, data of {length}, {document.Diagnostics.Count} diagnostics");
            }
        }

        failures.Should().BeEmpty($"{Describe(endOfLine)} must not cost a larger window");
    }

    [Theory]
    [InlineData("\n", true)]
    [InlineData("\r\n", true)]
    [InlineData("\r", true)]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    public void A_stream_reads_the_same_when_a_guard_s_window_ends_on_its_keyword(string endOfLine, bool ended)
    {
        // MaxObjectLength ends the window right after "stream", or after its carriage return: the dictionary is whole, and
        // the end-of-line, as the data, counts toward no window. The data starts past it, as under a window one byte
        // larger — never on it, a byte early, nor at the declared length past the cut with no search (#174).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
            [4] = file.Object(
                4,
                $"<< /Pad ({new string('x', 60)}) /Length {(ended ? 40 : 5000)} >>\nstream{endOfLine}" + new string('a', 40) +
                (ended ? "\nendstream" : string.Empty)),
            [6] = file.Object(6, "(six)"),
        };
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        var bytes = file.Bytes;
        var header = PdfTemplate.OffsetOf(bytes, "4 0 obj");
        var keywordEnd = PdfTemplate.OffsetOf(bytes, "stream" + endOfLine) + "stream".Length;
        var window = (int)(keywordEnd - header) + (endOfLine == "\r\n" ? 1 : 0);

        using var whole = PdfDocument.Open(bytes);
        using var guarded = PdfDocument.Open(
            bytes,
            PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxObjectLength = window } });

        var expected = whole.GetObject(new PdfObjectId(4)).AsStream().Required().GetRawBytes().ToArray();
        guarded.GetObject(new PdfObjectId(4)).AsStream().Required().GetRawBytes().ToArray().Should().Equal(expected);
        expected.Should().Equal(Encoding.ASCII.GetBytes(new string('a', 40)));
        guarded.Diagnostics.Select(entry => entry.Message).Should().Equal(whole.Diagnostics.Select(entry => entry.Message));
    }

    [Theory]
    [InlineData("X\nhello\nendstream")]
    [InlineData("endstream")]
    public void A_keyword_longer_than_stream_reads_the_same_wherever_the_window_edge_cuts_it(string tail)
    {
        // "streamX" is no stream keyword: the dictionary before it stands alone, its object lacking its endobj. A window that
        // ends right after "stream" cannot say so, and the file is not asked where the data starts past a keyword a regular
        // byte may go on: a larger window says (#174).
        var failures = new List<string>();

        for (var shift = -2; shift <= 2; shift++)
        {
            var head = "4 0 obj\n<< /Length 5 /Pad () >>\nstream";
            var pad = Window + shift - head.Length;
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
                [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
                [4] = file.Object(4, $"<< /Length 5 /Pad ({new string('x', pad)}) >>\nstream" + tail),
                [6] = file.Object(6, "(six)"),
            };
            file.Table(7, rows, "/Size 7 /Root 1 0 R");
            using var document = PdfDocument.Open(file.Bytes);

            var value = document.GetObject(new PdfObjectId(4));

            if (value is not PdfDictionary || !document.Reader.IsEndObjMissing(4, out _))
            {
                failures.Add($"shift {shift}: {value.GetType().Name}, endobj missing {document.Reader.IsEndObjMissing(4, out _)}");
            }
        }

        failures.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_endobj_a_regular_byte_goes_on_is_missing_wherever_the_window_edge_cuts_it(bool stream)
    {
        // "endobjx" is no endobj: the object lacks it. One the window's edge reaches may be the start of a longer keyword,
        // and is not taken whole: a stream's, as the file read on after its endstream says, and any other value's, as a larger
        // window says (#174).
        var failures = new List<string>();

        for (var shift = -2; shift <= 2; shift++)
        {
            // The data's length that ends the keyword "4 0 obj\n" and the body leave at the edge, shifted.
            var body = Enumerable.Range(0, Window)
                .Select(data => stream
                    ? string.Create(CultureInfo.InvariantCulture, $"<< /Length {data} >>\nstream\n") + new string('a', data) + "\nendstream\nendobj"
                    : "(" + new string('a', data) + ")\nendobj")
                .First(text => "4 0 obj\n".Length + text.Length == Window + shift);

            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
                [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
                [4] = file.Object(4, body + "x"),
                [6] = file.Object(6, "(six)"),
            };
            file.Table(7, rows, "/Size 7 /Root 1 0 R");
            using var document = PdfDocument.Open(file.Bytes);

            document.GetObject(new PdfObjectId(4));

            if (!document.Reader.IsEndObjMissing(4, out _))
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"shift {shift}: endobj taken whole"));
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void An_endobj_the_window_s_edge_reaches_ends_the_object_where_a_source_cut_short_after_opening_gives_no_byte_more()
    {
        // Object 6's endobj ends right at the edge of the 8 KB window it is read through, and an "x" follows it: "endobjx" is
        // no endobj, and the object lacks it. Once the document is open, the file is cut short right where the endobj ends,
        // as another process truncates it: the source gives no byte past the edge, and the file says the token ends there,
        // as where the file ends. The object reads whole, and nothing is reported (#174).
        const string Head = "6 0 obj\n(";
        const string Tail = ")\nendobj";
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
            [6] = file.Position,
        };
        file.Raw(Head + new string('a', Window - Head.Length - Tail.Length) + Tail + "x\n");
        rows[7] = file.Object(7, "(seven)");
        file.Table(8, rows, "/Size 8 /Root 1 0 R");
        var bytes = file.Bytes;

        using var whole = PdfDocument.Open(bytes);
        using var source = new CutShortSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        source.CutAt((int)rows[6] + Window);

        whole.GetObject(new PdfObjectId(6));
        whole.Reader.IsEndObjMissing(6, out _).Should().BeTrue();
        document.GetObject(new PdfObjectId(6)).Should().BeOfType<PdfString>().Which.Length.Should().Be(Window - Head.Length - Tail.Length);
        document.Reader.IsEndObjMissing(6, out _).Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void The_file_answers_for_a_window_s_edge_outside_it_without_asking_its_source()
    {
        // The parser asks the file what follows a token, or a stream keyword, its window's edge reaches, which lies inside the
        // file (#174, #224). Asked of an offset outside it, before its start or at its end, the file answers without asking
        // a source that refuses any read outside its data: no byte there goes on a token, nor is an end-of-line there skipped
        // before a stream's data, which starts where the keyword ends.
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .BuildClassic(rootNumber: 1);
        using var document = PdfDocument.Open(new StrictCountingSource(bytes), options: null, ownsSource: true);
        var provider = (IPdfStreamDataProvider)document.Reader;

        provider.TokenEndsAt(-1).Should().BeTrue();
        provider.TokenEndsAt(bytes.Length).Should().BeTrue();
        provider.DataStart(-1).Should().Be(-1);
        provider.DataStart(bytes.Length).Should().Be(bytes.Length);
    }

    [Fact]
    public void A_cross_reference_stream_reads_whole_when_its_guard_s_window_ends_on_its_keyword()
    {
        // MaxTrailerLength ends the window right after the stream's keyword: its dictionary is whole, and its rows are read
        // past it, as under a window one byte larger — not a byte early, misaligned, which rebuilt the index and judged the
        // catalog missing (#174).
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .BuildWithXRefStream(rootNumber: 1);
        var dictionary = PdfTemplate.OffsetOf(bytes, "/Type /XRef");
        var header = Encoding.Latin1.GetString(bytes).LastIndexOf(" 0 obj", (int)dictionary, StringComparison.Ordinal) - 1;
        var keywordEnd = Encoding.Latin1.GetString(bytes).IndexOf("stream", (int)dictionary, StringComparison.Ordinal) + "stream".Length;

        using var document = PdfDocument.Open(
            bytes,
            PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxTrailerLength = keywordEnd - header } });

        document.Diagnostics.Should().BeEmpty();
        document.WasRepaired.Should().BeFalse();
        new PdfValidator().Validate(document).Findings.Should().BeEmpty();
    }

    [Fact]
    public void An_anomaly_inside_an_object_longer_than_the_window_is_reported_once()
    {
        // Before the fix, every attempt reported what it saw: the key that is not a name came out twice,
        // once from the window that was too small and once from the one that was large enough.
        var body = "<< 12 /A 1 /Pad (" + new string('x', 2 * Window) + ") >>";

        using var document = PdfDocument.Open(Document(body));
        var value = document.GetObject(new PdfObjectId(ObjectNumber)).AsDictionary().Required();

        value[PdfName.Get("Pad")].Should().BeOfType<PdfString>().Which.Length.Should().Be(2 * Window);
        document.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PdfDiagnosticCodes.SyntaxUnexpectedToken);
    }

    [Fact]
    public void An_object_the_file_really_cuts_short_is_still_reported_once()
    {
        // The window was never the problem here: the file ends inside the object. The last attempt, the
        // one that reaches the end of the file, reports it, where the dictionary the file ends inside opens; the
        // attempts before it do not.
        var marker = "/Cut 1 >>";
        var complete = Document("<< /Pad (" + new string('x', 3 * Window) + ") " + marker);
        var end = Encoding.Latin1.GetString(complete).IndexOf(marker, StringComparison.Ordinal) + "/Cut ".Length;

        using var document = PdfDocument.Open(complete.AsMemory(0, end));
        var value = document.GetObject(new PdfObjectId(ObjectNumber)).AsDictionary().Required();

        value[PdfName.Get("Pad")].Should().BeOfType<PdfString>().Which.Length.Should().Be(3 * Window);
        document.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.SyntaxTruncatedObject)
            .Should().ContainSingle()
            .Which.Position.Should().Be(Encoding.Latin1.GetString(complete).IndexOf("<< /Pad", StringComparison.Ordinal));
        document.Diagnostics.Where(d => d.Code.StartsWith("syntax.", StringComparison.Ordinal) ||
                                        d.Code.StartsWith("stream.", StringComparison.Ordinal))
            .Should().ContainSingle();
    }

    [Fact]
    public void What_a_nested_load_reports_survives_the_attempt_that_is_dropped()
    {
        // The stream's /Length is an indirect object the index misplaces by three bytes, so resolving it
        // is a repair. It is resolved during the first attempt — which is then dropped, because the window
        // ends between the CR and the LF after "stream" and cannot say where the data starts. The repair
        // happened, and must stay reported, once; the second attempt finds the length already resolved.
        const string Data = "Hello world";
        var prefix = "<< /Length 6 0 R /Pad (";
        var suffix = ") >>\r\nstream";
        var fillerLength = Window - Header(ObjectNumber).Length - prefix.Length - suffix.Length - 1;
        var body = prefix + new string('x', fillerLength) + suffix + "\r\n" + Data + "\r\nendstream";

        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(ObjectNumber, body)
            .WithObject(6, Data.Length.ToString(CultureInfo.InvariantCulture))
            .BuildClassic(rootNumber: 1, offsetError: 3);

        using var document = PdfDocument.Open(bytes);
        var stream = document.GetObject(new PdfObjectId(ObjectNumber)).AsStream().Required();

        Encoding.ASCII.GetString(stream.GetRawBytes().Span).Should().Be(Data);
        document.Diagnostics.Where(d => d.Message.StartsWith("Object 6 ", StringComparison.Ordinal))
            .Should().ContainSingle()
            .Which.Code.Should().Be(PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Diagnostics.Should().OnlyContain(d => d.Code == PdfDiagnosticCodes.XRefOffsetAdjusted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_classic_table_reads_whole_wherever_its_window_edge_falls_near_its_trailer(bool anomalous)
    {
        // The table's window is 64 KB. Its edge is slid over the end of the first subsection, the header
        // of the second, its rows, the "trailer" keyword and the dictionary, one byte at a time: a trailer
        // cut there lost its /Root, and a cut keyword or subsection header made the table unreadable. An
        // anomalous trailer — a key that is not a name — is reported exactly once, wherever the edge falls.
        var failures = new List<string>();
        var edges = new HashSet<int>();
        var expected = anomalous ? PdfDiagnosticCodes.SyntaxUnexpectedToken : string.Empty;

        for (var shift = 0; shift <= 220; shift++)
        {
            var file = LongTable(shift, anomalous);
            var cut = PdfFileReader.XRefWindow - (file.RegionStart - file.XRefOffset);

            if (cut < 0 || cut > file.RegionEnd - file.RegionStart)
            {
                continue;
            }

            edges.Add(cut);
            using var document = PdfDocument.Open(file.Bytes);

            if (document.WasRepaired || string.Join(";", document.Diagnostics.Select(d => d.Code)) != expected ||
                document.Trailer.GetInteger(PdfName.Size) != file.Size ||
                document.Catalog?.IsOfType(PdfName.Catalog) != true)
            {
                failures.Add($"edge {cut} bytes into the region: repaired {document.WasRepaired}, " +
                             $"{document.Diagnostics.Count} diagnostics, /Size {document.Trailer.GetInteger(PdfName.Size)}");
            }
        }

        // A sweep that never reached the region would pass on anything.
        edges.Should().HaveCount(LongTable(0, anomalous).RegionEnd - LongTable(0, anomalous).RegionStart + 1);
        failures.Should().BeEmpty();
    }

    [Theory]
    [InlineData(-7)]
    [InlineData(5)]
    public void A_cross_reference_stream_whose_length_is_wrong_is_read_whole_and_reported(int error)
    {
        // 2,000 rows of 7 bytes: data past the 8 KB object window, inside the 64 KB a cross-reference stream
        // is read through, so its /Length is checked against the data — a first version of this change,
        // parsing it from 8 KB, took the length on trust and dropped the rows it did not cover. The
        // catalog is in the last row.
        const int Size = 2000;
        var text = new StringBuilder("%PDF-1.7\n");
        var pages = text.Length;
        text.Append("1 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        var catalog = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"{Size - 1} 0 obj\n<< /Type /Catalog /Pages 1 0 R >>\nendobj\n");
        var xref = text.Length;

        var rows = new List<byte>(Size * 7);
        for (var number = 0; number < Size; number++)
        {
            rows.AddRange(number switch
            {
                1 => XRefRow(pages),
                Size - 1 => XRefRow(catalog),
                _ => [0, 0, 0, 0, 0, 0, 0],
            });
        }

        text.Append(CultureInfo.InvariantCulture, $"{Size} 0 obj\n<< /Type /XRef /Size {Size} /W [1 4 2] /Root {Size - 1} 0 R ")
            .Append(CultureInfo.InvariantCulture, $"/Length {rows.Count + error} >>\nstream\n")
            .Append(Encoding.Latin1.GetString(rows.ToArray()))
            .Append(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n");

        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text.ToString()));

        document.WasRepaired.Should().BeFalse();
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.StreamLengthInvalid);
    }

    [Fact]
    public void A_cross_reference_stream_after_more_white_space_than_the_object_window_is_read_from_the_index()
    {
        var text = new StringBuilder("%PDF-1.7\n");
        var catalog = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = text.Length;
        text.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        var xref = text.Length;
        text.Append(' ', Window + 800);

        byte[] rows = [0, 0, 0, 0, 0, 0xFF, 0xFF, .. XRefRow(catalog), .. XRefRow(pages), .. XRefRow(xref)];
        text.Append(CultureInfo.InvariantCulture, $"3 0 obj\n<< /Type /XRef /Size 4 /W [1 4 2] /Root 1 0 R /Length {rows.Length} >>\nstream\n")
            .Append(Encoding.Latin1.GetString(rows))
            .Append(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n");

        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text.ToString()));

        document.WasRepaired.Should().BeFalse();
        document.Trailer.GetInteger(PdfName.Size).Should().Be(4);
        document.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void An_object_that_lost_its_end_ends_at_the_next_header_wherever_the_window_edge_cuts_it()
    {
        // The header that ends an array which lost its ] and its endobj meets the edge at every byte, and a byte or two
        // before and after: the edge never makes the header an integer and a stray keyword, nor hides it (#230).
        var failures = new List<string>();
        const string Before = "[(";
        const string After = ") 1 2 3\n6 0 obj\n(six)";
        var header = Header(ObjectNumber);

        for (var shortOfEdge = -2; shortOfEdge <= 10; shortOfEdge++)
        {
            var filler = Window - header.Length - Before.Length - ") 1 2 3\n".Length - shortOfEdge;
            using var document = PdfDocument.Open(Document(Before + new string('x', filler) + After));

            var read = document.GetObject(new PdfObjectId(ObjectNumber));
            var reports = string.Join(" | ", document.Diagnostics.Select(entry => entry.Message));

            if (read is not PdfArray { Count: 4 } ||
                reports != "An object header ended the object inside an array, which was never closed." ||
                !document.Reader.IsEndObjMissing(ObjectNumber, out _))
            {
                failures.Add($"the header {shortOfEdge} bytes before the edge: {read}, {reports}");
            }
        }

        failures.Should().BeEmpty();
    }

    [Theory]
    [InlineData(-40)]
    [InlineData(-20)]
    [InlineData(-3)]
    [InlineData(0)]
    [InlineData(5)]
    public void What_follows_an_endstream_the_window_holds_is_read_wherever_the_window_edge_falls(int shift)
    {
        // An integer follows object 4's endstream in place of its endobj, starting shift bytes past the window's edge: where
        // the edge cut that integer, or the white space before it, the object's endobj was judged unseen, and its loss went
        // unreported. What follows the keyword is read on in the file, as past the buffer (#174).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var start = file.Position;
        rows[4] = start;
        const string Before = "4 0 obj\n<< /Length 0000 >>\nstream\n";
        const string After = "\nendstream ";
        var length = Window + shift - Before.Length - After.Length;
        file.Raw(Before.Replace("0000", length.ToString("D4", CultureInfo.InvariantCulture), StringComparison.Ordinal) +
            new string('a', length) + After + "7777777777\n");
        rows[6] = file.Object(6, "(six)");
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);

        document.GetObject(new PdfObjectId(4)).AsStream().Required().RawLength.Should().Be(length);
        document.Reader.IsEndObjMissing(4, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(-30)]
    [InlineData(-21)]
    [InlineData(-18)]
    [InlineData(-15)]
    [InlineData(-13)]
    [InlineData(-11)]
    [InlineData(-9)]
    [InlineData(-7)]
    [InlineData(-5)]
    [InlineData(-3)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void An_object_s_endobj_ends_data_with_no_endstream_wherever_the_reads_part_what_follows_it(int shift)
    {
        // Object 4 lost its endstream. Its endobj, a comment, and a header no index places, which stops the search, fall
        // across the edge of the window and of the first read of the file past it: the keyword parted between two reads, the
        // header started in the read before the one that shows it whole. The data ends at the endobj wherever they fall (#174).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        rows[4] = file.Position;
        const string Before = "4 0 obj\n<< /Length 3 >>\nstream\n";
        var length = Window + shift - Before.Length;
        file.Raw(Before + new string('a', length) + "\nendobj\n% c\n7 0 obj\n(stray)\nendobj\n");
        rows[6] = file.Object(6, "(six)");
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);

        document.GetObject(new PdfObjectId(4)).AsStream().Required().RawLength.Should().Be(length);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.StreamTruncated)
            .Which.Message.Should().EndWith(string.Create(CultureInfo.InvariantCulture, $"its object's endobj ends it, after {length} bytes."));
    }

    [Theory]
    [InlineData(-7)]
    [InlineData(-5)]
    [InlineData(-3)]
    [InlineData(-1)]
    public void An_endobj_that_ends_a_longer_token_ends_no_data_wherever_the_reads_part_it(int shift)
    {
        // As above, but the keyword ends the token "aendobj", parted between the window and the first read past it: no endobj
        // ends the data, and the declared length, which ends before the next object, is kept.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        rows[4] = file.Position;
        const string Before = "4 0 obj\n<< /Length 3 >>\nstream\n";
        var length = Window + shift - Before.Length;
        file.Raw(Before + new string('a', length) + "endobj\n% c\n7 0 obj\n(stray)\nendobj\n");
        rows[6] = file.Object(6, "(six)");
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);

        document.GetObject(new PdfObjectId(4)).AsStream().Required().RawLength.Should().Be(3);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.StreamLengthInvalid)
            .Which.Message.Should().EndWith("the declared length is kept.");
    }

    [Theory]
    [InlineData("\nendobj\n12 % endobj\n0 obj", 0, true)]
    [InlineData("\n12 % endobj\n0 obj", 1, false)]
    [InlineData("\nendobj\n12\n% endobj\n0 obj", 0, true)]
    [InlineData("\nendobj\n12 0 % endobj\nobj", 0, true)]
    [InlineData("\nendobj\n12\n% endobj\n% endobj x\n0\n% endobj\nobj", 0, true)]
    [InlineData("\nendobj\n1234567 % endobj\n00000 % endobj\nobj", 0, true)]
    [InlineData("\nendobj \n 12 \n % endobj \n 0 \n obj", 0, true)]
    [InlineData("\nendobj\n12 % endobjx\n0 obj", 0, true)]
    public void An_endobj_a_comment_between_a_header_s_tokens_holds_changes_nothing_wherever_the_reads_part_the_header(
        string header, int past, bool ended)
    {
        // Object 5 lost its endstream, and its data runs to a header no index places whose tokens a comment holding endobj
        // parts (#261). The window, the first read of the file past it, or a lower object guard ends inside that header,
        // after the comment's endobj and before the read that decides the header: the data ends at the object's own endobj
        // or at the header, its endobj judged present or missing, the same wherever the reads part the header.
        var windowEdge = Window - LostEndPrefix.Length;
        var firstReadEdge = windowEdge - PdfFileReader.EndStreamSearchOverlap + PdfFileReader.EndStreamSearchFirstRead(windowEdge);
        var splits = new List<(int Filler, int? Guard)>();

        foreach (var edge in new[] { windowEdge, firstReadEdge })
        {
            for (var filler = edge - header.Length - 2; filler <= edge + 2; filler++)
            {
                splits.Add((filler, null));
            }
        }

        // A guard below the 8 KB window parses the object through a smaller one, and moves both edges: the window's to where
        // the guard ends, and the first read's to three times as far into the data, less what the reads repeat.
        for (var guard = 6000 + LostEndPrefix.Length - 2; guard <= 6000 + LostEndPrefix.Length + header.Length + 2; guard++)
        {
            splits.Add((6000, guard));
        }

        for (var held = (18_000 + PdfFileReader.EndStreamSearchOverlap - 2) / 3; held <= (18_000 + PdfFileReader.EndStreamSearchOverlap + header.Length + 2) / 3; held++)
        {
            for (var shift = 0; shift < 3; shift++)
            {
                splits.Add((18_000 + shift, held + LostEndPrefix.Length));
            }
        }

        foreach (var (filler, guard) in splits)
        {
            var length = filler + past;
            var ending = HeaderEnding(header, filler, guard);

            ending.Length.Should().Be(length, "the filler is {0} bytes and the guard {1}", filler, guard);
            ending.Diagnostic.Should().Be(
                ended
                    ? $"{PdfDiagnosticCodes.StreamTruncated}: its object's endobj ends it, after {CountText.Bytes(length)}."
                    : $"{PdfDiagnosticCodes.StreamLengthInvalid}: the {CountText.Bytes(length)} up to that object are taken as its data.",
                "the filler is {0} bytes and the guard {1}",
                filler,
                guard);
            ending.EndObjMissing.Should().Be(!ended, "the filler is {0} bytes and the guard {1}", filler, guard);
            ending.RuleFires.Should().Be(!ended, "the filler is {0} bytes and the guard {1}", filler, guard);
        }
    }

    [Theory]
    [InlineData("\nendobj\n12 % endstream\n0 obj", 0, true)]
    [InlineData("\n12 % endstream\n0 obj", 1, false)]
    [InlineData("\nendobj\n12 0 % endstream\nobj", 0, true)]
    [InlineData("\nendobj\n12\n% endstream\n0 obj", 0, true)]
    [InlineData("\nendobj\n12 % endstream\n0 % endstream\nobj", 0, true)]
    [InlineData("\nendobj\n12 % c\n% endstream\n% endstream x\n0\n% endstream\nobj", 0, true)]
    [InlineData("\nendobj\n12 %endstream\r0\robj", 0, true)]
    [InlineData("\nendobj \n 12 \n % endstream \n 0 \n obj", 0, true)]
    [InlineData("\n(x\n12 % endstream\n0 obj", 4, false)]
    [InlineData("\r(x\r12 % endstream\r0 obj", 4, false)]
    public void An_endstream_a_comment_between_a_header_s_tokens_holds_ends_no_data_wherever_the_reads_part_the_header(
        string header, int past, bool ended)
    {
        // Object 5 lost its endstream, and its data runs to a header no index places whose tokens a comment holding endstream
        // parts (#261) — the line before it holding a string it leaves open, or not. That endstream, the first of the data,
        // ended it, inside the header, which the data ran into, and the object's own endobj before the header was judged
        // missing, where a search whose next object is the header, the index placing it, stopped there. What follows the
        // comment's line is read until it decides the header: the data ends at the object's own endobj or at the header, the
        // same wherever the window, the first read of the file past it, or a lower object guard parts the header, and whether
        // the index places the header's object or not.
        var windowEdge = Window - LostEndPrefix.Length;
        var firstReadEdge = windowEdge - PdfFileReader.EndStreamSearchOverlap + PdfFileReader.EndStreamSearchFirstRead(windowEdge);
        var splits = new List<(int Filler, int? Guard)>();

        foreach (var edge in new[] { windowEdge, firstReadEdge })
        {
            for (var filler = edge - header.Length - 2; filler <= edge + 2; filler++)
            {
                splits.Add((filler, null));
            }
        }

        for (var guard = 6000 + LostEndPrefix.Length - 2; guard <= 6000 + LostEndPrefix.Length + header.Length + 2; guard++)
        {
            splits.Add((6000, guard));
        }

        foreach (var (filler, guard) in splits)
        {
            var length = filler + past;
            var ending = HeaderEnding(header, filler, guard);

            ending.Length.Should().Be(length, "the filler is {0} bytes and the guard {1}", filler, guard);
            ending.Diagnostic.Should().Be(
                ended
                    ? $"{PdfDiagnosticCodes.StreamTruncated}: its object's endobj ends it, after {CountText.Bytes(length)}."
                    : $"{PdfDiagnosticCodes.StreamLengthInvalid}: the {CountText.Bytes(length)} up to that object are taken as its data.",
                "the filler is {0} bytes and the guard {1}",
                filler,
                guard);
            ending.EndObjMissing.Should().Be(!ended, "the filler is {0} bytes and the guard {1}", filler, guard);
            ending.RuleFires.Should().Be(!ended, "the filler is {0} bytes and the guard {1}", filler, guard);
        }

        HeaderEnding(header, 100, null, placed: true).Should().Be(HeaderEnding(header, 100, null));
        HeaderEnding(header, 20_000, null, placed: true).Should().Be(HeaderEnding(header, 20_000, null));
    }

    [Theory]
    [InlineData("\nendobj\n12 % endstream\n0 obj", "\n0 ", 10, 4)]
    [InlineData("\nendobj\n12 % endstream\n% 7 0 obj\n(x)", "% 7 0 obj\n", 10, 4)]
    [InlineData("\nendobj\n12 % endstream\n0 obj", "\n0 ob", 1, 0)]
    public void An_endstream_a_comment_between_a_header_s_tokens_holds_is_decided_alike_through_reads_a_source_cuts_short(
        string header, string upTo, int before, int then)
    {
        // As above, through a source whose reads may give fewer bytes than asked before the end of the file (#52). A read
        // that starts in the data more than the bytes given before the end of the text given, and would reach past that end,
        // stops there; where a count is given, the read that starts ten bytes before that end gives only that many bytes,
        // and so ends before the read before it did. Such a read took back what the reads before had reached, and read the
        // header's generation, or a comment line's text, again as tokens; and reads a byte apart that end inside obj led to
        // one that starts past obj's first byte, from which the header before it was read back outside the read. The data
        // ends where whole reads end it. Object 6 is long enough that the file's last 4,096 bytes, which hold its startxref,
        // lie past the data.
        const int Filler = 20_000;
        var file = HeaderFile(header, Filler, padding: PdfFileReader.TailSearchLength);
        var dataStart = DataStartIn(file);
        var edge = dataStart + Filler + header.IndexOf(upTo, StringComparison.Ordinal) + upTo.Length;
        var source = new ShortCountSource(file, (offset, held) =>
            offset > dataStart && offset < edge - before && offset + held > edge ? (int)(edge - offset)
            : offset == edge - PdfFileReader.EndStreamSearchOverlap && then > 0 ? then
            : held);

        HeaderEnding(source, PdfReaderOptions.Default).Should().Be(HeaderEnding(PdfFileSource.FromMemory(file), PdfReaderOptions.Default));
        source.ShortReads.Should().BePositive();
    }

    [Theory]
    [InlineData("\n", 11)]
    [InlineData("\n", 12)]
    [InlineData("\n", 40)]
    [InlineData("\r\n", 11)]
    [InlineData("\r\n", 12)]
    [InlineData("\r\n", 40)]
    public void An_endstream_past_the_window_ends_the_data_alike_through_reads_a_source_cuts_short_to_more_bytes_than_the_overlap(
        string endOfLine, int cap)
    {
        // Object 5 declares a length past the file, and its endstream, which a line feed or a carriage return and a line
        // feed precede, lies past the window, through a source whose reads that start in the data give at most cap bytes,
        // before the end of the file (#52). A read of more bytes than the overlap, short or not, makes the next repeat the
        // overlap whole, so the read that first holds the keyword whole holds the end of line before it too: the data ends
        // where whole reads end it, wherever the reads fall about the keyword. One of no more bytes than the overlap repeats
        // less of it, and what then follows is short reads' to settle (#52), not held here: reads of 10 bytes leave the
        // carriage return of the pair in the data, reads of 9 the line feed as well, and reads shorter than the keyword miss
        // it, the data then running to the object's endobj, or, in reads of fewer than four bytes, which never hold obj and
        // the byte after it, past the next object's header to that object's endobj, which the reader takes for its own.
        // Object 6 is long enough that the file's last 4,096 bytes, which hold its startxref, lie past the reads cut short.
        var header = endOfLine + "endstream\nendobj\n12 0 obj";
        var windowEdge = Window - LostEndPrefix.Length;

        for (var filler = windowEdge + 30; filler < windowEdge + 60; filler++)
        {
            var file = HeaderFile(header, filler, padding: PdfFileReader.TailSearchLength);
            var dataStart = DataStartIn(file);
            var source = new ShortCountSource(file, (offset, held) =>
                offset > dataStart && offset < dataStart + filler + header.Length ? Math.Min(held, cap) : held);
            var whole = HeaderEnding(PdfFileSource.FromMemory(file), PdfReaderOptions.Default);

            HeaderEnding(source, PdfReaderOptions.Default).Should().Be(whole, "the filler is {0} bytes", filler);
            whole.Length.Should().Be(filler, "the filler is {0} bytes", filler);
            source.ShortReads.Should().BePositive("the filler is {0} bytes", filler);
        }
    }

    [Theory]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(4096)]
    public void An_endstream_a_comment_between_a_header_s_tokens_holds_is_decided_alike_through_a_source_that_reads_by_blocks(int block)
    {
        // As above, through a source whose reads stop at the end of the block they start in, as one over a store of blocks
        // may (#52), the header's obj two bytes before a block ends: the reads that end there start a byte apart, and the
        // first past the block's end starts past obj's first byte. The reads at the end of the file, cut short too, leave
        // the startxref unread, and the index is rebuilt as the document opens, which reads the stream then.
        const string Header = "\nendobj\n12 % endstream\n0 obj";
        var obj = DataStartIn(HeaderFile(Header, 0)) + Header.LastIndexOf("obj", StringComparison.Ordinal);
        var filler = 20_000 + (((block - 2 - ((obj + 20_000) % block)) + block) % block);
        var file = HeaderFile(Header, filler);
        var source = new ShortCountSource(file, (offset, held) => (int)Math.Min(held, block - (offset % block)));

        HeaderEnding(source, PdfReaderOptions.Default).Should().Be(HeaderEnding(PdfFileSource.FromMemory(file), PdfReaderOptions.Default));
        ((obj + filler) % block).Should().Be(block - 2);
    }

    [Theory]
    [InlineData("\nAAAA % endstream\n0 obj", 8, true)]
    [InlineData("\n/Name % endstream\n12 0 obj", 9, true)]
    [InlineData("\n12 % endstream\n0 0 obj", 6, true)]
    [InlineData("\n12 % endstream\n5 0 obj", 6, true)]
    [InlineData("\n12 % endstream\n0 abc\n12 0 obj", 6, true)]
    [InlineData("\n12 % endstream\nendobj\n12 0 obj", 6, false)]
    [InlineData("\n12 % endstream\n(0) obj", 6, true)]
    [InlineData("\n12 % endstream\n0 objx\n12 0 obj", 6, true)]
    [InlineData("\n12 % endstream\n0\n", 6, true)]
    [InlineData("\n12 (% endstream\n0 obj", 7, true)]
    [InlineData("\nendstream 12 0\nobj", 0, true)]
    public void An_endstream_in_a_comment_ends_the_data_where_no_header_that_starts_before_it_ends_past_it(
        string header, int past, bool endObjMissing)
    {
        // As above, but nothing after the comment's line ends a header that starts before the endstream: the line's tokens
        // end in a keyword or a name, a second number — which starts a header past the endstream —, a keyword or a string
        // follows the first, an obj a regular byte follows, or the first number is the last token before the end of the
        // object. An endstream in a string, or one that is a token, is in no comment, and a header after it starts past it.
        // The endstream, the first of the data, ends it, wherever the reads part what follows, and its object's endobj is
        // judged by what follows it.
        foreach (var filler in new[] { 100, 20_000, Window - LostEndPrefix.Length - header.Length + 6 })
        {
            var ending = HeaderEnding(header, filler, null);

            ending.Length.Should().Be(filler + past, "the filler is {0} bytes", filler);
            ending.Diagnostic.Should().StartWith(PdfDiagnosticCodes.StreamLengthInvalid + ":", "the filler is {0} bytes", filler);
            ending.EndObjMissing.Should().Be(endObjMissing, "the filler is {0} bytes", filler);
        }
    }

    [Theory]
    [InlineData("\nendobj\n12 % ", 0, true)]
    [InlineData("\n5 12 % ", 3, false)]
    public void An_endobj_a_long_comment_of_a_header_holds_changes_nothing_wherever_the_reads_after_its_number_end(
        string before, int past, bool ended)
    {
        // As above, but the comment runs on for 40,000 bytes, the header's numbers read long before it ends: two reads past
        // the window carry them, and the third decides the header. The second read takes in the comment's endobj, or stops
        // before it, where text after the endobj leaves the header to the next read; the tail is taken as the copy the first
        // read kept, and the second kept again, where the header's number starts.
        var header = before + new string('c', 40_000) + " endobj " + new string('c', 16) + "\n0 obj";
        var windowEdge = Window - LostEndPrefix.Length;
        var size = PdfFileReader.EndStreamSearchFirstRead(windowEdge);
        var secondRead = windowEdge - (2 * PdfFileReader.EndStreamSearchOverlap) + size;
        var secondReadEdge = secondRead + Math.Min(2 * size, PdfFileReader.EndStreamSearchChunk);
        var keyword = header.LastIndexOf("endobj", StringComparison.Ordinal);

        for (var filler = secondReadEdge - keyword - 36; filler <= secondReadEdge - keyword - 8; filler++)
        {
            var ending = HeaderEnding(header, filler, null);

            ending.Length.Should().Be(filler + past, "the filler is {0} bytes", filler);
            ending.EndObjMissing.Should().Be(!ended, "the filler is {0} bytes", filler);
            ending.RuleFires.Should().Be(!ended, "the filler is {0} bytes", filler);
        }
    }

    [Fact]
    public void An_endobj_a_comment_holds_changes_nothing_when_the_header_s_number_starts_where_a_read_does()
    {
        // As above, the header's number where the first read of the file past the window starts, or a byte either side of
        // it, and its comment as long as puts the comment's endobj in that read and leaves the header to the next: the copy
        // of the tail kept where the number starts is the one taken before that read reads on.
        var windowEdge = Window - LostEndPrefix.Length;
        var firstRead = windowEdge - PdfFileReader.EndStreamSearchOverlap;
        var firstReadEdge = firstRead + PdfFileReader.EndStreamSearchFirstRead(windowEdge);
        const string Before = "\nendobj\n12 % ";
        var after = " endobj " + new string('c', 16) + "\n0 obj";

        for (var shift = -1; shift <= 1; shift++)
        {
            var filler = firstRead - Before.Length + "12 % ".Length + shift;

            for (var comment = firstReadEdge - filler - Before.Length - 40; comment <= firstReadEdge - filler - Before.Length - 10; comment++)
            {
                var ending = HeaderEnding(Before + new string('c', comment) + after, filler, null);

                ending.Length.Should().Be(filler, "the number is {0} bytes off the read's start, the comment {1} bytes long", shift, comment);
                ending.EndObjMissing.Should().BeFalse("the number is {0} bytes off the read's start, the comment {1} bytes long", shift, comment);
            }
        }
    }

    [Fact]
    public void An_object_whose_header_lies_past_the_window_is_read_where_the_index_says()
    {
        // The index points at white space that runs past the 8 KB window before the object's header: the
        // header the edge cut is read again in a larger window, not searched for nearby and then rebuilt.
        var text = new StringBuilder("%PDF-1.7\n");
        var catalog = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = text.Length;
        text.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        var padded = text.Length;
        text.Append(' ', Window + 100).Append("3 0 obj\n(padded)\nendobj\n");
        var xref = text.Length;
        text.Append("xref\n0 4\n0000000000 65535 f\r\n")
            .Append(CultureInfo.InvariantCulture, $"{catalog:D10} 00000 n\r\n{pages:D10} 00000 n\r\n{padded:D10} 00000 n\r\n")
            .Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");

        using var document = PdfDocument.Open(Encoding.ASCII.GetBytes(text.ToString()));

        document.GetObject(new PdfObjectId(3)).Should().BeOfType<PdfString>();
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData(40)]
    [InlineData(15_000)]
    public void A_stream_the_end_of_the_file_cuts_short_is_reported_once(int kept)
    {
        // A declared length the file cannot hold is no longer taken as it is and quietly shortened: a window
        // that reaches the end of the file finds no "endstream", and says the stream was cut.
        var complete = Encoding.ASCII.GetBytes(
            "%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n" +
            "2 0 obj << /Type /Pages /Kids [] /Count 0 >> endobj\n" +
            "3 0 obj << /Length 20000 >>\nstream\n" + new string('d', 20000) + "\nendstream\nendobj\n");
        var dataStart = Encoding.ASCII.GetString(complete).IndexOf("stream\n", StringComparison.Ordinal) + "stream\n".Length;

        using var document = PdfDocument.Open(complete.AsMemory(0, dataStart + kept));
        var stream = document.GetObject(new PdfObjectId(3)).AsStream().Required();

        stream.GetRawBytes().Length.Should().Be(kept);
        document.Diagnostics.Where(d => d.Code.StartsWith("stream.", StringComparison.Ordinal))
            .Should().ContainSingle()
            .Which.Code.Should().Be(PdfDiagnosticCodes.StreamTruncated);
    }

    [Fact]
    public void A_stream_whose_length_the_file_cannot_hold_is_read_to_its_endstream()
    {
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(ObjectNumber, "<< /Length 999999 >>\nstream\nshort\nendstream")
            .WithObject(6, "(" + new string('p', 3 * Window) + ")")
            .BuildClassic(rootNumber: 1);

        using var document = PdfDocument.Open(bytes);
        var stream = document.GetObject(new PdfObjectId(ObjectNumber)).AsStream().Required();

        Encoding.ASCII.GetString(stream.GetRawBytes().Span).Should().Be("short");
        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.StreamLengthInvalid);
    }

    [Fact]
    public void A_stream_the_file_cuts_short_keeps_its_declared_length_without_asking_the_source_past_its_end()
    {
        // The file ends inside "endstream". The reader asks the file for the bytes after the data, and a
        // source may refuse a read that runs past its end: it must be asked only for the six there are. No endstream
        // nor endobj follows the declared length, which ends before the end of the file: it is kept, and reported (#174).
        var bytes = Encoding.ASCII.GetBytes(
            "%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n" +
            "2 0 obj << /Type /Pages /Kids [] /Count 0 >> endobj\n" +
            "3 0 obj << /Length 5 >> stream\nhello\nendst");

        using var document = PdfDocument.Open(new StrictCountingSource(bytes), options: null, ownsSource: true);
        var stream = document.GetObject(new PdfObjectId(3)).AsStream().Required();

        Encoding.ASCII.GetString(stream.GetRawBytes().Span).Should().Be("hello");
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.StreamLengthInvalid).Which.Message.Should().Be(
            "The stream declared 5 bytes, and no endstream follows them before the end of the file; the declared length is kept.");
    }

    /// <summary>
    /// Reads object 5 of a document built around <paramref name="body"/>, and compares it with what the
    /// parser makes of the same bytes when it sees all of them at once: the same object, and the same
    /// diagnostics. Returns null when they agree, and what differs otherwise.
    /// </summary>
    internal static string? ReadAndCompare(string body)
    {
        var whole = new PdfDiagnostics();
        var expected = Canonical(new PdfObjectParser(Encoding.Latin1.GetBytes(body), diagnostics: whole).ParseObject());

        // Through a source that refuses any read past its end, which is all the reader may ever ask for.
        using var document = PdfDocument.Open(new StrictCountingSource(Document(body)), options: null, ownsSource: true);
        var actual = Canonical(document.GetObject(new PdfObjectId(ObjectNumber)));

        if (actual != expected)
        {
            return $"read {Shorten(actual)}, expected {Shorten(expected)}";
        }

        var reported = string.Join("; ", document.Diagnostics.Select(d => d.Code));
        var seen = string.Join("; ", whole.Select(d => d.Code));

        return reported == seen ? null : $"reported [{reported}], where the whole object gives [{seen}]";
    }

    /// <summary>What comes before the data of the stream <see cref="HeaderEnding"/> reads: its header, a length past the file, the keyword.</summary>
    private const string LostEndPrefix = "5 0 obj\n<< /Length 9999999 >>\nstream\n";

    /// <summary>
    /// Reads object 5, a stream that lost its <c>endstream</c>, whose data is <paramref name="filler"/> bytes and then
    /// <paramref name="header"/>, a header no index places — unless <paramref name="placed"/> says the index places object 12
    /// where the header's first <c>12</c> starts, which then reads as the string its header begins —, under an object guard
    /// of <paramref name="guard"/> bytes or the default: what its data ends at — its length, what the report says of it,
    /// from the colon on, under its code —, whether the reader judges its <c>endobj</c> missing, and whether the validator
    /// does, the catalog naming it.
    /// </summary>
    private static (int Length, string Diagnostic, bool EndObjMissing, bool RuleFires) HeaderEnding(
        string header, int filler, int? guard, bool placed = false)
    {
        var options = guard is { } bytes
            ? PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxObjectLength = bytes } }
            : PdfReaderOptions.Default;

        return HeaderEnding(PdfFileSource.FromMemory(HeaderFile(header, filler, placed)), options, placed);
    }

    /// <summary>
    /// Reads object 5 of a file <see cref="HeaderFile"/> wrote, through <paramref name="source"/>, under
    /// <paramref name="options"/>, as <see cref="HeaderEnding(string, int, int?, bool)"/> says, object 12 included.
    /// </summary>
    private static (int Length, string Diagnostic, bool EndObjMissing, bool RuleFires) HeaderEnding(
        PdfFileSource source, PdfReaderOptions options, bool placed = false)
    {
        using var document = PdfDocument.Open(source, options, ownsSource: true);

        var length = document.GetObject(new PdfObjectId(ObjectNumber)).AsStream().Required().RawLength;
        var reported = document.Diagnostics.Single(d => d.Code is PdfDiagnosticCodes.StreamTruncated or PdfDiagnosticCodes.StreamLengthInvalid);
        var message = reported.Message;
        var colon = message.IndexOfAny([':', ';']);

        if (placed)
        {
            document.GetObject(new PdfObjectId(12)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("twelve");
        }

        return (
            length,
            reported.Code + ":" + message[(colon + 1)..],
            document.Reader.IsEndObjMissing(ObjectNumber, out _),
            new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.ObjectEndObjMissing));
    }

    /// <summary>
    /// Writes the file <see cref="HeaderEnding(string, int, int?, bool)"/> reads: object 5, a stream that lost its
    /// <c>endstream</c>, whose data is <paramref name="filler"/> bytes and then <paramref name="header"/>, then object 6, a
    /// string of <paramref name="padding"/> bytes, or <c>(six)</c> where that is 0; the index places object 12 where the
    /// header's first <c>12</c> starts where <paramref name="placed"/> says so.
    /// </summary>
    private static byte[] HeaderFile(string header, int filler, bool placed = false, int padding = 0)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Metadata 5 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        rows[ObjectNumber] = file.Position;

        if (placed)
        {
            rows[12] = file.Position + LostEndPrefix.Length + filler + header.IndexOf("12", StringComparison.Ordinal);
        }

        file.Raw(LostEndPrefix + new string('A', filler) + header + "\n(twelve)\nendobj\n");
        rows[6] = padding > 0 ? file.Padding(6, padding) : file.Object(6, "(six)");
        file.Table(placed ? 13 : 7, rows, placed ? "/Size 13 /Root 1 0 R" : "/Size 7 /Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>Gives where the data of the stream a file <see cref="HeaderFile"/> wrote starts.</summary>
    private static int DataStartIn(byte[] file) =>
        Encoding.Latin1.GetString(file).IndexOf(LostEndPrefix, StringComparison.Ordinal) + LostEndPrefix.Length;

    private static byte[] Document(string body) =>
        new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(ObjectNumber, body)
            .BuildClassic(rootNumber: 1);

    /// <summary>What <see cref="TestPdfBuilder"/> writes before an object's body.</summary>
    private static string Header(int number) => $"{number} 0 obj\n";

    private static int DataStart(int dataEnd) =>
        Header(ObjectNumber).Length + StreamPrefix(dataEnd).Length;

    private static string StreamPrefix(int dataEnd)
    {
        // The declared length depends on where the data starts, which depends on how many digits the
        // length has: four for every window this test uses.
        var guess = $"<< /Length {dataEnd:D4} >>\nstream\n";
        var length = dataEnd - Header(ObjectNumber).Length - guess.Length;
        return $"<< /Length {length:D4} >>\nstream\n";
    }

    /// <summary>A stream whose data ends <paramref name="dataEnd"/> bytes into its object.</summary>
    private static string StreamEndingAt(int dataEnd, string endOfLine)
    {
        var prefix = StreamPrefix(dataEnd);
        var length = dataEnd - Header(ObjectNumber).Length - prefix.Length;
        var data = new StringBuilder(length);

        for (var i = 0; i < length; i++)
        {
            data.Append((char)('a' + (i % 26)));
        }

        return prefix + data + endOfLine + "endstream";
    }

    /// <summary>
    /// A classic table of 3,272 entries in two subsections, then its trailer, with rows of the standard
    /// twenty bytes. <paramref name="shift"/> spaces after the first subsection's header move everything
    /// after it, one byte each; the region is from the first subsection's last two rows to the trailer's end.
    /// An <paramref name="anomalous"/> trailer holds a key that is not a name.
    /// </summary>
    private static (byte[] Bytes, int XRefOffset, int RegionStart, int RegionEnd, int Size) LongTable(int shift, bool anomalous)
    {
        const int First = 3270;
        const int Second = 2;
        var size = First + Second;
        var text = new StringBuilder();

        text.Append("%PDF-1.7\n");
        var catalog = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = text.Length;
        text.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

        var xref = text.Length;
        text.Append("xref\n0 ").Append(First).Append(' ', shift + 1).Append('\n');
        text.Append("0000000000 65535 f\r\n");
        text.Append(CultureInfo.InvariantCulture, $"{catalog:D10} 00000 n\r\n");
        text.Append(CultureInfo.InvariantCulture, $"{pages:D10} 00000 n\r\n");

        for (var i = 3; i < First; i++)
        {
            text.Append("0000000000 00000 f\r\n");
        }

        var regionStart = text.Length - 40;
        text.Append(First).Append(' ').Append(Second).Append('\n');
        text.Append("0000000000 00000 f\r\n0000000000 00000 f\r\n");
        text.Append("trailer\n<< /Size ").Append(size).Append(anomalous ? " 12" : string.Empty).Append(" /Root 1 0 R >>\n");
        var regionEnd = text.Length;
        text.Append("startxref\n").Append(xref).Append("\n%%EOF\n");

        return (Encoding.ASCII.GetBytes(text.ToString()), xref, regionStart, regionEnd, size);
    }

    /// <summary>A row of a cross-reference stream with fields 1, 4 and 2 bytes wide: an object at an offset.</summary>
    private static byte[] XRefRow(int offset) =>
        [1, (byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset, 0, 0];

    /// <summary>A form of an object that two parses agree on exactly when they found the same object.</summary>
    private static string Canonical(PdfObject value)
    {
        var text = new StringBuilder();
        Write(value, text);
        return text.ToString();

        static void Write(PdfObject value, StringBuilder text)
        {
            switch (value)
            {
                case PdfStream stream:
                    Write(stream.Dictionary, text);
                    text.Append(" stream ").Append(Convert.ToHexString(stream.GetRawBytes().Span));
                    break;

                case PdfDictionary dictionary:
                    text.Append("<<");
                    foreach (var key in dictionary.Keys.OrderBy(k => k.Value, StringComparer.Ordinal))
                    {
                        text.Append(" /").Append(key.Value).Append(' ');
                        Write(dictionary[key]!, text);
                    }

                    text.Append(" >>");
                    break;

                case PdfArray array:
                    text.Append('[');
                    foreach (var item in array)
                    {
                        text.Append(' ');
                        Write(item, text);
                    }

                    text.Append(" ]");
                    break;

                case PdfName name:
                    text.Append("PdfName:/").Append(name.Value);
                    break;

                case PdfString content:
                    text.Append(content.IsHexadecimal ? "<" : "(")
                        .Append(Convert.ToHexString(content.Bytes.Span))
                        .Append(content.IsHexadecimal ? ">" : ")");
                    break;

                default:
                    text.Append(value.GetType().Name).Append(':').Append(value);
                    break;
            }
        }
    }

    private static string Shorten(string text) => text.Length <= 200 ? text : text[..100] + " … " + text[^100..];

    private static string Describe(string endOfLine) => endOfLine switch
    {
        "\n" => "LF",
        "\r\n" => "CR LF",
        "\r" => "CR",
        " " => "a space",
        _ => "nothing",
    } + " before endstream";
}
