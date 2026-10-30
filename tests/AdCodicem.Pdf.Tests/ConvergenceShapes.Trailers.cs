using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

internal static partial class ConvergenceShapes
{
    /// <summary>A catalog naming object 97, which the file lacks, under <c>/Baz</c>.</summary>
    private const string TrailerCatalogNamingMissing = "<< /Type /Catalog /Pages 2 0 R /Baz 97 0 R >>";

    /// <summary>A catalog holding a name with a null character.</summary>
    private const string TrailerCatalogWithNullName = "<< /Type /Catalog /Pages 2 0 R /Na#00me 1 >>";

    /// <summary>A sound page of letter size whose parent is object 2.</summary>
    private const string TrailerLetterPage = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>";

    /// <summary>The newest trailer of #237's shape, <c>/Bar</c> last, after <c>/Prev</c>.</summary>
    private const string TrailerBarLast = "/Size 5 /Root 1 0 R /Prev 999999 /Bar 4 0 R";

    /// <summary>The newest trailer of #237's shape, <c>/Bar</c> first, so that the graph walks <c>/Root</c> before it.</summary>
    private const string TrailerBarFirst = "/Bar 4 0 R /Size 5 /Root 1 0 R /Prev 999999";

    /// <summary>The bytes of a UTF-8 byte order mark, as Latin-1 characters.</summary>
    private const string TrailerByteOrderMark = "\u00EF\u00BB\u00BF";

    /// <summary>
    /// #237's shape, <c>/Prev</c> last in the newest trailer and object 4 an older string: written from a template, with
    /// line feeds and the header at offset 0.
    /// </summary>
    internal const string TrailerPrevLastTemplate = """
        %PDF-1.7
        1 0 obj
        << /Type /Catalog /Pages 2 0 R >>
        endobj
        2 0 obj
        << /Type /Pages /Kids [3 0 R] /Count 1 >>
        endobj
        3 0 obj
        << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>
        endobj
        4 0 obj
        (an older object)
        endobj
        xref
        0 5
        {free}
        {row:1}
        {row:2}
        {row:3}
        {row:4}
        trailer
        << /Size 5 /Root 1 0 R /Foo 99 0 R /Info << /Title 5 >> >>
        startxref
        {xref:1}
        %%EOF
        xref
        0 4
        {free}
        {row:1}
        {row:2}
        {row:3}
        trailer
        << /Size 5 /Root 1 0 R /Bar 4 0 R /Prev 999999 >>
        startxref
        {xref:2}
        %%EOF

        """;

    /// <summary>
    /// The update deletes object 5, which the older body still defines, and places objects 3 and 6 at their first
    /// definitions while later ones follow them; <c>{FREE1}</c> stands for the row that frees 5 with generation 1.
    /// </summary>
    private const string TrailerDeletedObjectTemplate = """
        %PDF-1.7
        1 0 obj
        << /Type /Catalog /Pages 2 0 R /Extra 6 0 R /Gone 5 0 R >>
        endobj
        2 0 obj
        << /Type /Pages /Kids [3 0 R] /Count 1 >>
        endobj
        3 0 obj
        << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>
        endobj
        4 0 obj
        (an older object)
        endobj
        5 0 obj
        << /Deleted true >>
        endobj
        6 0 obj
        << /Ref 5 0 R >>
        endobj
        xref
        0 7
        {free}
        {row:1}
        {row:2}
        {row:3}
        {row:4}
        {row:5}
        {row:6}
        trailer
        << /Size 7 /Root 1 0 R /Foo 99 0 R >>
        startxref
        {xref:1}
        %%EOF
        3 0 obj
        << /Type /Page /Parent 2 0 R >>
        endobj
        6 0 obj
        << /Ref 2 0 R >>
        endobj
        xref
        0 4
        {free}
        {row:1}
        {row:2}
        {row:3}
        5 2
        {FREE1}
        {row:6}
        trailer
        << /Size 7 /Bar 4 0 R /Root 1 0 R /Prev 999999 >>
        startxref
        {xref:2}
        %%EOF

        """;

