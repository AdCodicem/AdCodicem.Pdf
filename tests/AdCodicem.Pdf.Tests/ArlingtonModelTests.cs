extern alias ArlingtonTool;

using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation.Arlington;
using FsCheck;
using FsCheck.Fluent;
using Tool = ArlingtonTool::AdCodicem.Pdf.Arlington;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The Arlington tables compiled into the core, read through their accessors: every row of the model decodes as the
/// generator reduced it from the TSV files, lookups go by ASCII name without allocating, and a few rows read as the
/// model writes them.
/// </summary>
public class ArlingtonModelTests
{
    private static readonly Lazy<Tool.CompiledModel> Compiled = new(static () =>
    {
        var repository = ArlingtonGeneratorTests.Repository;
        var model = repository.Model;
        return Tool.ArlingtonGenerator.Compile(model.ReadCommit(), model.ReadTsvFiles(), File.ReadAllText(repository.OverridesPath));
    });

    [Fact]
    public void Every_row_of_the_model_decodes_from_the_tables_as_the_generator_reduced_it_from_the_tsv_files()
    {
        var compiled = Compiled.Value;
        var problems = new List<string>();
        var rows = 0;
        ArlingtonModel.ObjectCount.Should().Be(compiled.Objects.Count);

        for (var i = 0; i < compiled.Objects.Count; i++)
        {
            var expected = compiled.Objects[i];
            var obj = ArlingtonModel.GetObject(i);
            Expect(problems, expected.Name, "name", obj.ToString(), expected.Name);
            Expect(problems, expected.Name, "kind", obj.IsArray, expected.IsArray);
            Expect(problems, expected.Name, "rows", obj.RowCount, expected.Rows.Count);
            ArlingtonModel.TryFindObject(expected.Name, out var found).Should().BeTrue();
            found.Should().Be(obj);

            for (var j = 0; j < Math.Min(obj.RowCount, expected.Rows.Count); j++)
            {
                CompareRow(problems, obj, obj.GetRow(j), expected.Rows[j], compiled);
                rows++;
            }
        }

        problems.Should().BeEmpty();
        ArlingtonModel.RowCount.Should().Be(rows);
        ArlingtonModel.CandidateSetCount.Should().Be(compiled.CandidateSets.Count);
    }

