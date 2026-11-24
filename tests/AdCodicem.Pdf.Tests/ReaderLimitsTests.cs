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

    /// <summary>The data of attached file 20 in <see cref="MissedLengths"/>, which spells object 77.</summary>
    private const string MissedData = "77 0 obj (inner) endobj\n";

    /// <summary>A guard for each bound of <see cref="PdfReaderLimits"/>, the trailer's reached two ways.</summary>
    public static TheoryData<string> Guards =>
        ["decoded stream", "stream length", "object", "section length", "section count", "trailer", "cross-reference stream", "index entries", "filter chain", "rebuild reads", "object-stream decodes", "nesting depth", "nested loads"];

    [Fact]
    public void The_defaults_are_the_bounds_the_reader_kept_before_they_were_options()
    {
        var limits = PdfReaderLimits.Default;

        limits.MaxDecodedStreamLength.Should().Be(256 * 1024 * 1024);
        limits.MaxStreamLength.Should().Be(256 * 1024 * 1024);
        limits.MaxObjectLength.Should().Be(16 * 1024 * 1024);
        limits.MaxXRefSectionLength.Should().Be(64 * 1024 * 1024);
        limits.MaxXRefSectionCount.Should().Be(1024);
        limits.MaxTrailerLength.Should().Be(64 * 1024);
        limits.MaxIndexEntries.Should().Be(8_388_607);
        limits.MaxFilterChainLength.Should().Be(64);
        limits.MaxRebuildReadPasses.Should().Be(4);
        limits.MaxObjectStreamDecodePasses.Should().Be(4);
        limits.MaxNestingDepth.Should().Be(128);
        limits.MaxNestedLoads.Should().Be(64);
        PdfReaderOptions.Default.Limits.Should().BeSameAs(limits);
        PdfReaderOptions.Default.ThrowOnLimit.Should().BeFalse();
    }

    [Fact]
    public void Unbounded_takes_every_guard_to_the_most_the_implementation_holds()
    {
        var limits = PdfReaderLimits.Unbounded;

        limits.MaxDecodedStreamLength.Should().Be(Array.MaxLength);
        limits.MaxStreamLength.Should().Be(Array.MaxLength);
        limits.MaxObjectLength.Should().Be(Array.MaxLength);
        limits.MaxXRefSectionLength.Should().Be(Array.MaxLength);
        limits.MaxXRefSectionCount.Should().Be(int.MaxValue);
        limits.MaxTrailerLength.Should().Be(Array.MaxLength);
        limits.MaxIndexEntries.Should().Be(int.MaxValue);
        limits.MaxFilterChainLength.Should().Be(int.MaxValue);
        limits.MaxRebuildReadPasses.Should().Be(int.MaxValue);
        limits.MaxObjectStreamDecodePasses.Should().Be(int.MaxValue);
        limits.MaxNestingDepth.Should().Be(int.MaxValue);
        limits.MaxNestedLoads.Should().Be(int.MaxValue);
    }

    [Fact]
    public void MaxTrailerLength_bounds_a_cross_reference_stream_s_dictionary_and_not_its_data()
    {
        // The data of a cross-reference stream whose /Length only it indexes was searched for its endstream within the
        // window that read the dictionary, which MaxTrailerLength bounds: a kilobyte of rows past 256 bytes reached the guard.
        // The data is searched in the file, as any stream's whose length is not confirmed (#220).
        var file = ChainFiles.AloneWithIndirectLength(padding: 150);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxTrailerLength = 256 }, ThrowOnLimit = true };

        using var document = PdfDocument.Open(file, options);

        document.Diagnostics.Should().BeEmpty();
        document.Reader.Index.Count.Should().Be(156);
    }

    [Theory]
    [InlineData(nameof(PdfReaderLimits.MaxDecodedStreamLength), 0)]
    [InlineData(nameof(PdfReaderLimits.MaxStreamLength), 0)]
    [InlineData(nameof(PdfReaderLimits.MaxObjectLength), -1)]
    [InlineData(nameof(PdfReaderLimits.MaxXRefSectionLength), int.MinValue)]
    [InlineData(nameof(PdfReaderLimits.MaxXRefSectionCount), 0)]
    [InlineData(nameof(PdfReaderLimits.MaxTrailerLength), -1)]
    [InlineData(nameof(PdfReaderLimits.MaxIndexEntries), 0)]
    [InlineData(nameof(PdfReaderLimits.MaxFilterChainLength), -1)]
    [InlineData(nameof(PdfReaderLimits.MaxRebuildReadPasses), 0)]
    [InlineData(nameof(PdfReaderLimits.MaxObjectStreamDecodePasses), 0)]
    [InlineData(nameof(PdfReaderLimits.MaxNestingDepth), 0)]
    [InlineData(nameof(PdfReaderLimits.MaxNestedLoads), -1)]
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
            MaxStreamLength = int.MaxValue,
            MaxObjectLength = int.MaxValue,
            MaxXRefSectionLength = int.MaxValue,
            MaxXRefSectionCount = int.MaxValue,
            MaxTrailerLength = int.MaxValue,
            MaxIndexEntries = int.MaxValue,
            MaxFilterChainLength = int.MaxValue,
            MaxRebuildReadPasses = int.MaxValue,
            MaxObjectStreamDecodePasses = int.MaxValue,
            MaxNestingDepth = int.MaxValue,
            MaxNestedLoads = int.MaxValue,
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
    [InlineData(nameof(PdfReaderOptions.DiagnosticCapacity))]
    [InlineData(nameof(PdfReaderOptions.ObjectCacheCapacity))]
    public void The_options_refuse_a_negative_capacity(string property)
    {
        var setting = () => property == nameof(PdfReaderOptions.DiagnosticCapacity)
            ? PdfReaderOptions.Default with { DiagnosticCapacity = -1 }
            : PdfReaderOptions.Default with { ObjectCacheCapacity = -1 };

        setting.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(property);
    }

    [Theory]
    [InlineData(0, 64)]
    [InlineData(63, 64)]
    [InlineData(64, 64)]
    [InlineData(65, 65)]
    public void The_cache_keeps_at_least_64_objects_whatever_the_options_ask(int asked, int kept)
    {
        using var document = PdfDocument.Open(
            new TestPdfBuilder().WithObject(1, Catalog).WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>").BuildClassic(rootNumber: 1),
            PdfReaderOptions.Default with { ObjectCacheCapacity = asked });

        document.Reader.CacheCapacity.Should().Be(kept);
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
                MaxRebuildReadPasses = int.MaxValue - 1,
                MaxObjectStreamDecodePasses = int.MaxValue - 1,
                MaxNestingDepth = int.MaxValue - 1,
                MaxNestedLoads = int.MaxValue - 1,
            },
            throwOnLimit: false);

        unbounded.Reach(PdfLimit.DecodedStream, diagnostics, "Cut.", 7);
        unbounded.Reach(PdfLimit.StreamLength, diagnostics, "Cut.", 7);
        unbounded.Reach(PdfLimit.XRefSectionCount, diagnostics, "Cut.", 8);
        justBelow.Reach(PdfLimit.XRefSectionCount, diagnostics, "Cut.", 9);
        unbounded.Reach(PdfLimit.IndexEntries, diagnostics, "Cut.", 10);
        justBelow.Reach(PdfLimit.IndexEntries, diagnostics, "Cut.", 11);
        unbounded.Reach(PdfLimit.FilterChain, diagnostics, "Cut.", 12);
        justBelow.Reach(PdfLimit.FilterChain, diagnostics, "Cut.", 13);
        unbounded.Reach(PdfLimit.RebuildReads, diagnostics, "Cut.", 14);
        justBelow.Reach(PdfLimit.RebuildReads, diagnostics, "Cut.", 15);
        unbounded.Reach(PdfLimit.ObjectStreamDecodes, diagnostics, "Cut.", 16);
        justBelow.Reach(PdfLimit.ObjectStreamDecodes, diagnostics, "Cut.", 17);
        unbounded.Reach(PdfLimit.NestingDepth, diagnostics, "Cut.", 18);
        justBelow.Reach(PdfLimit.NestingDepth, diagnostics, "Cut.", 19);
        unbounded.Reach(PdfLimit.NestedLoads, diagnostics, "Cut.", 20);
        justBelow.Reach(PdfLimit.NestedLoads, diagnostics, "Cut.", 21);

        diagnostics.Select(entry => entry.Message).Should().Equal(
            "Cut. PdfReaderLimits.MaxDecodedStreamLength is already at the most the reader can hold.",
            "Cut. PdfReaderLimits.MaxStreamLength is already at the most the reader can hold.",
            "Cut. PdfReaderLimits.MaxXRefSectionCount is already at the most the reader can hold.",
            "Cut. Raise PdfReaderLimits.MaxXRefSectionCount to read past it.",
            "Cut. PdfReaderLimits.MaxIndexEntries is already at the most the reader can hold.",
            "Cut. Raise PdfReaderLimits.MaxIndexEntries to read past it.",
            "Cut. PdfReaderLimits.MaxFilterChainLength is already at the most the reader can hold.",
            "Cut. Raise PdfReaderLimits.MaxFilterChainLength to read past it.",
            "Cut. PdfReaderLimits.MaxRebuildReadPasses is already at the most the reader can hold.",
            "Cut. Raise PdfReaderLimits.MaxRebuildReadPasses to read past it.",
            "Cut. PdfReaderLimits.MaxObjectStreamDecodePasses is already at the most the reader can hold.",
            "Cut. Raise PdfReaderLimits.MaxObjectStreamDecodePasses to read past it.",
            "Cut. PdfReaderLimits.MaxNestingDepth is already at the most the reader can hold.",
            "Cut. Raise PdfReaderLimits.MaxNestingDepth to read past it.",
            "Cut. PdfReaderLimits.MaxNestedLoads is already at the most the reader can hold.",
            "Cut. Raise PdfReaderLimits.MaxNestedLoads to read past it.");
    }

    [Theory]
    [MemberData(nameof(Guards))]
    public void Reaching_a_guard_keeps_what_fits_and_names_the_property_that_lifts_it(string guard)
    {
        var @case = Case.For(guard);
        using var document = PdfDocument.Open(@case.File, new PdfReaderOptions { Limits = @case.Reached });

        if (@case.ReadsWholeAtGuard)
        {
            @case.Read(document).Should().Be(@case.Whole, $"the {guard} guard cuts only what a walk of the library's reads, which reads it again");
        }
        else
        {
            @case.Read(document).Should().NotBe(@case.Whole, $"the {guard} guard cuts what is read");
        }

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

    [Fact]
    public void A_walk_s_budget_for_decoding_object_streams_again_starts_again_with_the_next_and_cuts_no_read_of_the_caller_s()
    {
        // Eight pages, objects 20 to 27, which name the catalog as their parent, lie in turn in object streams 10 and 11, of
        // 17 MB each. A validation that may decode them again once reads pages 20 to 23, then leaves stream 11, which the
        // reader let go, undecoded: pages 24 and 26 read as null, a guard's cut, and it finds the parent of six pages wrong.
        // The next validation's budget starts again: it reads the pages the cache kept, decodes stream 11, its first
        // decoding in that walk, and finds all eight; the guard is reported once. The caller's reads after them are whole.
        var file = HostileInputTests.AlternatingPages(8, 17 * 1024 * 1024, "<< /Type /Page /Parent 1 0 R >>");
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 } });

        WrongParents(new PdfValidator().Validate(document)).Should().Equal(20, 21, 22, 23, 25, 27);
        WrongParents(new PdfValidator().Validate(document)).Should().Equal(Enumerable.Range(20, 8));
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObjectStreamDecodes);

        for (var number = 20; number < 28; number++)
        {
            document.GetObject(new PdfObjectId(number)).AsDictionary().Required().IsOfType(PdfName.Page).Should().BeTrue();
        }

        static IEnumerable<int> WrongParents(PdfValidationReport report) =>
            report.Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.PageTreeParentWrong).Select(finding => finding.Location.Object!.Value.Number);
    }

    [Fact]
    public void An_object_stream_decoded_with_a_value_a_walk_s_guard_read_as_null_is_neither_hidden_nor_kept_and_reads_whole_after()
    {
        // Object stream 6's /DecodeParms names object 14, which the search for a catalog, past what it may decode again,
        // reads as null: it decodes stream 6 without it, as a stream whose parameters a guard cut, and takes object 18 for one
        // the guard cut. Neither the stream nor what it gave is kept, nor is it taken for one a guard hid: read after the
        // search — which finds the catalog, object 16, reading again what its guard cut —, object 18 reads whole, and nothing
        // is reported of stream 6.
        var file = ObjectStreamsReadInTurnFile(parametersCut: true);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 } });

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObjectStreamDecodes, PdfDiagnosticCodes.TrailerRootRecovered);
        document.Reader.CatalogFoundAs.Should().Be(16);
        document.Reader.ObjectsLeftOutByGuard.Should().BeFalse();
        document.GetObject(new PdfObjectId(18)).AsDictionary().Required().GetRaw(PdfName.Get("Kept")).Should().Be(PdfBoolean.True);
    }

    [Theory]
    [InlineData("/N")]
    [InlineData("/First")]
    [InlineData("/Type")]
    public void An_object_stream_whose_header_value_a_walk_s_guard_read_as_null_is_not_reported_and_reads_whole_after(string key)
    {
        // Object stream 6 takes its value from object 14, which the search for a catalog, past what it may decode again, reads
        // as null: the stream is not reported unreadable for it, and reads whole once the search ends (#395).
        var file = ObjectStreamsReadInTurnFile(parametersCut: true, headerKey: key);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 } });

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObjectStreamDecodes, PdfDiagnosticCodes.TrailerRootRecovered);
        document.GetObject(new PdfObjectId(18)).AsDictionary().Required().GetRaw(PdfName.Get("Kept")).Should().Be(PdfBoolean.True);
    }

    [Theory]
    [InlineData("/N", "2")]
    [InlineData("/First", "8")]
    [InlineData("/Type", "/ObjStm")]
    public void An_object_stream_whose_header_value_MaxObjectLength_cut_is_the_guard_s_and_its_members_read_as_cut(string key, string value)
    {
        // Object stream 10 takes its value from object 5, written past MaxObjectLength: the stream is not reported unreadable,
        // its members read as the guard's cut, the validator checks it in part, and raising the bound reads it whole (#395).
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
            [5] = (1, file.Object(5, new string(' ', 2000) + value), 0),
            [4] = (2, 10, 0),
            [6] = (2, 10, 1),
        };
        const string Data = "4 0 6 6 (four) (six) ";
        var entries = key switch
        {
            "/N" => "/Type /ObjStm /N 5 0 R /First 8",
            "/First" => "/Type /ObjStm /N 2 /First 5 0 R",
            _ => "/Type 5 0 R /N 2 /First 8",
        };
        rows[10] = (1, file.Object(10, string.Create(CultureInfo.InvariantCulture, $"<< {entries} /Length {Data.Length} >>\nstream\n{Data}\nendstream")), 0);
        file.CompressedCrossReferenceStream(20, 21, number => rows.TryGetValue(number, out var row) ? row : (0, 0, 0), "/Root 1 0 R");
        using var cut = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } });
        using var whole = PdfDocument.Open(file.Bytes);

        cut.GetObject(new PdfObjectId(4)).Should().Be(PdfNull.Instance);
        cut.Reader.IsCutAtLimit(4).Should().BeTrue();
        cut.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        var report = new PdfValidator().Validate(cut);
        report.Contains(PdfValidationRuleIds.XRefObjectStreamBroken).Should().BeFalse();
        report.Contains(PdfValidationRuleIds.XRefCheckedInPart).Should().BeTrue();
        string.Create(CultureInfo.InvariantCulture, $"{whole.GetObject(new PdfObjectId(4))}").Should().Be("\"four\"");
    }

    [Fact]
    public void A_search_for_a_catalog_that_reads_again_what_its_guard_cut_and_finds_none_rebuilds_the_index_as_a_whole_search_does()
    {
        // Object 16 is no catalog, and the catalog, object 30, has no row. The search for a catalog, past what it may decode
        // again, reads objects 14 and 16 as null; read again, neither is a catalog, and the index is rebuilt, as a search
        // within its guard rebuilds it, and the rebuild finds object 30.
        var file = ObjectStreamsReadInTurnFile(parametersCut: false, catalogWrittenDirectly: true);
        using var cut = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 } });
        using var whole = PdfDocument.Open(file);

        cut.Diagnostics.Select(entry => entry.Code).Should().Equal(
            PdfDiagnosticCodes.LimitObjectStreamDecodes, PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.TrailerRootRecovered);
        whole.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.TrailerRootRecovered);
        cut.Reader.CatalogFoundAs.Should().Be(30);
        whole.Reader.CatalogFoundAs.Should().Be(30);
    }

    [Fact]
    public void A_search_for_a_catalog_that_takes_one_past_candidates_its_guard_cut_reads_none_again_and_notes_them()
    {
        // Object 16 is no catalog, and the catalog is object 30, written directly past the object streams. The search for a
        // catalog, past what it may decode again, reads objects 14 to 17 as null, then takes object 30, as a search within
        // its guard does, without reading them again: it decoded each object stream twice — once, and once again within its
        // bound —, and decodes neither a third time. It notes that it passed over objects a limit cut.
        var file = ObjectStreamsReadInTurnFile(parametersCut: false, catalogWrittenDirectly: true, catalogRow: true);
        using var source = new OffsetCountingSource(file);
        using var cut = PdfDocument.Open(source, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 } }, ownsSource: false);
        using var whole = PdfDocument.Open(file);

        source.Reads.GetValueOrDefault(DataStart(file, "4 0 obj")).Should().Be(2, "object stream 4 is decoded for objects 10 and 12 alone");
        source.Reads.GetValueOrDefault(DataStart(file, "5 0 obj")).Should().Be(2, "object stream 5 is decoded for objects 11 and 13 alone");
        cut.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObjectStreamDecodes, PdfDiagnosticCodes.TrailerRootRecovered);
        cut.Reader.CatalogFoundAs.Should().Be(30);
        cut.Reader.CatalogCandidateCut.Should().BeTrue();
        whole.Reader.CatalogFoundAs.Should().Be(30);
        whole.Reader.CatalogCandidateCut.Should().BeFalse();

        static long DataStart(byte[] file, string header)
        {
            var at = OffsetOf(file, header);
            return at + file.AsSpan(at).IndexOf("stream\n"u8) + "stream\n".Length;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_validation_past_what_it_may_decode_again_leaves_unread_what_its_guard_cut_and_the_caller_reads_it_whole_after(bool throwOnLimit)
    {
        // A valid page tree lists eight pages, objects 20 to 27, that object streams 10 and 11, of 17 MB each, hold in turn.
        // A validation that may decode them again once has decoded each again once by its fifth page: it leaves the stream
        // the reader then let go undecoded, its pages are ones the guard cut, and the report finds no fault; under
        // ThrowOnLimit the validation throws there, at stream 11. Neither the nulls nor the exception are kept: the caller
        // reads every page whole after it.
        var file = HostileInputTests.AlternatingPages(8, 17 * 1024 * 1024, "<< /Type /Page /Parent 2 0 R >>");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 }, ThrowOnLimit = throwOnLimit };
        using var document = PdfDocument.Open(file, options);

        if (throwOnLimit)
        {
            var validating = () => new PdfValidator().Validate(document);
            var thrown = validating.Should().Throw<PdfLimitExceededException>().Which;
            thrown.Code.Should().Be(PdfDiagnosticCodes.LimitObjectStreamDecodes);
            thrown.Position.Should().Be(OffsetOf(file, "11 0 obj"));
            document.Diagnostics.Should().BeEmpty();
        }
        else
        {
            new PdfValidator().Validate(document).Findings.Should().BeEmpty();
            document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObjectStreamDecodes);
        }

        for (var number = 20; number < 28; number++)
        {
            document.GetObject(new PdfObjectId(number)).AsDictionary().Required().IsOfType(PdfName.Page).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("filter", false)]
    [InlineData("parameters", false)]
    [InlineData("length", false)]
    [InlineData("filter", true)]
    [InlineData("parameters", true)]
    [InlineData("length", true)]
    public void An_object_stream_whose_decoding_meets_what_a_walk_s_guard_cut_and_gives_nothing_is_decoded_once_however_many_of_its_objects_the_walk_reads(string value, bool catalogCut)
    {
        // The search for a catalog, past what it may decode again, reads object 28 as null, and decodes object stream 60
        // without it — its /Filter, its /DecodeParms, or its /Length —: the stream gives nothing, and its hundred objects
        // read as null for the guard, the stream decoded once, not once for each. Where the catalog is one the guard cut too,
        // the search reads again what it cut, stream by stream, decoding each once more at most: object 28, read whole there,
        // is let go before stream 60 is decoded again, which it reads as null again, the stream decoded once more. Read by
        // the caller after the search, stream 60 serves its objects.
        var file = DecodedThroughACutValueFile(value, 100, catalogCut, out var dataStart);
        using var source = new OffsetCountingSource(file);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 }, ObjectCacheCapacity = 64 };
        using var document = PdfDocument.Open(source, options, ownsSource: false);

        source.Reads.GetValueOrDefault(dataStart).Should().Be(catalogCut ? 2 : 1, "stream 60 is decoded once a walk");
        document.Reader.CatalogFoundAs.Should().Be(FirstMemberOfSixty + 100);
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObjectStreamDecodes, PdfDiagnosticCodes.TrailerRootRecovered);
        document.GetObject(new PdfObjectId(FirstMemberOfSixty)).AsDictionary().Required().GetRaw(PdfName.Get("Kept")).Should().Be(PdfBoolean.True);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_object_stream_that_gives_nothing_for_the_file_s_own_fault_in_a_walk_past_its_guard_is_kept_decoded_once_and_serves_none(bool catalogCut)
    {
        // Object stream 60 needs no value the search for a catalog, past what it may decode again, reads as null: a stray
        // token heads its header, which lists no object, and it gives nothing for the file's own fault. What it gave is kept,
        // as any decoding's is: it is decoded once, however many of its hundred objects the search reads — and where the
        // catalog is one the guard cut too, and the search reads again what it cut, not once more —, reported unreadable,
        // and serves none of them, to the caller after the search either.
        var file = DecodedThroughACutValueFile("own", 100, catalogCut, out var dataStart);
        using var source = new OffsetCountingSource(file);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 }, ObjectCacheCapacity = 64 };
        using var document = PdfDocument.Open(source, options, ownsSource: false);

        source.Reads.GetValueOrDefault(dataStart).Should().Be(1, "stream 60 is decoded once");
        document.Reader.CatalogFoundAs.Should().Be(FirstMemberOfSixty + 100);
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(
            PdfDiagnosticCodes.LimitObjectStreamDecodes, PdfDiagnosticCodes.ObjectStreamUnreadable, PdfDiagnosticCodes.TrailerRootRecovered);
        document.GetObject(new PdfObjectId(FirstMemberOfSixty)).Should().Be(PdfNull.Instance);
    }

    [Fact]
    public void An_object_stream_whose_decoding_meets_what_a_walk_s_guard_cut_and_rebuilds_the_index_serves_its_objects_from_the_rebuilt_index()
    {
        // The search for a catalog, past what it may decode again, reads object 28, the /Length of object stream 60, as null,
        // and decodes the stream without it for object 61. The stream's /N, object 63, is not where its row places it:
        // loading it rebuilds the index on the way, and the search ends there. What the decoding gave through the guard's cut
        // is neither kept nor served: the document reads and reports as one whose search decodes as much as it needs, the
        // guard besides — the rebuilt index's own search takes the catalog, object 62, and serves object 61 whole, from
        // stream 60 read with its /Length.
        var file = DecodedThroughACutValueFile("length", 1, catalogCut: false, out _, countMisplaced: true);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 } });
        using var whole = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = int.MaxValue } });

        whole.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.LimitObjectStreamDecodes);
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(
            whole.Diagnostics.Select(entry => entry.Code).Prepend(PdfDiagnosticCodes.LimitObjectStreamDecodes));
        document.WasRepaired.Should().BeTrue();
        document.Reader.CatalogFoundAs.Should().Be(FirstMemberOfSixty + 1);
        document.Reader.ObjectsLeftOutByGuard.Should().BeFalse();
        document.GetObject(new PdfObjectId(FirstMemberOfSixty)).AsDictionary().Required().GetRaw(PdfName.Get("Kept")).Should().Be(PdfBoolean.True);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(200)]
    public void An_object_whose_load_meets_what_a_walk_s_guard_cut_is_read_once_a_walk_however_many_candidates_lead_to_it_and_whole_after(int candidates)
    {
        // The candidates for a catalog lead through their /Type to object 61, a stream whose /Length, object 28, the search,
        // past what it may decode again, reads as null: the stream is read up to the first endstream its data spells, and
        // kept for the rest of the search, not read again for each candidate. The cache lets go of it as the search ends,
        // and of its place in the order the cache lets go in. Read by the caller after the search, its /Length is read, and
        // its data is whole.
        var file = CandidatesThroughALengthCutFile(candidates, out var at);
        using var source = new OffsetCountingSource(file);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 } };
        using var document = PdfDocument.Open(source, options, ownsSource: false);

        // Object 61 is read from the end of the line before its header, and is a candidate itself.
        source.Reads.GetValueOrDefault(at - 1).Should().Be(1, "object 61 is read once a walk");
        document.Reader.CatalogFoundAs.Should().Be(FirstCandidate + candidates);
        document.Reader.CatalogCandidateCut.Should().BeTrue();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitObjectStreamDecodes, PdfDiagnosticCodes.TrailerRootRecovered);
        document.Reader.CacheCounts.Ordered.Should().Be(document.Reader.CacheCounts.Held);
        Encoding.Latin1.GetString(document.GetObject(new PdfObjectId(61)).AsStream().Required().Decode().Span).Should().Be("01\nendstream\nendobj\n89");
    }

    [Theory]
    [InlineData("type", 20)]
    [InlineData("type", 150)]
    [InlineData("object stream length", 20)]
    [InlineData("object stream length", 150)]
    [InlineData("object stream type", 20)]
    [InlineData("object stream type", 150)]
    public void Under_ThrowOnLimit_an_object_whose_load_threw_a_walk_s_guard_is_read_once_a_walk_however_many_candidates_lead_to_it(string through, int candidates)
    {
        // Past what the search for a catalog may decode again, object 28 throws the guard's exception, and so does each read
        // that needs it: object 61, a stream whose /Length it is, which the candidates name as their /Type, or as the /Type
        // of object stream 60 that holds them; or object stream 60 itself, whose /Length it is, and which holds them. That
        // object is read once in the search, not once for each candidate, and opening throws the guard's exception once the
        // search ends.
        var file = through switch
        {
            "type" => CandidatesThroughALengthCutFile(candidates, out _),
            "object stream type" => CandidatesThroughALengthCutFile(candidates, out _, heldByObjectStream: true),
            _ => DecodedThroughACutValueFile("length", candidates, catalogCut: false, out _),
        };
        var at = OffsetOf(file, through == "object stream length" ? "\n60 0 obj" : "\n61 0 obj");
        using var source = new OffsetCountingSource(file);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 }, ThrowOnLimit = true };
        var opening = () => PdfDocument.Open(source, options, ownsSource: false);

        opening.Should().Throw<PdfLimitExceededException>().Which.Code.Should().Be(PdfDiagnosticCodes.LimitObjectStreamDecodes);
        source.Reads.GetValueOrDefault(at).Should().Be(1, "the object is read once a walk");
    }

    [Theory]
    [InlineData("type")]
    [InlineData("object stream length")]
    public void Within_a_walk_under_ThrowOnLimit_a_load_its_guard_threw_through_throws_again_without_reading_until_the_walk_ends(string through)
    {
        // A walk reads objects 10 to 33 in turn, decoding their object streams again once at most, and object 28 throws the
        // guard's exception. Object 61 needs object 28: a stream whose /Length it is, or a member of object stream 60, whose
        // /Length it is. Read again within the walk, object 61 throws the same exception again, and neither it nor object
        // stream 60 is read again. Once the walk ends, both read whole.
        var file = through == "type"
            ? CandidatesThroughALengthCutFile(1, out _, rootIsCatalog: true)
            : DecodedThroughACutValueFile("length", 1, catalogCut: false, out _, rootIsCatalog: true);
        var at = OffsetOf(file, through == "type" ? "\n61 0 obj" : "\n60 0 obj");
        using var source = new OffsetCountingSource(file);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 }, ThrowOnLimit = true };
        using var document = PdfDocument.Open(source, options, ownsSource: false);
        var reading = () => document.GetObject(new PdfObjectId(61));
        PdfLimitExceededException first, again;
        int readInWalk;
        document.Reader.BeginWalk("validation");

        try
        {
            for (var number = 10; number < 34; number++)
            {
                ReadPastGuard(document, number);
            }

            first = reading.Should().Throw<PdfLimitExceededException>().Which;
            again = reading.Should().Throw<PdfLimitExceededException>().Which;
            readInWalk = source.Reads.GetValueOrDefault(at);
        }
        finally
        {
            document.Reader.EndWalk();
        }

        first.Code.Should().Be(PdfDiagnosticCodes.LimitObjectStreamDecodes);
        again.Should().BeSameAs(first);
        readInWalk.Should().Be(1, "the object is read once a walk");
        var whole = document.GetObject(new PdfObjectId(61));

        if (through == "type")
        {
            Encoding.Latin1.GetString(whole.AsStream().Required().Decode().Span).Should().Be("01\nendstream\nendobj\n89");
        }
        else
        {
            whole.AsDictionary().Required().GetRaw(PdfName.Get("Kept")).Should().Be(PdfBoolean.True);
            document.GetObject(new PdfObjectId(60)).AsStream().Required().Dictionary.IsOfType(PdfName.ObjStm).Should().BeTrue();
        }

        source.Reads.GetValueOrDefault(at).Should().Be(2, "the object is read again once the walk ends");
    }

    [Fact]
    public void Within_a_walk_under_ThrowOnLimit_the_loads_its_guard_threw_through_throw_again_the_first_exception_it_threw_and_not_each_its_own()
    {
        // A walk reads objects 10 to 33 in turn, decoding their object streams again once at most: the twelve objects 16 to
        // 32 that streams 4 and 5 hold each throw an exception of their own, the walk leaving their stream undecoded. Read
        // again within the walk, each throws the first the walk threw: the walk keeps one exception, however many loads it
        // threw through.
        var file = CandidatesThroughALengthCutFile(1, out _, rootIsCatalog: true);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 }, ThrowOnLimit = true };
        using var document = PdfDocument.Open(file, options);
        var thrown = new List<(int Number, PdfLimitExceededException Exception)>();
        var again = new List<PdfLimitExceededException>();
        document.Reader.BeginWalk("validation");

        try
        {
            for (var number = 10; number < 34; number++)
            {
                if (ReadPastGuard(document, number) is { } exception)
                {
                    thrown.Add((number, exception));
                }
            }

            foreach (var (number, _) in thrown)
            {
                again.Add(ReadPastGuard(document, number)!);
            }
        }
        finally
        {
            document.Reader.EndWalk();
        }

        thrown.Select(each => each.Number).Should().Equal(16, 17, 19, 20, 22, 23, 25, 26, 28, 29, 31, 32);
        thrown.Select(each => each.Exception).Should().OnlyHaveUniqueItems();
        again.Should().AllSatisfy(exception => exception.Should().BeSameAs(thrown[0].Exception));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Each_walk_weighs_what_it_decodes_again_from_nothing_and_keeps_nothing_of_the_walk_before_it(bool throwOnLimit)
    {
        // Sixteen pages, objects 20 to 35, lie in turn in object streams 10 and 11, of 17 MB each. A walk reads pages 20 to
        // 27, decoding them again once at most: it decodes both streams, then each once again, and leaves pages 24 and 26
        // unread, the guard's cut — under ThrowOnLimit, each read throws, and a read again throws what the walk threw first
        // for it. The next walk reads pages 28 to 35, and weighs what it decodes from nothing: it cuts pages 32 and 34 alike,
        // and keeps its own exception. Once both end, no page is one a guard cut, and each reads whole (#181).
        var file = HostileInputTests.AlternatingPages(16, 17 * 1024 * 1024, "<< /Type /Page /Parent 2 0 R >>");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 }, ThrowOnLimit = throwOnLimit };
        using var document = PdfDocument.Open(file, options);

        var first = Walk(document, 20);
        var second = Walk(document, 28);

        first.Cut.Should().Equal(24, 26);
        second.Cut.Should().Equal(32, 34);

        if (throwOnLimit)
        {
            first.Again.Should().BeSameAs(first.Thrown);
            second.Again.Should().BeSameAs(second.Thrown);
            second.Thrown.Should().NotBeSameAs(first.Thrown);
        }

        for (var number = 20; number < 36; number++)
        {
            document.Reader.IsCutAtLimit(number).Should().BeFalse();
            document.GetObject(new PdfObjectId(number)).AsDictionary().Required().IsOfType(PdfName.Page).Should().BeTrue();
        }

        static (List<int> Cut, PdfLimitExceededException? Thrown, PdfLimitExceededException? Again) Walk(PdfDocument document, int from)
        {
            var cut = new List<int>();
            PdfLimitExceededException? thrown = null;
            PdfLimitExceededException? again = null;
            document.Reader.BeginWalk("validation");

            try
            {
                for (var number = from; number < from + 8; number++)
                {
                    try
                    {
                        if (document.GetObject(new PdfObjectId(number)) is PdfNull)
                        {
                            cut.Add(number);
                        }
                    }
                    catch (PdfLimitExceededException exception)
                    {
                        cut.Add(number);
                        thrown ??= exception;
                    }
                }

                if (thrown is not null)
                {
                    again = ReadPastGuard(document, cut[0]);
                }
            }
            finally
            {
                document.Reader.EndWalk();
            }

            return (cut, thrown, again);
        }
    }

    [Fact]
    public void What_the_reader_decodes_outside_a_walk_weighs_nothing_in_the_walk_after_it()
    {
        // Sixteen pages, objects 20 to 35, lie in turn in object streams 10 and 11, of 17 MB each. The caller reads pages 34
        // and 35, which decodes both streams outside any walk. A walk then reads pages 20 to 27, decoding them again once at
        // most: what it weighs starts from its own first decodings, and it leaves pages 24 and 26 unread, as a walk with
        // nothing decoded before it does (#181).
        var file = HostileInputTests.AlternatingPages(16, 17 * 1024 * 1024, "<< /Type /Page /Parent 2 0 R >>");
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 } });
        var cut = new List<int>();
        document.GetObject(new PdfObjectId(34)).Should().BeOfType<PdfDictionary>();
        document.GetObject(new PdfObjectId(35)).Should().BeOfType<PdfDictionary>();
        document.Reader.BeginWalk("validation");

        try
        {
            for (var number = 20; number < 28; number++)
            {
                if (document.GetObject(new PdfObjectId(number)) is PdfNull)
                {
                    cut.Add(number);
                }
            }
        }
        finally
        {
            document.Reader.EndWalk();
        }

        cut.Should().Equal(24, 26);
    }

    [Fact]
    public void An_object_an_object_stream_decoded_through_a_walk_s_cut_holds_is_one_the_guard_cut_until_the_walk_ends()
    {
        // A walk reads objects 10 to 33 in turn, decoding their object streams again once at most, and object 28 reads as null,
        // the guard's cut. Object stream 60 takes its /DecodeParms from 28: decoded without it, it gives nothing, and object
        // 61, which it holds, reads as null, one the guard cut while the walk lasts. Once it ends, 61 reads whole, and is no
        // object a guard cut (#181).
        var file = DecodedThroughACutValueFile("parameters", 1, catalogCut: false, out _, rootIsCatalog: true);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 } });
        PdfObject member;
        bool cut;
        document.Reader.BeginWalk("validation");

        try
        {
            for (var number = 10; number < 34; number++)
            {
                _ = document.GetObject(new PdfObjectId(number));
            }

            member = document.GetObject(new PdfObjectId(FirstMemberOfSixty));
            cut = document.Reader.IsCutAtLimit(FirstMemberOfSixty);
        }
        finally
        {
            document.Reader.EndWalk();
        }

        member.Should().Be(PdfNull.Instance);
        cut.Should().BeTrue();
        document.GetObject(new PdfObjectId(FirstMemberOfSixty)).AsDictionary().Required().GetRaw(PdfName.Get("Kept")).Should().Be(PdfBoolean.True);
        document.Reader.IsCutAtLimit(FirstMemberOfSixty).Should().BeFalse();
    }

    [Fact]
    public void Under_ThrowOnLimit_a_rebuild_a_search_for_a_catalog_sets_off_lets_go_of_the_loads_the_walk_s_guard_threw_through()
    {
        // The trailer's /Root names a string, and its /Prev names no section. Opened to throw, the search for a catalog reads
        // objects 10 to 33 in turn, past what it may decode again: object 28 throws the guard's exception, and so does stream
        // 61, whose /Length it is. Object 999, whose /Type names an object the index lacks, rebuilds it as it is read: the
        // rebuild lets go of what the walk kept of the loads its guard threw through, 61 among them, and loads every object,
        // to tell the object streams apart, 61 whole. The rebuild's own search took the first's place, which reads nothing
        // again: streams 4, 5 and 6 are each decoded three times — twice by the first search, once by the rebuild —, and
        // opening throws the guard's exception (#181).
        const string Data = "01\nendstream\nendobj\n89";
        var file = new HandWrittenFile(version: "1.5");
        var rows = WriteStreamsCutInTurn(file, Data.Length.ToString(CultureInfo.InvariantCulture), catalog: 0);
        rows[61] = (1, file.Object(61, $"<< /Length 28 0 R >>\nstream\n{Data}\nendstream"), 0);
        rows[999] = (1, file.Object(999, "<< /Type 5000 0 R >>"), 0);
        rows[1000] = (1, file.Object(1000, Catalog), 0);
        file.CompressedCrossReferenceStream(1001, 1002, number => rows.TryGetValue(number, out var row) ? row : (0, 0, 0), "/Root 1 0 R /Prev 1 0 R");
        var bytes = file.Bytes;
        using var source = new OffsetCountingSource(bytes);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 }, ThrowOnLimit = true };
        var opening = () => PdfDocument.Open(source, options, ownsSource: false);

        opening.Should().Throw<PdfLimitExceededException>().Which.Code.Should().Be(PdfDiagnosticCodes.LimitObjectStreamDecodes);

        foreach (var header in (string[])["4 0 obj", "5 0 obj", "6 0 obj"])
        {
            var at = OffsetOf(bytes, header);
            var dataStart = at + bytes.AsSpan(at).IndexOf("stream\n"u8) + "stream\n".Length;
            source.Reads.GetValueOrDefault(dataStart).Should().Be(3, $"the first search decodes stream {header[0]} twice, and the rebuild once");
        }
    }

    [Theory]
    [InlineData("none")]
    [InlineData("a candidate read through a stream decoded once")]
    [InlineData("the catalog cut short")]
    [InlineData("a candidate that rebuilds the index")]
    [InlineData("no catalog, a candidate that rebuilds the index")]
    public void A_search_for_a_catalog_reads_again_what_its_guard_cut_stream_by_stream_and_takes_the_first_catalog_in_its_order(string shape)
    {
        // The trailer's /Root names a string. Object streams 4, 5 and 6, of 17 MB each, hold objects 40 to 63 in turn, and 4
        // object 65 besides; objects 58, in 4, and 59, in 5, are catalogs, and object 64, written directly, takes its /Type
        // from 47, in 5. The search for a catalog, past what it may decode again, leaves the objects of streams 4 and 5 after
        // 45 unread, and 64; it reads them again stream by stream, each decoded once more, and takes 58, the first catalog in
        // its order, past which it reads nothing: neither 59, nor 64, read first, whose /Type would have had stream 5 decoded
        // twice. A candidate it reads again that would read a stream decoded already — 51, in 6, whose /Type is 65 — or the
        // catalog itself, cut short of its end by MaxObjectLength, is a candidate a limit cut, which it notes. One that names
        // an object the index lacks — 49, in 4, whose /Type is 5000 — rebuilds the index as it is read, and the search ends:
        // the rebuild's own search took its place, and takes object 70, a catalog the cross-reference stream does not place,
        // before any candidate its guard would cut. Where the file holds no catalog — 58 and 59 none, and no
        // 70 —, the rebuild's search, within the search that set it off, takes none, and notes the candidates its guard cut,
        // stream 5 decoded for 64 already (#181).
        var file = SearchCutFile(shape);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1, MaxObjectLength = 1024 } };
        using var document = PdfDocument.Open(file, options);

        document.WasRepaired.Should().Be(shape.EndsWith("a candidate that rebuilds the index", StringComparison.Ordinal));
        document.Reader.CatalogFoundAs.Should().Be(shape switch { "a candidate that rebuilds the index" => 70, "no catalog, a candidate that rebuilds the index" => 0, _ => 58 });
        document.Reader.CatalogCandidateCut.Should().Be(shape is not "none" and not "a candidate that rebuilds the index");
    }

    [Fact]
    public void A_search_for_a_catalog_a_rebuild_sets_off_under_ThrowOnLimit_notes_a_candidate_a_guard_threw_for_after_the_catalog_it_takes()
    {
        // Object 1, the catalog the cross-reference stream places, is defined again after it as a string, and the stream's
        // /Prev names no section. Objects 20 to 27, written directly, take their /Type from objects of object streams 4, 5
        // and 6, of 17 MB each, in turn, and the catalog, 29, from one of 5 that names /Catalog; object 30, after it, runs
        // past MaxObjectLength. Opened to throw, a lookup of object 5000, which the index lacks, rebuilds it, which takes the
        // later definition of 1: its search for a catalog, past what it may decode again, leaves candidates unread, 29 among
        // them, and the guard on objects' length throws for 30. It reads again what the walk's guard left unread, and takes
        // 29, past which it reads nothing: 30 is a candidate a limit cut all the same, which it notes (#181).
        var file = ObjectStreamsBehindTypesFile();
        var options = new PdfReaderOptions
        {
            ThrowOnLimit = true,
            Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1, MaxObjectLength = 1024 },
        };
        using var document = PdfDocument.Open(file, options);
        document.Reader.CatalogFoundAs.Should().Be(0);

        ReadPastGuard(document, 5000).Should().NotBeNull();

        document.WasRepaired.Should().BeTrue();
        document.Reader.CatalogFoundAs.Should().Be(29);
        document.Reader.CatalogCandidateCut.Should().BeTrue();
    }

    /// <summary>
    /// Writes the file of <see cref="A_search_for_a_catalog_a_rebuild_sets_off_under_ThrowOnLimit_notes_a_candidate_a_guard_threw_for_after_the_catalog_it_takes"/>.
    /// </summary>
    private static byte[] ObjectStreamsBehindTypesFile()
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
        };
        (int Stream, int[] Members)[] streams = [(4, [40, 41, 42]), (5, [50, 51, 52, 53]), (6, [60, 61])];

        foreach (var (stream, members) in streams)
        {
            rows[stream] = (1, file.PaddedObjectStream(stream, [.. members.Select(number => (number, number == 53 ? "/Catalog" : "/Thing"))], ObjectStreamDecoded), 0);

            for (var index = 0; index < members.Length; index++)
            {
                rows[members[index]] = (2, stream, index);
            }
        }

        foreach (var (number, type) in (ReadOnlySpan<(int, int)>)[(20, 40), (21, 50), (22, 60), (23, 41), (24, 51), (25, 61), (26, 42), (27, 52)])
        {
            rows[number] = (1, file.Object(number, string.Create(CultureInfo.InvariantCulture, $"<< /Type {type} 0 R >>")), 0);
        }

        rows[29] = (1, file.Object(29, "<< /Type 53 0 R /Pages 2 0 R >>"), 0);
        rows[30] = (1, file.Object(30, "<< /Padding (" + new string('x', 2000) + ") >>"), 0);
        file.Object(1, "(not a catalog)");
        file.CompressedCrossReferenceStream(70, 71, number => rows.TryGetValue(number, out var row) ? row : (0, 0, 0), "/Root 1 0 R /Prev 1 0 R");
        return file.Bytes;
    }

    [Fact]
    public void A_search_for_a_catalog_a_rebuild_sets_off_under_ThrowOnLimit_notes_a_candidate_its_second_reading_throws_for_before_the_catalog()
    {
        // Object 1, the catalog the cross-reference stream places, is defined again after it as a string, and the stream's
        // /Prev names no section. Objects 20 to 27, written directly, take their /Type from objects of object streams 4, 5 and
        // 6, of 17 MB each, in turn. Opened to throw, a lookup of object 5000, which the index lacks, rebuilds it, which takes
        // the later definition of 1: its search for a catalog, past what it may decode again, leaves the objects of stream 5
        // after 51 unread. Reading them again, it decodes 5 once more: object 55, which runs past MaxObjectLength, throws, and
        // object 56, after it, is a catalog, which the search takes, noting the candidate a limit cut before it (#181).
        var file = MemberPastTheBoundBeforeACatalogFile();
        var options = new PdfReaderOptions
        {
            ThrowOnLimit = true,
            Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1, MaxObjectLength = 1024 },
        };
        using var document = PdfDocument.Open(file, options);
        document.Reader.CatalogFoundAs.Should().Be(0);

        ReadPastGuard(document, 5000).Should().NotBeNull();

        document.WasRepaired.Should().BeTrue();
        document.Reader.CatalogFoundAs.Should().Be(56);
        document.Reader.CatalogCandidateCut.Should().BeTrue();
    }

    /// <summary>
    /// Writes the file of <see cref="A_search_for_a_catalog_a_rebuild_sets_off_under_ThrowOnLimit_notes_a_candidate_its_second_reading_throws_for_before_the_catalog"/>.
    /// </summary>
    private static byte[] MemberPastTheBoundBeforeACatalogFile()
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
        };
        (int Stream, int[] Members)[] streams = [(4, [40, 41, 42]), (5, [50, 51, 52, 53, 55, 56]), (6, [60, 61])];

        foreach (var (stream, members) in streams)
        {
            rows[stream] = (1, file.PaddedObjectStream(stream, [.. members.Select(number => (number, Member(number)))], ObjectStreamDecoded), 0);

            for (var index = 0; index < members.Length; index++)
            {
                rows[members[index]] = (2, stream, index);
            }
        }

        foreach (var (number, type) in (ReadOnlySpan<(int, int)>)[(20, 40), (21, 50), (22, 60), (23, 41), (24, 51), (25, 61), (26, 42), (27, 52)])
        {
            rows[number] = (1, file.Object(number, string.Create(CultureInfo.InvariantCulture, $"<< /Type {type} 0 R >>")), 0);
        }

        file.Object(1, "(not a catalog)");
        file.CompressedCrossReferenceStream(70, 71, number => rows.TryGetValue(number, out var row) ? row : (0, 0, 0), "/Root 1 0 R /Prev 1 0 R");
        return file.Bytes;

        static string Member(int number) => number switch
        {
            55 => "<< /Padding (" + new string('x', 2000) + ") >>",
            56 => Catalog,
            _ => "/Thing",
        };
    }

    [Fact]
    public void An_object_stream_a_rebuild_decodes_again_as_it_takes_the_object_streams_in_weighs_nothing_in_the_walk_it_runs_in()
    {
        // The trailer's /Root names a string, and its /Prev names no section: the search for a catalog reads object 3, whose
        // /Type is object 5000, which the index lacks, and the index is rebuilt within the search. It takes the object streams
        // in from the last written: 6, whose /Length is object 90, which no stream taken in holds yet, waits; 5, whose
        // /DecodeParms is object 7, which holds a reference to an object the file lacks, is decoded and not kept; 4 is. Then
        // 6, loaded again, reads 90 from 5, which is decoded again — the rebuild's taking in, which no walk weighs. The
        // rebuild's own search reads objects 20 to 24, whose /Type lies in turn in streams 5 and 4, of 17 MB each, which the
        // reader cannot keep together: within what it may decode again, it reads 24 whole, a catalog, and takes it (#181).
        var file = ObjectStreamDecodedAgainAsItIsTakenInFile();
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 } };
        using var document = PdfDocument.Open(file, options);

        document.WasRepaired.Should().BeTrue();
        document.Reader.CatalogFoundAs.Should().Be(24);
        document.Diagnostics.Should().NotContain(d => d.Code == PdfDiagnosticCodes.LimitObjectStreamDecodes);
    }

    /// <summary>
    /// Writes the file of <see cref="An_object_stream_a_rebuild_decodes_again_as_it_takes_the_object_streams_in_weighs_nothing_in_the_walk_it_runs_in"/>.
    /// </summary>
    private static byte[] ObjectStreamDecodedAgainAsItIsTakenInFile()
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "(not a catalog)"), 0),
            [2] = (1, file.Object(2, Pages), 0),
            [3] = (1, file.Object(3, "<< /Type 5000 0 R >>"), 0),
        };
        file.Object(7, "999 0 R");
        file.PaddedObjectStream(4, [(41, "/Thing"), (43, "/Thing")], ObjectStreamDecoded);
        file.PaddedObjectStream(5, [(40, "/Thing"), (42, "/Thing"), (44, "/Catalog"), (90, "9")], ObjectStreamDecoded, "/DecodeParms 7 0 R ");
        file.Object(6, "<< /Type /ObjStm /N 1 /First 6 /Length 90 0 R >>\nstream\n100 0 (x)\nendstream");

        foreach (var (number, type) in (ReadOnlySpan<(int, int)>)[(20, 40), (21, 41), (22, 42), (23, 43)])
        {
            file.Object(number, string.Create(CultureInfo.InvariantCulture, $"<< /Type {type} 0 R >>"));
        }

        file.Object(24, "<< /Type 44 0 R /Pages 2 0 R >>");
        file.CompressedCrossReferenceStream(8, 9, number => rows.TryGetValue(number, out var row) ? row : (0, 0, 0), "/Root 1 0 R /Prev 1 0 R");
        return file.Bytes;
    }

    [Fact]
    public void A_search_for_a_catalog_within_another_walk_reads_none_of_the_candidates_its_guard_cut_again()
    {
        // The trailer's /Root names a string, and its /Prev names no section: the search for a catalog reads object 3, whose
        // /Type is object 5000, which the index lacks, and the index is rebuilt within the search. The rebuild's own search
        // reads objects 20 to 24, whose /Type lies in turn in object streams 5 and 4, of 17 MB each, which the reader cannot
        // keep together, past what it may decode again: 24 is a candidate its guard cut, and none is a catalog. Eighty
        // objects read after 24 let the cache, of 64 objects, drop it. Within another walk, whose budget it counts with, the
        // search reads none of what its guard cut again, and notes the cut: 24 is read twice, by the rebuild, which loads
        // every object it places, and by the search (#181).
        var (file, candidate) = SearchWithinASearchFile();
        using var source = new OffsetCountingSource(file);
        var options = new PdfReaderOptions { ObjectCacheCapacity = 64, Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 } };
        using var document = PdfDocument.Open(source, options, ownsSource: false);

        document.WasRepaired.Should().BeTrue();
        document.Reader.CatalogFoundAs.Should().Be(0);
        document.Reader.CatalogCandidateCut.Should().BeTrue();
        source.Reads.GetValueOrDefault(candidate).Should().Be(2);
    }

    /// <summary>
    /// Writes the file of <see cref="A_search_for_a_catalog_within_another_walk_reads_none_of_the_candidates_its_guard_cut_again"/>,
    /// and gives where object 24 starts.
    /// </summary>
    private static (byte[] File, long Candidate) SearchWithinASearchFile()
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "(not a catalog)"), 0),
            [2] = (1, file.Object(2, Pages), 0),
            [3] = (1, file.Object(3, "<< /Type 5000 0 R >>"), 0),
        };
        file.PaddedObjectStream(4, [(41, "/Thing"), (43, "/Thing"), (45, "/Thing")], ObjectStreamDecoded);
        file.PaddedObjectStream(5, [(40, "/Thing"), (42, "/Thing"), (44, "/Thing")], ObjectStreamDecoded);

        foreach (var (number, type) in (ReadOnlySpan<(int, int)>)[(20, 40), (21, 41), (22, 42), (23, 43)])
        {
            file.Object(number, string.Create(CultureInfo.InvariantCulture, $"<< /Type {type} 0 R >>"));
        }

        var candidate = file.Object(24, "<< /Type 44 0 R >>");

        for (var number = 100; number < 180; number++)
        {
            file.Object(number, "<< >>");
        }

        file.CompressedCrossReferenceStream(8, 9, number => rows.TryGetValue(number, out var row) ? row : (0, 0, 0), "/Root 1 0 R /Prev 1 0 R");
        return (file.Bytes, candidate);
    }

    [Fact]
    public void A_search_for_a_catalog_that_takes_one_read_through_its_guard_s_cut_notes_it()
    {
        // The trailer's /Root names a string. Objects 40 to 45 lie in object streams 4, 5 and 6, of 17 MB each, in turn, and
        // object 50, a stream whose dictionary is a catalog's, takes its /Length from object 60, which stream 4 holds. The
        // search for a catalog decodes each stream again once, as much as it may, cutting nothing; reading 50, it reads 60 as
        // null, the guard's cut, and 50 up to its endstream, a catalog all the same: it takes it, and notes that it read it
        // through a limit's cut (#181).
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "(not a catalog)"), 0),
            [2] = (1, file.Object(2, Pages), 0),
        };
        (int Stream, int[] Members)[] streams = [(4, [40, 43, 60]), (5, [41, 44]), (6, [42, 45])];

        foreach (var (stream, members) in streams)
        {
            rows[stream] = (1, file.PaddedObjectStream(stream, [.. members.Select(number => (number, number == 60 ? "3" : "<< >>"))], ObjectStreamDecoded), 0);

            for (var index = 0; index < members.Length; index++)
            {
                rows[members[index]] = (2, stream, index);
            }
        }

        rows[50] = (1, file.Object(50, "<< /Type /Catalog /Pages 2 0 R /Length 60 0 R >>\nstream\nabc\nendstream"), 0);
        file.CompressedCrossReferenceStream(61, 62, number => rows.TryGetValue(number, out var row) ? row : (0, 0, 0), "/Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 } });

        document.Reader.CatalogFoundAs.Should().Be(50);
        document.Reader.CatalogCandidateCut.Should().BeTrue();
    }

    [Fact]
    public void A_search_for_a_catalog_opened_to_throw_reads_again_the_candidates_its_guard_threw_for_and_rebuilds_nothing()
    {
        // The file of the search above, opened to throw: each candidate the search's guard leaves unread throws, and the
        // search notes it, reads them again stream by stream once its first reading ends, each stream decoded once more, and
        // takes catalog 58; it rebuilds nothing. Opening throws the guard's exception: streams 4 and 5 were each decoded three
        // times — twice by the first reading, once by the second (#181).
        var file = SearchCutFile("none");
        using var source = new OffsetCountingSource(file);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 }, ThrowOnLimit = true };
        var opening = () => PdfDocument.Open(source, options, ownsSource: false);

        opening.Should().Throw<PdfLimitExceededException>().Which.Code.Should().Be(PdfDiagnosticCodes.LimitObjectStreamDecodes);

        foreach (var header in (string[])["4 0 obj", "5 0 obj"])
        {
            var at = OffsetOf(file, header);
            var dataStart = at + file.AsSpan(at).IndexOf("stream\n"u8) + "stream\n".Length;
            source.Reads.GetValueOrDefault(dataStart).Should().Be(3, $"the search decodes stream {header[0]} twice, then once more");
        }
    }

    /// <summary>
    /// Writes the file of <see cref="A_search_for_a_catalog_reads_again_what_its_guard_cut_stream_by_stream_and_takes_the_first_catalog_in_its_order"/>
    /// for <paramref name="shape"/>.
    /// </summary>
    private static byte[] SearchCutFile(string shape)
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "(not a catalog)"), 0),
            [2] = (1, file.Object(2, Pages), 0),
        };
        var held = new List<(int Number, string Body)>[] { [], [], [] };

        for (var number = 40; number < 64; number++)
        {
            var body = number switch
            {
                58 when shape == "the catalog cut short" => "<< /Type /Catalog /Pages 2 0 R /Padding (" + new string('x', 2000) + ") >>",
                58 or 59 when shape == "no catalog, a candidate that rebuilds the index" => "<< >>",
                58 or 59 => Catalog,
                51 when shape == "a candidate read through a stream decoded once" => "<< /Type 65 0 R >>",
                49 when shape.EndsWith("a candidate that rebuilds the index", StringComparison.Ordinal) => "<< /Type 5000 0 R >>",
                _ => "<< >>",
            };
            held[(number - 40) % 3].Add((number, body));
        }

        held[0].Add((65, "/Thing"));

        for (var stream = 0; stream < 3; stream++)
        {
            rows[4 + stream] = (1, file.PaddedObjectStream(4 + stream, [.. held[stream]], ObjectStreamDecoded), 0);

            for (var index = 0; index < held[stream].Count; index++)
            {
                rows[held[stream][index].Number] = (2, 4 + stream, index);
            }
        }

        rows[64] = (1, file.Object(64, "<< /Type 47 0 R >>"), 0);
        var rebuilds = shape == "a candidate that rebuilds the index";
        var incomplete = shape.EndsWith("a candidate that rebuilds the index", StringComparison.Ordinal);

        if (rebuilds)
        {
            file.Object(70, Catalog);
        }

        var entries = incomplete ? "/Root 1 0 R /Prev 1 0 R" : "/Root 1 0 R";
        file.CompressedCrossReferenceStream(66, 67, number => rows.TryGetValue(number, out var row) ? row : (0, 0, 0), entries);
        return file.Bytes;
    }

    [Fact]
    public void A_search_for_a_catalog_reads_again_a_candidate_written_directly_after_those_of_every_object_stream()
    {
        // Objects 40 to 63 lie in object streams 4, 2147483646 — the last but one number an object may have — and 6, of 17 MB
        // each, in turn, as in the searches above stream 5 holds them, and the second stream holds objects 100 to 199 after
        // them; none is a catalog. Object 64, written directly, takes /Catalog from 47, in that stream, and 199, its last,
        // takes its /Type from object 5000, which the index, whose /Prev names no section, lacks. The search's first reading
        // leaves 64 unread, and the stream's objects after 45; its second reads them before 64, written directly, though 64
        // comes before most of them in its order: 199, read, rebuilds the index, and the search ends (#181).
        var file = SearchCutFileWithAStreamNumberedLast();
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 } };
        using var document = PdfDocument.Open(file, options);

        document.WasRepaired.Should().BeTrue();
    }

    /// <summary>
    /// Writes the file of <see cref="A_search_for_a_catalog_reads_again_a_candidate_written_directly_after_those_of_every_object_stream"/>:
    /// that of <see cref="SearchCutFile"/>, object stream 5 numbered 2147483646, placed by a subsection of its own of the
    /// cross-reference stream, whose <c>/Prev</c> names no section.
    /// </summary>
    private static byte[] SearchCutFileWithAStreamNumberedLast()
    {
        const int Last = int.MaxValue - 1;
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "(not a catalog)"), 0),
            [2] = (1, file.Object(2, Pages), 0),
        };
        var held = new List<(int Number, string Body)>[] { [], [], [] };

        for (var number = 40; number < 64; number++)
        {
            held[(number - 40) % 3].Add((number, number == 47 ? "/Catalog" : "<< >>"));
        }

        held[0].Add((65, "/Thing"));

        for (var number = 100; number < 200; number++)
        {
            held[1].Add((number, number == 199 ? "<< /Type 5000 0 R >>" : "<< >>"));
        }

        int[] streams = [4, Last, 6];

        for (var stream = 0; stream < 3; stream++)
        {
            rows[streams[stream]] = (1, file.PaddedObjectStream(streams[stream], [.. held[stream]], ObjectStreamDecoded), 0);

            for (var index = 0; index < held[stream].Count; index++)
            {
                rows[held[stream][index].Number] = (2, streams[stream], index);
            }
        }

        rows[64] = (1, file.Object(64, "<< /Type 47 0 R >>"), 0);
        rows[0] = (0, 0, 65535);
        rows[66] = (1, file.Position, 0);
        var data = new StringBuilder();

        foreach (var number in (ReadOnlySpan<int>)[.. Enumerable.Range(0, 200), Last])
        {
            var (type, field2, field3) = rows.TryGetValue(number, out var row) ? row : (0, 0L, 0);
            data.Append((char)type)
                .Append((char)((field2 >> 24) & 0xFF)).Append((char)((field2 >> 16) & 0xFF))
                .Append((char)((field2 >> 8) & 0xFF)).Append((char)(field2 & 0xFF))
                .Append((char)((field3 >> 8) & 0xFF)).Append((char)(field3 & 0xFF));
        }

        var xref = rows[66].Field2;
        file.Object(
            66,
            string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Type /XRef /Size {int.MaxValue} /Index [0 200 {Last} 1] /W [1 4 2] /Root 1 0 R /Prev 1 0 R /Length {data.Length} >>\nstream\n{data}\nendstream"));
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"startxref\n{xref}\n%%EOF\n"));
        return file.Bytes;
    }

    [Fact]
    public void A_walk_within_another_ends_without_ending_it_and_the_guard_names_the_walk_around_it()
    {
        // A validation reads pages 20 to 23, which lie in turn in object streams 10 and 11, of 17 MB each, decoding each again
        // once, as much as it may; a search for a catalog runs within it, as one a rebuild sets off would, and ends. The
        // validation goes on as it was: page 24 reads as null, the guard's cut, which its report says of the validation (#181).
        var file = HostileInputTests.AlternatingPages(8, 17 * 1024 * 1024, "<< /Type /Page /Parent 2 0 R >>");
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 } });
        PdfObject page;
        document.Reader.BeginWalk("validation");

        try
        {
            for (var number = 20; number < 24; number++)
            {
                document.GetObject(new PdfObjectId(number)).Should().BeOfType<PdfDictionary>();
            }

            document.Reader.BeginWalk("search for a catalog");
            document.Reader.EndWalk();
            page = document.GetObject(new PdfObjectId(24));
        }
        finally
        {
            document.Reader.EndWalk();
        }

        page.Should().Be(PdfNull.Instance);
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().StartWith("The validation decoded again the object streams");
    }

    [Fact]
    public void The_objects_a_walk_s_guard_cut_take_no_place_in_the_cache_while_the_walk_lasts()
    {
        // Eighty pages, objects 20 to 99, lie in turn in object streams 10 and 11, of 17 MB each, read through a cache of 64
        // objects. A walk reads them in order, decoding them again once at most: past page 23, each page of stream 11 reads as
        // null, the guard's cut, and the 46 objects it read whole fit in the cache. The nulls are not kept: page 20, read
        // again within the walk, is the one the cache kept (#181).
        var file = HostileInputTests.AlternatingPages(80, 17 * 1024 * 1024, "<< /Type /Page /Parent 2 0 R >>");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 }, ObjectCacheCapacity = 64 };
        using var document = PdfDocument.Open(file, options);
        var cut = new List<int>();
        PdfObject again;
        document.Reader.BeginWalk("validation");

        try
        {
            for (var number = 20; number < 100; number++)
            {
                if (document.GetObject(new PdfObjectId(number)) is PdfNull)
                {
                    cut.Add(number);
                }
            }

            again = document.GetObject(new PdfObjectId(20));
        }
        finally
        {
            document.Reader.EndWalk();
        }

        cut.Should().Equal(Enumerable.Range(12, 38).Select(k => 2 * k));
        again.AsDictionary().Required().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Fact]
    public void A_walk_that_read_more_objects_through_its_guard_s_cut_than_the_cache_holds_lets_go_of_each_and_of_its_place_as_it_ends()
    {
        // A walk reads objects 10 to 33 in turn, decoding their object streams again once at most, and object 28 reads as
        // null, the guard's cut. Seventy streams, objects 300 to 369, take their /Length from it: each is read up to the first
        // endstream its data spells, and kept for the rest of the walk, past a cache of 64 objects, which they fill. The walk
        // lets go of each as it ends, and of its place in the order the cache lets go in: the cache holds nothing. Read whole
        // after it, they fill the cache again, and a walk that reads nothing lets go of none of them (#181).
        var file = StreamsThroughALengthCutFile(70);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 }, ObjectCacheCapacity = 64 };
        using var document = PdfDocument.Open(file, options);
        document.Reader.BeginWalk("validation");

        try
        {
            for (var number = 10; number < 34; number++)
            {
                _ = document.GetObject(new PdfObjectId(number));
            }

            for (var number = 300; number < 370; number++)
            {
                Encoding.Latin1.GetString(document.GetObject(new PdfObjectId(number)).AsStream().Required().Decode().Span).Should().Be("01");
            }
        }
        finally
        {
            document.Reader.EndWalk();
        }

        document.Reader.CacheCounts.Should().Be((0, 0));

        for (var number = 300; number < 370; number++)
        {
            Encoding.Latin1.GetString(document.GetObject(new PdfObjectId(number)).AsStream().Required().Decode().Span).Should().Be("01\nendstream\nendobj\n89");
        }

        document.Reader.BeginWalk("validation");
        document.Reader.EndWalk();
        document.Reader.CacheCounts.Should().Be((64, 64));
    }

    /// <summary>Reads object <paramref name="number"/>, and gives the exception of the guard it reached, or null.</summary>
    private static PdfLimitExceededException? ReadPastGuard(PdfDocument document, int number)
    {
        try
        {
            _ = document.GetObject(new PdfObjectId(number));
            return null;
        }
        catch (PdfLimitExceededException exception)
        {
            return exception;
        }
    }

    [Theory]
    [InlineData("decoded stream", "The /ASCIIHexDecode data decodes to more than 1 byte; decoding stopped there.")]
    [InlineData("stream length", "A stream's data runs past 1 byte; only what lies within it was read.")]
    [InlineData("object", "Object 1 runs past 1 byte, its stream data aside; only what lies within it was read.")]
    [InlineData("section length", "The cross-reference table runs past 1 byte; only the entries within it were read.")]
    [InlineData("section count", "The cross-reference chain has more than 1 section; the older ones were not read.")]
    [InlineData("cross-reference stream", "The dictionary of the object read as a cross-reference stream runs past 1 byte; only what lies within it was read.")]
    [InlineData("filter chain", "The /Filter entry names more than 1 filter; decoding stopped after the first.")]
    [InlineData("rebuild reads", "Past streams' direct lengths, the scan for objects read the length of the file; the data of each stream it could not confirm after that was read up to its first endstream, the object headers there not taken.")]
    [InlineData("object-stream decodes", "The search for a catalog decoded again the object streams the reader let go as much as what its first decodings of them weighed, 35651712 bytes: object stream 4 was not decoded again, and the objects it holds read as null until the search for a catalog ends.")]
    public void A_guard_set_to_one_says_its_bound_in_the_singular(string guard, string reported)
    {
        // Each guard at one, the least it takes, reached on the file of its own case: the bound it names is said in the
        // singular (#241). Object 3, a stream in the cases of the guards on streams, is decoded.
        var @case = Case.For(guard);
        using var document = PdfDocument.Open(@case.File, new PdfReaderOptions { Limits = With(@case.Reached, @case.LimitName, 1) });

        if (document.GetObject(new PdfObjectId(3)) is PdfStream stream)
        {
            _ = stream.Decode(document.Diagnostics);
        }

        document.Diagnostics.Where(entry => entry.Code == @case.Code).Select(entry => entry.Message)
            .Should().Contain($"{reported} Raise PdfReaderLimits.{@case.LimitName} to read past it.");
    }

    [Theory]
    [MemberData(nameof(Guards))]
    public void Raising_a_guard_reads_the_document_whole(string guard)
    {
        var @case = Case.For(guard);
        using var document = PdfDocument.Open(@case.File, new PdfReaderOptions { Limits = @case.Raised });

        @case.Read(document).Should().Be(@case.Whole);
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(
            @case.WholeReports, $"a sound file read within its guards reports nothing but the damage that sets off a rebuild ({guard})");
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
    public void A_stream_cut_at_MaxStreamLength_decodes_what_lies_within_it_as_data_the_reader_cut_and_is_reported_once()
    {
        // A hundred kilobytes of Flate data, which a bound of 10 KB cuts: the data is read up to the bound however often it
        // is asked for, its length stays the file's, and what the Flate data lacks past the cut is the guard's, not damage
        // in the file (#170).
        var compressed = HostileInputTests.Compress(RandomBytes(100_000));
        var written = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = written.Object(1, Catalog),
            [2] = written.Object(2, Pages),
            [3] = written.Object(3, string.Create(CultureInfo.InvariantCulture, $"<< /Filter /FlateDecode /Length {compressed.Length} >>\nstream\n{Encoding.Latin1.GetString(compressed)}\nendstream")),
        };
        written.Table(4, rows, "/Size 4 /Root 1 0 R");
        var file = written.Bytes;
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxStreamLength = 10 * 1024 } };
        using var document = PdfDocument.Open(file, options);
        var stream = document.GetObject(new PdfObjectId(3)).AsStream().Required();

        stream.GetRawBytes().Length.Should().Be(10 * 1024);
        stream.GetRawBytes().ToArray().Should().Equal(compressed.AsSpan(0, 10 * 1024).ToArray());
        stream.Data.Length.Should().Be(compressed.Length, "the length is the file's");
        var decoded = stream.Decode(document.Diagnostics);

        decoded.Length.Should().BeGreaterThan(0).And.BeLessThan(100_000);
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.LimitStreamLength);
        report.Message.Should().Be("A stream's data runs past 10 KB; only what lies within it was read. Raise PdfReaderLimits.MaxStreamLength to read past it.");
        report.Position.Should().Be(stream.Data.Position);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/Filter 5")]
    [InlineData("/Filter []")]
    [InlineData("/Filter [null]")]
    [InlineData("/Filter /DCTDecode")]
    [InlineData("/Filter /JPXDecode")]
    [InlineData("/Filter /JBIG2Decode")]
    [InlineData("/Filter /CCITTFaxDecode")]
    [InlineData("/Filter /FlateDecode")]
    [InlineData("/Filter /Crypt")]
    [InlineData("/Filter /Foo")]
    [InlineData("/Filter [/Foo /FlateDecode]")]
    public void Data_decoding_hands_back_as_it_is_is_bounded_by_MaxStreamLength_and_not_by_MaxDecodedStreamLength(string filter)
    {
        // Fifty thousand bytes that no filter decodes — none named, an image filter, one the library does not know, Flate
        // over data that is none —: decoding hands them back as they are, whole under a decoded bound of 1 KB, unreported.
        // Their bound is the one on what is read, here 10 KB, which a large uncompressed image raises alone (#170).
        var file = new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, Pages)
            .Stream(3, filter, new string('4', 50_000))
            .BuildClassic(rootNumber: 1);
        var limits = PdfReaderLimits.Default with { MaxStreamLength = 10 * 1024, MaxDecodedStreamLength = 1024 };
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });
        var stream = document.GetObject(new PdfObjectId(3)).AsStream().Required();

        var decoded = stream.Decode(document.Diagnostics);

        decoded.Length.Should().Be(10 * 1024);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.LimitStreamLength);
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.LimitDecodedStream);
    }

    [Theory]
    [InlineData(2_147_483_600L)]
    [InlineData(2_147_483_647L)]
    public void A_length_past_what_an_array_holds_ends_at_the_guard_and_never_in_an_exhausted_memory(long declared)
    {
        // A file of about 2 GB whose stream declares a length between the most an array holds and the most an int does,
        // which the file holds: reading its data threw an OutOfMemoryException each time, and reported nothing (#170). Under
        // a bound of 1 MB, a megabyte is read and the guard reported; under no bound the guard's ceiling is the array's.
        using var source = SparseSource.Huge(declared);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxStreamLength = 1024 * 1024 } };
        using var document = PdfDocument.Open(source, options, ownsSource: false);
        var stream = document.GetObject(new PdfObjectId(3)).AsStream().Required();

        stream.RawLength.Should().Be((int)declared);
        stream.GetRawBytes().Length.Should().Be(1024 * 1024);
        stream.Decode(document.Diagnostics).Length.Should().Be(1024 * 1024);
        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.LimitStreamLength);

        using var throwing = PdfDocument.Open(source, options with { ThrowOnLimit = true }, ownsSource: false);
        var reading = () => throwing.GetObject(new PdfObjectId(3)).AsStream().Required().GetRawBytes();
        reading.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxStreamLength));
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
    public void Opening_a_file_with_no_index_throws_the_guard_its_catalog_reaches_once_the_rebuilt_index_holds_it()
    {
        // The file has no index, and opening it rebuilds one. Object 1, the catalog the trailer names, is the one member of
        // object stream 10, and runs past the object bound; 13 is object stream 12's. The rebuild reads the file's /Root
        // through the index it rebuilt, the only step that reaches a guard here: the guard is held, as one any step of a
        // rebuild reaches is, and thrown once the index is whole, the object streams expanded — the message names the byte
        // of 10's decoded data the catalog starts at. Read without throwing, the same file opens, the guard reported there
        // and 13 found.
        var catalog = "<< /Type /Catalog /Pages 2 0 R /Pad (" + new string('x', 5000) + ") >>";
        var file = new TestPdfBuilder()
            .WithObject(2, Pages)
            .Stream(10, "/Type /ObjStm /N 1 /First 4", "1 0 " + catalog)
            .Stream(12, "/Type /ObjStm /N 1 /First 5", "13 0 (found)")
            .BuildClassic(rootNumber: 1, includeXRef: false);
        var limits = PdfReaderLimits.Default with { MaxObjectLength = 4096 };

        var opening = () => PdfDocument.Open(file, new PdfReaderOptions { Limits = limits, ThrowOnLimit = true });

        var thrown = opening.Should().Throw<PdfLimitExceededException>().Which;
        thrown.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));
        thrown.Message.Should().StartWith("Object 1, at byte 4 of object stream 10's decoded data, runs past 4 KB;");
        using var tolerant = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });
        tolerant.WasRepaired.Should().BeTrue();
        tolerant.GetObject(new PdfObjectId(13)).AsText().Should().Be("found");
        tolerant.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitObject)
            .Which.Message.Should().StartWith("Object 1, at byte 4 of object stream 10's decoded data, runs past 4 KB;");
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

    [Theory]
    [InlineData("white space")]
    [InlineData("white space and comment lines")]
    public void A_valid_file_a_guard_sends_into_a_rebuild_serves_no_object_a_stream_s_data_spells_after_lengths_taken_from_attached_data_that_end_in_one_run_of_white_space(string run)
    {
        // Eight attached streams, each with an indirect /Length whose object follows it, hold first in their data an object
        // of that length's number, an attached file's, whose integer ends the data in one run of 64 KiB between objects. A
        // stream after the run, with a direct /Length and 70 bytes of white space before its endstream, holds data that spells
        // object 77, which the file does not define. MaxXRefSectionLength cuts the table, and the rebuild's scan takes each
        // attached stream's length from the object its data holds: past that length it reads the first bytes alone, which do
        // not tell, and leaves the data as it is. Read on through the run for each stream, its reads past declared lengths
        // would have reached their bound before the last stream, whose endstream it would no longer have seen past its white
        // space, and object 77 would have been taken from that stream's data.
        const int Streams = 8;
        var filler = run == "white space"
            ? new string(' ', 64 * 1024)
            : " \n" + string.Concat(Enumerable.Repeat("% " + new string('-', 61) + "\n", 1024));
        var (_, dataStarts, runStart) = Build(null, 0);
        var (file, _, _) = Build(dataStarts, runStart);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 16 } };

        using var document = PdfDocument.Open(file, options);

        document.GetObject(new PdfObjectId(77)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitXRefSectionLength);

        (byte[] File, long[] DataStarts, long RunStart) Build(long[]? starts, long runAt)
        {
            var pdf = new HandWrittenFile();
            var rows = new Dictionary<int, long> { [1] = pdf.Object(1, Catalog), [2] = pdf.Object(2, Pages) };
            var written = new long[Streams];

            for (var i = 0; i < Streams; i++)
            {
                var (number, length) = (10 + (2 * i), 11 + (2 * i));
                var pointsTo = starts is null ? 0 : runAt - starts[i];
                var data = string.Create(CultureInfo.InvariantCulture, $"{length} 0 obj\n{pointsTo:D10}\nendobj\nattached bytes\n");
                rows[number] = pdf.Position;
                pdf.Raw(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< /Type /EmbeddedFile /Length {length} 0 R >>\nstream\n"));
                written[i] = pdf.Position;
                pdf.Raw(data + "endstream\nendobj\n");
                rows[length] = pdf.Object(length, data.Length.ToString(CultureInfo.InvariantCulture));
            }

            var at = pdf.Position;
            pdf.Raw(filler + "\n");
            rows[30] = pdf.Position;
            pdf.Raw("30 0 obj\n<< /Length 24 >>\nstream\n77 0 obj\n(inner)\nendobj\n" + new string(' ', 70) + "\nendstream\nendobj\n");
            pdf.Table(31, rows, "/Size 31 /Root 1 0 R");
            return (pdf.Bytes, written, at);
        }
    }

    [Theory]
    [InlineData("whose lengths end in one run of white space", 8, true)]
    [InlineData("of no length, each before a run of white space of its own", 16, false)]
    [InlineData("that lost their endstream, glued to a sign", 32, false)]
    [InlineData("that lost their endstream, glued to an end-of-file marker", 32, false)]
    [InlineData("that lost their endstream, before a mebibyte of text", 5, false)]
    public void A_valid_file_a_guard_sends_into_a_rebuild_serves_no_object_a_stream_s_data_spells_after_streams_an_attached_file_s_bytes_spell(
        string spelled, int count, bool reached)
    {
        // Object 10, an attached file with an indirect /Length whose object holds a note before its length, spells streams
        // in its data: their direct lengths end where one run of a mebibyte of spaces starts; or they have no length, and
        // 64 KiB of spaces follow each before an endobj;
        // or a sign or an end-of-file marker is glued to their headers, and no endstream follows their data, which may
        // run on through a mebibyte of text. Object 30, after it, has a direct length 70 spaces before its endstream, which
        // a first read cannot see past, and its data spells object 77, which the file does not define. MaxXRefSectionLength
        // cuts the table, and the rebuild's scan reads object 10's data as objects: the reads past the spelled streams'
        // lengths, and, for the memo's kinds, the searches for where a load ends their data, spent the reads that confirm
        // lengths, and the scan, which could not confirm object 30, took object 77 from its data (#361). A rebuild's scan
        // makes none of the memo's reads, and only the reads that confirm the spelled streams' lengths count against
        // MaxRebuildReadPasses: one run of white space all their lengths end in, which each read crosses, reaches it, and the
        // scan reads object 30's data, which it can no longer confirm, up to its first endstream, nothing taken there; a run
        // of its own after each, which only that stream's reads cross, does not, nor do streams that lost their endstream,
        // and the scan confirms object 30.
        var data = new StringBuilder();
        const int Each = 42;

        for (var i = 0; i < count; i++)
        {
            var number = 20 + i;
            data.Append(spelled switch
            {
                "whose lengths end in one run of white space" => string.Create(
                    CultureInfo.InvariantCulture, $"{number} 0 obj\n<< /Length {(Each * count) - ((Each * i) + 38):D7} >>\nstream\nabc\n"),
                "of no length, each before a run of white space of its own" => string.Create(
                    CultureInfo.InvariantCulture, $"{number} 0 obj\n<< /Length 0 >>\nstream\n{new string(' ', 64 * 1024)}endobj\n"),
                "that lost their endstream, glued to an end-of-file marker" => string.Create(
                    CultureInfo.InvariantCulture, $"%%EOF{number} 0 obj\n<< /Length 5 >>\nstream\nabcdefgh\n"),
                _ => string.Create(CultureInfo.InvariantCulture, $"+{number} 0 obj\n<< /Length 5 >>\nstream\nabcdefgh\n"),
            });
        }

        data.Append(spelled switch
        {
            "whose lengths end in one run of white space" => new string(' ', 1024 * 1024) + "attached tail\n",
            "that lost their endstream, before a mebibyte of text" => new string('t', 1024 * 1024) + "\n",
            _ => "\n",
        });
        var pdf = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = pdf.Object(1, Catalog), [2] = pdf.Object(2, Pages) };
        rows[10] = pdf.Position;
        pdf.Raw("10 0 obj\n<< /Type /EmbeddedFile /Length 11 0 R >>\nstream\n" + data + "endstream\nendobj\n");
        rows[11] = NotedLength(pdf, data.Length);
        rows[30] = pdf.Position;
        pdf.Raw("30 0 obj\n<< /Length 24 >>\nstream\n77 0 obj\n(inner)\nendobj\n" + new string(' ', 70) + "\nendstream\nendobj\n");
        pdf.Table(31, rows, "/Size 31 /Root 1 0 R");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 16 } };

        using var document = PdfDocument.Open(pdf.Bytes, options);

        document.GetObject(new PdfObjectId(77)).Should().BeSameAs(PdfNull.Instance);
        document.GetObject(new PdfObjectId(30)).AsStream().Required().GetRawBytes().Length.Should().Be(24);
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitXRefSectionLength);
        document.Diagnostics.Contains(PdfDiagnosticCodes.LimitRebuildReads).Should().Be(reached);
    }

    [Fact]
    public void A_rebuild_after_the_scan_s_memo_reached_the_read_guard_scans_again_and_reports_it()
    {
        // The guard's case, its objects indexed by a table whose /Prev names no section, so that a lookup of object 31,
        // which the table does not number, rebuilds the index. The scan's memo, made first, reaches MaxRebuildReadPasses
        // and reports nothing; the rebuild does not take its headers from it, scans the file again, and reports the guard
        // where its own scan reaches it.
        const string Head = "20 0 obj\n<< /Length 0000000 >>\nstream\nabc\n";
        var spelled = new StringBuilder();

        for (var i = 0; i < 6; i++)
        {
            var length = ((6 - i) * Head.Length) - (Head.Length - "abc\n".Length);
            spelled.Append(Head.Replace("20 0", string.Create(CultureInfo.InvariantCulture, $"{20 + i} 0"), StringComparison.Ordinal)
                .Replace("0000000", length.ToString("D7", CultureInfo.InvariantCulture), StringComparison.Ordinal));
        }

        var data = spelled.Append(' ', 64 * 1024).Append("attached tail\n").ToString();
        const string Inner = "%PDF-1.4\n5 0 obj\n<< /Length 3 >>\nstream\nabc\nendstream\nendobj\n% the attached file's last line";
        var pdf = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = pdf.Object(1, Catalog), [2] = pdf.Object(2, Pages) };
        rows[10] = pdf.Position;
        pdf.Raw("10 0 obj\n<< /Type /EmbeddedFile /Length 11 0 R >>\nstream\n" + data + "endstream\nendobj\n");
        rows[11] = NotedLength(pdf, data.Length);
        rows[30] = pdf.Position;
        pdf.Raw(string.Create(CultureInfo.InvariantCulture, $"30 0 obj\n<< /Type /EmbeddedFile /Length {Inner.Length} >>\nstream\n{Inner}") +
            new string(' ', 70) + "endstream endobj 31 0 obj (real) endobj\n");
        pdf.Table(31, rows, "/Size 31 /Root 1 0 R /Prev 1 0 R");
        var file = pdf.Bytes;
        using var document = PdfDocument.Open(file);

        var memo = document.Reader.GetScanMemo();
        var reportedBefore = document.Diagnostics.Contains(PdfDiagnosticCodes.LimitRebuildReads);
        var real = document.GetObject(new PdfObjectId(31));

        (memo.ReadsBounded, reportedBefore).Should().Be((true, false));
        real.Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.LimitRebuildReads)
            .Which.Position.Should().Be(OffsetOf(file, "24 0 obj") + Head.Length - "abc\n".Length);
    }

    [Theory]
    [InlineData("a string that quotes a header and its endobj", "a guard", 0)]
    [InlineData("a string that quotes a header and its endobj", "damage", 0)]
    [InlineData("a comment that holds obj", "a guard", 0)]
    [InlineData("a string that quotes a header past MaxObjectLength", "a guard", 16_384)]
    [InlineData("a string that quotes a header past MaxObjectLength", "damage", 16_384)]
    [InlineData("a stream's dictionary whose string quotes a header and its endobj", "a guard", 0)]
    [InlineData("a string that quotes a header, past an attached file cut inside a string", "a guard", 0)]
    [InlineData("a string that quotes a header, past an attached file cut inside a string", "damage", 0)]
    public void A_valid_object_a_rebuild_s_scan_cannot_delimit_is_read_whole_whatever_headers_it_holds(
        string holding, string rebuiltBy, int objectGuard)
    {
        // Object 5, a JavaScript action, or a stream whose dictionary holds a note, holds the header of an object 999 the file
        // does not define at the start of a line of its string, more than the window a load starts with into it: the string
        // quotes that object's endobj too, or a comment in the dictionary holds the word obj, or the string runs past
        // MaxObjectLength, or an attached PDF cut inside a string comes before it. The rebuild's scan cannot delimit object 5
        // and takes the header it holds, which the rebuilt index alone places. MaxXRefSectionLength cuts the table, or
        // startxref names no section: either way the index is rebuilt. A header inside a value is not past its end, and
        // object 5 is read as without the rebuild, whole, or as far as its guard, which is reported (#362).
        var file = new HandWrittenFile();
        var attached = holding.EndsWith("cut inside a string", StringComparison.Ordinal);
        var stream = holding.StartsWith("a stream's", StringComparison.Ordinal);
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /OpenAction 5 0 R " + (attached ? "/Names << /EmbeddedFiles << /Names [(a.pdf) 3 0 R] >> >> " : "") + ">>"),
            [2] = file.Object(2, Pages),
        };

        if (attached)
        {
            const string Data = "%PDF-1.4\n901 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendstream\nendobj\n900 0 obj\n<< /Title (cut here";
            rows[4] = file.Object(4, "<< /Type /EmbeddedFile /Length 7 0 R >>\nstream\n" + Data + "\nendstream");
            rows[7] = file.Object(7, Data.Length.ToString(CultureInfo.InvariantCulture));
            rows[3] = file.Object(3, "<< /Type /Filespec /F (a.pdf) /EF << /F 4 0 R >> >>");
        }

        var after = holding.EndsWith("MaxObjectLength", StringComparison.Ordinal) ? 30_000 : 10_000;
        var script = new string('x', 12_000) + "\n999 0 obj\n<< /Quoted true >>\n" + (holding.Contains("endobj", StringComparison.Ordinal) ? "endobj\n" : "")
            + new string('y', after);
        var comment = holding == "a comment that holds obj" ? "% the quoted obj below is text\n" : string.Empty;
        var key = stream ? "Note" : "JS";
        rows[5] = file.Object(5, stream
            ? "<< /Length 3 /Note (" + script + ") >>\nstream\nabc\nendstream"
            : "<< " + comment + "/S /JavaScript /JS (" + script + ") >>");
        file.Table(8, rows, "/Size 8 /Root 1 0 R");
        var bytes = rebuiltBy == "damage"
            ? Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(file.Bytes).Replace("startxref\n", "startxref\n1", StringComparison.Ordinal))
            : file.Bytes;
        var limits = rebuiltBy == "a guard" ? PdfReaderLimits.Default with { MaxXRefSectionLength = 16 } : PdfReaderLimits.Default;
        using var document = PdfDocument.Open(
            bytes, new PdfReaderOptions { Limits = objectGuard > 0 ? limits with { MaxObjectLength = objectGuard } : limits });

        var five = document.GetObject(new PdfObjectId(5));
        var read = (five is PdfStream { Dictionary: var dictionary } ? dictionary : five.AsDictionary().Required()).GetRaw(PdfName.Get(key));
        var codes = document.Diagnostics.Select(entry => entry.Code).ToList();

        document.WasRepaired.Should().BeTrue();
        (five is PdfStream).Should().Be(stream);
        document.Reader.IsCutAtLimit(5).Should().Be(objectGuard > 0);
        read.Should().BeOfType<PdfString>().Which.Length.Should().BeGreaterThan(12_000 + 40, "the string is read past the header it quotes");
        (objectGuard > 0 || ((PdfString)read).Length == script.Length).Should().BeTrue("a string within the guard is read whole");
        document.Diagnostics.Should().NotContain(
            entry => entry.Code == PdfDiagnosticCodes.SyntaxTruncatedObject && entry.Position >= rows[5], "the attached file's string is cut, not object 5's");
        codes.Contains(PdfDiagnosticCodes.LimitObject).Should().Be(objectGuard > 0);
    }

    [Theory]
    [InlineData("headers", 31, 4, "24 0 obj", "Unindexed")]
    [InlineData("headers", 99, 4, "24 0 obj", "Unindexed")]
    [InlineData("headers", 31, 64, null, "Defined")]
    [InlineData("headers", 99, 64, null, "Missing")]
    [InlineData("headers past the first stream", 99, 4, "30 0 obj", "Unindexed")]
    [InlineData("no header", 31, 4, null, "Defined")]
    [InlineData("no header", 99, 4, null, "Missing")]
    [InlineData("(an obj word)", 99, 4, null, "Missing")]
    [InlineData("% an obj\nword", 99, 4, null, "Missing")]
    [InlineData("(ab 77 0 obj)", 99, 4, "24 0 obj", "Unindexed")]
    public void An_object_a_rebuild_s_scan_left_out_of_the_index_at_the_read_guard_is_unknown_where_the_data_it_left_unread_may_hold_it(
        string unread, int named, int passes, string? stoppedAt, string presence)
    {
        // The guard's case, its catalog naming object 31, or object 99, which the file does not define, under /Lang. Once the
        // rebuild's scan has read past the spelled streams' lengths as much as MaxRebuildReadPasses lets it, it reads the data
        // of the streams it cannot confirm up to their first endstream: spelled stream 24's, which holds stream 25's header,
        // then object 30's, which holds an attached file's, and the comment after its inner endstream hides object 31.
        // Whether the file holds an object the rebuilt index lacks is then unknown, as where any guard left it out of the
        // index, and the validator says the index was checked in part from the first data left unread that may hold a header,
        // rather than judge the reference missing. Where stream 25's header is spoiled, that is object 30's data; where object
        // 30 is not written either, no data left unread may hold a header: the guard is reported all the same, and object 99
        // is missing — and so it is where the last line of stream 24's data, as long as before, holds the word obj in a
        // string or a comment, which no header precedes, but not where it holds a header in a string, which the scan would
        // take. Raised, the guard reads object 31, and object 99 is missing.
        var text = Encoding.Latin1.GetString(Case.For("rebuild reads").File).Replace(
            Catalog, string.Create(CultureInfo.InvariantCulture, $"<< /Type /Catalog /Pages 2 0 R /Lang {named} 0 R >>"), StringComparison.Ordinal);

        if (unread != "headers")
        {
            text = text.Replace("25 0 obj", "25 0 ob_", StringComparison.Ordinal);
        }

        if (unread is not ("headers" or "headers past the first stream"))
        {
            if (unread != "no header")
            {
                text = text.Replace("attached tail", unread, StringComparison.Ordinal);
            }

            var thirty = text.IndexOf("30 0 obj", StringComparison.Ordinal);
            var real = text.IndexOf("31 0 obj", StringComparison.Ordinal);
            text = text[..thirty] + new string(' ', real - thirty) + text[real..];
        }

        var file = Encoding.Latin1.GetBytes(text);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxRebuildReadPasses = passes } });

        var findings = new PdfValidator().Validate(document).Findings;
        var reports = document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.LimitRebuildReads);
        long?[] stopped = stoppedAt is null ? [] : [text.IndexOf("stream\n", text.IndexOf(stoppedAt, StringComparison.Ordinal), StringComparison.Ordinal) + "stream\n".Length];

        document.WasRepaired.Should().BeTrue();
        document.Reader.GetPresence(named).Should().Be(Enum.Parse<ObjectPresence>(presence));
        reports.Select(entry => entry.Position).Should().Equal(passes == 4 ? [OffsetOf(file, "24 0 obj") + 38] : []);
        findings.Where(finding => finding.RuleId == PdfValidationRuleIds.XRefCheckedInPart).Select(finding => finding.Location.Position)
            .Should().Equal(stopped);
        findings.Any(finding => finding.RuleId == PdfValidationRuleIds.ObjectReferenceMissing).Should().Be(presence == "Missing");
    }

    [Theory(Timeout = 60_000)]
    [InlineData("a header whose obj ends the first mebibyte of the data", "Unindexed")]
    [InlineData("a header past the first mebibyte of the data", "Unindexed")]
    [InlineData("no header in more than a mebibyte of data", "Missing")]
    [InlineData("a header that ends the file, no endstream after it", "Unindexed")]
    [InlineData("no data, the stream keyword ending the file", "Missing")]
    [InlineData("obj at the start of the data, a header after it", "Unindexed")]
    [InlineData("a word whose obj ends the first mebibyte of the data, a letter after it", "Missing")]
    [InlineData("a word that ends in obj at the end of the first mebibyte of the data", "Missing")]
    [InlineData("a word that holds obj five bytes short of the end of the first mebibyte of the data", "Missing")]
    [InlineData("a word that ends in obj glued to an endstream that ends the first mebibyte of the data", "Missing")]
    [InlineData("a header past a first read the source gives short", "Missing")]
    public async Task Data_a_rebuild_s_scan_reads_as_intact_past_the_read_guard_is_searched_for_a_header_up_to_its_endstream_or_the_end_of_the_file(
        string data, string presence)
    {
        // A file with no cross-reference table, its catalog naming object 99, which it does not define, under /Lang. Twenty-four
        // streams have direct lengths that all end where 256 KiB of spaces start, each object's data ending at its own
        // endstream: the scan reads through the spaces for each to confirm it, and reaches MaxRebuildReadPasses, after which
        // it reads the data of the streams it cannot confirm, theirs and then stream 40's, which has no length, up to their
        // first endstream, nothing taken there. Stream 40's data is searched for a header the scan would have taken, a
        // mebibyte at a time, each window repeating the obj keyword the one before ended with and the byte before it, up to
        // its endstream or the end of the file: where it holds one, whether the file holds object 99 is unknown; where it holds
        // none, or no data at all, object 99 is missing. An obj keyword counts where a header precedes it, the byte before it
        // ending a token, and no regular character follows it, whichever window holds it, and only up to the endstream; a
        // read the source gives short of what was asked ends the search, as the end of the data.
        const int Spelled = 24;
        const int Mebibyte = 1024 * 1024;
        const string Head = "20 0 obj\n<< /Length 0000000 >>\nstream\nabc\nendstream\nendobj\n";
        const string Ends = "\nendstream\nendobj\n";
        var text = new StringBuilder("%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R /Lang 99 0 R >>\nendobj\n2 0 obj\n" + Pages + "\nendobj\n")
            .Append("trailer\n<< /Root 1 0 R >>\n");
        var run = text.Length + (Spelled * Head.Length);

        for (var i = 0; i < Spelled; i++)
        {
            var length = run - (text.Length + Head.IndexOf("abc", StringComparison.Ordinal));
            text.Append(Head.Replace("20 0", string.Create(CultureInfo.InvariantCulture, $"{20 + i} 0"), StringComparison.Ordinal)
                .Replace("0000000", length.ToString("D7", CultureInfo.InvariantCulture), StringComparison.Ordinal));
        }

        text.Append(' ', 256 * 1024).Append("x\n40 0 obj\n<< >>\nstream\n");
        var dataStart = text.Length;
        text.Append(data switch
        {
            "a header whose obj ends the first mebibyte of the data" =>
                new string('t', Mebibyte - "\n77 0 obj".Length) + "\n77 0 obj\n" + new string('t', 1000) + Ends,
            "a header past the first mebibyte of the data" => new string('t', Mebibyte + 1000) + "\n77 0 obj\n" + Ends,
            "no header in more than a mebibyte of data" => new string('t', Mebibyte + 1000) + Ends,
            "a header that ends the file, no endstream after it" => "abc\n77 0 obj",
            "obj at the start of the data, a header after it" => "obj\n77 0 obj\n" + Ends,
            "a word whose obj ends the first mebibyte of the data, a letter after it" =>
                new string('t', Mebibyte - "\n77 0 obj".Length) + "\n77 0 objx\n" + new string('t', 1000) + Ends,
            "a word that ends in obj at the end of the first mebibyte of the data" =>
                new string('t', Mebibyte - "obj".Length) + "obj\n" + new string('t', 1000) + Ends,
            "a word that holds obj five bytes short of the end of the first mebibyte of the data" =>
                new string('t', Mebibyte - 5) + "obj" + new string('t', 1000) + Ends,
            "a word that ends in obj glued to an endstream that ends the first mebibyte of the data" =>
                new string('t', Mebibyte - 4) + "obj" + Ends[1..],
            "a header past a first read the source gives short" => new string('t', 5000) + "\n77 0 obj\n" + Ends,
            _ => string.Empty,
        });

        var shortRead = data == "a header past a first read the source gives short";
        using var source = new ShortCountSource(
            Encoding.Latin1.GetBytes(text.ToString()), (offset, held) => shortRead && offset == dataStart ? Math.Min(held, 1000) : held);
        using var document = await Task.Run(
            () => PdfDocument.Open(source, options: null, ownsSource: false), TestContext.Current.CancellationToken);

        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Contains(PdfDiagnosticCodes.LimitRebuildReads).Should().BeTrue();
        document.Reader.RebuildReadsBoundedAt.Should().Be(presence == "Unindexed" ? dataStart : -1);
        document.Reader.GetPresence(99).Should().Be(Enum.Parse<ObjectPresence>(presence));
    }

    [Theory]
    [InlineData(4, "Unindexed", null)]
    [InlineData(64, "Defined", PdfValidationRuleIds.XRefEntryShifted)]
    public void An_object_the_read_guard_left_out_of_a_rebuilt_index_is_unknown_though_the_chain_s_index_places_it(
        int passes, string presence, string? judged)
    {
        // The guard's case under a table whose rows each lie three bytes past the header they place, object 31's included.
        // The search near object 31's row finds no header there, the scan it takes headers from hiding it in the comment after
        // object 30's inner endstream once past the guard, and the rebuild that sets off hides it too. The chain's index
        // places object 31, and the rebuilt index lacks it for the guard's reasons: whether the file holds it is unknown, not
        // a reading the reader gave up on, and its row is not judged. Raised, the guard reads object 31, three bytes before
        // its row.
        using var document = PdfDocument.Open(
            SpelledStreamsUnderShiftedRows(), new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxRebuildReadPasses = passes } });

        var findings = new PdfValidator().Validate(document).Findings;

        document.Reader.GetPresence(31).Should().Be(Enum.Parse<ObjectPresence>(presence));
        findings.Where(finding => finding.Location.Object?.Number == 31).Select(finding => finding.RuleId)
            .Should().Equal(judged is null ? [] : [judged]);
    }

    [Fact]
    public void A_memo_whose_scan_reaches_the_read_guard_leaves_the_chain_s_index_as_it_was()
    {
        // The guard's case under a table whose rows each lie three bytes past the header they place. Reading the catalog
        // searches near its row, from the scan's memo, whose scan reaches MaxRebuildReadPasses where the data of stream 24
        // holds stream 25's header; the catalog is found there, and no rebuild follows. The memo reports nothing, and the
        // chain's index is not one a guard stopped short: an object it does not number is missing (#361).
        using var document = PdfDocument.Open(SpelledStreamsUnderShiftedRows());

        var catalog = document.GetObject(new PdfObjectId(1));

        catalog.AsDictionary().Required().GetName(PdfName.Type).Should().Be(PdfName.Get("Catalog"));
        document.WasRepaired.Should().BeFalse();
        document.Reader.GetScanMemo().ReadsBounded.Should().BeTrue();
        document.Reader.RebuildReadsBoundedAt.Should().Be(-1);
        document.Reader.GetPresence(99).Should().Be(ObjectPresence.Missing);
        document.Diagnostics.Contains(PdfDiagnosticCodes.LimitRebuildReads).Should().BeFalse();
    }

    [Theory]
    [InlineData(31, true)]
    [InlineData(99, true)]
    [InlineData(31, false)]
    [InlineData(99, false)]
    public void An_object_a_rebuilt_index_lacks_past_the_read_guard_throws_each_time_a_document_opened_to_throw_reads_it(
        int number, bool firstStreamHoldsHeader)
    {
        // As above, reading object 31 sets the rebuild off, which reaches the guard and throws once the index is whole. Read
        // again, object 31, or object 99, which no index places, reads as null by the guard's decision, which a document
        // opened to throw on one is told each time it asks, as where the index filled, where the guard was reported: at the
        // first stream the guard left unread, whether its data holds a header, or stream 25's is spoiled and object 30's
        // data, further on, is the first to hold one.
        using var document = PdfDocument.Open(
            SpelledStreamsUnderShiftedRows(firstStreamHoldsHeader), new PdfReaderOptions { ThrowOnLimit = true });
        var first = () => document.GetObject(new PdfObjectId(31));
        var again = () => document.GetObject(new PdfObjectId(number));

        var thrown = first.Should().Throw<PdfLimitExceededException>().Which;

        thrown.LimitName.Should().Be(nameof(PdfReaderLimits.MaxRebuildReadPasses));
        again.Should().Throw<PdfLimitExceededException>().Which.Position.Should().Be(thrown.Position);
    }

    [Theory]
    [InlineData(64, false, true)]
    [InlineData(100, false, false)]
    [InlineData(192, true, true)]
    public void A_stream_whose_reads_to_confirm_its_length_start_at_the_read_guard_is_not_confirmed_and_one_whose_reads_cross_it_is(
        int shortOfTheBound, bool glued, bool reached)
    {
        // A file with no cross-reference table, read with MaxRebuildReadPasses at one: object 10, an attached file with an
        // indirect /Length whose object holds a note before its length, spells three streams whose direct lengths all end
        // where a thousand spaces start, then object 30,
        // whose direct length ends a hundred spaces before its endstream, glued or not to a run of letters, and the file is
        // padded so that the reads past the spelled streams' lengths stop the given count of bytes short of its length. Object
        // 30's reads past its length, which double from 64 bytes, then start: where the next of them would start at the bound,
        // the scan does not make it, and the stream is not confirmed, the guard reported there; where it starts short of the
        // bound, it finds the endstream and crosses the bound, which confirms the stream, the guard reported nowhere; and
        // where they end at the bound, the letters glued to the keyword are not read, and the stream is not confirmed (#361).
        const string Head = "20 0 obj\n<< /Length 0000000 >>\nstream\nabc\n";
        const int Spelled = 3;
        const int Run = 1000;
        var data = new StringBuilder();

        for (var i = 0; i < Spelled; i++)
        {
            var length = ((Spelled - i) * Head.Length) - (Head.Length - "abc\n".Length);
            data.Append(Head.Replace("20 0", string.Create(CultureInfo.InvariantCulture, $"{20 + i} 0"), StringComparison.Ordinal)
                .Replace("0000000", length.ToString("D7", CultureInfo.InvariantCulture), StringComparison.Ordinal));
        }

        data.Append(' ', Run).Append("x\n30 0 obj\n<< /Length 3 >>\nstream\nabc").Append(' ', 100).Append("endstream")
            .Append(glued ? new string('Z', 80) : string.Empty).Append("\nendobj\n");
        var text = new StringBuilder("%PDF-1.7\n1 0 obj\n" + Catalog + "\nendobj\n2 0 obj\n" + Pages + "\nendobj\n")
            .Append("10 0 obj\n<< /Type /EmbeddedFile /Length 11 0 R >>\nstream\n").Append(data).Append("endstream\nendobj\n")
            .Append(CultureInfo.InvariantCulture, $"11 0 obj\n{LengthNote}{data.Length}\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n%");

        // Each spelled stream's reads past its length double from 64 bytes until one holds the first byte past the spaces.
        var spent = 0;

        for (var size = 64; spent <= Run; size *= 2)
        {
            spent += size;
        }

        var file = Encoding.Latin1.GetBytes(text.Append('p', (Spelled * spent) + shortOfTheBound - text.Length - 1).Append('\n').ToString());
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxRebuildReadPasses = 1 } });

        file.Length.Should().Be((Spelled * spent) + shortOfTheBound);
        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(30)).AsStream().Required().GetRawBytes().Length.Should().Be(3);
        document.Diagnostics.Contains(PdfDiagnosticCodes.LimitRebuildReads).Should().Be(reached);
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(6, true)]
    [InlineData(5, false)]
    [InlineData(4, false)]
    [InlineData(2, false)]
    [InlineData(5, false, "0")]
    [InlineData(2, false, null, false)]
    public void The_search_for_the_objects_of_indirect_lengths_confirms_nothing_past_its_own_bound_and_reports_nothing(
        int misses, bool throwOnLimit, string? named = null, bool marked = true)
    {
        // A file with no cross-reference table, read with MaxRebuildReadPasses at one. Past a comment line, attached files,
        // each with an indirect /Length naming an object the file does not define, come before attached file 20, whose
        // length object 21 follows it and whose data spells object 77, and 64 KiB of comment lines. For each miss, the
        // rebuild's scan searches the file from the stream's endstream to its end for a header of the length's number, and
        // finds none: each search reads about the file, a little less. The searches together read four times the file at
        // most, an internal bound: past it, with five misses or six, the scan confirms no more indirect lengths, and reads
        // stream 20's data as the bytes give it, object 77 taken there, as before it looked for any length's object; with
        // four or two, it confirms stream 20 and takes nothing in its data. Either way it reports nothing, and a document
        // opened to throw opens: the guard bounds only the reads past direct lengths, which this file makes none of (#363).
        // A memo made after the rebuild scans the file again, its searches counted from nothing: it confirms what the
        // rebuild confirmed. A miss whose /Length names object 0, which the scan never takes, searches and spends alike, and
        // so does one in a file whose last bytes hold no end-of-file marker.
        var file = MissedLengths(misses, named: named, marked: marked);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxRebuildReadPasses = 1 }, ThrowOnLimit = throwOnLimit };

        using var document = PdfDocument.Open(file, options);

        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Select(entry => entry.Code).Should().NotContain(PdfDiagnosticCodes.LimitRebuildReads);
        document.Reader.GetPresence(77).Should().Be(misses >= 5 ? ObjectPresence.Defined : ObjectPresence.Missing);
        document.GetObject(new PdfObjectId(20)).AsStream().Required().GetRawBytes().Length.Should().Be(MissedData.Length);
        document.Reader.GetScanMemo().TryGetHeaderAt(OffsetOf(file, "77 0 obj"), out _).Should().Be(misses >= 5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_search_for_a_length_s_object_stops_where_the_searches_reach_their_bound_short_of_the_object_farther_on(bool farther)
    {
        // The case above with four misses, which read a little less than four times the file: the search for object 21, the
        // length's object of attached file 20, starts short of the bound, and finds the object right past the stream. Past
        // the 64 KiB of comment lines, it reads as far as the bound lets it, and stops there: the scan reads stream 20's
        // data as the bytes give it, object 77 taken there (#363).
        var file = MissedLengths(4, farther ? "past the comment lines after it" : "right after it");

        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeTrue();
        document.Reader.GetPresence(77).Should().Be(farther ? ObjectPresence.Defined : ObjectPresence.Missing);
    }

    [Theory]
    [InlineData("before it", 18, false)]
    [InlineData("before it", 19, true)]
    [InlineData("past a line after it", 82, false)]
    [InlineData("past a line after it", 83, true)]
    [InlineData("before it", 130, false, true)]
    [InlineData("before it", 131, true, true)]
    public void A_search_for_a_length_s_object_is_made_and_goes_on_only_short_of_the_bound_the_searches_share(
        string where, int padding, bool confirmed, bool confirmedFirst = false)
    {
        // The case above with four misses, a comment line of 25 dashes before them, and one of padding before the fourth.
        // Object 21 written right before attached file 20: with 18 bytes of padding, the misses read exactly four times the
        // file, the bound, and the scan neither reads object 21 among the last values it passed nor searches past the
        // stream: it reads stream 20's data as the bytes give it, object 77 taken there; with 19, they read a byte short of the
        // bound, and the scan confirms the stream with object 21's length. Object 21 written past a comment line after the
        // stream, past the 64 bytes the search reads first: with 82 bytes, the misses read 64 bytes short of the bound, which
        // that first read reaches, and no second read is made; with 83, it is, and finds object 21 (#363). Where a stream whose
        // length its object before it confirms comes first, and an end-of-file marker that an endstream follows after each
        // miss, the reads of the bytes past that confirmed length and past each marker a search's read cuts count too: with
        // 130 bytes of padding, the searches read exactly the bound before object 21 is read, with 131 a byte short of it.
        var file = MissedLengths(4, where, prefix: 25, padding: padding, confirmedFirst: confirmedFirst);

        using var document = PdfDocument.Open(file);

        file.Length.Should().Be((confirmedFirst ? 66_276 : where == "before it" ? 66_136 : 66_218) + padding);
        document.WasRepaired.Should().BeTrue();
        document.Reader.GetPresence(77).Should().Be(confirmed ? ObjectPresence.Missing : ObjectPresence.Defined);
    }

    [Fact]
    public void A_stream_whose_indirect_length_its_object_confirms_is_confirmed_after_the_reads_that_confirm_direct_lengths_reached_the_read_guard()
    {
        // The guard's case, object 30's /Length written as object 32, which the file writes after object 31: past its
        // length, 70 spaces lie before its endstream, which a first read cannot see past. The reads past the spelled streams'
        // direct lengths reach MaxRebuildReadPasses, reported where stream 24's data starts; those that confirm indirect
        // lengths are counted apart, and read through the spaces to object 30's endstream: the scan passes the data whole,
        // and the comment after the attached file's own endstream hides nothing (#363).
        var text = Encoding.Latin1.GetString(Case.For("rebuild reads").File);
        var key = "30 0 obj\n<< /Type /EmbeddedFile /Length ";
        var at = text.IndexOf(key, StringComparison.Ordinal) + key.Length;
        var length = text[at..text.IndexOf(' ', at)];
        text = text[..at] + "32 0 R" + text[(at + length.Length)..];
        text = text.Replace("31 0 obj (real) endobj\n", "31 0 obj (real) endobj\n32 0 obj\n" + length + "\nendobj\n", StringComparison.Ordinal);
        var file = Encoding.Latin1.GetBytes(text);

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(31)).AsText().Should().Be("real");
        document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.LimitRebuildReads).Select(entry => entry.Position)
            .Should().Equal(OffsetOf(file, "24 0 obj") + "20 0 obj\n<< /Length 0000000 >>\nstream\n".Length);
    }

    [Theory]
    [InlineData("a sign", 5)]
    [InlineData("a sign", 32)]
    [InlineData("an end-of-file marker", 32)]
    public void A_rebuild_s_scan_makes_none_of_the_reads_that_decide_only_how_a_memo_counts_what_it_takes(string glued, int count)
    {
        // Object 10, an attached file with an indirect /Length whose object holds a note before its length, spells streams
        // in its data that lost their endstream, each header glued to a sign or to an end-of-file marker, which the scan takes and a load's search does not stop at,
        // before a mebibyte of text. MaxXRefSectionLength cuts the table, and the rebuild's scan reads object 10's data as
        // objects. The search for where a load ends each spelled stream's data, which decides only how a memo would count the
        // places the scan takes, crossed the mebibyte for each: five times the file, beside the scan's own reading and the
        // loads the rebuild's finish makes. A rebuild's scan keeps no memo, and makes no such read (#361): opening reads less
        // than the reads that confirm lengths may, which these streams do not spend.
        var data = new StringBuilder();

        for (var i = 0; i < count; i++)
        {
            data.Append(glued == "a sign" ? "+" : "%%EOF").Append(CultureInfo.InvariantCulture, $"{20 + i} 0 obj\n<< /Length 5 >>\nstream\nabcdefgh\n");
        }

        data.Append('t', 1024 * 1024).Append('\n');
        var pdf = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = pdf.Object(1, Catalog), [2] = pdf.Object(2, Pages) };
        rows[10] = pdf.Position;
        pdf.Raw("10 0 obj\n<< /Type /EmbeddedFile /Length 11 0 R >>\nstream\n" + data + "endstream\nendobj\n");
        rows[11] = NotedLength(pdf, data.Length);
        pdf.Table(31, rows, "/Size 31 /Root 1 0 R");
        var file = pdf.Bytes;
        using var source = new StrictCountingSource(file);

        using var document = PdfDocument.Open(source, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 16 } }, ownsSource: false);

        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitXRefSectionLength);
        source.BytesRead.Should().BeLessThan(PdfReaderLimits.Default.MaxRebuildReadPasses * (long)file.Length);
    }

    [Theory]
    [InlineData("an older revision", "%", 40, "")]
    [InlineData("an older revision", "(", 40, "")]
    [InlineData("an older revision", "%", 9, "")]
    [InlineData("an older revision", "%", 3, "")]
    [InlineData("an earlier attached file", "%", 40, "")]
    [InlineData("the attached file itself", "%", 40, "")]
    [InlineData("an older revision", "%", 40, "a note that names the data stream it fills")]
    [InlineData("the attached file itself", "%", 40, "a note that names the data stream it fills")]
    [InlineData("the attached file, past its first object", "%", 40, "a note that names the data stream it fills")]
    [InlineData("an older revision", "% a note that ends at endstream and no sooner ", 40, "")]
    [InlineData("the attached file itself", "( a note that ends at endstream and no sooner ", 40, "")]
    [InlineData("an older revision", "%", 40, "over a mebibyte of plain text")]
    [InlineData("the attached file itself", "%", 40, "over a mebibyte of plain text")]
    [InlineData("an older revision", "", 40, "a header in a comment whose string runs over a header of object 12")]
    [InlineData("an older revision", "%", 40, "a stream header in a comment whose dictionary spells endstream")]
    [InlineData("an older revision", "%", 40, "a stream whose dictionary spells endstream and whose length an object in its data holds")]
    [InlineData("an older revision", "", 40, "a trailer keyword past the first endstream, in a comment between a header's tokens")]
    [InlineData("an older revision", "", 40, "a trailer keyword past the first endstream, in a comment between the tokens of a header whose string holds a header of object 12")]
    public void A_valid_file_a_guard_sends_into_a_rebuild_serves_the_objects_defined_before_and_after_a_stream_another_object_of_its_length_s_number_would_cut(
        string writer, string last, int value, string text)
    {
        // Object 14, an attached form written uncompressed, holds an object header in its data, and its indirect /Length
        // names object 11, written after it. Another object 11 comes first — one an older revision wrote, which the update
        // redefines; one an attached PDF holds in an earlier stream's data; or one the form holds before that header or
        // after it —, and its value ends the data past that header, at it, or short of it with bytes of the data between.
        // The data's last line holds a comment or an open string, right before endstream, which ISO 32000 only recommends
        // an end of line before, or before a word endstream of the form's own text, and object 13 follows endobj on that
        // line. The form's text may name a stream in a word, run past a mebibyte, spell a header of object 12, defined
        // before object 14, inside a string, or spell streams and a trailer keyword of its own. MaxXRefSectionLength cuts
        // the table. The rebuild's scan tries that other object 11 for the length, which no endstream follows, then the one
        // written after the stream, which an endstream follows, and passes the data whole (#363). Where that object 11 holds
        // a note before its length, which the scan does not read past, the scan cannot confirm the length: ending
        // the data where a load given the other object 11 does would read the rest of the data between objects, where a
        // comment or a string would hide object 13, or a string end before a header of object 12, and the scan reads the
        // data as the bytes give it, up to its first endstream, whatever the length says.
        const string Form = "99 0 obj\n<< /FDF << /F (a.pdf) /Fields [] >> >>\nendobj\nplain text the attached form carries\n";
        var other = string.Create(CultureInfo.InvariantCulture, $"11 0 obj\n{value}\nendobj\n");
        var note = text switch
        {
            "a note that names the data stream it fills" => "a note about the data stream it fills\n",
            "over a mebibyte of plain text" => string.Concat(Enumerable.Repeat("plain text the attached form carries, line after line of it\n", 18_000)),
            "a header in a comment whose string runs over a header of object 12" =>
                "% 98 0 obj (a string that runs on\n12 0 obj (attached) on to here) about the data stream the form fills\n",
            "a stream header in a comment whose dictionary spells endstream" =>
                "% 98 0 obj << /Length 97 0 R /X (endstream) >> stream\nplain text the attached form carries\n",
            "a stream whose dictionary spells endstream and whose length an object in its data holds" =>
                "98 0 obj << /Length 97 0 R /X (endstream) >> stream\n97 0 obj 3 endobj\nplain text about the data stream it fills\n",
            "a trailer keyword past the first endstream, in a comment between a header's tokens" => "4 0\n%endstream %trailer\nobj (one\n%) ",
            "a trailer keyword past the first endstream, in a comment between the tokens of a header whose string holds a header of object 12" =>
                "4 0\n%endstream %trailer\nobj (line one\n12 0 obj (attached)\nline two)\nendobj\nplain text\n",
            _ => "",
        };
        var data = "%FDF-1.2\n" + (writer == "the attached file itself" ? other : "") + Form + (writer == "the attached file, past its first object" ? other : "") +
            note + last;
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 16 } };

        foreach (var lengthNote in new[] { string.Empty, LengthNote })
        {
            using var document = PdfDocument.Open(Write(lengthNote), options);

            document.GetObject(new PdfObjectId(13)).AsText().Should().Be("real");
            document.GetObject(new PdfObjectId(12)).AsText().Should().Be("early");
            document.WasRepaired.Should().BeTrue();
            document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitXRefSectionLength);
        }

        byte[] Write(string lengthNote)
        {
            var pdf = new HandWrittenFile();
            var rows = new Dictionary<int, long> { [1] = pdf.Object(1, Catalog), [2] = pdf.Object(2, Pages), [12] = pdf.Object(12, "(early)") };
            var trailer = "/Size 16 /Root 1 0 R";

            if (writer == "an older revision")
            {
                rows[14] = pdf.Position;
                pdf.Raw("14 0 obj\n<< /Type /EmbeddedFile /Length 11 0 R >>\nstream\n" + new string('x', value - 1) + "\nendstream\nendobj\n");
                rows[11] = pdf.Position;
                pdf.Raw(other);
                rows[13] = pdf.Object(13, "(old)");
                trailer += string.Create(CultureInfo.InvariantCulture, $" /Prev {pdf.Table(16, rows, trailer)}");
            }
            else if (writer == "an earlier attached file")
            {
                var inner = "%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" + other + "%%EOF\n";
                rows[10] = pdf.Position;
                pdf.Raw("10 0 obj\n<< /Type /EmbeddedFile /Length 15 0 R >>\nstream\n" + inner + "endstream\nendobj\n");
                rows[15] = pdf.Object(15, inner.Length.ToString(CultureInfo.InvariantCulture));
            }

            rows[14] = pdf.Position;
            pdf.Raw("14 0 obj\n<< /Type /EmbeddedFile /Length 11 0 R >>\nstream\n" + data + "endstream endobj ");
            rows[13] = pdf.Position;
            pdf.Raw("13 0 obj (real) endobj\n");
            rows[11] = pdf.Object(11, lengthNote + data.Length.ToString(CultureInfo.InvariantCulture));
            pdf.Table(16, rows, trailer);
            return pdf.Bytes;
        }
    }

    [Theory]
    [InlineData("a prose endstream before it, its data's last line a comment", "no other object")]
    [InlineData("a prose endstream before it, its data's last line a comment", "an older revision")]
    [InlineData("a prose endstream before it, its data's last line an open string", "no other object")]
    [InlineData("a prose endstream before it, its data's last line an open string", "an older revision")]
    [InlineData("the endstream of a stream of its own before it", "no other object")]
    [InlineData("the endstream of a stream of its own before it", "an older revision")]
    [InlineData("an attached PDF file that lost an endstream, its last line a comment", "no other object")]
    [InlineData("an attached PDF file that lost an endstream, its last line a comment", "an older revision")]
    [InlineData("a dictionary that spells endstream before it", "no other object")]
    [InlineData("a dictionary that spells endstream before it", "an older revision")]
    [InlineData("a dictionary that spells endstream before it", "the attached file itself")]
    [InlineData("a dictionary that spells endstream before it, a commented header whose string runs over a header of object 12", "an older revision")]
    [InlineData("a dictionary that spells endstream before it, in the data of a stream whose length an object holds", "an older revision")]
    public void A_valid_file_a_guard_sends_into_a_rebuild_serves_the_objects_defined_before_and_after_a_stream_whose_data_spells_a_stream_with_a_wrong_direct_length(
        string shape, string writer)
    {
        // Object 14, an attached file written uncompressed, has an indirect /Length naming object 11, written after it, and
        // its data spells a stream of its own with a direct /Length that no endstream follows, past a first endstream of the
        // data: a word of prose, the endstream of a stream before it, or one its dictionary spells, so that its data starts
        // past it; or the attached file is a PDF file whose second stream lost its endstream. Object 11 may come first as
        // well — one an older revision wrote, which the update redefines, or one the attached file holds —, or not at all.
        // The data's last line holds a comment or an open string, right before endstream, and object 13 follows endobj on
        // that line; or the data spells a header of object 12, defined before object 14, inside a string a commented header
        // opens. MaxXRefSectionLength cuts the table. The rebuild's scan confirms object 14's length with object 11, written
        // after it, and passes the data whole (#363). Where that object 11 holds a note before its length, which the scan
        // does not read past, the scan, which reads the data of object 14 as the bytes give it up to its first
        // endstream, reads the stream spelled past it as the bytes give it up to the next endstream, as a load given no
        // more than its bytes does, whatever its direct /Length says. Ending that data where a load given the length does
        // would read the rest between objects, where the comment or the string would hide object 13, or a string end before
        // the header of object 12.
        const string Spelled = "5 0 obj\n<< /Length 3 >>\nstream\nabcdefgh\n99 0 obj\n(x)\nendobj\n";
        const string Form = "%FDF-1.2\n99 0 obj\n<< /FDF << /F (a.pdf) /Fields [] >> >>\nendobj\n";
        const string Cut = "98 0 obj << /Length 5 /X (endstream) >> stream\nabc\n97 0 obj (x) endobj\n";
        var data = shape switch
        {
            "a prose endstream before it, its data's last line a comment" =>
                "Attached notes.\nThis text quotes the word endstream in plain prose.\n" + Spelled + "% a note ",
            "a prose endstream before it, its data's last line an open string" =>
                "Attached notes.\nThis text quotes the word endstream in plain prose.\n" + Spelled + "( a note ",
            "the endstream of a stream of its own before it" =>
                "Attached notes.\n6 0 obj\n<< /Length 4 >>\nstream\nwxyz\nendstream\nendobj\n" + Spelled + "% a note ",
            "an attached PDF file that lost an endstream, its last line a comment" =>
                "%PDF-1.4\n31 0 obj\n<< /Length 10 >>\nstream\n0123456789\nendstream\nendobj\n32 0 obj\n<< /Length 60 >>\nstream\n" +
                "BT /F1 12 Tf (cut) Tj ET\n33 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 33 0 R >>\n%%EOF\n% end ",
            "a dictionary that spells endstream before it" =>
                Form.Replace("%FDF-1.2\n", writer == "the attached file itself" ? "%FDF-1.2\n11 0 obj\n40\nendobj\n" : "%FDF-1.2\n", StringComparison.Ordinal) +
                Cut + "plain text\n%",
            "a dictionary that spells endstream before it, a commented header whose string runs over a header of object 12" =>
                Form + Cut + "% 50 0 obj (a string that runs on\n12 0 obj (attached) still the string) the end\nplain text\n",
            _ => Form + "98 0 obj << /Length 97 0 R /X (endstream) >> stream\n96 0 obj << /Length 5 /Y (endstream) >> stream\nabc\n" +
                "95 0 obj (x) endobj\nplain text\n%",
        };
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 16 } };

        foreach (var lengthNote in new[] { string.Empty, LengthNote })
        {
            using var document = PdfDocument.Open(Write(lengthNote), options);

            document.GetObject(new PdfObjectId(13)).AsText().Should().Be("real");
            document.GetObject(new PdfObjectId(12)).AsText().Should().Be("early");
            document.WasRepaired.Should().BeTrue();
            document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitXRefSectionLength);
        }

        byte[] Write(string lengthNote)
        {
            var pdf = new HandWrittenFile();
            var rows = new Dictionary<int, long> { [1] = pdf.Object(1, Catalog), [2] = pdf.Object(2, Pages), [12] = pdf.Object(12, "(early)") };
            var trailer = "/Size 15 /Root 1 0 R";

            if (writer == "an older revision")
            {
                rows[14] = pdf.Position;
                pdf.Raw("14 0 obj\n<< /Type /EmbeddedFile /Length 11 0 R >>\nstream\n" + new string('x', 39) + "\nendstream\nendobj\n");
                rows[11] = pdf.Object(11, "40");
                rows[13] = pdf.Object(13, "(old)");
                trailer += string.Create(CultureInfo.InvariantCulture, $" /Prev {pdf.Table(15, rows, trailer)}");
            }

            rows[14] = pdf.Position;
            pdf.Raw("14 0 obj\n<< /Type /EmbeddedFile /Length 11 0 R >>\nstream\n" + data + "endstream endobj ");
            rows[13] = pdf.Position;
            pdf.Raw("13 0 obj (real) endobj\n");
            rows[11] = pdf.Object(11, lengthNote + data.Length.ToString(CultureInfo.InvariantCulture));
            pdf.Table(15, rows, trailer);
            return pdf.Bytes;
        }
    }

    [Theory]
    [InlineData("a trailer keyword in a comment, its dictionary naming an encryption dictionary", "after the stream")]
    [InlineData("a trailer keyword in a comment, its dictionary naming an encryption dictionary", "before the stream")]
    [InlineData("a header of a stream whose length runs past the data's endstream", "after the stream")]
    [InlineData("a header of a stream whose length runs past the data's endstream", "before the stream")]
    [InlineData("a header of a stream whose length runs past the data's endstream", "after the stream, each token on a line of its own")]
    [InlineData("a header of object 12 past a word endstream", "after the stream")]
    [InlineData("a trailer keyword naming an encryption dictionary in a comment between the tokens of a header", "after the stream")]
    [InlineData("an xref keyword in a comment between the tokens of a header of object 12", "after the stream")]
    public void A_valid_file_a_guard_sends_into_a_rebuild_takes_nothing_from_the_data_of_a_stream_whose_indirect_length_its_object_confirms(
        string text, string lengthAt)
    {
        // An update rewrites object 14, an attached text written uncompressed whose indirect /Length names object 11, which
        // the update writes after the stream, each token on a line of its own or not, or right before it; object 13 follows
        // endobj on endstream's line, and object 15,
        // a stream, comes after. The older revision wrote object 14, object 11 and object 13 before, and object 12. The text
        // spells a trailer keyword in a comment whose dictionary names an encryption dictionary; or the header of a stream
        // whose direct length an endstream follows past object 15's data; or, past a word endstream, a header of object
        // 12; or such a header, a comment between its tokens holding a trailer keyword or an xref keyword. MaxXRefSectionLength
        // cuts the table, and the rebuild's scan, which reads no index, read the text as the bytes give it: it merged the
        // trailer, and the file, which no section encrypts, would not open; or it passed the spelled stream, past objects 13,
        // 11 and 15, and served the older revision's object 13; or it served the attached object 12. The scan reads object
        // 11 in the file's bytes, among the last values it passed or past the data's endstream, and an endstream follows the
        // length it holds: the data is the stream's, and the scan takes nothing there (#363). The memo the scan makes for
        // the searches near a row or an offset takes the same places.
        const string Encrypt = "<< /Encrypt << /Filter /Standard /V 1 /R 2 /O <00> /U <00> /P -4 >> >>";
        var data = text switch
        {
            "a trailer keyword in a comment, its dictionary naming an encryption dictionary" =>
                "Attached notes.\n%trailer " + Encrypt + "\nmore notes\n",
            "a header of a stream whose length runs past the data's endstream" => "Attached notes.\n98 0 obj\n<< /Length LLLLLLL >>\nstream\ninner text\n",
            "a header of object 12 past a word endstream" => "Attached notes.\nThis text quotes the word endstream in plain prose.\n12 0 obj (attached) endobj\nmore\n",
            "a trailer keyword naming an encryption dictionary in a comment between the tokens of a header" =>
                "Attached notes.\n12 0 %trailer " + Encrypt + "\nobj (attached) endobj\nmore\n",
            _ => "Attached notes.\n12 0 %xref\nobj (attached) endobj\nmore\n",
        };
        var pdf = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = pdf.Object(1, Catalog), [2] = pdf.Object(2, Pages), [12] = pdf.Object(12, "(early)") };
        var trailer = "/Size 16 /Root 1 0 R";
        rows[14] = pdf.Position;
        pdf.Raw("14 0 obj\n<< /Type /EmbeddedFile /Length 11 0 R >>\nstream\n" + new string('x', 39) + "\nendstream\nendobj\n");
        rows[11] = pdf.Object(11, "40");
        rows[13] = pdf.Object(13, "(old)");
        trailer += string.Create(CultureInfo.InvariantCulture, $" /Prev {pdf.Table(16, rows, trailer)}");
        var length = data.Length.ToString(CultureInfo.InvariantCulture);

        if (lengthAt == "before the stream")
        {
            rows[11] = pdf.Object(11, length);
        }

        rows[14] = pdf.Position;
        pdf.Raw("14 0 obj\n<< /Type /EmbeddedFile /Length 11 0 R >>\nstream\n");
        var dataStart = pdf.Position;
        pdf.Raw(data + "endstream endobj ");
        rows[13] = pdf.Position;
        pdf.Raw("13 0 obj (real) endobj\n");

        if (lengthAt == "after the stream")
        {
            rows[11] = pdf.Object(11, length);
        }
        else if (lengthAt != "before the stream")
        {
            rows[11] = pdf.Position;
            pdf.Raw("11\n0\nobj\n" + length + "\nendobj\n");
        }

        rows[15] = pdf.Position;
        pdf.Raw("15 0 obj\n<< /Length 3 >>\nstream\nabc\nendstream\nendobj\n");
        pdf.Table(16, rows, trailer);
        var written = Encoding.Latin1.GetString(pdf.Bytes);
        var inner = written.IndexOf("LLLLLLL", StringComparison.Ordinal);

        if (inner >= 0)
        {
            var innerData = written.IndexOf("stream\n", inner, StringComparison.Ordinal) + "stream\n".Length;
            var fifteenData = written.IndexOf("abc\nendstream", written.IndexOf("15 0 obj", StringComparison.Ordinal), StringComparison.Ordinal) + "abc\n".Length;
            written = written.Replace("LLLLLLL", (fifteenData - innerData).ToString("D7", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 16 } };

        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(written), options);

        var memo = document.Reader.GetScanMemo();
        document.IsEncrypted.Should().BeFalse();
        document.Trailer.ContainsKey(PdfName.Get("Encrypt")).Should().BeFalse();
        document.GetObject(new PdfObjectId(13)).AsText().Should().Be("real");
        document.GetObject(new PdfObjectId(12)).AsText().Should().Be("early");
        document.GetObject(new PdfObjectId(15)).AsStream().Required().GetRawBytes().Length.Should().Be(3);
        document.GetObject(new PdfObjectId(14)).AsStream().Required().GetRawBytes().Length.Should().Be(data.Length);
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Select(entry => entry.Code).Should().Contain(PdfDiagnosticCodes.LimitXRefSectionLength).And.NotContain(PdfDiagnosticCodes.LimitRebuildReads);
        memo.ReadsBounded.Should().BeFalse();
        Headers(memo, dataStart, dataStart + data.Length).Should().BeEmpty();

        foreach (var number in new[] { 1, 2, 11, 12, 13, 14, 15 })
        {
            document.Reader.Index.TryGet(number, out var entry).Should().BeTrue();
            document.Reader.PositionOf(entry).Should().Be(
                Headers(memo, 0, long.MaxValue).Last(header => header.Number == number).Position, "the rebuild takes the memo's last header of object {0}", number);
        }

        static List<ScannedHeader> Headers(ScanMemo memo, long from, long to)
        {
            var headers = new List<ScannedHeader>();

            foreach (var header in memo.HeadersWithin(from, to))
            {
                headers.Add(header);
            }

            return headers;
        }
    }

    [Theory]
    [InlineData("an older revision's length object, among the last values before the rewritten stream")]
    [InlineData("a newer revision's length object, the first of its number past the older stream's data")]
    public void A_valid_file_a_guard_sends_into_a_rebuild_serves_the_objects_of_an_update_another_revision_s_length_object_would_cut(string shape)
    {
        // An update rewrites object 4, a content stream whose indirect /Length names object 5, adds a form, object 7, and
        // writes object 5 again, after them. The older revision's object 5 comes among the last values before the rewritten
        // stream, past the older revision's trailer, and the length it holds ends that stream's data where the form's data
        // ends; or the older revision's stream 4 comes more than eight values past its object 5, and the newer revision's
        // object 5, the first of that number past the older data, ends that data where the form's data ends. Either way an
        // endstream follows. MaxXRefSectionLength cuts the table. The rebuild's scan reads no length from a value an older
        // revision wrote, and, past the data of a stream that is no attached file's, tries the first header alone: it
        // confirms neither stream with the other revision's length, and serves the form and the newer stream (#363).
        const string Line = "0 0 m 100 100 l S\n";
        var pdf = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = pdf.Object(1, Catalog), [2] = pdf.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>") };
        var older = shape.StartsWith("an older", StringComparison.Ordinal);
        long oldDataStart = 0;

        if (older)
        {
            rows[3] = pdf.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>");
            var oldData = Fill(700);
            rows[4] = pdf.Object(4, "<< /Length 5 0 R >>\nstream\n" + oldData + "\nendstream");
            rows[5] = pdf.Object(5, "3000");
        }
        else
        {
            rows[5] = pdf.Object(5, "LLLL");

            for (var number = 20; number < 29; number++)
            {
                rows[number] = pdf.Object(number, string.Create(CultureInfo.InvariantCulture, $"(filler {number})"));
            }

            rows[3] = pdf.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>");
            rows[4] = pdf.Position;
            pdf.Raw("4 0 obj\n<< /Length 5 0 R >>\nstream\n");
            oldDataStart = pdf.Position;
            pdf.Raw("0 0 m 100 \nendstream\nendobj\n");
        }

        var trailer = "/Size 40 /Root 1 0 R";
        trailer += string.Create(CultureInfo.InvariantCulture, $" /Prev {pdf.Table(40, rows, trailer)}");
        var newData = Fill(older ? 200 : 2500);
        long newDataStart = 0;

        if (older)
        {
            rows[4] = pdf.Position;
            pdf.Raw("4 0 obj\n<< /Length 5 0 R >>\nstream\n");
            newDataStart = pdf.Position;
            pdf.Raw(newData + "\nendstream\nendobj\n");
        }

        // The form's data ends where the other revision's length, counted from the data it does not measure, ends.
        const string FormHead = "7 0 obj\n<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /Length NNNN >>\nstream\n";
        rows[7] = pdf.Position;
        var formLength = (int)((older ? newDataStart + 3000 : oldDataStart + newData.Length) - (pdf.Position + FormHead.Length));
        formLength.Should().BeInRange(1000, 9999);
        var form = Fill(formLength);
        pdf.Raw(FormHead.Replace("NNNN", formLength.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) + form + "\nendstream\nendobj\n");

        if (!older)
        {
            rows[4] = pdf.Object(4, "<< /Length 5 0 R >>\nstream\n" + newData + "\nendstream");
        }

        rows[3] = pdf.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /XObject << /Fm1 7 0 R >> >> >>");
        rows[5] = pdf.Object(5, newData.Length.ToString(CultureInfo.InvariantCulture));
        pdf.Table(40, rows, trailer);
        var file = older ? pdf.Bytes : Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(pdf.Bytes).Replace("LLLL", newData.Length.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 16 } });

        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitXRefSectionLength);
        document.GetObject(new PdfObjectId(7)).AsStream().Required().GetRawBytes().ToArray().Should().Equal(Encoding.Latin1.GetBytes(form));
        document.GetObject(new PdfObjectId(4)).AsStream().Required().GetRawBytes().ToArray().Should().Equal(Encoding.Latin1.GetBytes(newData));

        static string Fill(int length) =>
            string.Concat(Enumerable.Repeat(Line, (length / Line.Length) + 1))[..length];
    }

    [Theory]
    [InlineData("an update that replaces an attached file, writing its length's object again before it")]
    [InlineData("an update that replaces an attached file, writing its length's object again after it")]
    [InlineData("an update that replaces an attached file, a content stream the older revision's last object")]
    [InlineData("an update that replaces an attached file, its length ending the older data where no endstream follows")]
    [InlineData("an update that writes a form, then the length's object of the attached file it replaces")]
    [InlineData("an update whose first object is the length's object of the older revision's last object, a content stream")]
    [InlineData("an attached PDF file that writes its own table, trailer and end-of-file marker, its length's object after it")]
    [InlineData("an update that replaces an attached file, writing its length's object again before it", "an ExifTool update comment")]
    [InlineData("an update that replaces an attached file, writing its length's object again before it", "80 spaces")]
    [InlineData("an update that replaces an attached file, writing its length's object again before it", "200 NULs")]
    [InlineData("an update whose first object is the length's object of the older revision's last object, a content stream", "an ExifTool update comment")]
    [InlineData("an update whose first object is the length's object of the older revision's last object, a content stream", "80 spaces")]
    [InlineData("an update whose first object is the length's object of the older revision's last object, a content stream", "200 NULs")]
    public void A_valid_file_a_guard_sends_into_a_rebuild_serves_its_objects_with_no_length_s_object_read_past_the_end_of_a_stream_s_revision(string shape, string past = "")
    {
        // An older revision writes object 9, ten values, then object 8, a stream whose indirect /Length names 9 — an attached
        // file, then a content stream, object 10, a page and a font; or the page, the font, then a content stream, its last
        // object. An update replaces object 8 and writes object 9 again, before it or after it, a form, object 12, first or
        // not. The length the newer object 9 holds ends the older stream's data where object 10's data, or the form's, ends,
        // an endstream following; or five bytes past it. MaxXRefSectionLength cuts the table, and the rebuild's scan, which
        // finds object 9 among none of the values before the older stream, searches past its data for it: it stops at the end
        // of the older revision, the end-of-file marker no endstream follows, and reads the stream as before it searched — it
        // once confirmed it with the newer length, and took neither object 10, nor the page, nor the font, nor the form, nor
        // the newer object 9 (#363). An attached file that writes its own table, trailer and marker is searched past
        // them, its marker followed by its stream's endstream, to object 9 after it: none of its objects is taken. The older
        // revision's marker ends it with the update's first header right after it, and with an ExifTool comment or padding
        // longer than the bytes read past it before that header, as real files write them. Every object is served as the
        // index serves it.
        const string Content = "BT /F1 24 Tf 72 720 Td (Hello) Tj ET";
        const string Attached = "<< /Type /EmbeddedFile /Length 9 0 R >>";
        const string Form = "<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /Length 15 >>";
        const string Inner = "%PDF-1.4\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n2 0 obj << /Type /Pages /Kids [] /Count 0 >> endobj\n" +
            "77 0 obj << /Length 3 >> stream\nabc\nendstream endobj\nxref\n0 1\n0000000000 65535 f \ntrailer << /Size 78 /Root 1 0 R >>\n" +
            "startxref\n142\n%%EOF";
        var content = shape.EndsWith("a content stream", StringComparison.Ordinal);
        var own = shape.StartsWith("an attached PDF file", StringComparison.Ordinal);
        var last = shape.EndsWith("the older revision's last object", StringComparison.Ordinal);
        var oldData = content ? Content : own ? Inner : new string('Q', 3000);
        var pdf = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = pdf.Object(1, Catalog), [2] = pdf.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>") };
        var trailer = "/Size 30 /Root 1 0 R";

        if (!own)
        {
            rows[9] = pdf.Object(9, oldData.Length.ToString(CultureInfo.InvariantCulture));

            for (var number = 20; number < 30; number++)
            {
                rows[number] = pdf.Object(number, string.Create(CultureInfo.InvariantCulture, $"<< /Filler {number} >>"));
            }
        }

        long oldDataStart = 0;
        long target = 0;

        if (!content && !own)
        {
            (oldDataStart, _) = Stream(8, Attached, oldData);
        }

        if (!last)
        {
            (_, target) = Stream(10, string.Create(CultureInfo.InvariantCulture, $"<< /Length {Content.Length} >>"), Content);
        }

        rows[3] = pdf.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 10 0 R /Resources << /Font << /F1 11 0 R >> >> >>");
        rows[11] = pdf.Object(11, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        if (last)
        {
            (_, target) = Stream(10, string.Create(CultureInfo.InvariantCulture, $"<< /Length {Content.Length} >>"), Content);
        }

        if (content)
        {
            (oldDataStart, _) = Stream(8, "<< /Length 9 0 R >>", oldData);
        }

        if (own)
        {
            Stream(8, Attached, oldData);
            rows[9] = pdf.Object(9, oldData.Length.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            trailer += string.Create(CultureInfo.InvariantCulture, $" /Prev {pdf.Table(30, rows, trailer)}");
            pdf.Raw(past switch
            {
                "an ExifTool update comment" => "%BeginExifToolUpdate\n",
                "80 spaces" => new string(' ', 80) + "\n",
                "200 NULs" => new string('\0', 200) + "\n",
                _ => string.Empty,
            });
        }

        var length = own ? 0 : shape switch
        {
            "an update that writes a form, then the length's object of the attached file it replaces" => Stream(12, Form, "0 0 m 10 10 l S").EndStream - oldDataStart - 1,
            "an update whose first object is the length's object of the older revision's last object, a content stream" => LengthFirst(),
            _ => target - oldDataStart - 1 + (shape.EndsWith("where no endstream follows", StringComparison.Ordinal) ? 5 : 0),
        };

        if (!own && !content)
        {
            var after = shape.EndsWith("after it", StringComparison.Ordinal);

            if (!after)
            {
                rows[9] = pdf.Object(9, length.ToString(CultureInfo.InvariantCulture));
            }

            Stream(8, Attached, new string('R', (int)length));

            if (after)
            {
                rows[9] = pdf.Object(9, length.ToString(CultureInfo.InvariantCulture));
            }
        }

        pdf.Table(30, rows, trailer);
        var file = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(pdf.Bytes).Replace("LLLLLLLLLL", length.ToString("D10", CultureInfo.InvariantCulture), StringComparison.Ordinal));

        using var intact = PdfDocument.Open(file);
        using var rebuilt = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 16 } });

        intact.WasRepaired.Should().BeFalse();
        rebuilt.WasRepaired.Should().BeTrue();
        rebuilt.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitXRefSectionLength);

        foreach (var number in intact.ObjectNumbers.Union(rebuilt.ObjectNumbers).ToList())
        {
            Served(rebuilt, number).Should().Be(Served(intact, number), "the rebuild serves object {0} as the index does", number);
        }

        // Object 9 comes first, its value as wide as the length it holds, written once the form, whose endstream that length
        // ends the older data at, is; then object 8, its content padded to that length by a comment.
        long LengthFirst()
        {
            rows[9] = pdf.Object(9, "LLLLLLLLLL");
            var form = Stream(12, Form, "0 0 m 10 10 l S").EndStream - oldDataStart - 1;
            Stream(8, "<< /Length 9 0 R >>", Content + "\n%" + new string(' ', (int)form - Content.Length - 2));
            return form;
        }

        (long DataStart, long EndStream) Stream(int number, string dictionary, string data)
        {
            rows[number] = pdf.Position;
            pdf.Raw(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{dictionary}\nstream\n"));
            var dataStart = pdf.Position;
            pdf.Raw(data + "\n");
            var endStream = pdf.Position;
            pdf.Raw("endstream\nendobj\n");
            return (dataStart, endStream);
        }

        static string Served(PdfDocument document, int number) =>
            document.GetObject(new PdfObjectId(number)) is PdfStream stream
                ? stream.Dictionary + Encoding.Latin1.GetString(stream.GetRawBytes().Span)
                : document.GetObject(new PdfObjectId(number))?.ToString() ?? "null";
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_rebuild_s_scan_searches_no_further_for_the_length_of_a_stream_whose_length_object_right_before_it_holds_a_length_no_endstream_follows(bool lostEndStream)
    {
        // Twelve content streams, each right after the object that holds its length, as linearized writers put it, whose
        // lengths a damage made short: each line ends with a carriage return and a line feed where the writer counted one
        // byte, and the eighth stream may have lost its endstream. Then attached file 90, whose length object 91 follows it
        // and whose data spells object 77, and 64 KiB of comment lines. The file has no cross-reference table. The rebuild's
        // scan reads each content stream's length from the object right before it, which no endstream follows, and searches
        // past the data for no other: each such search would read on to the end of the file, the twelve spending what the
        // reads that confirm indirect lengths may read before the scan reaches the attached file. The content streams are
        // read as before the scan looked for any length's object, the attached file's length is confirmed and nothing in
        // its data taken, and no guard is reached, for a document opened to throw on one too (#363).
        const string Data = "77 0 obj (inner) endobj\n";
        var text = new StringBuilder("%PDF-1.7\n1 0 obj\n" + Catalog + "\nendobj\n2 0 obj\n" + Pages + "\nendobj\n");

        for (var i = 0; i < 12; i++)
        {
            var lines = string.Concat(Enumerable.Repeat("0 0 m 100 100 l S\n", 8));
            text.Append(CultureInfo.InvariantCulture, $"{40 + i} 0 obj\n{lines.Length}\nendobj\n{10 + i} 0 obj\n<< /Length {40 + i} 0 R >>\nstream\r\n")
                .Append(lines.Replace("\n", "\r\n", StringComparison.Ordinal))
                .Append(lostEndStream && i == 7 ? "endobj\r\n" : "endstream\r\nendobj\r\n");
        }

        for (var line = 0; line < 1024; line++)
        {
            text.Append("% ").Append('-', 61).Append('\n');
        }

        text.Append("90 0 obj\n<< /Type /EmbeddedFile /Length 91 0 R >>\nstream\n").Append(Data)
            .Append(CultureInfo.InvariantCulture, $"endstream\nendobj\n91 0 obj\n{Data.Length}\nendobj\n");
        var file = Encoding.Latin1.GetBytes(text.Append("trailer\n<< /Root 1 0 R >>\n%%EOF\n").ToString());

        using var document = PdfDocument.Open(file, new PdfReaderOptions { ThrowOnLimit = true });

        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Select(entry => entry.Code).Should().NotContain(PdfDiagnosticCodes.LimitRebuildReads);
        document.Reader.GetPresence(77).Should().Be(ObjectPresence.Missing);

        for (var i = 0; i < 12; i++)
        {
            document.Reader.GetPresence(10 + i).Should().Be(ObjectPresence.Defined);
            document.Reader.GetPresence(40 + i).Should().Be(ObjectPresence.Defined);
        }
    }

    [Theory]
    [InlineData("its startxref five bytes past the cross-reference stream", false)]
    [InlineData("its startxref five bytes past the cross-reference stream", true)]
    [InlineData("three line feeds written before the sixth content stream", false)]
    [InlineData("three line feeds written before the sixth content stream", true)]
    [InlineData("the sixth content stream's endstream lost", false)]
    [InlineData("the sixth content stream's endstream lost", true)]
    [InlineData("no cross-reference stream", false)]
    [InlineData("no cross-reference stream", true)]
    public void A_damaged_file_whose_streams_lengths_an_object_stream_holds_reads_each_stream_as_before_the_scan_searched_for_length_objects(string damage, bool throwOnLimit)
    {
        // Twelve pages, written as a writer that packs the objects holding its content streams' lengths in an object stream
        // writes them: no header of those numbers lies in the file. Then attached file 90, whose length object 91 follows it,
        // and whose data spells object 77. A damage sends the reader to search near an offset or a row, or into a rebuild,
        // which the scan for objects serves. For each content stream, the scan reads the first header past its data, the
        // next page's, which is no object of its length's number, and searches no further: a search to the end of the file
        // for each would spend what the reads that confirm indirect lengths may read before the scan reaches the attached
        // file. The content streams are read as before the scan looked for any length's object, the attached file's length
        // is confirmed and nothing in its data taken, and no guard is reached, for a document opened to throw on one too
        // (#363).
        const string Data = "77 0 obj (inner) endobj\n";
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [" + string.Join(' ', Enumerable.Range(0, 12).Select(i => $"{3 + (2 * i)} 0 R")) + "] /Count 12 >>");
        var contents = new string[12];

        for (var i = 0; i < 12; i++)
        {
            contents[i] = string.Concat(Enumerable.Repeat(string.Create(CultureInfo.InvariantCulture, $"BT /F1 12 Tf 72 {700 - i} Td (page {i + 1}) Tj ET\n"), 6));
            builder.WithObject(3 + (2 * i), string.Create(CultureInfo.InvariantCulture, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {4 + (2 * i)} 0 R >>"))
                .WithObject(4 + (2 * i), string.Create(CultureInfo.InvariantCulture, $"<< /Length {30 + i} 0 R >>\nstream\n{contents[i]}\nendstream"))
                .WithObject(30 + i, contents[i].Length.ToString(CultureInfo.InvariantCulture));
        }

        builder.WithObject(90, "<< /Type /EmbeddedFile /Length 91 0 R >>\nstream\n" + Data + "\nendstream")
            .WithObject(91, Data.Length.ToString(CultureInfo.InvariantCulture));
        var text = Encoding.Latin1.GetString(builder.BuildWithXRefStream(rootNumber: 1, compressedObjects: [.. Enumerable.Range(30, 12)]));
        var sixth = text.IndexOf("14 0 obj", StringComparison.Ordinal);
        var startxref = text.LastIndexOf("startxref\n", StringComparison.Ordinal) + "startxref\n".Length;
        var xref = long.Parse(text.AsSpan(startxref, text.IndexOf('\n', startxref) - startxref), CultureInfo.InvariantCulture);
        text = damage switch
        {
            "its startxref five bytes past the cross-reference stream" =>
                text[..startxref] + (xref + 5).ToString(CultureInfo.InvariantCulture) + text[text.IndexOf('\n', startxref)..],
            "three line feeds written before the sixth content stream" => text[..sixth] + "\n\n\n" + text[sixth..],
            "the sixth content stream's endstream lost" =>
                text[..text.IndexOf("endstream", sixth, StringComparison.Ordinal)] + text[(text.IndexOf("endstream", sixth, StringComparison.Ordinal) + "endstream".Length)..],
            _ => text[..(int)xref] + "startxref\n0\n%%EOF\n",
        };

        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text), new PdfReaderOptions { ThrowOnLimit = throwOnLimit });

        document.Diagnostics.Select(entry => entry.Code).Should().NotContain(PdfDiagnosticCodes.LimitRebuildReads);
        document.Reader.GetPresence(77).Should().Be(ObjectPresence.Missing);
        document.GetObject(new PdfObjectId(90)).AsStream().Required().GetRawBytes().Length.Should().Be(Data.Length);

        for (var i = 0; i < 12; i++)
        {
            if (damage != "the sixth content stream's endstream lost" || i != 5)
            {
                Encoding.Latin1.GetString(document.GetObject(new PdfObjectId(4 + (2 * i))).AsStream().Required().GetRawBytes().Span).Should().Be(contents[i]);
            }
        }
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
    public void A_member_longer_than_MaxObjectLength_is_read_to_the_bound_and_each_is_reported_once()
    {
        // Objects 4 and 5, packed in object stream 6, run past a bound of 4 KB: each is read to it, as an object of the file
        // is, and each is reported, though both are placed where the stream's data starts; read again once the cache let
        // them go, neither is reported again (#160).
        var file = LongMembers(out var starts);
        var options = new PdfReaderOptions { ObjectCacheCapacity = 64, Limits = PdfReaderLimits.Default with { MaxObjectLength = 4096 } };
        using var document = PdfDocument.Open(file, options);

        for (var round = 0; round < 2; round++)
        {
            document.GetObject(new PdfObjectId(4)).AsArray().Required().Count.Should().BeInRange(2000, 2048);
            document.GetObject(new PdfObjectId(5)).AsArray().Required().Count.Should().BeInRange(2000, 2048);

            for (var number = 10; number < 90; number++)
            {
                document.GetObject(new PdfObjectId(number));
            }
        }

        document.Reader.IsCutAtLimit(4).Should().BeTrue();
        document.Reader.IsCutAtLimit(5).Should().BeTrue();
        var text = Encoding.Latin1.GetString(file);
        var dataStart = text.IndexOf("stream\n", text.IndexOf("/Type /ObjStm", StringComparison.Ordinal), StringComparison.Ordinal) + "stream\n".Length;
        document.Diagnostics.Select(entry => (entry.Code, entry.Position, entry.Message)).Should().Equal(
            (PdfDiagnosticCodes.LimitObject, dataStart, string.Create(CultureInfo.InvariantCulture, $"Object 4, at byte {starts[0]} of object stream 6's decoded data, runs past 4 KB; only what lies within it was read. Raise PdfReaderLimits.MaxObjectLength to read past it.")),
            (PdfDiagnosticCodes.LimitObject, dataStart, string.Create(CultureInfo.InvariantCulture, $"Object 5, at byte {starts[1]} of object stream 6's decoded data, runs past 4 KB; only what lies within it was read. Raise PdfReaderLimits.MaxObjectLength to read past it.")));
    }

    [Fact]
    public void A_member_longer_than_MaxObjectLength_throws_each_time_it_is_read_when_the_document_throws_on_a_limit()
    {
        var file = LongMembers(out _);
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxObjectLength = 4096 } };
        using var document = PdfDocument.Open(file, options);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var reading = () => document.GetObject(new PdfObjectId(4));
            reading.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));
        }
    }

    [Fact]
    public void Under_ThrowOnLimit_an_object_whose_load_reached_a_guard_throws_the_same_exception_again_without_reading_it_again()
    {
        // Object 5, written directly, runs past MaxObjectLength. Opened to throw, its read throws, and keeps the exception: read
        // again, it throws the same exception without reading the object again, and the document's trailer stays the one it
        // gave (#181).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        rows[5] = file.Object(5, "(" + new string('x', 2000) + ")");
        file.Table(6, rows, "/Size 6 /Root 1 0 R");
        using var source = new OffsetCountingSource(file.Bytes);
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } };
        using var document = PdfDocument.Open(source, options, ownsSource: false);
        var trailer = document.Trailer;

        var first = ReadPastGuard(document, 5);
        var read = source.BytesRead;

        first.Should().NotBeNull().And.Match<PdfLimitExceededException>(thrown => thrown.Code == PdfDiagnosticCodes.LimitObject);
        ReadPastGuard(document, 5).Should().BeSameAs(first);
        source.BytesRead.Should().Be(read);
        document.Trailer.Should().BeSameAs(trailer);
    }

    [Fact]
    public void Under_ThrowOnLimit_the_exceptions_loads_threw_are_kept_as_many_as_the_reader_caches_objects_the_oldest_let_go_first()
    {
        // Sixty-five objects, 10 to 74, written directly, each run past MaxObjectLength, read in turn through a cache of 64
        // objects, opened to throw: each throws its own exception, and keeps it, as many as the cache holds objects. Read again,
        // object 11 throws the exception it kept, without reading, and object 10, whose exception was let go first, is read
        // again, and throws anew (#181).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };

        for (var number = 10; number < 75; number++)
        {
            rows[number] = file.Object(number, "(" + new string('x', 2000) + ")");
        }

        file.Table(75, rows, "/Size 75 /Root 1 0 R");
        using var source = new OffsetCountingSource(file.Bytes);
        var options = new PdfReaderOptions { ThrowOnLimit = true, ObjectCacheCapacity = 64, Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } };
        using var document = PdfDocument.Open(source, options, ownsSource: false);

        var thrown = Enumerable.Range(10, 65).Select(number => ReadPastGuard(document, number)).ToList();
        var read = source.BytesRead;

        thrown.Should().AllSatisfy(exception => exception.Should().NotBeNull()).And.OnlyHaveUniqueItems();
        ReadPastGuard(document, 11).Should().BeSameAs(thrown[1]);
        source.BytesRead.Should().Be(read);
        ReadPastGuard(document, 10).Should().NotBeNull().And.NotBeSameAs(thrown[0]);
        source.BytesRead.Should().BeGreaterThan(read);
    }

    [Fact]
    public void Under_ThrowOnLimit_the_exceptions_object_stream_decodings_threw_are_kept_as_many_as_the_reader_caches_objects_the_oldest_let_go_first()
    {
        // Sixty-five object streams, 10 to 74, each decoding to 64 KB, past MaxDecodedStreamLength, and holding two objects, read
        // in turn through a cache of 64 objects, opened to throw: the first object of each throws its stream's own exception,
        // and the stream keeps it, as many as the cache holds objects. The second object of stream 11 throws the exception that
        // stream kept, without decoding it again; that of stream 10, whose exception was let go first, decodes it again, and
        // throws anew (#181).
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
        };

        for (var stream = 10; stream < 75; stream++)
        {
            var first = 100 + (2 * (stream - 10));
            rows[stream] = (1, file.PaddedObjectStream(stream, [(first, "<< >>"), (first + 1, "<< >>")], 64 * 1024), 0);
            rows[first] = (2, stream, 0);
            rows[first + 1] = (2, stream, 1);
        }

        file.CompressedCrossReferenceStream(300, 301, number => rows.TryGetValue(number, out var row) ? row : (0, 0, 0), "/Root 1 0 R");
        using var source = new OffsetCountingSource(file.Bytes);
        var options = new PdfReaderOptions { ThrowOnLimit = true, ObjectCacheCapacity = 64, Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 4096 } };
        using var document = PdfDocument.Open(source, options, ownsSource: false);

        var thrown = Enumerable.Range(0, 65).Select(k => ReadPastGuard(document, 100 + (2 * k))).ToList();
        var read = source.BytesRead;

        thrown.Should().AllSatisfy(exception => exception.Should().NotBeNull()).And.OnlyHaveUniqueItems();
        ReadPastGuard(document, 103).Should().BeSameAs(thrown[1]);
        source.BytesRead.Should().Be(read);
        ReadPastGuard(document, 101).Should().NotBeNull().And.NotBeSameAs(thrown[0]);
        source.BytesRead.Should().BeGreaterThan(read);
    }

    [Fact]
    public void Under_ThrowOnLimit_an_exception_the_loads_let_go_of_is_kept_by_the_next_load_it_is_thrown_through()
    {
        // Object stream 10 decodes to 64 KB, past MaxDecodedStreamLength, and holds objects 11 and 12. Opened to throw through
        // a cache of 64 objects, a read of 11 throws the stream's exception, which the stream and 11 keep. Sixty-four objects
        // written directly, 100 to 163, each run past MaxObjectLength: read in turn, each keeps its own, and 11 lets go of
        // its. A read of 12 throws the exception the stream kept, and 12 keeps it in its turn. Sixty-four object streams, 200
        // to 263, each take their /DecodeParms from an object they hold, which reads as null while they are decoded: a read of
        // the first object of each throws, and each stream keeps its exception, stream 10 letting go of its. Read again, 12
        // throws the exception it kept, without decoding stream 10 again (#181).
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
            [10] = (1, file.PaddedObjectStream(10, [(11, "<< >>"), (12, "<< >>")], 64 * 1024), 0),
            [11] = (2, 10, 0),
            [12] = (2, 10, 1),
        };

        for (var number = 100; number < 164; number++)
        {
            rows[number] = (1, file.Object(number, "(" + new string('x', 2000) + ")"), 0);
        }

        for (var stream = 200; stream < 264; stream++)
        {
            var first = 300 + (2 * (stream - 200));
            var parameters = string.Create(CultureInfo.InvariantCulture, $"/DecodeParms {first + 1} 0 R ");
            rows[stream] = (1, file.PaddedObjectStream(stream, [(first, "<< >>"), (first + 1, "<< >>")], 64 * 1024, parameters), 0);
            rows[first] = (2, stream, 0);
            rows[first + 1] = (2, stream, 1);
        }

        file.CompressedCrossReferenceStream(430, 431, number => rows.TryGetValue(number, out var row) ? row : (0, 0, 0), "/Root 1 0 R");
        using var source = new OffsetCountingSource(file.Bytes);
        var options = new PdfReaderOptions
        {
            ThrowOnLimit = true,
            ObjectCacheCapacity = 64,
            Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 4096, MaxObjectLength = 1024 },
        };
        using var document = PdfDocument.Open(source, options, ownsSource: false);

        var thrown = ReadPastGuard(document, 11);

        for (var number = 100; number < 164; number++)
        {
            ReadPastGuard(document, number).Should().NotBeNull();
        }

        ReadPastGuard(document, 12).Should().BeSameAs(thrown);

        for (var stream = 200; stream < 264; stream++)
        {
            ReadPastGuard(document, 300 + (2 * (stream - 200))).Should().NotBeNull();
        }

        var read = source.BytesRead;

        thrown.Should().NotBeNull().And.Match<PdfLimitExceededException>(exception => exception.Code == PdfDiagnosticCodes.LimitDecodedStream);
        ReadPastGuard(document, 12).Should().BeSameAs(thrown);
        source.BytesRead.Should().Be(read);
    }

    [Theory]
    [InlineData(129)]
    [InlineData(200)]
    public void Unbounded_limits_read_containers_nested_past_the_default_bound_whole(int levels)
    {
        var file = new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, Pages)
            .WithObject(3, new string('[', levels) + new string(']', levels))
            .BuildClassic(rootNumber: 1);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Unbounded, ThrowOnLimit = true });

        Depth(document.GetObject(new PdfObjectId(3))).Should().Be(levels);

        document.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("containers")]
    [InlineData("dictionaries")]
    [InlineData("loads")]
    public void What_the_thread_s_stack_cut_is_read_whole_once_a_thread_has_room_for_it(string nesting)
    {
        // Six hundred arrays, one inside the other, or six hundred dictionaries, each the /A of the one around it, under a
        // bound of a thousand; or object 3 in the first of 60 object streams, each taking its /Filter from an object the next
        // holds, under a bound of a thousand loads. Read on a small stack, beneath a frame taking 160 KB of it so that the cut
        // falls whatever the JIT made of the parser's frames, the stack cuts them, reported under the guard's code — the
        // parser's, or the loads' —; read again on a large one, they read whole: what the stack cut was not kept. Arrays and
        // dictionaries each ask the stack for room before a level: a parse of nested dictionaries alone passes through no
        // array.
        var file = nesting switch
        {
            "containers" => new TestPdfBuilder().WithObject(1, Catalog).WithObject(2, Pages).WithObject(3, new string('[', 600) + new string(']', 600)).BuildClassic(rootNumber: 1),
            "dictionaries" => new TestPdfBuilder().WithObject(1, Catalog).WithObject(2, Pages).WithObject(3, string.Concat(Enumerable.Repeat("<< /A ", 600)) + "1" + string.Concat(Enumerable.Repeat(" >>", 600))).BuildClassic(rootNumber: 1),
            _ => HostileInputTests.ObjectStreamsTakingTheirFilterFromTheNext(60),
        };
        var limits = PdfReaderLimits.Default with { MaxNestingDepth = 1000, MaxNestedLoads = 1000 };
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        var small = Stacks.Run(Stacks.Small, () => Beneath(160 * 1024, () => Read(document, nesting)));
        var large = Stacks.Run(Stacks.Large, () => Read(document, nesting));

        small.Should().NotBe(large);
        large.Should().Be(nesting == "loads" ? "\"three\"" : "600");

        // Runs work beneath a frame that takes bytes of the thread's stack.
        static T Beneath<T>(int bytes, Func<T> work)
        {
            Span<byte> taken = stackalloc byte[bytes];
            taken[^1] = 1;
            var result = work();
            taken[0] = taken[^1];
            return result;
        }
        document.Diagnostics.Should().ContainSingle(entry => entry.Code.StartsWith("limit.nest", StringComparison.Ordinal))
            .Which.Message.Should().EndWith("The thread's stack had no room for more; read the document from a thread with a larger stack.");

        static string Read(PdfDocument document, string nesting) => nesting switch
        {
            "containers" => Depth(document.GetObject(new PdfObjectId(3))).ToString(CultureInfo.InvariantCulture),
            "dictionaries" => DictionaryDepth(document.GetObject(new PdfObjectId(3))).ToString(CultureInfo.InvariantCulture),
            _ => string.Create(CultureInfo.InvariantCulture, $"{document.GetObject(new PdfObjectId(3))}"),
        };

        static int DictionaryDepth(PdfObject? value) =>
            value is PdfDictionary dictionary ? 1 + DictionaryDepth(dictionary.GetRaw(PdfName.Get("A"))) : 0;
    }

    [Theory]
    [InlineData(5, 3, 3)]
    [InlineData(129, 200, 129)]
    public void A_member_of_an_object_stream_nests_its_containers_as_deep_as_MaxNestingDepth_lets_it(int levels, int bound, int read)
    {
        // Member 3 of object stream 10 nests arrays one inside the other, under a bound lowered below them or raised past the
        // default: the bound holds within one object whether the file writes it directly or an object stream holds it.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
            [10] = (1, file.ObjectStream(10, [(3, new string('[', levels) + new string(']', levels)), (4, "(four)")]), 0),
            [3] = (2, 10, 0),
            [4] = (2, 10, 1),
        };
        file.CrossReferenceStream(20, 21, rows, "/Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxNestingDepth = bound } });

        Depth(document.GetObject(new PdfObjectId(3))).Should().Be(read);

        document.Diagnostics.Contains(PdfDiagnosticCodes.LimitNestingDepth).Should().Be(levels > bound);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(0)]
    public void A_member_a_guard_cuts_keeps_the_nesting_cut_its_parse_met_before_the_guard_whatever_room_the_diagnostics_have(int capacity)
    {
        // Member 3 of object stream 10 nests an array past a MaxNestingDepth of 3, then runs on in a string past a
        // MaxObjectLength of 1 KB, which cuts it. Of what its parse reported, what it says of what the member holds stands,
        // a container the nesting cut among it, as a fault of the syntax does, while the rest goes with the guard's cut: the
        // nesting cut is reported, or, with no room for a report, still known to have been met.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
            [10] = (1, file.ObjectStream(10, [(3, "[[[[[1]]]] (" + new string('x', 2000) + ")"), (4, "(four)")]), 0),
            [3] = (2, 10, 0),
            [4] = (2, 10, 1),
        };
        file.CrossReferenceStream(20, 21, rows, "/Root 1 0 R");
        var options = new PdfReaderOptions
        {
            DiagnosticCapacity = capacity,
            Limits = PdfReaderLimits.Default with { MaxNestingDepth = 3, MaxObjectLength = 1024 },
        };
        using var document = PdfDocument.Open(file.Bytes, options);

        document.GetObject(new PdfObjectId(3));

        document.Reader.IsCutAtLimit(3).Should().BeTrue();
        document.Diagnostics.Contains(PdfDiagnosticCodes.LimitNestingDepth).Should().BeTrue();
    }

    [Fact]
    public void Each_container_nested_past_the_bound_in_one_object_is_reported_and_the_first_is_the_one_thrown()
    {
        // Object 3 holds an array, then a dictionary, each nesting past a bound of three: each container cut is reported where
        // it opens, the second's report worded in full as the first's is while the diagnostics have room for it; opened to
        // throw, the reader throws the report of the first.
        var file = new TestPdfBuilder().WithObject(1, Catalog).WithObject(2, Pages).WithObject(3, "[[[[[0]]]] [[<< /A 1 >>]]]").BuildClassic(rootNumber: 1);
        var limits = PdfReaderLimits.Default with { MaxNestingDepth = 3 };
        const string Remedy = " reads as null, and what it holds is not read. Raise PdfReaderLimits.MaxNestingDepth to read past it.";
        var first = OffsetOf(file, "[[[[0") + 2;

        using (var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits }))
        {
            Depth(document.GetObject(new PdfObjectId(3))).Should().Be(3);

            document.Diagnostics.Should().Equal(
                new PdfDiagnostic(PdfDiagnosticSeverity.Warning, PdfDiagnosticCodes.LimitNestingDepth, "An array nested inside 3 others" + Remedy, first),
                new PdfDiagnostic(PdfDiagnosticSeverity.Warning, PdfDiagnosticCodes.LimitNestingDepth, "A dictionary nested inside 3 others" + Remedy, OffsetOf(file, "<< /A 1")));
        }

        using var throwing = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits, ThrowOnLimit = true });
        var reading = () => throwing.GetObject(new PdfObjectId(3));

        var thrown = reading.Should().Throw<PdfLimitExceededException>().Which;
        thrown.Message.Should().StartWith("An array nested inside 3 others" + Remedy);
        thrown.Position.Should().Be(first);
    }

    [Fact]
    public void Wherever_the_thread_s_stack_cuts_a_read_a_thread_with_room_reads_it_whole_after()
    {
        // Stream 3 takes its /Length from object 7, which object stream 10 holds, and its data holds the keyword endstream, so
        // that a read whose /Length reads as null ends it there. Read on a small thread beneath calls that took 96 KB to 160 KB
        // of its stack, a step at a time, each read is cut at another place, however the thread's methods are compiled: by the
        // parser at a dictionary, or by the reader's probe at the load of 3, of 7, or of object stream 10 itself. What a cut
        // read gave is kept for that read alone: on a large thread after it, 3 reads whole; and where the reader's probe cut a
        // load, no object is left taken for one the reader cannot produce (#163).
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
            [3] = (1, file.Object(3, "<< /Length 7 0 R >>\nstream\nxx endstream yy\nendstream"), 0),
            [10] = (1, file.ObjectStream(10, [(7, "15")]), 0),
            [7] = (2, 10, 0),
        };
        file.CrossReferenceStream(20, 21, rows, "/Root 1 0 R");

        for (var taken = 96 * 1024; taken < 160 * 1024; taken += 128)
        {
            using var document = PdfDocument.Open(file.Bytes);

            Stacks.Run(Stacks.Small, () => Beneath(taken, () => document.GetObject(new PdfObjectId(3))));

            if (document.Diagnostics.Contains(PdfDiagnosticCodes.LimitNestedLoads))
            {
                document.Reader.GetPresence(7).Should().Be(ObjectPresence.Defined, $"the reader's probe cut a load beneath {taken} bytes");
            }

            Stacks.Run(Stacks.Large, () => document.GetObject(new PdfObjectId(3)).AsStream().Required().Decode().Length)
                .Should().Be(15, $"what the read beneath {taken} bytes gave was kept for that read alone");
        }

        // Runs work beneath a frame that takes bytes of the thread's stack.
        static T Beneath<T>(int bytes, Func<T> work)
        {
            Span<byte> taken = stackalloc byte[bytes];
            taken[^1] = 1;
            var result = work();
            taken[0] = taken[^1];
            return result;
        }
    }

    [Fact]
    public void Under_ThrowOnLimit_what_the_thread_s_stack_cut_throws_is_not_thrown_again_once_a_thread_has_room()
    {
        // Object 3, in the first of 60 object streams each taking its /Filter from an object the next holds, read under raised
        // bounds on a small thread, which its stack runs short of: the exception the stack's cut throws is kept by the loads
        // and the decodings it leaves for that read alone. Read again on a large thread, 3 reads whole (#163, #397).
        var file = HostileInputTests.ObjectStreamsTakingTheirFilterFromTheNext(60);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxNestingDepth = 1000, MaxNestedLoads = 1000 }, ThrowOnLimit = true };
        using var document = PdfDocument.Open(file, options);

        var small = Stacks.Run(Stacks.Small, () => ReadPastGuard(document, 3));
        var large = Stacks.Run(Stacks.Large, () => string.Create(CultureInfo.InvariantCulture, $"{document.GetObject(new PdfObjectId(3))}"));

        small!.Code.Should().StartWith("limit.nest");
        large.Should().Be("\"three\"");
    }

    [Fact]
    public void Wherever_the_thread_s_stack_cuts_a_decoding_two_loads_share_neither_load_keeps_it_past_the_read()
    {
        // Object stream 30, holding 3, takes its /Filter from 41 and its /DecodeParms from 42, which object stream 40 holds; 40
        // takes its /Length from 7, which object stream 10 holds, and its data holds the keyword endstream between 41 and 42,
        // so that a decoding whose /Length reads as null ends there, without 42. Read on a small thread beneath calls that took
        // 64 KB to 160 KB of its stack, a step at a time, 3 is cut at another place each time, the decoding of 40 for 41 among
        // them: what it gave, served to 42's load from the same depth, is kept for that read alone, as is what 42 read through
        // it. On a large thread after it, 42 reads whole (#163).
        const string Data = "41 0 42 25 /FlateDecode endstream x << /K 1 >> ";
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
            [40] = (1, file.Object(40, $"<< /Type /ObjStm /N 2 /First 11 /Length 7 0 R >>\nstream\n{Data}\nendstream"), 0),
            [41] = (2, 40, 0),
            [42] = (2, 40, 1),
            [10] = (1, file.ObjectStream(10, [(7, Data.Length.ToString(CultureInfo.InvariantCulture))]), 0),
            [7] = (2, 10, 0),
            [30] = (1, file.Object(30, "<< /Type /ObjStm /N 1 /First 4 /Length 10 /Filter 41 0 R /DecodeParms 42 0 R >>\nstream\n3 0 (three)\nendstream"), 0),
            [3] = (2, 30, 0),
        };
        file.CrossReferenceStream(50, 51, rows, "/Root 1 0 R");

        for (var taken = 64 * 1024; taken < 160 * 1024; taken += 128)
        {
            using var document = PdfDocument.Open(file.Bytes);

            Stacks.Run(Stacks.Small, () => Beneath(taken, () => document.GetObject(new PdfObjectId(3))));

            Stacks.Run(Stacks.Large, () => document.GetObject(new PdfObjectId(42)))
                .Should().BeOfType<PdfDictionary>($"what the read beneath {taken} bytes gave was kept for that read alone");
        }

        // Runs work beneath a frame that takes bytes of the thread's stack.
        static T Beneath<T>(int bytes, Func<T> work)
        {
            Span<byte> taken = stackalloc byte[bytes];
            taken[^1] = 1;
            var result = work();
            taken[0] = taken[^1];
            return result;
        }
    }

    [Fact]
    public void Under_ThrowOnLimit_wherever_the_reader_s_probe_cuts_a_load_for_want_of_stack_the_read_throws()
    {
        // The file of the test before, opened to throw on a guard: read on a small thread beneath calls that took 96 KB to
        // 160 KB of its stack, a step at a time, each read is cut at another place, the reader's probe cutting the load of 3,
        // of 7 or of object stream 10 at some of them. Each load the probe cuts throws, under MaxNestedLoads' code, and is
        // not reported in its stead.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
            [3] = (1, file.Object(3, "<< /Length 7 0 R >>\nstream\nxx endstream yy\nendstream"), 0),
            [10] = (1, file.ObjectStream(10, [(7, "15")]), 0),
            [7] = (2, 10, 0),
        };
        file.CrossReferenceStream(20, 21, rows, "/Root 1 0 R");
        var codes = new HashSet<string>();

        for (var taken = 96 * 1024; taken < 160 * 1024; taken += 128)
        {
            using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { ThrowOnLimit = true });

            var code = Stacks.Run(Stacks.Small, () => Beneath(taken, () => ReadPastGuard(document, 3)?.Code));

            if (code is not null)
            {
                codes.Add(code);
            }

            document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.LimitNestedLoads).Should().BeEmpty($"each load cut beneath {taken} bytes threw");
        }

        codes.Should().Contain(PdfDiagnosticCodes.LimitNestedLoads, "the reader's probe cut some reads");

        // Runs work beneath a frame that takes bytes of the thread's stack.
        static T Beneath<T>(int bytes, Func<T> work)
        {
            Span<byte> taken = stackalloc byte[bytes];
            taken[^1] = 1;
            var result = work();
            taken[0] = taken[^1];
            return result;
        }
    }

    [Fact]
    public void Under_ThrowOnLimit_a_guard_reached_while_a_rebuild_a_load_set_off_waits_throws_on_a_thread_of_little_stack()
    {
        // Streams 100 to 399 each take their /Length from the next, and 101's row leads nowhere: read on a small thread under
        // raised bounds, 100 rebuilds the index with 100 and 101 in flight, and the loads then go on until the thread's stack
        // runs short, whose cut throws. The exception leaves every load in flight, each keeping it as it passes, and the
        // outermost alone finishes the rebuild before it leaves: thrown once, not thrown again from each load on top of the
        // stack the others took, which would take more than the thread has (#397).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };

        for (var number = 100; number < 400; number++)
        {
            rows[number] = file.Object(number, string.Create(CultureInfo.InvariantCulture, $"<< /Length {number + 1} 0 R >>\nstream\nx\nendstream"));
        }

        rows[400] = file.Object(400, "1");
        rows[9] = file.Padding(9);
        rows[101] = rows[9] + 600;
        file.Table(401, rows, "/Size 401 /Root 1 0 R");
        var limits = PdfReaderLimits.Default with { MaxNestingDepth = 1000, MaxNestedLoads = 1000 };
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = limits, ThrowOnLimit = true });

        var code = Stacks.Run(Stacks.Small, () => ReadPastGuard(document, 100)?.Code);

        code.Should().StartWith("limit.nest");
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData("the thread's stack", Stacks.Small)]
    [InlineData("the loads' nesting", Stacks.Large)]
    [InlineData("a guard past the nesting's reach", Stacks.Large)]
    public void Under_ThrowOnLimit_a_guard_reached_at_the_deepest_load_throws_on_a_thread_of_little_stack(string reached, int stack)
    {
        // Object 3, in the first of 60 object streams each taking its /Filter from an object the next holds, is read on a
        // small thread under raised bounds, which its stack runs short of; in the first of 70, past the loads' nesting; or in
        // the first of 63, the last of which decodes past a MaxDecodedStreamLength of 1 MB. The exception leaves every load in flight, each
        // keeping it as it passes: thrown once, not thrown again from each load on top of the stack the others took, which
        // would take more than the thread has.
        var (file, limits, code) = reached switch
        {
            "the thread's stack" => (HostileInputTests.ObjectStreamsTakingTheirFilterFromTheNext(60), PdfReaderLimits.Default with { MaxNestingDepth = 1000, MaxNestedLoads = 1000 }, "limit.nest"),
            "the loads' nesting" => (HostileInputTests.ObjectStreamsTakingTheirFilterFromTheNext(70), PdfReaderLimits.Default, PdfDiagnosticCodes.LimitNestedLoads),
            _ => (HostileInputTests.ObjectStreamsTakingTheirFilterFromTheNext(63, 2 * 1024 * 1024), PdfReaderLimits.Default with { MaxDecodedStreamLength = 1024 * 1024 }, PdfDiagnosticCodes.LimitDecodedStream),
        };
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits, ThrowOnLimit = true });

        var thrown = Stacks.Run(stack, () =>
        {
            try
            {
                document.GetObject(new PdfObjectId(3));
                return null;
            }
            catch (PdfLimitExceededException exception)
            {
                return exception.Code;
            }
        });

        thrown.Should().StartWith(code);
    }

    [Theory]
    [InlineData("a table's trailer", 5, 3)]
    [InlineData("a table's trailer", 129, 200)]
    [InlineData("a cross-reference stream's dictionary", 5, 3)]
    [InlineData("a cross-reference stream's dictionary", 129, 200)]
    public void A_value_of_the_index_nests_as_deep_as_MaxNestingDepth_lets_it(string where, int levels, int bound)
    {
        // A table's trailer, or a cross-reference stream's dictionary, gives /Deep, arrays nested one inside the other, under a
        // bound lowered below them or raised past the default: the index is read under the document's bound, a value past it
        // read as null and reported, and the rest of the index read. Opened to throw, a value past the bound throws as the
        // document opens.
        var bytes = IndexGiving(where, new string('[', levels) + new string(']', levels));
        var limits = PdfReaderLimits.Default with { MaxNestingDepth = bound };

        using (var document = PdfDocument.Open(bytes, new PdfReaderOptions { Limits = limits }))
        {
            string.Create(CultureInfo.InvariantCulture, $"{document.GetObject(new PdfObjectId(3))}").Should().Be("\"three\"");
            document.Diagnostics.Select(entry => entry.Code).Should().Equal(levels > bound ? [PdfDiagnosticCodes.LimitNestingDepth] : []);
        }

        var opening = () => PdfDocument.Open(bytes, new PdfReaderOptions { Limits = limits, ThrowOnLimit = true }).Dispose();

        if (levels > bound)
        {
            opening.Should().Throw<PdfLimitExceededException>().Which.Code.Should().Be(PdfDiagnosticCodes.LimitNestingDepth);
        }
        else
        {
            opening.Should().NotThrow();
        }

        static byte[] IndexGiving(string where, string deep)
        {
            if (where == "a table's trailer")
            {
                var table = new HandWrittenFile();
                var offsets = new Dictionary<int, long> { [1] = table.Object(1, Catalog), [2] = table.Object(2, Pages), [3] = table.Object(3, "(three)") };
                table.Table(4, offsets, "/Size 4 /Root 1 0 R /Deep " + deep);
                return table.Bytes;
            }

            var file = new HandWrittenFile(version: "1.5");
            var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
            {
                [1] = (1, file.Object(1, Catalog), 0),
                [2] = (1, file.Object(2, Pages), 0),
                [3] = (1, file.Object(3, "(three)"), 0),
            };
            file.CrossReferenceStream(4, 5, rows, "/Root 1 0 R /Deep " + deep);
            return file.Bytes;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_trailer_value_nested_past_the_bound_reads_as_null_and_the_chain_goes_on(bool crossesWindow)
    {
        // An update's trailer gives a value nested 129 arrays deep beside its /Prev; with the update's table long enough,
        // the trailer runs past the table's window and is read on its own. The cut container reads as null, and what the
        // trailer gives past it, its /Prev to the older revision holding the catalog and object 3 included, is read.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages), [3] = file.Object(3, "(three)") };
        var first = file.Table(4, rows, "/Size 4 /Root 1 0 R");
        var five = file.Object(5, "(five)");
        var xref = file.Position;
        var count = crossesWindow ? (65536 - 120) / 20 : 1;
        var table = new StringBuilder("xref\n0 1\n0000000000 65535 f \n");
        table.Append(CultureInfo.InvariantCulture, $"5 {count}\n{five:D10} 00000 n \n");

        for (var row = 1; row < count; row++)
        {
            table.Append("0000000000 00000 f \n");
        }

        table.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {5 + count} /Root 1 0 R /Prev {first} /Deep {new string('[', 129)}{new string(']', 129)} >>\nstartxref\n{xref}\n%%EOF\n");
        file.Raw(table.ToString());
        using var document = PdfDocument.Open(file.Bytes);

        string.Create(CultureInfo.InvariantCulture, $"{document.GetObject(new PdfObjectId(3))}").Should().Be("\"three\"");
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitNestingDepth);
    }

    [Theory]
    [InlineData("a stale row's object")]
    [InlineData("a member written as a stream")]
    public void Under_ThrowOnLimit_a_nesting_cut_in_a_parse_the_reader_drops_throws_nothing(string shape)
    {
        // Row 3 names object 4, 23 bytes past object 3, nested 200 arrays deep and named by nothing; or member 3 of an object
        // stream is written as a stream whose dictionary nests 129 arrays deep. The reader drops either parse: nothing is
        // reached, and nothing thrown, as in a document opened not to throw.
        byte[] bytes;

        if (shape == "a stale row's object")
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
            file.Object(3, "(three)");
            rows[3] = rows[4] = file.Object(4, new string('[', 200) + new string(']', 200));
            file.Table(5, rows, "/Size 5 /Root 1 0 R");
            bytes = file.Bytes;
        }
        else
        {
            var file = new HandWrittenFile(version: "1.5");
            var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
            {
                [1] = (1, file.Object(1, Catalog), 0),
                [2] = (1, file.Object(2, Pages), 0),
                [10] = (1, file.ObjectStream(10, [(3, "<< /A " + new string('[', 129) + new string(']', 129) + " /Length 3 >> stream\nabc\nendstream"), (4, "(four)")]), 0),
                [3] = (2, 10, 0),
                [4] = (2, 10, 1),
            };
            file.CrossReferenceStream(20, 21, rows, "/Root 1 0 R");
            bytes = file.Bytes;
        }

        string Read(bool throwOnLimit)
        {
            using var document = PdfDocument.Open(bytes, new PdfReaderOptions { ThrowOnLimit = throwOnLimit });
            return string.Create(CultureInfo.InvariantCulture, $"{document.GetObject(new PdfObjectId(3))}");
        }

        Read(throwOnLimit: true).Should().Be(Read(throwOnLimit: false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_object_whose_containers_nest_past_MaxNestingDepth_is_one_a_guard_cut(bool inObjectStream)
    {
        // Object 3 nests 129 arrays, written directly or held by object stream 10: the array past the bound reads as null, and
        // the object is one a guard cut, as one cut at MaxObjectLength is: what it holds past the cut is the guard's, not the
        // file's (#163).
        var nested = new string('[', 129) + new string(']', 129);
        byte[] bytes;

        if (inObjectStream)
        {
            var file = new HandWrittenFile(version: "1.5");
            var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
            {
                [1] = (1, file.Object(1, Catalog), 0),
                [2] = (1, file.Object(2, Pages), 0),
                [10] = (1, file.ObjectStream(10, [(3, nested)]), 0),
                [3] = (2, 10, 0),
            };
            file.CrossReferenceStream(20, 21, rows, "/Root 1 0 R");
            bytes = file.Bytes;
        }
        else
        {
            bytes = new TestPdfBuilder().WithObject(1, Catalog).WithObject(2, Pages).WithObject(3, nested).BuildClassic(rootNumber: 1);
        }

        using var document = PdfDocument.Open(bytes);

        Depth(document.GetObject(new PdfObjectId(3))).Should().Be(128);
        document.Reader.IsCutAtLimit(3).Should().BeTrue();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitNestingDepth);
    }

    [Fact]
    public void A_nesting_cut_in_a_parse_that_reads_no_numbered_object_makes_no_object_one_a_guard_cut()
    {
        // The cross-reference stream's dictionary nests 129 arrays deep, in a parse that reads no numbered object: the cut is
        // noted of none, not of 20, the stream's number, which a load of 20 would note (#175), nor of 0. Object stream 10
        // takes its /N from object 0, the head of the free list, which reads as null: the stream is unreadable for the
        // file's fault, and reported so, not taken for one a guard hid.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
            [10] = (1, file.Object(10, "<< /Type /ObjStm /N 0 0 R /First 4 /Length 10 >>\nstream\n4 0 (four)\nendstream"), 0),
            [4] = (2, 10, 0),
        };
        file.CrossReferenceStream(20, 21, rows, "/Root 1 0 R /Deep " + new string('[', 129) + new string(']', 129));
        using var document = PdfDocument.Open(file.Bytes);

        document.Reader.IsCutAtLimit(20).Should().BeFalse();
        document.GetObject(new PdfObjectId(4)).Should().Be(PdfNull.Instance);
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitNestingDepth, PdfDiagnosticCodes.ObjectStreamUnreadable);
    }

    [Fact]
    public void A_decoding_the_loads_nesting_cut_that_gives_no_contents_weighs_an_entry_against_the_object_streams_budget()
    {
        // Object 3, in the first of 70 object streams each taking its /Filter from an object the next holds, is read past the
        // loads' nesting: the 63 decodings that reach the cut give no contents, and each is kept for the depth it was made
        // from, weighing 256 bytes against the 32 MB the reader keeps of object streams, which thus bounds how many are kept
        // (#163).
        using var document = PdfDocument.Open(HostileInputTests.ObjectStreamsTakingTheirFilterFromTheNext(70));

        document.GetObject(new PdfObjectId(3)).Should().Be(PdfNull.Instance);
        document.Reader.ObjectStreamBytes.Should().Be(63 * 256);
    }

    [Fact]
    public void Under_ThrowOnLimit_every_load_an_exception_passes_through_keeps_it()
    {
        // Stream 6 takes its /Length from object 5, written directly past MaxObjectLength. Opened to throw, a read of 6 throws
        // what the load of 5 threw, and both keep it: read again, 6 throws without reading (#397).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        rows[5] = file.Object(5, "(" + new string('x', 2000) + ")");
        rows[6] = file.Object(6, "<< /Length 5 0 R >>\nstream\nabc\nendstream");
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        using var source = new OffsetCountingSource(file.Bytes);
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } };
        using var document = PdfDocument.Open(source, options, ownsSource: false);

        var first = ReadPastGuard(document, 6);
        var read = source.BytesRead;
        var again = ReadPastGuard(document, 6);

        first.Should().NotBeNull();
        again.Should().BeSameAs(first);
        ReadPastGuard(document, 5).Should().BeSameAs(first);
        source.BytesRead.Should().Be(read);
    }

    [Fact]
    public void Under_ThrowOnLimit_an_exception_kept_from_a_shallow_read_is_not_thrown_again_where_the_loads_nesting_cuts_the_read_first()
    {
        // Stream 6 takes its /Length from object 5, written past MaxObjectLength; streams 100 onward, 63 of them, each take
        // theirs from the next, the last from 6. Read first, 6 throws MaxObjectLength's exception, kept with how deep its read
        // went. Read through the chain, 6 is the 64th load, from which its read meets 5 past the loads' nesting: the kept
        // exception serves no read from there, and the read throws the nesting's (#163, #397).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        rows[5] = file.Object(5, "(" + new string('x', 2000) + ")");
        rows[6] = file.Object(6, "<< /Length 5 0 R >>\nstream\nabc\nendstream");

        for (var number = 100; number < 163; number++)
        {
            rows[number] = file.Object(number, string.Create(CultureInfo.InvariantCulture, $"<< /Length {(number == 162 ? 6 : number + 1)} 0 R >>\nstream\nx\nendstream"));
        }

        file.Table(163, rows, "/Size 163 /Root 1 0 R");
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        ReadPastGuard(document, 6)!.Code.Should().Be(PdfDiagnosticCodes.LimitObject);
        ReadPastGuard(document, 100)!.Code.Should().Be(PdfDiagnosticCodes.LimitNestedLoads);
    }

    [Fact]
    public void Under_ThrowOnLimit_the_loads_an_exception_kept_for_a_cut_read_is_thrown_through_keep_it_for_their_own_depths_alone()
    {
        // Two chains of 63 streams, 100 onward and 200 onward, each taking its /Length from the next, the last of each from
        // stream 6, which takes its own from object 5: read through either chain, 6 is the 64th load and 5 the 65th, which the
        // loads' nesting cuts. Read through the first, 6 keeps the nesting's exception for that depth; read through the
        // second, 6 throws it again, and the loads it passes through keep it as one the nesting cut, for their own depths
        // alone: read from the top, stream 262 reads whole (#163, #397).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        rows[5] = file.Object(5, "3");
        rows[6] = file.Object(6, "<< /Length 5 0 R >>\nstream\nabc\nendstream");

        foreach (var head in (int[])[100, 200])
        {
            for (var number = head; number < head + 63; number++)
            {
                rows[number] = file.Object(number, string.Create(CultureInfo.InvariantCulture, $"<< /Length {(number == head + 62 ? 6 : number + 1)} 0 R >>\nstream\nx\nendstream"));
            }
        }

        file.Table(263, rows, "/Size 263 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { ThrowOnLimit = true });

        ReadPastGuard(document, 100)!.Code.Should().Be(PdfDiagnosticCodes.LimitNestedLoads);
        ReadPastGuard(document, 200)!.Code.Should().Be(PdfDiagnosticCodes.LimitNestedLoads);
        document.GetObject(new PdfObjectId(262)).Should().BeOfType<PdfStream>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Under_ThrowOnLimit_an_exception_is_kept_with_how_deep_every_load_before_it_went(bool chainReadBefore)
    {
        // Object 4's object stream, 30, takes its /Length from stream 10, which heads a chain of ten, and its /Filter from object
        // 9, written past MaxObjectLength: read first, 4 reads the chain, ten levels deep — loaded, or served from the cache
        // where it was read before —, then 9, which throws. The exception is kept with how deep the read went before it, the
        // chain included, not only how deep the load that threw went: read through streams 100 onward, 53 of them, 4 is the
        // 54th load, from which its read would take the chain past the loads' nesting, and the read throws the nesting's
        // (#163, #397).
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
            [9] = (1, file.Object(9, "(" + new string('x', 2000) + ")"), 0),
            [19] = (1, file.Object(19, "<< /Length 1 >>\nstream\nx\nendstream"), 0),
            [30] = (1, file.Object(30, "<< /Type /ObjStm /N 1 /First 4 /Length 10 0 R /Filter 9 0 R >>\nstream\n4 0 (four)\nendstream"), 0),
            [4] = (2, 30, 0),
        };

        for (var number = 10; number < 19; number++)
        {
            rows[number] = (1, file.Object(number, string.Create(CultureInfo.InvariantCulture, $"<< /Length {number + 1} 0 R >>\nstream\nx\nendstream")), 0);
        }

        for (var number = 100; number < 153; number++)
        {
            rows[number] = (1, file.Object(number, string.Create(CultureInfo.InvariantCulture, $"<< /Length {(number == 152 ? 4 : number + 1)} 0 R >>\nstream\nx\nendstream")), 0);
        }

        file.CrossReferenceStream(200, 201, rows, "/Root 1 0 R");
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        if (chainReadBefore)
        {
            document.GetObject(new PdfObjectId(10)).Should().BeOfType<PdfStream>();
        }

        ReadPastGuard(document, 4)!.Code.Should().Be(PdfDiagnosticCodes.LimitObject);
        ReadPastGuard(document, 100)!.Code.Should().Be(PdfDiagnosticCodes.LimitNestedLoads);
    }

    [Fact]
    public void A_nesting_cut_in_a_definition_of_an_object_the_reader_no_longer_serves_makes_the_object_no_guard_s_cut()
    {
        // The chain's index places object stream 10, whose dictionary nests 129 arrays deep, and object 12 nowhere: reading 12
        // rebuilds the index, which serves the definition of 10 written last, << /K 1 >>. The validation reads the chain's 10,
        // whose nesting is cut; the cut is that definition's, not the object the reader serves, which no guard cut (#255).
        var deep = new string('[', 129) + new string(']', 129);
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
            [10] = (1, file.ObjectStream(10, [(11, "(eleven)")], "/Deep " + deep + " "), 0),
            [11] = (2, 10, 0),
        };
        rows[12] = (1, file.Padding(9) + 600, 0);
        file.CrossReferenceStream(20, 21, rows, "/Root 1 0 R");
        file.Object(10, "<< /K 1 >>");
        using var document = PdfDocument.Open(file.Bytes);

        document.GetObject(new PdfObjectId(12)).Should().Be(PdfNull.Instance);
        new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(10)).Should().BeOfType<PdfDictionary>();
        document.Reader.IsCutAtLimit(10).Should().BeFalse();
    }

    [Fact]
    public void Under_ThrowOnLimit_an_object_stream_whose_own_load_threw_throws_again_for_another_of_its_objects_after_the_loads_let_go_of_it()
    {
        // Object stream 30 takes its /Length from object 5, written past MaxObjectLength: read first, its object 3 throws from
        // the stream's own load, which its decoding keeps as it keeps its own exceptions (#397). Seventy objects past the
        // bound read after it throw too, and the loads let go of what they kept of 5 and 30, a cache of 64 objects holding
        // no more: object 4, which 30 holds, throws again from the decoding's, without reading the stream again.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
            [5] = (1, file.Object(5, "(" + new string('x', 2000) + ")"), 0),
            [30] = (1, file.Object(30, "<< /Type /ObjStm /N 2 /First 8 /Length 5 0 R >>\nstream\n3 0 4 4 (3) (4)\nendstream"), 0),
            [3] = (2, 30, 0),
            [4] = (2, 30, 1),
        };

        for (var number = 100; number < 170; number++)
        {
            rows[number] = (1, file.Object(number, "(" + new string('x', 2000) + ")"), 0);
        }

        file.CrossReferenceStream(200, 201, rows, "/Root 1 0 R");
        using var source = new OffsetCountingSource(file.Bytes);
        var options = new PdfReaderOptions { ThrowOnLimit = true, ObjectCacheCapacity = 64, Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } };
        using var document = PdfDocument.Open(source, options, ownsSource: false);

        ReadPastGuard(document, 3)!.Code.Should().Be(PdfDiagnosticCodes.LimitObject);

        for (var number = 100; number < 170; number++)
        {
            ReadPastGuard(document, number).Should().NotBeNull();
        }

        var read = source.BytesRead;
        ReadPastGuard(document, 4)!.Code.Should().Be(PdfDiagnosticCodes.LimitObject);
        source.BytesRead.Should().Be(read, "the decoding's exception is thrown again without loading the stream");
    }

    [Fact]
    public void An_object_stream_without_an_N_is_broken_whatever_guard_cut_its_First()
    {
        // Object stream 10 gives no /N, and takes its /First from object 5, written past MaxObjectLength: the stream cannot
        // serve its objects for the file's fault, whatever the guard hid of /First, and the validator finds it broken (#395).
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
            [5] = (1, file.Object(5, new string(' ', 2000) + "8"), 0),
            [4] = (2, 10, 0),
            [6] = (2, 10, 1),
        };
        const string Data = "4 0 6 6 (four) (six) ";
        rows[10] = (1, file.Object(10, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /First 5 0 R /Length {Data.Length} >>\nstream\n{Data}\nendstream")), 0);
        file.CompressedCrossReferenceStream(20, 21, number => rows.TryGetValue(number, out var row) ? row : (0, 0, 0), "/Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } });

        var report = new PdfValidator().Validate(document);

        report.Contains(PdfValidationRuleIds.XRefObjectStreamBroken).Should().BeTrue();
    }

    [Fact]
    public void Under_ThrowOnLimit_a_lookup_a_full_index_lacks_throws_anew_each_time()
    {
        // The classic table places object 3 on its own xref keyword: a read of 3 rebuilds the index, which a bound of 40 entries
        // leaves without the 50 objects 3,000,000 and on. Opened to throw, a lookup of an object the full index lacks throws
        // each time, reading nothing, and keeps nothing of what it threw (#181).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };

        for (var k = 0; k < 50; k++)
        {
            file.Object(3_000_000 + k, "(filler)");
        }

        rows[3] = file.Position;
        file.Table(4, rows, "/Size 4 /Root 1 0 R");
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxIndexEntries = 40 } };
        using var document = PdfDocument.Open(file.Bytes, options);
        ReadPastGuard(document, 3).Should().NotBeNull();

        var first = ReadPastGuard(document, 5_000_000);
        var again = ReadPastGuard(document, 5_000_000);

        first.Should().NotBeNull().And.Match<PdfLimitExceededException>(thrown => thrown.Code == PdfDiagnosticCodes.LimitIndexEntries);
        again.Should().NotBeNull().And.NotBeSameAs(first);
    }

    [Fact]
    public void Under_ThrowOnLimit_a_lookup_an_object_stream_s_decoding_makes_throws_anew_at_each_decoding()
    {
        // A read of object 50, whose entry points into the spaces of object 10's data, rebuilds the index, and the rebuild's
        // scan reaches MaxRebuildReadPasses. Object stream 60 gives its /N as object 77, which no index holds: decoding it, the
        // rebuild's expansion throws the guard at the lookup, and keeps from the chain's index 60's member 61. Opened to
        // throw, each read of 61 decodes 60 again and throws anew at the lookup, which no decoding keeps (#181).
        var file = LookupInAnObjectStreamDecoding();
        using var document = PdfDocument.Open(file, new PdfReaderOptions { ThrowOnLimit = true });
        ReadPastGuard(document, 50).Should().NotBeNull().And.Match<PdfLimitExceededException>(thrown => thrown.Code == PdfDiagnosticCodes.LimitRebuildReads);

        var first = ReadPastGuard(document, 61);
        var again = ReadPastGuard(document, 61);

        document.WasRepaired.Should().BeTrue();
        first.Should().NotBeNull().And.Match<PdfLimitExceededException>(thrown => thrown.Code == PdfDiagnosticCodes.LimitRebuildReads);
        again.Should().NotBeNull().And.NotBeSameAs(first);
    }

    [Fact]
    public void Disposing_lets_go_of_what_reads_the_loads_nesting_cut_kept_though_a_reference_taken_from_the_document_is_held()
    {
        // Stream 100 heads a chain of 70 streams, each taking its /Length from the next: read past the loads' nesting, what the
        // read gave of it is kept apart, for the depth it was read from (#163); the document disposed of, a reference taken
        // from it still held, that is let go of with all the document kept.
        var (held, cut) = ReadPastTheNestingAndDispose();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        cut.IsAlive.Should().BeFalse("what cut reads kept is let go of as the document is disposed");
        GC.KeepAlive(held);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (PdfObject Held, WeakReference Cut) ReadPastTheNestingAndDispose()
    {
        var builder = new TestPdfBuilder().WithObject(1, Catalog).WithObject(2, Pages);

        for (var number = 100; number < 170; number++)
        {
            builder.WithObject(number, string.Create(CultureInfo.InvariantCulture, $"<< /Length {(number == 169 ? "1" : string.Create(CultureInfo.InvariantCulture, $"{number + 1} 0 R"))} >>\nstream\nx\nendstream"));
        }

        var document = PdfDocument.Open(builder.BuildClassic(rootNumber: 1));
        var cut = document.GetObject(new PdfObjectId(100));
        ReferenceEquals(document.GetObject(new PdfObjectId(100)), cut).Should().BeTrue("the read the nesting cut is kept for its depth");
        var held = document.Trailer.GetRaw(PdfName.Root)!;

        document.Dispose();
        return (held, new WeakReference(cut));
    }

    [Fact]
    public void Under_ThrowOnLimit_disposing_lets_go_of_the_exceptions_the_loads_kept_though_a_reference_taken_from_the_document_is_held()
    {
        // Object 5 runs past MaxObjectLength. Opened to throw, a read of 5 throws, and its load keeps the exception for the
        // next read to throw again without reading (#181); the document disposed of, a reference taken from it still held,
        // the exception is let go of with all the document kept.
        var (held, thrown, kept) = ThrowKeepAndDispose();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        kept.Should().BeTrue("the load kept what it threw");
        thrown.IsAlive.Should().BeFalse("the exceptions the loads kept are let go of as the document is disposed");
        GC.KeepAlive(held);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (PdfObject Held, WeakReference Thrown, bool Kept) ThrowKeepAndDispose()
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        rows[5] = file.Object(5, "(" + new string('x', 2000) + ")");
        file.Table(6, rows, "/Size 6 /Root 1 0 R");
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } };
        var document = PdfDocument.Open(file.Bytes, options);
        var thrown = ReadPastGuard(document, 5);
        var kept = thrown is not null && ReferenceEquals(ReadPastGuard(document, 5), thrown);
        var held = document.Trailer.GetRaw(PdfName.Root)!;

        document.Dispose();
        return (held, new WeakReference(thrown), kept);
    }

    /// <summary>
    /// Writes a file whose row for object 50 points into the spaces of object 10's data, an attached file whose bytes spell
    /// streams (<see cref="SpelledStreamsData"/>), as <see cref="LengthInAMemberTakenInLate"/> does, and object stream 60,
    /// whose /N names object 77, which no index holds, its member 61 placed there by the chain's index, a cross-reference
    /// stream.
    /// </summary>
    private static byte[] LookupInAnObjectStreamDecoding()
    {
        var data = SpelledStreamsData();
        const string Members = "61 0 (sixty-one) ";
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
            [10] = (1, file.Object(10, "<< /Type /EmbeddedFile /Length 11 0 R >>\nstream\n" + data + "endstream"), 0),
            [11] = (1, file.Object(11, LengthNote + data.Length.ToString(CultureInfo.InvariantCulture)), 0),
            [60] = (1, file.Object(60, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 77 0 R /First 5 /Length {Members.Length} >>\nstream\n{Members}\nendstream")), 0),
            [61] = (2, 60, 0),
        };
        rows[50] = (1, rows[10].Field2 + 2000, 0);
        file.CrossReferenceStream(99, 100, rows, "/Root 1 0 R");
        return file.Bytes;
    }

    [Fact]
    public void Under_ThrowOnLimit_a_rebuild_lets_go_of_the_exceptions_the_loads_kept_and_the_object_is_read_from_the_rebuilt_index()
    {
        // Object 5, as the table places it, runs past MaxObjectLength; the file defines it again past the table, within the
        // bound. The table lacks object 31, and its /Prev names no section. Opened to throw, a read of 5 throws, and keeps the
        // exception; a lookup of 31 rebuilds the index, which takes the later definition, and lets go of what the loads kept:
        // object 5 reads as the rebuilt index has it (#181).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        rows[5] = file.Object(5, "(" + new string('x', 2000) + ")");
        file.Object(5, "(five)");
        file.Table(6, rows, "/Size 6 /Root 1 0 R /Prev 1 0 R");
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        ReadPastGuard(document, 5).Should().NotBeNull();
        _ = ReadPastGuard(document, 31);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(5)).AsText().Should().Be("five");
    }

    [Fact]
    public void Under_ThrowOnLimit_a_rebuild_whose_object_streams_replace_a_definition_reads_again_each_object_whose_load_threw_as_they_were_told_apart()
    {
        // Objects 5 and 6, written directly, run past MaxObjectLength, and object stream 10, written after them, defines 5
        // again. The table lacks object 31, and its /Prev names no section. Opened to throw, a lookup of 31 rebuilds the index,
        // whose expansion loads every object to tell the object streams apart: 5 and 6 each throw, and keep the exception. A
        // definition replaced, what was read is read again from the final index, and so are the exceptions the loads kept let
        // go of: read after the rebuild, object 6 is read again (#181).
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        file.Object(5, "(" + new string('x', 2000) + ")");
        var six = file.Object(6, "(" + new string('y', 2000) + ")");
        file.ObjectStream(10, [(5, "(five)")]);
        file.Table(3, rows, "/Size 3 /Root 1 0 R /Prev 1 0 R");
        using var source = new OffsetCountingSource(file.Bytes);
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } };
        using var document = PdfDocument.Open(source, options, ownsSource: false);

        ReadPastGuard(document, 31).Should().NotBeNull();
        var read = source.Reads.Where(entry => entry.Key >= six - 1 && entry.Key < six + 2000).Sum(entry => entry.Value);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(5)).AsText().Should().Be("five");
        ReadPastGuard(document, 6).Should().NotBeNull();
        source.Reads.Where(entry => entry.Key >= six - 1 && entry.Key < six + 2000).Sum(entry => entry.Value).Should().BeGreaterThan(read);
    }

    [Fact]
    public void Under_ThrowOnLimit_an_object_stream_taken_in_after_one_that_defines_an_object_again_reads_that_object_as_the_stream_defines_it()
    {
        // Object stream 20 takes its /N from object 5, written directly after it, past MaxObjectLength, and defined again, as
        // 1, by object stream 10, written last. Opened to throw, a lookup of object 31, which the table lacks, rebuilds the
        // index: telling the object streams apart, the expansion loads 5, which throws, and keeps the exception. Stream 10,
        // taken in first, defines 5 again, which lets go of that exception: stream 20, taken in next, reads its /N from 10,
        // and object 21, which it holds, is in the rebuilt index (#181).
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        file.Object(20, "<< /Type /ObjStm /N 5 0 R /First 5 /Length 17 >>\nstream\n21 0 (twenty-one)\nendstream");
        file.Object(5, "(" + new string('x', 2000) + ")");
        file.ObjectStream(10, [(5, "1")]);
        file.Table(3, rows, "/Size 3 /Root 1 0 R /Prev 1 0 R");
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        ReadPastGuard(document, 31).Should().NotBeNull();

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(21)).AsText().Should().Be("twenty-one");
    }

    [Fact]
    public void Under_ThrowOnLimit_the_exceptions_an_expansion_keeps_once_it_let_one_go_are_as_many_as_the_reader_caches_objects()
    {
        // Object 5 and the 63 objects 100 to 162, written directly, run past MaxObjectLength; object streams 20 and 22 take
        // their /N from objects 7 and 8, members of object stream 10, written last, which run past it too, and 10 defines 5
        // again. Opened to throw through a cache of 64 objects, a lookup of object 31, which the table lacks, rebuilds the
        // index: the expansion loads each object written directly, and keeps 64 exceptions; stream 10, taken in first, lets go
        // of the one 5 kept; streams 22 and 20, taken in next, each read their /N, which throws, and is kept, the oldest let
        // go of first. The rebuild throws the first guard it reached, and none of 21 and 23, which the streams hold, is read
        // (#181).
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        file.Object(5, "(" + new string('x', 2000) + ")");

        for (var number = 100; number < 163; number++)
        {
            file.Object(number, "(" + new string('x', 2000) + ")");
        }

        file.Object(20, "<< /Type /ObjStm /N 7 0 R /First 5 /Length 17 >>\nstream\n21 0 (twenty-one)\nendstream");
        file.Object(22, "<< /Type /ObjStm /N 8 0 R /First 5 /Length 19 >>\nstream\n23 0 (twenty-three)\nendstream");
        file.ObjectStream(10, [(5, "(five)"), (7, "(" + new string('y', 2000) + ")"), (8, "(" + new string('z', 2000) + ")")]);
        file.Table(3, rows, "/Size 3 /Root 1 0 R /Prev 1 0 R");
        var options = new PdfReaderOptions { ThrowOnLimit = true, ObjectCacheCapacity = 64, Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        ReadPastGuard(document, 31).Should().NotBeNull();

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(5)).AsText().Should().Be("five");
        document.GetObject(new PdfObjectId(21)).Should().Be(PdfNull.Instance);
        document.GetObject(new PdfObjectId(23)).Should().Be(PdfNull.Instance);
    }

    [Fact]
    public void Under_ThrowOnLimit_a_rebuild_lets_go_of_the_exceptions_object_stream_decodings_kept_and_a_member_is_read_through_the_rebuilt_index()
    {
        // Object stream 10 takes its /Filter from object 4, which the cross-reference stream places where it names two
        // filters, past a MaxFilterChainLength of 1; the file defines 4 again after it as the one filter the data is written
        // with. Opened to throw, a read of object 11 decodes stream 10, which throws, and keeps the exception. The stream's
        // /Prev names no section, and a lookup of object 500, which the index lacks, rebuilds it: the later definition of 4 is
        // taken, the stream decoded whole, and what the decodings kept let go of. Object stream 13, of 17 MB too, decoded for
        // object 14, lets go of stream 10, and object 12, which 10 holds, reads whole (#181).
        var data = Encoding.Latin1.GetBytes("11 0 12 9 ".PadRight(ObjectStreamDecoded) + "(eleven) (twelve) ");
        var compressed = HostileInputTests.Compress(data);
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
            [4] = (1, file.Object(4, "[/ASCIIHexDecode /ASCIIHexDecode]"), 0),
            [10] = (1, file.Object(10, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 2 /First {ObjectStreamDecoded} /Filter 4 0 R /Length {compressed.Length} >>\nstream\n{Encoding.Latin1.GetString(compressed)}\nendstream")), 0),
            [11] = (2, 10, 0),
            [12] = (2, 10, 1),
            [13] = (1, file.PaddedObjectStream(13, [(14, "<< >>")], ObjectStreamDecoded), 0),
            [14] = (2, 13, 0),
        };
        file.Object(4, "/FlateDecode");
        file.CompressedCrossReferenceStream(20, 21, number => rows.TryGetValue(number, out var row) ? row : (0, 0, 0), "/Root 1 0 R /Prev 1 0 R");
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxFilterChainLength = 1 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        ReadPastGuard(document, 11).Should().NotBeNull().And.Match<PdfLimitExceededException>(thrown => thrown.Code == PdfDiagnosticCodes.LimitFilterChain);
        _ = ReadPastGuard(document, 500);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(14)).Should().BeOfType<PdfDictionary>();
        document.GetObject(new PdfObjectId(12)).AsText().Should().Be("twelve");
    }

    [Fact]
    public void Under_ThrowOnLimit_an_object_stream_whose_decoding_reached_a_guard_as_a_rebuild_took_the_streams_in_is_decoded_again_when_read()
    {
        // Object stream 5's /Filter is object 7, which holds a reference to object 81, /RunLengthDecode, which object stream
        // 4, written before 5, holds; object stream 6, written after them, takes its /Length from object 90, which 5 holds,
        // and the table's /Prev names no section. Opened to throw, a lookup of object 5000 rebuilds the index, which takes the
        // streams in from the last written: 5 is decoded before 4 is taken in, 81 reading as null for the time being, and its
        // data, read as it is, holds 40 and 90; once 4 is, 6 is loaded again, which decodes 5 again through its /Filter, the
        // runs of object 40 past MaxDecodedStreamLength: the guard throws. What a decoding throws while the rebuild takes the
        // object streams in is not kept: read after it, object 40 decodes 5 again, and throws anew (#181).
        var file = FilterTakenInLateFile();
        var at = OffsetOf(file, "5 0 obj");
        var dataStart = at + file.AsSpan(at).IndexOf("stream\n"u8) + "stream\n".Length;
        using var source = new OffsetCountingSource(file);
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 4096 } };
        using var document = PdfDocument.Open(source, options, ownsSource: false);
        var rebuilding = ReadPastGuard(document, 5000);
        var decoded = source.Reads.GetValueOrDefault(dataStart);

        var reading = ReadPastGuard(document, 40);

        document.WasRepaired.Should().BeTrue();
        rebuilding.Should().NotBeNull();
        rebuilding!.Code.Should().Be(PdfDiagnosticCodes.LimitDecodedStream);
        reading.Should().NotBeNull().And.NotBeSameAs(rebuilding);
        reading!.Code.Should().Be(PdfDiagnosticCodes.LimitDecodedStream);
        decoded.Should().Be(2, "the rebuild decodes stream 5 as it takes it in, and again as it takes 6 in");
        source.Reads.GetValueOrDefault(dataStart).Should().Be(3);
    }

    /// <summary>
    /// Writes the file of <see cref="Under_ThrowOnLimit_an_object_stream_whose_decoding_reached_a_guard_as_a_rebuild_took_the_streams_in_is_decoded_again_when_read"/>:
    /// object 40, in object stream 5, is a string of 2,000 bytes of 0x81, each pair of which a run-length decoding repeats
    /// 128 times.
    /// </summary>
    private static byte[] FilterTakenInLateFile()
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, Catalog),
            [2] = file.Object(2, Pages),
        };
        file.Object(7, "81 0 R");
        file.ObjectStream(4, [(81, "/RunLengthDecode")]);
        file.ObjectStream(5, [(40, "(" + new string('\u0081', 2000) + ")"), (90, "9")], "/Filter 7 0 R ");
        file.Object(6, "<< /Type /ObjStm /N 1 /First 6 /Length 90 0 R >>\nstream\n100 0 (x)\nendstream");
        file.Table(3, rows, "/Size 3 /Root 1 0 R /Prev 1 0 R");
        return file.Bytes;
    }

    [Fact]
    public void A_rebuild_within_a_walk_lets_go_of_what_the_walk_decoded_cut_and_kept_and_the_walk_reads_through_the_rebuilt_index()
    {
        // A walk reads objects 10 to 33 in turn, decoding their object streams again once at most: object 28 reads as null, the
        // guard's cut, and stream 300, whose /Length it is, is read up to the first endstream its data spells, and kept for the
        // walk. The trailer's /Prev names no section, and a lookup of object 500, which the index lacks, rebuilds it within the
        // walk: what the walk decoded, cut and kept is let go of with what the reader read. Read through the rebuilt index, 28
        // is no object a guard cut, and object 11 is read whole, stream 5 decoded as the walk's first decoding of it there;
        // the cache keeps what the rebuild read of 300 past the walk (#181).
        var file = StreamsThroughALengthCutFile(1, "/Prev 1 0 R");
        using var source = new OffsetCountingSource(file);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 } };
        using var document = PdfDocument.Open(source, options, ownsSource: false);
        bool cutBefore, cutAfter;
        PdfObject member;
        document.Reader.BeginWalk("validation");

        try
        {
            for (var number = 10; number < 34; number++)
            {
                _ = document.GetObject(new PdfObjectId(number));
            }

            Encoding.Latin1.GetString(document.GetObject(new PdfObjectId(300)).AsStream().Required().Decode().Span).Should().Be("01");
            cutBefore = document.Reader.IsCutAtLimit(28);
            document.GetObject(new PdfObjectId(500)).Should().Be(PdfNull.Instance);
            cutAfter = document.Reader.IsCutAtLimit(28);
            member = document.GetObject(new PdfObjectId(11));
        }
        finally
        {
            document.Reader.EndWalk();
        }

        var read = source.BytesRead;

        document.WasRepaired.Should().BeTrue();
        cutBefore.Should().BeTrue();
        cutAfter.Should().BeFalse();
        member.Should().BeOfType<PdfDictionary>();
        document.GetObject(new PdfObjectId(300)).Should().BeOfType<PdfStream>();
        source.BytesRead.Should().Be(read);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/DecodeParms 5 0 R ")]
    [InlineData("/DecodeParms << /Predictor 9 0 R >> ")]
    public void An_object_stream_whose_decoding_threw_at_a_guard_throws_the_same_exception_for_each_member_read_after(string entries)
    {
        // Object stream 20 holds objects 9 and 5, and its data decodes to 64 KB, past a MaxDecodedStreamLength of 1 KB. Its
        // filter's parameters may name one of its own members, which reads as null while the stream is decoded, and as null
        // again at every decoding (#51): each decoding throws alike. Opened to throw, the first read throws, and every read
        // of a member after it throws the same exception, without decoding the stream again (#181).
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
            [20] = (1, file.PaddedObjectStream(20, [(9, "<< >>"), (5, "<< >>")], 64 * 1024, entries), 0),
            [9] = (2, 20, 0),
            [5] = (2, 20, 1),
        };
        file.CompressedCrossReferenceStream(21, 22, number => rows.TryGetValue(number, out var row) ? row : (0, 0, 0), "/Root 1 0 R");
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 1024 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        var first = Reading(9).Should().Throw<PdfLimitExceededException>().Which;

        first.Code.Should().Be(PdfDiagnosticCodes.LimitDecodedStream);
        Reading(9).Should().Throw<PdfLimitExceededException>().Which.Should().BeSameAs(first);
        Reading(5).Should().Throw<PdfLimitExceededException>().Which.Should().BeSameAs(first);

        Func<PdfObject> Reading(int number) => () => document.GetObject(new PdfObjectId(number));
    }

    [Fact]
    public void A_member_within_MaxObjectLength_is_read_whole_and_reported_nowhere()
    {
        var file = LongMembers(out _);
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(4)).AsArray().Required().Count.Should().Be(4000);
        document.GetObject(new PdfObjectId(5)).AsArray().Required().Count.Should().Be(4000);
        document.Diagnostics.Should().BeEmpty();
        document.Reader.IsCutAtLimit(4).Should().BeFalse();
    }

    [Theory]
    [InlineData(100, "")]
    [InlineData(101, "")]
    [InlineData(104, "")]
    [InlineData(106, "")]
    [InlineData(107, "X")]
    public void A_member_dictionary_whose_stream_keyword_MaxObjectLength_leaves_out_is_cut_there_and_reported(int bound, string tail)
    {
        // Member 3 is a dictionary of 100 bytes and the stream after it, which an object stream cannot hold. The bound ends
        // the member's window right after the dictionary, or inside "stream": what follows the dictionary is unknown, and the
        // member is cut at the guard, not read as a dictionary in silence. On the keyword's last byte, a regular byte past it
        // in the decoded data, "streamX", leaves what the keyword is unknown too, as an object written in the file whose
        // window ends there is (#282).
        var member = "<< /A (" + new string('x', 89) + ") >>\nstream" + tail + "\nabc\nendstream";
        member.IndexOf(">>", StringComparison.Ordinal).Should().Be(98);
        var file = new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, member)
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [3]);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = bound } };

        using var document = PdfDocument.Open(file, options);

        document.GetObject(new PdfObjectId(3)).Should().BeOfType<PdfDictionary>();
        document.Reader.IsCutAtLimit(3).Should().BeTrue();
        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.LimitObject);
    }

    [Theory]
    [InlineData(107, false)]
    [InlineData(107, true)]
    [InlineData(108, false)]
    public void A_member_dictionary_whose_stream_keyword_ends_where_MaxObjectLength_ends_its_window_is_a_stream(int bound, bool throwOnLimit)
    {
        // The bound ends member 3's window on the last byte of "stream", which the end of line past it in the decoded data
        // says is whole: the member is a stream, which an object stream cannot hold, and reads as null, as under a bound a
        // byte larger; no guard is reached, nor thrown (#282).
        const string Member = "<< /A (" + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx" + ") >>\nstream\nabc\nendstream";
        var file = new TestPdfBuilder()
            .WithObject(1, Catalog)
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, Member)
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [3]);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = bound }, ThrowOnLimit = throwOnLimit };

        using var document = PdfDocument.Open(file, options);

        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        document.Reader.IsCutAtLimit(3).Should().BeFalse();
        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.ObjectStreamMemberStream);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_guard_reached_reading_a_catalog_candidate_s_type_is_thrown_once_the_search_after_a_rebuild_ends(bool settle)
    {
        // /Root names object 3, a string. Object 4, the first a rebuild's search for the catalog reads, gives its /Type as a
        // reference to object 9, longer than MaxObjectLength; the catalog, object 1, comes after it. A rebuild — set off by
        // row 5, which leads into object 4's header, or by settling the index — searches for the catalog: the guard
        // reached reading 4's /Type is held until the search has found 1, then thrown, as one reading a candidate is. The
        // search at opening took 1 too, which is said once.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [4] = file.Object(4, "<< /Type 9 0 R >>"),
            [9] = file.Object(9, "(" + new string('x', 3000) + ")"),
            [3] = file.Object(3, "(three)"),
            [1] = file.Object(1, Catalog),
            [2] = file.Object(2, Pages),
        };
        rows[5] = rows[4];
        file.Table(10, rows, "/Size 10 /Root 3 0 R");
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxObjectLength = 2048 } };

        using var document = PdfDocument.Open(file.Bytes, options);
        Action rebuilding = settle ? document.Reader.SettleIndex : () => document.GetObject(new PdfObjectId(5));

        rebuilding.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));
        document.WasRepaired.Should().BeTrue();
        document.Trailer.GetRaw(PdfName.Root).Should().BeOfType<PdfReference>().Which.Id.Should().Be(new PdfObjectId(1));
        document.Diagnostics.Where(diagnostic => diagnostic.Code == PdfDiagnosticCodes.TrailerRootRecovered).Should().ContainSingle()
            .Which.Message.Should().EndWith("the catalog was found as object 1.");

        // The rebuild is finished: the next read rebuilds nothing, and throws nothing.
        document.Reader.SettleIndex();
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("endobj")]
    [InlineData("R")]
    [InlineData("streaml")]
    public void A_member_dictionary_followed_by_a_keyword_that_cannot_be_stream_where_MaxObjectLength_ends_its_window_is_whole(string keyword)
    {
        // Member 4's dictionary is followed by a keyword its window, which MaxObjectLength ends, ends on: however the
        // keyword goes on past the window, it is no stream keyword, so the dictionary is whole, as the guard lifted reads it.
        var member = "<< /A (" + new string('x', 100) + ") >> " + keyword;
        var written = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, written.Object(1, Catalog), 0),
            [2] = (1, written.Object(2, Pages), 0),
            [3] = (1, written.ObjectStream(3, [(4, member + new string(' ', 300)), (5, "42")]), 0),
            [4] = (2, 3, 0),
            [5] = (2, 3, 1),
        };
        written.CrossReferenceStream(6, 7, rows, "/Root 1 0 R");

        using var document = PdfDocument.Open(written.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = member.Length } });

        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfDictionary>();
        document.Reader.IsCutAtLimit(4).Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("[<< /A (x) >>]", 0)]
    [InlineData("[<< /A (x) >> >>", 1)]
    public void A_member_whose_value_ends_where_MaxObjectLength_ends_its_window_is_read_whole(string shape, int strays)
    {
        // Member 4's window ends where MaxObjectLength does, on the last byte of its value: a "]" or a ">>" is whole
        // wherever it lies, and no stream keyword. The member is read whole, and a ">>" closing its array is the file's
        // fault, reported, as it is with the window the member would have without the guard.
        var member = shape.Replace("(x)", "(" + new string('x', 200) + ")", StringComparison.Ordinal);
        var written = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, written.Object(1, Catalog), 0),
            [2] = (1, written.Object(2, Pages), 0),
            [3] = (1, written.ObjectStream(3, [(4, member + new string(' ', 300)), (5, "42")]), 0),
            [4] = (2, 3, 0),
            [5] = (2, 3, 1),
        };
        written.CrossReferenceStream(6, 7, rows, "/Root 1 0 R");

        using var document = PdfDocument.Open(written.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = member.Length } });

        document.GetObject(new PdfObjectId(4)).AsArray().Required().Single().Should().BeOfType<PdfDictionary>();
        document.Reader.IsCutAtLimit(4).Should().BeFalse();
        document.Diagnostics.Should().HaveCount(strays);
        document.Diagnostics.Should().AllSatisfy(diagnostic => diagnostic.Message.Should().StartWith("An array was closed by a dictionary end. It was met in object 4,"));
    }

    [Theory]
    [InlineData("a literal string", false)]
    [InlineData("a literal string", true)]
    [InlineData("a hexadecimal string", false)]
    [InlineData("a hexadecimal string", true)]
    [InlineData("a name", false)]
    [InlineData("a name", true)]
    [InlineData("a real", false)]
    [InlineData("a real", true)]
    [InlineData("a keyword", false)]
    [InlineData("a keyword", true)]
    public void A_member_whose_token_ends_where_MaxObjectLength_ends_its_window_is_read_whole(string shape, bool throwOnLimit)
    {
        // Member 4's window ends where MaxObjectLength does, right after its value, white space past it: a closed string is
        // whole wherever it ends, and a name, a real or a keyword the decoded byte past it says ends there. The member reads
        // as the window the member would have without the guard reads it, not cut, nothing reported, under ThrowOnLimit as
        // without (#282).
        var value = shape switch
        {
            "a literal string" => "(" + new string('a', 300) + ")",
            "a hexadecimal string" => "<" + string.Concat(Enumerable.Repeat("aa", 100)) + ">",
            "a name" => "/N" + new string('a', 100),
            "a real" => "1.5",
            _ => "true",
        };
        var file = MemberReachingItsBound(value);
        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(
            file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 200 + value.Length }, ThrowOnLimit = throwOnLimit });

        var read = document.GetObject(new PdfObjectId(4));
        read.Should().NotBeSameAs(PdfNull.Instance).And.BeOfType(whole.GetObject(new PdfObjectId(4)).GetType());
        read.ToString().Should().Be(whole.GetObject(new PdfObjectId(4)).ToString());
        document.Reader.IsCutAtLimit(4).Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/NameX", 5)]
    [InlineData("15", 1)]
    [InlineData("15", 2)]
    [InlineData("1.5", 2)]
    [InlineData("15 0 R", 2)]
    [InlineData("15 0 R", 4)]
    public void A_member_whose_token_MaxObjectLength_parts_or_whose_reference_it_may_cut_reads_as_null_cut(string value, int kept)
    {
        // Member 4's window ends where MaxObjectLength does, inside its value: a name, an integer or a real the decoded data
        // goes on past with a regular byte is parted, and no value the file wrote; so is an integer, whole or not, that may
        // be the number or the generation of a reference the window's edge cuts. The member reads as null, cut (#262, #279).
        using var document = PdfDocument.Open(
            MemberReachingItsBound(value), new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 200 + kept } });

        document.GetObject(new PdfObjectId(4)).Should().BeSameAs(PdfNull.Instance);
        document.Reader.IsCutAtLimit(4).Should().BeTrue();
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
    }

    [Fact]
    public void A_member_whose_reference_ends_where_MaxObjectLength_ends_its_window_is_the_reference()
    {
        // Member 4's window ends where MaxObjectLength does, right after the R of "6 0 R": the decoded byte past it says the
        // R is whole, as the file says of an object's, so the member is the reference the window the member would have
        // without the guard reads, and nothing is reported, under ThrowOnLimit as without.
        using var document = PdfDocument.Open(
            MemberReachingItsBound("6 0 R"),
            new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 205 }, ThrowOnLimit = true });

        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfReference>().Which.Id.Should().Be(new PdfObjectId(6));
        document.Reader.IsCutAtLimit(4).Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void A_member_whose_R_the_byte_past_MaxObjectLength_continues_is_cut()
    {
        // The byte past member 4's window continues the R, "RG": what the window holds may be no reference, and the member
        // is one the guard cut.
        using var document = PdfDocument.Open(
            MemberReachingItsBound("6 0 RG"), new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 205 } });

        _ = document.GetObject(new PdfObjectId(4));

        document.Reader.IsCutAtLimit(4).Should().BeTrue();
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
    }

    [Fact]
    public void A_member_whose_array_ends_where_a_guard_cuts_the_data_is_read_whole()
    {
        // Member 10, the last, ends on byte 17, where MaxDecodedStreamLength cuts the data: the "]" that closes it is whole,
        // and so is the member, though the dictionary before it reaches the cut.
        const string Data = "10 0 [<< /A 1 >>] (more data) ";
        var hex = Convert.ToHexString(Encoding.Latin1.GetBytes(Data)) + ">";
        var written = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, written.Object(1, Catalog), 0),
            [2] = (1, written.Object(2, Pages), 0),
            [3] = (1, written.Object(3, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First 5 /Filter /ASCIIHexDecode /Length {hex.Length} >>\nstream\n{hex}\nendstream")), 0),
            [10] = (2, 3, 0),
        };
        written.CrossReferenceStream(12, 13, rows, "/Root 1 0 R");

        using var document = PdfDocument.Open(written.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 17 } });

        document.GetObject(new PdfObjectId(10)).AsArray().Required().Single().Should().BeOfType<PdfDictionary>();
        document.Reader.IsCutAtLimit(10).Should().BeFalse();
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_member_whose_R_ends_where_a_guard_cuts_the_data_is_cut(bool throwOnLimit)
    {
        // Member 10, "6 0 R", ends on byte 10, where MaxDecodedStreamLength cuts the data: what follows the R, which would
        // say whether it ends a reference, is the guard's, and nothing past the data is asked. The number and the generation
        // may start a reference the cut parted, and are dropped: the member reads as null, cut, or the guard is thrown (#262).
        const string Data = "10 0 6 0 R (more data) ";
        var hex = Convert.ToHexString(Encoding.Latin1.GetBytes(Data)) + ">";
        var written = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, written.Object(1, Catalog), 0),
            [2] = (1, written.Object(2, Pages), 0),
            [3] = (1, written.Object(3, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First 5 /Filter /ASCIIHexDecode /Length {hex.Length} >>\nstream\n{hex}\nendstream")), 0),
            [10] = (2, 3, 0),
        };
        written.CrossReferenceStream(12, 13, rows, "/Root 1 0 R");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 10 }, ThrowOnLimit = throwOnLimit };

        using var document = PdfDocument.Open(written.Bytes, options);

        if (throwOnLimit)
        {
            var reading = () => document.GetObject(new PdfObjectId(10));
            reading.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxDecodedStreamLength));
            return;
        }

        document.GetObject(new PdfObjectId(10)).Should().BeSameAs(PdfNull.Instance);
        document.Reader.IsCutAtLimit(10).Should().BeTrue();
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
    }

    [Fact]
    public void A_member_left_open_where_a_guard_cuts_the_data_at_the_next_member_s_start_is_the_file_s_fault()
    {
        // Member 10, "[1 2", decodes to bytes 10 to 13 and is left open; member 11 starts at byte 14, where
        // MaxDecodedStreamLength cuts the data. The member ends at the next one's start whatever the guard did: what it
        // lacks is the file's, reported as such, and it is no member a guard cut.
        const string Data = "10 0 11 4 [1 23 ";
        var hex = Convert.ToHexString(Encoding.Latin1.GetBytes(Data)) + ">";
        var written = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, written.Object(1, Catalog), 0),
            [2] = (1, written.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [3] = (1, written.Object(3, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 2 /First 10 /Filter /ASCIIHexDecode /Length {hex.Length} >>\nstream\n{hex}\nendstream")), 0),
            [10] = (2, 3, 0),
            [11] = (2, 3, 1),
        };
        written.CrossReferenceStream(12, 13, rows, "/Root 1 0 R");

        using var document = PdfDocument.Open(written.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 14 } });

        document.GetObject(new PdfObjectId(10)).AsArray().Required().Count.Should().Be(2);
        document.Reader.IsCutAtLimit(10).Should().BeFalse();
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream, PdfDiagnosticCodes.SyntaxTruncatedObject);
        document.Diagnostics[1].Message.Should().StartWith("The next member of the object stream started inside an array");
    }

    [Fact]
    public void A_rebuild_damage_sets_off_keeps_the_entries_a_chain_a_guard_cut_gave_that_the_scan_cannot_find()
    {
        // The cross-reference stream decodes no further than five of its eight rows: objects 3 and 4, packed in object 6,
        // are indexed; the catalog, object 5, and object 6 itself are past the cut. /Root names object 2, the page tree,
        // which is no catalog, and none is among the indexed objects: that is damage, and the index is rebuilt. The scan
        // finds every header, but object 6 has no /Type /ObjStm, so the rebuild does not take in what it holds: only the
        // chain's entries for 3 and 4, kept through the rebuild, still place them — in a stream that serves no member, as
        // the validator reads it (#274).
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
        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        document.GetObject(new PdfObjectId(4)).Should().BeSameAs(PdfNull.Instance);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable)
            .Which.Message.Should().Be("Object stream 6 is a stream that is not of /Type /ObjStm; none of its objects can be read from it.");

        // Five headers found, two entries kept: the chain's free row for object 0 is no object, and is not.
        document.ObjectCount.Should().Be(7);
    }

    [Fact]
    public void A_rebuild_a_read_sets_off_keeps_the_entries_a_chain_a_guard_cut_gave_that_the_scan_cannot_find()
    {
        // As above, the rebuild set off by a read: object 4's row leads into padding, and reading 4 rebuilds the index while
        // 4 is being loaded. The cross-reference stream decodes no further than its first ten rows; object 6, which holds 7,
        // has a header the scan does not find (ChainFiles.HiddenHeader): once the rebuild is finished, only the chain's entries
        // for 6 and 7, kept through it, place 7.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, Catalog),
            [2] = file.Object(2, Pages),
            [6] = file.Position + ChainFiles.HiddenHeaderLead,
        };
        file.Raw(ChainFiles.HiddenHeader(6) + "\n<< /Type /ObjStm /N 1 /First 4 /Length 18 >>\nstream\n7 0 (packed seven)\nendstream\nendobj\n");
        rows[8] = file.Padding(8);
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

    [Fact]
    public void A_trailer_keyword_followed_by_what_a_trailer_guard_cuts_leaves_the_table_the_guard_s_not_read_in_part()
    {
        // The table's window, which MaxXRefSectionLength bounds, cuts the array after the trailer keyword, and MaxTrailerLength
        // cuts it again where it is read from a window of its own: what follows the keyword may be a dictionary past the cut,
        // and the table is the guard's, not one whose trailer is lost (#322).
        var file = PdfTemplate.Build(PdfTemplate.Sound.Replace(
            "<< /Size 4 /Root 1 0 R >>", "[ (" + new string('x', 300) + ") ]", StringComparison.Ordinal));
        var limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 128, MaxTrailerLength = 64 };

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        var section = document.Reader.Structure.Sections.Should().ContainSingle().Which;
        section.TrailerCutByLimit.Should().BeTrue();
        section.Incomplete.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Contain(PdfDiagnosticCodes.LimitTrailer)
            .And.NotContain(PdfDiagnosticCodes.XRefSectionUnreadable);
    }

    [Fact]
    public void A_table_read_in_part_for_a_row_whose_trailer_a_guard_cut_is_not_said_to_have_lost_its_trailer()
    {
        // The table's last row is no row, which reads it in part; the array after its trailer keyword is cut as above. Whether
        // a dictionary lay past the cut is the guard's to hide: the report names the row alone (#322).
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("{row:3}", "XXXXXXXXXX 00000 n ", StringComparison.Ordinal)
            .Replace("<< /Size 4 /Root 1 0 R >>", "[ (" + new string('x', 300) + ") ]", StringComparison.Ordinal));
        var limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 128, MaxTrailerLength = 64 };

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        document.Reader.Structure.Sections.Should().ContainSingle().Which.TrailerCutByLimit.Should().BeTrue();
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.XRefSectionUnreadable).Which.Message
            .Should().Contain("the row for object 3").And.NotContain("trailer keyword is followed by no dictionary");
    }

    [Theory]
    [InlineData("<< /Size", "missing")]
    [InlineData("startxref 0 << /Size", "the keyword startxref")]
    [InlineData("trai << /Size", "the keyword trai")]
    public void What_ends_the_rows_a_length_bound_cut_is_judged_as_it_is_where_none_did(string replacement, string judged)
    {
        // A dictionary the trailer keyword does not introduce is a trailer missing; anything else is no part of a table — the
        // start of the trailer keyword among them, where the file does not end with it.
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
            // Read as the trailer, as where no guard stopped the reader (#322): its /Root leads to the catalog, which no search
            // looks for.
            section.TrailerFault.Should().Be(XRefTrailerFault.Missing);
            section.Fault.Should().BeNull();
            document.Trailer.GetRaw(PdfName.Root).Should().BeOfType<PdfReference>();
            document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitXRefSectionLength);
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

    [Theory]
    [InlineData("", null)]
    [InlineData("t", null)]
    [InlineData("trai", null)]
    [InlineData("traile", null)]
    [InlineData("tx", "the keyword tx")]
    public void A_table_a_length_bound_cuts_that_runs_to_the_end_of_the_file_has_no_trailer(string tail, string? stray)
    {
        // The file ends in the table's rows, its startxref written before them, in reach of the search at the file's end. It
        // ends so within the trailer keyword too, whose start the search past the rows took for no part of a table (#207); a
        // word the keyword does not start is one, and the section is judged by it.
        var written = Trailed(string.Empty);
        var table = OffsetOf(written, "xref\n");
        var rows = written.AsSpan(table, OffsetOf(written, "trailer") - table).ToArray();
        const string Pointer = "startxref\n{0:D10}\n%%EOF\n";
        var pointer = Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture, Pointer, table + string.Format(CultureInfo.InvariantCulture, Pointer, 0).Length));
        byte[] file = [.. written.AsSpan(0, table), .. pointer, .. rows, .. Encoding.ASCII.GetBytes(tail)];
        var limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 128 };

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        var section = document.Reader.Structure.Sections.Should().ContainSingle().Which;
        section.Offset.Should().Be(table + pointer.Length);
        var findings = new PdfValidator().Validate(document).Findings;

        if (stray is null)
        {
            section.TrailerFault.Should().Be(XRefTrailerFault.Missing);
            section.TrailerPosition.Should().Be(file.Length);
            section.Fault.Should().BeNull();
            findings.Should().ContainSingle(finding => finding.RuleId == PdfValidationRuleIds.FileTrailerMissing);
            findings.Should().NotContain(finding => finding.RuleId == PdfValidationRuleIds.XRefSectionMalformed);
        }
        else
        {
            section.TrailerFault.Should().Be(XRefTrailerFault.None);
            section.TrailerPosition.Should().Be(file.Length - tail.Length);
            section.Fault.Should().Be($"it holds {stray} at offset {file.Length - tail.Length}, where a subsection or the trailer should start");
        }
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
    public void A_catalog_a_search_took_that_a_rebuild_reads_cut_is_looked_for_again()
    {
        // Review of #175 (L3-01): /Root names object 1, no catalog; the search takes object 4, whose later definition a limit
        // cuts. Settling rebuilds the index, which serves that definition: the reader took 4 as read, its own guess, though
        // object 9 is a catalog read whole.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Outlines >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>"),
            [4] = file.Object(4, "<< /Type /Catalog /Pages 2 0 R >>"),
        };
        file.Object(4, "<<" + new string(' ', 200) + "/Type /Catalog /Pages 2 0 R >>");
        rows[9] = file.Object(9, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Raw("%" + new string('c', 1200) + "\n");
        file.Object(6, "(six)");
        rows[6] = 3;
        file.Table(10, rows, "/Size 10 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 128 } });

        document.Reader.CatalogFoundAs.Should().Be(4);
        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeTrue();
        document.Reader.CatalogFoundAs.Should().Be(9);
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        report.Findings.Single(finding => finding.RuleId == PdfValidationRuleIds.FileRootInvalid).Message.Should().Be(
            "The trailer's /Root names object 1 0, which is a dictionary of /Type /Outlines, not a document catalog. The reader took object 9, which is one, for the catalog.");
    }

    [Theory]
    [InlineData("a catalog a limit cuts")]
    [InlineData("a whole catalog")]
    [InlineData("no catalog")]
    public void A_rebuild_after_a_search_reads_the_file_s_own_root_again(string later)
    {
        // Review of #175's fixes (M2-02): /Root names object 1, no catalog, and the search takes object 5. Settling rebuilds
        // the index, which serves a later object 1 and a later object 5, a font. The rebuild kept the search's guess in place
        // of the file's /Root: a later 1 the guard cut was looked past, a whole one ignored, and with none the guess stayed.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Outlines >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>"),
            [5] = file.Object(5, "<< /Type /Catalog /Pages 2 0 R /Lang (older) >>"),
        };

        switch (later)
        {
            case "a catalog a limit cuts":
                file.Object(1, "<<" + new string(' ', 200) + "/Type /Catalog /Pages 2 0 R >>");
                break;
            case "a whole catalog":
                file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Lang (file) >>");
                break;
        }

        file.Object(5, "<< /Type /Font >>");
        rows[7] = file.Object(7, "<< /Type /Catalog /Pages 2 0 R /Lang (seventh) >>");
        file.Raw("%" + new string('c', 1200) + "\n");
        file.Object(6, "(six)");
        rows[6] = 3;
        file.Table(8, rows, "/Size 8 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 128 } });
        document.Reader.CatalogFoundAs.Should().Be(5);

        document.Reader.SettleIndex();

        document.WasRepaired.Should().BeTrue();

        switch (later)
        {
            case "a catalog a limit cuts":
                document.Reader.CatalogFoundAs.Should().Be(0, "a /Root a guard cut is taken as read");
                document.Trailer.GetRaw(PdfName.Root).Should().BeOfType<PdfReference>().Which.Id.Number.Should().Be(1);
                break;
            case "a whole catalog":
                document.Reader.CatalogFoundAs.Should().Be(0);
                document.Catalog.Required()[PdfName.Lang].Should().BeOfType<PdfString>().Which.ToText().Should().Be("file");
                break;
            default:
                document.Reader.CatalogFoundAs.Should().Be(7, "the rebuilt index serves no catalog as object 5");
                document.Catalog.Required()[PdfName.Lang].Should().BeOfType<PdfString>().Which.ToText().Should().Be("seventh");
                break;
        }
    }

    [Fact]
    public void A_rebuild_reaching_a_guard_on_a_catalog_a_search_took_still_looks_for_another_when_opened_to_throw()
    {
        // M2-03: as in L3-01, under ThrowOnLimit. The rebuild read the search's guess, object 4, cut, and threw before it
        // looked again: the reader kept its guess, and every later read of the catalog threw.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Outlines >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>"),
            [4] = file.Object(4, "<< /Type /Catalog /Pages 2 0 R >>"),
        };
        file.Object(4, "<<" + new string(' ', 200) + "/Type /Catalog /Pages 2 0 R >>");
        rows[9] = file.Object(9, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Raw("%" + new string('c', 1200) + "\n");
        file.Object(6, "(six)");
        rows[6] = 3;
        file.Table(10, rows, "/Size 10 /Root 1 0 R");
        using var document = PdfDocument.Open(
            file.Bytes, new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxObjectLength = 128 } });
        document.Reader.CatalogFoundAs.Should().Be(4);

        FluentActions.Invoking(document.Reader.SettleIndex).Should().Throw<PdfLimitExceededException>();

        document.Reader.CatalogFoundAs.Should().Be(9);
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    [Fact]
    public void A_rebuild_reaching_a_guard_on_the_file_s_own_root_takes_it_as_read_when_opened_to_throw()
    {
        // As in M2-02's first shape, under ThrowOnLimit: /Root names object 8, no catalog, and the search takes object 5.
        // The rebuild settling makes serves a later object 8, a catalog a limit cuts, and reading it to judge /Root reaches
        // the guard: the rebuild takes the /Root as read, as one a guard cut, and looks for no other catalog. A later object
        // 1, which the same limit cuts, reached it first, as the rebuild took in object streams: that guard is the one
        // thrown, once the index is whole.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "(one)"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>"),
            [5] = file.Object(5, "<< /Type /Catalog /Pages 2 0 R /Lang (older) >>"),
            [8] = file.Object(8, "<< /Type /Outlines >>"),
        };
        var one = file.Object(1, "(" + new string('x', 300) + ")");
        file.Object(8, "<<" + new string(' ', 200) + "/Type /Catalog /Pages 2 0 R >>");
        file.Object(5, "<< /Type /Font >>");
        rows[7] = file.Object(7, "<< /Type /Catalog /Pages 2 0 R /Lang (seventh) >>");
        file.Raw("%" + new string('c', 1200) + "\n");
        file.Object(6, "(six)");
        rows[6] = 3;
        file.Table(9, rows, "/Size 9 /Root 8 0 R");
        using var document = PdfDocument.Open(
            file.Bytes, new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxObjectLength = 128 } });
        document.Reader.CatalogFoundAs.Should().Be(5);

        FluentActions.Invoking(document.Reader.SettleIndex).Should().Throw<PdfLimitExceededException>().Which.Position.Should().Be(one);

        document.WasRepaired.Should().BeTrue();
        document.Reader.CatalogFoundAs.Should().Be(0, "a /Root a guard cut is taken as read, and object 7 is not looked for");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_root_that_leads_through_a_chain_to_a_catalog_a_guard_cut_is_taken_as_read(bool older)
    {
        // M2-04: /Root names object 7, whose value is 1 0 R, a catalog cut before its /Type. The cut was looked for on
        // object 7: the reader looked for another catalog, or rebuilt the index, and file.root-invalid judged the cut one.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<<" + new string(' ', 200) + "/Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>"),
            [7] = file.Object(7, "1 0 R"),
        };

        if (older)
        {
            rows[5] = file.Object(5, "<< /Type /Catalog /Pages 2 0 R >>");
        }

        file.Table(8, rows, "/Size 8 /Root 7 0 R");
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 128 } });

        document.Reader.Structure.RootCutByGuard.Should().BeTrue();
        document.Reader.CatalogFoundAs.Should().Be(0);
        document.WasRepaired.Should().BeFalse();
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse();
        PageTreeWalk.Run(document).Partial.Should().BeTrue("the catalog the chain leads to may give its /Pages past the cut");
    }

    [Theory]
    [InlineData("past the decoded data")]
    [InlineData("past the decoded data, an older catalog beside")]
    [InlineData("in a stream whose dictionary a guard cut")]
    public void A_catalog_an_object_stream_s_cut_leaves_unread_is_taken_as_read(string shape)
    {
        // #259: the catalog is a member of an object stream a guard left unable to serve it — its decoded data cut before
        // the member starts, or its own dictionary cut. It read as missing: the reader looked for another catalog, taking an
        // older one and orphaning the real tree's page, or rebuilt the index for nothing, and file.root-invalid said the
        // file does not hold it.
        // The object stream holds its members in the order they are written: object 4, then the catalog.
        var builder = new TestPdfBuilder();

        if (shape != "in a stream whose dictionary a guard cut")
        {
            builder.WithObject(4, "(" + new string('x', 300) + ")");
        }

        builder
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>");
        PdfReaderLimits limits;
        byte[] file;

        if (shape == "in a stream whose dictionary a guard cut")
        {
            file = builder.BuildWithXRefStream(
                rootNumber: 1, compressedObjects: [1], objectStreamEntries: "/Pad (" + new string('x', 300) + ")");
            limits = PdfReaderLimits.Default with { MaxObjectLength = 128 };
        }
        else
        {
            if (shape.EndsWith("beside", StringComparison.Ordinal))
            {
                builder
                    .WithObject(5, "<< /Type /Catalog /Pages 6 0 R >>")
                    .WithObject(6, "<< /Type /Pages /Kids [7 0 R] /Count 1 >>")
                    .WithObject(7, "<< /Type /Page /Parent 6 0 R /MediaBox [0 0 1 1] /Resources << >> >>");
            }

            file = builder.BuildWithXRefStream(rootNumber: 1, compressedObjects: [4, 1], compressObjectStream: true);
            limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 100 };
        }

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        document.Reader.IsCutAtLimit(1).Should().BeTrue();
        document.Reader.Structure.RootCutByGuard.Should().BeTrue();
        document.Reader.CatalogFoundAs.Should().Be(0, "no other catalog is looked for");
        var report = new PdfValidator().Validate(document);
        document.WasRepaired.Should().BeFalse();
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse(string.Join("; ", report.Findings));
        report.Contains(PdfValidationRuleIds.PageTreePageOrphaned).Should().BeFalse();
        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.StartsWith("limit.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("past the decoded data")]
    [InlineData("first past the decoded data")]
    [InlineData("in a stream whose dictionary a guard cut")]
    public void A_catalog_an_object_stream_s_cut_leaves_unread_stays_taken_as_read_through_a_rebuild(string shape)
    {
        // N2-01: object 9's row places it inside object 8's string, and settling rebuilds the index. The scan finds no header
        // of the catalog, and, where the stream's header lies past the cut or its dictionary is cut, the expansion none in
        // the stream a guard hides: the catalog left the index, read as missing, and the search took the older catalog 5.
        // Past the decoded data, the header read, the expansion indexes the catalog, cut as before.
        using var document = PdfDocument.Open(HiddenMemberRebuilt(shape, member: 1, out var limits), new PdfReaderOptions { Limits = limits });

        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeTrue();
        document.Reader.IsCutAtLimit(1).Should().BeTrue();
        document.Reader.CatalogFoundAs.Should().Be(0, "no other catalog is looked for");
        document.Trailer.GetRaw(PdfName.Root).Should().BeOfType<PdfReference>().Which.Id.Number.Should().Be(1);
        document.Diagnostics.Should().NotContain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.TrailerRootRecovered);
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse(string.Join("; ", report.Findings));
    }

    [Theory]
    [InlineData("past the decoded data")]
    [InlineData("first past the decoded data")]
    public void A_member_an_object_stream_s_cut_leaves_unread_throws_again_after_a_rebuild(string shape)
    {
        // N2-01: opened to throw, the rebuild's expansion caught the guard decoding the stream and indexed none of its members;
        // read again, the root of the page tree was missing, and null, where the guard throws each time it is reached.
        var file = HiddenMemberRebuilt(shape, member: 2, out var limits);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits, ThrowOnLimit = true });

        FluentActions.Invoking(() => document.GetObject(new PdfObjectId(2))).Should().Throw<PdfLimitExceededException>();
        FluentActions.Invoking(document.Reader.SettleIndex).Should().Throw<PdfLimitExceededException>();

        document.WasRepaired.Should().BeTrue();
        FluentActions.Invoking(() => document.GetObject(new PdfObjectId(2))).Should().Throw<PdfLimitExceededException>();
    }

    [Theory]
    [InlineData("read")]
    [InlineData("validation")]
    public void A_stream_whose_length_a_rebuild_takes_in_late_reads_whole_after_the_rebuild_that_reached_a_guard(string how)
    {
        // Opened to throw, a read of object 50, whose row points into the spaces of object 10's data, or a validation, rebuilds
        // the index, and the rebuild's scan reaches MaxRebuildReadPasses. Telling the object streams apart, the rebuild loads
        // stream 42, whose /Length names object 41, a member of object stream 40 it has not taken in yet: the lookup throws
        // the guard, the rebuild goes on, and takes 41 in. The exception was kept for 41, and thrown by every read of 41 and
        // of 42 for the document's life; both read whole.
        var file = LengthInAMemberTakenInLate();
        using var document = PdfDocument.Open(file, new PdfReaderOptions { ThrowOnLimit = true });
        Action rebuilding = how == "read" ? () => document.GetObject(new PdfObjectId(50)) : () => new PdfValidator().Validate(document);

        rebuilding.Should().Throw<PdfLimitExceededException>().Which.Code.Should().Be(PdfDiagnosticCodes.LimitRebuildReads);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(41)).Should().BeOfType<PdfInteger>().Which.Value.Should().Be(44);
        document.GetObject(new PdfObjectId(42)).AsStream().Required().Decode().Length.Should().Be(44);
    }

    [Fact]
    public void An_object_whose_load_threw_in_a_rebuild_reads_as_the_object_stream_that_replaces_it_defines_it_and_the_lookup_reads_again()
    {
        // Opened to throw, a lookup of object 31, which the table lacks, rebuilds the index, whose /Prev names no section.
        // Telling the object streams apart, the rebuild loads object 5, written directly, which runs past MaxObjectLength: the
        // load throws, and keeps its exception. Object stream 10, written after it, defines 5 again, and replaces it: the
        // lookup throws the guard once the rebuild is finished, and object 5 reads as the stream defines it. The lookup keeps
        // nothing of what it threw, its load having rebuilt the index: read again, 31 reads as the rebuilt index has it.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        file.Object(5, "(" + new string('x', 2000) + ")");
        file.ObjectStream(10, [(5, "(five)")]);
        file.Table(3, rows, "/Size 3 /Root 1 0 R /Prev 1 0 R");
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        FluentActions.Invoking(() => document.GetObject(new PdfObjectId(31))).Should().Throw<PdfLimitExceededException>()
            .Which.Code.Should().Be(PdfDiagnosticCodes.LimitObject);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(5)).AsText().Should().Be("five");
        document.GetObject(new PdfObjectId(31)).Should().BeSameAs(PdfNull.Instance);
    }

    /// <summary>
    /// Gives the data of an attached file that spells six streams, each one's /Length running over the headers after it, then
    /// 64 KB of spaces: a rebuild's scan reads past them more than <see cref="PdfReaderLimits.MaxRebuildReadPasses"/> lets it.
    /// </summary>
    private static string SpelledStreamsData()
    {
        const int Spelled = 6;
        const string Head = "20 0 obj\n<< /Length 0000000 >>\nstream\nabc\n";
        var spelled = new StringBuilder();

        for (var i = 0; i < Spelled; i++)
        {
            var length = ((Spelled - i) * Head.Length) - (Head.Length - "abc\n".Length);
            spelled.Append(Head.Replace("20 0", string.Create(CultureInfo.InvariantCulture, $"{20 + i} 0"), StringComparison.Ordinal)
                .Replace("0000000", length.ToString("D7", CultureInfo.InvariantCulture), StringComparison.Ordinal));
        }

        return spelled.Append(' ', 64 * 1024).Append("attached tail\n").ToString();
    }

    /// <summary>
    /// Writes the file of the guard on the rebuild's reads (<see cref="Case.For"/>) under a classic table whose row for object
    /// 50 points into the spaces of object 10's data, and that lacks object 41, which object stream 40 holds; stream 42 takes
    /// its <c>/Length</c>, 44, from it.
    /// </summary>
    private static byte[] LengthInAMemberTakenInLate()
    {
        var data = SpelledStreamsData();
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, Catalog),
            [2] = file.Object(2, Pages),
            [10] = file.Object(10, "<< /Type /EmbeddedFile /Length 11 0 R >>\nstream\n" + data + "endstream"),
            [11] = file.Object(11, LengthNote + data.Length.ToString(CultureInfo.InvariantCulture)),
            [40] = file.ObjectStream(40, [(41, "44")]),
            [42] = file.Object(42, "<< /Length 41 0 R >>\nstream\n" + new string('x', 44) + "\nendstream"),
        };
        rows[50] = rows[10] + 2000;
        file.Table(51, rows, "/Size 51 /Root 1 0 R");
        return file.Bytes;
    }

    [Theory]
    [InlineData("a member its stream lacks")]
    [InlineData("a member written again directly")]
    [InlineData("a member of a stream a later object stream defines again")]
    public void A_rebuild_keeps_from_the_chain_only_what_it_lacks_of_the_streams_a_guard_hides(string shape)
    {
        // Object stream 10's dictionary runs past MaxObjectLength, so the chain's member of it, object 4, is kept through
        // the rebuild settling makes for row 9; the chain's row for object 7, which places it in object stream 11, read
        // whole and lacking it, is not, and neither is its row for 4 where the rebuild found 4 written directly, or where an
        // object stream written later defines 10 again, as no object stream. Each reads as a whole read, which no guard
        // hides, reads it.
        var file = HiddenAndWholeObjectStreams(
            direct: shape == "a member written again directly", unindexed: 0, redefined: shape == "a member of a stream a later object stream defines again");
        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 128 } });

        whole.Reader.SettleIndex();
        document.Reader.SettleIndex();

        document.WasRepaired.Should().BeTrue();
        document.Reader.ObjectNumbers.Order().Should().Equal(whole.Reader.ObjectNumbers.Order());
        document.Reader.ObjectNumbers.Should().NotContain(7);

        if (shape == "a member written again directly")
        {
            document.Reader.Index.TryGet(4, out var four).Should().BeTrue();
            four.Kind.Should().Be(XRefEntryKind.Regular, "an object written directly past the object stream that holds a copy of it wins over that copy");
            document.Reader.IsCutAtLimit(4).Should().BeFalse();
            document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("direct four");
            whole.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("direct four");
        }
        else if (shape == "a member of a stream a later object stream defines again")
        {
            document.Reader.ObjectNumbers.Should().NotContain(4);
            document.GetObject(new PdfObjectId(10)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("ten");
        }
        else
        {
            document.GetObject(new PdfObjectId(4)).Should().BeSameAs(PdfNull.Instance);
            document.Reader.IsCutAtLimit(4).Should().BeTrue("the guard hides the stream that holds it");
        }
    }

    [Fact]
    public void A_rebuild_that_fills_the_index_keeps_no_member_a_guard_hides_past_its_bound()
    {
        // The same file, with six objects no row indexes: the rebuild settling makes fills MaxIndexEntries with what it
        // found, and has no room left for object stream 10's member, which the chain gave.
        var file = HiddenAndWholeObjectStreams(direct: false, unindexed: 6);
        using var document = PdfDocument.Open(
            file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 128, MaxIndexEntries = 14 } });

        document.Reader.SettleIndex();

        document.WasRepaired.Should().BeTrue();
        document.Reader.ObjectNumbers.Should().HaveCount(14).And.NotContain(4);
        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitIndexEntries);
    }

    [Theory]
    [InlineData("<< /Type /ObjStm /First 4 >>")]
    [InlineData("<< /Type /ObjStm /N 0 /First 4 >>")]
    public void An_object_stream_refused_for_its_own_fault_hides_no_member_where_a_limit_also_cut_its_data(string dictionary)
    {
        // N2-02: object stream 10's dictionary, read whole, gives no usable /N; its data runs past what MaxStreamLength lets
        // the reader read. The stream is refused for the file's fault, and the catalog it holds was taken for one the guard
        // cut: no search for catalog 5, file.root-invalid silent, and the validator's probe said the stream was not checked.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"), 0),
            [3] = (1, file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>"), 0),
            [5] = (1, file.Object(5, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [10] = (1, file.Object(10, dictionary + "\nstream\n1 0 << /Type /Catalog /Pages 2 0 R >> " + new string('x', 600) + "\nendstream"), 0),
            [1] = (2, 10, 0),
        };
        file.CrossReferenceStream(11, 12, rows, "/Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxStreamLength = 256 } });

        document.GetObject(new PdfObjectId(10)).AsStream().Required().GetRawBytes().Length.Should().Be(256);
        document.Diagnostics.Should().Contain(entry => entry.Code == PdfDiagnosticCodes.LimitStreamLength);
        document.Reader.IsCutAtLimit(1).Should().BeFalse();
        document.Reader.Structure.RootCutByGuard.Should().BeFalse();
        document.Reader.CatalogFoundAs.Should().Be(5);
        var report = new PdfValidator().Validate(document);
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeTrue();
        report.Contains(PdfValidationRuleIds.XRefCheckedInPart).Should().BeFalse("the stream's dictionary, read whole, is the file's fault");
    }

    [Fact]
    public void A_catalog_written_in_a_trailer_a_guard_cut_is_taken_as_read()
    {
        // P1-02: the catalog is written in the trailer, its /Type and /Pages past MaxTrailerLength. It led to no catalog, and
        // the reader looked for another, taking catalog 1, which the trailer does not name.
        var file = PdfTemplate.SoundWith(
            "<< /Size 4 /Root 1 0 R >>",
            "<< /Size 4 /Root << /Pad (" + new string('x', 400) + ") /Type /Catalog /Pages 2 0 R >> >>");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxTrailerLength = 300, MaxXRefSectionLength = 128 } };
        using var document = PdfDocument.Open(file, options);

        var report = new PdfValidator().Validate(document);

        document.Reader.Structure.RootCutByGuard.Should().BeTrue();
        document.Reader.CatalogFoundAs.Should().Be(0, "no other catalog is looked for");
        document.Trailer.GetRaw(PdfName.Root).Should().BeOfType<PdfDictionary>();
        document.Diagnostics.Should().NotContain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.TrailerRootRecovered);
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse(string.Join("; ", report.Findings));
        report.Contains(PdfValidationRuleIds.PageTreePageOrphaned).Should().BeFalse();
    }

    [Fact]
    public void A_catalog_written_in_a_trailer_a_guard_cut_stays_taken_as_read_through_a_rebuild()
    {
        // Q1-06: object 3's row places it inside object 4's string, and settling rebuilds the index. The catalog written in
        // the cut trailer is the trailer's /Root still, and taken as read: no search, file.root-invalid silent.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
        };
        var padding = file.Padding(4, 3000);
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>");
        rows[3] = padding + 1500;
        rows[4] = padding;
        file.Table(5, rows, "/Size 5 /Root << /Pad (" + new string('x', 400) + ") /Type /Catalog /Pages 2 0 R >>");
        using var document = PdfDocument.Open(file.Bytes, TrailerCut());

        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeTrue();
        document.Reader.Structure.RootCutByGuard.Should().BeTrue();
        document.Reader.CatalogFoundAs.Should().Be(0, "no other catalog is looked for, before the rebuild or after it");
        document.Trailer.GetRaw(PdfName.Root).Should().BeOfType<PdfDictionary>();
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse(string.Join("; ", report.Findings));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_root_naming_an_object_a_guard_left_out_of_the_index_is_taken_as_read_and_no_other_is_looked_for(bool chain)
    {
        // Final check of #175: /Root names object 1, or object 9 whose value is 1 0 R, and only the older section, which
        // MaxXRefSectionCount leaves unread, indexes 1; the newest indexes a stray catalog, 7. The reader took 7 for the
        // catalog and said the trailer's /Root leads to none, where a whole read takes 1: a /Root a guard left out of the
        // index is taken as read, null until the guard is lifted, as one a guard cut is.
        var original = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R /Lang (file) >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>")
            .BuildClassic(rootNumber: 1);
        (int Number, string Body)[] update = chain
            ? [(9, "1 0 R"), (7, "<< /Type /Catalog /Pages 2 0 R /Lang (stray) >>")]
            : [(7, "<< /Type /Catalog /Pages 2 0 R /Lang (stray) >>")];
        var file = TestPdfBuilder.AppendIncrementalUpdate(original, rootNumber: chain ? 9 : 1, update);
        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 1 } });

        var report = new PdfValidator().Validate(document);

        whole.Catalog.Required()[PdfName.Lang].Should().BeOfType<PdfString>().Which.ToText().Should().Be("file");
        document.Catalog.Should().BeNull("the catalog lies in the section the guard left unread");
        document.Reader.CatalogFoundAs.Should().Be(0, "object 7 is another object than the one /Root names");
        document.Reader.Structure.RootHiddenByGuard.Should().BeTrue();
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitXRefSectionCount);
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse(string.Join("; ", report.Findings));
    }

    [Fact]
    public void A_root_a_rebuild_that_fills_the_index_leaves_out_is_taken_as_read_and_no_other_is_looked_for()
    {
        // As above, for an index a rebuild filled: startxref names no section, so the reader rebuilds the index as the
        // document opens, and MaxIndexEntries fills it before the catalog, object 1, written last; a stray catalog, 7,
        // written first, is in the index. The rebuild took 7 for the catalog.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [7] = file.Object(7, "<< /Type /Catalog /Pages 2 0 R /Lang (stray) >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>"),
        };

        for (var number = 10; number < 16; number++)
        {
            rows[number] = file.Object(number, "(filler)");
        }

        rows[1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Lang (file) >>");
        file.Table(16, rows, "/Size 16 /Root 1 0 R");
        var text = Encoding.Latin1.GetString(file.Bytes);
        var broken = Encoding.Latin1.GetBytes(text[..text.LastIndexOf("startxref", StringComparison.Ordinal)] + "startxref\n3\n%%EOF\n");
        using var whole = PdfDocument.Open(broken);
        using var document = PdfDocument.Open(broken, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 } });

        whole.WasRepaired.Should().BeTrue();
        whole.Catalog.Required()[PdfName.Lang].Should().BeOfType<PdfString>().Which.ToText().Should().Be("file");
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Contains(PdfDiagnosticCodes.LimitIndexEntries).Should().BeTrue();
        document.Catalog.Should().BeNull("the catalog lies past what the index holds");
        document.Reader.CatalogFoundAs.Should().Be(0, "object 7 is another object than the one /Root names");
        document.Diagnostics.Contains(PdfDiagnosticCodes.TrailerRootRecovered).Should().BeFalse();
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse(
            "a catalog a guard left out of the rebuilt index is unknown, not lacking (#132)");
    }

    [Fact]
    public void A_root_a_trailer_cut_before_it_hides_is_not_judged_missing()
    {
        // Q1-01: the trailer runs past MaxTrailerLength before /Root. The reader looks for the catalog among the objects the
        // index holds, as for a /Root it cannot read; file.root-invalid said the trailer has no /Root, which may lie past the
        // cut.
        var file = PdfTemplate.SoundWith("<< /Size 4 /Root 1 0 R >>", "<< /Size 4 /Pad (" + new string('x', 400) + ") /Root 1 0 R >>");
        using var document = PdfDocument.Open(file, TrailerCut());

        var report = new PdfValidator().Validate(document);

        document.Reader.CatalogFoundAs.Should().Be(1);
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse(string.Join("; ", report.Findings));
        report.Contains(PdfValidationRuleIds.XRefCheckedInPart).Should().BeTrue();
    }

    [Theory]
    [InlineData("the section count")]
    [InlineData("a trailer cut before /Root")]
    [InlineData("a table's rows cut")]
    [InlineData("a full index")]
    [InlineData("a stream's full index")]
    [InlineData("a section's dictionary cut")]
    public void A_root_a_guard_kept_the_trailers_from_giving_is_unknown_and_rebuilds_nothing(string guard)
    {
        // R2-01, R2-02, S2-01: only the oldest section indexes the catalog, and only its trailer, or the newest's past what
        // MaxTrailerLength reads, gives /Root; or the newest table's rows run past what MaxXRefSectionLength reads, or past
        // what MaxIndexEntries holds, which ends the chain at its trailer; or the newest section is a cross-reference stream
        // whose rows run past what MaxIndexEntries holds, its dictionary naming the oldest through an integer /Prev, which the
        // full index ends the chain short of (mutant R-ended-full-off); or the section the newest's /Prev names is a
        // cross-reference stream whose dictionary MaxTrailerLength cuts before it says what it is, which ends the chain there
        // (mutant R-thg-lost-off). The guard leaves the trailers the reader read without /Root: the reader rebuilt the index,
        // scanning past the guard, and file.root-invalid said the trailer has no /Root.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>"),
        };
        var first = file.Table(4, rows, "/Size 4 /Root 1 0 R");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 2 2] /Resources << >> >>");
        PdfReaderOptions options;

        if (guard == "the section count")
        {
            var middle = file.Position;
            file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n0 1\n0000000000 65535 f \n3 1\n{page:D10} 00000 n \ntrailer\n<< /Size 4 /Prev {first} >>\n"));
            var newest = file.Position;
            file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 4 /Prev {middle} >>\nstartxref\n{newest}\n%%EOF\n"));
            options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 2 } };
        }
        else if (guard is "a table's rows cut" or "a full index")
        {
            // Twenty free rows past the page's, which the guard leaves unread, then a trailer that gives no /Root.
            var newest = file.Position;
            var free = string.Concat(Enumerable.Repeat("0000000000 00000 f \n", 20));
            file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n0 1\n0000000000 65535 f \n3 1\n{page:D10} 00000 n \n4 20\n{free}trailer\n<< /Size 24 /Prev {first} >>\nstartxref\n{newest}\n%%EOF\n"));
            options = new PdfReaderOptions
            {
                Limits = guard == "a full index"
                    ? PdfReaderLimits.Default with { MaxIndexEntries = 4 }
                    : PdfReaderLimits.Default with { MaxXRefSectionLength = 128 },
            };
        }
        else if (guard == "a stream's full index")
        {
            // The same rows in a cross-reference stream, which the index fills before its row for object 6, and whose /Prev,
            // an integer, names the oldest table: the chain read with the rows does not reach it.
            var newest = file.Position;
            var data = Row(0, 0, 65535) + Row(1, page, 0) + string.Concat(Enumerable.Repeat(Row(0, 0, 0), 20)) + ">";
            file.Object(
                24,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"<< /Type /XRef /Size 25 /Index [0 1 3 21] /W [1 4 2] /Filter /ASCIIHexDecode /Prev {first} /Length {data.Length} >>\nstream\n{data}\nendstream"));
            file.Raw(string.Create(CultureInfo.InvariantCulture, $"startxref\n{newest}\n%%EOF\n"));
            options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 4 } };
        }
        else if (guard == "a section's dictionary cut")
        {
            // A cross-reference stream between them, which indexes itself alone and whose /Pad runs past the guard before
            // its /Type: the section is lost to the guard, and its /Prev to the oldest with it.
            var middle = file.Position;
            var row = string.Concat(
                (char)1, (char)((middle >> 24) & 0xFF), (char)((middle >> 16) & 0xFF), (char)((middle >> 8) & 0xFF), (char)(middle & 0xFF), (char)0, (char)0);
            file.Object(
                5,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"<< /Pad ({new string('x', 400)}) /Type /XRef /Size 6 /Index [5 1] /W [1 4 2] /Prev {first} /Length {row.Length} >>\nstream\n{row}\nendstream"));
            var newest = file.Position;
            file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n0 1\n0000000000 65535 f \n3 1\n{page:D10} 00000 n \ntrailer\n<< /Size 6 /Prev {middle} >>\nstartxref\n{newest}\n%%EOF\n"));
            options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxTrailerLength = 300 } };
        }
        else
        {
            // A sound update, whose trailer repeats /Root after an /ID long enough for the guard to cut it before.
            var id = "<" + new string('a', 400) + ">";
            var newest = file.Position;
            file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n0 1\n0000000000 65535 f \n3 1\n{page:D10} 00000 n \ntrailer\n<< /Size 4 /Prev {first} /ID [{id} {id}] /Root 1 0 R >>\nstartxref\n{newest}\n%%EOF\n"));
            options = TrailerCut();
        }

        using var document = PdfDocument.Open(file.Bytes, options);

        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeFalse("what a guard hides is no reason to rebuild the index (ADR 34)");
        document.Diagnostics.Should().NotContain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefRebuilt);
        document.Reader.Structure.RootHiddenByGuard.Should().BeTrue();
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse(string.Join("; ", report.Findings));
        report.Contains(PdfValidationRuleIds.XRefCheckedInPart).Should().BeTrue();
        using var whole = PdfDocument.Open(file.Bytes);
        whole.Catalog.Should().NotBeNull("read whole, the oldest trailer gives /Root");

        static string Row(int type, long offset, int generation) =>
            string.Create(CultureInfo.InvariantCulture, $"{type:X2}{offset:X8}{generation:X4}");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("/Catalog")]
    [InlineData("(1 0 R)")]
    [InlineData("<< /Type /Outlines >>")]
    public void A_root_written_as_no_reference_past_the_section_count_says_the_file_is_damaged(string root)
    {
        // R-roothidden-any: only the oldest section indexes the catalog, and the newest trailer, the one MaxXRefSectionCount
        // lets the chain read, gives a /Root that is no reference. The newest trailer's key wins, so no older trailer the guard
        // kept unread could give another: the /Root names no object, the file is damaged whatever lies past the cut, and the
        // reader rebuilds the index for the catalog its own index lacks, where a /Root the trailers lack would be the guard's.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>"),
        };
        var first = file.Table(4, rows, "/Size 4 /Root 1 0 R");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 2 2] /Resources << >> >>");
        var newest = file.Position;
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n0 1\n0000000000 65535 f \n3 1\n{page:D10} 00000 n \ntrailer\n<< /Size 4 /Prev {first} /Root {root} >>\nstartxref\n{newest}\n%%EOF\n"));

        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 1 } });

        var report = new PdfValidator().Validate(document);

        document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
        document.Reader.Structure.RootHiddenByGuard.Should().BeFalse();
        document.WasRepaired.Should().BeTrue();
        document.Reader.CatalogFoundAs.Should().Be(1);
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeTrue(string.Join("; ", report.Findings));
    }

    [Theory]
    [InlineData("/Prev /Garbage", "rows")]
    [InlineData("/Prev -5", "rows")]
    [InlineData("/Prev 99999999", "rows")]
    [InlineData("/XRefStm /Garbage", "rows")]
    [InlineData("/Prev /Garbage", "index")]
    public void A_trailer_past_a_guard_whose_older_sections_lie_nowhere_hides_no_root(string older, string guard)
    {
        // T3-01: the one table's rows run past what MaxXRefSectionLength reads, or past what MaxIndexEntries holds, and its
        // trailer gives no /Root and an older section that is no offset in the file: there is no older trailer for the guard
        // to have kept unread. The reader took the key's presence for one, rebuilt nothing and opened the document with no
        // catalog; it rebuilds for the /Root the file lacks, as a read of the whole table does.
        var file = new HandWrittenFile();
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>");
        var catalog = file.Object(20, "<< /Type /Catalog /Pages 2 0 R >>");
        var table = file.Position;
        var free = string.Concat(Enumerable.Repeat("0000000000 00000 f \n", 16));
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n0 21\n0000000000 65535 f \n0000000000 00000 f \n{pages:D10} 00000 n \n{page:D10} 00000 n \n{free}{catalog:D10} 00000 n \ntrailer\n<< /Size 21 {older} >>\nstartxref\n{table}\n%%EOF\n"));
        var limits = guard == "rows" ? PdfReaderLimits.Default with { MaxXRefSectionLength = 128 } : PdfReaderLimits.Default with { MaxIndexEntries = 4 };

        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = limits });

        document.Reader.Structure.TrailersHiddenByGuard.Should().BeFalse();
        document.WasRepaired.Should().BeTrue();
        document.Reader.CatalogFoundAs.Should().Be(20);
    }

    [Theory]
    [InlineData("/Prev {self}", "rows")]
    [InlineData("/Prev {self}", "index")]
    [InlineData("/Prev 99999999", "count")]
    public void A_trailer_past_a_guard_whose_older_section_is_read_or_nowhere_hides_no_root(string older, string guard)
    {
        // U3-2, U3-3: the one table's trailer gives no /Root and names, through /Prev, the table itself, which the chain has
        // read, or an offset outside the file, past the section count. No older trailer is there for the guard to have kept
        // unread: the reader rebuilds for the /Root the file lacks, as a read of the whole file does.
        var file = new HandWrittenFile();
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>");
        var catalog = file.Object(20, "<< /Type /Catalog /Pages 2 0 R >>");
        var table = file.Position;
        var free = string.Concat(Enumerable.Repeat("0000000000 00000 f \n", 16));
        var prev = older.Replace("{self}", table.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n0 21\n0000000000 65535 f \n0000000000 00000 f \n{pages:D10} 00000 n \n{page:D10} 00000 n \n{free}{catalog:D10} 00000 n \ntrailer\n<< /Size 21 {prev} >>\nstartxref\n{table}\n%%EOF\n"));
        var limits = guard switch
        {
            "rows" => PdfReaderLimits.Default with { MaxXRefSectionLength = 128 },
            "index" => PdfReaderLimits.Default with { MaxIndexEntries = 4 },
            _ => PdfReaderLimits.Default with { MaxXRefSectionCount = 1 },
        };

        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = limits });

        document.Reader.Structure.TrailersHiddenByGuard.Should().BeFalse();
        document.Reader.CatalogFoundAs.Should().Be(20);
    }

    [Fact]
    public void A_hybrid_stream_that_fills_the_index_hides_no_trailer_a_prev_outside_the_file_would_name()
    {
        // U3-2: a hybrid table's /XRefStm stream fills the index, and its trailer, which gives no /Root, names through /Prev
        // an offset past the file's end. The trailers were taken as hidden, and the document opened with no catalog.
        var file = new HandWrittenFile();
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>");
        var catalog = file.Object(20, "<< /Type /Catalog /Pages 2 0 R >>");
        var stream = file.Position;
        var data = new StringBuilder();

        for (var number = 0; number < 21; number++)
        {
            data.Append(number == 20 ? Row(1, catalog, 0) : number == 10 ? Row(1, stream, 0) : Row(0, 0, 0));
        }

        data.Append('>');
        file.Object(10, string.Create(CultureInfo.InvariantCulture, $"<< /Type /XRef /Size 21 /W [1 4 2] /Filter /ASCIIHexDecode /Length {data.Length} >>\nstream\n{data}\nendstream"));
        var table = file.Position;
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n0 4\n0000000000 65535 f \n0000000000 00000 f \n{pages:D10} 00000 n \n{page:D10} 00000 n \ntrailer\n<< /Size 21 /XRefStm {stream} /Prev 99999999 >>\nstartxref\n{table}\n%%EOF\n"));

        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 4 } });

        document.Reader.Structure.TrailersHiddenByGuard.Should().BeFalse();
        document.Reader.CatalogFoundAs.Should().Be(20);

        static string Row(int type, long offset, int generation) =>
            string.Create(CultureInfo.InvariantCulture, $"{type:X2}{offset:X8}{generation:X4}");
    }

    [Fact]
    public void A_table_that_fills_the_index_hides_the_trailer_of_the_hybrid_stream_its_xrefstm_names()
    {
        // R-names-xrefstm-off: the one table's rows fill MaxIndexEntries, and its trailer gives neither /Root nor /Prev, only an
        // /XRefStm naming the hybrid stream, whose dictionary gives /Root and whose row places the catalog. The chain ends at the
        // full index, short of the stream: its dictionary is a trailer the guard kept unread, which may give the /Root the
        // trailers lack, so the reader rebuilds nothing for it and file.root-invalid blames nothing (ADR 34).
        var file = new HandWrittenFile();
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>");
        var catalog = file.Object(20, "<< /Type /Catalog /Pages 2 0 R >>");
        var stream = file.Position;
        var data = string.Create(CultureInfo.InvariantCulture, $"01{catalog:X8}0000>");
        file.Object(10, string.Create(CultureInfo.InvariantCulture, $"<< /Type /XRef /Size 21 /Index [20 1] /W [1 4 2] /Root 20 0 R /Filter /ASCIIHexDecode /Length {data.Length} >>\nstream\n{data}\nendstream"));
        var table = file.Position;
        var free = string.Concat(Enumerable.Repeat("0000000000 00000 f \n", 16));
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n0 20\n0000000000 65535 f \n0000000000 00000 f \n{pages:D10} 00000 n \n{page:D10} 00000 n \n{free}trailer\n<< /Size 21 /XRefStm {stream} >>\nstartxref\n{table}\n%%EOF\n"));
        using var whole = PdfDocument.Open(file.Bytes);

        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 4 } });

        var report = new PdfValidator().Validate(document);

        whole.WasRepaired.Should().BeFalse();
        whole.Catalog.Should().NotBeNull("read whole, the hybrid stream's dictionary gives /Root");
        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitIndexEntries);
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
        document.Reader.Structure.RootHiddenByGuard.Should().BeTrue();
        document.WasRepaired.Should().BeFalse("what a guard hides is no reason to rebuild the index (ADR 34)");
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse(string.Join("; ", report.Findings));
    }

    [Fact]
    public void A_hybrid_stream_that_fills_the_index_hides_the_trailer_its_table_names_through_prev()
    {
        // R-thg-hybrid-off: the newest table leaves the index room, and its /XRefStm stream fills MaxIndexEntries; the table's
        // trailer gives no /Root and names through /Prev the older table, in the file and unread, whose trailer gives it. The
        // chain stops at the stream, so that trailer is the guard's to hide: the reader rebuilds nothing, and file.root-invalid
        // does not blame the file, where a whole read finds the catalog through the older trailer.
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>");
        var older = file.Table(4, new Dictionary<int, long> { [1] = catalog, [2] = pages, [3] = page }, "/Size 4 /Root 1 0 R");
        var stream = file.Position;
        var data = new StringBuilder();

        for (var number = 4; number < 11; number++)
        {
            data.Append(number == 10 ? Row(1, stream, 0) : Row(0, 0, 0));
        }

        data.Append('>');
        file.Object(10, string.Create(CultureInfo.InvariantCulture, $"<< /Type /XRef /Size 11 /Index [4 7] /W [1 4 2] /Filter /ASCIIHexDecode /Length {data.Length} >>\nstream\n{data}\nendstream"));
        var table = file.Position;
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n0 1\n0000000000 65535 f \n3 1\n{page:D10} 00000 n \ntrailer\n<< /Size 11 /XRefStm {stream} /Prev {older} >>\nstartxref\n{table}\n%%EOF\n"));
        using var whole = PdfDocument.Open(file.Bytes);

        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 4 } });

        var report = new PdfValidator().Validate(document);

        whole.WasRepaired.Should().BeFalse();
        whole.Catalog.Should().NotBeNull();
        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitIndexEntries);
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
        document.Reader.Structure.RootHiddenByGuard.Should().BeTrue();
        document.WasRepaired.Should().BeFalse("what a guard hides is no reason to rebuild the index (ADR 34)");
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse(string.Join("; ", report.Findings));

        static string Row(int type, long offset, int generation) =>
            string.Create(CultureInfo.InvariantCulture, $"{type:X2}{offset:X8}{generation:X4}");
    }

    [Theory]
    [InlineData("/Prev 21 0 R", "table rows")]
    [InlineData("/Prev 21 0 R", "table index")]
    [InlineData("/Prev 21 0 R", "stream rows")]
    [InlineData("/Prev 21 0 R", "stream index")]
    [InlineData("/Prev 99999999", "table rows")]
    [InlineData("/Prev 99999999", "table index")]
    [InlineData("/Prev 99999999", "stream rows")]
    [InlineData("/Prev 99999999", "stream index")]
    [InlineData("/Prev {self}", "table rows")]
    [InlineData("/Prev {self}", "table index")]
    [InlineData("/Prev {self}", "stream rows")]
    [InlineData("/Prev {self}", "stream index")]
    [InlineData("/Prev /Garbage", "table rows")]
    [InlineData("/Prev /Garbage", "table index")]
    [InlineData("/Prev /Garbage", "stream rows")]
    [InlineData("/Prev /Garbage", "stream index")]
    public void A_section_a_guard_stops_reports_the_faults_of_its_prev_a_whole_read_reports(string prev, string guard)
    {
        // #264: the newest section's rows run past MaxXRefSectionLength or MaxDecodedStreamLength, or fill MaxIndexEntries,
        // and its /Prev is a reference to a free row the guard let the reader read, an offset outside the file, the section
        // itself, or no offset at all. The chain ended at the section for the guard and said nothing of the /Prev, which
        // names no section whatever the guard hid: the guard is reported, then what a whole read reports, the index is
        // rebuilt for the /Root the trailers lack, as read whole, and the guard still throws where asked.
        var (file, _) = StoppedSectionFiles.Stopped(prev, stream: guard.StartsWith("stream", StringComparison.Ordinal));

        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, StoppedSectionFiles.Guard(guard));

        whole.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.XRefSectionMissing || entry.Code == PdfDiagnosticCodes.XRefChainCycle);
        document.Diagnostics[0].Code.Should().Be(StoppedSectionFiles.GuardCode(guard));
        document.Diagnostics.Skip(1).Should().Equal(whole.Diagnostics);
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeFalse();
        document.WasRepaired.Should().BeTrue();
        document.Catalog.Should().NotBeNull();

        var throwing = () => PdfDocument.Open(file, StoppedSectionFiles.Guard(guard) with { ThrowOnLimit = true });
        throwing.Should().Throw<PdfLimitExceededException>();
    }

    [Theory]
    [InlineData("/Prev 21 0 R", "[21 1]", "table rows")]
    [InlineData("/Prev 21 0 R", "[21 1]", "table index")]
    [InlineData("/Prev 21 0 R", "[21 1]", "stream rows")]
    [InlineData("/Prev 21 0 R", "[21 1]", "stream index")]
    [InlineData("/Prev 30 0 R", "[30 1]", "table rows")]
    [InlineData("/Prev 30 0 R", "[30 1]", "table index")]
    [InlineData("/Prev 30 0 R", "[30 1]", "stream rows")]
    [InlineData("/Prev 30 0 R", "[30 1]", "stream index")]
    [InlineData("/Prev 30 0 R", "[21 -1]", "table rows")]
    [InlineData("/Prev 30 0 R", "[21 -1]", "stream rows")]
    public void A_reference_a_section_a_guard_stops_gives_to_a_number_a_newer_section_refused_is_judged_as_a_whole_read_judges_it(
        string prev, string index, string guard)
    {
        // #264, beside #208: a newer cross-reference stream whose rows cannot be read declares object 21, whose free row the
        // section a guard stops reads before the cut, or object 30, whose row lies past it — or numbers its rows in a way that
        // cannot be read, which refuses every number —, and hands its /Prev on to that section, whose /Prev is a reference to
        // that number. No row past the guard can place it, every older row for it refused: the /Prev names no section
        // whatever the guard hid, and is reported as read whole, beside the guard. It was taken for one that may name a sound
        // section, the guard's, and nothing was said of it.
        var (file, _) = StoppedSectionFiles.Stopped(prev, stream: guard.StartsWith("stream", StringComparison.Ordinal), lost: index);

        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, StoppedSectionFiles.Guard(guard));

        whole.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.XRefSectionMissing);
        document.Diagnostics.Where(entry => entry.Code != StoppedSectionFiles.GuardCode(guard)).Should().Equal(whole.Diagnostics);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == StoppedSectionFiles.GuardCode(guard));
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeFalse();
        document.WasRepaired.Should().BeTrue();
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.XRefSectionNotFound).Should().BeTrue();
    }

    [Theory]
    [InlineData("table rows")]
    [InlineData("stream index")]
    public void A_reference_a_section_a_guard_stops_gives_to_a_number_no_newer_section_refused_stays_the_guard_s(string guard)
    {
        // As above, the newer stream declaring object 45, its own, and not object 30, which the /Prev names: a row past the
        // guard may place object 30, which may name a sound section, the guard's (#264).
        var (file, _) = StoppedSectionFiles.Stopped("/Prev 30 0 R", stream: guard.StartsWith("stream", StringComparison.Ordinal), lost: "[45 1]");

        using var document = PdfDocument.Open(file, StoppedSectionFiles.Guard(guard));

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionUnreadable, StoppedSectionFiles.GuardCode(guard));
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_section_a_guard_stops_whose_prev_names_a_number_it_lost_and_places_past_the_guard_stays_the_guard_s(bool stream)
    {
        // #264, beside #210: the newest section loses the row of object 10 — a table's row that is no row, or a stream's data
        // holding 30 of the 50 rows it declares, the numbers it lost those it declares — and places object 10, which holds
        // the older table's offset, in a row past where MaxXRefSectionLength cuts the table, or past where MaxIndexEntries
        // fills the index, the stream's rows placing the catalog and the page tree before it. Its /Prev is a reference
        // to object 10, which a whole read reads through that row, the numbers the section lost refused only to the sections
        // read after it: under the guard, the /Prev may name a sound section, the guard's, its trailers hidden, nothing
        // rebuilt and no /Root said missing. Refused before what the section names was judged, its own numbers were taken for
        // a newer section's, and the /Prev for one that names nothing.
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var older = file.Table(3, new Dictionary<int, long> { [1] = catalog, [2] = pages }, "/Size 3 /Root 1 0 R");
        var ten = file.Object(10, string.Create(CultureInfo.InvariantCulture, $"{older}"));
        var newest = file.Position;
        PdfReaderLimits limits;

        if (stream)
        {
            var data = new StringBuilder();

            for (var row = 0; row < 30; row++)
            {
                var (type, field2) = row switch
                {
                    1 => (1, catalog),
                    2 => (1, pages),
                    10 => (1, ten),
                    _ => (0, 0L),
                };
                data.Append((char)type).Append((char)((field2 >> 24) & 0xFF)).Append((char)((field2 >> 16) & 0xFF))
                    .Append((char)((field2 >> 8) & 0xFF)).Append((char)(field2 & 0xFF)).Append('\0').Append('\0');
            }

            file.Object(60, string.Create(CultureInfo.InvariantCulture, $"<< /Type /XRef /Size 61 /Index [0 50] /W [1 4 2] /Prev 10 0 R /Length {data.Length} >>\nstream\n{data}\nendstream"));
            limits = PdfReaderLimits.Default with { MaxIndexEntries = 5 };
        }
        else
        {
            var free = string.Concat(Enumerable.Repeat("0000000000 00000 f \n", 20));
            file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n0 1\n0000000000 65535 f \n10 1\nabcdefghij 00000 n \n11 20\n{free}10 1\n{ten:D10} 00000 n \ntrailer\n<< /Size 31 /Prev 10 0 R >>\n"));
            limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 150 };
        }

        file.Raw(string.Create(CultureInfo.InvariantCulture, $"startxref\n{newest}\n%%EOF\n"));

        using (var whole = PdfDocument.Open(file.Bytes))
        {
            whole.Reader.Structure.Sections.Should().HaveCount(2);
            whole.Reader.Structure.TrailersHiddenByGuard.Should().BeFalse();
            whole.WasRepaired.Should().BeFalse();
            whole.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
        }

        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = limits });

        document.Reader.Structure.Sections.Should().ContainSingle().Which.Incomplete.Should().BeTrue();
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
        document.WasRepaired.Should().BeFalse("what a guard hides is no reason to rebuild the index (ADR 34)");
        var report = new PdfValidator().Validate(document);
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse(string.Join("; ", report.Findings));
        report.Contains(PdfValidationRuleIds.XRefCheckedInPart).Should().BeTrue();
    }

    [Theory]
    [InlineData("999999", false, PdfDiagnosticCodes.XRefSectionMissing, PdfValidationRuleIds.XRefSectionNotFound)]
    [InlineData("{self}", false, PdfDiagnosticCodes.XRefChainCycle, PdfValidationRuleIds.XRefChainLoop)]
    [InlineData("999999", true, PdfDiagnosticCodes.XRefSectionMissing, PdfValidationRuleIds.XRefSectionNotFound)]
    public void A_table_its_length_bound_cuts_before_its_first_row_is_the_guard_s_and_its_prev_judged_whatever_the_index_holds(
        string prev, bool updated, string code, string rule)
    {
        // #264, beside ADR 34: 300 spaces follow the table's xref keyword, and MaxXRefSectionLength cuts it before its first
        // row. Its keyword declared it a section before the cut: it is kept, the guard's, whatever the index holds, and its
        // /Prev — an offset outside the file, or the table itself — names no section whatever the guard hid, which is
        // reported as read whole. Where the index held nothing — the table startxref names, or one an update that indexes
        // nothing names —, the table was taken for one that cannot be read, and its /Prev went unjudged. The chain leaves the
        // index empty, which is rebuilt at opening, as for any chain a guard leaves no row (ADR 34).
        var (file, table) = StoppedSectionFiles.CutBeforeItsRows(prev, updated);

        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 150 } });

        var judged = whole.Diagnostics.Should().ContainSingle().Which;
        judged.Code.Should().Be(code);
        document.Diagnostics.Should().Equal(document.Diagnostics[0], judged, document.Diagnostics[2]);
        document.Diagnostics[0].Code.Should().Be(PdfDiagnosticCodes.LimitXRefSectionLength);
        document.Diagnostics[0].Position.Should().Be(table);
        document.Diagnostics[2].Code.Should().Be(PdfDiagnosticCodes.XRefRebuilt);
        var section = document.Reader.Structure.Sections.Single(record => record.Offset == table);
        section.State.Should().Be(XRefSectionState.Read);
        section.CutByLimit.Should().BeTrue();
        new PdfValidator().Validate(whole).Contains(rule).Should().BeTrue();
        new PdfValidator().Validate(document).Contains(rule).Should().BeTrue();
    }

    [Theory]
    [InlineData("/Prev 30 0 R", "table rows")]
    [InlineData("/Prev 30 0 R", "table index")]
    [InlineData("/Prev 30 0 R", "stream rows")]
    [InlineData("/Prev 30 0 R", "stream index")]
    [InlineData("/XRefStm 30 0 R", "table rows")]
    [InlineData("/XRefStm 30 0 R", "table index")]
    [InlineData("/Prev {older}", "table rows")]
    [InlineData("/Prev {older}", "table index")]
    [InlineData("/Prev {older}", "stream rows")]
    [InlineData("/Prev {older}", "stream index")]
    [InlineData("/Prev 0", "table rows")]
    [InlineData("/Prev 0", "table index")]
    [InlineData("/Prev 0", "stream rows")]
    [InlineData("/Prev 0", "stream index")]
    [InlineData("/XRefStm 0", "table rows")]
    [InlineData("/XRefStm 0", "table index")]
    public void A_link_a_section_a_guard_stops_gives_that_may_be_sound_stays_the_guard_s(string links, string guard)
    {
        // #264: the /Prev, or a table's /XRefStm, is a reference to a row the guard left unread — free, which a whole read
        // reports —, or names the older section, in the file, or offset 0, where the header starts, which the chain has not
        // reached, and near which a whole read finds a section. Each may name a sound section as far as the reader can tell
        // without reading past the guard: the chain ends there for the guard, which is the only report, and the older
        // trailers are the guard's to hide.
        var (file, _) = StoppedSectionFiles.Stopped(links, stream: guard.StartsWith("stream", StringComparison.Ordinal));

        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, StoppedSectionFiles.Guard(guard));

        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(StoppedSectionFiles.GuardCode(guard));
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
        document.Reader.Structure.Sections.Should().ContainSingle();
        document.WasRepaired.Should().BeFalse();
        whole.Diagnostics.Any(entry => entry.Code == PdfDiagnosticCodes.XRefSectionMissing).Should().Be(links.EndsWith("30 0 R", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/Prev 0", "table rows")]
    [InlineData("/Prev 0", "table index")]
    [InlineData("/Prev 0", "stream rows")]
    [InlineData("/Prev 0", "stream index")]
    [InlineData("/XRefStm 0", "table rows")]
    [InlineData("/XRefStm 0", "table index")]
    public void A_link_naming_offset_zero_where_a_section_a_guard_stops_starts_is_judged_as_a_whole_read_judges_it(string links, string guard)
    {
        // #264: a file with no header starts with the section a guard stops, and its /Prev, or its /XRefStm, names offset 0,
        // the section itself, which the chain has read: a loop, or an /XRefStm that names the table itself, reported as read
        // whole, beside the guard. Offset 0 is an offset like any other.
        var file = StoppedSectionFiles.AtTheFirstByte(links, stream: guard.StartsWith("stream", StringComparison.Ordinal));

        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, StoppedSectionFiles.Guard(guard));

        whole.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.XRefChainCycle || entry.Code == PdfDiagnosticCodes.XRefHybridStreamLost);
        document.Diagnostics.Where(entry => entry.Code != StoppedSectionFiles.GuardCode(guard)).Should().Equal(whole.Diagnostics);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == StoppedSectionFiles.GuardCode(guard));
    }

    [Theory]
    [InlineData("/Prev {older}", "table index")]
    [InlineData("/Prev {older}", "stream index")]
    [InlineData("/Prev 30 0 R", "stream index")]
    public void A_section_at_offset_zero_that_fills_the_index_ends_the_chain_there_as_any_other(string links, string guard)
    {
        // #264: the section a guard stops starts the file, which has no header, and fills the index at offset 0: the chain
        // ends there, its /Prev — the older table, in the file, or a reference to a row past the full point — the guard's.
        var file = StoppedSectionFiles.AtTheFirstByte(links, stream: guard.StartsWith("stream", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file, StoppedSectionFiles.Guard(guard));

        document.Reader.IndexFullAt.Should().Be(0);
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.HeaderMissing, PdfDiagnosticCodes.LimitIndexEntries);
        document.Reader.Structure.Sections.Should().ContainSingle();
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
    }

    [Theory]
    [InlineData("99999999", 0, "stream rows")]
    [InlineData("99999999", 0, "stream index")]
    [InlineData("{self}", 0, "stream rows")]
    [InlineData("{self}", 0, "stream index")]
    [InlineData("{stream}", 0, "stream rows")]
    [InlineData("{stream}", 0, "stream index")]
    [InlineData("{stream}", -4, "stream rows")]
    [InlineData("{stream}", -4, "stream index")]
    public void A_table_whose_hybrid_stream_a_guard_stops_reports_the_faults_of_its_prev_a_whole_read_reports(string prev, int shift, string guard)
    {
        // #264: the table is read whole, and the cross-reference stream its /XRefStm names is the section the guard stops,
        // which ends the chain. The table's /Prev — an offset outside the file, the table itself, or the stream, where
        // /XRefStm names it or where it was found moved — names no older section whatever the guard hid: it is reported as
        // read whole, beside the guard. A /Prev that names what /XRefStm names reads the stream itself, which the guard then
        // stops, and the /XRefStm is reported as naming no stream of the table's revision (#156).
        var (file, _) = StoppedSectionFiles.HybridWhoseStreamIsStopped(prev, shift);

        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, StoppedSectionFiles.Guard(guard));

        whole.Diagnostics.Should().ContainSingle(entry =>
            entry.Code == PdfDiagnosticCodes.XRefSectionMissing ||
            entry.Code == PdfDiagnosticCodes.XRefChainCycle ||
            entry.Code == PdfDiagnosticCodes.XRefHybridStreamLost);
        document.Diagnostics.Where(entry => !entry.Code.StartsWith("limit.", StringComparison.Ordinal)).Should().Equal(whole.Diagnostics);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == StoppedSectionFiles.GuardCode(guard));
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeFalse();
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData("stream rows")]
    [InlineData("stream index")]
    public void A_prev_naming_offset_zero_of_a_table_whose_hybrid_stream_a_guard_stops_stays_the_guard_s(string guard)
    {
        // #264: as above, the table's /Prev naming offset 0, the header's line, near which a whole read finds the older table
        // and reads its /Root. Offset 0 lies in the file, and the chain has not reached what it names: it may name a sound
        // section, as any other offset in the file may, the guard's, and so are the trailers past it. The guard is the only
        // report, and nothing is rebuilt for the /Root the table's trailer lacks.
        var (file, _) = StoppedSectionFiles.HybridWhoseStreamIsStopped("0");

        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, StoppedSectionFiles.Guard(guard));

        whole.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted);
        whole.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(StoppedSectionFiles.GuardCode(guard));
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
        document.WasRepaired.Should().BeFalse("what a guard hides is no reason to rebuild the index (ADR 34)");
    }

    [Theory]
    [InlineData("/Prev {older} /XRefStm 99999999", "table rows")]
    [InlineData("/Prev {older} /XRefStm 99999999", "table index")]
    [InlineData("/Prev {older} /XRefStm {self}", "table rows")]
    [InlineData("/Prev {older} /XRefStm {self}", "table index")]
    [InlineData("/Prev {older} /XRefStm {newer}", "table rows")]
    [InlineData("/Prev {older} /XRefStm {newer}", "table index")]
    [InlineData("/Prev 99999999 /XRefStm 99999999", "table rows")]
    [InlineData("/Prev 99999999 /XRefStm 99999999", "table index")]
    [InlineData("/XRefStm 99999999", "table rows")]
    [InlineData("/XRefStm 99999999", "table index")]
    [InlineData("/XRefStm {self}", "table rows")]
    [InlineData("/XRefStm {self}", "table index")]
    public void An_xrefstm_a_table_a_guard_stops_gives_is_judged_as_a_whole_read_judges_it(string links, string guard)
    {
        // #264, with #156: the table a guard stops, under a newer one, names through /XRefStm an offset outside the file, the
        // table itself, the newer table, or what its /Prev names too, outside the file, with or without a /Prev. None is a
        // cross-reference stream of the table's revision, whatever the guard hid: each is reported as read whole — a section
        // missing, or an /XRefStm that names no stream —, beside the guard, and a /Prev outside the file with it. A /Prev
        // that names the older table, in the file, is the guard's, and so are the trailers past it.
        var (file, _) = StoppedSectionFiles.Stopped(links, newer: "/Prev");

        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, StoppedSectionFiles.Guard(guard));

        whole.Diagnostics.Should().Contain(entry => entry.Code == PdfDiagnosticCodes.XRefSectionMissing || entry.Code == PdfDiagnosticCodes.XRefHybridStreamLost);
        document.Diagnostics[0].Code.Should().Be(StoppedSectionFiles.GuardCode(guard));
        document.Diagnostics.Skip(1).Should().Equal(whole.Diagnostics);
        document.Reader.Structure.TrailersHiddenByGuard.Should().Be(links.StartsWith("/Prev {older}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("table rows")]
    [InlineData("table index")]
    public void An_xrefstm_naming_what_the_prev_of_a_table_a_guard_stops_names_is_reported_and_the_prev_stays_the_guard_s(string guard)
    {
        // #264, with #156: the table a guard stops names the older table, in the file, through /Prev and /XRefStm both. That
        // /XRefStm names no stream of the table's revision whatever the guard hid, as read whole, where the /Prev looks for
        // that table, and reads it; past the guard, the /Prev reads nothing, which the report says, and the older table is the
        // guard's to hide.
        var (file, section) = StoppedSectionFiles.Stopped("/Prev {older} /XRefStm {older}");

        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, StoppedSectionFiles.Guard(guard));

        var read = whole.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.XRefHybridStreamLost).Which;
        var guarded = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.XRefHybridStreamLost).Which;
        guarded.Position.Should().Be(read.Position);
        read.Message.Should().Contain("what its /Prev names too, which the chain looks for through that /Prev:");
        guarded.Message.Should().Be(read.Message.Replace(
            "what its /Prev names too, which the chain looks for through that /Prev",
            "the section its /Prev names too, past the guard that stopped the table",
            StringComparison.Ordinal));
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(StoppedSectionFiles.GuardCode(guard), PdfDiagnosticCodes.XRefHybridStreamLost);
        var naming = document.Reader.Structure.Sections.Should().ContainSingle(record => record.NamedBy == "/XRefStm").Which;
        naming.State.Should().Be(XRefSectionState.NamedAgain);
        naming.NamedFrom.Should().Be(section);
        naming.ReadAs.Should().BeNull("past the guard, the /Prev reads nothing");
        document.Reader.Structure.Sections.Should().HaveCount(2);
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_prev_looping_back_to_a_stream_a_guard_cut_says_the_chain_only_named_it(int namedShortBy)
    {
        // #264, beside #263 and #156: the newest table's /XRefStm names cross-reference stream 44 at its header, or at the end
        // of line before it, where no section read yet places the objects its /Type and its /Size refer to: it declares no
        // section, and is reported as one that cannot be read. The older table places them, and its /XRefStm reaches the
        // stream through the other offset: it gives /Type /XRef now, and its /Size, object 7, a comment of 3,000 bytes then 1,
        // is one MaxObjectLength cuts. The stream is the guard's, no section the chain reached and could not read: the older
        // table's /Prev, which names again what the newest table's /XRefStm named, loops back to an offset the chain only
        // named. Read whole, the older table's /XRefStm reads the stream, which the loop then reaches.
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>");
        var type = file.Object(5, "/XRef");
        var size = file.Object(7, "%" + new string('-', 3000) + "\n1");
        var stream = file.Position;
        file.Raw("44 0 obj\n<< /Type 5 0 R /Size 7 0 R /W [1 4 2] /Filter /ASCIIHexDecode /Length 15 >>\nstream\n0000000000FFFF>\nendstream\nendobj\n");
        file.Raw("%" + new string('-', 600) + "\n");
        var named = stream - namedShortBy;
        var older = file.Table(
            8,
            new Dictionary<int, long> { [1] = catalog, [2] = pages, [3] = page, [5] = type, [7] = size },
            string.Create(CultureInfo.InvariantCulture, $"/Size 45 /Root 1 0 R /XRefStm {stream - 1 + namedShortBy} /Prev {named}"));
        file.Table(1, [], string.Create(CultureInfo.InvariantCulture, $"/Size 45 /XRefStm {named} /Prev {older}"));

        using var whole = PdfDocument.Open(file.Bytes);
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } });

        whole.Reader.Structure.LoopTarget.Should().NotBe(XRefLoopTarget.Named, "read whole, the stream is a section the chain reached");
        document.Diagnostics.Select(entry => (entry.Code, entry.Position)).Should().Equal(
            (PdfDiagnosticCodes.XRefSectionUnreadable, named),
            (PdfDiagnosticCodes.XRefChainCycle, named),
            (PdfDiagnosticCodes.LimitObject, size));
        document.Diagnostics[1].Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference chain loops back on itself: the /Prev of the section at offset {older} names offset {named}, which the chain has already named."));
        document.Reader.Structure.LoopOffset.Should().Be(named);
        document.Reader.Structure.LoopTarget.Should().Be(XRefLoopTarget.Named);
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData("table rows")]
    [InlineData("table index")]
    public void An_xrefstm_in_the_file_a_table_a_guard_stops_names_stays_the_guard_s(string guard)
    {
        // #264: the table a guard stops names through /XRefStm the older table, in the file, and through /Prev an offset
        // outside the file. Whether the /XRefStm names a stream is known only by reading past the guard, which a whole read
        // does, finding a table in the stream's place: it is the guard's to hide, and nothing there is read. The /Prev names no
        // section whatever the guard hid, and is reported as read whole.
        var (file, _) = StoppedSectionFiles.Stopped("/Prev 99999999 /XRefStm {older}");

        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, StoppedSectionFiles.Guard(guard));

        whole.Diagnostics.Should().Contain(entry => entry.Code == PdfDiagnosticCodes.XRefHybridStreamLost);
        var missing = whole.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.XRefSectionMissing).Which;
        document.Diagnostics.Should().Equal(document.Diagnostics[0], missing);
        document.Diagnostics[0].Code.Should().Be(StoppedSectionFiles.GuardCode(guard));
        document.Reader.Structure.Sections.Should().NotContain(record => record.NamedBy == "/XRefStm");
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
    }

    [Theory]
    [InlineData("table rows")]
    [InlineData("table index")]
    public void A_table_read_in_a_stream_s_place_leaves_its_links_unjudged_where_a_guard_stops_its_rows_as_read_whole(string guard)
    {
        // #264, with #263: the newest table's /XRefStm names a classic table, read in the stream's place, whose rows a guard
        // stops and whose /Prev is no offset. A whole read leaves that table's links unread, as a hybrid file's stream's
        // own /Prev: so does a read past the guard, which reports the guard beside what the whole read reports.
        var (file, _) = StoppedSectionFiles.Stopped("/Prev /Garbage /XRefStm 99999999", newer: "/XRefStm");

        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, StoppedSectionFiles.Guard(guard));

        whole.Diagnostics.Should().Contain(entry => entry.Code == PdfDiagnosticCodes.XRefHybridStreamLost);
        whole.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.XRefSectionMissing);
        document.Diagnostics.Where(entry => entry.Code != StoppedSectionFiles.GuardCode(guard)).Should().Equal(whole.Diagnostics);
        document.Reader.Structure.Sections[1].CutByLimit.Should().BeTrue();
    }

    [Theory]
    [InlineData("table rows")]
    [InlineData("table index")]
    public void A_prev_in_a_trailer_a_guard_cut_past_rows_a_guard_stopped_is_left_to_the_guards(string guard)
    {
        // #264: the trailer past the rows a guard stopped runs past MaxTrailerLength, which cuts it after its /Prev, an offset
        // outside the file that a whole read reports. A value a cut trailer gives may itself be cut, which the parser does
        // not say: the /Prev is not judged, and the chain ends for the guards (#328).
        var (file, _) = StoppedSectionFiles.Stopped($"/Prev 99999999 /Pad ({new string('x', 400)})");
        var limits = StoppedSectionFiles.Guard(guard).Limits with { MaxTrailerLength = 300 };

        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        whole.Diagnostics.Should().Contain(entry => entry.Code == PdfDiagnosticCodes.XRefSectionMissing);
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(StoppedSectionFiles.GuardCode(guard), PdfDiagnosticCodes.LimitTrailer);
        document.Reader.Structure.Sections.Should().ContainSingle().Which.TrailerCutByLimit.Should().BeTrue();
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
    }

    [Theory]
    [InlineData("/Prev 21 0 R", "table rows")]
    [InlineData("/Prev 21 0 R", "table index")]
    [InlineData("/Prev 21 0 R", "stream rows")]
    [InlineData("/Prev 21 0 R", "stream index")]
    [InlineData("/XRefStm 21 0 R", "table rows")]
    [InlineData("/XRefStm 21 0 R", "table index")]
    public void A_link_a_section_a_guard_stops_writes_as_a_reference_to_an_object_the_object_guard_cut_stays_the_guard_s(string links, string guard)
    {
        // #264, beside #263: the section a guard stops gives its /Prev, or its /XRefStm, as a reference to object 21, a row it
        // read before the cut placing a comment of 3,000 bytes and then the older table's offset, which MaxObjectLength cuts.
        // What the link names is unknown, as in a section read whole: nothing is blamed on the file, nothing is rebuilt, and
        // the trailers past it are the guards' to hide. The section's own cut says what lies past it was not checked: the
        // link is the section's guard's, and is not said hidden by the object guard as well.
        var (file, _) = StoppedSectionFiles.Stopped(
            links,
            stream: guard.StartsWith("stream", StringComparison.Ordinal),
            twentyOne: "% " + new string('-', 3000) + "\n{older}");
        var limits = StoppedSectionFiles.Guard(guard).Limits with { MaxObjectLength = 1024 };

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(StoppedSectionFiles.GuardCode(guard), PdfDiagnosticCodes.LimitObject);
        document.WasRepaired.Should().BeFalse();
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
        document.Reader.Structure.Sections.Should().ContainSingle();
        var report = new PdfValidator().Validate(document);
        report.Contains(PdfValidationRuleIds.XRefSectionNotFound).Should().BeFalse();
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse();
        report.Findings.Should().ContainSingle(finding => finding.RuleId == PdfValidationRuleIds.XRefCheckedInPart);
        document.Reader.Structure.Sections[0].ChainHiddenByGuard.Should().BeNull();
    }

    [Theory]
    [InlineData("99999999")]
    [InlineData("21 0 R")]
    [InlineData("{self}")]
    public void A_stream_a_guard_stops_where_it_was_found_moved_reports_the_faults_of_its_prev_a_whole_read_reports(string prev)
    {
        // #264: the newest table's /Prev names cross-reference stream 44 four bytes before its header, where it is found, and
        // MaxDecodedStreamLength cuts its rows. Its /Prev — an offset outside the file, a reference to a free row, or where the
        // stream is, which the chain read though it named another offset — names no older section whatever the guard hid,
        // and is reported as read whole, beside the guard.
        var file = StoppedSectionFiles.StreamFoundMoved(prev);

        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, StoppedSectionFiles.Guard("stream rows"));

        whole.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.XRefSectionMissing || entry.Code == PdfDiagnosticCodes.XRefChainCycle);
        document.Diagnostics[0].Code.Should().Be(PdfDiagnosticCodes.LimitDecodedStream);
        document.Diagnostics.Skip(1).Should().Equal(whole.Diagnostics);
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeFalse();
        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void A_stream_a_guard_stops_where_it_was_found_moved_leaves_an_older_section_in_the_file_to_the_guard()
    {
        // #264: as above, the stream's /Prev naming the older table, in the file: it may be sound, and is the guard's.
        var file = StoppedSectionFiles.StreamFoundMoved("{older}");

        using var document = PdfDocument.Open(file, StoppedSectionFiles.Guard("stream rows"));

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream, PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData("rows")]
    [InlineData("index")]
    public void A_prev_written_as_a_reference_a_guard_kept_unread_hides_the_trailers(string guard)
    {
        // U3-1: the newest table's trailer gives no /Root, and its /Prev is a reference to object 21, an integer naming the
        // older table, whose row lies past what the guard read. The reference was taken to name no section: the reader
        // rebuilt the index past the guard, and file.root-invalid blamed the file. It is unknown, as the trailer it names.
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>");
        var older = file.Table(4, new Dictionary<int, long> { [1] = catalog, [2] = pages, [3] = page }, "/Size 4 /Root 1 0 R");
        var prev = file.Object(21, older.ToString(CultureInfo.InvariantCulture));
        var table = file.Position;
        var free = string.Concat(Enumerable.Repeat("0000000000 00000 f \n", 17));
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n0 1\n0000000000 65535 f \n2 2\n{pages:D10} 00000 n \n{page:D10} 00000 n \n4 17\n{free}21 1\n{prev:D10} 00000 n \ntrailer\n<< /Size 22 /Prev 21 0 R >>\nstartxref\n{table}\n%%EOF\n"));
        var limits = guard == "rows" ? PdfReaderLimits.Default with { MaxXRefSectionLength = 128 } : PdfReaderLimits.Default with { MaxIndexEntries = 4 };

        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = limits });

        var report = new PdfValidator().Validate(document);

        document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
        document.WasRepaired.Should().BeFalse();
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse(string.Join("; ", report.Findings));
    }

    [Theory]
    [InlineData("free", "/Prev 21 0 R")]
    [InlineData("another generation", "/Prev 21 1 R")]
    [InlineData("object 0", "/Prev 0 0 R")]
    [InlineData("a stream", "/Prev 21 0 R")]
    [InlineData("another object's", "/Prev 21 0 R")]
    [InlineData("refused", "/Prev 21 0 R")]
    [InlineData("another object's, past the object guard", "/Prev 21 0 R")]
    [InlineData("another generation's, past the object guard", "/Prev 21 0 R")]
    [InlineData("a chain past the cut", "/Prev 21 0 R")]
    [InlineData("an object past the object guard", "/Prev 21 0 R")]
    public void A_prev_written_as_a_reference_reads_past_a_guard_as_a_whole_read_reads_it(string row21, string prev)
    {
        // V2-1, V2-3, W1-5: the newest table's rows run past what MaxXRefSectionLength reads, its trailer gives no /Root, and
        // its /Prev is a reference whose row the guard read: a free one, of another generation, object 0, a stream, the row
        // of another object, whose body may run past MaxObjectLength (X1-2) — which a whole read reads as no section, and
        // rebuilds for the /Root the file lacks —; or a chain to an object whose row lies past the cut, or an object that
        // runs past MaxObjectLength, unknown. Each read as unknown, or as no section, alike.
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>");
        var older = file.Table(4, new Dictionary<int, long> { [1] = catalog, [2] = pages, [3] = page }, "/Size 4 /Root 1 0 R");
        var oldest = older.ToString(CultureInfo.InvariantCulture);
        var longBody = "%" + new string('c', 3_000) + "\n" + oldest;
        var twentyOne = row21 switch
        {
            "another object's, past the object guard" => file.Object(30, longBody),
            "another generation's, past the object guard" => file.Object(21, longBody, generation: 1),
            _ => file.Object(21, row21 switch
            {
                "a stream" => "<< /Length 3 >>\nstream\n123\nendstream",
                "a chain past the cut" => "22 0 R",
                "an object past the object guard" => longBody,
                _ => oldest,
            }),
        };
        var twentyTwo = row21 == "a chain past the cut" ? file.Object(22, oldest) : -1;
        var table = file.Position;
        var placed = row21 switch
        {
            "free" => "0000000000 00000 f \n",
            "another object's" => string.Create(CultureInfo.InvariantCulture, $"{pages:D10} 00000 n \n"),
            "refused" => string.Create(CultureInfo.InvariantCulture, $"{twentyOne:D10} 99999 n \n"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{twentyOne:D10} 00000 n \n"),
        };
        var lastRow = twentyTwo < 0 ? "0000000000 00000 f \n" : string.Create(CultureInfo.InvariantCulture, $"{twentyTwo:D10} 00000 n \n");
        var free = string.Concat(Enumerable.Repeat("0000000000 00000 f \n", 17));
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n0 1\n0000000000 65535 f \n21 1\n{placed}2 2\n{pages:D10} 00000 n \n{page:D10} 00000 n \n4 17\n{free}22 1\n{lastRow}trailer\n<< /Size 23 {prev} >>\nstartxref\n{table}\n%%EOF\n"));
        using var whole = PdfDocument.Open(file.Bytes);

        var limits = PdfReaderLimits.Default with
        {
            MaxXRefSectionLength = 128,
            MaxObjectLength = row21.EndsWith("past the object guard", StringComparison.Ordinal) ? 1_024 : PdfReaderLimits.Default.MaxObjectLength,
        };
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = limits });

        if (row21 is "a chain past the cut" or "an object past the object guard")
        {
            document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
            document.WasRepaired.Should().BeFalse();
        }
        else
        {
            document.Reader.Structure.TrailersHiddenByGuard.Should().BeFalse();
            document.WasRepaired.Should().Be(whole.WasRepaired).And.BeTrue();
            (document.Catalog is null).Should().Be(whole.Catalog is null);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_prev_a_stream_that_fills_the_index_writes_as_a_reference_past_the_full_point_is_no_fault_of_the_file(bool chain)
    {
        // V2-2: the newest section, a cross-reference stream, fills MaxIndexEntries before its row for object 21, which
        // holds the older table's offset its /Prev names as 21 0 R, or through object 5, which holds 21 0 R. The reader
        // took the guard to hide the trailers, and xref.section-not-found said the file's /Prev names nothing.
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>");
        var older = file.Table(4, new Dictionary<int, long> { [1] = catalog, [2] = pages, [3] = page }, "/Size 4 /Root 1 0 R");
        var twentyOne = file.Object(21, older.ToString(CultureInfo.InvariantCulture));
        var five = chain ? file.Object(5, "21 0 R") : -1;
        var stream = file.Position;
        var data = new StringBuilder();

        foreach (var number in new[] { 0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22 })
        {
            data.Append(number switch
            {
                2 => Row(1, pages, 0),
                3 => Row(1, page, 0),
                5 when chain => Row(1, five, 0),
                21 => Row(1, twentyOne, 0),
                22 => Row(1, stream, 0),
                0 => Row(0, 0, 65535),
                _ => Row(0, 0, 0),
            });
        }

        data.Append('>');
        file.Object(22, string.Create(CultureInfo.InvariantCulture, $"<< /Type /XRef /Size 23 /Index [0 1 2 21] /W [1 4 2] /Filter /ASCIIHexDecode /Prev {(chain ? 5 : 21)} 0 R /Length {data.Length} >>\nstream\n{data}\nendstream"));
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"startxref\n{stream}\n%%EOF\n"));

        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 } });

        var report = new PdfValidator().Validate(document);

        document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
        document.Diagnostics.Should().NotContain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefSectionMissing);
        report.Contains(PdfValidationRuleIds.XRefSectionNotFound).Should().BeFalse(string.Join("; ", report.Findings));
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse(string.Join("; ", report.Findings));

        static string Row(int type, long offset, int generation) =>
            string.Create(CultureInfo.InvariantCulture, $"{type:X2}{offset:X8}{generation:X4}");
    }

    [Theory]
    [InlineData("object 0, with no row")]
    [InlineData("object 0, its row past the full point")]
    [InlineData("a number no row declares")]
    [InlineData("a number whose row was refused")]
    public void A_prev_a_stream_that_fills_the_index_writes_as_a_reference_no_row_past_it_places_reads_as_a_whole_read_reads_it(string shape)
    {
        // W1-1, W1-2, W2-1: the newest section, a cross-reference stream with no /Root, fills MaxIndexEntries; its /Prev names
        // object 0, which no read places, a number its /Index declares no row for — tiff2pdf's /Prev 576066 0 R —, or one
        // whose row it refused. No row the guard left unread places any of them: the reader took each for one its row might,
        // hid the trailers and said nothing, where a whole read reports the /Prev, rebuilds and finds the catalog.
        var (index, rows, prev) = shape switch
        {
            "object 0, with no row" => ("[2 21]", Numbers(2, 21), "0 0 R"),
            "object 0, its row past the full point" => ("[2 21 0 1]", [.. Numbers(2, 21), 0], "0 0 R"),
            "a number no row declares" => ("[0 1 2 21]", [0, .. Numbers(2, 21)], "576066 0 R"),
            _ => ("[0 1 2 21]", [0, .. Numbers(2, 21)], "21 0 R"),
        };
        var file = NewestStreamNaming(index, rows, prev, refused: shape == "a number whose row was refused" ? 21 : -1);
        var limits = PdfReaderLimits.Default with { MaxIndexEntries = shape == "a number whose row was refused" ? 21 : 8 };
        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        var report = new PdfValidator().Validate(document);

        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitIndexEntries);
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeFalse();
        whole.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefSectionMissing);
        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefSectionMissing);
        report.Contains(PdfValidationRuleIds.XRefSectionNotFound).Should().BeTrue(string.Join("; ", report.Findings));
        document.WasRepaired.Should().Be(whole.WasRepaired).And.BeTrue();
        document.Catalog.Should().NotBeNull();
    }

    [Theory]
    [InlineData("the first row the index had no room for", "9 0 R", true)]
    [InlineData("the last row declared", "22 0 R", true)]
    [InlineData("one past the last row declared", "23 0 R", false)]
    [InlineData("a row of a later subsection", "31 0 R", true)]
    [InlineData("the first object's row, declared last", "1 0 R", true)]
    [InlineData("a number between two subsections", "25 0 R", false)]
    [InlineData("a row past the data a guard cut", "21 0 R", true)]
    [InlineData("a row its data, decoded whole, does not hold", "25 0 R", false)]
    public void A_prev_a_stream_a_guard_stopped_writes_as_a_reference_is_unknown_only_for_a_row_it_left_unread(string shape, string prev, bool hidden)
    {
        // The newest section, a cross-reference stream with no /Root, holds rows for 0 and 2 to 22 — or, for the later
        // subsection, 2 to 21 then 30 to 32, and for the first object's row, 2 to 22 then 1 —, and MaxIndexEntries fills at
        // the row for object 9; or MaxDecodedStreamLength leaves it the data of ten rows. Its /Prev names an object whose row
        // the guard left unread — object 1 as any other, only object 0 being one no read places —, which may place the
        // older section: the trailers are hidden. One past the rows the stream declares is the file's to lack, as a whole
        // read finds: reported, and the index rebuilt for the /Root; and so is one it declares but its data, decoded whole,
        // does not hold (X1-1), or one between two subsections, which no row numbers.
        var file = shape switch
        {
            "a row of a later subsection" or "a number between two subsections" => NewestStreamNaming("[0 1 2 20 30 3]", [0, .. Numbers(2, 20), 30, 31, 32], prev, refused: -1, size: 33),
            "a row its data, decoded whole, does not hold" => NewestStreamNaming("[0 1 2 21 25 1]", [0, .. Numbers(2, 21)], prev, refused: -1, size: 26),
            "the first object's row, declared last" => NewestStreamNaming("[0 1 2 21 1 1]", [0, .. Numbers(2, 21), 1], prev, refused: -1),
            _ => NewestStreamNaming("[0 1 2 21]", [0, .. Numbers(2, 21)], prev, refused: -1),
        };
        var limits = shape == "a row past the data a guard cut"
            ? PdfReaderLimits.Default with { MaxDecodedStreamLength = 70 }
            : PdfReaderLimits.Default with { MaxIndexEntries = 8 };
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.StartsWith("limit.", StringComparison.Ordinal));
        document.Reader.Structure.TrailersHiddenByGuard.Should().Be(hidden);
        document.Diagnostics.Any(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefSectionMissing).Should().Be(!hidden);
    }

    [Fact]
    public void A_prev_reference_in_a_table_a_guard_cut_after_a_stream_read_whole_is_unknown()
    {
        // The newest section, a cross-reference stream read whole, names an older table whose rows run past what
        // MaxXRefSectionLength reads; that table's /Prev is a reference to object 21, whose row lies past the cut. A table's
        // rows past a guard may number any object: object 21 is unknown, and the trailers are hidden. What the stream left
        // unread, nothing, says nothing of the table read after it.
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>");
        var older = file.Table(4, new Dictionary<int, long> { [1] = catalog, [2] = pages, [3] = page }, "/Size 4 /Root 1 0 R");
        var prev = file.Object(21, older.ToString(CultureInfo.InvariantCulture));
        var table = file.Position;
        var free = string.Concat(Enumerable.Repeat("0000000000 00000 f \n", 17));
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n0 1\n0000000000 65535 f \n2 2\n{pages:D10} 00000 n \n{page:D10} 00000 n \n4 17\n{free}21 1\n{prev:D10} 00000 n \ntrailer\n<< /Size 22 /Prev 21 0 R >>\nstartxref\n{table}\n%%EOF\n"));
        file.CrossReferenceStream(22, 23, [], string.Create(CultureInfo.InvariantCulture, $"/Index [22 1] /Prev {table}"));
        using var whole = PdfDocument.Open(file.Bytes);

        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 128 } });

        whole.Catalog.Should().NotBeNull();
        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitXRefSectionLength);
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
        document.WasRepaired.Should().BeFalse();
    }

    [Fact]
    public void A_prev_a_stream_that_fills_the_index_writes_as_a_reference_to_an_object_past_the_object_guard_hides_the_trailers()
    {
        // W1-5: the stream's row for object 21 is read before the index fills, and object 21, a comment of 3,000 bytes then
        // the older table's offset, runs past MaxObjectLength. What it holds is the guard's to hide: the trailers are hidden,
        // and neither xref.section-not-found nor file.root-invalid blames the file.
        var file = NewestStreamNaming("[0 1 21 1 2 21]", [0, 21, .. Numbers(2, 21)], "21 0 R", refused: -1, twentyOne: "%" + new string('c', 3_000) + "\n");
        var limits = PdfReaderLimits.Default with { MaxIndexEntries = 8, MaxObjectLength = 1_024 };
        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        var report = new PdfValidator().Validate(document);

        whole.WasRepaired.Should().BeFalse();
        whole.Catalog.Should().NotBeNull();
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
        document.Diagnostics.Should().NotContain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefSectionMissing);
        report.Contains(PdfValidationRuleIds.XRefSectionNotFound).Should().BeFalse(string.Join("; ", report.Findings));
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse(string.Join("; ", report.Findings));
    }

    [Fact]
    public void An_xrefstm_a_stream_that_fills_the_index_writes_names_no_trailer_for_the_guard_to_hide()
    {
        // R-names-stream-xrefstm: the newest section, a cross-reference stream with no /Root and no /Prev, fills
        // MaxIndexEntries, and its dictionary gives /XRefStm the offset of the older table, whose trailer gives /Root. Only a
        // hybrid table's trailer names a stream through /XRefStm, and no read follows a stream dictionary's: the older trailer
        // is none the guard kept unread. The reader rebuilds for the /Root the file lacks, as a whole read does.
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>");
        var older = file.Table(4, new Dictionary<int, long> { [1] = catalog, [2] = pages, [3] = page }, "/Size 4 /Root 1 0 R");
        var stream = file.Position;
        var data = new StringBuilder(Row(0, 0, 65535));

        for (var number = 2; number < 23; number++)
        {
            data.Append(number switch
            {
                2 => Row(1, pages, 0),
                3 => Row(1, page, 0),
                22 => Row(1, stream, 0),
                _ => Row(0, 0, 0),
            });
        }

        data.Append('>');
        file.Object(22, string.Create(CultureInfo.InvariantCulture, $"<< /Type /XRef /Size 23 /Index [0 1 2 21] /W [1 4 2] /Filter /ASCIIHexDecode /XRefStm {older} /Length {data.Length} >>\nstream\n{data}\nendstream"));
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"startxref\n{stream}\n%%EOF\n"));
        using var whole = PdfDocument.Open(file.Bytes);

        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 } });

        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitIndexEntries);
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeFalse();
        document.Reader.Structure.RootHiddenByGuard.Should().BeFalse();
        document.WasRepaired.Should().Be(whole.WasRepaired).And.BeTrue();
        document.Catalog.Should().NotBeNull();

        static string Row(int type, long offset, int generation) =>
            string.Create(CultureInfo.InvariantCulture, $"{type:X2}{offset:X8}{generation:X4}");
    }

    [Theory]
    [InlineData(31, true)]
    [InlineData(32, false)]
    public void A_prev_written_as_a_chain_of_references_past_a_guard_is_followed_as_far_as_a_whole_read_follows_it(int links, bool hidden)
    {
        // W1-4: /Prev names object 21, which names 22, and so on; the last of the chain holds the older table's offset. The
        // newest table's rows past the chain run past MaxXRefSectionLength. A whole read follows 32 objects: an offset at the
        // 32nd names the older section, which the guard's cut leaves unread; one at the 33rd is no offset, and the reader
        // rebuilds. The guarded read followed the chain 31 objects further, and hid the trailers for an offset no whole read
        // reaches.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>"),
        };
        var older = file.Table(4, new Dictionary<int, long>(rows), "/Size 4 /Root 1 0 R");
        var last = 21 + links;

        for (var number = 21; number < last; number++)
        {
            rows[number] = file.Object(number, string.Create(CultureInfo.InvariantCulture, $"{number + 1} 0 R"));
        }

        rows[last] = file.Object(last, older.ToString(CultureInfo.InvariantCulture));
        var table = file.Position;
        var text = new StringBuilder("xref\n0 1\n0000000000 65535 f \n2 2\n");
        text.Append(CultureInfo.InvariantCulture, $"{rows[2]:D10} 00000 n \n{rows[3]:D10} 00000 n \n21 {links + 1}\n");

        for (var number = 21; number <= last; number++)
        {
            text.Append(CultureInfo.InvariantCulture, $"{rows[number]:D10} 00000 n \n");
        }

        text.Append(CultureInfo.InvariantCulture, $"{last + 1} 100\n").Append(string.Concat(Enumerable.Repeat("0000000000 00000 f \n", 100)));
        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {last + 101} /Prev 21 0 R >>\nstartxref\n{table}\n%%EOF\n");
        file.Raw(text.ToString());
        using var whole = PdfDocument.Open(file.Bytes);
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 900 } });

        whole.WasRepaired.Should().Be(!hidden);
        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitXRefSectionLength);
        document.Reader.Structure.TrailersHiddenByGuard.Should().Be(hidden);
        document.WasRepaired.Should().Be(!hidden);
    }

    [Theory]
    [InlineData("an older section's catalog")]
    [InlineData("no catalog indexed")]
    public void A_cross_reference_stream_a_limit_cut_past_its_rows_hides_no_trailer(string shape)
    {
        // T3-02: the newest section is a cross-reference stream whose data MaxDecodedStreamLength cuts in its padding, past
        // every row, and the chain goes on through its /Prev to the oldest table, whose trailer the reader reads; no trailer
        // gives /Root. The cut took the trailers as hidden: the reader rebuilt nothing for the /Root the file lacks, and
        // file.root-invalid said nothing, or the document opened with no catalog.
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var rows = new Dictionary<int, long>
        {
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>"),
        };

        if (shape == "an older section's catalog")
        {
            rows[1] = catalog;
        }

        var oldest = file.Table(4, rows, "/Size 4");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 2 2] /Resources << >> >>");
        var at = file.Position;
        var data = Row(0, 0, 65535) + Row(0, 0, 0) + Row(1, rows[2], 0) + Row(1, page, 0) + Row(0, 0, 0) + Row(1, at, 0) + new string('0', 400) + ">";
        file.Object(5, string.Create(CultureInfo.InvariantCulture, $"<< /Type /XRef /Size 6 /W [1 4 2] /Prev {oldest} /Filter /ASCIIHexDecode /Length {data.Length} >>\nstream\n{data}\nendstream"));
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"startxref\n{at}\n%%EOF\n"));
        using var whole = PdfDocument.Open(file.Bytes);

        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 60 } });

        var report = new PdfValidator().Validate(document);

        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitDecodedStream);
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeFalse();
        document.Catalog.Should().NotBeNull();
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeTrue(string.Join("; ", report.Findings));
        new PdfValidator().Validate(whole).Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeTrue();

        static string Row(int type, long offset, int generation) =>
            string.Create(CultureInfo.InvariantCulture, $"{type:X2}{offset:X8}{generation:X4}");
    }

    [Fact]
    public void Rows_a_guard_cut_that_run_to_no_trailer_leave_the_root_the_file_lacks_to_be_found()
    {
        // S2-02: the one table's rows run past what MaxXRefSectionLength reads, and on to startxref, with no trailer: the
        // reader looks for it past the rows, to the end of the file, and finds none — the file lacks it, the guard hid
        // nothing —, so it rebuilds the index for the catalog, as a read of the whole table does. It took the guard to
        // have hidden a /Root, and opened the document with no catalog.
        var file = new HandWrittenFile();
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>");
        var catalog = file.Object(20, "<< /Type /Catalog /Pages 2 0 R >>");
        var table = file.Position;
        var free = string.Concat(Enumerable.Repeat("0000000000 00000 f \n", 16));
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"xref\n0 21\n0000000000 65535 f \n0000000000 00000 f \n{pages:D10} 00000 n \n{page:D10} 00000 n \n{free}{catalog:D10} 00000 n \nstartxref\n{table}\n%%EOF\n"));
        using var whole = PdfDocument.Open(file.Bytes);

        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 128 } });

        document.Reader.Structure.TrailersHiddenByGuard.Should().BeFalse();
        document.Reader.Structure.RootHiddenByGuard.Should().BeFalse();
        document.WasRepaired.Should().Be(whole.WasRepaired).And.BeTrue();
        document.Catalog.Should().NotBeNull();
        document.Reader.CatalogFoundAs.Should().Be(20);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void A_section_a_prev_names_a_few_bytes_off_ends_the_chain_where_its_trailer_is_cut(int shift)
    {
        // Q1-02: the middle section's trailer runs past MaxTrailerLength, and the newest names it 3 bytes off. Relocated, it
        // lost its trailer's cut: the chain read on to the oldest section, past the cut, which the reader takes the catalog
        // the update superseded from, and object 9 only that section indexes, though unknown, read as missing.
        var file = new HandWrittenFile();
        var oldest = new Dictionary<int, long>
        {
            [5] = file.Object(5, "<< /Type /Catalog /Pages 2 0 R /Version /1.4 >>"),
            [9] = file.Object(9, "(nine)"),
        };
        var first = file.Table(10, oldest, "/Size 10 /Root 5 0 R");
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Foo 9 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>"),
        };
        var middle = file.Table(4, rows, string.Create(CultureInfo.InvariantCulture, $"/Size 10 /Prev {first} /Pad ({new string('x', 400)}) /Root 1 0 R"));
        file.Table(1, [], string.Create(CultureInfo.InvariantCulture, $"/Size 10 /Prev {middle - shift}"));
        using var document = PdfDocument.Open(file.Bytes, TrailerCut());

        var report = new PdfValidator().Validate(document);

        document.Reader.GetPresence(9).Should().Be(ObjectPresence.Unindexed);
        document.Catalog.Should().NotBeNull().And.Subject.Should().NotContainKey(PdfName.Version, "the catalog object 5 gives is the one the update superseded");
        report.Contains(PdfValidationRuleIds.ObjectReferenceMissing).Should().BeFalse(string.Join("; ", report.Findings));
    }

    [Theory]
    [InlineData("rows")]
    [InlineData("index")]
    public void A_section_a_prev_names_a_few_bytes_off_whose_rows_a_guard_ends_hides_the_trailer_it_leaves_unread(string guard)
    {
        // R-thg-notrailer-none: the newest table gives no /Root, and names the older one 3 bytes off; the older one's trailer
        // gives it. Found near where it was named, the older table's rows run past MaxXRefSectionLength, or fill
        // MaxIndexEntries, and a place a section may have been moved to has its trailer not looked for past the guard: the
        // trailer is the guard's to hide, and the /Root the trailers lack is unknown, not the file's to lack.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>"),
        };
        var older = file.Table(40, rows, "/Size 40 /Root 1 0 R");
        file.Table(1, [], string.Create(CultureInfo.InvariantCulture, $"/Size 40 /Prev {older - 3}"));
        var limits = guard == "rows" ? PdfReaderLimits.Default with { MaxXRefSectionLength = 128 } : PdfReaderLimits.Default with { MaxIndexEntries = 8 };
        using var whole = PdfDocument.Open(file.Bytes);

        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = limits });

        var report = new PdfValidator().Validate(document);

        new PdfValidator().Validate(whole).Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse();
        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Reader.Structure.TrailersHiddenByGuard.Should().BeTrue();
        document.Reader.Structure.RootHiddenByGuard.Should().BeTrue();
        document.WasRepaired.Should().BeFalse();
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse(string.Join("; ", report.Findings));
    }

    [Fact]
    public void A_cross_reference_stream_a_guard_cut_does_not_make_an_object_of_its_number_a_cut_catalog()
    {
        // Review of #175 (L3-02): /Root names object 5, read whole and no catalog; object 5 is also an older cross-reference
        // stream, whose dictionary MaxTrailerLength cut as the chain was read. The reader took that cut for /Root's.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"), 0),
            [3] = (1, file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>"), 0),
            [4] = (1, file.Object(4, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
        };
        var stream = file.CrossReferenceStream(5, 5, rows, "/Root 4 0 R /Pad (" + new string('x', 400) + ")");
        var outlines = file.Object(5, "<< /Type /Outlines >>");
        var table = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \n5 1\n{outlines:D10} 00000 n \ntrailer\n<< /Size 6 /Root 5 0 R /Prev {stream} >>\nstartxref\n{table}\n%%EOF\n"));
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxTrailerLength = 200 } });

        document.Reader.Structure.RootCutByGuard.Should().BeFalse();
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue("the reader looks for the catalog /Root does not lead to");
        new PdfValidator().Validate(document).Findings.Should().Contain(finding => finding.RuleId == PdfValidationRuleIds.FileRootInvalid);
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

        (source.BytesRead - before).Should().BeLessThanOrEqualTo(4096 + PdfFileReader.ByteBeforeRow);
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

        // The cut falls before the "]": "2 3" may start "2 3 R", which it hides, and both are dropped (#262).
        document.GetObject(new PdfObjectId(4)).AsArray().Required().Should().ContainSingle().Which.Should().BeOfType<PdfInteger>().Which.Value.Should().Be(1);

        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.LimitObject);
        new PdfValidator().Validate(document).Findings.Should().NotContain(finding => finding.RuleId == PdfValidationRuleIds.ObjectEndObjMissing);
    }

    [Theory]
    [InlineData("<< /Type /Pages /Count 2 /Kids [3 0 R 4 0 R] >>", "[3 0 R 4")]
    [InlineData("<< /Type /Pages /Count 2 /Kids [3 0 R 4 0 R] >>", "[3 0 R 4 0")]
    [InlineData("<< /Type /Pages /Count 1 /Kids [3 0 R] /Foo 4 0 R >>", "/Foo 4")]
    [InlineData("<< /Type /Pages /Count 1 /Kids [3 0 R] /Foo 4 0 R >>", "/Foo 4 0")]
    public void A_reference_MaxObjectLength_cuts_after_its_number_or_its_generation_is_dropped(string node, string cutAfter)
    {
        // The bound ends object 2's window after the number of the reference to page 4, or after its generation: what the
        // window holds may be what the cut left of that reference, which reads as no two integers the file did not write.
        // The array leaves both out, the dictionary the key whose value they were, and the guard is reported (#262). Spaces
        // before the node set its cut past the end of the catalog, which the bound leaves whole.
        var padded = new string(' ', 40) + node;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, Catalog),
            [2] = file.Object(2, padded),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>"),
            [4] = file.Object(4, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>"),
        };
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        var bound = "2 0 obj\n".Length + padded.IndexOf(cutAfter, StringComparison.Ordinal) + cutAfter.Length;

        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = bound } });
        var read = document.GetObject(new PdfObjectId(2)).AsDictionary().Required();

        read.GetRaw(PdfName.Kids).Should().BeOfType<PdfArray>().Which.Should().ContainSingle()
            .Which.Should().BeOfType<PdfReference>().Which.Id.Should().Be(new PdfObjectId(3));
        read.ContainsKey(PdfName.Get("Foo")).Should().BeFalse();
        document.Reader.IsCutAtLimit(2).Should().BeTrue();
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
    }

    [Theory]
    [InlineData("/FlateDecode", 6, 0)]
    [InlineData("15", 1, 0)]
    [InlineData("1.5", 1, 0)]
    [InlineData("1.5", 2, 0)]
    [InlineData("true", 3, 0)]
    [InlineData("[1 /FlateDecode]", 6, 1)]
    [InlineData("[1 (x) 15]", 8, 2)]
    [InlineData("[1 1.5 true]", 5, 1)]
    [InlineData("[1 true]", 5, 1)]
    [InlineData("<< /Filter /FlateDecode >>", 16, 0)]
    [InlineData("<< /Length 15 >>", 12, 0)]
    [InlineData("<< /Decode 1.5 >>", 13, 0)]
    [InlineData("<< /A true >>", 9, 0)]
    public void A_name_a_number_or_a_keyword_MaxObjectLength_parts_is_dropped(string value, int kept, int count)
    {
        // Spaces before object 20's value make the bound end its window kept bytes into it, inside a name, an integer, a
        // real or a keyword: "/Flate" of "/FlateDecode", "1" of "15" or "1.5", "1." of "1.5", "tru" of "true". What the
        // window holds of the token is no value the file wrote: it reads as null alone, an array leaves it out, keeping the
        // count elements before it, a dictionary the key whose value it was, and the guard is reported (#279).
        var file = new HandWrittenFile();
        var head = "20 0 obj\n".Length;
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, Catalog),
            [2] = file.Object(2, Pages),
            [20] = file.Object(20, new string(' ', 1024 - head - kept) + value + new string(' ', 100)),
        };
        file.Table(21, rows, "/Size 21 /Root 1 0 R");

        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } });
        var read = document.GetObject(new PdfObjectId(20));

        switch (value[0])
        {
            case '[':
                read.AsArray().Required().Count.Should().Be(count);
                read.AsArray().Required()[0].Should().BeOfType<PdfInteger>().Which.Value.Should().Be(1);
                break;
            case '<':
                read.AsDictionary().Required().Count.Should().Be(count);
                break;
            default:
                read.Should().BeSameAs(PdfNull.Instance);
                break;
        }

        document.Reader.IsCutAtLimit(20).Should().BeTrue();
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
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
        document.Reader.Index.Capacity.Should().BeLessThan(128, "room is made for about the 102 rows 2 KB hold");
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
        document.Reader.Index.Capacity.Should().BeLessThan(64, "the sections hold four rows");
    }

    /// <summary>
    /// Writes objects 4 and 5, arrays of 4,000 zeros, packed in object stream 6, and objects 10 to 89, small strings that
    /// push them out of a cache of 64; with <paramref name="starts"/> where each member starts in the decoded data.
    /// </summary>
    private static byte[] LongMembers(out int[] starts)
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, Pages), 0),
            [4] = (2, 6, 0),
            [5] = (2, 6, 1),
        };
        var member = "[" + string.Concat(Enumerable.Repeat("0 ", 4000)) + "]\n";
        var header = string.Create(CultureInfo.InvariantCulture, $"4 0 5 {member.Length} ");
        starts = [header.Length, header.Length + member.Length];
        rows[6] = (1, file.Object(6, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 2 /First {header.Length} /Length {header.Length + (2 * member.Length)} >>\nstream\n{header}{member}{member}\nendstream")), 0);

        for (var number = 10; number < 90; number++)
        {
            rows[number] = (1, file.Object(number, "(filler)"), 0);
        }

        file.CrossReferenceStream(90, 91, rows, "/Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// Writes catalog 1, pages 2 and page 3, an older table indexing them with <c>/Root</c>, object 21 —
    /// <paramref name="twentyOne"/> then the older table's offset —, and a newest cross-reference stream, object 22, whose
    /// <c>/Index</c> is <paramref name="index"/>, holding a row for each of <paramref name="rows"/> in its order, and whose
    /// dictionary gives <c>/Prev</c> <paramref name="prev"/> and no <c>/Root</c>. The row of <paramref name="refused"/> places
    /// it in object stream 0, which no row may. The stream's <c>/Size</c> is <paramref name="size"/>.
    /// </summary>
    private static byte[] NewestStreamNaming(string index, int[] rows, string prev, int refused, string twentyOne = "", int size = 23)
    {
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>");
        var older = file.Table(4, new Dictionary<int, long> { [1] = catalog, [2] = pages, [3] = page }, "/Size 4 /Root 1 0 R");
        var placed = file.Object(21, twentyOne + older.ToString(CultureInfo.InvariantCulture));
        var stream = file.Position;
        var data = new StringBuilder();

        foreach (var number in rows)
        {
            data.Append(number switch
            {
                _ when number == refused => Row(2, 0, 0),
                0 => Row(0, 0, 65535),
                2 => Row(1, pages, 0),
                3 => Row(1, page, 0),
                21 => Row(1, placed, 0),
                22 => Row(1, stream, 0),
                _ => Row(0, 0, 0),
            });
        }

        data.Append('>');
        file.Object(22, string.Create(CultureInfo.InvariantCulture, $"<< /Type /XRef /Size {size} /Index {index} /W [1 4 2] /Filter /ASCIIHexDecode /Prev {prev} /Length {data.Length} >>\nstream\n{data}\nendstream"));
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"startxref\n{stream}\n%%EOF\n"));
        return file.Bytes;

        static string Row(int type, long offset, int generation) =>
            string.Create(CultureInfo.InvariantCulture, $"{type:X2}{offset:X8}{generation:X4}");
    }

    private static int[] Numbers(int first, int count) => [.. Enumerable.Range(first, count)];

    /// <summary>
    /// Writes a file whose object stream 3 holds member 4, 200 spaces, then <paramref name="value"/> and 300 spaces, and
    /// member 6, 42: a MaxObjectLength of 205 ends member 4's window right after the R of a reference.
    /// </summary>
    private static byte[] MemberReachingItsBound(string value)
    {
        var written = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, written.Object(1, Catalog), 0),
            [2] = (1, written.Object(2, Pages), 0),
            [3] = (1, written.ObjectStream(3, [(4, new string(' ', 200) + value + new string(' ', 300)), (6, "42")]), 0),
            [4] = (2, 3, 0),
            [6] = (2, 3, 1),
        };
        written.CrossReferenceStream(7, 8, rows, "/Root 1 0 R");
        return written.Bytes;
    }

    /// <summary>
    /// Writes a file whose chain places object 4 in object stream 10, whose dictionary runs past 128 bytes, and objects 6 and
    /// 7 in object stream 11, which holds 6 alone; row 9 places its object inside object 8's string, so that settling
    /// rebuilds. <paramref name="direct"/> writes object 4 again directly, <paramref name="unindexed"/> objects no row
    /// indexes, and <paramref name="redefined"/> object stream 5, which no row indexes, holding an object numbered 10.
    /// </summary>
    private static byte[] HiddenAndWholeObjectStreams(bool direct, int unindexed, bool redefined = false)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"), 0),
            [3] = (1, file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>"), 0),
            [4] = (2, 10, 0),
            [6] = (2, 11, 0),
            [7] = (2, 11, 1),
        };
        var eight = file.Object(8, "(" + new string('y', 2000) + ")");
        rows[8] = (1, eight, 0);
        rows[9] = (1, eight + 1000, 0);
        rows[10] = (1, file.ObjectStream(10, [(4, "(four)")], "/Pad (" + new string('x', 300) + ") "), 0);
        rows[11] = (1, file.ObjectStream(11, [(6, "(six)")]), 0);

        if (direct)
        {
            file.Object(4, "(direct four)");
        }

        if (redefined)
        {
            file.ObjectStream(5, [(10, "(ten)")]);
        }

        for (var number = 20; number < 20 + unindexed; number++)
        {
            file.Object(number, "(unindexed)");
        }

        file.CrossReferenceStream(12, 13, rows, "/Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// A document whose object <paramref name="member"/> — its catalog, 1, or the root of its page tree, 2 — a guard keeps
    /// its object stream from serving, <paramref name="shape"/> saying how, with an older catalog 5 beside it, and object
    /// 9's row of its cross-reference stream placed inside object 8's string, where no header lies: settling rebuilds the
    /// index.
    /// </summary>
    private static byte[] HiddenMemberRebuilt(string shape, int member, out PdfReaderLimits limits)
    {
        var builder = new TestPdfBuilder();

        if (shape == "past the decoded data")
        {
            builder.WithObject(4, "(" + new string('x', 300) + ")");
        }

        builder
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>")
            .WithObject(5, "<< /Type /Catalog /Pages 6 0 R >>")
            .WithObject(6, "<< /Type /Pages /Kids [7 0 R] /Count 1 >>")
            .WithObject(7, "<< /Type /Page /Parent 6 0 R /MediaBox [0 0 1 1] /Resources << >> >>")
            .WithObject(8, "(" + new string('y', 2000) + ")")
            .WithObject(9, "(nine)");
        byte[] file;

        switch (shape)
        {
            case "past the decoded data":
                file = builder.BuildWithXRefStream(rootNumber: 1, compressedObjects: [4, member], compressObjectStream: true);
                limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 100 };
                break;

            case "first past the decoded data":
                file = builder.BuildWithXRefStream(
                    rootNumber: 1, compressedObjects: [member], objectStreamHeader: header => header + new string(' ', 200), compressObjectStream: true);
                limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 100 };
                break;

            default:
                file = builder.BuildWithXRefStream(rootNumber: 1, compressedObjects: [member], objectStreamEntries: "/Pad (" + new string('x', 300) + ")");
                limits = PdfReaderLimits.Default with { MaxObjectLength = 128 };
                break;
        }

        // The rows are written uncompressed, seven bytes each: a type, an offset of four bytes, and two more.
        var text = Encoding.Latin1.GetString(file);
        var row = text.IndexOf("stream\n", text.LastIndexOf("/Type /XRef", StringComparison.Ordinal), StringComparison.Ordinal) + "stream\n".Length + (9 * 7);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(row + 1, 4), (uint)(text.IndexOf("yyyy", StringComparison.Ordinal) + 1000));
        return file;
    }

    /// <summary>
    /// Options that cut a trailer past 300 bytes, and a table's window past 128, so that a trailer is read in a window of its
    /// own, where the trailer guard reaches it.
    /// </summary>
    private static PdfReaderOptions TrailerCut() =>
        new() { Limits = PdfReaderLimits.Default with { MaxTrailerLength = 300, MaxXRefSectionLength = 128 } };

    /// <summary>Object 4, an array of three numbers without its endobj, padded to be longer than the sound document's objects.</summary>
    private static string PaddedArray { get; } = "4 0 obj\n[1 2 3" + new string(' ', 200) + "]";

    private static string LongArray { get; } = "[" + string.Join(' ', Enumerable.Range(0, 2000)) + "]";

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        new Random(170).NextBytes(bytes);
        return bytes;
    }

    /// <summary>
    /// A source of about 2 GB that holds none of it: a head, white space, and a tail at its end, read as the file would
    /// be, so that a stream may declare a length no array holds.
    /// </summary>
    private sealed class SparseSource(byte[] head, long tailAt, byte[] tail) : PdfFileSource
    {
        public override long Length => tailAt + tail.Length;

        /// <summary>
        /// A file whose object 3 is a stream of <paramref name="declared"/> bytes, which an <c>endstream</c> after them
        /// confirms, its table and trailer past them.
        /// </summary>
        public static SparseSource Huge(long declared)
        {
            var text = string.Create(
                CultureInfo.InvariantCulture,
                $"%PDF-1.7\n1 0 obj\n{Catalog}\nendobj\n2 0 obj\n{Pages}\nendobj\n3 0 obj\n<< /Length {declared} >>\nstream\n");
            var head = Encoding.Latin1.GetBytes(text);
            var tailAt = head.Length + declared;
            var xref = tailAt + "\nendstream\nendobj\n".Length;
            var tail = Encoding.Latin1.GetBytes(string.Create(
                CultureInfo.InvariantCulture,
                $"\nendstream\nendobj\nxref\n0 4\n0000000000 65535 f\r\n{9:D10} 00000 n\r\n{text.IndexOf("2 0 obj", StringComparison.Ordinal):D10} 00000 n\r\n{text.IndexOf("3 0 obj", StringComparison.Ordinal):D10} 00000 n\r\ntrailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
            return new SparseSource(head, tailAt, tail);
        }

        public override int Read(long offset, Span<byte> buffer)
        {
            var count = (int)Math.Clamp(Length - offset, 0, buffer.Length);

            for (var i = 0; i < count;)
            {
                var at = offset + i;
                int run;

                if (at < head.Length)
                {
                    run = (int)Math.Min(count - i, head.Length - at);
                    head.AsSpan((int)at, run).CopyTo(buffer[i..]);
                }
                else if (at < tailAt)
                {
                    run = (int)Math.Min(count - i, tailAt - at);
                    buffer.Slice(i, run).Fill((byte)' ');
                }
                else
                {
                    run = (int)Math.Min(count - i, tail.Length - (at - tailAt));
                    tail.AsSpan((int)(at - tailAt), run).CopyTo(buffer[i..]);
                }

                i += run;
            }

            return count;
        }
    }

    private static PdfReaderLimits With(PdfReaderLimits limits, string limit, int value) => limit switch
    {
        nameof(PdfReaderLimits.MaxDecodedStreamLength) => limits with { MaxDecodedStreamLength = value },
        nameof(PdfReaderLimits.MaxStreamLength) => limits with { MaxStreamLength = value },
        nameof(PdfReaderLimits.MaxObjectLength) => limits with { MaxObjectLength = value },
        nameof(PdfReaderLimits.MaxXRefSectionLength) => limits with { MaxXRefSectionLength = value },
        nameof(PdfReaderLimits.MaxXRefSectionCount) => limits with { MaxXRefSectionCount = value },
        nameof(PdfReaderLimits.MaxTrailerLength) => limits with { MaxTrailerLength = value },
        nameof(PdfReaderLimits.MaxIndexEntries) => limits with { MaxIndexEntries = value },
        nameof(PdfReaderLimits.MaxFilterChainLength) => limits with { MaxFilterChainLength = value },
        nameof(PdfReaderLimits.MaxRebuildReadPasses) => limits with { MaxRebuildReadPasses = value },
        nameof(PdfReaderLimits.MaxObjectStreamDecodePasses) => limits with { MaxObjectStreamDecodePasses = value },
        nameof(PdfReaderLimits.MaxNestingDepth) => limits with { MaxNestingDepth = value },
        nameof(PdfReaderLimits.MaxNestedLoads) => limits with { MaxNestedLoads = value },
        _ => throw new ArgumentOutOfRangeException(nameof(limit), limit, "Not a reader limit."),
    };

    private static int Bound(PdfReaderLimits limits, string limit) => limit switch
    {
        nameof(PdfReaderLimits.MaxDecodedStreamLength) => limits.MaxDecodedStreamLength,
        nameof(PdfReaderLimits.MaxStreamLength) => limits.MaxStreamLength,
        nameof(PdfReaderLimits.MaxObjectLength) => limits.MaxObjectLength,
        nameof(PdfReaderLimits.MaxXRefSectionLength) => limits.MaxXRefSectionLength,
        nameof(PdfReaderLimits.MaxXRefSectionCount) => limits.MaxXRefSectionCount,
        nameof(PdfReaderLimits.MaxTrailerLength) => limits.MaxTrailerLength,
        nameof(PdfReaderLimits.MaxIndexEntries) => limits.MaxIndexEntries,
        nameof(PdfReaderLimits.MaxFilterChainLength) => limits.MaxFilterChainLength,
        nameof(PdfReaderLimits.MaxRebuildReadPasses) => limits.MaxRebuildReadPasses,
        nameof(PdfReaderLimits.MaxObjectStreamDecodePasses) => limits.MaxObjectStreamDecodePasses,
        nameof(PdfReaderLimits.MaxNestingDepth) => limits.MaxNestingDepth,
        nameof(PdfReaderLimits.MaxNestedLoads) => limits.MaxNestedLoads,
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

    /// <summary>Gives how many arrays <paramref name="value"/> holds one inside the other, through their first elements.</summary>
    private static int Depth(PdfObject value) => value is PdfArray array ? 1 + (array.Count > 0 ? Depth(array[0]) : 0) : 0;

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

    /// <summary>
    /// Writes the file of the guard on the rebuild's reads (<see cref="Case.For"/>) under a classic table whose rows each lie
    /// three bytes past the header they place: objects 1, 2, 10, 11, 30 and 31.
    /// </summary>
    private static byte[] SpelledStreamsUnderShiftedRows(bool firstStreamHoldsHeader = true)
    {
        var file = Case.For("rebuild reads").File;
        var text = Encoding.Latin1.GetString(file);

        if (!firstStreamHoldsHeader)
        {
            text = text.Replace("25 0 obj", "25 0 ob_", StringComparison.Ordinal);
        }

        var body = text[..text.LastIndexOf("trailer", StringComparison.Ordinal)];
        var table = new StringBuilder("xref\n0 32\n0000000000 65535 f \n");

        for (var number = 1; number < 32; number++)
        {
            var at = number is 1 or 2 or 10 or 11 or 30 or 31
                ? body.IndexOf(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj"), StringComparison.Ordinal)
                : -1;
            table.Append(at < 0 ? "0000000000 65535 f \n" : string.Create(CultureInfo.InvariantCulture, $"{at + 3:D10} 00000 n \n"));
        }

        table.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size 32 /Root 1 0 R >>\nstartxref\n{body.Length}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(body + table);
    }

    /// <summary>
    /// Writes a file with no cross-reference table: a comment line of <paramref name="prefix"/> dashes, then
    /// <paramref name="misses"/> attached files whose indirect <c>/Length</c> names an object the file lacks — <paramref name="named"/>,
    /// or a number of its own for each —, a comment line of <paramref name="padding"/> bytes before the fourth where there are
    /// any, then attached file 20, whose data, <see cref="MissedData"/>, spells object 77, then 64 KiB of comment lines, then a
    /// trailer, and an end-of-file marker where <paramref name="marked"/>. Object 21, the length's object of stream 20, lies
    /// <paramref name="where"/>: right after it, before it, past a line after it, or past the comment lines after it. Where
    /// <paramref name="confirmedFirst"/>, stream 15, whose length object 16 before it confirms, comes before the misses, and an
    /// end-of-file marker that an endstream follows comes after each.
    /// </summary>
    private static byte[] MissedLengths(
        int misses, string where = "right after it", string? named = null, bool marked = true, int prefix = 2000, int padding = 0, bool confirmedFirst = false)
    {
        var length = string.Create(CultureInfo.InvariantCulture, $"21 0 obj\n{MissedData.Length}\nendobj\n");
        var text = new StringBuilder("%PDF-1.7\n1 0 obj\n" + Catalog + "\nendobj\n2 0 obj\n" + Pages + "\nendobj\n%").Append('-', prefix).Append('\n');
        text.Append(confirmedFirst ? "16 0 obj\n3\nendobj\n15 0 obj\n<< /Length 16 0 R >>\nstream\nabc\nendstream\nendobj\n" : "");

        for (var i = 0; i < misses; i++)
        {
            if (i == 3 && padding > 0)
            {
                text.Append('%').Append('p', padding).Append('\n');
            }

            var number = named ?? (90 + i).ToString(CultureInfo.InvariantCulture);
            text.Append(CultureInfo.InvariantCulture, $"{10 + i} 0 obj\n<< /Type /EmbeddedFile /Length {number} 0 R >>\nstream\nabc\nendstream\nendobj\n")
                .Append(confirmedFirst ? "%%EOF\nendstream\n" : "");
        }

        text.Append(where == "before it" ? length : "").Append("20 0 obj\n<< /Type /EmbeddedFile /Length 21 0 R >>\nstream\n").Append(MissedData)
            .Append("endstream\nendobj\n").Append(where == "right after it" ? length : where == "past a line after it" ? "%" + new string('-', 80) + "\n" + length : "");

        for (var line = 0; line < 1024; line++)
        {
            text.Append("% ").Append('-', 61).Append('\n');
        }

        text.Append(where == "past the comment lines after it" ? length : "").Append("trailer\n<< /Root 1 0 R >>\n").Append(marked ? "%%EOF\n" : "");
        return Encoding.Latin1.GetBytes(text.ToString());
    }

    private static int OffsetOf(byte[] file, string text) =>
        Encoding.Latin1.GetString(file).IndexOf(text, StringComparison.Ordinal);

    /// <summary>
    /// A note a writer puts in an attached file's length object, object 11, before its integer: the rebuild's scan reads
    /// such an object within 64 bytes of its header alone, and with the note cannot read the length there, so that it
    /// cannot confirm the attached file's indirect length, and reads its data as before (#363).
    /// </summary>
    private const string LengthNote = "% the length of the attached file's data, which its writer filled in once it had written it\n";

    /// <summary>Writes object 11, holding <paramref name="length"/> after <see cref="LengthNote"/>, and gives where it starts.</summary>
    private static long NotedLength(HandWrittenFile pdf, int length) =>
        pdf.Object(11, LengthNote + length.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// A sound document that reaches one guard — but for the damage that sets off a rebuild, for a guard only a rebuild
    /// reaches —: the limits that reach it, the limits that read it whole, what reading it observes, what the guard
    /// reports, and what a whole read still does (<see cref="WholeReports"/>).
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
        /// <summary>
        /// Gets the codes a read under the raised limits reports, in order: the damage that sets off a rebuild, and what the
        /// rebuild indexes.
        /// </summary>
        public string[] WholeReports { get; init; } = [];

        /// <summary>
        /// Gets a value indicating whether reading under the reached limits observes what it does under the raised ones: the
        /// guard cuts only what one of the library's walks reads, and the walk reads what it cut again.
        /// </summary>
        public bool ReadsWholeAtGuard { get; init; }

        public static Case For(string guard) => guard switch
        {
            "decoded stream" => DecodedStream(),
            "stream length" => LongStream(),
            "object" => LongObject(),
            "section length" => LongSection(),
            "section count" => ManySections(),
            "trailer" => LongTrailer(),
            "cross-reference stream" => LongXRefStreamDictionary(),
            "index entries" => ManyObjects(),
            "filter chain" => LongFilterChain(),
            "rebuild reads" => ManySpelledStreams(),
            "object-stream decodes" => ObjectStreamsReadInTurn(),
            "nesting depth" => DeepContainers(),
            "nested loads" => NestedLoads(),
            _ => throw new ArgumentOutOfRangeException(nameof(guard), guard, "Not a guard."),
        };

        private static Case DeepContainers()
        {
            // Five arrays, one inside the other, which a bound of three cuts at the fourth.
            var file = new TestPdfBuilder()
                .WithObject(1, Catalog)
                .WithObject(2, Pages)
                .WithObject(3, "[[[[[1]]]]]")
                .BuildClassic(rootNumber: 1);

            return new Case(
                nameof(PdfReaderLimits.MaxNestingDepth),
                PdfDiagnosticCodes.LimitNestingDepth,
                file,
                PdfReaderLimits.Default with { MaxNestingDepth = 3 },
                PdfReaderLimits.Default,
                document => Depth(document.GetObject(new PdfObjectId(3))).ToString(CultureInfo.InvariantCulture),
                "5",
                "An array nested inside 3 others reads as null, and what it holds is not read.",
                OffsetOf(file, "[[[[[1") + 3,
                ThrowsOnOpening: false,
                ReportsNothingElse: true);

            static int Depth(PdfObject value) => value is PdfArray { Count: 1 } array ? 1 + Depth(array[0]) : 0;
        }

        private static Case NestedLoads()
        {
            // Object 3, which object stream 7 holds, whose filter is object 20, which object stream 8 holds: reading 3 loads 7,
            // and decoding 7 loads 8, a third load, which a bound of two cuts. 7 is then decoded without its filter, and 3
            // is not found in it.
            var file = new HandWrittenFile(version: "1.5");
            var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
            {
                [1] = (1, file.Object(1, Catalog), 0),
                [2] = (1, file.Object(2, Pages), 0),
                [8] = (1, file.ObjectStream(8, [(20, "/ASCIIHexDecode")]), 0),
                [20] = (2, 8, 0),
                [3] = (2, 7, 0),
            };
            var data = Hex("3 0 (three) ");
            rows[7] = (1, file.Object(7, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First 4 /Filter 20 0 R /Length {data.Length} >>\nstream\n{data}\nendstream")), 0);
            file.CompressedCrossReferenceStream(30, 31, number => rows.TryGetValue(number, out var row) ? row : (0, 0, 0), "/Root 1 0 R");
            var bytes = file.Bytes;

            return new Case(
                nameof(PdfReaderLimits.MaxNestedLoads),
                PdfDiagnosticCodes.LimitNestedLoads,
                bytes,
                PdfReaderLimits.Default with { MaxNestedLoads = 2 },
                PdfReaderLimits.Default,
                document => string.Create(CultureInfo.InvariantCulture, $"{document.GetObject(new PdfObjectId(3))}"),
                "\"three\"",
                "Object 8 is loaded while 2 objects are being loaded, and reads as null there; it reads whole from a shallower place.",
                OffsetOf(bytes, "8 0 obj"),
                ThrowsOnOpening: false,
                ReportsNothingElse: false);
        }

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
                "The /ASCIIHexDecode data decodes to more than 1000 bytes; decoding stopped there.",
                OffsetOf(file, "stream\n") + "stream\n".Length,
                ThrowsOnOpening: false,
                ReportsNothingElse: true);

            static int Decoded(PdfDocument document) =>
                document.GetObject(new PdfObjectId(3)).AsStream().Required().Decode(document.Diagnostics).Length;
        }

        private static Case LongStream()
        {
            // Ten thousand bytes of data no filter decodes, which a bound of a thousand cuts when they are read.
            var file = new TestPdfBuilder()
                .WithObject(1, Catalog)
                .WithObject(2, Pages)
                .Stream(3, string.Empty, new string('A', 10_000))
                .BuildClassic(rootNumber: 1);

            return new Case(
                nameof(PdfReaderLimits.MaxStreamLength),
                PdfDiagnosticCodes.LimitStreamLength,
                file,
                PdfReaderLimits.Default with { MaxStreamLength = 1000 },
                PdfReaderLimits.Default,
                document => document.GetObject(new PdfObjectId(3)).AsStream().Required().GetRawBytes().Length.ToString(CultureInfo.InvariantCulture),
                "10000",
                "A stream's data runs past 1000 bytes; only what lies within it was read.",
                OffsetOf(file, "stream\n") + "stream\n".Length,
                ThrowsOnOpening: false,
                ReportsNothingElse: true);
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
                "The dictionary of the object read as a cross-reference stream runs past 64 KB; only what lies within it was read.",
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

        private static Case ManySpelledStreams()
        {
            // A file with no cross-reference table, which the reader rebuilds. Object 10, an attached file with an indirect
            // /Length whose object holds a note before its length, which the scan cannot read there (#363), spells six
            // streams in its data whose direct lengths all end where 64 KiB of spaces start: the scan,
            // which cannot confirm object 10, reads its data as objects, and reads through the spaces for each spelled stream
            // to confirm it, four times the file by the fifth, which it leaves unconfirmed. Object 30, an attached PDF file,
            // has a direct length 70 spaces before its endstream, which a first read cannot see past; its data holds a stream
            // of its own, and ends in a comment that runs on to object 31 on endstream's line. Once the guard is reached, the
            // scan reads its data up to its first endstream, the inner one, and the comment after it hides object 31. The
            // rebuild indexes the spelled streams, whose lengths no endstream follows, whatever the guard.
            const int Spelled = 6;
            const int Run = 64 * 1024;
            const string Head = "20 0 obj\n<< /Length 0000000 >>\nstream\nabc\n";
            var spelled = new StringBuilder();

            for (var i = 0; i < Spelled; i++)
            {
                var length = ((Spelled - i) * Head.Length) - (Head.Length - "abc\n".Length);
                spelled.Append(Head.Replace("20 0", string.Create(CultureInfo.InvariantCulture, $"{20 + i} 0"), StringComparison.Ordinal)
                    .Replace("0000000", length.ToString("D7", CultureInfo.InvariantCulture), StringComparison.Ordinal));
            }

            var data = spelled.Append(' ', Run).Append("attached tail\n").ToString();
            const string Inner = "%PDF-1.4\n5 0 obj\n<< /Length 3 >>\nstream\nabc\nendstream\nendobj\n% the attached file's last line";
            var text = new StringBuilder("%PDF-1.7\n1 0 obj\n" + Catalog + "\nendobj\n2 0 obj\n" + Pages + "\nendobj\n")
                .Append("10 0 obj\n<< /Type /EmbeddedFile /Length 11 0 R >>\nstream\n").Append(data).Append("endstream\nendobj\n")
                .Append(CultureInfo.InvariantCulture, $"11 0 obj\n{LengthNote}{data.Length}\nendobj\n")
                .Append(CultureInfo.InvariantCulture, $"30 0 obj\n<< /Type /EmbeddedFile /Length {Inner.Length} >>\nstream\n{Inner}")
                .Append(' ', 70).Append("endstream endobj 31 0 obj (real) endobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n");
            var file = Encoding.Latin1.GetBytes(text.ToString());

            return new Case(
                nameof(PdfReaderLimits.MaxRebuildReadPasses),
                PdfDiagnosticCodes.LimitRebuildReads,
                file,
                PdfReaderLimits.Default,
                PdfReaderLimits.Default with { MaxRebuildReadPasses = 64 },
                document => document.GetObject(new PdfObjectId(31)) is PdfString real ? real.AsText()! : "null",
                "real",
                "Past streams' direct lengths, the scan for objects read 4 times the length of the file; the data of each stream it could not confirm after that was read up to its first endstream, the object headers there not taken.",
                OffsetOf(file, "24 0 obj") + Head.Length - "abc\n".Length,
                ThrowsOnOpening: true,
                ReportsNothingElse: false)
            {
                WholeReports = [PdfDiagnosticCodes.XRefRebuilt, .. Enumerable.Repeat(PdfDiagnosticCodes.StreamLengthInvalid, Spelled)],
            };
        }

        private static Case ObjectStreamsReadInTurn()
        {
            // The search for a catalog reads objects 10 to 17 in number order, each decoding its stream again: within the
            // default four times what the first two decodings weighed, it finds the catalog, object 16; within one, it
            // decodes stream 4 no more past object 13, and reads 14 and 16 as null. Finding no catalog, it reads those two
            // again, decoding stream 4 once more, and takes object 16 as a whole search does: the index is not rebuilt.
            var bytes = ObjectStreamsReadInTurnFile(parametersCut: false);
            const long FirstDecodings = 2 * (ObjectStreamDecoded + (16 * 4));

            return new Case(
                nameof(PdfReaderLimits.MaxObjectStreamDecodePasses),
                PdfDiagnosticCodes.LimitObjectStreamDecodes,
                bytes,
                PdfReaderLimits.Default with { MaxObjectStreamDecodePasses = 1 },
                PdfReaderLimits.Default,
                document => string.Create(CultureInfo.InvariantCulture, $"{(document.WasRepaired ? "rebuilt" : "as written")}, catalog {document.Reader.CatalogFoundAs}"),
                "as written, catalog 16",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The search for a catalog decoded again the object streams the reader let go as much as what its first decodings of them weighed, {FirstDecodings} bytes: object stream 4 was not decoded again, and the objects it holds read as null until the search for a catalog ends."),
                OffsetOf(bytes, "4 0 obj"),
                ThrowsOnOpening: true,
                ReportsNothingElse: false)
            {
                WholeReports = [PdfDiagnosticCodes.TrailerRootRecovered],
                ReadsWholeAtGuard = true,
            };
        }

        private static byte[] Replace(byte[] file, string text, string replacement)
        {
            var written = Encoding.Latin1.GetString(file);
            written.Should().Contain(text);
            return Encoding.Latin1.GetBytes(written.Replace(text, replacement, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Gives an object stream's <c>/Type</c>, <c>/N</c> and <c>/First</c> for one member at byte 5, <paramref name="key"/>
    /// written as <paramref name="value"/>.
    /// </summary>
    private static string HeaderNaming(string key, string value) => key switch
    {
        "/N" => $"/Type /ObjStm /N {value} /First 5",
        "/First" => $"/Type /ObjStm /N 1 /First {value}",
        _ => $"/Type {value} /N 1 /First 5",
    };

    /// <summary>What object streams 4 and 5 of <see cref="ObjectStreamsReadInTurnFile"/> decode to, each.</summary>
    private const int ObjectStreamDecoded = 17 * 1024 * 1024;

    /// <summary>
    /// Writes a file whose object streams 4 and 5, which decode to 17 MB each, past half the budget the reader keeps them
    /// in, hold objects 10 to 17 in turn, the even ones in 4; the trailer's /Root names a string, and the catalog is object
    /// 16. With <paramref name="parametersCut"/>, object stream 6 holds object 18, and its <c>/DecodeParms</c> names object
    /// 14. With <paramref name="catalogWrittenDirectly"/>, object 16 is no catalog, and the catalog is object 30, written
    /// directly, which a row places only with <paramref name="catalogRow"/>.
    /// </summary>
    private static byte[] ObjectStreamsReadInTurnFile(bool parametersCut, bool catalogWrittenDirectly = false, bool catalogRow = false, string? headerKey = null)
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "(not a catalog)"), 0),
            [2] = (1, file.Object(2, Pages), 0),
        };
        var fourteen = headerKey switch { "/N" => "1", "/First" => "5", "/Type" => "/ObjStm", _ => "<< >>" };
        (int Number, string Body)[] even = [(10, "<< >>"), (12, "<< >>"), (14, fourteen), (16, catalogWrittenDirectly ? "<< >>" : Catalog)];
        (int Number, string Body)[] odd = [(11, "<< >>"), (13, "<< >>"), (15, "<< >>"), (17, "<< >>")];
        rows[4] = (1, file.PaddedObjectStream(4, even, ObjectStreamDecoded), 0);
        rows[5] = (1, file.PaddedObjectStream(5, odd, ObjectStreamDecoded), 0);

        for (var index = 0; index < 4; index++)
        {
            rows[even[index].Number] = (2, 4, index);
            rows[odd[index].Number] = (2, 5, index);
        }

        if (catalogWrittenDirectly)
        {
            var catalog = file.Object(30, Catalog);

            if (catalogRow)
            {
                rows[30] = (1, catalog, 0);
            }
        }

        if (parametersCut)
        {
            var data = HostileInputTests.Compress("18 0 << /Kept true >>"u8.ToArray());
            var entries = headerKey is null
                ? "/Type /ObjStm /N 1 /First 5 /DecodeParms 14 0 R"
                : HeaderNaming(headerKey, "14 0 R");
            rows[6] = (1, file.Object(6, string.Create(CultureInfo.InvariantCulture, $"<< {entries} /Filter /FlateDecode /Length {data.Length} >>\nstream\n{Encoding.Latin1.GetString(data)}\nendstream")), 0);
            rows[18] = (2, 6, 0);
        }

        file.CompressedCrossReferenceStream(20, catalogRow ? 31 : 21, number => rows.TryGetValue(number, out var row) ? row : (0, 0, 0), "/Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>The first of the objects object stream 60 of <see cref="DecodedThroughACutValueFile"/> holds.</summary>
    private const int FirstMemberOfSixty = 61;

    /// <summary>The first of the candidates for a catalog of <see cref="CandidatesThroughALengthCutFile"/>.</summary>
    private const int FirstCandidate = 1000;

    /// <summary>
    /// Writes a file whose /Root names a string — the catalog, with <paramref name="rootIsCatalog"/> —, read in number order
    /// by a search for a catalog that may decode its object streams again once (<see cref="WriteStreamsCutInTurn"/>), and
    /// whose object stream 60 holds <paramref name="members"/> objects, 61 and on, each <c>&lt;&lt; /Kept true &gt;&gt;</c>,
    /// whose decoding needs object 28: its <c>/Filter</c>, for <paramref name="value"/> <c>filter</c>; its
    /// <c>/DecodeParms</c>, a predictor its data is written with, for <c>parameters</c>; or its <c>/Length</c>, for
    /// <c>length</c>, its data not compressed and spelling an <c>endstream</c> and an <c>endobj</c> between its header and its
    /// first object. Read without object 28, the stream's header is still encoded, or garbage, or its objects lie past its
    /// data. For <c>own</c>, the stream needs no other object, and a stray token heads its header, which lists no object: a
    /// fault of the file. The catalog comes after stream 60's objects: written directly, or, with
    /// <paramref name="catalogCut"/>, in object stream 250. With <paramref name="countMisplaced"/>, the stream's <c>/N</c> is
    /// the object after the catalog, whose row places it in a string's data, where no header lies near.
    /// </summary>
    private static byte[] DecodedThroughACutValueFile(
        string value, int members, bool catalogCut, out long dataStart, bool rootIsCatalog = false, bool countMisplaced = false)
    {
        const int Columns = 64;
        var catalog = FirstMemberOfSixty + members;
        var header = new StringBuilder(value == "own" ? "x " : string.Empty);
        var bodies = new StringBuilder();

        for (var index = 0; index < members; index++)
        {
            header.Append(CultureInfo.InvariantCulture, $"{FirstMemberOfSixty + index} {bodies.Length} ");
            bodies.Append("<< /Kept true >> ");
        }

        if (value == "length")
        {
            header.Append("\nendstream\nendobj\n");
        }

        var first = header.Length;
        var plain = Encoding.Latin1.GetBytes((header.ToString() + bodies).PadRight((header.Length + bodies.Length + Columns - 1) / Columns * Columns));
        var data = value switch
        {
            "length" => plain,
            "parameters" => HostileInputTests.Compress(TiffPredicted(plain, Columns)),
            _ => HostileInputTests.Compress(plain),
        };
        var (held, entries) = value switch
        {
            "filter" => ("/FlateDecode", string.Create(CultureInfo.InvariantCulture, $"/Filter 28 0 R /Length {data.Length}")),
            "parameters" => (
                string.Create(CultureInfo.InvariantCulture, $"<< /Predictor 2 /Colors 1 /BitsPerComponent 8 /Columns {Columns} >>"),
                string.Create(CultureInfo.InvariantCulture, $"/Filter /FlateDecode /DecodeParms 28 0 R /Length {data.Length}")),
            "length" => (data.Length.ToString(CultureInfo.InvariantCulture), "/Length 28 0 R"),
            _ => ("<< >>", string.Create(CultureInfo.InvariantCulture, $"/Filter /FlateDecode /Length {data.Length}")),
        };
        var file = new HandWrittenFile(version: "1.5");
        var rows = WriteStreamsCutInTurn(file, held, catalogCut ? catalog : 0);
        var count = countMisplaced ? string.Create(CultureInfo.InvariantCulture, $"{catalog + 1} 0 R") : members.ToString(CultureInfo.InvariantCulture);
        rows[60] = (1, file.Object(60, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N {count} /First {first} {entries} >>\nstream\n{Encoding.Latin1.GetString(data)}\nendstream")), 0);

        if (countMisplaced)
        {
            file.Object(catalog + 1, members.ToString(CultureInfo.InvariantCulture));
            rows[catalog + 1] = (1, file.Padding(7, 2048) + 1024, 0);
        }

        for (var index = 0; index < members; index++)
        {
            rows[FirstMemberOfSixty + index] = (2, 60, index);
        }

        if (!catalogCut)
        {
            rows[catalog] = (1, file.Object(catalog, Catalog), 0);
        }

        var last = rows.Keys.Max();
        var root = string.Create(CultureInfo.InvariantCulture, $"/Root {(rootIsCatalog ? catalog : 1)} 0 R");
        file.CompressedCrossReferenceStream(last + 1, last + 2, number => rows.TryGetValue(number, out var row) ? row : (0, 0, 0), root);
        var bytes = file.Bytes;
        var at = OffsetOf(bytes, "\n60 0 obj");
        dataStart = at + bytes.AsSpan(at).IndexOf("stream\n"u8) + "stream\n".Length;
        return bytes;

        static byte[] TiffPredicted(byte[] plain, int columns)
        {
            var encoded = new byte[plain.Length];

            for (var index = 0; index < plain.Length; index++)
            {
                encoded[index] = index % columns == 0 ? plain[index] : (byte)(plain[index] - plain[index - 1]);
            }

            return encoded;
        }
    }

    /// <summary>
    /// Writes a file whose /Root names a string — the catalog, with <paramref name="rootIsCatalog"/> —, read in number order
    /// by a search for a catalog that may decode its object streams again once (<see cref="WriteStreamsCutInTurn"/>), and
    /// whose <paramref name="candidates"/> candidates, objects 1000 and on, lead to object 61: a stream whose <c>/Length</c> is
    /// object 28, and whose data spells an <c>endstream</c> and an <c>endobj</c> before its own. Each names it as its
    /// <c>/Type</c>; or, with <paramref name="heldByObjectStream"/>, object stream 60, its <c>/Length</c> direct, holds them,
    /// and names it as its own <c>/Type</c>. The catalog comes after them.
    /// </summary>
    private static byte[] CandidatesThroughALengthCutFile(int candidates, out long at, bool heldByObjectStream = false, bool rootIsCatalog = false)
    {
        const string Data = "01\nendstream\nendobj\n89";
        var file = new HandWrittenFile(version: "1.5");
        var rows = WriteStreamsCutInTurn(file, Data.Length.ToString(CultureInfo.InvariantCulture), catalog: 0);
        at = file.Object(61, $"<< /Length 28 0 R /Padding ({new string('x', 4096)}) >>\nstream\n{Data}\nendstream");
        rows[61] = (1, at, 0);

        if (heldByObjectStream)
        {
            var header = new StringBuilder();

            for (var index = 0; index < candidates; index++)
            {
                header.Append(CultureInfo.InvariantCulture, $"{FirstCandidate + index} {index * 6} ");
                rows[FirstCandidate + index] = (2, 60, index);
            }

            var data = header + string.Concat(Enumerable.Repeat("<< >> ", candidates));
            rows[60] = (1, file.Object(60, string.Create(CultureInfo.InvariantCulture, $"<< /Type 61 0 R /N {candidates} /First {header.Length} /Length {data.Length} >>\nstream\n{data}\nendstream")), 0);
        }
        else
        {
            for (var number = FirstCandidate; number < FirstCandidate + candidates; number++)
            {
                rows[number] = (1, file.Object(number, "<< /Type 61 0 R >>"), 0);
            }
        }

        var catalog = FirstCandidate + candidates;
        rows[catalog] = (1, file.Object(catalog, Catalog), 0);
        var root = string.Create(CultureInfo.InvariantCulture, $"/Root {(rootIsCatalog ? catalog : 1)} 0 R");
        file.CompressedCrossReferenceStream(catalog + 1, catalog + 2, number => rows.TryGetValue(number, out var row) ? row : (0, 0, 0), root);
        return file.Bytes;
    }

    /// <summary>
    /// Writes the objects of <see cref="WriteStreamsCutInTurn"/>, and <paramref name="count"/> streams, objects 300 and on, whose
    /// <c>/Length</c> is object 28, and whose data spells an endstream before its end; the catalog comes last. The trailer
    /// holds <paramref name="entries"/> besides its <c>/Root</c>.
    /// </summary>
    private static byte[] StreamsThroughALengthCutFile(int count, string entries = "")
    {
        const string Data = "01\nendstream\nendobj\n89";
        var file = new HandWrittenFile(version: "1.5");
        var rows = WriteStreamsCutInTurn(file, Data.Length.ToString(CultureInfo.InvariantCulture), catalog: 0);

        for (var number = 300; number < 300 + count; number++)
        {
            rows[number] = (1, file.Object(number, $"<< /Length 28 0 R >>\nstream\n{Data}\nendstream"), 0);
        }

        var catalog = 300 + count;
        rows[catalog] = (1, file.Object(catalog, Catalog), 0);
        file.CompressedCrossReferenceStream(
            catalog + 1, catalog + 2, number => rows.TryGetValue(number, out var row) ? row : (0, 0, 0), string.Create(CultureInfo.InvariantCulture, $"/Root {catalog} 0 R {entries}"));
        return file.Bytes;
    }

    /// <summary>
    /// Writes to <paramref name="file"/> object 1, a string, and an empty page tree; object streams 4, 5 and 6, which decode
    /// to <see cref="ObjectStreamDecoded"/> bytes each, past half the budget the reader keeps them in, and hold objects 10 to
    /// 33 in turn, object 28, in stream 4, holding <paramref name="value"/>; and object stream 250, which holds object 9 and,
    /// where <paramref name="catalog"/> is not 0, that catalog, stream 5 then holding the 80 objects after it besides. Gives
    /// the rows written.
    /// </summary>
    /// <remarks>
    /// Read in number order by a walk that may decode its object streams again once, each of streams 4, 5 and 6 is decoded
    /// twice for objects 10 to 15, after which the objects streams 4 and 5 hold read as null, the walk's guard cut — 28, and
    /// the 80 objects after the catalog, among them —, and so does the catalog, once stream 250 was let go. A search for a
    /// catalog that reads them again, stream by stream, reads object 28 whole as it decodes stream 4, and lets go of it as
    /// it caches 86 objects of stream 5, past a cache of 64 objects.
    /// </remarks>
    private static Dictionary<int, (int Type, long Field2, int Field3)> WriteStreamsCutInTurn(HandWrittenFile file, string value, int catalog)
    {
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "(not a catalog)"), 0),
            [2] = (1, file.Object(2, Pages), 0),
        };
        var held = new List<(int Number, string Body)>[] { [], [], [] };

        for (var number = 10; number < 34; number++)
        {
            held[(number - 10) % 3].Add((number, number == 28 ? value : "<< >>"));
        }

        for (var number = catalog + 1; catalog != 0 && number <= catalog + 80; number++)
        {
            held[1].Add((number, "<< >>"));
        }

        for (var stream = 0; stream < 3; stream++)
        {
            rows[4 + stream] = (1, file.PaddedObjectStream(4 + stream, [.. held[stream]], ObjectStreamDecoded), 0);

            for (var index = 0; index < held[stream].Count; index++)
            {
                rows[held[stream][index].Number] = (2, 4 + stream, index);
            }
        }

        rows[250] = (1, file.ObjectStream(250, catalog == 0 ? [(9, "<< >>")] : [(9, "<< >>"), (catalog, Catalog)]), 0);
        rows[9] = (2, 250, 0);

        if (catalog != 0)
        {
            rows[catalog] = (2, 250, 1);
        }

        return rows;
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

    [Fact]
    public void Decoding_into_diagnostics_of_one_s_own_reports_the_cut_max_stream_length_made_each_time()
    {
        // Object 4's 2,000 bytes run past MaxStreamLength: the reader reported the cut once, in the document's diagnostics,
        // and diagnostics a caller passed to Decode never held it, though limit.decoded-stream reaches them each time.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
            [4] = file.Object(4, "<< /Length 2000 >>\nstream\n" + new string('x', 2000) + "\nendstream"),
        };
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxStreamLength = 1024 } });
        var stream = document.GetObject(new PdfObjectId(4)).AsStream().Required();
        var mine = new PdfDiagnostics();

        stream.Decode(mine).Length.Should().Be(1024);
        stream.Decode(mine).Length.Should().Be(1024);

        mine.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitStreamLength, PdfDiagnosticCodes.LimitStreamLength);
        mine[0].Message.Should().Be(
            "A stream's data runs past 1 KB; only what lies within it was read. Raise PdfReaderLimits.MaxStreamLength to read past it.");
        document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.LimitStreamLength).Should().ContainSingle();
    }
}
