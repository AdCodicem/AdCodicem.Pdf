using System.Globalization;
using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Hostile inputs held to a budget on memory live at once: a measure of the whole process, taken alone, which bounds what is
/// held from above; what is let go is watched through a weak reference (#307).
/// </summary>
[Collection(TimingCollection.Name)]
public class HostileInputMemoryTests
{
    /// <summary>What the object stream a disposed document decoded holds, decoded: sixteen megabytes.</summary>
    private const int DecodedLength = 16 * 1024 * 1024;

    [Fact]
    public void Object_streams_each_holding_a_stream_keep_no_more_than_their_budget_once_their_members_are_read()
    {
        // Eight object streams of 8 MB, each holding one stream of five bytes: a member's data lay in its object stream's
        // decoded data, which it kept alive past the budget. Members written as streams are no value now, and the reader
        // keeps its budget and the heaviest stream at most (#161).
        const int Streams = 8;
        const int Decoded = 8 * 1024 * 1024;
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
        };

        for (var k = 0; k < Streams; k++)
        {
            var header = string.Create(CultureInfo.InvariantCulture, $"{20 + k} 0 ");
            var data = HostileInputTests.Compress(Encoding.ASCII.GetBytes(header + "<< /Length 5 >>stream\nhello\nendstream" + new string(' ', Decoded)));
            rows[10 + k] = (1, file.Object(10 + k, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First {header.Length} /Filter /FlateDecode /Length {data.Length} >>\nstream\n{Encoding.Latin1.GetString(data)}\nendstream")), 0);
            rows[20 + k] = (2, 10 + k, 0);
        }

        file.CrossReferenceStream(30, 31, rows, "/Root 1 0 R");
        var bytes = file.Bytes;
        var before = Retained();
        using var document = PdfDocument.Open(bytes);

        for (var k = 0; k < Streams; k++)
        {
            document.GetObject(new PdfObjectId(20 + k)).Should().BeSameAs(PdfNull.Instance);
        }

        var after = Retained();

        after.Should().BeLessThan(before + (32L * 1024 * 1024) + Decoded + (1024 * 1024), "the budget and the heaviest object stream, not every one");
        document.Diagnostics.Should().HaveCount(Streams).And.OnlyContain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.ObjectStreamMemberStream);
    }

    [Fact]
    public void Disposing_a_document_lets_go_of_the_object_streams_it_decoded_though_a_reference_it_gave_is_held()
    {
        // Sixteen megabytes of an object stream's decoded data, which the reader keeps, and a reference handed out, which
        // reaches the reader: disposing lets go of the data all the same. The data is watched through a weak reference, not
        // through the memory of the whole process: a shared pool lets go, at a full collection, of arrays a test before
        // parked in it half a minute earlier, which made that memory fall by 92 MB between two of its measures (#307).
        var file = HostileInputTests.ObjectStreams(streams: 1, decodedLength: DecodedLength, objectsPerStream: 1, everyIndexWrong: false);
        var (held, data) = OpenDecodeAndDispose(file);

        Collect();

        data.IsAlive.Should().BeFalse("the object streams the document decoded are let go");
        GC.KeepAlive(held);
    }

    [Fact]
    public void Validating_object_streams_whose_data_share_a_stretch_of_the_file_holds_no_copy_of_their_data()
    {
        // Three hundred object streams of one member each, each nested in the one before, every /Length confirmed by the one
        // endstream a megabyte on: validating them, and reading their members, kept each one's data with its stream in the
        // object cache, whatever the object stream budget let go — 300 MB from a file of 1 MB (#170).
        const int Streams = 300;
        var file = HostileInputTests.Overlapping(Streams, filler: 1024 * 1024, objectStreams: true);
        var before = Retained();
        using var document = PdfDocument.Open(file);

        new PdfValidator().Validate(document);

        for (var k = 0; k < Streams; k++)
        {
            document.GetObject(new PdfObjectId(3000 + k));
        }

        var after = Retained();

        after.Should().BeLessThan(before + (8L * 1024 * 1024), "a file in memory lends each object stream its data, and no copy is kept");
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("name")]
    [InlineData("key")]
    public void Placing_a_fault_lets_go_of_the_containers_the_search_went_through(string fault)
    {
        // The catalog holds, under /Zz, an array a caller wrote, which holds a reference to nothing, a name with a null
        // character, or a dictionary keyed by one. The object graph places the fault for its finding by searching the
        // catalog again, keeping the containers it goes through in the array the walk keeps its own in, which lives as long
        // as the graph: once the fault is found, the search lets go of them, as the walk does of each container it leaves
        // (#168). The array is watched through a weak reference once the catalog no longer holds it.
        using var document = PdfDocument.Open(PdfTemplate.Build(PdfTemplate.Sound));
        var (graph, array) = PlaceFaultUnderTheCatalog(document, fault);

        document.Catalog!.Remove(PdfName.Get("Zz"));
        Collect();

        array.IsAlive.Should().BeFalse("the search let go of the containers it went through");
        GC.KeepAlive(graph);
    }