    /// <summary>
    /// Object 6, which the catalog names, holds a name with a null character where both tables place it; a later
    /// definition, which no table places, does not.
    /// </summary>
    private const string TrailerNullNameTemplate = """
        %PDF-1.7
        1 0 obj
        << /Type /Catalog /Pages 2 0 R /Extra 6 0 R >>
        endobj
        2 0 obj
        << /Type /Pages /Kids [3 0 R] /Count 1 >>
        endobj
        3 0 obj
        << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>
        endobj
        4 0 obj
        (an older object)
        endobj
        6 0 obj
        << /K /A#00B >>
        endobj
        xref
        0 5
        {free}
        {row:1}
        {row:2}
        {row:3}
        {row:4}
        6 1
        {row:6}
        trailer
        << /Size 7 /Root 1 0 R >>
        startxref
        {xref:1}
        %%EOF
        6 0 obj
        << /K /AB >>
        endobj
        xref
        0 4
        {free}
        {row:1}
        {row:2}
        {row:3}
        6 1
        {row:6}
        trailer
        << /Size 7 /Bar 4 0 R /Root 1 0 R /Prev 999999 >>
        startxref
        {xref:2}
        %%EOF

        """;

    /// <summary>
    /// Object 7, which the catalog names, lacks its <c>endobj</c> where both tables place it; a later definition, which no
    /// table places, has it.
    /// </summary>
    private const string TrailerEndObjTemplate = """
        %PDF-1.7
        1 0 obj
        << /Type /Catalog /Pages 2 0 R /Extra 7 0 R >>
        endobj
        2 0 obj
        << /Type /Pages /Kids [3 0 R] /Count 1 >>
        endobj
        3 0 obj
        << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>
        endobj
        7 0 obj
        << /A 1 >>
        4 0 obj
        (an older object)
        endobj
        xref
        0 5
        {free}
        {row:1}
        {row:2}
        {row:3}
        {row:4}
        7 1
        {row:7}
        trailer
        << /Size 8 /Root 1 0 R >>
        startxref
        {xref:1}
        %%EOF
        7 0 obj
        << /A 2 >>
        endobj
        xref
        0 4
        {free}
        {row:1}
        {row:2}
        {row:3}
        7 1
        {row:7}
        trailer
        << /Size 8 /Bar 4 0 R /Root 1 0 R /Prev 999999 >>
        startxref
        {xref:2}
        %%EOF

        """;

