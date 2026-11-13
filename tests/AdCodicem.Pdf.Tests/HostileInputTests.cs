using System.Buffers;
using System.Buffers.Binary;
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
using AdCodicem.Pdf.Validation.Rules;

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
        // was read again for each section, and opening read the file several times over (#224). Each section's rows that
        // spell the endstream, of reserved types, are reported once for it (#221).
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
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(
            [.. Enumerable.Repeat(PdfDiagnosticCodes.XRefEntryReserved, Sections), PdfDiagnosticCodes.LimitObject]);
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
    [InlineData("trailer 5 0 R\n", "trailer 5 0\n")]
    [InlineData("trailer << /A << >>\nstream\nx\nendstream >>\n", "trailer << /A 1 >>\n")]
    public void Quotes_nothing_in_what_a_rebuild_s_scan_for_trailers_notes_once_the_diagnostics_are_full_however_many_keywords_make_a_note(string noted, string control)
    {
        // Twenty thousand trailer keywords and no table: opening rebuilds the index, whose scan for trailers parses what
        // follows each keyword, here a reference in the dictionary's place, or a dictionary a stream nested in it cuts
        // short, and notes it. Past the ten entries the document keeps, each note is counted and dropped, and neither the
        // keyword's offset nor the object's number is quoted for it: quoted for every keyword, the notes allocated some 310
        // and 330 bytes a keyword more than as many keywords followed by what makes no note, an integer or a sound
        // dictionary, where they now allocate some 30 bytes more and 30 fewer (#171).
        const int Keywords = 20_000;
        var options = PdfReaderOptions.Default with { DiagnosticCapacity = 10 };

        var allocated = AllocatedOpening(Keywords, noted, options, out var report);
        var extra = allocated - AllocatedOpening(Keywords, control, options, out var sound);

        sound.Should().NotContain(diagnostic => diagnostic.Code.StartsWith("syntax.", StringComparison.Ordinal));
        report.SuppressedCount.Should().BeGreaterThanOrEqualTo(Keywords - 10);
        report.Where(diagnostic => diagnostic.Code.StartsWith("syntax.", StringComparison.Ordinal)).Should().NotBeEmpty()
            .And.OnlyContain(diagnostic => diagnostic.Message.Contains("trailer keyword at offset ", StringComparison.Ordinal));
        extra.Should().BeLessThan(128L * Keywords, "a note the document's diagnostics drop is formatted for no keyword");
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
    public void Rows_of_a_reserved_type_take_no_more_entries_than_the_guard_allows()
    {
        // A row of a reserved type reads as a reference to the null object, its number indexed as a free one's is (#221):
        // such rows take entries as free rows do, and the guard bounds them. Eight million rows of type 3 decode from 16 KB;
        // under a guard of a million entries, the index fills at the millionth number, which is reported, and is sized for
        // a million. They once indexed nothing, and the room they asked for was bounded by the guard all the same.
        var file = ReservedTypeRows(8_000_000);
        var limits = PdfReaderLimits.Default with { MaxIndexEntries = 1_000_000 };
        file.Length.Should().BeLessThan(32 * 1024);

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        using var document = Measure(() => PdfDocument.Open(file, new PdfReaderOptions { Limits = limits }));
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        document.ObjectCount.Should().Be(1_000_000);
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitIndexEntries, PdfDiagnosticCodes.XRefEntryReserved);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.XRefEntryReserved).Which.Message.Should().Contain(
            " gives 999,997 rows a reserved type: the row for object 4 gives it type 3, the first of them. ");
        // Measured at 119,845,568 to 119,853,680 bytes on 2026-11-04, 5 % above: 16 MB of rows decoded, and the index sized
        // for a million entries, its slots with them, where it would be for eight million past the guard; 119,843,360 and
        // 119,851,472 on 2026-10-28, when the rows took no entry; 140,272,056 with the map it was before #155.
        allocated.Should().BeLessThan(125_850_000, "the room made for rows of a reserved type is bounded by the guard");
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
    public void The_numbers_a_stream_refuses_cost_no_more_than_a_map_of_them_to_an_int()
    {
        // Each of 100,000 rows gives an offset past what a long holds, and is refused: its number is held apart while the
        // chain is read, so that no older section's row stands for it, with the ordinal of the reading that refused it, which
        // tells a row a section refused itself from one a newer section refused (#210). Held with the section's record
        // instead, each number took 8 bytes more, which the set's growth doubles. Beside the same stream whose rows stand,
        // the refused numbers cost what a map of them to an int costs, filled one at a time, and a few kilobytes more.
        const int Rows = 100_000;
        var refused = RefusedRows(Rows, out _);
        var standing = RefusedRows(Rows, out _, offset: 10);

        var refusing = Allocations.Least(() => Opened(refused), out var refusedCodes);
        var placing = Allocations.Least(() => Opened(standing), out var standingCodes);
        var map = Allocations.Least(() => Map(Rows), out var mapped);

        refusedCodes.Should().Equal(PdfDiagnosticCodes.XRefSectionUnreadable);
        standingCodes.Should().BeEmpty();
        mapped.Should().Be(Rows);
        (refusing - placing).Should().BeLessThan(map + (16 * 1024), "each number refused is held with an int beside it, not a reference");

        static string[] Opened(byte[] file)
        {
            using var document = PdfDocument.Open(file);
            return [.. document.Diagnostics.Select(entry => entry.Code)];
        }

        static int Map(int count)
        {
            var numbers = new Dictionary<int, int>();

            for (var number = 4; number < count + 4; number++)
            {
                numbers.Add(number, 1);
            }

            return numbers.Count;
        }
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

        var (first, diagnostics) = ReadObject3OnASmallStack(builder.BuildClassic(rootNumber: 1));

        first.Should().BeOfType<PdfArray>();
        diagnostics.Contains(PdfDiagnosticCodes.SyntaxDepthExceeded).Should().BeTrue();
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

        var (first, diagnostics) = ReadObject3OnASmallStack(builder.BuildClassic(rootNumber: 1));

        first.Should().BeOfType<PdfDictionary>();
        diagnostics.Contains(PdfDiagnosticCodes.SyntaxDepthExceeded).Should().BeTrue();
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
        // refused, placing it in object stream 0, which is no object number —, the object left for a rebuild to find.
        // Decoding 7 asks for 5, which rebuilds the index; the rebuilt index places 5 in 7, which is decoded again, 5 read
        // as null, and kept. The first decoding then keeps 7 in place of the second: its data counts once, as it does in
        // the sound file. A row of a reserved type, which once left 5 out of the index, now reads as the null object
        // (#221).
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
        chars.AsSpan(rows + (5 * 7) + 1, 4).Clear();
        var file = Encoding.Latin1.GetBytes(chars);

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
        // second, a Flate step, from object 5, which 7 holds and the index does not list — its row is refused, placing it in
        // object stream 0, which is no object number —, in an index a row short of its /Size. Decoding 7 asks for 5, which
        // rebuilds the index. 9 names an object the file lacks, so that 7 and 9 both wait for the other streams and are
        // taken in as they were met (#348), 7 while 10 is not yet in the index: the rebuild reads 7 as far as its first
        // filter, which names none yet, and places 5 in it. Reading 5 decodes 7 again inside the first decoding, through /ASCIIHexDecode this time,
        // into fewer bytes than its /First: neither decoding gives anything to keep, and the second to end has nothing to
        // take the place of. A row of a reserved type, which once left 5 out of the index, now reads as the null object
        // (#221).
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
        WriteObject(9, "<< /Type /ObjStm /N 1 /First 5 /Extends 99 0 R /Length 21 >>\nstream\n10 0 /ASCIIHexDecode\n\nendstream");

        var xref = output.Position;
        using var rows = new MemoryStream();
        (int Type, long Field, int Index)[] entries =
        [
            (0, 0, 65535), (1, offsets[1], 0), (1, offsets[2], 0), (0, 0, 0), (0, 0, 0), (2, 0, 0),
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

    [Theory]
    [InlineData("a cross-reference stream alone")]
    [InlineData("a table and the stream its /XRefStm names")]
    public void An_encryption_a_cross_reference_stream_gives_refuses_a_document_rebuilt_as_it_opens(string sections)
    {
        // The file's startxref names nothing, and the rebuild that opening sets off merged only the dictionaries trailer
        // keywords introduce: a cross-reference stream's /Encrypt, the trailer ISO 32000-1 makes its dictionary (7.5.8.2),
        // was lost, and the document opened as unencrypted, handing out what it encrypts. The rebuild merges a stream's
        // trailer keys beside the classic trailers', the newest first (#154).
        const string Encrypt = "<< /Filter /Standard /V 1 /R 2 /O <00> /U <00> /P -4 >>";
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [4] = (1, file.Object(4, Encrypt), 0),
        };
        var stream = file.CrossReferenceStream(7, 8, rows, "/Root 1 0 R /Encrypt 4 0 R /ID [<01> <02>]");

        if (sections != "a cross-reference stream alone")
        {
            file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 8 /Root 1 0 R /XRefStm {stream} >>\n"));
        }

        var text = Encoding.Latin1.GetString(file.Bytes);
        var bytes = Encoding.Latin1.GetBytes(text[..text.LastIndexOf("startxref", StringComparison.Ordinal)] + "startxref\n9\n%%EOF\n");

        FluentActions.Invoking(() => PdfDocument.Open(bytes)).Should().Throw<PdfEncryptedException>();
        using var document = PdfDocument.Open(bytes, new PdfReaderOptions { ThrowOnEncrypted = false });
        document.WasRepaired.Should().BeTrue();
        document.IsEncrypted.Should().BeTrue();
        document.Trailer.ContainsKey(PdfName.ID).Should().BeTrue();
        document.Trailer.ContainsKey(PdfName.Get("W")).Should().BeFalse("a stream's own keys describe the stream, not the document");
    }

    [Theory]
    [InlineData("9 0 R", "", "The trailer's /Encrypt names object 9, which reads as null, not an encryption dictionary; the document is read as unencrypted.")]
    [InlineData("9 0 R", "null", "The trailer's /Encrypt names object 9, which reads as null, not an encryption dictionary; the document is read as unencrypted.")]
    [InlineData("9 0 R", "5", "The trailer's /Encrypt names object 9, which reads as an integer, not an encryption dictionary; the document is read as unencrypted.")]
    [InlineData("/Standard", "", "The trailer's /Encrypt is a name, not an encryption dictionary; the document is read as unencrypted.")]
    [InlineData("5", "", "The trailer's /Encrypt is an integer, not an encryption dictionary; the document is read as unencrypted.")]
    public void An_encrypt_entry_that_gives_no_encryption_dictionary_opens_the_document_unencrypted_and_is_reported(
        string value, string nine, string message)
    {
        // A reference to an object the file lacks, or to one that is null, is null (ISO 32000-1, 7.3.10), and a key given
        // null is absent (7.3.7): qpdf opens such a file as unencrypted. A name or an integer gives nothing to decrypt with,
        // and pdf.js and PDFBox read it so too. Each refused the file under ThrowOnEncrypted, with no diagnostic (#154).
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        if (nine.Length > 0)
        {
            builder.WithObject(9, nine);
        }

        var bytes = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(builder.BuildClassic(rootNumber: 1))
            .Replace("/Root 1 0 R >>", "/Root 1 0 R /Encrypt " + value + " >>", StringComparison.Ordinal));

        using var document = PdfDocument.Open(bytes);

        document.IsEncrypted.Should().BeFalse();
        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.TrailerEncryptInvalid)
            .Which.Message.Should().Be(message);
    }

    [Fact]
    public void An_encryption_a_rebuild_merges_is_in_the_next_trailer_and_not_in_one_taken_before()
    {
        // #237's shape, the older trailer giving /Encrypt: the document opens as unencrypted, and the rebuild reading /Bar sets
        // off merges /Encrypt into the trailer the document gives next; the trailer taken before keeps none (#167). Whether
        // the document is encrypted was decided as it opened, and stays so, the rebuild saying what it merged (#154).
        var template = ConvergenceShapes.TrailerPrevLastTemplate.Replace(
            "/Info << /Title 5 >>", "/Encrypt << /Filter /Standard /V 1 /R 2 /O <00> /U <00> /P -4 >>", StringComparison.Ordinal);
        using var document = PdfDocument.Open(PdfTemplate.Build(template));
        var taken = document.Trailer;
        document.IsEncrypted.Should().BeFalse();

        _ = document.GetObject(new PdfObjectId(4));

        document.WasRepaired.Should().BeTrue();
        taken.ContainsKey(PdfName.Encrypt).Should().BeFalse();
        document.Trailer.ContainsKey(PdfName.Encrypt).Should().BeTrue();
        document.IsEncrypted.Should().BeFalse();
        document.Reader.EncryptFoundAfterOpening.Should().BeTrue();
        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.TrailerEncryptLate);
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
        // Four hundred Flate object streams take their /DecodeParms from object 3, written directly in the file and 100 KB
        // long — as 3 0 R, or each under a generation of its own, which leads the reader to the same object. The walk reads
        // what each stream needs where the chain's index places it, outside the reader's cache: it read object 3 again for
        // every stream, 93 MB in all. It reads it once a validation, and each stream's dictionary through one window (#128).
        // The streams are compressed, so that decoding reads their /DecodeParms, and the walk with it (#277).
        const int Streams = 400;
        var streams = new List<PackedStream>();
        for (var k = 0; k < Streams; k++)
        {
            var parameters = string.Create(CultureInfo.InvariantCulture, $"/DecodeParms 3 {(generations ? k : 0)} R");
            streams.Add(new PackedStream(10 + (2 * k), parameters, [(11 + (2 * k), "<< >>")], PackedData.Flate));
        }

        var file = PackedFile(streams, [(3, "<< /Pad [" + new StringBuilder().Insert(0, "0 ", 50_000) + "] >>")]);
        using var source = new StrictCountingSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var opened = source.BytesRead;

        ObjectStreamDependencies.Run(document).Circular.Should().BeEmpty();

        (source.BytesRead - opened).Should().BeLessThan((Streams * (long)PdfFileReader.InitialObjectWindow) + (4L * file.Length));
    }

    [Theory]
    [InlineData("nest heads past the cache, met in turn, sharing their tails", 48)]
    [InlineData("nests ending at a stream another nest parsed", 112)]
    public void Nests_the_walk_cannot_keep_cost_each_object_stream_alike_whatever_their_number(string shape, int leastKilobytes)
    {
        // #181's residual, under the smallest cache, 64 objects, so that the files stay small. The walk keeps a nest of streams
        // by the stream heading it, as many as the reader caches objects, and only from a parse that met no stream parsed
        // before. Object streams that each meet 4 heads of their own set, 20 sets, more heads than the cache, met in turn and
        // sharing 4 tails of 20 streams, find no nest kept: each parses its heads' 84 streams again. Object streams sharing 8
        // parameters, each heading a nest of 20 whose last /Length names one stream, 9, parse it with the first nest, which is
        // kept: every other nest meets 9, parsed before, and is not kept, so that each walk parses the other 140 streams
        // again. Either costs each object stream what its nests cost, about 64 and 141 KB, about 14 MB under the default
        // cache for nests of 62 and 256 heads: in proportion to the streams, which closing #181 brings down to what each
        // stream's own dictionary costs, about 1 KB. A cache brings the first shape there once its 80 heads fit in it; the
        // second shape's 8 heads fit already, and it allocates about 1 KB only once the reader's cache holds every stream of
        // the nests, while the walk still parses them again, which closing #181 removes. The least each stream costs pins
        // the residual, which closing #181 flips; that it costs no more as the streams grow holds whether #181 is open or
        // closed.
        const int Few = 40;
        const int Many = 160;
        var options = new PdfReaderOptions { ObjectCacheCapacity = 64 };

        var none = Allocations.Least(() => PdfDocument.Open(UncachedNests(shape, Few / 2), options), document => ObjectStreamDependencies.Run(document), out var noneDocument, out _);
        var fewAllocated = Allocations.Least(() => PdfDocument.Open(UncachedNests(shape, Few), options), document => ObjectStreamDependencies.Run(document), out var fewDocument, out var fewResult);
        var manyAllocated = Allocations.Least(() => PdfDocument.Open(UncachedNests(shape, Many), options), document => ObjectStreamDependencies.Run(document), out var manyDocument, out var manyResult);

        using (noneDocument)
        using (fewDocument)
        using (manyDocument)
        {
            fewResult.CircularStreams.Should().BeEmpty();
            manyResult.CircularStreams.Should().BeEmpty();
            var first = (fewAllocated - none) / (Few / 2);
            var then = (manyAllocated - fewAllocated) / (Many - Few);
            then.Should().BeGreaterThan(leastKilobytes * 1024L, "#181 leaves each object stream to parse again the nests the walk cannot keep ({0} B a stream)", then);
            then.Should().BeLessThan(first * 5 / 4, "each object stream costs what its own nests cost, whatever their number ({0} B, then {1} B a stream)", first, then);
        }
    }

    [Theory]
    [InlineData("a parameters dictionary of 100 KB")]
    [InlineData("a chain of 20,000 references")]
    [InlineData("64 filters and parameters, each a chain")]
    [InlineData("64 parameters and their values, each a stream heading a nest of 62")]
    public void The_walk_of_the_object_streams_dependencies_allocates_for_each_stream_no_more_than_its_dictionary_costs(string shared)
    {
        // Object streams share what their /Filter and /DecodeParms lead to: a parameters dictionary of 100 KB; a chain of
        // 20,000 references, longer than the reader's cache; 64 filters and 64 parameters, each a chain of 31 references,
        // the parameters' values chains as well; or 64 parameters and the values their predictor asks for, each a stream
        // heading a nest of 62 streams by /Length, which the parse of each takes to the reader's deepest load: 16,128
        // streams, more than the walk keeps. The walk read what each led to again for every stream, the long chain parsed
        // again for each, 5.6 MB a stream, and each nest of streams, 14 MB (#201). It reads what
        // decoding reads of each object once a validation, takes a chain it has followed at once, and a nest of streams a
        // parse went through: what one more stream costs is its own dictionary.
        const int Few = 250;
        const int Many = 1000;
        var few = SharedByObjectStreams(shared, Few);
        var many = SharedByObjectStreams(shared, Many);

        var fewAllocated = Allocations.Least(() => PdfDocument.Open(few), document => ObjectStreamDependencies.Run(document), out var fewDocument, out var fewResult);
        var manyAllocated = Allocations.Least(() => PdfDocument.Open(many), document => ObjectStreamDependencies.Run(document), out var manyDocument, out var manyResult);

        using (fewDocument)
        using (manyDocument)
        {
            fewResult.Circular.Should().BeEmpty();
            manyResult.Circular.Should().BeEmpty();
            ((manyAllocated - fewAllocated) / (Many - Few)).Should().BeLessThan(1280, "what one more stream costs is its own dictionary");
        }
    }

    [Theory]
    [InlineData(true, 64)]
    [InlineData(false, 0)]
    public void Object_streams_whose_parameters_run_through_chains_of_other_streams_objects_are_walked_at_a_cost_for_each_that_does_not_grow_with_their_number(bool holdersShare, int circular)
    {
        // Object streams share 64 parameters, each the head of a chain of 32 objects, every one held by an object stream of
        // its own. The walk followed every chain through the reader, each stream keeping a need of every holder, 2,048 a
        // stream: 67 KB a stream, a gigabyte for 32,768 streams in a 4.6 MB file, held until the components were worked out
        // (#277). When the holders share the parameters, none decodes alike in every read order: the walk follows no chain,
        // and a stream keeps a need of each of the 64 heads' holders, which need themselves; when the holders need nothing,
        // each decodes alike, and the chains are followed, once a validation, without a need (#312). Either way what one more
        // stream costs is its own walk, and a need at most for each value its decoding reads.
        const int Few = 2500;
        const int Many = 4000;
        var few = MemberChains(Few, holdersShare);
        var many = MemberChains(Many, holdersShare);

        var fewAllocated = Allocations.Least(() => PdfDocument.Open(few), document => ObjectStreamDependencies.Run(document), out var fewDocument, out var fewResult);
        var manyAllocated = Allocations.Least(() => PdfDocument.Open(many), document => ObjectStreamDependencies.Run(document), out var manyDocument, out var manyResult);

        using (fewDocument)
        using (manyDocument)
        {
            fewResult.CircularStreams.Should().HaveCount(circular);
            manyResult.CircularStreams.Should().HaveCount(circular);
            ((manyAllocated - fewAllocated) / (Many - Few)).Should().BeLessThan(4096, "what one more stream costs is its own walk, and a need for each value its decoding reads at most");
        }
    }

    [Theory]
    [InlineData("chain")]
    [InlineData("ring")]
    [InlineData("rho")]
    [InlineData("star")]
    public void Object_streams_that_need_one_another_are_judged_circular_at_a_cost_for_each_that_does_not_grow_with_their_number(string shape)
    {
        // Object streams need one another's objects through their /DecodeParms: in a chain, the last needing none; in a
        // ring; in a rho, the last needing the one halfway; in a star, the first needing every other, which each need it,
        // under a bound on filters that lets it read them all. The walk looked for a way back to each stream with a search
        // from each of its needs, each as long as the streams it reached: 8.6 GB for a chain of 20,000 streams, 21 GB for a
        // ring (#168). It works out the streams that lead to one another once, taking each stream and each need once: what
        // one more stream costs is its own walk, and the reader's decoding of the stream it needs, 4.1 to 5.7 KB.
        const int Few = 500;
        const int Many = 2000;
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxFilterChainLength = Many } };
        var few = ChainedObjectStreams(shape, Few);
        var many = ChainedObjectStreams(shape, Many);

        var fewAllocated = Allocations.Least(() => PdfDocument.Open(few, options), document => ObjectStreamDependencies.Run(document), out var fewDocument, out var fewResult);
        var manyAllocated = Allocations.Least(() => PdfDocument.Open(many, options), document => ObjectStreamDependencies.Run(document), out var manyDocument, out var manyResult);

        using (fewDocument)
        using (manyDocument)
        {
            fewResult.CircularStreams.Should().HaveCount(CircularAmongChainedObjectStreams(shape, Few));
            manyResult.CircularStreams.Should().HaveCount(CircularAmongChainedObjectStreams(shape, Many));
            ((manyAllocated - fewAllocated) / (Many - Few)).Should().BeLessThan(8192, "what one more stream costs is its own walk");
        }
    }

    [Fact]
    public void An_object_stream_whose_standing_waits_on_others_reads_its_dictionary_once_however_often_it_is_walked_again()
    {
        // Object stream 3 takes its parameters from 6, which 5 holds; 5's /Filter names 16, or 64, elements, each a chain of
        // 30 objects held by object streams of their own, needing nothing, and its dictionary carries 64 KB besides. 5's
        // standing is worked out once each holder's is: a walk of 5 reads an object of a holder not known yet as null, which
        // ends its chain of filters, and is made again once that holder is known, 1,921 times for 64 elements. It read 5's
        // dictionary from the file again each time, 147 KB a holder; the walk keeps what it reads of it, a few hundred bytes,
        // once 5 waits, and what one more holder costs is its own walk (#312).
        const int Few = 16;
        const int Many = 64;
        const int Links = 30;

        var fewAllocated = Allocations.Least(() => PdfDocument.Open(HiddenHolders(Few, Links, 64 * 1024)), document => ObjectStreamDependencies.Run(document), out var fewDocument, out var fewResult);
        var manyAllocated = Allocations.Least(() => PdfDocument.Open(HiddenHolders(Many, Links, 64 * 1024)), document => ObjectStreamDependencies.Run(document), out var manyDocument, out var manyResult);

        using (fewDocument)
        using (manyDocument)
        {
            fewResult.CircularStreams.Should().BeEmpty();
            manyResult.CircularStreams.Should().BeEmpty();
            ((manyAllocated - fewAllocated) / ((Many - Few) * Links)).Should().BeLessThan(10 * 1024, "what one more holder costs is its own walk, not a reading of the dictionary that waits on it");
        }
    }

    [Fact]
    public void An_object_stream_whose_standing_waits_on_others_keeps_what_it_reads_of_its_dictionary_once_however_often_it_is_walked_again()
    {
        // Object stream 3 takes its parameters from 6, which 5 holds; 5's /Filter names 64 elements, each a chain of 30
        // objects held by object streams of their own, and its /DecodeParms, a dictionary, gives each of the five entries a
        // step's parameters are read from. 5 is walked 1,921 times, once more for each holder its walks met before it was
        // known. Once 5 waits, the walk keeps what it read of its dictionary, its parameters' five entries among them, and
        // walks it again from what it kept: what 5's parameters cost is reading and keeping them once, 1.9 KB more than a 5
        // that writes no /DecodeParms. Kept again at each walk, they cost 1,921 dictionaries, 938 KB more (#312).
        const string Parameters = "<< /Predictor 1 /Colors 1 /BitsPerComponent 8 /Columns 1 /EarlyChange 1 >>";

        var without = Allocations.Least(() => PdfDocument.Open(HiddenHolders(64, 30, 0)), document => ObjectStreamDependencies.Run(document), out var withoutDocument, out var withoutResult);
        var with = Allocations.Least(() => PdfDocument.Open(HiddenHolders(64, 30, 0, Parameters)), document => ObjectStreamDependencies.Run(document), out var withDocument, out var withResult);

        using (withoutDocument)
        using (withDocument)
        {
            withoutResult.CircularStreams.Should().BeEmpty();
            withResult.CircularStreams.Should().BeEmpty();
            (with - without).Should().BeLessThan(64 * 1024, "what a walk reads of a dictionary that waits is kept once, not once a walk");
        }
    }

    [Fact]
    public void The_walk_keeps_the_dictionaries_of_as_many_waiting_object_streams_as_the_reader_caches_and_reads_again_those_it_let_go()
    {
        // Seventy Flate object streams X1 to X70, numbered 1,000, 1,002 and on, each taking its parameters from the object
        // the next holds, the last from its own: X1's walk meets X2 not known yet, and X2 to X69 each wait on the next, in
        // that order. X70 does not decode alike, nor, walked again in turn, do X69, X68 and on. Under a cache of 64 objects,
        // the walk keeps the dictionaries of the last 64 that waited, X6 to X69, and reads again those of X2 to X5 when it
        // walks them again: each is read three times — by its first walk, its second and the walk that judges it —, X1's
        // once, the others' twice (#312).
        const int Count = 70;
        var file = PackedFile([.. Enumerable.Range(0, Count).Select(x => new PackedStream(
            1000 + (2 * x),
            string.Create(CultureInfo.InvariantCulture, $"/DecodeParms {(x + 1 < Count ? 1003 + (2 * x) : 1001 + (2 * x))} 0 R"),
            [(1001 + (2 * x), "<< /Predictor 1 >>")],
            PackedData.Flate))]);
        var text = Encoding.Latin1.GetString(file);
        using var source = new WindowLoggingSource(file);
        using var document = PdfDocument.Open(source, PdfReaderOptions.Default with { ObjectCacheCapacity = 64 }, ownsSource: false);
        document.Reader.SettleIndex();
        source.Windows.Clear();

        ObjectStreamDependencies.Run(document).CircularStreams.Should().Equal(1138);

        Enumerable.Range(0, Count).Select(x => 1000 + (2 * x))
            .Where(stream => source.Windows.Count(window => window.Offset == text.IndexOf(string.Create(CultureInfo.InvariantCulture, $"\n{stream} 0 obj"), StringComparison.Ordinal) + 1) == 3)
            .Should().Equal([1002, 1004, 1006, 1008], "the dictionaries of the 64 streams that waited last are kept, and no more");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Object_streams_whose_standings_wait_on_one_another_wait_on_a_stack_within_twice_their_number_and_one_walk_s_unknown_streams_whatever_the_walks_that_met_them(bool larger)
    {
        // Object stream X1, which 3 takes its parameters from, takes its /Length from an object X2 holds, and so on, 2,000 or
        // 8,000 of them; each takes its /Filter and /DecodeParms from two objects written in the file, 64 Flate steps whose
        // predictor reads three values, each an object of a holder of its own, 192 holders shared by every X. Each X's first
        // walk meets the next X and the 192 holders, none known yet, and pushes them all on the stack of the streams waiting:
        // 193 entries an X, 1,544,000 for 8,000. Each X waits on the next before any is known: the stack holds every X at
        // once, and the 192 holders, 2,192 or 8,192 streams waiting. A stream pushed again by a later walk leaves its entry
        // below stale; the stack is rid of such entries whenever it holds more than 1,024 and twice as many as it kept the
        // last time, so that it holds no more than twice the most streams waiting at once, or 1,024 entries, whichever is
        // more, besides one walk's 193 (#312): 4,488 entries for 2,000, 16,423 for 8,000.
        var count = larger ? 8000 : 2000;
        using var document = PdfDocument.Open(PendingFanout(count));

        var result = ObjectStreamDependencies.Run(document);

        result.CircularStreams.Should().BeEmpty();
        result.MostWaiting.Should().BeInRange(count, (2 * (count + 192)) + 193, "the stack holds no more than twice the most streams waiting at once, every X and the 192 holders, besides one walk's 193, whatever walks met them");
    }

    [Fact]
    public void A_walk_that_meets_an_object_stream_whose_standing_waits_on_none_holds_that_stream_alone_on_the_stack()
    {
        // Object stream 3 takes its parameters from the object X1 holds; X1 takes an empty chain of filters, and its walk
        // meets no object stream: its standing is worked out at its first walk, the stack holding X1 alone (#312).
        using var document = PdfDocument.Open(PendingFanout(1, steps: 0));

        var result = ObjectStreamDependencies.Run(document);

        result.CircularStreams.Should().BeEmpty();
        result.MostWaiting.Should().Be(1, "the stack holds the stream whose standing is worked out, and nothing it waits on");
    }

    [Fact]
    public void A_walk_of_a_standing_that_meets_several_objects_of_one_object_stream_not_known_yet_waits_on_it_once()
    {
        // Object stream 3 takes its parameters from the object X, 10, holds; X's predictor of 12 reads its /Colors,
        // /BitsPerComponent and /Columns from three objects holder 20 holds, whose standing is not known when X's is first
        // worked out. X's walk meets 20 three times, and waits on it once: the stack holds X and 20. Pushed once for each of
        // its objects met, 20 would stand on the stack three times (#312).
        var file = PackedFile(
        [
            new PackedStream(20, string.Empty, [(21, "1"), (22, "8"), (23, "1")], PackedData.Flate),
            new PackedStream(10, "/Filter /FlateDecode /DecodeParms << /Predictor 12 /Colors 21 0 R /BitsPerComponent 22 0 R /Columns 23 0 R >>", [(11, "5")], PackedData.Plain),
            new PackedStream(3, "/DecodeParms 11 0 R", [(4, "<< >>")], PackedData.Flate),
        ]);
        using var document = PdfDocument.Open(file);

        var result = ObjectStreamDependencies.Run(document);

        result.CircularStreams.Should().BeEmpty();
        result.MostWaiting.Should().Be(2, "X waits on its holder once, however many of the holder's objects its decoding reads");
    }

    [Fact]
    public void A_walk_of_a_standing_meets_every_object_stream_not_known_yet_that_its_decoding_reads_before_it_waits()
    {
        // Object stream 3 takes its parameters from the object X1 holds; X1's 64 Flate steps each take a predictor of 12
        // whose /Colors, /BitsPerComponent and /Columns are each the object of a holder of its own, 192 holders, none known
        // when X1's standing is first worked out. Its walk reads each as null and goes on, as decoding goes on past a value
        // read as null, and waits on every holder it met at once: the stack holds X1 and the 192 holders, each holder is
        // walked, and X1 once more. A walk ended at the first holder not known would wait on one holder at a time, walking X1
        // again for each, 193 walks where two do: the stack would never hold more than X1 and one holder (#312).
        using var document = PdfDocument.Open(PendingFanout(1));

        var result = ObjectStreamDependencies.Run(document);

        result.CircularStreams.Should().BeEmpty();
        result.MostWaiting.Should().Be(1 + (3 * 64), "one walk of X1 meets every holder its decoding reads");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_walk_of_a_standing_that_meets_more_object_streams_not_known_yet_than_the_stack_keeps_before_it_is_compacted_leaves_them_all_waiting()
    {
        // As above, with 400 Flate steps, which a raised MaxFilterChainLength lets the reader take: X1's first walk meets
        // 1,200 holders, none known yet, and pushes them all, 1,201 entries, past the 1,024 the stack holds before it is rid
        // of its stale entries. None is stale: each holder waits to be walked, and the stack keeps them all. Dropped as if
        // known, the holders would be met again by X1's next walk, pushed again past 1,024 and dropped again, and the walks
        // would never end (#312).
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxFilterChainLength = 400 } };
        using var document = PdfDocument.Open(PendingFanout(1, steps: 400), options);

        var result = await Task.Run(() => ObjectStreamDependencies.Run(document), TestContext.Current.CancellationToken);

        result.CircularStreams.Should().BeEmpty();
        result.MostWaiting.Should().Be(1 + (3 * 400), "one walk of X1 meets every holder, and the stack keeps each, which waits to be walked");
    }

    [Theory]
    [InlineData(34, 1054)]
    [InlineData(35, 1055)]
    public void Object_streams_whose_standings_wait_on_one_another_keep_every_entry_of_their_stack_until_it_holds_more_than_1024(int count, int most)
    {
        // Object stream 3 takes its parameters from the object X1 holds, and X1, X2 and on, 34 or 35 of them, each take their
        // /Length from the object the next holds and 10 Flate steps, each step's predictor reading three values, each the
        // object of a holder of its own, 30 holders shared by every X. Each X's first walk pushes the next X and the 30
        // holders, none known yet: 31 entries an X, the last pushing the holders alone. After X33's walk the stack holds
        // 1 + 33 x 31 = 1,024 entries, and keeps them all, stale ones included; X34's walk takes it to 1,054 or 1,055, past
        // 1,024, where it is rid of the stale ones, keeping one entry of each stream: X35's walk then pushes 30 more onto the
        // 65 left (#312).
        using var document = PdfDocument.Open(PendingFanout(count, steps: 10));

        var result = ObjectStreamDependencies.Run(document);

        result.CircularStreams.Should().BeEmpty();
        result.MostWaiting.Should().Be(most, "the stack is rid of its stale entries once it holds more than 1,024, not before");
    }

    [Fact]
    public void Object_streams_whose_standings_wait_on_one_another_are_rid_of_their_stack_s_stale_entries_as_soon_as_it_holds_1025()
    {
        // As above, with 65 X's and 5 Flate steps, 15 holders: each X's first walk pushes the next X and the 15 holders, 16
        // entries, the last X the holders alone. After X64's walk the stack holds 1 + 64 x 16 = 1,025 entries, one past
        // 1,024, and is rid of its stale ones there, keeping one entry of each stream, 80: X65's walk then pushes its 15 onto
        // them. Kept until it held more than 1,025, the stack would have taken those 15 as well, 1,040 entries (#312).
        using var document = PdfDocument.Open(PendingFanout(65, steps: 5));

        var result = ObjectStreamDependencies.Run(document);

        result.CircularStreams.Should().BeEmpty();
        result.MostWaiting.Should().Be(1025, "the stack is rid of its stale entries as soon as it holds more than 1,024");
    }

    [Fact]
    public void Object_streams_whose_standings_wait_on_one_another_let_their_stack_grow_to_twice_what_it_kept_before_it_is_rid_of_its_stale_entries_again()
    {
        // As above, with 800 X's and 2 Flate steps, 6 holders: each X's first walk pushes the next X and the 6 holders, 7
        // entries. Past 1,024 entries the stack is rid of its stale ones, keeping one entry of each X met and of each
        // holder, all waiting; once it kept more than 512, it is rid of them again only once it holds more than twice as
        // many: it holds 1,281 entries at most. Rid of them whenever it held more than 1,024, it would never have held more
        // than 1,031, and been swept the more often the more it kept, for each X's walk once it kept more than 1,017 (#312).
        using var document = PdfDocument.Open(PendingFanout(800, steps: 2));

        var result = ObjectStreamDependencies.Run(document);

        result.CircularStreams.Should().BeEmpty();
        result.MostWaiting.Should().Be(1281, "the stack is rid of its stale entries once it holds more than 1,024 and twice what it kept the last time");
    }

    [Fact]
    public void The_object_stream_whose_standing_the_stack_of_waiting_standings_was_started_for_stays_on_it_when_its_stale_entries_go()
    {
        // Object stream 3 takes its parameters from the object X1 holds, which names object 4, held by 3; X1 and 19 more X's
        // take their /Length from the next X's object and 17 Flate steps whose predictors read the objects of 51 holders.
        // X1, met first, waits at the bottom of the stack while the others are worked out; the stack passes 1,024 entries,
        // 1 + 19 x 52 + 51 = 1,040, and is rid of its stale ones. X1, not known yet, keeps its entry: once the others are
        // known it is walked again, decodes alike, and 3's decoding reads its object, which leads back to 3. Left out, X1
        // would stay a stream being worked out, its object read as null by 3, which would need nothing it holds (#312).
        using var document = PdfDocument.Open(PendingFanout(20, steps: 17, first: "4 0 R"));

        var result = ObjectStreamDependencies.Run(document);

        result.MostWaiting.Should().Be(1040);
        result.CircularStreams.Should().Equal([3], "3 reads the object of X1, which decodes alike, and is led back to its own object 4");
    }

    [Theory]
    [InlineData(20)]
    [InlineData(80)]
    public void Object_streams_whose_standings_wait_level_after_level_are_rid_of_every_entry_of_a_known_stream_once_their_stack_holds_more_than_1024(int levels)
    {
        // Object stream 3 takes its parameters from the object X1 holds; X1, X2 and on, 20 or 80 levels, each take their
        // /Length from an object of a stream of their own, naming the next X's object, and 64 Flate steps, each step's
        // predictor the object of a holder of the level before, its /BitsPerComponent that of a holder of its own level.
        // Xi's first walk meets the holders of the level before, which Xi-1's second walk pushed below Xi, and pushes them
        // again above it, where they are worked out; its second walk pushes Xi+1 and the 64 holders of its level. Each level
        // leaves 64 entries of streams known below the X's that wait, the only entry each of those streams has left. At most
        // 144 streams wait at once, the X's and one level's holders, so that the stack holds no more than 1,024 entries,
        // besides one walk's 65: it is rid of every entry of a known stream, rather than keeping entries that grow with every
        // stream it ever held, 1,236 for 20 levels, of 1,383 streams it held, and 5,008 for 80, of 5,343 (#312).
        const int Steps = 64;
        using var document = PdfDocument.Open(LayeredWaits(levels, Steps));

        var result = ObjectStreamDependencies.Run(document);

        result.CircularStreams.Should().BeEmpty();
        result.MostWaiting.Should().BeInRange(1025, 1024 + Steps + 1, "the stack, once past 1,024 entries, keeps an entry of each stream waiting alone, whatever the levels before");
    }

    [Fact]
    public void Object_streams_whose_standings_wait_on_one_another_drop_the_entries_later_walks_left_stale_rather_than_walking_them_again()
    {
        // Object stream 3 takes its parameters from the object X1 holds, and X1, X2 and on, one or five of them, each take
        // their /Length from the object the next holds and 64 Flate steps whose predictor reads three values, each the object
        // of a holder of its own, 192 holders shared by every X. Each X's first walk pushes the 192 holders again, none known
        // yet; below 1,024 entries the stack keeps every entry, so that the holders' entries an earlier X pushed are stale
        // when they are reached, the holders known by then: 768 for five X's. A stale entry is dropped, and what one more X
        // costs is its own walks, 17.7 KB; walked again, each stale entry's holder read its dictionary from the file once
        // more, 176 KB an X (#312).
        const int Few = 1;
        const int Many = 5;

        var fewAllocated = Allocations.Least(() => PdfDocument.Open(PendingFanout(Few)), document => ObjectStreamDependencies.Run(document), out var fewDocument, out var fewResult);
        var manyAllocated = Allocations.Least(() => PdfDocument.Open(PendingFanout(Many)), document => ObjectStreamDependencies.Run(document), out var manyDocument, out var manyResult);

        using (fewDocument)
        using (manyDocument)
        {
            fewResult.CircularStreams.Should().BeEmpty();
            manyResult.CircularStreams.Should().BeEmpty();
            ((manyAllocated - fewAllocated) / (Many - Few)).Should().BeLessThan(32 * 1024, "what one more stream that waits costs is its own walks, not walks of the holders it waits on again");
        }
    }

    [Fact]
    public void Object_streams_whose_standings_wait_on_one_another_drop_the_stale_entries_of_streams_that_do_not_decode_alike_rather_than_walking_them_again()
    {
        // As above, each of the 192 holders taking its /Length from the object it holds: its walk meets that object while
        // the reader holds the holder in its loads, and its standing is unlike. The holders' entries an earlier X pushed are
        // stale when they are reached, the holders known by then, 768 for five X's, and are dropped as those of streams that
        // decode alike are: what one more X costs stays its own walks and the needs they find, 41.5 KB. Taken for streams
        // still being worked out, each would be walked again, its dictionary read from the file once more, 202 KB an X
        // (#312).
        const int Few = 1;
        const int Many = 5;

        var fewAllocated = Allocations.Least(() => PdfDocument.Open(PendingFanout(Few, selfLengthHolders: true)), document => ObjectStreamDependencies.Run(document), out var fewDocument, out var fewResult);
        var manyAllocated = Allocations.Least(() => PdfDocument.Open(PendingFanout(Many, selfLengthHolders: true)), document => ObjectStreamDependencies.Run(document), out var manyDocument, out var manyResult);

        using (fewDocument)
        using (manyDocument)
        {
            fewResult.CircularStreams.Should().HaveCount(192, "each holder needs the object it holds");
            manyResult.CircularStreams.Should().HaveCount(192, "each holder needs the object it holds");
            ((manyAllocated - fewAllocated) / (Many - Few)).Should().BeLessThan(96 * 1024, "what one more stream that waits costs is its own walks, not walks of the holders it waits on again");
        }
    }

    [Fact]
    public void A_walk_of_a_standing_takes_at_once_a_nest_whose_parse_took_runs_and_nests_the_reader_s_index_places_as_the_chain_s()
    {
        // Object stream 3, walked first, takes its parameters from stream 200, whose /Length names stream 201: the walk keeps
        // the nest of 200. Stream 100 heads 40 streams by /Length, each naming the next through a link written between them,
        // a run the walk takes at once, the last naming 200, whose nest the walk takes at once as well: the walk keeps the
        // nest of 100, which 11 takes its parameters from. 100's parameters name 51, which 9 holds, so that 9's standing is
        // worked out there; 9 takes its parameters from 100 as well, or from a dictionary written in place, and does not
        // decode alike. Under a cache of 64 objects, fewer than the nest's 81, 9's walk of a standing takes the nest at once,
        // the reader's index placing every link of it as the chain's does: reading 100 costs 9's walk 5.4 KB, 100's
        // dictionary read again. Taken for a nest the index does not agree with, it was parsed again, its streams, 1 KB each,
        // read from the file once more: 100 KB (#312).
        var options = new PdfReaderOptions { ObjectCacheCapacity = 64 };

        var inPlaceAllocated = Allocations.Least(() => PdfDocument.Open(NestReadAgain(false), options), document => ObjectStreamDependencies.Run(document), out var inPlaceDocument, out var inPlaceResult);
        var nestAllocated = Allocations.Least(() => PdfDocument.Open(NestReadAgain(true), options), document => ObjectStreamDependencies.Run(document), out var nestDocument, out var nestResult);

        using (inPlaceDocument)
        using (nestDocument)
        {
            inPlaceResult.CircularStreams.Should().Equal([9]);
            nestResult.CircularStreams.Should().Equal([9]);
            (nestAllocated - inPlaceAllocated).Should().BeLessThan(16 * 1024, "a walk of a standing takes at once a nest whose links and nests the reader's index places as the chain's");
        }

        // Stream 100, padded, and the 39 after it by /Length, each through a link, the last naming 200; 9 reads 100 when
        // `nest` says so.
        static byte[] NestReadAgain(bool nest)
        {
            var parameters = "/Predictor 12 /Colors 51 0 R";
            var objects = new SortedDictionary<int, (string Entries, byte[]? Data)>
            {
                [3] = ConvergenceShapes.FlateObjectStream("/DecodeParms 200 0 R", (4, "<< >>")),
                [9] = ConvergenceShapes.FlateObjectStream(nest ? "/DecodeParms 100 0 R" : $"/DecodeParms << {parameters} >>", (51, "1")),
                [11] = ConvergenceShapes.FlateObjectStream("/DecodeParms 100 0 R", (12, "<< >>")),
                [200] = ("/Length 201 0 R", "ab"u8.ToArray()),
                [201] = ("/Length 2", "ab"u8.ToArray()),
            };

            for (var stream = 0; stream < 40; stream++)
            {
                var number = 100 + (2 * stream);
                var length = stream + 1 < 40 ? number + 1 : 200;
                objects[number] = (string.Create(CultureInfo.InvariantCulture, $"{(stream == 0 ? parameters : string.Empty)} /Pad ({new string('a', 1024)}) /Length {length} 0 R"), "ab"u8.ToArray());

                if (stream + 1 < 40)
                {
                    objects[number + 1] = (string.Create(CultureInfo.InvariantCulture, $"{number + 2} 0 R"), null);
                }
            }

            return ConvergenceShapes.StreamsAndMembers(objects, new() { [4] = (3, 0), [12] = (11, 0), [51] = (9, 0) });
        }
    }

    [Fact]
    public void A_value_decoding_reads_again_is_followed_once_however_many_objects_the_walk_let_go_of_meanwhile()
    {
        // An object stream takes 64 Flate steps, each its parameters from an object written in the file, 16 KB each, whose
        // predictor is another object written in the file, under a cache of 64 objects: the last step names again the first
        // step's parameters, after 124 other objects the walk let go of them for, or those of the step just before it. The
        // reader keeps what it loaded, and a value read again loads nothing more: the walk follows each value once, and the
        // two cost the same. Followed again, the first step's parameters were read from the file once more (#277).
        const int Padding = 16 * 1024;
        var options = new PdfReaderOptions { ObjectCacheCapacity = 64 };

        var nearAllocated = Allocations.Least(() => PdfDocument.Open(NamingAgain(162), options), document => ObjectStreamDependencies.Run(document), out var nearDocument, out var nearResult);
        var farAllocated = Allocations.Least(() => PdfDocument.Open(NamingAgain(100), options), document => ObjectStreamDependencies.Run(document), out var farDocument, out var farResult);

        using (nearDocument)
        using (farDocument)
        {
            nearResult.CircularStreams.Should().BeEmpty();
            farResult.CircularStreams.Should().BeEmpty();
            (farAllocated - nearAllocated).Should().BeLessThan(Padding / 2, "a value read again is followed once, however long ago it was read");
        }

        // Objects 100 to 162 hold the first 63 steps' parameters, 200 to 262 their predictors, and the last step names
        // `again`'s once more.
        static byte[] NamingAgain(int again)
        {
            var builder = new TestPdfBuilder()
                .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
                .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
                .WithObject(5, "<< /Title (five) >>")
                .WithObject(6, "<< /Title (six) >>");
            var parameters = new StringBuilder("[");

            for (var number = 100; number <= 162; number++)
            {
                builder.WithObject(number, string.Create(CultureInfo.InvariantCulture, $"<< /Predictor {number + 100} 0 R /Pad ({new string('a', Padding)}) >>"));
                builder.WithObject(number + 100, "1");
                parameters.Append(CultureInfo.InvariantCulture, $"{number} 0 R ");
            }

            parameters.Append(CultureInfo.InvariantCulture, $"{again} 0 R]");
            var filters = "[" + string.Join(' ', Enumerable.Repeat("/FlateDecode", 64)) + "]";
            return builder.BuildWithXRefStream(rootNumber: 1, compressedObjects: [5, 6], objectStreamEntries: $"/Filter {filters} /DecodeParms {parameters}");
        }
    }

    [Fact]
    public void A_walk_of_a_standing_ends_at_the_first_thing_that_tells_its_object_stream_does_not_decode_alike()
    {
        // Object streams each take their parameters from the object a holder of their own holds, whose /Length is that
        // object: decoding the holder reads its own object while the reader holds it in its loads, and its standing is unlike
        // at its first load. Each holder takes 64 Flate steps, whose parameters are objects written in the file, 1 KB each,
        // under a cache of 64 objects, fewer than the holders' parameters. A walk of a standing asks only whether the stream
        // decodes alike, and ends there, reading none of the parameters, which the walk that judges the holder reads once:
        // 237 KB a holder. One that went on read each holder's 64 parameters as well, let go before the walk that judges
        // read them again: 347 KB a holder (#312).
        const int Few = 10;
        const int Many = 40;
        var options = new PdfReaderOptions { ObjectCacheCapacity = 64 };

        var fewAllocated = Allocations.Least(() => PdfDocument.Open(HoldersUnlikeAtTheirLength(Few, 1024), options), document => ObjectStreamDependencies.Run(document), out var fewDocument, out var fewResult);
        var manyAllocated = Allocations.Least(() => PdfDocument.Open(HoldersUnlikeAtTheirLength(Many, 1024), options), document => ObjectStreamDependencies.Run(document), out var manyDocument, out var manyResult);

        using (fewDocument)
        using (manyDocument)
        {
            fewResult.CircularStreams.Should().HaveCount(Few);
            manyResult.CircularStreams.Should().HaveCount(Many);
            ((manyAllocated - fewAllocated) / (Many - Few)).Should().BeLessThan(288 * 1024, "a walk of a standing reads nothing past what tells it the stream does not decode alike");
        }
    }

    [Fact]
    public void Values_that_are_no_streams_take_no_place_among_the_nests_the_walk_keeps()
    {
        // Object stream 3, walked first, takes its parameters from stream 100, which heads 40 streams by /Length, 1 KB each,
        // each naming the next through a link: the walk keeps the nest of 100. 5 takes 64 Flate steps, each step's
        // parameters a dictionary written in the file; 9 takes its parameters from 100 as well, or from a dictionary written
        // in place. Under a cache of 64 objects and as many nests, the 64 dictionaries 5's decoding reads, none a stream,
        // give no nest, and 9's walk takes 100's at once: reading 100 costs it 100's dictionary alone. Parsed as streams are,
        // each would have given a nest of its own, the last 64 of them let go of 100's, and 9's walk parsed 100's nest again,
        // its streams read from the file once more (#201).
        var options = new PdfReaderOptions { ObjectCacheCapacity = 64 };

        var inPlaceAllocated = Allocations.Least(() => PdfDocument.Open(AfterValues(false), options), document => ObjectStreamDependencies.Run(document), out var inPlaceDocument, out var inPlaceResult);
        var nestAllocated = Allocations.Least(() => PdfDocument.Open(AfterValues(true), options), document => ObjectStreamDependencies.Run(document), out var nestDocument, out var nestResult);

        using (inPlaceDocument)
        using (nestDocument)
        {
            inPlaceResult.CircularStreams.Should().BeEmpty();
            nestResult.CircularStreams.Should().BeEmpty();
            (nestAllocated - inPlaceAllocated).Should().BeLessThan(16 * 1024, "values that are no streams leave the nest of 100 kept");
        }

        // Stream 100, padded, and the 39 after it by /Length, each through a link; 9 reads 100 when `nest` says so.
        static byte[] AfterValues(bool nest)
        {
            var (five, first) = ConvergenceShapes.ObjectStreamData((6, "<< >>"));
            var filters = "[" + string.Join(' ', Enumerable.Repeat("/FlateDecode", 64)) + "]";
            var parameters = "[" + string.Join(' ', Enumerable.Range(300, 64).Select(number => string.Create(CultureInfo.InvariantCulture, $"{number} 0 R"))) + "]";
            var objects = new SortedDictionary<int, (string Entries, byte[]? Data)>
            {
                [3] = ConvergenceShapes.FlateObjectStream("/DecodeParms 100 0 R", (4, "<< >>")),
                [5] = (string.Create(CultureInfo.InvariantCulture, $"/Type /ObjStm /N 1 /First {first} /Length @L /Filter {filters} /DecodeParms {parameters}"), Encoding.Latin1.GetBytes(five)),
                [9] = ConvergenceShapes.FlateObjectStream(nest ? "/DecodeParms 100 0 R" : "/DecodeParms << /Predictor 1 >>", (10, "<< >>")),
            };

            for (var stream = 0; stream < 40; stream++)
            {
                var number = 100 + (2 * stream);
                var length = stream + 1 < 40 ? string.Create(CultureInfo.InvariantCulture, $"{number + 1} 0 R") : "2";
                objects[number] = ($"/Pad ({new string('a', 1024)}) /Length {length}", "ab"u8.ToArray());

                if (stream + 1 < 40)
                {
                    objects[number + 1] = (string.Create(CultureInfo.InvariantCulture, $"{number + 2} 0 R"), null);
                }
            }

            for (var value = 0; value < 64; value++)
            {
                objects[300 + value] = ("<< /Predictor 1 >>", null);
            }

            return ConvergenceShapes.StreamsAndMembers(objects, new() { [4] = (3, 0), [6] = (5, 0), [10] = (9, 0) });
        }
    }

    [Fact]
    public void An_object_stream_keeps_one_need_of_each_stream_however_many_of_that_stream_s_objects_its_decoding_reads()
    {
        // Sixteen object streams take 64 Flate steps, each step's parameters an object holder 20 holds, 64 distinct ones or
        // the same one each time; 20 takes its /Length from the first of them, and does not decode alike. Each stream needs
        // 20 once, and keeps the first of its needs of 20: the distinct objects cost the walk 7.6 KB more, what it notes of
        // the values it read. Kept whole, each stream's 64 needs of 20 cost 2 KB more a stream (#277).
        var distinctAllocated = Allocations.Least(() => PdfDocument.Open(Holding(distinct: true)), document => ObjectStreamDependencies.Run(document), out var distinctDocument, out var distinctResult);
        var sameAllocated = Allocations.Least(() => PdfDocument.Open(Holding(distinct: false)), document => ObjectStreamDependencies.Run(document), out var sameDocument, out var sameResult);

        using (distinctDocument)
        using (sameDocument)
        {
            distinctResult.CircularStreams.Should().Equal([20]);
            sameResult.CircularStreams.Should().Equal([20]);
            (distinctAllocated - sameAllocated).Should().BeLessThan(16 * 1024, "each stream keeps one need of 20, however many of its objects it reads");
        }

        // Holder 20, holding 21 to 84, and the sixteen streams taking their parameters from them, or from 21 alone.
        static byte[] Holding(bool distinct)
        {
            var filters = "[" + string.Join(' ', Enumerable.Repeat("/FlateDecode", 64)) + "]";
            var parameters = "[" + string.Join(' ', Enumerable.Range(21, 64).Select(number => string.Create(CultureInfo.InvariantCulture, $"{(distinct ? number : 21)} 0 R"))) + "]";
            var packed = new List<PackedStream>
            {
                new(20, string.Empty, [.. Enumerable.Range(21, 64).Select(number => (number, "<< /Predictor 1 >>"))], PackedData.Flate, Length: "21 0 R"),
            };

            for (var stream = 0; stream < 16; stream++)
            {
                packed.Add(new PackedStream(100 + (2 * stream), $"/Filter {filters} /DecodeParms {parameters}", [(101 + (2 * stream), "<< >>")], PackedData.Plain));
            }

            return PackedFile(packed);
        }
    }

    [Fact]
    public void Object_streams_whose_parameters_head_a_nest_a_rebuild_places_otherwise_take_it_at_once_after_the_first_walk()
    {
        // Object streams 10, 12 and on, 4 or 16 of them, each take their parameters from stream 300, which heads 60 streams
        // by /Length, each naming the next through a link, every one of them written twice; a row leading nowhere makes the
        // validation rebuild the index, which places each at its second copy, where the chain's row places its first. The
        // walk that judges the first object stream parses the nest through the chain's index, and keeps it; each walk that
        // judges after it takes the nest at once, however the rebuilt index places it. Under a cache of 64 objects, fewer
        // than the nest's 119, one more object stream costs its own walk, 1.4 KB; parsing the nest again, each read its
        // streams, 1 KB each, from the file once more, 206 KB (#312).
        const int Few = 4;
        const int Many = 16;

        var fewAllocated = Allocations.Least(() => Settled(Rewritten(Few)), document => ObjectStreamDependencies.Run(document), out var fewDocument, out var fewResult);
        var manyAllocated = Allocations.Least(() => Settled(Rewritten(Many)), document => ObjectStreamDependencies.Run(document), out var manyDocument, out var manyResult);

        using (fewDocument)
        using (manyDocument)
        {
            fewDocument.WasRepaired.Should().BeTrue();
            fewResult.CircularStreams.Should().BeEmpty();
            manyResult.CircularStreams.Should().BeEmpty();
            ((manyAllocated - fewAllocated) / (Many - Few)).Should().BeLessThan(32 * 1024, "the walk that judges takes a nest it kept at once, however a rebuilt index places it");
        }

        static PdfDocument Settled(byte[] file)
        {
            var document = PdfDocument.Open(file, new PdfReaderOptions { ObjectCacheCapacity = 64 });
            document.Reader.SettleIndex();
            return document;
        }

        // The nest of 300, written twice, and `count` object streams taking their parameters from it.
        static byte[] Rewritten(int count)
        {
            var packed = new List<PackedStream>();
            var direct = new List<(int Number, string Body)>();

            for (var stream = 0; stream < 60; stream++)
            {
                var number = 300 + (2 * stream);
                var length = stream + 1 < 60 ? number + 1 : 2;
                direct.Add((number, string.Create(CultureInfo.InvariantCulture, $"<< /Pad ({new string('a', 1024)}) /Length {length}{(stream + 1 < 60 ? " 0 R" : string.Empty)} >>\nstream\nab\nendstream")));

                if (stream + 1 < 60)
                {
                    direct.Add((number + 1, string.Create(CultureInfo.InvariantCulture, $"{number + 2} 0 R")));
                }
            }

            for (var stream = 0; stream < count; stream++)
            {
                packed.Add(new PackedStream(10 + (2 * stream), "/DecodeParms 300 0 R", [(11 + (2 * stream), "<< >>")], PackedData.Flate));
            }

            return PackedFile(packed, direct, brokenRow: 7, twice: true);
        }
    }

    [Fact]
    public void Object_streams_whose_holders_a_rebuild_left_in_place_are_walked_through_the_runs_and_nests_their_walks_kept()
    {
        // Object stream 3 takes its parameters from 6, which 5 holds; 5's /DecodeParms names 16 streams, each heading a nest
        // of 30 streams whose /Length is a chain of 8, or 32, objects held by object streams of their own, the last naming
        // the next stream of the nest; a row leading nowhere makes the validation rebuild the index, which places them all as
        // the chain's does. 5's standing waits on each holder in turn, 1,889 walks at 32 links. A walk of a standing after a
        // rebuild took no run, asking the reader's index of each object it loads: each walk loaded every link the ones before
        // it had found again, 30 KB a hidden holder at 32 links. It takes a run, or a nest, where the reader's index places
        // every object of it as the chain's does, and what one more hidden holder costs stays its own walk (#312).
        const int Few = 8;
        const int Many = 32;
        const int Holders = 16 * 29;

        var fewAllocated = Allocations.Least(() => Settled(HiddenNests(16, 30, Few)), document => ObjectStreamDependencies.Run(document), out var fewDocument, out var fewResult);
        var manyAllocated = Allocations.Least(() => Settled(HiddenNests(16, 30, Many)), document => ObjectStreamDependencies.Run(document), out var manyDocument, out var manyResult);

        using (fewDocument)
        using (manyDocument)
        {
            fewDocument.WasRepaired.Should().BeTrue();
            fewResult.CircularStreams.Should().BeEmpty();
            manyResult.CircularStreams.Should().BeEmpty();
            ((manyAllocated - fewAllocated) / ((Many - Few) * Holders)).Should().BeLessThan(8 * 1024, "a walk after a rebuild takes the runs and nests the reader's index agrees with");
        }

        static PdfDocument Settled(byte[] file)
        {
            var document = PdfDocument.Open(file);
            document.Reader.SettleIndex();
            return document;
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
        // The section is probed through a few bytes, which spaces and "xref" fill, but the token is "xrefs". The probe reads
        // on past a token its edge reaches, finds no keyword there, nor an object, and the index is rebuilt (#174). A
        // MaxTrailerLength that ends right after "xref" would not cut the token there either: the probe reads XRefProbeLength
        // bytes past a trailer's window, far enough to read "xrefs" whole (#289).
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
            $"The validator stopped reading object headers where entries place them once it had read twice the file's length of them, past the first bytes of each: the entries of {Rows - probe.Broken.Total} objects it had not decided by then, the first object {3 + probe.Broken.Total}'s, were not checked."));
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
            return Allocations.Least(() => CrossReferenceProbe.Run(document).Broken.Total, out _);
        }

        var thousand = Probing(1_000);
        var fourThousand = Probing(4_000);

        (fourThousand - thousand).Should().BeLessThanOrEqualTo(3_000 * 4 + 64);
    }

    [Theory]
    [InlineData("outside the file")]
    [InlineData("at object 1's header")]
    public void Probing_millions_of_broken_entries_writes_the_findings_the_report_has_room_for_and_counts_the_rest(string shape)
    {
        // #168: a cross-reference stream of 20 KB whose two million rows place their objects outside the file, or a table
        // whose rows all place theirs where object 1's header is. The probe wrote each entry's finding, and kept it, for a
        // report that keeps 1,000: 223 to 339 bytes held an entry, 445 MB for the stream. It writes the first findings of
        // each kind the report has room for, and counts the rest: what one more entry costs is its number, copied to probe
        // the entries in order.
        const int Room = 1000;
        var (fewAllocated, few) = Probing(1_000_000);
        var (manyAllocated, many) = Probing(2_000_000);

        few.Broken.Total.Should().Be(1_000_000);
        many.Broken.Total.Should().Be(2_000_000);
        many.Broken.Kept.Should().Be(Room);
        many.Broken.Should().HaveCount(Room).And.OnlyContain(finding => finding.Message.StartsWith("The entry of object ", StringComparison.Ordinal));
        ((manyAllocated - fewAllocated) / 1_000_000.0).Should().BeLessThan(5, "one more entry costs its number, four bytes");

        (long Allocated, CrossReferenceProbe Probe) Probing(int entries)
        {
            using var document = PdfDocument.Open(shape == "outside the file" ? EntriesOutsideTheFile(entries) : EntriesAtObjectOne(entries));
            document.Reader.SettleIndex();
            var allocated = Allocations.Least(() => CrossReferenceProbe.Run(document, Room), out var probe);
            return (allocated, probe);
        }
    }

    [Fact]
    public void Probing_millions_of_members_an_object_stream_does_not_list_writes_the_findings_the_report_has_room_for()
    {
        // #168: an object stream that lists one object, where the index places two million: each other member's finding was
        // written and kept, 290 bytes a member, 579 MB in all. The probe writes those the report has room for and counts
        // the rest: what one more member costs is its number, copied, and its place in its stream, gathered to check the
        // stream's members together.
        const int Room = 1000;
        var (fewAllocated, few) = Probing(1_000_000);
        var (manyAllocated, many) = Probing(2_000_000);

        few.Broken.Total.Should().Be(1_000_000 - 2);
        many.Broken.Total.Should().Be(2_000_000 - 2, "every member but object 3, which the stream lists, and the stream itself");
        many.Broken.Kept.Should().Be(Room);
        ((manyAllocated - fewAllocated) / 1_000_000.0).Should().BeLessThan(24, "one more member costs its number and its place");

        static (long Allocated, CrossReferenceProbe Probe) Probing(int entries)
        {
            using var document = PdfDocument.Open(MembersTheHeaderDoesNotList(entries));
            document.Reader.SettleIndex();
            var allocated = Allocations.Least(() => CrossReferenceProbe.Run(document, Room), out var probe);
            return (allocated, probe);
        }
    }

    [Fact]
    public void Walking_a_node_of_millions_of_invalid_kids_writes_the_findings_the_report_has_room_for_and_counts_the_rest()
    {
        // #168: a node whose /Kids is two million numbers, each a kid that is neither a page nor a node. The walk wrote each
        // kid's finding, 800 bytes, and kept it, 340 bytes, for a report that keeps 1,000: seven million kids held 2.4 GB.
        // It writes the first findings the report has room for, and counts the rest: once the node is read, walking its
        // kids costs what walking any tree does, whatever their number.
        const int Room = 1000;
        var (fewAllocated, few) = Walking(1_000_000);
        var (manyAllocated, many) = Walking(2_000_000);

        few.InvalidKids.Total.Should().Be(1_000_000);
        many.InvalidKids.Total.Should().Be(2_000_000);
        many.InvalidKids.Kept.Should().Be(Room);
        many.PageCount.Should().Be(2_000_000, "each invalid kid takes a page's place, counted whether its finding is written or not");
        manyAllocated.Should().BeLessThan(2 * 1024 * 1024, "the findings the report has room for, and the walk's own frame");
        (manyAllocated - fewAllocated).Should().BeLessThan(128 * 1024, "a million kids more cost nothing written");

        static (long Allocated, PageTreeWalk Walk) Walking(int kids)
        {
            using var document = PdfDocument.Open(Kids(kids));
            document.GetObject(new PdfObjectId(2));
            var allocated = Allocations.Least(() => PageTreeWalk.Run(document, Room), out var walk);
            return (allocated, walk);
        }
    }

    [Fact]
    public void Walking_pages_written_in_their_node_s_array_writes_no_path_nor_finding_past_the_report_s_room()
    {
        // #168: a node listing two hundred thousand pages written in its array, each with neither a media box nor resources:
        // three findings a page, each written, its path, /Kids[12], written whether or not a finding named it, 3.2 KB a
        // page. The walk writes a kid's path for a finding only, and the findings the report has room for.
        const int Room = 1000;
        var (fewAllocated, few) = Walking(100_000);
        var (manyAllocated, many) = Walking(200_000);

        few.MediaBoxFaults.Total.Should().Be(100_000);
        many.InvalidKids.Total.Should().Be(200_000);
        many.MediaBoxFaults.Total.Should().Be(200_000);
        many.ResourcesMissing.Total.Should().Be(200_000);
        many.MediaBoxFaults.Kept.Should().Be(Room);
        many.MediaBoxFaults.First().Message.Should().Be(
            "The page object under /Kids[0] of page tree node 2 has no /MediaBox, and no page tree node above it gives one: the page's size is unknown.");
        ((manyAllocated - fewAllocated) / 100_000.0).Should().BeLessThan(4, "one more page costs no string");

        static (long Allocated, PageTreeWalk Walk) Walking(int kids)
        {
            using var document = PdfDocument.Open(KidsWrittenInTheArray(kids));
            document.GetObject(new PdfObjectId(2));
            var allocated = Allocations.Least(() => PageTreeWalk.Run(document, Room), out var walk);
            return (allocated, walk);
        }
    }

    [Fact]
    public void Walking_objects_that_refer_to_nothing_describes_those_the_report_has_room_for_and_reads_no_other_again()
    {
        // #168: fifty thousand objects, each referring to an object the file lacks and holding a name with a null character.
        // The graph read each object again to describe where its first fault lies, and wrote its finding, 1.5 KB an object
        // past what walking it costs. It describes those the report has room for, and counts the others: one more object
        // costs its own walk, which parses it, the reader's cache holding fewer.
        const int Room = 1000;
        var (fewAllocated, few) = Walking(25_000);
        var (manyAllocated, many) = Walking(50_000);

        few.MissingReferences.Total.Should().Be(25_000);
        many.MissingReferences.Total.Should().Be(50_000);
        many.NullCharacterNames.Total.Should().Be(50_000);
        many.MissingReferences.Kept.Should().Be(Room);
        many.NullCharacterNames.Kept.Should().Be(Room);
        ((manyAllocated - fewAllocated) / 25_000.0).Should().BeLessThan(800, "one more object costs its walk, not its description");

        static (long Allocated, ObjectGraph Graph) Walking(int objects)
        {
            using var document = PdfDocument.Open(ObjectsHolding(objects));
            var pages = PageTreeWalk.Run(document);
            var allocated = Allocations.Least(() => ObjectGraph.Run(document, pages, Room), out var graph);
            return (allocated, graph);
        }
    }

    [Fact]
    public void Validating_a_file_of_845_bytes_indexing_millions_of_broken_entries_holds_no_finding_past_the_report_s_room()
    {
        // #164's cross-reference stream of one-byte rows, under the default limits: 845 bytes that decode to 256 MB of rows,
        // of which the index holds the 8,388,605 its guard allows, every one past object 3 placing its object nowhere. The
        // probe wrote and kept each entry's finding: 5.5 GB allocated, 3.3 GB held, for a report of 1,000. Once the index is
        // settled — what opening and settling hold, 640 MB, is the index's, which the guard bounds —, validating costs the
        // probe's copy of the entries' numbers and little else. Its time is not held here: the probe places the header of
        // each of the 8,388,605 entries, some 2.5 to 3 s alone and 5 to 6 s under coverage, the thread's own work, which
        // grows several times over when the suite's heavier classes run beside it. What the test holds — the findings the
        // report keeps and counts, and what the thread allocates — does not change with what runs beside it.
        var file = OneByteRows(256 * 1024 * 1024, out _);
        file.Length.Should().BeLessThan(1024);
        using var document = PdfDocument.Open(file);
        document.Reader.SettleIndex();

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var report = new PdfValidator().Validate(document);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        report.Findings.Should().HaveCount(PdfValidatorOptions.Default.FindingCapacity);
        report.ErrorCount.Should().Be(8_388_603);
        report.SuppressedCount.Should().Be(report.ErrorCount + report.WarningCount + report.InformationCount - 1000);
        allocated.Should().BeLessThan((4L * report.ErrorCount) + (4 * 1024 * 1024), "the numbers of the entries, copied once, and the findings kept");
    }

    [Fact]
    public void Judging_the_size_of_sections_past_the_report_s_room_formats_no_message()
    {
        // #168: a chain of sections, each giving /Size 99 where 3 is right. The rule formatted each section's message, some
        // 470 bytes, then reported it to a report that had no room for it. It asks for room first, and counts a finding the
        // report cannot keep without writing it: what one more section costs is the highest number it and the older ones use.
        var (fewAllocated, few) = Judging(250);
        var (manyAllocated, many) = Judging(1000);

        few.WarningCount.Should().Be(250);
        many.WarningCount.Should().Be(1000);
        many.Findings.Should().BeEmpty();
        many.SuppressedCount.Should().Be(1000);
        manyAllocated.Should().BeLessThan(16 * 1024, "the highest number of each section, and no message");
        ((manyAllocated - fewAllocated) / 750.0).Should().BeLessThan(8, "one more section costs an int, not a message");

        static (long Allocated, PdfValidationReport Report) Judging(int sections)
        {
            using var document = PdfDocument.Open(SectionsGivingAWrongSize(sections).File);
            var rule = new SizeWrongRule();

            // A context of no room each measure, the probe the rule reads made outside it.
            var allocated = Allocations.Least(
                () =>
                {
                    var context = new ValidationContext(document, capacity: 0);
                    _ = context.Probe;
                    return context;
                },
                context =>
                {
                    rule.Check(context);
                    return 0;
                },
                out var context,
                out _);
            return (allocated, context.ToReport(ValidationProfile.Structural));
        }
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("0.", 2)]
    public void Judging_the_values_of_a_trailer_names_one_only_for_a_finding_the_report_has_room_for(string element, int findings)
    {
        // #168: a cross-reference stream whose /Index gives thousands of subsections of no rows, each element sound, or a real
        // with no fractional part, a finding each, which a report of no room counts. The rule named each element, "element 9
        // of its /Index", some 80 bytes, before judging it: it names one for a finding the report has room for alone.
        var (fewAllocated, few) = Judging(1000);
        var (manyAllocated, many) = Judging(4000);

        few.WarningCount.Should().Be(findings * 1000);
        many.WarningCount.Should().Be(findings * 4000);
        many.Findings.Should().BeEmpty();
        ((manyAllocated - fewAllocated) / 6000.0).Should().BeLessThan(1, "one more element costs no name");

        (long Allocated, PdfValidationReport Report) Judging(int pairs)
        {
            using var document = PdfDocument.Open(EmptySubsections(pairs, element));
            var rule = new TrailerValueWrongRule();
            var allocated = Allocations.Least(
                () => new ValidationContext(document, capacity: 0),
                context =>
                {
                    rule.Check(context);
                    return 0;
                },
                out var context,
                out _);
            return (allocated, context.ToReport(ValidationProfile.Structural));
        }
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
    public void A_thousand_tables_naming_one_stream_read_it_once()
    {
        // A thousand tables name through /XRefStm, all at one offset, one cross-reference stream of 100,000 rows, 700 KB. It is
        // read once, where the newest names it, and each older table's naming is recorded as naming it again, nothing read for
        // it (#156): the chain reads not a byte more than when the newest alone names it, the older tables giving its offset
        // under a key that names nothing, and allocates beside it the records of the namings, 168 bytes each as measured. Read
        // again for each table, the stream would cost 700 MB of reads.
        const int Tables = 1000;
        var every = HybridFiles.TablesNamingOneStream(Tables, rows: 100_000, every: true);
        var newest = HybridFiles.TablesNamingOneStream(Tables, rows: 100_000, every: false);

        var (everyRead, sections) = Read(every);
        var (newestRead, _) = Read(newest);
        var everyAllocated = Allocations.Least(() => Open(every), out _);
        var newestAllocated = Allocations.Least(() => Open(newest), out _);

        sections.Should().HaveCount(2 * Tables);
        sections.Where(section => section.IsHybridStream).Should().ContainSingle().Which.State.Should().Be(XRefSectionState.Read);
        sections.Where(section => section.State == XRefSectionState.NamedAgain).Should().HaveCount(Tables - 1)
            .And.OnlyContain(section => ReferenceEquals(section.ReadAs, sections[1]) && section.NamesNoStream == null);
        everyRead.Should().Be(newestRead, "a naming again reads nothing");
        (everyAllocated - newestAllocated).Should().BeLessThan(
            (Tables - 1) * 256L,
            "a naming again allocates its record, nothing it reads ({0:N0} bytes against {1:N0})",
            everyAllocated,
            newestAllocated);

        static (long Read, IReadOnlyList<XRefSectionRecord> Sections) Read(byte[] file)
        {
            using var source = new StrictCountingSource(file);
            using var document = Measure(() => PdfDocument.Open(source, options: null, ownsSource: false));
            document.Diagnostics.Should().BeEmpty();
            return (source.BytesRead, document.Reader.Structure.Sections);
        }

        static int Open(byte[] file)
        {
            using var document = PdfDocument.Open(file);
            return document.Reader.Structure.Sections.Count;
        }
    }

    [Fact]
    public void A_thousand_tables_naming_one_stream_through_the_white_space_before_it_read_it_once()
    {
        // The thousand tables name the stream of 100,000 rows each at its own offset, k bytes short of its header for the
        // table k revisions older than the newest, in the 999 spaces before it. The chain knew the stream by the offset that
        // named it, and read it again for each older table: 999 times 700 KB. It knows it by where its header starts, and
        // records each older naming as naming it again (#156): it reads beside the file whose tables all name the header
        // only what each naming's white space takes to cross — windows of 32, 256 and 2,048 bytes at most —, and allocates
        // nothing more.
        const int Tables = 1000;
        var shifted = HybridFiles.TablesNamingOneStream(Tables, rows: 100_000, every: true, spaces: Tables - 1, shifted: true);
        var same = HybridFiles.TablesNamingOneStream(Tables, rows: 100_000, every: true, spaces: Tables - 1);
        shifted.Should().HaveCount(same.Length, "the offsets are written in ten digits in both");

        var (shiftedRead, sections) = Read(shifted);
        var (sameRead, _) = Read(same);
        var shiftedAllocated = Allocations.Least(() => Open(shifted), out _);
        var sameAllocated = Allocations.Least(() => Open(same), out _);

        sections.Where(section => section.IsHybridStream).Should().ContainSingle().Which.State.Should().Be(XRefSectionState.Read);
        sections.Where(section => section.State == XRefSectionState.NamedAgain).Should().HaveCount(Tables - 1)
            .And.OnlyContain(section => ReferenceEquals(section.ReadAs, sections[1]) && section.NamesNoStream == null);
        (shiftedRead - sameRead).Should().BeInRange(
            0,
            (Tables - 1) * (32L + 256 + 2048),
            "each older naming reads its white space, not the stream ({0:N0} bytes against {1:N0})",
            shiftedRead,
            sameRead);
        (shiftedAllocated - sameAllocated).Should().BeLessThan(
            64 * 1024,
            "crossing the white space allocates nothing ({0:N0} bytes against {1:N0})",
            shiftedAllocated,
            sameAllocated);

        static (long Read, IReadOnlyList<XRefSectionRecord> Sections) Read(byte[] file)
        {
            using var source = new StrictCountingSource(file);
            using var document = Measure(() => PdfDocument.Open(source, options: null, ownsSource: false));
            document.Diagnostics.Should().BeEmpty();
            return (source.BytesRead, document.Reader.Structure.Sections);
        }

        static int Open(byte[] file)
        {
            using var document = PdfDocument.Open(file);
            return document.Reader.Structure.Sections.Count;
        }
    }

    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(true, 0, 0)]
    [InlineData(true, 3, 0)]
    [InlineData(true, 3, 1)]
    public void A_chain_whose_offsets_name_long_runs_of_white_space_crosses_each_run_once(bool hybrid, int streamShortBy, int objectsBefore)
    {
        // A hundred tables, each named, by startxref or by the /Prev of the table after it, at the start of the 60,000 spaces
        // before its xref keyword — and, in a hybrid file, each naming through /XRefStm a stream of its own, at its header or
        // 3 bytes short of it, inside the token before it, where the search near that offset finds it; or, with an object
        // written before the stream, 3 bytes into that object, whose header the search tries before it finds the stream.
        // Where the chain reaches what an offset names, it crossed the run as far as a trailer's window reaches, then again
        // where it read the section there, and a hybrid table's /Prev a third time: two or three crossings of 84 KB for each
        // run — and, where the search tried two places, the places it tried pushed the /Prev's probe out, and the /Prev's run
        // was crossed again. Each run is crossed once, its probe answering each time it is asked of, the /Prev's outlasting
        // its /XRefStm's search, however many places that search tries (#188): beside the same file whose offsets name the
        // keywords, a crossing a section at most, and a few bytes more.
        const int Tables = 100;
        const long Crossing = PdfFileReader.XRefProbeLength + 256 + 2048 + 16384 + 65536;
        var padded = Chain(namesTheRun: true);
        var exact = Chain(namesTheRun: false);
        padded.Should().HaveCount(exact.Length, "the offsets are written in ten digits in both");

        var paddedRead = Read(padded);
        var exactRead = Read(exact);

        (paddedRead - exactRead).Should().BeInRange(
            Tables * Crossing,
            Tables * (Crossing + 4096),
            "each run is crossed once ({0:N0} bytes against {1:N0})",
            paddedRead,
            exactRead);

        byte[] Chain(bool namesTheRun)
        {
            var text = new StringBuilder("%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
            var previous = -1L;

            for (var table = 0; table < Tables; table++)
            {
                var stream = -1L;
                var xrefStm = -1L;

                if (hybrid)
                {
                    var before = text.Length;

                    for (var objects = 0; objects < objectsBefore; objects++)
                    {
                        text.Append(CultureInfo.InvariantCulture, $"{200 + (table * objectsBefore) + objects} 0 obj\nnull\nendobj\n");
                    }

                    // One free row for object 0, in seven bytes.
                    stream = text.Length;
                    xrefStm = objectsBefore > 0 ? before + streamShortBy : stream - streamShortBy;
                    text.Append(CultureInfo.InvariantCulture, $"{table + 3} 0 obj\n<< /Type /XRef /Size 3 /Index [0 1] /W [1 4 2] /Length 7 >>\nstream\n")
                        .Append("\0\0\0\0\0\u00ff\u00ff\nendstream\nendobj\n");
                }

                var named = text.Length;
                text.Append(' ', 60_000);
                var xref = text.Length;
                text.Append("xref\n0 1\n0000000000 65535 f\r\ntrailer\n<< /Size 3 /Root 1 0 R")
                    .Append(previous < 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" /Prev {previous:D10}"))
                    .Append(stream < 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" /XRefStm {xrefStm:D10}"))
                    .Append(" >>\n");
                previous = namesTheRun ? named : xref;
            }

            text.Append(CultureInfo.InvariantCulture, $"startxref\n{previous:D10}\n%%EOF\n");
            return Encoding.Latin1.GetBytes(text.ToString());
        }

        long Read(byte[] file)
        {
            using var source = new StrictCountingSource(file);
            using var document = PdfDocument.Open(source, options: null, ownsSource: false);
            // Each table is read where it is named; a stream named short of it is found by as many searches as a document
            // allows, the others missing, which the file reads the same whichever way its tables are named.
            var sections = document.Reader.Structure.Sections;
            sections.Should().HaveCount(hybrid ? 2 * Tables : Tables);
            sections.Where(section => section.NamedBy != "/XRefStm").Should().OnlyContain(section => section.State == XRefSectionState.Read);
            sections.Where(section => section.NamedBy == "/XRefStm").Should().OnlyContain(section =>
                section.State == (streamShortBy > 0 ? XRefSectionState.Relocated : XRefSectionState.Read) || section.State == XRefSectionState.NotFound);
            return source.BytesRead;
        }
    }

    [Theory]
    [InlineData("99999999", false)]
    [InlineData("99999999", true)]
    [InlineData("50000", false)]
    public void A_table_reads_nothing_of_what_its_prev_names_for_an_xrefstm_whose_search_never_comes_there(string xrefStm, bool guard)
    {
        // The newest table, of 40 rows, names through /Prev an older section that 100 KB of comment follow, and through
        // /XRefStm an offset outside the file, or one inside the comment, some 50 KB past the older section: no search runs
        // near the first, and the one near the second comes nowhere near what the /Prev names. Where the older section is no
        // classic table, whether the /Prev keeps its place there was told all the same, reading a window of 64 KB at it —
        // past the guard, where MaxXRefSectionLength cut the newest table's rows, which reads nothing past it (#264). It is
        // told only where the search comes there: beside the same files without the /XRefStm, the table reads no more where
        // its /Prev names a cross-reference stream than where it names a classic table, which needs no look, but for a few
        // bytes the two files' lengths part (#156).
        var limits = guard ? PdfReaderLimits.Default with { MaxXRefSectionLength = 300 } : PdfReaderLimits.Default;
        var namingAStream = Read(Build(" /XRefStm " + xrefStm, table: false)) - Read(Build(string.Empty, table: false));
        var namingATable = Read(Build(" /XRefStm " + xrefStm, table: true)) - Read(Build(string.Empty, table: true));

        namingAStream.Should().BeInRange(
            namingATable - 1024,
            namingATable + 1024,
            "the /XRefStm reads {0:N0} bytes more where the /Prev names a classic table",
            namingATable);

        static byte[] Build(string xrefStm, bool table)
        {
            var file = new HandWrittenFile();
            var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
            var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
            var older = table
                ? file.Table(3, new() { [1] = catalog, [2] = pages }, "/Size 3 /Root 1 0 R")
                : file.CrossReferenceStream(9, 10, new() { [1] = (1, catalog, 0), [2] = (1, pages, 0) }, "/Root 1 0 R");
            file.Raw("%" + new string('-', 100 * 1024) + "\n");
            file.Table(40, new() { [1] = catalog, [2] = pages }, string.Create(CultureInfo.InvariantCulture, $"/Size 40 /Root 1 0 R /Prev {older}{xrefStm}"));
            return file.Bytes;
        }

        long Read(byte[] file)
        {
            using var source = new StrictCountingSource(file);
            using var document = PdfDocument.Open(source, new PdfReaderOptions { Limits = limits }, ownsSource: false);
            return source.BytesRead;
        }
    }

    [Theory]
    [InlineData("a stream 100 KB long that reads")]
    [InlineData("a dictionary the end of the file cuts")]
    public void The_look_at_what_a_prev_names_grows_no_window_for_stream_data_or_the_file_s_end_under_a_raised_trailer_limit(string four)
    {
        // The newest table names object 4 through /Prev, and through /XRefStm a place beside it, whose search comes to object
        // 4 and asks whether the /Prev keeps its place there, reading object 4 as the chain would: a stream that gives a /W
        // and no /Type, which reads, its data of 100 KB filling the first window read at it, or a dictionary a string the
        // file never closes runs to the end of the file. Neither is cut by a window's edge, and both are read in one window:
        // with MaxTrailerLength raised to 1 MB, as many bytes are read as with the default, no larger window read for what
        // a window full of data holds, nor again for the file's end (#156).
        var file = four == "a dictionary the end of the file cuts"
            ? HybridFiles.NamingAnObjectBesideTheStream("<< /Pad (").File
            : HybridFiles.PrevNamingAStreamBesideTheOldestTable(string.Empty, padding: 100_000).File;

        Read(PdfReaderLimits.Default with { MaxTrailerLength = 1024 * 1024 }).Should().Be(Read(PdfReaderLimits.Default));

        long Read(PdfReaderLimits limits)
        {
            using var source = new StrictCountingSource(file);
            using var document = PdfDocument.Open(source, new PdfReaderOptions { Limits = limits }, ownsSource: false);
            return source.BytesRead;
        }
    }

    [Fact]
    public void The_search_near_a_hybrid_table_s_prev_tries_an_object_past_the_stream_its_xrefstm_read_once()
    {
        // The newest table's /XRefStm reads stream 6, which gives no /Prev, and its /Prev falls 7 bytes short of stream 6. The
        // search near that offset tries objects 5 and 10, past stream 6, which declare no cross-reference stream, then passes
        // over stream 11, which may be the table's own, and takes it once every other place was. Only a stream that may be the
        // table's own is put back, and tried again: object 5 is tried once, the probe of its header read once, and dropped
        // (#156).
        var (file, _, stream, own, _) = HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", shortOfOwn: false, streamPrev: "nothing");
        var five = Encoding.Latin1.GetString(file).IndexOf("5 0 obj", (int)stream, StringComparison.Ordinal);
        using var source = new WindowLoggingSource(file);

        using (var document = PdfDocument.Open(source, options: null, ownsSource: false))
        {
            document.Reader.Structure.Sections[^1].Should().Match<XRefSectionRecord>(
                section => section.NamedBy == "/Prev" && section.State == XRefSectionState.Relocated && section.Offset == own);
        }

        source.Windows.Count(window => window.Offset == five && window.Length == PdfFileReader.XRefProbeLength).Should().Be(1);
    }

    [Fact]
    public void The_search_near_a_hybrid_table_s_prev_reads_nothing_at_the_header_of_the_stream_its_xrefstm_read_from_inside_its_number()
    {
        // The newest table's /XRefStm names a place 1 byte into stream 13's object number, from which the stream reads as
        // object 3, and its /Prev falls 7 bytes short of the stream's header, which starts 1 byte before where that reading
        // started. The stream gives no /Prev. The header's number runs on into where that reading started: it is the stream
        // the /XRefStm read, told by the dictionary that reading kept, and passed over as that stream is where it gives no
        // /Prev, its header not even probed, rather than taken as a stream before it. No other section lies within reach:
        // the section the /Prev names is missing, as where the /XRefStm names the stream's header itself (#156).
        var (file, stream, newest) = HybridFiles.NamingAStreamOneByteIntoItsNumber();
        using var source = new WindowLoggingSource(file);

        using (var document = PdfDocument.Open(source, options: null, ownsSource: false))
        {
            document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
                ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
                ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, stream + 1),
                ("/Prev", XRefSectionState.NotFound, XRefSectionKind.Unknown, stream - 7));
            document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionMissing);
        }

        source.Windows.Count(window => window.Offset == stream && window.Length == PdfFileReader.XRefProbeLength).Should().Be(0);
    }

    [Theory]
    [InlineData("/Type /XRef ", null, 1)]
    [InlineData("", null, 2)]
    [InlineData("/Type /XRef ", "/Size", 1)]
    [InlineData("/Type /XRef ", "/W", 1)]
    [InlineData("/Type /XRef ", "/Index", 1)]
    [InlineData("", "/Size", 2)]
    public void The_look_at_what_a_prev_names_reads_a_stream_s_data_only_where_the_data_decides_whether_the_prev_keeps_its_place(
        string type, string? cut, int reads)
    {
        // The newest table names object 4 through /Prev, and through /XRefStm a place beside it, whose search comes to object
        // 4 and asks whether the /Prev keeps its place there: a stream that reads, its data of 100 KB, which gives /Type /XRef
        // or no /Type. A stream of /Type /XRef keeps the /Prev's place whatever its data holds: the look decodes none of it,
        // and the /Prev's own reading, next, reads it once. Where the stream gives only a /W, whether its filters decode
        // anything of its data tells whether it reads as a section there: the look decodes it, and the /Prev's own reading
        // reads it again. Every decoding reads the data whole, from its first byte. So it goes where the stream's /Size, /W
        // or /Index is the reference 9 0 R, which MaxObjectLength cuts, the /Prev's own reading then ending the chain at the
        // guard's section. The look decoded a stream of /Type /XRef beside the cut value, to tell whether its data holds any
        // row, which tells nothing of the place it keeps: the data was read twice (#156, #221, #263).
        var (file, _, four, _) = HybridFiles.PrevNamingAStreamBesideTheOldestTable(type, padding: 100_000, cut: cut);
        var data = Encoding.Latin1.GetString(file).IndexOf("stream\n", (int)four, StringComparison.Ordinal) + "stream\n".Length;
        var options = cut is null ? null : new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } };
        (string, XRefSectionState)[] sections = cut is null
            ? [("startxref", XRefSectionState.Read), ("/XRefStm", XRefSectionState.NamedAgain), ("/Prev", XRefSectionState.Read), ("/Prev", XRefSectionState.Read)]
            : [("startxref", XRefSectionState.Read), ("/XRefStm", XRefSectionState.NamedAgain), ("/Prev", XRefSectionState.Malformed)];
        using var source = new WindowLoggingSource(file);

        using (var document = PdfDocument.Open(source, options, ownsSource: false))
        {
            document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State)).Should().Equal(sections);
            document.Reader.Structure.Sections[^1].CutByLimit.Should().Be(cut is not null);
        }

        source.Windows.Count(window => window.Offset == data).Should().Be(reads);
    }

    [Fact]
    public void The_look_at_what_a_prev_names_reads_the_object_there_through_the_windows_the_prev_s_own_reading_opens()
    {
        // The newest table names object 4 through /Prev, and through /XRefStm a place beside it, whose search comes to object
        // 4 and asks whether the /Prev keeps its place there: a stream of no type, whose dictionary holds 70,000 bytes and
        // whose data, 600,000 bytes, runs on past it. MaxTrailerLength raised to 1 MB, the dictionary is read through the
        // windows a trailer's is: 64 KB, which cuts it, then eight times as much, which holds it, the data served from the
        // file. The look opens those windows, past the probe of the white space before object 4, and so do the place's
        // reading by the search, which drops it, and the /Prev's own reading, which looks for its section nearby: each
        // reads object 4 as the others do (#156).
        var (file, oldest, four, stream, _) = HybridFiles.NamingAnObjectBesideTheStream(
            "<< /Pad (" + new string('-', 70_000) + ") /Length 600000 >>\nstream\n" + new string('-', 600_000) + "\nendstream");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxTrailerLength = 1024 * 1024 } };
        using var source = new WindowLoggingSource(file);

        using (var document = PdfDocument.Open(source, options, ownsSource: false))
        {
            document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Offset)).Skip(1).Should().Equal(
                ("/XRefStm", XRefSectionState.Relocated, stream),
                ("/Prev", XRefSectionState.Relocated, oldest));
        }

        const int Grown = 8 * PdfFileReader.XRefWindow;
        source.Windows.Where(window => window.Offset == four).Select(window => window.Length).Should().Equal(
            PdfFileReader.XRefProbeLength, PdfFileReader.XRefWindow, Grown, PdfFileReader.XRefWindow, Grown, PdfFileReader.XRefWindow, Grown);
    }

    [Fact]
    public void A_prev_looping_back_through_the_spaces_before_a_table_it_named_there_is_known_without_a_look()
    {
        // The newest table names the middle one 3 bytes short of it, where the chain reads it; a table between the middle and
        // the oldest is read next, then the oldest, whose /Prev names the middle again at that offset, or at its keyword. The
        // chain has read two other places since it crossed the spaces before the middle, which its last probes now answer:
        // a look at them would cross them again. Either loop is placed where the table starts, known from what the chain
        // read there, without a look at the spaces again: the chain reads not a byte more for the one than for the other
        // (#188).
        var (repeated, _, _, _) = NearbyFiles.ChainNamingTheSpacesBeforeTheMiddle(3, oldestPrev: 3, tablesBetween: 1);
        var (atKeyword, _, _, _) = NearbyFiles.ChainNamingTheSpacesBeforeTheMiddle(3, oldestPrev: 0, tablesBetween: 1);
        repeated.Should().HaveCount(atKeyword.Length);

        Read(repeated).Should().Be(Read(atKeyword), "the place the chain read a section at is known without a look");

        static long Read(byte[] file)
        {
            using var source = new StrictCountingSource(file);
            using var document = PdfDocument.Open(source, options: null, ownsSource: false);
            document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefChainCycle);
            return source.BytesRead;
        }
    }

    [Fact]
    public void A_section_named_again_where_it_was_found_moved_is_known_without_reading_there_again()
    {
        // The middle table, which the newest names 3 bytes short of it, is found moved; the oldest's /Prev names it again,
        // where it lies or where the newest named it. Either ends the chain in a loop at once, known by the position the
        // chain read the table at, or by the offset it named: the chain reads not a byte more for the one than for the other
        // (#188). The oldest's offset is written in thirteen digits, as long as the /XRefStm it stands for.
        var built = HybridFiles.OldestNaming("0000000000");
        var middle = PdfTemplate.OffsetOf(built, "\nxref\n", 2) + 1;
        var text = Encoding.Latin1.GetString(built)
            .Replace(string.Create(CultureInfo.InvariantCulture, $"/Prev {middle} >>"), string.Create(CultureInfo.InvariantCulture, $"/Prev {middle - 3} >>"), StringComparison.Ordinal);
        var whereItLies = Encoding.Latin1.GetBytes(text.Replace("/XRefStm 0000000000 >>", string.Create(CultureInfo.InvariantCulture, $"/Prev {middle:D13} >>"), StringComparison.Ordinal));
        var whereNamed = Encoding.Latin1.GetBytes(text.Replace("/XRefStm 0000000000 >>", string.Create(CultureInfo.InvariantCulture, $"/Prev {middle - 3:D13} >>"), StringComparison.Ordinal));
        whereItLies.Should().HaveCount(whereNamed.Length);

        Read(whereItLies).Should().Be(Read(whereNamed), "the place the chain read a section at is known without a look");

        static long Read(byte[] file)
        {
            using var source = new StrictCountingSource(file);
            using var document = PdfDocument.Open(source, options: null, ownsSource: false);
            document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefChainCycle);
            return source.BytesRead;
        }
    }

    [Fact]
    public void A_section_named_in_the_white_space_that_ends_the_file_is_looked_at_in_one_window_each_time()
    {
        // The table's /Prev names the ten spaces that end the file, past its %%EOF. The probe that finds where a section
        // there would start holds them and all the file has there: it is not grown, as a probe the file does not end is, up
        // to a trailer's window, which would read the same ten bytes four times more at each look (#188). The chain looks
        // there to know whether it reached the place and to read the section it finds nothing of, and parses what is there.
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Padding(3);
        var table = file.Position;
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 3\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \ntrailer\n<< /Size 3 /Root 1 0 R /Prev PPPPPPPPPP >>\nstartxref\n{table}\n%%EOF\n");
        var spaces = table + text.Length;
        file.Raw(text.Replace("PPPPPPPPPP", spaces.ToString("D10", CultureInfo.InvariantCulture), StringComparison.Ordinal) + new string(' ', 10));
        var bytes = file.Bytes;
        var position = bytes.Length - 10;
        using var source = new WindowLoggingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);

        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionMissing);
        source.Windows.Count(window => window.Offset == position).Should().BeLessThanOrEqualTo(3, "each look reads the ten bytes once");
    }

    [Fact]
    public void Four_times_as_many_lost_streams_declaring_numbers_of_their_own_allocate_about_four_times_as_much()
    {
        // A chain of cross-reference streams whose rows cannot be read, each handing its /Prev on and refusing to the older
        // rows four numbers of its own, the ranges kept growing to 4,096 over 1,024 streams (#208). The ranges are merged into
        // arrays the shared pool lends and takes back: what the chain allocates follows what it reads, the streams'
        // dictionaries and the older table's 64 rows a stream, 4.02 times as much for four times the streams, on every run.
        // A list of every range kept, made again for each stream, allocated 14 times as much, 106 MB against 7.6 MB.
        const int Small = 256;
        const int Large = 4 * Small;
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = Large + 2 } };
        var small = LostStreamFiles.OwnNumbers(Small, each: 4, rows: 64 * Small);
        var large = LostStreamFiles.OwnNumbers(Large, each: 4, rows: 64 * Large);

        var smallAllocated = Allocations.Least(() => Open(small), out var smallSections);
        var largeAllocated = Allocations.Least(() => Open(large), out var largeSections);

        smallSections.Should().Be(Small + 2);
        largeSections.Should().Be(Large + 2);
        largeAllocated.Should().BeLessThan(
            smallAllocated * 9 / 2,
            "four times the streams must allocate about four times as much ({0:N0} bytes for {1:N0}, {2:N0} for {3:N0})",
            smallAllocated,
            Small,
            largeAllocated,
            Large);

        int Open(byte[] file)
        {
            using var document = PdfDocument.Open(file, options);
            document.Reader.ChainIndex!.TryGet(3, out _).Should().BeTrue("no stream declares object 3");
            return document.Reader.Structure.Sections.Count;
        }
    }

    [Fact]
    public void A_refusal_gives_back_the_arrays_it_merges_ranges_in()
    {
        // A lost stream declares 4,096 one-row subsections apart, and its /Prev names a table of 16 rows; the same file with
        // the stream's /Prev renamed names no older section, and refuses nothing (#208). Opened again and again, the first
        // allocates beyond the second the numbering it reads, 16 bytes a subsection, and the older table it reaches: 66.8 KB.
        // The arrays the ranges are merged in come from the shared pool and go back to it, each document's once its chain is
        // read: one kept back would cost 64 KB more each opening.
        var refusing = LostStreamFiles.RangesOverOlderRows(apart: true, rows: 16);
        var text = Encoding.Latin1.GetString(refusing);
        var prev = text.IndexOf("/Prev", StringComparison.Ordinal);
        var namingNone = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, prev), "/Xrev", text.AsSpan(prev + 5)));

        var refusingAllocated = Allocations.Least(() => Open(refusing), out var refusingSections);
        var namingNoneAllocated = Allocations.Least(() => Open(namingNone), out var namingNoneSections);

        refusingSections.Should().Be(3);
        namingNoneSections.Should().Be(2);
        (refusingAllocated - namingNoneAllocated).Should().BeLessThan(
            96 * 1024,
            "a refusal allocates the numbering it reads, and gives back what it rents ({0:N0} bytes against {1:N0})",
            refusingAllocated,
            namingNoneAllocated);

        static int Open(byte[] file)
        {
            using var document = PdfDocument.Open(file);
            return document.Reader.Structure.Sections.Count;
        }
    }

    [Fact]
    public void A_refusal_merged_with_ranges_refused_before_gives_back_the_array_they_were_kept_in()
    {
        // A lost stream declares 40 subsections, every other number from 10, every other one of them of one row and the
        // rest of none; its /Prev names an older lost stream that declares 16 one-row subsections apart from 100, and
        // the older stream's /Prev names the table of 16 rows (#208). The shared pool lends arrays by sizes, 16, 32, 64
        // and up, and lends a thread first the array of a size that thread gave back last. The newer stream's 20 ranges
        // are kept in an array of 32, its numbering read into one of 64; the older stream's 16 are merged with them
        // into an array of 64 as well, and the array of 32 goes back to the pool once they are merged. So the array of
        // 32 this thread gives back before the file is opened keeps the newer stream's ranges, and is lent again once
        // the file is read: kept back at the merge, it would be lost to the pool, which would lend another. The bytes
        // allocated do not tell this apart: where the pool already keeps other arrays of 32, from what the process did
        // before, it lends one of those without allocating. A collection of the oldest generation under memory pressure
        // may empty what the pool keeps for each thread: an opening one falls in is done again.
        var file = LostStreamFiles.Chain(2, rows: 16, objects: [], s => s == 0 ? Newer() : Older());
        var pool = ArrayPool<(long First, long End)>.Shared;
        var lent = pool.Rent(20);
        pool.Return(lent);
        pool.Rent(20).Should().BeSameAs(lent, "the pool lends a thread first the array of a size it gave back last");
        pool.Return(lent);

        var again = lent;
        var sections = 0;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var collections = GC.CollectionCount(2);
            lent = pool.Rent(20);
            pool.Return(lent);
            sections = Open(file);
            again = pool.Rent(20);
            pool.Return(again);

            if (GC.CollectionCount(2) == collections)
            {
                break;
            }
        }

        sections.Should().Be(4);
        again.Should().BeSameAs(
            lent,
            "the array the newer stream's ranges were kept in goes back to the pool once the older stream's are merged with them");

        static IEnumerable<(long First, long Count)> Newer() =>
            Enumerable.Range(0, 40).Select(i => (10L + (2L * i), i % 2 == 0 ? 1L : 0L));

        static IEnumerable<(long First, long Count)> Older() => Enumerable.Range(0, 16).Select(i => (100L + (2L * i), 1L));

        static int Open(byte[] file)
        {
            using var document = PdfDocument.Open(file);
            return document.Reader.Structure.Sections.Count;
        }
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
    /// The sound document spread out, object 3's row 700 bytes before its header, inside object 4's string: the document
    /// opens with five entries, and reading 3 rebuilds its index.
    /// </summary>
    private static byte[] RebuiltOnReadingThree() =>
        PdfTemplate.Build(PdfTemplate.Spread.Replace("{row:3}", "{row:3:0:-700}", StringComparison.Ordinal));

    /// <summary>
    /// Writes a file indexed by a cross-reference stream whose catalog names object 6, which object stream 7 holds, with
    /// 2,000 objects written directly in the file, and the row of <paramref name="brokenRow"/> outside the file.
    /// </summary>
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

    /// <summary>
    /// A file of <paramref name="streams"/> object streams, each holding one dictionary, that share what their
    /// <c>/Filter</c> and <c>/DecodeParms</c> lead to, as <paramref name="shared"/> describes it: a parameters dictionary of
    /// 100 KB, a chain of 20,000 references to one, <see cref="SharedChains"/>'s 64 filters and parameters, or
    /// <see cref="LengthNests"/>'.
    /// </summary>
    internal static byte[] SharedByObjectStreams(string shared, int streams)
    {
        var packed = new List<PackedStream>();

        // Numbered past the chain's objects.
        for (var k = 0; k < streams; k++)
        {
            packed.Add(new PackedStream(30_000 + (2 * k), "/DecodeParms 3 0 R", [(30_001 + (2 * k), "<< >>")], PackedData.Flate));
        }

        switch (shared)
        {
            case "a parameters dictionary of 100 KB":
                return PackedFile(packed, [(3, "<< /Predictor 1 /Pad [" + new StringBuilder().Insert(0, "0 ", 50_000) + "] >>")]);

            case "a chain of 20,000 references":
                var chain = new List<(int Number, string Body)>();

                for (var link = 3; link < 20_003; link++)
                {
                    chain.Add((link, link + 1 < 20_003 ? string.Create(CultureInfo.InvariantCulture, $"{link + 1} 0 R") : "<< /Predictor 1 >>"));
                }

                return PackedFile(packed, chain);

            case "64 filters and parameters, each a chain":
                return SharedChains(streams, links: 31);

            default:
                return LengthNests(streams);
        }
    }

    /// <summary>
    /// A file of <paramref name="streams"/> object streams, each holding one dictionary, that share <c>/Filter 3 0 R</c> and
    /// <c>/DecodeParms 4 0 R</c>: 64 references each — as many filters as the reader decodes through —, to a chain of
    /// <paramref name="links"/> references ending at <c>/FlateDecode</c>, and to one ending at parameters whose predictor,
    /// colors, bits and columns are chains as long. Decoding reads every one of them for each stream. When
    /// <paramref name="oneStep"/> says so, the streams take <c>/Filter 10 0 R</c> and <c>/DecodeParms 100 0 R</c>, the
    /// chains' first objects, themselves: one step, which reads each of the six chains once. The streams' data is written
    /// as it is: the walk of their dependencies reads none of it.
    /// </summary>
    internal static byte[] SharedChains(int streams, int links, bool oneStep = false)
    {
        var direct = new List<(int Number, string Body)>
        {
            (3, "[" + string.Join(' ', Enumerable.Repeat("10 0 R", 64)) + "]"),
            (4, "[" + string.Join(' ', Enumerable.Repeat("100 0 R", 64)) + "]"),
        };
        var dictionary = oneStep ? "/Filter 10 0 R /DecodeParms 100 0 R" : "/Filter 3 0 R /DecodeParms 4 0 R";

        Chain(10, "/FlateDecode");
        Chain(100, "<< /Predictor 200 0 R /Colors 300 0 R /BitsPerComponent 400 0 R /Columns 500 0 R >>");
        Chain(200, "12");
        Chain(300, "1");
        Chain(400, "8");
        Chain(500, "1");

        var packed = new List<PackedStream>();

        for (var k = 0; k < streams; k++)
        {
            packed.Add(new PackedStream(1000 + (2 * k), dictionary, [(1001 + (2 * k), "<< >>")], PackedData.Plain));
        }

        return PackedFile(packed, direct);

        void Chain(int first, string last)
        {
            for (var link = first; link < first + links; link++)
            {
                direct.Add((link, link + 1 < first + links ? string.Create(CultureInfo.InvariantCulture, $"{link + 1} 0 R") : last));
            }
        }
    }

    /// <summary>
    /// A file of <paramref name="streams"/> object streams, numbered 10, 12 and so on, each holding one dictionary, numbered
    /// after it, and needing one another's through its <c>/DecodeParms</c>, as <paramref name="shape"/> says: a chain, each
    /// needing the next's and the last none; a ring, the last needing the first's; a rho, the last needing the halfway one's;
    /// a star, the first needing every other's — through as many Flate steps, each reading the element at its place, its
    /// data written as it is, which the first step fails on and the reader takes as it is —, and every other needing the
    /// first's. The other streams are Flate-compressed, so that decoding reads their parameters.
    /// </summary>
    internal static byte[] ChainedObjectStreams(string shape, int streams)
    {
        var packed = new List<PackedStream>();

        for (var k = 0; k < streams; k++)
        {
            var needed = shape switch
            {
                "chain" => k + 1 < streams ? k + 1 : -1,
                "ring" => (k + 1) % streams,
                "rho" => k + 1 < streams ? k + 1 : streams / 2,
                "star" => k == 0 ? -1 : 0,
                _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "No such shape."),
            };

            var dictionary = needed < 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"/DecodeParms {11 + (2 * needed)} 0 R");
            var data = PackedData.Flate;

            if (shape == "star" && k == 0)
            {
                var filters = new StringBuilder("/Filter [");
                var parameters = new StringBuilder("] /DecodeParms [");

                for (var other = 1; other < streams; other++)
                {
                    filters.Append("/FlateDecode ");
                    parameters.Append(CultureInfo.InvariantCulture, $"{11 + (2 * other)} 0 R ");
                }

                dictionary = filters.Append(parameters).Append(']').ToString();
                data = PackedData.Plain;
            }

            packed.Add(new PackedStream(10 + (2 * k), dictionary, [(11 + (2 * k), "<< >>")], data));
        }

        return PackedFile(packed);
    }

    /// <summary>
    /// Gives how many of the <paramref name="streams"/> object streams <see cref="ChainedObjectStreams"/> writes in
    /// <paramref name="shape"/> are circular: none of a chain; of a rho, those from the halfway one to the last; all of a ring
    /// or a star.
    /// </summary>
    internal static int CircularAmongChainedObjectStreams(string shape, int streams) => shape switch
    {
        "chain" => 0,
        "rho" => streams - (streams / 2),
        _ => streams,
    };

    /// <summary>
    /// A file of <paramref name="streams"/> object streams, numbered 20,000, 20,002 and so on, each holding one dictionary,
    /// whose nests the walk cannot keep: of <paramref name="shape"/>, nest heads past the cache, met in turn, sharing their
    /// tails — each stream takes four Flate steps' parameters from the four heads of one of 20 sets, its own set its number
    /// in turn, each head taking its <c>/Length</c> from the first of four tails of 20 streams —; or nests ending at a
    /// stream another nest parsed — every stream takes eight Flate steps' parameters from eight streams, each heading a nest
    /// of 20, the last of each taking its <c>/Length</c> from stream 9.
    /// </summary>
    internal static byte[] UncachedNests(string shape, int streams)
    {
        var direct = new List<(int Number, string Body)>();
        var packed = new List<PackedStream>();
        var shared = shape.StartsWith("nests ending", StringComparison.Ordinal);

        if (shared)
        {
            var heads = new StringBuilder("[");
            direct.Add((9, "<< /Length 0 >>\nstream\n\nendstream"));

            for (var head = 0; head < 8; head++)
            {
                Nest(1000 + (100 * head), 20, "9 0 R", "/Predictor 1 ");
                heads.Append(CultureInfo.InvariantCulture, $"{1000 + (100 * head)} 0 R ");
            }

            direct.Add((5, "[" + string.Join(' ', Enumerable.Repeat("/FlateDecode", 8)) + "]"));
            direct.Add((6, heads.Append(']').ToString()));
        }
        else
        {
            for (var tail = 0; tail < 4; tail++)
            {
                Nest(1000 + (100 * tail), 20, "0", string.Empty);
            }

            for (var head = 0; head < 80; head++)
            {
                direct.Add((5000 + head, string.Create(CultureInfo.InvariantCulture, $"<< /Predictor 1 /Length {1000 + (100 * (head % 4))} 0 R >>\nstream\n\nendstream")));
            }
        }

        for (var k = 0; k < streams; k++)
        {
            var set = 5000 + (4 * (k % 20));
            var entries = shared
                ? "/Filter 5 0 R /DecodeParms 6 0 R"
                : string.Create(CultureInfo.InvariantCulture, $"/Filter [/FlateDecode /FlateDecode /FlateDecode /FlateDecode] /DecodeParms [{set} 0 R {set + 1} 0 R {set + 2} 0 R {set + 3} 0 R]");
            packed.Add(new PackedStream(20_000 + (2 * k), entries, [(20_001 + (2 * k), "<< >>")], PackedData.Plain));
        }

        return PackedFile(packed, direct);

        void Nest(int head, int count, string last, string entries)
        {
            for (var level = 0; level < count; level++)
            {
                var length = level + 1 < count ? string.Create(CultureInfo.InvariantCulture, $"{head + level + 1} 0 R") : last;
                direct.Add((head + level, $"<< {(level == 0 ? entries : string.Empty)}/Length {length} >>\nstream\n\nendstream"));
            }
        }
    }

    /// <summary>
    /// A file of object streams numbered 100,000, 100,002 and so on, each holding one object, numbered after it, that share
    /// <c>/Filter 5 0 R</c>, 64 Flate steps, and <c>/DecodeParms 6 0 R</c>, 64 references to the heads of chains of
    /// <paramref name="links"/> members: the first 64 times <paramref name="links"/> streams hold the members, one each,
    /// every member naming the next and the last of each chain an empty dictionary (#277). When
    /// <paramref name="holdersShare"/> says so, the holders are the first of the <paramref name="streams"/> and take the
    /// filters and parameters themselves; otherwise they need nothing, and the <paramref name="streams"/> after them, each
    /// holding an empty dictionary, take them. When <paramref name="inFile"/> says so, the chains' objects are written
    /// directly in the file instead, and no stream holds them; when <paramref name="twice"/> says so as well, each is written
    /// again right after itself, which a rebuild places it at, and the row of 7 leads nowhere. The streams' data is written
    /// as it is: the walk of their dependencies reads none of it.
    /// </summary>
    internal static byte[] MemberChains(int streams, bool holdersShare, int links = 32, bool inFile = false, bool twice = false)
    {
        const int Heads = 64;
        var holders = Heads * links;
        var heads = new StringBuilder("[");

        for (var head = 0; head < Heads; head++)
        {
            heads.Append(CultureInfo.InvariantCulture, $"{ChainMember(head * links)} 0 R ");
        }

        var direct = new List<(int Number, string Body)>
        {
            (5, "[" + string.Join(' ', Enumerable.Repeat("/FlateDecode", Heads)) + "]"),
            (6, heads.Append(']').ToString()),
        };
        var packed = new List<PackedStream>();
        var total = holdersShare ? Math.Max(streams, holders) : holders + streams;

        for (var k = 0; k < total; k++)
        {
            var member = k >= holders ? "<< >>"
                : (k % links) + 1 < links ? string.Create(CultureInfo.InvariantCulture, $"{ChainMember(k + 1)} 0 R")
                : "<< >>";

            if (inFile && k < holders)
            {
                direct.Add((ChainMember(k), member));
                continue;
            }

            var shares = holdersShare || k >= holders;
            packed.Add(new PackedStream(ChainMember(k) - 1, shares ? "/Filter 5 0 R /DecodeParms 6 0 R" : string.Empty, [(ChainMember(k), member)], PackedData.Plain));
        }

        return PackedFile(packed, direct, twice ? 7 : 0, twice);

        static int ChainMember(int k) => 100_001 + (2 * k);
    }

    /// <summary>
    /// A file of <paramref name="streams"/> object streams, numbered 20,000, 20,002 and so on, each holding one dictionary,
    /// that share <c>/Filter 5 0 R</c>, 64 Flate steps, and <c>/DecodeParms 6 0 R</c>, 64 references to streams of
    /// parameters whose predictor asks for their <c>/Colors</c>, <c>/BitsPerComponent</c> and <c>/Columns</c>, each a stream
    /// as well (#201). Each of the 256 heads a nest of <paramref name="depth"/> streams by
    /// <c>/Length</c>, each taking it from the next, the last from a direct 0: a stream met on a chain has its
    /// <c>/Length</c> followed a level deeper, which the parse of 62 such streams below one met at the second level takes to
    /// the reader's deepest load, 16,128 streams a walk — ending where the chains do, or, of a deeper nest, cut there. The
    /// streams' data is written as it is: the walk of their dependencies reads none of it.
    /// </summary>
    internal static byte[] LengthNests(int streams, int depth = 62)
    {
        const int Steps = 64;
        var direct = new List<(int Number, string Body)>
        {
            (5, "[" + string.Join(' ', Enumerable.Repeat("/FlateDecode", Steps)) + "]"),
        };
        var parameters = new StringBuilder("[");
        var next = 10;

        for (var step = 0; step < Steps; step++)
        {
            var (colors, bits, columns) = (Nest(string.Empty), Nest(string.Empty), Nest(string.Empty));
            var head = Nest(string.Create(CultureInfo.InvariantCulture, $"/Predictor 10 /Colors {colors} 0 R /BitsPerComponent {bits} 0 R /Columns {columns} 0 R "));
            parameters.Append(CultureInfo.InvariantCulture, $"{head} 0 R ");
        }

        direct.Add((6, parameters.Append(']').ToString()));
        var packed = new List<PackedStream>();

        for (var k = 0; k < streams; k++)
        {
            packed.Add(new PackedStream(20_000 + (2 * k), "/Filter 5 0 R /DecodeParms 6 0 R", [(20_001 + (2 * k), "<< >>")], PackedData.Plain));
        }

        return PackedFile(packed, direct);

        // Writes a stream whose dictionary adds `entries`, heading a nest of `depth` streams, each taking its /Length from
        // the next.
        int Nest(string entries)
        {
            var head = next;
            next += depth + 1;

            for (var level = 0; level <= depth; level++)
            {
                var length = level < depth ? string.Create(CultureInfo.InvariantCulture, $"{head + level + 1} 0 R") : "0";
                direct.Add((head + level, $"<< {(level == 0 ? entries : string.Empty)}/Length {length} >>\nstream\n\nendstream"));
            }

            return head;
        }
    }

    /// <summary>
    /// A file where object stream 3 takes its parameters from 6, which object stream 5 holds; 5's <c>/Filter</c> names
    /// <paramref name="elements"/> elements, each the first of a chain of <paramref name="links"/> objects, each held by an
    /// object stream of its own, which needs nothing, and naming the next, the last <c>/FlateDecode</c>; 5's dictionary
    /// carries <paramref name="padding"/> bytes besides, in a string no reader reads, and <paramref name="parameters"/>, when
    /// given, as its <c>/DecodeParms</c>. A walk of 5 is made again for each link, which ends its chain of filters as long as
    /// its holder is not known.
    /// </summary>
    internal static byte[] HiddenHolders(int elements, int links, int padding, string? parameters = null)
    {
        var packed = new List<PackedStream>();
        var filters = new StringBuilder("[");
        var next = 10;

        for (var element = 0; element < elements; element++)
        {
            filters.Append(CultureInfo.InvariantCulture, $"{next + 1} 0 R ");

            for (var link = 0; link < links; link++)
            {
                var body = link + 1 < links ? string.Create(CultureInfo.InvariantCulture, $"{next + 3} 0 R") : "/FlateDecode";
                packed.Add(new PackedStream(next, string.Empty, [(next + 1, body)], PackedData.Flate));
                next += 2;
            }
        }

        var decodeParms = parameters is null ? string.Empty : " /DecodeParms " + parameters;
        packed.Add(new PackedStream(5, $"/Filter {filters.Append(']')} /Pad <{new string('a', padding)}>{decodeParms}", [(6, "<< /Predictor 1 >>")], PackedData.Plain));
        packed.Add(new PackedStream(3, "/DecodeParms 6 0 R", [(4, "<< >>")], PackedData.Flate));
        return PackedFile(packed);
    }

    /// <summary>
    /// A file where object stream 3 takes its parameters from the object X1 holds, and <paramref name="count"/> object
    /// streams X1, X2 and on each take their <c>/Length</c> from the object the next holds, and their <c>/Filter</c> and
    /// <c>/DecodeParms</c> from objects 900 and 901, written in the file: <paramref name="steps"/> Flate steps, each
    /// step's parameters a predictor of 12 whose <c>/Colors</c>, <c>/BitsPerComponent</c> and <c>/Columns</c> are each the
    /// object of a holder of its own, three holders a step, 192 for 64 steps, which need nothing.
    /// </summary>
    /// <param name="count">How many object streams X there are.</param>
    /// <param name="steps">How many Flate steps each X takes.</param>
    /// <param name="first">What X1 holds, which 3 takes its parameters from: 5, or a value that leads elsewhere.</param>
    /// <param name="selfLengthHolders">
    /// Whether each holder takes its <c>/Length</c> from the object it holds, which its decoding then reads while the reader
    /// holds it in its loads: its standing is unlike at its first load.
    /// </param>
    internal static byte[] PendingFanout(int count, int steps = 64, string first = "5", bool selfLengthHolders = false)
    {
        var packed = new List<PackedStream>();
        var parameters = new StringBuilder("[");
        var next = 1000;

        for (var step = 0; step < steps; step++)
        {
            parameters.Append(CultureInfo.InvariantCulture, $"<< /Predictor 12 /Colors {next + 1} 0 R /BitsPerComponent {next + 3} 0 R /Columns {next + 5} 0 R >> ");

            for (var value = 0; value < 3; value++)
            {
                var length = selfLengthHolders ? string.Create(CultureInfo.InvariantCulture, $"{next + 1} 0 R") : null;
                packed.Add(new PackedStream(next, string.Empty, [(next + 1, "1")], PackedData.Flate, Length: length));
                next += 2;
            }
        }

        var firstX = next;

        for (var x = 0; x < count; x++)
        {
            var number = firstX + (2 * x);
            var length = x + 1 < count ? string.Create(CultureInfo.InvariantCulture, $"{number + 3} 0 R") : null;
            packed.Add(new PackedStream(number, "/Filter 900 0 R /DecodeParms 901 0 R", [(number + 1, x == 0 ? first : "5")], PackedData.Plain, Length: length));
        }

        packed.Add(new PackedStream(3, string.Create(CultureInfo.InvariantCulture, $"/DecodeParms {firstX + 1} 0 R"), [(4, "<< >>")], PackedData.Flate));
        return PackedFile(packed, [(900, "[" + string.Join(' ', Enumerable.Repeat("/FlateDecode", steps)) + "]"), (901, parameters.Append(']').ToString())]);
    }

    /// <summary>
    /// A file where object stream 3 takes its parameters from the object X1 holds, and <paramref name="levels"/> object
    /// streams X1, X2 and on each take their <c>/Length</c> from the object an object stream of their own holds, naming the
    /// object the next X holds, the last X its own length; and <paramref name="steps"/> Flate steps, the step j of Xi taking
    /// its predictor from the object B(i - 1, j) holds, 2, and its <c>/BitsPerComponent</c> from the one B(i, j) holds: one
    /// holder for each step of each level, and of one level before the first, each needing nothing.
    /// </summary>
    internal static byte[] LayeredWaits(int levels, int steps)
    {
        var packed = new List<PackedStream>();
        var filters = "[" + string.Join(' ', Enumerable.Repeat("/FlateDecode", steps)) + "]";
        var lengths = 1000 + (2 * (levels + 1) * steps);
        var first = lengths + (2 * levels);

        for (var holder = 0; holder < (levels + 1) * steps; holder++)
        {
            packed.Add(new PackedStream(1000 + (2 * holder), string.Empty, [(1001 + (2 * holder), "2")], PackedData.Flate));
        }

        for (var level = 1; level <= levels; level++)
        {
            var number = first + (2 * level);
            var parameters = new StringBuilder("[");

            for (var step = 0; step < steps; step++)
            {
                parameters.Append(CultureInfo.InvariantCulture, $"<< /Predictor {Holder(level - 1, step)} 0 R /BitsPerComponent {Holder(level, step)} 0 R >> ");
            }

            string? length = null;

            if (level < levels)
            {
                packed.Add(new PackedStream(lengths + (2 * level), string.Empty, [(lengths + (2 * level) + 1, string.Create(CultureInfo.InvariantCulture, $"{number + 3} 0 R"))], PackedData.Flate));
                length = string.Create(CultureInfo.InvariantCulture, $"{lengths + (2 * level) + 1} 0 R");
            }

            packed.Add(new PackedStream(number, $"/Filter {filters} /DecodeParms {parameters.Append(']')}", [(number + 1, "7")], PackedData.Plain, Length: length));
        }

        packed.Add(new PackedStream(3, string.Create(CultureInfo.InvariantCulture, $"/DecodeParms {first + 3} 0 R"), [(4, "<< >>")], PackedData.Flate));
        return PackedFile(packed);

        int Holder(int level, int step) => 1001 + (2 * ((level * steps) + step));
    }

    /// <summary>
    /// A file where <paramref name="count"/> object streams, numbered 10, 12 and so on, each take their parameters from the
    /// object a holder of their own holds, numbered after them, whose <c>/Length</c> is that object: the holder's standing
    /// is unlike at its first load. Each holder takes 64 Flate steps, whose parameters are 64 objects written in the file,
    /// numbered after the holders, each carrying <paramref name="padding"/> bytes in a string no reader reads.
    /// </summary>
    internal static byte[] HoldersUnlikeAtTheirLength(int count, int padding)
    {
        const int Steps = 64;
        var packed = new List<PackedStream>();
        var direct = new List<(int Number, string Body)>();
        var filters = "[" + string.Join(' ', Enumerable.Repeat("/FlateDecode", Steps)) + "]";
        var pad = new string('a', padding);

        for (var k = 0; k < count; k++)
        {
            var holder = 10 + (2 * (count + k));
            var parameters = new StringBuilder("[");

            for (var step = 0; step < Steps; step++)
            {
                var number = 10 + (4 * count) + (Steps * k) + step;
                direct.Add((number, $"<< /Predictor 1 /Pad <{pad}> >>"));
                parameters.Append(CultureInfo.InvariantCulture, $"{number} 0 R ");
            }

            var length = string.Create(CultureInfo.InvariantCulture, $"{holder + 1} 0 R");
            packed.Add(new PackedStream(holder, $"/Filter {filters} /DecodeParms {parameters.Append(']')}", [(holder + 1, "<< /Predictor 1 >>")], PackedData.Plain, Length: length));
            packed.Add(new PackedStream(10 + (2 * k), string.Create(CultureInfo.InvariantCulture, $"/DecodeParms {holder + 1} 0 R"), [(11 + (2 * k), "<< >>")], PackedData.Flate));
        }

        return PackedFile(packed, direct);
    }

    /// <summary>
    /// A file of <paramref name="count"/> object streams X1, X2 and on, numbered 1,000, 1,002 and so on, each holding one
    /// object and taking its <c>/Length</c> from the object the next holds, the last from its own, and its
    /// <c>/DecodeParms</c> from an array of <paramref name="elements"/> zeros written in its dictionary, or from parameters
    /// holding that array under the key <paramref name="key"/>.
    /// </summary>
    internal static byte[] WaitingArrays(int count, int elements, string? key = null)
    {
        var zeros = "[" + new StringBuilder().Insert(0, "0 ", elements) + "]";
        zeros = key is null ? zeros : "<< /Predictor 1 " + key + " " + zeros + " >>";
        var packed = new List<PackedStream>();

        for (var x = 0; x < count; x++)
        {
            var number = 1000 + (2 * x);
            var length = string.Create(CultureInfo.InvariantCulture, $"{(x + 1 < count ? number + 3 : number + 1)} 0 R");
            packed.Add(new PackedStream(number, "/DecodeParms " + zeros, [(number + 1, "5")], PackedData.Plain, Length: length));
        }

        return PackedFile(packed);
    }

    /// <summary>
    /// A file where object stream 3 takes its parameters from 6, which object stream 5 holds; 5's <c>/DecodeParms</c> names
    /// <paramref name="nests"/> streams, each heading a nest of <paramref name="levels"/> streams, each taking its
    /// <c>/Length</c> from a chain of <paramref name="links"/> objects, each held by an object stream of its own, which needs
    /// nothing, the last naming the next stream of the nest; the last stream's is 0. The row of object 7 leads nowhere, so
    /// that settling the index rebuilds it.
    /// </summary>
    internal static byte[] HiddenNests(int nests, int levels, int links)
    {
        var packed = new List<PackedStream>();
        var direct = new List<(int Number, string Body)>();
        var heads = new StringBuilder("[");
        var (next, holder) = (100_000, 10);

        for (var nest = 0; nest < nests; nest++)
        {
            var head = next;
            next += levels;
            heads.Append(CultureInfo.InvariantCulture, $"{head} 0 R ");

            for (var level = 0; level < levels; level++)
            {
                var length = "0";

                if (level + 1 < levels)
                {
                    length = string.Create(CultureInfo.InvariantCulture, $"{holder + 1} 0 R");

                    for (var link = 0; link < links; link++)
                    {
                        var body = string.Create(CultureInfo.InvariantCulture, $"{(link + 1 < links ? holder + 3 : head + level + 1)} 0 R");
                        packed.Add(new PackedStream(holder, string.Empty, [(holder + 1, body)], PackedData.Plain));
                        holder += 2;
                    }
                }

                direct.Add((head + level, $"<< {(level == 0 ? "/Predictor 1 " : string.Empty)}/Length {length} >>\nstream\n\nendstream"));
            }
        }

        packed.Add(new PackedStream(5, $"/Filter [{string.Concat(Enumerable.Repeat("/FlateDecode ", nests))}] /DecodeParms {heads.Append(']')}", [(6, "<< /Predictor 1 >>")], PackedData.Plain));
        packed.Add(new PackedStream(3, "/DecodeParms 6 0 R", [(4, "<< >>")], PackedData.Flate));
        return PackedFile(packed, direct, brokenRow: 7);
    }

    /// <summary>
    /// Writes a file whose index is a cross-reference stream, with the object streams <paramref name="streams"/>
    /// describes and the objects <paramref name="direct"/> gives written directly; objects 1 and 2 are a catalog and
    /// an empty page tree. The row of <paramref name="brokenRow"/>, when there is one, places it at offset 1, where no
    /// object is, so that loading it rebuilds the index. When <paramref name="twice"/> says so, each object
    /// <paramref name="direct"/> gives is written again right after itself, its row placing the first.
    /// </summary>
    private static byte[] PackedFile(IReadOnlyList<PackedStream> streams, IReadOnlyList<(int Number, string Body)>? direct = null, int brokenRow = 0, bool twice = false)
    {
        using var output = new MemoryStream();
        void Write(string text) => output.Write(Encoding.ASCII.GetBytes(text));

        Write("%PDF-1.5\n");
        var offsets = new Dictionary<int, long>();
        var packed = new Dictionary<int, (int Stream, int Index)>();
        var objects = new List<(int Number, string Body)> { (1, "<< /Type /Catalog /Pages 2 0 R >>"), (2, "<< /Type /Pages /Kids [] /Count 0 >>") };
        objects.AddRange(direct ?? []);

        for (var i = 0; i < objects.Count; i++)
        {
            var (number, body) = objects[i];
            offsets[number] = output.Position;
            Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n"));

            if (twice && i >= 2)
            {
                Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n"));
            }
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
                $"{stream.Number} 0 obj\n<< /Type /ObjStm /N {stream.Members.Count} /First {header.Length} {stream.Dictionary}{filter} /Length {stream.Length ?? data.Length.ToString(CultureInfo.InvariantCulture)} >>\nstream\n"));
            output.Write(data);
            Write("\nendstream\nendobj\n");
        }

        var xrefNumber = Math.Max(Math.Max(offsets.Keys.Max(), packed.Keys.Max()), brokenRow) + 1;
        var xrefOffset = output.Position;
        offsets[xrefNumber] = xrefOffset;
        using var rows = new MemoryStream();

        for (var number = 0; number <= xrefNumber; number++)
        {
            if (number == brokenRow && brokenRow > 0)
            {
                rows.Write([1, 0, 0, 0, 1, 0, 0]);
            }
            else if (offsets.TryGetValue(number, out var offset))
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
    /// The catalog and a page tree whose root, giving a media box and resources, lists <paramref name="kids"/> kids written
    /// as <paramref name="kid"/> — or lists <paramref name="nodes"/> nodes, each listing them —: <c>0 </c> by default, a
    /// number, which stands for a page the tree cannot read.
    /// </summary>
    internal static byte[] Kids(int kids, int nodes = 1, string kid = "0 ")
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>") };
        var listed = new StringBuilder(kids * kid.Length);

        for (var k = 0; k < kids; k++)
        {
            listed.Append(kid);
        }

        if (nodes == 1)
        {
            rows[2] = file.Object(2, $"<< /Type /Pages /Count 1 /MediaBox [0 0 10 10] /Resources << >> /Kids [{listed}] >>");
        }
        else
        {
            var below = new StringBuilder();

            for (var node = 3; node < nodes + 3; node++)
            {
                below.Append(CultureInfo.InvariantCulture, $"{node} 0 R ");
                rows[node] = file.Object(node, $"<< /Type /Pages /Parent 2 0 R /Count 1 /Kids [{listed}] >>");
            }

            rows[2] = file.Object(2, string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Type /Pages /Count {nodes} /MediaBox [0 0 10 10] /Resources << >> /Kids [{below}] >>"));
        }

        var size = nodes == 1 ? 3 : nodes + 3;
        file.Table(size, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {size} /Root 1 0 R"));
        return file.Bytes;
    }

    /// <summary>
    /// The catalog and a page tree whose root, giving neither a media box nor resources, lists <paramref name="kids"/> kids
    /// written in its array as <paramref name="kid"/>: an empty dictionary by default, a page with neither.
    /// </summary>
    internal static byte[] KidsWrittenInTheArray(int kids, string kid = "<<>> ")
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>") };
        var listed = new StringBuilder(kids * kid.Length);

        for (var k = 0; k < kids; k++)
        {
            listed.Append(kid);
        }

        rows[2] = file.Object(2, string.Create(CultureInfo.InvariantCulture, $"<< /Type /Pages /Count {kids} /Kids [{listed}] >>"));
        file.Table(3, rows, "/Size 3 /Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// The catalog, an empty page tree, <paramref name="objects"/> written as objects of their own, and a Flate cross-reference
    /// stream, <c>/W [1 4 2]</c>, whose rows place every other number from 3 to <paramref name="entries"/> + 2 as
    /// <paramref name="row"/> says.
    /// </summary>
    internal static byte[] CompressedRows(int entries, Func<int, (int Type, long Field2, int Field3)> row, params (int Number, string Body)[] objects)
    {
        using var output = new MemoryStream();
        void Write(string text) => output.Write(Encoding.Latin1.GetBytes(text));

        Write("%PDF-1.5\n");
        var offsets = new Dictionary<int, long> { [1] = output.Position };
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[2] = output.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

        foreach (var (number, body) in objects)
        {
            offsets[number] = output.Position;
            Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n"));
        }

        var xref = entries + 3;
        var xrefOffset = output.Position;
        var rows = new byte[(xref + 1) * 7];

        for (var number = 0; number <= xref; number++)
        {
            var (type, field2, field3) = number == 0 ? (0, 0L, 65535)
                : number == xref ? (1, xrefOffset, 0)
                : offsets.TryGetValue(number, out var at) ? (1, at, 0)
                : row(number);
            rows[number * 7] = (byte)type;
            BinaryPrimitives.WriteUInt32BigEndian(rows.AsSpan((number * 7) + 1), (uint)field2);
            BinaryPrimitives.WriteUInt16BigEndian(rows.AsSpan((number * 7) + 5), (ushort)field3);
        }

        var data = Compress(rows);
        Write(string.Create(
            CultureInfo.InvariantCulture,
            $"{xref} 0 obj\n<< /Type /XRef /Size {xref + 1} /W [1 4 2] /Root 1 0 R /Filter /FlateDecode /Length {data.Length} >>\nstream\n"));
        output.Write(data);
        Write(string.Create(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF\n"));
        return output.ToArray();
    }

    /// <summary>A cross-reference stream whose <paramref name="entries"/> rows place their objects outside the file.</summary>
    internal static byte[] EntriesOutsideTheFile(int entries) => CompressedRows(entries, _ => (1, 0x7FFF_FFF0L, 0));

    /// <summary>
    /// A cross-reference stream that places objects 3 to <paramref name="entries"/> + 2 in object stream 5, each at the index
    /// its number less 3 gives, where the stream lists object 3 alone: every other is a member its header does not list.
    /// </summary>
    internal static byte[] MembersTheHeaderDoesNotList(int entries) =>
        CompressedRows(entries, number => (2, 5, number - 3), (5, "<< /Type /ObjStm /N 1 /First 4 /Length 9 >>\nstream\n3 0 <<>> \nendstream"));

    /// <summary>A cross-reference stream whose <paramref name="rows"/> rows each place an object in an object stream the index lacks.</summary>
    internal static byte[] ObjectStreamsTheIndexLacks(int rows) => CompressedRows(rows, number => (2, 4_000_000 + number, 0));

    /// <summary>A classic table whose <paramref name="entries"/> rows past object 2 each place their object where object 1's header is.</summary>
    internal static byte[] EntriesAtObjectOne(int entries)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>(entries + 2) { [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>") };
        rows[2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        for (var number = 3; number < entries + 3; number++)
        {
            rows[number] = rows[1];
        }

        file.Table(entries + 3, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {entries + 3} /Root 1 0 R"));
        return file.Bytes;
    }

    /// <summary>
    /// The catalog and an empty page tree, indexed by a chain of <paramref name="sections"/> classic tables, each placing both
    /// again under a trailer that gives <c>/Size 99</c> where 3 is right, and names the table before it through
    /// <c>/Prev</c>. Gives where each table starts, the oldest first.
    /// </summary>
    internal static (byte[] File, long[] Tables) SectionsGivingAWrongSize(int sections)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>") };
        rows[2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var tables = new long[sections];

        for (var section = 0; section < sections; section++)
        {
            tables[section] = file.Table(
                3,
                rows,
                section == 0
                    ? "/Size 99 /Root 1 0 R"
                    : string.Create(CultureInfo.InvariantCulture, $"/Size 99 /Root 1 0 R /Prev {tables[section - 1]}"));
        }

        return (file.Bytes, tables);
    }

    /// <summary>
    /// The catalog and an empty page tree, indexed by cross-reference stream 3, whose <c>/Index</c> gives its one subsection,
    /// then <paramref name="pairs"/> subsections of no rows, each a pair of <paramref name="element"/>.
    /// </summary>
    private static byte[] EmptySubsections(int pairs, string element)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, (int, long, int)> { [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0) };
        rows[2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0);
        var index = new StringBuilder("/Root 1 0 R /Index [0 4");

        for (var pair = 0; pair < pairs; pair++)
        {
            index.Append(' ').Append(element).Append(' ').Append(element);
        }

        file.CrossReferenceStream(3, 4, rows, index.Append(']').ToString());
        return file.Bytes;
    }

    /// <summary>
    /// A catalog that lists <paramref name="objects"/> objects, each holding <paramref name="held"/>: by default, a reference
    /// to an object the file lacks, and a key with a null character.
    /// </summary>
    internal static byte[] ObjectsHolding(int objects, string held = "/M 9999999 0 R /N#00 1")
    {
        var file = new HandWrittenFile();
        var listed = new StringBuilder(objects * 10);

        for (var number = 3; number < objects + 3; number++)
        {
            listed.Append(CultureInfo.InvariantCulture, $"{number} 0 R ");
        }

        var rows = new Dictionary<int, long>(objects + 2) { [1] = file.Object(1, $"<< /Type /Catalog /Pages 2 0 R /All [{listed}] >>") };
        rows[2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        for (var number = 3; number < objects + 3; number++)
        {
            rows[number] = file.Object(number, $"<< {held} >>");
        }

        file.Table(objects + 3, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {objects + 3} /Root 1 0 R"));
        return file.Bytes;
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
    /// Flate filter: rows 1 to 3 place objects 1 to 3, every other row is of type 3, which reads as a reference to the null
    /// object, its number indexed as a free one's is (#221).
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
    /// rows for objects 4 onward whose offsets need all of eight bytes, past what a long holds — or that give
    /// <paramref name="offset"/>, where one a long holds is given, and stand.
    /// </summary>
    private static byte[] RefusedRows(int refused, out long section, ulong offset = ulong.MaxValue)
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
            Row(1, offset);
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

    /// <summary>
    /// Opens the catalog and an empty page tree followed by <paramref name="keywords"/> copies of <paramref name="tail"/>,
    /// and no table, so that opening rebuilds the index, and gives what opening allocated, and the document's diagnostics.
    /// </summary>
    private static long AllocatedOpening(int keywords, string tail, PdfReaderOptions options, out PdfDiagnostics diagnostics)
    {
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Raw(string.Concat(Enumerable.Repeat(tail, keywords)));
        var bytes = file.Bytes;

        var allocated = Allocations.Least(
            () =>
            {
                using var document = Measure(() => PdfDocument.Open(bytes, options));
                return document.Diagnostics;
            },
            out diagnostics);

        return allocated;
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
    /// Opens <paramref name="file"/> and reads its object 3 on the suite's small stack: what was read, and the document's
    /// diagnostics.
    /// </summary>
    /// <remarks>
    /// The thread asks for 256 KB, and the C library could give it a stack another thread left, of up to four times that:
    /// a chain these tests nest to run short of 256 KB, but not of four times that, could then be read uncut, as the
    /// streams nested in 32 arrays were in the full suite under coverage. No thread of the suite asks for such a stack
    /// (<see cref="Stacks"/>).
    /// </remarks>
    private static (PdfObject First, PdfDiagnostics Diagnostics) ReadObject3OnASmallStack(byte[] file) =>
        Stacks.Run(
            Stacks.Small,
            () =>
            {
                using var document = PdfDocument.Open(file);
                return (document.GetObject(new PdfObjectId(3)), document.Diagnostics);
            });

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

    /// <summary>
    /// An object stream to write: its number, what its dictionary adds, the objects it holds and its data, and its
    /// <c>/Length</c> when it is not that of its data.
    /// </summary>
    private sealed record PackedStream(int Number, string Dictionary, IReadOnlyList<(int Number, string Body)> Members, PackedData Data, int Padding = 0, string? Length = null);
}