    /// <summary>
    /// Sets the catalog's <c>/Zz</c> to an array holding <paramref name="fault"/>, walks the objects of
    /// <paramref name="document"/> and places the fault, giving the graph and a weak reference to the array; out of line, so
    /// that no strong reference to the array outlives the call.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (ObjectGraph Graph, WeakReference Array) PlaceFaultUnderTheCatalog(PdfDocument document, string fault)
    {
        var keyed = new PdfDictionary();
        keyed.Set(PdfName.Get("A\0"), PdfInteger.Create(1));
        var array = new PdfArray(
        [
            fault switch
            {
                "reference" => new PdfReference(new PdfObjectId(9999), document.Reader),
                "name" => PdfName.Get("A\0"),
                _ => keyed,
            },
        ]);
        document.Catalog!.Set(PdfName.Get("Zz"), array);
        document.Reader.SettleIndex();

        var graph = ObjectGraph.Run(document, PageTreeWalk.Run(document));

        graph.MissingReferences.Concat(graph.NullCharacterNames).Should().ContainSingle().Which.Message.Should().Contain("under /Zz[0]");
        return (graph, new WeakReference(array));
    }

    /// <summary>
    /// Opens <paramref name="file"/>, reads the member of its object stream, takes a reference from the document and
    /// disposes of it, giving the reference and a weak reference to the array that held the stream's decoded data; out of
    /// line, so that no strong reference to the document or to the array outlives the call.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (PdfObject Held, WeakReference Data) OpenDecodeAndDispose(byte[] file)
    {
        var document = PdfDocument.Open(file);
        var stream = HostileInputTests.StreamNumber(0);
        document.Reader.KeptObjectStreamData(stream).IsEmpty.Should().BeTrue("nothing is decoded before an object is read");
        document.GetObject(new PdfObjectId(HostileInputTests.PackedNumber(0, 0))).AsDictionary().Required();
        var held = document.Trailer.GetRaw(PdfName.Root)!;
        var data = Watch(document.Reader.KeptObjectStreamData(stream));

        Collect();

        data.IsAlive.Should().BeTrue("the reader keeps the decoded data while the document is open");
        document.Dispose();
        return (held, data);
    }

