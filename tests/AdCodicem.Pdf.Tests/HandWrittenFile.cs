using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Writes a PDF object by object, giving where each starts, so that a test can write rows that place an object where it
/// is not, headers a rebuild does not find, and line endings or bytes before the header that <see cref="PdfTemplate"/>
/// does not write. Offsets are counted from the header, as rows count them.
/// </summary>
internal sealed class HandWrittenFile
{
    private readonly StringBuilder _text = new();
    private readonly int _prefix;
    private readonly string _eol;

    /// <summary>Starts a file with <paramref name="prefix"/> before its header, its lines ending with <paramref name="eol"/>.</summary>
    public HandWrittenFile(string prefix = "", string version = "1.7", string eol = "\n")
    {
        _prefix = prefix.Length;
        _eol = eol;
        _text.Append(prefix).Append("%PDF-").Append(version).Append(eol);
    }

    /// <summary>Gets where the next byte written starts, counted from the header.</summary>
    public long Position => _text.Length - _prefix;

    /// <summary>Gets the file written.</summary>
    public byte[] Bytes => Encoding.Latin1.GetBytes(_text.ToString());

    /// <summary>Writes object <paramref name="number"/>, and gives where its header starts.</summary>
    public long Object(int number, string body, int generation = 0)
    {
        var at = Position;
        _text.Append(CultureInfo.InvariantCulture, $"{number} {generation} obj{_eol}{body}{_eol}endobj{_eol}");
        return at;
    }

    /// <summary>Writes object <paramref name="number"/> as a string of <paramref name="length"/> bytes, which no reference names.</summary>
    public long Padding(int number, int length = 1200) => Object(number, "(" + new string('x', length) + ")");

    /// <summary>Writes <paramref name="text"/> as it is.</summary>
    public void Raw(string text) => _text.Append(text);

    /// <summary>
    /// Writes a classic table of the numbers below <paramref name="size"/>, one row each from <paramref name="rows"/> —
    /// a number it lacks is free —, its trailer holding <paramref name="trailer"/>, and the <c>startxref</c> naming it.
    /// </summary>
    public long Table(int size, Dictionary<int, long> rows, string trailer)
    {
        var at = Position;
        var eol = _eol == "\n" ? " \n" : "\r\n";
        _text.Append("xref").Append(_eol).Append(CultureInfo.InvariantCulture, $"0 {size}").Append(_eol);

        for (var number = 0; number < size; number++)
        {
            if (number == 0)
            {
                _text.Append("0000000000 65535 f").Append(eol);
            }
            else if (rows.TryGetValue(number, out var offset))
            {
                _text.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n").Append(eol);
            }
            else
            {
                _text.Append("0000000000 00000 f").Append(eol);
            }
        }

        _text.Append("trailer").Append(_eol).Append("<< ").Append(trailer).Append(" >>").Append(_eol);
        _text.Append("startxref").Append(_eol).Append(at.ToString(CultureInfo.InvariantCulture)).Append(_eol).Append("%%EOF").Append(_eol);
        return at;
    }

