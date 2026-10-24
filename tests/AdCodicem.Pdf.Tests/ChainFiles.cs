using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Files whose cross-reference chain refers to objects for what it needs to read itself — a cross-reference stream's
/// dictionary written with references, an indirect <c>/Length</c>, a <c>/Prev</c> or an <c>/XRefStm</c> written as a
/// reference —, which the reader reads without loading anything while the chain is read (#182).
/// </summary>
internal static class ChainFiles
{
    /// <summary>The keys <see cref="UnderAnUpdate"/> writes as references, one shape each.</summary>
    public static readonly TheoryData<string> Keys =
        ["Type", "W", "W element", "Index", "Index element", "Size", "Filter", "DecodeParms", "Columns", "Length"];

    /// <summary>
    /// A file of two revisions. The first holds the catalog, the page tree and the page, objects 1 to 3, object 5, and
    /// cross-reference stream 7, which indexes objects 0 to 7 in rows of 7 bytes and writes <paramref name="key"/> as a
    /// reference to object 5, which holds the value. The second is a classic table that indexes object 5 again — where it
    /// is when <paramref name="placed"/>, at offset 99999, outside the file, otherwise — and names stream 7 through
    /// <c>/Prev</c>. Object 5's value is followed by <paramref name="padding"/> spaces. With <paramref name="reals"/>, the
    /// stream's <c>/Length</c> and the integers object 5 holds are written as reals with no fractional part. A comment
    /// keeps the newer table out of reach of the search near stream 7, which would take it for the stream (#188).
    /// </summary>
    public static byte[] UnderAnUpdate(string key, bool placed, string version = "1.5", int padding = 0, bool reals = false)
    {
        var (entries, five, rows) = key switch
        {
            "Type" => ("/Type 5 0 R /Size 8 /W [1 4 2] /Length {length}", "/XRef", Rows.Raw),
            "W" => ("/Type /XRef /Size 8 /W 5 0 R /Length {length}", "[1 4 2]", Rows.Raw),
            "W element" => ("/Type /XRef /Size 8 /W [1 5 0 R 2] /Length {length}", "4", Rows.Raw),
            "Index" => ("/Type /XRef /Size 8 /W [1 4 2] /Index 5 0 R /Length {length}", "[0 8]", Rows.Raw),
            "Index element" => ("/Type /XRef /Size 8 /W [1 4 2] /Index [0 5 0 R] /Length {length}", "8", Rows.Raw),
            "Size" => ("/Type /XRef /Size 5 0 R /W [1 4 2] /Length {length}", "8", Rows.Raw),
            "Filter" => ("/Type /XRef /Size 8 /W [1 4 2] /Filter 5 0 R /Length {length}", "/FlateDecode", Rows.Flate),
            "DecodeParms" => (
                "/Type /XRef /Size 8 /W [1 4 2] /Filter /FlateDecode /DecodeParms 5 0 R /Length {length}",
                "<< /Predictor 12 /Columns 7 >>",
                Rows.PngFlate),
            "Columns" => (
                "/Type /XRef /Size 8 /W [1 4 2] /Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns 5 0 R >> /Length {length}",
                "7",
                Rows.PngFlate),
            "Length" => ("/Type /XRef /Size 8 /W [1 4 2] /Length 5 0 R", "56", Rows.Raw),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "No such shape."),
        };

        if (reals)
        {
            entries = entries.Replace("/Length {length}", "/Length {length}.0", StringComparison.Ordinal);
            five = five.Replace("/Predictor 12 /Columns 7", "/Predictor 12.0 /Columns 7.0", StringComparison.Ordinal);
        }

