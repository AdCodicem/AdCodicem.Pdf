using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

internal static partial class ConvergenceShapes
{
    /// <summary>The smallest cache the reader keeps, which <see cref="PdfReaderOptions.ObjectCacheCapacity"/> below it gives.</summary>
    internal const int SmallestCache = 64;

    /// <summary>
    /// The files of #312 and of the review of its closing: object stream 6 holds 7 and takes its parameters from 50, which
    /// object stream 5 holds, and whose value depends on how 5's decoding reads its last Flate step's predictor (see
    /// <see cref="HolderWithPredictedMember"/>): <c>7 0 R</c>, which closes 6's loop, or <c>77 0 R</c>, which leads nowhere.
    /// Each file makes 5's decoding depend on what the reader read before, or on its cache: the walk follows 50 only where 5
    /// decodes alike in every read order, and the verdict is the same whatever was read — at the default cache, and at the
    /// smallest, which lets go of a stream a decoding parsed before meeting it again.
    /// </summary>
    private static IEnumerable<ConvergenceShape> HolderShapes()
    {
        PdfObjectId[][] reads = [[new(50)], [new(5)], [new(6)], [new(7)], [new(5), new(7)], [new(50), new(7)], [new(7), new(50)]];

        foreach (var capacity in (int[])[8192, SmallestCache])
        {
            var cache = capacity == SmallestCache ? ", the smallest cache" : string.Empty;

            // 5's predictor is stream 20, whose /Length names 20 itself: 6's first Flate step reads 20, re-entering it, and the
            // reader reads 20 as null for the rest of 6's decoding, 5's included when it decodes inside it. 5 met 20 again
            // while its parse was in flight, and does not decode alike. With 20's /Length written directly, it does: 50 is
            // followed to 7, and 6 is circular.
            yield return new($"a holder reading a stream that names itself as its /Length{cache}", NullBefore("20 0 R"), PdfReaderLimits.Default, [.. reads, [new(20), new(7)]], capacity);
            yield return new($"a holder reading a stream whose /Length is written directly{cache}", NullBefore("2"), PdfReaderLimits.Default, [.. reads, [new(20), new(7)]], capacity);

            // 5's own /Length names 5, or names 30, whose /Length names 5: the reader parses 5 again while it holds it, and reads
            // it as null for the rest of 5's decoding, which takes 5 as its own parameters, unless a read before left 5 in its
            // cache.
            yield return new($"a holder whose /Length names itself{cache}", OwnLength("5 0 R"), PdfReaderLimits.Default, reads, capacity);
            yield return new($"a holder whose /Length names a stream naming it{cache}", OwnLength("30 0 R"), PdfReaderLimits.Default, [.. reads, [new(30), new(7)]], capacity);
            yield return new($"a holder whose /Length is written directly{cache}", OwnLength(string.Empty), PdfReaderLimits.Default, reads, capacity);

            // 5 reads stream 20 twice, whose /Length leads through a chain of streams back to 20: at 62 streams, the reader
            // re-enters 20 at its 65th load when 5 is decoded first, and is cut a load earlier when it decodes inside 6.
            foreach (var links in (int[])[61, 62, 63])
            {
                yield return new($"a holder reading a stream twice whose /Length leads back to it through {links} streams{cache}", ReentryAt(links), PdfReaderLimits.Default, [.. reads, [new(20), new(7)]], capacity);
            }

            // 5 parses stream 22 inside 21's parse, cut at the reader's deepest load; 80 streams later, a cache of 64 has let
            // go of 22, and 5 parses it again two levels higher, where it re-enters 22.
            yield return new($"a holder parsing a stream again once its cache let it go{cache}", Reparsed(40), PdfReaderLimits.Default, [.. reads, [new(22), new(7)]], capacity);
            yield return new($"a holder parsing a stream again with nothing between{cache}", Reparsed(0), PdfReaderLimits.Default, [.. reads, [new(22), new(7)]], capacity);

            // Object stream 3, walked first, parses 140, whose nest of 61 streams the reader's nesting cuts at 20; 5's decoding
            // reads 20, whose /Length is 140: taken there, the nest's cut re-enters 20.
            yield return new($"a holder taking a nest whose cut re-enters a stream in flight{cache}", NestCut(), PdfReaderLimits.Default, [.. reads, [new(20), new(7)]], capacity);
        }

        // #312: 7 takes its parameters from 100, which 9 holds; 9 from 170, which 11 holds; 100 is 5 0 R, which 7 holds.
        foreach (var parameters in (string[])["100, directly", "100, through 99, written in the file", "100, once stream 3 has read 170"])
        {
            yield return new(
                $"a loop through the value of an object whose stream needs another, {parameters}",
                LoopThroughAHolder(parameters),
                PdfReaderLimits.Default,
                [[new(5)], [new(6)], [new(100)], [new(170)], [new(100), new(6)], [new(170), new(6)], [new(5), new(6)]]);
        }

        // #163: 9, which holds 170, is first parsed at the reader's deepest load, its /Length cut there, after a read of 100;
        // at 61 streams it is parsed a level higher, and whole.
        yield return new(
            "a holder first parsed a level above the reader's deepest load",
            Shallow(61),
            PdfReaderLimits.Default,
            [[new(100)], [new(6)], [new(170)], [new(100), new(6)], [new(6), new(100)]]);

        // A rebuild redefines the stream holding 7's parameters: the walk follows the chain's, which needs nothing, and not
        // the rebuilt one, which the reader serves.
        yield return new(
            "a holder a rebuild defines again",
            RedefinedHolder(laterOnly: false),
            PdfReaderLimits.Default,
            [[new(30)], [new(6)], [new(100)], [new(5)], [new(30), new(6)], [new(30), new(100), new(6)], [new(6), new(30)], [new(8)]]);

        // 7's /N leads through 61 streams to 170, which 9 holds, met at the 63rd load: the reader's nesting cuts 9's decoding
        // there, and 9's /Length too, at a first read of 6.
        yield return new(
            "a holder met where the reader's nesting cuts its decoding",
            Edge(61, indirectLength: true, endstreamInside: true),
            PdfReaderLimits.Default,
            [[new(6)], [new(170)], [new(9)], [new(7)], [new(170), new(6)], [new(9), new(6)], [new(6), new(170)]]);
    }

