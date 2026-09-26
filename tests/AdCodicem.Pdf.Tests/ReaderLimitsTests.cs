using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The reader's guards as options (ADR 34): each one is on by default, keeps what fits and names the property
/// that lifts it when reached, reads the document whole once raised, and throws when the caller asks it to.
/// </summary>
public class ReaderLimitsTests
{
    private const string Catalog = "<< /Type /Catalog /Pages 2 0 R >>";
    private const string Pages = "<< /Type /Pages /Kids [] /Count 0 >>";

    /// <summary>A guard for each bound of <see cref="PdfReaderLimits"/>, the trailer's reached two ways.</summary>
    public static TheoryData<string> Guards =>
        ["decoded stream", "object", "section length", "section count", "trailer", "cross-reference stream"];

    [Fact]
    public void The_defaults_are_the_bounds_the_reader_kept_before_they_were_options()
    {
        var limits = PdfReaderLimits.Default;

        limits.MaxDecodedStreamLength.Should().Be(256 * 1024 * 1024);
        limits.MaxObjectLength.Should().Be(16 * 1024 * 1024);
        limits.MaxXRefSectionLength.Should().Be(64 * 1024 * 1024);
        limits.MaxXRefSectionCount.Should().Be(1024);
        limits.MaxTrailerLength.Should().Be(64 * 1024);
        PdfReaderOptions.Default.Limits.Should().BeSameAs(limits);
        PdfReaderOptions.Default.ThrowOnLimit.Should().BeFalse();
    }

    [Fact]
    public void Unbounded_takes_every_guard_to_the_most_the_implementation_holds()
    {
        var limits = PdfReaderLimits.Unbounded;

        limits.MaxDecodedStreamLength.Should().Be(Array.MaxLength);
        limits.MaxObjectLength.Should().Be(Array.MaxLength);
        limits.MaxXRefSectionLength.Should().Be(Array.MaxLength);
        limits.MaxXRefSectionCount.Should().Be(int.MaxValue);
        limits.MaxTrailerLength.Should().Be(Array.MaxLength);
    }

    [Theory]
    [InlineData(nameof(PdfReaderLimits.MaxDecodedStreamLength), 0)]
    [InlineData(nameof(PdfReaderLimits.MaxObjectLength), -1)]
    [InlineData(nameof(PdfReaderLimits.MaxXRefSectionLength), int.MinValue)]
    [InlineData(nameof(PdfReaderLimits.MaxXRefSectionCount), 0)]
    [InlineData(nameof(PdfReaderLimits.MaxTrailerLength), -1)]
    public void A_limit_of_zero_or_less_is_refused_and_named(string limit, int value)
    {
        var setting = () => With(PdfReaderLimits.Default, limit, value);

        setting.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(limit);
    }

    [Fact]
    public void A_length_beyond_what_an_array_holds_is_taken_as_that_length()
    {
        var limits = PdfReaderLimits.Default with
        {
            MaxDecodedStreamLength = int.MaxValue,
            MaxObjectLength = int.MaxValue,
            MaxXRefSectionLength = int.MaxValue,
            MaxXRefSectionCount = int.MaxValue,
            MaxTrailerLength = int.MaxValue,
        };

        limits.Should().Be(PdfReaderLimits.Unbounded);
    }

    [Fact]
    public void The_options_refuse_limits_that_are_not_there()
    {
        var setting = () => PdfReaderOptions.Default with { Limits = null! };

        setting.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [MemberData(nameof(Guards))]
    public void Reaching_a_guard_keeps_what_fits_and_names_the_property_that_lifts_it(string guard)
    {
        var @case = Case.For(guard);
        using var document = PdfDocument.Open(@case.File, new PdfReaderOptions { Limits = @case.Reached });

        @case.Read(document).Should().NotBe(@case.Whole, $"the {guard} guard cuts what is read");

        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == @case.Code).Which;
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Message.Should().Be($"{@case.Reported} Raise PdfReaderLimits.{@case.LimitName} to read past it.");
        report.Position.Should().Be(@case.Position);

        if (@case.ReportsNothingElse)
        {
            // What the parser met at the window's edge is the reader's cut, not damage in the file: only the
            // guard is reported.
            document.Diagnostics.Should().ContainSingle();
        }
    }

