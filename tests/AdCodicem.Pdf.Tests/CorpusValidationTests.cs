using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The acceptance conditions of M2, run against real documents: what the validator says about each corpus
/// document, against what its manifest entry declares.
/// </summary>
/// <remarks>
/// A validator that calls Chromium's, LibreOffice's or Word's output broken is wrong, not strict; one that
/// stays silent on a file cut in half is no use. Both are caught here, the first by the rule that sound files
/// earn no error, both by the rule that every document earns exactly the findings its entry declares.
/// </remarks>
public class CorpusValidationTests
{
    public static TheoryData<string> AllDocuments => Theory(Corpus.Paths);

    public static TheoryData<string> WellFormedDocuments => Theory(Corpus.PathsWhere(document => document.Expect.Clean));

    public static TheoryData<string> ConformanceFailures =>
        Theory(Corpus.PathsWhere(document => document.Expect.ConformanceValid == false));

    [Theory]
    [MemberData(nameof(AllDocuments))]
    public void The_validator_reports_on_every_document_the_reader_opens(string file)
    {
        // Never skipped, not even for a document the library cannot meet yet: whatever else is wrong, the
        // validator reports and does not throw.
        var entry = Corpus.Get(file);

        var validating = () => Validate(entry);

        validating.Should().NotThrow($"{entry.Name} opens, so it is reported on");
    }

    [Theory]
    [MemberData(nameof(AllDocuments))]
    public void Every_document_produces_exactly_its_declared_findings(string file)
    {
        var entry = Corpus.Get(file);
        Assert.SkipWhen(entry.Expect.Unsupported is not null, $"{entry.Name}: {entry.Expect.Unsupported}");

        var report = Validate(entry);

        report.Findings.Select(finding => finding.RuleId).Distinct().Should().BeEquivalentTo(
            entry.Expect.Findings,
            $"{entry.Name} declares {Describe(entry.Expect.Findings)} and the validator reported {Describe(report)}");
    }

    [Theory]
    [MemberData(nameof(WellFormedDocuments))]
    public void Well_formed_documents_have_no_errors(string file)
    {
        var entry = Corpus.Get(file);
        Assert.SkipWhen(entry.Expect.Unsupported is not null, $"{entry.Name}: {entry.Expect.Unsupported}");

        var report = Validate(entry);

        report.HasErrors.Should().BeFalse($"{entry.Name} is well formed, and the validator reported {Describe(report)}");
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ConformanceFailures))]
    public void Conformance_failures_are_not_structural_errors(string file)
    {
        // veraPDF finds these PDF/A-invalid; the structural profile must not: conformance is M12's business.
        var entry = Corpus.Get(file);
        Assert.SkipWhen(entry.Expect.Unsupported is not null, $"{entry.Name}: {entry.Expect.Unsupported}");

        var report = Validate(entry);

        report.HasErrors.Should().BeFalse($"{entry.Name} breaks PDF/A, not the structure: {Describe(report)}");
    }

    [Theory]
    [MemberData(nameof(AllDocuments))]
    public void Validation_is_deterministic(string file)
    {
        var entry = Corpus.Get(file);

        var first = Validate(entry);
        var second = Validate(entry);

        second.Findings.Should().Equal(first.Findings, $"{entry.Name} is validated twice from scratch");
        second.ToString().Should().Be(first.ToString());
    }

    [Fact]
    public void Every_declared_finding_names_a_rule_that_exists()
    {
        // Over every entry, fetched or not: a finding the manifest expects under a misspelt identifier would
        // otherwise pass wherever the document is absent, and fail only on the nightly runner.
        var known = typeof(PdfValidationRuleIds).GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var entry in ManifestEntries())
        {
            entry.Expect.Findings.Should().OnlyContain(ruleId => known.Contains(ruleId), $"{entry.File} names rules that exist");
            entry.Expect.Findings.Should().OnlyHaveUniqueItems($"{entry.File} names each rule once");
        }
    }

    [Theory]
    [InlineData("""{"file":"x.pdf","title":"t","expect":{"finding":["file.eof-missing"]}}""")]
    [InlineData("""{"file":"x.pdf","title":"t","readerLimits":{"maxObjectLenght":1}}""")]
    public void A_misspelt_expectation_or_reader_limit_fails_loading(string entry)
    {
        // Ignored, "finding" would leave the entry declaring no finding, and a sound-looking document would pass
        // for the wrong reason: the model refuses what it does not know, under the options the corpus loads with.
        var loading = () => System.Text.Json.JsonSerializer.Deserialize<CorpusDocument>(
            entry, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        loading.Should().Throw<System.Text.Json.JsonException>();
    }

    /// <summary>
    /// Opens a corpus document as its entry says — under its raised reader limits, if any — and validates it
    /// under the default profile. An encrypted document is opened too: its structure is readable without its
    /// key, which only its content needs (M11).
    /// </summary>
    private static PdfValidationReport Validate(CorpusDocument entry)
    {
        var options = CorpusReadingTests.OptionsFor(entry) with { ThrowOnEncrypted = false };
        using var document = PdfDocument.Open(Corpus.PathOf(entry.File), options);
        return new PdfValidator().Validate(document);
    }

    /// <summary>Every entry of the committed manifest, including remote documents this machine did not fetch.</summary>
    private static List<CorpusDocument> ManifestEntries()
    {
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Corpus.Root, "manifest.json")));
        return System.Text.Json.JsonSerializer.Deserialize<List<CorpusDocument>>(
            manifest.RootElement.GetProperty("documents"),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static string Describe(IReadOnlyCollection<string> findings) =>
        findings.Count == 0 ? "no finding" : string.Join(", ", findings);

    private static string Describe(PdfValidationReport report) =>
        report.Findings.Count == 0 ? "nothing" : string.Join("; ", report.Findings.Select(finding => finding.ToString()));

    private static TheoryData<string> Theory(IReadOnlyList<string> paths)
    {
        var data = new TheoryData<string>();

        foreach (var path in paths)
        {
            data.Add(path);
        }

        return data;
    }
}