    /// <summary>
    /// Gives the data of object stream 5, holding 50 as <c>7 0 R</c> read through a PNG predictor of 12 columns, and as
    /// <c>77 0 R</c> without one: its last Flate step's data is a row tag, 0, then <c>50 0 77 0 R </c>, and its <c>/First</c>
    /// is 6, which the tag, left in place when no predictor reads it, moves a byte. Compressed as many times as
    /// <paramref name="flates"/> says, the last but one step's data predicted when <paramref name="predicted"/> says so.
    /// </summary>
    internal static byte[] HolderWithPredictedMember(int flates, bool predicted)
    {
        byte[] data = [0, .. "50 0 77 0 R "u8.ToArray()];
        data = HostileInputTests.Compress(data);

        for (var step = 1; step < flates; step++)
        {
            data = HostileInputTests.Compress(predicted && step == 1 ? PngRows(data, 12) : data);
        }

        return data;
    }

    /// <summary>Gives <paramref name="data"/> as the rows of a PNG predictor of <paramref name="columns"/> bytes, each tagged 0.</summary>
    internal static byte[] PngRows(byte[] data, int columns)
    {
        var rows = (data.Length + columns - 1) / columns;
        var predicted = new byte[rows * (columns + 1)];

        for (var row = 0; row < rows; row++)
        {
            var length = Math.Min(columns, data.Length - (row * columns));
            data.AsSpan(row * columns, length).CopyTo(predicted.AsSpan((row * (columns + 1)) + 1));

            // Past the data, the last row is padded with blanks, which follow the last object.
            predicted.AsSpan((row * (columns + 1)) + 1 + length, columns - length).Fill((byte)' ');
        }

        return predicted;
    }

