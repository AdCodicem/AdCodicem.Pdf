using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;

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
        // across or past the edge. The reader asks the file for the 13 bytes after the data instead of
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

            if (read > Window + 13 || length != Window - shortOfEdge - DataStart(Window - shortOfEdge) ||
                document.Diagnostics.Count > 0)
            {
                failures.Add($"{shortOfEdge} bytes short: read {read} bytes, data of {length}, {document.Diagnostics.Count} diagnostics");
            }
        }

        failures.Should().BeEmpty($"{Describe(endOfLine)} must not cost a larger window");
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
        // one that reaches the end of the file, reports it; the attempts before it do not.
        var marker = "/Cut 1 >>";
        var complete = Document("<< /Pad (" + new string('x', 3 * Window) + ") " + marker);
        var end = Encoding.Latin1.GetString(complete).IndexOf(marker, StringComparison.Ordinal) + "/Cut ".Length;

        using var document = PdfDocument.Open(complete.AsMemory(0, end));
        var value = document.GetObject(new PdfObjectId(ObjectNumber)).AsDictionary().Required();

        value[PdfName.Get("Pad")].Should().BeOfType<PdfString>().Which.Length.Should().Be(3 * Window);
        document.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.SyntaxTruncatedObject)
            .Should().ContainSingle()
            .Which.Position.Should().Be(end);
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
        // catalogue is in the last row.
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
    public void A_stream_the_file_cuts_short_is_reported_without_asking_the_source_past_its_end()
    {
        // The file ends inside "endstream". The reader asks the file for the bytes after the data, and a
        // source may refuse a read that runs past its end: it must be asked only for the six there are.
        var bytes = Encoding.ASCII.GetBytes(
            "%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n" +
            "2 0 obj << /Type /Pages /Kids [] /Count 0 >> endobj\n" +
            "3 0 obj << /Length 5 >> stream\nhello\nendst");

        using var document = PdfDocument.Open(new StrictCountingSource(bytes), options: null, ownsSource: true);
        var stream = document.GetObject(new PdfObjectId(3)).AsStream().Required();

        Encoding.ASCII.GetString(stream.GetRawBytes().Span).Should().StartWith("hello");
        document.Diagnostics.Contains(PdfDiagnosticCodes.StreamTruncated).Should().BeTrue();
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
                        text.Append(' ').Append(key).Append(' ');
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
