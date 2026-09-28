using System.Globalization;
using System.Text;

namespace AdCodicem.Pdf.TestSupport;

/// <summary>
/// Assembles small PDF files with exact byte offsets, so the reader can be tested against every shape of
/// cross-reference the format allows — and against files that have been damaged on purpose.
/// </summary>
public sealed class TestPdfBuilder
{
    private readonly List<(int Number, byte[] Body)> _objects = [];

    public TestPdfBuilder WithObject(int number, string body)
    {
        _objects.Add((number, Encoding.ASCII.GetBytes(body)));
        return this;
    }

    public TestPdfBuilder Stream(int number, string dictionaryEntries, string data)
    {
        var body = $"<< {dictionaryEntries} /Length {data.Length} >>\nstream\n{data}\nendstream";
        return WithObject(number, body);
    }

    /// <summary>Writes a file with a classic cross-reference table.</summary>
    public byte[] BuildClassic(int rootNumber, long offsetError = 0, bool includeXRef = true)
    {
        using var writer = new Writer();
        writer.WriteHeader("1.7");

        var offsets = WriteObjects(writer);
        var size = MaxNumber() + 1;

        if (!includeXRef)
        {
            // A file whose index was never written: the reader has to find everything by scanning.
            writer.WriteLine("%%EOF");
            return writer.ToArray();
        }

        var xrefOffset = writer.Position;
        writer.WriteXRefTable(offsets, size, offsetError);
        writer.WriteLine("trailer");
        writer.WriteLine($"<< /Size {size} /Root {rootNumber} 0 R >>");
        writer.WriteStartXRef(xrefOffset);
        return writer.ToArray();
    }

    /// <summary>Writes a file whose index is a cross-reference stream, optionally with an object stream.</summary>
    /// <param name="rootNumber">The catalog's object number.</param>
    /// <param name="compressedObjects">The objects packed in the object stream, in order.</param>
    /// <param name="objectStreamHeader">Rewrites the object stream's header — <c>2 0 3 57 </c> — before its /First is worked out.</param>
    /// <param name="compressObjectStream">Writes the object stream's data Flate-compressed.</param>
    /// <param name="compressXRefStream">Writes the cross-reference stream's rows Flate-compressed.</param>
    /// <param name="objectStreamEntries">Entries added to the object stream's dictionary, such as <c>/DecodeParms 5 0 R</c>.</param>
    public byte[] BuildWithXRefStream(
        int rootNumber,
        int[]? compressedObjects = null,
        Func<string, string>? objectStreamHeader = null,
        bool compressObjectStream = false,
        bool compressXRefStream = false,
        string? objectStreamEntries = null)
    {
        using var writer = new Writer();
        writer.WriteHeader("1.5");

        compressedObjects ??= [];
        var packed = _objects.Where(o => compressedObjects.Contains(o.Number)).ToList();
        var direct = _objects.Where(o => !compressedObjects.Contains(o.Number)).ToList();

        var offsets = new Dictionary<int, long>();
        foreach (var (number, body) in direct)
        {
            offsets[number] = writer.Position;
            writer.WriteObject(number, body);
        }

        var objectStreamNumber = MaxNumber() + 1;
        var xrefNumber = objectStreamNumber + 1;
        var indexInStream = new Dictionary<int, int>();

        if (packed.Count > 0)
        {
            var header = new StringBuilder();
            var contents = new StringBuilder();

            for (var i = 0; i < packed.Count; i++)
            {
                indexInStream[packed[i].Number] = i;
                header.Append(packed[i].Number).Append(' ').Append(contents.Length).Append(' ');
                contents.Append(Encoding.ASCII.GetString(packed[i].Body)).Append('\n');
            }

            var headerText = objectStreamHeader?.Invoke(header.ToString()) ?? header.ToString();
            var data = Encoding.ASCII.GetBytes(headerText + contents);
            offsets[objectStreamNumber] = writer.Position;
            writer.WriteObject(
                objectStreamNumber,
                StreamBody($"/Type /ObjStm /N {packed.Count} /First {headerText.Length} {objectStreamEntries}".TrimEnd(), data, compressObjectStream));
        }

        var size = xrefNumber + 1;
        var xrefOffset = writer.Position;
        var rows = new List<byte>();

        for (var number = 0; number < size; number++)
        {
            if (number == 0)
            {
                rows.AddRange(Row(0, 0, 65535));
            }
            else if (offsets.TryGetValue(number, out var offset))
            {
                rows.AddRange(Row(1, (uint)offset, 0));
            }
            else if (indexInStream.TryGetValue(number, out var index))
            {
                rows.AddRange(Row(2, (uint)objectStreamNumber, (ushort)index));
            }
            else if (number == xrefNumber)
            {
                rows.AddRange(Row(1, (uint)xrefOffset, 0));
            }
            else
            {
                rows.AddRange(Row(0, 0, 0));
            }
        }

        writer.WriteObject(
            xrefNumber,
            StreamBody($"/Type /XRef /Size {size} /W [1 4 2] /Root {rootNumber} 0 R", [.. rows], compressXRefStream));

        writer.WriteStartXRef(xrefOffset);
        return writer.ToArray();

        static byte[] StreamBody(string entries, byte[] data, bool compress)
        {
            if (compress)
            {
                using var compressed = new MemoryStream();
                using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal))
                {
                    zlib.Write(data);
                }

                data = compressed.ToArray();
                entries += " /Filter /FlateDecode";
            }

