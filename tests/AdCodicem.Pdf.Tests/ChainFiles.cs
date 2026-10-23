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
    /// <paramref name="spellingHeader"/> an object header. The rows are compressed when <paramref name="compressed"/>, and the
    /// data ends with <paramref name="endOfLine"/> before its <c>endstream</c>.
    /// </summary>
    public static byte[] AloneWithIndirectLength(
        int lengthOff = 0,
        bool spellingEndStream = false,
        bool compressed = false,
        bool endingInCarriageReturn = false,
        string endOfLine = "\n",
        int padding = 0,
        bool lengthLast = false,
        bool spellingHeader = false)
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
        file.Object(lengthNumber, length.ToString(CultureInfo.InvariantCulture));
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

            return [.. data];
        }
    }

    /// <summary>
    /// A file of one cross-reference stream, object 5, whose <c>/Length</c> names object 4, which only that stream indexes,
    /// in a row before two rows of reserved types that spell <c>endstream</c> after an end-of-line; the page's row comes
    /// after them. With <paramref name="reservedRowFirst"/>, the <c>/Index</c> numbers object 4 twice: first in a row of a
    /// reserved type, which the specification says to ignore, then in the row that places it. The file is valid.
    /// </summary>
    public static byte[] AloneWithIndirectLengthBeforeSpelledRows(bool reservedRowFirst)
    {
        using var file = new Writer("1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        var rowCount = reservedRowFirst ? 9 : 8;
        file.Object(4, (rowCount * 7).ToString(CultureInfo.InvariantCulture));
        var stream = file.Position;
        var reserved = reservedRowFirst ? [(3, 0L, 0)] : Array.Empty<(int, long, int)>();
        var data = new List<byte>(Encode([(0, 0, 65535), (1, file.OffsetOf(1), 0), (1, file.OffsetOf(2), 0), .. reserved, (1, file.OffsetOf(4), 0), (1, stream, 0)], Rows.Raw));
        data.AddRange("\nendstream\0\0\0\0"u8.ToArray());
        data.AddRange(Encode([(1, file.OffsetOf(3), 0)], Rows.Raw));
        var index = reservedRowFirst ? "0 3 4 1 4 2 6 2 3 1" : "0 3 4 2 6 2 3 1";
        file.Stream(5, $"/Type /XRef /Size 8 /W [1 4 2] /Index [{index}] /Root 1 0 R /Length 4 0 R", [.. data]);
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
    /// <paramref name="padding"/> spaces.
    /// </summary>
    public static byte[] UpdateNamingItsPrevious(string prev, int padding = 0)
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
        file.Object(9, new string(' ', padding) + first.ToString(CultureInfo.InvariantCulture));
        var second = file.Position;
        file.Text(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n4 1\n{file.OffsetOf(4):D10} 00000 n\r\n9 1\n{file.OffsetOf(9):D10} 00000 n\r\ntrailer\n<< /Size 10 /Root 1 0 R /Prev {prev.Replace("{offset}", first.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)} >>\nstartxref\n{second}\n%%EOF\n"));
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

        if (encoding == Rows.Raw)
        {
            return [.. data];
        }

        using var output = new MemoryStream();

        using (var zlib = new ZLibStream(output, CompressionLevel.NoCompression, leaveOpen: true))
        {
            zlib.Write([.. data]);
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
