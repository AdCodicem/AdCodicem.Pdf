using System.Buffers.Binary;
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
/// The reader's guards as options (ADR 34): each one is on by default, keeps what fits and names the property
/// that lifts it when reached, reads the document whole once raised, and throws when the caller asks it to.
/// </summary>
public class ReaderLimitsTests
{
    private const string Catalog = "<< /Type /Catalog /Pages 2 0 R >>";
    private const string Pages = "<< /Type /Pages /Kids [] /Count 0 >>";

    /// <summary>A guard for each bound of <see cref="PdfReaderLimits"/>, the trailer's reached two ways.</summary>
    public static TheoryData<string> Guards =>
        ["decoded stream", "object", "section length", "section count", "trailer", "cross-reference stream", "index entries", "filter chain"];

    [Fact]
    public void The_defaults_are_the_bounds_the_reader_kept_before_they_were_options()
    {
        var limits = PdfReaderLimits.Default;

        limits.MaxDecodedStreamLength.Should().Be(256 * 1024 * 1024);
        limits.MaxObjectLength.Should().Be(16 * 1024 * 1024);
        limits.MaxXRefSectionLength.Should().Be(64 * 1024 * 1024);
        limits.MaxXRefSectionCount.Should().Be(1024);
        limits.MaxTrailerLength.Should().Be(64 * 1024);
        limits.MaxIndexEntries.Should().Be(8_388_607);
        limits.MaxFilterChainLength.Should().Be(64);
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
        limits.MaxIndexEntries.Should().Be(int.MaxValue);
        limits.MaxFilterChainLength.Should().Be(int.MaxValue);
    }

