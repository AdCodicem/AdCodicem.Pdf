using System.Diagnostics;
using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The <c>page-tree</c> family of M02's third slice: loops, repeated nodes, nodes without kids, kids that are no page,
/// <c>/Count</c>, <c>/Parent</c>, the media box and the resources each page inherits, and pages the tree leaves out —
/// each rule on a file that breaks it, a sound file, and a file that is unusual and legal.
/// </summary>
public class PageTreeRuleTests
{
    /// <summary>
    /// Three pages under two levels: page 3 and node 4 under the root 2, pages 5 and 6 under node 4. The root gives
    /// every page its media box and its resources; page 6 has a media box of its own.
    /// </summary>
    private const string Tree = """
        %PDF-1.7
        1 0 obj
        << /Type /Catalog /Pages 2 0 R >>
        endobj
        2 0 obj
        << /Type /Pages /Kids [3 0 R 4 0 R] /Count 3 /MediaBox [0 0 595 842] /Resources << >> >>
        endobj
        3 0 obj
        << /Type /Page /Parent 2 0 R >>
        endobj
        4 0 obj
        << /Type /Pages /Parent 2 0 R /Kids [5 0 R 6 0 R] /Count 2 >>
        endobj
        5 0 obj
        << /Type /Page /Parent 4 0 R >>
        endobj
        6 0 obj
        << /Type /Page /Parent 4 0 R /MediaBox [0 0 612 792] >>
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
        << /Size 7 /Root 1 0 R >>
        startxref
        {xref:1}
        %%EOF

        """;

    [Fact]
    public void A_tree_whose_pages_inherit_their_box_and_resources_is_sound()
    {
        using var document = PdfDocument.Open(PdfTemplate.Build(Tree));

        new PdfValidator().Validate(document).Findings.Should().BeEmpty();
        PageTreeWalk.Run(document).PageCount.Should().Be(3);
    }

    [Fact]
    public void An_empty_tree_is_sound()
    {
        var report = Validate(PdfTemplate.SoundWith("/Kids [3 0 R] /Count 1", "/Kids [] /Count 0"));

        report.Contains(PdfValidationRuleIds.PageTreeKidsMissing).Should().BeFalse("an empty /Kids is a /Kids");
        report.Contains(PdfValidationRuleIds.PageTreeCountMismatch).Should().BeFalse();
    }

    [Fact]
    public void A_kid_naming_the_node_that_lists_it_is_a_loop_and_an_error()
    {
        var file = TreeWith("/Kids [3 0 R 4 0 R] /Count 3", "/Kids [2 0 R] /Count 3");

        var report = Validate(file);

        var finding = Single(report, PdfValidationRuleIds.PageTreeCycle);
        finding.Severity.Should().Be(PdfValidationSeverity.Error, "what the tree should have listed there is unknown (ADR 45)");
        finding.Location.Object.Should().Be(new PdfObjectId(2));
        finding.Message.Should().Be(
            "The kid at index 0 of page tree node 2 names object 2, which is that node itself: the tree loops back on itself there, and what it should have listed is unknown.");
        report.Contains(PdfValidationRuleIds.PageTreeCountMismatch).Should().BeFalse("a count above a loop cannot be judged");
    }

    [Fact]
    public void A_kid_naming_a_node_above_it_is_a_loop_and_the_counts_above_are_not_judged()
    {
        var file = TreeWith("/Kids [5 0 R 6 0 R] /Count 2", "/Kids [5 0 R 2 0 R 6 0 R] /Count 2");

        using var document = PdfDocument.Open(file);
        var report = new PdfValidator().Validate(document);

        Single(report, PdfValidationRuleIds.PageTreeCycle).Message.Should().Be(
            "The kid at index 1 of page tree node 4 names object 2, a node above it: the tree loops back on itself there, and what it should have listed is unknown.");
        report.Contains(PdfValidationRuleIds.PageTreeCountMismatch).Should().BeFalse();
        report.Contains(PdfValidationRuleIds.PageTreeNodeRepeated).Should().BeFalse("a loop is not a repetition");
        PageTreeWalk.Run(document).PageCount.Should().Be(3, "a kid that loops back counts nothing");
    }

    [Fact]
    public void Back_links_through_parents_and_destinations_are_no_loop()
    {
        var file = TreeWith(
            "<< /Type /Page /Parent 4 0 R >>",
            "<< /Type /Page /Parent 4 0 R /Annots [<< /Type /Annot /Subtype /Link /Rect [0 0 1 1] /Dest [3 0 R /Fit] >>] >>");

        Validate(file).Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_page_listed_twice_is_repeated_and_counted_each_time()
    {
        var file = TreeWith("/Kids [3 0 R 4 0 R] /Count 3", "/Kids [3 0 R 4 0 R 3 0 R] /Count 4");

        using var document = PdfDocument.Open(file);
        var report = new PdfValidator().Validate(document);

        var finding = Single(report, PdfValidationRuleIds.PageTreeNodeRepeated);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning, "the page is shown twice, as the file evidently means (ADR 45)");
        finding.Message.Should().Be(
            "The kid at index 2 of page tree node 2 names object 3, which the tree already lists: its page is counted again.");
        report.Contains(PdfValidationRuleIds.PageTreeCountMismatch).Should().BeFalse("the /Count counts the page twice, as the tree lists it");
        PageTreeWalk.Run(document).PageCount.Should().Be(4);
    }

    [Fact]
    public void A_node_listed_twice_counts_its_pages_again()
    {
        var file = TreeWith("/Kids [3 0 R 4 0 R] /Count 3", "/Kids [3 0 R 4 0 R 4 0 R] /Count 5");

        using var document = PdfDocument.Open(file);
        var report = new PdfValidator().Validate(document);

        Single(report, PdfValidationRuleIds.PageTreeNodeRepeated).Message.Should().Be(
            "The kid at index 2 of page tree node 2 names object 4, which the tree already lists: its 2 pages are counted again.");
        report.Contains(PdfValidationRuleIds.PageTreeCountMismatch).Should().BeFalse();
        PageTreeWalk.Run(document).PageCount.Should().Be(5);
    }

    [Fact]
    public void A_page_a_destination_names_again_is_not_repeated()
    {
        var file = TreeWith("/Type /Catalog /Pages 2 0 R", "/Type /Catalog /Pages 2 0 R /OpenAction [5 0 R /Fit]");

        Validate(file).Findings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/Type /Pages /Parent 2 0 R /Count 2", "Page tree node 4 has no /Kids: it lists no page.")]
    [InlineData("/Type /Pages /Parent 2 0 R /Kids 7 /Count 2", "Page tree node 4 has a number for /Kids, not an array: it lists no page.")]
    public void A_node_without_a_kids_array_lists_no_page(string node, string message)
    {
        var file = TreeWith("/Type /Pages /Parent 2 0 R /Kids [5 0 R 6 0 R] /Count 2", node);

        using var document = PdfDocument.Open(file);
        var report = new PdfValidator().Validate(document);

        var finding = Single(report, PdfValidationRuleIds.PageTreeKidsMissing);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning);
        finding.Location.Object.Should().Be(new PdfObjectId(4));
        finding.Message.Should().Be(message);
        report.Contains(PdfValidationRuleIds.PageTreeCountMismatch).Should().BeFalse("the counts above it cannot be judged");
        report.Findings.Where(found => found.RuleId == PdfValidationRuleIds.PageTreePageOrphaned)
            .Select(found => found.Location.Object).Should().Equal(new PdfObjectId(5), new PdfObjectId(6));
        PageTreeWalk.Run(document).PageCount.Should().Be(1);
    }

    [Theory]
    [InlineData("<< /Kids [5 0 R 6 0 R] >>", "Page tree node 4 has a dictionary for /Kids, not an array: it lists no page.")]
    [InlineData("2", "Page tree node 4 has a number for /Kids, not an array: it lists no page.")]
    [InlineData("null", "Page tree node 4 has no /Kids: it lists no page.")]
    public void A_kids_entry_naming_an_object_that_is_no_array_lists_no_page(string seventh, string message)
    {
        var report = Validate(TreeWith("/Kids [5 0 R 6 0 R] /Count 2", "/Kids 7 0 R /Count 2", seventh));

        var finding = Single(report, PdfValidationRuleIds.PageTreeKidsMissing);
        finding.Location.Object.Should().Be(new PdfObjectId(4));
        finding.Message.Should().Be(message);
        report.Contains(PdfValidationRuleIds.ObjectReferenceMissing).Should().BeFalse("object 7 is there");
    }

    [Fact]
    public void A_kids_array_naming_nothing_is_the_missing_reference_alone()
    {
        var report = Validate(TreeWith("/Kids [5 0 R 6 0 R] /Count 2", "/Kids 9 0 R /Count 2"));

        report.Contains(PdfValidationRuleIds.PageTreeKidsMissing).Should().BeFalse();
        Single(report, PdfValidationRuleIds.ObjectReferenceMissing).Location.Object.Should().Be(new PdfObjectId(4));
    }

