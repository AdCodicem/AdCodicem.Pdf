using System.Diagnostics;
using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// What a hostile input costs in time, measured as how it grows with the input rather than against a budget: run
/// alone, in <see cref="TimingCollection"/>.
/// </summary>
[Collection(TimingCollection.Name)]
public class ReaderTimingTests
{
    [Fact]
    public void The_members_of_an_object_stream_one_byte_apart_are_read_in_linear_time()
    {
        // Each member is read to the next one's start, not to the end of the data: four times the members, over four times
        // the brackets, take about four times as long, where reading each to the end would take sixteen (#200). The
        // measure is the ratio, the best of five runs of each size.
        const int Small = 20_000;
        const int Large = 4 * Small;
        var small = HostileInputTests.MembersOneByteApart(Small);
        var large = HostileInputTests.MembersOneByteApart(Large);

        ReadEvery(small, Small);
        var smallTime = TimeSpan.MaxValue;
        var largeTime = TimeSpan.MaxValue;

        for (var run = 0; run < 5; run++)
        {
            smallTime = Min(smallTime, Time(() => ReadEvery(small, Small)));
            largeTime = Min(largeTime, Time(() => ReadEvery(large, Large)));
        }

        largeTime.Should().BeLessThan(
            smallTime * 8,
            "four times the members must cost about four times the time ({0} for {1:N0}, {2} for {3:N0})",
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
            var arrays = 0;

            for (var i = 0; i < count; i++)
            {
                arrays += document.GetObject(new PdfObjectId(1000 + i)) is PdfArray ? 1 : 0;
            }

            arrays.Should().Be(count);
        }
    }

    [Fact]
    public void An_object_stream_whose_every_index_is_wrong_is_read_in_linear_time()
    {
        // One stream whose every entry in the index names the wrong place in it: each object is found by its
        // number, through a lookup built once, not by a search of the stream's header for each. Four times as many
        // objects then take about four times as long, where a search would take sixteen. The measure is that
        // ratio, the best of five runs of each size, so that it holds on a slow machine and under coverage
        // instrumentation alike, where a fixed time budget did not.
        const int Small = 50_000;
        const int Large = 4 * Small;
        var small = HostileInputTests.ObjectStreams(streams: 1, decodedLength: 0, objectsPerStream: Small, everyIndexWrong: true);
        var large = HostileInputTests.ObjectStreams(streams: 1, decodedLength: 0, objectsPerStream: Large, everyIndexWrong: true);

        ReadEvery(small, Small);
        var smallTime = TimeSpan.MaxValue;
        var largeTime = TimeSpan.MaxValue;

        for (var run = 0; run < 5; run++)
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
                dictionaries += document.GetObject(new PdfObjectId(HostileInputTests.PackedNumber(0, i, count))) is PdfDictionary ? 1 : 0;
            }

