using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures what reading every member of one object stream costs: its header read once, and each member parsed through
/// the window that ends at the next member's start (#160, #200).
/// </summary>
/// <remarks>
/// The object stream holds small dictionaries, unfiltered, so that what is measured is the header and the members, not a
/// filter. With a wrong index, every entry gives the index of the member after its own, and each member is found by the
/// lookup by number the stream builds once, sorted. The document is opened inside the measurement: its object cache would
/// otherwise keep the members of the smaller stream between iterations.
/// </remarks>
[MemoryDiagnoser]
public class ObjectStreamMemberBenchmarks
{
    private const int FirstMember = 4;

    private byte[] _document = [];

    [Params(1000, 100_000)]
    public int MemberCount { get; set; }

    [Params(false, true)]
    public bool WrongIndex { get; set; }

    [GlobalSetup]
    public void Setup() => _document = Create(MemberCount, WrongIndex);

    [Benchmark(Description = "Index, then read every member of an object stream")]
    public int ReadEveryMember()
    {
        using var document = PdfDocument.Open(_document);
        var read = 0;

        for (var number = FirstMember; number < FirstMember + MemberCount; number++)
        {
            if (document.GetObject(new PdfObjectId(number)) is PdfDictionary)
            {
                read++;
            }
        }

        return read;
    }

    /// <summary>
    /// Writes a file indexed by a cross-reference stream whose object stream 3 holds <paramref name="members"/> dictionaries,
    /// numbered from <see cref="FirstMember"/>; with <paramref name="wrongIndex"/>, each entry gives the index of the member
    /// after its own.
    /// </summary>
    private static byte[] Create(int members, bool wrongIndex)
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

        var header = new StringBuilder();
        var body = new StringBuilder();

        for (var index = 0; index < members; index++)
        {
            header.Append(CultureInfo.InvariantCulture, $"{FirstMember + index} {body.Length} ");
            body.Append(CultureInfo.InvariantCulture, $"<< /Kind /Member /Index {index} /Values [1 2 3 4] >>\n");
        }

        var data = header.ToString() + body;
        WriteObject(3, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N {members} /First {header.Length} /Length {data.Length} >>\nstream\n{data}\nendstream"));

        var xref = FirstMember + members;
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
            else if (number >= FirstMember)
            {
                var index = number - FirstMember;
                row[0] = 2;
                Put(row[1..5], 3);
                Put(row[5..9], wrongIndex ? (index + 1) % members : index);
            }
        }

        Write(string.Create(CultureInfo.InvariantCulture, $"{xref} 0 obj\n<< /Type /XRef /Size {xref + 1} /W [1 4 4] /Root 1 0 R /Length {rows.Length} >>\nstream\n"));
        output.Write(rows);
        Write(string.Create(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF\n"));
        return output.ToArray();
    }

    private static void Put(Span<byte> field, long value)
    {
        for (var i = field.Length - 1; i >= 0; i--)
        {
            field[i] = (byte)value;
            value >>= 8;
        }
    }
}