    [Theory]
    [MemberData(nameof(Guards))]
    public void Raising_a_guard_reads_the_document_whole(string guard)
    {
        var @case = Case.For(guard);
        using var document = PdfDocument.Open(@case.File, new PdfReaderOptions { Limits = @case.Raised });

        @case.Read(document).Should().Be(@case.Whole);
        document.Diagnostics.Should().BeEmpty($"a sound file read within its guards reports nothing ({guard})");
    }

    [Theory]
    [MemberData(nameof(Guards))]
    public void A_document_opened_to_throw_throws_from_the_operation_that_reaches_the_guard(string guard)
    {
        var @case = Case.For(guard);
        var options = new PdfReaderOptions { Limits = @case.Reached, ThrowOnLimit = true };
        PdfDocument? document = null;

        try
        {
            var reading = () =>
            {
                document = PdfDocument.Open(@case.File, options);
                @case.Read(document);
            };

            var thrown = reading.Should().Throw<PdfLimitExceededException>().Which;

            (document is null).Should().Be(@case.ThrowsOnOpening, $"the {guard} guard is reached {(@case.ThrowsOnOpening ? "while" : "after")} opening");
            thrown.Code.Should().Be(@case.Code);
            thrown.LimitName.Should().Be(@case.LimitName);
            thrown.Limit.Should().Be(Bound(@case.Reached, @case.LimitName));
            thrown.Position.Should().Be(@case.Position);
            thrown.Message.Should().Be($"{@case.Reported} Raise PdfReaderLimits.{@case.LimitName} to read past it.");
        }
        finally
        {
            document?.Dispose();
        }
    }

    [Fact]
    public void A_stream_decoded_without_diagnostics_reports_the_bound_to_its_document()
    {
        var @case = Case.For("decoded stream");
        using var document = PdfDocument.Open(@case.File, new PdfReaderOptions { Limits = @case.Reached });

        // Opening decodes nothing: the stream carries its document's guards until it is decoded.
        document.Diagnostics.Should().BeEmpty();
        var decoded = document.GetObject(new PdfObjectId(3)).AsStream().Required().Decode();

        decoded.Length.Should().Be(1000);
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
    }

