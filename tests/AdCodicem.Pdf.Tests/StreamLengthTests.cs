using System.Globalization;
using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// A stream's <c>/Length</c> checked wherever its data ends (#55), and said as the file wrote it when it cannot be taken
/// (#120).
/// </summary>
/// <remarks>
/// The reader parses an object through an 8 KB window. A stream whose data ends inside it has its length checked
/// against the <c>endstream</c> after it; one whose data runs past it had its length taken as declared, and a wrong one
/// cut the data short or took in what followed, in silence. The file is now asked whether <c>endstream</c> follows the
/// declared length, and searched for the first one when it does not — up to the nearer of the next object the file's
/// index places, as the file wrote it or as the reader rebuilt it as the document opened, and the first object header its
/// bytes hold, or the end of the file —, once for each stream, bounded by nothing a read changes. What the reader found of
/// a stream whose length is not confirmed is recorded for the validation rules, and reported once however often the
/// stream is parsed.
/// </remarks>
public class StreamLengthTests
{
    private const int StreamNumber = 5;

    private static readonly int Window = PdfFileReader.InitialObjectWindow;

    /// <summary>More white space between two tokens than a read of the search repeats of the one before it.</summary>
    private const string Spaces = "                 ";

    /// <summary>
    /// An object header wider than a read of the search repeats of the one before it: the largest number and generation,
    /// zeros before them, white space between them.
    /// </summary>
    private const string Wide = "0002147483647" + Spaces + "00065535" + Spaces + "obj";

    [Fact]
    public void A_stream_past_the_window_whose_length_endstream_follows_is_read_as_declared_for_a_few_bytes_more()
    {
        var data = Data(3 * Window);
        var file = Document((StreamNumber, Stream(data.Length, data)), (6, "(after)"));

        using var source = new StrictCountingSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var beforeLoading = source.BytesRead;

        var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        (source.BytesRead - beforeLoading).Should().Be(Window + PdfObjectParser.EndObjLookahead, "the window, then the bytes after the declared length");
        stream.Data.Length.Should().Be(data.Length);
        document.Diagnostics.Should().BeEmpty();
        document.Reader.TryGetStreamLengthFault(StreamNumber, out _).Should().BeFalse("a sound stream records nothing");
        document.Reader.IsEndObjMissing(StreamNumber, out _).Should().BeFalse();
    }

    [Fact]
    public void A_stream_past_the_window_whose_length_is_short_ends_at_the_endstream_the_file_holds()
    {
        // The last object: the search runs to the end of the file, and finds the endstream 40 bytes on.
        var data = Data(3 * Window);
        var file = Document((StreamNumber, Stream(data.Length - 40, data)));
        var dataStart = DataStartOf(file, StreamNumber);

        using var document = PdfDocument.Open(file);
        var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        stream.GetRawBytes().Length.Should().Be(data.Length);
        var diagnostic = document.Diagnostics.Should().ContainSingle().Which;
        diagnostic.Code.Should().Be(PdfDiagnosticCodes.StreamLengthInvalid);
        diagnostic.Message.Should().Be(Invariant($"The stream declared {data.Length - 40} bytes but ended after {data.Length}."));
        diagnostic.Position.Should().Be(dataStart);

        document.Reader.TryGetStreamLengthFault(StreamNumber, out var fault).Should().BeTrue();
        fault.Should().Be(new StreamLengthFault
        {
            Form = StreamLengthForm.Integer,
            Declared = data.Length - 40,
            Taken = data.Length,
            Found = data.Length,
            EndStream = EndStreamState.Found,
            DataStart = dataStart,
        });
        document.Reader.IsEndObjMissing(StreamNumber, out _).Should().BeFalse("the file was asked what follows the endstream found");
    }

    [Fact]
    public void A_stream_past_the_window_whose_length_is_long_ends_at_the_endstream_the_window_holds()
    {
        // JHOVE's Atypon article, object 49: 62,065 bytes declared, 1,647 there. The declared end lies inside the object
        // that follows, which the file can hold.
        var file = Document((StreamNumber, Stream(3 * Window, "short data")), (6, "(" + new string('p', 4 * Window) + ")"));

        using var document = PdfDocument.Open(file);
        var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        Encoding.ASCII.GetString(stream.GetRawBytes().Span).Should().Be("short data");
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(
            Invariant($"The stream declared {3 * Window} bytes but ended after 10."));
    }

