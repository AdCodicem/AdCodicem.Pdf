extern alias ArlingtonTool;

using System.Security.Cryptography;
using System.Text;
using AdCodicem.Pdf.Validation.Arlington;
using Tool = ArlingtonTool::AdCodicem.Pdf.Arlington;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The generator of the Arlington tables, <c>tools/AdCodicem.Pdf.Arlington</c>: the committed tables are exactly what
/// it makes of the vendored model and the overrides; the vendored model is the pinned one, byte for byte; every
/// override still overrides something; and what it cannot encode, it refuses rather than drops.
/// </summary>
public class ArlingtonGeneratorTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    private const string NoOverrides = "Object\tKey\tColumn\tValue\tRule\tReason\tEvidence\n";

    private static readonly string Header = string.Join('\t', Tool.Grammar.Columns);

    internal static Tool.Repository Repository { get; } = Tool.Repository.Find(AppContext.BaseDirectory);

    [Fact]
    public void Regenerating_the_tables_from_the_vendored_model_and_the_overrides_gives_the_committed_file_byte_for_byte()
    {
        var committed = File.ReadAllText(Repository.GeneratedPath).ReplaceLineEndings("\n");

        var generated = Repository.Generate();

        FirstDifference(committed, generated).Should().BeNull(
            "the committed tables are what the generator makes of the pinned model and the overrides: after changing " +
            "either, or the generator, run `dotnet run --project tools/AdCodicem.Pdf.Arlington -- generate`");
        generated.Should().Be(committed);
    }

    [Fact]
    public void Every_vendored_file_has_the_hash_the_lock_gives_it_and_the_lock_names_every_file_and_no_other()
    {
        Repository.Model.Verify().Should().BeEmpty("the vendored model is upstream's, byte for byte, at the pinned commit");
    }

    [Fact]
    public void The_lock_pins_the_commit_the_tables_name_and_vendors_the_whole_latest_model_with_its_license_and_notice()
    {
        var (commit, entries) = Tool.VendoredModel.ParseLock(File.ReadAllText(Repository.Model.LockPath));

        commit.Should().Be(ArlingtonModel.ModelCommit);
        entries.Select(static e => e.Path).Should().Contain(["LICENSE", "NOTICE.txt"]);
        entries.Count(static e => e.Path.StartsWith("tsv/latest/", StringComparison.Ordinal)).Should().Be(ArlingtonModel.ObjectCount);
        entries.Select(static e => e.Path).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void A_vendored_file_changed_added_or_removed_after_locking_is_caught()
    {
        var directory = Directory.CreateTempSubdirectory("arlington-lock-").FullName;

        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "tsv", "latest"));
            File.WriteAllText(Path.Combine(directory, "tsv", "latest", "Catalog.tsv"), "one\n");
            File.WriteAllText(Path.Combine(directory, "tsv", "latest", "Info.tsv"), "two\n");
            File.WriteAllText(Path.Combine(directory, "LICENSE"), "license\n");
            var model = new Tool.VendoredModel(directory);
            model.WriteLock(Commit);
            model.Verify().Should().BeEmpty();

            File.WriteAllText(Path.Combine(directory, "tsv", "latest", "Catalog.tsv"), "One\n");
            File.Delete(Path.Combine(directory, "tsv", "latest", "Info.tsv"));
            File.WriteAllText(Path.Combine(directory, "NOTICE.txt"), "notice\n");

            model.Verify().Should().BeEquivalentTo(
                "tsv/latest/Catalog.tsv does not have the SHA-256 the lock gives it.",
                "tsv/latest/Info.tsv is in the lock but not in the model's directory.",
                "NOTICE.txt is in the model's directory but not in the lock.");
            model.ReadCommit().Should().Be(Commit);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void The_lock_is_in_the_format_sha256sum_checks()
    {
        var lines = File.ReadAllLines(Repository.Model.LockPath);
        var license = lines.Single(static l => l.EndsWith("  LICENSE", StringComparison.Ordinal));

        license[..64].Should().Be(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Repository.Model.Directory, "LICENSE")))));
    }

    [Fact]
    public void Every_override_still_changes_the_pinned_model()
    {
        // The generator refuses an override the pinned model has made needless: an edit that leaves the row as the
        // model already has it, a silenced rule the row can no longer give, an inheritance for a key no longer required.
        var model = Repository.Model;

        var act = () => Tool.ArlingtonGenerator.Compile(model.ReadCommit(), model.ReadTsvFiles(), File.ReadAllText(Repository.OverridesPath));

        act.Should().NotThrow();
    }

    [Fact]
    public void The_overrides_apply_to_the_rows_they_name()
    {
        ArlingtonModel.TryFindObject("XObjectFormType1", out var form).Should().BeTrue();
        form.TryFindRow("Resources", out var resources).Should().BeTrue();
        resources.Requirement.Should().Be(ArlingtonRequirement.InVersions, "ISO 32000-1 does not require a form's /Resources (O1)");
        resources.RequiredFrom.Should().Be(20);
        resources.RequiredBefore.Should().Be(ArlingtonVersion.Unbounded);

        ModelRow("Resource", "Encoding").DeprecatedIn.Should().Be(ArlingtonVersion.None, "O2");
        ModelRow("FontDescriptorTrueType", "FontWeight").Types.Should().Be(ArlingtonTypes.Number, "O3");
        ModelRow("SignatureBuildDataDict", "V").DeprecatedIn.Should().Be(ArlingtonVersion.None, "O4");
        ModelRow("SignatureBuildDataAppDict", "REx").Types.Should().Be(ArlingtonTypes.Name | ArlingtonTypes.StringText, "O5");
        ModelRow("FieldTx", "DA").Overrides.Should().Be(ArlingtonOverride.InheritsAcroFormDefaultAppearance, "O6");
        ModelRow("FieldSig", "DA").Overrides.Should().Be(ArlingtonOverride.None, "the model does not require a signature field's /DA");
        ModelRow("ArrayOfOptContentOrders", "0").Requirement.Should().Be(ArlingtonRequirement.No, "O7");
        ModelRow("ArrayOfOptContentOrders", "*").Types.Should().Be(ArlingtonTypes.Dictionary, "a nested sub-array is no ISO 32000-1 order element");
        ModelRow("XObjectFormType1", "FormType").Requirement.Should().Be(ArlingtonRequirement.No, "ISO 32000-1 makes /FormType optional in every version (O8)");
        ModelRow("XObjectFormType1", "Matrix").Requirement.Should().Be(ArlingtonRequirement.No, "O8");
        ModelRow("XObjectFormType1", "Name").Requirement.Should().Be(ArlingtonRequirement.InVersions, "O8");
        ModelRow("XObjectFormType1", "Name").RequiredFrom.Should().Be(10, "ISO 32000-1 requires /Name in PDF 1.0 only");
        ModelRow("XObjectFormType1", "Name").RequiredBefore.Should().Be(11);
        ModelRow("FontType3", "Encoding").Types.Should().Be(ArlingtonTypes.Dictionary, "ISO 32000-1 requires \"an encoding dictionary\" of a Type 3 font: no override");
        ModelRow("OptContentCreatorInfo", "SubType").Types.Should().Be(ArlingtonTypes.Name | ArlingtonTypes.StringText, "O9");
        ModelRow("OptContentCreatorInfo", "Subtype").Requirement.Should().Be(ArlingtonRequirement.Yes, "the key the misspelling stands for stays required");

        const ArlingtonOverride pageTree = ArlingtonOverride.SilentWherePageTreeJudges;
        ModelRow("PageObject", "MediaBox").Overrides.Should().Be(ArlingtonOverride.KeyMissingSilent | ArlingtonOverride.ValueTypeWrongSilent | pageTree);
        ModelRow("PageObject", "MediaBox").Requirement.Should().Be(ArlingtonRequirement.Yes, "a silenced rule leaves the row as the model has it, for typing");
        ModelRow("PageObject", "Resources").Overrides.Should().Be(ArlingtonOverride.KeyMissingSilent | pageTree);
        ModelRow("PageTreeNodeRoot", "Count").Overrides.Should().Be(ArlingtonOverride.KeyMissingSilent | pageTree);
        ModelRow("ArrayOfPageTreeNodeKids", "*").Overrides.Should().Be(ArlingtonOverride.ValueTypeWrongSilent | pageTree);
        ModelRow("PageObject", "Type").Overrides.Should().Be(ArlingtonOverride.None, "a page's wrong /Type is the generated rules' to report");
        ModelRow("Catalog", "Type").Overrides.Should().Be(ArlingtonOverride.None, "a catalog without /Type is the generated rules' to report");
    }

    [Fact]
    public void Generating_is_a_function_of_the_model_and_the_overrides_whatever_order_the_files_come_in()
    {
        var files = Sample();
        var reversed = Enumerable.Reverse(files).ToList();

        Tool.ArlingtonGenerator.Generate(Commit, reversed, NoOverrides).Should().Be(Tool.ArlingtonGenerator.Generate(Commit, files, NoOverrides));
    }

    [Fact]
    public void A_content_override_edits_the_cell_before_the_tables_are_built_and_marks_the_row_in_the_generated_text()
    {
        var overrides = NoOverrides + "Info\tTitle\tType\tname;string-text\tobject.value-type-wrong\tbecause\tsomewhere\n";

        var model = Tool.ArlingtonGenerator.Compile(Commit, Sample(), overrides);
        var text = Tool.ArlingtonGenerator.Generate(Commit, Sample(), overrides);

        var title = model.Objects.Single(static o => o.Name == "Info").Rows.Single(static r => r.Key == "Title");
        ((uint)title.Types).Should().Be((uint)(ArlingtonTypes.Name | ArlingtonTypes.StringText));
        title.Edited.Should().BeTrue();
        text.Should().Contain("/* Info/Title, edited */");
    }

    [Fact]
    public void An_edit_that_leaves_the_row_as_the_model_has_it_is_stale_and_refused()
    {
        var overrides = NoOverrides + "Info\tTitle\tRequired\tFALSE\tobject.key-missing\tbecause\tsomewhere\n";

        Refused(Sample(), overrides, "overrides.tsv, line 2: Info/Title already has the Required the override gives it: the override is stale and must go.");
    }

    [Fact]
    public void An_edit_of_several_objects_is_stale_as_soon_as_one_of_them_has_the_value()
    {
        var overrides = NoOverrides + "Catalog,Info\tType\tRequired\tTRUE\tobject.key-missing\tbecause\tsomewhere\n";
        var files = Sample(Tsv("Info", Row("Type", "name", values: "[Info]")));

        Refused(files, overrides, "overrides.tsv, line 2: Catalog/Type already has the Required the override gives it: the override is stale and must go.");
    }

    [Fact]
    public void Edits_of_one_row_that_need_each_other_are_not_stale()
    {
        // A second type needs its own Link list: the Link edit alone would leave the row unreadable, and the Type
        // edit alone would too, so neither is judged against the model without the other.
        var overrides = NoOverrides +
            "Catalog\tInfo\tType\tdictionary;name\tobject.value-type-wrong\tbecause\te\n" +
            "Catalog\tInfo\tLink\t[Info];[]\tobject.value-type-wrong\tbecause\te\n";

        var model = Tool.ArlingtonGenerator.Compile(Commit, Sample(), overrides);

        var info = model.Objects.Single(static o => o.Name == "Catalog").Rows.Single(static r => r.Key == "Info");
        ((uint)info.Types).Should().Be((uint)(ArlingtonTypes.Dictionary | ArlingtonTypes.Name));
        info.Edited.Should().BeTrue();
    }

    [Fact]
    public void An_edit_a_row_needs_only_for_another_edit_is_stale_once_the_model_has_both()
    {
        var files = Sample(Tsv("Info", Row("Mode", "dictionary;name", link: "[Catalog];[]")));
        var overrides = NoOverrides +
            "Info\tMode\tType\tdictionary;name\tobject.value-type-wrong\tbecause\te\n" +
            "Info\tMode\tLink\t[Catalog];[]\tobject.value-type-wrong\tbecause\te\n";

        Refused(files, overrides, "overrides.tsv, line 2: Info/Mode already has the Type the override gives it: the override is stale and must go.");
    }

    [Fact]
    public void Silencing_a_rule_the_row_cannot_give_is_stale_and_refused()
    {
        var overrides = NoOverrides + "Info\tTitle\tFindings\tnone\tobject.key-missing\tcovered by page-tree.kids-missing\tsomewhere\n";

        Refused(Sample(), overrides, "overrides.tsv, line 2: Info/Title cannot give object.key-missing as the model stands, so the override has nothing to override and must go.");
    }

    [Theory]
    [InlineData("Catalog\tType\tFindings\tnone\tobject.key-deprecated\tcovered by file.root-invalid\te\n", "*Catalog/Type cannot give object.key-deprecated*")]
    [InlineData("Info\tMode\tFindings\tnone\tobject.type-value-wrong\tcovered by file.root-invalid\te\n", "*Info/Mode cannot give object.type-value-wrong*")]
    [InlineData("Info\tTitle\tInheritance\tAcroForm/DA\tobject.key-missing\tbecause\te\n", "*Info/Title cannot give object.key-missing*")]
    public void An_override_with_nothing_left_to_override_is_refused(string line, string message)
    {
        Refused(Sample(), NoOverrides + line, message);
    }

    [Fact]
    public void A_silenced_rule_and_an_inheritance_are_carried_as_data_for_the_walk()
    {
        var overrides = NoOverrides +
            "Catalog\tType\tFindings\tnone\tobject.key-missing,object.type-value-wrong\tcovered by file.root-invalid, for the test\te\n" +
            "Info\tDA\tInheritance\tAcroForm/DA\tobject.key-missing\tbecause\te\n";
        var files = Sample(Tsv("Info", Row("DA", "string-byte", required: "TRUE", inheritable: "TRUE")));

        var model = Tool.ArlingtonGenerator.Compile(Commit, files, overrides);

        var catalog = model.Objects.Single(static o => o.Name == "Catalog").Rows.Single(static r => r.Key == "Type");
        ((byte)catalog.Overrides).Should().Be(
            (byte)(ArlingtonOverride.KeyMissingSilent | ArlingtonOverride.TypeValueWrongSilent),
            "a rule of another family than the page tree's covers the key wherever the walk meets it");
        ((byte)catalog.Requirement).Should().Be((byte)ArlingtonRequirement.Yes, "a silenced rule leaves the row as the model has it");
        ((byte)model.Objects.Single(static o => o.Name == "Info").Rows.Single().Overrides).Should().Be((byte)ArlingtonOverride.InheritsAcroFormDefaultAppearance);
    }

    [Fact]
    public void A_silence_a_page_tree_rule_covers_holds_where_the_page_tree_judges()
    {
        var overrides = NoOverrides +
            "Catalog\tType\tFindings\tnone\tobject.key-missing\tcovered by page-tree.kids-missing, for the test\te\n" +
            "Catalog\tType\tFindings\tnone\tobject.type-value-wrong\tcovered by page-tree.kid-invalid, for the test\te\n";

        var model = Tool.ArlingtonGenerator.Compile(Commit, Sample(), overrides);

        var catalog = model.Objects.Single(static o => o.Name == "Catalog").Rows.Single(static r => r.Key == "Type");
        ((byte)catalog.Overrides).Should().Be(
            (byte)(ArlingtonOverride.KeyMissingSilent | ArlingtonOverride.TypeValueWrongSilent | ArlingtonOverride.SilentWherePageTreeJudges),
            "two silences of one key may share the condition");
    }

    [Fact]
    public void An_object_with_more_rows_than_a_64_bit_mask_holds_is_refused()
    {
        var rows = Enumerable.Range(0, 65).Select(static i => Row($"K{i}", "integer")).ToArray();

        Refused(Sample(Tsv("Info", rows)), NoOverrides, "Info has 65 rows; the walk marks the keys of an object in one 64-bit mask, so the tables hold 64 at most.");
    }

    [Theory]
    [InlineData("Type", "text", "Info.tsv, line 2 (Title), column Type: 'text' is not one of the model's 18 types.")]
    [InlineData("Type", "string-text;name", "*column Type: 'string-text;name' does not list its types once each, in the model's order.")]
    [InlineData("Type", "fn:Future(2.1,name)", "*column Type: 'fn:Future(2.1,name)' is not one of the model's 18 types.")]
    [InlineData("SinceVersion", "fn:Eval(fn:Extension(X,1.3) && 1.5)", "*column SinceVersion: 'fn:Eval(fn:Extension(X,1.3) && 1.5)' is not a SinceVersion the tables can reduce.")]
    [InlineData("SinceVersion", "1.8", "*column SinceVersion: '1.8' is not a SinceVersion the tables can reduce.")]
    [InlineData("DeprecatedIn", "2.1", "*column DeprecatedIn: '2.1' is not a PDF version the tables can hold (1.0 to 1.7, or 2.0).")]
    [InlineData("Required", "MAYBE", "*column Required: 'MAYBE' is neither TRUE, FALSE nor fn:IsRequired(...).")]
    [InlineData("Required", "fn:IsRequired(fn:IsPresent(Mode)", "*column Required: 'fn:IsRequired(fn:IsPresent(Mode)' is neither TRUE, FALSE nor fn:IsRequired(...).")]
    [InlineData("Inheritable", "YES", "*column Inheritable: 'YES' is neither TRUE nor FALSE.")]
    [InlineData("Link", "[Info];[Info]", "*column Link: '[Info];[Info]' gives 2 lists where the row's Type has 1.")]
    [InlineData("Link", "Info", "*column Link: 'Info' is not a bracketed list.")]
    [InlineData("Link", "[fn:Eval(Info)]", "*column Link: 'fn:Eval(Info)' does not name an object.")]
    [InlineData("Link", "[Info", "*column Link: '[Info' leaves a bracket or a quote open.")]
    [InlineData("PossibleValues", "[a,,b]", "*column PossibleValues: '[a,,b]' holds an empty value.")]
    [InlineData("PossibleValues", "[a];[b]", "*column PossibleValues: '[a];[b]' gives 2 lists where the row's Type has 1.")]
    public void A_cell_the_grammar_does_not_recognize_is_refused_naming_its_file_line_and_column(string column, string cell, string message)
    {
        var row = Row("Title", "string-text").Split('\t');
        row[Array.IndexOf(Tool.Grammar.Columns.ToArray(), column)] = cell;

        Refused(Sample(Tsv("Info", string.Join('\t', row))), NoOverrides, message);
    }

    [Fact]
    public void A_link_to_an_object_the_model_lacks_is_refused()
    {
        Refused(Sample(Tsv("Info", Row("Next", "dictionary", link: "[Missing]"))), NoOverrides, "Info.tsv, line 2 (Next): Link names Missing, which the model does not have.");
    }

    [Fact]
    public void A_link_for_a_type_that_holds_no_object_is_refused()
    {
        Refused(Sample(Tsv("Info", Row("Count", "integer", link: "[Info]"))), NoOverrides, "Info.tsv, line 2 (Count): Link names objects for the type integer, whose values are never objects.");
    }

    [Fact]
    public void An_object_linked_both_as_an_array_and_as_a_dictionary_is_refused()
    {
        Refused(Sample(Tsv("Info", Row("List", "array", link: "[ArrayOfItems]"), Row("Other", "dictionary", link: "[ArrayOfItems]"))), NoOverrides, "ArrayOfItems is linked both as an array and as a dictionary or stream.");
    }

    [Theory]
    [InlineData(new[] { "1", "0" }, "*an array's fixed elements are numbered 0, 1, 2... in order.")]
    [InlineData(new[] { "0", "2*" }, "*an array's repeating group follows its fixed elements, numbered on from them.")]
    [InlineData(new[] { "0*", "*" }, "*an array has a repeating group or a wildcard, not both.")]
    [InlineData(new[] { "*", "0" }, "*'0' is not an element of an array.")]
    [InlineData(new[] { "0", "Size" }, "*'Size' is not an element of an array.")]
    public void An_array_whose_rows_are_not_its_elements_in_order_is_refused(string[] keys, string message)
    {
        Refused(Sample(Tsv("ArrayOfItems", [.. keys.Select(static k => Row(k, "integer"))])), NoOverrides, message);
    }

    [Theory]
    [InlineData("0*", "*a repeating group belongs to an array, and Info is not one.")]
    [InlineData("A*", "*'A*' is not a key the tables can hold.")]
    [InlineData("A B", "*'A B' is not a key the tables can hold.")]
    public void A_dictionary_key_the_tables_cannot_hold_is_refused(string key, string message)
    {
        Refused(Sample(Tsv("Info", Row(key, "integer"))), NoOverrides, message);
    }

    [Fact]
    public void A_key_given_twice_is_refused()
    {
        Refused(Sample(Tsv("Info", Row("Title", "integer"), Row("Title", "name"))), NoOverrides, "Info.tsv, line 3 (Title): the key is given twice.");
    }

    [Theory]
    [InlineData("Key\tType\n", "Info.tsv does not start with the model's twelve columns.")]
    [InlineData("{header}\nTitle\tname\n", "Info.tsv, line 2: 2 cells where the model has 12.")]
    [InlineData("{header}\n{row}", "Info.tsv does not end with a line feed.")]
    [InlineData("{header}\r\n{row}\r\n", "Info.tsv holds a character other than printable ASCII, tabs and line feeds.")]
    [InlineData("{header}\n{row}é\n", "Info.tsv holds a character other than printable ASCII, tabs and line feeds.")]
    [InlineData("{header}\n", "Info.tsv has no row.")]
    public void A_file_that_is_not_a_table_of_the_model_s_columns_is_refused(string text, string message)
    {
        var file = new Tool.SourceFile("Info", text.Replace("{header}", Header, StringComparison.Ordinal).Replace("{row}", Row("Title", "name"), StringComparison.Ordinal));

        Refused(Sample(file), NoOverrides, message);
    }

    [Fact]
    public void A_commit_that_is_not_a_full_hash_is_refused()
    {
        var act = () => Tool.ArlingtonGenerator.Generate("c48b363", Sample(), NoOverrides);

        act.Should().Throw<InvalidDataException>().WithMessage("'c48b363' is not a full commit hash of the model.");
    }

    [Theory]
    [InlineData("Nothing\tTitle\tType\tname\tobject.value-type-wrong\tr\te\n", "overrides.tsv, line 2: the model has no object Nothing.")]
    [InlineData("Info\tNothing\tType\tname\tobject.value-type-wrong\tr\te\n", "overrides.tsv, line 2: Info has no key Nothing.")]
    [InlineData("Info\tTitle\tNote\tx\tobject.value-type-wrong\tr\te\n", "overrides.tsv, line 2: 'Note' is neither a column of the model the tables are built from, Findings, nor Inheritance.")]
    [InlineData("Info\tTitle\tType\tname\tobject.key-forbidden\tr\te\n", "overrides.tsv, line 2: 'object.key-forbidden' is not one of the generated rules.")]
    [InlineData("Info\tTitle\tType\tname\tobject.value-type-wrong\t\te\n", "overrides.tsv, line 2: an override states its reason and its evidence.")]
    [InlineData("Info\tTitle\tType\tname\tobject.value-type-wrong\tr\n", "overrides.tsv, line 2: 6 cells where the file has 7.")]
    [InlineData("Info\tTitle\tFindings\tsilent\tobject.value-type-wrong\tcovered by page-tree.kids-missing\te\n", "overrides.tsv, line 2: the value of Findings is 'none'.")]
    [InlineData("Info\tTitle\tFindings\tnone\tobject.value-type-wrong\tthe walk sees it elsewhere\te\n", "overrides.tsv, line 2: silencing a generated rule needs a reason that reads 'covered by <the hand-written rule>'.")]
    [InlineData("Info\tTitle\tFindings\tnone\tobject.value-type-wrong\tcovered by object.key-missing\te\n", "overrides.tsv, line 2: silencing a generated rule needs a reason that reads 'covered by <the hand-written rule>'.")]
    [InlineData("Info\tTitle\tInheritance\tParent/DA\tobject.key-missing\tr\te\n", "overrides.tsv, line 2: the only source of inheritance is 'AcroForm/DA', for object.key-missing.")]
    [InlineData("Info,Info\tTitle\tType\tname\tobject.value-type-wrong\tr\te\n", "overrides.tsv, line 2: the Object column lists an empty or a repeated name.")]
    [InlineData("Info\tTitle\tType\tname\tobject.value-type-wrong\tr\te\nInfo\tTitle\tType\tinteger\tobject.value-type-wrong\tr\te\n", "overrides.tsv, line 3: Info/Title has its Type edited twice.")]
    [InlineData("Info\tTitle\tType\tlist\tobject.value-type-wrong\tr\te\n", "Info.tsv, line 2 (Title), column Type as overrides.tsv, line 2 edits it: 'list' is not one of the model's 18 types.")]
    public void An_override_the_generator_cannot_apply_is_refused(string lines, string message)
    {
        Refused(Sample(), NoOverrides + lines, message);
    }

    [Theory]
    [InlineData("", "overrides.tsv has no header.")]
    [InlineData("Object Key Column Value Rule Reason Evidence\n", "overrides.tsv, line 1: the header must be 'Object Key Column Value Rule Reason Evidence', tab-separated.")]
    [InlineData("# a comment\r\n", "overrides.tsv holds a carriage return: lines end with a line feed alone.")]
    public void An_overrides_file_without_its_header_is_refused(string text, string message)
    {
        Refused(Sample(), text, message);
    }

    [Fact]
    public void Comment_lines_and_blank_lines_of_the_overrides_are_skipped()
    {
        var overrides = "# why\n\n" + NoOverrides + "# a dropped override, and why\n\nInfo\tTitle\tType\tname\tobject.value-type-wrong\tr\te\n";

        var entries = Tool.OverrideFile.Parse(overrides);

        entries.Should().ContainSingle().Which.Line.Should().Be(6);
    }

    /// <summary>A small model: a catalog, an array it links, and a dictionary with a few kinds of rows.</summary>
    internal static List<Tool.SourceFile> Sample(params Tool.SourceFile[] replacements)
    {
        List<Tool.SourceFile> files =
        [
            Tsv(
                "Catalog",
                Row("Items", "array", link: "[ArrayOfItems]"),
                Row("Info", "dictionary", link: "[Info]"),
                Row("Type", "name", required: "TRUE", values: "[Catalog]")),
            Tsv("ArrayOfItems", Row("*", "integer")),
            Tsv(
                "Info",
                Row("Title", "string-text"),
                Row("Mode", "name", values: "[A,B]"),
                Row("Old", "integer", deprecated: "1.3"),
                Row("Type", "name", required: "TRUE", values: "[Info]")),
        ];

        foreach (var replacement in replacements)
        {
            var index = files.FindIndex(f => f.Name == replacement.Name);

            if (index < 0)
            {
                files.Add(replacement);
            }
            else
            {
                files[index] = replacement;
            }
        }

        return files;
    }

    /// <summary>A TSV file of the model: the header, then one line per row.</summary>
    internal static Tool.SourceFile Tsv(string name, params string[] rows)
    {
        var text = new StringBuilder(Header).Append('\n');

        foreach (var row in rows)
        {
            text.Append(row).Append('\n');
        }

        return new Tool.SourceFile(name, text.ToString());
    }

    /// <summary>A row of the model, its twelve cells tab-separated.</summary>
    internal static string Row(
        string key,
        string type,
        string since = "1.0",
        string deprecated = "",
        string required = "FALSE",
        string inheritable = "FALSE",
        string values = "",
        string link = "") =>
        string.Join('\t', key, type, since, deprecated, required, "FALSE", inheritable, string.Empty, values, string.Empty, link, string.Empty);

    private static ArlingtonRow ModelRow(string objectName, string key)
    {
        ArlingtonModel.TryFindObject(objectName, out var found).Should().BeTrue();
        found.TryFindRow(key, out var row).Should().BeTrue();
        return row;
    }

    private static void Refused(List<Tool.SourceFile> files, string overrides, string message)
    {
        var act = () => Tool.ArlingtonGenerator.Generate(Commit, files, overrides);

        act.Should().Throw<InvalidDataException>().WithMessage(message);
    }

    private static string? FirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');

        for (var i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
        {
            var e = i < expectedLines.Length ? expectedLines[i] : "(end of file)";
            var a = i < actualLines.Length ? actualLines[i] : "(end of file)";

            if (e != a)
            {
                return $"line {i + 1}: committed '{e}', generated '{a}'";
            }
        }

        return null;
    }
}
