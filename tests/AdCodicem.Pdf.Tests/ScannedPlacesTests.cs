using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// What the rebuild's scan for objects takes of a file is recorded once a document, whatever was read before, and a
/// rebuild takes its headers from that record rather than scanning the file again.
/// </summary>
public class ScannedPlacesTests
{
    private const string Catalog = "<< /Type /Catalog /Pages 2 0 R >>";
    private const string Pages = "<< /Type /Pages /Kids [] /Count 0 >>";

    [Theory]
    [InlineData("a header between objects", "Clean")]
    [InlineData("a header whose number is written with a sign", "Clean")]
    [InlineData("a header in a comment between objects", null)]
    [InlineData("a header in a string between objects", null)]
    [InlineData("a header in the data of a stream whose length an endstream confirms", null)]
    [InlineData("a header in the data of a stream whose length is an object", "Raw")]
    [InlineData("a header in what a string that holds endobj runs into", "Raw")]
    public void A_header_is_recorded_where_the_scan_takes_it_with_how_it_took_it(string shape, string? kind)
    {
        var text = shape switch
        {
            "a header between objects" => "9 0 obj\n(nine)\nendobj\n",
            "a header whose number is written with a sign" => "+9 0 obj\n(nine)\nendobj\n",
            "a header in a comment between objects" => "% 9 0 obj\n",
            "a header in a string between objects" => "(x 9 0 obj)\n",
            "a header in the data of a stream whose length an endstream confirms" =>
                "5 0 obj\n<< /Length 22 >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n",
            "a header in the data of a stream whose length is an object" =>
                "5 0 obj\n<< /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj\nendstream\nendobj\n6 0 obj\n22\nendobj\n",
            _ => "5 0 obj\n(a ( b\nendobj\n9 0 obj\n(nine)\nendobj\n) )\nendobj\n",
        };
        var place = text.Contains("+9", StringComparison.Ordinal) ? text.IndexOf("+9", StringComparison.Ordinal) : text.IndexOf("9 0 obj", StringComparison.Ordinal);
        var (file, at) = Sound(text, place);

        using var document = PdfDocument.Open(file);
        var memo = document.Reader.GetScanMemo();

        document.WasRepaired.Should().BeFalse();
        memo.IsWhole.Should().BeTrue();
        memo.InScanOrder.Should().BeTrue();

        if (kind is null)
        {
            memo.TryGetHeaderAt(at, out _).Should().BeFalse();
            Headers(memo, 0, long.MaxValue).Should().NotContain(header => header.Number == 9);
        }
        else
        {
            memo.TryGetHeaderAt(at, out var header).Should().BeTrue();
            header.Should().Be(new ScannedHeader(at, 9, 0, Enum.Parse<ScannedPlaceKind>(kind)));
            memo.TryGetHeaderAt(at + 1, out _).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("a keyword between objects", "Clean")]
    [InlineData("a keyword glued to an end-of-file marker", "Clean")]
    [InlineData("a keyword after a closing bracket", "Clean")]
    [InlineData("a keyword in the data of a stream whose length is an object", "Raw")]
    [InlineData("a keyword in a comment", null)]
    [InlineData("a keyword in a string between objects", null)]
    [InlineData("a keyword inside an object", null)]
    [InlineData("a name", null)]
    [InlineData("a longer word", null)]
    [InlineData("the end of startxref", null)]
    public void An_xref_keyword_is_recorded_only_where_a_load_reads_it_as_a_token_the_scan_takes(string shape, string? kind)
    {
        var (text, word) = shape switch
        {
            "a keyword between objects" => ("xref\n", "xref"),
            "a keyword glued to an end-of-file marker" => ("%%EOFxref\n", "xref"),
            "a keyword after a closing bracket" => ("[1]xref\n", "xref"),
            "a keyword in the data of a stream whose length is an object" =>
                ("5 0 obj\n<< /Length 6 0 R >>\nstream\nxref\nendstream\nendobj\n6 0 obj\n5\nendobj\n", "xref"),
            "a keyword in a comment" => ("% xref\n", "xref"),
            "a keyword in a string between objects" => ("(see xref)\n", "xref"),
            "a keyword inside an object" => ("9 0 obj\n(an xref)\nendobj\n", "xref"),
            "a name" => ("/xref\n", "xref"),
            "a longer word" => ("xrefs\n", "xrefs"),
            _ => (string.Empty, string.Empty),
        };
        var (file, at) = Sound(text, word.Length == 0 ? 0 : text.IndexOf(word, StringComparison.Ordinal));
        var written = Encoding.Latin1.GetString(file);
        var table = written.LastIndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        at = word.Length == 0 ? written.LastIndexOf("startxref", StringComparison.Ordinal) + "start".Length : at;

        using var document = PdfDocument.Open(file);
        var memo = document.Reader.GetScanMemo();

        memo.TryGetXRefKeywordAt(table, out var tableKind).Should().BeTrue();
        tableKind.Should().Be(ScannedPlaceKind.Clean);
        memo.TryGetHeaderAt(table, out _).Should().BeFalse();

        if (kind is null)
        {
            memo.TryGetXRefKeywordAt(at, out _).Should().BeFalse();
        }
        else
        {
            memo.TryGetXRefKeywordAt(at, out var found).Should().BeTrue();
            found.Should().Be(Enum.Parse<ScannedPlaceKind>(kind));
        }
    }

    [Fact]
    public void A_header_whose_comment_between_its_tokens_holds_an_xref_keyword_is_taken_and_the_keyword_is_not()
    {
        // The keyword lies in a comment the header's walk crosses: the scan tells it apart from the header, which it takes,
        // and indexes, as it did before it looked for keywords.
        const string Text = "9 % xref\n0 obj\n(nine)\nendobj\n";
        var (file, at) = Sound(Text, 0);
        var broken = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(file).Replace("startxref\n", "startxref\n9", StringComparison.Ordinal));

        using var document = PdfDocument.Open(broken);
        var memo = document.Reader.GetScanMemo();

        document.WasRepaired.Should().BeTrue();
        memo.TryGetHeaderAt(at, out var header).Should().BeTrue();
        header.Should().Be(new ScannedHeader(at, 9, 0, ScannedPlaceKind.Clean));
        memo.TryGetXRefKeywordAt(at + 4, out _).Should().BeFalse();
        document.Reader.Index.TryGet(9, out var entry).Should().BeTrue();
        document.Reader.PositionOf(entry).Should().Be(at);
        document.GetObject(new PdfObjectId(9)).AsText().Should().Be("nine");
    }

