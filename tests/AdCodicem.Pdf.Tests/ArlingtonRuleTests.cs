extern alias ArlingtonTool;

using System.Diagnostics;
using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;
using Tool = ArlingtonTool::AdCodicem.Pdf.Arlington;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The rules generated from the Arlington PDF Model — keys missing, values of the wrong type, <c>/Type</c> and
/// <c>/Subtype</c> values the model does not list, deprecated keys —, and the walk that types every object reachable
/// from the trailer: each rule on a file that breaks it, a sound file and a file that is unusual and legal, the
/// overrides <c>tools/AdCodicem.Pdf.Arlington/overrides.tsv</c> records, and what the walk must survive.
/// </summary>
public class ArlingtonRuleTests
{
    private const string Catalog = "<< /Type /Catalog /Pages 2 0 R >>";
    private const string Root = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";
    private const string Page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>";

    private static readonly string[] GeneratedRules =
    [
        PdfValidationRuleIds.ObjectKeyMissing,
        PdfValidationRuleIds.ObjectValueTypeWrong,
        PdfValidationRuleIds.ObjectTypeValueWrong,
        PdfValidationRuleIds.ObjectKeyDeprecated,
    ];

    [Fact]
    public void A_sound_document_earns_no_finding_of_the_generated_rules()
    {
        Validate(Pdf(Catalog, Root, Page)).Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_catalog_without_type_lacks_a_key_the_model_requires()
    {
        var report = Validate(Pdf("<< /Pages 2 0 R >>", Root, Page));

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.ObjectKeyMissing);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning, "readers take a dictionary with /Pages for the catalog (ADR 45)");
        finding.Location.Object.Should().Be(new PdfObjectId(1));
        finding.Message.Should().Be("Object 1 0, a Catalog in the Arlington model, lacks /Type, which the model requires.");
        finding.Remedy.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_catalog_given_a_null_type_lacks_it()
    {
        // null in a dictionary is absence (ISO 32000-1, 7.3.7), written there or as an object.
        Single(Validate(Pdf("<< /Type null /Pages 2 0 R >>", Root, Page)), PdfValidationRuleIds.ObjectKeyMissing);
        Single(Validate(Pdf("<< /Type 4 0 R /Pages 2 0 R >>", Root, Page, "null")), PdfValidationRuleIds.ObjectKeyMissing);
    }

    [Fact]
    public void A_catalog_without_pages_lacks_them_for_no_other_rule_reports_it()
    {
        var report = Validate(Pdf("<< /Type /Catalog >>", Root, Page));

        Single(report, PdfValidationRuleIds.ObjectKeyMissing).Message.Should().Be(
            "Object 1 0, a Catalog in the Arlington model, lacks /Pages, which the model requires.");
        report.Findings.Select(finding => finding.RuleId).Should().BeEquivalentTo(
            [PdfValidationRuleIds.ObjectKeyMissing, PdfValidationRuleIds.PageTreePageOrphaned], "the page nothing lists is another fault");
    }

