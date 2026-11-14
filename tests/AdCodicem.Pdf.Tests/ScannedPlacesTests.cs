using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// What the rebuild's scan for objects takes of a file is recorded once a document, the first time it is asked for, whatever
/// was read before; a rebuild takes its headers from that record where one was made rather than scanning the file again, and
/// makes none of its own.
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

    [Theory]
    [InlineData(1_048_569)]
    [InlineData(1_048_570)]
    [InlineData(1_048_571)]
    [InlineData(1_048_572)]
    [InlineData(1_048_573)]
    [InlineData(1_048_574)]
    [InlineData(1_048_575)]
    public void An_xref_keyword_is_recorded_wherever_it_starts_against_the_end_of_a_piece_of_the_scan(int at)
    {
        // The scan reads the file a mebibyte at a time, the last byte of a piece read only to tell a keyword that ends the
        // bytes searched: a table whose keyword starts within the first piece's last seven bytes, the keyword ending within
        // the bytes searched, past them or at the piece's last byte, is recorded as one anywhere else.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        file.Raw(new string(' ', at - (int)file.Position - 1) + "\n");
        var table = file.Table(3, rows, "/Size 3 /Root 1 0 R");

        using var document = PdfDocument.Open(file.Bytes);
        var memo = document.Reader.GetScanMemo();

        table.Should().Be(at);
        memo.TryGetXRefKeywordAt(at, out var kind).Should().BeTrue();
        kind.Should().Be(ScannedPlaceKind.Clean);
    }

    [Theory]
    [InlineData(1_048_568)]
    [InlineData(1_048_569)]
    [InlineData(1_048_570)]
    [InlineData(1_048_571)]
    [InlineData(1_048_572)]
    [InlineData(1_048_573)]
    [InlineData(1_048_574)]
    [InlineData(1_048_575)]
    public void A_trailer_keyword_is_taken_wherever_it_starts_against_the_end_of_a_piece_of_the_scan(int at)
    {
        // A trailer keyword that starts within the first piece's last eight bytes, ending within the bytes searched, past them
        // or at the piece's last byte, is taken as one anywhere else: the rebuild merges its dictionary, keys only it holds
        // among them, and the memo holds the same places wherever it lies.
        var file = new HandWrittenFile();
        file.Object(1, Catalog);
        file.Object(2, Pages);
        file.Object(3, "<< /Title (three) >>");
        file.Raw(new string(' ', at - (int)file.Position - 1) + "\n");
        file.Raw("trailer\n<< /Size 4 /Root 1 0 R /Info 3 0 R /ID [<01> <02>] >>\nstartxref\n99999999\n%%EOF\n");

        using var document = PdfDocument.Open(file.Bytes);
        var memo = document.Reader.GetScanMemo();

        Encoding.Latin1.GetString(file.Bytes).IndexOf("trailer", StringComparison.Ordinal).Should().Be(at);
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.TrailerRootRecovered);
        document.Trailer.GetRaw(PdfName.Info).Should().BeOfType<PdfReference>().Which.Id.Should().Be(new PdfObjectId(3));
        document.Trailer.ContainsKey(PdfName.ID).Should().BeTrue();
        memo.Count.Should().Be(4);
        memo.IsWhole.Should().BeTrue();
        memo.Trailers!.Keywords.Should().Equal(at + "trailer".Length);
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
        // Object 4's row lies outside the file: reading it rebuilds the index. The memo is made by the first request for it —
        // before any read, after the rebuild, or after other reads —, and is the same each time.
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
    public void A_rebuild_takes_the_headers_from_a_memo_made_before_it_reading_no_byte_for_its_scan_and_makes_none_itself()
    {
        // The same rebuild, run once the memo was asked for and once before: the same index, the same objects and the same
        // diagnostics, a definition met again among them; the rebuild that takes the memo reads the file a whole length less.
        // The one run first makes no memo: asked for after it, the memo is made by a scan of the file of its own, and is the
        // memo asked for first.
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
        before = rebuildFirst.BytesRead;
        var madeAfter = scanned.Reader.GetScanMemo();
        var askingAfter = rebuildFirst.BytesRead - before;

        replayed.WasRepaired.Should().BeTrue();
        replayed.Diagnostics.Should().Equal(scanned.Diagnostics);
        replayed.Diagnostics.Should().Contain(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined);
        Entries(replayed).Should().Equal(Entries(scanned));
        replayed.GetObject(new PdfObjectId(3)).AsText().Should().Be("three, updated");
        scanning.Should().BeGreaterThanOrEqualTo(replaying + file.Length, "the rebuild that takes the memo scans nothing");
        askingAfter.Should().BeGreaterThanOrEqualTo(file.Length, "the rebuild run first made no memo");
        Describe(madeAfter, file.Length).Should().Equal(Describe(replayed.Reader.GetScanMemo(), file.Length));
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
    [InlineData("asked for after the rebuild", "headers repeated at varied distances")]
    [InlineData("asked for after the rebuild", "one header repeated at one distance")]
    public void A_memo_past_its_bound_says_where_it_stops_being_whole_and_the_newest_definition_still_wins(string when, string repeats)
    {
        // MaxIndexEntries of 8: the index holds four numbers, while the scan takes the headers of 1 and 2, twenty older ones
        // of 3, the newest of 3, a keyword and a trailer keyword. The memo holds the first eight places, and stops being whole
        // where the ninth starts; the rebuild, run before the memo was asked for or after, scans the file, serves the newest
        // definition of 3, and reports no guard, as none cut what it serves. A caller that goes without the memo's word past
        // there reports the guard, once.
        var file = Repeated(repeats == "one header repeated at one distance", out var definitions);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 } };

        using var document = PdfDocument.Open(file, options);
        var made = when == "made before the rebuild" ? document.Reader.GetScanMemo() : null;
        document.GetObject(new PdfObjectId(4));
        var memo = document.Reader.GetScanMemo();

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(3)).AsText().Should().Be("newest");
        document.Diagnostics.Should().NotContain(entry => entry.Code.StartsWith("limit.", StringComparison.Ordinal));
        (made ?? memo).Should().BeSameAs(memo);
        memo.IsWhole.Should().BeFalse();
        memo.Count.Should().Be(8);
        memo.FullAt.Should().Be(definitions[6]);
        memo.WholeUntil.Should().BeGreaterThan(definitions[5]).And.BeLessThanOrEqualTo(definitions[6]);
        memo.Trailers.Should().BeNull();
        memo.TryGetHeaderAt(definitions[5], out _).Should().BeTrue();
        memo.TryGetHeaderAt(definitions[6], out _).Should().BeFalse();
        Headers(memo, 0, long.MaxValue).Should().HaveCount(8);

        document.Reader.ReachScanMemoBound();
        document.Reader.ReachScanMemoBound();

        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries).Which;
        report.Position.Should().Be(definitions[6]);
        report.Message.Should().Be(
            "The scan for objects recorded 8 object headers and keywords; those past them were not recorded. Raise PdfReaderLimits.MaxIndexEntries to read past it.");
    }

    [Fact]
    public void An_updated_file_whose_places_outnumber_the_bound_but_not_its_numbers_rebuilds_with_no_guard_reached()
    {
        // Three objects, the third defined again by two updates, each with its table and trailer: eleven places for three
        // numbers. Under a bound of 5, opened to throw on a guard, the rebuild the broken startxref sets off throws nothing,
        // and serves the newest definition.
        var file = new StringBuilder("%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
            .Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n3 0 obj\n(first)\nendobj\n");

        foreach (var update in new[] { "second", "third" })
        {
            file.Append("xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Root 1 0 R /Size 4 >>\nstartxref\n999999\n%%EOF\n")
                .Append(CultureInfo.InvariantCulture, $"3 0 obj\n({update})\nendobj\n");
        }

        file.Append("xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Root 1 0 R /Size 4 >>\nstartxref\n999999\n%%EOF\n");
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxIndexEntries = 5 } };

        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(file.ToString()), options);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(3)).AsText().Should().Be("third");
        document.Diagnostics.Should().NotContain(entry => entry.Code.StartsWith("limit.", StringComparison.Ordinal));
        document.Reader.GetScanMemo().IsWhole.Should().BeFalse();
    }

    [Fact]
    public void The_memo_reports_its_bound_apart_from_the_index_which_reports_its_own_where_both_fill()
    {
        // Objects 1 to 10, a table for 1 to 3 whose row for 3 lies outside the file: under a bound of 5, the memo, asked for
        // first, and the index the read of 3 rebuilds both fill at the header of 6. The index reports its bound as the
        // rebuild reaches it, and the memo its own once a caller goes without its word: neither hides the other.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages), [3] = 999_999 };
        var sixth = 0L;

        for (var number = 3; number <= 10; number++)
        {
            var at = file.Object(number, string.Create(CultureInfo.InvariantCulture, $"(object {number})"));
            sixth = number == 6 ? at : sixth;
        }

        file.Table(4, rows, "/Size 4 /Root 1 0 R");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 5 } };

        using var document = PdfDocument.Open(file.Bytes, options);
        var memo = document.Reader.GetScanMemo();
        document.GetObject(new PdfObjectId(3)).AsText().Should().Be("object 3");
        document.Reader.ReachScanMemoBound();

        memo.FullAt.Should().Be(sixth);
        document.WasRepaired.Should().BeTrue();
        var reports = document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries).ToList();
        reports.Select(entry => entry.Position).Should().Equal(sixth, sixth);
        reports[0].Message.Should().StartWith("The index reached 5 entries;");
        reports[1].Message.Should().StartWith("The scan for objects recorded 5 object headers and keywords;");
    }

    [Fact]
    public void A_memo_past_its_bound_throws_only_where_a_caller_goes_without_it_for_a_document_opened_to_throw_on_a_guard()
    {
        // Asked for, the memo throws nothing as it is made; the caller that goes without its word throws each time. Asked for
        // after a rebuild, which serves the newest definition, it throws nothing either.
        var file = Repeated(alike: false, out var definitions);
        var options = new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 } };

        using (var asked = PdfDocument.Open(file, options))
        {
            asked.Reader.GetScanMemo().FullAt.Should().Be(definitions[6]);

            for (var call = 0; call < 2; call++)
            {
                var thrown = FluentActions.Invoking(asked.Reader.ReachScanMemoBound).Should().Throw<PdfLimitExceededException>().Which;
                thrown.LimitName.Should().Be(nameof(PdfReaderLimits.MaxIndexEntries));
                thrown.Position.Should().Be(definitions[6]);
            }
        }

        using var rebuilt = PdfDocument.Open(file, options);
        rebuilt.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfNull>();
        rebuilt.GetObject(new PdfObjectId(3)).AsText().Should().Be("newest");
        rebuilt.Reader.GetScanMemo().IsWhole.Should().BeFalse();
    }

    [Theory]
    [InlineData("past where the memo is whole", false)]
    [InlineData("past where the memo is whole", true)]
    [InlineData("before where the memo is whole, the bytes searched reaching past it", false)]
    [InlineData("before where the memo is whole, the bytes searched reaching past it", true)]
    [InlineData("past where a source that gives fewer bytes than asked ended the scan", false)]
    [InlineData("past where a source that gives fewer bytes than asked ended the scan", true)]
    public void The_search_near_a_row_reports_the_memo_s_bound_only_where_it_finds_a_header_past_where_the_memo_is_whole(
        string header, bool throwOnLimit)
    {
        // MaxIndexEntries of 8: the scan takes the headers of 1 and 2, then ten definitions of 3, and object 4's header after
        // them or before; the memo holds no place past the eighth the scan takes. Object 4's header lies 5 bytes before its
        // row. Past there, the search near the row goes without the memo's word for it, reports the memo's bound, and serves
        // the header as the bytes give it, a choice it cannot vouch for; opened to throw on a guard, the read throws there.
        // Before there, the memo answers for it, though the bytes searched reach past there: the bound changed nothing read,
        // and is not reported. Past 6,000 bytes of comment read through a source that gives at most 4,096 bytes a read, the
        // memo is whole but holds nothing past where the scan's short window ended it (#52): the header is served as the
        // bytes give it all the same, and no guard is reported or thrown, none having cut the memo.
        var shortReads = header == "past where a source that gives fewer bytes than asked ended the scan";
        var past = header != "before where the memo is whole, the bytes searched reaching past it";
        var boundReached = past && !shortReads;
        var four = "4 0 obj\n(four)\nendobj\n";
        var threes = string.Concat(Enumerable.Range(0, 10).Select(index => string.Create(CultureInfo.InvariantCulture, $"3 0 obj\n({index})\nendobj\n")));
        var body = shortReads ? "%" + new string('-', 5998) + "\n" + four : past ? threes + four : four + threes;
        var (file, row) = NearbyFiles.FourNear(body, "4 0 obj", 5);
        var options = new PdfReaderOptions { ThrowOnLimit = throwOnLimit, Limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 } };
        PdfFileSource source = shortReads ? new CappedReadSource(file, 4096) : PdfFileSource.FromMemory(file);

        using var document = PdfDocument.Open(source, options, ownsSource: true);

        if (boundReached && throwOnLimit)
        {
            FluentActions.Invoking(() => document.GetObject(new PdfObjectId(4))).Should().Throw<PdfLimitExceededException>()
                .Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxIndexEntries));
            return;
        }

        document.GetObject(new PdfObjectId(4)).AsText().Should().Be("four");
        var memo = document.Reader.GetScanMemo();
        var adjusted = (PdfDiagnosticCodes.XRefOffsetAdjusted, row - 5);
        IEnumerable<(string, long)> reported = boundReached ? [(PdfDiagnosticCodes.LimitIndexEntries, memo.FullAt), adjusted] : [adjusted];

        document.WasRepaired.Should().BeFalse();
        memo.IsWhole.Should().Be(shortReads);
        memo.WholeUntil.Should().BeLessThan(row + PdfFileReader.NearbySearchRadius, "the bytes searched reach past where the memo is whole");
        (row - 5 >= memo.WholeUntil).Should().Be(past);
        document.Diagnostics.Select(entry => (entry.Code, entry.Position)).Should().Equal(reported);
        new PdfValidator().Validate(document).Findings.Where(finding => finding.Location.Object == new PdfObjectId(4))
            .Select(finding => (finding.RuleId, finding.Severity)).Should().Equal(past
                ? (PdfValidationRuleIds.XRefEntryUnconfirmed, PdfValidationSeverity.Error)
                : (PdfValidationRuleIds.XRefEntryShifted, PdfValidationSeverity.Warning));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4096)]
    public void The_memo_holds_no_place_past_where_a_source_that_gives_fewer_bytes_than_asked_ended_the_scan(int cap)
    {
        // Past the catalog and the page tree, 6,000 bytes of comment come before object 3, read through a source that gives
        // every byte asked for, or at most 4,096 bytes a read, as a source over a store of blocks may. The scan takes a window
        // shorter than asked for the end of the data there, and its memo, whole, holds nothing past it, nor past where the
        // page tree's value ends, as far back as a header whose obj lies past the end may start: no place there is vouched
        // for. Read whole, the memo holds every place to the file's end (#52).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        file.Raw("%" + new string('-', 5998) + "\n");
        rows[3] = file.Object(3, "(three)");
        file.Table(4, rows, "/Size 4 /Root 1 0 R");
        PdfFileSource source = cap > 0 ? new CappedReadSource(file.Bytes, cap) : PdfFileSource.FromMemory(file.Bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: true);

        var memo = document.Reader.GetScanMemo();
        var pagesEnd = rows[2] + "2 0 obj\n".Length + Pages.Length;

        (memo.IsWhole, memo.WholeUntil, memo.TryGetHeaderAt(rows[3], out _)).Should().Be(cap > 0 ? (true, pagesEnd, false) : (true, long.MaxValue, true));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void A_header_the_end_of_a_short_read_cuts_lies_past_where_the_memo_is_whole_and_is_served_as_the_bytes_give_it(int before)
    {
        // Read through a source that gives at most 4,096 bytes a read, object 4's header starts the given count of bytes
        // before the end of the scan's first window, its obj keyword ending past it, and its row lies 3 bytes past it. The
        // scan takes the window for the end of the data and cannot take the header, which may start as far back as where
        // object 3's value ends: the memo is whole only before there. The search near the row serves the header as the bytes
        // give it, a choice it cannot vouch for; object 5, past the end, is read at its row, and nothing is rebuilt (#52).
        const int Cap = 4096;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        rows[3] = file.Padding(3, (int)(Cap - before - file.Position - "3 0 obj\n()\nendobj\n".Length));
        var header = file.Object(4, "(four)");
        rows[4] = header + 3;
        rows[5] = file.Object(5, "(five)");
        file.Table(6, rows, "/Size 6 /Root 1 0 R");
        using var document = PdfDocument.Open(new CappedReadSource(file.Bytes, Cap), options: null, ownsSource: true);

        var four = document.GetObject(new PdfObjectId(4)).AsText();
        var five = document.GetObject(new PdfObjectId(5)).AsText();
        var memo = document.Reader.GetScanMemo();

        header.Should().Be(Cap - before);
        (four, five, document.WasRepaired).Should().Be(("four", "five", false));
        document.Diagnostics.Select(entry => (entry.Code, entry.Position)).Should().Equal((PdfDiagnosticCodes.XRefOffsetAdjusted, header));
        memo.IsWhole.Should().BeTrue();
        memo.WholeUntil.Should().BeInRange(rows[3] + 1, header);
        memo.TryGetHeaderAt(rows[3], out _).Should().BeTrue();
        new PdfValidator().Validate(document).Findings.Where(finding => finding.Location.Object == new PdfObjectId(4))
            .Select(finding => (finding.RuleId, finding.Severity)).Should().Equal((PdfValidationRuleIds.XRefEntryUnconfirmed, PdfValidationSeverity.Error));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void An_xref_keyword_the_end_of_a_short_read_cuts_lies_past_where_the_memo_is_whole_and_its_section_is_taken_unconfirmed(int before)
    {
        // Read through a source that gives at most 4,096 bytes a read, the first table's xref keyword starts the given count of
        // bytes before the end of the scan's first window, and the update's /Prev names the place 2 bytes into it. The scan
        // cannot take the keyword the window cuts, and its memo is whole only before where object 3's value ends: the search
        // near the offset tries the keyword without the memo's word, and takes the section, which the reader cannot vouch for
        // (#52).
        const int Cap = 4096;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        var padding = (int)(Cap - before - file.Position - "3 0 obj\n()\nendobj\n".Length);
        rows[3] = file.Padding(3, padding);
        var keyword = file.Table(4, rows, "/Size 4 /Root 1 0 R");
        var named = keyword + 2;
        var four = file.Object(4, "(four)");
        var update = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \n4 1\n{four:D10} 00000 n \ntrailer\n<< /Size 5 /Root 1 0 R /Prev {named} >>\nstartxref\n{update}\n%%EOF\n"));
        using var document = PdfDocument.Open(new CappedReadSource(file.Bytes, Cap), options: null, ownsSource: true);

        var section = document.Reader.Structure.Sections[1];

        keyword.Should().Be(Cap - before);
        (section.State, section.Offset).Should().Be((XRefSectionState.Relocated, keyword));
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture, $"The cross-reference section /Prev names at offset {named} was found 2 bytes before it."));
        document.GetObject(new PdfObjectId(3)).AsText().Should().Be(new string('x', padding));
        document.WasRepaired.Should().BeFalse();
        new PdfValidator().Validate(document).Findings.Where(finding => finding.RuleId.StartsWith("xref.section", StringComparison.Ordinal))
            .Select(finding => (finding.RuleId, finding.Severity, finding.Location.Position)).Should().Equal(
                (PdfValidationRuleIds.XRefSectionUnconfirmed, PdfValidationSeverity.Error, keyword));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_header_whose_walk_back_a_short_read_ends_is_not_taken_and_the_scan_goes_on_past_it(bool rebuildFirst)
    {
        // Read through a source that gives at most 1 MiB a read, each piece of the scan whole: past 800,000 bytes that hold no
        // header, object 5's number is followed by 300,000 spaces before its generation and its obj keyword, which lies in the
        // scan's second piece. Reading the header back from there asks for more than the source gives: the window comes back
        // short, the end of what the source holds there (#52), and the scan takes no header there but goes on past it, a memo
        // whole only before where the page tree's value ends, where the header it could not read may start. Object 6's row
        // lies 3 bytes past its header, and object 7's outside the file: the search near the row serves 6 as the bytes give
        // it, and the rebuild, run before the memo was made or after it, indexes 6 and not 5.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, Catalog), [2] = file.Object(2, Pages) };
        file.Raw(new string('x', 800_000) + "\n");
        var five = file.Position;
        file.Raw("5" + new string(' ', 300_000) + "0 obj\n(five)\nendobj\n");
        var six = file.Object(6, "(six)");
        rows[6] = six + 3;
        rows[7] = 9_999_999;
        file.Table(8, rows, "/Size 8 /Root 1 0 R");
        using var document = PdfDocument.Open(new CappedReadSource(file.Bytes, 1 << 20), options: null, ownsSource: true);

        if (rebuildFirst)
        {
            document.GetObject(new PdfObjectId(7));
        }

        var served = document.GetObject(new PdfObjectId(6)).AsText();
        document.GetObject(new PdfObjectId(7));
        var memo = document.Reader.GetScanMemo();

        served.Should().Be("six");
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().Contain(entry => entry.Code == PdfDiagnosticCodes.XRefRebuilt);
        document.Diagnostics.Any(entry => entry.Code == PdfDiagnosticCodes.XRefOffsetAdjusted && entry.Position == six).Should().Be(!rebuildFirst);
        document.GetObject(new PdfObjectId(5)).Should().BeSameAs(PdfNull.Instance);
        document.Reader.Index.TryGet(5, out _).Should().BeFalse();
        document.Reader.Index.TryGet(6, out var entry).Should().BeTrue();
        document.Reader.PositionOf(entry).Should().Be(six);
        memo.IsWhole.Should().BeTrue();
        memo.WholeUntil.Should().Be(rows[2] + "2 0 obj\n".Length + Pages.Length);
        memo.TryGetHeaderAt(five, out _).Should().BeFalse();
        memo.TryGetHeaderAt(six, out _).Should().BeTrue();
    }

    [Fact]
    public void A_whole_memo_or_none_has_no_bound_to_report()
    {
        // A whole memo, and a document that has made none, report nothing, opened to throw on a guard or not.
        var file = Updated(out _);
        var options = new PdfReaderOptions { ThrowOnLimit = true };

        using var unmade = PdfDocument.Open(file, options);
        unmade.Reader.ReachScanMemoBound();
        using var whole = PdfDocument.Open(file, options);
        whole.Reader.GetScanMemo().IsWhole.Should().BeTrue();
        whole.Reader.ReachScanMemoBound();

        unmade.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries);
        whole.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries);
    }

    [Fact]
    public void A_rebuild_the_source_fails_past_the_memo_s_replay_leaves_the_memo_for_the_rebuild_run_again_to_take()
    {
        // The memo is asked for first; the source fails the rebuild that replays it as it merges the trailer: what the rebuild
        // reported is withdrawn, and the read that runs the rebuild again takes the same memo, with the index, the objects and
        // the diagnostics of a rebuild the source never failed, which scanned the file.
        var file = Updated(out _);
        var trailer = Encoding.Latin1.GetString(file).LastIndexOf("trailer", StringComparison.Ordinal) + "trailer".Length;
        using var source = new FailingSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var memo = document.Reader.GetScanMemo();
        source.FailAt = trailer;

        FluentActions.Invoking(() => document.GetObject(new PdfObjectId(4))).Should().Throw<IOException>();
        document.GetObject(new PdfObjectId(4));

        using var sound = PdfDocument.Open(file);
        sound.GetObject(new PdfObjectId(4));

        document.Reader.GetScanMemo().Should().BeSameAs(memo);
        memo.IsWhole.Should().BeTrue();
        document.Diagnostics.Should().Equal(sound.Diagnostics);
        Entries(document).Should().Equal(Entries(sound));
        document.GetObject(new PdfObjectId(3)).AsText().Should().Be("three, updated");
    }

    [Fact]
    public void Places_of_one_header_at_one_distance_are_read_back_one_by_one()
    {
        var builder = new ScanMemo.Builder(100);

        foreach (var at in new long[] { 100, 108, 116, 124 })
        {
            builder.TryAddHeader(at, 7, 0, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        }

        builder.TryAddHeader(200, 8, 65_535, ScannedPlaceKind.Raw, floor: 0).Should().BeTrue();
        var memo = builder.Complete(new ScannedTrailers());

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
        var builder = new ScanMemo.Builder(100);
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
            builder.TryAddHeader(place.Position, place.Number, place.Generation, place.Kind, floor: 0).Should().BeTrue();
        }

        var memo = builder.Complete(new ScannedTrailers());
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
        var builder = new ScanMemo.Builder(100);
        var run = new List<ScannedHeader>();

        for (var at = 100L; at <= 148; at += 8)
        {
            builder.TryAddHeader(at, 7, 0, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
            run.Add(Header(at, 7));
        }

        builder.TryAddHeader(position, 9, 0, ScannedPlaceKind.Raw, floor: 0).Should().BeTrue();
        var memo = builder.Complete(new ScannedTrailers());

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
        var builder = new ScanMemo.Builder(100);
        builder.TryAddXRefKeyword(10, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        builder.TryAddXRefKeyword(20, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        builder.TryAddXRefKeyword(30, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        builder.TryAddHeader(40, 5, 0, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        builder.TryAddXRefKeyword(50, ScannedPlaceKind.Raw, floor: 0).Should().BeTrue();

        var memo = builder.Complete(new ScannedTrailers());

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
        var builder = new ScanMemo.Builder(3);
        builder.TryAddHeader(10, 1, 0, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        builder.TryAddTrailer(20, floor: 15).Should().BeTrue();
        builder.TryAddXRefKeyword(30, ScannedPlaceKind.Clean, floor: 25).Should().BeTrue();
        builder.IsWhole.Should().BeTrue();

        builder.TryAddHeader(40, 2, 0, ScannedPlaceKind.Clean, floor: 35).Should().BeFalse();
        builder.IsWhole.Should().BeFalse();
        builder.TryAddHeader(50, 3, 0, ScannedPlaceKind.Clean, floor: 45).Should().BeFalse();
        builder.TryAddTrailer(60, floor: 55).Should().BeFalse();
        var memo = builder.Complete(new ScannedTrailers());

        memo.Count.Should().Be(3);
        memo.IsWhole.Should().BeFalse();
        memo.FullAt.Should().Be(40);
        memo.WholeUntil.Should().Be(35);
        memo.Trailers.Should().BeNull();
        Headers(memo, 0, long.MaxValue).Should().Equal(Header(10, 1));
        memo.TryGetXRefKeywordAt(30, out _).Should().BeTrue();

        var past = new ScanMemo.Builder(1);
        past.TryAddHeader(10, 1, 0, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        past.TryAddHeader(40, 2, 0, ScannedPlaceKind.Clean, floor: 60).Should().BeFalse();
        past.Complete(new ScannedTrailers()).WholeUntil.Should().Be(40);
    }

    [Theory]
    [InlineData(30L, 25L, false, 25L)]
    [InlineData(30L, 35L, false, 30L)]
    [InlineData(30L, 25L, true, 25L)]
    public void A_short_read_leaves_the_memo_whole_only_before_the_end_of_the_data_or_where_a_header_it_cuts_may_start(
        long end, long floor, bool fillsAfter, long wholeUntil)
    {
        // A window shorter than asked ends the data the scan read; a header whose obj lies past there may start as far back as
        // where the bytes the scan's last object left to read anew start. A memo that fills afterward, the scan going on past
        // a header it could not read back, is whole no further.
        var builder = new ScanMemo.Builder(1);
        builder.TryAddHeader(10, 1, 0, ScannedPlaceKind.Clean, floor: 0).Should().BeTrue();
        builder.EndAt(end, floor);

        if (fillsAfter)
        {
            builder.TryAddHeader(40, 2, 0, ScannedPlaceKind.Clean, floor: 35).Should().BeFalse();
        }

        var memo = builder.Complete(new ScannedTrailers());

        (memo.IsWhole, memo.WholeUntil).Should().Be((!fillsAfter, wholeUntil));
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