    /// <summary>
    /// Writes object stream <paramref name="number"/>, holding <paramref name="members"/> uncompressed, its dictionary
    /// holding <paramref name="entries"/> besides its own, and gives where its header starts.
    /// </summary>
    public long ObjectStream(int number, (int Number, string Body)[] members, string entries = "")
    {
        var header = new StringBuilder();
        var contents = new StringBuilder();

        foreach (var (member, body) in members)
        {
            header.Append(CultureInfo.InvariantCulture, $"{member} {contents.Length} ");
            contents.Append(body).Append(' ');
        }

        var data = header.ToString() + contents;
        return Object(
            number,
            string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Type /ObjStm /N {members.Length} /First {header.Length} /Length {data.Length} {entries}>>{_eol}stream{_eol}{data}{_eol}endstream"));
    }

    /// <summary>
    /// Writes object stream <paramref name="number"/>, Flate-compressed, holding <paramref name="members"/> after spaces
    /// between its header and its first object that make its data decode to <paramref name="decoded"/> bytes, and gives
    /// where its header starts.
    /// </summary>
    public long PaddedObjectStream(int number, (int Number, string Body)[] members, int decoded)
    {
        var header = new StringBuilder();
        var contents = new StringBuilder();

        foreach (var (member, body) in members)
        {
            header.Append(CultureInfo.InvariantCulture, $"{member} {contents.Length} ");
            contents.Append(body).Append(' ');
        }

        var first = Math.Max(header.Length, decoded - contents.Length);
        var data = Deflate(Encoding.Latin1.GetBytes(header.ToString().PadRight(first) + contents));
        return Object(
            number,
            string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Type /ObjStm /N {members.Length} /First {first} /Filter /FlateDecode /Length {data.Length} >>{_eol}stream{_eol}{Encoding.Latin1.GetString(data)}{_eol}endstream"));
    }

    /// <summary>
    /// Writes cross-reference stream <paramref name="number"/> of the numbers below <paramref name="size"/>, <c>/W [1 4 2]</c>,
    /// Flate-compressed, one row each from <paramref name="row"/> — its own and that of object 0 aside —, its dictionary
    /// holding <paramref name="entries"/>, and the <c>startxref</c> naming it.
    /// </summary>
    public long CompressedCrossReferenceStream(int number, int size, Func<int, (int Type, long Field2, int Field3)> row, string entries)
    {
        var at = Position;
        var rows = new byte[size * 7];

        for (var each = 0; each < size; each++)
        {
            var (type, field2, field3) = each == number ? (1, at, 0) : each == 0 ? (0, 0L, 65535) : row(each);
            var slot = rows.AsSpan(each * 7, 7);
            slot[0] = (byte)type;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(slot[1..], (uint)field2);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(slot[5..], (ushort)field3);
        }

        var data = Deflate(rows);
        Object(
            number,
            string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Type /XRef /Size {size} /W [1 4 2] /Filter /FlateDecode /Length {data.Length} {entries} >>{_eol}stream{_eol}{Encoding.Latin1.GetString(data)}{_eol}endstream"));
        _text.Append("startxref").Append(_eol).Append(at.ToString(CultureInfo.InvariantCulture)).Append(_eol).Append("%%EOF").Append(_eol);
        return at;
    }

    /// <summary>
    /// Writes cross-reference stream <paramref name="number"/> of the numbers below <paramref name="size"/>, <c>/W [1 4 2]</c>,
    /// one row each from <paramref name="rows"/> — its own aside, a number it lacks is free —, its dictionary holding
    /// <paramref name="entries"/> and, unless <paramref name="length"/> says not to, the <c>/Length</c> of its data, and the
    /// <c>startxref</c> naming it. Its header is written <paramref name="header"/> where one is given, <c>N 0 obj</c> otherwise.
    /// </summary>
    public long CrossReferenceStream(
        int number, int size, Dictionary<int, (int Type, long Field2, int Field3)> rows, string entries, bool length = true, string? header = null)
    {
        var at = Position;
        var data = new StringBuilder();

        for (var row = 0; row < size; row++)
        {
            var (type, field2, field3) = row == number ? (1, at, 0)
                : row == 0 ? (0, 0L, 65535)
                : rows.TryGetValue(row, out var given) ? given : (0, 0L, 0);
            data.Append((char)type);
            data.Append((char)((field2 >> 24) & 0xFF)).Append((char)((field2 >> 16) & 0xFF))
                .Append((char)((field2 >> 8) & 0xFF)).Append((char)(field2 & 0xFF));
            data.Append((char)((field3 >> 8) & 0xFF)).Append((char)(field3 & 0xFF));
        }

        var body = string.Create(
            CultureInfo.InvariantCulture,
            $"<< /Type /XRef /Size {size} /W [1 4 2] {entries}{(length ? $" /Length {data.Length}" : "")} >>{_eol}stream{_eol}{data}{_eol}endstream");

        if (header is null)
        {
            Object(number, body);
        }
        else
        {
            _text.Append(header).Append(_eol).Append(body).Append(_eol).Append("endobj").Append(_eol);
        }

        _text.Append("startxref").Append(_eol).Append(at.ToString(CultureInfo.InvariantCulture)).Append(_eol).Append("%%EOF").Append(_eol);
        return at;
    }

    /// <summary>Compresses <paramref name="data"/> with zlib, as a <c>/FlateDecode</c> stream holds it.</summary>
    private static byte[] Deflate(byte[] data)
    {
        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return compressed.ToArray();
    }
}