    [Fact]
    public void Any_row_the_grammar_allows_reduces_to_what_its_cells_say()
    {
        // The other half of the round trip: from generated rows, written in the model's grammar, to what the generator
        // reduces them to. The committed tables are then decoded exhaustively by the test above.
        var types = Gen.Choose(1, (1 << 18) - 1).Select(static mask =>
            Enumerable.Range(0, 18).Where(bit => (mask & (1 << bit)) != 0).Select(static bit => (ArlingtonType)bit).ToList());
        var version = Gen.Elements("1.0", "1.1", "1.2", "1.3", "1.4", "1.5", "1.6", "1.7", "2.0");
        var since = Gen.OneOf(
            version,
            version.Select(static v => $"fn:Eval(fn:Extension(ADBE_Extn3,1.7) || {v})"),
            Gen.Constant("fn:Extension(ISO_19005_3)"));
        var deprecated = Gen.OneOf(Gen.Constant(string.Empty), version);
        var required = Gen.OneOf(
            Gen.Elements("TRUE", "FALSE", "fn:IsRequired(@Subtype==Form)"),
            version.Select(static v => $"fn:IsRequired(fn:IsPDFVersion({v}))"),
            version.Select(static v => $"fn:IsRequired(fn:BeforeVersion({v}))"),
            version.Select(static v => $"fn:IsRequired(fn:SinceVersion({v}))"));
        var arrayCandidates = Gen.Elements("[ArrayOfItems]", "[fn:IsPDFVersion(1.2,ArrayOfItems)]", "[]");
        var otherCandidates = Gen.Elements("[Catalog]", "[Info,Catalog,Info]", "[fn:SinceVersion(1.5,fn:Extension(ADBE_Extn3,Info))]", "[]");
        var rows =
            from t in types
            from s in since
            from d in deprecated
            from r in required
            from inheritable in Gen.Elements("TRUE", "FALSE")
            from arrayLinks in Gen.ArrayOf(arrayCandidates, t.Count)
            from otherLinks in Gen.ArrayOf(otherCandidates, t.Count)
            from wrapped in Gen.Elements(true, false)
            select (Types: t, Since: s, Deprecated: d, Required: r, Inheritable: inheritable,
                Links: t.Select((type, i) => type == ArlingtonType.Array ? arrayLinks[i] : otherLinks[i]).ToList(), Wrapped: wrapped);

        Check.One(PropertySettings, Prop.ForAll(rows.ToArbitrary(), row =>
        {
            var typeCell = string.Join(';', row.Types.Select((t, i) =>
                row.Wrapped && i == 0 ? $"fn:SinceVersion(1.5,{ArlingtonTypeNames.Of(t)})" : ArlingtonTypeNames.Of(t)));
            var links = row.Types.Select((t, i) => ((ArlingtonTypes)(1u << (int)t) & ArlingtonTypes.Linkable) != 0 ? row.Links[i] : "[]").ToList();
            var linkCell = links.All(static l => l == "[]") ? string.Empty : string.Join(';', links);
            var nameSlot = row.Types.IndexOf(ArlingtonType.Name);
            var valueCell = nameSlot < 0
                ? string.Empty
                : string.Join(';', row.Types.Select((_, i) => i == nameSlot ? "[fn:Deprecated(1.4,Old),New]" : "[]"));
            var cells = string.Join('\t', "Key", typeCell, row.Since, row.Deprecated, row.Required, "FALSE", row.Inheritable, string.Empty, valueCell, string.Empty, linkCell, string.Empty);
            var files = ArlingtonGeneratorTests.Sample(ArlingtonGeneratorTests.Tsv("Info", cells));

            var reduced = Tool.ArlingtonGenerator.Compile("0123456789abcdef0123456789abcdef01234567", files, "Object\tKey\tColumn\tValue\tRule\tReason\tEvidence\n")
                .Objects.Single(static o => o.Name == "Info").Rows.Single();

            var expectedTypes = row.Types.Aggregate(ArlingtonTypes.None, static (mask, t) => mask | (ArlingtonTypes)(1u << (int)t));
            var expectedLinks = row.Types.Select((t, i) => (Type: t, Candidates: Candidates(links[i]))).Where(static l => l.Candidates.Count > 0).ToList();
            var (requirement, from, before) = ExpectedRequirement(row.Required);

            return (uint)reduced.Types == (uint)expectedTypes
                && reduced.Since == ExpectedSince(row.Since)
                && reduced.Deprecated == (row.Deprecated.Length == 0 ? ArlingtonVersion.None : Code(row.Deprecated))
                && (byte)reduced.Requirement == (byte)requirement
                && reduced.RequiredFrom == from
                && reduced.RequiredBefore == before
                && ((byte)reduced.Flags & (byte)ArlingtonRowFlags.Inheritable) != 0 == (row.Inheritable == "TRUE")
                && reduced.Links.Select(static l => ((ArlingtonType)(byte)l.Type, string.Join(',', l.Candidates))).SequenceEqual(expectedLinks.Select(static l => (l.Type, string.Join(',', l.Candidates))))
                && reduced.Values.Select(static v => ((ArlingtonType)(byte)v.Type, v.Text)).SequenceEqual(nameSlot < 0 ? [] : [(ArlingtonType.Name, "New"), (ArlingtonType.Name, "Old")]);
        }));
    }

    [Fact]
    public void A_page_s_media_box_reads_as_the_model_writes_it()
    {
        var row = ModelRow("PageObject", "MediaBox");

        row.Types.Should().Be(ArlingtonTypes.Rectangle);
        row.Since.Should().Be(10);
        row.DeprecatedIn.Should().Be(ArlingtonVersion.None);
        row.Requirement.Should().Be(ArlingtonRequirement.Yes);
        row.IsInheritable.Should().BeTrue();
        row.IsUnconditionallyRequired.Should().BeTrue();
        row.IsRequiredIn(10).Should().BeTrue();
        row.LinkCount.Should().Be(0);
        row.ValueCount.Should().Be(0);
        row.ToString().Should().Be("PageObject/MediaBox");
        row.Object.GetRow(row.Position).Should().Be(row);
        row.Index.Should().Be(row.Object.GetRow(0).Index + row.Position);
        row.Object.FixedElementCount.Should().Be(0, "a dictionary has no elements");
        row.Object.RepeatingCount.Should().Be(0);
        ArlingtonModel.ExtrasOf(row.Index + 1).IsEmpty.Should().BeTrue("PageObject/Metadata, the row after it, has neither a version range nor an override");
    }