    /// <summary>
    /// Gives the data of an object stream holding <paramref name="members"/>, each followed by a blank, and where its first
    /// object starts in it, past the header that lists them.
    /// </summary>
    internal static (string Data, int First) ObjectStreamData(params (int Number, string Body)[] members)
    {
        var header = new StringBuilder();
        var contents = new StringBuilder();

        foreach (var (number, body) in members)
        {
            header.Append(CultureInfo.InvariantCulture, $"{number} {contents.Length} ");
            contents.Append(body).Append(' ');
        }

        return (header.ToString() + contents, header.Length);
    }

    /// <summary>
    /// Gives the entries and the data of an object stream holding <paramref name="members"/>, Flate-compressed, its
    /// dictionary holding <paramref name="entries"/> besides its own, <c>@L</c> standing for the data's length.
    /// </summary>
    internal static (string Entries, byte[]? Data) FlateObjectStream(string entries, params (int Number, string Body)[] members)
    {
        var (data, first) = ObjectStreamData(members);
        return (
            string.Create(CultureInfo.InvariantCulture, $"/Type /ObjStm /N {members.Length} /First {first} /Length @L /Filter /FlateDecode {entries}"),
            HostileInputTests.Compress(Encoding.Latin1.GetBytes(data)));
    }

    /// <summary>
    /// Writes a file of catalog 1, an empty page tree 2, <paramref name="objects"/> — each a dictionary's entries and, for a
    /// stream, its data, <c>@L</c> in the entries standing for the data's length —, the objects <paramref name="members"/>
    /// places in object streams, and a cross-reference stream after them.
    /// </summary>
    internal static byte[] StreamsAndMembers(SortedDictionary<int, (string Entries, byte[]? Data)> objects, Dictionary<int, (int Stream, int Index)> members)
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
        };

        foreach (var (number, (entries, data)) in objects)
        {
            rows[number] = (1, data is null
                ? file.Object(number, entries)
                : file.Object(number, $"<< {entries.Replace("@L", data.Length.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)} >>\nstream\n{Encoding.Latin1.GetString(data)}\nendstream"), 0);
        }

        foreach (var (number, (stream, index)) in members)
        {
            rows[number] = (2, stream, index);
        }

        var size = rows.Keys.Max() + 1;
        file.CrossReferenceStream(size, size + 1, rows, "/Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// Object stream 6, holding 7, <c>&lt;&lt; /Predictor 1 &gt;&gt;</c>, compressed once, and taking its parameters from
    /// <paramref name="parameters"/>; with <paramref name="others"/> besides, and 50 in 5.
    /// </summary>
    private static byte[] ReadingFifty(SortedDictionary<int, (string Entries, byte[]? Data)> others, string filters = "/FlateDecode", string parameters = "50 0 R", byte[]? data = null)
    {
        others[6] = ($"/Type /ObjStm /N 1 /First 4 /Length @L /Filter {filters} /DecodeParms {parameters}", data ?? HostileInputTests.Compress("7 0\n<< /Predictor 1 >>\n"u8.ToArray()));
        return StreamsAndMembers(others, new() { [50] = (5, 0), [7] = (6, 0) });
    }

    /// <summary>
    /// The file of the review of #312's closing: object stream 5 takes its last Flate step's predictor from stream 20,
    /// <c>/Predictor 12 /Columns 12</c>, whose <c>/Length</c> is <paramref name="length"/>; 6 takes the same stream as its
    /// first step's parameters, and 50 as its second's.
    /// </summary>
    internal static byte[] NullBefore(string length) =>
        ReadingFifty(
            new()
            {
                [20] = ($"/Predictor 12 /Columns 12 /Length {length}", "rr"u8.ToArray()),
                [5] = ("/Type /ObjStm /N 1 /First 6 /Length @L /Filter /FlateDecode /DecodeParms 20 0 R", HolderWithPredictedMember(1, predicted: false)),
            },
            "[/FlateDecode /FlateDecode]",
            "[20 0 R 50 0 R]",
            HostileInputTests.Compress(PngRows(HostileInputTests.Compress("7 0\n<< /Predictor 1 >>\n"u8.ToArray()), 12)));

    /// <summary>
    /// Object stream 5 takes its own dictionary as its parameters, <c>/Predictor 12 /Columns 12</c>, and its <c>/Length</c>
    /// from <paramref name="length"/>: itself, <c>5 0 R</c>; stream 30, whose <c>/Length</c> names 5, <c>30 0 R</c>; or,
    /// empty, the length of its data.
    /// </summary>
    internal static byte[] OwnLength(string length)
    {
        var objects = new SortedDictionary<int, (string Entries, byte[]? Data)>
        {
            [5] = ($"/Type /ObjStm /N 1 /First 6 /Length {(length.Length > 0 ? length : "@L")} /Filter /FlateDecode /DecodeParms 5 0 R /Predictor 12 /Columns 12", HolderWithPredictedMember(1, predicted: false)),
        };

        if (length == "30 0 R")
        {
            objects[30] = ("/Length 5 0 R", "x"u8.ToArray());
        }

        return ReadingFifty(objects);
    }

    /// <summary>
    /// Object stream 5 takes both its Flate steps' parameters from stream 20, <c>/Predictor 12 /Columns 12</c>, whose
    /// <c>/Length</c> leads through streams 100 onward, <paramref name="links"/> of them, each naming the next, the last
    /// naming 20.
    /// </summary>
    internal static byte[] ReentryAt(int links)
    {
        var objects = new SortedDictionary<int, (string Entries, byte[]? Data)>
        {
            [20] = ("/Predictor 12 /Columns 12 /Length 100 0 R", "rr"u8.ToArray()),
            [5] = ("/Type /ObjStm /N 1 /First 6 /Length @L /Filter [/FlateDecode /FlateDecode] /DecodeParms [20 0 R 20 0 R]", HolderWithPredictedMember(2, predicted: true)),
        };

        for (var link = 0; link < links; link++)
        {
            objects[100 + link] = (string.Create(CultureInfo.InvariantCulture, $"/Length {(link == links - 1 ? 20 : 101 + link)} 0 R"), "x"u8.ToArray());
        }

        return ReadingFifty(objects);
    }

    /// <summary>
    /// Object stream 5 runs five Flate steps, taking their parameters from 21, 24, 25, 22 and 22: 21's <c>/Length</c> names
    /// stream 22, <c>/Predictor 12 /Columns 12</c>, whose own leads through 62 streams back to 22; 24 and 25 each head
    /// <paramref name="spacers"/> streams by <c>/Length</c>, which a cache of 64 objects lets go of 22 past.
    /// </summary>
    internal static byte[] Reparsed(int spacers)
    {
        var objects = new SortedDictionary<int, (string Entries, byte[]? Data)>
        {
            [21] = ("/Length 22 0 R", "pp"u8.ToArray()),
            [22] = ("/Predictor 12 /Columns 12 /Length 100 0 R", "rr"u8.ToArray()),
            [24] = (spacers > 0 ? "/Length 300 0 R" : "/Length 2", "ss"u8.ToArray()),
            [25] = (spacers > 0 ? "/Length 400 0 R" : "/Length 2", "ss"u8.ToArray()),
            [5] = (
                "/Type /ObjStm /N 1 /First 6 /Length @L /Filter [/FlateDecode /FlateDecode /FlateDecode /FlateDecode /FlateDecode] /DecodeParms [21 0 R 24 0 R 25 0 R 22 0 R 22 0 R]",
                HolderWithPredictedMember(5, predicted: true)),
        };

        for (var link = 0; link < 62; link++)
        {
            objects[100 + link] = (string.Create(CultureInfo.InvariantCulture, $"/Length {(link == 61 ? 22 : 101 + link)} 0 R"), "x"u8.ToArray());
        }

        foreach (var head in (int[])[300, 400])
        {
            for (var spacer = 0; spacer < spacers; spacer++)
            {
                objects[head + spacer] = (spacer == spacers - 1 ? "/Length 1" : string.Create(CultureInfo.InvariantCulture, $"/Length {head + 1 + spacer} 0 R"), "y"u8.ToArray());
            }
        }

        return ReadingFifty(objects);
    }

    /// <summary>
    /// Object stream 3, holding 8, takes its <c>/Length</c> from stream 140, which heads streams 141 to 201 by
    /// <c>/Length</c>, the last naming stream 20, <c>/Predictor 12 /Columns 12</c>, whose own <c>/Length</c> names 140;
    /// object stream 5 takes both its Flate steps' parameters from 20.
    /// </summary>
    internal static byte[] NestCut()
    {
        var objects = new SortedDictionary<int, (string Entries, byte[]? Data)>
        {
            [3] = ("/Type /ObjStm /N 1 /First 4 /Length 140 0 R /Filter /FlateDecode", HostileInputTests.Compress("8 0\n<< >>\n"u8.ToArray())),
            [20] = ("/Predictor 12 /Columns 12 /Length 140 0 R", "rr"u8.ToArray()),
            [5] = ("/Type /ObjStm /N 1 /First 6 /Length @L /Filter [/FlateDecode /FlateDecode] /DecodeParms [20 0 R 20 0 R]", HolderWithPredictedMember(2, predicted: true)),
        };

        for (var stream = 140; stream <= 201; stream++)
        {
            objects[stream] = (string.Create(CultureInfo.InvariantCulture, $"/Length {(stream == 201 ? 20 : stream + 1)} 0 R"), "x"u8.ToArray());
        }

        objects[6] = ("/Type /ObjStm /N 1 /First 4 /Length @L /Filter /FlateDecode /DecodeParms 50 0 R", HostileInputTests.Compress("7 0\n<< /Predictor 1 >>\n"u8.ToArray()));
        return StreamsAndMembers(objects, new() { [50] = (5, 0), [7] = (6, 0), [8] = (3, 0) });
    }

    /// <summary>
    /// #312's file: object stream 7 holds 5 and 6, and takes its parameters from 100, directly or through 99, written in the
    /// file; 9 holds 100, <c>5 0 R</c>, and takes its own from 170, which 11 holds, <c>171 0 R</c>; 171, written in the file,
    /// is <c>&lt;&lt; /Predictor 1 &gt;&gt;</c>. When <paramref name="parameters"/> ends with 170, object stream 3, walked
    /// first, takes its parameters from 170 as well; when it ends with a third, 11 takes its own from 180, which 13 holds,
    /// needing nothing.
    /// </summary>
    internal static byte[] LoopThroughAHolder(string parameters)
    {
        var throughNinetyNine = parameters.Contains("99", StringComparison.Ordinal);
        var objects = new SortedDictionary<int, (string Entries, byte[]? Data)>
        {
            [7] = FlateObjectStream(throughNinetyNine ? "/DecodeParms 99 0 R" : "/DecodeParms 100 0 R", (5, "<< /Predictor 1 >>"), (6, "<< /Title (six) >>")),
            [9] = FlateObjectStream("/DecodeParms 170 0 R", (100, "5 0 R")),
            [11] = FlateObjectStream(parameters.EndsWith("a third", StringComparison.Ordinal) ? "/DecodeParms 180 0 R" : string.Empty, (170, "171 0 R")),
            [171] = ("<< /Predictor 1 >>", null),
        };
        var members = new Dictionary<int, (int Stream, int Index)> { [5] = (7, 0), [6] = (7, 1), [100] = (9, 0), [170] = (11, 0) };

        if (throughNinetyNine)
        {
            objects[99] = ("100 0 R", null);
        }

        if (parameters.EndsWith("170", StringComparison.Ordinal))
        {
            objects[3] = FlateObjectStream("/DecodeParms 170 0 R", (4, "<< >>"));
            members[4] = (3, 0);
        }

        if (parameters.EndsWith("a third", StringComparison.Ordinal))
        {
            objects[13] = FlateObjectStream(string.Empty, (180, "<< /Predictor 1 >>"));
            members[180] = (13, 0);
        }

        return StreamsAndMembers(objects, members);
    }

    /// <summary>
    /// The file of #163's residual: object stream 7 holds 5 and 6, and takes its parameters from 170; 9, written as it is,
    /// holds 171, a string holding <c>endstream</c>, and 170, <c>5 0 R</c>, its <c>/Length</c> taken from 50, the true
    /// length; streams 100 onward, <paramref name="nest"/> of them, each take their <c>/Length</c> from the next, the last
    /// from 170. Read first, 100 makes the reader parse 9 at its deepest load, where 50 is cut: 9 ends at the
    /// <c>endstream</c> inside its data, and 170 reads as null for good.
    /// </summary>
    internal static byte[] Shallow(int nest)
    {
        var (nine, first) = ObjectStreamData((171, "(\nendstream\nendobj\n)"), (170, "5 0 R"));
        var objects = new SortedDictionary<int, (string Entries, byte[]? Data)>
        {
            [7] = FlateObjectStream("/DecodeParms 170 0 R", (5, "<< /Predictor 1 >>"), (6, "<< /Title (six) >>")),
            [9] = (string.Create(CultureInfo.InvariantCulture, $"/Type /ObjStm /N 2 /First {first} /Length 50 0 R"), Encoding.Latin1.GetBytes(nine)),
            [50] = (nine.Length.ToString(CultureInfo.InvariantCulture), null),
        };

        for (var stream = 0; stream < nest; stream++)
        {
            objects[100 + stream] = (string.Create(CultureInfo.InvariantCulture, $"/Length {(stream + 1 < nest ? 101 + stream : 170)} 0 R"), "ab"u8.ToArray());
        }

        return StreamsAndMembers(objects, new() { [5] = (7, 0), [6] = (7, 1), [171] = (9, 0), [170] = (9, 1) });
    }

    /// <summary>
    /// Object stream 7, written as it is, holds 5, 2, and 6, and takes its <c>/N</c> from stream 100, which heads
    /// <paramref name="streams"/> streams by <c>/Length</c>, the last naming 180, written in the file, <c>170 0 R</c>; 9 holds
    /// 171, a string holding <c>endstream</c> when <paramref name="endstreamInside"/> says so, and 170, <c>5 0 R</c>, its
    /// <c>/Length</c> taken from 50, the true length, when <paramref name="indirectLength"/> says so.
    /// </summary>
    internal static byte[] Edge(int streams, bool indirectLength, bool endstreamInside)
    {
        var (nine, first) = ObjectStreamData((171, endstreamInside ? "(\nendstream\nendobj\n)" : "(...................)"), (170, "5 0 R"));
        var (seven, sevenFirst) = ObjectStreamData((5, "2"), (6, "<< /Title (six) >>"));
        var objects = new SortedDictionary<int, (string Entries, byte[]? Data)>
        {
            [7] = (string.Create(CultureInfo.InvariantCulture, $"/Type /ObjStm /N 100 0 R /First {sevenFirst} /Length @L"), Encoding.Latin1.GetBytes(seven)),
            [9] = (string.Create(CultureInfo.InvariantCulture, $"/Type /ObjStm /N 2 /First {first} /Length {(indirectLength ? "50 0 R" : "@L")}"), Encoding.Latin1.GetBytes(nine)),
            [50] = (nine.Length.ToString(CultureInfo.InvariantCulture), null),
            [180] = ("170 0 R", null),
        };

        for (var stream = 0; stream < streams; stream++)
        {
            objects[100 + stream] = (string.Create(CultureInfo.InvariantCulture, $"/Length {(stream + 1 < streams ? 101 + stream : 180)} 0 R"), "ab"u8.ToArray());
        }

        return StreamsAndMembers(objects, new() { [5] = (7, 0), [6] = (7, 1), [171] = (9, 0), [170] = (9, 1) });
    }

    /// <summary>
    /// Object stream 7 holds 5, <c>&lt;&lt; /Predictor 10 /Columns 12 &gt;&gt;</c>, 6 and 8, and takes its parameters from
    /// 100, which the chain's index places in 9, <c>&lt;&lt; /Predictor 1 &gt;&gt;</c>, needing nothing. A later object 9,
    /// which no row places, holds 100 through a PNG predictor of 12 columns it takes from 5: <c>8 0 R</c> read through it,
    /// <c>88 0 R</c> without. The row of 30 leads nowhere, so that a load of it rebuilds the index, which finds the later 9.
    /// When <paramref name="laterOnly"/> says so, the chain's row places 9 at the later definition, and there is no 30.
    /// </summary>
    internal static byte[] RedefinedHolder(bool laterOnly)
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [7] = (1, Flate(7, "5 0 6 32 8 51 ", "<< /Predictor 10 /Columns 12 >> << /Title (six) >> << /Predictor 1 >> ", "/N 3 /DecodeParms 100 0 R"), 0),
            [5] = (2, 7, 0),
            [6] = (2, 7, 1),
            [8] = (2, 7, 2),
            [100] = (2, 9, 0),
        };

        var chain = Flate(9, "100 0 ", "<< /Predictor 1 >> ", "/N 1");
        var later = file.Object(9, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First 7 /Filter /FlateDecode /DecodeParms 5 0 R /Length {LaterNine.Length} >>\nstream\n{LaterNine}\nendstream"));
        rows[9] = (1, laterOnly ? later : chain, 0);

        if (!laterOnly)
        {
            rows[30] = (1, 1, 0);
            rows[31] = (2, 30, 0);
        }

        file.CrossReferenceStream(200, 201, rows, "/Root 1 0 R");
        return file.Bytes;

        long Flate(int number, string header, string members, string entries)
        {
            var data = Encoding.Latin1.GetString(HostileInputTests.Compress(Encoding.Latin1.GetBytes(header + members)));
            return file.Object(
                number,
                string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm {entries} /First {header.Length} /Filter /FlateDecode /Length {data.Length} >>\nstream\n{data}\nendstream"));
        }
    }

    /// <summary>
    /// Gives the reads a generated file of <see cref="GeneratedHolders"/> is read with before validating: each holder's
    /// object, the stream judged and its objects, the streams of predictors, and some of them in turn.
    /// </summary>
    internal static PdfObjectId[][] GeneratedReads =>
    [
        [new(50)], [new(51)], [new(52)], [new(3)], [new(4)], [new(6)], [new(7)], [new(8)], [new(20)], [new(21)],
        [new(50), new(7)], [new(51), new(8)], [new(20), new(7)], [new(21), new(52), new(7)], [new(7), new(50)], [new(52), new(51), new(50)],
    ];

    /// <summary>
    /// Writes a file drawn from <paramref name="seed"/>, of the parts that make what an object stream's decoding gives
    /// depend on what the reader read before (#312). Object stream 6 holds 7 and 8, and takes its two Flate steps'
    /// parameters from a stream of predictor, a nest of streams or nothing, then from an object of one of the holders 3, 4
    /// and 5. Holder <c>3 + i</c> holds <c>50 + i</c>, which reads as 7, 8 or 9 — 9 written in the file — where its second
    /// Flate step reads a predictor of 12 columns, and as a number no object has where it does not; it takes its first step's
    /// parameters from nothing, a stream of predictor, a nest or another holder's object, and its second's from a predictor
    /// written directly, a stream of predictor or another holder's object. Streams of predictor 20 and 21 take their
    /// <c>/Length</c> from 2, themselves, each other, a link to themselves, a nest of 1, 30, 61, 62 or 63 streams leading
    /// back to them, or a holder's object; nests 100, 200 and 300 end at a stream of predictor, at 2, or at a holder's
    /// object.
    /// </summary>
    internal static byte[] GeneratedHolders(int seed)
    {
        var random = new Random(seed);
        var objects = new SortedDictionary<int, (string Entries, byte[]? Data)>
        {
            [9] = ("<< /Predictor 1 >>", null),
        };
        var members = new Dictionary<int, (int Stream, int Index)> { [7] = (6, 0), [8] = (6, 1) };

        string Holder() => string.Create(CultureInfo.InvariantCulture, $"{50 + random.Next(3)} 0 R");

        string Nest(int head)
        {
            var count = (int[])[1, 30, 61, 62, 63];
            var length = count[random.Next(count.Length)];
            var last = random.Next(3) switch
            {
                0 => string.Create(CultureInfo.InvariantCulture, $"{20 + random.Next(2)} 0 R"),
                1 => "2",
                _ => Holder(),
            };

            for (var stream = 0; stream < length; stream++)
            {
                objects[head + stream] = (string.Create(CultureInfo.InvariantCulture, $"/Length {(stream + 1 < length ? $"{head + stream + 1} 0 R" : last)}"), "x"u8.ToArray());
            }

            return string.Create(CultureInfo.InvariantCulture, $"{head} 0 R");
        }

        for (var predictor = 0; predictor < 2; predictor++)
        {
            var number = 20 + predictor;
            var length = random.Next(6) switch
            {
                0 => "2",
                1 => string.Create(CultureInfo.InvariantCulture, $"{number} 0 R"),
                2 => string.Create(CultureInfo.InvariantCulture, $"{40 + predictor} 0 R"),
                3 => string.Create(CultureInfo.InvariantCulture, $"{30 + predictor} 0 R"),
                4 => Nest(1000 + (100 * predictor)),
                _ => Holder(),
            };
            objects[number] = ($"/Predictor 12 /Columns 12 /Length {length}", "rr"u8.ToArray());
            objects[30 + predictor] = (string.Create(CultureInfo.InvariantCulture, $"{number} 0 R"), null);
        }

        // 40 names 21 and 41 names 20, which each other's /Length reach through them.
        objects[40] = ("21 0 R", null);
        objects[41] = ("20 0 R", null);

        for (var holder = 0; holder < 3; holder++)
        {
            var first = random.Next(4) switch
            {
                0 => "null",
                1 => string.Create(CultureInfo.InvariantCulture, $"{20 + random.Next(2)} 0 R"),
                2 => Nest(100 * (holder + 1)),
                _ => Holder(),
            };
            var second = random.Next(3) switch
            {
                0 => "<< /Predictor 12 /Columns 12 >>",
                1 => string.Create(CultureInfo.InvariantCulture, $"{20 + random.Next(2)} 0 R"),
                _ => Holder(),
            };
            var target = 7 + random.Next(3);
            var data = HostileInputTests.Compress(Encoding.Latin1.GetBytes(string.Create(CultureInfo.InvariantCulture, $"\0{50 + holder} 0 {target}{target} 0 R ")));
            data = HostileInputTests.Compress(first is "20 0 R" or "21 0 R" ? PngRows(data, 12) : data);
            objects[3 + holder] = ($"/Type /ObjStm /N 1 /First 6 /Length @L /Filter [/FlateDecode /FlateDecode] /DecodeParms [{first} {second}]", data);
            members[50 + holder] = (3 + holder, 0);
        }

        var parameters = random.Next(3) switch
        {
            0 => "null",
            1 => string.Create(CultureInfo.InvariantCulture, $"{20 + random.Next(2)} 0 R"),
            _ => Nest(500),
        };
        var (six, sixFirst) = ObjectStreamData((7, "<< /Predictor 1 >>"), (8, "<< /Predictor 1 >>"));
        objects[6] = (
            string.Create(CultureInfo.InvariantCulture, $"/Type /ObjStm /N 2 /First {sixFirst} /Length @L /Filter [/FlateDecode /FlateDecode] /DecodeParms [{parameters} {Holder()}]"),
            HostileInputTests.Compress(HostileInputTests.Compress(Encoding.Latin1.GetBytes(six))));
        return StreamsAndMembers(objects, members);
    }

    /// <summary>The data of the later object 9 of <see cref="RedefinedHolder"/>: a row tag, then 100's header and value.</summary>
    private static string LaterNine => Encoding.Latin1.GetString(HostileInputTests.Compress(Encoding.Latin1.GetBytes("\0" + "100 0 88 0 R")));
}
