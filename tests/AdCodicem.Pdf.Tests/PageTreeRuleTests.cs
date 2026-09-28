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
    public void A_page_written_in_the_array_is_read_as_it_is()
    {
        var file = TreeWith("/Kids [5 0 R 6 0 R] /Count 2", "/Kids [5 0 R << /Type /Page >>] /Count 2");

        var report = Validate(file);

        Single(report, PdfValidationRuleIds.PageTreeKidInvalid).Message.Should().Be(
            "The kid at index 1 of page tree node 4 is a dictionary written in the array, where the tree wants an indirect reference to one.");
        report.Contains(PdfValidationRuleIds.PageTreeParentWrong).Should().BeFalse("a kid with no number cannot be named by /Parent");
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
    [InlineData("[0 0 612]", "The /MediaBox of object 6 is an array of 3 values, not a rectangle.")]
    [InlineData("/Letter", "The /MediaBox of object 6 is a name, not a rectangle.")]
    [InlineData("[0 0 (612) 792]", "The /MediaBox of object 6 holds a string among its corners, not a rectangle of four numbers.")]
    [InlineData("[0 0 612 0]", "The /MediaBox of object 6, [0 0 612 0], encloses no area.")]
    [InlineData("[10.5 20 10.5 792]", "The /MediaBox of object 6, [10.5 20 10.5 792], encloses no area.")]
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

    private static byte[] TreeWith(string text, string replacement)
    {
        Tree.Should().Contain(text);
        return PdfTemplate.Build(Tree.Replace(text, replacement, StringComparison.Ordinal));
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

    private static PdfValidationReport Validate(byte[] file)
    {
        using var document = PdfDocument.Open(file);
        return new PdfValidator().Validate(document);
    }
}
