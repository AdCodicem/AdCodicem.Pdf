using System.Globalization;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

internal static partial class ConvergenceShapes
{
    /// <summary>
    /// The files in which a rebuild of the index, set off by the validator at one point of its walks or by a read before it,
    /// changed what was found; those in which a note the reader kept of a definition the rebuild replaced or dropped did, a
    /// missing endobj or a cut by a guard; and five in which a rebuild never did, kept as checks.
    /// </summary>
    private static IEnumerable<ConvergenceShape> RecordShapes()
    {
        var guarded = PdfReaderLimits.Default with { MaxObjectLength = 256 };

        // The chain's page 3 lacks /MediaBox and /Resources, a later 3 is sound, and kid 4's row leads nowhere. Validating
        // judged the chain's 3, as it did after reading 3 first, then rebuilt at kid 4; reading 4 first rebuilt before, and
        // the chain's 3 was never judged.
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, Catalog),
                [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>"),
                [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R >>"),
                [6] = file.Padding(6),
            };
            rows[4] = rows[6] + 600;
            file.Object(4, Page);
            file.Object(3, Page);
            file.Table(7, rows, "/Size 7 /Root 1 0 R");
            yield return new ConvergenceShape(
                "page redefined after the chain's copy, a later kid's row leading nowhere",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(4)], [new PdfObjectId(3)]]);
        }

        // Bytes before the header, CRLF: the chain's node 5 gives /Count 2 over one page, a later 5 gives 1, and kid 8's row,
        // after 5, leads nowhere. Reading 8 first dropped the count finding on the chain's 5.
        {
            var file = new HandWrittenFile(prefix: "JUNK\r\n", eol: "\r\n");
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, Catalog),
                [2] = file.Object(2, "<< /Type /Pages /Kids [5 0 R 8 0 R] /Count 2 >>"),
                [5] = file.Object(5, "<< /Type /Pages /Parent 2 0 R /Kids [6 0 R] /Count 2 >>"),
                [6] = file.Object(6, "<< /Type /Page /Parent 5 0 R /MediaBox [0 0 595 842] /Resources << >> >>"),
                [9] = file.Padding(9, 1500),
            };
            rows[8] = rows[9] + 700;
            file.Object(8, Page);
            file.Object(5, "<< /Type /Pages /Parent 2 0 R /Kids [6 0 R] /Count 1 >>");
            file.Table(10, rows, "/Size 10 /Root 1 0 R");
            yield return new ConvergenceShape(
                "node redefined with the right count, a later kid's row leading nowhere, junk header, crlf",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(8)]]);
        }

        // Kid 5 is a page in the chain's definition and a node with a missing kid 99 in a later one; kid 4's row leads
        // nowhere. Validating saw 5 as a page in the page tree and as a node in the graph; reading 4 first, as a node in both.
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, Catalog),
                [2] = file.Object(2, "<< /Type /Pages /Kids [5 0 R 4 0 R] /Count 2 >>"),
                [5] = file.Object(5, Page),
                [6] = file.Padding(6),
            };
            rows[4] = rows[6] + 600;
            file.Object(4, Page);
            file.Object(5, "<< /Type /Pages /Parent 2 0 R /Kids [99 0 R] /Count 1 >>");
            file.Table(7, rows, "/Size 7 /Root 1 0 R");
            yield return new ConvergenceShape(
                "kid a page in the chain and a node in a later definition, the next kid's row broken",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(4)]]);
        }

        // The chain's page 3 lacks /MediaBox and /Resources, a later 3 is sound; only object 8, which nothing reaches, has a
        // row that leads nowhere. Validating never rebuilt; reading 8 first did, and dropped the findings on the chain's 3.
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, Catalog),
                [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>"),
                [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R >>"),
                [4] = file.Object(4, Page),
                [6] = file.Padding(6),
            };
            rows[8] = rows[6] + 600;
            file.Object(8, "(unreachable)");
            file.Object(3, Page);
            file.Table(9, rows, "/Size 9 /Root 1 0 R");
            yield return new ConvergenceShape(
                "page redefined after the chain's copy, an unreachable object's row leading nowhere",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(8)]]);
        }

        // The trailer's /Prev leads nowhere and kid 8, after the chain's bare page 3, is in the file but not in the chain; a
        // later 3 is sound. Reading 8 first rebuilt the index and dropped the findings on the chain's 3.
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, Catalog),
                [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R 8 0 R] /Count 2 >>"),
                [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R >>"),
            };
            file.Object(8, Page);
            file.Object(3, Page);
            file.Table(8, rows, "/Size 9 /Root 1 0 R /Prev 999999");
            yield return new ConvergenceShape(
                "page redefined after the chain's copy, a prev leading nowhere and a kid the chain lacks",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(8)]]);
        }

        // A cross-reference stream places page 3 in object stream 10, sound; an older direct 3 lacks /MediaBox and /Resources,
        // and kid 4's row leads nowhere. Reading 4 first made the rebuild's direct 3 the one judged.
        {
            var file = new HandWrittenFile(version: "1.5");
            var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
            {
                [1] = (1, file.Object(1, Catalog), 0),
                [2] = (1, file.Object(2, "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>"), 0),
            };
            file.Object(3, "<< /Type /Page /Parent 2 0 R >>");
            rows[7] = (1, file.Padding(7), 0);
            rows[4] = (1, rows[7].Field2 + 600, 0);
            file.Object(4, Page);
            rows[10] = (1, RecordObjectStream(file, 10, 3, Page), 0);
            rows[3] = (2, 10, 0);
            file.CrossReferenceStream(11, 12, rows, "/Root 1 0 R");
            yield return new ConvergenceShape(
                "compressed page with an older direct copy, a later kid's row leading nowhere",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(4)]]);
        }

        // Bytes before the header: page 3's resources 5 are in object stream 10, naming a font the file lacks, and in object
        // stream 12, sound, which the chain names; unreachable 8's row leads nowhere. Reading 8 first served 10's copy.
        {
            var file = new HandWrittenFile(prefix: "%garbage\n", version: "1.5");
            var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
            {
                [1] = (1, file.Object(1, Catalog), 0),
                [2] = (1, file.Object(2, Pages), 0),
                [3] = (1, file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources 5 0 R >>"), 0),
                [10] = (1, RecordObjectStream(file, 10, 5, "<< /Font << /F1 9 0 R >> >>"), 0),
                [7] = (1, file.Padding(7), 0),
            };
            rows[8] = (1, rows[7].Field2 + 600, 0);
            file.Object(8, "(unreachable)");
            rows[12] = (1, RecordObjectStream(file, 12, 5, "<< /ProcSet [/PDF] >>"), 0);
            rows[5] = (2, 12, 0);
            file.CrossReferenceStream(13, 14, rows, "/Root 1 0 R");
            yield return new ConvergenceShape(
                "resources in two object streams, an unreachable object's row leading nowhere, junk header",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(8)]]);
        }

        // The chain's 5, under the catalog's /Test, holds a name with a null character, a later 5 does not; /Info 7's row
        // leads nowhere. Validating threw once, as it did after reading 5 first; reading 7 first did not.
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Test 5 0 R >>"),
                [2] = file.Object(2, Pages),
                [3] = file.Object(3, Page),
                [5] = file.Object(5, "<< /Bad /A#00B >>"),
                [6] = file.Padding(6),
            };
            rows[7] = rows[6] + 600;
            file.Object(7, "<< /Title (info) >>");
            file.Object(5, "<< /Good 1 >>");
            file.Table(8, rows, "/Size 8 /Info 7 0 R /Root 1 0 R");
            yield return new ConvergenceShape(
                "name with a null character redefined without it, info's row leading nowhere",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(7)], [new PdfObjectId(5)]]);
        }

        // Bytes before the header, CRLF: the chain's 12, in the catalog's /Foo, holds a key with a null character, a later 12
        // does not; /Info 9's row leads nowhere. Validating threw once; reading 9 first did not.
        {
            var file = new HandWrittenFile(prefix: "xx", eol: "\r\n");
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Foo [12 0 R] >>"),
                [2] = file.Object(2, Pages),
                [3] = file.Object(3, Page),
                [12] = file.Object(12, "<< /K#00ey 1 >>"),
                [6] = file.Padding(6, 1400),
            };
            rows[9] = rows[6] + 650;
            file.Object(9, "<< /Producer (p) >>");
            file.Object(12, "<< /Key 1 >>");
            file.Table(13, rows, "/Size 13 /Info 9 0 R /Root 1 0 R");
            yield return new ConvergenceShape(
                "key with a null character redefined without it, info's row leading nowhere, crlf",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(9)]]);
        }

        // The chain's 5, under the catalog's /Test, refers to object 99, which the file lacks, a later 5 to page 3; /Info 7's
        // row leads nowhere. Validating reported a reference to "object 0 0"; reading 7 first reported none.
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Test 5 0 R >>"),
                [2] = file.Object(2, Pages),
                [3] = file.Object(3, Page),
                [5] = file.Object(5, "<< /Ref 99 0 R >>"),
                [6] = file.Padding(6),
            };
            rows[7] = rows[6] + 600;
            file.Object(7, "<< /Title (info) >>");
            file.Object(5, "<< /Ref 3 0 R >>");
            file.Table(8, rows, "/Size 8 /Info 7 0 R /Root 1 0 R");
            yield return new ConvergenceShape(
                "reference to a missing object redefined to a present one, info's row leading nowhere",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(7)]]);
        }

        // CRLF: the chain's 12, under page 3's /Properties, refers to objects 97 and 98, which the file lacks, a later 12
        // holds two numbers; /Info 40's row leads nowhere. Reading 40 first dropped the finding on 12.
        {
            var file = new HandWrittenFile(eol: "\r\n");
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, Catalog),
                [2] = file.Object(2, Pages),
                [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Properties << /P1 12 0 R >> >> >>"),
                [12] = file.Object(12, "<< /Foo [97 0 R 98 0 R] >>"),
                [6] = file.Padding(6, 1300),
            };
            rows[40] = rows[6] + 640;
            file.Object(40, "<< /Title (info) >>");
            file.Object(12, "<< /Foo [1 2] >>");
            file.Table(41, rows, "/Size 41 /Info 40 0 R /Root 1 0 R");
            yield return new ConvergenceShape(
                "two references to missing objects redefined as numbers, info's row leading nowhere, crlf",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(40)]]);
        }

        // The chain's 5, under the catalog's /Test, has a stray value before its endobj, a later 5 does not; /Info 7's row
        // leads nowhere. Reading 7 first, which rebuilds, never changed the report; it stays as a check.
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Test 5 0 R >>"),
                [2] = file.Object(2, Pages),
                [3] = file.Object(3, Page),
                [5] = file.Object(5, "<< /A 1 >>\n(stray)"),
                [6] = file.Padding(6),
            };
            rows[7] = rows[6] + 600;
            file.Object(7, "<< /Title (info) >>");
            file.Object(5, "<< /A 1 >>");
            file.Table(8, rows, "/Size 8 /Info 7 0 R /Root 1 0 R");
            yield return new ConvergenceShape(
                "object missing its endobj redefined with it, info's row leading nowhere",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(7)]]);
        }

        // The chain's row for 5 misses its header, 5 1 obj, by 3 bytes, and a later 5 4 obj follows, neither ending at its
        // endobj; /Info 7's row leads nowhere. Reading 7 first, which rebuilds, never changed the report; it stays as a check.
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Test 5 0 R >>"),
                [2] = file.Object(2, Pages),
                [3] = file.Object(3, Page),
            };
            file.Raw("% c\n");
            rows[5] = file.Object(5, "<< /A 1 >>\n(stray)", 1) - 3;
            rows[6] = file.Padding(6);
            rows[7] = rows[6] + 600;
            file.Object(7, "<< /Title (info) >>");
            file.Object(5, "<< /A 1 >>\n(other)", 4);
            file.Table(8, rows, "/Size 8 /Info 7 0 R /Root 1 0 R");
            yield return new ConvergenceShape(
                "relocated object missing its endobj redefined at generation 4, info's row leading nowhere",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(7)]]);
        }

        // Object 5, whose header, 5 2 obj, the chain's row misses by 3 bytes, refers to object 99, which the file lacks, and
        // does not end at its endobj; /Info 7's row leads nowhere. Reading 7 first, which rebuilds, never changed the report;
        // it stays as a check.
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Test 5 0 R >>"),
                [2] = file.Object(2, Pages),
                [3] = file.Object(3, Page),
            };
            file.Raw("% c\n");
            rows[5] = file.Object(5, "<< /Ref 99 0 R >>\n(stray)", 2) - 3;
            rows[6] = file.Padding(6);
            rows[7] = rows[6] + 600;
            file.Object(7, "<< /Title (info) >>");
            file.Table(8, rows, "/Size 8 /Info 7 0 R /Root 1 0 R");
            yield return new ConvergenceShape(
                "relocated object missing its endobj, info's row leading nowhere",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(7)]]);
        }

        // The catalog's /Test names object 8, which the file holds and the table does not index; /Info 7's row leads
        // nowhere. Validating reported 8 missing; reading 7 first, which rebuilds and indexes 8, did not.
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Test 8 0 R >>"),
                [2] = file.Object(2, Pages),
                [3] = file.Object(3, Page),
                [6] = file.Padding(6),
            };
            rows[7] = rows[6] + 600;
            file.Object(7, "<< /Title (info) >>");
            file.Object(8, "<< /Hello 1 >>");
            file.Table(8, rows, "/Size 8 /Info 7 0 R /Root 1 0 R");
            yield return new ConvergenceShape(
                "object the table does not index named by the catalog, info's row leading nowhere",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(7)]]);
        }

        // Bytes before the header, CRLF: kid 8, which the file holds and the table does not index, comes before kid 4,
        // whose row leads nowhere. Validating reported kid 8 missing; reading 4 first did not.
        {
            var file = new HandWrittenFile(prefix: "\r\n", eol: "\r\n");
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, Catalog),
                [2] = file.Object(2, "<< /Type /Pages /Kids [8 0 R 4 0 R] /Count 2 >>"),
                [6] = file.Padding(6),
            };
            rows[4] = rows[6] + 600;
            file.Object(4, Page);
            file.Object(8, Page);
            file.Table(8, rows, "/Size 8 /Root 1 0 R");
            yield return new ConvergenceShape(
                "kid the table does not index before a kid whose row leads nowhere, crlf",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(4)]]);
        }

        // The chain's 1 is the catalog, a later 1 an annotation; catalog 20, which nothing names, has a page 22 without
        // /MediaBox or /Resources; /Info 7's row leads nowhere. Validating, as it did after reading 1 first, judged page 22
        // by the Arlington model alone; reading 7 first made 20 the catalog for every rule.
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, Catalog),
                [2] = file.Object(2, Pages),
                [3] = file.Object(3, Page),
                [20] = file.Object(20, "<< /Type /Catalog /Pages 21 0 R >>"),
                [21] = file.Object(21, "<< /Type /Pages /Kids [22 0 R] /Count 1 >>"),
                [22] = file.Object(22, "<< /Type /Page /Parent 21 0 R >>"),
                [6] = file.Padding(6),
            };
            rows[7] = rows[6] + 600;
            file.Object(7, "<< /Title (info) >>");
            file.Object(1, "<< /Type /Annot /Subtype /Text /Rect [0 0 1 1] >>");
            file.Table(23, rows, "/Size 23 /Info 7 0 R /Root 1 0 R");
            yield return new ConvergenceShape(
                "catalog redefined as an annotation beside an orphan catalog, info's row leading nowhere",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(7)], [new PdfObjectId(1)]]);
        }

        // CRLF: the catalog redefined as an annotation beside orphan catalog 20, as above, but only unreachable 8's row leads
        // nowhere. Validating never rebuilt; reading 8 first did, and made 20 the catalog.
        {
            var file = new HandWrittenFile(eol: "\r\n");
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, Catalog),
                [2] = file.Object(2, Pages),
                [3] = file.Object(3, Page),
                [20] = file.Object(20, "<< /Type /Catalog /Pages 21 0 R >>"),
                [21] = file.Object(21, "<< /Type /Pages /Kids [22 0 R] /Count 1 >>"),
                [22] = file.Object(22, "<< /Type /Page /Parent 21 0 R >>"),
                [6] = file.Padding(6),
            };
            rows[8] = rows[6] + 600;
            file.Object(8, "(unreachable)");
            file.Object(1, "<< /Type /Annot /Subtype /Text /Rect [0 0 1 1] >>");
            file.Table(23, rows, "/Size 23 /Root 1 0 R");
            yield return new ConvergenceShape(
                "catalog redefined as an annotation beside an orphan catalog, unreachable row broken, crlf",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(8)]]);
        }

        // CRLF: the catalog redefined as an annotation beside orphan catalog 20, whose page 22 also names annotation 77, which
        // the file lacks; /Info 7's row leads nowhere. Reading 7 first made 20 the catalog for every rule, and 77 a missing
        // reference.
        {
            var file = new HandWrittenFile(eol: "\r\n");
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, Catalog),
                [2] = file.Object(2, Pages),
                [3] = file.Object(3, Page),
                [20] = file.Object(20, "<< /Type /Catalog /Pages 21 0 R >>"),
                [21] = file.Object(21, "<< /Type /Pages /Kids [22 0 R] /Count 1 >>"),
                [22] = file.Object(22, "<< /Type /Page /Parent 21 0 R /Annots [77 0 R] >>"),
                [6] = file.Padding(6),
            };
            rows[7] = rows[6] + 600;
            file.Object(7, "<< /Title (info) >>");
            file.Object(1, "<< /Type /Annot /Subtype /Text /Rect [0 0 1 1] >>");
            file.Table(23, rows, "/Size 23 /Info 7 0 R /Root 1 0 R");
            yield return new ConvergenceShape(
                "catalog redefined as an annotation, orphan catalog's page naming a missing one, crlf",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(7)]]);
        }

        // An unlinked trailer mid-file names object 99, which the file lacks, under /Foo and gives /Info a numeric /Title;
        // only unreachable 8's row leads nowhere. Reading 8 first rebuilt, merging that trailer's keys.
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, Catalog),
                [2] = file.Object(2, Pages),
                [3] = file.Object(3, Page),
            };
            file.Raw("trailer\n<< /Foo 99 0 R /Info << /Title 5 >> >>\n");
            rows[6] = file.Padding(6);
            rows[8] = rows[6] + 600;
            file.Object(8, "(unreachable)");
            file.Table(9, rows, "/Size 9 /Root 1 0 R");
            yield return new ConvergenceShape(
                "unlinked older trailer mid-file, an unreachable object's row leading nowhere",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(8)]]);
        }

        // The chain's outline 5 is sound, a later 5 gives /Count as a string and /First as a number; /Info 7's row leads
        // nowhere. Reading 7 first, which rebuilds, never changed the report; it stays as a check.
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Outlines 5 0 R >>"),
                [2] = file.Object(2, Pages),
                [3] = file.Object(3, Page),
                [5] = file.Object(5, "<< /Type /Outlines /Count 0 >>"),
                [6] = file.Padding(6),
            };
            rows[7] = rows[6] + 600;
            file.Object(7, "<< /Title (info) >>");
            file.Object(5, "<< /Type /Outlines /Count (zero) /First 3 >>");
            file.Table(8, rows, "/Size 8 /Info 7 0 R /Root 1 0 R");
            yield return new ConvergenceShape(
                "outline redefined with a string count, info's row leading nowhere",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(7)]]);
        }

        // The chain's page 3 lacks /MediaBox and /Resources, a later 3 is sound, and object stream 10's /Length names 11,
        // whose row leads nowhere. Reading 11 first, which rebuilds, never changed the report; it stays as a check.
        {
            var file = new HandWrittenFile(version: "1.5");
            var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
            {
                [1] = (1, file.Object(1, Catalog), 0),
                [2] = (1, file.Object(2, Pages), 0),
                [3] = (1, file.Object(3, "<< /Type /Page /Parent 2 0 R >>"), 0),
            };
            var data = "5 0 << /Title (t) >>\n";
            rows[10] = (1, file.Object(10, $"<< /Type /ObjStm /N 1 /First 4 /Length 11 0 R >>\nstream\n{data}\nendstream"), 0);
            rows[5] = (2, 10, 0);
            rows[7] = (1, file.Padding(7), 0);
            rows[11] = (1, rows[7].Field2 + 600, 0);
            file.Object(11, data.Length.ToString(CultureInfo.InvariantCulture));
            file.Object(3, Page);
            file.CrossReferenceStream(12, 13, rows, "/Root 1 0 R /Info 5 0 R");
            yield return new ConvergenceShape(
                "page redefined, an object stream's length whose row leads nowhere",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(11)]]);
        }

        // /Root names no catalog, so opening takes catalog 20; catalog 30 follows, then a later 20, an annotation; /Info 40's
        // row leads nowhere. Validating said the reader took 20, as it did after reading 20 first; reading 40 first, 30.
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Foo >>"),
                [2] = file.Object(2, Pages),
                [3] = file.Object(3, Page),
                [20] = file.Object(20, Catalog),
                [6] = file.Padding(6),
            };
            rows[40] = rows[6] + 600;
            file.Object(40, "<< /Title (info) >>");
            rows[30] = file.Object(30, Catalog);
            file.Object(20, "<< /Type /Annot /Subtype /Text /Rect [0 0 1 1] >>");
            file.Table(41, rows, "/Size 41 /Info 40 0 R /Root 1 0 R");
            yield return new ConvergenceShape(
                "root not a catalog, catalog taken redefined as an annotation, info's row leading nowhere",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(40)], [new PdfObjectId(20)]]);
        }

        // Spaces before the header, CRLF: /Root names no catalog and catalog 20 is redefined as an annotation, as above, but
        // only unreachable 40's row leads nowhere. Validating said the reader took 20; reading 40 first, 30.
        {
            var file = new HandWrittenFile(prefix: "  ", eol: "\r\n");
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Foo >>"),
                [2] = file.Object(2, Pages),
                [3] = file.Object(3, Page),
                [20] = file.Object(20, Catalog),
                [6] = file.Padding(6),
            };
            rows[40] = rows[6] + 600;
            file.Object(40, "(unreachable)");
            rows[30] = file.Object(30, Catalog);
            file.Object(20, "<< /Type /Annot /Subtype /Text /Rect [0 0 1 1] >>");
            file.Table(41, rows, "/Size 41 /Root 1 0 R");
            yield return new ConvergenceShape(
                "root not a catalog, catalog taken redefined as an annotation, unreachable row broken, crlf",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(40)]]);
        }

        // Under a MaxObjectLength of 256, the chain's outline 5 is cut by the guard and a later 5 gives /Count as a string;
        // /Info 7's row leads nowhere. Validating, and reading 5 first, skipped 5 as cut; reading 7 first judged the later 5.
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Outlines 5 0 R >>"),
                [2] = file.Object(2, Pages),
                [3] = file.Object(3, Page),
                [5] = file.Object(5, "<< /Type /Outlines /Count 0 /Pad (" + new string('y', 400) + ") >>"),
                [6] = file.Padding(6),
            };
            rows[7] = rows[6] + 600;
            file.Object(7, "<< /Title (info) >>");
            file.Object(5, "<< /Type /Outlines /Count (zero) >>");
            file.Table(8, rows, "/Size 8 /Info 7 0 R /Root 1 0 R");
            yield return new ConvergenceShape(
                "outline cut by the length guard redefined short, info's row leading nowhere",
                file.Bytes,
                guarded,
                [[new PdfObjectId(7)], [new PdfObjectId(5)]]);
        }

        // Under a MaxObjectLength of 256, bytes before the header, CRLF: page 3's annotation 5 is cut by the guard and a later
        // 5 gives /Rect as a string; /Info 9's row leads nowhere. Validating skipped 5 as cut; reading 9 first judged it.
        {
            var file = new HandWrittenFile(prefix: "\r\n", eol: "\r\n");
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, Catalog),
                [2] = file.Object(2, Pages),
                [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> /Annots [5 0 R] >>"),
                [5] = file.Object(5, "<< /Type /Annot /Subtype /Text /Rect [0 0 1 1] /Contents (" + new string('z', 500) + ") >>"),
                [6] = file.Padding(6, 1300),
            };
            rows[9] = rows[6] + 650;
            file.Object(9, "<< /Title (info) >>");
            file.Object(5, "<< /Type /Annot /Subtype /Text /Rect (r) >>");
            file.Table(10, rows, "/Size 10 /Info 9 0 R /Root 1 0 R");
            yield return new ConvergenceShape(
                "annotation cut by the length guard redefined short, info's row leading nowhere, crlf",
                file.Bytes,
                guarded,
                [[new PdfObjectId(9)]]);
        }

        // Kid 5's header, "5 0 % lost" then "obj", is read where its row places it but not found by a rebuild, and no endobj
        // follows its value; kid 4's row leads nowhere. Validating, as it did after reading 5 first, reported 5's missing
        // endobj and page faults, a second validation the endobj alone, as a note kept of a definition the rebuild dropped;
        // reading 4 first, neither.
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, Catalog),
                [2] = file.Object(2, "<< /Type /Pages /Kids [5 0 R 4 0 R] /Count 2 >>"),
                [5] = file.Position,
            };
            file.Raw("5 0 % lost\nobj\n<< /Type /Page /Parent 2 0 R >>\n(stray)\nendobj\n");
            rows[6] = file.Padding(6);
            rows[4] = rows[6] + 600;
            file.Object(4, Page);
            file.Table(7, rows, "/Size 7 /Root 1 0 R");
            yield return new ConvergenceShape(
                "page whose header a rebuild cannot find, missing its endobj, the next kid's row broken",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(4)], [new PdfObjectId(5)]]);
        }

        // CRLF: the catalog's /Test 5 has a header a rebuild cannot find, refers to object 99, which the file lacks, and no
        // endobj follows its value; /Info 7's row leads nowhere. Validating reported its reference to "object 0 0" and its
        // missing endobj, a second validation the endobj alone, as a note kept of a definition the rebuild dropped; reading 7
        // first, neither.
        {
            var file = new HandWrittenFile(eol: "\r\n");
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Test 5 0 R >>"),
                [2] = file.Object(2, Pages),
                [3] = file.Object(3, Page),
                [5] = file.Position,
            };
            file.Raw("5 0 %c\r\nobj\r\n<< /Ref 99 0 R >>\r\n(stray)\r\nendobj\r\n");
            rows[6] = file.Padding(6);
            rows[7] = rows[6] + 600;
            file.Object(7, "<< /Title (info) >>");
            file.Table(8, rows, "/Size 8 /Info 7 0 R /Root 1 0 R");
            yield return new ConvergenceShape(
                "object whose header a rebuild cannot find, missing its endobj, info's row broken, crlf",
                file.Bytes,
                PdfReaderLimits.Default,
                [[new PdfObjectId(7)]]);
        }
    }

    /// <summary>
    /// Writes object stream <paramref name="number"/> holding <paramref name="member"/> alone, its value followed by a line
    /// feed rather than the space <see cref="HandWrittenFile.ObjectStream"/> writes, as these files were first written.
    /// </summary>
    private static long RecordObjectStream(HandWrittenFile file, int number, int member, string body)
    {
        var header = string.Create(CultureInfo.InvariantCulture, $"{member} 0 ");
        var data = header + body + "\n";
        return file.Object(
            number,
            string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Type /ObjStm /N 1 /First {header.Length} /Length {data.Length} >>\nstream\n{data}\nendstream"));
    }
}