    [Fact]
    public void A_node_s_parent_links_two_candidates_no_plain_value_tells_apart_and_is_a_back_link()
    {
        var row = ModelRow("PageTreeNode", "Parent");

        row.IsBackLink.Should().BeTrue();
        row.TryGetLink(ArlingtonType.Dictionary, out var link).Should().BeTrue();
        link.IsCandidateSet.Should().BeTrue();
        link.CandidateCount.Should().Be(2);
        link.GetCandidate(0).ToString().Should().Be("PageTreeNode");
        link.GetCandidate(1).ToString().Should().Be("PageTreeNodeRoot");
        link.TryGetPlan(arrays: false, out _).Should().BeFalse("both are /Type /Pages: only the root's missing /Parent tells them apart");
        row.TryGetLink(ArlingtonType.Array, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("ArrayOfPageTreeNodeKids", "*", false, "Type")]
    [InlineData("ArrayOfAnnots", "*", false, "Subtype")]
    [InlineData("Catalog", "OpenAction", true, "1")]
    [InlineData("ShadingMap", "*", false, "ShadingType")]
    public void A_candidate_set_carries_the_key_that_tells_its_candidates_apart(string objectName, string key, bool arrays, string plan)
    {
        var row = ModelRow(objectName, key);
        var link = Enumerable.Range(0, row.LinkCount).Select(row.GetLink).First(l => l.IsCandidateSet && l.TryGetPlan(arrays, out _));

        link.TryGetPlan(arrays, out var name).Should().BeTrue();
        name.ToString().Should().Be(plan);
    }

    [Fact]
    public void A_catalog_s_type_has_its_one_plain_value_and_weighs_as_a_discriminator()
    {
        var row = ModelRow("Catalog", "Type");

        row.IsDiscriminator.Should().BeTrue();
        row.ValueCount.Should().Be(1);
        row.GetValue(0).Type.Should().Be(ArlingtonType.Name);
        row.GetValue(0).Text.ToString().Should().Be("Catalog");
        row.HasValuesFor(ArlingtonType.Name).Should().BeTrue();
        row.HasValuesFor(ArlingtonType.Integer).Should().BeFalse();
        row.HasValue(ArlingtonType.Name, "Catalog").Should().BeTrue();
        row.HasValue(ArlingtonType.Name, "catalog").Should().BeFalse("names compare ordinally");
        row.HasValueOfAnyType("Pages").Should().BeFalse();
    }

    [Fact]
    public void A_file_s_value_is_a_plain_value_when_its_token_is_one_as_the_model_writes_it()
    {
        var catalog = ModelRow("Catalog", "Type");
        // XObjectFormType1's /FormType was required before PDF 1.3 too, until O8 overrode it after ISO 32000-1.
        var formType = ModelRow("XObjectFormPSpassthrough", "FormType");
        var functionType = ModelRow("FunctionType2", "FunctionType");

        catalog.HasValue(ArlingtonType.Name, PdfName.Catalog).Should().BeTrue();
        catalog.HasValue(ArlingtonType.Name, PdfName.Pages).Should().BeFalse();
        catalog.HasValueOfAnyType(PdfName.Catalog).Should().BeTrue();
        formType.HasValue(ArlingtonType.Integer, PdfInteger.Create(1)).Should().BeTrue();
        formType.HasValue(ArlingtonType.Integer, PdfInteger.Create(-1)).Should().BeFalse();
        formType.HasValue(ArlingtonType.Integer, new PdfReal(1)).Should().BeFalse("a real is no token of the model's");
        formType.HasValue(ArlingtonType.Integer, PdfInteger.Create(long.MinValue)).Should().BeFalse();
        functionType.HasValueOfAnyType(PdfInteger.Create(2)).Should().BeTrue();
        functionType.HasValueOfAnyType(PdfInteger.Create(3)).Should().BeFalse();
        functionType.HasValueOfAnyType(PdfString.FromText("2")).Should().BeFalse();
        functionType.HasValue(ArlingtonType.Boolean, PdfBoolean.True).Should().BeFalse("the row has no boolean values");
        functionType.HasValueOfAnyType(PdfBoolean.False).Should().BeFalse();
    }

    [Fact]
    public void An_array_s_rows_are_its_fixed_elements_then_its_repeating_group()
    {
        ArlingtonModel.TryFindObject("ArrayOfOPI2Inks", out var inks).Should().BeTrue();

        inks.IsArray.Should().BeTrue();
        inks.FixedElementCount.Should().Be(1);
        inks.RepeatingCount.Should().Be(2);
        inks.GetRow(0).ElementIndex.Should().Be(0);
        inks.GetRow(1).IsRepeating.Should().BeTrue();
        inks.GetRow(1).ElementIndex.Should().Be(1);
        inks.GetRow(2).Key.ToString().Should().Be("2*");
        inks.GetRow(2).IsRequiredIn(20).Should().BeFalse("no single element stands for a repeating group's member");
        inks.TryGetWildcard(out _).Should().BeFalse();

        ArlingtonModel.TryFindObject("ArrayOfPageTreeNodeKids", out var kids).Should().BeTrue();
        kids.TryGetWildcard(out var wildcard).Should().BeTrue();
        wildcard.IsWildcard.Should().BeTrue();
        wildcard.ElementIndex.Should().Be(-1);
        kids.TryFindRowOrWildcard("7", out var any).Should().BeTrue();
        any.Should().Be(wildcard);

        ArlingtonModel.TryFindObject("FontMap", out var fonts).Should().BeTrue();
        fonts.IsArray.Should().BeFalse("a map is a dictionary whose only row is the wildcard");
        fonts.TryFindRow("F1", out _).Should().BeFalse();
        fonts.TryFindRowOrWildcard("F1", out var font).Should().BeTrue();
        font.IsWildcard.Should().BeTrue();
    }

    [Fact]
    public void A_requirement_on_the_version_alone_holds_in_its_range_only()
    {
        var name = ModelRow("FontType1", "Name");
        name.Requirement.Should().Be(ArlingtonRequirement.InVersions);
        name.IsRequiredIn(10).Should().BeTrue("fn:IsPDFVersion(1.0)");
        name.IsRequiredIn(11).Should().BeFalse();

        // XObjectFormType1's /FormType was required before PDF 1.3 too, until O8 overrode it after ISO 32000-1.
        var formType = ModelRow("XObjectFormPSpassthrough", "FormType");
        formType.RequiredFrom.Should().Be(ArlingtonVersion.None);
        formType.RequiredBefore.Should().Be(13, "fn:BeforeVersion(1.3)");
        formType.IsRequiredIn(12).Should().BeTrue();
        formType.IsRequiredIn(13).Should().BeFalse();
        formType.IsUnconditionallyRequired.Should().BeFalse();

        var parent = ModelRow("PageObject", "Parent");
        parent.Requirement.Should().Be(ArlingtonRequirement.Conditional, "fn:IsRequired(@Type!=Template) is not evaluated");
        parent.IsRequiredIn(20).Should().BeFalse();
    }

    [Fact]
    public void A_key_required_from_a_later_version_is_not_required_before_it_and_an_extension_s_key_never_is()
    {
        var rows = Compiled.Value.Objects.SelectMany(static o => o.Rows.Select(r => (Object: o.Name, Row: r))).ToList();
        var later = rows.First(static r => (byte)r.Row.Requirement == (byte)ArlingtonRequirement.Yes && r.Row.Since == 15 && !r.Row.Key.Contains('*', StringComparison.Ordinal));
        var extension = rows.First(static r => (byte)r.Row.Requirement == (byte)ArlingtonRequirement.Yes && r.Row.Since == ArlingtonVersion.ExtensionOnly);

        ModelRow(later.Object, later.Row.Key).IsRequiredIn(14).Should().BeFalse();
        ModelRow(later.Object, later.Row.Key).IsRequiredIn(15).Should().BeTrue();
        ModelRow(extension.Object, extension.Row.Key).IsExtensionOnly.Should().BeTrue();
        ModelRow(extension.Object, extension.Row.Key).IsRequiredIn(20).Should().BeFalse();
        ModelRow(extension.Object, extension.Row.Key).IsUnconditionallyRequired.Should().BeFalse();
    }

    [Fact]
    public void A_key_is_deprecated_in_its_version_and_every_later_one()
    {
        var row = ModelRow("Resource", "ProcSet");

        row.DeprecatedIn.Should().Be(20);
        row.IsDeprecatedIn(17).Should().BeFalse();
        row.IsDeprecatedIn(20).Should().BeTrue();
    }

    [Fact]
    public void A_value_takes_the_first_of_the_row_s_types_its_class_allows()
    {
        ArlingtonModel.TypesAccepting(PdfInteger.Create(3)).Should().Be(ArlingtonTypes.Integer | ArlingtonTypes.Bitmask | ArlingtonTypes.Number);
        ArlingtonModel.TypesAccepting(new PdfReal(400)).Should().Be(ArlingtonTypes.Number, "a real is never an integer, integral or not");
        ArlingtonModel.TypesAccepting(new PdfStream(new PdfDictionary(), PdfStreamData.FromMemory(ReadOnlyMemory<byte>.Empty))).Should().Be(ArlingtonTypes.Stream, "a stream is never a dictionary");
        ArlingtonModel.TypesAccepting(new PdfDictionary()).Should().Be(ArlingtonTypes.Dictionary | ArlingtonTypes.NameTree | ArlingtonTypes.NumberTree);
        ArlingtonModel.TypesAccepting(PdfNull.Instance).Should().Be(ArlingtonTypes.Null);
        ArlingtonModel.TypesAccepting(PdfBoolean.True).Should().Be(ArlingtonTypes.Boolean);
        ArlingtonModel.TypesAccepting(PdfName.Catalog).Should().Be(ArlingtonTypes.Name);
        ArlingtonModel.TypesAccepting(PdfString.FromText("D:2026")).Should().Be(
            ArlingtonTypes.String | ArlingtonTypes.StringAscii | ArlingtonTypes.StringByte | ArlingtonTypes.StringText | ArlingtonTypes.Date);
        ArlingtonModel.TypesAccepting(new PdfArray()).Should().Be(ArlingtonTypes.Array | ArlingtonTypes.Rectangle | ArlingtonTypes.Matrix);
        ArlingtonModel.TypesAccepting(new PdfReference(new PdfObjectId(1))).Should().Be(ArlingtonTypes.None, "a reference is resolved first");

        var width = ModelRow("FontDescriptorTrueType", "FontWeight");
        width.TryMatch(ArlingtonModel.TypesAccepting(new PdfReal(400)), out var type).Should().BeTrue("O3 makes FontWeight a number");
        type.Should().Be(ArlingtonType.Number);
        ModelRow("PageObject", "MediaBox").TryMatch(ArlingtonModel.TypesAccepting(new PdfDictionary()), out _).Should().BeFalse();
    }

    [Fact]
    public void Names_are_looked_up_ordinally_and_a_name_the_model_lacks_is_not_found()
    {
        ArlingtonModel.TryFindObject("PageObject", out var page).Should().BeTrue();
        page.Index.Should().Be(ArlingtonModel.PageObject);
        ArlingtonModel.TryFindObject("pageobject", out _).Should().BeFalse();
        ArlingtonModel.TryFindObject("Pagé", out _).Should().BeFalse();
        ArlingtonModel.TryFindObject(string.Empty, out _).Should().BeFalse();
        page.TryFindRow("MediaBox", out _).Should().BeTrue();
        page.TryFindRow("Mediabox", out _).Should().BeFalse();
        page.TryFindRow("Médiabox", out _).Should().BeFalse();
        page.TryFindRowOrWildcard("PieceInfo2", out _).Should().BeFalse("a page has no wildcard row");
        ArlingtonModel.GetObject(ArlingtonModel.FileTrailer).NameIs("FileTrailer").Should().BeTrue();
        ArlingtonModel.GetObject(ArlingtonModel.Catalog).NameIs("Catalog").Should().BeTrue();
    }

    [Fact]
    public void Looking_rows_up_allocates_nothing()
    {
        ArlingtonModel.TryFindObject("PageObject", out var page).Should().BeTrue();
        var found = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 1000; i++)
        {
            if (page.TryFindRow("Resources", out var row) && row.TryGetLink(ArlingtonType.Dictionary, out var link) && link.GetCandidate(0).NameIs("Resource"))
            {
                found++;
            }

            if (ArlingtonModel.TryFindObject("Catalog", out var catalog) && catalog.TryFindRow("Type", out var type) && type.HasValue(ArlingtonType.Name, "Catalog"))
            {
                found++;
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        found.Should().Be(2000);
        allocated.Should().Be(0);
    }

    [Theory]
    [InlineData("1.0", 10)]
    [InlineData("1.7", 17)]
    [InlineData("2.0", 20)]
    public void An_iso_version_is_one_byte_that_compares_as_versions_do(string text, byte expected)
    {
        ArlingtonVersion.TryParse(text, out var version).Should().BeTrue();

        version.Should().Be(expected);
        ArlingtonVersion.Format(version).Should().Be(text);
    }

    [Theory]
    [InlineData("1.8")]
    [InlineData("2.1")]
    [InlineData("0.9")]
    [InlineData("17")]
    [InlineData("1.x")]
    [InlineData("")]
    public void A_version_iso_32000_does_not_define_is_not_read(string text)
    {
        ArlingtonVersion.TryParse(text, out var version).Should().BeFalse();
        version.Should().Be(ArlingtonVersion.None);
    }

    [Fact]
    public void The_markers_that_are_no_version_say_what_they_mark()
    {
        ArlingtonVersion.Format(ArlingtonVersion.None).Should().Be("none");
        ArlingtonVersion.Format(ArlingtonVersion.ExtensionOnly).Should().Be("extension");
    }

    [Fact]
    public void Every_type_s_name_reads_back_as_that_type()
    {
        for (var type = ArlingtonType.Array; type <= ArlingtonType.StringText; type++)
        {
            ArlingtonTypeNames.TryParse(ArlingtonTypeNames.Of(type), out var read).Should().BeTrue();
            read.Should().Be(type);
        }

        ArlingtonTypeNames.TryParse("text", out _).Should().BeFalse();
        var act = () => ArlingtonTypeNames.Of((ArlingtonType)18);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Positions_outside_the_tables_are_refused()
    {
        var page = ArlingtonModel.GetObject(ArlingtonModel.PageObject);
        var resources = ModelRow("PageObject", "Resources");
        var type = ModelRow("PageObject", "Type");

        FluentActions.Invoking(static () => ArlingtonModel.GetObject(-1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(static () => ArlingtonModel.GetObject(ArlingtonModel.ObjectCount)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => page.GetRow(page.RowCount)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => page.GetRow(-1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => resources.GetLink(resources.LinkCount)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => resources.GetLink(-1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => type.GetValue(type.ValueCount)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => type.GetValue(-1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => resources.GetLink(0).GetCandidate(1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => resources.GetLink(0).GetCandidate(-1)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_link_to_one_object_is_no_candidate_set_and_has_no_plan()
    {
        var link = ModelRow("PageObject", "Resources").GetLink(0);

        link.IsCandidateSet.Should().BeFalse();
        link.CandidateSet.Should().Be(-1);
        link.CandidateCount.Should().Be(1);
        link.GetCandidate(0).ToString().Should().Be("Resource");
        link.TryGetPlan(arrays: false, out _).Should().BeFalse();
        link.TryGetPlan(arrays: true, out _).Should().BeFalse();
    }

    [Fact]
    public void Only_an_index_or_a_repeating_member_s_key_reads_as_an_element()
    {
        var type = ModelRow("PageObject", "Type");
        ModelRow("ArrayOfPageTreeNodeKids", "*").Key.TryGetIndex(out _).Should().BeFalse();
        type.Key.TryGetIndex(out _).Should().BeFalse();
        type.ElementIndex.Should().Be(-1, "a dictionary's row describes no element");
        ModelRow("ArrayOfQuadPoints", "7*").Key.TryGetIndex(out var member).Should().BeTrue();
        member.Should().Be(7);
        ModelRow("ArrayOf_8Numbers", "7").ElementIndex.Should().Be(7);
    }

    [Fact]
    public void Two_handles_on_the_same_entry_of_the_tables_are_equal()
    {
        var first = ModelRow("Catalog", "Type");
        var second = ModelRow("Catalog", "Type");
        var other = ModelRow("Catalog", "Pages");

        (first == second).Should().BeTrue();
        (first != other).Should().BeTrue();
        first.Equals((object)second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
        (first.Object == other.Object).Should().BeTrue();
        (first.Object != ArlingtonModel.GetObject(ArlingtonModel.PageObject)).Should().BeTrue();
        first.Object.Equals((object)other.Object).Should().BeTrue();
        first.Object.GetHashCode().Should().Be(ArlingtonModel.Catalog);
        (first.Key == second.Key).Should().BeTrue();
        (first.Key != other.Key).Should().BeTrue();
        first.Key.Equals((object)second.Key).Should().BeTrue();
        first.Key.GetHashCode().Should().Be(first.Key.Id);
        (first.GetValue(0) == second.GetValue(0)).Should().BeTrue();
        (first.GetValue(0) != ModelRow("PageObject", "Type").GetValue(0)).Should().BeTrue();
        first.GetValue(0).Equals((object)second.GetValue(0)).Should().BeTrue();
        first.GetValue(0).GetHashCode().Should().Be(second.GetValue(0).GetHashCode());
        var pages = other.GetLink(0);
        (pages == other.GetLink(0)).Should().BeTrue();
        (pages != ModelRow("PageObject", "Resources").GetLink(0)).Should().BeTrue();
        pages.Equals((object)other.GetLink(0)).Should().BeTrue();
        pages.GetHashCode().Should().Be(other.GetLink(0).GetHashCode());
        first.Equals("Catalog/Type").Should().BeFalse();
    }

    [Fact]
    public void A_handle_equals_no_handle_of_another_kind_even_on_the_same_number()
    {
        // Each kind of handle is a number into its own table, and an object's name is the name of the object's number:
        // handles of two kinds may hold the same number and name the same text, and are not equal.
        var catalog = ArlingtonModel.GetObject(ArlingtonModel.Catalog);
        var name = new ArlingtonName(catalog.Index);
        var link = new ArlingtonLink(catalog.Index);
        var value = new ArlingtonValue(catalog.Index);

        name.ToString().Should().Be(catalog.ToString());
        catalog.Equals((object)name).Should().BeFalse();
        name.Equals((object)catalog).Should().BeFalse();
        link.Equals((object)value).Should().BeFalse();
        value.Equals((object)link).Should().BeFalse();
        catalog.Equals(null).Should().BeFalse();
        name.Equals(null).Should().BeFalse();
        link.Equals(null).Should().BeFalse();
        value.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void A_key_s_own_row_answers_before_the_wildcard()
    {
        ArlingtonModel.TryFindObject("ColorSpaceMap", out var colorSpaces).Should().BeTrue();

        colorSpaces.TryFindRowOrWildcard("DefaultRGB", out var own).Should().BeTrue();
        colorSpaces.TryFindRowOrWildcard("CS0", out var open).Should().BeTrue();

        own.Key.ToString().Should().Be("DefaultRGB");
        own.IsWildcard.Should().BeFalse();
        open.IsWildcard.Should().BeTrue();
    }

    private static Config PropertySettings => Config.QuickThrowOnFailure.WithMaxTest(200).WithReplay(0x5EED_1729UL, 0x9E37_79B9_7F4A_7C15UL).WithQuietOnSuccess(true);

    private static void CompareRow(List<string> problems, ArlingtonObject obj, ArlingtonRow row, Tool.CompiledRow expected, Tool.CompiledModel compiled)
    {
        var where = $"{obj}/{expected.Key}";
        Expect(problems, where, "key", row.Key.ToString(), expected.Key);
        Expect(problems, where, "types", (uint)row.Types, (uint)expected.Types);
        Expect(problems, where, "since", row.Since, expected.Since);
        Expect(problems, where, "deprecated", row.DeprecatedIn, expected.Deprecated);
        Expect(problems, where, "requirement", (byte)row.Requirement, (byte)expected.Requirement);
        var inVersions = (byte)expected.Requirement == (byte)ArlingtonRequirement.InVersions;
        Expect(problems, where, "required from", row.RequiredFrom, inVersions ? expected.RequiredFrom : ArlingtonVersion.None);
        Expect(problems, where, "required before", row.RequiredBefore, inVersions ? expected.RequiredBefore : ArlingtonVersion.Unbounded);
        Expect(problems, where, "flags", (byte)(row.Flags & ~(ArlingtonRowFlags.RequirementMask | ArlingtonRowFlags.HasExtras)), (byte)expected.Flags);
        Expect(problems, where, "overrides", (byte)row.Overrides, (byte)expected.Overrides);
        Expect(problems, where, "found by key", obj.TryFindRow(expected.Key, out var byKey) && byKey == row, true);
        Expect(problems, where, "link groups", row.LinkCount, expected.Links.Count);
        Expect(problems, where, "values", row.ValueCount, expected.Values.Count);

        for (var k = 0; k < Math.Min(row.LinkCount, expected.Links.Count); k++)
        {
            var link = row.GetLink(k);
            var candidates = Enumerable.Range(0, link.CandidateCount).Select(c => link.GetCandidate(c).ToString());
            var type = (ArlingtonType)(byte)expected.Links[k].Type;
            Expect(problems, where, "link type", link.Type, type);
            Expect(problems, where, "candidates", string.Join(',', candidates), string.Join(',', expected.Links[k].Candidates));
            Expect(problems, where, "link by type", row.TryGetLink(type, out var byType) && byType == link, true);

            if (link.IsCandidateSet)
            {
                var set = compiled.CandidateSets[link.CandidateSet];
                Expect<string?>(problems, where, "array plan", link.TryGetPlan(arrays: true, out var arrayPlan) ? arrayPlan.ToString() : null, set.ArrayPlan);
                Expect<string?>(problems, where, "other plan", link.TryGetPlan(arrays: false, out var otherPlan) ? otherPlan.ToString() : null, set.OtherPlan);
            }
        }

        for (var v = 0; v < Math.Min(row.ValueCount, expected.Values.Count); v++)
        {
            var value = row.GetValue(v);
            var type = (ArlingtonType)(byte)expected.Values[v].Type;
            Expect(problems, where, "value", $"{value.Type} {value.Text}", $"{type} {expected.Values[v].Text}");
            Expect(problems, where, "value found", row.HasValue(type, expected.Values[v].Text), true);
        }
    }

    private static void Expect<T>(List<string> problems, string where, string what, T actual, T expected)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            problems.Add($"{where}: {what} decodes as {actual}, where the generator has {expected}");
        }
    }

    private static ArlingtonRow ModelRow(string objectName, string key)
    {
        ArlingtonModel.TryFindObject(objectName, out var found).Should().BeTrue(objectName);
        found.TryFindRow(key, out var row).Should().BeTrue($"{objectName}/{key}");
        return row;
    }

    private static List<string> Candidates(string slot) => slot switch
    {
        "[ArrayOfItems]" or "[fn:IsPDFVersion(1.2,ArrayOfItems)]" => ["ArrayOfItems"],
        "[Catalog]" => ["Catalog"],
        "[Info,Catalog,Info]" => ["Info", "Catalog"],
        "[fn:SinceVersion(1.5,fn:Extension(ADBE_Extn3,Info))]" => ["Info"],
        _ => [],
    };

    private static byte Code(string version) => (byte)(((version[0] - '0') * 10) + (version[2] - '0'));

    private static byte ExpectedSince(string since)
    {
        if (since.StartsWith("fn:Extension(", StringComparison.Ordinal))
        {
            return ArlingtonVersion.ExtensionOnly;
        }

        // fn:Eval(fn:Extension(E,v) || w): the ISO version w, just before the closing parenthesis.
        return Code(since.StartsWith("fn:Eval(", StringComparison.Ordinal) ? since[^4..^1] : since);
    }

    private static (ArlingtonRequirement, byte, byte) ExpectedRequirement(string required)
    {
        switch (required)
        {
            case "TRUE":
                return (ArlingtonRequirement.Yes, ArlingtonVersion.None, ArlingtonVersion.None);
            case "FALSE":
                return (ArlingtonRequirement.No, ArlingtonVersion.None, ArlingtonVersion.None);
        }

        if (!required.StartsWith("fn:IsRequired(fn:", StringComparison.Ordinal))
        {
            return (ArlingtonRequirement.Conditional, ArlingtonVersion.None, ArlingtonVersion.None);
        }

        var version = Code(required[^5..^2]);

        return required switch
        {
            _ when required.Contains("IsPDFVersion", StringComparison.Ordinal) => (ArlingtonRequirement.InVersions, version, (byte)(version + 1)),
            _ when required.Contains("BeforeVersion", StringComparison.Ordinal) => (ArlingtonRequirement.InVersions, ArlingtonVersion.None, version),
            _ => (ArlingtonRequirement.InVersions, version, ArlingtonVersion.Unbounded),
        };
    }
}