    [Fact]
    public void The_memo_is_the_same_whatever_was_read_before_it_was_made()
    {
        // Object 4's row lies outside the file: reading it rebuilds the index. The memo is made by the first that needs it —
        // a request for it before any read, the rebuild, or a request after other reads —, and is the same each time.
        var file = Updated(out _);

        using var asked = PdfDocument.Open(file);
        var first = asked.Reader.GetScanMemo();
        asked.GetObject(new PdfObjectId(4));

        using var rebuilt = PdfDocument.Open(file);
        rebuilt.GetObject(new PdfObjectId(4));
        var second = rebuilt.Reader.GetScanMemo();

        using var read = PdfDocument.Open(file);
        read.GetObject(new PdfObjectId(1));
        read.GetObject(new PdfObjectId(3));
        var third = read.Reader.GetScanMemo();

        asked.WasRepaired.Should().BeTrue();
        rebuilt.WasRepaired.Should().BeTrue();
        read.WasRepaired.Should().BeFalse();
        asked.Reader.GetScanMemo().Should().BeSameAs(first);
        rebuilt.Reader.GetScanMemo().Should().BeSameAs(second);
        Describe(second, file.Length).Should().Equal(Describe(first, file.Length));
        Describe(third, file.Length).Should().Equal(Describe(first, file.Length));
        Headers(first, 0, long.MaxValue).Select(header => header.Number).Should().Equal(1, 2, 3, 5, 9, 3);
    }

