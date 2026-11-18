using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The acceptance conditions of M02, run against real documents: what the validator says about each corpus
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

    public static TheoryData<string> DocumentsWithDiagnosedDamage =>
        Theory(Corpus.PathsWhere(document => document.Features.Any(DiagnosedDamage.ContainsKey)));

    /// <summary>The well-formed documents tagged with an anomaly every reader opens; a damaged one carries other faults too.</summary>
    public static TheoryData<string> DocumentsWithFieldAnomalies =>
        Theory(Corpus.PathsWhere(document => document.Expect.Clean && document.Features.Any(FieldAnomalies.Contains)));

    /// <summary>
    /// The damage the corpus tags documents with, and the rule that names it: a document so tagged earns that rule's
    /// finding, whatever else it earns. These are the faults the documents recorded as unsupported until M02 were held
    /// for: read without a word, or recovered otherwise than qpdf recovers them.
    /// </summary>
    private static readonly Dictionary<string, string> DiagnosedDamage = new(StringComparer.Ordinal)
    {
        ["trailer-without-size"] = PdfValidationRuleIds.FileSizeWrong,
        ["xref-entry-wrong"] = PdfValidationRuleIds.XRefEntryBroken,
        ["missing-endobj"] = PdfValidationRuleIds.ObjectEndObjMissing,
        ["nul-in-name"] = PdfValidationRuleIds.ObjectNameNullCharacter,
        ["dangling-reference"] = PdfValidationRuleIds.ObjectReferenceMissing,
        ["reference-to-missing-object"] = PdfValidationRuleIds.ObjectReferenceMissing,
        ["missing-mediabox"] = PdfValidationRuleIds.PageTreeMediaBoxInvalid,
        ["missing-resources"] = PdfValidationRuleIds.PageTreeResourcesMissing,
        ["page-object-is-stream"] = PdfValidationRuleIds.PageTreeKidInvalid,
        ["page-tree-null-kid"] = PdfValidationRuleIds.PageTreeKidInvalid,
        ["dangling-page-tree-kids"] = PdfValidationRuleIds.PageTreeKidInvalid,
        ["catalog-without-type"] = PdfValidationRuleIds.ObjectKeyMissing,
        ["ipres2017-t02-02-002"] = PdfValidationRuleIds.PageTreeCycle,
        ["ipres2017-t02-02-003"] = PdfValidationRuleIds.PageTreeKidInvalid,
        ["ipres2017-t02-02-004"] = PdfValidationRuleIds.PageTreeKidInvalid,
        ["ipres2017-t02-02-005"] = PdfValidationRuleIds.PageTreeKidsMissing,
        ["ipres2017-t02-02-007"] = PdfValidationRuleIds.PageTreeCountMismatch,
        ["ipres2017-t02-02-008"] = PdfValidationRuleIds.PageTreeCountMismatch,
        ["ipres2017-t02-02-009"] = PdfValidationRuleIds.ObjectTypeValueWrong,
        ["ipres2017-t02-03-002"] = PdfValidationRuleIds.ObjectTypeValueWrong,
        ["ipres2017-t02-03-006"] = PdfValidationRuleIds.PageTreeKidInvalid,
        ["ipres2017-t03-007"] = PdfValidationRuleIds.XRefEntryShifted,
        ["ipres2017-t03-010"] = PdfValidationRuleIds.XRefGenerationMismatch,
        ["ipres2017-t04-010"] = PdfValidationRuleIds.FileTrailerMalformed,
        ["ipres2017-t04-011"] = PdfValidationRuleIds.FileRootInvalid,
        ["ipres2017-t04-012"] = PdfValidationRuleIds.FileRootInvalid,
        ["ipres2017-t04-013"] = PdfValidationRuleIds.FileRootInvalid,
        ["ipres2017-t04-014"] = PdfValidationRuleIds.FileRootInvalid,
        ["ipres2017-t04-015"] = PdfValidationRuleIds.FileSizeWrong,
    };

    /// <summary>
    /// The anomalies the corpus tags documents with that every reader opens: stale linearization hints, a /Size one
    /// too large, a cross-reference stream without its own entry, references to objects the index lacks, and the
    /// rest M02 names.
    /// </summary>
    private static readonly HashSet<string> FieldAnomalies = new(StringComparer.Ordinal)
    {
        "linearization-hints-inconsistent",
        "size-off-by-one",
        "xref-stream-missing-self-entry",
        "xref-stream-missing-own-entry",
        "references-missing-from-xref",
        "reference-to-missing-object",
        "malformed-font-xmp",
        "utf16le-info-dictionary",
        "two-startxref-lines-before-eof",
        "markinfo-aliases-pages-node",
        "invalid-creation-date-year-zero",
    };

    /// <summary>
    /// How the corpus model reads the manifest: the key <c>readerLimits</c> is its <c>ReaderLimits</c>.
    /// </summary>
    private static readonly System.Text.Json.JsonSerializerOptions ManifestOptions =
        new() { PropertyNameCaseInsensitive = true };

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
        Assert.SkipWhen(
            entry.Expect.IsUnsupportedIn(nameof(Every_document_produces_exactly_its_declared_findings)),
            $"{entry.Name}: {entry.Expect.Unsupported}");

        var report = Validate(entry);

        report.Findings.Select(finding => finding.RuleId).Distinct().Should().BeEquivalentTo(
            entry.Expect.Findings,
            $"{entry.Name} declares {Describe(entry.Expect.Findings)} and the validator reported {Describe(report)}");
    }

    [Theory]
    [MemberData(nameof(AllDocuments))]
    public void Every_document_rebuilt_as_it_opens_earns_an_error_unless_a_limit_was_reached(string file)
    {
        // A rebuild at opening means the reader cannot vouch that it reads what was written (ADR 45): some rule says why, an
        // empty chain's included (#218). A rebuild a reader limit set off is the limit's, which xref.checked-in-part says; a
        // document where one was reached is left out, the report not telling which set the rebuild off.
        var entry = Corpus.Get(file);
        var options = CorpusReadingTests.OptionsFor(entry) with { ThrowOnEncrypted = false };
        using var document = PdfDocument.Open(Corpus.PathOf(entry.File), options);

        if (!document.WasRepaired || document.Diagnostics.Any(diagnostic => diagnostic.Code.StartsWith("limit.", StringComparison.Ordinal)))
        {
            return;
        }

        var report = new PdfValidator().Validate(document);

        report.Findings.Should().Contain(
            finding => finding.Severity == PdfValidationSeverity.Error,
            $"{entry.Name} is rebuilt as it opens, and the validator reported {Describe(report)}");
    }

    [Theory]
    [MemberData(nameof(WellFormedDocuments))]
    public void Well_formed_documents_have_no_errors(string file)
    {
        var entry = Corpus.Get(file);
        Assert.SkipWhen(
            entry.Expect.IsUnsupportedIn(nameof(Well_formed_documents_have_no_errors)),
            $"{entry.Name}: {entry.Expect.Unsupported}");

        var report = Validate(entry);

        report.HasErrors.Should().BeFalse($"{entry.Name} is well formed, and the validator reported {Describe(report)}");
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ConformanceFailures))]
    public void Conformance_failures_are_not_structural_errors(string file)
    {
        // veraPDF finds these PDF/A-invalid; the structural profile must not: conformance is M20's business.
        var entry = Corpus.Get(file);
        Assert.SkipWhen(
            entry.Expect.IsUnsupportedIn(nameof(Conformance_failures_are_not_structural_errors)),
            $"{entry.Name}: {entry.Expect.Unsupported}");

        var report = Validate(entry);

        report.HasErrors.Should().BeFalse($"{entry.Name} breaks PDF/A, not the structure: {Describe(report)}");
    }

    [Theory]
    [MemberData(nameof(DocumentsWithDiagnosedDamage))]
    public void Documents_waiting_for_M02_are_now_diagnosed(string file)
    {
        var entry = Corpus.Get(file);
        Assert.SkipWhen(
            entry.Expect.IsUnsupportedIn(nameof(Documents_waiting_for_M02_are_now_diagnosed)),
            $"{entry.Name}: {entry.Expect.Unsupported}");

        var report = Validate(entry);

        foreach (var feature in entry.Features.Where(DiagnosedDamage.ContainsKey))
        {
            report.Contains(DiagnosedDamage[feature]).Should().BeTrue(
                $"{entry.Name} is tagged {feature}, which {DiagnosedDamage[feature]} names, and the validator reported {Describe(report)}");
        }
    }

    [Theory]
    [MemberData(nameof(DocumentsWithFieldAnomalies))]
    public void Field_anomalies_are_warnings_not_errors(string file)
    {
        // Every reader opens these files; a reference to an undefined object is null by the specification.
        var entry = Corpus.Get(file);
        Assert.SkipWhen(
            entry.Expect.IsUnsupportedIn(nameof(Field_anomalies_are_warnings_not_errors)),
            $"{entry.Name}: {entry.Expect.Unsupported}");

        var report = Validate(entry);

        report.HasErrors.Should().BeFalse($"{entry.Name} carries an anomaly every reader accepts, and the validator reported {Describe(report)}");
    }

    [Fact]
    public void Validating_the_largest_document_stays_within_its_budget()
    {
        var entry = Corpus.Documents.First(document => document.Features.Contains("many-pages"));
        var bytes = Corpus.Read(entry.File);

        // Per-thread, as the reading budget measures it: the suite runs in parallel.
        var before = GC.GetAllocatedBytesForCurrentThread();
        PdfValidationReport report;

        using (var document = PdfDocument.Open(bytes, CorpusReadingTests.OptionsFor(entry)))
        {
            report = new PdfValidator().Validate(document);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Opening the thousand-page journal and validating it — every entry of its index probed, its page tree
        // walked, every object reachable from its trailer resolved once, every object of its index inspected for a
        // page the tree leaves out, the 6,008 objects and values the trailer reaches typed and checked against the
        // Arlington model — measured 3.8 MB, about 2 KB for each of its 2,006 objects, of which the Arlington walk takes
        // 0.5 MB: proportional to the objects, not to their content, since no stream's data is read. The budget leaves
        // headroom and fails loudly on a regression.
        report.Findings.Should().BeEmpty();
        allocated.Should().BeLessThan(
            ValidationBudget,
            $"validating {entry.Name} allocated {allocated / 1024} KB");
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

    [Theory]
    [MemberData(nameof(AllDocuments))]
    public void A_report_that_keeps_fewer_findings_is_the_whole_report_cut_to_its_first(string file)
    {
        // #168: each analysis writes only the findings the report has room for when it is made, and counts the rest. A
        // report at a lower capacity is the one that keeps every finding cut to its first, with the same counts and the same
        // rules named: at none, one, half and all but one of its findings.
        var entry = Corpus.Get(file);
        var whole = Validate(entry, int.MaxValue);
        var total = whole.Findings.Count;

        foreach (var capacity in new[] { 0, 1, total / 2, total - 1 }.Where(capacity => capacity >= 0 && capacity < total).Distinct())
        {
            var report = Validate(entry, capacity);
            var because = $"{entry.Name} validated at a capacity of {capacity}, of {total} findings";

            report.Findings.Should().Equal(whole.Findings.Take(capacity), because);
            report.ToString().Should().Be(whole.ToString(), because);
            report.SuppressedCount.Should().Be(total - capacity, because);
            ValidationProfile.Structural.RuleIds.Where(report.Contains).Should().Equal(
                ValidationProfile.Structural.RuleIds.Where(whole.Contains), because);
        }
    }

    [Theory]
    [MemberData(nameof(AllDocuments))]
    public void No_read_rebuilds_the_index_after_a_validation(string file)
    {
        // The validator rebuilds the index before its first rule when some read could still do so (#237): once it has, or
        // found that none could, reading every object the index holds rebuilds nothing. No committed document has an index
        // a later read would rebuild, so these hold it against a regression; eight of the remote corpus do, three of them
        // Quartz files whose damage no rule reaches, and the nightly run holds it on them.
        var entry = Corpus.Get(file);
        using var document = PdfDocument.Open(Corpus.PathOf(entry.File), CorpusReadingTests.OptionsFor(entry) with { ThrowOnEncrypted = false });
        new PdfValidator().Validate(document);
        var repaired = document.WasRepaired;

        foreach (var number in document.ObjectNumbers.ToList())
        {
            document.GetObject(new PdfObjectId(number));
        }

        document.WasRepaired.Should().Be(repaired, $"{entry.Name} was validated, and its index settled, before it was read");
    }

    [Theory]
    [InlineData(GreekExchangeRates, "200 bytes from 150 before the endstream of the first stream past a quarter of the file whose length is indirect", PdfValidationRuleIds.XRefSectionShifted)]
    [InlineData(DmcaSummary, "the endstream of streams 77 and 79", PdfValidationRuleIds.XRefEntryShifted)]
    [InlineData(DmcaSummary, "the endstream and the endobj of streams 77 and 79", PdfValidationRuleIds.XRefEntryShifted)]
    [InlineData(DmcaSummary, "the endstream of streams 77 and 79, every row then shifted 7 bytes on", PdfValidationRuleIds.XRefEntryShifted)]
    [InlineData(DmcaSummary, "the endstream and the endobj of streams 77 and 79, every row then shifted 7 bytes on", PdfValidationRuleIds.XRefEntryShifted)]
    public void A_corpus_document_whose_streams_lost_their_endstream_has_the_entries_and_sections_past_them_shifted_not_unconfirmed(
        string file, string lost, string shifted)
    {
        // A stream with an indirect length loses its endstream: in the Greek notice, with its endobj, the object of its length
        // and the next header, which leaves the section /Prev names, an update's, 200 bytes before where it is named, past
        // the stream's data, and nothing tells where a load ends that data but the next header, past the section; in the
        // DMCA summary, two of them, the second's header in the first's data, their length objects neither just before nor
        // just after them. The scan for objects learns where a load ends each one's data — the section's keyword ends it,
        // and the second stream is awaited in the first's place —, and holds what it takes past there as taken between
        // objects: the sections and entries past the loss, the rows written before the loss, are shifted (#372).
        var entry = Corpus.Get(file);
        var options = CorpusReadingTests.OptionsFor(entry) with { ThrowOnEncrypted = false };
        using var document = PdfDocument.Open(Lose(Corpus.Read(file), lost), options);

        var report = new PdfValidator().Validate(document);

        report.Findings.Should().Contain(finding => finding.RuleId == shifted, $"{entry.Name} lost {lost}, and the validator reported {Describe(report)}");
        report.Findings.Should().NotContain(
            finding => finding.RuleId == PdfValidationRuleIds.XRefSectionUnconfirmed || finding.RuleId == PdfValidationRuleIds.XRefEntryUnconfirmed,
            $"{entry.Name} lost {lost}, and the validator reported {Describe(report)}");
    }

    private const string GreekExchangeRates = "vendor/eu-publications/pdflib-oj-exchange-rates-greek.pdf";

    private const string DmcaSummary = "vendor/us-federal/pdfwriter3-copyright-office-dmca-summary-1998-rc4-40.pdf";

    /// <summary>Gives <paramref name="bytes"/> with what <paramref name="lost"/> names cut out, and the rows shifted where it says.</summary>
    private static byte[] Lose(byte[] bytes, string lost)
    {
        var text = Encoding.Latin1.GetString(bytes);

        if (lost.StartsWith("200 bytes", StringComparison.Ordinal))
        {
            foreach (Match start in Regex.Matches(text, @">>\s*stream(\r\n|\n|\r)"))
            {
                var dataStart = start.Index + start.Length;
                var header = text.LastIndexOf(" obj", start.Index, StringComparison.Ordinal);

                if (dataStart > bytes.Length / 4 && Regex.IsMatch(text[header..start.Index], @"/Length\s+\d+\s+\d+\s+R"))
                {
                    var cut = text.IndexOf("endstream", dataStart, StringComparison.Ordinal) - 150;
                    return [.. bytes.AsSpan(0, cut), .. bytes.AsSpan(cut + 200)];
                }
            }
        }

        foreach (var number in (int[])[79, 77])
        {
            var keyword = text.IndexOf("endstream", Regex.Match(text, string.Create(CultureInfo.InvariantCulture, $@"(?<![0-9]){number} 0 obj")).Index, StringComparison.Ordinal);
            var end = lost.Contains("endobj", StringComparison.Ordinal)
                ? text.IndexOf("endobj", keyword, StringComparison.Ordinal) + "endobj".Length
                : keyword + "endstream".Length;
            text = text[..keyword] + text[end..];
        }

        if (lost.Contains("shifted", StringComparison.Ordinal))
        {
            text = Regex.Replace(text, @"(?<=[\r\n])(\d{10}) (\d{5}) n(?=[ \r\n])", row => string.Create(
                CultureInfo.InvariantCulture, $"{long.Parse(row.Groups[1].Value, CultureInfo.InvariantCulture) + 7:D10} {row.Groups[2].Value} n"));
        }

        return Encoding.Latin1.GetBytes(text);
    }

    [Fact]
    public void Every_declared_finding_names_a_rule_that_exists()
    {
        // Over every entry, fetched or not: a finding the manifest expects under a misspelled identifier would
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
    public void A_misspelled_expectation_or_reader_limit_fails_loading(string entry)
    {
        // Ignored, "finding" would leave the entry declaring no finding, and a sound-looking document would pass
        // for the wrong reason: the model refuses what it does not know, under the options the corpus loads with.
        var loading = () => System.Text.Json.JsonSerializer.Deserialize<CorpusDocument>(entry, ManifestOptions);

        loading.Should().Throw<System.Text.Json.JsonException>();
    }

    /// <summary>
    /// Opens a corpus document as its entry says — under its raised reader limits, if any — and validates it
    /// under the default profile. An encrypted document is opened too: its structure is readable without its
    /// key, which only its content needs (M16).
    /// </summary>
    private static PdfValidationReport Validate(CorpusDocument entry, int? capacity = null)
    {
        var options = CorpusReadingTests.OptionsFor(entry) with { ThrowOnEncrypted = false };
        using var document = PdfDocument.Open(Corpus.PathOf(entry.File), options);
        return new PdfValidator(capacity is { } findings ? new PdfValidatorOptions { FindingCapacity = findings } : null).Validate(document);
    }

    /// <summary>Every entry of the committed manifest, including remote documents this machine did not fetch.</summary>
    private static List<CorpusDocument> ManifestEntries()
    {
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Corpus.Root, "manifest.json")));
        return System.Text.Json.JsonSerializer.Deserialize<List<CorpusDocument>>(
            manifest.RootElement.GetProperty("documents"), ManifestOptions)!;
    }

    /// <summary>What validating the thousand-page journal may allocate, opening included.</summary>
    private const long ValidationBudget = 6 * 1024 * 1024;

    private static string Describe(string[] findings) =>
        findings.Length == 0 ? "no finding" : string.Join(", ", findings);

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
