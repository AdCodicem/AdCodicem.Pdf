using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

internal static partial class ConvergenceShapes
{
    /// <summary>
    /// The files of #128: object streams that each need a member they hold, where a rebuild of the index no longer places a
    /// stream where its row does, or an object the stream needs that member through, so that the walk of their dependencies
    /// judged them through whatever index a read before it, or one of its own, had left. Each is read before validating as
    /// it was measured: every number its index holds, one at a time, and for the first five <c>/Size</c> and
    /// <c>/Size</c> + 5 as well; then, in order in one document, the reads its measure made before walking the dependencies
    /// or looking at the index: the broken stream, the two streams that need their members, and for the first five object
    /// 12. The numbers after or before them in the names are the broken stream's, which the walk takes in that order.
    /// </summary>
    private static IEnumerable<ConvergenceShape> ObjectStreamShapes()
    {
        (int Number, string Body)[] six = [(5, "<< /Predictor 1 >>"), (6, "<< /Title (six) >>")];
        (int Number, string Body)[] sixteen = [(15, "<< /Predictor 1 >>"), (16, "<< /Title (sixteen) >>")];

        // Object streams 7 and 8 each need, through /DecodeParms, a member they hold, and a comment after each header's number
        // hides them from a rebuild; the row of stream 30, which holds 31, points into the binary comment. Reading 30 or 31
        // rebuilt the index and lost both circular findings.
        yield return new(
            "circular object streams, headers split after the number, a broken stream after them",
            ObjectStreamPairAfterBinaryComment(("7 %c\n0 obj\n", "/DecodeParms 5 0 R", six), ("8 %c\n0 obj\n", "/DecodeParms 15 0 R", sixteen), 30),
            PdfReaderLimits.Default,
            ObjectStreamReads([.. Enumerable.Range(0, 42), 46], [30, 7, 8, 12]));

        // Stream 7 needs its member 9 as its filter, its header split before obj; stream 8 needs 17 as its predictor, and a
        // later plain object 8 is what a rebuild keeps; the rows are 1 2 1 wide, and as in the first, the row of stream 30
        // points into the binary comment. Reading 30 or 31 lost both circular findings.
        yield return new(
            "circular object streams, one header split before obj, the other redefined after them",
            ObjectStreamPairAfterBinaryComment(
                ("7 0\n%h\nobj\n", "/Filter [9 0 R]", [(9, "/ASCIIHexDecode"), (10, "(ten)")]),
                ("8 0 obj\n", "/DecodeParms << /Predictor 17 0 R >>", [(17, "1"), (18, "(eighteen)")]),
                30,
                later: "8 0 obj\n<< /Note (a later plain object 8) >>\nendobj\n",
                narrow: true),
            PdfReaderLimits.Default,
            ObjectStreamReads([.. Enumerable.Range(0, 37), 41], [30, 7, 8, 12]));

        // As the first, but stream 8's header is whole, so a rebuild finds it where its row places it. Reading 30 or 31 lost
        // the circular finding of 7 alone.
        yield return new(
            "circular object streams, only the first header split, a broken stream after them",
            ObjectStreamPairAfterBinaryComment(("7 %c\n0 obj\n", "/DecodeParms 5 0 R", six), ("8 0 obj\n", "/DecodeParms 15 0 R", sixteen), 30),
            PdfReaderLimits.Default,
            ObjectStreamReads([.. Enumerable.Range(0, 42), 46], [30, 7, 8, 12]));

        // As the first, but the broken stream is 3, which the walk loaded before 7 and 8: the rebuild that load set off hid
        // both circular findings from every validation, a fresh one included.
        yield return new(
            "circular object streams, headers split after the number, a broken stream before them",
            ObjectStreamPairAfterBinaryComment(("7 %c\n0 obj\n", "/DecodeParms 5 0 R", six), ("8 %c\n0 obj\n", "/DecodeParms 15 0 R", sixteen), 3),
            PdfReaderLimits.Default,
            ObjectStreamReads([.. Enumerable.Range(0, 42), 46], [3, 7, 8, 12]));

        // As the first, but stream 7's header is whole, and it needs its member 5 through regular object 12, whose header a
        // comment splits after its number. Reading 30 or 31 lost both circular findings, 7's because 12 no longer loaded.
        yield return new(
            "an object stream needing its member through an object whose header is split",
            ObjectStreamPairAfterBinaryComment(
                ("7 0 obj\n", "/DecodeParms 12 0 R", six),
                ("8 %c\n0 obj\n", "/DecodeParms 15 0 R", sixteen),
                30,
                twelve: "12 %c\n0 obj\n<< /Predictor 5 0 R >>\nendobj\n"),
            PdfReaderLimits.Default,
            ObjectStreamReads([.. Enumerable.Range(0, 42), 46], [30, 7, 8, 12]));

        // Streams 7 and 8 each need a member they hold, a comment splitting their headers before obj; stream 20, which no row
        // names, lists 8 again, and the row of stream 30, which holds 31, points to offset 1. Reading 30 or 31 lost both
        // circular findings.
        yield return new(
            "circular object streams split before obj, one listed again, a broken stream after them",
            ObjectStreamPairListedAgain((7, 5, 6), (8, 15, 16), 20, 30, 32),
            PdfReaderLimits.Default,
            ObjectStreamReads([.. Enumerable.Range(0, 33)], [30, 7, 8]));

        // The same file renumbered: streams 9 and 12, listing stream 25, broken stream 40. Reading 40 or 41 lost both circular
        // findings.
        yield return new(
            "circular object streams split before obj, one listed again, renumbered",
            ObjectStreamPairListedAgain((9, 3, 4), (12, 13, 14), 25, 40, 44),
            PdfReaderLimits.Default,
            ObjectStreamReads([.. Enumerable.Range(0, 45)], [40, 9, 12]));

        // The file of streams 7, 8 and 20 above, with the broken stream numbered 3, which the walk loaded first: the rebuild
        // hid 7 and 8 from every validation, a fresh one included.
        yield return new(
            "circular object streams split before obj, one listed again, a broken stream before them",
            ObjectStreamPairListedAgain((7, 5, 6), (8, 15, 16), 20, 3, 32),
            PdfReaderLimits.Default,
            ObjectStreamReads([.. Enumerable.Range(0, 33)], [3, 7, 8]));

        // The file of streams 7, 8 and 20 above, stream 7's /DecodeParms also naming object 9, whose row points to offset 1:
        // loading 9 rebuilt the index in the middle of the walk and hid 8 from every validation, and validating again, or
        // reading 9, 30 or 31 first, hid 7 too.
        yield return new(
            "circular object streams split before obj, one needing a broken object too",
            ObjectStreamPairListedAgain((7, 5, 6), (8, 15, 16), 20, 30, 32, firstNeeds: "[9 0 R 5 0 R]", alsoBroken: 9),
            PdfReaderLimits.Default,
            ObjectStreamReads([.. Enumerable.Range(0, 33)], [30, 7, 8]));

        // #277's review: object streams 10 and 11 take their parameters from each other's objects, and 12 from 10's; what the
        // reader serves of 6 depends on which of 10 and 11 it decoded first. The walk read 6 through the reader and followed
        // it to 7, which 12 holds: 12 was found circular after nothing was read, or 6 alone, or 6 then 5 and 7, and not after
        // 5, 7 or 8.
        yield return new(
            "object streams taking their parameters from one another's objects, a third leading into their loop",
            ObjectStreamsTakingParametersFromOneAnother(),
            PdfReaderLimits.Default,
            [[new(5)], [new(6)], [new(7)], [new(8)], [new(5), new(6), new(7)], [new(7), new(6), new(5)], [new(6), new(5), new(7)]]);
    }