    /// <summary>
    /// The files crafted while measuring #237. Most hold an update whose <c>/Prev</c> leads outside the file, so that
    /// the first read of an object only the older section indexes rebuilt the index: during a validation, that merged
    /// the older trailer's keys into the live one and served later definitions of objects the walks had already read.
    /// Two instead place object 4 outside the file with a row of their only table, which rebuilds the index the same way
    /// and merges the trailer left after their <c>%%EOF</c>. Two others give a row a refused generation, so that only a
    /// read made before validating rebuilt the index.
    /// </summary>
    private static IEnumerable<ConvergenceShape> TrailerShapes()
    {
        PdfObjectId[] bar = [new(4)];
        PdfObjectId[] catalogPagesAndKid = [new(1), new(2), new(3)];
        PdfObjectId[] rootThenBar = [new(1), new(4)];
        PdfObjectId[] barThenRoot = [new(4), new(1)];

        // #237's shape: the older trailer, which the chain no longer reaches, names the missing 99 under /Foo.
        // Reading 4, under /Bar, rebuilt the index and merged /Foo in: read first, it added the finding the first
        // validation lacked.
        yield return TrailerShape(
            "unlinked older trailer naming a missing object, bar last, lf",
            TrailerUpdate(string.Empty, "\n"),
            bar,
            catalogPagesAndKid,
            rootThenBar);

        // The same file with CRLF line ends and three bytes before its header. Reading 4 first added the /Foo finding.
        yield return TrailerShape(
            "unlinked older trailer naming a missing object, bar last, crlf",
            TrailerUpdate("XYZ", "\r\n"),
            bar,
            catalogPagesAndKid,
            rootThenBar);

        // #237's shape with the page tree root redefined after the last %%EOF with a /Count of 2. Reading 4 first added
        // the /Foo finding and the count mismatch, which the first validation's page tree, read on the chain's root,
        // did not see.
        yield return TrailerShape(
            "unlinked older trailer, pages redefined in an interrupted update, lf",
            TrailerUpdate(string.Empty, "\n", appended: [(2, "<< /Type /Pages /Kids [3 0 R] /Count 2 >>")]),
            bar,
            catalogPagesAndKid,
            rootThenBar);

        // The same file with CRLF line ends and three bytes before its header. Reading 4 first added the count mismatch
        // and the /Foo finding.
        yield return TrailerShape(
            "unlinked older trailer, pages redefined in an interrupted update, crlf",
            TrailerUpdate("XYZ", "\r\n", appended: [(2, "<< /Type /Pages /Kids [3 0 R] /Count 2 >>")]),
            bar,
            catalogPagesAndKid,
            rootThenBar);

        // The chain's catalog names the missing 97, the catalog after the last %%EOF does not, and /Bar comes first.
        // The first validation reported object 1 referring to "object 0 0"; reading 4 first reported the /Foo finding
        // instead.
        yield return TrailerShape(
            "unlinked older trailer, catalog naming a missing object redefined, bar first, lf",
            TrailerUpdate(string.Empty, "\n", TrailerCatalogNamingMissing, newest: TrailerBarFirst, appended: [(1, Catalog)]),
            bar,
            catalogPagesAndKid);

        // The same file with CRLF line ends and three bytes before its header. Reading 4 first replaced the "0 0"
        // finding with the /Foo one.
        yield return TrailerShape(
            "unlinked older trailer, catalog naming a missing object redefined, bar first, crlf",
            TrailerUpdate("XYZ", "\r\n", TrailerCatalogNamingMissing, newest: TrailerBarFirst, appended: [(1, Catalog)]),
            bar,
            catalogPagesAndKid);

        // The chain's catalog holds a name with a null character, the catalog after the last %%EOF does not, and /Bar
        // comes first. The first validation threw a NullReferenceException; reading 4 first gave a report.
        yield return TrailerShape(
            "unlinked older trailer, catalog with a null-character name redefined, bar first, lf",
            TrailerUpdate(string.Empty, "\n", TrailerCatalogWithNullName, newest: TrailerBarFirst, appended: [(1, Catalog)]),
            bar,
            catalogPagesAndKid);

        // The same file with CRLF line ends and three bytes before its header. Reading 4 first gave a report, not a throw.
        yield return TrailerShape(
            "unlinked older trailer, catalog with a null-character name redefined, bar first, crlf",
            TrailerUpdate("XYZ", "\r\n", TrailerCatalogWithNullName, newest: TrailerBarFirst, appended: [(1, Catalog)]),
            bar,
            catalogPagesAndKid);

        // The chain's page tree root lacks its endobj, the one after the last %%EOF has it, and /Bar comes first. Only
        // the /Foo finding depended on reading 4 first.
        yield return TrailerShape(
            "unlinked older trailer, pages lacking endobj redefined in an interrupted update, lf",
            TrailerUpdate(string.Empty, "\n", newest: TrailerBarFirst, pagesClosed: false, appended: [(2, Pages)]),
            bar,
            catalogPagesAndKid);

        // The same file with CRLF line ends and three bytes before its header. Reading 4 first added the /Foo finding.
        yield return TrailerShape(
            "unlinked older trailer, pages lacking endobj redefined in an interrupted update, crlf",
            TrailerUpdate("XYZ", "\r\n", newest: TrailerBarFirst, pagesClosed: false, appended: [(2, Pages)]),
            bar,
            catalogPagesAndKid);

        // The chain's catalog names the missing 97, the catalog after the last %%EOF does not, and /Bar comes last, so the
        // graph followed it before /Root. Only the /Foo finding depended on reading 4 first.
        yield return TrailerShape(
            "unlinked older trailer, catalog naming a missing object redefined, bar last, lf",
            TrailerUpdate(string.Empty, "\n", TrailerCatalogNamingMissing, appended: [(1, Catalog)]),
            bar,
            catalogPagesAndKid,
            rootThenBar);

        // The same file with CRLF line ends and three bytes before its header. Reading 4 first added the /Foo finding.
        yield return TrailerShape(
            "unlinked older trailer, catalog naming a missing object redefined, bar last, crlf",
            TrailerUpdate("XYZ", "\r\n", TrailerCatalogNamingMissing, appended: [(1, Catalog)]),
            bar,
            catalogPagesAndKid,
            rootThenBar);

        // One whole table whose row places 4 outside the file, then a page tree root with a /Count of 2 and a trailer
        // naming the missing 99, both after the %%EOF. Reading 4 first added the count mismatch and the /Foo finding.
        yield return TrailerShape(
            "row outside the file, pages redefined and a trailer left in an interrupted update, lf",
            TrailerBrokenRow(string.Empty, "\n"),
            bar,
            catalogPagesAndKid,
            rootThenBar);

        // The same file with CRLF line ends and three bytes before its header. Reading 4 first added both findings.
        yield return TrailerShape(
            "row outside the file, pages redefined and a trailer left in an interrupted update, crlf",
            TrailerBrokenRow("XYZ", "\r\n"),
            bar,
            catalogPagesAndKid,
            rootThenBar);

        // #237's shape with page 3's /Rotate a real number, and a page tree root after the last %%EOF listing a new page 5
        // before 3. Reading 4 first moved the /Rotate finding from page 0 to page 1, and added the /Foo finding.
        yield return TrailerShape(
            "unlinked older trailer, page tree redefined with a new first page, lf",
            TrailerUpdate(
                string.Empty,
                "\n",
                page: "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> /Rotate 1.5 >>",
                appended: [(2, "<< /Type /Pages /Kids [5 0 R 3 0 R] /Count 2 >>"), (5, Page)]),
            bar,
            catalogPagesAndKid,
            rootThenBar);

        // The same file with CRLF line ends and three bytes before its header. Reading 4 first moved the finding's page
        // and added the /Foo finding.
        yield return TrailerShape(
            "unlinked older trailer, page tree redefined with a new first page, crlf",
            TrailerUpdate(
                "XYZ",
                "\r\n",
                page: "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> /Rotate 1.5 >>",
                appended: [(2, "<< /Type /Pages /Kids [5 0 R 3 0 R] /Count 2 >>"), (5, Page)]),
            bar,
            catalogPagesAndKid,
            rootThenBar);

        // One whole table whose row for 5, which nothing names, gives generation 99999, and a page 4 the tree does not
        // list. Reading 5 first rebuilt the index, and the orphaned page was no longer reported.
        yield return TrailerShape(
            "refused generation for an object nothing names, beside an orphan page, lf",
            TrailerRefusedRow(string.Empty, "\n"),
            [new(5)],
            catalogPagesAndKid,
            [new(1)]);

        // The same file with CRLF line ends and three bytes before its header. Reading 5 first hid the orphaned page.
        yield return TrailerShape(
            "refused generation for an object nothing names, beside an orphan page, crlf",
            TrailerRefusedRow("XYZ", "\r\n"),
            [new(5)],
            catalogPagesAndKid,
            [new(1)]);

        // #237's shape with /Prev last in the newest trailer, written from a template. Reading 4 first, or resolving the
        // trailer's values, added the /Foo finding.
        yield return TrailerShape(
            "unlinked older trailer naming a missing object, prev last, lf",
            PdfTemplate.Build(TrailerPrevLastTemplate),
            bar,
            catalogPagesAndKid,
            rootThenBar);

        // #237's shape with CRLF line ends after a byte order mark, keys in another order, and page 3 redefined in the
        // update. Reading 4 first added the /Foo finding.
        yield return TrailerShape(
            "unlinked older trailer naming a missing object, page redefined, crlf",
            TrailerRedefinedPage(),
            bar,
            catalogPagesAndKid,
            rootThenBar);

        // The update frees 5, which the older body defines, and redefines 3 and 6 while its rows still place their
        // first definitions. Reading 4 first replaced the finding on /Gone and an "object 0 0" one with /Foo and
        // page 3's missing box and resources.
        yield return TrailerShape(
            "deleted object still in the body, rows placing objects at their first definitions, lf",
            PdfTemplate.Build(TrailerDeletedObjectTemplate.Replace("{FREE1}", "0000000000 00001 f ", StringComparison.Ordinal)),
            bar,
            catalogPagesAndKid,
            barThenRoot);

        // The same shape written by hand, with CRLF line ends after three bytes, the trailers' keys in another order, a
        // letter-size page and object 4 a dictionary; its later 6 refers to 1, and its later 3, written after 6, gives
        // /Contents 4 0 R. Reading 4 first changed the same findings.
        yield return TrailerShape(
            "deleted object still in the body, rows placing objects at their first definitions, crlf",
            TrailerDeletedObject(),
            bar,
            catalogPagesAndKid,
            barThenRoot);

        // The older trailer, which the chain no longer reaches, gives /Encrypt; the newest section, a cross-reference
        // stream, places 7 in object stream 8. Reading 4 first merged /Encrypt, and 7 went unchecked.
        yield return TrailerShape(
            "unlinked older trailer giving encrypt, newest section a cross-reference stream, lf",
            TrailerEncryptedOlderSection(string.Empty, "\n"),
            bar,
            catalogPagesAndKid,
            rootThenBar);

        // The same file with CRLF line ends after a byte order mark. Reading 4 first added xref.checked-in-part.
        yield return TrailerShape(
            "unlinked older trailer giving encrypt, newest section a cross-reference stream, crlf",
            TrailerEncryptedOlderSection(TrailerByteOrderMark, "\r\n"),
            bar,
            catalogPagesAndKid,
            rootThenBar);

        // Object 6 holds a name with a null character where the tables place it, and not in a later definition. The
        // first validation threw a NullReferenceException; reading 4 first gave a report.
        yield return TrailerShape(
            "name with a null character in the definition the rows place, later fixed, lf",
            PdfTemplate.Build(TrailerNullNameTemplate),
            bar,
            catalogPagesAndKid,
            barThenRoot);

        // The same shape written by hand, with CRLF line ends after a byte order mark. The first validation threw; reading
        // 4 first gave a report.
        yield return TrailerShape(
            "name with a null character in the definition the rows place, later fixed, crlf",
            TrailerNullName(),
            bar,
            catalogPagesAndKid,
            barThenRoot);

        // Object 7 lacks its endobj where the tables place it, and has it in a later definition. No read changed the
        // report: the rebuild forgets the note along with the definition.
        yield return TrailerShape(
            "object lacking endobj in the definition the rows place, later closed, lf",
            PdfTemplate.Build(TrailerEndObjTemplate),
            bar,
            catalogPagesAndKid,
            barThenRoot,
            [new(7), new(4), new(7)]);

        // The same shape written by hand, with CRLF line ends after three bytes. No read changed the report.
        yield return TrailerShape(
            "object lacking endobj in the definition the rows place, later closed, crlf",
            TrailerEndObj(),
            bar,
            catalogPagesAndKid,
            barThenRoot,
            [new(7), new(4), new(7)]);
    }