    [Fact]
    public void A_rebuild_takes_the_headers_from_the_memo_and_reads_no_byte_of_the_file_for_its_scan()
    {
        // The same rebuild, run once the memo was asked for and once before: the same index, the same objects and the same
        // diagnostics, a definition met again among them; the rebuild that takes the memo reads the file a whole length less.
        var file = Updated(out _);

        var memoFirst = new StrictCountingSource(file);
        using var replayed = PdfDocument.Open(memoFirst, options: null, ownsSource: false);
        replayed.Reader.GetScanMemo();
        var before = memoFirst.BytesRead;
        replayed.GetObject(new PdfObjectId(4));
        var replaying = memoFirst.BytesRead - before;

        var rebuildFirst = new StrictCountingSource(file);
        using var scanned = PdfDocument.Open(rebuildFirst, options: null, ownsSource: false);
        before = rebuildFirst.BytesRead;
        scanned.GetObject(new PdfObjectId(4));
        var scanning = rebuildFirst.BytesRead - before;

        replayed.WasRepaired.Should().BeTrue();
        replayed.Diagnostics.Should().Equal(scanned.Diagnostics);
        replayed.Diagnostics.Should().Contain(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined);
        Entries(replayed).Should().Equal(Entries(scanned));
        replayed.GetObject(new PdfObjectId(3)).AsText().Should().Be("three, updated");
        scanning.Should().BeGreaterThanOrEqualTo(replaying + file.Length, "the rebuild that takes the memo scans nothing");
    }

    [Fact]
    public void A_rebuild_scans_again_where_the_memo_does_not_hold_its_headers_in_the_order_the_scan_took_them()
    {
        // In the data of a stream whose length is an object, the scan takes a header of 8 in a comment, then another header
        // of 8, whose tokens run back across that comment: the memo puts the second before the first, and a rebuild scans
        // the file again, as one run before the memo was made does, so that the definition the scan takes last wins.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        var data = file.Position + "5 0 obj\n<< /Length 6 0 R >>\nstream\n".Length;
        file.Raw("5 0 obj\n<< /Length 6 0 R >>\nstream\n8 % 8 0 obj <\n0 obj (x)\nendstream\nendobj\n");
        rows[4] = 999_999;
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        var bytes = file.Bytes;

        var memoFirst = new StrictCountingSource(bytes);
        using var asked = PdfDocument.Open(memoFirst, options: null, ownsSource: false);
        var memo = asked.Reader.GetScanMemo();
        var before = memoFirst.BytesRead;
        asked.GetObject(new PdfObjectId(4));

        using var rebuilt = PdfDocument.Open(bytes);
        rebuilt.GetObject(new PdfObjectId(4));

        memo.InScanOrder.Should().BeFalse();
        Headers(memo, data, data + 20).Should().Equal(
            new ScannedHeader(data, 8, 0, ScannedPlaceKind.Raw), new ScannedHeader(data + 4, 8, 0, ScannedPlaceKind.Raw));
        asked.WasRepaired.Should().BeTrue();
        asked.Diagnostics.Should().Equal(rebuilt.Diagnostics);
        Entries(asked).Should().Equal(Entries(rebuilt));
        asked.Reader.Index.TryGet(8, out var entry).Should().BeTrue();
        asked.Reader.PositionOf(entry).Should().Be(data);
        (memoFirst.BytesRead - before).Should().BeGreaterThanOrEqualTo(bytes.Length, "the rebuild scans the file again");
    }