    [Fact]
    public void A_root_typed_pagez_has_a_type_the_model_does_not_list()
    {
        var report = Validate(Pdf(Catalog, "<< /Type /Pagez /Kids [3 0 R] /Count 1 >>", Page));

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.ObjectTypeValueWrong);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning);
        finding.Location.Object.Should().Be(new PdfObjectId(2));
        finding.Message.Should().Be("Object 2 0, a PageTreeNodeRoot in the Arlington model, has /Type /Pagez, where the model wants /Pages.");
    }

    [Fact]
    public void A_page_typed_font_is_still_a_page_whose_type_the_model_does_not_list()
    {
        var report = Validate(Pdf(Catalog, Root, Page.Replace("/Type /Page ", "/Type /Font ", StringComparison.Ordinal)));

        var finding = Single(report, PdfValidationRuleIds.ObjectTypeValueWrong);
        finding.Location.PageIndex.Should().Be(0, "the object is the first page");
        finding.Message.Should().Be("Object 3 0, a PageObject in the Arlington model, has /Type /Font, where the model wants /Page or /Template.");
    }

    [Fact]
    public void A_type_left_out_where_the_model_makes_it_optional_is_sound()
    {
        // A form XObject's /Type is optional (Table 95), and a template page is a page.
        var report = Validate(Pdf(
            Catalog,
            "<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>",
            Page.Replace("/Resources << >>", "/Resources << /XObject << /Fm0 4 0 R >> >>", StringComparison.Ordinal),
            "<< /Subtype /Form /BBox [0 0 1 1] /Resources << >> /Length 0 >>\nstream\n\nendstream",
            "<< /Type /Template /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>"));

        report.Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_page_given_a_stream_body_is_the_page_tree_rule_s_alone()
    {
        // The kid's fault is page-tree.kid-invalid's: ArrayOfPageTreeNodeKids/* is covered by it (overrides.tsv).
        var report = Validate(Pdf(Catalog, Root, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> /Length 0 >>\nstream\n\nendstream"));

        report.Findings.Select(finding => finding.RuleId).Should().Equal(PdfValidationRuleIds.PageTreeKidInvalid);
    }

    [Fact]
    public void A_stream_where_a_dictionary_belongs_is_reported_on_its_holder_and_not_walked()
    {
        var report = Validate(Pdf(
            "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R >>",
            Root,
            Page,
            "<< /Type /Wrong /Count 0 /Length 0 >>\nstream\n\nendstream"));

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.ObjectValueTypeWrong);
        finding.Location.Object.Should().Be(new PdfObjectId(1));
        finding.Message.Should().Be("Object 1 0, a Catalog in the Arlington model, has /Outlines as a stream, where the model wants a dictionary.");
    }

    [Fact]
    public void A_value_is_judged_by_its_class_never_converted()
    {
        var report = Validate(Pdf(Catalog, Root, Page.Replace(">> >>", ">> /Rotate 90.0 >>", StringComparison.Ordinal)));

        Single(report, PdfValidationRuleIds.ObjectValueTypeWrong).Message.Should().Be(
            "Object 3 0, a PageObject in the Arlington model, has /Rotate as a real number, where the model wants an integer.");
    }

    [Theory]
    [InlineData("/AcroForm << /Fields [] /NeedAppearances (yes) >>", "The dictionary under /AcroForm of object 1 0, an InteractiveForm in the Arlington model, has /NeedAppearances as a string, where the model wants a boolean.")]
    [InlineData("/PageLabels (i)", "Object 1 0, a Catalog in the Arlington model, has /PageLabels as a string, where the model wants a number tree.")]
    [InlineData("/Names << /Dests (none) >>", "The dictionary under /Names of object 1 0, a Name in the Arlington model, has /Dests as a string, where the model wants a name tree.")]
    [InlineData("/OpenAction << /S /URI /URI (https://example.org) /Next /None >>", "The dictionary under /OpenAction of object 1 0, an ActionURI in the Arlington model, has /Next as a name, where the model wants an array or a dictionary.")]
    public void A_message_says_what_the_model_wants_in_words(string entry, string message)
    {
        var report = Validate(Pdf($"<< /Type /Catalog /Pages 2 0 R {entry} >>", Root, Page));

        Single(report, PdfValidationRuleIds.ObjectValueTypeWrong).Message.Should().Be(message);
    }

    [Theory]
    [InlineData("/Annots [<< /Type /Annot /Subtype /Link /Rect (all) >>]", "The dictionary under /Annots[0] of object 3 0, an AnnotLink in the Arlington model, has /Rect as a string, where the model wants a rectangle.")]
    [InlineData("/Resources << /XObject << /Fm0 4 0 R >> >>", "Object 4 0, an XObjectFormType1 in the Arlington model, has /Matrix as a string, where the model wants a matrix.")]
    public void A_message_names_rectangles_and_matrices(string entry, string message)
    {
        var report = Validate(Pdf(
            Catalog,
            Root,
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] {(entry.StartsWith("/Resources", StringComparison.Ordinal) ? entry : "/Resources << >> " + entry)} >>",
            "<< /Type /XObject /Subtype /Form /BBox [0 0 1 1] /Matrix (identity) /Resources << >> /Length 0 >>\nstream\n\nendstream"));

        Single(report, PdfValidationRuleIds.ObjectValueTypeWrong).Message.Should().Be(message);
    }

    [Fact]
    public void A_message_names_a_date()
    {
        var report = Validate(Declaring("1.7", "/Info 4 0 R", Catalog, Root, Page, "<< /CreationDate 20240101 >>"));

        Single(report, PdfValidationRuleIds.ObjectValueTypeWrong).Message.Should().Be(
            "Object 4 0, a DocInfo in the Arlington model, has /CreationDate as an integer, where the model wants a date.");
    }

    [Fact]
    public void A_type_among_many_is_counted_rather_than_listed()
    {
        var report = Validate(StructureWith("/A << /O /Layout /Type /Bogus >>"));

        Single(report, PdfValidationRuleIds.ObjectTypeValueWrong).Message.Should().Be(
            "The dictionary under /A of object 5 0, a StructureAttributesDict in the Arlington model, has /Type /Bogus, where the model wants one of the 5 names it lists.");
    }

    [Fact]
    public void An_integer_where_a_number_belongs_is_sound()
    {
        var report = Validate(Pdf(Catalog, Root, Page.Replace(">> >>", ">> /UserUnit 2 >>", StringComparison.Ordinal)));

        report.Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_value_written_inside_another_object_is_located_at_that_object_with_its_path()
    {
        var report = Validate(Pdf(Catalog, Root, Page.Replace("/Resources << >>", "/Resources << /Font (none) >>", StringComparison.Ordinal)));

        var finding = Single(report, PdfValidationRuleIds.ObjectValueTypeWrong);
        finding.Location.Object.Should().Be(new PdfObjectId(3));
        finding.Location.PageIndex.Should().Be(0);
        finding.Message.Should().Be(
            "The dictionary under /Resources of object 3 0, a Resource in the Arlington model, has /Font as a string, where the model wants a dictionary.");
    }

    [Fact]
    public void Several_objects_with_one_fault_are_one_finding_at_the_first_with_their_count()
    {
        var report = Validate(Pdf(
            Catalog,
            "<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R] /Count 3 /MediaBox [0 0 595 842] /Resources << >> >>",
            "<< /Type /Page /Parent 2 0 R /Rotate (90) >>",
            "<< /Type /Page /Parent 2 0 R /Rotate (180) >>",
            "<< /Type /Page /Parent 2 0 R /Rotate /Left >>"));

        var finding = Single(report, PdfValidationRuleIds.ObjectValueTypeWrong);
        finding.Location.Object.Should().Be(new PdfObjectId(3));
        finding.Message.Should().Be(
            "A PageObject has /Rotate as a string, where the Arlington model wants an integer; 3 objects give /Rotate a type it does not allow, the first object 3 0.");
    }

    [Fact]
    public void An_object_that_breaks_a_row_several_times_is_counted_once()
    {
        var report = Validate(Pdf(
            Catalog,
            Root,
            Page.Replace("/Resources << >>", "/Resources << /ProcSet [1 2 3] /ColorSpace << /CS0 (a) /CS1 7 >> >>", StringComparison.Ordinal)));

        // One array with three integers, one dictionary with two values of the wrong type: one object each.
        report.Findings.Select(finding => finding.Message).Should().Equal(
            "The dictionary under /Resources/ColorSpace of object 3 0, a ColorSpaceMap in the Arlington model, has /CS0 as a string, where the model wants an array or a name.",
            "The array under /Resources/ProcSet of object 3 0, an ArrayOfNamesForProcSet in the Arlington model, has element 0 as an integer, where the model wants a name.");
    }

    [Fact]
    public void The_trailer_s_own_entries_are_the_file_family_s_and_what_it_leads_to_is_checked()
    {
        var report = Validate(Declaring("1.7", "/ID [<00> 7] /Info 4 0 R", Catalog, Root, Page, "<< /Author 7 >>"));

        Single(report, PdfValidationRuleIds.ObjectValueTypeWrong).Message.Should().Be(
            "Object 4 0, a DocInfo in the Arlington model, has /Author as an integer, where the model wants a string.");
    }

    [Fact]
    public void A_dictionary_written_in_the_trailer_is_located_at_the_trailer()
    {
        var report = Validate(Declaring("1.7", "/Info << /Author 7 >>", Catalog, Root, Page));

        var finding = Single(report, PdfValidationRuleIds.ObjectValueTypeWrong);
        finding.Message.Should().Be(
            "The dictionary under /Info of the trailer, a DocInfo in the Arlington model, has /Author as an integer, where the model wants a string.");
        finding.Location.Object.Should().BeNull();
        finding.Location.Position.Should().BePositive();
    }

    [Fact]
    public void A_key_the_model_does_not_know_is_not_judged()
    {
        Validate(Pdf(Catalog, Root, Page.Replace(">> >>", ">> /ACME_Private (x) >>", StringComparison.Ordinal))).Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_tie_between_candidates_leaves_the_value_unchecked()
    {
        // A link's [page /XYZ left top zoom] is DestXYZArray or DestXYZStructArray alike: element 0 is a dictionary for
        // both, and only a look at what it names would tell them apart.
        var file = Pdf(Catalog, Root, Page.Replace(">> >>", ">> /Annots [<< /Type /Annot /Subtype /Link /Rect [0 0 1 1] /Dest [3 0 R /XYZ (left) 0 0] >>] >>", StringComparison.Ordinal));

        Validate(file).Findings.Should().BeEmpty("a value the walk cannot type is not judged");
        Walk(file).Ties.Should().Be(1);
    }

    [Fact]
    public void An_object_many_contexts_tie_on_is_weighed_once()
    {
        var links = string.Concat(Enumerable.Repeat("<< /Type /Annot /Subtype /Link /Rect [0 0 1 1] /Dest 4 0 R >> ", 500));
        var file = Pdf(Catalog, Root, Page.Replace(">> >>", $">> /Annots [{links}] >>", StringComparison.Ordinal), "[3 0 R /XYZ (left) 0 0]");

        Validate(file).Findings.Should().BeEmpty();
        Walk(file).Ties.Should().Be(1, "the object ties on the same candidates in every context");
    }

    [Fact]
    public void A_value_a_tie_leaves_untyped_is_typed_by_a_later_context()
    {
        // Object 10 is first met as a link's destination, where the two XYZ candidates tie, then at the end of an outline,
        // where only DestXYZArray is linked: the later context types it, and its fault is judged.
        var report = Validate(Pdf(
            "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R >>",
            Root,
            Page.Replace(">> >>", ">> /Annots [<< /Type /Annot /Subtype /Link /Rect [0 0 1 1] /Dest 10 0 R >>] >>", StringComparison.Ordinal),
            "<< /Type /Outlines /First 5 0 R /Last 9 0 R /Count 5 >>",
            "<< /Title (A) /Parent 4 0 R /Next 6 0 R >>",
            "<< /Title (B) /Parent 4 0 R /Prev 5 0 R /Next 7 0 R >>",
            "<< /Title (C) /Parent 4 0 R /Prev 6 0 R /Next 8 0 R >>",
            "<< /Title (D) /Parent 4 0 R /Prev 7 0 R /Next 9 0 R >>",
            "<< /Title (E) /Parent 4 0 R /Prev 8 0 R /Dest 10 0 R >>",
            "[3 0 R /XYZ (left) 0 0]"));

        Single(report, PdfValidationRuleIds.ObjectValueTypeWrong).Message.Should().Be(
            "Object 10 0, a DestXYZArray in the Arlington model, has element 2 as a string, where the model wants null or a number.");
    }

    [Fact]
    public void A_parent_types_nothing_even_under_a_key_a_wildcard_allows()
    {
        // Object 5 is any dictionary to the structure tree's OBJR, so its /Parent falls under the model's wildcard: were it
        // followed, object 6 would be typed "any dictionary" a step before the outline reaches it as an outline item.
        var report = Validate(Pdf(
            "<< /Type /Catalog /Pages 2 0 R /Outlines 7 0 R /StructTreeRoot 4 0 R >>",
            Root,
            Page,
            "<< /Type /StructTreeRoot /K << /Type /StructElem /S /Figure /P 4 0 R /K << /Type /OBJR /Pg 3 0 R /Obj 5 0 R >> >> >>",
            "<< /Parent 6 0 R >>",
            "<< /Title 42 /Parent 7 0 R /Prev 11 0 R >>",
            "<< /Type /Outlines /First 8 0 R /Count 5 >>",
            "<< /Title (A) /Parent 7 0 R /Next 9 0 R >>",
            "<< /Title (B) /Parent 7 0 R /Prev 8 0 R /Next 10 0 R >>",
            "<< /Title (C) /Parent 7 0 R /Prev 9 0 R /Next 11 0 R >>",
            "<< /Title (D) /Parent 7 0 R /Prev 10 0 R /Next 6 0 R >>"));

        Single(report, PdfValidationRuleIds.ObjectValueTypeWrong).Message.Should().Be(
            "Object 6 0, an OutlineItem in the Arlington model, has /Title as an integer, where the model wants a string.");
    }

    [Fact]
    public void A_root_that_leads_to_no_catalog_is_the_file_rule_s_alone()
    {
        // /Root names the page tree, and no object is a catalog: file.root-invalid's, with nothing to type as a catalog.
        var file = PdfTemplate.Build(Template("<< /Pages 2 0 R /Type /Book >>", Root, Page).Replace("/Root 1 0 R", "/Root 2 0 R", StringComparison.Ordinal));

        var report = Validate(file);

        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeTrue();
        report.Findings.Select(finding => finding.RuleId).Should().NotContain(GeneratedRules);
    }

    [Fact]
    public void A_discriminator_chooses_the_candidate_and_its_rules_apply()
    {
        // /S /URI names ActionURI among the actions, which requires /URI.
        var report = Validate(Pdf("<< /Type /Catalog /Pages 2 0 R /OpenAction << /S /URI >> >>", Root, Page));

        Single(report, PdfValidationRuleIds.ObjectKeyMissing).Message.Should().Be(
            "The dictionary under /OpenAction of object 1 0, an ActionURI in the Arlington model, lacks /URI, which the model requires.");
    }

    [Fact]
    public void A_repeating_group_skips_an_optional_member_that_is_absent()
    {
        // ArrayOfAttributeRevisions is [dictionary integer?]*: the integers that follow attributes are optional.
        Validate(StructureWith("/A [<< /O /Layout >> << /O /List >> 0 << /O /Table >>]")).Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_repeating_group_member_of_the_wrong_type_is_reported_as_its_element()
    {
        var report = Validate(StructureWith("/A [<< /O /Layout >> 0 << /O /List >> (x)]"));

        Single(report, PdfValidationRuleIds.ObjectValueTypeWrong).Message.Should().Be(
            "The array under /A of object 5 0, an ArrayOfAttributeRevisions in the Arlington model, has element 3 as a string, where the model wants a dictionary or a stream.");
    }

    [Fact]
    public void An_array_shorter_than_its_required_elements_lacks_those_it_does_not_hold()
    {
        var report = Validate(Pdf(Catalog, Root, Page.Replace("/Resources << >>", "/Resources << /ColorSpace << /CS0 [/Indexed /DeviceRGB 1] >> >>", StringComparison.Ordinal)));

        Single(report, PdfValidationRuleIds.ObjectKeyMissing).Message.Should().Be(
            "The array under /Resources/ColorSpace/CS0 of object 3 0, an IndexedColorSpace in the Arlington model, lacks element 3, which the model requires: it holds 3 elements.");
    }

    [Fact]
    public void An_inheritable_key_an_ancestor_gives_is_not_missing()
    {
        var form = "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] >> >>";

        var inherited = Validate(Pdf(form, Root, Page, "<< /FT /Tx /T (parent) /DA (/Helv 0 Tf 0 g) /Kids [5 0 R] >>", "<< /FT /Tx /T (child) /Parent 4 0 R >>"));
        var lacking = Validate(Pdf(form, Root, Page, "<< /FT /Tx /T (parent) /Kids [5 0 R] >>", "<< /FT /Tx /T (child) /Parent 4 0 R >>"));

        inherited.Findings.Should().BeEmpty();
        Single(lacking, PdfValidationRuleIds.ObjectKeyMissing).Message.Should().Be(
            "A FieldTx lacks /DA, which the Arlington model requires; 2 objects do, the first object 4 0.");
    }

    [Fact]
    public void A_field_takes_its_default_appearance_from_the_interactive_form()
    {
        // O6: ISO 32000-1 Table 218 makes the AcroForm's /DA the document-wide default, which the model does not encode.
        var withDefault = Validate(Pdf("<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] /DA (/Helv 0 Tf 0 g) >> >>", Root, Page, "<< /FT /Tx /T (a) >>"));
        var without = Validate(Pdf("<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] >> >>", Root, Page, "<< /FT /Tx /T (a) >>"));

        withDefault.Findings.Should().BeEmpty();
        Single(without, PdfValidationRuleIds.ObjectKeyMissing).Message.Should().Be(
            "Object 4 0, a FieldTx in the Arlington model, lacks /DA, which the model requires.");
    }

    [Fact]
    public void A_reference_to_an_object_the_file_lacks_is_present_and_not_judged()
    {
        // /Type names nothing: the key is there, its value unknown, and object.reference-missing reports the fault.
        var report = Validate(Pdf("<< /Type 9 0 R /Pages 2 0 R /Outlines 8 0 R >>", Root, Page));

        report.Findings.Select(finding => finding.RuleId).Should().Equal(PdfValidationRuleIds.ObjectReferenceMissing);
    }

    [Fact]
    public void An_object_the_reader_could_not_produce_is_present_and_not_judged()
    {
        // Object 6's header is gone: its entry leads nowhere, which xref.entry-broken reports; the outline item's
        // /Title is there, of a value no one can know.
        var template = Template(
            "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R >>",
            Root,
            Page,
            "<< /Type /Outlines /First 5 0 R /Last 5 0 R /Count 1 >>",
            "<< /Title 6 0 R /Parent 4 0 R >>",
            "(Title)");

        var report = Validate(PdfTemplate.Build(template.Replace("6 0 obj\n", "6 0 obk\n", StringComparison.Ordinal)));

        report.Contains(PdfValidationRuleIds.XRefEntryBroken).Should().BeTrue();
        report.Findings.Select(finding => finding.RuleId).Should().NotContain(GeneratedRules);
    }

    [Fact]
    public void A_null_written_in_an_array_is_judged_and_one_an_object_holds_is_not()
    {
        var direct = Validate(Pdf(Catalog, Root, Page.Replace("/Resources << >>", "/Resources << /ProcSet [null] >>", StringComparison.Ordinal)));
        var referenced = Validate(Pdf(Catalog, Root, Page.Replace("/Resources << >>", "/Resources << /ProcSet [4 0 R] >>", StringComparison.Ordinal), "null"));

        Single(direct, PdfValidationRuleIds.ObjectValueTypeWrong).Message.Should().Be(
            "The array under /Resources/ProcSet of object 3 0, an ArrayOfNamesForProcSet in the Arlington model, has element 0 as null, where the model wants a name.");
        referenced.Findings.Should().BeEmpty("a reference that reads as null may be an object the reader lost, whose fault is not the array's");
    }

    [Theory]
    [InlineData("1.0", 1)]
    [InlineData("1.2", 0)]
    [InlineData("1.7", 0)]
    [InlineData("9.9", 0)]
    public void A_key_required_in_one_version_is_judged_in_that_version_alone(string version, int missing)
    {
        // A form XObject's /Name is required in PDF 1.0 only (O8); an unknown version judges no version-bound key.
        var report = Validate(Declaring(
            version,
            string.Empty,
            Catalog,
            Root,
            Page.Replace("/Resources << >>", "/Resources << /XObject << /Fm0 4 0 R >> >>", StringComparison.Ordinal),
            "<< /Type /XObject /Subtype /Form /BBox [0 0 1 1] /Resources << >> /Length 0 >>\nstream\n\nendstream"));

        var findings = report.Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.ObjectKeyMissing).ToList();
        findings.Should().HaveCount(missing);
        findings.Should().OnlyContain(finding => finding.Message ==
            "Object 4 0, an XObjectFormType1 in the Arlington model, lacks /Name, which the model requires.");
    }

    [Theory]
    [InlineData("2.0", "", true)]
    [InlineData("1.7", "", false)]
    [InlineData("1.7", "/Version /2.0", true)]
    [InlineData("2.x", "", false)]
    public void A_key_deprecated_in_the_declared_version_is_information(string header, string catalogVersion, bool reported)
    {
        var report = Validate(Declaring(
            header,
            "/Info 4 0 R",
            $"<< /Type /Catalog /Pages 2 0 R {catalogVersion} >>",
            Root,
            Page,
            "<< /Author (A. Writer) >>"));

        var findings = report.Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.ObjectKeyDeprecated).ToList();

        if (!reported)
        {
            findings.Should().BeEmpty();
            return;
        }

        var finding = findings.Should().ContainSingle().Which;
        finding.Severity.Should().Be(PdfValidationSeverity.Information, "a deprecated key still conforms (ADR 45)");
        finding.Message.Should().Be(
            "Object 4 0, a DocInfo in the Arlington model, has /Author, which the model says PDF 2.0 deprecates; the file declares PDF 2.0.");
    }

    [Fact]
    public void The_version_is_the_header_s_or_the_catalog_s_when_later_and_none_without_a_header()
    {
        Version("1.4", string.Empty).Should().Be(14);
        Version("1.4", "/Version /1.6").Should().Be(16);
        Version("1.7", "/Version /1.4").Should().Be(17, "a catalog cannot lower the header's version");
        Version("1.9", string.Empty).Should().Be(0, "1.9 is no version of PDF");
        Version("1.9", "/Version /1.5").Should().Be(0, "a catalog's version is later than a header's, and there is none to be later than");

        using var headerless = PdfDocument.Open(Headerless(Pdf(Catalog, Root, Page)));
        headerless.Version.Should().Be("1.4", "the reader's default, which the walk does not take for a declaration");
        ArlingtonWalk.DeclaredVersion(headerless).Should().Be(0);

        using var catalogOnly = PdfDocument.Open(Headerless(Pdf("<< /Type /Catalog /Pages 2 0 R /Version /1.7 >>", Root, Page)));
        ArlingtonWalk.DeclaredVersion(catalogOnly).Should().Be(0, "without a header, the file declares no version the rules rely on");
    }

    [Fact]
    public void O1_a_form_without_resources_is_judged_only_in_PDF_2_0()
    {
        const string form = "<< /Type /XObject /Subtype /Form /BBox [0 0 1 1] /Length 0 >>\nstream\n\nendstream";
        var page = Page.Replace("/Resources << >>", "/Resources << /XObject << /Fm0 4 0 R >> >>", StringComparison.Ordinal);

        Validate(Declaring("1.7", string.Empty, Catalog, Root, page, form)).Findings.Should().BeEmpty();
        Validate(Declaring("9.9", string.Empty, Catalog, Root, page, form)).Contains(PdfValidationRuleIds.ObjectKeyMissing).Should().BeFalse(
            "a file that declares no version of PDF is held to no key a version requires");
        Single(Validate(Declaring("2.0", string.Empty, Catalog, Root, page, form)), PdfValidationRuleIds.ObjectKeyMissing).Message.Should().Be(
            "Object 4 0, an XObjectFormType1 in the Arlington model, lacks /Resources, which the model requires.");
    }

    [Fact]
    public void O2_an_encoding_in_a_form_s_resources_is_not_deprecated()
    {
        // In PDF 2.0 the form's resources are judged, and /ProcSet is deprecated there; /Encoding is not.
        var report = Validate(Declaring(
            "2.0",
            string.Empty,
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [] /DR << /Encoding << /PDFDocEncoding 4 0 R >> /ProcSet [/PDF] >> >> >>",
            Root,
            Page,
            "<< /Type /Encoding /Differences [24 /breve] >>"));

        report.Findings.Should().OnlyContain(finding => finding.RuleId == PdfValidationRuleIds.ObjectKeyDeprecated)
            .And.Contain(finding => finding.Message ==
                "The dictionary under /AcroForm/DR of object 1 0, a Resource in the Arlington model, has /ProcSet, which the model says PDF 2.0 deprecates; the file declares PDF 2.0.")
            .And.NotContain(finding => finding.Message.Contains("/Encoding", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("400.0", false)]
    [InlineData("400", false)]
    [InlineData("(bold)", true)]
    public void O3_a_font_weight_is_a_number(string weight, bool reported)
    {
        var report = Validate(Pdf(
            Catalog,
            Root,
            Page.Replace("/Resources << >>", "/Resources << /Font << /F0 4 0 R >> >>", StringComparison.Ordinal),
            "<< /Type /Font /Subtype /TrueType /BaseFont /Arial /FirstChar 32 /LastChar 32 /Widths [250] /FontDescriptor 5 0 R >>",
            $"<< /Type /FontDescriptor /FontName /Arial /Flags 32 /FontBBox [0 0 1 1] /ItalicAngle 0 /Ascent 1 /Descent 0 /CapHeight 1 /StemV 80 /FontWeight {weight} >>"));

        report.Contains(PdfValidationRuleIds.ObjectValueTypeWrong).Should().Be(reported);
        report.Findings.Where(finding => finding.RuleId != PdfValidationRuleIds.ObjectValueTypeWrong).Should().BeEmpty();
    }

    [Theory]
    [InlineData("/REx /11.0.0", false)]
    [InlineData("/REx (11.0.0)", false)]
    [InlineData("/REx 11", true)]
    public void O4_and_O5_a_signature_s_build_data_is_Adobe_s_to_specify(string revision, bool reported)
    {
        var report = Validate(Pdf(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] /SigFlags 3 >> >>",
            Root,
            Page,
            "<< /FT /Sig /T (Signature) /V 5 0 R >>",
            $"<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached /ByteRange [0 0 0 0] /Contents <00> /Prop_Build << /Filter << /Name /Adobe.PPKLite /V 2 >> /App << /Name /Acrobat {revision} /V 11 >> >> >>"));

        report.Contains(PdfValidationRuleIds.ObjectKeyDeprecated).Should().BeFalse("ISO 32000-1 deprecates nothing in Prop_Build");
        report.Contains(PdfValidationRuleIds.ObjectValueTypeWrong).Should().Be(reported);
        report.Findings.Where(finding => finding.RuleId != PdfValidationRuleIds.ObjectValueTypeWrong).Should().BeEmpty();
    }

    [Theory]
    [InlineData("[4 0 R []]", false)]
    [InlineData("[4 0 R [(Label) 4 0 R]]", false)]
    [InlineData("[4 0 R [[4 0 R]]]", true)]
    public void O7_an_empty_order_sub_array_is_sound_and_a_nested_one_is_not(string order, bool reported)
    {
        var report = Validate(Pdf(
            $"<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [4 0 R] /D << /Order {order} >> >> >>",
            Root,
            Page,
            "<< /Type /OCG /Name (Layer) >>"));

        report.Contains(PdfValidationRuleIds.ObjectValueTypeWrong).Should().Be(reported, "ISO 32000-1 Table 101 does not describe a sub-array nested in a sub-array");
        report.Contains(PdfValidationRuleIds.ObjectKeyMissing).Should().BeFalse("nothing requires a sub-array to hold an element");
    }

    [Theory]
    [InlineData("1.2", "/Subtype /Form /BBox [0 0 1 1]", false)]
    [InlineData("1.2", "/Subtype /Form /FormType 1 /BBox [0 0 1 1] /Matrix [1 0 0 1 0 0] /Name /Fm0", false)]
    [InlineData("1.2", "/BBox [0 0 1 1]", true)]
    public void O8_a_form_s_type_and_matrix_are_optional_in_every_version(string version, string entries, bool reported)
    {
        // ISO 32000-1 Table 95 makes /FormType and /Matrix optional and /Name required in PDF 1.0 only; /Subtype stays
        // required. Distiller 2 and 3 wrote such forms, the second as appearance streams without /Subtype.
        var report = Validate(Declaring(
            version,
            string.Empty,
            Catalog,
            Root,
            Page.Replace("/Resources << >>", "/Resources << >> /Annots [5 0 R]", StringComparison.Ordinal),
            $"<< {entries} /Resources << >> /Length 0 >>\nstream\n\nendstream",
            "<< /Type /Annot /Subtype /Square /Rect [0 0 1 1] /AP << /N << /On 4 0 R >> >> /AS /On >>"));

        if (!reported)
        {
            report.Findings.Should().BeEmpty();
            return;
        }

        Single(report, PdfValidationRuleIds.ObjectKeyMissing).Message.Should().Be(
            "Object 4 0, an XObjectFormType1 in the Arlington model, lacks /Subtype, which the model requires.");
    }

    [Theory]
    [InlineData("<< /Type /Encoding /Differences [65 /A] >>", false)]
    [InlineData("/WinAnsiEncoding", true)]
    [InlineData("(WinAnsiEncoding)", true)]
    public void A_type_3_font_s_encoding_is_the_dictionary_ISO_32000_1_requires(string encoding, bool reported)
    {
        var report = Validate(Pdf(
            Catalog,
            Root,
            Page.Replace("/Resources << >>", "/Resources << /Font << /T0 4 0 R >> >>", StringComparison.Ordinal),
            $"<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1 1] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /A 5 0 R >> /Encoding {encoding} /FirstChar 65 /LastChar 65 /Widths [1000] /Resources << >> >>",
            "<< /Length 0 >>\nstream\n\nendstream"));

        report.Contains(PdfValidationRuleIds.ObjectValueTypeWrong).Should().Be(
            reported, "ISO 32000-1 Table 112 requires \"An encoding dictionary whose Differences array shall specify the complete character encoding\"");
        report.Findings.Where(finding => finding.RuleId != PdfValidationRuleIds.ObjectValueTypeWrong).Should().BeEmpty();
    }

    [Theory]
    [InlineData("/Creator (Esri) /Subtype /Artwork", null)]
    [InlineData("/Creator (Esri) /Subtype /Artwork /SubType (Layer)", null)]
    [InlineData("/Creator (Esri) /SubType (Layer)", PdfValidationRuleIds.ObjectKeyMissing)]
    [InlineData("/Creator (Esri) /Subtype (Artwork)", PdfValidationRuleIds.ObjectValueTypeWrong)]
    public void O9_a_misspelled_creator_subtype_is_an_additional_entry_and_the_key_it_stands_for_stays_required(string entries, string? ruleId)
    {
        var report = Validate(Pdf(
            "<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [4 0 R] /D << /Order [4 0 R] >> >> >>",
            Root,
            Page,
            $"<< /Type /OCG /Name (Layer) /Usage << /CreatorInfo << {entries} >> >> >>"));

        report.Findings.Select(finding => finding.RuleId).Should().BeEquivalentTo(ruleId is null ? [] : [ruleId]);
    }

    [Theory]
    [InlineData("/Type /Page /Parent 2 0 R /Resources << >>", PdfValidationRuleIds.PageTreeMediaBoxInvalid)]
    [InlineData("/Type /Page /Parent 2 0 R /Resources << >> /MediaBox (A4)", PdfValidationRuleIds.PageTreeMediaBoxInvalid)]
    [InlineData("/Type /Page /Parent 2 0 R /MediaBox [0 0 595 842]", PdfValidationRuleIds.PageTreeResourcesMissing)]
    [InlineData("/Type /Page /Parent (2) /MediaBox [0 0 595 842] /Resources << >>", PdfValidationRuleIds.PageTreeParentWrong)]
    public void A_page_s_fault_a_page_tree_rule_reports_is_that_rule_s_alone(string page, string ruleId)
    {
        var report = Validate(Pdf(Catalog, Root, $"<< {page} >>"));

        report.Findings.Select(finding => finding.RuleId).Should().Equal(ruleId);
    }

    [Theory]
    [InlineData("/Type /Pages /Count 1", PdfValidationRuleIds.PageTreeKidsMissing)]
    [InlineData("/Type /Pages /Kids (3 0 R) /Count 1", PdfValidationRuleIds.PageTreeKidsMissing)]
    [InlineData("/Type /Pages /Kids [3 0 R]", PdfValidationRuleIds.PageTreeCountMismatch)]
    [InlineData("/Type /Pages /Kids [3 0 R] /Count 1 /MediaBox /A4", PdfValidationRuleIds.PageTreeMediaBoxInvalid)]
    [InlineData("/Type /Pages /Kids [3 0 R null] /Count 2", PdfValidationRuleIds.PageTreeKidInvalid)]
    public void A_node_s_fault_a_page_tree_rule_reports_is_that_rule_s_alone(string node, string ruleId)
    {
        var report = Validate(Pdf(Catalog, $"<< {node} >>", Page));

        report.Findings.Select(finding => finding.RuleId).Should().Contain(ruleId).And.NotContain(GeneratedRules);
    }

    [Theory]
    [InlineData("/Type /Pages /Kids 4 0 R /Count 1", "<< /Kids [3 0 R] >>", PdfValidationRuleIds.PageTreeKidsMissing)]
    [InlineData("/Type /Pages /Kids 4 0 R /Count 1", "7", PdfValidationRuleIds.PageTreeKidsMissing)]
    [InlineData("/Type /Pages /Kids 4 0 R /Count 1", "null", PdfValidationRuleIds.PageTreeKidsMissing)]
    [InlineData("/Type /Pages /Kids [3 0 R] /Count 4 0 R", "null", PdfValidationRuleIds.PageTreeCountMismatch)]
    [InlineData("/Type /Pages", "null", PdfValidationRuleIds.PageTreeCountMismatch)]
    [InlineData("/Type /Pages /Kids [3 0 R 2 0 R]", "null", PdfValidationRuleIds.PageTreeCountMismatch)]
    public void A_node_s_fault_given_through_a_reference_or_a_null_is_still_the_page_tree_rule_s(string node, string fourth, string ruleId)
    {
        var report = Validate(Pdf(Catalog, $"<< {node} >>", Page, fourth));

        report.Findings.Select(finding => finding.RuleId).Should().Contain(ruleId).And.NotContain(GeneratedRules);
    }

    [Theory]
    [InlineData("/MediaBox 4 0 R /Resources << >>", PdfValidationRuleIds.PageTreeMediaBoxInvalid)]
    [InlineData("/MediaBox null /Resources << >>", PdfValidationRuleIds.PageTreeMediaBoxInvalid)]
    [InlineData("/MediaBox [0 0 595 842] /Resources 4 0 R", PdfValidationRuleIds.PageTreeResourcesMissing)]
    [InlineData("/MediaBox [0 0 595 842] /Resources null", PdfValidationRuleIds.PageTreeResourcesMissing)]
    public void A_page_s_fault_given_through_a_reference_or_a_null_is_still_the_page_tree_rule_s(string entries, string ruleId)
    {
        var report = Validate(Pdf(Catalog, Root, $"<< /Type /Page /Parent 2 0 R {entries} >>", "null"));

        report.Findings.Select(finding => finding.RuleId).Should().Equal(ruleId);
    }

    [Fact]
    public void A_kid_of_a_kids_array_that_is_an_object_of_its_own_is_the_page_tree_rule_s_alone()
    {
        var report = Validate(Pdf(Catalog, "<< /Type /Pages /Kids 4 0 R /Count 2 >>", Page, "[3 0 R (4 0 R)]"));

        report.Findings.Select(finding => finding.RuleId).Should().Equal(PdfValidationRuleIds.PageTreeKidInvalid);
    }

    [Fact]
    public void A_page_the_tree_does_not_list_is_the_generated_rules_to_judge()
    {
        // Page 4 is only a destination's: no page tree rule judges its /Parent, its box or its resources.
        var report = Validate(Pdf(
            "<< /Type /Catalog /Pages 2 0 R /OpenAction [4 0 R /Fit] >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 595 842] /Resources << >> >>",
            "<< /Type /Page /Parent 2 0 R >>",
            "<< /Type /Page /Parent (2 0 R) /Contents 5 0 R >>",
            "<< /Length 0 >>\nstream\n\nendstream"));

        report.Findings.Select(finding => finding.RuleId).Should().Equal(
            PdfValidationRuleIds.ObjectKeyMissing,
            PdfValidationRuleIds.ObjectKeyMissing,
            PdfValidationRuleIds.ObjectValueTypeWrong,
            PdfValidationRuleIds.PageTreePageOrphaned);
        report.Findings.Take(3).Select(finding => finding.Message).Should().Equal(
            "Object 4 0, a PageObject in the Arlington model, lacks /MediaBox, which the model requires.",
            "Object 4 0, a PageObject in the Arlington model, lacks /Resources, which the model requires.",
            "Object 4 0, a PageObject in the Arlington model, has /Parent as a string, where the model wants a dictionary.");
    }

    [Fact]
    public void A_parent_the_page_tree_cannot_compare_is_the_generated_rules_to_judge()
    {
        // Page 3 lies under a node written in the root's /Kids, which has no number for a /Parent to name.
        var report = Validate(Pdf(
            Catalog,
            "<< /Type /Pages /Kids [<< /Type /Pages /Parent 2 0 R /Kids [3 0 R] /Count 1 >>] /Count 1 >>",
            "<< /Type /Page /Parent (2 0 R) /MediaBox [0 0 595 842] /Resources << >> >>"));

        report.Findings.Select(finding => finding.RuleId).Should().Equal(PdfValidationRuleIds.ObjectValueTypeWrong, PdfValidationRuleIds.PageTreeKidInvalid);
    }

    [Fact]
    public void A_node_without_parent_is_the_page_tree_rule_s_alone()
    {
        var report = Validate(Pdf(
            Catalog,
            "<< /Type /Pages /Kids [4 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 4 0 R /MediaBox [0 0 595 842] /Resources << >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"));

        report.Findings.Select(finding => finding.RuleId).Should().Equal(PdfValidationRuleIds.PageTreeParentWrong);
    }

    [Theory]
    [InlineData("/Count 1.0", PdfValidationRuleIds.ObjectValueTypeWrong)]
    [InlineData("/Resources (none)", PdfValidationRuleIds.ObjectValueTypeWrong)]
    public void A_fault_the_page_tree_rules_leave_is_a_generated_rule_s(string entry, string ruleId)
    {
        // A /Count that is no integer is not count-mismatch's, nor a /Resources that is no dictionary resources-missing's.
        var file = entry.StartsWith("/Count", StringComparison.Ordinal)
            ? Pdf(Catalog, $"<< /Type /Pages /Kids [3 0 R] {entry} >>", Page)
            : Pdf(Catalog, Root, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] {entry} >>");

        Validate(file).Findings.Select(finding => finding.RuleId).Should().Equal(ruleId);
    }

    [Fact]
    public void The_overrides_name_the_generated_rules_by_their_identifiers()
    {
        Tool.OverrideFile.KeyMissing.Should().Be(PdfValidationRuleIds.ObjectKeyMissing);
        Tool.OverrideFile.ValueTypeWrong.Should().Be(PdfValidationRuleIds.ObjectValueTypeWrong);
        Tool.OverrideFile.TypeValueWrong.Should().Be(PdfValidationRuleIds.ObjectTypeValueWrong);
        Tool.OverrideFile.KeyDeprecated.Should().Be(PdfValidationRuleIds.ObjectKeyDeprecated);
    }

    [Fact]
    public void A_long_outline_chain_is_walked_without_recursion()
    {
        const int items = 20_000;
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R >>")
            .WithObject(2, Root)
            .WithObject(3, Page)
            .WithObject(4, string.Create(CultureInfo.InvariantCulture, $"<< /Type /Outlines /First 5 0 R /Last {4 + items} 0 R /Count {items} >>"));

        for (var number = 5; number < 5 + items; number++)
        {
            var next = number + 1 < 5 + items ? string.Create(CultureInfo.InvariantCulture, $" /Next {number + 1} 0 R") : string.Empty;
            var previous = number > 5 ? string.Create(CultureInfo.InvariantCulture, $" /Prev {number - 1} 0 R") : string.Empty;
            builder.WithObject(number, $"<< /Title (Item) /Parent 4 0 R{previous}{next} >>");
        }

        var file = builder.BuildClassic(rootNumber: 1);
        var stopwatch = Stopwatch.StartNew();

        var report = Validate(file);

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));
        report.Findings.Should().BeEmpty();
        Walk(file).Checked.Should().BeGreaterThanOrEqualTo(items);
    }

    [Fact]
    public void A_parent_cycle_among_fields_ends_the_search_for_an_inherited_key()
    {
        var report = Validate(Pdf(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] >> >>",
            Root,
            Page,
            "<< /FT /Tx /T (a) /Parent 5 0 R /Kids [5 0 R] >>",
            "<< /FT /Tx /T (b) /Parent 4 0 R >>"));

        Single(report, PdfValidationRuleIds.ObjectKeyMissing).Message.Should().StartWith("A FieldTx lacks /DA");
    }

    [Fact]
    public void A_name_tree_whose_kids_loop_is_walked_once()
    {
        var report = Validate(Pdf(
            "<< /Type /Catalog /Pages 2 0 R /Names << /Dests 4 0 R >> >>",
            Root,
            Page,
            "<< /Kids [5 0 R] >>",
            "<< /Kids [4 0 R 5 0 R] /Names [(a) << /D [3 0 R /Fit] /Extra 1 >> (b) << /D 7 >>] >>"));

        Single(report, PdfValidationRuleIds.ObjectValueTypeWrong).Message.Should().Be(
            "The dictionary under /Names[3] of object 5 0, a DestDict in the Arlington model, has /D as an integer, where the model wants an array.");
    }

    [Theory(Timeout = 60_000)]
    [InlineData("/Names << /Dests << /Kids 4 0 R >> >>")]
    [InlineData("/PageLabels << /Kids 4 0 R >>")]
    public async Task A_tree_node_written_in_the_kids_array_it_names_is_expanded_once(string entry)
    {
        var file = Pdf($"<< /Type /Catalog /Pages 2 0 R {entry} >>", Root, Page, "[<< /Kids 4 0 R >>]");

        var report = await Task.Run(() => Validate(file), TestContext.Current.CancellationToken);

        report.Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_tree_two_entries_name_is_walked_once_as_the_first_types_it()
    {
        // /Dests comes first in ordinal order: its destination is judged, and the tree is not walked again as files.
        var report = Validate(Pdf(
            "<< /Type /Catalog /Pages 2 0 R /Names << /EmbeddedFiles 4 0 R /Dests 4 0 R >> >>",
            Root,
            Page,
            "<< /Names [(a) << /D 7 >>] >>"));

        Single(report, PdfValidationRuleIds.ObjectValueTypeWrong).Message.Should().Be(
            "The dictionary under /Names[1] of object 4 0, a DestDict in the Arlington model, has /D as an integer, where the model wants an array.");
        report.Findings.Should().ContainSingle();
    }

    [Fact(Timeout = 60_000)]
    public async Task Tree_nodes_that_share_one_kids_array_expand_it_once()
    {
        const int nodes = 3_000;
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R /Names << /Dests << /Kids 4 0 R >> >> >>")
            .WithObject(2, Root)
            .WithObject(3, Page)
            .WithObject(4, "[" + string.Join(' ', Enumerable.Range(5, nodes).Select(static number => string.Create(CultureInfo.InvariantCulture, $"{number} 0 R"))) + "]");

        for (var number = 5; number < 5 + nodes; number++)
        {
            builder.WithObject(number, "<< /Kids 4 0 R >>");
        }

        using var document = PdfDocument.Open(builder.BuildClassic(rootNumber: 1));
        var pages = PageTreeWalk.Run(document);
        await Task.Run(() => ArlingtonWalk.Run(document, pages), TestContext.Current.CancellationToken);

        // Read once already: what a second walk allocates is its own.
        var allocated = await Task.Run(
            () =>
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                ArlingtonWalk.Run(document, pages);
                return GC.GetAllocatedBytesForCurrentThread() - before;
            },
            TestContext.Current.CancellationToken);

        allocated.Should().BeLessThan(2 * 1024 * 1024, "the shared array is expanded once, not once per node that names it");
    }

    [Fact(Timeout = 60_000)]
    public async Task Tree_nodes_that_share_one_names_array_have_its_values_checked_once()
    {
        const int nodes = 1_000;
        var leaves = new StringBuilder("[");

        for (var index = 0; index < nodes; index++)
        {
            leaves.Append(CultureInfo.InvariantCulture, $"(d{index:D4}) << /D [3 0 R /Fit] >> ");
        }

        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R /Names << /Dests << /Kids [" +
                string.Join(' ', Enumerable.Range(5, nodes).Select(static number => string.Create(CultureInfo.InvariantCulture, $"{number} 0 R"))) + "] >> >> >>")
            .WithObject(2, Root)
            .WithObject(3, Page)
            .WithObject(4, leaves.Append(']').ToString());

        for (var number = 5; number < 5 + nodes; number++)
        {
            builder.WithObject(number, "<< /Names 4 0 R >>");
        }

        var file = builder.BuildClassic(rootNumber: 1);

        var walk = await Task.Run(() => Walk(file), TestContext.Current.CancellationToken);

        walk.Checked.Should().BeLessThan(3 * nodes, "each destination and its array are checked once, however many nodes name the array");
    }

    [Fact]
    public void Two_validations_give_the_same_findings_in_the_same_order()
    {
        var file = Pdf(
            "<< /Pages 2 0 R /OpenAction << /S /URI >> /Outlines 4 0 R >>",
            "<< /Type /Pagez /Kids [3 0 R] /Count 1 >>",
            Page.Replace(">> >>", ">> /Rotate (90) /UserUnit /Big >>", StringComparison.Ordinal),
            "<< /Type /Outline >>");
        using var document = PdfDocument.Open(file);
        var validator = new PdfValidator();

        var first = validator.Validate(document);
        var second = validator.Validate(document);

        first.Findings.Should().HaveCountGreaterThan(3);
        second.Findings.Should().Equal(first.Findings);
    }

    [Fact]
    public void An_object_a_limit_cut_is_not_judged()
    {
        // The catalog runs past 128 bytes, its /Pages beyond them: what it lacks may lie past the limit.
        var file = Pdf("<< /Type /Catalog" + new string(' ', 150) + "/Pages 2 0 R >>", Root, Page);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 128 } });

        var report = new PdfValidator().Validate(document);

        document.Reader.IsCutAtLimit(1).Should().BeTrue();
        report.Findings.Select(finding => finding.RuleId).Should().NotContain(GeneratedRules);
    }

    [Fact]
    public void A_value_inside_a_tree_node_a_limit_cut_is_not_judged()
    {
        // The node runs past 128 bytes: the destination in it is read only as far as the limit, without its /D.
        var file = Pdf(
            "<< /Type /Catalog /Pages 2 0 R /Names << /Dests 4 0 R >> >>",
            Root,
            Page,
            "<< /Names [(a) <<" + new string(' ', 150) + "/D [3 0 R /Fit] >>] >>");
        using var document = PdfDocument.Open(file, Limited(maxObjectLength: 128));

        var report = new PdfValidator().Validate(document);

        document.Reader.IsCutAtLimit(4).Should().BeTrue();
        report.Findings.Select(finding => finding.RuleId).Should().NotContain(GeneratedRules);
    }

    [Fact]
    public void A_key_an_ancestor_a_limit_cut_may_give_is_not_missing()
    {
        // Field 5 inherits /DA from field 4, which gives it past the 128 bytes the reader reads of it.
        var file = Pdf(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [5 0 R] >> >>",
            Root,
            Page,
            "<< /FT /Tx /T (a) /Kids [5 0 R]" + new string(' ', 150) + "/DA (/Helv 0 Tf 0 g) >>",
            "<< /FT /Tx /T (b) /Parent 4 0 R >>");
        using var document = PdfDocument.Open(file, Limited(maxObjectLength: 128));

        var report = new PdfValidator().Validate(document);

        document.Reader.IsCutAtLimit(4).Should().BeTrue();
        report.Findings.Select(finding => finding.RuleId).Should().NotContain(GeneratedRules);
        Validate(file).Findings.Should().BeEmpty("read whole, field 4 gives field 5 its /DA");
    }

    [Fact]
    public void An_object_an_object_stream_s_decoding_limit_cut_is_not_judged()
    {
        // Object 4 lies whole within the 100 bytes decoded, and item 5 runs past them, its /Title beyond.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R >>")
            .WithObject(2, Root)
            .WithObject(3, Page)
            .WithObject(4, "<< /Type /Outlines /First 5 0 R /Last 5 0 R /Count 1 >>")
            .WithObject(5, "<< /Parent 4 0 R" + new string(' ', 200) + "/Title (x) >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [4, 5], compressObjectStream: true);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 100 } });

        var report = new PdfValidator().Validate(document);

        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitDecodedStream);
        document.Reader.IsCutAtLimit(5).Should().BeTrue();
        document.Reader.IsCutAtLimit(4).Should().BeFalse("it ends before the limit does");
        report.Findings.Select(finding => finding.RuleId).Should().NotContain(GeneratedRules);
        Validate(file).Findings.Should().BeEmpty("read whole, item 5 has its /Title");
    }

    [Fact]
    public void A_catalog_written_in_the_trailer_is_checked_there()
    {
        var template = Template("<< /Title (Direct) >>", Root, Page)
            .Replace("/Root 1 0 R", "/Root << /Type /Catalog /Pages 2 0 R /PageMode 7 >> /Info 1 0 R", StringComparison.Ordinal);

        var finding = Single(Validate(PdfTemplate.Build(template)), PdfValidationRuleIds.ObjectValueTypeWrong);

        finding.Message.Should().Be(
            "The dictionary under /Root of the trailer, a Catalog in the Arlington model, has /PageMode as an integer, where the model wants a name.");
        finding.Location.Object.Should().BeNull();
        finding.Location.Position.Should().BePositive();
    }

    [Fact]
    public void A_type_written_as_an_object_of_its_own_is_judged_by_the_name_it_holds()
    {
        var report = Validate(Pdf(Catalog, Root, Page.Replace("/Type /Page ", "/Type 4 0 R ", StringComparison.Ordinal), "/Font"));

        Single(report, PdfValidationRuleIds.ObjectTypeValueWrong).Message.Should().Be(
            "Object 3 0, a PageObject in the Arlington model, has /Type /Font, where the model wants /Page or /Template.");
    }

    [Fact]
    public void A_tree_node_written_as_a_stream_has_its_values_judged()
    {
        var report = Validate(Pdf(
            "<< /Type /Catalog /Pages 2 0 R /Names << /Dests 4 0 R >> >>",
            Root,
            Page,
            "<< /Kids [5 0 R] >>",
            "<< /Names [(a) << /D 7 >>] /Length 0 >>\nstream\n\nendstream"));

        Single(report, PdfValidationRuleIds.ObjectValueTypeWrong).Message.Should().Be(
            "The dictionary under /Names[1] of object 5 0, a DestDict in the Arlington model, has /D as an integer, where the model wants an array.");
    }

    [Fact]
    public void Arrays_of_a_tree_met_first_under_another_key_are_still_expanded()
    {
        // The private keys come first in ordinal order: objects 5 and 7 are read there, before the tree reaches them as
        // its /Kids and its /Names.
        var report = Validate(Pdf(
            "<< /Type /Catalog /Pages 2 0 R /Names << /ACME_Kids 5 0 R /ACME_Names 7 0 R /Dests 4 0 R >> >>",
            Root,
            Page,
            "<< /Kids 5 0 R >>",
            "[6 0 R]",
            "<< /Names 7 0 R >>",
            "[(a) << /D 8 >>]"));

        Single(report, PdfValidationRuleIds.ObjectValueTypeWrong).Message.Should().Be(
            "The dictionary at [1] in object 7 0, a DestDict in the Arlington model, has /D as an integer, where the model wants an array.");
    }

    [Fact]
    public void An_object_a_rebuild_turns_into_an_integer_mid_walk_is_not_checked()
    {
        // The chain names a section that is not there: the index is rebuilt when /PageLabels asks for an object it lacks,
        // after /Outlines typed object 4 and before object 4 is checked. A later definition of object 4, which the
        // rebuilt index takes, leaves no outline to judge. Validation meets the rebuild before the walk, when the
        // object graph resolves every object; the walk run alone does not.
        var template = Template(
            "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R /PageLabels 9 0 R >>",
            Root,
            Page,
            "<< /Type /Outlines /Count (x) >>").Replace("/Root 1 0 R", "/Root 1 0 R /Prev 999999", StringComparison.Ordinal);

        var kept = Walk(PdfTemplate.Build(template));
        var redefined = Walk(PdfTemplate.Build(template + "4 0 obj\n7\nendobj\n"));

        kept.ValueTypesWrong.Should().ContainSingle().Which.Message.Should().Be(
            "Object 4 0, an Outline in the Arlington model, has /Count as a string, where the model wants an integer.");
        redefined.ValueTypesWrong.Should().NotContain(finding => finding.Message.Contains("Outline", StringComparison.Ordinal));
        redefined.Checked.Should().Be(kept.Checked - 1, "object 4 is read again as it is checked, and is an integer by then");
    }

    [Theory]
    [InlineData("tree node", "<< /Type /Catalog /Pages 2 0 R /Names << /ACME 5 0 R /AP 9 0 R /Dests 4 0 R >> >>", "<< /Kids [5 0 R] >>", "<< /Names [(a) << /D 7 >>] >>", 5)]
    [InlineData("candidate", "<< /Type /Catalog /Pages 2 0 R /ACME 4 0 R /ACMF 9 0 R /OpenAction 4 0 R >>", "<< /S /GoTo /D [3 0 R /Fit] >>", "null", 4)]
    [InlineData("ancestor", "<< /Type /Catalog /Pages 2 0 R /ACME 5 0 R /ACMF 9 0 R /OpenAction [4 0 R /Fit] >>", "<< /Type /Page /Parent 5 0 R /Resources << >> >>", "<< /Type /Pages /Kids [4 0 R] /Count 1 /MediaBox [0 0 1 1] >>", 5)]
    public void An_object_a_rebuild_turns_into_a_number_mid_walk_is_skipped_without_throwing(string role, string catalog, string four, string five, int redefined)
    {
        // The chain names a section that is not there: the index is rebuilt when /ACMF or /AP asks for object 9, which
        // the file lacks, after the walk met object 4 or 5 as a dictionary and before it read that object again as a
        // tree node, a candidate to type, or an ancestor to inherit from. The rebuilt index takes a later definition,
        // a number: the walk skips what is no longer a dictionary rather than throwing. Validation meets the rebuild
        // before the walk, when the object graph resolves every object; the walk run alone does not.
        var template = Template(catalog, Root, Page, four, five).Replace("/Root 1 0 R", "/Root 1 0 R /Prev 999999", StringComparison.Ordinal)
            + string.Create(CultureInfo.InvariantCulture, $"{redefined} 0 obj\n7\nendobj\n");

        using var document = PdfDocument.Open(PdfTemplate.Build(template));
        var walk = ArlingtonWalk.Run(document, PageTreeWalk.Run(document));

        document.Diagnostics.Contains(PdfDiagnosticCodes.XRefRebuilt).Should().BeTrue(role);
        document.GetObject(new PdfObjectId(redefined)).Should().BeOfType<PdfInteger>(role);
        walk.Checked.Should().BePositive(role);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("9 0 R")]
    public void A_discriminator_written_as_null_or_naming_nothing_leaves_the_candidates_to_be_weighed(string subtype)
    {
        // /S is the plan's key among the actions, and gives it no name to read: /Flags 1, a value only ActionResetForm
        // lists, makes it the best score, and the action is checked as one.
        var file = Pdf($"<< /Type /Catalog /Pages 2 0 R /OpenAction << /S {subtype} /Flags 1 /Fields (all) >> >>", Root, Page);

        Single(Validate(file), PdfValidationRuleIds.ObjectValueTypeWrong).Message.Should().Be(
            "The dictionary under /OpenAction of object 1 0, an ActionResetForm in the Arlington model, has /Fields as a string, where the model wants an array.");
        Walk(file).ChosenByScore.Should().Be(1);
    }

    [Fact]
    public void A_discriminator_several_objects_share_through_a_reference_chooses_for_each_of_them()
    {
        // Object 4 is read once, for the first annotation; the second reads the name the walk already knows.
        var file = Pdf(
            Catalog,
            Root,
            Page.Replace(">> >>", ">> /Annots [<< /Type /Annot /Subtype 4 0 R /Rect (a) >> << /Type /Annot /Subtype 4 0 R /Rect (b) >>] >>", StringComparison.Ordinal),
            "/Link");

        Single(Validate(file), PdfValidationRuleIds.ObjectValueTypeWrong).Message.Should().Be(
            "An AnnotLink has /Rect as a string, where the Arlington model wants a rectangle; 2 objects give /Rect a type it does not allow, the first the dictionary under /Annots[0] of object 3 0.");

        var walk = Walk(file);
        walk.ChosenByScore.Should().Be(0, "the plan reads /Link for both annotations");
        walk.Ties.Should().Be(0);
    }

    [Fact]
    public void An_inheritable_key_a_parent_written_as_a_stream_gives_is_not_missing()
    {
        // The stream's dictionary gives /DA to field 5: its parent's being a stream is the one fault.
        var report = Validate(Pdf(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [5 0 R] >> >>",
            Root,
            Page,
            "<< /FT /Tx /T (parent) /DA (/Helv 0 Tf 0 g) /Kids [5 0 R] /Length 0 >>\nstream\n\nendstream",
            "<< /FT /Tx /T (child) /Parent 4 0 R >>"));

        report.Findings.Should().ContainSingle().Which.Message.Should().Be(
            "Object 5 0, a FieldTx in the Arlington model, has /Parent as a stream, where the model wants a dictionary.");
    }

    [Fact]
    public void A_key_only_an_extension_requires_is_not_required_of_the_file()
    {
        // Adobe's Extension Level 3 requires /Symbology, /Width, /Height, /XSymWidth and /YSymHeight of a PaperMetaData;
        // no version of ISO 32000 does. The dictionary is checked all the same: its /Version is no number.
        var report = Validate(Pdf(
            Catalog,
            Root,
            Page.Replace(">> >>", ">> /Annots [<< /Type /Annot /Subtype /Widget /Rect [0 0 1 1] /PMD << /Type /PaperMetaData /Version (1) >> >>] >>", StringComparison.Ordinal)));

        report.Findings.Should().ContainSingle().Which.Message.Should().Be(
            "The dictionary under /Annots[0]/PMD of object 3 0, a PaperMetaData in the Arlington model, has /Version as a string, where the model wants a number.");
    }

    [Fact]
    public void A_value_of_a_trailer_no_section_locates_is_located_at_the_document()
    {
        // Without startxref, the trailer is found by scanning and no section is recorded: a finding on a value the
        // trailer holds is located at the document.
        var text = Encoding.Latin1.GetString(Declaring("1.7", "/Info << /Author 7 >>", Catalog, Root, Page));
        var file = Encoding.Latin1.GetBytes(text[..text.IndexOf("startxref", StringComparison.Ordinal)]);

        var finding = Single(Validate(file), PdfValidationRuleIds.ObjectValueTypeWrong);

        finding.Message.Should().Be(
            "The dictionary under /Info of the trailer, a DocInfo in the Arlington model, has /Author as an integer, where the model wants a string.");
        finding.Location.IsDocument.Should().BeTrue();
    }

    private static PdfReaderOptions Limited(int maxObjectLength) =>
        new() { Limits = PdfReaderLimits.Default with { MaxObjectLength = maxObjectLength } };

    /// <summary>A structure tree whose one element, object 5, carries <paramref name="entries"/>.</summary>
    private static byte[] StructureWith(string entries) => Pdf(
        "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 4 0 R >>",
        Root,
        Page,
        "<< /Type /StructTreeRoot /K 5 0 R >>",
        $"<< /Type /StructElem /S /Document /P 4 0 R {entries} >>");

    private static byte Version(string header, string catalog)
    {
        using var document = PdfDocument.Open(Declaring(header, string.Empty, $"<< /Type /Catalog /Pages 2 0 R {catalog} >>", Root, Page));
        return ArlingtonWalk.DeclaredVersion(document);
    }

    private static ArlingtonWalk Walk(byte[] file)
    {
        using var document = PdfDocument.Open(file);
        return ArlingtonWalk.Run(document, PageTreeWalk.Run(document));
    }

    /// <summary>A PDF 1.7 file whose objects, numbered from 1, are <paramref name="bodies"/>.</summary>
    private static byte[] Pdf(params string[] bodies) => Declaring("1.7", string.Empty, bodies);

    /// <summary>A file whose header declares <paramref name="version"/>, whose trailer adds <paramref name="trailer"/>, and whose objects are <paramref name="bodies"/>.</summary>
    private static byte[] Declaring(string version, string trailer, params string[] bodies) =>
        PdfTemplate.Build(Template(bodies).Replace("%PDF-1.7", "%PDF-" + version, StringComparison.Ordinal).Replace("/Root 1 0 R", "/Root 1 0 R " + trailer, StringComparison.Ordinal));

    private static string Template(params string[] bodies)
    {
        var text = new StringBuilder("%PDF-1.7\n");

        for (var index = 0; index < bodies.Length; index++)
        {
            text.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{bodies[index]}\nendobj\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"xref\n0 {bodies.Length + 1}\n{{free}}\n");

        for (var index = 0; index < bodies.Length; index++)
        {
            text.Append(CultureInfo.InvariantCulture, $"{{row:{index + 1}}}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {bodies.Length + 1} /Root 1 0 R >>\nstartxref\n{{xref:1}}\n%%EOF\n");
        return text.ToString();
    }

    /// <summary>The file with its header overwritten, every offset unchanged.</summary>
    private static byte[] Headerless(byte[] file)
    {
        var copy = (byte[])file.Clone();
        "%XYZ-1.7"u8.CopyTo(copy);
        return copy;
    }

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
