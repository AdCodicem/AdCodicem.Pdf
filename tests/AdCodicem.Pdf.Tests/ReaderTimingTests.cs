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
        // each — four times the members take about four times as long, where a scan would take sixteen. The probes alone are
        // timed, the documents opened and settled before, and each probed once first; each turn starts from a collected heap.
        // The probe is given the room a validation gives it by default, 1,000 findings of each kind: it writes those and
        // counts the rest, and still looks up and searches every member (#317). Given room for every finding, it kept one
        // for each member, 63 MB for the large stream whose every index is wrong and 75 MB for the one whose members are
        // written as streams, and the collections that keeping them took fell inside the large probe: four to seven, some
        // 80 to 120 ms of pause in a probe of 180 to 390 ms, where the small probe met one. Its ratio measured 5.0 to 7.3
        // without load and up to 10.5 under four busy loops, and went past 8 three times in CI. Given that room, the large
        // probe meets no collection, or a single young one; but one probe of the small stream takes 5 to 17 ms, and on a
        // busy machine one of its five turns would fall in a quiet moment that no turn of the large one met: under four busy
        // loops the ratio went past 8 in 3 runs of 71, at 8.7 to 9.4, each time beside other timing tests. Each turn times
        // four probes back to back, 14 to 46 ms of the small stream's, which a busy machine slows apart from the large
        // one's far less often than a single probe: the ratio measured 3.7 to 4.6 without load over 50 runs, and under four
        // busy loops 2.3 to 6.6 over 300 runs beside two other timing tests and 2.1 to 7.7 over 100 runs on its own — 7.7
        // once, nothing else past 6.4 —, never past 8. A heavier load still slows the large probes alone now and then: with
        // builds running beside four busy loops, the load reaching 8 to 14 on four cores, the ratio went past 8 once in 20
        // runs on its own, at 8.6, and once in 5 runs beside the other timing tests and the hostile input tests, at 9.0, its
        // small probes at their usual time and every large turn slowed; under that load three other timing tests of this
        // class failed too, up to 18.3 at a bound of 12. A scan of the header for each member, the lookup removed, measured
        // 13.1 to 21.9 where every index is wrong, and failed each of 45 runs, under four busy loops and without, and each
        // of 3 at a load of 9.5 to 16.
        const int Small = 50_000;
        const int Large = 4 * Small;
        const int Probes = 4;
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
            smallTime = Min(smallTime, Time(small));
            largeTime = Min(largeTime, Time(large));
        }

        largeTime.Should().BeLessThan(
            smallTime * 8,
            "four times the members must cost about four times the time ({0} for {1:N0}, {2} for {3:N0})",
            smallTime,
            Small,
            largeTime,
            Large);

        static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

        static TimeSpan Time(PdfDocument document)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var stopwatch = Stopwatch.StartNew();

            for (var probe = 0; probe < Probes; probe++)
            {
                Probe(document);
            }

            return stopwatch.Elapsed;
        }

        static void Probe(PdfDocument document)
        {
            var probe = CrossReferenceProbe.Run(document, PdfValidatorOptions.Default.FindingCapacity);
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
        // Two thousand object streams take /Filter 10 0 R and /DecodeParms 100 0 R, each the first of a chain of references,
        // the parameters' values chains too, which decoding reads for each stream. The walk follows a chain once a
        // validation, keeping the run of references it loads by the object it starts at: chains of 31 references cost what
        // chains of 4 do, 4.3 ms against 4.2 ms, where reading each object of a chain again for each stream cost about four
        // times as much, 107 ms against 28 ms (#201, #277). Each stream once read 64 filters and 64 parameters, which drowned
        // the chains: the walk without the memo then cost 1.9 times as much, within the bound. The measure is the ratio, the
        // best walk of each, run in turn.
        const int Streams = 2000;
        using var shortChains = PdfDocument.Open(HostileInputTests.SharedChains(Streams, links: 4, oneStep: true));
        using var longChains = PdfDocument.Open(HostileInputTests.SharedChains(Streams, links: 31, oneStep: true));

        var (shortTime, longTime) = BestOfEach(() => ObjectStreamDependencies.Run(shortChains), () => ObjectStreamDependencies.Run(longChains));

        longTime.Should().BeLessThan(
            shortTime * 2,
            "chains eight times as long must be walked in about the same time ({0} against {1})",
            longTime,
            shortTime);
    }

    [Fact]
    public void Sixteen_times_as_many_object_streams_whose_parameters_head_nests_of_streams_have_their_dependencies_walked_in_less_than_four_times_the_time()
    {
        // Object streams share 64 parameters and the values their predictor asks for, each a stream heading a nest of 63
        // streams by /Length, which the parse of each follows to the reader's deepest load, where it is cut: 16,128 streams,
        // more than the walk keeps of what it reads. The walk followed every nest again for each object stream, reading again
        // all it had let go, 50 ms a stream: sixteen times the streams took seventeen times as long, 0.78 s for 16 and 13.1 s
        // for 256. It takes at once the nest of streams a parse went through, from the depth it went through it, and what one
        // more object stream costs, about 0.25 ms, is small next to what parsing the nests once costs, about 45 ms: 256
        // streams take two to three times as long as 16 (#201). The measure is the ratio, the best walk of each, run in turn.
        const int Few = 16;
        const int Many = 256;
        using var few = PdfDocument.Open(HostileInputTests.LengthNests(Few, depth: 63));
        using var many = PdfDocument.Open(HostileInputTests.LengthNests(Many, depth: 63));

        var (fewTime, manyTime) = BestOfEach(() => ObjectStreamDependencies.Run(few), () => ObjectStreamDependencies.Run(many));

        manyTime.Should().BeLessThan(
            fewTime * 4,
            "sixteen times the object streams must be walked in less than four times the time ({0} for {1:N0}, {2} for {3:N0})",
            fewTime,
            Few,
            manyTime,
            Many);
    }

    [Theory]
    [InlineData("four times the streams", 8)]
    [InlineData("chains eight times as long", 3)]
    [InlineData("chains eight times as long, written in the file", 3)]
    [InlineData("chains eight times as long, written in the file twice, the index rebuilt", 3)]
    public void Object_streams_that_share_chains_of_objects_written_in_the_file_or_held_by_streams_that_decode_alike_have_their_dependencies_walked_in_time_their_number_alone_sets(string larger, int ratio)
    {
        // Object streams share 64 parameters, each the head of a chain of objects held by object streams that decode alike in
        // every read order, one each (#312), or written in the file. The walk follows a chain once a validation, keeping the
        // run of objects it loads by the one it starts at — once it knows their holders decode alike (#277) —: four times the
        // streams take about four times as long, and chains of 31 objects about what chains of 4 do, where following each
        // chain for each stream, an object at a time or through the reader, cost about eight times as much. A rebuild that
        // places the chains' objects at copies written after them leaves the walk that judges the chain's index taking the
        // runs it kept. The measure is the ratio, the best walk of each, run in turn.
        var twice = larger.Contains("twice", StringComparison.Ordinal);
        var (small, large) = larger switch
        {
            "four times the streams" => (HostileInputTests.MemberChains(1000, holdersShare: false, links: 31), HostileInputTests.MemberChains(4000, holdersShare: false, links: 31)),
            "chains eight times as long" => (HostileInputTests.MemberChains(2000, holdersShare: false, links: 4), HostileInputTests.MemberChains(2000, holdersShare: false, links: 31)),
            _ => (HostileInputTests.MemberChains(2000, holdersShare: false, links: 4, inFile: true, twice), HostileInputTests.MemberChains(2000, holdersShare: false, links: 31, inFile: true, twice)),
        };
        using var smallDocument = PdfDocument.Open(small);
        using var largeDocument = PdfDocument.Open(large);

        if (twice)
        {
            smallDocument.Reader.SettleIndex();
            largeDocument.Reader.SettleIndex();
            largeDocument.WasRepaired.Should().BeTrue();
        }

        var (smallTime, largeTime) = BestOfEach(() => ObjectStreamDependencies.Run(smallDocument), () => ObjectStreamDependencies.Run(largeDocument));

        largeTime.Should().BeLessThan(
            smallTime * ratio,
            "{0} must cost at most {1} times the time ({2} against {3})",
            larger,
            ratio,
            largeTime,
            smallTime);
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
        // four times the streams take about four times as long, where the search took more than sixteen times as long. The
        // measure is the ratio, the best of five walks of each, each walked once first; each walk starts from a collected
        // heap, so that what a walk before it left, the needs and findings of each stream, is not collected inside the next.
        // Walks this short, tens of milliseconds, are all slowed now and then on a busy machine: a bound of 8 failed once in
        // 15 runs under load, and 8 times in 300 under four busy loops, where alternating the walks until neither improves,
        // as BestOfEach does, failed about as often. A bound of 12 still tells a walk that grows with the streams from one
        // that grows with their square.
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
            smallTime * 12,
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
    public void A_page_tree_node_of_four_times_the_kids_is_walked_at_about_four_times_the_cost_once_the_cache_lets_go_of_it()
    {
        // A page tree node lists 20,000 pages, another 80,000, each opened with a cache of 64 objects, which lets go of the
        // node long before its last kid. The walk's frame keeps the node's array of kids, as the parse made it, rather than
        // the node (#201): four times the kids cost about four times as much. Reading the node again for each kid once the
        // cache let go of it would read it again every 63 kids: the large walk then took 18.5 times as long as the small
        // one, 21 s against 1.1 s, and allocated 19 times as much, 9.8 GB against 506 MB. What each walk allocates on the
        // test's thread holds the cost whatever else the process does: once each was walked first, the large walk allocates
        // 4.2 times what the small one does, 48.8 MB against 11.6 MB, on every run, under coverage or not. The time is held
        // as well, as the ratio of the best walk of each, run in turn, each from a collected heap: without a collection
        // before it, one large walk in three met one, which took it to 5 to 14 times the small walk's best. Where the
        // collector lets new objects take less than the large walk allocates before it collects — 16.8 MB, say —, every
        // large walk meets two collections or three, collected heap or not, and the ratio comes near 6: a bound of 12 holds
        // that, and still tells a walk that grows with the kids from one that grows with their square.
        const int Small = 20_000;
        const int Large = 4 * Small;
        var options = new PdfReaderOptions { ObjectCacheCapacity = 64 };
        using var small = PdfDocument.Open(Node(Small), options);
        using var large = PdfDocument.Open(Node(Large), options);
        small.Reader.SettleIndex();
        large.Reader.SettleIndex();

        Walk(small, Small);
        Walk(large, Large);
        var smallAllocated = Allocated(() => Walk(small, Small));
        var largeAllocated = Allocated(() => Walk(large, Large));

        largeAllocated.Should().BeLessThan(
            smallAllocated * 6,
            "four times the kids must allocate about four times as much ({0:N0} bytes for {1:N0}, {2:N0} for {3:N0})",
            smallAllocated,
            Small,
            largeAllocated,
            Large);

        var (smallTime, largeTime) = BestOfEach(() => Walk(small, Small), () => Walk(large, Large), collect: true);

        largeTime.Should().BeLessThan(
            smallTime * 12,
            "four times the kids must cost about four times the time ({0} for {1:N0}, {2} for {3:N0})",
            smallTime,
            Small,
            largeTime,
            Large);

        // One measure: a collection that falls in it adds some kilobytes, nothing beside these megabytes. The least of the
        // measures no collection paused, as Allocations.Least takes it, would walk a node read again for each kid thirty
        // times, a collection pausing every such walk, some ten minutes before it failed.
        static long Allocated(Action action)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            action();
            return GC.GetAllocatedBytesForCurrentThread() - allocated;
        }

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

    [Fact]
    public void Four_times_as_many_lost_streams_declaring_numbers_of_their_own_cost_about_four_times_the_time()
    {
        // A chain of cross-reference streams of /Type /XRef whose rows cannot be read, each handing its /Prev on and refusing
        // to the older rows the numbers it declares (#208): 700 one-row subsections of its own, about as many as
        // MaxTrailerLength, lowered to 8 KB, lets its dictionary hold. Past 4,096 ranges kept, every number is refused, and
        // nothing more is merged: four times the streams take about four times as long. Merging every range declared so far
        // again for each stream, with no bound, took 11.7 times as long, 1.3 s against 114 ms; but what holds the bound is
        // what each opening asserts, object 3 refused past 4,096 ranges, which such a merge fails at its first opening.
        // The time tells a chain whose cost grows with the streams, about four times, from one whose cost grows with their
        // square, about sixteen. Every opening of the larger file is slowed now and then on a busy machine while those of the
        // smaller one are not: the ratio measured 4.0 to 4.2 without load over 20 runs and 3.2 to 6.2 over 300 runs under
        // four busy loops, but reached 7.4 to 10.0 with builds running beside them, which a bound of 12 allows for, as it
        // does for the circular object streams. The measure is the ratio, the best opening of each, run in turn.
        const int Small = 256;
        const int Large = 4 * Small;
        var options = new PdfReaderOptions
        {
            Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = Large + 2, MaxTrailerLength = LostStreamFiles.TrailerLength },
        };
        var small = LostStreamFiles.OwnNumbers(Small, each: 700, rows: 16);
        var large = LostStreamFiles.OwnNumbers(Large, each: 700, rows: 16);

        var (smallTime, largeTime) = BestOfEach(() => Open(small, Small), () => Open(large, Large));

        largeTime.Should().BeLessThan(
            smallTime * 12,
            "four times the streams must cost about four times the time ({0} for {1:N0}, {2} for {3:N0})",
            smallTime,
            Small,
            largeTime,
            Large);

        void Open(byte[] file, int streams)
        {
            using var document = PdfDocument.Open(file, options);
            document.Reader.Structure.Sections.Should().HaveCount(streams + 2);
            document.Diagnostics.Should().OnlyContain(d => d.Code == PdfDiagnosticCodes.XRefSectionUnreadable);
            document.Reader.ChainIndex!.TryGet(3, out _).Should().BeFalse("past 4,096 ranges, every number is refused");
        }
    }

    [Fact]
    public void Older_rows_looked_for_among_4096_refused_ranges_are_read_in_about_the_time_they_are_among_one()
    {
        // A lost stream declares 4,096 one-row subsections, every other number, or one after the other, which meet in one
        // range, and the older table holds 262,144 rows (#208). Each row's number is looked for among the ranges by halves:
        // 4,096 of them cost about what one does. Looking for each row range by range took 42 times as long, 1.18 s against
        // 28 ms. The measure is the ratio, the best opening of each, run in turn.
        const int Rows = 262_144;
        var apart = LostStreamFiles.RangesOverOlderRows(apart: true, Rows);
        var joined = LostStreamFiles.RangesOverOlderRows(apart: false, Rows);

        var (joinedTime, apartTime) = BestOfEach(() => Open(joined, apart: false), () => Open(apart, apart: true));

        apartTime.Should().BeLessThan(
            joinedTime * 2,
            "rows looked for among 4,096 ranges must cost about what they do among one ({0} against {1})",
            apartTime,
            joinedTime);

        static void Open(byte[] file, bool apart)
        {
            using var document = PdfDocument.Open(file);
            var index = document.Reader.ChainIndex!;
            index.TryGet(3, out _).Should().BeTrue();
            index.TryGet(11, out _).Should().Be(apart);
            index.TryGet(10 + 4096, out _).Should().Be(!apart);
        }
    }

    [Fact]
    public void A_search_through_four_times_as_long_a_line_of_one_byte_tokens_costs_about_four_times_the_time()
    {
        // A stream whose /Length runs past its data, one line of one-byte tokens that a '%' ends: the search for its endstream
        // lexes the line each read ends in as a load reads it, and carries what it lexed into the next read, a few fields,
        // rather than lexing the line again from its start (#261). The search allocates the same whatever the line's length,
        // and four times the line take about four times as long. Lexing the line again from its start at each read took 13
        // times as long, 1.13 s against 85 ms. The measure is the ratio, the best search of each, run in turn.
        const int Small = 1024 * 1024;
        const int Large = 4 * Small;
        var small = LineOfOneByteTokens(Small);
        var large = LineOfOneByteTokens(Large);

        var smallAllocated = Allocations.Least(() => Search(small, Small), out _);
        var largeAllocated = Allocations.Least(() => Search(large, Large), out _);
        var (smallTime, largeTime) = BestOfEach(() => Search(small, Small), () => Search(large, Large));

        largeAllocated.Should().Be(smallAllocated, "what the search allocates does not grow with what it reads");
        largeTime.Should().BeLessThan(
            smallTime * 8,
            "four times the line must cost about four times the time ({0} for {1:N0} bytes, {2} for {3:N0})",
            smallTime,
            Small,
            largeTime,
            Large);

        static int Search(byte[] file, int length)
        {
            using var document = PdfDocument.Open(file);
            var data = document.GetObject(new PdfObjectId(3)).AsStream().Required().RawLength;
            data.Should().Be(length);
            return data;
        }

        static byte[] LineOfOneByteTokens(int length)
        {
            var data = new StringBuilder(length);

            while (data.Length < length - 1)
            {
                data.Append("1 ");
            }

            data.Length = length - 1;
            return new TestPdfBuilder()
                .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
                .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
                .WithObject(3, string.Create(CultureInfo.InvariantCulture, $"<< /Length {length + 1000} >>\nstream\n{data}%\nendstream"))
                .BuildClassic(rootNumber: 1);
        }
    }

    [Theory]
    [InlineData("trailer keywords and no object after them")]
    [InlineData("headers inside one string left open")]
    [InlineData("headers whose lengths end in one run of white space")]
    [InlineData("headers whose integer value a string left open follows")]
    [InlineData("headers whose dictionary a string left open follows")]
    [InlineData("headers whose integer value comment lines of headers follow")]
    [InlineData("headers in the comments of a stream whose length is an object")]
    public void A_rebuild_s_scan_of_four_times_the_bytes_takes_about_four_times_the_time(string shape)
    {
        // The rebuild's scan searched again for the next obj after each trailer keyword, to the end of its piece where none
        // came; lexed the value of each header inside a string left open to the end of its piece; and read the white space
        // after each stream's direct length as far as it ran; and lexed whole the string after a value it looked past, an
        // integer's or a dictionary's, again for each header inside it, and the comment lines after it, which a header the
        // scan takes may lie in — after the end-of-file marker, or in the data of a stream whose length it cannot confirm.
        // Each cost what the file held after it: four times the bytes took 17, 32 and 10 times as long, the open string 77 s
        // against 2.4 s, the strings after values 40 s for a megabyte, the comment lines 62 s. Each byte is now searched, lexed and read past a length a bounded number of times, and four times the
        // bytes take about four times as long (#171). The measure is the ratio, the best opening of each, run in turn.
        var small = ScanShape(shape, 1);
        var large = ScanShape(shape, 4);

        var (smallTime, largeTime) = BestOfEach(() => Open(small), () => Open(large));

        largeTime.Should().BeLessThan(
            smallTime * 8,
            "four times the bytes must cost about four times the time ({0} for {1:N0} bytes, {2} for {3:N0})",
            smallTime,
            small.Length,
            largeTime,
            large.Length);

        static void Open(byte[] file)
        {
            using var document = PdfDocument.Open(file);
            document.WasRepaired.Should().BeTrue();
            document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
        }

        static byte[] ScanShape(string shape, int scale)
        {
            var text = new StringBuilder("%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

            if (shape == "headers in the comments of a stream whose length is an object")
            {
                text.Append("4 0 obj <</Length 5 0 R>>stream\n");
            }

            var (unit, length) = shape switch
            {
                "trailer keywords and no object after them" => ("trailer oxj\n", 256 * 1024),
                "headers inside one string left open" => ("3 0 obj [(\n", 256 * 1024),
                "headers whose integer value a string left open follows" => ("3 0 obj 5 <\n", 256 * 1024),
                "headers whose dictionary a string left open follows" => ("3 0 obj<<>> (\n", 256 * 1024),
                "headers whose integer value comment lines of headers follow" => ("%%EOF 3 0 obj 5\n", 256 * 1024),
                "headers in the comments of a stream whose length is an object" => ("% 3 0 obj 5\n", 256 * 1024),
                _ => ("3 0 obj << /Length 65536 >>\nstream\n", 32 * 1024),
            };

            while (text.Length < scale * length)
            {
                text.Append(unit);
            }

            if (shape == "headers whose lengths end in one run of white space")
            {
                text.Append(' ', scale * 1024 * 1024);
            }

            return Encoding.Latin1.GetBytes(text.Append("\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n").ToString());
        }
    }

    [Fact]
    public void Four_times_as_many_stream_headers_whose_lengths_end_in_white_space_allocate_what_the_fewer_do()
    {
        // Stream headers of one number, each with a direct /Length that ends at a different offset of one run of spaces, and
        // no endstream after it. The scan confirmed each length through the reader's own check, which kept a result for each
        // place, the white space read past a budget too: memory followed the number of headers, 1.2 GB for a file of 710 MB.
        // The scan keeps nothing of the white space it reads, and what it reads is bounded apart from the loads' searches:
        // four times the headers allocate what the fewer do (#171).
        var few = Headers(20_000);
        var many = Headers(80_000);

        var fewAllocated = Allocations.Least(() => Open(few), out _);
        var manyAllocated = Allocations.Least(() => Open(many), out _);

        manyAllocated.Should().BeLessThan(fewAllocated + (256 * 1024), "the scan keeps nothing for each header ({0} against {1})", manyAllocated, fewAllocated);

        static int Open(byte[] file)
        {
            using var document = PdfDocument.Open(file);
            document.WasRepaired.Should().BeTrue();
            return document.Catalog?.Count ?? 0;
        }

        static byte[] Headers(int count)
        {
            var text = new StringBuilder("%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

            for (var i = 0; i < count; i++)
            {
                text.Append(CultureInfo.InvariantCulture, $"3 0 obj<</Length {count * 37:D10}>>stream\n");
            }

            return Encoding.Latin1.GetBytes(text.Append(' ', count * 40).Append("x\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n").ToString());
        }
    }

    /// <summary>
    /// Gets the shortest run of each action, the two run in turn: five times each at least, and on until neither has run
    /// faster for three turns — at most forty turns, or twenty seconds —, so that both are timed warm whatever ran before.
    /// With <paramref name="collect"/>, each run starts from a collected heap, collected outside the time, so that a run that
    /// allocates less than the collector lets the youngest generation take before it collects is not paused inside.
    /// </summary>
    private static (TimeSpan First, TimeSpan Second) BestOfEach(Action first, Action second, bool collect = false)
    {
        var bestFirst = TimeSpan.MaxValue;
        var bestSecond = TimeSpan.MaxValue;
        var steady = 0;
        var spent = Stopwatch.StartNew();

        for (var turn = 0; turn < 5 || (steady < 3 && turn < 40 && spent.Elapsed < TimeSpan.FromSeconds(20)); turn++)
        {
            var timeFirst = Time(first, collect);
            var timeSecond = Time(second, collect);
            steady = timeFirst < bestFirst || timeSecond < bestSecond ? 0 : steady + 1;
            bestFirst = timeFirst < bestFirst ? timeFirst : bestFirst;
            bestSecond = timeSecond < bestSecond ? timeSecond : bestSecond;
        }

        return (bestFirst, bestSecond);

        static TimeSpan Time(Action action, bool collect)
        {
            if (collect)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

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