            dictionaries.Should().Be(count);
            document.Diagnostics.Contains(PdfDiagnosticCodes.ObjectStreamMemberMoved).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("a header broken before its last member")]
    [InlineData("members written as streams")]
    [InlineData("an index wrong for every member")]
    public void The_validator_judges_an_object_stream_s_members_in_linear_time(string shape)
    {
        // One object stream of many members, each placed by the index: the probe finds each in the header through a lookup
        // built once, and each member written as a stream by a search of those it found, not by scanning the header for
        // each — four times the members take about four times as long, where a scan would take sixteen. The probe alone is
        // timed, the documents opened and settled before, and each probed once first; each run starts from a collected heap,
        // so that the findings of a run before it, one for each member, are not collected inside the next.
        const int Small = 50_000;
        const int Large = 4 * Small;
        using var small = PdfDocument.Open(Packed(Small, shape));
        using var large = PdfDocument.Open(Packed(Large, shape));
        small.Reader.SettleIndex();
        large.Reader.SettleIndex();

        Probe(small);
        Probe(large);
        var smallTime = TimeSpan.MaxValue;
        var largeTime = TimeSpan.MaxValue;

        for (var run = 0; run < 5; run++)
        {
            smallTime = Min(smallTime, Time(() => Probe(small)));
            largeTime = Min(largeTime, Time(() => Probe(large)));
        }

        largeTime.Should().BeLessThan(
            smallTime * 8,
            "four times the members must cost about four times the time ({0} for {1:N0}, {2} for {3:N0})",
            smallTime,
            Small,
            largeTime,
            Large);

        static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

        static TimeSpan Time(Action action)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var stopwatch = Stopwatch.StartNew();
            action();
            return stopwatch.Elapsed;
        }

        static void Probe(PdfDocument document)
        {
            var probe = CrossReferenceProbe.Run(document);
            (probe.Broken.Total + probe.Shifted.Total + probe.BrokenObjectStreams.Count).Should().BePositive();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_index_of_numbers_chosen_to_share_a_bucket_opens_as_fast_as_one_numbered_in_order(bool aimedAtTheMap)
    {
        // 34,167 numbers 62,851 apart, as many as an int holds, each in a subsection of its own: before #155 every one fell
        // in a single bucket of the index's map, and indexing each walked those before it, 2 s against 40 ms. The index now
        // finds a number in a slot while numbers run in order, and past them in a map hashed through the process's seed:
        // the same rows numbered either way open in about the same time. Numbers as many apart as that map has buckets once
        // it holds them all aim at the map itself. The measure is the ratio, the best opening of each, run in turn.
        const int Count = 34_167;
        var apart = aimedAtTheMap ? CollidingNumbers.GrownSize(Count) : CollidingNumbers.TableSize;
        var colliding = CollidingNumbers.Table(CollidingNumbers.Numbers(Count, apart, colliding: true));
        var ordered = CollidingNumbers.Table(CollidingNumbers.Numbers(Count, apart, colliding: false));

        var (orderedTime, collidingTime) = BestOfEach(() => Open(ordered), () => Open(colliding));

        collidingTime.Should().BeLessThan(
            orderedTime * 4,
            "rows numbered to collide must cost about what the same rows numbered in order do ({0} against {1})",
            collidingTime,
            orderedTime);

        static void Open(byte[] file)
        {
            using var document = PdfDocument.Open(file);
            document.ObjectCount.Should().Be(Count + 3);
        }
    }

    [Fact]
    public void Object_streams_that_share_chains_eight_times_as_long_have_their_dependencies_walked_in_about_the_same_time()
    {
        // Two thousand object streams share 64 filters and 64 parameters, each a chain of references, the parameters' values
        // chains too, which decoding reads for each stream. The walk follows a chain once a validation, keeping the run of
        // references it loads by the object it starts at, and a value once a stream: chains of 31 references cost about what
        // chains of 4 do, where following each chain again for each stream cost about eight times as much, 4.8 s for 8,000
        // streams (#201, #277). The measure is the ratio, the best walk of each, run in turn.
        const int Streams = 2000;
        using var shortChains = PdfDocument.Open(HostileInputTests.SharedChains(Streams, links: 4));
        using var longChains = PdfDocument.Open(HostileInputTests.SharedChains(Streams, links: 31));

        var (shortTime, longTime) = BestOfEach(() => ObjectStreamDependencies.Run(shortChains), () => ObjectStreamDependencies.Run(longChains));

        longTime.Should().BeLessThan(
            shortTime * 3,
            "chains eight times as long must be walked in about the same time ({0} against {1})",
            longTime,
            shortTime);
    }

    [Theory]
    [InlineData("chain")]
    [InlineData("ring")]
    [InlineData("rho")]
    [InlineData("star")]
    public void Four_times_as_many_object_streams_that_need_one_another_are_judged_circular_in_about_four_times_the_time(string shape)
    {
        // Object streams need one another's objects through their /DecodeParms, in HostileInputTests' shapes. The walk
        // looked for a way back to each stream with a search from each of its needs, which cost the square of their number:
        // 17 s for a chain of 20,000 streams, 44 s for a ring (#168). It works out the streams that lead to one another once:
        // four times the streams take about four times as long, where the search took sixteen. The measure is the ratio, the
        // best of five walks of each, each walked once first; each walk starts from a collected heap, so that what a walk
        // before it left, the needs and findings of each stream, is not collected inside the next.
        const int Small = 2000;
        const int Large = 4 * Small;
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxFilterChainLength = Large } };
        using var small = PdfDocument.Open(HostileInputTests.ChainedObjectStreams(shape, Small), options);
        using var large = PdfDocument.Open(HostileInputTests.ChainedObjectStreams(shape, Large), options);

        ObjectStreamDependencies.Run(small).CircularStreams.Should().HaveCount(HostileInputTests.CircularAmongChainedObjectStreams(shape, Small));
        ObjectStreamDependencies.Run(large).CircularStreams.Should().HaveCount(HostileInputTests.CircularAmongChainedObjectStreams(shape, Large));
        var smallTime = TimeSpan.MaxValue;
        var largeTime = TimeSpan.MaxValue;

        for (var run = 0; run < 5; run++)
        {
            smallTime = Min(smallTime, Time(() => ObjectStreamDependencies.Run(small)));
            largeTime = Min(largeTime, Time(() => ObjectStreamDependencies.Run(large)));
        }

        largeTime.Should().BeLessThan(
            smallTime * 8,
            "four times the streams must cost about four times the time ({0} for {1:N0}, {2} for {3:N0})",
            smallTime,
            Small,
            largeTime,
            Large);

        static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

        static TimeSpan Time(Action action)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var stopwatch = Stopwatch.StartNew();
            action();
            return stopwatch.Elapsed;
        }
    }

    [Fact]
    public void An_index_of_numbers_chosen_to_share_a_bucket_validates_as_fast_as_one_numbered_in_order()
    {
        // The same 34,167 numbers, validated once open: the probe of the index's entries and the search for pages the tree
        // leaves out go through every number the index holds, and before #155 looked each up in the map's one bucket, 4.2 s
        // against 2.3 ms. The measure is the ratio, the best validation of each, run in turn.
        const int Count = 34_167;
        using var colliding = PdfDocument.Open(CollidingNumbers.Table(CollidingNumbers.Numbers(Count, CollidingNumbers.TableSize, colliding: true)));
        using var ordered = PdfDocument.Open(CollidingNumbers.Table(CollidingNumbers.Numbers(Count, CollidingNumbers.TableSize, colliding: false)));
        var validator = new PdfValidator();

        var (orderedTime, collidingTime) = BestOfEach(() => validator.Validate(ordered), () => validator.Validate(colliding));

        collidingTime.Should().BeLessThan(
            orderedTime * 4,
            "rows numbered to collide must validate in about the time the same rows numbered in order do ({0} against {1})",
            collidingTime,
            orderedTime);
    }

    [Fact]
    public void A_rebuild_of_numbers_chosen_to_share_a_bucket_takes_the_time_of_one_numbered_in_order()
    {
        // No table: the reader indexes the 36,350 objects it finds, numbered as many buckets apart as a map grown to hold
        // them has. Before #155 the rebuild put them all in one bucket, 8 s against 70 ms; it now takes about what the
        // same objects numbered in order do. The measure is the ratio, the best opening of each, run in turn.
        const int Count = 36_350;
        var size = CollidingNumbers.GrownSize(Count + 2);
        var colliding = CollidingNumbers.Unindexed(CollidingNumbers.Numbers(Count, size, colliding: true));
        var ordered = CollidingNumbers.Unindexed(CollidingNumbers.Numbers(Count, size, colliding: false));

        var (orderedTime, collidingTime) = BestOfEach(() => Open(ordered), () => Open(colliding));

        collidingTime.Should().BeLessThan(
            orderedTime * 4,
            "objects numbered to collide must cost about what the same objects numbered in order do ({0} against {1})",
            collidingTime,
            orderedTime);

        static void Open(byte[] file)
        {
            using var document = PdfDocument.Open(file);
            document.WasRepaired.Should().BeTrue();
            document.ObjectCount.Should().Be(Count + 2);
        }
    }

    [Fact]
    public void Objects_numbered_to_share_a_bucket_of_the_cache_resolve_as_fast_as_objects_numbered_in_order()
    {
        // 20,000 objects as many buckets apart as the reader's cache has once it holds its 8,192, and 200,000 references to
        // them in turn: each misses the cache, which was keyed by the number itself, and walked the cache's one bucket to
        // look it up, to add it, and to let the oldest go. Hashed through the process's seed, the same references cost
        // about what they do to objects numbered in order. The measure is the ratio, the best resolution of each, run in turn.
        const int Count = 20_000;
        const int References = 200_000;
        var size = CollidingNumbers.GrownSize(new PdfReaderOptions().ObjectCacheCapacity);
        var colliding = CollidingNumbers.Referenced(CollidingNumbers.Numbers(Count, size, colliding: true), References);
        var ordered = CollidingNumbers.Referenced(CollidingNumbers.Numbers(Count, size, colliding: false), References);

        var (orderedTime, collidingTime) = BestOfEach(() => ResolveAll(ordered), () => ResolveAll(colliding));

        collidingTime.Should().BeLessThan(
            orderedTime * 4,
            "objects numbered to collide must resolve in about the time the same objects numbered in order do ({0} against {1})",
            collidingTime,
            orderedTime);

        static void ResolveAll(byte[] file)
        {
            using var document = PdfDocument.Open(file);
            var array = document.GetObject(new PdfObjectId(3)).AsArray().Required();
            var integers = 0;

            for (var i = 0; i < array.Count; i++)
            {
                integers += array.Resolved(i) is PdfInteger ? 1 : 0;
            }

            integers.Should().Be(References);
        }
    }

    [Fact]
    public void A_validation_of_numbers_chosen_to_share_a_bucket_takes_the_time_of_one_numbered_in_order()
    {
        // The catalog reaches 100,000 references to 30,000 free numbers, as many buckets apart as a set grown to hold them
        // has: the walk of the reachable objects kept them in sets keyed by the number itself, all in one bucket. Hashed
        // through the process's seed, they validate in about the time the same references to numbers in order do. The
        // measure is the ratio, the best validation of each, run in turn.
        const int Count = 30_000;
        const int References = 100_000;
        var size = CollidingNumbers.GrownSize(Count);
        using var colliding = PdfDocument.Open(CollidingNumbers.ReachedFree(CollidingNumbers.Numbers(Count, size, colliding: true), References));
        using var ordered = PdfDocument.Open(CollidingNumbers.ReachedFree(CollidingNumbers.Numbers(Count, size, colliding: false), References));
        var validator = new PdfValidator();

        var (orderedTime, collidingTime) = BestOfEach(() => validator.Validate(ordered), () => validator.Validate(colliding));

        collidingTime.Should().BeLessThan(
            orderedTime * 4,
            "references numbered to collide must validate in about the time the same references numbered in order do ({0} against {1})",
            collidingTime,
            orderedTime);
    }

    [Fact]
    public void A_page_tree_node_of_four_times_the_kids_is_walked_in_about_four_times_the_time_once_the_cache_lets_go_of_it()
    {
        // A page tree node lists 20,000 pages, another 80,000, each opened with a cache of 64 objects, which lets go of the
        // node long before its last kid. The walk's frame keeps the node's array of kids, as the parse made it, rather than
        // the node (#201): four times the kids take about four times as long. Reading the node again for each kid once the
        // cache let go of it would read it again every 63 kids, sixteen times as long: 11 s for 50,000 kids, 57 s for
        // 100,000. The measure is the ratio, the best walk of each, run in turn.
        const int Small = 20_000;
        const int Large = 4 * Small;
        var options = new PdfReaderOptions { ObjectCacheCapacity = 64 };
        using var small = PdfDocument.Open(Node(Small), options);
        using var large = PdfDocument.Open(Node(Large), options);
        small.Reader.SettleIndex();
        large.Reader.SettleIndex();

        var (smallTime, largeTime) = BestOfEach(() => Walk(small, Small), () => Walk(large, Large));

        largeTime.Should().BeLessThan(
            smallTime * 8,
            "four times the kids must cost about four times the time ({0} for {1:N0}, {2} for {3:N0})",
            smallTime,
            Small,
            largeTime,
            Large);

        static void Walk(PdfDocument document, int kids) => PageTreeWalk.Run(document).PageCount.Should().Be(kids);

        static byte[] Node(int kids)
        {
            var builder = new TestPdfBuilder().WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>");
            var listed = new StringBuilder(kids * 8);

            for (var kid = 3; kid < kids + 3; kid++)
            {
                listed.Append(CultureInfo.InvariantCulture, $"{kid} 0 R ");
                builder.WithObject(kid, "<< /Type /Page /Parent 2 0 R >>");
            }

            return builder
                .WithObject(2, string.Create(CultureInfo.InvariantCulture, $"<< /Type /Pages /Count {kids} /MediaBox [0 0 612 792] /Resources << >> /Kids [{listed}] >>"))
                .BuildClassic(rootNumber: 1);
        }
    }

    /// <summary>
    /// Gets the shortest run of each action, the two run in turn: five times each at least, and on until neither has run
    /// faster for three turns — at most forty turns, or twenty seconds —, so that both are timed warm whatever ran before.
    /// </summary>
    private static (TimeSpan First, TimeSpan Second) BestOfEach(Action first, Action second)
    {
        var bestFirst = TimeSpan.MaxValue;
        var bestSecond = TimeSpan.MaxValue;
        var steady = 0;
        var spent = Stopwatch.StartNew();

        for (var turn = 0; turn < 5 || (steady < 3 && turn < 40 && spent.Elapsed < TimeSpan.FromSeconds(20)); turn++)
        {
            var timeFirst = Time(first);
            var timeSecond = Time(second);
            steady = timeFirst < bestFirst || timeSecond < bestSecond ? 0 : steady + 1;
            bestFirst = timeFirst < bestFirst ? timeFirst : bestFirst;
            bestSecond = timeSecond < bestSecond ? timeSecond : bestSecond;
        }

        return (bestFirst, bestSecond);

        static TimeSpan Time(Action action)
        {
            var stopwatch = Stopwatch.StartNew();
            action();
            return stopwatch.Elapsed;
        }
    }

    /// <summary>
    /// Writes a file whose object stream 3 holds <paramref name="members"/> objects, numbered from 4, each placed by a row
    /// of a cross-reference stream: <c>1</c> each, its header broken before the last by a pair whose offset is not past the
    /// one before; or each written as a stream; or <c>1</c> each, every row giving the next member's index.
    /// </summary>
    private static byte[] Packed(int members, string shape)
    {
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

        var member = shape == "members written as streams" ? "<<>>stream\n" : "1\n";
        var header = new StringBuilder();

        var broken = shape == "a header broken before its last member";

        for (var index = 0; index < (broken ? members - 1 : members); index++)
        {
            header.Append(CultureInfo.InvariantCulture, $"{4 + index} {index * member.Length} ");
        }

        if (broken)
        {
            header.Append("999 0 ");
        }

        var declared = members;

        var data = header + string.Concat(Enumerable.Repeat(member, members));
        WriteObject(3, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N {declared} /First {header.Length} /Length {data.Length} >>\nstream\n{data}\nendstream"));

        var xref = 4 + members;
        var xrefOffset = output.Position;
        offsets[xref] = xrefOffset;
        var rows = new byte[(xref + 1) * 9];

        for (var number = 0; number <= xref; number++)
        {
            var row = rows.AsSpan(number * 9, 9);

            if (offsets.TryGetValue(number, out var offset))
            {
                row[0] = 1;
                Put(row[1..5], offset);
            }
            else if (number >= 4)
            {
                var index = number - 4;
                row[0] = 2;
                Put(row[1..5], 3);
                Put(row[5..9], shape == "an index wrong for every member" ? (index + 1) % members : index);
            }
        }

        Write(string.Create(CultureInfo.InvariantCulture, $"{xref} 0 obj\n<< /Type /XRef /Size {xref + 1} /W [1 4 4] /Root 1 0 R /Length {rows.Length} >>\nstream\n"));
        output.Write(rows);
        Write(string.Create(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF\n"));
        return output.ToArray();

        static void Put(Span<byte> field, long value)
        {
            for (var i = field.Length - 1; i >= 0; i--)
            {
                field[i] = (byte)value;
                value >>= 8;
            }
        }
    }
}
