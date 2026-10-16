using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

internal static partial class ConvergenceShapes
{
    /// <summary>A page tree root listing no page.</summary>
    private const string LiveReadEmptyPages = "<< /Type /Pages /Kids [] /Count 0 >>";

    /// <summary>The head of a classic table's free list, with its line feed.</summary>
    private const string LiveReadFreeHead = "0000000000 65535 f \n";

    /// <summary>A free row of generation 0, with its line feed.</summary>
    private const string LiveReadFree = "0000000000 00000 f \n";

    /// <summary>
    /// The files in which a rule read the reader as it stood when the rule ran — whether the index holds an object, where it
    /// filled, what a reference in a trailer or in an object stream's header resolves to, whether the index was rebuilt, an
    /// object's generation —, so that a read before validating which rebuilt the index changed the report. Beside them are
    /// nine controls, in which such a read changed no finding. Each is read before validating as it was measured: the one
    /// object whose read rebuilt the index or, in two controls, relocated it; in one control the document rebuilt its index
    /// as it opened, so the read before validating finds it rebuilt already.
    /// </summary>
    private static IEnumerable<ConvergenceShape> LiveReadShapes()
    {
        // Catalog 1, the root, is indexed only by the oldest of three sections, which the section count cuts off; the newest
        // places another catalog, 7, and object 5 at object 1's header. Reading 5 first rebuilt the index, which then held
        // the root, and added file.root-invalid.
        yield return new(
            "root indexed only by a section the count cuts off, another catalog in the newest",
            LiveReadRootBeyondSectionCount(info: false),
            PdfReaderLimits.Default with { MaxXRefSectionCount = 2 },
            [[new PdfObjectId(5)]]);

        // The same file, its newest trailer naming 5 under /Info, so that the walk rebuilt the index after file.root-invalid
        // had run. A second validation, or reading 5 first, added file.root-invalid.
        yield return new(
            "root indexed only by a section the count cuts off, info leading to another object",
            LiveReadRootBeyondSectionCount(info: true),
            PdfReaderLimits.Default with { MaxXRefSectionCount = 2 },
            [[new PdfObjectId(5)]]);

        // Catalog 1, the root, is indexed only by the older section; the newer places another catalog, 7, object 5 at object
        // 1's header, and ten free rows that fill the index before the older section is read. Reading 5 first rebuilt the
        // index, which then held the root, and added file.root-invalid.
        yield return new(
            "root indexed only past a full index, another catalog in the newer section",
            LiveReadRootBeyondIndexEntries(),
            PdfReaderLimits.Default with { MaxIndexEntries = 6 },
            [[new PdfObjectId(5)]]);

        // /Root names 50, a dictionary of /Type /Foo that only the older section places, which the section count cuts off;
        // the newer places 4 and object 5 at object 1's header, and no catalog. A control: looking for a catalog, the
        // document read 5 as it opened and rebuilt the index, so no read before validating changed the report.
        yield return new(
            "root a non-catalog indexed only by a section the count cuts off",
            LiveReadNonCatalogRootBeyondSectionCount(),
            PdfReaderLimits.Default with { MaxXRefSectionCount = 1 },
            [[new PdfObjectId(5)]]);

        // The newest table's trailer is an array, an unlinked older trailer names the root, and object 5's row leads to
        // object 1's header. A control: reading 5 first moved where the reader found its trailer, and changed no finding.
        yield return new(
            "newest trailer an array, an unlinked older trailer naming the root",
            LiveReadArrayTrailer("/Root 1 0 R"),
            PdfReaderLimits.Default,
            [[new PdfObjectId(5)]]);

        // The only trailer is the integer 42, a comment line after the header holds a trailer naming the root, and object 5's
        // row leads into that comment. A control: reading 5 first changed no finding.
        yield return new(
            "only trailer an integer, a trailer naming the root in a comment",
            LiveReadIntegerTrailer(),
            PdfReaderLimits.Default,
            [[new PdfObjectId(5)]]);

        // The newest table's trailer is an array, and an unlinked older trailer names the root and 9, which the file lacks,
        // under /Info. Reading 5 first rebuilt the index and merged that trailer, and added the missing /Info.
        yield return new(
            "newest trailer an array, an unlinked older trailer naming a missing info",
            LiveReadArrayTrailer("/Root 1 0 R /Info 9 0 R"),
            PdfReaderLimits.Default,
            [[new PdfObjectId(5)]]);

        // An update's trailer gives /Prev 7 0 R, object 7 holding the older table's offset as an integer and a later
        // definition no row places as a real. Reading 5, whose row leads to object 1, made /Prev resolve to the real and
        // changed file.trailer-value-wrong's message.
        yield return new(
            "prev a reference to an integer, redefined later as a real",
            LiveReadPrevReference(redefined: true),
            PdfReaderLimits.Default,
            [[new PdfObjectId(5)]]);

        // The same file without the later definition of 7. A control: reading 5 first rebuilt the index, and changed no
        // finding.
        yield return new(
            "prev a reference to an integer, never redefined",
            LiveReadPrevReference(redefined: false),
            PdfReaderLimits.Default,
            [[new PdfObjectId(5)]]);

        // The only section is a cross-reference stream whose /Length is 9 0 R, object 9 holding the length as an integer and a
        // later definition no row places as a real. Reading 5, whose row leads to object 1, made /Length resolve to the real
        // and added file.trailer-value-wrong.
        yield return new(
            "cross-reference stream length a reference to an integer, redefined later as a real",
            LiveReadLengthReference(),
            PdfReaderLimits.Default,
            [[new PdfObjectId(5)]]);

        // Object stream 10 gives /N as 11 0 R; the definition of 11 that the index places holds 2, and a later one 3; object
        // 7's row leads to object 1. Reading 7 first made the probe read /N as 3, and added xref.object-stream-broken.
        yield return new(
            "object stream count a reference, redefined later",
            LiveReadObjectStreamReference(first: false, lengthBroken: false),
            PdfReaderLimits.Default,
            [[new PdfObjectId(7)]]);

        // Object stream 10 gives /First as 11 0 R; the definition of 11 that the index places holds 8, and a later one 30;
        // object 7's row leads to object 1. Reading 7 first made the probe read /First as 30, and added
        // xref.object-stream-broken.
        yield return new(
            "object stream first offset a reference, redefined later",
            LiveReadObjectStreamReference(first: true, lengthBroken: false),
            PdfReaderLimits.Default,
            [[new PdfObjectId(7)]]);

        // The object stream whose /N is redefined later, then object stream 20, whose /Length names 21, whose row leads to
        // object 1, so that the probe rebuilt the index between the two streams. A second validation, or reading 21 first,
        // added xref.object-stream-broken.
        yield return new(
            "object stream count a reference redefined later, then a stream whose length rebuilds",
            LiveReadObjectStreamReference(first: false, lengthBroken: true),
            PdfReaderLimits.Default,
            [[new PdfObjectId(21)]]);

        // A page the tree does not list, and a stream 7 nothing names whose /Length is 8 0 R, the row for 8 refused for its
        // generation 99999. Loading 7 in page-tree.page-orphaned's own loop, after the rule had found the index sound,
        // rebuilt it; a second validation, or reading 8 first, no longer reported the orphaned page.
        yield return new(
            "orphaned page, an unnamed stream whose length names a refused row",
            LiveReadOrphanedPage(8),
            PdfReaderLimits.Default,
            [[new PdfObjectId(8)]]);

        // The same orphaned page and refused row, the stream's /Length naming 50, which the table lacks. The same loop rebuilt
        // the index; a second validation, or reading 50 first, no longer reported the orphaned page.
        yield return new(
            "orphaned page, an unnamed stream whose length names a number the table lacks",
            LiveReadOrphanedPage(50),
            PdfReaderLimits.Default,
            [[new PdfObjectId(50)]]);

        // Page 3's row gives generation 0 where its header says 3 1 obj, the page holds /Rotate (x), and object 5's row leads
        // to object 1. A control: reading 5 first rebuilt the index, and the Arlington finding stayed on 3 0.
        yield return new(
            "page whose header gives generation 1, a rotate of the wrong type",
            LiveReadPageOfGenerationOne("/Rotate (x)", shift: 0, broken: true),
            PdfReaderLimits.Default,
            [[new PdfObjectId(5)]]);

        // The same page holding /Thumb 9 0 R, an object the file lacks, instead. Reading 5 first rebuilt the index, and moved
        // object.reference-missing from object 3 0 to 3 1.
        yield return new(
            "page whose header gives generation 1, a thumbnail the file lacks",
            LiveReadPageOfGenerationOne("/Thumb 9 0 R", shift: 0, broken: true),
            PdfReaderLimits.Default,
            [[new PdfObjectId(5)]]);

        // The same page, its row two bytes before its header, and no row leading elsewhere. A control: the walk relocated
        // page 3 whatever was read before, and reading 3 first changed no finding.
        yield return new(
            "page whose header gives generation 1 two bytes past its row, a thumbnail the file lacks",
            LiveReadPageOfGenerationOne("/Thumb 9 0 R", shift: -2, broken: false),
            PdfReaderLimits.Default,
            [[new PdfObjectId(3)]]);

        // The table indexes six numbers, the index has room for eight, ten objects no row places follow the page, and object
        // 5's row leads to object 1. Reading 5 first rebuilt the index until it filled, and added xref.checked-in-part.
        yield return new(
            "more unindexed objects than the index has room for",
            LiveReadUnindexedObjects(Catalog, 1),
            PdfReaderLimits.Default with { MaxIndexEntries = 8 },
            [[new PdfObjectId(5)]]);

        // The same file, the catalog naming 5 under /Outlines, so that the walk rebuilt the index after xref.checked-in-part
        // had run. A second validation, or reading 5 first, added xref.checked-in-part.
        yield return new(
            "more unindexed objects than the index has room for, outlines leading to another object",
            LiveReadUnindexedObjects("<< /Type /Catalog /Pages 2 0 R /Outlines 5 0 R >>", 1),
            PdfReaderLimits.Default with { MaxIndexEntries = 8 },
            [[new PdfObjectId(5)]]);

        // The same file, its trailer's /Root naming 40, which the file lacks. Reading 5 first rebuilt the index until it
        // filled, which left 40 unindexed rather than absent, so file.root-invalid gave way to xref.checked-in-part.
        yield return new(
            "more unindexed objects than the index has room for, a root the file lacks",
            LiveReadUnindexedObjects(Catalog, 40),
            PdfReaderLimits.Default with { MaxIndexEntries = 8 },
            [[new PdfObjectId(5)]]);

        // The catalog's /Outlines names 8, which the file defines and no row places, and object 5's row leads to object 1.
        // Reading 5 first rebuilt the index, which then held 8, so object.reference-missing no longer reported the reference
        // to it.
        yield return new(
            "outlines naming an object the table does not index",
            LiveReadUnplacedOutlines(),
            PdfReaderLimits.Default,
            [[new PdfObjectId(5)]]);

        // An orphan object 4 whose row gives generation 0 three bytes before its header, 4 1 obj. A control: reading 4 first
        // relocated it, and changed no finding.
        yield return new(
            "orphan whose header gives generation 1 three bytes past its row",
            LiveReadShiftedOrphan(),
            PdfReaderLimits.Default,
            [[new PdfObjectId(4)]]);

        // A /Size of 3 below the numbers the table indexes, and object 5's row leads to object 1. A control: reading 5 first
        // rebuilt the index, and changed no finding.
        yield return new(
            "size below the numbers the table indexes",
            LiveReadSizeBelowRows(),
            PdfReaderLimits.Default,
            [[new PdfObjectId(5)]]);

        // A chain the section count cuts at one, its newest section placing every object and object 5 at object 1's header.
        // A control: reading 5 first rebuilt the index, and changed no finding.
        yield return new(
            "chain cut by the section count, every object in the newest section",
            LiveReadChainCutAtNewest(),
            PdfReaderLimits.Default with { MaxXRefSectionCount = 1 },
            [[new PdfObjectId(5)]]);
    }

