using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;
using AdCodicem.Pdf.Validation.Arlington;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The words of the Arlington rules' findings: an array's length in the singular, and the count that stands for the
/// other objects breaking the same row — the same key, an element of an array, a key a dictionary leaves open —, as a
/// small document written to earn each of them reads.
/// </summary>
public class ArlingtonMessageTests
{
    private const string Catalog = "<< /Type /Catalog /Pages 2 0 R >>";
    private const string TwoPages = "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>";

    [Fact]
    public void An_array_short_of_a_required_element_that_holds_one_says_so_in_the_singular()
    {
        var report = Validate(Document(Catalog, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", PageWith("/ColorSpace << /CS0 [/CalRGB] >>")));

        Single(report, PdfValidationRuleIds.ObjectKeyMissing).Message.Should().Be(
            "The array under /Resources/ColorSpace/CS0 of object 3 0, a CalRGBColorSpace in the Arlington model, lacks element 1, which the model requires: it holds 1 element.");
    }

    [Fact]
    public void Arrays_short_of_a_required_element_are_counted_and_the_first_says_how_many_it_holds()
    {
        var page = PageWith("/ColorSpace << /CS0 [/CalRGB] >>");

        var report = Validate(Document(Catalog, TwoPages, page, page));

        var finding = Single(report, PdfValidationRuleIds.ObjectKeyMissing);
        finding.Location.Object.Should().Be(new PdfObjectId(3));
        finding.Message.Should().Be(
            "A CalRGBColorSpace lacks element 1, which the Arlington model requires; 2 arrays do, the first the array under /Resources/ColorSpace/CS0 of object 3 0, which holds 1 element.");
    }

    [Fact]
    public void Dictionaries_that_give_keys_they_leave_open_a_wrong_type_are_counted_together_whatever_the_key()
    {
        // ColorSpaceMap describes its keys with its wildcard: /CS0 in one and /CS1 in the other break the same row.
        var report = Validate(Document(Catalog, TwoPages, PageWith("/ColorSpace << /CS0 (a) >>"), PageWith("/ColorSpace << /CS1 (b) >>")));

        Single(report, PdfValidationRuleIds.ObjectValueTypeWrong).Message.Should().Be(
            "A ColorSpaceMap has /CS0 as a string, where the Arlington model wants an array or a name; 2 objects give a key it leaves open a type it does not allow, the first the dictionary under /Resources/ColorSpace of object 3 0.");
    }

    [Fact]
    public void Arrays_that_give_an_element_a_wrong_type_are_counted_by_element()
    {
        var report = Validate(Document(Catalog, TwoPages, PageWith("/ProcSet [1]"), PageWith("/ProcSet [/PDF 2]")));

        Single(report, PdfValidationRuleIds.ObjectValueTypeWrong).Message.Should().Be(
            "An ArrayOfNamesForProcSet has element 0 as an integer, where the Arlington model wants a name; 2 arrays give an element a type it does not allow, the first the array under /Resources/ProcSet of object 3 0.");
    }

    [Fact]
    public void Objects_that_give_a_type_a_value_the_model_does_not_list_are_counted()
    {
        var page = PageWith("/ExtGState << /GS0 << /Type /Font >> >>");

        var report = Validate(Document(Catalog, TwoPages, page, page));

        Single(report, PdfValidationRuleIds.ObjectTypeValueWrong).Message.Should().Be(
            "A GraphicsStateParameter has /Type /Font, where the Arlington model wants /ExtGState; 2 objects give /Type a value it does not list, the first the dictionary under /Resources/ExtGState/GS0 of object 3 0.");
    }

    [Fact]
    public void Objects_that_use_a_deprecated_key_are_counted_with_the_version_the_file_declares()
    {
        // The catalog declares PDF 2.0, which deprecates /ProcSet and the names its array lists.
        var page = PageWith("/ProcSet [/PDF]");

        var report = Validate(Document("<< /Type /Catalog /Pages 2 0 R /Version /2.0 >>", TwoPages, page, page));

        report.Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.ObjectKeyDeprecated)
            .Select(finding => finding.Message).Should().Equal(
            "A Resource has /ProcSet, which the Arlington model says PDF 2.0 deprecates, and the file declares PDF 2.0; 2 objects do, the first the dictionary under /Resources of object 3 0.",
            "An ArrayOfNamesForProcSet has element 0, which the Arlington model says PDF 2.0 deprecates, and the file declares PDF 2.0; 2 arrays do, the first the array under /Resources/ProcSet of object 3 0.");
    }

    [Fact]
    public void A_type_value_that_is_no_name_on_a_row_that_lists_none_is_still_written_in_words()
    {
        // The walk records a wrong /Type or /Subtype only when the value is a name, on a row that lists names: the
        // message writer, given a tally no file can produce, still writes a sentence rather than a blank.
        var tally = Tally(ArlingtonWalk.Rule.TypeValueWrong, "Count", PdfInteger.Create(3));

        ArlingtonText.Message(tally, "object 2 0", 17).Should().Be(
            "Object 2 0, a PageTreeNode in the Arlington model, has /Count a value, where the model wants another value.");
    }

    [Theory]
    [InlineData("Object 2 0", "Object 2 0, a PageTreeNode in the Arlington model, lacks /Kids, which the model requires.")]
    [InlineData("", ", a PageTreeNode in the Arlington model, lacks /Kids, which the model requires.")]
    public void A_location_already_capitalized_or_empty_is_written_as_it_is(string where, string expected)
    {
        // The walk writes locations from a lower-case word; the message writer capitalizes one, and leaves alone a
        // location that already starts with a capital, or that is empty.
        ArlingtonText.Message(Tally(ArlingtonWalk.Rule.KeyMissing, "Kids", null), where, 17).Should().Be(expected);
    }

    /// <summary>A tally of <paramref name="rule"/> on a page tree node's row for <paramref name="key"/>, met once, in object 2.</summary>
    private static ArlingtonWalk.Tally Tally(ArlingtonWalk.Rule rule, string key, PdfObject? detail)
    {
        ArlingtonModel.GetObject(ArlingtonModel.PageTreeNode).TryFindRow(key, out var row).Should().BeTrue();
        return new ArlingtonWalk.Tally(rule, row, new PdfObjectId(2), step: -1, PdfName.Get(key), element: -1, detail, length: -1);
    }

    /// <summary>A page of the tree whose root is object 2, with <paramref name="resources"/> in its resources.</summary>
    private static string PageWith(string resources) =>
        $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << {resources} >> >>";

    /// <summary>A PDF 1.7 file whose objects, numbered from 1, are <paramref name="bodies"/>, object 1 the catalog.</summary>
    private static byte[] Document(params string[] bodies)
    {
        var builder = new TestPdfBuilder();

        for (var index = 0; index < bodies.Length; index++)
        {
            builder.WithObject(index + 1, bodies[index]);
        }

        return builder.BuildClassic(rootNumber: 1);
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