    [Theory]
    [InlineData(nameof(PdfReaderLimits.MaxDecodedStreamLength), 0)]
    [InlineData(nameof(PdfReaderLimits.MaxObjectLength), -1)]
    [InlineData(nameof(PdfReaderLimits.MaxXRefSectionLength), int.MinValue)]
    [InlineData(nameof(PdfReaderLimits.MaxXRefSectionCount), 0)]
    [InlineData(nameof(PdfReaderLimits.MaxTrailerLength), -1)]
    [InlineData(nameof(PdfReaderLimits.MaxIndexEntries), 0)]
    [InlineData(nameof(PdfReaderLimits.MaxFilterChainLength), -1)]
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
            MaxIndexEntries = int.MaxValue,
            MaxFilterChainLength = int.MaxValue,
        };

        limits.Should().Be(PdfReaderLimits.Unbounded);
    }

    [Fact]
    public void The_options_refuse_limits_that_are_not_there()
    {
        var setting = () => PdfReaderOptions.Default with { Limits = null! };

        setting.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_guard_already_at_the_most_the_reader_can_hold_does_not_ask_to_be_raised()
    {
        // Past about 2 GB a stream cannot be held in one piece, whatever the options (issue #48): the report says so
        // rather than send the caller after a property that can go no higher. A count one below its ceiling still
        // asks to be raised.
        var diagnostics = new PdfDiagnostics();
        var unbounded = new PdfLimitGuard(PdfReaderLimits.Unbounded, throwOnLimit: false);
        var justBelow = new PdfLimitGuard(
            PdfReaderLimits.Default with
            {
                MaxXRefSectionCount = int.MaxValue - 1,
                MaxIndexEntries = int.MaxValue - 1,
                MaxFilterChainLength = int.MaxValue - 1,
            },
            throwOnLimit: false);

        unbounded.Reach(PdfLimit.DecodedStream, diagnostics, "Cut.", 7);
        unbounded.Reach(PdfLimit.XRefSectionCount, diagnostics, "Cut.", 8);
        justBelow.Reach(PdfLimit.XRefSectionCount, diagnostics, "Cut.", 9);
        unbounded.Reach(PdfLimit.IndexEntries, diagnostics, "Cut.", 10);
        justBelow.Reach(PdfLimit.IndexEntries, diagnostics, "Cut.", 11);
        unbounded.Reach(PdfLimit.FilterChain, diagnostics, "Cut.", 12);
        justBelow.Reach(PdfLimit.FilterChain, diagnostics, "Cut.", 13);

        diagnostics.Select(entry => entry.Message).Should().Equal(
            "Cut. PdfReaderLimits.MaxDecodedStreamLength is already at the most the reader can hold.",
            "Cut. PdfReaderLimits.MaxXRefSectionCount is already at the most the reader can hold.",
            "Cut. Raise PdfReaderLimits.MaxXRefSectionCount to read past it.",
            "Cut. PdfReaderLimits.MaxIndexEntries is already at the most the reader can hold.",
            "Cut. Raise PdfReaderLimits.MaxIndexEntries to read past it.",
            "Cut. PdfReaderLimits.MaxFilterChainLength is already at the most the reader can hold.",
            "Cut. Raise PdfReaderLimits.MaxFilterChainLength to read past it.");
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

    [Theory]
    [InlineData("object stream", "null | limit.filter-chain | xref.checked-in-part:Information")]
    [InlineData("cross-reference stream", "page | limit.filter-chain,xref.rebuilt | xref.checked-in-part:Information")]
    public void A_stream_the_reader_reads_whose_filters_pass_the_guard_is_cut_before_its_data(string which, string expected)
    {
        // A sound file whose object stream, holding the page, or whose cross-reference stream passes its data through 65
        // Flate filters. Past the guard on filters the data is still encoded, and none of it is rows or objects: the
        // stream is cut before them, as a guard that leaves nothing of a stream cuts it. The object stream's members are
        // unknown until the guard is raised, and a chain left with no section is rebuilt, as for any guard; nothing of
        // the file is reported as damage — no section read from compressed bytes, no object stream judged unreadable —, and
        // the guard is reported once, though the reader and the validator each decode the object stream (#144).
        var file = FileOfLayeredStreams(which == "object stream" ? 65 : 1, which == "cross-reference stream" ? 65 : 1);

        Outcome(file, PdfReaderLimits.Default with { MaxFilterChainLength = 100 }).Should().Be("page |  | ");
        Outcome(file, PdfReaderLimits.Default).Should().Be(expected);

        static string Outcome(byte[] file, PdfReaderLimits limits)
        {
            using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });
            var page = document.GetObject(new PdfObjectId(3)) is PdfDictionary ? "page" : "null";
            var findings = new PdfValidator().Validate(document).Findings
                .Where(finding => finding.RuleId != PdfValidationRuleIds.PageTreeResourcesMissing)
                .Select(finding => $"{finding.RuleId}:{finding.Severity}");
            return $"{page} | {string.Join(",", document.Diagnostics.Select(d => d.Code))} | {string.Join(",", findings)}";
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
        // Objects 11 and 13 live in object streams the index does not describe, and the index is incomplete,
        // so asking for 13 rebuilds it. The first object stream decodes past the bound; the second is where 13
        // is. A rebuild abandoned at the first would have left 13 out of the index for good, since a rebuild is
        // never run twice.
        var header = "11 0 ";
        var packed = header + "(" + new string('y', 2000) + ")";
        var file = WithIncompleteIndex(new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, Pages)
            .Stream(10, $"/Type /ObjStm /N 1 /First {header.Length} /Filter /ASCIIHexDecode", Hex(packed))
            .Stream(12, "/Type /ObjStm /N 1 /First 5", "13 0 (found)")
            .BuildClassic(rootNumber: 1));
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
    public void A_rebuild_that_reaches_a_guard_looking_for_the_catalog_still_finds_it()
    {
        // Object 1, the catalog the index names, is redefined as null after the file's end, where only a
        // rebuild sees it; the index is incomplete, so asking for an object it lacks rebuilds it. Rebuilding
        // then looks for another catalog among every object, and object 3 on the way reaches the object
        // bound; the search goes on to object 4 before the guard throws.
        var written = WithIncompleteIndex(new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, Pages)
            .WithObject(3, LongArray)
            .WithObject(4, Catalog)
            .BuildClassic(rootNumber: 1));
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
    public void A_rebuild_indexes_object_numbers_up_to_the_entry_guard_and_reports_the_first_it_had_no_room_for()
    {
        // #183, #158: a file with no index, rebuilt by scanning, of a hundred objects under a bound of fifty. The first fifty
        // are indexed, in the file's order, and read; the rest read as null; the header of the fifty-first is reported.
        var file = Numbered(100).BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 50 } });

        document.WasRepaired.Should().BeTrue();
        document.ObjectCount.Should().Be(50);
        document.GetObject(new PdfObjectId(50)).AsText().Should().Be("50");
        document.GetObject(new PdfObjectId(51)).Should().BeSameAs(PdfNull.Instance);
        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries).Which;
        report.Position.Should().Be(OffsetOf(file, "51 0 obj"));
        report.Message.Should().Be(
            "The index reached 50 entries; the objects past them were not indexed. Raise PdfReaderLimits.MaxIndexEntries to read past it.");
    }

    [Fact]
    public void A_rebuild_past_the_entry_guard_still_takes_the_newest_definition_of_a_number_it_holds()
    {
        // The scan runs to the file's end: past the bound, a number it holds is still defined again, and the newest
        // definition wins, as it does for every rebuild; a number it lacks takes no room.
        var written = Numbered(100).BuildClassic(rootNumber: 1, includeXRef: false);
        byte[] file = [.. written, .. "5 0 obj\n(newest)\nendobj\n101 0 obj\n(no room)\nendobj\n"u8];

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 50 } });

        document.GetObject(new PdfObjectId(5)).AsText().Should().Be("newest");
        document.GetObject(new PdfObjectId(101)).Should().BeSameAs(PdfNull.Instance);
        document.ObjectCount.Should().Be(50);
    }

    [Fact]
    public void A_rebuild_that_reaches_the_entry_guard_finishes_the_index_before_it_throws()
    {
        // The chain indexes the catalog and the page tree alone, and names through /Prev a section that is not there, so
        // that asking for an object it lacks rebuilds the index. The scan reaches the bound at object 51, and goes on to the
        // file's end before the guard throws: object 5's newest definition, after it, is taken.
        var builder = Numbered(100);
        var written = builder.BuildClassic(rootNumber: 1, includeXRef: false);
        byte[] objects = [.. written.AsSpan(0, written.AsSpan().LastIndexOf("%%EOF"u8)), .. "5 0 obj\n(newest)\nendobj\n"u8];
        var xref = objects.Length;
        var table = string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 3\n0000000000 65535 f\r\n{OffsetOf(objects, "1 0 obj"):D10} 00000 n\r\n{OffsetOf(objects, "2 0 obj"):D10} 00000 n\r\ntrailer\n<< /Size 3 /Root 1 0 R /Prev 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        byte[] file = [.. objects, .. Encoding.ASCII.GetBytes(table)];
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 50 }, ThrowOnLimit = true };

        using var document = PdfDocument.Open(file, options);
        var rebuilding = () => document.GetObject(new PdfObjectId(80));

        var thrown = rebuilding.Should().Throw<PdfLimitExceededException>().Which;
        thrown.LimitName.Should().Be(nameof(PdfReaderLimits.MaxIndexEntries));
        thrown.Position.Should().Be(OffsetOf(file, "51 0 obj"));
        document.WasRepaired.Should().BeTrue();
        document.ObjectCount.Should().Be(50);
        document.GetObject(new PdfObjectId(5)).AsText().Should().Be("newest");

        // An object the full index left out reads as null by the guard's decision, which a document opened to throw on one
        // is told each time it asks.
        rebuilding.Should().Throw<PdfLimitExceededException>().Which.Position.Should().Be(OffsetOf(file, "51 0 obj"));
    }

    [Fact]
    public void A_read_whose_rebuild_fills_the_index_throws_that_guard_before_any_it_reaches_after()
    {
        // Object stream 6 holds the catalog's object 7, decodes to 10,000 bytes past a bound of 4,096, and takes its /Length
        // from object 5, whose row leads to the catalog's header; ten objects are written outside the index. Reading 7
        // rebuilds the index while 6 is being loaded, and the scan fills it: that guard is the first the read reaches, and
        // the one it throws, rather than the bound 6's decoding reaches afterward, which went unreported.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Test 7 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
        };
        var decoded = new byte[10_000];
        decoded.AsSpan().Fill((byte)' ');
        "7 0 (member)"u8.CopyTo(decoded);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(decoded);
        }

        var data = Encoding.Latin1.GetString(compressed.ToArray());
        rows[6] = (1, file.Object(6, $"<< /Type /ObjStm /N 1 /First 4 /Filter /FlateDecode /Length 5 0 R >>\nstream\n{data}\nendstream"), 0);
        rows[7] = (2, 6, 0);
        rows[5] = (1, 9, 0);
        rows[8] = (1, file.Padding(8), 0);
        file.Object(5, data.Length.ToString(CultureInfo.InvariantCulture));

        for (var number = 20; number < 30; number++)
        {
            file.Object(number, "null");
        }

        file.CrossReferenceStream(10, 11, rows, "/Root 1 0 R");
        var options = new PdfReaderOptions
        {
            Limits = PdfReaderLimits.Default with { MaxIndexEntries = 12, MaxDecodedStreamLength = 4096 },
            ThrowOnLimit = true,
        };
        using var document = PdfDocument.Open(file.Bytes, options);

        FluentActions.Invoking(() => document.GetObject(new PdfObjectId(7))).Should().Throw<PdfLimitExceededException>()
            .Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxIndexEntries));

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(1)).Should().BeOfType<PdfDictionary>();
    }

    [Fact]
    public void A_lookup_past_a_full_index_reads_null_and_rebuilds_nothing()
    {
        // #164, #183: a rebuild would scan the whole file to refill an index already at its bound, and could add nothing.
        var @case = Case.For("index entries");

        using var document = PdfDocument.Open(@case.File, new PdfReaderOptions { Limits = @case.Reached });

        document.GetObject(new PdfObjectId(250)).Should().BeSameAs(PdfNull.Instance);
        document.Reader.GetPresence(250).Should().Be(ObjectPresence.Unindexed, "whether the file holds it is unknown");
        document.Reader.GetPresence(99).Should().Be(ObjectPresence.Defined);
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitIndexEntries);
    }

    [Fact]
    public void A_catalog_past_a_guard_cut_reads_as_null_and_rebuilds_nothing()
    {
        // The catalog only the oldest section indexes lies past MaxXRefSectionCount: /Root names an object the index lacks
        // because of the guard, which is no damage, and the document opens without a catalog until the guard is lifted.
        var @case = Case.For("section count");

        using (var document = PdfDocument.Open(@case.File, new PdfReaderOptions { Limits = @case.Reached }))
        {
            document.Catalog.Should().BeNull();
            document.WasRepaired.Should().BeFalse();
            document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitXRefSectionCount);

            // Nor is it a catalog the file lacks: file.root-invalid, an error, once said so.
            var findings = new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId);
            findings.Should().Contain(PdfValidationRuleIds.XRefCheckedInPart).And.NotContain(PdfValidationRuleIds.FileRootInvalid);
        }

        using var lifted = PdfDocument.Open(@case.File, new PdfReaderOptions { Limits = @case.Raised });
        lifted.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    [Fact]
    public void An_object_stream_past_a_full_index_is_unknown_and_what_it_holds_too()
    {
        // Object 3's row places it in object stream 4, whose own row lies past the bound: the validator said the index
        // places an object in a stream it does not hold, an error of the file's.
        var file = new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, Pages)
            .WithObject(3, "(packed)")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [3]);
        var limits = PdfReaderLimits.Default with { MaxIndexEntries = 4 };

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        document.Reader.GetPresence(3).Should().Be(ObjectPresence.Unindexed);
        document.WasRepaired.Should().BeFalse();
        var findings = new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId).ToList();
        findings.Should().Contain(PdfValidationRuleIds.XRefCheckedInPart).And.NotContain(PdfValidationRuleIds.XRefObjectStreamBroken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_row_an_older_section_refuses_for_a_number_a_newer_one_gave_takes_no_room(bool refusedInTheUpdate)
    {
        // The original refuses its row for object 3, which the update gives, or refuses as well: the number is the index's
        // already, or a refused one, and takes no room again. Under a bound of exactly the four numbers the file gives, the
        // index was said to be full.
        var original = Encoding.Latin1.GetString(new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, Pages)
            .WithObject(3, "(three)")
            .BuildClassic(rootNumber: 1));
        var rows = original.IndexOf("xref\n0 4\n", StringComparison.Ordinal) + "xref\n0 4\n".Length;
        var three = rows + (3 * 21) + 11;
        original[three..].Should().StartWith("00000 n");
        original = string.Concat(original.AsSpan(0, three), "70000", original.AsSpan(three + 5));
        var text = Encoding.Latin1.GetString(TestPdfBuilder.AppendIncrementalUpdate(
            Encoding.Latin1.GetBytes(original), rootNumber: 1, [(3, "(new three)")]));

        if (refusedInTheUpdate)
        {
            var update = text.LastIndexOf("xref\n3 1\n", StringComparison.Ordinal) + "xref\n3 1\n".Length + 11;
            text[update..].Should().StartWith("00000 n");
            text = string.Concat(text.AsSpan(0, update), "70000", text.AsSpan(update + 5));
        }

        var limits = PdfReaderLimits.Default with { MaxIndexEntries = 4 };

        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text), new PdfReaderOptions { Limits = limits });

        document.Reader.Structure.Sections.Should().HaveCount(2);
        document.Diagnostics.Select(entry => entry.Code).Should().NotContain(PdfDiagnosticCodes.LimitIndexEntries);
        document.Reader.Structure.IndexFullAt.Should().BeNegative();
        document.Reader.Structure.Sections[1].RefusedRow.Should().Contain("the row for object 3");
        (document.Reader.Structure.Sections[0].RefusedRow is not null).Should().Be(refusedInTheUpdate);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(100)]
    public void A_row_refused_before_a_full_index_is_the_file_s_and_an_object_stream_past_it_stays_unknown(int bound)
    {
        // The stream refuses row 3 and fills the index at row 4 under a bound of 4, before object stream 5's row: the
        // refused row makes the catalog, packed in 5, damage that sets off a rebuild. The rebuild found 5, and the validator
        // blamed the file for a stream the chain's index lacked only because the guard stopped it; and the refused row,
        // read before the cut, was reported nowhere.
        var file = new List<byte>();
        void Append(string text) => file.AddRange(Encoding.Latin1.GetBytes(text));

        Append("%PDF-1.7\n");
        var members = Catalog + "\n" + Pages + "\n";
        var header = string.Create(CultureInfo.InvariantCulture, $"1 0 2 {Catalog.Length + 1} ");
        var objectStream = file.Count;
        Append(string.Create(
            CultureInfo.InvariantCulture,
            $"5 0 obj\n<< /Type /ObjStm /N 2 /First {header.Length} /Length {header.Length + members.Length} >>\nstream\n{header}{members}\nendstream\nendobj\n"));
        var xref = file.Count;
        byte[] rows =
        [
            0, 0, 0, 0, 0, 255, 255,
            2, 0, 0, 0, 5, 0, 0,
            2, 0, 0, 0, 5, 0, 1,
            2, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 1,
            1, 0, 0, 0, (byte)objectStream, 0, 0,
            1, 0, 0, (byte)(xref >> 8), (byte)xref, 0, 0,
        ];
        Append(string.Create(CultureInfo.InvariantCulture, $"6 0 obj\n<< /Type /XRef /Size 7 /W [1 4 2] /Root 1 0 R /Length {rows.Length} >>\nstream\n"));
        file.AddRange(rows);
        Append(string.Create(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n"));
        var limits = PdfReaderLimits.Default with { MaxIndexEntries = bound };

        using var document = PdfDocument.Open(file.ToArray(), new PdfReaderOptions { Limits = limits });

        document.WasRepaired.Should().Be(bound == 4);
        var findings = new PdfValidator().Validate(document).Findings;
        findings.Should().ContainSingle(finding => finding.RuleId == PdfValidationRuleIds.XRefSectionMalformed)
            .Which.Message.Should().Contain("the row for object 3");
        findings.Select(finding => finding.RuleId).Should().NotContain(PdfValidationRuleIds.XRefObjectStreamBroken);
        findings.Any(finding => finding.RuleId == PdfValidationRuleIds.XRefCheckedInPart).Should().Be(bound == 4);
    }

    [Fact]
    public void A_catalog_packed_in_an_object_stream_past_a_full_index_reads_as_null_and_rebuilds_nothing()
    {
        // The catalog's row is indexed, the row of the object stream that holds it is past the bound: the catalog cannot
        // be produced, which was taken for damage, and the index was rebuilt, as the validator blamed the file for an
        // object stream it does not hold. Both are unknown until the guard is lifted.
        var file = new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, Pages)
            .WithObject(3, "(packed)")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [1, 3]);
        var limits = PdfReaderLimits.Default with { MaxIndexEntries = 4 };

        using (var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits }))
        {
            document.Catalog.Should().BeNull();
            document.WasRepaired.Should().BeFalse();
            document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitIndexEntries);
            document.Reader.GetPresence(1).Should().Be(ObjectPresence.Unindexed);
            document.Reader.GetPresence(4).Should().Be(ObjectPresence.Unindexed);

            var findings = new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId).ToList();
            findings.Should().Contain(PdfValidationRuleIds.XRefCheckedInPart);
            findings.Should().NotContain([PdfValidationRuleIds.FileRootInvalid, PdfValidationRuleIds.XRefObjectStreamBroken]);
        }

        using var lifted = PdfDocument.Open(file);
        lifted.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    [Fact]
    public void A_rebuild_damage_sets_off_keeps_the_entries_a_chain_a_guard_cut_gave_that_the_scan_cannot_find()
    {
        // The cross-reference stream decodes no further than five of its eight rows: objects 3 and 4, packed in object 6,
        // are indexed; the catalog, object 5, and object 6 itself are past the cut. /Root names object 2, the page tree,
        // which is no catalog, and none is among the indexed objects: that is damage, and the index is rebuilt. The scan
        // finds every header, but object 6 has no /Type /ObjStm, so the rebuild does not take in what it holds: only the
        // chain's entries for 3 and 4, kept through the rebuild, still place them.
        var written = new TestPdfBuilder()
            .WithObject(1, "(one)")
            .WithObject(2, Pages)
            .WithObject(3, "(packed three)")
            .WithObject(4, "(packed four)")
            .WithObject(5, Catalog)
            .BuildWithXRefStream(rootNumber: 2, compressedObjects: [3, 4], compressXRefStream: true);
        var file = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(written).Replace("/Type /ObjStm", "/Kind /ObjStm", StringComparison.Ordinal));
        var limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 35 };

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        document.WasRepaired.Should().BeTrue();
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        document.GetObject(new PdfObjectId(3)).AsText().Should().Be("packed three");
        document.GetObject(new PdfObjectId(4)).AsText().Should().Be("packed four");

        // Five headers found, two entries kept: the chain's free row for object 0 is no object, and is not.
        document.ObjectCount.Should().Be(7);
    }

    [Fact]
    public void A_rebuild_a_read_sets_off_keeps_the_entries_a_chain_a_guard_cut_gave_that_the_scan_cannot_find()
    {
        // As above, the rebuild set off by a read: object 4's row leads into padding, and reading 4 rebuilds the index while
        // 4 is being loaded. The cross-reference stream decodes no further than its first ten rows; object 6, which holds 7,
        // has no /Type /ObjStm: once the rebuild is finished, only the chain's entry for 7, kept through it, places 7.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, Catalog),
            [2] = file.Object(2, Pages),
            [6] = file.Object(6, "<< /Kind /ObjStm /N 1 /First 4 /Length 18 >>\nstream\n7 0 (packed seven)\nendstream"),
            [8] = file.Padding(8),
        };
        rows[4] = rows[8] + 600;
        file.Object(4, "(four)");

        for (var number = 10; number < 30; number++)
        {
            rows[number] = file.Object(number, "null");
        }

        var data = new byte[31 * 7];
        data[0] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(5), 65535);

        foreach (var (number, offset) in rows)
        {
            data[number * 7] = 1;
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan((number * 7) + 1), (uint)offset);
        }

        data[7 * 7] = 2;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan((7 * 7) + 1), 6);
        var xref = file.Position;
        data[30 * 7] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan((30 * 7) + 1), (uint)xref);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(data);
        }

        var stream = Encoding.Latin1.GetString(compressed.ToArray());
        file.Object(30, string.Create(CultureInfo.InvariantCulture, $"<< /Type /XRef /Size 31 /W [1 4 2] /Root 1 0 R /Filter /FlateDecode /Length {stream.Length} >>\nstream\n{stream}\nendstream"));
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"startxref\n{xref}\n%%EOF\n"));
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 70 } });

        document.GetObject(new PdfObjectId(4)).AsText().Should().Be("four");

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(7)).AsText().Should().Be("packed seven");
    }

    [Fact]
    public void A_rebuild_takes_in_the_members_of_object_streams_no_further_than_the_entry_guard()
    {
        // ADR 34 said a rebuild indexed at most two million objects: it counted the headers the scan read, and the object
        // streams' members, taken in after the scan, counted for nothing. The bound counts them: four headers found, then
        // ninety-six of the five hundred members, and the object stream that held them is reported.
        var header = string.Join(' ', Enumerable.Range(10, 500).Select(number => string.Create(CultureInfo.InvariantCulture, $"{number} {(number - 10) * 2}")));
        var members = string.Concat(Enumerable.Repeat("0 ", 500));
        var file = new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, Pages)
            .Stream(3, string.Create(CultureInfo.InvariantCulture, $"/Type /ObjStm /N 500 /First {header.Length + 1}"), header + " " + members)
            .WithObject(4, "(direct)")
            .BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 100 } });

        document.ObjectCount.Should().Be(100);
        document.GetObject(new PdfObjectId(105)).Should().BeOfType<PdfInteger>();
        document.GetObject(new PdfObjectId(106)).Should().BeSameAs(PdfNull.Instance);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries)
            .Which.Position.Should().Be(OffsetOf(file, "3 0 obj"));
    }

    [Fact]
    public void A_table_its_length_bound_cuts_keeps_its_trailer_and_the_encryption_it_names()
    {
        // The cut fell before the trailer, and the reader stopped there: /Encrypt, /Info and /ID were lost with it, and an
        // encrypted file opened as plain, ThrowOnEncrypted never seeing what it guards against. The trailer past the rows
        // left unread is read all the same.
        var file = Trailed("/Encrypt 5 0 R /Info 6 0 R /ID [<0123> <4567>]");
        var limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 128 };

        var opening = () => PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });
        opening.Should().Throw<PdfEncryptedException>();

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits, ThrowOnEncrypted = false });
        document.IsEncrypted.Should().BeTrue();
        document.Trailer.ContainsKey(PdfName.Info).Should().BeTrue();
        document.Trailer.ContainsKey(PdfName.ID).Should().BeTrue();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitXRefSectionLength);
        document.Reader.Structure.Sections.Should().ContainSingle().Which.TrailerPosition.Should().Be(OffsetOf(file, "trailer"));
    }

    [Theory]
    [InlineData("trailer\n", "% the rows end here\ntrailer\n", false)]
    [InlineData("trailer\n", "%-------------------------------------------------------------\ntrailer\n", false)]
    [InlineData(" 00000 n \r\ntrailer\n", " +0000 n \r\ntrailer\n", false)]
    [InlineData(" n \r\ntrailer\n", " x \r\ntrailer\n", true)]
    [InlineData(" n \r\ntrailer\n", " ntrailer \r\ntrailer\n", true)]
    public void A_table_its_length_bound_cuts_keeps_the_encryption_its_trailer_names_past_what_no_row_holds(
        string written, string replacement, bool damaged)
    {
        // Past the cut, the search for the trailer stopped at the first byte no row holds, and the trailer was lost when a
        // comment, a damaged row or a signed number came before it: an encrypted file opened as plain. A reader that reads
        // the rows reads past each to the trailer, and so does the search.
        var text = Encoding.Latin1.GetString(Trailed("/Encrypt 5 0 R"));
        text.Should().Contain(written);
        var file = Encoding.Latin1.GetBytes(text.Replace(written, replacement, StringComparison.Ordinal));
        var limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 128 };

        var unguarded = () => PdfDocument.Open(file);
        unguarded.Should().Throw<PdfEncryptedException>();
        var opening = () => PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });
        opening.Should().Throw<PdfEncryptedException>();

        // A comment and a signed number are no fault; a damaged row is, and "trailer" ending a longer token is no keyword.
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits, ThrowOnEncrypted = false });
        var section = document.Reader.Structure.Sections.Should().ContainSingle().Which;
        section.TrailerPosition.Should().Be(OffsetOf(file, "trailer\n<<"));
        (section.Fault is not null).Should().Be(damaged);
        document.IsEncrypted.Should().BeTrue();
    }

    [Theory]
    [InlineData(nameof(PdfReaderLimits.MaxXRefSectionLength))]
    [InlineData(nameof(PdfReaderLimits.MaxIndexEntries))]
    public void A_trailer_past_rows_left_unread_that_its_own_bound_cuts_keeps_what_precedes_the_cut(string limit)
    {
        // The trailer past the rows a guard left unread runs past MaxTrailerLength: what it gives before the cut is kept,
        // and what lies past it is unknown, the trailer the guard's as a trailer its table's window cut is.
        var file = Trailed($"/Pad ({new string('x', 70 * 1024)}) /Info 6 0 R");
        var limits = limit == nameof(PdfReaderLimits.MaxIndexEntries)
            ? PdfReaderLimits.Default with { MaxIndexEntries = 4 }
            : PdfReaderLimits.Default with { MaxXRefSectionLength = 128 };

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        var section = document.Reader.Structure.Sections.Should().ContainSingle().Which;
        section.TrailerCutByLimit.Should().BeTrue();
        section.CutByLimit.Should().BeTrue();
        document.Diagnostics.Select(entry => entry.Code).Should().Contain(PdfDiagnosticCodes.LimitTrailer);
        document.Trailer.GetRaw(PdfName.Root).Should().NotBeNull();
        document.Trailer.GetRaw(PdfName.Info).Should().BeNull();
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData("% rows of the first revision follow", 15)]
    [InlineData("%          trailer << /Size 11 /Root 1 0 R >>", 10)]
    public void A_cut_in_a_comment_among_the_rows_searches_for_the_trailer_from_the_comment_s_start(string comment, int into)
    {
        // The search started seven bytes before the cut, inside the comment the lexer was skipping: its text was taken for
        // a fault of the table's, and a trailer written in it for the table's own, an encrypted file opened as plain.
        var text = Encoding.Latin1.GetString(Trailed("/Encrypt 5 0 R"));
        const string FirstRow = "0000000000 65535 f \r\n";
        var file = Encoding.Latin1.GetBytes(text.Replace(FirstRow, FirstRow + comment + "\r\n", StringComparison.Ordinal));
        var limits = PdfReaderLimits.Default with { MaxXRefSectionLength = OffsetOf(file, comment) - OffsetOf(file, "xref\n") + into };

        var opening = () => PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });
        opening.Should().Throw<PdfEncryptedException>();

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits, ThrowOnEncrypted = false });
        var section = document.Reader.Structure.Sections.Should().ContainSingle().Which;
        section.CutByLimit.Should().BeTrue();
        section.Fault.Should().BeNull();
        section.TrailerPosition.Should().Be(OffsetOf(file, "trailer\n<<"));
        document.IsEncrypted.Should().BeTrue();
    }

    [Theory]
    [InlineData(nameof(PdfReaderLimits.MaxXRefSectionLength), "% trailer << /Root 9 0 R >>\n")]
    [InlineData(nameof(PdfReaderLimits.MaxIndexEntries), "% trailer << /Root 9 0 R >>\n")]
    [InlineData(nameof(PdfReaderLimits.MaxXRefSectionLength), "ntrailer\n")]
    public void The_trailer_past_rows_left_unread_is_found_wherever_the_search_s_window_ends(string limit, string before)
    {
        // The search for the trailer reads the file a megabyte at a time: a comment, or the keyword, that the window's end
        // splits is looked at again from the next one, and a comment that runs past the window is still one in the next.
        // White space puts what comes before the trailer — a comment with a fake trailer in its text, or "trailer" ending a
        // longer token, a fault of the table's — and the trailer on every byte around the window's end.
        var text = Encoding.Latin1.GetString(Trailed("/Info 6 0 R"));
        var at = text.IndexOf("trailer\n", StringComparison.Ordinal);
        var head = Encoding.Latin1.GetBytes(text[..at]);
        var tail = Encoding.Latin1.GetBytes(before + text[at..]);
        var limits = limit == nameof(PdfReaderLimits.MaxIndexEntries)
            ? PdfReaderLimits.Default with { MaxIndexEntries = 4 }
            : PdfReaderLimits.Default with { MaxXRefSectionLength = 128 };

        for (var spaces = (1024 * 1024) - 300; spaces <= (1024 * 1024) + 8; spaces++)
        {
            var file = new byte[head.Length + spaces + tail.Length];
            head.CopyTo(file, 0);
            file.AsSpan(head.Length, spaces).Fill((byte)' ');
            tail.CopyTo(file, head.Length + spaces);

            using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

            var section = document.Reader.Structure.Sections.Should().ContainSingle().Which;
            section.TrailerPosition.Should().Be(head.Length + spaces + tail.AsSpan().IndexOf("trailer\n<<"u8), "with {0} spaces", spaces);
            (section.Fault is null).Should().Be(before[0] == '%', "with {0} spaces", spaces);
            document.Trailer.GetRaw(PdfName.Info).Should().BeOfType<PdfReference>("with {0} spaces", spaces)
                .Which.Id.Should().Be(new PdfObjectId(6));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(6)]
    public void A_table_cut_before_its_keyword_ends_keeps_its_trailer(int bound)
    {
        // The search started seven bytes before the cut, so that a keyword the cut split is found: under a bound shorter
        // than the keyword it started before the table, in another object's bytes, and named them as the table's fault.
        var file = Trailed("/Info 6 0 R");
        var limits = PdfReaderLimits.Default with { MaxXRefSectionLength = bound };

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        var section = document.Reader.Structure.Sections.Should().ContainSingle().Which;
        section.Fault.Should().BeNull();
        section.TrailerFault.Should().Be(XRefTrailerFault.None);
        section.TrailerPosition.Should().Be(OffsetOf(file, "trailer"));
        document.Trailer.ContainsKey(PdfName.Info).Should().BeTrue();
    }

    [Fact]
    public void Rows_past_a_full_index_are_not_read_and_the_trailer_past_them_is()
    {
        // A cross-reference stream's rows past a full index were not read, and a classic table's were: a row refused past
        // the bound marked the index damaged, and the next lookup rebuilt it. The rows left can only repeat numbers the
        // index holds; the trailer past them names the encryption, and is read.
        var text = Encoding.Latin1.GetString(Trailed("/Encrypt 5 0 R"));
        var file = Encoding.Latin1.GetBytes(text.Replace(" 00000 n \r\ntrailer", " 70000 n \r\ntrailer", StringComparison.Ordinal));
        var limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 };

        var opening = () => PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });
        opening.Should().Throw<PdfEncryptedException>();

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits, ThrowOnEncrypted = false });
        document.GetObject(new PdfObjectId(10)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitIndexEntries);
        document.Reader.Structure.Sections.Should().ContainSingle().Which.TrailerPosition.Should().Be(OffsetOf(file, "trailer"));
    }

    [Fact]
    public void A_table_its_length_bound_cuts_ends_the_chain_though_its_trailer_names_an_older_section()
    {
        // Rows past the cut may define objects an older section defines too: read, the older rows would stand for them.
        // The update defines the catalog again, and objects 3 to 10: the bound keeps its first two rows.
        var original = Numbered(10).BuildClassic(rootNumber: 1);
        var file = TestPdfBuilder.AppendIncrementalUpdate(
            original, rootNumber: 1, [(1, Catalog), .. Enumerable.Range(3, 8).Select(number => (number, "(updated)"))]);
        var update = OffsetOf(file, "xref\n1 1");
        var limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 64 };

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        document.Reader.Structure.Sections.Should().ContainSingle().Which.Offset.Should().Be(update);
        document.Trailer.ContainsKey(PdfName.Prev).Should().BeTrue();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitXRefSectionLength);
    }

    [Theory]
    [InlineData("<< /Size", "missing")]
    [InlineData("startxref 0 << /Size", "the keyword startxref")]
    public void What_ends_the_rows_a_length_bound_cut_is_judged_as_it_is_where_none_did(string replacement, string judged)
    {
        // A dictionary the trailer keyword does not introduce is a trailer missing; anything else is no part of a table.
        var file = Trailed(string.Empty);
        file = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(file).Replace("trailer\n<< /Size", replacement, StringComparison.Ordinal));
        var end = Encoding.Latin1.GetString(file).LastIndexOf(replacement, StringComparison.Ordinal);
        var limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 128 };

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        var section = document.Reader.Structure.Sections.Should().ContainSingle().Which;
        section.TrailerPosition.Should().Be(end);

        var findings = new PdfValidator().Validate(document).Findings;

        if (judged == "missing")
        {
            section.TrailerFault.Should().Be(XRefTrailerFault.Missing);
            section.Fault.Should().BeNull();
            findings.Should().ContainSingle(finding => finding.RuleId == PdfValidationRuleIds.FileTrailerMissing);
            findings.Should().NotContain(finding => finding.RuleId == PdfValidationRuleIds.XRefSectionMalformed);
        }
        else
        {
            // Met past the cut, the fault is the file's, and was reported nowhere.
            section.TrailerFault.Should().Be(XRefTrailerFault.None);
            section.Fault.Should().Be($"it holds {judged} at offset {end}, where a subsection or the trailer should start");
            findings.Should().ContainSingle(finding => finding.RuleId == PdfValidationRuleIds.XRefSectionMalformed)
                .Which.Message.Should().EndWith($"is malformed: it holds {judged} at offset {end}, where a subsection or the trailer should start.");
        }
    }

    [Fact]
    public void A_table_a_length_bound_cuts_that_runs_to_the_end_of_the_file_has_no_trailer()
    {
        // The file ends in the table's rows, its startxref written before them, in reach of the search at the file's end.
        var written = Trailed(string.Empty);
        var table = OffsetOf(written, "xref\n");
        var rows = written.AsSpan(table, OffsetOf(written, "trailer") - table).ToArray();
        const string Pointer = "startxref\n{0:D10}\n%%EOF\n";
        var pointer = Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture, Pointer, table + string.Format(CultureInfo.InvariantCulture, Pointer, 0).Length));
        byte[] file = [.. written.AsSpan(0, table), .. pointer, .. rows];
        var limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 128 };

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        var section = document.Reader.Structure.Sections.Should().ContainSingle().Which;
        section.Offset.Should().Be(table + pointer.Length);
        section.TrailerFault.Should().Be(XRefTrailerFault.Missing);
        section.TrailerPosition.Should().Be(file.Length);
    }

    [Fact]
    public void A_guard_reached_looking_for_the_catalog_among_the_indexed_objects_throws_once_it_is_found()
    {
        // /Root names no object, so the catalog is looked for among those the index holds, without a rebuild.
        // Object 1 reaches the object bound on the way to object 3, the catalog, which is found before the guard
        // throws.
        var file = new TestPdfBuilder()
            .WithObject(1, LongArray)
            .WithObject(2, Pages)
            .WithObject(3, Catalog)
            .BuildClassic(rootNumber: 9);
        var limits = PdfReaderLimits.Default with { MaxObjectLength = 4096 };
        var opening = () => PdfDocument.Open(file, new PdfReaderOptions { Limits = limits, ThrowOnLimit = true });

        opening.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be("MaxObjectLength");

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject, PdfDiagnosticCodes.TrailerRootRecovered);
    }

    [Fact]
    public void A_catalog_a_guard_cut_is_taken_as_read_and_no_other_is_looked_for_even_by_a_later_rebuild()
    {
        // The catalog, object 1, runs past 128 bytes before its /Type; object 5 is an older catalog. Object 4's row leads
        // nowhere, so that reading it rebuilds the index, which then looks at /Root again.
        var file = PdfTemplate.Build($$"""
            %PDF-1.7
            1 0 obj
            <<{{new string(' ', 200)}}/Type /Catalog /Pages 2 0 R >>
            endobj
            2 0 obj
            << /Type /Pages /Kids [3 0 R] /Count 1 >>
            endobj
            3 0 obj
            << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>
            endobj
            5 0 obj
            << /Type /Catalog /Pages 2 0 R >>
            endobj
            6 0 obj
            ({{new string('x', 600)}})
            endobj
            4 0 obj
            (four)
            endobj
            xref
            0 7
            {free}
            {row:1}
            {row:2}
            {row:3}
            0000000003 00000 n 
            {row:5}
            {row:6}
            trailer
            << /Size 7 /Root 1 0 R >>
            startxref
            {xref:1}
            %%EOF

            """);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 128 } };

        using var document = PdfDocument.Open(file, options);

        document.WasRepaired.Should().BeFalse("a catalog a guard cut is no damage to rebuild the index for");
        document.Reader.Structure.RootCutByGuard.Should().BeTrue();
        document.Catalog.Required().ContainsKey(PdfName.Type).Should().BeFalse("its /Type lies past the cut");

        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("four");

        document.WasRepaired.Should().BeTrue();
        document.Reader.CatalogFoundAs.Should().Be(0);
        document.Catalog.Required().ContainsKey(PdfName.Type).Should().BeFalse("the rebuild took /Root as read too, not object 5");
        document.Diagnostics.Contains(PdfDiagnosticCodes.TrailerRootRecovered).Should().BeFalse();
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
    public void What_an_object_its_bound_cuts_met_before_the_cut_is_still_reported()
    {
        // ADR 34: what the parser met at the cut is the guard's; a stray parenthesis well before it is the file's, and is
        // reported beside the guard.
        var file = new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, Pages)
            .WithObject(3, "<< /A 1 ) /B 2 /Pad (" + new string('x', 3000) + ") >>")
            .BuildClassic(rootNumber: 1);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } });

        document.GetObject(new PdfObjectId(3)).AsDictionary().Required().GetInteger(PdfName.Get("B")).Should().Be(2);

        document.Diagnostics.Select(entry => (entry.Code, entry.Position)).Should().Equal(
            (PdfDiagnosticCodes.LimitObject, (long)OffsetOf(file, "3 0 obj")),
            (PdfDiagnosticCodes.SyntaxUnexpectedToken, (long)OffsetOf(file, ") /B")));
    }

    [Fact]
    public void A_fault_after_a_stream_whose_length_the_file_cannot_hold_is_still_reported_beside_the_bound()
    {
        // The file cannot hold the inner stream's /Length, so only a window reaching its end could settle the length, and
        // the bound stops the reader short of it (#174). The search finds the endstream all the same: what follows it is
        // read whole, and a key given again there is the file's fault, met before the cut.
        var file = new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, Pages)
            .WithObject(3, "<< /S << /Length 99999999 >> stream\nabc\nendstream /A 1 /A 2 >>")
            .WithObject(4, "(" + new string('x', 3000) + ")")
            .BuildClassic(rootNumber: 1);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } });

        document.GetObject(new PdfObjectId(3)).AsDictionary().Required().GetInteger(PdfName.Get("A")).Should().Be(2);

        document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.SyntaxKeyRepeated).Select(entry => entry.Position)
            .Should().Equal((long)OffsetOf(file, "/A 2"));
    }

    [Fact]
    public void An_object_its_bound_cuts_after_more_faults_than_the_diagnostics_hold_still_reports_the_bound()
    {
        // The guard is the one report that names the property to raise: the faults before the cut fill what is left.
        var file = new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, Pages)
            .WithObject(3, "<< " + string.Concat(Enumerable.Repeat(") ", 20)) + "/Pad (" + new string('x', 3000) + ") >>")
            .BuildClassic(rootNumber: 1);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 }, DiagnosticCapacity = 5 };
        using var document = PdfDocument.Open(file, options);

        _ = document.GetObject(new PdfObjectId(3));

        document.Diagnostics[0].Code.Should().Be(PdfDiagnosticCodes.LimitObject);
        document.Diagnostics.Should().HaveCount(5);
        document.Diagnostics.SuppressedCount.Should().Be(16);
    }

    [Fact]
    public void A_key_given_again_before_its_bound_s_cut_is_reported_and_keeps_what_it_held_when_the_cut_leaves_a_keyword_short()
    {
        // The repeat lies at the key, before the cut: it is reported. Its value, a keyword the cut leaves short, reads as a
        // null of the guard's making, which removes nothing.
        const string Body = "<< /Pad (xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx) /A 1 /A true >>";
        var file = new TestPdfBuilder().WithObject(1, Catalog).WithObject(2, Pages).WithObject(3, Body).BuildClassic(rootNumber: 1);
        var bound = OffsetOf(file, "/A true") - OffsetOf(file, "3 0 obj") + "/A tr".Length;
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = bound } });

        document.GetObject(new PdfObjectId(3)).AsDictionary().Required().GetInteger(PdfName.Get("A")).Should().Be(1);

        document.Diagnostics.Select(entry => (entry.Code, entry.Position)).Should().Equal(
            (PdfDiagnosticCodes.LimitObject, (long)OffsetOf(file, "3 0 obj")),
            (PdfDiagnosticCodes.SyntaxKeyRepeated, (long)OffsetOf(file, "/A true")));
    }

    [Fact]
    public void A_number_its_bound_cuts_is_no_number_out_of_range()
    {
        // Four hundred digits are past what a real holds; the bound leaves 350 of them, which say nothing of the number.
        var file = new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, Pages)
            .WithObject(3, "[1 " + new string('9', 400) + "]")
            .BuildClassic(rootNumber: 1);
        var bound = OffsetOf(file, "[1 ") - OffsetOf(file, "3 0 obj") + "[1 ".Length + 350;
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = bound } });

        _ = document.GetObject(new PdfObjectId(3));

        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.LimitObject);
    }

    [Fact]
    public void An_object_its_bound_cuts_anywhere_in_its_last_tokens_reports_the_bound_alone()
    {
        // Wherever the bound falls in the last tokens — inside a keyword, a reference, a name and its escape, a string, a
        // number, between the angle brackets that end the dictionary —, the token it cuts is no fault of the file's, nor is
        // what that token leaves open: only the guard is reported.
        const string Tail = "/K true /L 12 0 R /M [1 2] /N (s) /O <41> /P 1.5 /Q /R#20S /T >>";
        var file = new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, Pages)
            .WithObject(3, "<< /Pad (" + new string('x', 200) + ") " + Tail)
            .BuildClassic(rootNumber: 1);
        var start = OffsetOf(file, "3 0 obj");
        var tail = OffsetOf(file, Tail) - start;
        var failures = new List<string>();

        for (var bound = tail; bound < tail + Tail.Length; bound++)
        {
            using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = bound } });
            _ = document.GetObject(new PdfObjectId(3));
            var codes = string.Join(", ", document.Diagnostics.Select(entry => entry.Code));

            if (codes != PdfDiagnosticCodes.LimitObject)
            {
                failures.Add($"cut after '{Tail[..(bound - tail)]}': {codes}");
            }
        }

        failures.Should().BeEmpty();
    }

    [Theory]
    [InlineData("<< /A 1 ) /B 2 /Pad (", PdfDiagnosticCodes.SyntaxUnexpectedToken)]
    [InlineData("<< /A 1 /B tr", null)]
    [InlineData("<< /A 1 /B [1 (s", null)]
    public void What_a_member_its_object_stream_s_decoding_bound_cuts_met_before_the_cut_is_still_reported(string cutAfter, string? reported)
    {
        // The object stream decodes to more than the bound allows, and its member is cut where the decoded data stops: what
        // the member met before the cut is reported, what the cut leaves open or makes of a token is not.
        var body = cutAfter + "ue) /Pad (" + new string('x', 300) + ") >>";
        var file = new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, Pages)
            .WithObject(4, body)
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [4], compressObjectStream: true);
        var bound = "4 0 ".Length + cutAfter.Length;
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = bound } });

        _ = document.GetObject(new PdfObjectId(4));

        document.Reader.IsCutAtLimit(4).Should().BeTrue();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(
            reported is null ? [PdfDiagnosticCodes.LimitDecodedStream] : [PdfDiagnosticCodes.LimitDecodedStream, reported]);
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

    [Fact]
    public void An_object_as_long_as_its_bound_that_ends_the_file_is_read_whole()
    {
        // The window that holds the object ends where the file does: it cut nothing, however long it is. The object, which
        // lacks its endobj, is the file's to report, not the guard's. Its padding makes it the longest of the file.
        var file = PdfTemplate.Build(PdfTemplate.SoundEndingWith(PaddedArray));
        var length = file.Length - OffsetOf(file, "4 0 obj");
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = length } });

        document.GetObject(new PdfObjectId(4)).AsArray().Required().Should().HaveCount(3);

        document.Diagnostics.Should().BeEmpty();
        new PdfValidator().Validate(document).Findings.Should().ContainSingle()
            .Which.RuleId.Should().Be(PdfValidationRuleIds.ObjectEndObjMissing);
    }

    [Fact]
    public void An_object_one_byte_longer_than_its_bound_that_ends_the_file_is_cut_by_the_bound()
    {
        var file = PdfTemplate.Build(PdfTemplate.SoundEndingWith(PaddedArray));
        var length = file.Length - OffsetOf(file, "4 0 obj");
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = length - 1 } });

        document.GetObject(new PdfObjectId(4)).AsArray().Required().Should().HaveCount(3);

        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.LimitObject);
        new PdfValidator().Validate(document).Findings.Should().NotContain(finding => finding.RuleId == PdfValidationRuleIds.ObjectEndObjMissing);
    }

    [Fact]
    public void A_value_the_chain_reads_as_long_as_the_object_bound_that_ends_the_file_is_read()
    {
        // An update names its previous section through a reference to an object the file ends with, as long as
        // MaxObjectLength allows: the window that holds it ends where the file does, and the chain goes on.
        var file = PdfTemplate.Build("""
            %PDF-1.4
            1 0 obj
            << /Type /Catalog /Pages 2 0 R >>
            endobj
            2 0 obj
            << /Type /Pages /Kids [3 0 R] /Count 1 >>
            endobj
            3 0 obj
            << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>
            endobj
            xref
            0 4
            {free}
            {row:1}
            {row:2}
            {row:3}
            trailer
            << /Size 4 /Root 1 0 R >>
            startxref
            {xref:1}
            %%EOF
            xref
            9 1
            {row:9}
            trailer
            << /Size 10 /Root 1 0 R /Prev 9 0 R >>
            startxref
            {xref:2}
            %%EOF
            9 0 obj
            """ + new string(' ', 200) + "{xref:1}");
        var length = file.Length - OffsetOf(file, "9 0 obj");
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = length } });

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    [Fact]
    public void A_table_as_long_as_its_bound_that_ends_the_file_is_no_table_the_bound_cut()
    {
        // The table's window ends where the file does: the file ends after the rows, before any trailer, and the reader
        // says so rather than blaming the bound.
        var file = PdfTemplate.Build("""
            %PDF-1.7
            1 0 obj
            << /Type /Catalog /Pages 2 0 R >>
            endobj
            2 0 obj
            << /Type /Pages /Kids [3 0 R] /Count 1 >>
            endobj
            3 0 obj
            << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>
            endobj
            startxref
            {xref:1}
            %%EOF
            xref
            0 4
            {free}
            {row:1}
            {row:2}
            {row:3}
            """);
        var length = file.Length - OffsetOf(file, "xref\n0 4");
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = length } });

        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.LimitXRefSectionLength);
        new PdfValidator().Validate(document).Findings.Should().Contain(finding => finding.RuleId == PdfValidationRuleIds.FileTrailerMissing);
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

    [Fact]
    public void A_table_its_length_guard_cuts_makes_room_for_no_more_rows_than_the_guard_lets_the_reader_read()
    {
        // The index makes room for a subsection's rows before it reads them, as many as the bytes left of the section hold:
        // those MaxXRefSectionLength lets the reader read, not the 100 KB of the table past them.
        var file = Numbered(5000).BuildClassic(rootNumber: 1);
        var limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 2048 };

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.LimitXRefSectionLength);
        document.ObjectCount.Should().BeLessThan(103);
        document.Reader.Index.Entries.EnsureCapacity(0).Should().BeLessThan(128, "room is made for about the 102 rows 2 KB hold");
    }

    [Fact]
    public void A_newest_size_makes_room_for_no_object_the_sections_hold_no_row_for()
    {
        // The update claims five million objects, and 2 MB of the file are a string: room was made for as many objects as
        // the file's bytes hold rows of, 100,000, for a document of four. A /Size numbers objects, sparse as they may be,
        // and holds none: room is made for the rows the sections hold.
        var original = new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, Pages)
            .WithObject(3, $"({new string('x', 2 * 1024 * 1024)})")
            .BuildClassic(rootNumber: 1);
        var text = Encoding.Latin1.GetString(TestPdfBuilder.AppendIncrementalUpdate(original, rootNumber: 1, [(1, Catalog)]));
        var newest = text.LastIndexOf("/Size 4 ", StringComparison.Ordinal);
        var file = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, newest), "/Size 5000000 ", text.AsSpan(newest + "/Size 4 ".Length)));

        using var document = PdfDocument.Open(file);

        document.ObjectCount.Should().Be(4);
        document.Diagnostics.Should().BeEmpty();
        document.Reader.Index.Entries.EnsureCapacity(0).Should().BeLessThan(64, "the sections hold four rows");
    }

    /// <summary>Object 4, an array of three numbers without its endobj, padded to be longer than the sound document's objects.</summary>
    private static string PaddedArray { get; } = "4 0 obj\n[1 2 3" + new string(' ', 200) + "]";

    private static string LongArray { get; } = "[" + string.Join(' ', Enumerable.Range(0, 2000)) + "]";

    private static PdfReaderLimits With(PdfReaderLimits limits, string limit, int value) => limit switch
    {
        nameof(PdfReaderLimits.MaxDecodedStreamLength) => limits with { MaxDecodedStreamLength = value },
        nameof(PdfReaderLimits.MaxObjectLength) => limits with { MaxObjectLength = value },
        nameof(PdfReaderLimits.MaxXRefSectionLength) => limits with { MaxXRefSectionLength = value },
        nameof(PdfReaderLimits.MaxXRefSectionCount) => limits with { MaxXRefSectionCount = value },
        nameof(PdfReaderLimits.MaxTrailerLength) => limits with { MaxTrailerLength = value },
        nameof(PdfReaderLimits.MaxIndexEntries) => limits with { MaxIndexEntries = value },
        nameof(PdfReaderLimits.MaxFilterChainLength) => limits with { MaxFilterChainLength = value },
        _ => throw new ArgumentOutOfRangeException(nameof(limit), limit, "Not a reader limit."),
    };

    private static int Bound(PdfReaderLimits limits, string limit) => limit switch
    {
        nameof(PdfReaderLimits.MaxDecodedStreamLength) => limits.MaxDecodedStreamLength,
        nameof(PdfReaderLimits.MaxObjectLength) => limits.MaxObjectLength,
        nameof(PdfReaderLimits.MaxXRefSectionLength) => limits.MaxXRefSectionLength,
        nameof(PdfReaderLimits.MaxXRefSectionCount) => limits.MaxXRefSectionCount,
        nameof(PdfReaderLimits.MaxTrailerLength) => limits.MaxTrailerLength,
        nameof(PdfReaderLimits.MaxIndexEntries) => limits.MaxIndexEntries,
        nameof(PdfReaderLimits.MaxFilterChainLength) => limits.MaxFilterChainLength,
        _ => throw new ArgumentOutOfRangeException(nameof(limit), limit, "Not a reader limit."),
    };

    /// <summary>
    /// Ten objects, among them an encryption dictionary as object 5 and a document information dictionary as object 6,
    /// whose table's trailer holds <paramref name="entries"/> after its /Size and /Root.
    /// </summary>
    private static byte[] Trailed(string entries)
    {
        var builder = new TestPdfBuilder().WithObject(1, Catalog).WithObject(2, Pages);
        for (var number = 3; number <= 10; number++)
        {
            builder.WithObject(number, number switch
            {
                5 => "<< /Filter /Standard /V 1 /R 2 /O <00> /U <00> /P -4 >>",
                6 => "<< /Title (cut) >>",
                _ => "null",
            });
        }

        var written = builder.BuildClassic(rootNumber: 1);
        var text = Encoding.Latin1.GetString(written);
        text.Should().Contain("<< /Size 11 /Root 1 0 R >>");
        return Encoding.Latin1.GetBytes(text.Replace("<< /Size 11 /Root 1 0 R >>", $"<< /Size 11 /Root 1 0 R {entries} >>", StringComparison.Ordinal));
    }

    /// <summary>The catalog, an empty page tree, and objects 3 to <paramref name="last"/>, each its own number.</summary>
    private static TestPdfBuilder Numbered(int last)
    {
        var builder = new TestPdfBuilder().WithObject(1, Catalog).WithObject(2, Pages);
        for (var number = 3; number <= last; number++)
        {
            builder.WithObject(number, string.Create(CultureInfo.InvariantCulture, $"({number})"));
        }

        return builder;
    }

    private static string Hex(string text) => Convert.ToHexString(Encoding.ASCII.GetBytes(text)) + ">";

    /// <summary>
    /// A file whose page, object 3, object stream 4 holds, and whose index is cross-reference stream 5: the object stream's
    /// data passes through <paramref name="objectStreamLayers"/> Flate filters, the rows through <paramref name="rowLayers"/>.
    /// </summary>
    private static byte[] FileOfLayeredStreams(int objectStreamLayers, int rowLayers)
    {
        using var output = new MemoryStream();
        var offsets = new long[6];
        void Write(string text) => output.Write(Encoding.ASCII.GetBytes(text));

        void WriteStream(int number, string entries, byte[] data, int layers)
        {
            for (var layer = 0; layer < layers; layer++)
            {
                using var compressed = new MemoryStream();
                using (var zlib = new ZLibStream(compressed, CompressionLevel.NoCompression, leaveOpen: true))
                {
                    zlib.Write(data);
                }

                data = compressed.ToArray();
            }

            var filters = string.Concat(Enumerable.Repeat("/FlateDecode ", layers));
            offsets[number] = output.Position;
            Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< {entries} /Filter [{filters}] /Length {data.Length} >>\nstream\n"));
            output.Write(data);
            Write("\nendstream\nendobj\n");
        }

        Write("%PDF-1.5\n");
        offsets[1] = output.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[2] = output.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        const string Member = "3 0 << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>";
        WriteStream(4, "/Type /ObjStm /N 1 /First 4", Encoding.ASCII.GetBytes(Member), objectStreamLayers);
        offsets[5] = output.Position;
        var rows = new byte[6 * 7];

        for (var number = 0; number < 6; number++)
        {
            var (type, field, index) = number switch
            {
                0 => (0, 0L, 65535),
                3 => (2, 4L, 0),
                _ => (1, offsets[number], 0),
            };
            rows[number * 7] = (byte)type;
            BinaryPrimitives.WriteUInt32BigEndian(rows.AsSpan((number * 7) + 1), (uint)field);
            BinaryPrimitives.WriteUInt16BigEndian(rows.AsSpan((number * 7) + 5), (ushort)index);
        }

        var xref = offsets[5];
        WriteStream(5, "/Type /XRef /Size 6 /W [1 4 2] /Root 1 0 R", rows, rowLayers);
        Write(string.Create(CultureInfo.InvariantCulture, $"startxref\n{xref}\n%%EOF\n"));
        return output.ToArray();
    }

    /// <summary>
    /// How the reader came by its index and its catalog: as the file wrote them, the catalog looked for among the
    /// indexed objects when <c>/Root</c> was out of reach, or the whole index rebuilt by scanning.
    /// </summary>
    private static string IndexState(PdfDocument document) =>
        document.WasRepaired ? "rebuilt"
        : document.Diagnostics.Contains(PdfDiagnosticCodes.TrailerRootRecovered) ? "catalog recovered"
        : "as written";

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
            "index entries" => ManyObjects(),
            "filter chain" => LongFilterChain(),
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

        private static Case LongFilterChain()
        {
            // Three layers of hex around a word, which a bound of two filters leaves one layer of.
            var file = new TestPdfBuilder()
                .WithObject(1, Catalog)
                .WithObject(2, Pages)
                .Stream(3, "/Filter [/ASCIIHexDecode /ASCIIHexDecode /ASCIIHexDecode]", Hex(Hex(Hex("Hello"))))
                .BuildClassic(rootNumber: 1);

            return new Case(
                nameof(PdfReaderLimits.MaxFilterChainLength),
                PdfDiagnosticCodes.LimitFilterChain,
                file,
                PdfReaderLimits.Default with { MaxFilterChainLength = 2 },
                PdfReaderLimits.Default,
                document => Encoding.ASCII.GetString(document.GetObject(new PdfObjectId(3)).AsStream().Required().Decode(document.Diagnostics).Span),
                "Hello",
                "The /Filter entry names more than 2 filters; decoding stopped after the first 2.",
                OffsetOf(file, "stream\n") + "stream\n".Length,
                ThrowsOnOpening: false,
                ReportsNothingElse: true);
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
            // Three hundred entries, 6 KB of table, which a bound of 2 KB cuts: the entries within it are indexed, and the
            // trailer past the rows left unread is read all the same, with the /Root it holds.
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
                document => document.ObjectCount.ToString(CultureInfo.InvariantCulture),
                "300",
                "The cross-reference table runs past 2 KB; only the entries within it were read.",
                OffsetOf(file, "xref\n"),
                ThrowsOnOpening: true,
                ReportsNothingElse: true);
        }

        private static Case ManySections()
        {
            // A document saved three times after it was written: four sections, of which a bound of two reads
            // the newest. The catalog is only in the oldest, and reads as null until the guard is lifted (#183).
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
                document => document.ObjectCount.ToString(CultureInfo.InvariantCulture),
                "4",
                "The cross-reference chain has more than 2 sections; the older ones were not read.",
                sections[1],
                ThrowsOnOpening: true,
                ReportsNothingElse: false);
        }

        private static Case LongTrailer()
        {
            // A trailer of 100 KB, whose /Root comes after a long string: past the 64 KB a trailer is read to by
            // default, the /Root is out of reach and the catalog is looked for among the indexed objects.
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

        private static Case ManyObjects()
        {
            // Three hundred objects, of which a bound of a hundred indexes the first hundred rows of the table: the
            // catalog is among them, and the rows after them add nothing.
            var builder = new TestPdfBuilder().WithObject(1, Catalog).WithObject(2, Pages);
            for (var number = 3; number < 300; number++)
            {
                builder.WithObject(number, "null");
            }

            var file = builder.BuildClassic(rootNumber: 1);

            return new Case(
                nameof(PdfReaderLimits.MaxIndexEntries),
                PdfDiagnosticCodes.LimitIndexEntries,
                file,
                PdfReaderLimits.Default with { MaxIndexEntries = 100 },
                PdfReaderLimits.Default,
                document => document.ObjectCount.ToString(CultureInfo.InvariantCulture),
                "300",
                "The index reached 100 entries; the objects past them were not indexed.",
                OffsetOf(file, "xref\n"),
                ThrowsOnOpening: true,
                ReportsNothingElse: true);
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

    /// <summary>
    /// Gives the trailer a <c>/Prev</c> that names no section, so that the index may lack objects (T25) and an
    /// object it does not hold is looked for by rebuilding it (T27), rather than read as null.
    /// </summary>
    private static byte[] WithIncompleteIndex(byte[] file)
    {
        var trailer = file.AsSpan().LastIndexOf("trailer\n<<"u8);
        trailer.Should().BeGreaterThan(0);
        var at = trailer + "trailer\n<<".Length;
        return [.. file.AsSpan(0, at), .. " /Prev 1 0 R"u8, .. file.AsSpan(at)];
    }
}