    /// <summary>A shape opened under the default limits, as the probes opened every file of #237.</summary>
    private static ConvergenceShape TrailerShape(string name, byte[] file, params PdfObjectId[][] reads) =>
        new(name, file, PdfReaderLimits.Default, reads);

    /// <summary>
    /// Writes #237's shape after <paramref name="prefix"/>, its lines ending with <paramref name="eol"/>: objects 1 to 4, a
    /// table of them whose trailer names the missing 99 under <c>/Foo</c> and an <c>/Info</c> whose title is a number, an
    /// update whose table places 1 to 3 at their first definitions under <paramref name="newest"/>, then
    /// <paramref name="appended"/>, defined after the last <c>%%EOF</c> with no table.
    /// </summary>
    private static byte[] TrailerUpdate(
        string prefix,
        string eol,
        string catalog = Catalog,
        string page = Page,
        string newest = TrailerBarLast,
        bool pagesClosed = true,
        (int Number, string Body)[]? appended = null)
    {
        var file = new HandWrittenFile(prefix, eol: eol);
        var rows = new Dictionary<int, long> { [1] = file.Object(1, catalog) };

        if (pagesClosed)
        {
            rows[2] = file.Object(2, Pages);
        }
        else
        {
            rows[2] = file.Position;
            file.Raw("2 0 obj" + eol + Pages + eol);
        }

        rows[3] = file.Object(3, page);
        rows[4] = file.Object(4, "(four)");
        file.Table(5, rows, "/Size 5 /Root 1 0 R /Foo 99 0 R /Info << /Title 5 >>");
        file.Table(4, rows, newest);

        foreach (var (number, body) in appended ?? [])
        {
            file.Object(number, body);
        }

        return file.Bytes;
    }