    [Fact]
    public void A_stream_past_the_window_with_no_endstream_before_the_next_object_keeps_its_declared_length()
    {
        // The endstream the search would find next is object 6's: the next object bounds it.
        var data = Data(3 * Window);
        var file = Reachable((StreamNumber, Stream(data.Length + 100, data, end: "\n")), (6, Stream(5, "hello")));
        var dataStart = DataStartOf(file, StreamNumber);
        var next = HeaderOf(file, 6);

        using var document = PdfDocument.Open(file);
        var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        stream.Data.Length.Should().Be(data.Length + 100);
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(
            Invariant($"The stream declared {data.Length + 100} bytes, and no endstream follows them before the next object, at {next}; the declared length is kept."));

        document.Reader.TryGetStreamLengthFault(StreamNumber, out var fault).Should().BeTrue();
        fault.Should().Be(new StreamLengthFault
        {
            Form = StreamLengthForm.Integer,
            Declared = data.Length + 100,
            Taken = data.Length + 100,
            EndStream = EndStreamState.MissingBeforeNextObject,
            NextObject = next,
            DataStart = dataStart,
        });

        // Where the object ends is unknown, and its endobj is not judged.
        document.Reader.IsEndObjMissing(StreamNumber, out _).Should().BeFalse();
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.ObjectEndObjMissing).Should().BeFalse();
    }

    [Fact]
    public void A_stream_past_the_window_with_no_endstream_before_the_end_of_the_file_keeps_its_declared_length()
    {
        var data = Data(3 * Window);
        var file = Document((StreamNumber, Stream(data.Length, data, end: "\nendstreak")));

        using var document = PdfDocument.Open(file);
        var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        stream.Data.Length.Should().Be(data.Length);
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(
            Invariant($"The stream declared {data.Length} bytes, and no endstream follows them before the end of the file; the declared length is kept."));
        document.Reader.TryGetStreamLengthFault(StreamNumber, out var fault).Should().BeTrue();
        fault.EndStream.Should().Be(EndStreamState.MissingBeforeEndOfFile);
        fault.NextObject.Should().BeNull();
    }

    [Fact]
    public void A_stream_past_the_window_with_no_endstream_before_an_object_the_window_holds_keeps_its_declared_length()
    {
        // The next object starts inside the window: the part of the window before it is all the search reads.
        var file = Document((StreamNumber, Stream(3 * Window, "short data", end: string.Empty)), (6, "(" + new string('p', 4 * Window) + ")"));

        using var source = new StrictCountingSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var beforeLoading = source.BytesRead;

        document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required().Data.Length.Should().Be(3 * Window);

        (source.BytesRead - beforeLoading).Should().Be(Window + PdfObjectParser.EndObjLookahead, "the file past the window is not searched");
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(
            Invariant($"The stream declared {3 * Window} bytes, and no endstream follows them before the next object, at {HeaderOf(file, 6)}; the declared length is kept."));
    }

    [Fact]
    public void A_stream_whose_declared_length_ends_the_file_without_endstream_keeps_it()
    {
        // The declared length runs to the last byte of the file: there is nothing after it to ask for, and nothing to find.
        var data = Data(3 * Window);
        var file = Encoding.ASCII.GetBytes(
            "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n" +
            Invariant($"5 0 obj\n<< /Length {data.Length} >>\nstream\n{data}"));

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required().Data.Length.Should().Be(data.Length);

        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid).Which.Message.Should().Be(
            Invariant($"The stream declared {data.Length} bytes, and no endstream follows them before the end of the file; the declared length is kept."));
        document.Reader.IsEndObjMissing(StreamNumber, out _).Should().BeFalse("where the object ends is unknown");
    }

    [Fact]
    public void The_search_stops_at_an_object_the_index_as_written_places_though_a_rebuilt_index_lacks_it()
    {
        // PDFium's urban planning report, object 695: a block of zeros erased the end of the stream's data, its endstream
        // and the objects after it, their headers with them. The rebuilt index lacks them, and the first header the bytes
        // still hold is object 7's; the index as written still places object 6 there, nearer, and the endstream past it is
        // object 7's.
        var data = Data(3 * Window);
        var file = Document(
            (StreamNumber, Stream(data.Length, data)),
            (6, "(" + new string('p', 2000) + ")"),
            (7, Stream(5, "hello")));
        var dataStart = (int)DataStartOf(file, StreamNumber);
        var erased = HeaderOf(file, 6);
        var seven = HeaderOf(file, 7);
        Array.Clear(file, dataStart + (2 * Window), (int)seven - (dataStart + (2 * Window)));

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(6)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeTrue("object 6 is nowhere near where the index places it");
        var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        stream.Data.Length.Should().Be(data.Length);
        seven.Should().BeGreaterThan(erased, "the first header the bytes hold after the data is object 7's");
        document.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid).Should().ContainSingle()
            .Which.Message.Should().Be(
                Invariant($"The stream declared {data.Length} bytes, and no endstream follows them before the next object, at {erased}; the declared length is kept."));
    }

    [Fact]
    public void The_search_stops_at_the_next_object_of_an_index_rebuilt_as_the_document_opened()
    {
        // No index was written: the one the rebuild made is the only one, and object 6's endstream is past its header.
        var data = Data(3 * Window);
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(StreamNumber, Stream(data.Length - 7, data, end: "\n"))
            .WithObject(6, Stream(5, "hello"))
            .BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(file);
        document.WasRepaired.Should().BeTrue();
        var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        stream.Data.Length.Should().Be(data.Length - 7);
        document.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid).Should().ContainSingle()
            .Which.Message.Should().EndWith(Invariant($"before the next object, at {HeaderOf(file, 6)}; the declared length is kept."));
    }

    [Fact]
    public void The_search_stops_at_an_object_header_the_bytes_hold_before_the_next_object_the_index_as_written_places()
    {
        // Object 5 lost its endstream, and object 12's header follows it, an object no index places: the index as written
        // places object 6 next, past object 12's endstream. The header the bytes hold is the nearer, and the search stops
        // there rather than take in object 12.
        var data = Data(3 * Window);
        var file = Document(
            (StreamNumber, Stream(data.Length + 400, data, end: "\nendstreax\nendobj\n12 0 obj\n<< /Length 5 >>\nstream\nhello\nendstream")),
            (6, "(" + new string('p', 2000) + ")"));
        var twelve = HeaderOf(file, 12);
        var dataStart = DataStartOf(file, StreamNumber);

        using var document = PdfDocument.Open(file);
        var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        stream.Data.Length.Should().Be(data.Length + 400);
        HeaderOf(file, 6).Should().BeGreaterThan(twelve);
        document.Reader.TryGetStreamLengthFault(StreamNumber, out var fault).Should().BeTrue();
        fault.Should().Be(new StreamLengthFault
        {
            Form = StreamLengthForm.Integer,
            Declared = data.Length + 400,
            Taken = data.Length + 400,
            EndStream = EndStreamState.MissingBeforeNextObject,
            NextObject = twelve,
            DataStart = dataStart,
        });
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(
            Invariant($"The stream declared {data.Length + 400} bytes, and no endstream follows them before the next object, at {twelve}; the declared length is kept."));
    }

    [Theory]
    [InlineData("12 0 obj", 0)]
    [InlineData("12 0 obj<< /Length 5 >>", 0)]
    [InlineData("]12 0 obj", 1)]
    [InlineData("0012 00000 obj", 0)]
    [InlineData("2147483647 65535 obj", 0)]
    [InlineData("12\t0\r\nobj%", 0)]
    [InlineData("12\0\0 0\0obj\0", 0)]
    [InlineData(Wide, 0)]
    [InlineData("12 000000 obj", 0)]
    [InlineData("01234567890 0 obj", 0)]
    [InlineData("000000000000000000000012 0 obj", 0)]
    [InlineData("12 " + Spaces + "0 obj", 0)]
    [InlineData("12 0" + Spaces + " obj", 0)]
    [InlineData("12\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\00 obj", 0)]
    [InlineData("endobj", null)]
    [InlineData("12 0 objx", null)]
    [InlineData("12 0 objects", null)]
    [InlineData("x12 0 obj", null)]
    [InlineData("1.2 0 obj", null)]
    [InlineData("12 0obj", null)]
    [InlineData("12obj", null)]
    [InlineData("0 obj", null)]
    [InlineData("0 0 obj", null)]
    [InlineData("12 +0 obj", null)]
    [InlineData("12 65536 obj", null)]
    [InlineData("12 0000065536 obj", null)]
    [InlineData("2147483648 0 obj", null)]
    [InlineData("02147483648 0 obj", null)]
    [InlineData("99999999999999999999999 0 obj", null)]
    [InlineData("12 0 %\nobj", null)]
    [InlineData("12 %\n0 obj", null)]
    public void Text_that_follows_a_lost_endstream_stops_the_search_when_it_is_an_object_header_and_only_then(string text, int? header)
    {
        // An object header is "N G obj" at a token boundary, as the bytes hold it: a number and a generation whose values
        // the parser takes, however many zeros lead them, and white space between its tokens, however much. Text that is
        // one stops the search before the endstream after it, and the declared length is kept; text that only looks like
        // one — endobj, obj glued to what follows or precedes it, digits after a regular character, a number or a
        // generation out of range, a comment between its tokens — is read past, and the endstream after it ends the data.
        var data = Data(3 * Window);
        var file = Document(
            (StreamNumber, Stream(data.Length + 400, data, end: "\n" + text + "\nendstream")),
            (6, "(" + new string('p', 2000) + ")"));
        var dataStart = DataStartOf(file, StreamNumber);

        using var document = PdfDocument.Open(file);
        var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        document.Reader.TryGetStreamLengthFault(StreamNumber, out var fault).Should().BeTrue();

        if (header is { } at)
        {
            var start = dataStart + data.Length + 1 + at;
            stream.Data.Length.Should().Be(data.Length + 400);
            fault.EndStream.Should().Be(EndStreamState.MissingBeforeNextObject);
            fault.NextObject.Should().Be(start);
            document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(
                Invariant($"The stream declared {data.Length + 400} bytes, and no endstream follows them before the next object, at {start}; the declared length is kept."));
        }
        else
        {
            stream.Data.Length.Should().Be(data.Length + 1 + text.Length);
            fault.EndStream.Should().Be(EndStreamState.Found);
            document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(
                Invariant($"The stream declared {data.Length + 400} bytes but ended after {data.Length + 1 + text.Length}."));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_start_and_the_end_of_the_search_bound_an_object_header_as_white_space_does(bool atStart)
    {
        // The data starts with a header: nothing before it is read, and the search stops where it starts. Or the data ends
        // with one, and the index as written places object 6 at the byte after its obj: the search ends there, and the
        // header stops it where it starts, before that offset.
        var data = Data(3 * Window);
        var file = Document(
            (StreamNumber, Stream(data.Length + 400, atStart ? "12 0 obj\n" + data : data + "\n12 0 obj", end: string.Empty)),
            (6, "(" + new string('p', 2000) + ")"));
        var dataStart = DataStartOf(file, StreamNumber);
        var header = atStart ? dataStart : dataStart + data.Length + 1;

        if (!atStart)
        {
            Rewrite(file, 6, header + "12 0 obj".Length);
        }

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required().Data.Length.Should().Be(data.Length + 400);

        document.Reader.TryGetStreamLengthFault(StreamNumber, out var fault).Should().BeTrue();
        fault.NextObject.Should().Be(header);
    }

    [Theory]
    [InlineData("7 0 obj", true)]
    [InlineData(Wide, true)]
    [InlineData("x" + Wide, false)]
    [InlineData(Wide + "x", false)]
    [InlineData("2147483648" + Spaces + "0 obj", false)]
    [InlineData("7" + Spaces + "65536 obj", false)]
    public void A_header_the_edge_between_two_reads_of_the_search_cuts_is_decided_whole(string text, bool header)
    {
        // The search reads the window's part of the data, then the file a read at a time, each repeating the last bytes of
        // the one before and twice as large: a header, with the byte before it and the byte after its obj, is decided
        // whole wherever the edge of the window or of the reads past it cuts it — here one wider than a read repeats, and
        // the same with a regular character before or after it, or a number or a generation out of range, which are none.
        // What a read's start cuts of a header, the read before carries into it. The start of a read is no boundary before
        // a number, nor its end one after obj, unless the search starts or ends there. A header missed, the endstream after
        // it would end the data; text taken for one, the declared length would be kept.
        var prefix = Invariant($"{StreamNumber} 0 obj\n") + Stream(0, string.Empty, end: string.Empty, width: 7);
        var read = PdfFileReader.EndStreamSearchFirstRead(Window - prefix.Length);
        var first = Window - PdfFileReader.EndStreamSearchOverlap + read;
        int[] edges = [Window, first, first - PdfFileReader.EndStreamSearchOverlap + (2 * read)];

        foreach (var edge in edges)
        {
            for (var at = edge - text.Length - 2; at <= edge + 2; at++)
            {
                var length = at - prefix.Length - 1;
                var data = Data(length);
                var file = Document(
                    (StreamNumber, Stream(length + 400, data, end: "\n" + text + "\nendstream", width: 7)),
                    (6, "(" + new string('p', 2000) + ")"));

                using var document = PdfDocument.Open(file);
                var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

                var because = Invariant($"the text starts {at - edge} bytes from the edge at {edge}");
                document.Reader.TryGetStreamLengthFault(StreamNumber, out var fault).Should().BeTrue();

                if (header)
                {
                    stream.Data.Length.Should().Be(length + 400, because);
                    fault.NextObject.Should().Be(HeaderOf(file, StreamNumber) + at, because);
                }
                else
                {
                    stream.Data.Length.Should().Be(length + 1 + text.Length, because);
                }
            }
        }
    }

    [Theory]
    [InlineData("12{0}0 obj", ' ', true)]
    [InlineData("12{0}0{0}obj", '\0', true)]
    [InlineData("12 {0} obj", '0', true)]
    [InlineData("{0}12 0 obj", '0', true)]
    [InlineData("1{0} 0 obj", '0', false)]
    [InlineData("12 1{0} obj", '0', false)]
    [InlineData("x{0}12 0 obj", '0', false)]
    [InlineData("12{0}x 0 obj", ' ', false)]
    public void A_header_whose_runs_span_whole_reads_of_the_search_is_decided_as_its_bytes_say(string text, char fill, bool header)
    {
        // White space or digits that run on from inside the window across its edge and the whole of the first read past
        // it: each read carries into the next what it cuts of a header, however long, and the search decides it as it
        // would the bytes whole. Zeros leading a number or a generation change nothing; other digits make it too large;
        // a regular character before the number, or in place of a token, makes none.
        var spread = string.Format(CultureInfo.InvariantCulture, text, new string(fill, 5 * Window));
        var data = Data(Window / 2);
        var declared = data.Length + spread.Length + 400;
        var file = Document(
            (StreamNumber, Stream(declared, data, end: "\n" + spread + "\nendstream")),
            (6, "(" + new string('p', 2000) + ")"));
        var dataStart = DataStartOf(file, StreamNumber);

        using var document = PdfDocument.Open(file);
        var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        document.Reader.TryGetStreamLengthFault(StreamNumber, out var fault).Should().BeTrue();

        if (header)
        {
            stream.Data.Length.Should().Be(declared);
            fault.NextObject.Should().Be(dataStart + data.Length + 1, "the header starts where its number does, its zeros with it");
        }
        else
        {
            stream.Data.Length.Should().Be(data.Length + 1 + spread.Length);
            fault.EndStream.Should().Be(EndStreamState.Found);
        }
    }

    [Theory]
    [InlineData(false, 24_576, "{0} 0 obj", null)]
    [InlineData(true, 24_576, "{0} 0 obj", null)]
    [InlineData(false, 10_000, "{0} 0 obj", null)]
    [InlineData(true, 10_000, "{0} 0 obj", null)]
    [InlineData(false, 24_576, "{0}" + Spaces + "0 obj", null)]
    [InlineData(true, 24_576, "{0}" + Spaces + "0 obj", null)]
    [InlineData(false, 24_576, "{0}\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\00 obj", null)]
    [InlineData(true, 24_576, "{0}\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\00 obj", null)]
    [InlineData(false, 24_576, "{0} 000000 obj", null)]
    [InlineData(true, 24_576, "{0} 000000 obj", null)]
    [InlineData(false, 1_500, "{0} 0 obj", 1024)]
    [InlineData(true, 1_500, "{0} 0 obj", 1024)]
    public void Streams_only_a_rebuilt_index_places_stop_at_the_next_header_and_hold_the_same_data_whatever_is_read_first(
        bool rebuiltFirst, int length, string header, int? maxObjectLength)
    {
        // The review's file C2. Objects 10 to 38 are streams that lost their endstream, and object 39's endstream follows
        // them, all after object 9's endobj, where the index as written places nothing; object 8's entry leads nowhere,
        // and asking for it rebuilds the index, which places them. Object 50, which the index as written places after them
        // all, declares 100 bytes more than its data, which its endstream follows. Bounded by the index as written alone,
        // each of 10 to 38 ran to object 39's endstream: the searches read the file over and over and spent the document's
        // budget, object 50 was searched only when read before the rebuild, and it held 30,000 bytes or 30,100 according
        // to the order. Each stream now stops at the header of the one after it, and holds the same data in either order,
        // however the headers are written that both the rebuild and the parser take: more white space than a read of the
        // search repeats, zeros leading the generation. The searches cannot know where that header is before they read
        // it: reading the file 64 KB at a time, or 16 KB at first whatever window the object was parsed through, the
        // searches of streams of 10,000 bytes, or of 1,500 bytes read through a window of 1 KB, would read the file more
        // than four times over, and spend the budget again; each read of a search is twice as large as the one before,
        // from twice what the parser holds of the data, and the searches read twice the file at most.
        var data = Data(length);
        var fifty = Data(30_000);
        var hidden = new StringBuilder("(nine)\nendobj\n");

        for (var number = 10; number < 39; number++)
        {
            hidden.Append(Header(header, number)).Append('\n').Append(Stream(data.Length + 100, data, end: string.Empty)).Append("\nendobj\n");
        }

        hidden.Append(Header(header, 39)).Append('\n').Append(Stream(5, "hello"));

        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(8, "(eight)")
            .WithObject(9, hidden.ToString())
            .WithObject(50, Stream(fifty.Length + 100, fifty))
            .WithObject(51, "(" + new string('q', 300) + ")")
            .BuildClassic(rootNumber: 1);

        Rewrite(file, 8, 10_000_000);

        var options = maxObjectLength is { } most
            ? PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxObjectLength = most } }
            : PdfReaderOptions.Default;
        using var document = PdfDocument.Open(file, options);
        int[] first = rebuiltFirst ? [8, 50] : [50, 8];

        foreach (var number in first.Concat(Enumerable.Range(10, 29)))
        {
            document.GetObject(new PdfObjectId(number));
        }

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(50)).AsStream().Required().Data.Length.Should().Be(fifty.Length);
        var text = Encoding.Latin1.GetString(file);

        for (var number = 10; number < 39; number++)
        {
            document.GetObject(new PdfObjectId(number)).AsStream().Required().Data.Length.Should().Be(data.Length + 100);
            document.Reader.TryGetStreamLengthFault(number, out var fault).Should().BeTrue();
            fault.EndStream.Should().Be(EndStreamState.MissingBeforeNextObject, "the document's searches have read far less than they may");
            fault.NextObject.Should().Be(text.IndexOf("\n" + Header(header, number + 1) + "\n", StringComparison.Ordinal) + 1);
        }

        document.Reader.TryGetStreamLengthFault(50, out var fiftyFault).Should().BeTrue();
        fiftyFault.EndStream.Should().Be(EndStreamState.Found);

        static string Header(string format, int number) => string.Format(CultureInfo.InvariantCulture, format, number);
    }

    [Theory]
    [InlineData("\nendobj\n", false)]
    [InlineData("\r\n% a comment between the keywords\r\nendobj\n", false)]
    [InlineData("\nendobx\n", true)]
    [InlineData("\n8 0 obj\n", true)]
    [InlineData("x\nendobj\n", true)]
    public void What_follows_the_endstream_of_a_stream_past_the_window_is_seen(string afterEndStream, bool missing)
    {
        // What follows a stream longer than the window was never seen, and endobj never judged there (#55's note in the
        // validation rules): the file is asked for a few bytes more than the endstream, and says.
        var data = Data(3 * Window);
        var file = PdfTemplate.Build(Invariant($"""
            %PDF-1.7
            1 0 obj
            << /Type /Catalog /Pages 2 0 R /Metadata 5 0 R >>
            endobj
            2 0 obj
            << /Type /Pages /Kids [] /Count 0 >>
            endobj
            5 0 obj
            << /Length {data.Length} >>
            stream
            {data}
            endstream{afterEndStream}xref
            0 6
            {"{free}"}
            {"{row:1}"}
            {"{row:2}"}
            {"{free}"}
            {"{free}"}
            {"{row:5}"}
            trailer
            << /Size 6 /Root 1 0 R >>
            startxref
            {"{xref:1}"}
            %%EOF

            """));

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required().Data.Length.Should().Be(data.Length);

        document.Reader.IsEndObjMissing(StreamNumber, out _).Should().Be(missing);
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.ObjectEndObjMissing).Should().Be(missing);
    }

    [Theory]
    [InlineData("\nendobj\n", false)]
    [InlineData("\n8 0 obj\n", true)]
    public void What_follows_an_endstream_the_search_found_is_seen(string afterEndStream, bool missing)
    {
        // The declared length is 40 bytes short: the search finds the endstream, and the file is asked what follows it.
        var data = Data(3 * Window);
        var file = PdfTemplate.Build(Invariant($"""
            %PDF-1.7
            1 0 obj
            << /Type /Catalog /Pages 2 0 R /Metadata 5 0 R >>
            endobj
            2 0 obj
            << /Type /Pages /Kids [] /Count 0 >>
            endobj
            5 0 obj
            << /Length {data.Length - 40} >>
            stream
            {data}
            endstream{afterEndStream}xref
            0 6
            {"{free}"}
            {"{row:1}"}
            {"{row:2}"}
            {"{free}"}
            {"{free}"}
            {"{row:5}"}
            trailer
            << /Size 6 /Root 1 0 R >>
            startxref
            {"{xref:1}"}
            %%EOF

            """));

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required().Data.Length.Should().Be(data.Length);

        document.Reader.TryGetStreamLengthFault(StreamNumber, out var fault).Should().BeTrue();
        fault.EndStream.Should().Be(EndStreamState.Found);
        document.Reader.IsEndObjMissing(StreamNumber, out _).Should().Be(missing);
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.ObjectEndObjMissing).Should().Be(missing);
    }

    [Fact]
    public void What_follows_the_endstream_of_a_stream_past_the_window_is_unseen_past_a_few_bytes()
    {
        var data = Data(3 * Window);
        var file = Document((StreamNumber, Stream(data.Length, data, end: "\nendstream" + new string(' ', PdfObjectParser.EndObjLookahead))));

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        document.Reader.IsEndObjMissing(StreamNumber, out _).Should().BeFalse("what follows is not seen, and not judged");
    }

    [Fact]
    public void A_stream_past_the_window_that_the_end_of_the_file_follows_lacks_its_endobj()
    {
        // No index, no endobj: the file ends after endstream, which the reader reads to its end.
        var data = Data(3 * Window);
        var file = Encoding.ASCII.GetBytes(
            "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n" +
            Invariant($"5 0 obj\n<< /Length {data.Length} >>\nstream\n{data}\nendstream"));

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required().Data.Length.Should().Be(data.Length);

        document.Reader.IsEndObjMissing(StreamNumber, out _).Should().BeTrue();
        document.Diagnostics.Contains(PdfDiagnosticCodes.StreamLengthInvalid).Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_stream_whose_data_ends_at_the_window_edge_or_just_past_it_is_confirmed_for_a_few_bytes_more(int pastEdge)
    {
        // Up to the edge, the window holds the data and the file is asked for the endstream after it (T21); past it,
        // the file is asked for the endstream and what follows. Neither searches.
        var header = Invariant($"{StreamNumber} 0 obj\n");
        var prefix = "<< /Length 0000 >>\nstream\n";
        var length = Window + pastEdge - header.Length - prefix.Length;
        var data = Data(length);
        var file = Document((StreamNumber, Stream(length, data, width: 4)), (6, "(" + new string('p', 2 * Window) + ")"));

        using var source = new StrictCountingSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var beforeLoading = source.BytesRead;

        var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        (source.BytesRead - beforeLoading).Should().BeLessThanOrEqualTo(Window + PdfObjectParser.EndObjLookahead);
        stream.Data.Length.Should().Be(length);
        document.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(24_576)]
    public void A_stream_whose_data_holds_endstream_is_confirmed_at_its_declared_end_and_never_searched(int length)
    {
        // An embedded PDF carries its own streams: the first endstream after the data's start is theirs, and the declared
        // length, which endstream follows, is what the file says — inside the window and past it.
        var embedded = "%PDF-1.4\n1 0 obj\n<< /Length 4 >>\nstream\nabcd\nendstream\nendobj\n";
        var data = embedded + Data(length - embedded.Length);
        var file = Document((StreamNumber, Stream(data.Length, data)), (6, "(" + new string('p', 4 * Window) + ")"));

        using var source = new StrictCountingSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var beforeLoading = source.BytesRead;

        var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        (source.BytesRead - beforeLoading).Should().BeLessThanOrEqualTo(Window + PdfObjectParser.EndObjLookahead);
        stream.Data.Length.Should().Be(data.Length);
        document.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_stream_whose_length_is_wrong_is_searched_and_reported_once_however_often_the_cache_lets_it_go(bool pastWindow)
    {
        // Inside the window, a stream the cache let go was reported again each time it was parsed; past it, the search
        // would read the file again. Seventy objects read in between let it go from a cache of 64.
        var data = Data(pastWindow ? 3 * Window : 200);
        var objects = new List<(int, string)> { (StreamNumber, Stream(data.Length - 40, data)) };

        for (var number = 10; number < 80; number++)
        {
            objects.Add((number, Invariant($"({number})")));
        }

        var file = Document([.. objects]);

        using var source = new StrictCountingSource(file);
        using var document = PdfDocument.Open(source, PdfReaderOptions.Default with { ObjectCacheCapacity = 64 }, ownsSource: false);
        var first = document.GetObject(new PdfObjectId(StreamNumber));

        for (var number = 10; number < 80; number++)
        {
            document.GetObject(new PdfObjectId(number));
        }

        var beforeAgain = source.BytesRead;
        var again = document.GetObject(new PdfObjectId(StreamNumber));

        again.Should().NotBeSameAs(first, "the cache let the stream go, and it was parsed again");
        again.AsStream().Required().Data.Length.Should().Be(data.Length);
        (source.BytesRead - beforeAgain).Should().BeLessThanOrEqualTo(Window + PdfObjectParser.EndObjLookahead, "the file is not searched again");
        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.StreamLengthInvalid);
        document.Reader.TryGetStreamLengthFault(StreamNumber, out var fault).Should().BeTrue();
        fault.Found.Should().Be(data.Length);
    }

    [Fact]
    public void A_stream_whose_length_is_wrong_is_searched_and_reported_once_when_the_index_is_rebuilt()
    {
        // Object 6's entry leads nowhere near it: asking for it rebuilds the index, which lets every object go, and the
        // rebuild reads the stream again.
        var data = Data(3 * Window);
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(StreamNumber, Stream(data.Length - 40, data))
            .WithObject(6, "(six)")
            .BuildClassic(rootNumber: 1);
        var row = Encoding.Latin1.GetString(file).LastIndexOf(Invariant($"{HeaderOf(file, 6):D10} 00000 n"), StringComparison.Ordinal);
        Encoding.ASCII.GetBytes("0000000009").CopyTo(file, row);
        var dataStart = DataStartOf(file, StreamNumber);

        using var source = new LoggingSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        source.Forget();
        document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        document.GetObject(new PdfObjectId(6)).Should().BeOfType<PdfString>();
        document.WasRepaired.Should().BeTrue();
        var again = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        again.Data.Length.Should().Be(data.Length);
        source.Searches(dataStart, data.Length).Should().Be(1, "the file was searched once");
        document.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid).Should().ContainSingle();
    }

    [Fact]
    public void A_report_the_first_reading_made_survives_that_reading_being_dropped()
    {
        // Object 7's entry places it where object 8 starts: the reader parses object 8 there — searching its stream —,
        // finds the wrong number, and drops the reading with what it reported. Object 8, read for its own sake, is
        // reported, from what the first search found.
        var data = Data(3 * Window);
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(7, "(" + new string('p', 2000) + ")")
            .WithObject(8, Stream(data.Length - 40, data))
            .BuildClassic(rootNumber: 1);
        var text = Encoding.Latin1.GetString(file);
        var row = text.LastIndexOf(Invariant($"{HeaderOf(file, 7):D10} 00000 n"), StringComparison.Ordinal);
        Encoding.ASCII.GetBytes(Invariant($"{HeaderOf(file, 8):D10}")).CopyTo(file, row);
        var dataStart = DataStartOf(file, 8);

        using var source = new LoggingSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        source.Forget();

        document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfString>();
        document.GetObject(new PdfObjectId(8)).AsStream().Required().Data.Length.Should().Be(data.Length);

        document.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid).Should().ContainSingle()
            .Which.Position.Should().Be(dataStart);
        source.Searches(dataStart, data.Length).Should().Be(1, "the file was searched once");
    }

    public static TheoryData<string, string?, string, string, string?, string?> LengthForms => new()
    {
        { string.Empty, null, "The stream has no /Length; its data ends after 5 bytes.", nameof(StreamLengthForm.Absent), null, null },
        { "/Length 5.5", null, "The stream's /Length is a real number, 5.5, not a non-negative integer; its data ends after 5 bytes.", nameof(StreamLengthForm.NotAnInteger), "a real number", "5.5" },
        { "/Length /Five", null, "The stream's /Length is a name, /Five, not a non-negative integer; its data ends after 5 bytes.", nameof(StreamLengthForm.NotAnInteger), "a name", "/Five" },
        { "/Length (5)", null, "The stream's /Length is a string, not a non-negative integer; its data ends after 5 bytes.", nameof(StreamLengthForm.NotAnInteger), "a string", null },
        { "/Length [5]", null, "The stream's /Length is an array, not a non-negative integer; its data ends after 5 bytes.", nameof(StreamLengthForm.NotAnInteger), "an array", null },
        { "/Length true", null, "The stream's /Length is a boolean, true, not a non-negative integer; its data ends after 5 bytes.", nameof(StreamLengthForm.NotAnInteger), "a boolean", "true" },
        { "/Length false", null, "The stream's /Length is a boolean, false, not a non-negative integer; its data ends after 5 bytes.", nameof(StreamLengthForm.NotAnInteger), "a boolean", "false" },
        { "/Length << /N 5 >>", null, "The stream's /Length is a dictionary, not a non-negative integer; its data ends after 5 bytes.", nameof(StreamLengthForm.NotAnInteger), "a dictionary", null },
        { "/Length -5", null, "The stream's /Length is -5, a length no stream can have; its data ends after 5 bytes.", nameof(StreamLengthForm.OutOfRange), null, null },
        { "/Length 3000000000", null, "The stream's /Length is 3000000000, more than the reader can take as a length; its data ends after 5 bytes.", nameof(StreamLengthForm.OutOfRange), null, null },
        { "/Length 9 0 R", null, "The stream's /Length names object 9 0, which the file lacks; its data ends after 5 bytes.", nameof(StreamLengthForm.ObjectMissing), null, null },
        { "/Length 0 0 R", null, "The stream's /Length names object 0 0, which the file lacks; its data ends after 5 bytes.", nameof(StreamLengthForm.ObjectMissing), null, null },
        { "/Length 5 0 R", null, "The stream's /Length names object 5 0, which could not be read; its data ends after 5 bytes.", nameof(StreamLengthForm.ObjectUnreadable), null, null },
        { "/Length 6 0 R", "5.5", "The stream's /Length names object 6 0, which holds a real number, 5.5, not a non-negative integer; its data ends after 5 bytes.", nameof(StreamLengthForm.NotAnInteger), "a real number", "5.5" },
        { "/Length 6 0 R", "<< /N 5 >>", "The stream's /Length names object 6 0, which holds a dictionary, not a non-negative integer; its data ends after 5 bytes.", nameof(StreamLengthForm.NotAnInteger), "a dictionary", null },
        { "/Length 6 0 R", "<< /Length 1 >>\nstream\nx\nendstream", "The stream's /Length names object 6 0, which holds a stream, not a non-negative integer; its data ends after 5 bytes.", nameof(StreamLengthForm.NotAnInteger), "a stream", null },
        { "/Length 6 0 R", "null", "The stream's /Length names object 6 0, which holds null, not a non-negative integer; its data ends after 5 bytes.", nameof(StreamLengthForm.NotAnInteger), "null", null },
        { "/Length 6 0 R", "-5", "The stream's /Length names object 6 0, which holds -5, a length no stream can have; its data ends after 5 bytes.", nameof(StreamLengthForm.OutOfRange), null, null },
        { "/Length 6 0 R", "3", "The stream declared 3 bytes but ended after 5.", nameof(StreamLengthForm.Integer), null, null },
    };

    [Theory]
    [MemberData(nameof(LengthForms))]
    public void A_length_that_cannot_be_taken_is_reported_as_the_file_wrote_it(
        string entry, string? named, string message, string form, string? kind, string? value)
    {
        // #120: every one of these used to say the stream "declared -1 bytes".
        var objects = new List<(int, string)> { (StreamNumber, Invariant($"<< {entry} >>\nstream\nhello\nendstream")) };

        if (named is not null)
        {
            objects.Add((6, named));
        }

        var file = Document([.. objects]);

        using var document = PdfDocument.Open(file);
        var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        Encoding.ASCII.GetString(stream.GetRawBytes().Span).Should().Be("hello");
        var diagnostic = document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid).Which;
        diagnostic.Message.Should().Be(message);
        diagnostic.Position.Should().Be(DataStartOf(file, StreamNumber));

        document.Reader.TryGetStreamLengthFault(StreamNumber, out var fault).Should().BeTrue();
        fault.Form.ToString().Should().Be(form);
        fault.Kind.Should().Be(kind);
        (fault.Value?.ToString()).Should().Be(value);
        fault.Reference.Should().Be(entry.EndsWith(" R", StringComparison.Ordinal) ? new PdfObjectId(entry[8] - '0') : null);
        fault.Declared.Should().Be(form is nameof(StreamLengthForm.Integer) or nameof(StreamLengthForm.OutOfRange)
            ? long.Parse(named ?? entry["/Length ".Length..], CultureInfo.InvariantCulture)
            : null);
        fault.Taken.Should().Be(5);
        fault.Found.Should().Be(5);
        fault.EndStream.Should().Be(EndStreamState.Found);
    }

    [Fact]
    public void A_length_written_as_a_name_holding_control_characters_is_quoted_escaped()
    {
        // A name gives itself any byte through #xx: the message writes them back that way, and holds none (#159).
        var file = Document((StreamNumber, "<< /Length /X#0Aforged#20line#1B#9B >>\nstream\nhello\nendstream"));

        using var document = PdfDocument.Open(file);
        _ = document.GetObject(new PdfObjectId(StreamNumber));

        var diagnostic = document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid).Which;
        diagnostic.Message.Should().Be(
            "The stream's /Length is a name, /X#0Aforged#20line#1B#9B, not a non-negative integer; its data ends after 5 bytes.");
        diagnostic.ToString().Should().MatchRegex("^[ -~]*$");
    }

    [Fact]
    public void A_stream_inside_an_object_stream_quotes_its_length_s_name_escaped()
    {
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "<< /Length /Q#0Aw#1B >>\nstream\nabc\nendstream")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3]);

        using var document = PdfDocument.Open(file);
        _ = document.GetObject(new PdfObjectId(3));

        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid)
            .Which.Message.Should().StartWith("The stream's /Length is a name, /Q#0Aw#1B, not a non-negative integer;").And.MatchRegex("^[ -~]*$");
    }

    [Fact]
    public void A_length_naming_an_object_the_index_places_nowhere_could_not_be_read()
    {
        // The entry is in use and leads outside the file: the object is the index's to produce, and it could not.
        var file = Document((StreamNumber, "<< /Length 6 0 R >>\nstream\nhello\nendstream"), (6, "5"));
        var row = Encoding.Latin1.GetString(file).LastIndexOf(Invariant($"{HeaderOf(file, 6):D10} 00000 n"), StringComparison.Ordinal);
        Encoding.ASCII.GetBytes("9999999999").CopyTo(file, row);
        Array.Clear(file, (int)HeaderOf(file, 6), "6 0 obj".Length);

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid)
            .Which.Message.Should().Be("The stream's /Length names object 6 0, which could not be read; its data ends after 5 bytes.");
    }

    [Fact]
    public void A_length_an_object_stream_holds_for_itself_could_not_be_read()
    {
        // The object stream's /Length names object 5, which the stream holds: it cannot be read before the stream is.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, "0")
            .WithObject(6, "<< /Title (six) >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [5, 6], objectStreamEntries: "/Pad (0123456789)");
        var text = Encoding.Latin1.GetString(file);
        var dictionary = text.IndexOf("/Pad (0123456789) /Length ", StringComparison.Ordinal);
        var end = text.IndexOf(" >>", dictionary, StringComparison.Ordinal);
        text = text[..dictionary] + "/Length 5 0 R".PadRight(end - dictionary) + text[end..];

        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));
        document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfStream>();

        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid)
            .Which.Message.Should().StartWith("The stream's /Length names object 5 0, which could not be read; its data ends after ");
        document.Reader.TryGetStreamLengthFault(7, out var fault).Should().BeTrue();
        fault.Form.Should().Be(StreamLengthForm.ObjectUnreadable);
        fault.Reference.Should().Be(new PdfObjectId(5));
    }

    [Theory]
    [InlineData("<< /Length 9 0 R >>\nstream\nhello\nendstream", "The stream's /Length names object 9 0, which could not be read; its data ends after 5 bytes.")]
    [InlineData("<< /Length 6 0 R >>\nstream\nhello\nendstream", "The stream declared 3 bytes but ended after 5.")]
    public void A_parser_alone_says_a_reference_it_cannot_resolve_could_not_be_read(string body, string message)
    {
        // Without the reader, the parser cannot tell an object the file lacks from one it could not produce.
        var source = Substitute.For<IPdfObjectSource>();
        source.GetObject(new PdfObjectId(6)).Returns(PdfInteger.Create(3));
        source.GetObject(new PdfObjectId(9)).Returns(PdfNull.Instance);
        var diagnostics = new PdfDiagnostics();

        var stream = new PdfObjectParser(Encoding.ASCII.GetBytes(body), source: source, diagnostics: diagnostics).ParseObject();

        stream.AsStream().Required().Data.Length.Should().Be(5);
        diagnostics.Should().ContainSingle().Which.Message.Should().Be(message);
    }

    [Fact]
    public void A_parser_without_diagnostics_reads_a_stream_whose_length_is_wrong_to_its_endstream()
    {
        var stream = new PdfObjectParser(Encoding.ASCII.GetBytes("<< /Length 3 >>\nstream\nhello\nendstream")).ParseObject();

        stream.AsStream().Required().Data.Length.Should().Be(5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_stream_that_endobj_follows_without_its_endstream_says_what_it_takes(bool pastWindow)
    {
        // iPRES's t02-05-01-014: the data runs to the end of the file, which the endobj after it does not stop.
        var data = Data(pastWindow ? 3 * Window : 60);
        var file = Encoding.ASCII.GetBytes(
            "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n" +
            Invariant($"5 0 obj\n<< /Length {data.Length + 20000} >>\nstream\n{data}\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n"));
        var dataStart = DataStartOf(file, StreamNumber);
        var rest = file.Length - dataStart;

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required().Data.Length.Should().Be((int)rest);

        var diagnostic = document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.StreamTruncated).Which;
        diagnostic.Message.Should().Be(Invariant($"The stream has no endstream before the endobj that follows its data; the {rest} bytes to the end of the file are taken as its data."));
        diagnostic.Position.Should().Be(dataStart);
        document.Reader.TryGetStreamLengthFault(StreamNumber, out var fault).Should().BeTrue();
        fault.EndStream.Should().Be(EndStreamState.MissingBeforeEndObj);
        fault.Taken.Should().Be((int)rest);
        fault.Found.Should().BeNull();
    }

    [Fact]
    public void A_stream_the_end_of_the_file_cuts_records_what_it_took()
    {
        var data = Data(200);
        var file = Encoding.ASCII.GetBytes(
            "%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n2 0 obj << /Type /Pages /Kids [] /Count 0 >> endobj\n" +
            Invariant($"5 0 obj << /Length 20000 >>\nstream\n{data}"));

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required().Data.Length.Should().Be(data.Length);

        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.StreamTruncated)
            .Which.Message.Should().Be("A stream ran past the end of the file.");
        document.Reader.TryGetStreamLengthFault(StreamNumber, out var fault).Should().BeTrue();
        fault.Should().Be(new StreamLengthFault
        {
            Form = StreamLengthForm.Integer,
            Declared = 20000,
            Taken = data.Length,
            EndStream = EndStreamState.MissingBeforeEndOfFile,
            DataStart = DataStartOf(file, StreamNumber),
        });
    }

    [Fact]
    public void A_stream_a_limit_cuts_records_nothing_of_its_length()
    {
        // The window stopped at the guard, not at the end of the file: what the parser found there is the limit's.
        var file = Document((StreamNumber, "<< >>\nstream\n" + Data(3 * Window) + "\nendstream"));
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxObjectLength = Window } };

        using var document = PdfDocument.Open(file, options);
        document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.LimitObject);
        document.Reader.TryGetStreamLengthFault(StreamNumber, out _).Should().BeFalse();
    }

    [Fact]
    public void A_stream_read_sound_after_the_index_is_rebuilt_clears_what_was_recorded_of_its_number()
    {
        // The chain places object 5 at a copy whose length is wrong; the rebuild, keeping the last definition, finds a
        // sound one. Object 7's entry leads nowhere near it, and asking for it rebuilds the index.
        var file = TestPdfBuilder.AppendIncrementalUpdate(
            Document((StreamNumber, "<< /Length 3 >>\nstream\nhello\nendstream"), (6, "(" + new string('p', 2000) + ")")),
            rootNumber: 1,
            [(StreamNumber, "<< /Length 5 >>\nstream\nhello\nendstream"), (7, "(seven)")]);
        var text = Encoding.Latin1.GetString(file);
        var update = text.LastIndexOf("\n5 0 obj", StringComparison.Ordinal) + 1;
        var row = text.LastIndexOf(Invariant($"{update:D10} 00000 n"), StringComparison.Ordinal);
        Encoding.ASCII.GetBytes(Invariant($"{text.IndexOf("\n5 0 obj", StringComparison.Ordinal) + 1:D10}")).CopyTo(file, row);
        var seven = text.LastIndexOf(Invariant($"{text.LastIndexOf("\n7 0 obj", StringComparison.Ordinal) + 1:D10} 00000 n"), StringComparison.Ordinal);
        Encoding.ASCII.GetBytes("0000000009").CopyTo(file, seven);

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(StreamNumber));
        document.Reader.TryGetStreamLengthFault(StreamNumber, out _).Should().BeTrue("the chain places the copy whose length is wrong");

        document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfString>();
        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required().Data.Length.Should().Be(5);

        document.Reader.TryGetStreamLengthFault(StreamNumber, out _).Should().BeFalse("the copy read now is sound");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_next_object_a_search_stops_at_is_placed_from_the_header_junk_precedes(bool endStreamFollowsData)
    {
        // Six hundred bytes of junk come before %PDF-, and the index counts from there. Declared 300 bytes short, the
        // stream's endstream lies past where object 6's entry would place it counted from the start of the file: it is
        // found only when the bound is placed from the header. Without an endstream, the report names where object 6
        // starts in the file, and the search stops there rather than at object 6's endstream.
        const int Junk = 600;
        var data = Data(3 * Window);
        var built = endStreamFollowsData
            ? Document((StreamNumber, Stream(data.Length - 300, data)), (6, "(" + new string('p', 2000) + ")"))
            : Document((StreamNumber, Stream(data.Length + 100, data, end: "\n")), (6, Stream(5, "hello")));
        var file = new byte[Junk + built.Length];
        file.AsSpan(0, Junk - 1).Fill((byte)'j');
        file[Junk - 1] = (byte)'\n';
        built.CopyTo(file, Junk);
        var next = HeaderOf(file, 6);

        using var document = PdfDocument.Open(file);
        var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        document.Reader.HeaderOffset.Should().Be(Junk);
        document.Reader.TryGetStreamLengthFault(StreamNumber, out var fault).Should().BeTrue();
        var message = document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid).Which.Message;

        if (endStreamFollowsData)
        {
            stream.Data.Length.Should().Be(data.Length);
            fault.EndStream.Should().Be(EndStreamState.Found);
        }
        else
        {
            stream.Data.Length.Should().Be(data.Length + 100);
            fault.NextObject.Should().Be(next);
            message.Should().Be(
                Invariant($"The stream declared {data.Length + 100} bytes, and no endstream follows them before the next object, at {next}; the declared length is kept."));
        }
    }

    [Fact]
    public void An_entry_that_places_a_stream_inside_its_own_data_does_not_end_its_search()
    {
        // Object 5's entry lies 200 bytes past its header, inside its data: the reader finds the object nearby, and the
        // offset the entry gave is no next object — the endstream after the data ends it.
        var data = Data(3 * Window);
        var file = Document((StreamNumber, Stream(data.Length - 40, data)));
        var header = HeaderOf(file, StreamNumber);
        var row = Encoding.Latin1.GetString(file).LastIndexOf(Invariant($"{header:D10} 00000 n"), StringComparison.Ordinal);
        Encoding.ASCII.GetBytes(Invariant($"{header + 200:D10}")).CopyTo(file, row);

        using var document = PdfDocument.Open(file);
        var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        stream.Data.Length.Should().Be(data.Length);
        document.Diagnostics.Contains(PdfDiagnosticCodes.XRefOffsetAdjusted).Should().BeTrue();
        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid).Which.Message.Should().Be(
            Invariant($"The stream declared {data.Length - 40} bytes but ended after {data.Length}."));
    }

    [Fact]
    public void A_stream_another_entry_leads_to_is_searched_once_whatever_its_own_entry_says()
    {
        // Objects 3 and 4's rows lead to the headers of streams 6 and 12, whose lengths are 40 bytes short: 6's own row is
        // free, and 12 has none. Each is searched as the reader parses it there, under its own number, and the reading,
        // of the wrong object, is dropped; the index rebuilt, each read for its own sake is what that search found.
        var data = Data(3 * Window);
        var file = PdfTemplate.Build(Invariant($"""
            %PDF-1.7
            1 0 obj
            << /Type /Catalog /Pages 2 0 R >>
            endobj
            2 0 obj
            << /Type /Pages /Kids [] /Count 0 >>
            endobj
            6 0 obj
            << /Length {data.Length - 40} >>
            stream
            {data}
            endstream
            endobj
            12 0 obj
            << /Length {data.Length - 40} >>
            stream
            {data}
            endstream
            endobj
            xref
            0 8
            {"{free}"}
            {"{row:1}"}
            {"{row:2}"}
            {"{row:6}"}
            {"{row:12}"}
            {"{free}"}
            {"{free}"}
            {"{free}"}
            trailer
            << /Size 8 /Root 1 0 R >>
            startxref
            {"{xref:1}"}
            %%EOF

            """));

        using var source = new LoggingSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        source.Forget();

        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        document.GetObject(new PdfObjectId(4)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeTrue();

        foreach (var number in new[] { 6, 12 })
        {
            var dataStart = DataStartOf(file, number);
            document.GetObject(new PdfObjectId(number)).AsStream().Required().Data.Length.Should().Be(data.Length);
            source.Searches(dataStart, data.Length).Should().Be(1, "the file was searched once for each stream");
            document.Diagnostics.Should().ContainSingle(d => d.Position == dataStart).Which.Message.Should().Be(
                Invariant($"The stream declared {data.Length - 40} bytes but ended after {data.Length}."));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_stream_past_the_window_holds_the_same_data_whether_it_is_read_before_or_after_the_index_is_rebuilt(bool rebuiltFirst)
    {
        // Object 5 lost its endstream. Object 7, which the index as written marks free, follows it; object 8's entry
        // leads nowhere near it, and asking for it rebuilds the index, which places object 7. The index as written places
        // nothing between object 5 and object 6, but the bytes hold object 7's header: read before the rebuild or after
        // it, the search stops there, and the stream keeps its declared length rather than object 7's header and data. The
        // rebuilt index is not asked, which would have bounded a stream read after the rebuild only: its data would depend
        // on what was read first.
        var data = Data(3 * Window);
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(StreamNumber, Stream(data.Length + 400, data, end: string.Empty))
            .WithObject(7, Stream(5, "hello"))
            .WithObject(6, "(" + new string('p', 2000) + ")")
            .WithObject(8, "(eight)")
            .BuildClassic(rootNumber: 1);
        Rewrite(file, 7, null);
        Rewrite(file, 8, 9);
        var seven = HeaderOf(file, 7);

        using var document = PdfDocument.Open(file);

        if (rebuiltFirst)
        {
            document.GetObject(new PdfObjectId(8)).Should().BeOfType<PdfString>();
            document.WasRepaired.Should().BeTrue();
        }

        var first = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();
        document.GetObject(new PdfObjectId(8)).Should().BeOfType<PdfString>();
        document.WasRepaired.Should().BeTrue();
        var again = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        first.Data.Length.Should().Be(data.Length + 400);
        again.Data.Length.Should().Be(data.Length + 400);
        document.GetObject(new PdfObjectId(7)).AsStream().Required().Data.Length.Should().Be(5, "the rebuilt index serves object 7 in its own right");
        document.Reader.TryGetStreamLengthFault(StreamNumber, out var fault).Should().BeTrue();
        fault.NextObject.Should().Be(seven);
        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid).Which.Message.Should().Be(
            Invariant($"The stream declared {data.Length + 400} bytes, and no endstream follows them before the next object, at {seven}; the declared length is kept."));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_stream_past_the_window_stops_where_the_index_as_written_places_the_next_object_whether_or_not_it_was_found_elsewhere_first(bool nextReadFirst)
    {
        // As the IBM QMF manual's entries do, object 16's points one byte into its header, at "6 0 obj": reading object 16
        // finds it a byte before, and corrects the index the reader reads with. The search for object 5's endstream, which
        // it lost, is bounded by the index as the file wrote it, and stops at the same place whether object 16 was read and
        // found first or not.
        var data = Data(3 * Window);
        var file = Document((StreamNumber, Stream(data.Length + 100, data, end: "\n")), (16, "(" + new string('p', 200) + ")"));
        var header = HeaderOf(file, 16);
        var row = Encoding.Latin1.GetString(file).LastIndexOf(Invariant($"{header:D10} 00000 n"), StringComparison.Ordinal);
        Encoding.ASCII.GetBytes(Invariant($"{header + 1:D10}")).CopyTo(file, row);

        using var document = PdfDocument.Open(file);

        if (nextReadFirst)
        {
            document.GetObject(new PdfObjectId(16)).Should().BeOfType<PdfString>();
            document.Reader.Index.TryGet(16, out var corrected).Should().BeTrue();
            corrected.Offset.Should().Be(header);
        }

        var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        stream.Data.Length.Should().Be(data.Length + 100);
        document.Reader.TryGetStreamLengthFault(StreamNumber, out var fault).Should().BeTrue();
        fault.NextObject.Should().Be(header + 1, "the index as written places object 16 there");
        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid).Which.Message.Should().Be(
            Invariant($"The stream declared {data.Length + 100} bytes, and no endstream follows them before the next object, at {header + 1}; the declared length is kept."));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_index_rebuilt_as_the_document_opened_bounds_the_searches_as_it_stood_once_opened_whatever_is_read_after(bool reverse)
    {
        // No index was written. Object 8's string holds "7 0 objx", which the rebuild takes for object 7's last definition:
        // object 7 is not there, and is found near it as the document opens, which corrects its entry. The rebuild loads
        // every object it places as it takes in the object streams, so object 5, which lost its endstream, is searched as
        // the document opens, up to object 6. Reading afterwards, in any order, searches nothing and changes neither the
        // index nor what the stream holds: the index as it stood once opened is the one that bounds the searches.
        var data = Data(3 * Window);
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(StreamNumber, Stream(data.Length + 100, data, end: "\n"))
            .WithObject(6, "(" + new string('p', 200) + ")")
            .WithObject(7, Stream(5, "hello"))
            .WithObject(8, "(7 0 objx)")
            .BuildClassic(rootNumber: 1, includeXRef: false);
        var dataStart = DataStartOf(file, StreamNumber);

        using var source = new LoggingSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        source.Forget();

        document.Reader.ChainIndex.Should().BeNull("no index was written");
        document.Diagnostics.Contains(PdfDiagnosticCodes.XRefOffsetAdjusted).Should().BeTrue("object 7 was found near its entry as the document opened");
        document.Reader.Index.TryGet(7, out var seven).Should().BeTrue();
        seven.Offset.Should().Be(HeaderOf(file, 7));
        var opened = new Dictionary<int, XRefEntry>(document.Reader.Index.Entries);

        var numbers = document.ObjectNumbers.Order().ToList();
        foreach (var number in reverse ? Enumerable.Reverse(numbers) : numbers)
        {
            document.GetObject(new PdfObjectId(number));
        }

        document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required().Data.Length.Should().Be(data.Length + 100);
        document.GetObject(new PdfObjectId(7)).AsStream().Required().Data.Length.Should().Be(5);
        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid).Which.Message.Should().Be(
            Invariant($"The stream declared {data.Length + 100} bytes, and no endstream follows them before the next object, at {HeaderOf(file, 6)}; the declared length is kept."));
        source.Searches(dataStart, data.Length).Should().Be(0, "the stream was searched as the document opened");
        document.Reader.Index.Entries.Should().Equal(opened, "nothing read after the document opened changes an index rebuilt as it opened");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_next_object_a_search_stops_at_is_the_same_before_and_after_the_index_is_copied_though_the_chain_corrected_an_entry(bool copiedFirst)
    {
        // The newest section is a cross-reference stream past the first window whose length is 7 bytes long: searching it
        // sorts the offsets of an index still empty. The older one's /Length names object 10, whose entry lies 2 bytes
        // before its header, in the endobj of object 5: reading it finds object 10 there, and corrects the entry while the
        // chain is read, the offset it replaced kept among those sorted. Object 5 lost its endstream; its search stops at
        // object 10's header, where the index places it, whether the reader has copied the index since — object 6's entry
        // points one byte into its header, and reading object 6 corrects it — or not.
        var data = Data(3 * Window);
        var file = CorrectedWhileTheChainIsRead(data, out var tenAt);

        using var document = PdfDocument.Open(file);
        document.Diagnostics.Should().Contain(d => d.Code == PdfDiagnosticCodes.XRefOffsetAdjusted && d.Position == tenAt);

        if (copiedFirst)
        {
            document.GetObject(new PdfObjectId(6)).Should().BeOfType<PdfString>();
            document.Reader.ChainIndex.Should().NotBeSameAs(document.Reader.Index, "correcting object 6's entry copied the index first");
        }

        var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        stream.Data.Length.Should().Be(data.Length + 100);
        document.Reader.TryGetStreamLengthFault(StreamNumber, out var fault).Should().BeTrue();
        fault.NextObject.Should().Be(tenAt);
        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid && d.Position == DataStartOf(file, StreamNumber))
            .Which.Message.Should().Be(
                Invariant($"The stream declared {data.Length + 100} bytes, and no endstream follows them before the next object, at {tenAt}; the declared length is kept."));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void An_endstream_the_edge_between_two_reads_of_the_search_cuts_is_found_whole(string endOfLine)
    {
        // The search reads the file past the window a read at a time, each repeating the last bytes of the one before:
        // the keyword, and the end-of-line before it that the data leaves out, are found whole wherever the edge of the
        // first read cuts them. That read starts a few bytes before the end of the window the object was parsed in.
        var prefix = Invariant($"{StreamNumber} 0 obj\n") + Stream(0, string.Empty, end: string.Empty, width: 7);
        var firstReadEnd = Window - PdfFileReader.EndStreamSearchOverlap + PdfFileReader.EndStreamSearchFirstRead(Window - prefix.Length);

        for (var keyword = firstReadEnd - 20; keyword <= firstReadEnd + 5; keyword++)
        {
            var length = keyword - prefix.Length - endOfLine.Length;
            var data = Data(length);
            var file = Document((StreamNumber, Stream(length - 100, data, end: endOfLine + "endstream", width: 7)));

            using var document = PdfDocument.Open(file);
            var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

            stream.Data.Length.Should().Be(length, Invariant($"the keyword starts {keyword - firstReadEnd} bytes from the edge"));
        }
    }

    [Fact]
    public void Streams_whose_data_share_a_stretch_read_the_file_a_bounded_number_of_times_over()
    {
        // Each stream's header lies inside the dictionary of the one before, as a string: every header comes before every
        // data start, no index entry after any, and each stream's search would read the file to its end. The searches read
        // it a few times over together; then a stream keeps its declared length without a search, and says so.
        const int Streams = 30;
        const int Declared = 100_000;
        var nested = Invariant($"{Streams + 2} 0 obj<</Length {Declared}>>stream\n");

        for (var number = Streams + 1; number >= 3; number--)
        {
            nested = Invariant($"{number} 0 obj<</S({nested})/Length {Declared}>>stream\n");
        }

        var head = "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n" + nested;
        var offsets = new List<long> { 0, 9, head.IndexOf("2 0 obj", StringComparison.Ordinal) };

        for (var number = 3; number <= Streams + 2; number++)
        {
            offsets.Add(head.IndexOf(Invariant($"{number} 0 obj<<"), StringComparison.Ordinal));
        }

        var body = head + new string('x', 1024 * 1024) + "\n";
        var xref = new StringBuilder(Invariant($"xref\n0 {offsets.Count}\n0000000000 65535 f\r\n"));

        foreach (var offset in offsets.Skip(1))
        {
            xref.Append(Invariant($"{offset:D10} 00000 n\r\n"));
        }

        var file = Encoding.Latin1.GetBytes(body + xref + Invariant($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{body.Length}\n%%EOF\n"));

        using var source = new StrictCountingSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var opened = source.BytesRead;

        for (var number = 3; number <= Streams + 2; number++)
        {
            document.GetObject(new PdfObjectId(number)).AsStream().Required().Data.Length.Should().Be(Declared);
        }

        (source.BytesRead - opened).Should().BeLessThan((PdfFileReader.EndStreamSearchPasses + 2) * file.Length, "the searches read the file a few times over, not once for each stream");
        document.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid).Should().HaveCount(Streams);
        document.Reader.TryGetStreamLengthFault(Streams + 2, out var last).Should().BeTrue();
        last.Should().Be(new StreamLengthFault
        {
            Form = StreamLengthForm.Integer,
            Declared = Declared,
            Taken = Declared,
            EndStream = EndStreamState.NotSearched,
            DataStart = head.IndexOf(">>stream\n", StringComparison.Ordinal) + ">>stream\n".Length,
        });
        document.Diagnostics.Should().ContainSingle(d => d.Position == last.DataStart).Which.Message.Should().Be(
            Invariant($"The stream declared {Declared} bytes, and no endstream follows them; the declared length is kept without a search, the document's searches for endstream having read as much of the file as they may."));
    }

    [Fact]
    public void An_index_offset_past_the_end_of_the_file_or_before_the_data_bounds_nothing()
    {
        // Objects 8 and 9 are placed past the end of the file and inside the stream's own dictionary: the search runs to
        // the end of the file, where the endstream is.
        var data = Data(3 * Window);
        var file = Document((StreamNumber, Stream(data.Length - 40, data)), (8, "null"), (9, "null"));
        var text = Encoding.Latin1.GetString(file);
        var eight = text.LastIndexOf(Invariant($"{HeaderOf(file, 8):D10} 00000 n"), StringComparison.Ordinal);
        var nine = text.LastIndexOf(Invariant($"{HeaderOf(file, 9):D10} 00000 n"), StringComparison.Ordinal);
        Encoding.ASCII.GetBytes("9999999999").CopyTo(file, eight);
        Encoding.ASCII.GetBytes(Invariant($"{HeaderOf(file, StreamNumber) + 3:D10}")).CopyTo(file, nine);

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required().Data.Length.Should().Be(data.Length);

        document.Reader.TryGetStreamLengthFault(StreamNumber, out var fault).Should().BeTrue();
        fault.EndStream.Should().Be(EndStreamState.Found);
    }

    [Fact]
    public void Searches_through_an_index_of_a_million_entries_take_bounded_time()
    {
        // A cross-reference stream of a million rows, nearly all placing an object past the end of the file, and two
        // streams whose lengths are wrong: the index is sorted for the first search, and kept sorted for the second.
        const int Size = 1_000_000;
        var data = Data(3 * Window);
        var file = MillionEntries(Size, data);

        using var document = Measure(() => PdfDocument.Open(file));

        var (first, second) = Measure(() => (
            document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required(),
            document.GetObject(new PdfObjectId(6)).AsStream().Required()));

        first.Data.Length.Should().Be(data.Length);
        second.Data.Length.Should().Be(data.Length);
        document.Reader.Index.Count.Should().Be(Size);
        document.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid).Should().HaveCount(2);
    }

    [Fact]
    public void A_file_of_data_with_no_endstream_is_searched_once_to_its_end()
    {
        // Sixteen megabytes of data declared, and nothing after them but the index and the end of the file: the search
        // reads them once, a window at a time, and a second reading of the stream, after the cache let it go, reads its
        // window and the bytes after its declared length. The objects that fill the cache come before it.
        const int Length = 16 * 1024 * 1024;
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        for (var number = 10; number < 80; number++)
        {
            builder.WithObject(number, Invariant($"({number})"));
        }

        var file = builder.WithObject(StreamNumber, Invariant($"<< /Length {Length - 100} >>\nstream\n") + Data(Length)).BuildClassic(rootNumber: 1);

        using var source = new StrictCountingSource(file);
        using var document = PdfDocument.Open(source, PdfReaderOptions.Default with { ObjectCacheCapacity = 64 }, ownsSource: false);
        var opened = source.BytesRead;

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var stream = Measure(() => document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required());
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        stream.Data.Length.Should().Be(Length - 100);
        var repeated = ((Length / PdfFileReader.EndStreamSearchChunk) + 1) * PdfFileReader.EndStreamSearchOverlap;
        (source.BytesRead - opened).Should().BeInRange(Length - Window, Length + Window + repeated, "the data is read once, and what each read repeats of the one before");
        allocated.Should().BeLessThan(1024 * 1024, "the search reads through one pooled window at a time, and allocates nothing for what it reads");
        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid)
            .Which.Message.Should().EndWith("before the end of the file; the declared length is kept.");

        for (var number = 10; number < 80; number++)
        {
            document.GetObject(new PdfObjectId(number));
        }

        var beforeAgain = source.BytesRead;
        document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required().Data.Length.Should().Be(Length - 100);
        (source.BytesRead - beforeAgain).Should().BeLessThanOrEqualTo(Window + PdfObjectParser.EndObjLookahead);
    }

    [Fact]
    public void A_source_that_returns_less_than_it_holds_ends_the_search()
    {
        // The file shrinks under the document after it opened: the search stops where the source stops giving bytes.
        var data = Data(3 * Window);
        var file = Document((StreamNumber, Stream(data.Length - 40, data)), (6, "(" + new string('p', 4 * Window) + ")"));
        var source = new CutShortSource(file);

        using var document = PdfDocument.Open(source, options: null, ownsSource: true);
        source.CutAt((int)DataStartOf(file, StreamNumber) + Window + 100);
        var stream = document.GetObject(new PdfObjectId(StreamNumber)).AsStream().Required();

        stream.Data.Length.Should().Be(data.Length - 40, "no endstream was found before the source stopped, and the declared length is kept");
    }

    [Theory]
    [InlineData(10, 5501, 19954, 19952)]
    [InlineData(13, 26856, 26018, 26016)]
    [InlineData(64, 93559, 16400, 16398)]
    public void The_damaged_federal_regulation_reads_its_streams_past_the_window_to_their_endstream(
        int number, long dataStart, int declared, int length)
    {
        // GovDocs1's DEA extract, typeset by Distiller 3: its lengths run two bytes past the data, and three of its streams
        // run past the window, where the length was taken as declared.
        using var document = PdfDocument.Open(Corpus.Read("vendor/opf-format-corpus/distiller3-dea-cfr-damaged.pdf"));

        var stream = document.GetObject(new PdfObjectId(number)).AsStream().Required();

        stream.Data.Position.Should().Be(dataStart);
        stream.Data.Length.Should().Be(length);
        document.Diagnostics.Should().ContainSingle(d => d.Position == dataStart).Which.Message.Should().Be(
            Invariant($"The stream declared {declared} bytes but ended after {length}."));
        document.Reader.TryGetStreamLengthFault(number, out var fault).Should().BeTrue();
        fault.Should().Be(new StreamLengthFault
        {
            Form = StreamLengthForm.Integer,
            Declared = declared,
            Taken = length,
            Found = length,
            EndStream = EndStreamState.Found,
            DataStart = dataStart,
        });
    }

    /// <summary>
    /// The streams of the remote corpus whose length the reader found wrong past the window (#55), or could not take
    /// (#120), with what it takes and says: those fetched here join the theory.
    /// </summary>
    public static TheoryData<string, int, long, int, string> RemoteStreams
    {
        get
        {
            var data = new TheoryData<string, int, long, int, string>();

            foreach (var (file, number, dataStart, length, message) in RemoteRows)
            {
                if (Corpus.Documents.Any(document => document.File == file))
                {
                    data.Add(file, number, dataStart, length, message);
                }
            }

            return data;
        }
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(RemoteStreams))]
    public void A_remote_document_reads_the_stream_whose_length_is_wrong_as_the_measurement_found_it(
        string file, int number, long dataStart, int length, string message)
    {
        var entry = Corpus.Get(file);
        using var document = PdfDocument.Open(Corpus.Read(file), CorpusOptions(entry));

        // Read as the acceptance tests read it: every object, so that a rebuild of the index happens where it does there.
        foreach (var each in document.ObjectNumbers.ToList())
        {
            document.GetObject(new PdfObjectId(each));
        }

        document.Reader.TryGetStreamLengthFault(number, out var fault).Should().BeTrue();
        fault.DataStart.Should().Be(dataStart);
        fault.Taken.Should().Be(length);
        document.Diagnostics.Should().ContainSingle(d => d.Position == dataStart && d.Code == PdfDiagnosticCodes.StreamLengthInvalid)
            .Which.Message.Should().Be(message);
    }

    private static readonly (string File, int Number, long DataStart, int Length, string Message)[] RemoteRows =
    [
        ("remote/opf-format-corpus/ibm-id-workbench-xpp-qmf-manual.pdf", 93, 89018, 115264, "The stream declared 115266 bytes but ended after 115264."),
        ("remote/opf-format-corpus/ibm-id-workbench-xpp-qmf-manual.pdf", 169, 238133, 69061, "The stream declared 69064 bytes but ended after 69061."),
        ("remote/opf-format-corpus/pdfwriter4-powerpoint-ornl-sns-ring-physics.pdf", 144, 491659, 23865, "The stream declared 23867 bytes but ended after 23865."),
        ("remote/opf-format-corpus/quartz-word-samhsa-prevention-pathways-fact-sheet.pdf", 27, 3278, 10191, "The stream declared 10165 bytes but ended after 10191."),
        ("remote/opf-format-corpus/quartz-word-samhsa-prevention-pathways-fact-sheet.pdf", 30, 13606, 13499, "The stream declared 13441 bytes but ended after 13499."),
        ("remote/opf-format-corpus/quartz-word-samhsa-prevention-pathways-fact-sheet.pdf", 35, 28489, 9191, "The stream declared 9158 bytes but ended after 9191."),
        ("remote/opf-format-corpus/quartz-word-samhsa-prevention-pathways-fact-sheet.pdf", 45, 48512, 32444, "The stream declared 32303 bytes but ended after 32444."),
        ("remote/opf-format-corpus/quartz-word-samhsa-prevention-pathways-fact-sheet.pdf", 50, 82353, 21088, "The stream declared 21010 bytes but ended after 21088."),
        ("remote/opf-format-corpus/quartz-word-samhsa-prevention-pathways-fact-sheet.pdf", 55, 104851, 8703, "The stream declared 8673 bytes but ended after 8703."),
        ("remote/opf-format-corpus/quartz-word-samhsa-prevention-pathways-fact-sheet.pdf", 60, 114932, 23192, "The stream declared 23106 bytes but ended after 23192."),
        ("remote/opf-format-corpus/quartz-word-samhsa-prevention-pathways-fact-sheet.pdf", 72, 145068, 8458, "The stream declared 8434 bytes but ended after 8458."),
        ("remote/opf-format-corpus/jhove-hul-35-atypon-pdfplus-journal-article.pdf", 49, 249330, 1647, "The stream declared 62065 bytes but ended after 1647."),
        ("remote/opf-format-corpus/jhove-hul-35-pdfium-urban-planning-report.pdf", 695, 19901349, 202154, "The stream declared 202154 bytes, and no endstream follows them before the next object, at 20103524; the declared length is kept."),
        ("remote/ipres2017/t02-05-01-015-invalid-stream-length-in-dictionary.pdf", 4, 316, 68, "The stream's /Length is a real number, 61.5, not a non-negative integer; its data ends after 68 bytes."),
        ("remote/ipres2017/t02-05-01-016-stream-length-missing-in-dictionary.pdf", 4, 304, 68, "The stream has no /Length; its data ends after 68 bytes."),
        ("remote/opf-format-corpus/jhove-hul-157-tiff2pdf-1996-journal-article-scan.pdf", 16, 542577, 5555, "The stream's /Length names object 18 0, which holds a stream, not a non-negative integer; its data ends after 5555 bytes."),
        ("remote/opf-format-corpus/jhove-hul-157-tiff2pdf-1996-journal-article-scan.pdf", 18, 548315, 5952, "The stream's /Length names object 20 0, which holds a stream, not a non-negative integer; its data ends after 5952 bytes."),
        ("remote/opf-format-corpus/jhove-hul-157-tiff2pdf-1996-journal-article-scan.pdf", 20, 554450, 4935, "The stream's /Length names object 22 0, which holds a stream, not a non-negative integer; its data ends after 4935 bytes."),
        ("remote/opf-format-corpus/jhove-hul-157-tiff2pdf-1996-journal-article-scan.pdf", 22, 559568, 5019, "The stream's /Length names object 24 0, which holds a stream, not a non-negative integer; its data ends after 5019 bytes."),
        ("remote/opf-format-corpus/jhove-hul-157-tiff2pdf-1996-journal-article-scan.pdf", 24, 564770, 6173, "The stream's /Length names object 26 0, which holds a stream, not a non-negative integer; its data ends after 6173 bytes."),
        ("remote/opf-format-corpus/jhove-hul-157-tiff2pdf-1996-journal-article-scan.pdf", 26, 571126, 4897, "The stream's /Length names object 28 0, which holds a dictionary, not a non-negative integer; its data ends after 4897 bytes."),
        ("remote/opf-format-corpus/jhove-hul-157-tiff2pdf-1998-journal-article-scan.pdf", 10, 280125, 5233, "The stream's /Length names object 12 0, which holds a stream, not a non-negative integer; its data ends after 5233 bytes."),
        ("remote/opf-format-corpus/jhove-hul-157-tiff2pdf-1998-journal-article-scan.pdf", 12, 285541, 5098, "The stream's /Length names object 14 0, which holds a stream, not a non-negative integer; its data ends after 5098 bytes."),
        ("remote/opf-format-corpus/jhove-hul-157-tiff2pdf-1998-journal-article-scan.pdf", 14, 290822, 4357, "The stream's /Length names object 16 0, which holds a stream, not a non-negative integer; its data ends after 4357 bytes."),
        ("remote/opf-format-corpus/jhove-hul-157-tiff2pdf-1998-journal-article-scan.pdf", 16, 295362, 1893, "The stream's /Length names object 18 0, which holds a dictionary, not a non-negative integer; its data ends after 1893 bytes."),
    ];

    /// <summary>The limits a corpus document is opened with, as the acceptance tests open it.</summary>
    private static PdfReaderOptions CorpusOptions(CorpusDocument entry) => entry.ReaderLimits is not { } raised
        ? PdfReaderOptions.Default
        : PdfReaderOptions.Default with
        {
            Limits = PdfReaderLimits.Default with
            {
                MaxDecodedStreamLength = raised.MaxDecodedStreamLength ?? PdfReaderLimits.Default.MaxDecodedStreamLength,
                MaxObjectLength = raised.MaxObjectLength ?? PdfReaderLimits.Default.MaxObjectLength,
                MaxXRefSectionLength = raised.MaxXRefSectionLength ?? PdfReaderLimits.Default.MaxXRefSectionLength,
                MaxXRefSectionCount = raised.MaxXRefSectionCount ?? PdfReaderLimits.Default.MaxXRefSectionCount,
                MaxTrailerLength = raised.MaxTrailerLength ?? PdfReaderLimits.Default.MaxTrailerLength,
            },
        };

    /// <summary>A stream's body: its <c>/Length</c>, <paramref name="width"/> digits wide when given, and its data.</summary>
    private static string Stream(int length, string data, string end = "\nendstream", int width = 0) =>
        "<< /Length " + length.ToString(width > 0 ? new string('0', width) : "0", CultureInfo.InvariantCulture) +
        " >>\nstream\n" + data + end;

    /// <summary>Data in which no <c>endstream</c> can be found: the alphabet over and over.</summary>
    private static string Data(int length) => string.Create(length, 0, static (span, _) =>
    {
        for (var i = 0; i < span.Length; i++)
        {
            span[i] = (char)('a' + (i % 26));
        }
    });

    /// <summary>A document with a classic table: its catalog and page tree, objects 1 and 2, then the objects given.</summary>
    private static byte[] Document(params (int Number, string Body)[] objects)
    {
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        foreach (var (number, body) in objects)
        {
            builder.WithObject(number, body);
        }

        return builder.BuildClassic(rootNumber: 1);
    }

    /// <summary>A document as <see cref="Document"/> writes it, whose catalog names object 5 as its metadata, for the validator to reach it.</summary>
    private static byte[] Reachable(params (int Number, string Body)[] objects)
    {
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R /Metadata 5 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        foreach (var (number, body) in objects)
        {
            builder.WithObject(number, body);
        }

        return builder.BuildClassic(rootNumber: 1);
    }

    /// <summary>
    /// Rewrites the row of object <paramref name="number"/> in a file <see cref="TestPdfBuilder"/> wrote with a classic
    /// table: to place it at <paramref name="offset"/>, or to mark it free when null.
    /// </summary>
    private static void Rewrite(byte[] file, int number, long? offset)
    {
        var row = Encoding.Latin1.GetString(file).LastIndexOf(Invariant($"{HeaderOf(file, number):D10} 00000 n"), StringComparison.Ordinal);
        Encoding.ASCII.GetBytes(offset is { } at ? Invariant($"{at:D10} 00000 n") : "0000000000 65535 f").CopyTo(file, row);
    }

    /// <summary>Where the header of object <paramref name="number"/> starts, the first time a line starts with it.</summary>
    private static long HeaderOf(byte[] file, int number) =>
        Encoding.Latin1.GetString(file).IndexOf(Invariant($"\n{number} 0 obj"), StringComparison.Ordinal) + 1;

    /// <summary>Where the data of object <paramref name="number"/>'s stream starts.</summary>
    private static long DataStartOf(byte[] file, int number) =>
        Encoding.Latin1.GetString(file).IndexOf("stream\n", (int)HeaderOf(file, number), StringComparison.Ordinal) + "stream\n".Length;

    /// <summary>
    /// A file whose index is a Flate-compressed cross-reference stream of <paramref name="size"/> rows: objects 1 and 2,
    /// streams 5 and 6 whose lengths are 40 bytes short, and every other row an object past the end of the file.
    /// </summary>
    private static byte[] MillionEntries(int size, string data)
    {
        using var output = new MemoryStream();
        var offsets = new Dictionary<int, long>();

        void Write(string text) => output.Write(Encoding.Latin1.GetBytes(text));

        Write("%PDF-1.5\n");
        offsets[1] = output.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[2] = output.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

        foreach (var number in new[] { 5, 6 })
        {
            offsets[number] = output.Position;
            Write(Invariant($"{number} 0 obj\n{Stream(data.Length - 40, data)}\nendobj\n"));
        }

        var xref = output.Position;
        using var rows = new MemoryStream();

        for (var number = 0; number < size; number++)
        {
            var (type, offset) = number switch
            {
                0 => (0, 0L),
                _ when offsets.TryGetValue(number, out var at) => (1, at),
                _ when number == size - 1 => (1, xref),
                _ => (1, 100_000_000L + number),
            };

            rows.Write([(byte)type, (byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset, 0, 0]);
        }

        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            rows.Position = 0;
            rows.CopyTo(zlib);
        }

        Write(Invariant($"{size - 1} 0 obj\n<< /Type /XRef /Size {size} /W [1 4 2] /Root 1 0 R /Filter /FlateDecode /Length {compressed.Length} >>\nstream\n"));
        output.Write(compressed.ToArray());
        Write(Invariant($"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n"));
        return output.ToArray();
    }

    /// <summary>
    /// A file of two cross-reference streams: the newest, past the first window, 7 bytes shorter than its /Length says; the
    /// older, whose /Length names object 10, which the newest places 2 bytes before its header. Object 5 is a stream whose
    /// endstream is lost, object 10 follows it, then object 6, whose entry points one byte into its header.
    /// </summary>
    private static byte[] CorrectedWhileTheChainIsRead(string data, out long tenAt)
    {
        using var output = new MemoryStream();
        var offsets = new Dictionary<int, long>();

        void Write(string text) => output.Write(Encoding.Latin1.GetBytes(text));

        byte[] Rows(int size)
        {
            var rows = new byte[size * 7];

            for (var number = 1; number < size; number++)
            {
                if (offsets.TryGetValue(number, out var at))
                {
                    var offset = number switch { 10 => at - 2, 6 => at + 1, _ => at };
                    rows.AsSpan(number * 7, 7).Clear();
                    rows[number * 7] = 1;
                    rows[(number * 7) + 1] = (byte)(offset >> 24);
                    rows[(number * 7) + 2] = (byte)(offset >> 16);
                    rows[(number * 7) + 3] = (byte)(offset >> 8);
                    rows[(number * 7) + 4] = (byte)offset;
                }
            }

            return rows;
        }

        Write("%PDF-1.5\n");
        offsets[1] = output.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[2] = output.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        offsets[StreamNumber] = output.Position;
        Write(Invariant($"{StreamNumber} 0 obj\n{Stream(data.Length + 100, data, end: "\n")}endobj\n"));
        offsets[10] = output.Position;
        Write(Invariant($"10 0 obj\n{21 * 7}\nendobj\n"));
        offsets[6] = output.Position;
        Write("6 0 obj\n(" + new string('p', 200) + ")\nendobj\n");
        offsets[20] = output.Position;
        Write("20 0 obj\n<< /Type /XRef /Size 21 /W [1 4 2] /Root 1 0 R /Length 10 0 R >>\nstream\n");
        output.Write(Rows(21));
        Write("\nendstream\nendobj\n");
        offsets[30] = output.Position;

        // Ten thousand rows of 7 bytes run past the 64 KB window the reader first reads a section through.
        const int Size = 10_000;
        Write(Invariant($"30 0 obj\n<< /Type /XRef /Size {Size} /W [1 4 2] /Root 1 0 R /Prev {offsets[20]} /Length {(Size * 7) + 7} >>\nstream\n"));
        output.Write(Rows(Size));
        Write(Invariant($"\nendstream\nendobj\nstartxref\n{offsets[30]}\n%%EOF\n"));

        tenAt = offsets[10];
        return output.ToArray();
    }

    private static T Measure<T>(Func<T> action)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = action();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "a hostile input must not be allowed to take unbounded time");
        return result;
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>A source that records where each read starts, and how much it asks for.</summary>
    private sealed class LoggingSource(byte[] data) : PdfFileSource
    {
        private readonly List<(long Offset, int Length)> _reads = [];

        public override long Length => data.Length;

        /// <summary>
        /// Counts the searches for endstream made in the data of a stream: the reads that start inside it and ask for more
        /// than the few bytes the checks at its end read, a read that carries on from the one before — starting where it
        /// ended, less what each read repeats — counted with it.
        /// </summary>
        public int Searches(long dataStart, int length)
        {
            var searches = 0;

            for (var index = 0; index < _reads.Count; index++)
            {
                var (offset, size) = _reads[index];
                var carriesOn = index > 0 &&
                    offset == _reads[index - 1].Offset + _reads[index - 1].Length - PdfFileReader.EndStreamSearchOverlap;

                if (offset > dataStart && offset < dataStart + length && size > PdfObjectParser.EndObjLookahead && !carriesOn)
                {
                    searches++;
                }
            }

            return searches;
        }

        /// <summary>Forgets the reads made so far: opening the document reads the tail of the file, where the data may be.</summary>
        public void Forget() => _reads.Clear();

        public override int Read(long offset, Span<byte> buffer)
        {
            _reads.Add((offset, buffer.Length));
            var count = (int)Math.Clamp(data.Length - offset, 0, buffer.Length);
            data.AsSpan((int)offset, count).CopyTo(buffer);
            return count;
        }
    }
}
