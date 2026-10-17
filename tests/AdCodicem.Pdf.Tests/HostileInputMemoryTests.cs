using System.Globalization;
using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Hostile inputs held to a budget on memory live at once: a measure of the whole process, taken alone.
/// </summary>
[Collection(TimingCollection.Name)]
public class HostileInputMemoryTests
{
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

    [Fact]
    public void Walking_a_chain_of_references_holds_what_the_reader_keeps_whatever_its_length()
    {
        // Two hundred objects, each naming the next under /Next, its first key, and holding a string of 200,000 bytes, opened
        // with a cache of eight objects. The object graph described each object from the value it walked, which held every
        // link of the chain at once until the chain's end: 41 MB, growing with the chain. It reads an object again to
        // describe it, so that what stays live is what the reader keeps — its cache, the windows it reads through —: 14 MB,
        // the same for a chain of a hundred.
        const int Links = 200;
        var pad = new string('x', 200_000);
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R /Chain 3 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        for (var link = 3; link < Links + 3; link++)
        {
            builder.WithObject(link, string.Create(CultureInfo.InvariantCulture, $"<< /Next {link + 1} 0 R /Pad ({pad}) >>"));
        }

        builder.WithObject(Links + 3, "null");
        var source = new SamplingSource(builder.BuildClassic(rootNumber: 1));
        using var document = PdfDocument.Open(source, PdfReaderOptions.Default with { ObjectCacheCapacity = 8 }, ownsSource: true);
        var pages = Validation.PageTreeWalk.Run(document);
        var before = GC.GetTotalMemory(forceFullCollection: true);
        source.Sampling = true;

        Validation.ObjectGraph.Run(document, pages);

        source.Sampling = false;
        var held = source.MostLive - before;
        held.Should().BeLessThan(20L * 1024 * 1024, $"memory was sampled {source.Samples} times");
    }

    [Fact]
    public void The_walk_of_the_object_streams_dependencies_holds_what_the_reader_keeps_whatever_it_reaches()
    {
        // An object stream takes its /DecodeParms from object 3, an array of references to 2,000 arrays, each of a thousand
        // references, opened with a cache of 64 objects. The walk kept the references of every object it read, to read each
        // once: 16 MB, growing with what the decoding keys reach. It keeps as many objects as the reader keeps in its cache.
        const int Arrays = 2000;
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [5] = (1, file.ObjectStream(5, [(6, "<< >>")], "/DecodeParms 3 0 R "), 0),
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