    [Theory]
    [InlineData("null", "The kid at index 1 of page tree node 2 is null: it counts as a page with nothing on it.")]
    [InlineData("9 0 R", "The kid at index 1 of page tree node 2 names object 9, which the file lacks: it counts as a page with nothing on it.")]
    [InlineData("0 0 R", "The kid at index 1 of page tree node 2 names object 0, which the file lacks: it counts as a page with nothing on it.")]
    [InlineData("42", "The kid at index 1 of page tree node 2 is a number, neither a page nor a node: it counts as a page with nothing on it.")]
    [InlineData("/Page", "The kid at index 1 of page tree node 2 is a name, neither a page nor a node: it counts as a page with nothing on it.")]
    public void A_kid_that_is_no_page_takes_a_blank_page_s_place(string kid, string message)
    {
        var file = TreeWith("/Kids [3 0 R 4 0 R] /Count 3", $"/Kids [3 0 R {kid} 4 0 R] /Count 4");

        using var document = PdfDocument.Open(file);
        var report = new PdfValidator().Validate(document);

        var finding = Single(report, PdfValidationRuleIds.PageTreeKidInvalid);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning, "the reader reads the kid as null, as the specification says (ADR 45)");
        finding.Location.Object.Should().Be(new PdfObjectId(2));
        finding.Location.PageIndex.Should().Be(1);
        finding.Message.Should().Be(message);
        report.Contains(PdfValidationRuleIds.ObjectReferenceMissing).Should().BeFalse("a kid that names nothing is this rule's");
        report.Contains(PdfValidationRuleIds.PageTreeCountMismatch).Should().BeFalse("the /Count counts the blank page");
        PageTreeWalk.Run(document).PageCount.Should().Be(4);
    }

    [Fact]
    public void A_kid_that_is_no_page_past_the_last_page_index_is_located_at_its_node_alone()
    {
        // Each node lists the one below it twice: 2^32 pages before the root's last kid, more than a page index can say.
        const int levels = 32;
        var builder = new TestPdfBuilder().WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>");

        for (var node = 2; node < levels + 2; node++)
        {
            var last = node == 2 ? " null" : string.Empty;
            builder.WithObject(node, $"<< /Type /Pages /Kids [{node + 1} 0 R {node + 1} 0 R{last}] /Count 2 >>");
        }

        builder.WithObject(levels + 2, "<< /Type /Page /MediaBox [0 0 595 842] /Resources << >> >>");

        var finding = Single(Validate(builder.BuildClassic(rootNumber: 1)), PdfValidationRuleIds.PageTreeKidInvalid);

        finding.Location.Object.Should().Be(new PdfObjectId(2));
        finding.Location.PageIndex.Should().BeNull();
        finding.Message.Should().Be("The kid at index 2 of page tree node 2 is null: it counts as a page with nothing on it.");
    }

    [Fact]
    public void A_kid_naming_a_literal_null_takes_a_blank_page_s_place()
    {
        var template = Tree
            .Replace("/Kids [3 0 R 4 0 R] /Count 3", "/Kids [3 0 R 7 0 R 4 0 R] /Count 4", StringComparison.Ordinal)
            .Replace("xref\n0 7\n", "7 0 obj\nnull\nendobj\nxref\n0 8\n", StringComparison.Ordinal)
            .Replace("{row:6}\n", "{row:6}\n{row:7}\n", StringComparison.Ordinal)
            .Replace("/Size 7", "/Size 8", StringComparison.Ordinal);

        Single(Validate(PdfTemplate.Build(template)), PdfValidationRuleIds.PageTreeKidInvalid).Message.Should().Be(
            "The kid at index 1 of page tree node 2 names object 7, which is null: it counts as a page with nothing on it.");
    }

    [Fact]
    public void A_page_given_a_stream_body_is_read_through_its_dictionary()
    {
        var file = TreeWith("<< /Type /Page /Parent 2 0 R >>\nendobj", "<< /Type /Page /Parent 2 0 R /Length 0 >>\nstream\n\nendstream\nendobj");

        using var document = PdfDocument.Open(file);
        var report = new PdfValidator().Validate(document);

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.PageTreeKidInvalid);
        finding.Location.Object.Should().Be(new PdfObjectId(3));
        finding.Message.Should().Be(
            "The kid at index 0 of page tree node 2 names object 3, a stream rather than a dictionary: the reader reads the stream's dictionary in its place.");
        PageTreeWalk.Run(document).PageCount.Should().Be(3);
    }

    [Fact]
    public void A_root_given_a_stream_body_is_walked_through_its_dictionary()
    {
        var file = TreeWith("/Resources << >> >>\nendobj", "/Resources << >> /Length 0 >>\nstream\n\nendstream\nendobj");

        using var document = PdfDocument.Open(file);
        var report = new PdfValidator().Validate(document);

        PageTreeWalk.Run(document).PageCount.Should().Be(3);
        report.Contains(PdfValidationRuleIds.PageTreePageOrphaned).Should().BeFalse("the root's kids are walked: " + string.Join("; ", report.Findings));
    }

    [Fact]
    public void A_page_given_a_stream_body_in_the_array_is_reported_at_the_node_listing_it()
    {
        var file = TreeWith("/Kids [5 0 R 6 0 R] /Count 2", "/Kids [5 0 R << /Type /Page /Length 0 >>\nstream\n\nendstream] /Count 2");

        using var document = PdfDocument.Open(file);
        var report = new PdfValidator().Validate(document);

        var finding = Single(report, PdfValidationRuleIds.PageTreeKidInvalid);
        finding.Location.Should().Be(PdfValidationLocation.OfObject(new PdfObjectId(4)), "the stream has no number of its own to be located by");
        finding.Message.Should().Be(
            "The kid at index 1 of page tree node 4 is a stream written in the array, where the tree wants an indirect reference to a dictionary: the reader reads the stream's dictionary in its place.");
        PageTreeWalk.Run(document).PageCount.Should().Be(3);
    }

    [Fact]
    public void A_page_written_in_the_array_is_read_as_it_is()
    {
        var file = TreeWith("/Kids [5 0 R 6 0 R] /Count 2", "/Kids [5 0 R << /Type /Page >>] /Count 2");

        var report = Validate(file);

        Single(report, PdfValidationRuleIds.PageTreeKidInvalid).Message.Should().Be(
            "The kid at index 1 of page tree node 4 is a dictionary written in the array, where the tree wants an indirect reference to one.");
        report.Contains(PdfValidationRuleIds.PageTreeParentWrong).Should().BeFalse("a kid with no number cannot be named by /Parent");
    }

    [Fact]
    public void A_node_written_in_the_array_without_kids_is_named_by_where_it_is_written()
    {
        var file = TreeWith("/Kids [5 0 R 6 0 R] /Count 2", "/Kids [5 0 R << /Type /Pages /Count 0 >>] /Count 2");

        var finding = Single(Validate(file), PdfValidationRuleIds.PageTreeKidsMissing);

        finding.Message.Should().Be("The page tree node under /Kids[1] of page tree node 4 has no /Kids: it lists no page.");
        finding.Location.Should().Be(PdfValidationLocation.OfObject(new PdfObjectId(4)), "it is written in node 4");
    }

    [Fact]
    public void A_page_written_in_the_array_is_named_and_located_by_the_node_it_is_written_in()
    {
        // Batch 13's shape (#129): the page was located at "object 0 0".
        var file = PdfTemplate.SoundWith("/Kids [3 0 R]", "/Kids [3 0 R << /A 9 0 R >>]");

        var report = Validate(file);

        Single(report, PdfValidationRuleIds.PageTreeMediaBoxInvalid).Message.Should().Be(
            "The page object under /Kids[1] of page tree node 2 has no /MediaBox, and no page tree node above it gives one: the page's size is unknown.");
        Single(report, PdfValidationRuleIds.PageTreeMediaBoxInvalid).Location.Should().Be(PdfValidationLocation.OnPage(1, new PdfObjectId(2)));
        Single(report, PdfValidationRuleIds.PageTreeResourcesMissing).Message.Should().Be(
            "The page object under /Kids[1] of page tree node 2 has no /Resources, and no page tree node above it gives any.");
        Single(report, PdfValidationRuleIds.PageTreeResourcesMissing).Location.Should().Be(PdfValidationLocation.OnPage(1, new PdfObjectId(2)));
    }

    [Theory]
    [InlineData("/MediaBox 5", "The /MediaBox of the page object under /Kids[1] of page tree node 4 is a number, not a rectangle.")]
    [InlineData("/MediaBox [0 0 5]", "The /MediaBox of the page object under /Kids[1] of page tree node 4 is an array of 3 values, not a rectangle.")]
    [InlineData("/MediaBox [0 0 5 (x)]", "The /MediaBox of the page object under /Kids[1] of page tree node 4 holds a string among its corners, not a rectangle of four numbers.")]
    [InlineData("/MediaBox [0 0 0 0]", "The /MediaBox of the page object under /Kids[1] of page tree node 4, [0 0 0 0], encloses no area.")]
    public void The_media_box_of_a_page_written_in_the_array_is_that_page_s(string box, string message)
    {
        var file = TreeWith("/Kids [5 0 R 6 0 R] /Count 2", $"/Kids [5 0 R << /Type /Page {box} >>] /Count 2");

        var finding = Single(Validate(file), PdfValidationRuleIds.PageTreeMediaBoxInvalid);

        finding.Message.Should().Be(message);
        finding.Location.Should().Be(PdfValidationLocation.OnPage(2, new PdfObjectId(4)));
    }

    [Fact]
    public void A_node_written_in_the_array_is_named_by_its_path_in_every_finding_on_it()
    {
        // After pages 5 and 6, a node in node 4's array, itself listing a null and a page in its own, with a media box of
        // three values and a /Count of 3; the page's media box is a number. The root's /Count is 5, as are the pages the
        // tree lists.
        var file = PdfTemplate.Build(Tree
            .Replace("/Kids [3 0 R 4 0 R] /Count 3", "/Kids [3 0 R 4 0 R] /Count 5", StringComparison.Ordinal)
            .Replace(
                "/Kids [5 0 R 6 0 R] /Count 2",
                "/Kids [5 0 R 6 0 R << /Type /Pages /Kids [null << /Type /Page /MediaBox 7 >>] /Count 3 /MediaBox [0 0 5] >>] /Count 4",
                StringComparison.Ordinal));

        using var document = PdfDocument.Open(file);
        var report = new PdfValidator().Validate(document);
        var node = PdfValidationLocation.OfObject(new PdfObjectId(4));

        Single(report, PdfValidationRuleIds.PageTreeCountMismatch).Should().Match<PdfValidationFinding>(finding =>
            finding.Message == "The page tree node under /Kids[2] of page tree node 4 gives /Count 3, and 2 pages lie below it." &&
            finding.Location == node);
        report.Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.PageTreeMediaBoxInvalid).Select(finding => (finding.Location, finding.Message))
            .Should().Equal(
                (node, "The /MediaBox of the page tree node under /Kids[2] of page tree node 4 is an array of 3 values, not a rectangle."),
                (PdfValidationLocation.OnPage(4, new PdfObjectId(4)), "The /MediaBox of the page object under /Kids[2]/Kids[1] of page tree node 4 is a number, not a rectangle."));
        report.Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.PageTreeKidInvalid).Select(finding => (finding.Location, finding.Message))
            .Should().Equal(
                (node, "The kid at index 2 of page tree node 4 is a dictionary written in the array, where the tree wants an indirect reference to one."),
                (PdfValidationLocation.OnPage(3, new PdfObjectId(4)), "The kid at index 0 of the page tree node under /Kids[2] of page tree node 4 is null: it counts as a page with nothing on it."),
                (node, "The kid at index 1 of the page tree node under /Kids[2] of page tree node 4 is a dictionary written in the array, where the tree wants an indirect reference to one."));
        report.Contains(PdfValidationRuleIds.PageTreePageOrphaned).Should().BeFalse();
        PageTreeWalk.Run(document).PageCount.Should().Be(5);
    }

    [Fact]
    public void A_kid_of_a_node_written_in_the_array_that_loops_back_or_repeats_is_named_by_that_node_s_path()
    {
        var looping = TreeWith("/Kids [5 0 R 6 0 R] /Count 2", "/Kids [5 0 R << /Type /Pages /Kids [2 0 R] /Count 0 >> 6 0 R] /Count 2");
        var repeating = TreeWith("/Kids [5 0 R 6 0 R] /Count 2", "/Kids [5 0 R << /Type /Pages /Kids [5 0 R] /Count 1 >> 6 0 R] /Count 3");

        var cycle = Single(Validate(looping), PdfValidationRuleIds.PageTreeCycle);
        var repeated = Single(Validate(repeating), PdfValidationRuleIds.PageTreeNodeRepeated);

        cycle.Message.Should().Be(
            "The kid at index 0 of the page tree node under /Kids[1] of page tree node 4 names object 2, a node above it: the tree loops back on itself there, and what it should have listed is unknown.");
        cycle.Location.Should().Be(PdfValidationLocation.OfObject(new PdfObjectId(4)));
        repeated.Message.Should().Be(
            "The kid at index 0 of the page tree node under /Kids[1] of page tree node 4 names object 5, which the tree already lists: its page is counted again.");
        repeated.Location.Should().Be(PdfValidationLocation.OfObject(new PdfObjectId(4)));
    }

    [Fact]
    public void A_root_written_in_the_catalog_is_named_under_the_catalog_s_pages_not_a_parent_s_kids()
    {
        var file = PdfTemplate.SoundWith(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Catalog /Pages << /Type /Pages /Kids [3 0 R] /Count 2 /Parent 3 0 R >> >>");

        var report = Validate(file);
        var catalog = PdfValidationLocation.OfObject(new PdfObjectId(1));

        Single(report, PdfValidationRuleIds.PageTreeCountMismatch).Should().Match<PdfValidationFinding>(finding =>
            finding.Message == "The page tree node under /Pages of object 1 gives /Count 2, and 1 page lies below it." &&
            finding.Location == catalog);
        Single(report, PdfValidationRuleIds.PageTreeParentWrong).Should().Match<PdfValidationFinding>(finding =>
            finding.Message == "The root of the page tree, under /Pages of object 1, has a /Parent, which only the nodes below it may have." &&
            finding.Location == catalog);
    }

    [Fact]
    public void A_root_written_in_the_catalog_that_is_a_page_is_located_on_its_page_at_the_catalog()
    {
        var file = PdfTemplate.SoundWith(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Catalog /Pages << /Type /Page /Kids 5 /MediaBox [0 0 0 0] >> >>");

        var report = Validate(file);

        report.Findings.Where(finding => finding.RuleId.StartsWith("page-tree.", StringComparison.Ordinal) && finding.RuleId != PdfValidationRuleIds.PageTreePageOrphaned)
            .Select(finding => (finding.Location, finding.Message))
            .Should().Equal(
                (PdfValidationLocation.OnPage(0, new PdfObjectId(1)), "The /MediaBox of the page object under /Pages of object 1, [0 0 0 0], encloses no area."),
                (PdfValidationLocation.OnPage(0, new PdfObjectId(1)), "The page object under /Pages of object 1 has no /Resources, and no page tree node above it gives any."));
    }

    [Fact]
    public void A_root_written_in_a_catalog_written_in_the_trailer_is_located_at_the_trailer()
    {
        var file = PdfTemplate.SoundWith(
            "<< /Size 4 /Root 1 0 R >>",
            "<< /Size 4 /Root << /Type /Catalog /Pages << /Type /Pages /Kids [3 0 R] /Count 2 >> >> >>");

        using var document = PdfDocument.Open(file);
        var finding = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.PageTreeCountMismatch);

        finding.Message.Should().Be("The page tree node under /Root/Pages of the trailer gives /Count 2, and 1 page lies below it.");
        finding.Location.Should().Be(PdfValidationLocation.AtPosition(document.Reader.TrailerLocation));
        document.Reader.TrailerLocation.Should().Be(PdfTemplate.OffsetOf(file, "trailer"));
    }

    [Fact]
    public void A_kid_written_in_an_array_of_kids_of_its_own_is_located_at_that_array()
    {
        var file = TreeWith("/Kids [5 0 R 6 0 R] /Count 2", "/Kids 7 0 R /Count 2", "[5 0 R << /Type /Page /MediaBox 5 >>]");

        var report = Validate(file);
        var array = new PdfObjectId(7);

        Single(report, PdfValidationRuleIds.PageTreeMediaBoxInvalid).Should().Match<PdfValidationFinding>(finding =>
            finding.Message == "The /MediaBox of the page object at [1] in object 7 is a number, not a rectangle." &&
            finding.Location == PdfValidationLocation.OnPage(2, array));
        Single(report, PdfValidationRuleIds.PageTreeKidInvalid).Location.Should().Be(
            PdfValidationLocation.OfObject(new PdfObjectId(4)), "the kid is node 4's, whichever object its array is");
    }

    [Fact]
    public void A_long_path_to_a_node_written_in_the_array_is_shortened_as_any_path_is()
    {
        // Six nodes, each written in the one above, the last without /Kids: a path of twelve steps, the four past the first
        // four and before the last four counted rather than written.
        var nested = "<< /Type /Pages /Count 0 >>";

        for (var level = 0; level < 5; level++)
        {
            nested = $"<< /Type /Pages /Kids [{nested}] /Count 0 >>";
        }

        var file = TreeWith("/Kids [5 0 R 6 0 R] /Count 2", $"/Kids [{nested}] /Count 0");

        Single(Validate(file), PdfValidationRuleIds.PageTreeKidsMissing).Message.Should().Be(
            "The page tree node under /Kids[0]/Kids[0] (4 steps) /Kids[0]/Kids[0] of page tree node 4 has no /Kids: it lists no page.");
    }

    [Fact]
    public void A_kid_whose_entry_leads_nowhere_is_the_entry_s_finding_and_still_takes_a_page_s_place()
    {
        // Object 5's header is gone: its entry leads nowhere, and a rebuild does not find it either.
        var file = PdfTemplate.Build(Tree.Replace("5 0 obj\n", "5 0 obk\n", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file);
        var report = new PdfValidator().Validate(document);

        report.Contains(PdfValidationRuleIds.XRefEntryBroken).Should().BeTrue();
        report.Contains(PdfValidationRuleIds.PageTreeKidInvalid).Should().BeFalse("the entry is the fault, which the cross-reference rules report");
        report.Contains(PdfValidationRuleIds.PageTreeCountMismatch).Should().BeFalse("what the kid was cannot be known");
        report.Contains(PdfValidationRuleIds.PageTreePageOrphaned).Should().BeFalse("the index is not sound enough to say a page is left out");
        PageTreeWalk.Run(document).PageCount.Should().Be(3);
    }

    [Fact]
    public void A_kid_a_guard_left_out_of_the_index_is_no_kid_invalid_and_still_takes_a_page_s_place()
    {
        // The update rewrites the catalog and the page tree; the page lies only in the original section, which
        // MaxXRefSectionCount leaves unread: the kid is unknown, not one the file lacks.
        var original = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>")
            .BuildClassic(rootNumber: 1);
        var file = TestPdfBuilder.AppendIncrementalUpdate(
            original,
            rootNumber: 1,
            [(1, "<< /Type /Catalog /Pages 2 0 R >>"), (2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")]);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 1 } };

        using var document = PdfDocument.Open(file, options);
        var report = new PdfValidator().Validate(document);

        report.Contains(PdfValidationRuleIds.XRefCheckedInPart).Should().BeTrue();
        report.Contains(PdfValidationRuleIds.PageTreeKidInvalid).Should().BeFalse("whether the file holds the kid is unknown");
        report.Contains(PdfValidationRuleIds.ObjectReferenceMissing).Should().BeFalse();
        PageTreeWalk.Run(document).PageCount.Should().Be(1);
    }

    [Theory]
    [InlineData("/Kids [3 0 R 4 0 R] /Count 3", "/Kids [3 0 R 4 0 R] /Count 9", 2, "Page tree node 2 gives /Count 9, and 3 pages lie below it.")]
    [InlineData("/Kids [3 0 R 4 0 R] /Count 3", "/Kids [3 0 R 4 0 R]", 2, "Page tree node 2 has no /Count; 3 pages lie below it.")]
    [InlineData("/Kids [5 0 R 6 0 R] /Count 2", "/Kids [5 0 R 6 0 R] /Count 1", 4, "Page tree node 4 gives /Count 1, and 2 pages lie below it.")]
    public void A_count_that_is_not_the_number_of_pages_below_is_a_warning(string text, string replacement, int node, string message)
    {
        var report = Validate(TreeWith(text, replacement));

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.PageTreeCountMismatch);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning, "the reader counts the pages the tree lists (ADR 45)");
        finding.Location.Object.Should().Be(new PdfObjectId(node));
        finding.Message.Should().Be(message);
    }

    [Theory]
    [InlineData("/Kids [5 0 R 6 0 R] /Count null")]
    [InlineData("/Kids [5 0 R 6 0 R] /Count 7 0 R")]
    public void A_count_given_as_null_is_missing(string replacement)
    {
        var report = Validate(TreeWith("/Kids [5 0 R 6 0 R] /Count 2", replacement, "null"));

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.PageTreeCountMismatch);
        finding.Message.Should().Be("Page tree node 4 has no /Count; 2 pages lie below it.", "null in a dictionary is absence (ISO 32000-1, 7.3.7)");
    }

    [Fact]
    public void A_node_without_kids_lacks_its_count_as_well()
    {
        var report = Validate(TreeWith("/Type /Pages /Kids [3 0 R 4 0 R] /Count 3", "/Type /Pages"));

        Single(report, PdfValidationRuleIds.PageTreeKidsMissing).Message.Should().Be("Page tree node 2 has no /Kids: it lists no page.");
        Single(report, PdfValidationRuleIds.PageTreeCountMismatch).Message.Should().Be("Page tree node 2 has no /Count; 0 pages lie below it.");
    }

    [Fact]
    public void A_node_whose_pages_cannot_be_counted_still_lacks_its_count()
    {
        var report = Validate(TreeWith("/Kids [5 0 R 6 0 R] /Count 2", "/Kids [5 0 R 2 0 R 6 0 R]"));

        report.Contains(PdfValidationRuleIds.PageTreeCycle).Should().BeTrue();
        Single(report, PdfValidationRuleIds.PageTreeCountMismatch).Message.Should().Be(
            "Page tree node 4 has no /Count, and how many pages lie below it is unknown.");
    }

    [Fact]
    public void A_count_one_page_short_says_page_in_the_singular()
    {
        Single(Validate(PdfTemplate.SoundWith("/Count 1", "/Count 2")), PdfValidationRuleIds.PageTreeCountMismatch).Message
            .Should().Be("Page tree node 2 gives /Count 2, and 1 page lies below it.");
    }

    [Fact]
    public void A_count_that_is_no_integer_is_not_this_rule_s()
    {
        Validate(TreeWith("/Count 3 /MediaBox", "/Count 3.0 /MediaBox")).Contains(PdfValidationRuleIds.PageTreeCountMismatch).Should().BeFalse();
    }

    [Theory]
    [InlineData("<< /Type /Page /Parent 4 0 R >>", "<< /Type /Page >>", "Page object 5 has no /Parent, and page tree node 4 lists it.")]
    [InlineData("<< /Type /Page /Parent 4 0 R >>", "<< /Type /Page /Parent 2 0 R >>", "Page object 5 gives /Parent 2 0 R, and page tree node 4 lists it.")]
    [InlineData("<< /Type /Page /Parent 4 0 R >>", "<< /Type /Page /Parent [4 0 R] >>", "Page object 5 has an array for /Parent, and page tree node 4 lists it.")]
    public void A_page_whose_parent_is_not_the_node_listing_it_is_a_warning(string text, string replacement, string message)
    {
        var report = Validate(TreeWith(text, replacement));

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.PageTreeParentWrong);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning, "the reader walks the tree through /Kids (ADR 45)");
        finding.Location.Object.Should().Be(new PdfObjectId(5));
        finding.Location.PageIndex.Should().Be(1);
        finding.Message.Should().Be(message);
    }

    [Fact]
    public void A_node_without_parent_and_a_root_with_one_are_reported()
    {
        var file = TreeWith("/Type /Pages /Parent 2 0 R /Kids [5 0 R 6 0 R]", "/Type /Pages /Kids [5 0 R 6 0 R]");
        var rooted = TreeWith("/Type /Pages /Kids [3 0 R 4 0 R]", "/Type /Pages /Parent 1 0 R /Kids [3 0 R 4 0 R]");

        Single(Validate(file), PdfValidationRuleIds.PageTreeParentWrong).Message.Should().Be(
            "Page tree node 4 has no /Parent, and page tree node 2 lists it.");
        Single(Validate(rooted), PdfValidationRuleIds.PageTreeParentWrong).Message.Should().Be(
            "The root of the page tree, object 2, has a /Parent, which only the nodes below it may have.");
    }

    [Fact]
    public void Pages_that_inherit_no_media_box_are_each_reported()
    {
        var report = Validate(TreeWith(" /MediaBox [0 0 595 842]", string.Empty));

        var findings = report.Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.PageTreeMediaBoxInvalid).ToList();
        findings.Select(finding => finding.Location.PageIndex).Should().Equal(0, 1);
        findings[0].Severity.Should().Be(PdfValidationSeverity.Warning, "the reader has chosen no size for the page (ADR 45)");
        findings[0].Location.Object.Should().Be(new PdfObjectId(3));
        findings[0].Location.ToString().Should().Be("page 1, object 3 0");
        findings[0].Message.Should().Be("Page object 3 has no /MediaBox, and no page tree node above it gives one: the page's size is unknown.");
        report.Findings.Should().HaveCount(2, "page 6 has a box of its own");
    }

    [Theory]
    [InlineData(" /MediaBox null")]
    [InlineData(" /MediaBox 7 0 R")]
    public void A_media_box_given_as_null_is_missing(string replacement)
    {
        var report = Validate(TreeWith(" /MediaBox [0 0 595 842]", replacement, "null"));

        report.Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.PageTreeMediaBoxInvalid)
            .Select(finding => finding.Location.PageIndex).Should().Equal(0, 1);
        report.Findings.Should().HaveCount(2, "the null the root gives is absence, and page 6 has a box of its own");
    }

    [Theory]
    [InlineData("[0 0 612]", "The /MediaBox of object 6 is an array of 3 values, not a rectangle.")]
    [InlineData("/Letter", "The /MediaBox of object 6 is a name, not a rectangle.")]
    [InlineData("[0 0 (612) 792]", "The /MediaBox of object 6 holds a string among its corners, not a rectangle of four numbers.")]
    [InlineData("[0 0 612 0]", "The /MediaBox of object 6, [0 0 612 0], encloses no area.")]
    [InlineData("[10.5 20 10.5 792]", "The /MediaBox of object 6, [10.5 20 10.5 792], encloses no area.")]
    [InlineData("[0.0000001 0 0.0000001 792.0]", "The /MediaBox of object 6, [0.0000001 0 0.0000001 792.0], encloses no area.")]
    [InlineData("[123456789012345678901 0 123456789012345678901 1]", "The /MediaBox of object 6, [123456789012345680000.0 0 123456789012345680000.0 1], encloses no area.")]
    public void A_media_box_that_is_no_rectangle_is_reported_where_it_is_written(string box, string message)
    {
        var finding = Single(
            Validate(TreeWith("/MediaBox [0 0 612 792]", "/MediaBox " + box)),
            PdfValidationRuleIds.PageTreeMediaBoxInvalid);

        finding.Location.Object.Should().Be(new PdfObjectId(6));
        finding.Location.PageIndex.Should().Be(2);
        finding.Message.Should().Be(message);
    }

    [Fact]
    public void A_node_s_media_box_that_is_no_rectangle_is_reported_once_at_the_node()
    {
        var finding = Single(
            Validate(TreeWith("/MediaBox [0 0 595 842]", "/MediaBox [0 0 595]")),
            PdfValidationRuleIds.PageTreeMediaBoxInvalid);

        finding.Location.Object.Should().Be(new PdfObjectId(2));
        finding.Location.PageIndex.Should().BeNull();
        finding.Message.Should().Be("The /MediaBox of object 2 is an array of 3 values, not a rectangle.");
    }

    [Theory]
    [InlineData("[612 792 0 0]")]
    [InlineData("[0.5 0 612.25 792]")]
    [InlineData("[-612 -792 0 0]")]
    public void Any_two_opposite_corners_make_a_media_box(string box)
    {
        Validate(TreeWith("/MediaBox [0 0 612 792]", "/MediaBox " + box)).Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_media_box_naming_nothing_is_the_missing_reference_alone()
    {
        var report = Validate(TreeWith("/MediaBox [0 0 612 792]", "/MediaBox 9 0 R"));

        report.Contains(PdfValidationRuleIds.PageTreeMediaBoxInvalid).Should().BeFalse();
        Single(report, PdfValidationRuleIds.ObjectReferenceMissing).Location.PageIndex.Should().Be(2);
    }

    [Fact]
    public void Pages_that_inherit_no_resources_are_each_reported()
    {
        var report = Validate(TreeWith(" /Resources << >>", string.Empty));

        var findings = report.Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.PageTreeResourcesMissing).ToList();
        findings.Select(finding => finding.Location.Object).Should().Equal(new PdfObjectId(3), new PdfObjectId(5), new PdfObjectId(6));
        findings[0].Severity.Should().Be(PdfValidationSeverity.Warning);
        findings[0].Message.Should().Be("Page object 3 has no /Resources, and no page tree node above it gives any.");
        report.Findings.Should().HaveCount(3);
    }

    [Theory]
    [InlineData(" /Resources null")]
    [InlineData(" /Resources 7 0 R")]
    public void Resources_given_as_null_are_missing(string replacement)
    {
        var report = Validate(TreeWith(" /Resources << >>", replacement, "null"));

        report.Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.PageTreeResourcesMissing)
            .Select(finding => finding.Location.Object).Should().Equal(new PdfObjectId(3), new PdfObjectId(5), new PdfObjectId(6));
        report.Findings.Should().HaveCount(3);
    }

    [Fact]
    public void Resources_given_by_the_page_or_a_node_between_are_enough()
    {
        var template = Tree
            .Replace(" /Resources << >>", string.Empty, StringComparison.Ordinal)
            .Replace("<< /Type /Page /Parent 2 0 R >>", "<< /Type /Page /Parent 2 0 R /Resources << /ProcSet [/PDF] >> >>", StringComparison.Ordinal)
            .Replace("/Kids [5 0 R 6 0 R] /Count 2", "/Kids [5 0 R 6 0 R] /Count 2 /Resources 7 0 R", StringComparison.Ordinal)
            .Replace("xref\n0 7\n", "7 0 obj\n<< >>\nendobj\nxref\n0 8\n", StringComparison.Ordinal)
            .Replace("{row:6}\n", "{row:6}\n{row:7}\n", StringComparison.Ordinal)
            .Replace("/Size 7", "/Size 8", StringComparison.Ordinal);

        Validate(PdfTemplate.Build(template)).Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_page_the_tree_leaves_out_is_said_to_be_orphaned()
    {
        var report = Validate(WithObject(7, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>"));

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.PageTreePageOrphaned);
        finding.Severity.Should().Be(PdfValidationSeverity.Information, "nothing forbids a page left out (ADR 45)");
        finding.Location.Object.Should().Be(new PdfObjectId(7));
        finding.Remedy.Should().BeNull();
        finding.Message.Should().Be(
            "Object 7 is a page, of /Type /Page with a /Parent, that the page tree does not list: no reader shows it.");
    }

    [Fact]
    public void A_dictionary_typed_page_with_neither_parent_nor_contents_is_no_orphaned_page()
    {
        // DocuSign writes marked-content property lists typed /Page.
        Validate(WithObject(7, "<< /Type /Page >>")).Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_page_reached_only_through_a_destination_is_orphaned_when_it_has_contents()
    {
        var template = Tree
            .Replace("/Type /Catalog /Pages 2 0 R", "/Type /Catalog /Pages 2 0 R /OpenAction [7 0 R /Fit]", StringComparison.Ordinal)
            .Replace("xref\n0 7\n", "7 0 obj\n<< /Type /Page /Contents 8 0 R >>\nendobj\n8 0 obj\n<< /Length 0 >>\nstream\n\nendstream\nendobj\nxref\n0 9\n", StringComparison.Ordinal)
            .Replace("{row:6}\n", "{row:6}\n{row:7}\n{row:8}\n", StringComparison.Ordinal)
            .Replace("/Size 7", "/Size 9", StringComparison.Ordinal);

        Single(Validate(PdfTemplate.Build(template)), PdfValidationRuleIds.PageTreePageOrphaned).Message.Should().Be(
            "Object 7 is a page, of /Type /Page with a /Contents, that the page tree does not list: no reader shows it.");
    }

    [Fact]
    public void A_catalog_whose_pages_names_nothing_leaves_no_tree_and_its_pages_orphaned()
    {
        var report = Validate(TreeWith("/Type /Catalog /Pages 2 0 R", "/Type /Catalog /Pages 9 0 R"));

        Single(report, PdfValidationRuleIds.ObjectReferenceMissing).Message.Should().Be(
            "Object 1 refers to object 9 0 under /Pages, which the file lacks: the reference reads as null.");
        report.Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.PageTreePageOrphaned)
            .Select(finding => finding.Location.Object).Should().Equal(new PdfObjectId(3), new PdfObjectId(5), new PdfObjectId(6));
        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void A_pages_entry_set_to_null_in_memory_leaves_no_tree_and_its_pages_orphaned()
    {
        // No file can hold it — the parser drops an entry whose value is null —, but the catalog is a dictionary a caller can edit.
        using var document = PdfDocument.Open(PdfTemplate.Build(Tree));
        document.Catalog!.Set(PdfName.Pages, PdfNull.Instance);

        var walk = PageTreeWalk.Run(document);

        walk.Partial.Should().BeFalse("a null in place of the tree is no object the reader failed to produce");
        walk.PageCount.Should().Be(0);
        new PdfValidator().Validate(document).Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.PageTreePageOrphaned)
            .Select(finding => finding.Location.Object).Should().Equal(new PdfObjectId(3), new PdfObjectId(5), new PdfObjectId(6));
    }

    [Theory]
    [InlineData("<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Pages{0}/Kids [3 0 R] /Count 1 >>")]
    [InlineData("<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Pages /Kids [3 0 R]{0}/Count 1 >>")]
    [InlineData("<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Pages /Count 1 /Kids [3 0 R{0}] >>")]
    [InlineData("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>", "<< /Type /Page /Parent 2 0 R{0}/MediaBox [0 0 595 842] /Resources << >> >>")]
    [InlineData("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>", "<< /Type /Page /MediaBox [0 0 595 842] /Resources << >>{0}/Parent 2 0 R >>")]
    [InlineData("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>", "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0{0}595 842] >>")]
    public void What_a_node_or_a_page_a_limit_cut_lacks_is_not_judged(string text, string padded)
    {
        // Two hundred blanks put what follows them past the reader's limit: valid, and read whole without it.
        var file = PdfTemplate.SoundWith(text, string.Format(CultureInfo.InvariantCulture, padded, new string(' ', 200)));

        using var whole = PdfDocument.Open(file);
        new PdfValidator().Validate(whole).Findings.Should().BeEmpty("the file is sound");

        using var document = PdfDocument.Open(file, CutAt(128));

        new PdfValidator().Validate(document).Findings.Should().BeEmpty("what lies past the cut is the limit's, not the file's");
        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitObject);
        PageTreeWalk.Run(document).Partial.Should().BeTrue();
    }

    [Fact]
    public void What_a_node_a_limit_cut_holds_is_not_judged_either()
    {
        // The root's /Parent and its media box of three values lie before the cut, and might have been cut themselves.
        var file = PdfTemplate.SoundWith(
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Pages /Parent 3 0 R /MediaBox [0 0 5] /Kids [3 0 R] /Count 1" + new string(' ', 200) + ">>");

        using var document = PdfDocument.Open(file, CutAt(128));

        new PdfValidator().Validate(document).Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_kids_array_a_limit_cut_counts_nothing_the_cut_may_have_left_of_a_reference()
    {
        // The cut falls after "3 0" of the second "3 0 R", which the parser keeps as two numbers.
        var file = PdfTemplate.SoundWith(
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Pages /Count 2 /Kids [3 0 R" + new string(' ', 100) + "3 0" + new string(' ', 100) + "R] >>");

        using var document = PdfDocument.Open(file, CutAt(160));
        var walk = PageTreeWalk.Run(document);

        new PdfValidator().Validate(document).Findings.Should().BeEmpty("neither number is a kid, nor is the count judged");
        walk.PageCount.Should().Be(1);
        walk.Partial.Should().BeTrue();
    }

    [Fact]
    public void A_node_a_limit_cut_is_taken_to_give_what_its_pages_inherit()
    {
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace(
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1" + new string(' ', 200) + "/MediaBox [0 0 595 842] /Resources << >> >>",
                StringComparison.Ordinal)
            .Replace("/Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >>", "/Parent 2 0 R", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file, CutAt(128));

        new PdfValidator().Validate(document).Findings.Should().BeEmpty("the node may give the box and the resources past the cut");
    }

    [Fact]
    public void The_pages_a_node_a_limit_cut_lists_are_judged_as_any_other()
    {
        // Page 3, read whole, names itself as its /Parent and gives a box that encloses no area.
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Pages /Kids [3 0 R] /Count 1" + new string(' ', 200) + ">>", StringComparison.Ordinal)
            .Replace("/Parent 2 0 R /MediaBox [0 0 595 842]", "/Parent 3 0 R /MediaBox [0 0 0 842]", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file, CutAt(128));
        var report = new PdfValidator().Validate(document);

        report.Findings.Select(finding => (finding.RuleId, finding.Message)).Should().Equal(
            (PdfValidationRuleIds.PageTreeParentWrong, "Page object 3 gives /Parent 3 0 R, and page tree node 2 lists it."),
            (PdfValidationRuleIds.PageTreeMediaBoxInvalid, "The /MediaBox of object 3, [0 0 0 842], encloses no area."));
    }

    [Fact]
    public void A_kid_written_in_a_node_a_limit_cut_is_judged_on_nothing_and_still_counted()
    {
        var file = PdfTemplate.SoundWith(
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Pages /Count 1 /Kids [3 0 R << /Type /Page >>]" + new string(' ', 200) + ">>");

        using var document = PdfDocument.Open(file, CutAt(128));

        new PdfValidator().Validate(document).Findings.Should().BeEmpty();
        PageTreeWalk.Run(document).PageCount.Should().Be(2, "a dictionary is no fragment of a reference");
    }

    [Fact]
    public void An_array_of_kids_of_its_own_a_limit_cut_leaves_what_it_holds_and_the_counts_unjudged()
    {
        // Node 2, read whole, gives /Count 3 for an array whose third kid lies past the cut; the second is null.
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Pages /Kids 4 0 R /Count 3 >>", StringComparison.Ordinal)
            .Replace("xref\n0 4\n", "4 0 obj\n[3 0 R null" + new string(' ', 200) + "3 0 R]\nendobj\nxref\n0 5\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n{row:4}\n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 5", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file, CutAt(128));
        var walk = PageTreeWalk.Run(document);

        new PdfValidator().Validate(document).Findings.Should().BeEmpty();
        walk.Partial.Should().BeTrue();
        walk.PageCount.Should().Be(1);
    }

    [Fact]
    public void A_catalog_a_limit_cut_before_its_pages_leaves_no_page_orphaned()
    {
        var file = PdfTemplate.SoundWith("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Catalog" + new string(' ', 200) + "/Pages 2 0 R >>");

        using var document = PdfDocument.Open(file, CutAt(128));
        var walk = PageTreeWalk.Run(document);

        new PdfValidator().Validate(document).Findings.Should().BeEmpty("the tree may lie past the cut");
        walk.Partial.Should().BeTrue();
        walk.PageCount.Should().Be(0);
    }

    [Fact]
    public void A_page_a_limit_cut_keeps_pages_the_tree_leaves_out_from_being_reported()
    {
        // Page 3 may have been a node whose /Kids lies past the cut, and object 4 one of its pages.
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("<< /Type /Page /Parent 2 0 R ", "<< /Type /Page" + new string(' ', 200) + "/Parent 2 0 R ", StringComparison.Ordinal)
            .Replace("xref\n0 4\n", "4 0 obj\n<< /Type /Page /Parent 3 0 R >>\nendobj\nxref\n0 5\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n{row:4}\n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 5", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file, CutAt(128));

        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.PageTreePageOrphaned).Should().BeFalse();
        Validate(file).Contains(PdfValidationRuleIds.PageTreePageOrphaned).Should().BeTrue("read whole, page 3 lists no kid");
    }

    [Fact]
    public void An_array_of_kids_that_holds_a_node_naming_it_again_is_a_loop()
    {
        // Review of #129 (L1-01): object 7, node 2's array of kids, holds a node written in it whose /Kids is object 7
        // again. Neither has a number the walk could see on its path, and it entered them without end. Node 2's /Count of 2
        // is not judged against the one page counted: what the loop should have listed is unknown (M5-05).
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Pages /Kids 7 0 R /Count 2 >>", StringComparison.Ordinal)
            .Replace("xref\n0 4\n", "7 0 obj\n[3 0 R << /Type /Pages /Kids 7 0 R /Count 0 >>]\nendobj\nxref\n0 8\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n{free}\n{free}\n{free}\n{row:7}\n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 8", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file);
        var report = new PdfValidator().Validate(document);

        var cycle = Single(report, PdfValidationRuleIds.PageTreeCycle);
        cycle.Message.Should().Be(
            "The page tree node at [1] in object 7 gives /Kids 7 0 R, an array of kids above it: the tree loops back on itself there, and what it should have listed is unknown.");
        cycle.Location.Should().Be(PdfValidationLocation.OfObject(new PdfObjectId(7)));
        report.Contains(PdfValidationRuleIds.PageTreeCountMismatch).Should().BeFalse("the count above a loop is not judged");
        PageTreeWalk.Run(document).PageCount.Should().Be(1);
    }

    [Fact]
    public void Arrays_of_kids_shared_by_nodes_written_in_them_are_walked_once_each()
    {
        // Thirty arrays, each holding two nodes written in it that both name the next array: walked again for each node,
        // they made 2^30 visits of 4 KB; each array is walked once, and counted again where it is named again.
        const int Arrays = 30;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids 10 0 R /Count 1 /MediaBox [0 0 1 1] /Resources << >> >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R >>"),
        };

        for (var array = 10; array < 10 + Arrays; array++)
        {
            var next = array + 1 < 10 + Arrays ? array + 1 : 0;
            rows[array] = file.Object(array, next == 0
                ? "[3 0 R]"
                : string.Create(CultureInfo.InvariantCulture, $"[<< /Type /Pages /Count 1 /Kids {next} 0 R >> << /Type /Pages /Count 1 /Kids {next} 0 R >>]"));
        }

        file.Table(10 + Arrays, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {10 + Arrays} /Root 1 0 R"));
        using var document = PdfDocument.Open(file.Bytes);
        var stopwatch = Stopwatch.StartNew();

        var report = new PdfValidator().Validate(document);

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        report.Findings.Count(finding => finding.RuleId == PdfValidationRuleIds.PageTreeNodeRepeated).Should().Be(Arrays - 1);
        report.Contains(PdfValidationRuleIds.PageTreeCycle).Should().BeFalse("no array lies on its own path");
    }

    [Fact]
    public void A_kid_reached_through_a_chain_of_references_is_named_and_judged_as_the_object_the_chain_leads_to()
    {
        // Review of #129 and #175 (L1-02, L2-06): object 5's value is 8 0 R. Node 8 was named and located as page tree node
        // 5, and a limit that cut node 8 went unseen, its /Count judged and its pages called orphaned.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [5 0 R] /Count 2 /MediaBox [0 0 1 1] /Resources << >> >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 8 0 R >>"),
            [5] = file.Object(5, "8 0 R"),
            [6] = file.Object(6, "<< /Type /Page /Parent 8 0 R >>"),
            [8] = file.Object(8, "<< /Type /Pages /Parent 2 0 R /Kids [3 0 R" + new string(' ', 200) + "6 0 R] /Count 2 >>"),
        };
        file.Table(9, rows, "/Size 9 /Root 1 0 R");

        var whole = Validate(file.Bytes);
        using var document = PdfDocument.Open(file.Bytes, CutAt(128));
        var cut = new PdfValidator().Validate(document);

        whole.Findings.Should().BeEmpty("node 8 lists pages 3 and 6, whose /Parent names it");
        cut.Findings.Should().BeEmpty("node 8 is cut, and what lies past the cut is unknown");
        PageTreeWalk.Run(document).Partial.Should().BeTrue();
    }

    [Fact]
    public void An_object_a_limit_cut_read_as_a_page_leaves_the_count_of_the_node_listing_it_unjudged()
    {
        // Review of #175 (L2-01): node 3 writes its /Type and /Kids past the cut, and reads as a page; the root's /Count of 3
        // was judged against the 2 pages that made.
        var file = PdfTemplate.Build(Tree.Replace(
            "<< /Type /Pages /Parent 2 0 R /Kids [5 0 R 6 0 R] /Count 2 >>",
            "<< /Parent 2 0 R" + new string(' ', 200) + "/Type /Pages /Kids [5 0 R 6 0 R] /Count 2 >>",
            StringComparison.Ordinal));

        using var document = PdfDocument.Open(file, CutAt(128));

        new PdfValidator().Validate(document).Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_reference_to_nothing_written_in_a_cut_node_s_kids_is_the_object_graph_s()
    {
        // Review of #175 (L2-02): the walk judges nothing of a cut node's kids, and the object graph left them to it.
        var file = PdfTemplate.SoundWith(
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Pages /Kids [3 0 R 9 0 R] /Count 2" + new string(' ', 200) + ">>");

        using var document = PdfDocument.Open(file, CutAt(128));
        var report = new PdfValidator().Validate(document);

        Single(report, PdfValidationRuleIds.ObjectReferenceMissing).Message.Should().Be(
            "Object 2 refers to object 9 0 under /Kids[1], which the file lacks: the reference reads as null.");
        report.Contains(PdfValidationRuleIds.PageTreeKidInvalid).Should().BeFalse();
        Validate(file).Contains(PdfValidationRuleIds.ObjectReferenceMissing).Should().BeFalse("read whole, it is the walk's");
    }

    [Fact]
    public void A_node_listed_again_whose_pages_could_not_be_counted_leaves_the_count_above_it_unjudged()
    {
        // Review of #175 (L2-03): node 4, which a limit cuts inside its /Kids, is listed by the root and again by node 5,
        // whose /Count was judged against the one page read of node 4.
        var file = PdfTemplate.Build(Tree
            .Replace("/Kids [3 0 R 4 0 R] /Count 3", "/Kids [4 0 R 7 0 R] /Count 4", StringComparison.Ordinal)
            .Replace("/Kids [5 0 R 6 0 R] /Count 2", "/Kids [5 0 R" + new string(' ', 200) + "6 0 R] /Count 2", StringComparison.Ordinal)
            .Replace("xref\n0 7\n", "7 0 obj\n<< /Type /Pages /Parent 2 0 R /Kids [4 0 R] /Count 2 >>\nendobj\nxref\n0 8\n", StringComparison.Ordinal)
            .Replace("{row:6}\n", "{row:6}\n{row:7}\n", StringComparison.Ordinal)
            .Replace("/Size 7", "/Size 8", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file, CutAt(128));
        var report = new PdfValidator().Validate(document);

        Single(report, PdfValidationRuleIds.PageTreeNodeRepeated).Message.Should().Be(
            "The kid at index 0 of page tree node 7 names object 4, which the tree already lists: what lies below it is counted again, how many pages that is being unknown.");
        report.Contains(PdfValidationRuleIds.PageTreeCountMismatch).Should().BeFalse();
    }

    [Fact]
    public void Values_of_a_cut_kids_array_before_its_last_reference_are_counted_as_read_whole()
    {
        // Review of #175 (L2-04): the null at index 1 is followed by 5 0 R, and cannot be what the cut left of a reference;
        // dropping it moved page 5's findings from page 3 to page 2.
        var file = PdfTemplate.Build(Tree
            .Replace("/Kids [3 0 R 4 0 R] /Count 3", "/Kids 7 0 R /Count 3", StringComparison.Ordinal)
            .Replace("<< /Type /Page /Parent 4 0 R >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 0 0] >>", StringComparison.Ordinal)
            .Replace("xref\n0 7\n", "7 0 obj\n[3 0 R null 5 0 R" + new string(' ', 200) + "3 0 R]\nendobj\nxref\n0 8\n", StringComparison.Ordinal)
            .Replace("{row:6}\n", "{row:6}\n{row:7}\n", StringComparison.Ordinal)
            .Replace("/Size 7", "/Size 8", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file, CutAt(128));
        var report = new PdfValidator().Validate(document);

        Single(report, PdfValidationRuleIds.PageTreeMediaBoxInvalid).Location.Should().Be(PdfValidationLocation.OnPage(2, new PdfObjectId(5)));
        report.Contains(PdfValidationRuleIds.PageTreeKidInvalid).Should().BeFalse("what a cut array holds is not judged");
        PageTreeWalk.Run(document).PageCount.Should().Be(3);
    }

    [Fact]
    public void An_object_whose_type_a_limit_cut_short_is_no_orphaned_page()
    {
        // Review of #175 (L2-05): the cut falls inside /Pages, and the parser keeps /Page.
        var file = WithObject(7, "<< /Parent 2 0 R /Foo (" + new string('x', 150) + ") /Type /Pages" + new string(' ', 200) + "/Kids [] /Count 0 >>");
        var pages = Encoding.Latin1.GetString(file).IndexOf("/Pages" + new string(' ', 200), StringComparison.Ordinal);
        var header = Encoding.Latin1.GetString(file).IndexOf("7 0 obj", StringComparison.Ordinal);

        using var document = PdfDocument.Open(file, CutAt(pages - header + "/Page".Length));
        var report = new PdfValidator().Validate(document);

        document.GetObject(new PdfObjectId(7)).AsDictionary().Required().IsOfType(PdfName.Page).Should().BeTrue("the cut left /Page of /Pages");
        report.Contains(PdfValidationRuleIds.PageTreePageOrphaned).Should().BeFalse();
    }

    [Fact]
    public void A_page_or_node_written_in_a_cut_node_is_judged_on_nothing()
    {
        // Review of #175 (L2-07): a page with a box that is a number, and a node without /Count, written in a node a limit
        // cuts: neither is judged, though each would be read whole.
        var page = PdfTemplate.SoundWith(
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Pages /Count 2 /Kids [3 0 R << /Type /Page /MediaBox 5 >>]" + new string(' ', 200) + ">>");
        var node = PdfTemplate.SoundWith(
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Pages /Count 2 /Kids [3 0 R << /Type /Pages /Kids [] >>]" + new string(' ', 200) + ">>");

        using var pageDocument = PdfDocument.Open(page, CutAt(128));
        using var nodeDocument = PdfDocument.Open(node, CutAt(128));

        new PdfValidator().Validate(pageDocument).Findings.Should().BeEmpty();
        new PdfValidator().Validate(nodeDocument).Findings.Should().BeEmpty();
        Validate(page).Contains(PdfValidationRuleIds.PageTreeMediaBoxInvalid).Should().BeTrue("read whole, the box is judged");
        Validate(node).Contains(PdfValidationRuleIds.PageTreeCountMismatch).Should().BeTrue("read whole, the count is judged");
    }

    [Theory]
    [InlineData("[3 0 R 2 0 R 3 0 R{0}]")]
    [InlineData("[3 0 R 5 0 R 6 0 R{0}]")]
    public void What_a_kids_array_a_limit_cut_holds_is_not_judged(string array)
    {
        // Review of #175 (L5-04): a loop and a repeat, then a stream and a number named as kids, in an array of kids a limit
        // cuts: none is judged, though each is reported read whole.
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Pages /Kids 4 0 R /Count 3 >>", StringComparison.Ordinal)
            .Replace(
                "xref\n0 4\n",
                "4 0 obj\n" + string.Format(CultureInfo.InvariantCulture, array, new string(' ', 200)) + "\nendobj\n" +
                "5 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> /Length 0 >>\nstream\n\nendstream\nendobj\n6 0 obj\n12\nendobj\nxref\n0 7\n",
                StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n{row:4}\n{row:5}\n{row:6}\n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 7", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file, CutAt(128));
        var findings = new PdfValidator().Validate(document).Findings.Where(finding => finding.RuleId.StartsWith("page-tree.", StringComparison.Ordinal));

        findings.Select(finding => finding.Message).Should().BeSubsetOf(
            ["The kid at index 1 of page tree node 2 names object 5, a stream rather than a dictionary: the reader reads the stream's dictionary in its place."],
            "a kid's own finding is located at the kid, and only the slot's are the cut array's");
        Validate(file).Findings.Should().Contain(finding => finding.RuleId == PdfValidationRuleIds.PageTreeCycle || finding.RuleId == PdfValidationRuleIds.PageTreeKidInvalid);
    }

    [Fact]
    public void A_node_a_limit_cut_whose_kids_are_an_array_read_whole_leaves_its_count_unjudged()
    {
        // Review of #175 (L5-04): node 2's /Count lies past the cut; its kids, in object 4, are read whole.
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Pages /Kids 4 0 R" + new string(' ', 200) + "/Count 1 >>", StringComparison.Ordinal)
            .Replace("xref\n0 4\n", "4 0 obj\n[3 0 R]\nendobj\nxref\n0 5\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n{row:4}\n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 5", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file, CutAt(128));

        new PdfValidator().Validate(document).Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_catalog_a_limit_cut_whose_pages_names_nothing_leaves_the_walk_partial()
    {
        // Review of #175 (L5-04): the /Pages read before the cut names an object the file lacks; past the cut may lie more.
        var file = PdfTemplate.SoundWith("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Catalog /Pages 9 0 R" + new string(' ', 200) + "/Lang (en) >>");

        using var document = PdfDocument.Open(file, CutAt(128));
        var report = new PdfValidator().Validate(document);

        PageTreeWalk.Run(document).Partial.Should().BeTrue();
        report.Contains(PdfValidationRuleIds.PageTreePageOrphaned).Should().BeFalse();
    }

    [Fact]
    public void What_was_read_before_validating_changes_nothing()
    {
        var file = TreeWith("/Kids [3 0 R 4 0 R] /Count 3", "/Kids [3 0 R 9 0 R 4 0 R 3 0 R] /Count 3");
        using var document = PdfDocument.Open(file);
        var validator = new PdfValidator();

        var before = validator.Validate(document);

        for (var number = 1; number <= 9; number++)
        {
            document.GetObject(new PdfObjectId(number));
        }

        var after = validator.Validate(document);

        after.Findings.Should().Equal(before.Findings);
        before.Findings.Select(finding => finding.RuleId).Should().Equal(
            PdfValidationRuleIds.PageTreeNodeRepeated,
            PdfValidationRuleIds.PageTreeKidInvalid,
            PdfValidationRuleIds.PageTreeCountMismatch);
    }

    [Fact]
    public void A_tree_nested_deeper_than_any_stack_is_walked()
    {
        // 20,000 nodes, each the only kid of the one above: the walk keeps a frame per node of its path, on the
        // heap, and the stack never notices.
        const int depth = 20_000;
        var builder = new TestPdfBuilder().WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>");

        for (var node = 2; node < depth + 2; node++)
        {
            var parent = node == 2 ? string.Empty : $"/Parent {node - 1} 0 R ";
            builder.WithObject(node, $"<< /Type /Pages {parent}/Kids [{node + 1} 0 R] /Count 1 >>");
        }

        builder.WithObject(depth + 2, $"<< /Type /Page /Parent {depth + 1} 0 R /MediaBox [0 0 595 842] /Resources << >> >>");

        using var document = PdfDocument.Open(builder.BuildClassic(rootNumber: 1));
        var report = new PdfValidator().Validate(document);

        report.Findings.Should().BeEmpty();
        PageTreeWalk.Run(document).PageCount.Should().Be(1);
    }

    [Fact]
    public void Nodes_listed_twice_at_every_level_are_counted_without_being_walked_again()
    {
        // Each node lists the one below it twice: 2^40 pages as qpdf would count them, and forty nodes to walk.
        const int levels = 40;
        var builder = new TestPdfBuilder().WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>");

        for (var node = 2; node < levels + 2; node++)
        {
            builder.WithObject(node, $"<< /Type /Pages /Kids [{node + 1} 0 R {node + 1} 0 R] /Count 2 >>");
        }

        builder.WithObject(levels + 2, "<< /Type /Page /MediaBox [0 0 595 842] /Resources << >> >>");

        using var document = PdfDocument.Open(builder.BuildClassic(rootNumber: 1));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var report = new PdfValidator().Validate(document);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        report.Findings.Count(finding => finding.RuleId == PdfValidationRuleIds.PageTreeNodeRepeated).Should().Be(levels);
        PageTreeWalk.Run(document).PageCount.Should().Be(int.MaxValue, "the count saturates rather than overflowing");
    }

    [Theory]
    [InlineData("[7 0 R 3 0 R]", 0)]
    [InlineData("[3 0 R 7 0 R]", 1)]
    public void An_array_of_kids_a_kid_names_is_walked_under_the_node_whose_kids_it_is(string kids, int index)
    {
        // Review of #129's fixes (M1-01): arrays of kids and kids shared one set of objects walked. Named first by a kid,
        // array 7 was taken for walked, and node 3's pages were never entered — orphaned, and counted for nothing; walked
        // first, the kid naming it was a node listed again, its pages counted twice.
        var file = Objects(
            (2, "<< /Type /Pages /Kids " + kids + " /Count 3 /MediaBox [0 0 1 1] /Resources << >> >>"),
            (3, "<< /Type /Pages /Parent 2 0 R /Kids 7 0 R /Count 2 >>"),
            (5, "<< /Type /Page /Parent 3 0 R >>"),
            (6, "<< /Type /Page /Parent 3 0 R >>"),
            (7, "[5 0 R 6 0 R]"));
        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        report.Findings.Select(finding => finding.RuleId).Should().Equal(PdfValidationRuleIds.PageTreeKidInvalid);
        report.Findings[0].Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The kid at index {index} of page tree node 2 names object 7, which is an array, neither a page nor a node: it counts as a page with nothing on it."));
        PageTreeWalk.Run(document).PageCount.Should().Be(3);
    }

    [Fact]
    public void A_kid_naming_the_array_of_kids_it_is_written_in_is_no_loop()
    {
        // An array of kids is no node: the kid at [1] in object 7 names an array, and was called a node above it.
        var file = Objects(
            (2, "<< /Type /Pages /Kids 7 0 R /Count 2 /MediaBox [0 0 1 1] /Resources << >> >>"),
            (3, "<< /Type /Page /Parent 2 0 R >>"),
            (7, "[3 0 R 7 0 R]"));
        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        report.Findings.Select(finding => finding.RuleId).Should().Equal(PdfValidationRuleIds.PageTreeKidInvalid);
        report.Findings[0].Message.Should().Contain("names object 7, which is an array, neither a page nor a node");
        PageTreeWalk.Run(document).PageCount.Should().Be(2);
    }

    [Fact]
    public void A_node_whose_array_of_kids_was_walked_already_is_counted_for_its_pages_where_listed_again()
    {
        // M1-02: node 4 found array 7 walked, and was left without being entered: listed again, it counted for one page,
        // and node 2's /Count of 6 was said to be wrong.
        var file = Objects(
            (2, "<< /Type /Pages /Kids [3 0 R 4 0 R 4 0 R] /Count 6 /MediaBox [0 0 1 1] /Resources << >> >>"),
            (3, "<< /Type /Pages /Parent 2 0 R /Kids 7 0 R /Count 2 >>"),
            (4, "<< /Type /Pages /Parent 2 0 R /Kids 7 0 R /Count 2 >>"),
            (5, "<< /Type /Page /Parent 3 0 R >>"),
            (6, "<< /Type /Page /Parent 3 0 R >>"),
            (7, "[5 0 R 6 0 R]"));
        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        report.Findings.Select(finding => (finding.RuleId, finding.Message)).Should().BeEquivalentTo(
        [
            (PdfValidationRuleIds.PageTreeNodeRepeated, "Page tree node 4 gives /Kids 7 0 R, an array of kids the tree already lists: its 2 pages are counted again."),
            (PdfValidationRuleIds.PageTreeNodeRepeated, "The kid at index 2 of page tree node 2 names object 4, which the tree already lists: its 2 pages are counted again."),
        ]);
        PageTreeWalk.Run(document).PageCount.Should().Be(6);
    }

    [Fact]
    public void A_node_whose_array_of_kids_loops_is_counted_again_as_unknown_where_listed_again()
    {
        // Node 3's array of kids, 7, is node 4's, above it: where node 5 lists node 3 again, what lies below it is unknown,
        // and node 5's /Count is not judged against one page.
        var file = Objects(
            (2, "<< /Type /Pages /Kids [4 0 R 5 0 R] /Count 1 /MediaBox [0 0 1 1] /Resources << >> >>"),
            (3, "<< /Type /Pages /Parent 4 0 R /Kids 7 0 R /Count 0 >>"),
            (4, "<< /Type /Pages /Parent 2 0 R /Kids 7 0 R /Count 0 >>"),
            (5, "<< /Type /Pages /Parent 2 0 R /Kids [3 0 R] /Count 0 >>"),
            (7, "[3 0 R]"));
        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        report.Contains(PdfValidationRuleIds.PageTreeCountMismatch).Should().BeFalse(string.Join("; ", report.Findings));
        Single(report, PdfValidationRuleIds.PageTreeCycle).Message.Should().Be(
            "Page tree node 3 gives /Kids 7 0 R, an array of kids above it: the tree loops back on itself there, and what it should have listed is unknown.");
        Single(report, PdfValidationRuleIds.PageTreeNodeRepeated).Message.Should().Be(
            "The kid at index 0 of page tree node 5 names object 3, which the tree already lists: what lies below it is counted again, how many pages that is being unknown.");
    }

    [Theory]
    [InlineData("walked already", "Page tree node 6 has no /Count; 1 page lies below it.")]
    [InlineData("above it", "Page tree node 6 has no /Count, and how many pages lie below it is unknown.")]
    public void A_node_whose_array_of_kids_is_not_walked_again_still_has_its_missing_count_reported(string array, string message)
    {
        // M1-03: a node returned before it was entered, and the /Count it lacks went unreported by any rule, the Arlington
        // walk leaving a node's keys to this one.
        var file = array == "walked already"
            ? Objects(
                (2, "<< /Type /Pages /Kids [5 0 R 6 0 R] /Count 2 /MediaBox [0 0 1 1] /Resources << >> >>"),
                (3, "<< /Type /Page /Parent 5 0 R >>"),
                (5, "<< /Type /Pages /Parent 2 0 R /Kids 7 0 R /Count 1 >>"),
                (6, "<< /Type /Pages /Parent 2 0 R /Kids 7 0 R >>"),
                (7, "[3 0 R]"))
            : Objects(
                (2, "<< /Type /Pages /Kids 7 0 R /Count 2 /MediaBox [0 0 1 1] /Resources << >> >>"),
                (3, "<< /Type /Page /Parent 2 0 R >>"),
                (6, "<< /Type /Pages /Parent 2 0 R /Kids 7 0 R >>"),
                (7, "[3 0 R 6 0 R]"));

        var report = Validate(file);

        report.Findings.Should().Contain(finding => finding.RuleId == PdfValidationRuleIds.PageTreeCountMismatch && finding.Message == message);
    }

    [Fact]
    public void A_page_a_limit_cut_listed_again_is_counted_again_as_unknown()
    {
        // M1-04: object 3, a cut "page" that is a node past the cut, made node 4's count unknown, and node 5's, listing it
        // again, was judged against one page.
        var file = Objects(
            (2, "<< /Type /Pages /Kids [4 0 R 5 0 R] /Count 4 /MediaBox [0 0 1 1] /Resources << >> >>"),
            (3, "<< /Parent 4 0 R" + new string(' ', 200) + "/Type /Pages /Kids [6 0 R 7 0 R] /Count 2 >>"),
            (4, "<< /Type /Pages /Parent 2 0 R /Kids [3 0 R] /Count 2 >>"),
            (5, "<< /Type /Pages /Parent 2 0 R /Kids [3 0 R] /Count 2 >>"),
            (6, "<< /Type /Page /Parent 3 0 R >>"),
            (7, "<< /Type /Page /Parent 3 0 R >>"));
        using var document = PdfDocument.Open(file, CutAt(128));

        var report = new PdfValidator().Validate(document);

        report.Contains(PdfValidationRuleIds.PageTreeCountMismatch).Should().BeFalse(string.Join("; ", report.Findings));
        Single(report, PdfValidationRuleIds.PageTreeNodeRepeated).Message.Should().Be(
            "The kid at index 0 of page tree node 5 names object 3, which the tree already lists: what lies below it is counted again, how many pages that is being unknown.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    public void An_object_whose_value_is_a_reference_to_a_listed_page_is_no_orphaned_page(int links)
    {
        // M1-05: the tree lists page 3 through a chain of references; each link resolves to the page's dictionary, and was
        // called a page the tree does not list.
        var objects = new List<(int Number, string Body)>
        {
            (2, "<< /Type /Pages /Kids [10 0 R] /Count 1 /MediaBox [0 0 1 1] /Resources << >> >>"),
            (3, "<< /Type /Page /Parent 2 0 R >>"),
        };

        for (var link = 0; link < links; link++)
        {
            objects.Add((10 + link, link + 1 < links ? string.Create(CultureInfo.InvariantCulture, $"{11 + link} 0 R") : "3 0 R"));
        }

        var report = Validate(Objects([.. objects]));

        report.Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_chain_of_references_too_long_to_resolve_leads_to_no_page()
    {
        // M1-06: thirty-one references lead to page 3, one more than a reference resolves through: the kid reads as null,
        // and the walk took page 3 for listed, then called it listed again at index 1 and orphaned at once.
        var objects = new List<(int Number, string Body)>
        {
            (2, "<< /Type /Pages /Kids [10 0 R 3 0 R] /Count 2 /MediaBox [0 0 1 1] /Resources << >> >>"),
            (3, "<< /Type /Page /Parent 2 0 R >>"),
        };

        for (var link = 10; link <= 40; link++)
        {
            objects.Add((link, string.Create(CultureInfo.InvariantCulture, $"{(link < 40 ? link + 1 : 3)} 0 R")));
        }

        using var document = PdfDocument.Open(Objects([.. objects]));

        var report = new PdfValidator().Validate(document);

        report.Findings.Select(finding => (finding.RuleId, finding.Message)).Should().Equal((
            PdfValidationRuleIds.PageTreeKidInvalid,
            "The kid at index 0 of page tree node 2 names object 10, which is null: it counts as a page with nothing on it."));
        PageTreeWalk.Run(document).PageIndexes.Should().ContainKey(3).WhoseValue.Should().Be(1);
    }

    [Theory]
    [InlineData("[3 0 R 9 0 R]")]
    [InlineData("[3 0 R << /Type /Pages /Kids [12 0 R] /Count 0 >>]")]
    public void A_kid_to_nothing_in_an_array_of_kids_of_its_own_is_reported_once(string array)
    {
        // M1-07: a kid naming an object the file lacks, in node 2's array of kids 8, was page-tree.kid-invalid's and the object
        // graph's object.reference-missing's at once.
        var report = Validate(Objects(
            (2, "<< /Type /Pages /Kids 8 0 R /Count 2 /MediaBox [0 0 1 1] /Resources << >> >>"),
            (3, "<< /Type /Page /Parent 2 0 R >>"),
            (8, array)));

        report.Contains(PdfValidationRuleIds.ObjectReferenceMissing).Should().BeFalse(string.Join("; ", report.Findings));
        report.Findings.Should().ContainSingle(finding => finding.RuleId == PdfValidationRuleIds.PageTreeKidInvalid && finding.Message.Contains("which the file lacks", StringComparison.Ordinal));
    }

    [Fact]
    public void A_root_reached_through_a_chain_of_references_is_named_as_the_node_it_leads_to()
    {
        // M5-04: the catalog's /Pages names object 9, whose value is 2 0 R. The root is node 2, and the findings on it name
        // it so.
        var report = Validate(Objects(
            (1, "<< /Type /Catalog /Pages 9 0 R >>"),
            (2, "<< /Type /Pages /Kids [3 0 R] /Count 5 /MediaBox [0 0 1 1] /Resources << >> >>"),
            (3, "<< /Type /Page /Parent 2 0 R >>"),
            (9, "2 0 R")));

        Single(report, PdfValidationRuleIds.PageTreeCountMismatch).Message.Should().Be("Page tree node 2 gives /Count 5, and 1 page lies below it.");
    }

    [Fact]
    public void An_array_of_kids_reached_through_a_chain_of_references_is_the_array_it_leads_to()
    {
        // M5-04: node 4's /Kids names object 8, whose value is 7 0 R; node 5's names object 7. Both are array 7, walked
        // once, and counted again under node 5.
        var report = Validate(Objects(
            (2, "<< /Type /Pages /Kids [4 0 R 5 0 R] /Count 2 /MediaBox [0 0 1 1] /Resources << >> >>"),
            (3, "<< /Type /Page /Parent 4 0 R >>"),
            (4, "<< /Type /Pages /Parent 2 0 R /Kids 8 0 R /Count 1 >>"),
            (5, "<< /Type /Pages /Parent 2 0 R /Kids 7 0 R /Count 1 >>"),
            (7, "[3 0 R]"),
            (8, "7 0 R")));

        report.Findings.Select(finding => (finding.RuleId, finding.Message)).Should().Equal((
            PdfValidationRuleIds.PageTreeNodeRepeated,
            "Page tree node 5 gives /Kids 7 0 R, an array of kids the tree already lists: its page is counted again."));
    }

    [Theory]
    [InlineData("a page listed again")]
    [InlineData("an array of kids listed again")]
    public void What_a_node_a_limit_cut_lists_again_is_counted_again(string again)
    {
        // M5-06: node 4, cut by the limit after its /Kids, lists again what node 3 lists. It is not judged, but counted
        // again all the same: page 8, listed last, is the fifth page, or the third, as qpdf counts the tree.
        var pad = " /Pad (" + new string('x', 300) + ")";
        var file = again == "a page listed again"
            ? Objects(
                (2, "<< /Type /Pages /Kids [3 0 R 4 0 R 8 0 R] /Count 4 /MediaBox [0 0 1 1] /Resources << >> >>"),
                (3, "<< /Type /Pages /Parent 2 0 R /Kids [5 0 R] /Count 1 >>"),
                (4, "<< /Type /Pages /Parent 2 0 R /Kids [5 0 R] /Count 1" + pad + " >>"),
                (5, "<< /Type /Page /Parent 3 0 R >>"),
                (8, "<< /Type /Page /Parent 2 0 R >>"))
            : Objects(
                (2, "<< /Type /Pages /Kids [3 0 R 4 0 R 8 0 R] /Count 5 /MediaBox [0 0 1 1] /Resources << >> >>"),
                (3, "<< /Type /Pages /Parent 2 0 R /Kids 7 0 R /Count 2 >>"),
                (4, "<< /Type /Pages /Parent 2 0 R /Kids 7 0 R /Count 2" + pad + " >>"),
                (5, "<< /Type /Page /Parent 3 0 R >>"),
                (6, "<< /Type /Page /Parent 3 0 R >>"),
                (7, "[5 0 R 6 0 R]"),
                (8, "<< /Type /Page /Parent 2 0 R >>"));
        using var document = PdfDocument.Open(file, CutAt(128));

        var walk = PageTreeWalk.Run(document);

        document.Reader.IsCutAtLimit(4).Should().BeTrue();
        walk.PageIndexes[8].Should().Be(again == "a page listed again" ? 2 : 4);
        walk.PageCount.Should().Be(again == "a page listed again" ? 3 : 5);
    }

    /// <summary>A document whose catalog, object 1, names node 2 as the root of its page tree, with <paramref name="objects"/>.</summary>
    private static byte[] Objects(params (int Number, string Body)[] objects)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long> { [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>") };
        var size = 2;

        foreach (var (number, body) in objects)
        {
            rows[number] = file.Object(number, body);
            size = Math.Max(size, number + 1);
        }

        file.Table(size, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {size} /Root 1 0 R"));
        return file.Bytes;
    }

    private static byte[] TreeWith(string text, string replacement)
    {
        Tree.Should().Contain(text);
        return PdfTemplate.Build(Tree.Replace(text, replacement, StringComparison.Ordinal));
    }

    /// <summary>The tree with <paramref name="text"/> replaced, and an object 7 whose body is <paramref name="seventh"/>.</summary>
    private static byte[] TreeWith(string text, string replacement, string seventh)
    {
        Tree.Should().Contain(text);
        return PdfTemplate.Build(Tree
            .Replace(text, replacement, StringComparison.Ordinal)
            .Replace("xref\n0 7\n", $"7 0 obj\n{seventh}\nendobj\nxref\n0 8\n", StringComparison.Ordinal)
            .Replace("{row:6}\n", "{row:6}\n{row:7}\n", StringComparison.Ordinal)
            .Replace("/Size 7", "/Size 8", StringComparison.Ordinal));
    }

    /// <summary>The tree with object <paramref name="number"/> added, which nothing refers to.</summary>
    private static byte[] WithObject(int number, string body) =>
        PdfTemplate.Build(Tree
            .Replace("xref\n0 7\n", $"{number} 0 obj\n{body}\nendobj\nxref\n0 {number + 1}\n", StringComparison.Ordinal)
            .Replace("{row:6}\n", $"{{row:6}}\n{{row:{number}}}\n", StringComparison.Ordinal)
            .Replace("/Size 7", $"/Size {number + 1}", StringComparison.Ordinal));

    private static PdfValidationFinding Single(PdfValidationReport report, string ruleId)
    {
        report.Findings.Where(finding => finding.RuleId == ruleId).Should().ContainSingle(
            $"one finding of {ruleId}, among: {string.Join("; ", report.Findings)}");
        return report.Findings.First(finding => finding.RuleId == ruleId);
    }

    /// <summary>Opens a document with <see cref="PdfReaderLimits.MaxObjectLength"/> lowered to <paramref name="maxObjectLength"/>.</summary>
    private static PdfReaderOptions CutAt(int maxObjectLength) =>
        new() { Limits = PdfReaderLimits.Default with { MaxObjectLength = maxObjectLength } };

    private static PdfValidationReport Validate(byte[] file)
    {
        using var document = PdfDocument.Open(file);
        return new PdfValidator().Validate(document);
    }
}