    /// <summary>
    /// Writes one whole table whose row places object 4 outside the file, then, after its <c>%%EOF</c>, a page tree root with a
    /// <c>/Count</c> of 2 and a trailer, which no table follows, naming the missing 99 under <c>/Foo</c>.
    /// </summary>
    private static byte[] TrailerBrokenRow(string prefix, string eol)
    {
        var file = new HandWrittenFile(prefix, eol: eol);
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, Catalog),
            [2] = file.Object(2, Pages),
            [3] = file.Object(3, Page),
            [4] = 999999,
        };
        file.Object(4, "(four)");
        file.Table(5, rows, "/Size 5 /Root 1 0 R /Bar 4 0 R");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 2 >>");
        file.Raw("trailer" + eol + "<< /Size 5 /Root 1 0 R /Foo 99 0 R >>" + eol);
        return file.Bytes;
    }

    /// <summary>
    /// Writes one whole table whose row for object 5, which nothing names, gives generation 99999, beside a page 4 that the
    /// page tree does not list.
    /// </summary>
    private static byte[] TrailerRefusedRow(string prefix, string eol)
    {
        var file = new HandWrittenFile(prefix, eol: eol);
        var catalog = file.Object(1, Catalog);
        var pages = file.Object(2, Pages);
        var listed = file.Object(3, Page);
        var orphan = file.Object(4, Page);
        var refused = file.Object(5, "(five)");
        TrailerTable(
            file,
            eol,
            "/Size 6 /Root 1 0 R",
            (0, [(0, 65535, 'f'), (catalog, 0, 'n'), (pages, 0, 'n'), (listed, 0, 'n'), (orphan, 0, 'n'), (refused, 99999, 'n')]));
        return file.Bytes;
    }

    /// <summary>
    /// Writes #237's shape with CRLF line ends after a byte order mark and a binary comment: object 4 a dictionary, the
    /// trailers' keys in another order, and page 3 redefined in the update with a <c>/Rotate</c> of 90.
    /// </summary>
    private static byte[] TrailerRedefinedPage()
    {
        const string eol = "\r\n";
        var file = new HandWrittenFile(TrailerByteOrderMark, eol: eol);
        file.Raw("%\u00E2\u00E3\u00CF\u00D3" + eol);
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, Catalog),
            [2] = file.Object(2, Pages),
            [3] = file.Object(3, TrailerLetterPage),
            [4] = file.Object(4, "<< /Kind /Old >>"),
        };
        file.Table(5, rows, "/Info << /Title 5 >> /Size 5 /Foo 99 0 R /Root 1 0 R");
        rows[3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Rotate 90 >>");
        file.Table(4, rows, "/Size 5 /Prev 999999 /Root 1 0 R /Bar 4 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// Writes the update that frees object 5 while the older body defines it, with CRLF line ends after three bytes: the
    /// later definitions of 6 and 3 come before the update's table, which places them at their first ones.
    /// </summary>
    private static byte[] TrailerDeletedObject()
    {
        const string eol = "\r\n";
        var file = new HandWrittenFile("~~\n", eol: eol);
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Extra 6 0 R /Gone 5 0 R >>"),
            [2] = file.Object(2, Pages),
            [3] = file.Object(3, TrailerLetterPage),
            [4] = file.Object(4, "<< /Kind /Old >>"),
            [5] = file.Object(5, "<< /Deleted true >>"),
            [6] = file.Object(6, "<< /Ref 5 0 R >>"),
        };
        file.Table(7, rows, "/Foo 99 0 R /Size 7 /Root 1 0 R");
        file.Object(6, "<< /Ref 1 0 R >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>");
        TrailerTable(
            file,
            eol,
            "/Bar 4 0 R /Root 1 0 R /Size 7 /Prev 999999",
            (0, [(0, 65535, 'f'), (rows[1], 0, 'n'), (rows[2], 0, 'n'), (rows[3], 0, 'n')]),
            (5, [(0, 1, 'f'), (rows[6], 0, 'n')]));
        return file.Bytes;
    }

    /// <summary>
    /// Writes a classic table whose trailer, which the chain does not reach, gives <c>/Encrypt</c>, then object stream 8
    /// holding object 7, which the catalog names, and cross-reference stream 9, <c>/W [1 2 2]</c> and
    /// <c>/Index [0 4 7 3]</c>, whose <c>/Prev</c> leads outside the file.
    /// </summary>
    private static byte[] TrailerEncryptedOlderSection(string prefix, string eol)
    {
        const string members = "7 0 << /Note (packed) >>";
        var file = new HandWrittenFile(prefix, eol: eol);
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Packed 7 0 R >>"),
            [2] = file.Object(2, Pages),
            [3] = file.Object(3, Page),
            [4] = file.Object(4, "(an older object)"),
        };
        file.Table(
            5,
            rows,
            "/Size 5 /Root 1 0 R /Encrypt << /Filter /Standard /V 1 /R 2 /Length 40 /O (0123456789abcdef0123456789abcdef) "
            + "/U (0123456789abcdef0123456789abcdef) /P -4 >> /ID [<0011> <0011>]");
        var objectStream = file.Object(
            8,
            string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Type /ObjStm /N 1 /First 4 /Length {members.Length} >>{eol}stream{eol}{members}{eol}endstream"));
        var at = file.Position;
        (int Type, long Field2, int Field3)[] entries =
            [(0, 0, 65535), (1, rows[1], 0), (1, rows[2], 0), (1, rows[3], 0), (2, 8, 0), (1, objectStream, 0), (1, at, 0)];
        var data = new StringBuilder();

        foreach (var (type, field2, field3) in entries)
        {
            data.Append((char)type)
                .Append((char)((field2 >> 8) & 0xFF)).Append((char)(field2 & 0xFF))
                .Append((char)((field3 >> 8) & 0xFF)).Append((char)(field3 & 0xFF));
        }

        file.Object(
            9,
            string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Type /XRef /Size 10 /W [1 2 2] /Index [0 4 7 3] /Root 1 0 R /Bar 4 0 R /Prev 999999 /Length {data.Length} >>")
            + eol + "stream" + eol + data + eol + "endstream");
        file.Raw("startxref" + eol + at.ToString(CultureInfo.InvariantCulture) + eol + "%%EOF" + eol);
        return file.Bytes;
    }

    /// <summary>
    /// Writes object 6 with a name holding a null character where both tables place it, and a later definition without
    /// it, with CRLF line ends after a byte order mark.
    /// </summary>
    private static byte[] TrailerNullName()
    {
        const string eol = "\r\n";
        var file = new HandWrittenFile(TrailerByteOrderMark, eol: eol);
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Extra 6 0 R >>");
        var pages = file.Object(2, Pages);
        var page = file.Object(3, TrailerLetterPage);
        var older = file.Object(4, "<< /Kind /Old >>");
        var named = file.Object(6, "<< /Name /x#00y /Other 1 >>");
        TrailerTable(
            file,
            eol,
            "/Root 1 0 R /Size 7",
            (0, [(0, 65535, 'f'), (catalog, 0, 'n'), (pages, 0, 'n'), (page, 0, 'n'), (older, 0, 'n')]),
            (6, [(named, 0, 'n')]));
        file.Object(6, "<< /Name /xy /Other 1 >>");
        TrailerTable(
            file,
            eol,
            "/Bar 4 0 R /Root 1 0 R /Size 7 /Prev 999999",
            (0, [(0, 65535, 'f'), (catalog, 0, 'n'), (pages, 0, 'n'), (page, 0, 'n')]),
            (6, [(named, 0, 'n')]));
        return file.Bytes;
    }

    /// <summary>
    /// Writes object 7 without its <c>endobj</c> where both tables place it, and a later definition with it, with CRLF line
    /// ends after three bytes.
    /// </summary>
    private static byte[] TrailerEndObj()
    {
        const string eol = "\r\n";
        var file = new HandWrittenFile("~~\n", eol: eol);
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Extra 7 0 R >>");
        var pages = file.Object(2, Pages);
        var page = file.Object(3, TrailerLetterPage);
        var unclosed = file.Position;
        file.Raw("7 0 obj" + eol + "<< /B (no endobj) >>" + eol);
        var older = file.Object(4, "<< /Kind /Old >>");
        TrailerTable(
            file,
            eol,
            "/Root 1 0 R /Size 8",
            (0, [(0, 65535, 'f'), (catalog, 0, 'n'), (pages, 0, 'n'), (page, 0, 'n'), (older, 0, 'n')]),
            (7, [(unclosed, 0, 'n')]));
        file.Object(7, "<< /B (with endobj) >>");
        TrailerTable(
            file,
            eol,
            "/Bar 4 0 R /Root 1 0 R /Size 8 /Prev 999999",
            (0, [(0, 65535, 'f'), (catalog, 0, 'n'), (pages, 0, 'n'), (page, 0, 'n')]),
            (7, [(unclosed, 0, 'n')]));
        return file.Bytes;
    }

    /// <summary>
    /// Writes a classic table of <paramref name="subsections"/>, each its first number and its rows — an offset, a
    /// generation and <c>n</c> or <c>f</c> —, its trailer holding <paramref name="trailer"/>, and the <c>startxref</c> naming
    /// it, for what <see cref="HandWrittenFile.Table"/> does not write: several subsections, or a generation other than 0.
    /// </summary>
    private static void TrailerTable(
        HandWrittenFile file,
        string eol,
        string trailer,
        params (int First, (long Offset, int Generation, char Kind)[] Rows)[] subsections)
    {
        var at = file.Position;
        var text = new StringBuilder("xref").Append(eol);

        foreach (var (first, rows) in subsections)
        {
            text.Append(CultureInfo.InvariantCulture, $"{first} {rows.Length}").Append(eol);

            foreach (var (offset, generation, kind) in rows)
            {
                text.Append(CultureInfo.InvariantCulture, $"{offset:D10} {generation:D5} {kind}").Append(eol == "\n" ? " \n" : "\r\n");
            }
        }

        text.Append("trailer").Append(eol).Append("<< ").Append(trailer).Append(" >>").Append(eol)
            .Append("startxref").Append(eol).Append(at.ToString(CultureInfo.InvariantCulture)).Append(eol)
            .Append("%%EOF").Append(eol);
        file.Raw(text.ToString());
    }
}