    /// <summary>Gives a weak reference to the array behind <paramref name="kept"/>, the whole of an object stream's decoded data.</summary>
    private static WeakReference Watch(ReadOnlyMemory<byte> kept)
    {
        System.Runtime.InteropServices.MemoryMarshal.TryGetArray(kept, out var data).Should().BeTrue("decoded data is held in an array");
        data.Count.Should().Be(DecodedLength, "the reader keeps the whole of the decoded data");
        return new WeakReference(data.Array);
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static long Retained()
    {
        Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    [Fact]
    public void A_chain_of_object_streams_each_needing_the_next_to_decode_holds_one_level_s_output_at_once()
    {
        // 64 object streams, each decoding through [/FlateDecode /FlateDecode] to 4 MB less a byte, each taking its
        // second step's /Predictor from an object the next one holds, under a bound of 4 MB. Memory live at once is
        // sampled, every object collected first, each time the reader reads the file. Decoding while the parameters
        // loaded held one output per level, 64 deep: about 250 MB. Resolved first, no level holds an output while the
        // next decodes, and what stays live is the object streams the reader keeps, within its 32 MB budget (#165).
        const int Bound = 4 * 1024 * 1024;
        var source = new SamplingSource(NestedChain(64, Bound - 1));
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = Bound } };
        using var document = PdfDocument.Open(source, options, ownsSource: true);
        var before = GC.GetTotalMemory(forceFullCollection: true);
        source.Sampling = true;

        var three = document.GetObject(new PdfObjectId(3));

        source.Sampling = false;
        three.Should().BeOfType<PdfDictionary>();
        var held = source.MostLive - before;
        held.Should().BeLessThan((32L * 1024 * 1024) + (3L * Bound), $"memory was sampled {source.Samples} times");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Walking_a_chain_of_references_holds_what_the_reader_keeps_whatever_its_length(bool referenceLast)
    {
        // Two hundred objects, each naming the next under /Next and holding a string of 200,000 bytes, opened with a cache
        // of eight objects. With /Next first, the object graph described each object from the value it walked, which held
        // every link of the chain at once until the chain's end: 41 MB, growing with the chain; it reads an object again to
        // describe it. With /Next last, its stack held each object's /Pad, met before /Next and walked only once the chain
        // had ended: 40 MB, and 80 MB for a chain of four hundred (#168); it walks an object whole when it reads it, and
        // keeps of the next only its number. What stays live either way is what the reader keeps — its cache, the windows it
        // reads through —: 13 MB, the same for a chain of a hundred or of four hundred.
        const int Links = 200;
        var pad = new string('x', 200_000);
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R /Chain 3 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        for (var link = 3; link < Links + 3; link++)
        {
            builder.WithObject(
                link,
                referenceLast
                    ? string.Create(CultureInfo.InvariantCulture, $"<< /Pad ({pad}) /Next {link + 1} 0 R >>")
                    : string.Create(CultureInfo.InvariantCulture, $"<< /Next {link + 1} 0 R /Pad ({pad}) >>"));
        }

        builder.WithObject(Links + 3, "null");
        var source = new SamplingSource(builder.BuildClassic(rootNumber: 1));
        using var document = PdfDocument.Open(source, PdfReaderOptions.Default with { ObjectCacheCapacity = 8 }, ownsSource: true);
        document.Reader.SettleIndex();
        var pages = Validation.PageTreeWalk.Run(document);
        var before = GC.GetTotalMemory(forceFullCollection: true);
        source.Sampling = true;

        var graph = Validation.ObjectGraph.Run(document, pages);

        source.Sampling = false;
        graph.MissingReferences.Total.Should().Be(0, "the chain ends at a literal null");
        var held = source.MostLive - before;
        held.Should().BeLessThan(20L * 1024 * 1024, $"memory was sampled {source.Samples} times");
    }

    [Theory]
    [InlineData("under its own generation")]
    [InlineData("under every generation")]
    public void References_met_to_an_object_waiting_to_be_read_cost_the_walk_what_they_cost_once_it_is_read(string shape)
    {
        // The catalog names object 5, then object 6, which names object 5 200,000 times, under its own generation or under
        // each of 1 to 65,535 in turn: read last claimed first, object 6 is walked while object 5 waits. Each reference met to
        // it then was kept until it was read, to tell its generation — one entry each, then one for each generation —, growing
        // with the references; those under the claim's generation are counted by the object that holds them, one under another
        // is told at once, the object read for it, and the walk allocates about what it allocates with the keys the other way
        // round, object 5 read first (#185).
        var references = new StringBuilder();

        for (var k = 0; k < 200_000; k++)
        {
            references.Append("5 ").Append(shape == "under its own generation" ? 0 : 1 + (k % 65_535)).Append(" R ");
        }

        var waiting = Allocated("/A 5 0 R /B 6 0 R");
        var read = Allocated("/B 6 0 R /A 5 0 R");

        waiting.Should().BeLessThan(read + (512 * 1024), $"read first, the walk allocated {read:N0} bytes");

        long Allocated(string entries)
        {
            var file = new TestPdfBuilder()
                .WithObject(1, "<< /Type /Catalog /Pages 2 0 R " + entries + " >>")
                .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
                .WithObject(5, "(five)")
                .WithObject(6, "[" + references + "]")
                .BuildClassic(rootNumber: 1);
            using var document = PdfDocument.Open(file);
            document.Reader.SettleIndex();
            var pages = Validation.PageTreeWalk.Run(document);
            _ = document.GetObject(6);
            var before = GC.GetAllocatedBytesForCurrentThread();

            Validation.ObjectGraph.Run(document, pages).MissingReferences.Total.Should().Be(shape == "under its own generation" ? 0 : 1);

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }

    [Fact]
    public void References_from_many_holders_to_many_objects_waiting_to_be_read_cost_the_walk_what_they_cost_once_they_are_read()
    {
        // The catalog names 800 objects, then 800 holders, each naming every one of the 800: read last claimed first, the
        // holders are walked while the 800 objects wait. A count was kept for each holder and object, as many as the
        // references; past ObjectGraph.MaxWaitingCounts counts, an object is read at the next reference to it from a holder
        // that has no count yet, and the walk allocates about what it allocates with the holders named first, the objects read
        // first (#185).
        const int Objects = 800;
        var objects = string.Join(' ', Enumerable.Range(10, Objects).Select(number => string.Create(CultureInfo.InvariantCulture, $"{number} 0 R")));
        var holders = string.Join(' ', Enumerable.Range(1_000, Objects).Select(number => string.Create(CultureInfo.InvariantCulture, $"{number} 0 R")));

        var waiting = Allocated("/W [" + objects + "] /H [" + holders + "]");
        var read = Allocated("/H [" + holders + "] /W [" + objects + "]");

        waiting.Should().BeLessThan(read + (6 * 1024 * 1024), $"objects read first, the walk allocated {read:N0} bytes");

        long Allocated(string entries)
        {
            var builder = new TestPdfBuilder()
                .WithObject(1, "<< /Type /Catalog /Pages 2 0 R " + entries + " >>")
                .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");

            for (var k = 0; k < Objects; k++)
            {
                builder.WithObject(10 + k, "(object)").WithObject(1_000 + k, "[" + objects + "]");
            }

            using var document = PdfDocument.Open(builder.BuildClassic(rootNumber: 1));
            document.Reader.SettleIndex();
            var pages = Validation.PageTreeWalk.Run(document);
            var before = GC.GetAllocatedBytesForCurrentThread();

            Validation.ObjectGraph.Run(document, pages).MissingReferences.Total.Should().Be(0);

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }

    [Fact]
    public void Walking_a_deep_page_tree_whose_nodes_carry_large_values_holds_what_the_reader_keeps_whatever_its_depth()
    {
        // Four hundred page tree nodes, each the one kid of the node above it and carrying a string of 100,000 bytes after
        // its /Kids, the last listing a page, opened with a cache of 64 objects. The walk kept each node on its path in its
        // frame, past the reader's cache, until it left the node: 40 MB, and 80 MB for eight hundred nodes (#201). A frame
        // keeps the node's /Count and its array of kids, not the node, and what stays live is what the reader keeps, its
        // cache: 7 MB, the same for two hundred nodes or eight hundred.
        const int Depth = 400;
        var big = new string('x', 100_000);
        var builder = new TestPdfBuilder().WithObject(1, "<< /Type /Catalog /Pages 3 0 R >>");
        var last = Depth + 2;

        for (var node = 3; node <= last; node++)
        {
            var parent = node == 3 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"/Parent {node - 1} 0 R ");
            builder.WithObject(node, string.Create(CultureInfo.InvariantCulture, $"<< /Type /Pages {parent}/Kids [{node + 1} 0 R] /Count 1 /Big ({big}) >>"));
        }

        builder.WithObject(last + 1, string.Create(CultureInfo.InvariantCulture, $"<< /Type /Page /Parent {last} 0 R /MediaBox [0 0 612 792] /Resources << >> >>"));
        var source = new SamplingSource(builder.BuildClassic(rootNumber: 1)) { Every = 4 };
        using var document = PdfDocument.Open(source, PdfReaderOptions.Default with { ObjectCacheCapacity = 64 }, ownsSource: true);
        document.Reader.SettleIndex();
        var before = GC.GetTotalMemory(forceFullCollection: true);
        source.Sampling = true;

        var walk = Validation.PageTreeWalk.Run(document);

        source.Sampling = false;
        walk.PageCount.Should().Be(1);
        walk.CountMismatches.Total.Should().Be(0, "each node gives the one page below it");
        var held = source.MostLive - before;
        held.Should().BeLessThan(16L * 1024 * 1024, $"memory was sampled {source.Samples} times");
    }

    [Fact]
    public void The_walk_of_the_object_streams_dependencies_holds_what_the_reader_keeps_whatever_it_reaches()
    {
        // A Flate object stream takes its /DecodeParms from object 3, an array of references to 2,000 arrays, each of a
        // thousand references, opened with a cache of 64 objects. The walk kept the references of every object it read, to
        // read each once: 16 MB, growing with what the decoding keys reach. It keeps as many objects as the reader keeps in
        // its cache; and it now reads what decoding reads (#277): the one Flate step's element of 3, an array that gives no
        // parameters, and of each array as many elements as a chain of filters reads.
        const int Arrays = 2000;
        const string Members = "6 0 << >>";
        var data = Encoding.Latin1.GetString(Compress(Encoding.Latin1.GetBytes(Members)));
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [5] = (1, file.Object(5, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First 4 /Filter /FlateDecode /DecodeParms 3 0 R /Length {data.Length} >>\nstream\n{data}\nendstream")), 0),
            [6] = (2, 5, 0),
        };
        var all = new StringBuilder("[");
        var thousand = "[" + new StringBuilder().Insert(0, "1 0 R ", 1000) + "]";

        for (var number = 10; number < Arrays + 10; number++)
        {
            all.Append(CultureInfo.InvariantCulture, $"{number} 0 R ");
            rows[number] = (1, file.Object(number, thousand), 0);
        }

        rows[3] = (1, file.Object(3, all.Append(']').ToString()), 0);
        file.CrossReferenceStream(Arrays + 10, Arrays + 11, rows, "/Root 1 0 R");
        var source = new SamplingSource(file.Bytes) { Every = 16 };
        using var document = PdfDocument.Open(source, PdfReaderOptions.Default with { ObjectCacheCapacity = 64 }, ownsSource: true);
        var before = GC.GetTotalMemory(forceFullCollection: true);
        source.Sampling = true;

        Validation.ObjectStreamDependencies.Run(document).Circular.Should().BeEmpty();

        source.Sampling = false;
        var held = source.MostLive - before;
        held.Should().BeLessThan(6L * 1024 * 1024, $"memory was sampled {source.Samples} times");
    }

    [Theory]
    [InlineData("[<< /Predictor 1 /Columns {0} >>]", 32)]
    [InlineData("<< /Predictor 1 /Columns {0} >>", 4)]
    public void The_walk_of_the_object_streams_dependencies_keeps_no_more_objects_than_the_reader_caches(string parameter, int mostMegabytes)
    {
        // Five hundred Flate object streams each take their /DecodeParms from an object of their own, written in the file: an
        // array holding << /Predictor 1 /Columns [...] >>, of 20,000 zeros, opened with a cache of 64 objects. The walk reads
        // each once a validation and keeps what decoding may read of it, as it keeps every object it reads — of an array, as
        // many elements as a chain of filters reads, as they are written —, about 260 KB each: of all five hundred, 130 MB.
        // It keeps as many as the reader keeps in its cache, the oldest let go first (#201): 17 MB. Of the dictionary written
        // directly, it keeps only the entries a filter's step reads written as a reference, an integer or a real, not a
        // /Columns array, which decoding reads as absent (#181): 50 KB, where the dictionary kept whole took 17 MB.
        const int Streams = 500;
        var columns = "[" + new StringBuilder().Insert(0, "0 ", 20_000) + "]";
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
        };

        for (var k = 0; k < Streams; k++)
        {
            var (parameters, stream, member) = (1000 + k, 2000 + (2 * k), 2001 + (2 * k));
            var header = string.Create(CultureInfo.InvariantCulture, $"{member} 0 ");
            var data = Encoding.Latin1.GetString(Compress(Encoding.Latin1.GetBytes(header + "<< >>")));
            rows[parameters] = (1, file.Object(parameters, string.Format(CultureInfo.InvariantCulture, parameter, columns)), 0);
            rows[stream] = (1, file.Object(stream, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First {header.Length} /Filter /FlateDecode /DecodeParms {parameters} 0 R /Length {data.Length} >>\nstream\n{data}\nendstream")), 0);
            rows[member] = (2, stream, 0);
        }

        file.CrossReferenceStream(3000, 3001, rows, "/Root 1 0 R");
        var source = new SamplingSource(file.Bytes) { Every = 16 };
        using var document = PdfDocument.Open(source, PdfReaderOptions.Default with { ObjectCacheCapacity = 64 }, ownsSource: true);
        var before = GC.GetTotalMemory(forceFullCollection: true);
        source.Sampling = true;

        Validation.ObjectStreamDependencies.Run(document).Circular.Should().BeEmpty();

        source.Sampling = false;
        var held = source.MostLive - before;
        held.Should().BeLessThan(mostMegabytes * 1024L * 1024, $"memory was sampled {source.Samples} times");
    }

    [Theory]
    [InlineData("<< /Predictor 1 /Pad [{0}] >>")]
    [InlineData("[<< /Predictor 1 >> {0}]")]
    public void The_walk_keeps_of_each_object_it_reads_what_decoding_may_read_of_it(string parameters)
    {
        // As above, a hundred streams, each taking its /DecodeParms from an object of its own: parameters that hold 50,000
        // zeros under a key no step reads, or an array of parameters whose 50,000 elements past the first lie past the
        // guard on filters. The walk keeps of each what decoding may read — the entries a step reads, the elements a chain
        // of filters reads —, and holds no more than the reader keeps: kept whole, the 64 objects it keeps at once held
        // 25 MB (#201).
        const int Streams = 100;
        var value = string.Format(CultureInfo.InvariantCulture, parameters, new StringBuilder().Insert(0, "0 ", 50_000));
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
        };

        for (var k = 0; k < Streams; k++)
        {
            var (decodeParms, stream, member) = (1000 + k, 2000 + (2 * k), 2001 + (2 * k));
            var header = string.Create(CultureInfo.InvariantCulture, $"{member} 0 ");
            var data = Encoding.Latin1.GetString(Compress(Encoding.Latin1.GetBytes(header + "<< >>")));
            rows[decodeParms] = (1, file.Object(decodeParms, value), 0);
            rows[stream] = (1, file.Object(stream, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First {header.Length} /Filter /FlateDecode /DecodeParms {decodeParms} 0 R /Length {data.Length} >>\nstream\n{data}\nendstream")), 0);
            rows[member] = (2, stream, 0);
        }

        file.CrossReferenceStream(3000, 3001, rows, "/Root 1 0 R");
        var source = new SamplingSource(file.Bytes) { Every = 16 };
        using var document = PdfDocument.Open(source, PdfReaderOptions.Default with { ObjectCacheCapacity = 64 }, ownsSource: true);
        var before = GC.GetTotalMemory(forceFullCollection: true);
        source.Sampling = true;

        Validation.ObjectStreamDependencies.Run(document).Circular.Should().BeEmpty();

        source.Sampling = false;
        var held = source.MostLive - before;
        held.Should().BeLessThan(8L * 1024 * 1024, $"memory was sampled {source.Samples} times");
    }

    [Theory]
    [InlineData("/Columns [{0}]", "<<>> ")]
    [InlineData("/Colors << {0}>>", "/K{0} [] ")]
    public void The_walk_keeps_of_the_parameters_of_the_streams_met_on_the_way_what_decoding_reads_whatever_the_file_wrote_in_them(string parameter, string element)
    {
        // Sixty-four object streams, each taking its /Length from the first of a nest of 60 streams by /Length, each of which
        // gives a parameter of a filter's step that is neither a number nor a reference: an array of 64 dictionaries under
        // /Columns, or a dictionary of 64 arrays under /Colors, which decoding reads as an entry absent. Opened with the
        // smallest cache, 64 objects, the walk keeps a link for each of the 3,840 streams. A link that held what a filter's
        // step reads of its stream's dictionary, as the file wrote it, kept each one's parameter: 25 to 30 MB at once. The
        // walk keeps of a parameter only a reference, an integer or a real, and the parameters of as many streams as the
        // reader caches objects, apart from the links (#181): what stays live is what the reader keeps, 2 to 3.5 MB.
        const int Heads = 64;
        const int Depth = 60;
        var value = string.Format(CultureInfo.InvariantCulture, parameter, string.Concat(Enumerable.Range(0, 64).Select(index => string.Format(CultureInfo.InvariantCulture, element, index))));
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
        };
        var next = 10;

        for (var head = 0; head < Heads; head++)
        {
            var first = next;

            for (var level = 0; level < Depth; level++, next++)
            {
                var length = level + 1 < Depth ? string.Create(CultureInfo.InvariantCulture, $"{next + 1} 0 R") : "0";
                rows[next] = (1, file.Object(next, $"<< {value} /Length {length} >>\nstream\n\nendstream"), 0);
            }

            var (stream, member) = (next, next + 1);
            var header = string.Create(CultureInfo.InvariantCulture, $"{member} 0 ");
            rows[stream] = (1, file.Object(stream, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First {header.Length} /Length {first} 0 R >>\nstream\n{header}<< >>\nendstream")), 0);
            rows[member] = (2, stream, 0);
            next += 2;
        }

        file.CrossReferenceStream(next, next + 1, rows, "/Root 1 0 R");
        var source = new SamplingSource(file.Bytes) { Every = 32 };
        using var document = PdfDocument.Open(source, PdfReaderOptions.Default with { ObjectCacheCapacity = 64 }, ownsSource: true);
        var before = GC.GetTotalMemory(forceFullCollection: true);
        source.Sampling = true;

        Validation.ObjectStreamDependencies.Run(document).Circular.Should().BeEmpty();

        source.Sampling = false;
        var held = source.MostLive - before;
        held.Should().BeLessThan(8L * 1024 * 1024, $"memory was sampled {source.Samples} times");
    }

    private static byte[] Compress(byte[] data)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return compressed.ToArray();
    }

    /// <summary>
    /// A file whose catalog's object 3 object stream 101 holds, and each object stream 100+k of
    /// <paramref name="levels"/> takes its predictor from object 200+k, which object stream 100+k+1 holds; the last
    /// is not encoded.
    /// </summary>
    private static byte[] NestedChain(int levels, int decodedLength)
    {
        using var output = new MemoryStream();
        var offsets = new Dictionary<int, long>();
        void Write(string text) => output.Write(Encoding.ASCII.GetBytes(text));

        void WriteObject(int number, string dictionary, byte[]? data = null)
        {
            offsets[number] = output.Position;
            Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{dictionary}"));

            if (data is not null)
            {
                Write("\nstream\n");
                output.Write(data);
                Write("\nendstream");
            }

            Write("\nendobj\n");
        }

        Write("%PDF-1.5\n");
        WriteObject(1, "<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        for (var level = 1; level <= levels + 1; level++)
        {
            var member = level == 1 ? 3 : 200 + level - 1;
            var body = level == 1 ? "<< /Title (three) >>" : "1";
            var header = string.Create(CultureInfo.InvariantCulture, $"{member} 0 ");
            var last = level == levels + 1;
            var contents = new byte[last ? header.Length + body.Length : Math.Max(decodedLength, header.Length + body.Length)];
            contents.AsSpan().Fill((byte)' ');
            Encoding.ASCII.GetBytes(header + body).CopyTo(contents, 0);

            if (last)
            {
                WriteObject(100 + level, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First {header.Length} /Length {contents.Length} >>"), contents);
            }
            else
            {
                var data = Compress(Compress(contents));
                WriteObject(
                    100 + level,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"<< /Type /ObjStm /N 1 /First {header.Length} /Filter [/FlateDecode /FlateDecode] /DecodeParms [null << /Predictor {200 + level} 0 R >>] /Length {data.Length} >>"),
                    data);
            }
        }

        const int XRef = 400;
        var rows = new byte[(XRef + 1) * 7];

        void Row(int number, int type, long field, int index)
        {
            var at = number * 7;
            rows[at] = (byte)type;
            rows[at + 1] = (byte)(field >> 24);
            rows[at + 2] = (byte)(field >> 16);
            rows[at + 3] = (byte)(field >> 8);
            rows[at + 4] = (byte)field;
            rows[at + 5] = (byte)(index >> 8);
            rows[at + 6] = (byte)index;
        }

        Row(0, 0, 0, 65535);

        foreach (var (number, offset) in offsets)
        {
            Row(number, 1, offset, 0);
        }

        Row(3, 2, 101, 0);

        for (var level = 1; level <= levels; level++)
        {
            Row(200 + level, 2, 100 + level + 1, 0);
        }

        var xref = output.Position;
        Row(XRef, 1, xref, 0);
        Write(string.Create(CultureInfo.InvariantCulture, $"{XRef} 0 obj\n<< /Type /XRef /Size {XRef + 1} /W [1 4 2] /Root 1 0 R /Length {rows.Length} >>\nstream\n"));
        output.Write(rows);
        Write(string.Create(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n"));
        return output.ToArray();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/Pad")]
    public void The_walk_keeps_of_the_dictionaries_of_object_streams_whose_standing_waits_what_their_decoding_reads(string? key)
    {
        // Two hundred object streams, each taking its /Length from the object the next holds, the last from its own, and its
        // /DecodeParms from an array of 5,000 zeros written in its dictionary, or from parameters holding it. The walk of the
        // first meets the second's object, whose standing waits on the third's, and so on: 199 standings wait at once, and
        // the walk keeps what it read of each one's dictionary, to walk it again once the one it waits on is known (#312).
        // The reader keeps each stream it read for the walk, some 75 KB, 15 MB for all; of an array, decoding reads as many
        // elements as a chain of filters has steps, and of parameters what a step reads, and the walk keeps no more: kept
        // whole, the 199 arrays held another 13 MB at once.
        var source = new SamplingSource(HostileInputTests.WaitingArrays(200, 5000, key)) { Every = 16 };
        using var document = PdfDocument.Open(source, PdfReaderOptions.Default, ownsSource: true);
        var before = GC.GetTotalMemory(forceFullCollection: true);
        source.Sampling = true;

        Validation.ObjectStreamDependencies.Run(document).CircularStreams.Should().Equal(1398);

        source.Sampling = false;
        var held = source.MostLive - before;
        held.Should().BeLessThan(20L * 1024 * 1024, $"memory was sampled {source.Samples} times");
    }

    [Fact]
    public void The_walk_keeps_the_dictionaries_of_as_many_object_streams_whose_standing_waits_as_the_reader_caches_objects()
    {
        // Four hundred object streams, each taking its /Length from the object the next holds, the last from its own, and its
        // /DecodeParms from an array holding << /Predictor 1 /Columns [...] >>, of 5,000 zeros, opened with a cache of 64
        // objects: 399 standings wait at once, and the walk keeps what it reads of each one's dictionary, the array whole, as
        // many elements as a chain of filters reads, some 75 KB, as the reader keeps each stream it read for the walk: some
        // 9 MB in all. It keeps as many as the reader caches, the oldest let go first, and read again when walked again
        // (#312): kept for every stream that waits, they held 31 MB at once.
        var source = new SamplingSource(HostileInputTests.WaitingArrays(400, 5000, "/Columns", placed: true)) { Every = 16 };
        using var document = PdfDocument.Open(source, PdfReaderOptions.Default with { ObjectCacheCapacity = 64 }, ownsSource: true);
        var before = GC.GetTotalMemory(forceFullCollection: true);
        source.Sampling = true;

        Validation.ObjectStreamDependencies.Run(document).CircularStreams.Should().Equal(1798);

        source.Sampling = false;
        var held = source.MostLive - before;
        held.Should().BeLessThan(16L * 1024 * 1024, $"memory was sampled {source.Samples} times");
    }

    [Theory]
    [InlineData("lookups past a full index")]
    [InlineData("lengths a rebuilt index lacks")]
    [InlineData("filter chains")]
    public void A_document_opened_to_throw_keeps_a_bounded_number_of_the_exceptions_its_reads_threw(string shape)
    {
        // Under ThrowOnLimit, a load or a decoding that reached a guard keeps its exception, to throw it again without reading
        // up to the bound again. A lookup of a number a full index lacks threw anew each time, and kept each: 40,000 lookups
        // of a caller kept some 40 MB. A rebuild's taking in of the object streams loads every stream it indexed, each of
        // 20,000 naming a /Length its index lacks: each lookup kept its own, some 40 MB from a file of 2 MB. And each of
        // 16,000 members alone in an object stream whose filters pass the guard, read twice by a caller, kept its own, and
        // its stream's, some 32 MB. A lookup saves no read, and is kept nowhere; the others are kept as many as the reader
        // caches objects, the oldest let go first. Each read the caller sees throws the guard's own exception.
        var (file, options, first, reads) = shape switch
        {
            "lookups past a full index" => (
                IndexFullOnRebuild(),
                new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxIndexEntries = 40 } },
                3,
                Enumerable.Range(5_000_000, 40_000).ToArray()),
            "lengths a rebuilt index lacks" => (
                LengthsTheRebuiltIndexLacks(20_000),
                new PdfReaderOptions { ThrowOnLimit = true },
                31,
                Enumerable.Range(100, 20_000).ToArray()),
            _ => (
                MembersBehindTooManyFilters(16_000),
                new PdfReaderOptions { ThrowOnLimit = true, ObjectCacheCapacity = 64, Limits = PdfReaderLimits.Default with { MaxFilterChainLength = 1 } },
                10,
                [.. Enumerable.Range(10, 16_000), .. Enumerable.Range(10, 16_000)]),
        };
        var before = Retained();
        using var document = PdfDocument.Open(file, options);
        var opening = FluentActions.Invoking(() => document.GetObject(new PdfObjectId(first)));
        var guard = opening.Should().Throw<PdfLimitExceededException>().Which;
        var thrown = new HashSet<(string Code, string Message)>();

        foreach (var number in reads)
        {
            try
            {
                document.GetObject(new PdfObjectId(number));
            }
            catch (PdfLimitExceededException exception)
            {
                thrown.Add((exception.Code, exception.Message));
            }
        }

        var after = Retained();

        thrown.Should().Equal((guard.Code, guard.Message));
        (after - before).Should().BeLessThan(8L * 1024 * 1024, "the exceptions a document keeps are bounded, not one for each read");
        GC.KeepAlive(document);
    }

    /// <summary>
    /// Writes a file whose classic table places object 3 on its own <c>xref</c> keyword, so that a lookup of 3 rebuilds the
    /// index, and holds objects 3,000,000 and on, which a bound of 40 entries leaves out of the rebuilt index.
    /// </summary>
    private static byte[] IndexFullOnRebuild()
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };

        for (var k = 0; k < 50; k++)
        {
            file.Object(3_000_000 + k, "(filler)");
        }

        rows[3] = file.Position;
        file.Table(4, rows, "/Size 4 /Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// Writes a file like that of the guard on the rebuild's reads, with sixteen spelled streams, objects 50 to 65, under a
    /// classic table whose <c>/Prev</c> names no section, so that a lookup of object 31, which the table lacks, rebuilds the
    /// index and its scan reaches the guard; with <paramref name="streams"/> streams, objects 100 and on, each of a
    /// <c>/Length</c> that names an object the file lacks.
    /// </summary>
    private static byte[] LengthsTheRebuiltIndexLacks(int streams)
    {
        const int Spelled = 16;
        const string Head = "50 0 obj\n<< /Length 0000000 >>\nstream\nabc\n";
        var spelled = new StringBuilder();

        for (var i = 0; i < Spelled; i++)
        {
            var length = ((Spelled - i) * Head.Length) - (Head.Length - "abc\n".Length);
            spelled.Append(Head.Replace("50 0", string.Create(CultureInfo.InvariantCulture, $"{50 + i} 0"), StringComparison.Ordinal)
                .Replace("0000000", length.ToString("D7", CultureInfo.InvariantCulture), StringComparison.Ordinal));
        }

        var data = spelled.Append(' ', Math.Max(64 * 1024, streams * 60)).Append("attached tail\n").ToString();
        const string Inner = "%PDF-1.4\n5 0 obj\n<< /Length 3 >>\nstream\nabc\nendstream\nendobj\n% the attached file's last line";
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
            [10] = file.Object(10, "<< /Type /EmbeddedFile /Length 11 0 R >>\nstream\n" + data + "endstream"),
            [11] = file.Object(11, "% the length of the attached file's data, which its writer filled in once it had written it\n" + data.Length.ToString(CultureInfo.InvariantCulture)),
            [30] = file.Position,
        };
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"30 0 obj\n<< /Type /EmbeddedFile /Length {Inner.Length} >>\nstream\n{Inner}") +
            new string(' ', 70) + "endstream endobj 31 0 obj (real) endobj\n");
        var small = new StringBuilder();

        for (var k = 0; k < streams; k++)
        {
            small.Append(CultureInfo.InvariantCulture, $"{100 + k} 0 obj<</Length {1_000_000 + k} 0 R>>stream\nabc\nendstream endobj\n");
        }

        file.Raw(small.ToString());
        file.Table(31, rows, "/Size 31 /Root 1 0 R /Prev 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// Writes a file of <paramref name="members"/> objects, 10 and on, each alone in an object stream whose <c>/Filter</c>
    /// names two hexadecimal filters, the first of which decodes its data, all placed by a cross-reference stream.
    /// </summary>
    private static byte[] MembersBehindTooManyFilters(int members)
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
        };

        for (var k = 0; k < members; k++)
        {
            var stream = 10 + members + k;
            var header = string.Create(CultureInfo.InvariantCulture, $"{10 + k} 0 ");
            var hex = Convert.ToHexString(Encoding.ASCII.GetBytes(header + "(m) ")) + ">";
            rows[stream] = (1, file.Object(stream, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First {header.Length} /Filter [/ASCIIHexDecode /ASCIIHexDecode] /Length {hex.Length} >>\nstream\n{hex}\nendstream")), 0);
            rows[10 + k] = (2, stream, 0);
        }

        file.CrossReferenceStream(10 + (2 * members), 11 + (2 * members), rows, "/Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>A file in memory that samples the memory live at once, every object collected first, at each read.</summary>
    private sealed class SamplingSource(byte[] data) : PdfFileSource
    {
        private int _reads;

        /// <summary>Gets or sets a value indicating whether reads sample the memory live at once.</summary>
        public bool Sampling { get; set; }

        /// <summary>Gets or sets how many reads one sample is taken for: every read by default.</summary>
        public int Every { get; init; } = 1;

        /// <summary>Gets the most memory a sample found live at once.</summary>
        public long MostLive { get; private set; }

        /// <summary>Gets how many samples were taken.</summary>
        public int Samples { get; private set; }

        public override long Length => data.Length;

        public override int Read(long offset, Span<byte> buffer)
        {
            if (Sampling && _reads++ % Every == 0)
            {
                Samples++;
                MostLive = Math.Max(MostLive, GC.GetTotalMemory(forceFullCollection: true));
            }

            var count = (int)Math.Min(buffer.Length, data.Length - offset);
            data.AsSpan((int)offset, count).CopyTo(buffer);
            return count;
        }
    }
}