            return [.. Encoding.Latin1.GetBytes($"<< {entries} /Length {data.Length} >>\nstream\n"), .. data, .. "\nendstream"u8];
        }

        static byte[] Row(byte type, uint field2, ushort field3) =>
        [
            type,
            (byte)(field2 >> 24), (byte)(field2 >> 16), (byte)(field2 >> 8), (byte)field2,
            (byte)(field3 >> 8), (byte)field3,
        ];
    }

    /// <summary>Appends an incremental update that redefines objects, as a real editor would.</summary>
    public static byte[] AppendIncrementalUpdate(
        byte[] original,
        int rootNumber,
        (int Number, string Body)[] updates,
        bool pointPreviousAtSelf = false)
    {
        using var writer = new Writer();
        writer.WriteRaw(original);

        var previousStartXRef = FindStartXRef(original);
        var offsets = new Dictionary<int, long>();

        foreach (var (number, body) in updates)
        {
            offsets[number] = writer.Position;
            writer.WriteObject(number, Encoding.ASCII.GetBytes(body));
        }

        var xrefOffset = writer.Position;
        writer.WriteLine("xref");

        foreach (var (number, offset) in offsets.OrderBy(pair => pair.Key))
        {
            writer.WriteLine($"{number} 1");
            writer.WriteRaw(Encoding.ASCII.GetBytes($"{offset:D10} {0:D5} n \r\n"));
        }

        writer.WriteLine("trailer");
        var previous = pointPreviousAtSelf ? xrefOffset : previousStartXRef;
        var size = Math.Max(FindSize(original), updates.Max(update => update.Number) + 1);
        writer.WriteLine($"<< /Size {size} /Root {rootNumber} 0 R /Prev {previous} >>");
        writer.WriteStartXRef(xrefOffset);
        return writer.ToArray();
    }

    /// <summary>The <c>/Size</c> the newest trailer gives, which an update counts on from.</summary>
    private static int FindSize(byte[] data)
    {
        var text = Encoding.Latin1.GetString(data);
        var index = text.LastIndexOf("/Size ", StringComparison.Ordinal);
        var rest = text[(index + "/Size ".Length)..];
        var end = rest.IndexOfAny([' ', '/', '>', '\r', '\n']);
        return int.Parse(end < 0 ? rest : rest[..end], CultureInfo.InvariantCulture);
    }

    private static long FindStartXRef(byte[] data)
    {
        var text = Encoding.Latin1.GetString(data);
        var index = text.LastIndexOf("startxref", StringComparison.Ordinal);
        var rest = text[(index + "startxref".Length)..].Trim();
        var end = rest.IndexOfAny([' ', '\r', '\n']);
        return long.Parse(end < 0 ? rest : rest[..end], CultureInfo.InvariantCulture);
    }

    private Dictionary<int, long> WriteObjects(Writer writer)
    {
        var offsets = new Dictionary<int, long>();

        foreach (var (number, body) in _objects)
        {
            offsets[number] = writer.Position;
            writer.WriteObject(number, body);
        }

        return offsets;
    }

    private int MaxNumber() => _objects.Count == 0 ? 0 : _objects.Max(o => o.Number);

    private sealed class Writer : IDisposable
    {
        private readonly MemoryStream _stream = new();

        public long Position => _stream.Position;

        public void WriteHeader(string version)
        {
            WriteLine($"%PDF-{version}");
            WriteRaw([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);
        }

        public void WriteObject(int number, byte[] body)
        {
            WriteLine($"{number} 0 obj");
            WriteRaw(body);
            WriteRaw("\nendobj\n"u8.ToArray());
        }

        public void WriteXRefTable(Dictionary<int, long> offsets, int size, long offsetError)
        {
            WriteLine("xref");
            WriteLine($"0 {size}");
            WriteRaw(Encoding.ASCII.GetBytes($"{0:D10} {65535:D5} f \r\n"));

            for (var number = 1; number < size; number++)
            {
                var offset = offsets.TryGetValue(number, out var value) ? value + offsetError : 0;
                var kind = offsets.ContainsKey(number) ? 'n' : 'f';
                WriteRaw(Encoding.ASCII.GetBytes($"{offset:D10} {0:D5} {kind} \r\n"));
            }
        }

        public void WriteStartXRef(long xrefOffset)
        {
            WriteLine("startxref");
            WriteLine(xrefOffset.ToString(CultureInfo.InvariantCulture));
            WriteLine("%%EOF");
        }

        public void WriteLine(string text)
        {
            WriteRaw(Encoding.Latin1.GetBytes(text));
            _stream.WriteByte((byte)'\n');
        }

        public void WriteRaw(byte[] data) => _stream.Write(data, 0, data.Length);

        public byte[] ToArray() => _stream.ToArray();

        public void Dispose() => _stream.Dispose();
    }
}