    [Fact]
    public void An_object_read_again_after_the_cache_let_it_go_is_reported_once_and_throws_each_time()
    {
        var builder = new TestPdfBuilder().WithObject(1, Catalog).WithObject(2, Pages).WithObject(3, LongArray);
        for (var number = 4; number < 80; number++)
        {
            builder.WithObject(number, "null");
        }

        var file = builder.BuildClassic(rootNumber: 1);
        var limits = PdfReaderLimits.Default with { MaxObjectLength = 4096 };

        using (var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits, ObjectCacheCapacity = 64 }))
        {
            ReadTwiceAcrossTheCache(document);
            document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.LimitObject).Should().ContainSingle();
        }

        using var strict = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits, ObjectCacheCapacity = 64, ThrowOnLimit = true });

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var reading = () => strict.GetObject(new PdfObjectId(3));
            reading.Should().Throw<PdfLimitExceededException>($"attempt {attempt + 1} reaches the guard again");
        }

        static void ReadTwiceAcrossTheCache(PdfDocument document)
        {
            document.GetObject(new PdfObjectId(3)).AsArray().Required();
            for (var number = 4; number < 80; number++)
            {
                document.GetObject(new PdfObjectId(number));
            }

            document.GetObject(new PdfObjectId(3)).AsArray().Required();
        }
    }

    [Fact]
    public void A_rebuild_that_reaches_a_guard_finishes_the_index_before_it_throws()
    {
        // Objects 11 and 13 live in object streams the index does not describe, so asking for 13 rebuilds it.
        // The first object stream decodes past the bound; the second is where 13 is. A rebuild abandoned at the
        // first would have left 13 out of the index for good, since a rebuild is never run twice.
        var header = "11 0 ";
        var packed = header + "(" + new string('y', 2000) + ")";
        var file = new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, Pages)
            .Stream(10, $"/Type /ObjStm /N 1 /First {header.Length} /Filter /ASCIIHexDecode", Hex(packed))
            .Stream(12, "/Type /ObjStm /N 1 /First 5", "13 0 (found)")
            .BuildClassic(rootNumber: 1);
        var options = new PdfReaderOptions
        {
            Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 1000 },
            ThrowOnLimit = true,
        };

        using var document = PdfDocument.Open(file, options);
        var rebuilding = () => document.GetObject(new PdfObjectId(13));

        rebuilding.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be("MaxDecodedStreamLength");
        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(13)).AsText().Should().Be("found");
    }

    [Fact]
    public void A_rebuild_that_reaches_a_guard_looking_for_the_catalogue_still_finds_it()
    {
        // Object 1, the catalogue the index names, is redefined as null after the file's end, where only a
        // rebuild sees it. Rebuilding then looks for another catalogue among every object, and object 3 on
        // the way reaches the object bound; the search goes on to object 4 before the guard throws.
        var written = new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, Pages)
            .WithObject(3, LongArray)
            .WithObject(4, Catalog)
            .BuildClassic(rootNumber: 1);
        byte[] file = [.. written, .. "1 0 obj\nnull\nendobj\n"u8];
        var options = new PdfReaderOptions
        {
            Limits = PdfReaderLimits.Default with { MaxObjectLength = 4096 },
            ThrowOnLimit = true,
        };

        using var document = PdfDocument.Open(file, options);
        var rebuilding = () => document.GetObject(new PdfObjectId(99));

        rebuilding.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be("MaxObjectLength");
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    [Fact]
    public void An_object_cut_by_its_bound_is_not_reported_as_damage()
    {
        // The bound falls just after a key, where the parser, finding no value, reports an object the file
        // ended in the middle of. The file did not end there, the reader stopped: only the guard is reported.
        // "3 0 obj\n" and "<< /Pad (" take 17 bytes, ") /Key " seven: 4,072 bytes of padding end the key at 4 KB.
        var file = new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, Pages)
            .WithObject(3, $"<< /Pad ({new string('s', 4072)}) /Key 1 >>")
            .BuildClassic(rootNumber: 1);
        var limits = PdfReaderLimits.Default with { MaxObjectLength = 4096 };

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        document.GetObject(new PdfObjectId(3)).AsDictionary().Required();

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
    }

    [Fact]
    public void An_object_is_read_no_further_than_its_bound()
    {
        var file = Case.For("object").File;
        using var source = new StrictCountingSource(file);
        using var document = PdfDocument.Open(source, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 4096 } }, ownsSource: false);
        var before = source.BytesRead;

        document.GetObject(new PdfObjectId(3)).AsArray().Required();

        (source.BytesRead - before).Should().BeLessThanOrEqualTo(4096);
    }

    [Fact]
    public void An_object_whose_header_alone_runs_past_the_bound_reports_the_bound()
    {
        var file = new TestPdfBuilder().WithObject(1, Catalog).WithObject(2, Pages).BuildClassic(rootNumber: 1);

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 4 } });

        document.Diagnostics.Should().Contain(entry =>
            entry.Code == PdfDiagnosticCodes.LimitObject && entry.Position == OffsetOf(file, "1 0 obj"));
        document.Catalog.Should().BeNull("no object can be read within four bytes");
    }

    [Theory]
    [InlineData("xre")]
    [InlineData("trai")]
    public void A_table_whose_keywords_the_bound_cuts_reports_the_bound(string cut)
    {
        // A bound that ends inside "xref", or inside the "trailer" that follows the entries, leaves a keyword
        // the reader must not take for another: the table was cut by the bound, not written so.
        var file = new TestPdfBuilder().WithObject(1, Catalog).WithObject(2, Pages).BuildClassic(rootNumber: 1);
        var table = OffsetOf(file, "xref\n");
        var bound = cut == "xre" ? 3 : OffsetOf(file, "trailer") - table + cut.Length;

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = bound } });

        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.LimitXRefSectionLength)
            .Which.Position.Should().Be(table);
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    [Fact]
    public void A_trailer_that_the_table_bound_cuts_is_read_through_a_window_of_its_own()
    {
        // The entries fit within the bound and the trailer starts within it, but runs past it: the trailer is
        // not the table, and is read whole within its own bound.
        var file = new TestPdfBuilder().WithObject(1, Catalog).WithObject(2, Pages).BuildClassic(rootNumber: 1);
        var bound = OffsetOf(file, "trailer\n<< /Size 3") + "trailer\n<< /Size 3".Length - OffsetOf(file, "xref\n");

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = bound } });

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Opening_releases_a_source_it_owns_when_a_guard_throws(bool ownsSource)
    {
        var @case = Case.For("section length");
        using var source = new TrackedSource(@case.File);
        var opening = () => PdfDocument.Open(source, new PdfReaderOptions { Limits = @case.Reached, ThrowOnLimit = true }, ownsSource);

        opening.Should().Throw<PdfLimitExceededException>();
        source.Disposed.Should().Be(ownsSource);
    }

    [Fact]
    public void Opening_an_empty_input_releases_the_source_it_owns()
    {
        using var source = new TrackedSource([]);
        var opening = () => PdfDocument.Open(source, options: null, ownsSource: true);

        opening.Should().Throw<PdfFormatException>();
        source.Disposed.Should().BeTrue();
    }

    private static string LongArray { get; } = "[" + string.Join(' ', Enumerable.Range(0, 2000)) + "]";

    private static PdfReaderLimits With(PdfReaderLimits limits, string limit, int value) => limit switch
    {
        nameof(PdfReaderLimits.MaxDecodedStreamLength) => limits with { MaxDecodedStreamLength = value },
        nameof(PdfReaderLimits.MaxObjectLength) => limits with { MaxObjectLength = value },
        nameof(PdfReaderLimits.MaxXRefSectionLength) => limits with { MaxXRefSectionLength = value },
        nameof(PdfReaderLimits.MaxXRefSectionCount) => limits with { MaxXRefSectionCount = value },
        nameof(PdfReaderLimits.MaxTrailerLength) => limits with { MaxTrailerLength = value },
        _ => throw new ArgumentOutOfRangeException(nameof(limit), limit, "Not a reader limit."),
    };

    private static int Bound(PdfReaderLimits limits, string limit) => limit switch
    {
        nameof(PdfReaderLimits.MaxDecodedStreamLength) => limits.MaxDecodedStreamLength,
        nameof(PdfReaderLimits.MaxObjectLength) => limits.MaxObjectLength,
        nameof(PdfReaderLimits.MaxXRefSectionLength) => limits.MaxXRefSectionLength,
        nameof(PdfReaderLimits.MaxXRefSectionCount) => limits.MaxXRefSectionCount,
        nameof(PdfReaderLimits.MaxTrailerLength) => limits.MaxTrailerLength,
        _ => throw new ArgumentOutOfRangeException(nameof(limit), limit, "Not a reader limit."),
    };

    private static string Hex(string text) => Convert.ToHexString(Encoding.ASCII.GetBytes(text)) + ">";

    private static string IndexState(PdfDocument document) => document.WasRepaired ? "rebuilt" : "as written";

    private static int OffsetOf(byte[] file, string text) =>
        Encoding.Latin1.GetString(file).IndexOf(text, StringComparison.Ordinal);

    /// <summary>
    /// A sound document that reaches one guard: the limits that reach it, the limits that read it whole, what
    /// reading it observes, and what the guard reports.
    /// </summary>
    private sealed record Case(
        string LimitName,
        string Code,
        byte[] File,
        PdfReaderLimits Reached,
        PdfReaderLimits Raised,
        Func<PdfDocument, string> Read,
        string Whole,
        string Reported,
        long Position,
        bool ThrowsOnOpening,
        bool ReportsNothingElse)
    {
        public static Case For(string guard) => guard switch
        {
            "decoded stream" => DecodedStream(),
            "object" => LongObject(),
            "section length" => LongSection(),
            "section count" => ManySections(),
            "trailer" => LongTrailer(),
            "cross-reference stream" => LongXRefStreamDictionary(),
            _ => throw new ArgumentOutOfRangeException(nameof(guard), guard, "Not a guard."),
        };

        private static Case DecodedStream()
        {
            // Ten thousand bytes, which a bound of a thousand cuts when the stream is decoded.
            var file = new TestPdfBuilder()
                .WithObject(1, Catalog)
                .WithObject(2, Pages)
                .Stream(3, "/Filter /ASCIIHexDecode", Hex(new string('A', 10_000)))
                .BuildClassic(rootNumber: 1);

            return new Case(
                nameof(PdfReaderLimits.MaxDecodedStreamLength),
                PdfDiagnosticCodes.LimitDecodedStream,
                file,
                PdfReaderLimits.Default with { MaxDecodedStreamLength = 1000 },
                PdfReaderLimits.Default,
                document => Decoded(document).ToString(CultureInfo.InvariantCulture),
                "10000",
                "The /ASCIIHexDecode data decodes to more than 1,000 bytes; decoding stopped there.",
                OffsetOf(file, "stream\n") + "stream\n".Length,
                ThrowsOnOpening: false,
                ReportsNothingElse: true);

            static int Decoded(PdfDocument document) =>
                document.GetObject(new PdfObjectId(3)).AsStream().Required().Decode(document.Diagnostics).Length;
        }

        private static Case LongObject()
        {
            // An array of two thousand numbers, about 9 KB, which a bound of 4 KB cuts part-way.
            var file = new TestPdfBuilder()
                .WithObject(1, Catalog)
                .WithObject(2, Pages)
                .WithObject(3, LongArray)
                .BuildClassic(rootNumber: 1);

            return new Case(
                nameof(PdfReaderLimits.MaxObjectLength),
                PdfDiagnosticCodes.LimitObject,
                file,
                PdfReaderLimits.Default with { MaxObjectLength = 4096 },
                PdfReaderLimits.Default,
                document => document.GetObject(new PdfObjectId(3)).AsArray().Required().Count.ToString(CultureInfo.InvariantCulture),
                "2000",
                "Object 3 runs past 4 KB, its stream data aside; only what lies within it was read.",
                OffsetOf(file, "3 0 obj"),
                ThrowsOnOpening: false,
                ReportsNothingElse: true);
        }

        private static Case LongSection()
        {
            // Three hundred entries, 6 KB of table, which a bound of 2 KB cuts before its trailer: the index is
            // then rebuilt by scanning, the /Root the trailer held being out of reach.
            var builder = new TestPdfBuilder().WithObject(1, Catalog).WithObject(2, Pages);
            for (var number = 3; number < 300; number++)
            {
                builder.WithObject(number, "null");
            }

            var file = builder.BuildClassic(rootNumber: 1);

            return new Case(
                nameof(PdfReaderLimits.MaxXRefSectionLength),
                PdfDiagnosticCodes.LimitXRefSectionLength,
                file,
                PdfReaderLimits.Default with { MaxXRefSectionLength = 2048 },
                PdfReaderLimits.Default,
                IndexState,
                "as written",
                "The cross-reference table runs past 2 KB; only the entries within it were read.",
                OffsetOf(file, "xref\n"),
                ThrowsOnOpening: true,
                ReportsNothingElse: false);
        }

        private static Case ManySections()
        {
            // A document saved three times after it was written: four sections, of which a bound of two reads
            // the newest. The catalogue is only in the oldest, so reaching it rebuilds the index.
            var file = new TestPdfBuilder().WithObject(1, Catalog).WithObject(2, Pages).BuildClassic(rootNumber: 1);
            var sections = new List<int> { OffsetOf(file, "xref\n") };

            for (var update = 1; update <= 3; update++)
            {
                var length = file.Length;
                file = TestPdfBuilder.AppendIncrementalUpdate(file, rootNumber: 1, [(3, $"({update})")]);
                sections.Add(length + Encoding.Latin1.GetString(file, length, file.Length - length).IndexOf("xref\n", StringComparison.Ordinal));
            }

            return new Case(
                nameof(PdfReaderLimits.MaxXRefSectionCount),
                PdfDiagnosticCodes.LimitXRefSectionCount,
                file,
                PdfReaderLimits.Default with { MaxXRefSectionCount = 2 },
                PdfReaderLimits.Default,
                IndexState,
                "as written",
                "The cross-reference chain has more than 2 sections; the older ones were not read.",
                sections[1],
                ThrowsOnOpening: true,
                ReportsNothingElse: false);
        }

        private static Case LongTrailer()
        {
            // A trailer of 100 KB, whose /Root comes after a long string: past the 64 KB a trailer is read to by
            // default, the /Root is out of reach and the index is rebuilt to find the catalogue.
            var written = new TestPdfBuilder().WithObject(1, Catalog).WithObject(2, Pages).BuildClassic(rootNumber: 1);
            var file = Replace(written, "<< /Size 3 /Root 1 0 R >>", $"<< /Size 3 /Pad ({new string('x', 100_000)}) /Root 1 0 R >>");

            return new Case(
                nameof(PdfReaderLimits.MaxTrailerLength),
                PdfDiagnosticCodes.LimitTrailer,
                file,
                PdfReaderLimits.Default,
                PdfReaderLimits.Default with { MaxTrailerLength = 1024 * 1024 },
                IndexState,
                "as written",
                "A trailer runs past 64 KB; only what lies within it was read.",
                OffsetOf(file, "trailer\n") + "trailer".Length,
                ThrowsOnOpening: true,
                ReportsNothingElse: false);
        }

        private static Case LongXRefStreamDictionary()
        {
            // The same trailer, as a cross-reference stream's dictionary: past 64 KB the stream is out of reach
            // and the index is rebuilt.
            var written = new TestPdfBuilder().WithObject(1, Catalog).WithObject(2, Pages).BuildWithXRefStream(rootNumber: 1);
            var file = Replace(written, "<< /Type /XRef /Size", $"<< /Type /XRef /Pad ({new string('x', 100_000)}) /Size");

            return new Case(
                nameof(PdfReaderLimits.MaxTrailerLength),
                PdfDiagnosticCodes.LimitTrailer,
                file,
                PdfReaderLimits.Default,
                PdfReaderLimits.Default with { MaxTrailerLength = 1024 * 1024 },
                IndexState,
                "as written",
                "A cross-reference stream's dictionary runs past 64 KB; only what lies within it was read.",
                OffsetOf(file, "<< /Type /XRef") - "4 0 obj\n".Length,
                ThrowsOnOpening: true,
                ReportsNothingElse: false);
        }

        private static byte[] Replace(byte[] file, string text, string replacement)
        {
            var written = Encoding.Latin1.GetString(file);
            written.Should().Contain(text);
            return Encoding.Latin1.GetBytes(written.Replace(text, replacement, StringComparison.Ordinal));
        }
    }

    /// <summary>A source that records whether it was released.</summary>
    private sealed class TrackedSource(byte[] data) : PdfFileSource
    {
        public bool Disposed { get; private set; }

        public override long Length => data.Length;

        public override int Read(long offset, Span<byte> buffer)
        {
            var available = (int)Math.Clamp(data.Length - offset, 0, buffer.Length);
            data.AsSpan((int)offset, available).CopyTo(buffer);
            return available;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
