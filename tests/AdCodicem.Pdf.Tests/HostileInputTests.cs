using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// A PDF is an untrusted input. These tests assert the only two acceptable outcomes for a hostile one:
/// a usable result, or a typed failure — never a hang, a crash, or an allocation the file chose.
/// </summary>
public class HostileInputTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    [Fact]
    public void Refuses_an_input_that_contains_no_objects()
    {
        var noise = Encoding.ASCII.GetBytes("%PDF-1.7\n" + new string('x', 5000));

        FluentThrow<PdfFormatException>(() => PdfDocument.Open(noise));
    }

    [Fact]
    public void Ignores_a_stream_length_larger_than_the_file()
    {
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>")
            .WithObject(4, "<< /Length 2147483647 >>\nstream\nshort\nendstream")
            .BuildClassic(rootNumber: 1);

        using var document = Measure(() => PdfDocument.Open(bytes));

        var stream = document.GetObject(new PdfObjectId(4)).AsStream().Required();
        stream.GetRawBytes().Length.Should().BeLessThan(bytes.Length);
    }

    [Fact]
    public void Ignores_an_object_stream_that_claims_more_objects_than_it_could_hold()
    {
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, "<< /Type /ObjStm /N 1000000000 /First 4 /Length 8 >>\nstream\n1 0 <<>>\nendstream")
            .BuildClassic(rootNumber: 1);

        using var document = Measure(() => PdfDocument.Open(bytes));

        document.Catalog.Required();
    }

    [Fact]
    public void Survives_a_cross_reference_stream_with_impossible_field_widths()
    {
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "<< /Type /XRef /Size 3 /W [99 99 99] /Root 1 0 R /Length 4 >>\nstream\nAAAA\nendstream")
            .BuildClassic(rootNumber: 1);

        using var document = Measure(() => PdfDocument.Open(bytes));

        document.Catalog.Required();
    }

    [Fact]
    public void Survives_an_object_whose_length_refers_to_itself()
    {
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "<< /Length 3 0 R >>\nstream\nloop\nendstream")
            .BuildClassic(rootNumber: 1);

        using var document = Measure(() => PdfDocument.Open(bytes));

        document.GetObject(new PdfObjectId(3)).Required();
    }

    [Fact]
    public void Survives_a_document_whose_pages_form_a_cycle()
    {
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [2 0 R] /Count 1 >>")
            .BuildClassic(rootNumber: 1);

        using var document = Measure(() => PdfDocument.Open(bytes));

        document.Catalog.GetDictionary(PdfName.Pages).Required();
    }

    [Fact]
    public void Survives_a_file_made_only_of_object_headers()
    {
        var text = new StringBuilder("%PDF-1.7\n");
        for (var i = 1; i <= 2000; i++)
        {
            text.Append(i).Append(" 0 obj\n");
        }

        var bytes = Encoding.ASCII.GetBytes(text.ToString());

        // Objects exist but nothing is well formed: opening must fail cleanly or return an empty document.
        var document = Measure(() =>
        {
            try
            {
                return PdfDocument.Open(bytes);
            }
            catch (PdfFormatException)
            {
                return null;
            }
        });

        document?.Dispose();
    }

    [Fact]
    public void Survives_deeply_nested_containers_inside_an_object()
    {
        var deep = new string('[', 20000) + new string(']', 20000);
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, $"<< /Type /Pages /Kids [] /Count 0 /Deep {deep} >>")
            .BuildClassic(rootNumber: 1);

        using var document = Measure(() => PdfDocument.Open(bytes));

        document.GetObject(new PdfObjectId(2)).Required();
        document.Diagnostics.Contains(PdfDiagnosticCodes.SyntaxDepthExceeded).Should().BeTrue();
    }

    [Fact]
    public void Survives_a_chain_of_lengths_longer_than_the_stack_is_deep()
    {
        // Each stream takes its /Length from the next, so reading the first loads the second while it is
        // being parsed, which loads the third: 50,000 levels took the process down with the stack.
        const int Chain = 50_000;
        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new long[Chain + 3];

        offsets[1] = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[2] = text.Length;
        text.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

        for (var number = 3; number < Chain + 3; number++)
        {
            offsets[number] = text.Length;
            var length = number + 1 < Chain + 3 ? $"{number + 1} 0 R" : "1";
            text.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< /Length {length} >>\nstream\nx\nendstream\nendobj\n");
        }

        var xref = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 {Chain + 3}\n0000000000 65535 f\r\n");
        for (var number = 1; number < Chain + 3; number++)
        {
            text.Append(CultureInfo.InvariantCulture, $"{offsets[number]:D10} 00000 n\r\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {Chain + 3} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");

        using var document = PdfDocument.Open(Encoding.ASCII.GetBytes(text.ToString()));
        var first = Measure(() => document.GetObject(new PdfObjectId(3)));

        first.AsStream().Required().GetRawBytes().Length.Should().Be(1);
        document.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.SyntaxDepthExceeded).Should().ContainSingle();
    }

    [Theory]
    [InlineData(32, 64)]
    [InlineData(127, 64)]
    public void A_file_nesting_objects_deeper_than_the_stack_allows_reads_them_as_null_without_crashing(int nesting, int streams)
    {
        // Each stream takes its /Length from the next and sits inside nested arrays, so every nested load parses
        // that deep again: within both the parser's and the loader's bounds, yet more stack than a small thread
        // has. The reader stops where the stack runs short, as it does at either bound.
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        for (var number = 3; number < 3 + streams; number++)
        {
            var length = number + 1 < 3 + streams ? $"{number + 1} 0 R" : "1";
            builder.WithObject(
                number,
                new string('[', nesting) + $"<< /Length {length} >>\nstream\nx\nendstream" + new string(']', nesting));
        }

        var file = builder.BuildClassic(rootNumber: 1);
        PdfObject? first = null;
        PdfDiagnostics? diagnostics = null;
        Exception? failure = null;

        var thread = new Thread(
            () =>
            {
                try
                {
                    using var document = PdfDocument.Open(file);
                    first = document.GetObject(new PdfObjectId(3));
                    diagnostics = document.Diagnostics;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failure = exception;
                }
            },
            maxStackSize: 256 * 1024);

        thread.Start();
        thread.Join();

        failure.Should().BeNull();
        first.Should().BeOfType<PdfArray>();
        diagnostics.Required().Contains(PdfDiagnosticCodes.SyntaxDepthExceeded).Should().BeTrue();
    }

    [Fact]
    public void An_object_referenced_under_many_generations_is_read_once()
    {
        // The index holds one entry per object number, so every generation of a number is the same object: it is
        // parsed once and kept once, however many generations a file references it under.
        const int Integers = 10_000;
        const int Generations = 1_000;
        var array = new StringBuilder("[");
        for (var i = 0; i < Integers; i++)
        {
            array.Append(100_000 + i).Append(' ');
        }

        var references = new StringBuilder("[");
        for (var generation = 0; generation < Generations; generation++)
        {
            references.Append("5 ").Append(generation).Append(" R ");
        }

        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, array.Append(']').ToString())
            .WithObject(6, references.Append(']').ToString())
            .BuildClassic(rootNumber: 1);

        using var document = PdfDocument.Open(file);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var first = document.GetObject(new PdfObjectId(5));
        var all = document.GetObject(new PdfObjectId(6)).AsArray().Required();
        var same = 0;
        for (var index = 0; index < all.Count; index++)
        {
            same += ReferenceEquals(all.Resolved(index), first) ? 1 : 0;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        same.Should().Be(Generations);
        allocated.Should().BeLessThan(4L * 1024 * 1024, "the array is parsed once, not once per generation");
        first.AsArray().Required().Count.Should().Be(Integers);
        document.GetObject(new PdfObjectId(5, 7)).Should().BeSameAs(first);
    }

    [Fact]
    public void Object_streams_are_kept_decoded_within_a_budget_and_decoded_again_when_needed()
    {
        // Forty object streams that decode to 2 MB each would hold 80 MB kept together, from a file of a few hundred
        // kilobytes. The reader lets the oldest go past its budget, and reads an object of one it let go all the same.
        const int Streams = 40;
        var file = ObjectStreams(Streams, decodedLength: 2 * 1024 * 1024, objectsPerStream: 1, everyIndexWrong: false);

        using var document = PdfDocument.Open(file, PdfReaderOptions.Default with { ObjectCacheCapacity = 64 });

        for (var k = 0; k < Streams; k++)
        {
            document.GetObject(new PdfObjectId(PackedNumber(k, 0))).AsDictionary().Required().Count.Should().Be(1);
        }

        document.Reader.ObjectStreamBytes.Should().BeLessThanOrEqualTo(32L * 1024 * 1024);

        // Eighty objects later, the first is out of the object cache as well: reading it decodes its stream again.
        var first = document.GetObject(new PdfObjectId(PackedNumber(0, 0))).AsDictionary().Required();
        first.GetRaw(PdfName.Get("K")).Should().BeOfType<PdfString>();
        document.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void An_object_stream_that_needs_an_object_it_holds_reads_the_same_after_the_budget_lets_it_go()
    {
        // Object stream 7 names its own object 5 as its filter: decoded, it reads 5 as null, so its data is taken as
        // written. Once 5 is read and kept, and a stream of 33 MB has pushed 7 out of the budget, decoding 7 again
        // must read 5 as null again, or 7 would be taken as hexadecimal and object 6 lost.
        var file = PackedFile(
            [
                new PackedStream(7, "/Filter 5 0 R", [(5, "/ASCIIHexDecode"), (6, "<< /Title (six) >>")], PackedData.Plain),
                new PackedStream(8, string.Empty, [(9, "<< /K (large) >>")], PackedData.Flate, Padding: 33 * 1024 * 1024),
            ]);

        using var fresh = PdfDocument.Open(file);
        var expected = fresh.GetObject(new PdfObjectId(6));

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(5)).Should().Be(PdfName.Get("ASCIIHexDecode"));
        document.GetObject(new PdfObjectId(9)).Should().BeOfType<PdfDictionary>();
        var six = document.GetObject(new PdfObjectId(6));

        Title(six).Should().Be("six").And.Be(Title(expected));
        document.Reader.ObjectStreamBytes.Should().BeLessThanOrEqualTo(34L * 1024 * 1024);
    }

    [Fact]
    public void An_object_stream_decoded_again_while_it_is_decoded_counts_once_against_the_budget()
    {
        // Object stream 7 takes its /DecodeParms from object 5, which it holds and the index does not list — its row is
        // of a reserved type —, in an index that declares a row more than it holds. Decoding 7 asks for 5, which
        // rebuilds the index; the rebuilt index places 5 in 7, which is decoded again, 5 read as null, and kept. The
        // first decoding then keeps 7 in place of the second: its data counts once, as it does in the sound file.
        var sound = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, "<< /Predictor 1 >>")
            .WithObject(6, "<< /Title (six) >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [5, 6], compressObjectStream: true, objectStreamEntries: "/DecodeParms 5 0 R");
        var text = Encoding.Latin1.GetString(sound);
        var dictionary = text.IndexOf("/Type /XRef /Size 9 ", StringComparison.Ordinal);
        var rows = text.IndexOf("stream\n", dictionary, StringComparison.Ordinal) + "stream\n".Length;
        var chars = text.ToCharArray();
        chars[rows + (5 * 7)] = '\u0003';
        var file = Encoding.Latin1.GetBytes(new string(chars).Replace("/Type /XRef /Size 9 ", "/Type /XRef /Size 10 ", StringComparison.Ordinal));

        using var expected = PdfDocument.Open(sound);
        Title(expected.GetObject(new PdfObjectId(6))).Should().Be("six");

        using var document = PdfDocument.Open(file);
        var six = document.GetObject(new PdfObjectId(6));

        Title(six).Should().Be("six");
        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(5)).AsDictionary().Required().GetInteger(PdfName.Get("Predictor")).Should().Be(1);
        document.Reader.ObjectStreamBytes.Should().Be(expected.Reader.ObjectStreamBytes).And.BePositive();
    }

    [Fact]
    public void An_object_stream_decoded_again_while_it_is_decoded_and_short_both_times_reads_as_nothing_without_failing()
    {
        // Object stream 7 takes its /Filter from object 10, which object stream 9 holds, and its /DecodeParms from
        // object 5, which 7 holds and the index does not list — its row is of a reserved type —, in an index a row
        // short of its /Size. Decoding 7 asks for 5, which rebuilds the index while 10 is not yet in it: the rebuild
        // reads 7 unfiltered, and places 5 in it. Reading 5 decodes 7 again inside the first decoding, through
        // /ASCIIHexDecode this time, into fewer bytes than its /First: neither decoding gives anything to keep, and the
        // second to end has nothing to take the place of.
        using var output = new MemoryStream();
        var offsets = new Dictionary<int, long>();
        void Write(string text) => output.Write(Encoding.ASCII.GetBytes(text));

        void WriteObject(int number, string body)
        {
            offsets[number] = output.Position;
            Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n"));
        }

        Write("%PDF-1.5\n");
        WriteObject(1, "<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        WriteObject(7, "<< /Type /ObjStm /N 2 /First 9 /Filter 10 0 R /DecodeParms 5 0 R /Length 47 >>\nstream\n5 0 6 19 << /Predictor 1 >>\n<< /Title (six) >>\n\nendstream");
        WriteObject(9, "<< /Type /ObjStm /N 1 /First 5 /Length 21 >>\nstream\n10 0 /ASCIIHexDecode\n\nendstream");

        var xref = output.Position;
        using var rows = new MemoryStream();
        (int Type, long Field, int Index)[] entries =
        [
            (0, 0, 65535), (1, offsets[1], 0), (1, offsets[2], 0), (0, 0, 0), (0, 0, 0), (3, 7, 0),
            (2, 7, 1), (1, offsets[7], 0), (0, 0, 0), (1, offsets[9], 0), (2, 9, 0), (1, xref, 0),
        ];

        foreach (var (type, field, index) in entries)
        {
            rows.Write([(byte)type, (byte)(field >> 24), (byte)(field >> 16), (byte)(field >> 8), (byte)field, (byte)(index >> 8), (byte)index]);
        }

        Write(string.Create(CultureInfo.InvariantCulture, $"11 0 obj\n<< /Type /XRef /Size 13 /W [1 4 2] /Root 1 0 R /Length {rows.Length} >>\nstream\n"));
        output.Write(rows.ToArray());
        Write(string.Create(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n"));

        using var document = PdfDocument.Open(output.ToArray());
        var six = document.GetObject(new PdfObjectId(6));

        six.Should().BeOfType<PdfNull>();
        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(10)).Should().Be(PdfName.Get("ASCIIHexDecode"));
        document.Reader.ObjectStreamBytes.Should().Be(21, "only object stream 9 is kept");
        document.Diagnostics.Where(diagnostic => diagnostic.Code == PdfDiagnosticCodes.StreamTruncated).Should().HaveCount(2);
        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.StreamSelfReference);
    }

    [Fact]
    public void Two_object_streams_that_need_each_other_read_the_same_after_the_budget_lets_them_go()
    {
        // Stream 10 takes its filter from 21, held by stream 11, which takes its own from 23, held by stream 10:
        // decoding 10 decodes 11, which reads 23 as null and is taken as written; 21 then says 10 is hexadecimal.
        // Once 23 is read and kept and both streams are pushed out of the budget, decoding 11 again on its own must
        // read 23 as null again, or it would be taken as hexadecimal and object 22 lost.
        var file = PackedFile(
            [
                new PackedStream(10, "/Filter 21 0 R", [(20, "<< /Title (twenty) >>"), (23, "/ASCIIHexDecode")], PackedData.Hex),
                new PackedStream(11, "/Filter 23 0 R", [(21, "/ASCIIHexDecode"), (22, "<< /Title (twenty-two) >>")], PackedData.Plain),
                new PackedStream(12, string.Empty, [(9, "<< /K (large) >>")], PackedData.Flate, Padding: 33 * 1024 * 1024),
            ]);

        using var fresh = PdfDocument.Open(file);
        fresh.GetObject(new PdfObjectId(20)).Should().BeOfType<PdfDictionary>();
        var expected = fresh.GetObject(new PdfObjectId(22));

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(20)).Should().BeOfType<PdfDictionary>();
        document.GetObject(new PdfObjectId(23)).Should().Be(PdfName.Get("ASCIIHexDecode"));
        document.GetObject(new PdfObjectId(9)).Should().BeOfType<PdfDictionary>();
        var twentyTwo = document.GetObject(new PdfObjectId(22));

        Title(twentyTwo).Should().Be("twenty-two").And.Be(Title(expected));
    }

    [Fact]
    public void An_object_stream_a_load_too_deep_could_not_reach_is_read_from_a_shallower_one()
    {
        // Sixty-three streams each take their /Length from the next; the last takes it from object 5, in object
        // stream 7. Asked from the top of the chain, object 5 is the sixty-fourth load and stream 7 the sixty-fifth,
        // one past the deepest the reader follows: stream 7 reads as null there. Asked afterwards on their own, the
        // objects it holds read as they are.
        var direct = new List<(int Number, string Body)>();
        for (var number = 100; number < 163; number++)
        {
            var length = number < 162 ? $"{number + 1} 0 R" : "5 0 R";
            direct.Add((number, $"<< /Length {length} >>\nstream\nx\nendstream"));
        }

        var file = PackedFile(
            [new PackedStream(7, string.Empty, [(5, "1"), (6, "<< /Title (six) >>")], PackedData.Plain)],
            direct);

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(100));

        document.Diagnostics.Contains(PdfDiagnosticCodes.SyntaxDepthExceeded).Should().BeTrue();
        document.GetObject(new PdfObjectId(6)).Should().BeOfType<PdfDictionary>();
        document.GetObject(new PdfObjectId(5)).Should().Be(PdfInteger.Create(1));
    }

    [Fact]
    public void An_object_stream_whose_every_index_is_wrong_is_read_in_linear_time()
    {
        // One stream whose every entry in the index names the wrong place in it: each object is found by its
        // number, through a lookup built once, not by a search of the stream's header for each. Four times as many
        // objects then take about four times as long, where a search would take sixteen. The measure is that
        // ratio, the best of three runs of each size, so that it holds on a slow machine and under coverage
        // instrumentation alike, where a fixed time budget did not.
        const int Small = 50_000;
        const int Large = 4 * Small;
        var small = ObjectStreams(streams: 1, decodedLength: 0, objectsPerStream: Small, everyIndexWrong: true);
        var large = ObjectStreams(streams: 1, decodedLength: 0, objectsPerStream: Large, everyIndexWrong: true);

        ReadEvery(small, Small);
        var smallTime = TimeSpan.MaxValue;
        var largeTime = TimeSpan.MaxValue;

        for (var run = 0; run < 3; run++)
        {
            smallTime = Min(smallTime, Time(() => ReadEvery(small, Small)));
            largeTime = Min(largeTime, Time(() => ReadEvery(large, Large)));
        }

        largeTime.Should().BeLessThan(
            smallTime * 8,
            "four times the objects must cost about four times the time ({0} for {1:N0}, {2} for {3:N0})",
            smallTime,
            Small,
            largeTime,
            Large);

        static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

        static TimeSpan Time(Action action)
        {
            var stopwatch = Stopwatch.StartNew();
            action();
            return stopwatch.Elapsed;
        }

        static void ReadEvery(byte[] file, int count)
        {
            using var document = PdfDocument.Open(file);
            var dictionaries = 0;

            for (var i = 0; i < count; i++)
            {
                dictionaries += document.GetObject(new PdfObjectId(PackedNumber(0, i, count))) is PdfDictionary ? 1 : 0;
            }

            dictionaries.Should().Be(count);
            document.Diagnostics.Contains(PdfDiagnosticCodes.XRefOffsetAdjusted).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("free")]
    [InlineData("unlisted")]
    [InlineData("compressed")]
    public void Reports_a_chain_that_runs_too_deep_into_an_object_with_no_offset_without_a_position(string end)
    {
        // Sixty-four streams, each taking its /Length from the next; the last takes it from an object loaded one
        // level past the deepest the reader follows, which has no offset of its own to report: one the index
        // lists as free, one it does not list, one stored in an object stream.
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        if (end == "compressed")
        {
            builder.WithObject(3, "1");
        }

        AddLengthChain(builder, first: 4, streams: 64, end: end == "unlisted" ? "1000 0 R" : "3 0 R");
        var file = end == "compressed"
            ? builder.BuildWithXRefStream(rootNumber: 1, compressedObjects: [3])
            : builder.BuildClassic(rootNumber: 1);

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(4)).AsStream().Required();

        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.SyntaxDepthExceeded)
            .Which.Position.Should().Be(-1);
    }

    [Fact]
    public void Reports_a_chain_that_runs_too_deep_where_the_object_it_stops_at_starts()
    {
        // Sixty-five streams: the sixty-fifth is loaded one level past the deepest the reader follows, and is
        // reported where it starts in the file, bytes before the header included.
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        AddLengthChain(builder, first: 3, streams: 65, end: "1");
        byte[] file = [.. "junk before the header\n"u8, .. builder.BuildClassic(rootNumber: 1)];

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(3)).AsStream().Required();

        var stoppedAt = Encoding.Latin1.GetString(file).IndexOf("\n67 0 obj", StringComparison.Ordinal) + 1;
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.SyntaxDepthExceeded)
            .Which.Position.Should().Be(stoppedAt);
    }

    [Fact]
    public void Reports_chains_that_run_too_deep_once()
    {
        // Two chains, each deeper than the reader follows: the second reaches the same limit, and the report
        // already made says all there is to say.
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        AddLengthChain(builder, first: 3, streams: 70, end: "1");
        AddLengthChain(builder, first: 100, streams: 70, end: "1");

        using var document = PdfDocument.Open(builder.BuildClassic(rootNumber: 1));
        document.GetObject(new PdfObjectId(3)).AsStream().Required();
        document.GetObject(new PdfObjectId(100)).AsStream().Required();

        document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.SyntaxDepthExceeded).Should().ContainSingle();
    }

    [Fact]
    public void Does_not_take_a_keyword_the_probe_saw_cut_for_a_cross_reference_table()
    {
        // The section is probed through a few bytes, which spaces and "xref" fill: the probe sees "xref", but the
        // token is "xrefs". The table's own window reads it whole and refuses it, and the index is rebuilt.
        var written = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .BuildClassic(rootNumber: 1);
        var text = Encoding.Latin1.GetString(written);
        var bytes = Encoding.Latin1.GetBytes(text.Replace("\nxref\n0 3", "\n" + new string(' ', PdfFileReader.XRefProbeLength - "xref".Length) + "xrefs\n0 3", StringComparison.Ordinal));

        using var document = PdfDocument.Open(bytes);

        document.WasRepaired.Should().BeTrue();
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    [Fact]
    public void Opening_a_chain_of_trailers_that_never_close_reads_a_bounded_amount()
    {
        // Each section's trailer opens an array that never closes, so it runs past the window's edge and on
        // to the end of the file, through every section after it and a megabyte of comment. A cut trailer
        // is read again through a window of its own, which stops at 64 KB: growing the table's window to the
        // end of the file for each of the hundred sections read about 300 MB.
        const int Sections = 100;
        var text = new StringBuilder("%PDF-1.7\n");
        var catalog = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = text.Length;
        text.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        var previous = -1;

        for (var section = 0; section < Sections; section++)
        {
            var xref = text.Length;
            text.Append("xref\n0 3\n0000000000 65535 f\r\n")
                .Append(CultureInfo.InvariantCulture, $"{catalog:D10} 00000 n\r\n{pages:D10} 00000 n\r\n")
                .Append("trailer\n<< /Size 3 /Root 1 0 R ")
                .Append(previous < 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"/Prev {previous} "))
                .Append("/Open [\n");
            previous = xref;
        }

        text.Append('%').Append('x', 1024 * 1024).Append('\n');
        text.Append(CultureInfo.InvariantCulture, $"startxref\n{previous}\n%%EOF\n");
        var bytes = Encoding.ASCII.GetBytes(text.ToString());

        using var source = new StrictCountingSource(bytes);
        using var document = Measure(() => PdfDocument.Open(source, options: null, ownsSource: false));

        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        source.BytesRead.Should().BeLessThan(Sections * 256L * 1024);
    }

    [Fact]
    public void Opening_a_chain_of_hybrid_sections_whose_streams_never_close_reads_a_bounded_amount()
    {
        // Every section of the chain names, in /XRefStm, a cross-reference stream of its own whose
        // dictionary opens an array that never closes and runs on to the end of the file. Each is parsed
        // through windows that stop at 64 KB, so the chain costs a bounded read per section, not one the
        // size of the file.
        const int Sections = 100;
        var text = new StringBuilder("%PDF-1.7\n");
        var catalog = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = text.Length;
        text.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

        var streams = new long[Sections];
        for (var section = 0; section < Sections; section++)
        {
            streams[section] = text.Length;
            text.Append(CultureInfo.InvariantCulture, $"{section + 3} 0 obj\n<< /Type /XRef /W [1 4 2] /Open [\n");
        }

        text.Append('%').Append('x', 1024 * 1024).Append('\n');
        var previous = -1;

        for (var section = 0; section < Sections; section++)
        {
            var xref = text.Length;
            text.Append("xref\n0 3\n0000000000 65535 f\r\n")
                .Append(CultureInfo.InvariantCulture, $"{catalog:D10} 00000 n\r\n{pages:D10} 00000 n\r\n")
                .Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size 3 /Root 1 0 R /XRefStm {streams[section]} ")
                .Append(previous < 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"/Prev {previous} "))
                .Append(">>\n");
            previous = xref;
        }

        text.Append(CultureInfo.InvariantCulture, $"startxref\n{previous}\n%%EOF\n");
        var bytes = Encoding.ASCII.GetBytes(text.ToString());

        using var source = new StrictCountingSource(bytes);
        using var document = Measure(() => PdfDocument.Open(source, options: null, ownsSource: false));

        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        source.BytesRead.Should().BeLessThan(Sections * 256L * 1024);
    }

    [Fact]
    public async Task Survives_a_string_longer_than_the_largest_window()
    {
        // A string that never closes runs to the end of every window, so each attempt asks for a larger
        // one; only the 16 MB bound ends that. The load runs on its own task so that a missing bound fails
        // the test instead of hanging the run.
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, "(" + new string('x', 17 * 1024 * 1024))
            .BuildClassic(rootNumber: 1);

        using var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var cancellation = TestContext.Current.CancellationToken;
        var load = Task.Run(() => document.GetObject(new PdfObjectId(5)), cancellation);

        var first = await Task.WhenAny(load, Task.Delay(Budget, cancellation));
        first.Should().BeSameAs(load, "the window stops growing at 16 MB");
        (await load).Should().BeOfType<PdfString>().Which.Length.Should().BeLessThanOrEqualTo(16 * 1024 * 1024);
        source.BytesRead.Should().BeLessThan(24L * 1024 * 1024);
    }

    [Fact]
    public void Decodes_a_run_length_stream_no_further_than_the_bound()
    {
        // 2.3 million pairs that each decode to 128 bytes: 294 MB out of 4.6 MB in, past the 256 MB any
        // stream may decode to. RunLength had no bound at all.
        const int Pairs = 2_300_000;
        var data = new byte[(Pairs * 2) + 1];
        for (var pair = 0; pair < Pairs; pair++)
        {
            data[2 * pair] = 0x81;
            data[(2 * pair) + 1] = (byte)'A';
        }

        data[^1] = 0x80;
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, PdfName.RunLengthDecode);
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory(data));
        var diagnostics = new PdfDiagnostics();

        var decoded = Measure(() => stream.Decode(diagnostics));

        decoded.Length.Should().Be(256 * 1024 * 1024);
        var report = diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.LimitDecodedStream);
        report.Message.Should().Be(
            "The /RunLengthDecode data decodes to more than 256 MB; decoding stopped there. " +
            "Raise PdfReaderLimits.MaxDecodedStreamLength to read past it.");
    }

    /// <summary>
    /// Adds <paramref name="streams"/> streams from object <paramref name="first"/> on, each taking its /Length
    /// from the next, the last from <paramref name="end"/>.
    /// </summary>
    private static void AddLengthChain(TestPdfBuilder builder, int first, int streams, string end)
    {
        for (var number = first; number < first + streams; number++)
        {
            var length = number + 1 < first + streams ? $"{number + 1} 0 R" : end;
            builder.WithObject(number, $"<< /Length {length} >>\nstream\nx\nendstream");
        }
    }

    /// <summary>The text of a dictionary's /Title, or null when the value is no dictionary with a string there.</summary>
    private static string? Title(PdfObject value) =>
        value is PdfDictionary dictionary && dictionary.GetRaw(PdfName.Get("Title")) is PdfString title ? title.ToText() : null;

    /// <summary>
    /// Writes a file whose index is a cross-reference stream, with the object streams <paramref name="streams"/>
    /// describes and the objects <paramref name="direct"/> gives written directly; objects 1 and 2 are a catalog and
    /// an empty page tree.
    /// </summary>
    private static byte[] PackedFile(IReadOnlyList<PackedStream> streams, IReadOnlyList<(int Number, string Body)>? direct = null)
    {
        using var output = new MemoryStream();
        void Write(string text) => output.Write(Encoding.ASCII.GetBytes(text));

        Write("%PDF-1.5\n");
        var offsets = new Dictionary<int, long>();
        var packed = new Dictionary<int, (int Stream, int Index)>();
        var objects = new List<(int Number, string Body)> { (1, "<< /Type /Catalog /Pages 2 0 R >>"), (2, "<< /Type /Pages /Kids [] /Count 0 >>") };
        objects.AddRange(direct ?? []);

        foreach (var (number, body) in objects)
        {
            offsets[number] = output.Position;
            Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n"));
        }

        foreach (var stream in streams)
        {
            var header = new StringBuilder();
            var body = new StringBuilder();

            for (var i = 0; i < stream.Members.Count; i++)
            {
                packed[stream.Members[i].Number] = (stream.Number, i);
                header.Append(CultureInfo.InvariantCulture, $"{stream.Members[i].Number} {body.Length} ");
                body.Append(stream.Members[i].Body).Append('\n');
            }

            var text = Encoding.ASCII.GetBytes(header.ToString() + body);
            byte[] data;
            var filter = string.Empty;

            switch (stream.Data)
            {
                case PackedData.Hex:
                    data = Encoding.ASCII.GetBytes(Convert.ToHexString(text) + ">");
                    break;

                case PackedData.Flate:
                    using (var compressed = new MemoryStream())
                    {
                        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                        {
                            zlib.Write(text);
                            zlib.Write(new byte[stream.Padding].AsSpan());
                        }

                        data = compressed.ToArray();
                    }

                    filter = " /Filter /FlateDecode";
                    break;

                default:
                    data = text;
                    break;
            }

            offsets[stream.Number] = output.Position;
            Write(string.Create(
                CultureInfo.InvariantCulture,
                $"{stream.Number} 0 obj\n<< /Type /ObjStm /N {stream.Members.Count} /First {header.Length} {stream.Dictionary}{filter} /Length {data.Length} >>\nstream\n"));
            output.Write(data);
            Write("\nendstream\nendobj\n");
        }

        var xrefNumber = Math.Max(offsets.Keys.Max(), packed.Keys.Max()) + 1;
        var xrefOffset = output.Position;
        offsets[xrefNumber] = xrefOffset;
        using var rows = new MemoryStream();

        for (var number = 0; number <= xrefNumber; number++)
        {
            if (offsets.TryGetValue(number, out var offset))
            {
                rows.Write([1, (byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset, 0, 0]);
            }
            else if (packed.TryGetValue(number, out var entry))
            {
                rows.Write([2, (byte)(entry.Stream >> 24), (byte)(entry.Stream >> 16), (byte)(entry.Stream >> 8), (byte)entry.Stream, (byte)(entry.Index >> 8), (byte)entry.Index]);
            }
            else
            {
                rows.Write([0, 0, 0, 0, 0, 0, 0]);
            }
        }

        Write(string.Create(
            CultureInfo.InvariantCulture,
            $"{xrefNumber} 0 obj\n<< /Type /XRef /Size {xrefNumber + 1} /W [1 4 2] /Root 1 0 R /Length {rows.Length} >>\nstream\n"));
        output.Write(rows.ToArray());
        Write(string.Create(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF\n"));
        return output.ToArray();
    }

    /// <summary>The number of object stream <paramref name="stream"/>.</summary>
    private static int StreamNumber(int stream) => 3 + stream;

    /// <summary>The number of the object at <paramref name="index"/> of object stream <paramref name="stream"/>.</summary>
    private static int PackedNumber(int stream, int index, int objectsPerStream = 1) => 1000 + (stream * objectsPerStream) + index;

    /// <summary>
    /// Writes a file whose index is a cross-reference stream, with <paramref name="streams"/> object streams holding
    /// <paramref name="objectsPerStream"/> small dictionaries each, padded with white space to decode to at least
    /// <paramref name="decodedLength"/> bytes; with <paramref name="everyIndexWrong"/>, every entry gives index 0.
    /// </summary>
    private static byte[] ObjectStreams(int streams, int decodedLength, int objectsPerStream, bool everyIndexWrong)
    {
        using var output = new MemoryStream();
        void Write(string text) => output.Write(Encoding.ASCII.GetBytes(text));

        Write("%PDF-1.5\n");
        var offsets = new Dictionary<int, long>();
        var packed = new Dictionary<int, (int Stream, int Index)>();
        offsets[1] = output.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[2] = output.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

        for (var k = 0; k < streams; k++)
        {
            var header = new StringBuilder();
            var body = new StringBuilder();

            for (var i = 0; i < objectsPerStream; i++)
            {
                var number = PackedNumber(k, i, objectsPerStream);
                packed[number] = (StreamNumber(k), everyIndexWrong ? 0 : i);
                header.Append(CultureInfo.InvariantCulture, $"{number} {body.Length} ");
                body.Append("<< /K (thing) >>\n");
            }

            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(Encoding.ASCII.GetBytes(header.ToString() + body));
                zlib.Write(new byte[Math.Max(0, decodedLength - header.Length - body.Length)].AsSpan());
            }

            var data = compressed.ToArray();
            offsets[StreamNumber(k)] = output.Position;
            Write(string.Create(
                CultureInfo.InvariantCulture,
                $"{StreamNumber(k)} 0 obj\n<< /Type /ObjStm /N {objectsPerStream} /First {header.Length} /Filter /FlateDecode /Length {data.Length} >>\nstream\n"));
            output.Write(data);
            Write("\nendstream\nendobj\n");
        }

        var xrefNumber = PackedNumber(streams, 0, objectsPerStream);
        var xrefOffset = output.Position;
        offsets[xrefNumber] = xrefOffset;
        using var rows = new MemoryStream();

        for (var number = 0; number <= xrefNumber; number++)
        {
            if (offsets.TryGetValue(number, out var offset))
            {
                rows.Write([1, (byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset, 0, 0]);
            }
            else if (packed.TryGetValue(number, out var entry))
            {
                rows.Write([2, (byte)(entry.Stream >> 24), (byte)(entry.Stream >> 16), (byte)(entry.Stream >> 8), (byte)entry.Stream, (byte)(entry.Index >> 8), (byte)entry.Index]);
            }
            else
            {
                rows.Write([0, 0, 0, 0, 0, 0, 0]);
            }
        }

        using var compressedRows = new MemoryStream();
        using (var zlib = new ZLibStream(compressedRows, CompressionLevel.Optimal, leaveOpen: true))
        {
            rows.Position = 0;
            rows.CopyTo(zlib);
        }

        Write(string.Create(
            CultureInfo.InvariantCulture,
            $"{xrefNumber} 0 obj\n<< /Type /XRef /Size {xrefNumber + 1} /W [1 4 2] /Root 1 0 R /Filter /FlateDecode /Length {compressedRows.Length} >>\nstream\n"));
        output.Write(compressedRows.ToArray());
        Write(string.Create(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF\n"));
        return output.ToArray();
    }

    private static T Measure<T>(Func<T> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = action();
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(Budget, "A hostile input must not be allowed to take unbounded time.");
        return result;
    }

    /// <summary>How an object stream's data is written.</summary>
    private enum PackedData
    {
        /// <summary>As it is, with no filter.</summary>
        Plain,

        /// <summary>In hexadecimal, for a filter the stream's dictionary names.</summary>
        Hex,

        /// <summary>Flate-compressed, with /Filter /FlateDecode.</summary>
        Flate,
    }

    /// <summary>An object stream to write: its number, what its dictionary adds, the objects it holds and its data.</summary>
    private sealed record PackedStream(int Number, string Dictionary, IReadOnlyList<(int Number, string Body)> Members, PackedData Data, int Padding = 0);
}