    [Theory]
    [InlineData("made before the rebuild", "headers repeated at varied distances")]
    [InlineData("made before the rebuild", "one header repeated at one distance")]
    [InlineData("made by the rebuild", "headers repeated at varied distances")]
    [InlineData("made by the rebuild", "one header repeated at one distance")]
    public void A_memo_past_its_bound_says_where_it_stops_being_whole_and_the_newest_definition_still_wins(string when, string repeats)
    {
        // MaxIndexEntries of 8: the index holds four numbers, while the scan takes the headers of 1 and 2, twenty older ones
        // of 3, the newest of 3, a keyword and a trailer keyword. The memo holds the first eight places, and stops being whole
        // where the ninth starts, which is reported once; the rebuild, made before the memo or past it, still serves the
        // newest definition of 3.
        var file = Repeated(repeats == "one header repeated at one distance", out var definitions);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 } };

        using var document = PdfDocument.Open(file, options);
        var made = when == "made before the rebuild" ? document.Reader.GetScanMemo() : null;
        document.GetObject(new PdfObjectId(4));
        var memo = document.Reader.GetScanMemo();

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(3)).AsText().Should().Be("newest");
        (made ?? memo).Should().BeSameAs(memo);
        memo.IsWhole.Should().BeFalse();
        memo.Count.Should().Be(8);
        memo.FullAt.Should().Be(definitions[6]);
        memo.WholeUntil.Should().BeGreaterThan(definitions[5]).And.BeLessThanOrEqualTo(definitions[6]);
        memo.Trailers.Should().BeNull();
        memo.TryGetHeaderAt(definitions[5], out _).Should().BeTrue();
        memo.TryGetHeaderAt(definitions[6], out _).Should().BeFalse();
        Headers(memo, 0, long.MaxValue).Should().HaveCount(8);
        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries).Which;
        report.Position.Should().Be(definitions[6]);
        report.Message.Should().Be(
            "The scan for objects recorded 8 object headers and keywords; those past them were not recorded. Raise PdfReaderLimits.MaxIndexEntries to read past it.");
    }

    [Fact]
    public void A_memo_past_its_bound_is_not_reported_again_where_the_index_reported_the_bound()
    {
        // MaxIndexEntries of 2: the chain fills the index, which is reported, and the memo, asked for, fills too, which the
        // one report covers.
        var file = Repeated(alike: false, out _);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 2 } };

        using var document = PdfDocument.Open(file, options);
        var memo = document.Reader.GetScanMemo();

        memo.IsWhole.Should().BeFalse();
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries)
            .Which.Message.Should().StartWith("The index reached 2 entries;");
    }

    [Fact]
    public void A_memo_past_its_bound_throws_once_for_a_document_opened_to_throw_on_a_guard()
    {
        // Asked for, the memo throws the guard as it is made, and is kept as far as the guard let it be; made by a rebuild,
        // the guard is thrown once the index is whole, and the rebuild serves the newest definition.
        var file = Repeated(alike: false, out var definitions);
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 } };

        using (var asked = PdfDocument.Open(file, options))
        {
            var thrown = FluentActions.Invoking(() => asked.Reader.GetScanMemo()).Should().Throw<PdfLimitExceededException>().Which;
            thrown.LimitName.Should().Be(nameof(PdfReaderLimits.MaxIndexEntries));
            thrown.Position.Should().Be(definitions[6]);
            asked.Reader.GetScanMemo().FullAt.Should().Be(definitions[6]);
        }

        using var rebuilt = PdfDocument.Open(file, options);
        FluentActions.Invoking(() => rebuilt.GetObject(new PdfObjectId(4))).Should().Throw<PdfLimitExceededException>()
            .Which.Position.Should().Be(definitions[6]);
        rebuilt.GetObject(new PdfObjectId(3)).AsText().Should().Be("newest");
        rebuilt.Reader.GetScanMemo().IsWhole.Should().BeFalse();
    }

    [Fact]
    public void A_memo_a_rebuild_the_source_fails_made_is_made_again_and_reported_again()
    {
        // The source fails the rebuild as it merges the trailer, past the scan: what the rebuild reported is withdrawn, the
        // memo it made with it, and the read that runs the rebuild again makes the memo again and reports its bound once.
        var file = Repeated(alike: false, out var definitions);
        var trailer = Encoding.Latin1.GetString(file).LastIndexOf("trailer", StringComparison.Ordinal) + "trailer".Length;
        using var source = new FailingSource(file);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 } };
        using var document = PdfDocument.Open(source, options, ownsSource: false);
        source.FailAt = trailer;

        FluentActions.Invoking(() => document.GetObject(new PdfObjectId(4))).Should().Throw<IOException>();

        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries);
        document.GetObject(new PdfObjectId(3)).AsText().Should().Be("newest");
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries)
            .Which.Position.Should().Be(definitions[6]);
    }

    [Fact]
    public void Places_of_one_header_at_one_distance_are_read_back_one_by_one()
    {
        var memo = new ScanMemo(100);

        foreach (var at in new long[] { 100, 108, 116, 124 })
        {
            memo.TryAddHeader(at, 7, 0, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        }

        memo.TryAddHeader(200, 8, 65_535, ScannedPlaceKind.Raw, floor: 0).Should().BeTrue();
        memo.Complete(new ScannedTrailers());

        memo.Count.Should().Be(5);
        memo.IsWhole.Should().BeTrue();
        memo.WholeUntil.Should().Be(long.MaxValue);
        memo.InScanOrder.Should().BeTrue();
        memo.Trailers.Should().NotBeNull();
        Headers(memo, 0, long.MaxValue).Should().Equal(
            Header(100, 7), Header(108, 7), Header(116, 7), Header(124, 7), new ScannedHeader(200, 8, 65_535, ScannedPlaceKind.Raw));
        Headers(memo, 110, 125).Should().Equal(Header(116, 7), Header(124, 7));
        Headers(memo, 108, 109).Should().Equal(Header(108, 7));
        Headers(memo, 101, 116).Should().Equal(Header(108, 7));
        Headers(memo, 125, 200).Should().BeEmpty();

        foreach (var at in new long[] { 100, 108, 116, 124 })
        {
            memo.TryGetHeaderAt(at, out var header).Should().BeTrue();
            header.Should().Be(Header(at, 7));
        }

        memo.TryGetHeaderAt(99, out _).Should().BeFalse();
        memo.TryGetHeaderAt(112, out _).Should().BeFalse();
        memo.TryGetHeaderAt(132, out _).Should().BeFalse();
        memo.TryGetHeaderAt(200, out var last).Should().BeTrue();
        last.Should().Be(new ScannedHeader(200, 8, 65_535, ScannedPlaceKind.Raw));
    }

    [Theory]
    [InlineData("another number")]
    [InlineData("another generation")]
    [InlineData("another kind")]
    [InlineData("another distance")]
    [InlineData("a distance past what an int holds")]
    public void Places_that_differ_from_a_run_are_held_apart_from_it(string shape)
    {
        var memo = new ScanMemo(100);
        var stride = shape == "a distance past what an int holds" ? 3_000_000_000L : 8;
        var third = shape switch
        {
            "another number" => new ScannedHeader(2 * stride + 0, 9, 0, ScannedPlaceKind.Clean),
            "another generation" => new ScannedHeader(2 * stride, 7, 1, ScannedPlaceKind.Clean),
            "another kind" => new ScannedHeader(2 * stride, 7, 0, ScannedPlaceKind.Raw),
            "another distance" => new ScannedHeader(2 * stride + 1, 7, 0, ScannedPlaceKind.Clean),
            _ => new ScannedHeader(2 * stride, 7, 0, ScannedPlaceKind.Clean),
        };
        ScannedHeader[] places = [Header(0, 7), Header(stride, 7), third, Header(third.Position + 5, 7)];

        foreach (var place in places)
        {
            memo.TryAddHeader(place.Position, place.Number, place.Generation, place.Kind, floor: 0).Should().BeTrue();
        }

        Headers(memo, 0, long.MaxValue).Should().Equal(places);

        foreach (var place in places)
        {
            memo.TryGetHeaderAt(place.Position, out var header).Should().BeTrue();
            header.Should().Be(place);
        }

        memo.TryGetHeaderAt(stride / 2, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(50L)]
    [InlineData(100L)]
    [InlineData(104L)]
    [InlineData(116L)]
    [InlineData(124L)]
    [InlineData(140L)]
    [InlineData(148L)]
    public void A_place_taken_before_the_last_is_put_in_its_place_and_splits_the_run_it_falls_within(long position)
    {
        // A run of seven places of 7, from 100 to 148, eight bytes apart, then a header of 9 the scan took after it.
        var memo = new ScanMemo(100);
        var run = new List<ScannedHeader>();

        for (var at = 100L; at <= 148; at += 8)
        {
            memo.TryAddHeader(at, 7, 0, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
            run.Add(Header(at, 7));
        }

        memo.TryAddHeader(position, 9, 0, ScannedPlaceKind.Raw, floor: 0).Should().BeTrue();

        var inserted = new ScannedHeader(position, 9, 0, ScannedPlaceKind.Raw);
        var expected = run.Where(header => header.Position <= position).Append(inserted).Concat(run.Where(header => header.Position > position)).ToList();
        memo.InScanOrder.Should().BeFalse();
        memo.Count.Should().Be(8);
        Headers(memo, 0, long.MaxValue).Should().Equal(expected);
        Headers(memo, position + 1, long.MaxValue).Should().Equal(run.Where(header => header.Position > position));
        Headers(memo, 0, position).Should().Equal(run.Where(header => header.Position < position));

        foreach (var header in run.Where(header => header.Position != position))
        {
            memo.TryGetHeaderAt(header.Position, out var found).Should().BeTrue();
            found.Should().Be(header);
        }

        memo.TryGetHeaderAt(position, out var at9).Should().BeTrue();
        at9.Should().Be(inserted);
    }

    [Fact]
    public void Xref_keywords_are_found_where_they_start_and_are_no_headers()
    {
        var memo = new ScanMemo(100);
        memo.TryAddXRefKeyword(10, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        memo.TryAddXRefKeyword(20, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        memo.TryAddXRefKeyword(30, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        memo.TryAddHeader(40, 5, 0, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        memo.TryAddXRefKeyword(50, ScannedPlaceKind.Raw, floor: 0).Should().BeTrue();

        Headers(memo, 0, long.MaxValue).Should().Equal(Header(40, 5));
        Headers(memo, 15, long.MaxValue).Should().Equal(Header(40, 5));
        memo.TryGetXRefKeywordAt(20, out var kind).Should().BeTrue();
        kind.Should().Be(ScannedPlaceKind.Clean);
        memo.TryGetXRefKeywordAt(50, out kind).Should().BeTrue();
        kind.Should().Be(ScannedPlaceKind.Raw);
        memo.TryGetXRefKeywordAt(25, out _).Should().BeFalse();
        memo.TryGetXRefKeywordAt(40, out _).Should().BeFalse();
        memo.TryGetHeaderAt(20, out _).Should().BeFalse();
        memo.TryGetHeaderAt(30, out _).Should().BeFalse();
    }

    [Fact]
    public void A_memo_takes_no_place_past_its_bound_and_is_whole_only_before_the_first_it_left_out()
    {
        var memo = new ScanMemo(3);
        memo.TryAddHeader(10, 1, 0, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        memo.TryAddTrailer(20, floor: 15).Should().BeTrue();
        memo.TryAddXRefKeyword(30, ScannedPlaceKind.Clean, floor: 25).Should().BeTrue();

        memo.TryAddHeader(40, 2, 0, ScannedPlaceKind.Clean, floor: 35).Should().BeFalse();
        memo.TryAddHeader(50, 3, 0, ScannedPlaceKind.Clean, floor: 45).Should().BeFalse();
        memo.TryAddTrailer(60, floor: 55).Should().BeFalse();
        memo.Complete(new ScannedTrailers());

        memo.Count.Should().Be(3);
        memo.IsWhole.Should().BeFalse();
        memo.FullAt.Should().Be(40);
        memo.WholeUntil.Should().Be(35);
        memo.Trailers.Should().BeNull();
        Headers(memo, 0, long.MaxValue).Should().Equal(Header(10, 1));
        memo.TryGetXRefKeywordAt(30, out _).Should().BeTrue();

        var past = new ScanMemo(1);
        past.TryAddHeader(10, 1, 0, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        past.TryAddHeader(40, 2, 0, ScannedPlaceKind.Clean, floor: 60).Should().BeFalse();
        past.WholeUntil.Should().Be(40);
    }

    private static ScannedHeader Header(long at, int number) => new(at, number, 0, ScannedPlaceKind.Clean);

    /// <summary>Gets the headers <paramref name="memo"/> holds from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static List<ScannedHeader> Headers(ScanMemo memo, long from, long to)
    {
        var headers = new List<ScannedHeader>();

        foreach (var header in memo.HeadersWithin(from, to))
        {
            headers.Add(header);
        }

        return headers;
    }

    /// <summary>Says what <paramref name="memo"/> holds: its headers, its keywords at each byte of the file, its trailers.</summary>
    private static List<string> Describe(ScanMemo memo, int length)
    {
        var described = Headers(memo, 0, long.MaxValue).Select(header => header.ToString()).ToList();

        for (var at = 0; at < length; at++)
        {
            if (memo.TryGetXRefKeywordAt(at, out var kind))
            {
                described.Add(string.Create(CultureInfo.InvariantCulture, $"xref at {at}, {kind}"));
            }
        }

        described.Add(string.Create(CultureInfo.InvariantCulture, $"{memo.Count} places, whole: {memo.IsWhole}, in order: {memo.InScanOrder}"));
        described.Add(string.Join(", ", memo.Trailers!.Keywords) + " / " + string.Join(", ", memo.Trailers.Streams));
        return described;
    }

    /// <summary>Gets the entries of the index <paramref name="document"/> holds, by number.</summary>
    private static List<string> Entries(PdfDocument document)
    {
        var entries = new List<string>();

        foreach (var (number, entry) in document.Reader.Index)
        {
            entries.Add(string.Create(CultureInfo.InvariantCulture, $"{number}: {entry}"));
        }

        entries.Sort(StringComparer.Ordinal);
        return entries;
    }

    /// <summary>
    /// Writes a sound file of objects 1 and 2, then <paramref name="text"/>, then its table, and gives where the byte
    /// <paramref name="place"/> bytes into the text lies.
    /// </summary>
    private static (byte[] File, long At) Sound(string text, int place)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        var at = file.Position + place;
        file.Raw(text);
        file.Table(3, rows, "/Size 3 /Root 1 0 R");
        return (file.Bytes, at);
    }

    /// <summary>
    /// Writes a file whose object 3 an update defines again, with a stream whose length is an object that holds a header and
    /// a keyword, and whose row for object 4 lies outside the file: reading 4 rebuilds the index.
    /// </summary>
    private static byte[] Updated(out long updated)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages), [3] = file.Object(3, "(three)") };
        file.Raw("% a comment that holds 8 0 obj\n");
        file.Raw("5 0 obj\n<< /Length 6 0 R >>\nstream\n9 0 obj (inner) endobj xref\nendstream\nendobj\n");
        rows[4] = 999_999;
        var table = file.Table(5, rows, "/Size 5 /Root 1 0 R");
        updated = file.Object(3, "(three, updated)");
        var update = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \n3 2\n{updated:D10} 00000 n \n0000999999 00000 n \ntrailer\n<< /Size 5 /Root 1 0 R /Prev {table} >>\nstartxref\n{update}\n%%EOF\n"));
        return file.Bytes;
    }

    /// <summary>
    /// Writes a file whose object 3 is defined twenty times before its newest definition, each the same length where
    /// <paramref name="alike"/>, and whose row for object 4 lies outside the file: reading 4 rebuilds the index.
    /// </summary>
    private static byte[] Repeated(bool alike, out List<long> definitions)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        definitions = [];

        for (var i = 0; i < 20; i++)
        {
            definitions.Add(file.Object(3, alike ? "(old)" : string.Create(CultureInfo.InvariantCulture, $"(old {new string('x', i)})")));
        }

        definitions.Add(file.Object(3, "(newest)"));
        rows[3] = definitions[0];
        rows[4] = 999_999;
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>A file read from memory whose read starting at one offset fails once, with an I/O error.</summary>
    private sealed class FailingSource(byte[] data) : PdfFileSource
    {
        private readonly PdfFileSource _inner = FromMemory(data);

        /// <summary>Gets or sets where the read that fails starts; -1 for none.</summary>
        public long FailAt { get; set; } = -1;

        /// <inheritdoc/>
        public override long Length => _inner.Length;

        /// <inheritdoc/>
        public override int Read(long offset, Span<byte> buffer)
        {
            if (offset == FailAt)
            {
                FailAt = -1;
                throw new IOException("The read failed.");
            }

            return _inner.Read(offset, buffer);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