        return UnderAnUpdate(entries, five + new string(' ', padding), rows != Rows.Raw, rows == Rows.PngFlate, placed, version: version);
    }

    /// <summary>
    /// A file as the other <see cref="UnderAnUpdate(string, bool, string, int, bool)"/> writes it: stream 7's dictionary
    /// holds <paramref name="entries"/>, <c>{length}</c> standing for its data's length, and object 5 holds
    /// <paramref name="five"/>. Object 6, when <paramref name="six"/> gives it, is indexed by stream 7 alone; the objects
    /// of <paramref name="placedToo"/> by the newer table alone.
    /// </summary>
    public static byte[] UnderAnUpdate(
        string entries,
        string five,
        bool compressed,
        bool predicted,
        bool placed,
        string? six = null,
        IReadOnlyDictionary<int, string>? placedToo = null,
        string version = "1.5")
    {
        using var file = new Writer(version);
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        file.Object(5, five);

        if (six is not null)
        {
            file.Object(6, six);
        }

        foreach (var (number, body) in placedToo ?? new Dictionary<int, string>())
        {
            file.Object(number, body);
        }

        var stream = file.Position;
        var data = Encode(
            [
                (0, 0, 65535), (1, file.OffsetOf(1), 0), (1, file.OffsetOf(2), 0), (1, file.OffsetOf(3), 0), (0, 0, 0),
                (1, file.OffsetOf(5), 0), six is null ? (0, 0, 0) : (1, file.OffsetOf(6), 0), (1, stream, 0),
            ],
            predicted ? Rows.PngFlate : compressed ? Rows.Flate : Rows.Raw);
        file.Stream(7, entries.Replace("{length}", data.Length.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) + " /Root 1 0 R", data);
        file.Text("%" + new string('-', 1200) + "\n");
        var xref = file.Position;
        var rows = new StringBuilder(string.Create(
            CultureInfo.InvariantCulture, $"xref\n0 1\n0000000000 65535 f\r\n5 1\n{(placed ? file.OffsetOf(5) : 99999):D10} 00000 n\r\n"));
        var size = 8;

        foreach (var number in (placedToo ?? new Dictionary<int, string>()).Keys)
        {
            rows.Append(CultureInfo.InvariantCulture, $"{number} 1\n{file.OffsetOf(number):D10} 00000 n\r\n");
            size = Math.Max(size, number + 1);
        }

        file.Text(rows.ToString());
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"trailer\n<< /Size {size} /Root 1 0 R /Prev {stream} >>\nstartxref\n{xref}\n%%EOF\n"));
        return file.ToArray();
    }

    /// <summary>
    /// A file of one cross-reference stream, object 5, whose <c>/Length</c> names an object which only that stream
    /// indexes — object 4, or with <paramref name="lengthLast"/> the one its last row places, object 4 then free —: valid,
    /// since ISO 32000-1 lets a stream's <c>/Length</c> be indirect, and 7.5.8.2 does not except a cross-reference stream's.
    /// The length gives the data's length, plus <paramref name="lengthOff"/>. After the rows of objects 0 to 5 come
    /// <paramref name="padding"/> free rows; with <paramref name="endingInCarriageReturn"/>, a row that gives the next
    /// object free with generation 13, so that it ends in a carriage return; then, with <paramref name="spellingEndStream"/>,
    /// two rows of reserved types that spell <c>endstream</c> after an end-of-line, or with
    /// <paramref name="spellingHeader"/> an object header. The rows are compressed when <paramref name="compressed"/>, the
    /// data loses its last <paramref name="dropped"/> bytes, and it ends with <paramref name="endOfLine"/> before its
    /// <c>endstream</c>, which its <c>endobj</c> and the <c>startxref</c> follow. The object that holds the length holds
    /// <paramref name="lengthPadding"/> spaces before it.
    /// </summary>
    public static byte[] AloneWithIndirectLength(
        int lengthOff = 0,
        bool spellingEndStream = false,
        bool compressed = false,
        bool endingInCarriageReturn = false,
        string endOfLine = "\n",
        int padding = 0,
        bool lengthLast = false,
        bool spellingHeader = false,
        int dropped = 0,
        int lengthPadding = 0)
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        var spelled = spellingEndStream ? "\nendstream\0\0\0\0"u8.ToArray() : spellingHeader ? "\n1 0 obj\0\0\0\0\0\0"u8.ToArray() : [];
        var size = 6 + padding + (endingInCarriageReturn ? 1 : 0) + (spelled.Length / 7) + (lengthLast ? 1 : 0);
        var lengthNumber = lengthLast ? size - 1 : 4;
        var encoding = compressed ? Rows.Flate : Rows.Raw;

        // Uncompressed rows, or stored in blocks of their own, take as many bytes whatever they hold.
        var length = Data(0, 0).Length + lengthOff;
        file.Object(lengthNumber, new string(' ', lengthPadding) + length.ToString(CultureInfo.InvariantCulture));
        var stream = file.Position;
        var filter = compressed ? " /Filter /FlateDecode" : string.Empty;
        file.Stream(
            5,
            string.Create(CultureInfo.InvariantCulture, $"/Type /XRef /Size {size} /W [1 4 2]{filter} /Root 1 0 R /Length {lengthNumber} 0 R"),
            Data(file.OffsetOf(lengthNumber), stream),
            endOfLine);
        file.StartXRef(stream);
        return file.ToArray();

        byte[] Data(long lengthAt, long streamAt)
        {
            var rows = new List<(int Type, long Second, int Third)>
            {
                (0, 0, 65535), (1, file.OffsetOf(1), 0), (1, file.OffsetOf(2), 0), (1, file.OffsetOf(3), 0),
                lengthLast ? (0, 0, 0) : (1, lengthAt, 0), (1, streamAt, 0),
            };

            rows.AddRange(Enumerable.Repeat((0, 0L, 0), padding));

            if (endingInCarriageReturn)
            {
                rows.Add((0, 0, 13));
            }

            // Types 10 and 101 or 106, reserved: the rows are ignored, and their bytes read "\nendstream", or "\n1 0 obj". The
            // length's row past them is written as they are, uncompressed.
            var data = new List<byte>(Encode([.. rows], encoding));
            data.AddRange(spelled);

            if (lengthLast)
            {
                data.AddRange(Encode([(1, lengthAt, 0)], Rows.Raw));
            }

            data.RemoveRange(data.Count - dropped, dropped);
            return [.. data];
        }
    }

    /// <summary>
    /// A file of one cross-reference stream, object 5, whose <c>/Length</c> names object 4, which only that stream indexes,
    /// in a row before two rows of reserved types that spell <c>endstream</c> after an end-of-line; the page's row comes
    /// after them. With <paramref name="reservedRowFirst"/>, the <c>/Index</c> numbers object 4 twice: first in a row of a
    /// reserved type, which the specification says to ignore, then in the row that places it. With <paramref name="stored"/>,
    /// the data is compressed in stored blocks, which show the rows as written, <paramref name="layers"/> times over, each
    /// layer a Flate filter. The file is valid.
    /// </summary>
    public static byte[] AloneWithIndirectLengthBeforeSpelledRows(bool reservedRowFirst, bool stored = false, int layers = 1)
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        var reserved = reservedRowFirst ? [(3, 0L, 0)] : Array.Empty<(int, long, int)>();

        // The data's length does not depend on the offsets its rows hold.
        byte[] Data(long four, long stream)
        {
            var data = new List<byte>(Encode([(0, 0, 65535), (1, file.OffsetOf(1), 0), (1, file.OffsetOf(2), 0), .. reserved, (1, four, 0), (1, stream, 0)], Rows.Raw));
            data.AddRange("\nendstream\0\0\0\0"u8.ToArray());
            data.AddRange(Encode([(1, file.OffsetOf(3), 0)], Rows.Raw));
            byte[] bytes = [.. data];

            for (var layer = 0; stored && layer < layers; layer++)
            {
                bytes = Compress(bytes);
            }

            return bytes;
        }

        file.Object(4, Data(0, 0).Length.ToString(CultureInfo.InvariantCulture));
        var stream = file.Position;
        var index = reservedRowFirst ? "0 3 4 1 4 2 6 2 3 1" : "0 3 4 2 6 2 3 1";
        var filter = stored ? " /Filter [" + string.Join(' ', Enumerable.Repeat("/FlateDecode", layers)) + "]" : string.Empty;
        file.Stream(5, $"/Type /XRef /Size 8 /W [1 4 2] /Index [{index}]{filter} /Root 1 0 R /Length 4 0 R", Data(file.OffsetOf(4), stream));
        file.StartXRef(stream);
        return file.ToArray();
    }

    /// <summary>
    /// A file of one cross-reference stream, object 5, whose rows have no type field — <c>/W [0 4 2]</c>, every row of type 1
    /// —, whose <c>/Length</c> names object 4, which only that stream indexes, and whose rows of objects 6 and 7 spell
    /// <c>endstream</c> after an end-of-line, before the page's row. The file is valid.
    /// </summary>
    public static byte[] AloneWithIndirectLengthAndNoTypeField()
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        file.Object(4, "42");
        var stream = file.Position;
        var data = new List<byte>();

        foreach (var offset in new[] { file.OffsetOf(1), file.OffsetOf(2), file.OffsetOf(4), stream })
        {
            data.AddRange([(byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset, 0, 0]);
        }

        data.AddRange("\nendstream\0\0"u8.ToArray());
        var page = file.OffsetOf(3);
        data.AddRange([(byte)(page >> 24), (byte)(page >> 16), (byte)(page >> 8), (byte)page, 0, 0]);
        file.Stream(5, "/Type /XRef /Size 8 /W [0 4 2] /Index [1 2 4 2 6 2 3 1] /Root 1 0 R /Length 4 0 R", [.. data]);
        file.StartXRef(stream);
        return file.ToArray();
    }

    /// <summary>
    /// A file of two revisions. The first writes object 8 as <c>(old)</c> and indexes it in cross-reference stream 7. The
    /// second writes it again as <c>(new)</c>, and indexes it in cross-reference stream 10, whose <c>/Length</c> names
    /// object 4, which only that stream indexes, and whose rows of objects 5 and 6, of reserved types, spell
    /// <c>endstream</c> after an end-of-line when <paramref name="spellingEndStream"/>. With
    /// <paramref name="olderIndirect"/>, stream 7's <c>/Length</c> names object 9, which only stream 7 indexes, and a comment
    /// sets the update further from it than a search for a section's misplaced offset reaches. Object 4 holds
    /// <paramref name="four"/>, in which <c>{length}</c> stands for the length. The file is valid. With
    /// <paramref name="nine"/>, the update writes object 9 again, holding it, and stream 10 indexes it there.
    /// </summary>
    public static byte[] UpdateWithIndirectLength(
        bool spellingEndStream,
        bool olderIndirect = false,
        string four = "{length}",
        string? nine = null)
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        file.Object(8, "(old)");
        var olderRows = olderIndirect ? 10 : 9;

        if (olderIndirect)
        {
            file.Object(9, (olderRows * 7).ToString(CultureInfo.InvariantCulture));
        }

        var first = file.Position;
        (int Type, long Second, int Third)[] older =
            [(0, 0, 65535), (1, file.OffsetOf(1), 0), (1, file.OffsetOf(2), 0), (1, file.OffsetOf(3), 0), (0, 0, 0), (0, 0, 0), (0, 0, 0), (1, first, 0), (1, file.OffsetOf(8), 0)];
        var rows = Encode(olderIndirect ? [.. older, (1, file.OffsetOf(9), 0)] : older, Rows.Raw);
        var olderLength = olderIndirect ? "9 0 R" : rows.Length.ToString(CultureInfo.InvariantCulture);
        file.Stream(7, string.Create(CultureInfo.InvariantCulture, $"/Type /XRef /Size {olderRows} /W [1 4 2] /Root 1 0 R /Length {olderLength}"), rows);
        file.StartXRef(first);

        if (olderIndirect)
        {
            file.Text("%" + new string('x', 2 * AdCodicem.Pdf.IO.PdfFileReader.NearbySearchRadius) + "\n");
        }

        file.Object(8, "(new)");

        if (nine is not null)
        {
            file.Object(9, nine);
        }

        var spelled = spellingEndStream ? "\nendstream\0\0\0\0"u8.ToArray() : Encode([(0, 0, 0), (0, 0, 0)], Rows.Raw);
        var after = nine is null ? 14 : 21;
        file.Object(4, four.Replace("{length}", (7 + spelled.Length + after).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        var second = file.Position;
        var data = new List<byte>(Encode([(1, file.OffsetOf(4), 0)], Rows.Raw));
        data.AddRange(spelled);
        data.AddRange(Encode(nine is null ? [(1, file.OffsetOf(8), 0), (1, second, 0)] : [(1, file.OffsetOf(8), 0), (1, file.OffsetOf(9), 0), (1, second, 0)], Rows.Raw));
        var index = nine is null ? "4 3 8 1 10 1" : "4 3 8 3";
        file.Stream(
            10,
            string.Create(CultureInfo.InvariantCulture, $"/Type /XRef /Size 11 /W [1 4 2] /Index [{index}] /Root 1 0 R /Prev {first} /Length 4 0 R"),
            [.. data]);
        file.StartXRef(second);
        return file.ToArray();
    }

    /// <summary>
    /// A file of two revisions. The first writes object 8 as <c>(old)</c> and indexes it in cross-reference stream 7, whose
    /// <c>/Length</c> names object 4, which only that stream indexes; its rows of objects 5 and 6, of reserved types, spell
    /// <c>endstream</c> after an end-of-line when <paramref name="spellingEndStream"/>. The second, set further from it than a
    /// search for a section's misplaced offset reaches, writes object 8 again as <c>(new)</c> and indexes it in
    /// cross-reference stream 9. The file is valid.
    /// </summary>
    public static byte[] OlderWithIndirectLength(bool spellingEndStream)
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        file.Object(8, "(old)");
        file.Object(4, "63");
        var first = file.Position;
        var data = new List<byte>(Encode([(0, 0, 65535), (1, file.OffsetOf(1), 0), (1, file.OffsetOf(2), 0), (1, file.OffsetOf(3), 0), (1, file.OffsetOf(4), 0)], Rows.Raw));
        data.AddRange(spellingEndStream ? "\nendstream\0\0\0\0"u8.ToArray() : Encode([(0, 0, 0), (0, 0, 0)], Rows.Raw));
        data.AddRange(Encode([(1, first, 0), (1, file.OffsetOf(8), 0)], Rows.Raw));
        file.Stream(7, "/Type /XRef /Size 9 /W [1 4 2] /Root 1 0 R /Length 4 0 R", [.. data]);
        file.StartXRef(first);
        file.Text("%" + new string('x', 2 * AdCodicem.Pdf.IO.PdfFileReader.NearbySearchRadius) + "\n");

        file.Object(8, "(new)");
        var second = file.Position;
        var rows = Encode([(1, file.OffsetOf(8), 0), (1, second, 0)], Rows.Raw);
        file.Stream(
            9,
            string.Create(CultureInfo.InvariantCulture, $"/Type /XRef /Size 10 /W [1 4 2] /Index [8 2] /Root 1 0 R /Prev {first} /Length {rows.Length}"),
            rows);
        file.StartXRef(second);
        return file.ToArray();
    }

    /// <summary>
    /// A file of <paramref name="count"/> revisions, each a cross-reference stream whose <c>/Length</c> names an object only
    /// that stream indexes, written before it, and whose rows give that object, the stream, and
    /// <paramref name="padding"/> free objects after them; the first indexes the catalog, the page tree and the page as
    /// well. The file is valid.
    /// </summary>
    public static byte[] UpdatesWithIndirectLengths(int count, int padding)
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        var previous = -1L;

        for (var i = 0; i < count; i++)
        {
            var length = 4 + (i * (padding + 2));
            var rows = new List<(int Type, long Second, int Third)>();

            if (i == 0)
            {
                rows.AddRange([(0, 0, 65535), (1, file.OffsetOf(1), 0), (1, file.OffsetOf(2), 0), (1, file.OffsetOf(3), 0)]);
            }

            file.Object(length, ((rows.Count + padding + 2) * 7).ToString(CultureInfo.InvariantCulture));
            var stream = file.Position;
            rows.AddRange([(1, file.OffsetOf(length), 0), (1, stream, 0)]);
            rows.AddRange(Enumerable.Repeat((0, 0L, 0), padding));
            var index = i == 0 ? $"0 {length + padding + 2}" : $"{length} {padding + 2}";
            var prev = previous < 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" /Prev {previous}");
            file.Stream(
                length + 1,
                string.Create(CultureInfo.InvariantCulture, $"/Type /XRef /Size {length + padding + 2} /W [1 4 2] /Index [{index}] /Root 1 0 R{prev} /Length {length} 0 R"),
                Encode([.. rows], Rows.Raw));
            file.StartXRef(stream);
            previous = stream;
        }

        return file.ToArray();
    }

    /// <summary>
    /// A hybrid file: a classic table indexes the catalog, the page tree and object 6, which holds the offset of
    /// cross-reference stream 7; its trailer's <c>/XRefStm</c> is <paramref name="xrefStm"/>, in which <c>{offset}</c>
    /// stands for that offset. Only the stream indexes the page, object 3. Object 6's value comes after
    /// <paramref name="padding"/> spaces.
    /// </summary>
    public static byte[] HybridNamingItsStream(string xrefStm, int padding = 0)
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");

        // The stream's offset is written in object 6 with a fixed width, so that it can be known before the stream is.
        var six = file.Position;
        var streamAt = six + padding + "6 0 obj\n0000000000\nendobj\n".Length;
        file.Object(6, new string(' ', padding) + streamAt.ToString("D10", CultureInfo.InvariantCulture));
        var stream = file.Position;
        var data = Encode([(1, file.OffsetOf(3), 0)], Rows.Raw);
        file.Stream(7, string.Create(CultureInfo.InvariantCulture, $"/Type /XRef /Size 7 /W [1 4 2] /Index [3 1] /Length {data.Length}"), data);
        var xref = file.Position;
        var named = xrefStm.Replace("{offset}", stream.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 3\n0000000000 65535 f\r\n{file.OffsetOf(1):D10} 00000 n\r\n{file.OffsetOf(2):D10} 00000 n\r\n6 1\n{six:D10} 00000 n\r\ntrailer\n<< /Size 7 /Root 1 0 R /XRefStm {named} >>\nstartxref\n{xref}\n%%EOF\n"));
        return file.ToArray();
    }

    /// <summary>
    /// A file of two classic tables: the first indexes the catalog, the page tree and the page; the second, an update,
    /// indexes object 4 and object 9, which holds the first table's offset, and names that table through
    /// <paramref name="prev"/>, in which <c>{offset}</c> stands for the offset. Object 9's value comes after
    /// <paramref name="padding"/> spaces; with <paramref name="real"/>, it is written as a real with no fractional part.
    /// </summary>
    public static byte[] UpdateNamingItsPrevious(string prev, int padding = 0, bool real = false)
    {
        using var file = new Writer("1.4");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        var first = file.Position;
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 4\n0000000000 65535 f\r\n{file.OffsetOf(1):D10} 00000 n\r\n{file.OffsetOf(2):D10} 00000 n\r\n{file.OffsetOf(3):D10} 00000 n\r\ntrailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{first}\n%%EOF\n"));
        file.Object(4, "(an update)");
        file.Object(9, new string(' ', padding) + first.ToString(CultureInfo.InvariantCulture) + (real ? ".0" : string.Empty));
        var second = file.Position;
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n4 1\n{file.OffsetOf(4):D10} 00000 n\r\n9 1\n{file.OffsetOf(9):D10} 00000 n\r\ntrailer\n<< /Size 10 /Root 1 0 R /Prev {prev.Replace("{offset}", first.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)} >>\nstartxref\n{second}\n%%EOF\n"));
        return file.ToArray();
    }

    /// <summary>
    /// A file of two revisions. The first holds the catalog, the page tree and the page, and cross-reference stream 7, whose
    /// <c>/Length</c> names object 5, which holds the data's length after 3,000 spaces; or, when <paramref name="chained"/>,
    /// object 4, which holds <c>9 0 R</c>, object 9 holding the length so. Its rows of objects 8 and 9 — 10 and 11 when
    /// <paramref name="chained"/> —, of reserved types, spell <c>endstream</c> after an end-of-line, and the page's row
    /// comes after them. The second is a classic table that indexes object 5 — 9 when <paramref name="chained"/> — again,
    /// where it is, and names stream 7 through <c>/Prev</c>. The file is valid, unless <paramref name="lengthOff"/> is added
    /// to the length; <paramref name="padding"/> spaces come before it. With <paramref name="spelledFirst"/>, the rows that
    /// spell <c>endstream</c> come first: chained, stream 7's row of object 4 then lies past the data taken to that
    /// <c>endstream</c>, and the file is read whole only by rebuilding its index. With <paramref name="lengthFirst"/>, the
    /// spaces come after the length instead.
    /// </summary>
    public static byte[] UpdatePlacingAPaddedLength(
        bool chained, int padding = 3000, int lengthOff = 0, bool spelledFirst = false, bool lengthFirst = false)
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        var lengthNumber = chained ? 9 : 5;
        var spelled = "\nendstream\0\0\0\0"u8.ToArray();
        var length = (6 * 7 + spelled.Length + lengthOff).ToString(CultureInfo.InvariantCulture);
        file.Object(lengthNumber, lengthFirst ? length + new string(' ', padding) : new string(' ', padding) + length);

        if (chained)
        {
            file.Object(4, "9 0 R");
        }

        var stream = file.Position;
        var rows = Encode(
            [(0, 0, 65535), (1, file.OffsetOf(1), 0), (1, file.OffsetOf(2), 0), (1, file.OffsetOf(chained ? 4 : 5), 0), (1, stream, 0)],
            Rows.Raw);
        var data = new List<byte>(spelledFirst ? spelled : rows);
        data.AddRange(spelledFirst ? rows : spelled);
        data.AddRange(Encode([(1, file.OffsetOf(3), 0)], Rows.Raw));
        var (spelledRows, lengthRow) = chained ? ("10 2", "4 1") : ("8 2", "5 1");
        var index = spelledFirst ? $"[{spelledRows} 0 3 {lengthRow} 7 1 3 1]" : $"[0 3 {lengthRow} 7 1 {spelledRows} 3 1]";
        file.Stream(
            7,
            string.Create(CultureInfo.InvariantCulture, $"/Type /XRef /Size {(chained ? 12 : 10)} /W [1 4 2] /Index {index} /Root 1 0 R /Length {(chained ? 4 : 5)} 0 R"),
            [.. data]);
        file.Text("%" + new string('-', 1200) + "\n");
        var xref = file.Position;
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f\r\n{lengthNumber} 1\n{file.OffsetOf(lengthNumber):D10} 00000 n\r\ntrailer\n<< /Size {(chained ? 12 : 10)} /Root 1 0 R /Prev {stream} >>\nstartxref\n{xref}\n%%EOF\n"));
        return file.ToArray();
    }

    /// <summary>
    /// A file of two revisions. The first holds the catalog, the page tree and the page, object 5, which holds the data's
    /// length, 210, after 1,014 spaces — a <c>MaxObjectLength</c> of 1,024 parts it after its 21 —, and cross-reference
    /// stream 7, whose <c>/Length</c> names object 5 and whose rows spell <c>endstream</c> after an end-of-line twice, 7 and
    /// 21 bytes in, before the rows of objects 1 to 3, 5 and 7 and <paramref name="free"/> free ones. With
    /// <paramref name="compressed"/>, the rows are compressed in a stored block, which shows them as written 7 bytes in: the
    /// data is 284 bytes with 29 free rows, which the same bound parts after its 28, where the second <c>endstream</c> is,
    /// and 221 with 20, parted after its 22, where none is. The second is a classic table that indexes object 5 again, where
    /// it is, and names stream 7 through <c>/Prev</c>. The file is valid.
    /// </summary>
    public static byte[] UpdatePlacingAPartedLength(int free = 20, bool compressed = false)
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");

        // The data's length does not depend on the offsets its rows hold.
        byte[] Data(long five, long stream)
        {
            var spelled = "\nendstream\0\0\0\0"u8.ToArray();
            var data = new List<byte>(Encode([(0, 0, 65535)], Rows.Raw));
            data.AddRange(spelled);
            data.AddRange(spelled);
            data.AddRange(Encode([(1, file.OffsetOf(1), 0), (1, file.OffsetOf(2), 0), (1, file.OffsetOf(3), 0), (1, five, 0), (1, stream, 0)], Rows.Raw));
            data.AddRange(Encode([.. Enumerable.Repeat((0, 0L, 0), free)], Rows.Raw));
            return compressed ? Compress([.. data]) : [.. data];
        }

        file.Object(5, new string(' ', 1014) + Data(0, 0).Length.ToString(CultureInfo.InvariantCulture));
        var stream = file.Position;
        var size = 30 + free;
        file.Stream(
            7,
            string.Create(
                CultureInfo.InvariantCulture,
                $"/Type /XRef /Size {size} /W [1 4 2] /Index [0 1 20 2 22 2 1 3 5 1 7 1 30 {free}]{(compressed ? " /Filter /FlateDecode" : string.Empty)} /Root 1 0 R /Length 5 0 R"),
            Data(file.OffsetOf(5), stream));
        file.Text("%" + new string('-', 1200) + "\n");
        var xref = file.Position;
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f\r\n5 1\n{file.OffsetOf(5):D10} 00000 n\r\ntrailer\n<< /Size {size} /Root 1 0 R /Prev {stream} >>\nstartxref\n{xref}\n%%EOF\n"));
        return file.ToArray();
    }

    /// <summary>
    /// A file of two revisions. The first holds the catalog, the page tree and the page, object 4, and cross-reference stream
    /// 5, whose rows of objects 0 to 5, none spelling <c>endstream</c>, are compressed in a stored block and written in
    /// hexadecimal after a stray <c>x</c>, which encodes nothing: <c>/Filter [/ASCIIHexDecode /FlateDecode]</c>. Its
    /// <c>/Length</c> names object 4, which holds it and which only the stream indexes — or, unless
    /// <paramref name="deferred"/>, is written direct. The second is a classic table that indexes objects 1 to 3 and names
    /// stream 5 through <c>/Prev</c>.
    /// </summary>
    public static byte[] UpdateOverAStrayHexByte(bool deferred)
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");

        // The data's length does not depend on the offsets its rows hold.
        byte[] Data(long four, long stream) => Encoding.ASCII.GetBytes(
            "x" + Convert.ToHexString(Compress(Encode(
                [(0, 0, 65535), (1, file.OffsetOf(1), 0), (1, file.OffsetOf(2), 0), (1, file.OffsetOf(3), 0), (1, four, 0), (1, stream, 0)],
                Rows.Raw))) + ">");

        var length = Data(0, 0).Length.ToString(CultureInfo.InvariantCulture);
        file.Object(4, length);
        var at = file.Position;
        file.Stream(
            5,
            $"/Type /XRef /Size 6 /W [1 4 2] /Filter [/ASCIIHexDecode /FlateDecode] /Root 1 0 R /Length {(deferred ? "4 0 R" : length)}",
            Data(file.OffsetOf(4), at));
        file.Text("%" + new string('-', 1200) + "\n");
        var xref = file.Position;
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 4\n0000000000 65535 f\r\n{file.OffsetOf(1):D10} 00000 n\r\n{file.OffsetOf(2):D10} 00000 n\r\n{file.OffsetOf(3):D10} 00000 n\r\ntrailer\n<< /Size 6 /Root 1 0 R /Prev {at} >>\nstartxref\n{xref}\n%%EOF\n"));
        return file.ToArray();
    }

    /// <summary>
    /// A file of two revisions, as <see cref="UpdateOverAStrayHexByte"/> writes it, but stream 5's hexadecimal rows hold no
    /// stray byte. With <paramref name="compressed"/>, the rows and <paramref name="padding"/> zero bytes after them are
    /// compressed, the zero bytes to a few, and written with no <c>&gt;</c>: <c>/Filter [/ASCIIHexDecode /FlateDecode]</c>. Without,
    /// the rows are written as they are, <c>/Filter /ASCIIHexDecode</c>, and an end-of-line, <c>endstream</c> and another
    /// follow the digits of 14 bytes and one more: the deferred search stops there.
    /// </summary>
    public static byte[] UpdateOverHexRows(bool deferred, bool compressed, int padding = 0)
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");

        // Compressed, the data's length depends on the offsets its rows hold: written in four digits, it does not move them.
        byte[] Data(long four, long stream)
        {
            var rows = Encode(
                [(0, 0, 65535), (1, file.OffsetOf(1), 0), (1, file.OffsetOf(2), 0), (1, file.OffsetOf(3), 0), (1, four, 0), (1, stream, 0)],
                Rows.Raw);

            if (compressed)
            {
                using var output = new MemoryStream();

                using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
                {
                    zlib.Write([.. rows, .. new byte[padding]]);
                }

                return Encoding.ASCII.GetBytes(Convert.ToHexString(output.ToArray()));
            }

            var hex = Convert.ToHexString(rows);
            return Encoding.ASCII.GetBytes(hex[..29] + "\nendstream\n" + hex[29..] + ">");
        }

        var four = file.Position;
        var at = four + "4 0 obj\n0000\nendobj\n".Length;
        var data = Data(four, at);
        var length = data.Length.ToString("D4", CultureInfo.InvariantCulture);
        file.Object(4, length);
        file.Stream(
            5,
            $"/Type /XRef /Size 6 /W [1 4 2] /Filter {(compressed ? "[/ASCIIHexDecode /FlateDecode]" : "/ASCIIHexDecode")} /Root 1 0 R /Length {(deferred ? "4 0 R" : length)}",
            data);
        file.Text("%" + new string('-', 1200) + "\n");
        var xref = file.Position;
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 4\n0000000000 65535 f\r\n{file.OffsetOf(1):D10} 00000 n\r\n{file.OffsetOf(2):D10} 00000 n\r\n{file.OffsetOf(3):D10} 00000 n\r\ntrailer\n<< /Size 6 /Root 1 0 R /Prev {at} >>\nstartxref\n{xref}\n%%EOF\n"));
        return file.ToArray();
    }

    /// <summary>
    /// A file of two revisions, as <see cref="UpdateOverAStrayHexByte"/> writes it, but stream 5 is
    /// <c>/Filter [/RunLengthDecode /FlateDecode]</c>: a first literal run of RunLength holds the Flate data, which decodes to
    /// its rows and 1,000 zero bytes, and a second the bytes of an end-of-line, <c>endstream</c>, another end-of-line and
    /// <c>XYZ</c>, where a deferred search stops. Its <c>/Length</c> names object 4, which only the stream's own row places,
    /// unless <paramref name="deferred"/> is false: written direct. The file is valid.
    /// </summary>
    public static byte[] UpdateOverRunsSpellingAnEndStream(bool deferred)
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");

        // Compressed, the data's length depends on the offsets its rows hold: written in four digits, it does not move them.
        byte[] Data(long four, long stream)
        {
            var rows = Encode(
                [(0, 0, 65535), (1, file.OffsetOf(1), 0), (1, file.OffsetOf(2), 0), (1, file.OffsetOf(3), 0), (1, four, 0), (1, stream, 0)],
                Rows.Raw);
            using var output = new MemoryStream();

            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write([.. rows, .. new byte[1000]]);
            }

            var flate = output.ToArray();
            var data = new List<byte>();

            for (var i = 0; i < flate.Length; i += 128)
            {
                var count = Math.Min(128, flate.Length - i);
                data.Add((byte)(count - 1));
                data.AddRange(flate.AsSpan(i, count).ToArray());
            }

            var spelled = "\nendstream\nXYZ"u8.ToArray();
            data.Add((byte)(spelled.Length - 1));
            data.AddRange(spelled);
            data.Add(128);
            return [.. data];
        }

        var four = file.Position;
        var at = four + "4 0 obj\n0000\nendobj\n".Length;
        var data = Data(four, at);
        var length = data.Length.ToString("D4", CultureInfo.InvariantCulture);
        file.Object(4, length);
        file.Stream(
            5,
            $"/Type /XRef /Size 6 /W [1 4 2] /Filter [/RunLengthDecode /FlateDecode] /Root 1 0 R /Length {(deferred ? "4 0 R" : length)}",
            data);
        file.Text("%" + new string('-', 1200) + "\n");
        var xref = file.Position;
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 4\n0000000000 65535 f\r\n{file.OffsetOf(1):D10} 00000 n\r\n{file.OffsetOf(2):D10} 00000 n\r\n{file.OffsetOf(3):D10} 00000 n\r\ntrailer\n<< /Size 6 /Root 1 0 R /Prev {at} >>\nstartxref\n{xref}\n%%EOF\n"));
        return file.ToArray();
    }

    /// <summary>
    /// A file of one cross-reference stream, object 5, whose <c>/Length</c> names object 4, which only the stream's own row
    /// places and which holds the length after 1,100 spaces — a <c>MaxObjectLength</c> of 1,024 cuts it before its value.
    /// Its rows, of objects 0 to 2, 4, 5 and 8, then 3 (<c>/Index [0 3 4 2 8 1 3 1]</c>), lose their last 3 bytes: the page's
    /// row lacks, the file's fault. With <paramref name="objectAfter"/>, object 8, which its row places, follows the stream,
    /// and the <c>startxref</c> otherwise.
    /// </summary>
    public static byte[] AloneWithACutLengthLackingItsLastRow(bool objectAfter)
    {
        // Every row and offset is as wide whatever it holds: where object 8 lies is read once, then written.
        _ = Build(0, out var eight);
        return Build(eight, out _);

        byte[] Build(long eightAt, out long eight)
        {
            using var file = new Writer("1.5");
            file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
            file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
            file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
            var four = file.Position;
            var at = four + ("4 0 obj\n" + new string(' ', 1100) + "46\nendobj\n").Length;
            var rows = Encode(
                [
                    (0, 0, 65535), (1, file.OffsetOf(1), 0), (1, file.OffsetOf(2), 0), (1, four, 0), (1, at, 0), (1, eightAt, 0),
                    (1, file.OffsetOf(3), 0),
                ],
                Rows.Raw);
            file.Object(4, new string(' ', 1100) + "46");
            file.Stream(5, "/Type /XRef /Size 9 /W [1 4 2] /Index [0 3 4 2 8 1 3 1] /Root 1 0 R /Length 4 0 R", rows[..^3]);
            eight = file.Position;

            if (objectAfter)
            {
                file.Object(8, "(eight)");
            }

            file.StartXRef(at);
            return file.ToArray();
        }
    }

    /// <summary>
    /// A file of one cross-reference stream, object 7, in ASCII85, whose <c>/Length</c> names object 5, which only the
    /// stream's own row places, holding the length after 1,015 spaces: a <c>MaxObjectLength</c> of 1,024 parts it after its
    /// first digit. Its rows are those of objects 0, 5, 1 to 3, 20 and 21, of reserved types, and 7 (<c>/Index</c>). The text
    /// spells an end-of-line and <c>endstream</c> four digits into the group that holds the page's row's last three bytes and
    /// row 20's type, which is chosen so that the group's fifth digit is the <c>e</c> of <c>endstream</c>: one that a
    /// <c>u</c> in its place carries into the page's row's generation where <paramref name="carry"/>, and that it does not
    /// otherwise. The file is valid.
    /// </summary>
    public static byte[] Ascii85RowsPartedBySpelledEndStream(bool carry)
    {
        for (var pad = 0; ; pad++)
        {
            using var file = new Writer("1.5");
            file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
            file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
            file.Text("%" + new string('-', pad) + "\n");
            file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
            var low = (int)(file.OffsetOf(3) & 0xFF);

            // The group [low 00 00 type] sums to low + type modulo 85, 2^24 being 1 there: its fifth digit is "e", 68, for one
            // type in each range. A "u" in its place adds 16 to the group, which carries into the generation past 239.
            var type = -1;

            for (var candidate = carry ? 240 : 200; candidate <= (carry ? 255 : 239) && type < 0; candidate++)
            {
                type = (low + candidate) % 85 == 68 ? candidate : -1;
            }

            if (type < 0)
            {
                continue;
            }

            // The digits "ndstr" and "eam!!" that follow the group are rows 20 and 21's bytes.
            string Text(long five, long stream) =>
                Ascii85(Encode([(0, 0, 65535), (1, five, 0), (1, file.OffsetOf(1), 0), (1, file.OffsetOf(2), 0)], Rows.Raw)) +
                Ascii85([1, (byte)(file.OffsetOf(3) >> 24), (byte)(file.OffsetOf(3) >> 16), (byte)(file.OffsetOf(3) >> 8)]) +
                Ascii85([(byte)low, 0, 0, (byte)type])[..4] + "\nendstr" + "eam\n!!" + Ascii85([.. new byte[5], .. Encode([(1, stream, 0)], Rows.Raw)]) + "~>";

            file.Object(5, new string(' ', 1015) + Text(0, 0).Length.ToString(CultureInfo.InvariantCulture));
            var at = file.Position;
            file.Stream(
                7,
                "/Type /XRef /Size 22 /W [1 4 2] /Index [0 1 5 1 1 3 20 2 7 1] /Filter /ASCII85Decode /Root 1 0 R /Length 5 0 R",
                Encoding.ASCII.GetBytes(Text(file.OffsetOf(5), at)));
            file.StartXRef(at);
            return file.ToArray();
        }
    }

    /// <summary>
    /// A file of two revisions. The first holds the catalog, the page tree, the page, object 5, which holds the length of
    /// cross-reference stream 7 after 1,015 spaces — a <c>MaxObjectLength</c> of 1,024 parts it after its first digit —, and
    /// stream 7, Flate in a stored block with a PNG predictor of <paramref name="columns"/> columns, whose rows are the
    /// catalog's, two of reserved types that spell <c>endstream</c> after an end-of-line, then the page tree's, the page's,
    /// object 5's and its own (<c>/Index [1 1 20 2 2 2 5 1 7 1]</c>). The second is a classic table that places object 5
    /// and names stream 7 through <c>/Prev</c>. The file is valid.
    /// </summary>
    public static byte[] PredictedRowsPartedBySpelledEndStream(int columns)
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");

        // The data's length does not depend on the offsets its rows hold.
        byte[] Data(long five, long stream)
        {
            var rows = new List<byte>(Encode([(1, file.OffsetOf(1), 0)], Rows.Raw));
            rows.AddRange("\nendstream\0\0\0\0"u8.ToArray());
            rows.AddRange(Encode([(1, file.OffsetOf(2), 0), (1, file.OffsetOf(3), 0), (1, five, 0), (1, stream, 0)], Rows.Raw));

            while (rows.Count % columns != 0)
            {
                rows.Add(0);
            }

            var predicted = new List<byte>();

            for (var i = 0; i < rows.Count; i += columns)
            {
                predicted.Add(0);
                predicted.AddRange(rows.GetRange(i, columns));
            }

            return Compress([.. predicted]);
        }

        file.Object(5, new string(' ', 1015) + Data(0, 0).Length.ToString(CultureInfo.InvariantCulture));
        var at = file.Position;
        file.Stream(
            7,
            string.Create(
                CultureInfo.InvariantCulture,
                $"/Type /XRef /Size 22 /W [1 4 2] /Index [1 1 20 2 2 2 5 1 7 1] /Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns {columns} >> /Root 1 0 R /Length 5 0 R"),
            Data(file.OffsetOf(5), at));
        file.Text("%" + new string('-', 1200) + "\n");
        var xref = file.Position;
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f\r\n5 1\n{file.OffsetOf(5):D10} 00000 n\r\ntrailer\n<< /Size 22 /Root 1 0 R /Prev {at} >>\nstartxref\n{xref}\n%%EOF\n"));
        return file.ToArray();
    }

    /// <summary>
    /// A file of one cross-reference stream, object 7, whose <c>/Length</c> names object 5, which only the stream's own row
    /// places, before the rows of objects 1 and 2; two rows of reserved types spell <c>endstream</c> after an end-of-line
    /// after them, 28 and 42 bytes in, before the rows of objects 3 and 7 and fifty free ones, 420 bytes in all. Object 5
    /// holds the length after 1,014 spaces — a <c>MaxObjectLength</c> of 1,024 parts it after its 42 —, or, with
    /// <paramref name="throughReference"/>, <c>6 0 R</c> and 1,100 spaces after it — the same bound cuts it after the whole
    /// reference —, object 6 holding the length, which a newer classic table indexes and names the stream through
    /// <c>/Prev</c> from. The file is valid, unless <paramref name="lackingRow"/>: the stream's <c>/Index</c> declares one
    /// row more than its data holds.
    /// </summary>
    public static byte[] OwnRowPlacingAPaddedLength(bool throughReference, bool lackingRow = false)
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        const int Free = 50;
        var spelled = "\nendstream\0\0\0\0"u8.ToArray();

        // The data's length does not depend on the offsets its rows hold.
        byte[] Data(long five, long stream)
        {
            var data = new List<byte>(Encode([(0, 0, 65535), (1, five, 0), (1, file.OffsetOf(1), 0), (1, file.OffsetOf(2), 0)], Rows.Raw));
            data.AddRange(spelled);
            data.AddRange(spelled);
            data.AddRange(Encode([(1, file.OffsetOf(3), 0), (1, stream, 0), .. Enumerable.Repeat((0, 0L, 0), Free)], Rows.Raw));
            return [.. data];
        }

        var length = Data(0, 0).Length.ToString(CultureInfo.InvariantCulture);

        if (throughReference)
        {
            file.Object(6, length);
        }

        file.Object(5, throughReference ? "6 0 R" + new string(' ', 1100) : new string(' ', 1014) + length);
        var stream = file.Position;
        var size = 30 + Free + (lackingRow ? 1 : 0);
        file.Stream(
            7,
            string.Create(CultureInfo.InvariantCulture, $"/Type /XRef /Size {size} /W [1 4 2] /Index [0 1 5 1 1 2 20 2 22 2 3 1 7 1 30 {size - 30}] /Root 1 0 R /Length 5 0 R"),
            Data(file.OffsetOf(5), stream));

        if (!throughReference)
        {
            file.StartXRef(stream);
            return file.ToArray();
        }

        file.Text("%" + new string('-', 1200) + "\n");
        var xref = file.Position;
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f\r\n6 1\n{file.OffsetOf(6):D10} 00000 n\r\ntrailer\n<< /Size {size} /Root 1 0 R /Prev {stream} >>\nstartxref\n{xref}\n%%EOF\n"));
        return file.ToArray();
    }

    /// <summary>
    /// A file of three revisions. The first holds the catalog, the page tree and the page, object 5, which holds
    /// <c>6 0 R</c> and 1,100 spaces after it — a <c>MaxObjectLength</c> of 1,024 cuts it after the whole reference —,
    /// object 6, which holds 49, and cross-reference stream 7, whose <c>/Length</c> names object 5 and whose rows of objects
    /// 0 to 2 and 7 come before two rows of reserved types that spell <c>endstream</c> after an end-of-line, before the
    /// page's row: 49 bytes. The second is cross-reference stream 20, of 49 bytes too, which indexes objects 6 and 20 to 25,
    /// and names stream 7 through <c>/Prev</c>; its <c>/Length</c> is 49, or, with <paramref name="bothNameIt"/>,
    /// <c>5 0 R</c>, which the chain then reads before any section places object 6. The third is a classic table that
    /// indexes object 5 and names stream 20 through <c>/Prev</c>. The file is valid, unless <paramref name="lackingRow"/>:
    /// stream 7's <c>/Index</c> declares one row more than its data holds.
    /// </summary>
    public static byte[] UpdatesReachingALengthThroughACutReference(bool bothNameIt, bool lackingRow = false)
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        file.Object(5, "6 0 R" + new string(' ', 1100));
        file.Object(6, "49");
        var older = file.Position;
        var data = new List<byte>(Encode([(0, 0, 65535), (1, file.OffsetOf(1), 0), (1, file.OffsetOf(2), 0), (1, older, 0)], Rows.Raw));
        data.AddRange("\nendstream\0\0\0\0"u8.ToArray());
        data.AddRange(Encode([(1, file.OffsetOf(3), 0)], Rows.Raw));
        file.Stream(
            7,
            string.Create(CultureInfo.InvariantCulture, $"/Type /XRef /Size {(lackingRow ? 11 : 10)} /W [1 4 2] /Index [0 3 7 1 8 2 3 {(lackingRow ? 2 : 1)}] /Root 1 0 R /Length 5 0 R"),
            [.. data]);
        var newer = file.Position;
        var rows = Encode([(1, file.OffsetOf(6), 0), (1, newer, 0), .. Enumerable.Repeat((0, 0L, 0), 5)], Rows.Raw);
        file.Stream(
            20,
            string.Create(CultureInfo.InvariantCulture, $"/Type /XRef /Size 26 /W [1 4 2] /Index [6 1 20 6] /Root 1 0 R /Prev {older} /Length {(bothNameIt ? "5 0 R" : "49")}"),
            rows);
        file.Text("%" + new string('-', 1200) + "\n");
        var xref = file.Position;
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f\r\n5 1\n{file.OffsetOf(5):D10} 00000 n\r\ntrailer\n<< /Size 26 /Root 1 0 R /Prev {newer} >>\nstartxref\n{xref}\n%%EOF\n"));
        return file.ToArray();
    }

    /// <summary>
    /// A file as <see cref="UpdatePlacingAPaddedLength"/> writes it, the rows that spell <c>endstream</c> numbering objects 8
    /// and 9, but for a length a chain of <paramref name="objects"/> objects gives: stream 7's <c>/Length</c> names object 5
    /// — or, with <paramref name="ownRow"/>, object 4, which only stream 7's own row places —, which holds <c>100 0 R</c>;
    /// object 100 names object 101, and so on to the last, which holds the data's length after 3,000 spaces. The newer table
    /// indexes object 5, unless <paramref name="ownRow"/>, and objects 100 to the last, where they are. The file is valid.
    /// </summary>
    public static byte[] UpdatePlacingAPaddedLengthChain(int objects, bool ownRow)
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        var lengthNumber = ownRow ? 4 : 5;
        var last = 100 + objects - 2;
        var spelled = "\nendstream\0\0\0\0"u8.ToArray();
        file.Object(lengthNumber, "100 0 R");

        for (var number = 100; number <= last; number++)
        {
            file.Object(
                number,
                number < last
                    ? string.Create(CultureInfo.InvariantCulture, $"{number + 1} 0 R")
                    : new string(' ', 3000) + (6 * 7 + spelled.Length).ToString(CultureInfo.InvariantCulture));
        }

        var stream = file.Position;
        var data = new List<byte>(Encode(
            [(0, 0, 65535), (1, file.OffsetOf(1), 0), (1, file.OffsetOf(2), 0), (1, file.OffsetOf(lengthNumber), 0), (1, stream, 0)],
            Rows.Raw));
        data.AddRange(spelled);
        data.AddRange(Encode([(1, file.OffsetOf(3), 0)], Rows.Raw));
        file.Stream(
            7,
            string.Create(CultureInfo.InvariantCulture, $"/Type /XRef /Size 10 /W [1 4 2] /Index [0 3 {lengthNumber} 1 7 1 8 2 3 1] /Root 1 0 R /Length {lengthNumber} 0 R"),
            [.. data]);
        file.Text("%" + new string('-', 1200) + "\n");
        var xref = file.Position;
        var rows = new StringBuilder("xref\n0 1\n0000000000 65535 f\r\n");

        if (!ownRow)
        {
            rows.Append(CultureInfo.InvariantCulture, $"5 1\n{file.OffsetOf(5):D10} 00000 n\r\n");
        }

        rows.Append(CultureInfo.InvariantCulture, $"100 {objects - 1}\n");

        for (var number = 100; number <= last; number++)
        {
            rows.Append(CultureInfo.InvariantCulture, $"{file.OffsetOf(number):D10} 00000 n\r\n");
        }

        rows.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {last + 1} /Root 1 0 R /Prev {stream} >>\nstartxref\n{xref}\n%%EOF\n");
        file.Text(rows.ToString());
        return file.ToArray();
    }

    /// <summary>
    /// A hybrid update over a classic table that indexes the catalog, the page tree and the page. The update writes object
    /// 9, which holds <paramref name="nine"/> after <paramref name="padding"/> spaces, and object 10, which cross-reference
    /// stream 7 indexes; the stream's own <c>/Prev</c> is <paramref name="streamPrev"/>, which the reader does not follow.
    /// The update's table indexes object 9, and its trailer names the older table through <c>/Prev</c> and the stream
    /// through <c>/XRefStm</c>.
    /// </summary>
    public static byte[] HybridWithStreamPrev(string streamPrev, int padding = 3000, string nine = "123")
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        var older = file.Position;
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 4\n0000000000 65535 f\r\n{file.OffsetOf(1):D10} 00000 n\r\n{file.OffsetOf(2):D10} 00000 n\r\n{file.OffsetOf(3):D10} 00000 n\r\ntrailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{older}\n%%EOF\n"));
        file.Object(9, new string(' ', padding) + nine);
        file.Object(10, "(ten)");
        var stream = file.Position;
        var data = Encode([(1, file.OffsetOf(10), 0)], Rows.Raw);
        file.Stream(
            7,
            string.Create(CultureInfo.InvariantCulture, $"/Type /XRef /Size 11 /W [1 4 2] /Index [10 1] /Prev {streamPrev} /Length {data.Length}"),
            data);
        var table = file.Position;
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f\r\n9 1\n{file.OffsetOf(9):D10} 00000 n\r\ntrailer\n<< /Size 11 /Root 1 0 R /Prev {older} /XRefStm {stream} >>\nstartxref\n{table}\n%%EOF\n"));
        return file.ToArray();
    }

    /// <summary>
    /// A file as <see cref="HybridWithStreamPrev"/> writes it, but for what the update's table names through
    /// <c>/XRefStm</c>: a classic table, where ISO 32000-1 names a cross-reference stream (Table 19), which indexes object
    /// 10 and whose trailer gives <paramref name="key"/> as <paramref name="value"/>.
    /// </summary>
    public static byte[] HybridNamingATable(string key, string value, int padding = 3000)
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        var older = file.Position;
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 4\n0000000000 65535 f\r\n{file.OffsetOf(1):D10} 00000 n\r\n{file.OffsetOf(2):D10} 00000 n\r\n{file.OffsetOf(3):D10} 00000 n\r\ntrailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{older}\n%%EOF\n"));
        file.Object(9, new string(' ', padding) + "123");
        file.Object(10, "(ten)");
        var named = file.Position;
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n10 1\n{file.OffsetOf(10):D10} 00000 n\r\ntrailer\n<< /Size 11 /{key} {value} >>\n"));
        var table = file.Position;
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f\r\n9 1\n{file.OffsetOf(9):D10} 00000 n\r\ntrailer\n<< /Size 11 /Root 1 0 R /Prev {older} /XRefStm {named} >>\nstartxref\n{table}\n%%EOF\n"));
        return file.ToArray();
    }

    /// <summary>
    /// A classic table updated by a cross-reference stream. The table indexes the catalog, the page tree, the page and
    /// object 12, the highest number; cross-reference stream 10, the update, indexes the first three with object 4, a page
    /// the tree does not list, and object 9, which holds the table's offset after <paramref name="padding"/> spaces —
    /// written as a real with no fractional part with <paramref name="real"/> —, and names the table through
    /// <c>/Prev 9 0 R</c>. The table and the stream both give <c>/Size 13</c>.
    /// </summary>
    public static byte[] StreamUpdateHidingItsHighestNumber(int padding, bool real = false)
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        file.Object(12, "(twelve)");
        var first = file.Position;
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 4\n0000000000 65535 f\r\n{file.OffsetOf(1):D10} 00000 n\r\n{file.OffsetOf(2):D10} 00000 n\r\n{file.OffsetOf(3):D10} 00000 n\r\n12 1\n{file.OffsetOf(12):D10} 00000 n\r\ntrailer\n<< /Size 13 /Root 1 0 R >>\nstartxref\n{first}\n%%EOF\n"));
        file.Object(4, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        file.Object(9, new string(' ', padding) + first.ToString(CultureInfo.InvariantCulture) + (real ? ".0" : string.Empty));
        var stream = file.Position;
        var data = Encode(
            [(0, 0, 65535), (1, file.OffsetOf(1), 0), (1, file.OffsetOf(2), 0), (1, file.OffsetOf(3), 0), (1, file.OffsetOf(4), 0), (1, file.OffsetOf(9), 0), (1, stream, 0)],
            Rows.Raw);
        file.Stream(
            10,
            string.Create(CultureInfo.InvariantCulture, $"/Type /XRef /Size 13 /W [1 4 2] /Index [0 5 9 2] /Root 1 0 R /Prev 9 0 R /Length {data.Length}"),
            data);
        file.StartXRef(stream);
        return file.ToArray();
    }

    /// <summary>
    /// A file of two classic tables. The first indexes the catalog, the page tree, the page and object 12, the highest
    /// number; the second, an update, writes the catalog, the page tree and the page again, indexes them with object 4, a
    /// page the tree does not list, and object 9, which holds the first table's offset after <paramref name="padding"/>
    /// spaces, and names that table through <c>/Prev 9 0 R</c>. Both trailers give <c>/Size 13</c>.
    /// </summary>
    public static byte[] UpdateHidingItsHighestNumber(int padding)
    {
        using var file = new Writer("1.4");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        file.Object(12, "(twelve)");
        var first = file.Position;
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 4\n0000000000 65535 f\r\n{file.OffsetOf(1):D10} 00000 n\r\n{file.OffsetOf(2):D10} 00000 n\r\n{file.OffsetOf(3):D10} 00000 n\r\n12 1\n{file.OffsetOf(12):D10} 00000 n\r\ntrailer\n<< /Size 13 /Root 1 0 R >>\nstartxref\n{first}\n%%EOF\n"));
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        file.Object(4, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        file.Object(9, new string(' ', padding) + first.ToString(CultureInfo.InvariantCulture));
        var second = file.Position;
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n1 4\n{file.OffsetOf(1):D10} 00000 n\r\n{file.OffsetOf(2):D10} 00000 n\r\n{file.OffsetOf(3):D10} 00000 n\r\n{file.OffsetOf(4):D10} 00000 n\r\n9 1\n{file.OffsetOf(9):D10} 00000 n\r\ntrailer\n<< /Size 13 /Root 1 0 R /Prev 9 0 R >>\nstartxref\n{second}\n%%EOF\n"));
        return file.ToArray();
    }

    private enum Rows
    {
        Raw,
        Flate,
        PngFlate,
    }

    /// <summary>Writes rows of [1 4 2] bytes, as they are, compressed, or predicted with PNG's None filter and compressed.</summary>
    private static byte[] Encode((int Type, long Second, int Third)[] rows, Rows encoding)
    {
        var data = new List<byte>();

        foreach (var (type, second, third) in rows)
        {
            if (encoding == Rows.PngFlate)
            {
                data.Add(0);
            }

            data.AddRange([(byte)type, (byte)(second >> 24), (byte)(second >> 16), (byte)(second >> 8), (byte)second, (byte)(third >> 8), (byte)third]);
        }

        return encoding == Rows.Raw ? [.. data] : Compress([.. data]);
    }

    /// <summary>Writes <paramref name="bytes"/> in ASCII85, whole groups but the last, with no <c>z</c> and no <c>~&gt;</c>.</summary>
    private static string Ascii85(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder();
        Span<char> digits = stackalloc char[5];

        for (var i = 0; i < bytes.Length; i += 4)
        {
            var count = Math.Min(4, bytes.Length - i);
            uint value = 0;

            for (var j = 0; j < 4; j++)
            {
                value = (value << 8) | (j < count ? bytes[i + j] : 0u);
            }

            for (var j = 4; j >= 0; j--)
            {
                digits[j] = (char)('!' + (value % 85));
                value /= 85;
            }

            text.Append(digits[..(count + 1)]);
        }

        return text.ToString();
    }

    /// <summary>Compresses data in stored blocks, which show it as written.</summary>
    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();

        using (var zlib = new ZLibStream(output, CompressionLevel.NoCompression, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>Writes a file object by object, knowing where each starts.</summary>
    private sealed class Writer : IDisposable
    {
        private readonly MemoryStream _output = new();
        private readonly Dictionary<int, long> _offsets = [];

        public Writer(string version) => Text($"%PDF-{version}\n");

        public long Position => _output.Position;

        public long OffsetOf(int number) => _offsets[number];

        public void Text(string text) => _output.Write(Encoding.Latin1.GetBytes(text));

        public void Object(int number, string body)
        {
            _offsets[number] = Position;
            Text(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n"));
        }

        public void Stream(int number, string entries, byte[] data, string endOfLine = "\n")
        {
            _offsets[number] = Position;
            Text(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< {entries} >>\nstream\n"));
            _output.Write(data);
            Text(endOfLine + "endstream\nendobj\n");
        }

        public void StartXRef(long offset) => Text(string.Create(CultureInfo.InvariantCulture, $"startxref\n{offset}\n%%EOF\n"));

        public byte[] ToArray() => _output.ToArray();

        public void Dispose() => _output.Dispose();
    }
}