    /// <summary>
    /// Object streams 10, 11 and 12, Flate-compressed, which take their parameters from one another's members (#277's
    /// review): 10 holds 5, <c>&lt;&lt; /Predictor 10 /Columns 11 &gt;&gt;</c>, and 8, <c>&lt;&lt; /Predictor 1 &gt;&gt;</c>,
    /// and takes its parameters from 6; 11 holds 6, its data reading <c>7 0 R</c> through a PNG predictor of 11 columns and
    /// <c>77 0 R</c> without one, and takes its parameters from 5; 12 holds 7, <c>&lt;&lt; /Predictor 1 &gt;&gt;</c>, and
    /// takes its parameters from 8. The index is a cross-reference stream, object 13.
    /// </summary>
    internal static byte[] ObjectStreamsTakingParametersFromOneAnother()
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [5] = (2, 10, 0),
            [8] = (2, 10, 1),
            [6] = (2, 11, 0),
            [7] = (2, 12, 0),
        };

        const string Five = "<< /Predictor 10 /Columns 11 >> ";
        rows[10] = (1, Flate(10, string.Create(CultureInfo.InvariantCulture, $"5 0 8 {Five.Length} "), Five + "<< /Predictor 1 >> ", "/N 2", "/DecodeParms 6 0 R"), 0);

        // One row of a PNG predictor: its tag, 0, then the 11 bytes as they are. Without the predictor, the tag is a blank
        // before the header, and /First lands a byte earlier.
        rows[11] = (1, Flate(11, "\0", "6 0 77 0 R ", "/N 1 /First 5", "/DecodeParms 5 0 R"), 0);
        rows[12] = (1, Flate(12, "7 0 ", "<< /Predictor 1 >> ", "/N 1", "/DecodeParms 8 0 R"), 0);
        file.CrossReferenceStream(13, 14, rows, "/Root 1 0 R");
        return file.Bytes;

        long Flate(int number, string header, string members, string count, string entries)
        {
            var data = Encoding.Latin1.GetString(HostileInputTests.Compress(Encoding.Latin1.GetBytes(header + members)));
            var first = count.Contains("/First", StringComparison.Ordinal) ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" /First {header.Length}");
            return file.Object(
                number,
                string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm {count}{first} /Filter /FlateDecode {entries} /Length {data.Length} >>\nstream\n{data}\nendstream"));
        }
    }

    /// <summary>
    /// Gives one way of reading per number of <paramref name="alone"/>, that object alone, then one that reads the objects
    /// of <paramref name="together"/>, in order.
    /// </summary>
    private static PdfObjectId[][] ObjectStreamReads(int[] alone, int[] together)
    {
        var reads = new PdfObjectId[alone.Length + 1][];

        for (var index = 0; index < alone.Length; index++)
        {
            reads[index] = [new PdfObjectId(alone[index])];
        }

        var last = new PdfObjectId[together.Length];

        for (var index = 0; index < together.Length; index++)
        {
            last[index] = new PdfObjectId(together[index]);
        }

        reads[alone.Length] = last;
        return reads;
    }

    /// <summary>
    /// Writes, after a binary comment line, catalog 1, an empty page tree 2, <paramref name="twelve"/>, object streams 7 and 8
    /// — each after its own header, its members followed by a space, what it needs among its entries —,
    /// <paramref name="later"/>, and a cross-reference stream whose row for <paramref name="broken"/> points into the binary
    /// comment, and which places <paramref name="broken"/> + 1 in it.
    /// </summary>
    /// <param name="twelve">Object 12, header and all, which a row places; empty for none.</param>
    /// <param name="narrow">Whether the rows are <c>/W [1 2 1]</c>, in cross-reference stream 35, rather than <c>[1 4 2]</c> in 40.</param>
    private static byte[] ObjectStreamPairAfterBinaryComment(
        (string Header, string Entries, (int Number, string Body)[] Members) seven,
        (string Header, string Entries, (int Number, string Body)[] Members) eight,
        int broken,
        string twelve = "",
        string later = "",
        bool narrow = false)
    {
        var file = new HandWrittenFile(version: "1.5");
        file.Raw("%âãÏÓ\n");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [broken] = (1, 9, 0),
            [broken + 1] = (2, broken, 0),
        };

        if (twelve.Length > 0)
        {
            rows[12] = (1, file.Position, 0);
            file.Raw(twelve);
        }

        Write(7, seven);
        Write(8, eight);
        file.Raw(later);

        if (narrow)
        {
            ObjectStreamNarrowCrossReference(file, 35, rows);
        }
        else
        {
            file.CrossReferenceStream(40, 41, rows, "/Root 1 0 R");
        }

        return file.Bytes;

        void Write(int number, (string Header, string Entries, (int Number, string Body)[] Members) stream)
        {
            var offsets = new StringBuilder();
            var members = new StringBuilder();

            for (var index = 0; index < stream.Members.Length; index++)
            {
                offsets.Append(CultureInfo.InvariantCulture, $"{stream.Members[index].Number} {members.Length} ");
                members.Append(stream.Members[index].Body).Append(' ');
                rows[stream.Members[index].Number] = (2, number, index);
            }

            rows[number] = (1, ObjectStreamAfter(file, stream.Header, stream.Entries, stream.Members.Length, offsets.ToString(), members.ToString()), 0);
        }
    }

    /// <summary>
    /// Writes catalog 1, an empty page tree 2, object streams <paramref name="first"/> and <paramref name="second"/> — each
    /// holding the object it needs through <c>/DecodeParms</c> and another, a comment splitting its header before
    /// <c>obj</c> —, a stream <paramref name="listing"/> no row names that lists <paramref name="second"/>'s number, and
    /// cross-reference stream <paramref name="crossReference"/>, whose row for <paramref name="broken"/> points to offset 1,
    /// and which places <paramref name="broken"/> + 1 in it.
    /// </summary>
    /// <param name="firstNeeds">What the first stream's <c>/DecodeParms</c> holds instead of a reference to its member.</param>
    /// <param name="alsoBroken">Another number whose row points to offset 1; 0 for none.</param>
    private static byte[] ObjectStreamPairListedAgain(
        (int Stream, int Needed, int Other) first,
        (int Stream, int Needed, int Other) second,
        int listing,
        int broken,
        int crossReference,
        string firstNeeds = "",
        int alsoBroken = 0)
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [first.Stream] = (1, Write(first, firstNeeds.Length > 0 ? firstNeeds : Reference(first.Needed), "six"), 0),
            [second.Stream] = (1, Write(second, Reference(second.Needed), "sixteen"), 0),
            [first.Needed] = (2, first.Stream, 0),
            [first.Other] = (2, first.Stream, 1),
            [second.Needed] = (2, second.Stream, 0),
            [second.Other] = (2, second.Stream, 1),
            [broken] = (1, 1, 0),
            [broken + 1] = (2, broken, 0),
        };

        if (alsoBroken > 0)
        {
            rows[alsoBroken] = (1, 1, 0);
        }

        var listed = string.Create(CultureInfo.InvariantCulture, $"{second.Stream} 0 ");
        file.Object(
            listing,
            string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First {listed.Length} /Length {listed.Length + 5} >>\nstream\n{listed}<< >>\nendstream"));
        file.CrossReferenceStream(crossReference, crossReference + 1, rows, "/Root 1 0 R");
        return file.Bytes;

        long Write((int Stream, int Needed, int Other) stream, string needs, string title) =>
            ObjectStreamAfter(
                file,
                string.Create(CultureInfo.InvariantCulture, $"{stream.Stream} 0 %x\nobj\n"),
                "/DecodeParms " + needs,
                2,
                string.Create(CultureInfo.InvariantCulture, $"{stream.Needed} 0 {stream.Other} 19 "),
                "<< /Predictor 1 >> << /Title (" + title + ") >>");

        static string Reference(int number) => string.Create(CultureInfo.InvariantCulture, $"{number} 0 R");
    }

    /// <summary>
    /// Writes, after <paramref name="header"/>, an object stream of <paramref name="count"/> members whose data is
    /// <paramref name="offsets"/> then <paramref name="members"/>, its dictionary holding <paramref name="entries"/> between
    /// <c>/First</c> and <c>/Length</c>, and gives where the header starts.
    /// </summary>
    private static long ObjectStreamAfter(HandWrittenFile file, string header, string entries, int count, string offsets, string members)
    {
        var at = file.Position;
        file.Raw(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{header}<< /Type /ObjStm /N {count} /First {offsets.Length} {entries} /Length {offsets.Length + members.Length} >>\nstream\n{offsets}{members}\nendstream\nendobj\n"));
        return at;
    }

    /// <summary>
    /// Writes cross-reference stream <paramref name="number"/> of the numbers up to it, <c>/W [1 2 1]</c>, one row each from
    /// <paramref name="rows"/> — its own aside, a number it lacks is free —, and the <c>startxref</c> naming it.
    /// </summary>
    private static void ObjectStreamNarrowCrossReference(HandWrittenFile file, int number, Dictionary<int, (int Type, long Field2, int Field3)> rows)
    {
        var at = file.Position;
        var data = new StringBuilder();

        for (var row = 0; row <= number; row++)
        {
            var (type, field2, field3) = row == number ? (1, at, 0)
                : row == 0 ? (0, 0L, 255)
                : rows.TryGetValue(row, out var given) ? given : (0, 0L, 0);
            data.Append((char)type).Append((char)((field2 >> 8) & 0xFF)).Append((char)(field2 & 0xFF)).Append((char)field3);
        }

        file.Object(
            number,
            string.Create(CultureInfo.InvariantCulture, $"<< /Type /XRef /Size {number + 1} /W [1 2 1] /Root 1 0 R /Length {data.Length} >>\nstream\n{data}\nendstream"));
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"startxref\n{at}\n%%EOF\n"));
    }
}