    /// <summary>
    /// Writes the sound one-page document's catalog 1, page tree root 2 and page 3, and gives where each starts, by number.
    /// </summary>
    private static Dictionary<int, long> LiveReadSound(HandWrittenFile file) => new()
    {
        [1] = file.Object(1, Catalog),
        [2] = file.Object(2, Pages),
        [3] = file.Object(3, Page),
    };

    /// <summary>
    /// Gives the classic table row, line feed included, placing an object of generation <paramref name="generation"/> at
    /// <paramref name="offset"/>.
    /// </summary>
    private static string LiveReadRow(long offset, int generation = 0) =>
        string.Create(CultureInfo.InvariantCulture, $"{offset:D10} {generation:D5} n \n");

    /// <summary>
    /// Writes a classic table of <paramref name="subsections"/>, each row given whole, its trailer
    /// <paramref name="trailer"/> as it is, and the <c>startxref</c> naming it, and gives where it starts.
    /// </summary>
    private static long LiveReadSection(HandWrittenFile file, string trailer, params (int First, string[] Rows)[] subsections)
    {
        var at = file.Position;
        var text = new StringBuilder("xref\n");

        foreach (var (first, rows) in subsections)
        {
            text.Append(CultureInfo.InvariantCulture, $"{first} {rows.Length}\n");

            foreach (var row in rows)
            {
                text.Append(row);
            }
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n{trailer}\nstartxref\n{at}\n%%EOF\n");
        file.Raw(text.ToString());
        return at;
    }

    /// <summary>
    /// Writes the sound document under three sections: the oldest places 1 to 3, an update places 4, and the newest places
    /// another catalog, 7, then 5 at object 1's header, every trailer naming 1 as <c>/Root</c>.
    /// </summary>
    /// <param name="info">Whether the newest trailer names 5 under <c>/Info</c>.</param>
    private static byte[] LiveReadRootBeyondSectionCount(bool info)
    {
        var file = new HandWrittenFile();
        var sound = LiveReadSound(file);
        var oldest = file.Table(4, sound, "/Size 4 /Root 1 0 R");
        var four = file.Object(4, "(update)");
        var update = LiveReadSection(
            file,
            string.Create(CultureInfo.InvariantCulture, $"<< /Size 5 /Root 1 0 R /Prev {oldest} >>"),
            (4, [LiveReadRow(four)]));
        var seven = file.Object(7, "<< /Type /Catalog >>");
        LiveReadSection(
            file,
            string.Create(CultureInfo.InvariantCulture, $"<< /Size 8 /Root 1 0 R /Prev {update}{(info ? " /Info 5 0 R" : string.Empty)} >>"),
            (7, [LiveReadRow(seven)]),
            (5, [LiveReadRow(9)]));
        return file.Bytes;
    }

    /// <summary>
    /// Writes the sound document under two sections: the older places 1 to 3, the newer another catalog, 7, then 5 at
    /// object 1's header, then ten free rows from 20.
    /// </summary>
    private static byte[] LiveReadRootBeyondIndexEntries()
    {
        var file = new HandWrittenFile();
        var sound = LiveReadSound(file);
        var older = file.Table(4, sound, "/Size 4 /Root 1 0 R");
        var seven = file.Object(7, "<< /Type /Catalog >>");
        LiveReadSection(
            file,
            string.Create(CultureInfo.InvariantCulture, $"<< /Size 30 /Root 1 0 R /Prev {older} >>"),
            (7, [LiveReadRow(seven)]),
            (5, [LiveReadRow(9)]),
            (20, [.. Enumerable.Repeat(LiveReadFree, 10)]));
        return file.Bytes;
    }

    /// <summary>
    /// Writes the sound document and object 50, a dictionary of <c>/Type /Foo</c>, under a table placing 1 to 3 and 50,
    /// then an update placing 4 and 5 at object 1's header, both trailers naming 50 as <c>/Root</c>.
    /// </summary>
    private static byte[] LiveReadNonCatalogRootBeyondSectionCount()
    {
        var file = new HandWrittenFile();
        var sound = LiveReadSound(file);
        var fifty = file.Object(50, "<< /Type /Foo >>");
        var older = LiveReadSection(
            file,
            "<< /Size 51 /Root 50 0 R >>",
            (0, [LiveReadFreeHead, LiveReadRow(sound[1]), LiveReadRow(sound[2]), LiveReadRow(sound[3])]),
            (50, [LiveReadRow(fifty)]));
        var four = file.Object(4, "(update)");
        LiveReadSection(
            file,
            string.Create(CultureInfo.InvariantCulture, $"<< /Size 51 /Root 50 0 R /Prev {older} >>"),
            (4, [LiveReadRow(four), LiveReadRow(9)]));
        return file.Bytes;
    }

    /// <summary>
    /// Writes the sound document under a table whose trailer holds <paramref name="older"/>, then, unlinked to it, a table
    /// placing 1 to 3 and 5 at object 1's header, whose trailer is an array.
    /// </summary>
    private static byte[] LiveReadArrayTrailer(string older)
    {
        var file = new HandWrittenFile();
        var sound = LiveReadSound(file);
        file.Table(4, sound, older);
        LiveReadSection(
            file,
            "[ /Size 6 ]",
            (0,
            [
                LiveReadFreeHead, LiveReadRow(sound[1]), LiveReadRow(sound[2]), LiveReadRow(sound[3]), LiveReadFree, LiveReadRow(9),
            ]));
        return file.Bytes;
    }

    /// <summary>
    /// Writes a comment line holding a trailer that names the root, the sound document, and a table placing 1 to 3 and 5
    /// inside that comment, whose trailer is an integer.
    /// </summary>
    private static byte[] LiveReadIntegerTrailer()
    {
        var file = new HandWrittenFile();
        file.Raw("%trailer << /Root 1 0 R >>\n");
        var sound = LiveReadSound(file);
        LiveReadSection(
            file,
            "42",
            (0,
            [
                LiveReadFreeHead, LiveReadRow(sound[1]), LiveReadRow(sound[2]), LiveReadRow(sound[3]), LiveReadFree, LiveReadRow(12),
            ]));
        return file.Bytes;
    }

    /// <summary>
    /// Writes the sound document, object 7 holding its table's offset, and an update placing 5 at object 1's header and
    /// 7 where it is, whose trailer gives <c>/Prev 7 0 R</c>.
    /// </summary>
    /// <param name="redefined">Whether a later definition of 7, which no row places, holds the offset as a real.</param>
    private static byte[] LiveReadPrevReference(bool redefined)
    {
        var file = new HandWrittenFile();
        var sound = LiveReadSound(file);
        var offset = file.Table(4, sound, "/Size 4 /Root 1 0 R").ToString(CultureInfo.InvariantCulture);
        var seven = file.Object(7, offset);

        if (redefined)
        {
            file.Object(7, offset + ".0");
        }

        LiveReadSection(
            file,
            "<< /Size 8 /Root 1 0 R /Prev 7 0 R >>",
            (0, [LiveReadFreeHead]),
            (5, [LiveReadRow(9)]),
            (7, [LiveReadRow(seven)]));
        return file.Bytes;
    }

    /// <summary>
    /// Writes the sound document, object 9 holding the length of the cross-reference stream's 77 bytes of data, a later
    /// definition of 9 holding it as a real, and cross-reference stream 10, whose <c>/Length</c> is <c>9 0 R</c>, placing 1
    /// to 3, 9 where it is first defined, and 5 at object 1's header.
    /// </summary>
    private static byte[] LiveReadLengthReference()
    {
        var file = new HandWrittenFile();
        var sound = LiveReadSound(file);
        var nine = file.Object(9, "77");
        file.Object(9, "77.0");
        file.CrossReferenceStream(
            10,
            11,
            new()
            {
                [1] = (1, sound[1], 0),
                [2] = (1, sound[2], 0),
                [3] = (1, sound[3], 0),
                [5] = (1, 9, 0),
                [9] = (1, nine, 0),
            },
            "/Root 1 0 R /Length 9 0 R",
            length: false);
        return file.Bytes;
    }

    /// <summary>
    /// Writes a catalog, an empty page tree, object 11, object stream 10 holding 5 and 6, whose <c>/N</c> or
    /// <c>/First</c> is <c>11 0 R</c>, and a later definition of 11 giving another value, under a cross-reference stream
    /// that places 11 where it is first defined.
    /// </summary>
    /// <param name="first">Whether <c>/First</c> rather than <c>/N</c> is the reference.</param>
    /// <param name="lengthBroken">
    /// Whether object stream 20 follows, holding 8, its <c>/Length</c> naming 21, whose row leads to object 1's header;
    /// otherwise the row for 7 leads there.
    /// </param>
    private static byte[] LiveReadObjectStreamReference(bool first, bool lengthBroken)
    {
        const string Data = "5 0 6 7 (five) (six)";
        var counts = first ? "/N 2 /First 11 0 R" : "/N 11 0 R /First 8";
        var stream = string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm {counts} /Length {Data.Length} >>\nstream\n{Data}\nendstream");
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, Catalog), 0),
            [2] = (1, file.Object(2, LiveReadEmptyPages), 0),
            [11] = (1, file.Object(11, first ? "8" : "2"), 0),
            [10] = (1, file.Object(10, stream), 0),
            [5] = (2, 10, 0),
            [6] = (2, 10, 1),
            [lengthBroken ? 21 : 7] = (1, 9, 0),
        };
        file.Object(11, first ? "30" : "3");

        if (lengthBroken)
        {
            rows[20] = (1, file.Object(20, "<< /Type /ObjStm /N 1 /First 4 /Length 21 0 R >>\nstream\n8 0 (eight)\nendstream"), 0);
            rows[8] = (2, 20, 0);
            file.CrossReferenceStream(22, 23, rows, "/Root 1 0 R", length: false);
        }
        else
        {
            file.CrossReferenceStream(12, 13, rows, "/Root 1 0 R", length: false);
        }

        return file.Bytes;
    }

    /// <summary>
    /// Writes the sound document, a stream 7 nothing names whose <c>/Length</c> names <paramref name="length"/>, and a page
    /// 9 the tree does not list, under a table whose row for 8 gives generation 99999.
    /// </summary>
    private static byte[] LiveReadOrphanedPage(int length)
    {
        var file = new HandWrittenFile();
        var sound = LiveReadSound(file);
        var seven = file.Object(7, string.Create(CultureInfo.InvariantCulture, $"<< /Length {length} 0 R >>\nstream\nabc\nendstream"));
        var nine = file.Object(9, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] >>");
        LiveReadSection(
            file,
            "<< /Size 10 /Root 1 0 R >>",
            (0,
            [
                LiveReadFreeHead, LiveReadRow(sound[1]), LiveReadRow(sound[2]), LiveReadRow(sound[3]), LiveReadFree, LiveReadFree,
                LiveReadFree, LiveReadRow(seven), LiveReadRow(0, 99999), LiveReadRow(nine),
            ]));
        return file.Bytes;
    }

    /// <summary>
    /// Writes a catalog, a page tree root, and page 3 under the header <c>3 1 obj</c>, holding <paramref name="entry"/>, under
    /// a table whose row places it at generation 0, <paramref name="shift"/> bytes from its header.
    /// </summary>
    /// <param name="broken">Whether the table also places 5 at object 1's header.</param>
    private static byte[] LiveReadPageOfGenerationOne(string entry, int shift, bool broken)
    {
        var page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> " + entry + " >>";
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, Catalog),
            [2] = file.Object(2, Pages),
            [3] = file.Object(3, page, generation: 1) + shift,
        };

        if (broken)
        {
            rows[5] = 9;
            file.Table(6, rows, "/Size 6 /Root 1 0 R");
        }
        else
        {
            file.Table(4, rows, "/Size 4 /Root 1 0 R");
        }

        return file.Bytes;
    }

    /// <summary>
    /// Writes <paramref name="catalog"/>, a page tree root, a page, and ten objects 10 to 19, under a table placing the
    /// first three and 5 at object 1's header, whose trailer names <paramref name="root"/> as <c>/Root</c>.
    /// </summary>
    private static byte[] LiveReadUnindexedObjects(string catalog, int root)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, catalog),
            [2] = file.Object(2, Pages),
            [3] = file.Object(3, Page),
            [5] = 9,
        };

        for (var number = 10; number < 20; number++)
        {
            file.Object(number, "(orphan)");
        }

        file.Table(6, rows, string.Create(CultureInfo.InvariantCulture, $"/Size 6 /Root {root} 0 R"));
        return file.Bytes;
    }

    /// <summary>
    /// Writes a catalog whose <c>/Outlines</c> names 8, a page tree root, a page, and 8, under a table placing the first
    /// three and 5 at object 1's header.
    /// </summary>
    private static byte[] LiveReadUnplacedOutlines()
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Outlines 8 0 R >>"),
            [2] = file.Object(2, Pages),
            [3] = file.Object(3, Page),
            [5] = 9,
        };
        file.Object(8, "<< /Type /Outlines /Count 0 >>");
        file.Table(6, rows, "/Size 6 /Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// Writes the sound document and an object 4 under the header <c>4 1 obj</c>, under a table whose row places it at
    /// generation 0, three bytes before its header.
    /// </summary>
    private static byte[] LiveReadShiftedOrphan()
    {
        var file = new HandWrittenFile();
        var rows = LiveReadSound(file);
        rows[4] = file.Object(4, "(orphan)", generation: 1) - 3;
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// Writes the sound document under a table placing 1 to 3 and 5 at object 1's header, whose <c>/Size</c> is 3.
    /// </summary>
    private static byte[] LiveReadSizeBelowRows()
    {
        var file = new HandWrittenFile();
        var rows = LiveReadSound(file);
        rows[5] = 9;
        file.Table(6, rows, "/Size 3 /Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// Writes the sound document under a table placing 1 to 3, then an update placing them again and 5 at object 1's
    /// header.
    /// </summary>
    private static byte[] LiveReadChainCutAtNewest()
    {
        var file = new HandWrittenFile();
        var rows = LiveReadSound(file);
        var older = file.Table(4, rows, "/Size 4 /Root 1 0 R");
        rows[5] = 9;
        file.Table(6, rows, string.Create(CultureInfo.InvariantCulture, $"/Size 6 /Root 1 0 R /Prev {older}"));
        return file.Bytes;
    }
}
