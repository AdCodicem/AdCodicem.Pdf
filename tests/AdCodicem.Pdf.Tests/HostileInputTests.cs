using System.Diagnostics;
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
/// A PDF is an untrusted input. These tests assert the only two acceptable outcomes for a hostile one:
/// a usable result, or a typed failure — never a hang, a crash, or an allocation the file chose.
/// </summary>
public class HostileInputTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);


    [Theory]
    [InlineData("/A 1 ")]
    [InlineData("/A null ")]
    public void A_dictionary_that_gives_one_key_a_million_times_reads_in_time_and_keeps_a_bounded_report(string entry)
    {
        // Each repeat is a report: the first thousand are kept, the rest counted, and none past the capacity is formatted.
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "<< " + string.Concat(Enumerable.Repeat(entry, 1_000_000)) + ">>")
            .BuildClassic(rootNumber: 1);
        using var document = PdfDocument.Open(bytes);

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var value = Measure(() => document.GetObject(new PdfObjectId(3)).AsDictionary().Required());
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        value.Should().HaveCount(entry.Contains("null", StringComparison.Ordinal) ? 0 : 1);
        allocated.Should().BeLessThan(150L * 1024 * 1024, "a repeat past the capacity is counted, its message not built");
        document.Diagnostics.Should().HaveCount(document.Diagnostics.Capacity)
            .And.OnlyContain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.SyntaxKeyRepeated);
        document.Diagnostics.SuppressedCount.Should().Be(999_999 - document.Diagnostics.Capacity);
    }

    [Fact]
    public void A_dictionary_of_distinct_keys_each_given_null_reads_in_time()
    {
        // A key given null makes no entry, and is remembered in case it is given again: two hundred thousand of them read in
        // a time that grows with their number, not with its square.
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "<< " + string.Concat(Enumerable.Range(0, 200_000).Select(i => $"/K{i} null ")) + ">>")
            .BuildClassic(rootNumber: 1);
        using var document = PdfDocument.Open(bytes);

        Measure(() => document.GetObject(new PdfObjectId(3)).AsDictionary().Required()).Should().BeEmpty();
        document.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void A_string_that_never_closes_takes_the_end_of_the_file_and_is_reported_once_where_it_opens()
    {
        // #172's file: object 4's string opens a parenthesis it never closes, and takes endobj, object 5, the table and the
        // trailer with it, to the end of the file. Its value is kept as read, object 5 is still read where the table places
        // it, and the string is reported where it opens.
        var file = PdfTemplate.Build("""
            %PDF-1.7
            1 0 obj
            << /Type /Catalog /Pages 2 0 R /Test 4 0 R >>
            endobj
            2 0 obj
            << /Type /Pages /Kids [3 0 R] /Count 1 >>
            endobj
            3 0 obj
            << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>
            endobj
            4 0 obj (abc(def) endobj
            5 0 obj null endobj
            xref
            0 6
            {free}
            {row:1}
            {row:2}
            {row:3}
            {row:4}
            {row:5}
            trailer
            << /Size 6 /Root 1 0 R >>
            startxref
            {xref:1}
            %%EOF

            """);
        var opens = PdfTemplate.OffsetOf(file, "(abc(def)");
        using var document = PdfDocument.Open(file);

        var swallowed = document.GetObject(new PdfObjectId(4)).AsString().Required();

        swallowed.Length.Should().Be(file.Length - (int)opens - 1);
        swallowed.ToText().Should().StartWith("abc(def) endobj\n5 0 obj null endobj\nxref").And.EndWith("%%EOF\n");
        document.GetObject(new PdfObjectId(5)).Should().BeSameAs(PdfNull.Instance);
        var report = document.Diagnostics.Should().ContainSingle().Subject;
        report.Code.Should().Be(PdfDiagnosticCodes.SyntaxTruncatedObject);
        report.Position.Should().Be(opens);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The file ended inside a literal string, which takes the {file.Length - opens:N0} bytes from where it opens to that end."));
    }

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
    public void An_object_stream_that_claims_more_objects_than_it_could_hold_serves_those_it_lists_and_allocates_nothing_for_the_rest()
    {
        // A billion objects in /N, and a header of four bytes: the tables grow with the pairs read, not with /N — what a /N
        // of one allocates (#160). That they start no larger than the header can hold, the header's own test holds.
        var allocated = Allocations.Least(
            () => PdfDocument.Open(Claiming(1_000_000_000)),
            document => Measure(() => document.GetObject(new PdfObjectId(6))),
            out var document,
            out var six);
        var honest = Allocations.Least(() => PdfDocument.Open(Claiming(1)), document => Measure(() => document.GetObject(new PdfObjectId(6))), out var opened, out _);
        opened.Dispose();

        using (document)
        {
            six.Should().BeOfType<PdfDictionary>();
            allocated.Should().BeLessThan(honest + 4096, "the header lists one pair, whatever its /N says");
            document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(
                "Object stream 5 has a header that ends after 1 of the 1,000,000,000 objects its /N declares; only the first 1 of its 1,000,000,000 objects can be read from it.");
        }
    }

    [Fact]
    public void An_object_stream_header_s_tables_start_no_larger_than_its_bytes_can_hold()
    {
        // A header of four bytes and a /N of a billion: the header holds one pair at most, so the tables start at one, not
        // at 1,024 (#160).
        var data = Encoding.ASCII.GetBytes("6 0 <<>>");

        var allocated = Allocations.Least(() => ObjectStreamHeader.Read(data, 1_000_000_000, 4, cutByGuard: false), out var read);

        read.Count.Should().Be(1);
        read.Fault.Should().Be(ObjectStreamHeaderFault.Ended);
        allocated.Should().BeLessThanOrEqualTo(2 * (sizeof(int) + 32), "two tables of one pair each");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100_000)]
    [InlineData(1_000_000)]
    public void A_length_the_file_cannot_hold_is_settled_by_the_endstream_found_without_reaching_a_guard(int dataLength)
    {
        // "/Length 2147483647", the data, its endstream, then an object of 17 MB: the window grew to MaxObjectLength to
        // settle a length the file cannot hold, read 21.5 MB, reported limit.object and marked the object cut, though its
        // data was found whole, and threw under ThrowOnLimit (#174). The endstream found settles it, the data read once.
        var data = dataLength == 0 ? "short" : new string('d', dataLength);
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, "<< /Length 2147483647 >>\nstream\n" + data + "\nendstream")
            .WithObject(6, "(" + new string('p', 17 * 1024 * 1024) + ")")
            .BuildClassic(rootNumber: 1);

        foreach (var throwOnLimit in new[] { false, true })
        {
            using var source = new StrictCountingSource(file);
            using var document = PdfDocument.Open(source, new PdfReaderOptions { ThrowOnLimit = throwOnLimit }, ownsSource: false);
            var opened = source.BytesRead;
            var stream = document.GetObject(new PdfObjectId(5)).AsStream().Required();

            stream.Data.Length.Should().Be(data.Length);
            document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(
                string.Create(CultureInfo.InvariantCulture, $"The stream declared 2147483647 bytes but ended after {data.Length}."));
            document.Reader.IsCutAtLimit(5).Should().BeFalse();
            (source.BytesRead - opened).Should().BeLessThan(data.Length + (4L * PdfFileReader.InitialObjectWindow), "the data, read once");
        }
    }

    [Fact]
    public void Many_lengths_the_file_cannot_hold_read_the_file_once_not_once_each()
    {
        // Fifty such streams before an object of 17 MB: each grew its window to MaxObjectLength, and the reader read the
        // file sixty times over (#174).
        const int Streams = 50;
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        for (var k = 0; k < Streams; k++)
        {
            builder.WithObject(10 + k, "<< /Length 2147483647 >>\nstream\nshort\nendstream");
        }

        var file = builder.WithObject(100, "(" + new string('p', 17 * 1024 * 1024) + ")").BuildClassic(rootNumber: 1);
        using var source = new StrictCountingSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var opened = source.BytesRead;

        for (var k = 0; k < Streams; k++)
        {
            document.GetObject(new PdfObjectId(10 + k)).AsStream().Required().Data.Length.Should().Be(5);
        }

        (source.BytesRead - opened).Should().BeLessThan(Streams * 2L * PdfFileReader.InitialObjectWindow, "a window and a few bytes for each");
        document.Diagnostics.Should().HaveCount(Streams).And.OnlyContain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.StreamLengthInvalid);
    }

    [Fact]
    public void A_stream_whose_endobj_follows_its_length_without_endstream_reads_no_further_than_its_window_whatever_follows()
    {
        // iPRES's t02-05-01-014 before an object of 20 MB: no endstream after the data, which the window held, so it grew to
        // MaxObjectLength looking for one, took 16 MB as the data of a 68-byte stream, reported limit.object and marked the
        // object cut (#174). Its endobj plays the endstream's part.
        var data = new string('d', 68);
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, "<< /Length 68 >>\nstream\n" + data + "\r\n\n")
            .WithObject(6, "(" + new string('p', 20 * 1024 * 1024) + ")")
            .BuildClassic(rootNumber: 1);

        using var source = new StrictCountingSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var opened = source.BytesRead;

        document.GetObject(new PdfObjectId(5)).AsStream().Required().Data.Length.Should().Be(68);

        (source.BytesRead - opened).Should().BeLessThanOrEqualTo(PdfFileReader.InitialObjectWindow, "the window holds the object and what follows it");
        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.StreamTruncated);
        document.Reader.IsCutAtLimit(5).Should().BeFalse();
    }

    [Fact]
    public void A_length_the_file_cannot_hold_with_no_endstream_takes_the_rest_of_the_file_without_reaching_a_guard()
    {
        // "/Length 2147483647" and 20 MB of data to the end of the file, with no endstream and no endobj: the window grew to
        // MaxObjectLength and the guard was reported (#174). The data runs to the end of the file, the object without its
        // endobj, as for any stream the end of the file cuts.
        var text = "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n" +
            "5 0 obj\n<< /Length 2147483647 >>\nstream\n";
        var file = Encoding.ASCII.GetBytes(text + new string('d', 20 * 1024 * 1024));

        using var document = PdfDocument.Open(file);
        var stream = document.GetObject(new PdfObjectId(5)).AsStream().Required();

        stream.Data.Length.Should().Be(20 * 1024 * 1024);
        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.StreamTruncated).Which.Message.Should().Be(
            "The stream declared 2147483647 bytes, and no endstream follows its data before the end of the file; the 20971520 bytes to the end of the file are taken as its data.");
        document.Diagnostics.Should().NotContain(d => d.Code.StartsWith("limit.", StringComparison.Ordinal));
        document.Reader.IsCutAtLimit(5).Should().BeFalse();
        document.Reader.IsEndObjMissing(5, out _).Should().BeTrue();
    }

    [Fact]
    public void Sections_whose_lengths_name_one_object_a_guard_cut_read_it_once()
    {
        // A hundred cross-reference streams, chained by /Prev, each with /Length 5 0 R and rows that spell an endstream
        // first; the newest table places object 5, which holds 28 then 2,000,000 spaces, past MaxObjectLength. Each section
        // takes its data again to the 28 the cut left, which an endstream confirms, and the chain goes on: the cut object
        // was read again for each section, and opening read the file several times over (#224).
        const int Sections = 100;
        var file = new HandWrittenFile(version: "1.5");
        var one = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var two = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var five = file.Object(5, "28" + new string(' ', 2_000_000));
        var rows = Row(1, one) + Row(1, two);
        long previous = -1;

        for (var k = 0; k < Sections; k++)
        {
            var at = file.Position;
            var prev = previous < 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" /Prev {previous}");
            file.Raw(string.Create(CultureInfo.InvariantCulture, $"{100 + k} 0 obj\n<< /Type /XRef /Size 200 /W [1 4 2] /Index [8 2 1 2] /Root 1 0 R /Length 5 0 R{prev} >>\nstream\n"));
            file.Raw("\nendstream\0\0\0\0" + rows + "\nendstream\nendobj\n");
            previous = at;
        }

        var table = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f\r\n5 1\n{five:D10} 00000 n\r\ntrailer\n<< /Size 200 /Root 1 0 R /Prev {previous} >>\nstartxref\n{table}\n%%EOF\n"));
        var bytes = file.Bytes;
        using var source = new StrictCountingSource(bytes);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 64 * 1024 } };

        using var document = PdfDocument.Open(source, options, ownsSource: false);

        document.Reader.Structure.Sections.Should().HaveCount(Sections + 1).And.OnlyContain(section => !section.CutByLimit);
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        source.BytesRead.Should().BeLessThan(bytes.Length);

        static string Row(int type, long offset) => string.Concat(
            (char)type, (char)((offset >> 24) & 0xFF), (char)((offset >> 16) & 0xFF), (char)((offset >> 8) & 0xFF), (char)(offset & 0xFF), '\0', '\0');
    }

    [Fact]
    public void A_deferred_length_s_data_with_no_endstream_makes_its_section_malformed_reading_the_file_a_few_times()
    {
        // A cross-reference stream whose /Length only it indexes, 3 MB of rows, and its endstream misspelled: its search
        // reads the data to the end of the file, and its section is malformed — the file's fault, no guard's (#220). The
        // rebuild scans the file, and its stream, its length read then, is searched again: the file is read about four
        // times, as its sound twin is twice — the search and the decoding.
        var file = ChainFiles.AloneWithIndirectLength(padding: 450_000);
        file[(int)PdfTemplate.OffsetOf(file, "endstream")] = (byte)'x';
        using var source = new StrictCountingSource(file);

        using var document = PdfDocument.Open(source, options: null, ownsSource: false);

        document.Reader.Structure.Sections.Should().ContainSingle().Which.State.Should().Be(XRefSectionState.Malformed);
        document.Diagnostics.Should().NotContain(d => d.Code.StartsWith("limit.", StringComparison.Ordinal));
        source.BytesRead.Should().BeLessThan(5L * file.Length);
    }

    [Fact]
    public void Reading_streams_whose_data_share_a_stretch_of_a_file_held_in_memory_allocates_none_of_their_data()
    {
        // Two hundred streams, each header inside the data of the one before, every /Length confirmed by the one endstream
        // a megabyte on: each stream's data is the whole stretch, and reading each one's copied it, 200 MB from a file of
        // 1 MB (#170). A source in memory lends each stream's data where it lies.
        const int Streams = 200;
        var file = Overlapping(Streams, filler: 1024 * 1024);

        var allocated = Allocations.Least(() => PdfDocument.Open(file), ReadEvery, out var document, out var read);

        using (document)
        {
            read.Should().BeGreaterThan(Streams * 1024L * 1024, "each stream's data is the whole stretch");
            allocated.Should().BeLessThan(2L * 1024 * 1024, "the streams' objects, and none of their data");
            document.Diagnostics.Should().BeEmpty();
        }

        static long ReadEvery(PdfDocument document)
        {
            long read = 0;

            for (var k = 0; k < Streams; k++)
            {
                read += document.GetObject(new PdfObjectId(3 + k)).AsStream().Required().GetRawBytes().Length;
            }

            return read;
        }
    }

    [Fact]
    public void An_object_stream_header_s_tables_grow_no_larger_than_its_bytes_can_hold()
    {
        // 1,100 pairs, each of object 1, and a /N of a billion: the header's 6,590 bytes hold 1,647 pairs at most, so the
        // tables grow from 1,024 to that, not to 2,048, before they are trimmed to the pairs read (#160).
        var header = new StringBuilder();

        for (var pair = 0; pair < 1100; pair++)
        {
            header.Append(CultureInfo.InvariantCulture, $"1 {pair} ");
        }

        var data = Encoding.ASCII.GetBytes(header.ToString() + new string(' ', 1100));
        var first = header.Length;
        var room = (first + 1) / 4;
        room.Should().BeInRange(1101, 2047);

        var allocated = Allocations.Least(() => ObjectStreamHeader.Read(data, 1_000_000_000, first, cutByGuard: false), out var read);

        read.Count.Should().Be(1100);
        read.Fault.Should().Be(ObjectStreamHeaderFault.Ended);
        allocated.Should().BeLessThanOrEqualTo((2L * (1024 + room + 1100) * sizeof(int)) + (6 * 32), "the tables are 1,024, then the header's room, then the pairs read");
    }

    [Fact]
    public void An_object_stream_whose_N_its_blank_header_cannot_hold_allocates_no_table_for_it()
    {
        // Four megabytes of blanks before /First, and a /N of a million: the reader and the validator read no pair, and
        // allocate what decoding the stream takes, as for a /N of one, not tables sized by /N (#160).
        const int First = 4 * 1024 * 1024;
        var lying = BlankHeader(First, (First / 4) + 1);
        var honest = BlankHeader(First, 1);

        var reading = Allocations.Least(() => PdfDocument.Open(lying), Read, out var opened, out var six);
        var readingHonest = Allocations.Least(() => PdfDocument.Open(honest), Read, out var openedHonest, out _);
        var validating = Allocations.Least(() => PdfDocument.Open(lying), Validate, out var validated, out var report);
        var validatingHonest = Allocations.Least(() => PdfDocument.Open(honest), Validate, out var validatedHonest, out _);

        using (opened)
        using (openedHonest)
        using (validated)
        using (validatedHonest)
        {
            six.Should().BeSameAs(PdfNull.Instance);
            reading.Should().BeLessThan(readingHonest + (64 * 1024), "nothing is allocated in proportion to /N");
            validating.Should().BeLessThan(validatingHonest + (64 * 1024), "the validator reads the header as the reader does");
            report.Contains(PdfValidationRuleIds.XRefObjectStreamBroken).Should().BeTrue();
        }

        static PdfObject Read(PdfDocument document) => Measure(() => document.GetObject(new PdfObjectId(6)));

        static PdfValidationReport Validate(PdfDocument document) => Measure(() => new PdfValidator().Validate(document));

        static byte[] BlankHeader(int first, int count)
        {
            var file = new HandWrittenFile(version: "1.5");
            var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
            {
                [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
                [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            };
            var data = Compress(Encoding.ASCII.GetBytes(new string(' ', first) + "<<>>"));
            rows[5] = (1, file.Object(5, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N {count} /First {first} /Filter /FlateDecode /Length {data.Length} >>\nstream\n{Encoding.Latin1.GetString(data)}\nendstream")), 0);
            rows[6] = (2, 5, 0);
            file.CrossReferenceStream(7, 8, rows, "/Root 1 0 R");
            return file.Bytes;
        }
    }

    [Fact]
    public void An_object_stream_that_lists_one_offset_over_and_over_keeps_one_member()
    {
        // A million pairs "1 0 ", every member at the same offset: the header ends at the second, whose offset does not
        // follow the first's, and the stream weighs its data and one pair, not a million (#160).
        const int Pairs = 1_000_000;
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
        };
        var decoded = string.Concat(Enumerable.Repeat("1 0 ", Pairs)) + "<<>>";
        var data = Compress(Encoding.ASCII.GetBytes(decoded));
        rows[5] = (1, file.Object(5, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N {Pairs} /First {4 * Pairs} /Filter /FlateDecode /Length {data.Length} >>\nstream\n{Encoding.Latin1.GetString(data)}\nendstream")), 0);
        rows[6] = (2, 5, 0);
        file.CrossReferenceStream(7, 8, rows, "/Root 1 0 R");

        using var document = PdfDocument.Open(file.Bytes);

        Measure(() => document.GetObject(new PdfObjectId(6))).Should().BeSameAs(PdfNull.Instance);
        document.Reader.ObjectStreamBytes.Should().Be(decoded.Length + 16);
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(
            "Object stream 5 has a header that lists object 1 at offset 0, not past the offset of the object before it, 0, after 1 of the 1,000,000 objects its /N declares; only the first 1 of its 1,000,000 objects can be read from it.");
    }

    [Fact]
    public void A_member_of_millions_of_arrays_is_read_no_further_than_MaxObjectLength()
    {
        // Eight megabytes of "[]" in one member, from a file of a few kilobytes: read whole, it allocated some 370 MB. Read
        // to a bound of 256 KB, it allocates in proportion to the bound, and the guard says so (#160).
        const int Pairs = 4 * 1024 * 1024;
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
        };
        var decoded = new byte[4 + 1 + (2 * Pairs) + 1];
        "4 0 ["u8.CopyTo(decoded);

        for (var i = 5; i < decoded.Length - 1; i += 2)
        {
            decoded[i] = (byte)'[';
            decoded[i + 1] = (byte)']';
        }

        decoded[^1] = (byte)']';
        var data = Compress(decoded);
        rows[5] = (1, file.Object(5, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First 4 /Filter /FlateDecode /Length {data.Length} >>\nstream\n{Encoding.Latin1.GetString(data)}\nendstream")), 0);
        rows[4] = (2, 5, 0);
        file.CrossReferenceStream(6, 7, rows, "/Root 1 0 R");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 256 * 1024 } };

        var allocated = Allocations.Least(
            () => PdfDocument.Open(file.Bytes, options),
            document => Measure(() => document.GetObject(new PdfObjectId(4))),
            out var opened,
            out var four);

        using (opened)
        {
            four.AsArray().Required().Count.Should().BeLessThanOrEqualTo(128 * 1024, "the bound holds 128 Ki pairs, the last one cut");
            allocated.Should().BeLessThan(128L * 1024 * 1024, "the stream is decoded, and its member read to the bound, not whole");
            opened.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitObject);
        }
    }

    [Fact]
    public void A_stream_inside_an_object_stream_is_not_decoded_whatever_it_would_decode_to()
    {
        // A member written as a stream whose data inflates to 50 MB: under a bound of 1 MB and ThrowOnLimit, it decoded whole,
        // with no report, under the default guard, once a caller decoded what the reader served. It now reads as null, and
        // its data is never decoded (#161).
        var zeros = Compress(new byte[50 * 1024 * 1024]);
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Big 7 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
        };
        var data = Compress(Encoding.Latin1.GetBytes(string.Create(CultureInfo.InvariantCulture, $"7 0 << /Filter /FlateDecode /Length {zeros.Length} >>stream\n{Encoding.Latin1.GetString(zeros)}\nendstream")));
        rows[5] = (1, file.Object(5, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First 4 /Filter /FlateDecode /Length {data.Length} >>\nstream\n{Encoding.Latin1.GetString(data)}\nendstream")), 0);
        rows[7] = (2, 5, 0);
        file.CrossReferenceStream(6, 8, rows, "/Root 1 0 R");
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 1024 * 1024 } };

        var allocated = Allocations.Least(
            () => PdfDocument.Open(file.Bytes, options),
            document => Measure(() =>
            {
                // A caller decodes the stream it is given: that is where a member's data was decoded.
                var value = document.GetObject(new PdfObjectId(7));
                _ = (value as PdfStream)?.Decode();
                return value;
            }),
            out var opened,
            out var seven);

        using (opened)
        {
            seven.Should().BeSameAs(PdfNull.Instance);
            allocated.Should().BeLessThan(4L * 1024 * 1024, "the object stream is decoded, and its member's data is not");
            opened.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.ObjectStreamMemberStream);
        }
    }

    [Fact]
    public void An_object_stream_of_members_one_byte_apart_reads_each_to_the_next_one_s_start_in_time()
    {
        // A hundred and sixty thousand members, each an open bracket the next one's start closes: read to the end of the
        // data, they took a minute together (#200). Each is reported, and the report formats a message only while the
        // diagnostics keep it.
        const int Members = 160_000;
        var file = MembersOneByteApart(Members);
        using var document = PdfDocument.Open(file);

        var arrays = Measure(() =>
        {
            var read = 0;

            for (var i = 0; i < Members; i++)
            {
                read += document.GetObject(new PdfObjectId(1000 + i)) is PdfArray { Count: 0 } ? 1 : 0;
            }

            return read;
        });

        arrays.Should().Be(Members);
        document.Diagnostics.Should().OnlyContain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.SyntaxTruncatedObject);
        document.Diagnostics[0].Message.Should().StartWith("The next member of the object stream started inside an array, which was never closed.");
    }

    [Fact]
    public void The_object_stream_budget_weighs_the_header_tables_with_the_data()
    {
        // Each member costs its number and its offset, and its place in the lookup an entry's wrong index builds (#160).
        const int Members = 1000;
        var file = ObjectStreams(streams: 1, decodedLength: 0, objectsPerStream: Members, everyIndexWrong: false);
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(PackedNumber(0, 0, Members))).AsDictionary().Required();

        var decoded = document.GetObject(new PdfObjectId(StreamNumber(0))).AsStream().Required().Decode().Length;
        document.Reader.ObjectStreamBytes.Should().Be(decoded + (16L * Members));
    }

    [Fact]
    public void Reads_streams_whose_length_names_a_name_of_millions_of_characters_within_an_allocation_budget()
    {
        // #159's shape, scaled down: an object stream inflates to a name of 4M characters, and four streams take it as
        // their /Length. Each report quotes the name's first 127 bytes, never a copy of the whole.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, "/" + new string('A', 4 * 1024 * 1024))
            .WithObject(6, "<< /Length 5 0 R >>\nstream\nabc\nendstream")
            .WithObject(7, "<< /Length 5 0 R >>\nstream\nabc\nendstream")
            .WithObject(8, "<< /Length 5 0 R >>\nstream\nabc\nendstream")
            .WithObject(9, "<< /Length 5 0 R >>\nstream\nabc\nendstream")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [5], compressObjectStream: true);
        using var document = PdfDocument.Open(file);
        _ = document.GetObject(new PdfObjectId(5));

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        Measure(() =>
        {
            for (var number = 6; number <= 9; number++)
            {
                _ = document.GetObject(new PdfObjectId(number));
            }

            return 0;
        });
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        allocated.Should().BeLessThan(2 * 1024 * 1024, "a report quotes 127 bytes of the name, whatever its length");
        document.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.StreamLengthInvalid).Should().HaveCount(4)
            .And.OnlyContain(d => d.Message.Contains(" (the first 127 of 4,194,304 bytes)", StringComparison.Ordinal));
    }

    [Fact]
    public void Reads_fifty_streams_whose_length_names_one_long_name_within_an_allocation_budget()
    {
        // A diagnostic capacity of one keeps no message: what reading the streams costs is all that is left to bound.
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, "/" + new string('A', 1_000_000));

        for (var number = 6; number < 56; number++)
        {
            builder.WithObject(number, "<< /Length 5 0 R >>\nstream\nabc\nendstream");
        }

        using var document = PdfDocument.Open(builder.BuildClassic(rootNumber: 1), PdfReaderOptions.Default with { DiagnosticCapacity = 1 });
        _ = document.GetObject(new PdfObjectId(5));

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var number = 6; number < 56; number++)
        {
            _ = document.GetObject(new PdfObjectId(number));
        }

        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        allocated.Should().BeLessThan(4 * 1024 * 1024, "no stream copies the name it takes for its length");

        // What the reader keeps of each stream's fault for the document's life is the name itself, not words about it.
        var name = document.GetObject(new PdfObjectId(5));
        for (var number = 6; number < 56; number++)
        {
            document.Reader.TryGetStreamLengthFault(number, out var fault).Should().BeTrue();
            fault.Kind.Should().Be("a name");
            fault.Value.Should().BeSameAs(name);
        }
    }

    [Fact]
    public void Decodes_a_stream_under_a_filter_name_of_a_million_characters_a_hundred_times_within_a_budget()
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, PdfName.Get(new string('X', 1_000_000)));
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory("abc"u8.ToArray()));
        var diagnostics = new PdfDiagnostics();
        _ = stream.Decode(diagnostics);

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var time = 0; time < 100; time++)
        {
            _ = stream.Decode(diagnostics);
        }

        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        allocated.Should().BeLessThan(1024 * 1024, "each report quotes 127 bytes of the filter's name");
        diagnostics[0].Message.Should().StartWith("The filter /" + new string('X', 127) + " (the first 127 of 1,000,000 bytes)");
    }

    [Fact]
    public void Quotes_a_keyword_of_ten_megabytes_where_a_subsection_should_start_by_its_first_bytes()
    {
        // The table's window grows until it holds the keyword whole; what the section's fault keeps of it, and the
        // finding repeats, is 127 bytes and its length.
        var keyword = new string('k', 10 * 1024 * 1024);
        var file = PdfTemplate.SoundWith("{row:3}\ntrailer", "{row:3}\n" + keyword + "\ntrailer");

        using var document = PdfDocument.Open(file);
        var finding = new PdfValidator().Validate(document).Findings.Single(f => f.RuleId == PdfValidationRuleIds.XRefSectionMalformed);

        finding.Message.Length.Should().BeLessThan(512);
        finding.Message.Should().Contain("it holds the keyword " + new string('k', 127) + " (the first 127 of 10,485,760 bytes) at offset ");
    }

    [Fact]
    public void Validating_an_object_whose_key_path_holds_megabyte_keys_stays_within_an_allocation_budget()
    {
        // Ten keys of a megabyte each lead to a reference to nothing. The finding names the path by its first and last
        // four keys, each quoted by its first 127 bytes, never by copies of the ten megabytes.
        var keys = new string[10];
        var value = new StringBuilder("99 0 R");

        for (var index = keys.Length - 1; index >= 0; index--)
        {
            keys[index] = new string((char)('A' + index), 1024 * 1024);
            value.Insert(0, "<< /" + keys[index] + " ").Append(" >>");
        }

        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R /Extra 3 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, value.ToString())
            .BuildClassic(rootNumber: 1);
        using var document = PdfDocument.Open(file);
        _ = document.GetObject(new PdfObjectId(3));

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var findings = new PdfValidator().Validate(document).Findings;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        allocated.Should().BeLessThan(4 * 1024 * 1024, "a path quotes 127 bytes of each of the eight keys it keeps");
        var quoted = "/" + new string('A', 127) + " (the first 127 of 1,048,576 bytes)";
        findings.Single(f => f.RuleId == PdfValidationRuleIds.ObjectReferenceMissing).Message
            .Should().Contain("under " + quoted).And.Contain(" (2 steps) /" + new string('G', 127));
    }

    [Fact]
    public void Formats_no_message_for_the_faults_of_an_object_stream_member_once_the_diagnostics_are_full()
    {
        // A member of a million stray tokens meets a fault at each. Past the thousand entries kept, each is counted and
        // dropped, and its message, which names the member and the byte, is not built for nothing.
        var member = "[" + string.Concat(Enumerable.Repeat(") ", 1_000_000)) + "]";
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, member)
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [3]);
        using var document = PdfDocument.Open(file);

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var array = Measure(() => document.GetObject(new PdfObjectId(3)).AsArray().Required());
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        array.Count.Should().Be(1_000_000);
        document.Diagnostics.SuppressedCount.Should().BeGreaterThan(990_000);
        allocated.Should().BeLessThan(64 * 1024 * 1024, "a message is built only for an entry the diagnostics keep");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Quotes_no_number_beyond_what_a_real_can_hold_once_the_diagnostics_are_full(bool packed)
    {
        // Twenty thousand numbers of 310 digits, each read as null and reported. Past the thousand entries kept, each is
        // counted and dropped, and the quote of its first 127 bytes is not built for nothing: quoted, they allocated 24 and
        // 31 MB, against 4 and 8 MB once only the kept ones are.
        var numbers = "[" + string.Concat(Enumerable.Repeat("1" + new string('0', 309) + " ", 20_000)) + "]";
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, numbers);
        using var document = PdfDocument.Open(packed ? builder.BuildWithXRefStream(rootNumber: 1, compressedObjects: [3]) : builder.BuildClassic(rootNumber: 1));

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var array = Measure(() => document.GetObject(new PdfObjectId(3)).AsArray().Required());
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        array.Count.Should().Be(20_000);
        document.Diagnostics.SuppressedCount.Should().BeGreaterThan(18_000);
        allocated.Should().BeLessThan(16 * 1024 * 1024, "a number is quoted only for an entry the diagnostics keep");
    }

    [Theory]
    [InlineData(false, "number")]
    [InlineData(true, "number")]
    [InlineData(false, "repeated key")]
    [InlineData(true, "repeated key")]
    [InlineData(false, "name")]
    [InlineData(true, "name")]
    [InlineData(false, "hexadecimal string")]
    [InlineData(true, "hexadecimal string")]
    [InlineData(false, "endobj")]
    public void Quotes_nothing_once_the_document_s_diagnostics_are_full_however_many_objects_hold_the_faults(bool packed, string fault)
    {
        // Twenty thousand objects, each holding a fault whose report quotes or counts something: a number of 310 digits, a
        // key given again, a name whose number sign is no escape, a hexadecimal string with stray bytes, an array an endobj
        // ends. Past the thousand entries the document keeps, each report is counted and dropped, and neither its quote nor
        // its message is built, whichever object makes it: built for each object, the numbers' allocated about 16 MB more than
        // objects as long without the fault. Each fault is set against the same object written soundly.
        var (faulty, sound) = fault switch
        {
            "number" => ("[1" + new string('0', 309) + "]", "[1" + new string(' ', 309) + "]"),
            "repeated key" => ("<< /LongerKeyName 1 /LongerKeyName 2 >>", "<< /LongerKeyName 1 /LongerKeyNamf 2 >>"),
            "name" => ("[/LongerName#zzWithASign]", "[/LongerName#20WithASign]"),
            "hexadecimal string" => ("[<41x42y43z44>]", "[<41 42 43 44>]"),
            _ => ("[1 2 3 ", "[1 2 3]"),
        };
        var extra = AllocatedReadingEveryObject(ManyObjects(packed, faulty)) - AllocatedReadingEveryObject(ManyObjects(packed, sound));

        extra.Should().BeLessThan(4 * 1024 * 1024, "a report the document's diagnostics drop is formatted for no object");
    }

    [Theory]
    [InlineData("/W [99 99 99]", "is malformed: it holds 0 rows where its /Index and /Size declare 5.")]
    [InlineData("/W [1 2147483647 2147483647]", "is malformed: it holds 0 rows where its /Index and /Size declare 5.")]
    [InlineData("/W [1 4294967300 2]", "is malformed: it holds 0 rows where its /Index and /Size declare 5.")]
    [InlineData("/W [9223372036854775807 9223372036854775807 9223372036854775807]", "is malformed: it holds 0 rows where its /Index and /Size declare 5.")]
    [InlineData("/W [1 -4294967300 2]", "cannot be read: its /W gives a field a negative width.")]
    [InlineData("/W [-9223372036854775808 4 2]", "cannot be read: its /W gives a field a negative width.")]
    public void Survives_a_cross_reference_stream_with_impossible_field_widths(string widths, string fault)
    {
        // The stream startxref names, so that its widths are read. Believed, [1 2147483647 2147483647] made a row length
        // that wrapped to -1, and slicing a row threw; narrowed to an int, 4294967300 was 4, and the rows read as [1 4 2] in
        // silence; summed in a long, three widths of 2^63 - 1 wrap. Each width is narrowed only once it is known to be
        // within the decoded data, and a row longer than the data is one the data does not hold: the section holds none of
        // the rows it declares, and the index is rebuilt. A negative width is no width (#158, #216).
        var sound = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .BuildWithXRefStream(rootNumber: 1);
        var bytes = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(sound).Replace("/W [1 4 2]", widths, StringComparison.Ordinal));

        using var document = Measure(() => PdfDocument.Open(bytes));

        document.Catalog.Required();
        document.WasRepaired.Should().BeTrue();
        new PdfValidator().Validate(document).Findings.Should().ContainSingle(finding => finding.RuleId == PdfValidationRuleIds.XRefSectionMalformed)
            .Which.Message.Should().EndWith(fault);
    }

    [Fact]
    public void A_cross_reference_stream_of_one_byte_rows_decoding_to_the_bound_indexes_no_more_entries_than_the_guard_allows()
    {
        // #164: /W [0 1 0] makes a row of one byte, an object at the offset that byte gives, and two Flate filters let a
        // few hundred bytes decode to the 256 MB bound itself, which no guard reports: 268,435,456 rows. Unguarded, the
        // index took one entry a row, 20 GB and more, and the process ran out of memory. The guard, lowered here to keep the
        // test light, stops the index at its bound, reported once, at the section. Decoding the 256 MB is what the rest
        // of the allocation is, whatever the guard.
        var file = OneByteRows(256 * 1024 * 1024, out var section);
        var limits = PdfReaderLimits.Default with { MaxIndexEntries = 1_000_000 };
        file.Length.Should().BeLessThan(2048);

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        using var document = Measure(() => PdfDocument.Open(file, new PdfReaderOptions { Limits = limits }));
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        document.ObjectCount.Should().Be(1_000_000);
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.LimitIndexEntries);
        report.Position.Should().Be(section);
        report.Message.Should().Be(
            "The index reached 1,000,000 entries; the objects past them were not indexed. Raise PdfReaderLimits.MaxIndexEntries to read past it.");
        // Measured at 575,312,600 bytes twice on 2026-10-28, 5 % above: the index sized once for the guard's entries,
        // where the map it was before #155 took 595,837,384 sized once and 675,532,296 grown by doubling.
        allocated.Should().BeLessThan(604_080_000, "the index holds the guard's entries, and the decoded rows are read once");
    }

    [Fact]
    public void Blank_lines_among_a_table_s_rows_make_room_for_no_more_rows_than_their_bytes_hold()
    {
        // A table's rows are counted by their lines, and a line can be a single byte: two million line feeds among four
        // rows made room for two million entries, 104 MB, where their bytes hold 110,000 rows of 19 bytes.
        var text = Encoding.Latin1.GetString(new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "null")
            .BuildClassic(rootNumber: 1));
        var file = Encoding.Latin1.GetBytes(text.Replace("trailer\n", new string('\n', 2_000_000) + "trailer\n", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file);

        document.ObjectCount.Should().Be(4);
        document.Reader.Index.Capacity.Should().BeLessThan(200_000);
    }

    [Theory]
    [InlineData(DeclaredSize.Stream, 8_000_000)]
    [InlineData(DeclaredSize.Stream, 268_435_456)]
    [InlineData(DeclaredSize.Table, 8_000_000)]
    [InlineData(DeclaredSize.Chain, 8_000_000)]
    public void A_declared_size_far_past_the_rows_a_file_holds_makes_room_for_no_more_than_they_hold(string shape, int declared)
    {
        // #164: the index makes room for a section's rows before it reads them, and for a chain's objects once its newest
        // section is read. A count the file declares is its claim: a few hundred bytes claiming eight million objects made
        // room for them, 416 MB, before anything was read. Room is made for no more rows than the data holds, or the bytes
        // hold at a classic table's 20 bytes a row.
        var file = SizeClaimed(shape, declared);
        file.Length.Should().BeLessThan(1024);

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        using var document = Measure(() => PdfDocument.Open(file));
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        document.ObjectCount.Should().BeLessThan(6);
        allocated.Should().BeLessThan(64 * 1024, "room is made for the rows the file holds, not for those it claims");
    }

    [Fact]
    public void Rows_that_index_nothing_make_room_for_no_more_entries_than_the_guard_allows()
    {
        // A reserved row type indexes nothing, and the room its rows ask for is reserved all the same, as the rows the data
        // holds: the guard bounds it, as it bounds the rows of a type that index. Eight million rows of type 3 decode from 16
        // KB; under a guard of a million entries, the index is sized for a million.
        var file = ReservedTypeRows(8_000_000);
        var limits = PdfReaderLimits.Default with { MaxIndexEntries = 1_000_000 };
        file.Length.Should().BeLessThan(32 * 1024);

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        using var document = Measure(() => PdfDocument.Open(file, new PdfReaderOptions { Limits = limits }));
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        document.ObjectCount.Should().Be(4);
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries);
        // Measured at 119,843,360 and 119,851,472 bytes on 2026-10-28, 5 % above: 16 MB of rows decoded, and the index
        // sized for a million entries, its slots with them, where it would be for eight million past the guard;
        // 140,272,056 with the map it was before #155.
        allocated.Should().BeLessThan(125_850_000, "the room made for rows that index nothing is bounded by the guard");
    }

    [Fact]
    public void A_rebuild_finds_a_document_behind_millions_of_repeated_headers_of_one_object()
    {
        // #183: a rebuild counted every header it read, and stopped after two million, once the 1 MB piece it was reading
        // was done, so that 2,200,000 copies of "1 0 obj" hid the document after them, which nothing reported. A header for
        // a number the index holds takes no room: the scan reads to the file's end, and the last definition wins.
        const int Copies = 2_200_000;
        var prefix = Encoding.ASCII.GetBytes("%PDF-1.7\n" + string.Concat(Enumerable.Repeat("1 0 obj\n", Copies)));
        byte[] file =
        [
            .. prefix,
            .. "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n"u8,
            .. "trailer\n<< /Root 1 0 R >>\nstartxref\n999999999\n%%EOF\n"u8,
        ];

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        using var document = Measure(() => PdfDocument.Open(file));
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        document.WasRepaired.Should().BeTrue();
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        document.ObjectCount.Should().Be(2);
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries);
        allocated.Should().BeLessThan(1024 * 1024, "a header read again replaces an entry, and allocates nothing");
    }

    [Fact]
    public void Rows_a_section_refuses_count_toward_the_entries_the_index_may_hold()
    {
        // A refused row keeps its number out of the index, so that no older section's row stands for it: the reader holds
        // that number all the same, and a section of refused rows alone would fill memory the index never shows. Object
        // 0 and the three objects of the file are indexed; the rows for 4 to 53 give offsets past what a long holds.
        var file = RefusedRows(50, out var section);

        using (var guarded = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 20 } }))
        {
            guarded.ObjectCount.Should().Be(4);
            guarded.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
            guarded.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries)
                .Which.Position.Should().Be(section);
        }

        using var whole = PdfDocument.Open(file);
        whole.ObjectCount.Should().Be(4);
        whole.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries);
    }

    [Fact]
    public void Rows_past_a_full_index_are_not_read_so_that_one_refused_there_marks_nothing_damaged()
    {
        // The index fills at the stream's third row; the rows past it can only repeat numbers it holds, and are not read.
        // Read, a row refused there marked the index damaged, and the next lookup of an object past the bound rebuilt it.
        var file = RefusedRows(50, out _);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 2 } };

        using var document = PdfDocument.Open(file, options);

        document.GetObject(new PdfObjectId(10)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitIndexEntries);
    }

    [Fact]
    public void Serves_no_object_under_a_number_past_a_long_that_once_wrapped_to_its_number()
    {
        // #157: 92233720368547758082 wrapped at its 19th digit to 2, so /Pages led to object 2, and the header written
        // with that number, where the index places object 2, was taken for object 2's, with nothing reported. It is a
        // real: /Pages reads as one, the "0 R" after it is reported, and no object 2 is there.
        var template = PdfTemplate.Sound
            .Replace("/Pages 2 0 R", "/Pages 92233720368547758082 0 R", StringComparison.Ordinal)
            .Replace("\n2 0 obj\n", "\n2 00000000000000000000 obj\n", StringComparison.Ordinal);
        var bytes = Encoding.Latin1.GetBytes(
            Encoding.Latin1.GetString(PdfTemplate.Build(template))
                .Replace("2 00000000000000000000 obj", "92233720368547758082 0 obj", StringComparison.Ordinal));

        using var document = Measure(() => PdfDocument.Open(bytes));

        document.Catalog.GetRaw(PdfName.Pages).Should().BeOfType<PdfReal>().Which.Value.Should().Be(92233720368547758082d);
        document.GetObject(new PdfObjectId(2)).Should().BeSameAs(PdfNull.Instance);
        document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.SyntaxUnexpectedToken).Select(entry => entry.Position)
            .Should().Contain(PdfTemplate.OffsetOf(bytes, "/Pages 92233720368547758082 0 R") + "/Pages 92233720368547758082 ".Length);
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.SyntaxNumberOutOfRange);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0.")]
    public void Reads_a_number_fifteen_million_digits_long_in_time(string lead)
    {
        // Within MaxObjectLength, 16 MiB, so that the parser meets the digits rather than the object's limit. As an
        // integer, it is past the largest a real can hold, and reads as null; as a real below 1, it reads 7/9.
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "[" + lead + new string('7', 15_000_000) + "]")
            .BuildClassic(rootNumber: 1);
        using var document = PdfDocument.Open(bytes);

        var value = Measure(() => document.GetObject(new PdfObjectId(3)).AsArray().Required()[0]);

        if (lead.Length == 0)
        {
            value.Should().BeSameAs(PdfNull.Instance);
            document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.SyntaxNumberOutOfRange);
        }
        else
        {
            value.Should().BeOfType<PdfReal>().Which.Value.Should().Be(0.7777777777777778);
        }
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
    public void Opening_a_chain_whose_value_leads_through_lengths_longer_than_the_stack_is_deep_reads_a_bounded_amount()
    {
        // The older stream's /Index is object 5, a stream whose /Length is the next stream's, 50,000 times over, each
        // placed by the newer table. A stream is no value the chain reads, but each is parsed to be known as one, its
        // /Length read first: the reads nest as loads do, within MaxNestedLoads and the stack, and the section is
        // malformed.
        const int Chain = 50_000;
        var placed = new Dictionary<int, string>();

        for (var number = 10; number < 10 + Chain; number++)
        {
            var length = number + 1 < 10 + Chain ? $"{number + 1} 0 R" : "1";
            placed[number] = $"<< /Length {length} >>\nstream\nx\nendstream";
        }

        var file = ChainFiles.UnderAnUpdate(
            "/Type /XRef /Size 8 /W [1 4 2] /Index 5 0 R /Length {length}",
            "<< /Length 10 0 R >>\nstream\nx\nendstream",
            compressed: false,
            predicted: false,
            placed: true,
            placedToo: placed);

        using var document = Measure(() => PdfDocument.Open(file));

        document.Reader.Structure.Sections[1].Fault.Should().Be(
            "its /Index is the reference 5 0 R, which no section read before it places where it can be read");
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
    public void Survives_deeply_nested_dictionaries_inside_an_object()
    {
        var deep = NestedDictionaries(20000, "0");
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

        var (first, diagnostics, failure) = ReadObject3OnASmallStack(builder.BuildClassic(rootNumber: 1));

        failure.Should().BeNull();
        first.Should().BeOfType<PdfArray>();
        diagnostics.Required().Contains(PdfDiagnosticCodes.SyntaxDepthExceeded).Should().BeTrue();
    }

    [Theory]
    [InlineData(32, 64)]
    [InlineData(127, 64)]
    public void A_file_nesting_dictionaries_deeper_than_the_stack_allows_reads_them_as_null_without_crashing(int nesting, int streams)
    {
        // The same chain of streams as above, each inside nested dictionaries instead of nested arrays.
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        for (var number = 3; number < 3 + streams; number++)
        {
            var length = number + 1 < 3 + streams ? $"{number + 1} 0 R" : "1";
            builder.WithObject(number, NestedDictionaries(nesting, $"<< /Length {length} >>\nstream\nx\nendstream"));
        }

        var (first, diagnostics, failure) = ReadObject3OnASmallStack(builder.BuildClassic(rootNumber: 1));

        failure.Should().BeNull();
        first.Should().BeOfType<PdfDictionary>();
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
    public void A_fault_of_an_object_stream_is_held_once_by_its_document_however_often_the_stream_is_decoded()
    {
        // Object stream 7's hexadecimal data lacks its end marker. The reader decodes it to serve object 6, and again to
        // serve object 10 once a stream of 33 MB has pushed it out of the budget; the validator decodes it once more, and
        // a caller twice, passing no diagnostics and then the document's: the document holds the report once (#144).
        // Diagnostics a caller passes of its own hold every decoding's. Object 9 is followed by 33 MB of padding, which an
        // object limit of 64 MB lets the reader see to its end.
        var file = PackedFile(
            [
                new PackedStream(7, "/Filter /ASCIIHexDecode", [(6, "<< /Title (six) >>"), (10, "<< /Title (ten) >>")], PackedData.Hex),
                new PackedStream(8, string.Empty, [(9, "<< /K (large) >>")], PackedData.Flate, Padding: 33 * 1024 * 1024),
            ]);
        file[file.AsSpan().IndexOf(">\nendstream"u8)] = (byte)' ';
        var header = file.AsSpan().IndexOf("7 0 obj"u8);
        var dataStart = header + file.AsSpan(header).IndexOf("stream\n"u8) + "stream\n".Length;
        var report = $"Repair filter.failed at {dataStart}: An ASCIIHex stream ends without its > end marker; it decoded to the end of its data.";

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 64 * 1024 * 1024 } });
        Title(document.GetObject(new PdfObjectId(6))).Should().Be("six");
        document.GetObject(new PdfObjectId(9)).Should().BeOfType<PdfDictionary>();
        Title(document.GetObject(new PdfObjectId(10))).Should().Be("ten");
        new PdfValidator().Validate(document);
        var stream = document.GetObject(new PdfObjectId(7)).AsStream().Required();
        stream.Decode();
        stream.Decode(document.Diagnostics);
        var own = new PdfDiagnostics();
        stream.Decode(own);
        stream.Decode(own);

        document.Diagnostics.Select(d => d.ToString()).Should().Equal(report);
        own.Select(d => d.ToString()).Should().Equal(report, report);
    }

    [Fact]
    public void A_document_holds_alike_reports_of_two_streams_and_of_two_filters_of_one_stream_each_once_a_decoding()
    {
        // Streams 3 and 4 lack their hexadecimal end markers alike, and each filter of stream 5 lacks it too: reports under
        // the same code, of the same message for stream 5's two filters, which are not repeats of an earlier decoding.
        // Decoded twice each into the document's diagnostics, they are held once a stream and once a filter (#144).
        var inner = Convert.ToHexString("Hello"u8);
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .Stream(3, "/Filter /ASCIIHexDecode", inner)
            .Stream(4, "/Filter /ASCIIHexDecode", inner)
            .Stream(5, "/Filter [/ASCIIHexDecode /ASCIIHexDecode]", Convert.ToHexString(Encoding.ASCII.GetBytes(inner)))
            .BuildClassic(rootNumber: 1);

        using var document = PdfDocument.Open(file);

        foreach (var number in (int[])[3, 4, 5, 3, 4, 5])
        {
            Encoding.ASCII.GetString(document.GetObject(new PdfObjectId(number)).AsStream().Required().Decode().Span).Should().Be("Hello");
        }

        document.Diagnostics.Select(d => d.Code).Should().Equal(Enumerable.Repeat(PdfDiagnosticCodes.FilterFailed, 4));
        document.Diagnostics.Select(d => d.Position).Distinct().Should().HaveCount(3);
        document.Diagnostics.Select(d => d.Message).Should().OnlyContain(
            message => message == "An ASCIIHex stream ends without its > end marker; it decoded to the end of its data.");
    }

    [Fact]
    public void What_a_decoding_met_before_a_guard_threw_is_held_once_though_each_decoding_throws()
    {
        // Stream 3's hexadecimal data passes over a byte that encodes nothing, then decodes past the bound: opened to
        // throw, each decoding throws at the guard, and what it met before is moved into the document all the same, once.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .Stream(3, "/Filter /ASCIIHexDecode", "4G" + Convert.ToHexString(new byte[64]) + ">")
            .BuildClassic(rootNumber: 1);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 16 }, ThrowOnLimit = true };

        using var document = PdfDocument.Open(file, options);
        var stream = document.GetObject(new PdfObjectId(3)).AsStream().Required();

        FluentActions.Invoking(() => stream.Decode()).Should().Throw<PdfLimitExceededException>();
        FluentActions.Invoking(() => stream.Decode(document.Diagnostics)).Should().Throw<PdfLimitExceededException>();

        document.Diagnostics.Select(d => d.Message).Should().Equal(
            "An ASCIIHex stream holds a byte that encodes nothing before decoding stopped at the bound, at byte 2 of its 131; it was passed over.");
    }

    [Fact]
    public void A_report_the_capacity_leaves_out_of_one_decoding_is_counted_by_its_document()
    {
        // One decoding reports two faults of stream 3 — a byte that encodes nothing, and no end marker — into a document
        // that keeps one report: the second is counted among those it left out, as any report it has no room for is.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .Stream(3, "/Filter /ASCIIHexDecode", "4G8656C6C6F")
            .BuildClassic(rootNumber: 1);

        using var document = PdfDocument.Open(file, new PdfReaderOptions { DiagnosticCapacity = 1 });
        document.GetObject(new PdfObjectId(3)).AsStream().Required().Decode();

        document.Diagnostics.Select(d => d.Message).Should().Equal(
            "An ASCIIHex stream holds a byte that encodes nothing, at byte 2 of its 11; it was passed over.");
        document.Diagnostics.SuppressedCount.Should().Be(1);
    }

    [Fact]
    public void A_report_full_diagnostics_left_out_is_counted_again_each_time_its_stream_is_decoded()
    {
        // Stream 3 fills a document that keeps one report; stream 4's fault finds no room, and each decoding of stream 4
        // meets it again: it is counted each time, as a report the document has no room for is.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .Stream(3, "/Filter /ASCIIHexDecode", "48656C6C6F")
            .Stream(4, "/Filter /ASCIIHexDecode", "4G8656C6C6F>")
            .BuildClassic(rootNumber: 1);

        using var document = PdfDocument.Open(file, new PdfReaderOptions { DiagnosticCapacity = 1 });
        document.GetObject(new PdfObjectId(3)).AsStream().Required().Decode();
        var stream = document.GetObject(new PdfObjectId(4)).AsStream().Required();
        var counts = new List<int>();

        for (var decoding = 0; decoding < 3; decoding++)
        {
            stream.Decode();
            counts.Add(document.Diagnostics.SuppressedCount);
        }

        counts.Should().Equal(1, 2, 3);
        document.Diagnostics.Should().ContainSingle();
    }

    [Fact]
    public void A_report_its_document_holds_takes_no_room_when_a_stream_is_decoded_again_into_full_diagnostics()
    {
        // The document keeps one report: decoding the stream again repeats it, which is dropped before it can take room or
        // be counted as one the capacity left out.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .Stream(3, "/Filter /ASCIIHexDecode", "48656C6C6F")
            .BuildClassic(rootNumber: 1);

        using var document = PdfDocument.Open(file, new PdfReaderOptions { DiagnosticCapacity = 1 });
        var stream = document.GetObject(new PdfObjectId(3)).AsStream().Required();
        stream.Decode();
        stream.Decode();

        document.Diagnostics.Should().ContainSingle();
        document.Diagnostics.SuppressedCount.Should().Be(0);
    }

    [Fact]
    public void A_guard_an_object_stream_reaches_is_held_once_by_its_document_and_counted_each_time()
    {
        // Object stream 7 decodes past the bound: the reader, the validator and a caller each decode it, and each cut is
        // the guard's, counted though its document holds its report once (#144): what the guard cut is never taken for
        // damage. Opened to throw, each decoding throws.
        var file = PackedFile([new PackedStream(7, string.Empty, [(6, "<< /Title (six) >>")], PackedData.Flate, Padding: 4096)]);
        var limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 1024 };

        using (var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits }))
        {
            Title(document.GetObject(new PdfObjectId(6))).Should().Be("six");
            new PdfValidator().Validate(document);
            var stream = document.GetObject(new PdfObjectId(7)).AsStream().Required();
            stream.Decode();
            var own = new PdfDiagnostics();
            stream.Decode(own);

            document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
            document.Diagnostics.LimitsReached.Should().Be(3);
            own.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
        }

        using var strict = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits, ThrowOnLimit = true });
        var data = strict.GetObject(new PdfObjectId(7)).AsStream().Required();

        FluentActions.Invoking(() => data.Decode()).Should().Throw<PdfLimitExceededException>();
        FluentActions.Invoking(() => data.Decode()).Should().Throw<PdfLimitExceededException>();
        strict.Diagnostics.Should().BeEmpty();
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
        // Object stream 7 takes its first filter from object 10, which object stream 9 holds, and the parameters of its
        // second, a Flate step, from object 5, which 7 holds and the index does not list — its row is of a reserved type
        // —, in an index a row short of its /Size. Decoding 7 asks for 5, which rebuilds the index while 10 is not yet in
        // it: the rebuild reads 7 as far as its first filter, which names none yet, and places 5 in it. Reading 5 decodes
        // 7 again inside the first decoding, through /ASCIIHexDecode this time, into fewer bytes than its /First: neither
        // decoding gives anything to keep, and the second to end has nothing to take the place of.
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
        WriteObject(7, "<< /Type /ObjStm /N 2 /First 9 /Filter [10 0 R /FlateDecode] /DecodeParms 5 0 R /Length 47 >>\nstream\n5 0 6 19 << /Predictor 1 >>\n<< /Title (six) >>\n\nendstream");
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
        document.Reader.ObjectStreamBytes.Should().Be(21 + 16, "only object stream 9 is kept: its data, and its header's tables and lookup");
        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.ObjectStreamUnreadable, "the stream is reported once, however often it is decoded (#187)");
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
    public void A_rebuild_a_read_sets_off_reads_the_file_no_more_than_one_run_first()
    {
        // Object 6, which the catalog names, is held by object stream 7, and 2,000 objects are written directly in the file.
        // With 7's row outside the file, reading 6 rebuilds the index while 7 is being loaded; with an unreferenced row
        // outside the file instead, settling rebuilds it first. Expanded once for the loads in flight and again when the
        // rebuild was finished, the objects were read twice over; they are read once.
        var (underALoad, first) = (ObjectsAndAMember(brokenRow: 7), ObjectsAndAMember(brokenRow: 8));
        using var inFlightSource = new StrictCountingSource(underALoad);
        using var firstSource = new StrictCountingSource(first);
        using var inFlight = PdfDocument.Open(inFlightSource, options: null, ownsSource: false);
        using var settled = PdfDocument.Open(firstSource, options: null, ownsSource: false);
        var (inFlightOpened, firstOpened) = (inFlightSource.BytesRead, firstSource.BytesRead);

        inFlight.GetObject(new PdfObjectId(6)).Should().BeOfType<PdfString>();
        settled.Reader.SettleIndex();
        settled.GetObject(new PdfObjectId(6)).Should().BeOfType<PdfString>();

        inFlight.WasRepaired.Should().BeTrue();
        settled.WasRepaired.Should().BeTrue();
        (inFlightSource.BytesRead - inFlightOpened).Should().BeLessThan((firstSource.BytesRead - firstOpened) * 5 / 4);
    }

    [Fact]
    public void Enumerating_the_object_numbers_across_a_rebuild_a_read_sets_off_gives_the_numbers_it_began_with()
    {
        // Object 3's row lies 700 bytes before its header, inside object 4's string, so that reading 3 rebuilds the index
        // while the loop enumerates it. The enumeration threw at its next step; it gives the numbers the index held when it
        // began, and the next one those of the rebuilt index, which holds no free entry (#167).
        using var document = PdfDocument.Open(RebuiltOnReadingThree());
        var visited = new List<int>();

        foreach (var number in document.ObjectNumbers)
        {
            visited.Add(number);
            _ = document.GetObject(new PdfObjectId(number));
        }

        visited.Should().Equal(0, 1, 2, 3, 4);
        document.WasRepaired.Should().BeTrue();
        document.ObjectCount.Should().Be(4);
        document.ObjectNumbers.Should().Equal(1, 2, 4, 3);
    }

    [Fact]
    public void Enumerating_the_object_numbers_across_validations_gives_the_numbers_it_began_with()
    {
        // The same file, validated at each step: the first validation rebuilds the index before its first rule (ADR 50),
        // while the loop enumerates it, which threw after one validation (#167).
        using var document = PdfDocument.Open(RebuiltOnReadingThree());
        var visited = new List<int>();

        foreach (var number in document.ObjectNumbers)
        {
            visited.Add(number);
            _ = new PdfValidator().Validate(document);
        }

        visited.Should().Equal(0, 1, 2, 3, 4);
        document.WasRepaired.Should().BeTrue();
        document.ObjectNumbers.Should().Equal(1, 2, 4, 3);
    }

    [Theory]
    [InlineData("entries")]
    [InlineData("keys")]
    [InlineData("validations")]
    public void A_trailer_taken_before_a_rebuild_is_left_as_it_was_and_the_next_one_is_the_rebuilt_one(string loop)
    {
        // #237's shape: the newest trailer names /Bar 4 0 R and a /Prev nothing is at, and an older one gives /Foo and /Info.
        // Resolving /Bar rebuilds the index, and so does validating, which merges the older trailer: into the very dictionary
        // the loop enumerated, which threw at its next step. It merges into a new one, which the next call gives (#167).
        using var document = PdfDocument.Open(PdfTemplate.Build(ConvergenceShapes.TrailerPrevLastTemplate));
        var taken = document.Trailer;

        switch (loop)
        {
            case "entries":
                foreach (var (_, value) in document.Trailer)
                {
                    _ = value.Resolve();
                }

                break;

            case "keys":
                foreach (var key in document.Trailer.Keys)
                {
                    _ = document.Trailer[key]!.Resolve();
                }

                break;

            default:
                foreach (var (_, _) in document.Trailer)
                {
                    _ = new PdfValidator().Validate(document);
                }

                break;
        }

        document.WasRepaired.Should().BeTrue();
        taken.Keys.Select(key => key.Value).Should().Equal("Size", "Root", "Bar", "Prev");
        document.Trailer.Should().NotBeSameAs(taken);
        document.Trailer.Keys.Select(key => key.Value).Should().BeEquivalentTo(["Size", "Root", "Bar", "Prev", "Foo", "Info"]);
        document.Catalog.Should().BeSameAs(document.GetObject(new PdfObjectId(1)) as PdfDictionary);
    }

    [Fact]
    public void An_encryption_a_rebuild_merges_is_in_the_next_trailer_and_not_in_one_taken_before()
    {
        // #237's shape, the older trailer giving /Encrypt: the document opens as unencrypted, and the rebuild reading /Bar sets
        // off merges /Encrypt into the trailer the document gives next, which IsEncrypted reads (#154); the trailer taken
        // before keeps none (#167).
        var template = ConvergenceShapes.TrailerPrevLastTemplate.Replace(
            "/Info << /Title 5 >>", "/Encrypt << /Filter /Standard /V 1 /R 2 /O <00> /U <00> /P -4 >>", StringComparison.Ordinal);
        using var document = PdfDocument.Open(PdfTemplate.Build(template));
        var taken = document.Trailer;
        document.IsEncrypted.Should().BeFalse();

        _ = document.GetObject(new PdfObjectId(4));

        document.WasRepaired.Should().BeTrue();
        taken.ContainsKey(PdfName.Encrypt).Should().BeFalse();
        document.Trailer.ContainsKey(PdfName.Encrypt).Should().BeTrue();
        document.IsEncrypted.Should().BeTrue();
    }

    [Fact]
    public void Enumerating_the_diagnostics_across_a_read_that_rebuilds_the_index_gives_the_entries_it_began_with()
    {
        // #237's shape opens with one entry, for the section /Prev names, which is missing; reading object 4 rebuilds the
        // index, which records what it did while the loop enumerates the first entry. The enumeration threw at its next
        // step; it gives the entry it began with, and the record holds both (#167).
        using var document = PdfDocument.Open(PdfTemplate.Build(ConvergenceShapes.TrailerPrevLastTemplate));
        var enumerated = new List<PdfDiagnostic>();

        foreach (var diagnostic in document.Diagnostics)
        {
            enumerated.Add(diagnostic);
            _ = document.GetObject(new PdfObjectId(4));
        }

        enumerated.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionMissing);
        document.Diagnostics.Should().HaveCount(2).And.StartWith(enumerated);
        document.Diagnostics.Contains(PdfDiagnosticCodes.XRefRebuilt).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_walk_of_the_object_streams_dependencies_reads_an_object_many_streams_need_once(bool generations)
    {
        // Four hundred object streams take their /DecodeParms from object 3, written directly in the file and 100 KB long —
        // as 3 0 R, or each under a generation of its own, which leads the reader to the same object. The walk reads what
        // each stream needs where the chain's index places it, outside the reader's cache: it read object 3 again for every
        // stream, 93 MB in all. It reads it once a validation, and each stream's dictionary through one window (#128).
        const int Streams = 400;
        var streams = new List<PackedStream>();
        for (var k = 0; k < Streams; k++)
        {
            var parameters = string.Create(CultureInfo.InvariantCulture, $"/DecodeParms 3 {(generations ? k : 0)} R");
            streams.Add(new PackedStream(10 + (2 * k), parameters, [(11 + (2 * k), "<< >>")], PackedData.Plain));
        }

        var file = PackedFile(streams, [(3, "<< /Pad [" + new StringBuilder().Insert(0, "0 ", 50_000) + "] >>")]);
        using var source = new StrictCountingSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var opened = source.BytesRead;

        ObjectStreamDependencies.Run(document).Circular.Should().BeEmpty();

        (source.BytesRead - opened).Should().BeLessThan((Streams * (long)PdfFileReader.InitialObjectWindow) + (4L * file.Length));
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
        // The section is probed through a few bytes, which spaces and "xref" fill, but the token is "xrefs". The probe reads
        // on past a token its edge reaches, finds no keyword there, nor an object, and the index is rebuilt (#174). Where
        // MaxTrailerLength stops the probe right after "xref", the table's own window reads the token whole and refuses it
        // under another rule (#295).
        var written = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .BuildClassic(rootNumber: 1);
        var text = Encoding.Latin1.GetString(written);
        var bytes = Encoding.Latin1.GetBytes(text.Replace("\nxref\n0 3", "\n" + new string(' ', PdfFileReader.XRefProbeLength - "xref".Length) + "xrefs\n0 3", StringComparison.Ordinal));

        using var document = PdfDocument.Open(bytes);

        document.WasRepaired.Should().BeTrue();
        document.Reader.Structure.Sections[0].State.Should().Be(XRefSectionState.NotFound);
        document.Reader.Structure.Sections[0].Fault.Should().Be("holds neither the xref keyword nor an object");
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    [Fact]
    public void Settling_the_index_reads_of_each_entry_no_more_than_a_load_reads_before_its_body()
    {
        // Two thousand objects, each three bytes past where its row places it. Settling the index, as the validator does
        // before its first rule, reads at each row the first bytes of a header and the 512 bytes either side, as a load
        // does before it relocates the object, then the header found: no more, and nothing is rebuilt (#237).
        const int Count = 2000;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };

        for (var number = 3; number < Count + 3; number++)
        {
            rows[number] = file.Object(number, "null") + 3;
        }

        file.Table(Count + 3, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {Count + 3} /Root 1 0 R"));
        using var source = new StrictCountingSource(file.Bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var opened = source.BytesRead;

        document.Reader.SettleIndex();

        document.WasRepaired.Should().BeFalse();
        (source.BytesRead - opened).Should().BeLessThanOrEqualTo((Count + 2) * (64L + 1024 + 64));

        // Settled once, the index is not looked at again.
        var settled = source.BytesRead;
        document.Reader.SettleIndex();
        source.BytesRead.Should().Be(settled);
    }

    [Theory]
    [InlineData("a comment line")]
    [InlineData("blanks")]
    public void Settling_the_index_reads_a_header_after_a_long_lead_in_no_larger_window_than_a_load_reads(string lead)
    {
        // Two hundred objects, each after a comment line or a run of blanks of 5,000 bytes, which its row names. Settling
        // reads the first 64 bytes at each row, then windows eight times larger up to the one a load starts with, which holds
        // the header: no larger window, and no more than a load reads to reach the header, the small windows aside. Blanks
        // were crossed in windows that passed the 8 KB a load starts with, 36 KB a row (M3-01). What each row consumes, its
        // lead, is the file's length in all, within settling's allowance.
        const int Count = 200;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };

        for (var number = 3; number < Count + 3; number++)
        {
            rows[number] = file.Position;
            file.Raw(lead == "blanks" ? new string(' ', 5000) : "%" + new string('c', 4999) + "\n");
            file.Object(number, "null");
        }

        file.Table(Count + 3, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {Count + 3} /Root 1 0 R"));
        using var source = new StrictCountingSource(file.Bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var opened = source.BytesRead;

        document.Reader.SettleIndex();

        document.WasRepaired.Should().BeFalse();
        (source.BytesRead - opened).Should().BeLessThanOrEqualTo((Count + 2) * (64L + 512 + 4096 + PdfFileReader.InitialObjectWindow));
    }

    [Fact]
    public void Settling_rows_whose_header_ends_where_a_run_crossed_before_starts_reads_no_larger_window_than_a_load_reads()
    {
        // N3-02: a hundred rows, written highest number first, each 13,000 blanks or more before its header, which ends where
        // the lead of the row settled before it starts. Each crossing stopped at that remembered run, with the header at the
        // window's edge, and the next window grew from the crossing's, 512 KB, where a load reads 8 KB, then 64 KB: settling
        // read seven times what loads of the rows read. The window after a crossing now grows from what it lexes.
        const int Count = 100;
        const int Lead = 13_000;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };

        for (var number = Count + 2; number >= 3; number--)
        {
            rows[number] = file.Position;
            file.Raw(new string(' ', Lead + number - 3) + string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj"));
        }

        file.Raw("\nnull\nendobj\n");
        file.Table(Count + 3, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {Count + 3} /Root 1 0 R"));
        var bytes = file.Bytes;
        long loads = 0;

        for (var number = 3; number < Count + 3; number++)
        {
            using var alone = new StrictCountingSource(bytes);
            using var opened = PdfDocument.Open(alone, options: null, ownsSource: false);
            var before = alone.BytesRead;
            opened.GetObject(new PdfObjectId(number));
            loads += alone.BytesRead - before;
            opened.WasRepaired.Should().BeFalse();
        }

        using var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var read = source.BytesRead;

        document.Reader.SettleIndex();

        document.WasRepaired.Should().BeFalse();
        (source.BytesRead - read).Should().BeLessThanOrEqualTo(loads + ((Count + 2) * (64L + 512 + 4096)));
    }

    [Theory]
    [InlineData(5_000, 70_000)]
    [InlineData(12_865, 70_000)]
    [InlineData(12_764, 50_000)]
    [InlineData(406_180, 65_430)]
    [InlineData(438_780, 50_000)]
    [InlineData(450_000, 70_000)]
    [InlineData(45_633, 70_000)]
    [InlineData(50_000, 70_000)]
    [InlineData(11_000, 20_000)]
    [InlineData(111_281, 70_000)]
    public void Settling_rows_whose_long_header_follows_a_lead_reads_no_more_than_their_loads(int lead, int zeros)
    {
        // P3-01, Q2-01, Q2-02: rows, each a lead of blanks then a header of zeros and "n 0 obj". The crossing's last window
        // lexed all but a byte of a step, and the next, grown from the step under it, was read again to gain that byte; grown
        // once more, it was as large as a load's window and ended past it, by the lead; and after a long lead, a window as
        // large as a load's read past the load's. Each window after a crossing grows to twice what it lexed at least, and
        // ends no further than the window a load reads the header in, from the row; a window between, which a load does not
        // read, read 24 KB more than a load did (R1-02). Each row reads no more than its load and the first small windows; the
        // catalog and its pages, 64 bytes each.
        const int Count = 4;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };

        for (var number = 3; number < Count + 3; number++)
        {
            rows[number] = file.Position;
            file.Raw(new string('\0', lead) + new string('0', zeros) + string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj null endobj\n"));
        }

        file.Table(Count + 3, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {Count + 3} /Root 1 0 R"));
        var bytes = file.Bytes;
        long loads = 0;

        for (var number = 3; number < Count + 3; number++)
        {
            using var alone = new StrictCountingSource(bytes);
            using var opened = PdfDocument.Open(alone, options: null, ownsSource: false);
            var before = alone.BytesRead;
            opened.GetObject(new PdfObjectId(number)).Should().Be(PdfNull.Instance);
            loads += alone.BytesRead - before;
            opened.WasRepaired.Should().BeFalse();
        }

        using var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var read = source.BytesRead;

        document.Reader.SettleIndex();

        document.WasRepaired.Should().BeFalse();
        (source.BytesRead - read).Should().BeLessThanOrEqualTo(loads + (Count * (64L + 512 + 4096)) + (2 * 64));
    }

    [Theory]
    [InlineData(60_000, 8_734)]
    [InlineData(64_000, 8_200)]
    [InlineData(60_000, 12_900)]
    [InlineData(20_000, 8_734)]
    public void Settling_a_row_whose_crossing_a_remembered_run_stops_past_a_comment_reads_no_more_than_its_load(int run, int comment)
    {
        // S1-01: object 4's row leads into blanks, a comment past a load's first window, and blanks again up to a run object
        // 3's row crossed first, 100 bytes before object 4's header. The crossing stopped at the run, past the comment it
        // then lexed from, and the next window read it all again from the comment, 43 KB more than a load of the row read.
        // The lexing reads on from where its window stopped, past the run.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var lead = file.Position;
        file.Raw(new string('\0', comment) + "%" + new string('c', 998) + "\n" + new string('\0', run + 100 - comment - 1000));
        file.Object(4, "(four)");
        file.Object(3, "(three)");
        rows[3] = lead + run;
        rows[4] = lead;
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        var bytes = file.Bytes;
        long loads = 0;

        foreach (var number in new[] { 3, 4 })
        {
            using var alone = new StrictCountingSource(bytes);
            using var opened = PdfDocument.Open(alone, options: null, ownsSource: false);
            var before = alone.BytesRead;
            opened.GetObject(new PdfObjectId(number)).Should().BeOfType<PdfString>();
            loads += alone.BytesRead - before;
        }

        using var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var read = source.BytesRead;

        document.Reader.SettleIndex();

        document.WasRepaired.Should().BeFalse();
        (source.BytesRead - read).Should().BeLessThanOrEqualTo(loads + (2 * 64));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1_000)]
    public void Placing_a_row_whose_comment_ends_in_a_run_crossed_inside_it_reads_the_comment_to_its_line_end(int inside)
    {
        // Object 4's row leads into a comment of blanks whose line ends 2,000 bytes on, then into 70,000 blanks before its
        // header. Object 5's row, inside the comment, crosses its blanks, the line end and the blanks past it, which it
        // remembers as a run: inside the comment, the run is the comment's up to its line end, and object 4's row reads it,
        // as a load does, rather than reading on past it still inside the comment, which would swallow the header. A row
        // close to the comment's start has object 4's second window end inside the run, still in the comment (T2-4).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var lead = file.Position;
        file.Raw("%c" + new string('\0', 2_000) + "\n" + new string('\0', 70_000));
        file.Object(4, "(four)");
        rows[4] = lead;
        rows[5] = lead + inside;
        file.Table(6, rows, "/Size 6 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);
        var reading = new HeaderReading(long.MaxValue);
        document.Reader.ChainIndex!.TryGet(5, out var insideEntry).Should().BeTrue();
        document.Reader.ChainIndex.TryGet(4, out var commented).Should().BeTrue();

        document.Reader.PlaceHeader(5, insideEntry, reading).Place.Should().Be(HeaderPlace.NotFound);
        var placement = document.Reader.PlaceHeader(4, commented, reading);

        placement.Place.Should().Be(HeaderPlace.AtOffset);
        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>();
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_pass_that_spent_its_allowance_decides_a_row_skipped_into_a_run_in_its_first_window(bool crossedFirst)
    {
        // W2-3: object 3's row crosses 110 blanks, which the pass remembers; object 7's header lies past them, 5 bytes past
        // its row. With nothing left of the allowance, the row skipped to the run's end and read a window running past its
        // first 64 bytes, and was left undecided, though they decide it; read without the crossing before, it was decided.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var lead = file.Position;
        file.Raw(new string(' ', 110));
        file.Object(7, "null");
        rows[3] = lead;
        rows[7] = lead + 105;
        file.Table(8, rows, "/Size 8 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);
        var reading = new HeaderReading(1_000);
        document.Reader.ChainIndex!.TryGet(3, out var three).Should().BeTrue();
        document.Reader.ChainIndex.TryGet(7, out var seven).Should().BeTrue();

        if (crossedFirst)
        {
            document.Reader.PlaceHeader(3, three, reading).Place.Should().Be(HeaderPlace.NotFound);
        }

        reading.Consume(long.MaxValue);
        var placement = document.Reader.PlaceHeader(7, seven, reading);

        placement.Place.Should().Be(HeaderPlace.AtOffset);
        placement.Position.Should().Be(lead + 110);
    }

    [Fact]
    public void A_pass_with_less_left_than_a_window_of_blanks_holds_leaves_its_row_undecided()
    {
        // Object 3's row lies 2,000 blanks before its header. Past the first window, read free of the pass's allowance, the
        // next is blanks whole, 512 bytes, and the pass has 100 left: crossing it spends the pass, which reads no further,
        // and the entry is left undecided, where a pass with room enough finds the header past the blanks. What it did not
        // pay for, it does not remember crossing: no later reading skips those blanks free.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var lead = file.Position;
        file.Raw(new string(' ', 2_000));
        file.Object(3, "null");
        rows[3] = lead;
        file.Table(4, rows, "/Size 4 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);
        document.Reader.ChainIndex!.TryGet(3, out var entry).Should().BeTrue();
        var reading = new HeaderReading(100);

        var placement = document.Reader.PlaceHeader(3, entry, reading);
        var whole = document.Reader.PlaceHeader(3, entry, new HeaderReading(long.MaxValue));

        placement.Place.Should().Be(HeaderPlace.Undecided);
        reading.Remaining.Should().Be(0);
        reading.PastBlanks(lead).Should().Be(lead + 64);
        whole.Place.Should().Be(HeaderPlace.AtOffset);
        whole.Position.Should().Be(lead + 2_000);
    }

    [Theory]
    [InlineData(0, 64)]
    [InlineData(65_471, 74_304)]
    [InlineData(65_472, 74_304)]
    public void A_pass_that_spends_its_allowance_on_a_cut_window_reads_it_no_second_time(long allowance, long most)
    {
        // X2-1: object 3's row starts "3 0 (" and a string of 600,000 bytes, which each window cuts. A window charged exactly
        // what the pass had left, or a first window cut with nothing left, was read again from the string's start up to the
        // same edge, past the row's first 64 bytes, to leave the entry undecided all the same: up to a load's window twice.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        rows[3] = file.Position;
        file.Raw("3 0 (" + new string('a', 600_000) + ")\n");
        file.Table(4, rows, "/Size 4 /Root 1 0 R");
        using var source = new StrictCountingSource(file.Bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        document.Reader.ChainIndex!.TryGet(3, out var entry).Should().BeTrue();
        var before = source.BytesRead;

        var placement = document.Reader.PlaceHeader(3, entry, new HeaderReading(allowance));

        placement.Place.Should().Be(HeaderPlace.Undecided);
        (source.BytesRead - before).Should().BeLessThanOrEqualTo(most);
    }

    [Theory]
    [InlineData("(abc)")]
    [InlineData("<616263>")]
    public void A_spent_pass_decides_a_row_whose_string_closes_where_a_run_crossed_starts(string closed)
    {
        // Y2-1: object 7's row starts a string closed on the byte before 100 blanks, which object 3's row, past the string,
        // crosses and remembers as a run; object 7's header lies past them. A string closed there is whole: the run that
        // follows it is no part of it. With nothing left of the allowance, the row was left undecided, the string taken
        // for one the run's start cut; it is decided as a load decides, its header found near it.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var lead = file.Position;
        file.Raw(closed + new string(' ', 100));
        file.Object(7, "null");
        rows[3] = lead + closed.Length;
        rows[7] = lead;
        file.Table(8, rows, "/Size 8 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);
        document.Reader.ChainIndex!.TryGet(3, out var three).Should().BeTrue();
        document.Reader.ChainIndex.TryGet(7, out var seven).Should().BeTrue();
        var load = document.Reader.PlaceHeader(7, seven, new HeaderReading(long.MaxValue));
        var reading = new HeaderReading(long.MaxValue);
        document.Reader.PlaceHeader(3, three, reading).Place.Should().Be(HeaderPlace.NotFound);
        reading.Consume(long.MaxValue);

        var placement = document.Reader.PlaceHeader(7, seven, reading);

        load.Place.Should().Be(HeaderPlace.Nearby);
        placement.Should().Be(load);
    }

    [Fact]
    public void A_crossing_stopped_where_a_run_starts_reads_no_string_closed_there_again()
    {
        // Y2-2: object 6's row leads into 9,000 blanks, then a string of 56 KB closed on the byte before a run of 100
        // blanks, which object 5's row, placed first, crosses and remembers. Object 6's crossing stops where the run
        // starts: the string closed there is whole, and was read again from its start, up to the rest of a load's window.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var at = file.Position;
        file.Raw(new string(' ', 9_000) + "(" + new string('a', 55_998) + ")" + new string(' ', 100) + "endobj\n");
        file.Object(4, "(four)");
        rows[5] = at + 65_000;
        rows[6] = at;
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        using var source = new StrictCountingSource(file.Bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        document.Reader.ChainIndex!.TryGet(5, out var five).Should().BeTrue();
        document.Reader.ChainIndex.TryGet(6, out var six).Should().BeTrue();
        var start = source.BytesRead;
        var alone = document.Reader.PlaceHeader(6, six, new HeaderReading(long.MaxValue));
        var aloneRead = source.BytesRead - start;
        var reading = new HeaderReading(long.MaxValue);
        document.Reader.PlaceHeader(5, five, reading);
        start = source.BytesRead;

        var shared = document.Reader.PlaceHeader(6, six, reading);

        shared.Place.Should().Be(alone.Place);
        (source.BytesRead - start).Should().BeLessThanOrEqualTo(aloneRead);
    }

    [Fact]
    public void A_crossing_stopped_where_a_run_starts_inside_a_string_reads_the_string_again_once_at_most()
    {
        // X2-2: object 6's row leads into 8,200 blanks, then a string holding 200 null bytes, inside which object 5's row
        // lies; placed first, it crosses them and remembers them as a run. Object 6's crossing stops where the run starts,
        // finds the string there and reads it on from its start, as a string runs through white space: one crossing window
        // more than the row alone reads, 64 KB at most, and the same decision.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var at = file.Position;
        file.Raw(new string(' ', 8_200) + "(" + new string('a', 51_800) + new string('\0', 200) + new string('a', 400_000) + ") ");
        file.Object(4, "(four)");
        rows[5] = at + 8_200 + 1 + 51_800 + 50;
        rows[6] = at;
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        using var source = new StrictCountingSource(file.Bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        document.Reader.ChainIndex!.TryGet(5, out var five).Should().BeTrue();
        document.Reader.ChainIndex.TryGet(6, out var six).Should().BeTrue();
        var start = source.BytesRead;
        var alone = document.Reader.PlaceHeader(6, six, new HeaderReading(long.MaxValue));
        var aloneRead = source.BytesRead - start;
        var reading = new HeaderReading(long.MaxValue);
        document.Reader.PlaceHeader(5, five, reading);
        start = source.BytesRead;

        var shared = document.Reader.PlaceHeader(6, six, reading);

        shared.Place.Should().Be(alone.Place);
        (source.BytesRead - start).Should().BeLessThanOrEqualTo(aloneRead + (64 * 1024));
    }

    [Theory]
    [InlineData(8_192)]
    [InlineData(65_536)]
    public void A_pass_with_a_few_bytes_left_decides_a_row_skipped_to_a_run_s_end_a_byte_short_of_a_load_s_window(int window)
    {
        // Object 3's row crosses blanks the pass remembers. Object 4's row lies inside them, their end on the last byte of a
        // window a load reads from the row, where object 4's header starts, running on a hundred bytes. Past the run, the
        // row's window ends where the load's does: it reads the header's first byte alone, charged one byte, then reads the
        // header again from its start, and decides it. A window running past the load's read 64 bytes at once, charged
        // them, more than the ten left, and left the row undecided (mutant R-cross-cap-past).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var lead = file.Position;
        var blanks = window + 2_000;
        file.Raw(new string(' ', blanks) + "4" + new string(' ', 100) + "0 obj\n(four)\nendobj\n");
        rows[3] = lead;
        rows[4] = lead + blanks - (window - 1);
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);
        var reading = new HeaderReading(long.MaxValue);
        document.Reader.ChainIndex!.TryGet(3, out var three).Should().BeTrue();
        document.Reader.ChainIndex.TryGet(4, out var four).Should().BeTrue();
        document.Reader.PlaceHeader(3, three, reading).Place.Should().Be(HeaderPlace.NotFound);

        reading.Consume(reading.Remaining - 10).Should().BeTrue();
        var placement = document.Reader.PlaceHeader(4, four, reading);

        placement.Place.Should().Be(HeaderPlace.AtOffset);
        placement.Position.Should().Be(lead + blanks);
    }

    [Theory]
    [InlineData(16_400)]
    [InlineData(60_000)]
    public void A_pass_with_a_few_bytes_left_at_the_edge_of_a_load_s_first_window_decides_a_header_the_rest_of_the_next_holds(int blanks)
    {
        // Object 4's header is "4", blanks past the 8 KB a load reads first, then "0 obj", which a load reads in its next
        // window, 64 KB from the row. The row's windows reach the load's first edge with 100 bytes of the pass left, the first
        // 64 bytes free: past the window a load starts with, the next is the rest of the load's, read at once, and decides the
        // row, charged however little is left. A window of 8 KB between, which a load does not read, ended in the blanks, and
        // the row was left undecided for want of 8 KB (mutant R-snap-gt).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var at = file.Position;
        file.Raw("4" + new string(' ', blanks) + "0 obj\n(four)\nendobj\n");
        rows[4] = at;
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);
        var reading = new HeaderReading(PdfFileReader.InitialObjectWindow - 64 + 100);
        document.Reader.ChainIndex!.TryGet(4, out var four).Should().BeTrue();

        var placement = document.Reader.PlaceHeader(4, four, reading);

        placement.Place.Should().Be(HeaderPlace.AtOffset);
        placement.Position.Should().Be(at);
        reading.Remaining.Should().Be(0, "what the row crossed up to its decision is charged, however little was left");
        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>();
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_pass_that_spent_its_allowance_decides_a_header_its_first_window_ends_on_where_a_run_crossed_starts(bool crossedFirst)
    {
        // Object 7's row leads into 57 blanks, then "7 0 obj", whose obj ends where the row's first 64 bytes do; object 3's
        // row, right past it, crosses 110 blanks, which the pass remembers. A token the window's edge reaches may go on past
        // it, but not where white space a reading crossed lies: with nothing left of the allowance, the first window decides
        // the row. Read without the crossing before, obj may be the start of "objx", and the row is left undecided, the
        // token not read again (mutant R-edge-blank-off).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var lead = file.Position;
        file.Raw(new string(' ', 57) + "7 0 obj" + new string(' ', 110) + "null\nendobj\n");
        rows[3] = lead + 64;
        rows[7] = lead;
        file.Table(8, rows, "/Size 8 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);
        var reading = new HeaderReading(1_000);
        document.Reader.ChainIndex!.TryGet(3, out var three).Should().BeTrue();
        document.Reader.ChainIndex.TryGet(7, out var seven).Should().BeTrue();

        if (crossedFirst)
        {
            document.Reader.PlaceHeader(3, three, reading).Place.Should().Be(HeaderPlace.NotFound);
        }

        reading.Consume(long.MaxValue);

        document.Reader.PlaceHeader(7, seven, reading).Place.Should().Be(crossedFirst ? HeaderPlace.AtOffset : HeaderPlace.Undecided);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Placing_a_row_whose_token_ends_on_a_load_s_largest_window_reaches_the_guard_whatever_lies_past(bool crossedFirst)
    {
        // Object 3's row starts a keyword as long as the object guard, which ends where a load's largest window from the row
        // does; past it lie 200 blanks, which object 4's row, at their start, crosses and remembers as a run. At a load's
        // largest window, a load reaches the guard on a token its edge reaches, whatever follows: the row is past the guard,
        // crossed first or not. Ending the keyword at the run, as a smaller window's edge does, took it whole, and said the
        // row holds no header (mutant R-edge-blank-atreach).
        const int Guard = 4_096;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var lead = file.Position;
        file.Raw(new string('x', Guard) + new string(' ', 200));
        file.Object(4, "(four)");
        rows[3] = lead;
        rows[4] = lead + Guard;
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = Guard } };
        using var document = PdfDocument.Open(file.Bytes, options);
        var reading = new HeaderReading(long.MaxValue);
        document.Reader.ChainIndex!.TryGet(3, out var three).Should().BeTrue();
        document.Reader.ChainIndex.TryGet(4, out var four).Should().BeTrue();

        if (crossedFirst)
        {
            document.Reader.PlaceHeader(4, four, reading).Place.Should().Be(HeaderPlace.AtOffset);
        }

        var placement = document.Reader.PlaceHeader(3, three, reading);

        placement.Place.Should().Be(HeaderPlace.PastGuard);
        placement.GuardAtOffset.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(50_000)]
    public void Placing_a_row_whose_first_token_is_a_string_through_a_run_crossed_reads_the_string_whole(int? maxObjectLength)
    {
        // Object 4's row starts a string of 70,000 blanks, which object 5's row, inside it, crosses and remembers as a run. A
        // string runs on through white space: object 4's row reads it again from its start, past the run, to its end, as a
        // load does, rather than taking the string for one the run ends. No string begins a header; but one that runs past
        // the object guard is one a load reaches the guard on, and so does the row (T2-5).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var lead = file.Position;
        file.Raw("(" + new string('\0', 70_000) + ") ");
        file.Object(4, "(four)");
        rows[4] = lead;
        rows[5] = lead + 10;
        file.Table(6, rows, "/Size 6 /Root 1 0 R");
        var limits = maxObjectLength is { } bound ? PdfReaderLimits.Default with { MaxObjectLength = bound } : PdfReaderLimits.Default;
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = limits });
        var reading = new HeaderReading(long.MaxValue);
        document.Reader.ChainIndex!.TryGet(5, out var inside).Should().BeTrue();
        document.Reader.ChainIndex.TryGet(4, out var stringed).Should().BeTrue();
        var place = maxObjectLength is null ? HeaderPlace.NotFound : HeaderPlace.PastGuard;

        document.Reader.PlaceHeader(5, inside, reading).Place.Should().Be(place);

        document.Reader.PlaceHeader(4, stringed, reading).Place.Should().Be(place);
    }

    [Fact]
    public void Probing_rows_that_skip_a_run_into_a_comment_reads_a_few_times_what_they_consume()
    {
        // T2-1: two thousand rows place their objects at 70,000 blanks before a comment of 4,700 bytes, then "x" and 450 KB
        // of filler. Past the run the first row crossed, each row read the rest of a load's window from the row, 450 KB,
        // to the "x" that decides it, and was charged up to it: the probe read 180 times the file, and a file of 16 MB
        // twice its length past a run of 4 MB was read 4,500 times. Past a run it skips, a row consumes every window it
        // reads whole.
        const int Rows = 2_000;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var at = file.Position;
        file.Raw(new string('\0', 70_000) + "%" + new string('c', 4_699) + "\nx " + new string('z', 450_000) + "\n");

        for (var number = 3; number < Rows + 3; number++)
        {
            rows[number] = at;
        }

        file.Table(Rows + 3, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {Rows + 3} /Root 1 0 R"));
        var bytes = file.Bytes;
        using var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        document.Reader.SettleIndex();
        var settled = source.BytesRead;

        _ = CrossReferenceProbe.Run(document);

        (source.BytesRead - settled).Should().BeLessThan(10L * bytes.Length);
    }

    [Theory]
    [InlineData(100, 600_000)]
    [InlineData(100, 65_536)]
    [InlineData(12_000, 450_000)]
    public void Placing_a_row_past_a_run_it_skips_reads_a_long_token_as_a_load_reads_it(int run, int token)
    {
        // U2-1, U2-3: object 4's row leads into a run of blanks object 3's row crossed first, then a name of hundreds of KB.
        // Past the run, its windows were sized on a second grid, from the run's end, or kept within eight times what was
        // read since: each stopped short of a load's window, and the name was read again to its end, up to 600 KB more than
        // the row reads alone. They keep a load's windows from the row, as the row alone does.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var at = file.Position;
        file.Raw(new string('\0', run) + "/" + new string('a', token - 1) + " x " + new string('z', 600_000) + "\n");
        rows[3] = at;
        rows[4] = at;
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        var bytes = file.Bytes;

        using var aloneSource = new StrictCountingSource(bytes);
        using var aloneDocument = PdfDocument.Open(aloneSource, options: null, ownsSource: false);
        aloneDocument.Reader.ChainIndex!.TryGet(4, out var four).Should().BeTrue();
        var before = aloneSource.BytesRead;
        aloneDocument.Reader.PlaceHeader(4, four, new HeaderReading(long.MaxValue)).Place.Should().Be(HeaderPlace.NotFound);
        var alone = aloneSource.BytesRead - before;

        using var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var reading = new HeaderReading(long.MaxValue);
        document.Reader.ChainIndex!.TryGet(3, out var three).Should().BeTrue();
        document.Reader.PlaceHeader(3, three, reading);
        before = source.BytesRead;

        document.Reader.PlaceHeader(4, four, reading).Place.Should().Be(HeaderPlace.NotFound);

        (source.BytesRead - before).Should().BeLessThanOrEqualTo(alone + 64 + 512 + 4_096);
    }

    [Fact]
    public void Probing_rows_past_a_run_into_a_token_cut_where_a_load_window_ends_reads_a_few_times_the_file()
    {
        // U2-2: a thousand rows place their objects at 4,095 blanks before a name of 4,097 bytes, " x " and 70 KB of filler.
        // Past the run, each row read the name again, then the rest of a load's window, 70 KB, and consumed only the name:
        // the probe read 34 times the file. Past a run it skips, a row consumes what it reads.
        const int Rows = 1_000;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var at = file.Position;
        file.Raw(new string('\0', 4_095) + "/" + new string('a', 4_096) + " x " + new string('z', 70_000) + "\n");

        for (var number = 3; number < Rows + 3; number++)
        {
            rows[number] = at;
        }

        file.Table(Rows + 3, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {Rows + 3} /Root 1 0 R"));
        var bytes = file.Bytes;
        using var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        document.Reader.SettleIndex();
        var settled = source.BytesRead;

        _ = CrossReferenceProbe.Run(document);

        (source.BytesRead - settled).Should().BeLessThan(10L * bytes.Length);
    }

    [Fact]
    public void Placing_a_row_whose_string_holds_a_run_crossed_reads_no_more_than_the_row_alone()
    {
        // T2-2: object 6's row leads into 70,000 blanks, then a string of 400 KB holding a run of blanks object 5's row
        // crossed first. The window that lexed the string stopped where the run starts, and the next read the string again
        // from its start: 400 KB more than the row read alone. A window that lexes reads on through a run.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var at = file.Position;
        file.Raw("1" + new string(' ', 69_999) + "(" + new string('a', 430_000) + new string('\0', 200) + new string('a', 300_000) + ") ");
        rows[6] = at;
        rows[5] = at + 70_000 + 1 + 430_000;
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        var bytes = file.Bytes;

        using var aloneSource = new StrictCountingSource(bytes);
        using var aloneDocument = PdfDocument.Open(aloneSource, options: null, ownsSource: false);
        aloneDocument.Reader.ChainIndex!.TryGet(6, out var six).Should().BeTrue();
        var before = aloneSource.BytesRead;
        aloneDocument.Reader.PlaceHeader(6, six, new HeaderReading(long.MaxValue)).Place.Should().Be(HeaderPlace.NotFound);
        var alone = aloneSource.BytesRead - before;

        using var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var reading = new HeaderReading(long.MaxValue);
        document.Reader.ChainIndex!.TryGet(5, out var five).Should().BeTrue();
        document.Reader.PlaceHeader(5, five, reading);
        before = source.BytesRead;

        document.Reader.PlaceHeader(6, six, reading).Place.Should().Be(HeaderPlace.NotFound);

        (source.BytesRead - before).Should().BeLessThanOrEqualTo(alone);
    }

    [Theory]
    [InlineData(8_192, "")]
    [InlineData(65_536, "")]
    [InlineData(65_536, "x")]
    public void Placing_a_row_whose_header_of_another_object_ends_a_load_s_window_reads_no_further(int edge, string after)
    {
        // T2-3: object 8's row holds "6 0 obj", its obj ending where a load's window does. A load takes the header as it is,
        // and, of another object, reads no further there; the row read it again in the rest of the next window, 458 KB past
        // what the load read. It decides as the load, object 6's header at the row, even an "objx" past the window.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var at = file.Position;
        file.Raw("6 0" + new string(' ', edge - 6) + "obj" + after + "\n(eight)\nendobj\n%" + new string('c', 600_000) + "\n");
        rows[8] = at;
        file.Table(9, rows, "/Size 9 /Root 1 0 R");
        using var source = new StrictCountingSource(file.Bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        document.Reader.ChainIndex!.TryGet(8, out var entry).Should().BeTrue();
        var before = source.BytesRead;

        var placement = document.Reader.PlaceHeader(8, entry, new HeaderReading(long.MaxValue));

        placement.Place.Should().Be(HeaderPlace.NotFound);
        placement.NumberAtOffset.Should().Be(6);
        (source.BytesRead - before).Should().BeLessThanOrEqualTo(edge + (2 * PdfFileReader.NearbySearchRadius));
    }

    [Theory]
    [InlineData(8_192, "", true)]
    [InlineData(8_192, "x", false)]
    [InlineData(65_536, "x", false)]
    public void Placing_a_row_whose_own_header_ends_a_load_s_window_reads_on_past_it_as_a_load_does(int edge, string after, bool placed)
    {
        // R-loadedge-any-number: object 6's row holds "6 0 obj", its obj ending where a load's window does. Only a header of
        // another object is taken as it is there: a load of object 6 finds its own number, reads on in a larger window, and
        // takes no "objx" for a header. The row decides as the load: object 6 at the row where the keyword ends, no header
        // where it runs on.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var at = file.Position;
        file.Raw("6 0" + new string(' ', edge - 6) + "obj" + after + "\n(six)\nendobj\n");
        rows[6] = at;
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);
        document.Reader.ChainIndex!.TryGet(6, out var entry).Should().BeTrue();

        var placement = document.Reader.PlaceHeader(6, entry, new HeaderReading(long.MaxValue));

        placement.Place.Should().Be(placed ? HeaderPlace.AtOffset : HeaderPlace.NotFound);
        (document.Reader.ReadWhereChainPlaces(6, entry) is not null).Should().Be(placed);
    }

    [Theory]
    [InlineData(8_192, "")]
    [InlineData(8_192, "x")]
    [InlineData(65_536, "x")]
    public void Placing_a_row_whose_blanks_lead_to_a_header_of_another_object_ending_a_load_s_window_reads_no_further(int edge, string after)
    {
        // R-cross-cap-off: object 8's row holds blanks, then "6 0 obj", its obj ending where a load's window does. A load
        // crosses the blanks in that window, takes the header as it is and reads no further there; the windows that cross the
        // blanks grow eightfold, and one that ran past the load's edge read more than the load, and an "objx" there as no
        // header. Each ends where the load's does: the row decides as the load, object 6's header, from what the load reads.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var at = file.Position;
        file.Raw(new string(' ', edge - 7) + "6 0 obj" + after + "\n(eight)\nendobj\n%" + new string('c', 100_000) + "\n");
        rows[8] = at;
        file.Table(9, rows, "/Size 9 /Root 1 0 R");
        using var source = new StrictCountingSource(file.Bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        document.Reader.ChainIndex!.TryGet(8, out var entry).Should().BeTrue();
        var before = source.BytesRead;

        var placement = document.Reader.PlaceHeader(8, entry, new HeaderReading(long.MaxValue));

        placement.Place.Should().Be(HeaderPlace.NotFound);
        placement.NumberAtOffset.Should().Be(6);
        (source.BytesRead - before).Should().BeLessThanOrEqualTo(edge + (2 * PdfFileReader.NearbySearchRadius));
    }

    [Theory]
    [InlineData(64)]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(300)]
    [InlineData(400)]
    [InlineData(500)]
    public void Placing_a_row_whose_header_runs_past_its_lead_s_window_reads_again_at_most_half_of_each_window_short_of_a_load_s(int lead)
    {
        // R-reread-2x-off: object 3's row holds a lead of blanks, crossed in its first 64 bytes and the next 512, then a
        // number written with a thousand leading zeros, which the edge of those 512 bytes cuts. Each window after reads it
        // again from its start, and grows from the last to twice what it reads again at least, while it is one of the small
        // ones and ends short of the window a load reads first; past them, it is the rest of that window. A window grown
        // only from the 64-byte step under what the crossing lexed read up to 476 bytes again, in 512, to gain 36.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var row = file.Position;
        rows[3] = row;
        file.Raw(new string(' ', lead) + new string('0', 1000) + "3 0 obj null endobj\n%" + new string('c', 10_000) + "\n");
        file.Table(4, rows, "/Size 4 /Root 1 0 R");
        using var source = new WindowLoggingSource(file.Bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        document.Reader.ChainIndex!.TryGet(3, out var entry).Should().BeTrue();
        source.Windows.Clear();

        document.Reader.PlaceHeader(3, entry, new HeaderReading(long.MaxValue)).Place.Should().Be(HeaderPlace.AtOffset);

        var loadEnd = row + PdfFileReader.InitialObjectWindow;
        source.Windows.Should().Contain(window => window.Offset == row + lead, "the number is read again from its start");

        for (var index = 1; index < source.Windows.Count; index++)
        {
            var (offset, length) = source.Windows[index];
            var again = source.Windows[index - 1].Offset + source.Windows[index - 1].Length - offset;

            if (again > 0 && offset + length != loadEnd)
            {
                ((long)length).Should().BeGreaterThanOrEqualTo(2 * again, "the window at {0} reads {1} bytes again", offset - row, again);
            }
        }
    }

    [Fact]
    public void Placing_a_row_whose_header_s_tokens_lie_apart_reads_no_more_than_its_load()
    {
        // Object 4's row holds "4", a thousand blanks, a generation of 5,000 zeros, then "obj". The row's windows of 64 and
        // 512 bytes stop in the blanks; the next, which would stop 3,520 bytes short of the 8 KB a load reads, less than its
        // own 4,096, is the rest of that window, as a load reads none between, and decides the header: the row reads what
        // its load reads. A window of 4,096 bytes cut the zeros, and the next read them again from their start, 3,671 bytes
        // more than the load (mutant R-cap-min).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var at = file.Position;
        file.Raw("4" + new string(' ', 1_000) + new string('0', 5_000) + " obj\n(four)\nendobj\n%" + new string('c', 20_000) + "\n");
        rows[4] = at;
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        var bytes = file.Bytes;

        using var aloneSource = new StrictCountingSource(bytes);
        using var aloneDocument = PdfDocument.Open(aloneSource, options: null, ownsSource: false);
        var before = aloneSource.BytesRead;
        aloneDocument.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>();
        var load = aloneSource.BytesRead - before;
        aloneDocument.WasRepaired.Should().BeFalse();

        using var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        document.Reader.ChainIndex!.TryGet(4, out var four).Should().BeTrue();
        before = source.BytesRead;

        var placement = document.Reader.PlaceHeader(4, four, new HeaderReading(long.MaxValue));

        placement.Place.Should().Be(HeaderPlace.AtOffset);
        placement.Position.Should().Be(at);
        (source.BytesRead - before).Should().BeLessThanOrEqualTo(load);
    }

    [Fact]
    public void Validating_a_file_cut_short_under_a_row_reads_no_header_there()
    {
        // N3-01: another process cuts the file at object 3's row after the document opened. Settling's window there gave
        // nothing, and slicing it from its first token, which an empty window lacks, threw out of the validator; a load of
        // object 3 reads the end of the data there.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var cut = file.Position;
        rows[3] = file.Object(3, "(three)");
        file.Table(4, rows, "/Size 4 /Root 1 0 R");
        var source = new CutShortSource(file.Bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: true);
        source.CutAt((int)cut);

        var report = new PdfValidator().Validate(document);

        report.Findings.Should().Contain(finding =>
            finding.RuleId == PdfValidationRuleIds.XRefEntryBroken && finding.Location.Object == new PdfObjectId(3));
        document.GetObject(new PdfObjectId(3)).Should().Be(PdfNull.Instance);
    }

    [Fact]
    public void Probing_the_entries_consumes_no_more_than_twice_the_file_past_the_first_bytes_of_each_header()
    {
        // Two hundred rows place their objects in two comments of 100 KB, past which no header lies, each row in the other
        // comment than the row before: each row reads its comment to its end, taking what it consumes past the first 64
        // bytes from twice the file's length, and reading a few times that at most; once that is spent, the probe says the
        // entries it had not decided were not checked (#175).
        const int Rows = 200;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var first = file.Position;
        file.Raw("%" + new string('c', 100 * 1024) + "\nx\n");
        var second = file.Position;
        file.Raw("%" + new string('c', 100 * 1024) + "\nx\n");

        for (var number = 3; number < Rows + 3; number++)
        {
            rows[number] = (number % 2 == 0 ? first : second) + number;
        }

        file.Table(Rows + 3, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {Rows + 3} /Root 1 0 R"));
        var bytes = file.Bytes;
        using var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        document.Reader.SettleIndex();
        var settled = source.BytesRead;

        var probe = CrossReferenceProbe.Run(document);

        (source.BytesRead - settled).Should().BeLessThanOrEqualTo(
            (Rows * (64L + (2 * PdfFileReader.NearbySearchRadius))) + (10L * 2 * bytes.Length));
        probe.Broken.Should().NotBeEmpty("the rows decided before the reading was spent lead nowhere");
        probe.NotChecked.Should().ContainSingle().Which.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The validator stopped reading object headers where entries place them once it had read twice the file's length of them, past the first bytes of each: the entries of {Rows - probe.Broken.Count} objects it had not decided by then, the first object {3 + probe.Broken.Count}'s, were not checked."));
    }

    [Fact]
    public void Probing_rows_that_lead_into_one_run_of_blanks_crosses_it_once()
    {
        // As in a document whose block of 3 MB a fault zeroed, eighty rows place their objects in one run of 2 MB of null
        // bytes, which are white space, past which no header lies. A load from any of them reads the run to its end; the probe
        // crosses it once, and each row after the first reads on from its end: every entry is decided, and leads nowhere.
        const int Rows = 80;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var run = file.Position;
        file.Raw(new string('\0', 2 * 1024 * 1024) + "x\n");

        for (var number = 3; number < Rows + 3; number++)
        {
            rows[number] = run + (number * 1000);
        }

        file.Table(Rows + 3, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {Rows + 3} /Root 1 0 R"));
        var bytes = file.Bytes;
        using var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        document.Reader.SettleIndex();
        var settled = source.BytesRead;

        var probe = CrossReferenceProbe.Run(document);

        (source.BytesRead - settled).Should().BeLessThan(bytes.Length + (Rows * (64L + (2 * PdfFileReader.NearbySearchRadius))));
        probe.Broken.Should().HaveCount(Rows);
        probe.NotChecked.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Probing_rows_that_lead_into_one_run_of_blanks_crosses_it_once_whatever_their_order(bool descending)
    {
        // Forty rows place their objects 20,000 bytes apart in one run of 1 MB of null bytes, past which no header lies. Taken
        // in the order of their numbers, the rows' offsets went up or down: going down, each row crossed again the part of
        // the run the row before had crossed, and paid for it, and the probe said 34 entries were not checked. A crossing
        // that meets the run remembered joins it, and reads on from its end (#175).
        const int Rows = 40;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var run = file.Position;
        file.Raw(new string('\0', 1024 * 1024) + "x\n");

        for (var number = 3; number < Rows + 3; number++)
        {
            rows[number] = run + ((descending ? Rows + 2 - number : number - 3) * 20_000L);
        }

        file.Table(Rows + 3, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {Rows + 3} /Root 1 0 R"));
        var bytes = file.Bytes;
        using var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        document.Reader.SettleIndex();
        var settled = source.BytesRead;

        var probe = CrossReferenceProbe.Run(document);

        (source.BytesRead - settled).Should().BeLessThan(bytes.Length + (Rows * (64L + (2 * PdfFileReader.NearbySearchRadius))));
        probe.Broken.Should().HaveCount(Rows);
        probe.NotChecked.Should().BeEmpty();
    }

    [Fact]
    public void Validating_reads_no_more_than_a_few_times_the_file_however_many_rows_lead_into_one_run_of_blanks()
    {
        // Forty rows place their objects a byte apart at the start of a run of blanks longer than the object guard, of a
        // document opened to throw on a limit. A load of any of them would throw there, and rebuild nothing; settling read
        // each to the guard and went on, 1.6 MB a row, then at most twice the file's length before it rebuilt the index
        // (batch 13). Settling and the probe each cross the run once, and the rows inside it read on from its end (#175):
        // settling rebuilds nothing, as a load would not, and the probe says each entry was not checked.
        const int Rows = 40;
        const int Guard = 1024 * 1024;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };

        for (var number = 3; number < Rows + 3; number++)
        {
            rows[number] = file.Position + number;
        }

        file.Raw(new string(' ', Guard + 4096) + "\n");
        file.Table(Rows + 3, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {Rows + 3} /Root 1 0 R"));
        var bytes = file.Bytes;
        using var source = new StrictCountingSource(bytes);
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxObjectLength = Guard } };
        using var document = PdfDocument.Open(source, options, ownsSource: false);
        var opened = source.BytesRead;

        var report = new PdfValidator().Validate(document);

        (source.BytesRead - opened).Should().BeLessThan(3L * bytes.Length);
        document.WasRepaired.Should().BeFalse();
        report.Findings.Should().HaveCount(Rows).And.OnlyContain(finding => finding.RuleId == PdfValidationRuleIds.XRefCheckedInPart);
    }

    [Fact]
    public void Probing_rows_that_lead_into_several_blocks_of_blanks_crosses_each_once()
    {
        // Review of #175's fixes (M4-01): forty rows lead into a block of 512 KB of null bytes, forty more into a second one
        // of 256 KB. Only the longest run crossed was remembered: each row into the shorter block crossed it again, and the
        // probe said 36 entries were not checked. Several runs are remembered, each crossed once.
        const int Rows = 40;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var number = 3;

        foreach (var (length, after) in new[] { (512 * 1024, "x\n"), (256 * 1024, "y\n") })
        {
            var block = file.Position;
            file.Raw(new string('\0', length) + after);

            for (var row = 0; row < Rows; row++, number++)
            {
                rows[number] = block + (row * (length / Rows));
            }
        }

        file.Table(number, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {number} /Root 1 0 R"));
        var bytes = file.Bytes;
        using var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        document.Reader.SettleIndex();
        var settled = source.BytesRead;

        var probe = CrossReferenceProbe.Run(document);

        // Each block crossed once, a window past its end at most; each row, a first window at its offset and one past the
        // block, and the search near it.
        (source.BytesRead - settled).Should().BeLessThan(bytes.Length + (2 * 64 * 1024) + (2 * Rows * (128L + (2 * PdfFileReader.NearbySearchRadius))));
        probe.Broken.Should().HaveCount(2 * Rows);
        probe.NotChecked.Should().BeEmpty();
    }

    [Fact]
    public void Probing_rows_inside_a_run_of_blanks_longer_than_the_object_guard_joins_what_each_crossed()
    {
        // M5-02: four hundred rows, a kilobyte apart, inside one run of 1 MB of null bytes, under an object guard of 200 KB.
        // Each row's crossing stops at its guard, short of the run's end; the next row's meets it, and joins it. Remembered
        // apart, each crossing was read again, the allowance was spent, and 338 entries were said not checked.
        const int Rows = 400;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var run = file.Position;
        file.Raw(new string('\0', 1024 * 1024) + "x\n");

        for (var number = 3; number < Rows + 3; number++)
        {
            rows[number] = run + ((number - 3) * 1000L);
        }

        file.Table(Rows + 3, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {Rows + 3} /Root 1 0 R"));
        var bytes = file.Bytes;
        using var source = new StrictCountingSource(bytes);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 200_000 } };
        using var document = PdfDocument.Open(source, options, ownsSource: false);
        document.Reader.SettleIndex();
        var settled = source.BytesRead;

        var probe = CrossReferenceProbe.Run(document);

        probe.NotChecked.Should().HaveCount(Rows).And.OnlyContain(finding => finding.Message.Contains("MaxObjectLength", StringComparison.Ordinal));
        (source.BytesRead - settled).Should().BeLessThan(bytes.Length + (Rows * (64L + (2 * PdfFileReader.NearbySearchRadius))));
    }

    [Fact]
    public void Settling_rows_shifted_into_several_blocks_of_blanks_rebuilds_nothing()
    {
        // M4-01: twenty objects before each of two blocks of null bytes, each row a few bytes into the block after its object,
        // which a load finds near the row. Settling crossed the shorter block again for each row into it, spent what it may
        // read, and rebuilt an index no load rebuilds.
        const int Objects = 20;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var number = 3;

        foreach (var length in new[] { 512 * 1024, 256 * 1024 })
        {
            var first = number;

            for (var count = 0; count < Objects; count++, number++)
            {
                file.Object(number, "null");
            }

            var block = file.Position;
            file.Raw(new string('\0', length));
            file.Object(1000 + number, "null");

            for (var shifted = first; shifted < number; shifted++)
            {
                rows[shifted] = block + 1 + (shifted - first);
            }
        }

        file.Table(number, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {number} /Root 1 0 R"));
        var bytes = file.Bytes;
        using var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var opened = source.BytesRead;

        document.Reader.SettleIndex();

        document.WasRepaired.Should().BeFalse();
        (source.BytesRead - opened).Should().BeLessThan(bytes.Length + (2 * 64 * 1024) + (2 * Objects * (128L + (2 * PdfFileReader.NearbySearchRadius) + 64)));
        new PdfValidator().Validate(document).Findings.Should().HaveCount(2 * Objects)
            .And.OnlyContain(finding => finding.RuleId == PdfValidationRuleIds.XRefEntryShifted);
    }

    [Fact]
    public void Probing_a_sound_index_allocates_nothing_an_entry_but_its_numbers()
    {
        // M4-02: the probe wrote every entry's offset as a string, a finding or not, 34 bytes an entry; it keeps only the
        // sorted numbers of the entries, four bytes each.
        static long Probing(int padding)
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
                [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
            };

            for (var number = 3; number < padding + 3; number++)
            {
                rows[number] = file.Padding(number, 10);
            }

            file.Table(padding + 3, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {padding + 3} /Root 1 0 R"));
            using var document = PdfDocument.Open(file.Bytes);
            document.Reader.SettleIndex();
            return Allocations.Least(() => CrossReferenceProbe.Run(document).Broken.Count, out _);
        }

        var thousand = Probing(1_000);
        var fourThousand = Probing(4_000);

        (fourThousand - thousand).Should().BeLessThanOrEqualTo(3_000 * 4 + 64);
    }

    [Fact]
    public void Probing_rows_whose_header_runs_through_a_run_of_blanks_already_crossed_reads_on_from_its_end()
    {
        // Object 4's header holds 1 MB of blanks between its generation and obj. Row 3 places its object inside them, and
        // crosses them; a thousand rows place theirs at the header, whose reading reads on from the end of the run once it
        // has lexed the generation: no row reads the blanks again, and every row is checked. Each read them again, lexing
        // them, and the rows past twice the file's length were said not checked (#175).
        const int Rows = 1000;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var header = file.Position;
        file.Raw("4 0" + new string(' ', 1024 * 1024) + "obj\n(four)\nendobj\n");
        rows[3] = header + 10;

        for (var number = 5; number < Rows + 5; number++)
        {
            rows[number] = header;
        }

        file.Table(Rows + 5, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {Rows + 5} /Root 1 0 R"));
        var bytes = file.Bytes;
        using var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        document.Reader.SettleIndex();
        var settled = source.BytesRead;

        var probe = CrossReferenceProbe.Run(document);

        (source.BytesRead - settled).Should().BeLessThan((Rows * (64L + (2 * PdfFileReader.NearbySearchRadius))) + (20L * bytes.Length));
        probe.NotChecked.Should().BeEmpty();
    }

    [Fact]
    public void Settling_rows_that_lead_into_one_long_comment_reads_no_more_than_twice_the_file_of_them_before_it_rebuilds()
    {
        // Forty rows place their objects a byte apart at the start of one comment longer than the object guard, of a
        // document opened to throw on a limit: a load of any of them would throw there, and rebuild nothing, and settling
        // reads each to the guard. What it consumes past the first bytes of each is twice the file's length at most, past
        // which it rebuilds the index in one pass: without that bound it read the file 62 times (#175).
        const int Rows = 40;
        const int Guard = 1024 * 1024;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };

        for (var number = 3; number < Rows + 3; number++)
        {
            rows[number] = file.Position + number;
        }

        file.Raw("%" + new string('c', Guard + 4096) + "\n");
        file.Table(Rows + 3, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {Rows + 3} /Root 1 0 R"));
        var bytes = file.Bytes;
        using var source = new StrictCountingSource(bytes);
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxObjectLength = Guard } };
        using var document = PdfDocument.Open(source, options, ownsSource: false);
        var opened = source.BytesRead;

        document.Reader.SettleIndex();

        document.WasRepaired.Should().BeTrue();
        (source.BytesRead - opened).Should().BeLessThan(6L * bytes.Length);
    }

    [Fact]
    public void Validating_rows_that_all_lead_into_one_long_comment_reads_the_first_bytes_of_each_once_its_fill_is_read()
    {
        // Twenty thousand rows place their objects at one comment of 8,000 bytes: the first window and the one a load starts
        // with were read free of the probe's allowance, 9 KB a row, and the file was read hundreds of times. What a header's
        // reading consumes is taken from twice the file's length, past the first 64 bytes; once that is spent, each row
        // reads those 64 bytes, and its entry is said not checked (#175).
        const int Count = 20_000;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var at = file.Position;
        file.Raw("%" + new string('c', 8000) + "\n9 0 obj null endobj\n");

        for (var number = 3; number < Count + 3; number++)
        {
            rows[number] = at;
        }

        file.Table(Count + 3, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {Count + 3} /Root 1 0 R"));
        var bytes = file.Bytes;
        using var source = new StrictCountingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var opened = source.BytesRead;

        var report = new PdfValidator().Validate(document);

        // The first bytes of each row; before the allowance is spent, a few times what the rows read consumed, twice the
        // file's length in all; and the rebuild settling sets off, once over the file.
        (source.BytesRead - opened).Should().BeLessThan((Count * 64L) + (20L * bytes.Length));
        report.Findings.Should().ContainSingle(finding => finding.RuleId == PdfValidationRuleIds.XRefCheckedInPart)
            .Which.Message.Should().EndWith(" were not checked.");
        report.Findings.Should().Contain(finding => finding.RuleId == PdfValidationRuleIds.XRefEntryBroken);
    }

    [Fact]
    public void Opening_a_chain_of_trailers_that_never_close_reads_a_bounded_amount()
    {
        // Each section's trailer opens an array that never closes, so it runs past the window's edge and on
        // to the end of the file, through every section after it and a megabyte of comment. A cut trailer
        // is read again through a window of its own, which stops at 64 KB: growing the table's window to the
        // end of the file for each of the hundred sections read about 300 MB. MaxTrailerLength cuts the newest
        // trailer, which ends the chain: what lay past the cut, its /Prev among them, is unknown.
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
        document.Reader.Structure.Sections.Should().ContainSingle().Which.TrailerCutByLimit.Should().BeTrue();
        source.BytesRead.Should().BeLessThan(256L * 1024);
    }

    [Fact]
    public void Opening_a_chain_of_hybrid_sections_whose_streams_never_close_reads_a_bounded_amount()
    {
        // Every section of the chain names, in /XRefStm, a cross-reference stream of its own whose
        // dictionary opens an array that never closes and runs on to the end of the file. The newest is parsed
        // through windows that stop at 64 KB, a bounded read, not one the size of the file; MaxTrailerLength
        // cuts it, which ends the chain, an older section's rows standing for none of what it defines.
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
        document.Reader.Structure.Sections.Should().HaveCount(2).And.Subject.Last().CutByLimit.Should().BeTrue();
        source.BytesRead.Should().BeLessThan(256L * 1024);
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

    [Fact]
    public void Decodes_data_no_flate_reading_can_decode_once_however_many_flate_filters_name_it()
    {
        // A hundred thousand /FlateDecode over two bytes no Flate reading can decode. Each filter handed the next the
        // bytes it could not decode, and each read them again: 424 MB allocated, two seconds, and as many reports as
        // the diagnostics hold. The chain ends at the first, which reports once.
        var chain = new PdfObject[100_000];
        Array.Fill(chain, PdfName.FlateDecode);
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, new PdfArray(chain));
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory(new byte[] { 0xFF, 0xFF }));
        var diagnostics = new PdfDiagnostics();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var decoded = Measure(() => stream.Decode(diagnostics));

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(16L * 1024 * 1024);
        decoded.ToArray().Should().Equal(0xFF, 0xFF);
        diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.FilterFailed);
    }

    [Fact]
    public void Opens_a_cross_reference_stream_whose_run_length_filters_multiply_its_data_at_a_cost_its_number_of_filters_leaves_alone()
    {
        // #166's file: a cross-reference stream naming /RunLengthDecode again and again over two bytes, each filter
        // multiplying what the one before gave by 64. Each filter was bounded alone: from the fifth on, each filled an
        // array of the bound, 3.3 GB as the 513-byte file opened at the default bound. The filters before the last share
        // one bound, so that once they have decoded it together the rest decode nothing: opening the file allocates a
        // few bounds' worth, the same whether it names sixteen filters or sixty-four.
        const int Bound = 4 * 1024 * 1024;
        var sixteen = Opening(16, out var reports);
        var sixtyFour = Opening(64, out _);

        sixteen.Should().BeLessThan(6L * Bound);
        (sixtyFour - sixteen).Should().BeLessThan(64 * 1024);
        reports.Should().ContainSingle(code => code == PdfDiagnosticCodes.LimitDecodedStream);

        static long Opening(int filters, out string[] reports)
        {
            var file = Encoding.Latin1.GetBytes(
                "%PDF-1.5\n1 0 obj\n<< /Type /XRef /Size 2 /W [1 1 1] /Root 2 0 R /Length 2 /Filter [" +
                string.Concat(Enumerable.Repeat("/RunLengthDecode ", filters)) +
                "] >>\nstream\n\u0081\u0081\nendstream\nendobj\nstartxref\n9\n%%EOF\n");
            var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = Bound } };
            PdfDocument.Open(file, options).Dispose();
            var before = GC.GetAllocatedBytesForCurrentThread();

            using var document = Measure(() => PdfDocument.Open(file, options));

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            reports = [.. document.Diagnostics.Select(d => d.Code)];
            return allocated;
        }
    }

    [Fact]
    public void Decodes_a_long_chain_of_sound_flate_layers_within_twice_the_bound()
    {
        // Two thousand layers of zlib, each storing the one inside it, around two bytes: a sound stream whose layers
        // grow by a few bytes each, about 22 MB together. Each filter was bounded alone, so that a few hundred kilobytes
        // of file decoded every layer, gigabytes for twenty thousand. The filters before the last share the bound: they
        // stop once they have passed along a megabyte together, and the chain allocates a few bounds' worth, under a
        // guard raised to let every filter run.
        const int Bound = 1024 * 1024;
        var data = "ok"u8.ToArray();

        for (var layer = 0; layer < 2_000; layer++)
        {
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.NoCompression, leaveOpen: true))
            {
                zlib.Write(data);
            }

            data = compressed.ToArray();
        }

        var chain = new PdfObject[2_000];
        Array.Fill(chain, PdfName.FlateDecode);
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, new PdfArray(chain));
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory(data));
        var guard = new PdfLimitGuard(
            PdfReaderLimits.Default with { MaxDecodedStreamLength = Bound, MaxFilterChainLength = 2_000 }, throwOnLimit: false);
        var diagnostics = new PdfDiagnostics();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var decoded = Measure(() => IO.Filters.PdfFilterPipeline.Decode(stream, diagnostics, guard));

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(8L * Bound);
        decoded.Length.Should().Be(0);
        diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
    }

    [Fact]
    public void Decodes_a_stream_naming_a_million_filters_at_the_cost_of_as_many_as_the_guard_lets_run()
    {
        // A million /RunLengthDecode over FF FF, which each decodes to itself: every filter has data to decode, whatever
        // the bound, and each cost about a hundred bytes and a tenth of a microsecond, seconds for an array an object
        // stream holds, and a filter whose data never empties can cost tens of kilobytes. The guard ends the chain after
        // the 64th, and the elements past it are not even read; each of the 64 reports the end-of-data byte its data
        // lacks (#141), a cost the guard bounds too.
        var chain = new PdfObject[1_000_000];
        Array.Fill(chain, PdfName.RunLengthDecode);
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, new PdfArray(chain));
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory(new byte[] { 0xFF, 0xFF }));
        var diagnostics = new PdfDiagnostics();

        // Decoded once, and its time asserted, before the window: what a first assertion sets up is no part of decoding.
        Measure(() => stream.Decode(new PdfDiagnostics()));
        var before = GC.GetAllocatedBytesForCurrentThread();

        var decoded = Measure(() => stream.Decode(diagnostics));

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(64 * 1024);
        decoded.ToArray().Should().Equal(0xFF, 0xFF);
        diagnostics.Select(d => d.Code).Should().Equal([.. Enumerable.Repeat(PdfDiagnosticCodes.FilterFailed, 64), PdfDiagnosticCodes.LimitFilterChain]);
        diagnostics.Take(64).Should().OnlyContain(d => d.Severity == PdfDiagnosticSeverity.Repair);
    }

    [Fact]
    public void Decodes_a_stream_naming_a_million_filters_without_keeping_anything_for_the_names_its_array_holds()
    {
        // A million /Crypt named directly, read under every guard raised as far as it goes: an object stream's member can
        // hold forty million in 256 MB of data. Keeping an entry for each filter cost about 80 bytes a name on every
        // decode, and ran out of memory past two gigabytes; the array holds the names, and a chain keeps nothing more.
        var chain = new PdfObject[1_000_000];
        Array.Fill(chain, PdfName.Crypt);
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, new PdfArray(chain));
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory("hello"u8.ToArray()));
        var guard = new PdfLimitGuard(PdfReaderLimits.Unbounded, throwOnLimit: false);
        var diagnostics = new PdfDiagnostics();

        // Decoded and told once, and the time asserted, before the window: what a first assertion sets up is no part of
        // decoding. Decoding walks the million names, long enough for a collection another test triggers to fall in the
        // window and have the thread's count take an allocation quantum it never used: each budget holds the least of three
        // measures no collection paused (#253).
        Measure(() => IO.Filters.PdfFilterPipeline.Decode(stream, diagnostics, guard));
        stream.HasImageFilter();

        var decoding = Allocations.Least(
            () => Measure(() => IO.Filters.PdfFilterPipeline.Decode(stream, diagnostics, guard)), out var decoded);
        var telling = Allocations.Least(stream.HasImageFilter, out var image);

        decoding.Should().BeLessThan(4 * 1024);
        telling.Should().BeLessThan(1024);
        image.Should().BeFalse();
        Encoding.ASCII.GetString(decoded.Span).Should().Be("hello");
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Decodes_nothing_further_once_a_filter_has_decoded_a_stream_to_nothing()
    {
        // A hundred thousand /LZWDecode over data the first decodes to nothing — a clear code, then the end-of-data code —,
        // read under every guard raised as far as it goes: each filter after it decoded nothing to nothing, and set up a
        // code table of 42 KB to do it, 4 GB in all. Decoding stops at data that is empty.
        var chain = new PdfObject[100_000];
        Array.Fill(chain, PdfName.LZWDecode);
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, new PdfArray(chain));
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory(new byte[] { 0x80, 0x40, 0x40 }));
        var guard = new PdfLimitGuard(PdfReaderLimits.Unbounded, throwOnLimit: false);
        var diagnostics = new PdfDiagnostics();

        // Decoded once, and its time asserted, before the window: what a first assertion sets up is no part of decoding.
        Measure(() => IO.Filters.PdfFilterPipeline.Decode(stream, diagnostics, guard));
        var before = GC.GetAllocatedBytesForCurrentThread();

        var decoded = Measure(() => IO.Filters.PdfFilterPipeline.Decode(stream, diagnostics, guard));

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(256 * 1024);
        decoded.Length.Should().Be(0);
        diagnostics.Should().BeEmpty();
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
    /// <summary>
    /// Writes a file indexed by a cross-reference stream whose catalog names object 6, which object stream 7 holds, with
    /// 2,000 objects written directly in the file, and the row of <paramref name="brokenRow"/> outside the file.
    /// </summary>
    /// <summary>
    /// The sound document spread out, object 3's row 700 bytes before its header, inside object 4's string: the document
    /// opens with five entries, and reading 3 rebuilds its index.
    /// </summary>
    private static byte[] RebuiltOnReadingThree() =>
        PdfTemplate.Build(PdfTemplate.Spread.Replace("{row:3}", "{row:3:0:-700}", StringComparison.Ordinal));

    private static byte[] ObjectsAndAMember(int brokenRow)
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Test 6 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [6] = (2, 7, 0),
            [7] = (1, file.ObjectStream(7, [(6, "(six)")]), 0),
            [8] = (1, file.Object(8, "<< /Eight 8 >>"), 0),
        };

        for (var number = 10; number < 2010; number++)
        {
            rows[number] = (1, file.Object(number, string.Create(CultureInfo.InvariantCulture, $"<< /Number {number} >>")), 0);
        }

        rows[brokenRow] = (1, 99_999_999, 0);
        file.CrossReferenceStream(2010, 2011, rows, "/Root 1 0 R");
        return file.Bytes;
    }

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
    internal static int StreamNumber(int stream) => 3 + stream;

    /// <summary>The number of the object at <paramref name="index"/> of object stream <paramref name="stream"/>.</summary>
    internal static int PackedNumber(int stream, int index, int objectsPerStream = 1) => 1000 + (stream * objectsPerStream) + index;

    /// <summary>Writes an object stream whose header lists object 6 and whose /N claims <paramref name="count"/> objects.</summary>
    private static byte[] Claiming(int count)
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [5] = (1, file.Object(5, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N {count} /First 4 /Length 8 >>\nstream\n6 0 <<>>\nendstream")), 0),
            [6] = (2, 5, 0),
        };
        file.CrossReferenceStream(7, 8, rows, "/Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// Writes a file of <paramref name="streams"/> streams, objects 3 and on, each one's header inside the data of the one
    /// before, every <c>/Length</c> ending at the one <c>endstream</c> after <paramref name="filler"/> bytes: each length
    /// confirmed, each stream's data the whole stretch from its start to that <c>endstream</c> (#170). With
    /// <paramref name="objectStreams"/>, objects 10 and on, each an object stream of one member, objects 3000 and on, that a
    /// cross-reference stream places.
    /// </summary>
    internal static byte[] Overlapping(int streams, int filler, bool objectStreams = false)
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
        };
        var start = file.Position;
        var body = new StringBuilder();
        var lengthAt = new int[streams];
        var dataStart = new int[streams];

        for (var k = 0; k < streams; k++)
        {
            var number = (objectStreams ? 10 : 3) + k;
            var member = string.Create(CultureInfo.InvariantCulture, $"{3000 + k} 0 ");
            rows[number] = (1, start + body.Length, 0);
            body.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< ");

            if (objectStreams)
            {
                body.Append(CultureInfo.InvariantCulture, $"/Type /ObjStm /N 1 /First {member.Length} ");
            }

            body.Append("/Length ");
            lengthAt[k] = body.Length;
            body.Append("0000000000 >>\nstream\n");
            dataStart[k] = body.Length;

            if (objectStreams)
            {
                body.Append(member);
                rows[3000 + k] = (2, number, 0);
            }
        }

        body.Append('x', filler);
        var end = body.Length;
        body.Append("\nendstream\nendobj\n");

        for (var k = 0; k < streams; k++)
        {
            var digits = (end - dataStart[k]).ToString("D10", CultureInfo.InvariantCulture);

            for (var d = 0; d < digits.Length; d++)
            {
                body[lengthAt[k] + d] = digits[d];
            }
        }

        file.Raw(body.ToString());
        var index = objectStreams ? 6000 : 1000;
        file.CrossReferenceStream(index, index + 1, rows, "/Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// Writes a file whose object stream holds <paramref name="members"/> members one byte apart, objects 1000 and on, over
    /// as many open brackets: read to the end of the data, each would take every bracket after it (#200).
    /// </summary>
    internal static byte[] MembersOneByteApart(int members)
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
        };
        var header = new StringBuilder();

        for (var i = 0; i < members; i++)
        {
            header.Append(CultureInfo.InvariantCulture, $"{1000 + i} {i} ");
            rows[1000 + i] = (2, 5, i);
        }

        var data = Compress(Encoding.ASCII.GetBytes(header.ToString() + new string('[', members)));
        rows[5] = (1, file.Object(5, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N {members} /First {header.Length} /Filter /FlateDecode /Length {data.Length} >>\nstream\n{Encoding.Latin1.GetString(data)}\nendstream")), 0);
        file.CrossReferenceStream(3, 1000 + members, rows, "/Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// Writes a file whose index is a cross-reference stream, with <paramref name="streams"/> object streams holding
    /// <paramref name="objectsPerStream"/> small dictionaries each, padded with white space to decode to at least
    /// <paramref name="decodedLength"/> bytes; with <paramref name="everyIndexWrong"/>, every entry gives index 0.
    /// </summary>
    internal static byte[] ObjectStreams(int streams, int decodedLength, int objectsPerStream, bool everyIndexWrong)
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

    /// <summary>
    /// The catalog, an empty page tree and a cross-reference stream of <paramref name="rows"/> rows of one byte, under
    /// two Flate filters: rows 1 to 3 place objects 1 to 3, every other row an object at offset 0. Built a piece at a
    /// time, so that the rows are never held whole.
    /// </summary>
    private static byte[] OneByteRows(int rows, out long section)
    {
        var head = new StringBuilder("%PDF-1.7\n");
        var catalog = head.Length;
        head.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = head.Length;
        head.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        section = head.Length;

        using var inner = new MemoryStream();
        using (var zlib = new ZLibStream(inner, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write([0, (byte)catalog, (byte)pages, (byte)section]);
            var zeros = new byte[64 * 1024];

            for (long written = 4; written < rows; written += zeros.Length)
            {
                zlib.Write(zeros, 0, (int)Math.Min(zeros.Length, rows - written));
            }
        }

        using var outer = new MemoryStream();
        using (var zlib = new ZLibStream(outer, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(inner.GetBuffer(), 0, (int)inner.Length);
        }

        var data = outer.ToArray();
        head.Append(string.Create(
            CultureInfo.InvariantCulture,
            $"3 0 obj\n<< /Type /XRef /Size {rows} /W [0 1 0] /Root 1 0 R /Filter [/FlateDecode /FlateDecode] /Length {data.Length} >>\nstream\n"));

        return
        [
            .. Encoding.ASCII.GetBytes(head.ToString()),
            .. data,
            .. Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{section}\n%%EOF\n")),
        ];
    }

    /// <summary>
    /// The catalog and an empty page tree, indexed by a cross-reference stream whose <c>/Size</c> claims
    /// <paramref name="declared"/> rows, a classic table whose subsection claims them, or a chain of two classic sections
    /// whose newest trailer's <c>/Size</c> claims them; the file holds rows for the objects it has.
    /// </summary>
    private static byte[] SizeClaimed(string shape, int declared)
    {
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var claim = declared.ToString(CultureInfo.InvariantCulture);

        if (shape == DeclaredSize.Stream)
        {
            return Encoding.Latin1.GetBytes(
                Encoding.Latin1.GetString(builder.BuildWithXRefStream(rootNumber: 1)).Replace("/Size 5 ", $"/Size {claim} ", StringComparison.Ordinal));
        }

        var classic = builder.BuildClassic(rootNumber: 1);

        if (shape == DeclaredSize.Table)
        {
            return Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(classic).Replace("xref\n0 3\n", $"xref\n0 {claim}\n", StringComparison.Ordinal));
        }

        var text = Encoding.Latin1.GetString(TestPdfBuilder.AppendIncrementalUpdate(classic, rootNumber: 1, [(1, "<< /Type /Catalog /Pages 2 0 R >>")]));
        var newest = text.LastIndexOf("/Size 3 ", StringComparison.Ordinal);
        return Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, newest), $"/Size {claim} ", text.AsSpan(newest + "/Size 3 ".Length)));
    }

    /// <summary>The ways <see cref="SizeClaimed"/> claims its rows.</summary>
    private static class DeclaredSize
    {
        public const string Stream = "a cross-reference stream's /Size";
        public const string Table = "a classic table's subsection";
        public const string Chain = "a chain's newest /Size";
    }

    /// <summary>
    /// The catalog, an empty page tree and a cross-reference stream of <paramref name="rows"/> rows of two bytes under one
    /// Flate filter: rows 1 to 3 place objects 1 to 3, every other row is of type 3, which indexes nothing.
    /// </summary>
    private static byte[] ReservedTypeRows(int rows)
    {
        var head = new StringBuilder("%PDF-1.7\n");
        var catalog = head.Length;
        head.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = head.Length;
        head.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        var section = head.Length;

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write([0, 0, 1, (byte)catalog, 1, (byte)pages, 1, (byte)section]);
            var reserved = new byte[64 * 1024];
            reserved.AsSpan().Fill(3);

            for (long written = 8; written < 2L * rows; written += reserved.Length)
            {
                zlib.Write(reserved, 0, (int)Math.Min(reserved.Length, (2L * rows) - written));
            }
        }

        var data = compressed.ToArray();
        head.Append(string.Create(
            CultureInfo.InvariantCulture,
            $"3 0 obj\n<< /Type /XRef /Size {rows} /W [1 1 0] /Root 1 0 R /Filter /FlateDecode /Length {data.Length} >>\nstream\n"));

        return
        [
            .. Encoding.ASCII.GetBytes(head.ToString()),
            .. data,
            .. Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{section}\n%%EOF\n")),
        ];
    }

    /// <summary>
    /// The catalog, an empty page tree and a cross-reference stream whose rows place them, then <paramref name="refused"/>
    /// rows for objects 4 onward whose offsets need all of eight bytes, past what a long holds.
    /// </summary>
    private static byte[] RefusedRows(int refused, out long section)
    {
        var head = new StringBuilder("%PDF-1.7\n");
        var catalog = head.Length;
        head.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = head.Length;
        head.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        section = head.Length;

        var rows = new List<byte>();
        Row(0, 0);
        Row(1, (ulong)catalog);
        Row(1, (ulong)pages);
        Row(1, (ulong)section);

        for (var i = 0; i < refused; i++)
        {
            Row(1, ulong.MaxValue);
        }

        head.Append(string.Create(
            CultureInfo.InvariantCulture,
            $"3 0 obj\n<< /Type /XRef /Size {4 + refused} /W [1 8 1] /Root 1 0 R /Length {rows.Count} >>\nstream\n"));

        return
        [
            .. Encoding.Latin1.GetBytes(head.ToString()),
            .. rows,
            .. Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{section}\n%%EOF\n")),
        ];

        void Row(byte type, ulong offset)
        {
            rows.Add(type);

            for (var shift = 56; shift >= 0; shift -= 8)
            {
                rows.Add((byte)(offset >> shift));
            }

            rows.Add(0);
        }
    }

    /// <summary>The catalog and an empty page tree, then objects 3 to 20,002, each <paramref name="body"/>, packed or written directly.</summary>
    private static byte[] ManyObjects(bool packed, string body)
    {
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        for (var number = 3; number < 20_003; number++)
        {
            builder.WithObject(number, body);
        }

        return packed
            ? builder.BuildWithXRefStream(rootNumber: 1, compressedObjects: [.. Enumerable.Range(3, 20_000)])
            : builder.BuildClassic(rootNumber: 1);
    }

    /// <summary>Opens <paramref name="file"/> and reads its objects 3 to 20,002, and gives what reading them allocated.</summary>
    private static long AllocatedReadingEveryObject(byte[] file)
    {
        using var document = PdfDocument.Open(file);
        var allocated = GC.GetAllocatedBytesForCurrentThread();

        for (var number = 3; number < 20_003; number++)
        {
            _ = document.GetObject(new PdfObjectId(number));
        }

        return GC.GetAllocatedBytesForCurrentThread() - allocated;
    }

    /// <summary>Compresses <paramref name="data"/> with zlib, as a <c>/FlateDecode</c> stream holds it.</summary>
    internal static byte[] Compress(byte[] data)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return compressed.ToArray();
    }

    private static T Measure<T>(Func<T> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = action();
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(Budget, "A hostile input must not be allowed to take unbounded time.");
        return result;
    }

    /// <summary>
    /// Writes <paramref name="innermost"/> as the value of <paramref name="depth"/> nested dictionaries:
    /// <c>&lt;&lt; /A &lt;&lt; /A … &gt;&gt; &gt;&gt;</c>.
    /// </summary>
    private static string NestedDictionaries(int depth, string innermost) =>
        string.Concat(Enumerable.Repeat("<< /A ", depth)) + innermost + string.Concat(Enumerable.Repeat(" >>", depth));

    /// <summary>
    /// Opens <paramref name="file"/> and reads its object 3 on a small thread, whose stack is 256 KB: what was read,
    /// the document's diagnostics, and what was thrown instead, if anything.
    /// </summary>
    private static (PdfObject? First, PdfDiagnostics? Diagnostics, Exception? Failure) ReadObject3OnASmallStack(
        byte[] file)
    {
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

        return (first, diagnostics, failure);
    }

    /// <summary>A file in memory that records each window read from it: where it starts, and how many bytes it holds.</summary>
    private sealed class WindowLoggingSource(byte[] data) : PdfFileSource
    {
        /// <summary>Gets the windows read, in order.</summary>
        public List<(long Offset, int Length)> Windows { get; } = [];

        public override long Length => data.Length;

        public override int Read(long offset, Span<byte> buffer)
        {
            var count = (int)Math.Clamp(data.Length - offset, 0, buffer.Length);
            data.AsSpan((int)offset, count).CopyTo(buffer);
            Windows.Add((offset, count));
            return count;
        }
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
