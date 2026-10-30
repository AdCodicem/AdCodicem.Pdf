using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The rule engine: how a validator runs a profile over a document, and what its report keeps and counts.
/// The rules themselves are tested on their own; these use rules made for the purpose.
/// </summary>
public class ValidatorTests
{
    [Fact]
    public void A_sound_document_has_nothing_to_report_under_the_structural_profile()
    {
        using var document = PdfDocument.Open(SoundFile());

        var report = new PdfValidator().Validate(document);

        report.Findings.Should().BeEmpty();
        report.HasErrors.Should().BeFalse();
        report.HasWarnings.Should().BeFalse();
        report.SuppressedCount.Should().Be(0);
    }

    [Fact]
    public void A_validator_without_options_runs_the_structural_profile()
    {
        var validator = new PdfValidator();

        validator.Options.Should().BeSameAs(PdfValidatorOptions.Default);
        validator.Options.Profile.Should().BeSameAs(ValidationProfile.Structural);
        validator.Options.FindingCapacity.Should().Be(1000);

        using var document = PdfDocument.Open(SoundFile());
        var report = validator.Validate(document);

        report.ProfileName.Should().Be("structural");
        report.ProfileVersion.Should().Be(1);
        report.ToString().Should().Be("structural 1: errors 0, warnings 0, information 0");
    }

    [Fact]
    public void The_structural_profile_names_the_rules_it_runs_in_order()
    {
        ValidationProfile.Structural.RuleIds.Should().Equal(
            PdfValidationRuleIds.FileHeaderMissing,
            PdfValidationRuleIds.FileHeaderOffset,
            PdfValidationRuleIds.FileHeaderVersionInvalid,
            PdfValidationRuleIds.FileEofMissing,
            PdfValidationRuleIds.FileStartXRefMissing,
            PdfValidationRuleIds.FileStartXRefWrong,
            PdfValidationRuleIds.FileTrailerMissing,
            PdfValidationRuleIds.FileTrailerMalformed,
            PdfValidationRuleIds.FileRootInvalid,
            PdfValidationRuleIds.FileSizeWrong,
            PdfValidationRuleIds.FileTrailerValueWrong,
            PdfValidationRuleIds.XRefSectionMalformed,
            PdfValidationRuleIds.XRefSectionNotFound,
            PdfValidationRuleIds.XRefSectionShifted,
            PdfValidationRuleIds.XRefChainLoop,
            PdfValidationRuleIds.XRefEntryBroken,
            PdfValidationRuleIds.XRefEntryShifted,
            PdfValidationRuleIds.XRefGenerationMismatch,
            PdfValidationRuleIds.XRefObjectStreamCircular,
            PdfValidationRuleIds.XRefObjectStreamBroken,
            PdfValidationRuleIds.XRefObjectStreamValueWrong,
            PdfValidationRuleIds.XRefOffsetImprecise,
            PdfValidationRuleIds.XRefObjectPastSize,
            PdfValidationRuleIds.XRefCheckedInPart,
            PdfValidationRuleIds.ObjectReferenceMissing,
            PdfValidationRuleIds.ObjectEndObjMissing,
            PdfValidationRuleIds.ObjectNameNullCharacter,
            PdfValidationRuleIds.ObjectKeyMissing,
            PdfValidationRuleIds.ObjectValueTypeWrong,
            PdfValidationRuleIds.ObjectTypeValueWrong,
            PdfValidationRuleIds.ObjectKeyDeprecated,
            PdfValidationRuleIds.PageTreeCycle,
            PdfValidationRuleIds.PageTreeNodeRepeated,
            PdfValidationRuleIds.PageTreeKidsMissing,
            PdfValidationRuleIds.PageTreeKidInvalid,
            PdfValidationRuleIds.PageTreeCountMismatch,
            PdfValidationRuleIds.PageTreeParentWrong,
            PdfValidationRuleIds.PageTreeMediaBoxInvalid,
            PdfValidationRuleIds.PageTreeResourcesMissing,
            PdfValidationRuleIds.PageTreePageOrphaned);
        ValidationProfile.Structural.ToString().Should().Be("structural 1");
    }

    [Fact]
    public void Findings_come_in_the_profile_s_order_then_in_the_order_each_rule_found_them()
    {
        var profile = new ValidationProfile(
            "ordered",
            2,
            [
                new ReportingRule("test.first", PdfValidationSeverity.Warning, 2),
                new ReportingRule("test.second", PdfValidationSeverity.Error, 1),
                new ReportingRule("test.third", PdfValidationSeverity.Information, 2),
            ]);
        using var document = PdfDocument.Open(SoundFile());

        var report = new PdfValidator(new PdfValidatorOptions { Profile = profile }).Validate(document);

        profile.RuleIds.Should().Equal("test.first", "test.second", "test.third");
        report.ProfileName.Should().Be("ordered");
        report.ProfileVersion.Should().Be(2);
        report.ToString().Should().Be("ordered 2: errors 1, warnings 2, information 2");
        report.Findings.Select(finding => (finding.RuleId, finding.Message)).Should().Equal(
            ("test.first", "Finding 1 of test.first."),
            ("test.first", "Finding 2 of test.first."),
            ("test.second", "Finding 1 of test.second."),
            ("test.third", "Finding 1 of test.third."),
            ("test.third", "Finding 2 of test.third."));
        report.Findings.Select(finding => finding.Severity).Should().Equal(
            PdfValidationSeverity.Warning,
            PdfValidationSeverity.Warning,
            PdfValidationSeverity.Error,
            PdfValidationSeverity.Information,
            PdfValidationSeverity.Information);
        report.ErrorCount.Should().Be(1);
        report.WarningCount.Should().Be(2);
        report.InformationCount.Should().Be(2);
        report.HasErrors.Should().BeTrue();
        report.HasWarnings.Should().BeTrue();
    }

    [Fact]
    public void The_report_keeps_as_many_findings_as_its_capacity_and_counts_them_all()
    {
        var profile = Profile(
            new ReportingRule("test.many", PdfValidationSeverity.Warning, 5),
            new ReportingRule("test.late", PdfValidationSeverity.Error, 1));
        using var document = PdfDocument.Open(SoundFile());

        var report = new PdfValidator(new PdfValidatorOptions { Profile = profile, FindingCapacity = 2 }).Validate(document);

        report.Findings.Select(finding => finding.Message).Should().Equal(
            "Finding 1 of test.many.", "Finding 2 of test.many.");
        report.WarningCount.Should().Be(5);
        report.ErrorCount.Should().Be(1);
        report.SuppressedCount.Should().Be(4);

        // A rule whose every finding was dropped still reported: the report says so.
        report.Contains("test.late").Should().BeTrue();
        report.HasErrors.Should().BeTrue();
        report.Contains("test.absent").Should().BeFalse();

        var askingForNothing = () => report.Contains(null!);
        askingForNothing.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_capacity_of_zero_keeps_no_finding_and_still_counts_them()
    {
        var profile = Profile(new ReportingRule("test.many", PdfValidationSeverity.Information, 3));
        using var document = PdfDocument.Open(SoundFile());

        var report = new PdfValidator(new PdfValidatorOptions { Profile = profile, FindingCapacity = 0 }).Validate(document);

        report.Findings.Should().BeEmpty();
        report.InformationCount.Should().Be(3);
        report.SuppressedCount.Should().Be(3);
        report.Contains("test.many").Should().BeTrue();
    }

    [Fact]
    public void An_analysis_writes_the_first_findings_its_room_holds_and_counts_the_rest()
    {
        // #168: what an analysis made with a room of two writes, in the order it found them; the rest it counts, for its rule
        // to count in turn.
        var findings = new ProbeFindings(room: 2);

        for (var number = 1; number <= 5; number++)
        {
            if (findings.Take())
            {
                findings.Keep(new ProbeFinding(PdfValidationLocation.AtPosition(number), $"Finding {number}."));
            }
        }

        findings.Total.Should().Be(5);
        findings.Kept.Should().Be(2);
        findings.Unkept.Should().Be(3);
        findings.Select(finding => finding.Message).Should().Equal("Finding 1.", "Finding 2.");
        ((System.Collections.IEnumerable)findings).Cast<ProbeFinding>().Should().HaveCount(2);
    }

    [Fact]
    public void An_analysis_made_once_the_report_is_full_writes_nothing_and_counts_every_finding()
    {
        var findings = new ProbeFindings(room: 0);

        findings.Take().Should().BeFalse();
        findings.Take().Should().BeFalse();

        findings.Total.Should().Be(2);
        findings.Kept.Should().Be(0);
        findings.Unkept.Should().Be(2);
        findings.Should().BeEmpty();
    }

    [Fact]
    public void A_rule_that_writes_its_own_findings_writes_those_the_report_has_room_for_and_counts_the_rest()
    {
        // The room is the capacity less the findings kept: it shrinks as the rules report. A rule asking for room before it
        // writes a finding gets it while there is some; past it, its finding is counted, under its id, and not written.
        var asking = new AskingRule("test.asking", PdfValidationSeverity.Error, 3);
        var profile = Profile(new ReportingRule("test.first", PdfValidationSeverity.Warning, 2), asking);
        using var document = PdfDocument.Open(SoundFile());

        var report = new PdfValidator(new PdfValidatorOptions { Profile = profile, FindingCapacity = 3 }).Validate(document);

        asking.Rooms.Should().Equal(1, 0, 0);
        asking.Written.Should().Be(1, "the report had room for one of its findings");
        report.Findings.Select(finding => finding.Message).Should().Equal(
            "Finding 1 of test.first.", "Finding 2 of test.first.", "Finding 1 of test.asking.");
        report.ErrorCount.Should().Be(3);
        report.WarningCount.Should().Be(2);
        report.SuppressedCount.Should().Be(2);
        report.Contains("test.asking").Should().BeTrue();
    }

    [Fact]
    public void A_rule_counts_what_its_analysis_did_not_write_at_its_severity_and_counting_none_names_no_rule()
    {
        var profile = Profile(
            new CountingRule("test.none", PdfValidationSeverity.Warning, 0),
            new CountingRule("test.errors", PdfValidationSeverity.Error, 2),
            new CountingRule("test.warnings", PdfValidationSeverity.Warning, 3),
            new CountingRule("test.information", PdfValidationSeverity.Information, 4));
        using var document = PdfDocument.Open(SoundFile());

        var report = new PdfValidator(new PdfValidatorOptions { Profile = profile }).Validate(document);

        report.Findings.Should().BeEmpty();
        report.ErrorCount.Should().Be(2);
        report.WarningCount.Should().Be(3);
        report.InformationCount.Should().Be(4);
        report.SuppressedCount.Should().Be(9);
        report.Contains("test.none").Should().BeFalse("counting no finding is no finding");
        report.Contains("test.errors").Should().BeTrue();
        report.Contains("test.warnings").Should().BeTrue();
        report.Contains("test.information").Should().BeTrue();
    }

    [Fact]
    public void Counts_past_what_an_int_holds_stay_at_their_greatest()
    {
        // #168: findings now counted rather than kept, a file of gigabytes can hold more than an int counts. Each count, and
        // what the report did not keep, stays at its greatest rather than wrapping.
        var profile = Profile(
            new CountingRule("test.errors", PdfValidationSeverity.Error, int.MaxValue, times: 2),
            new CountingRule("test.warnings", PdfValidationSeverity.Warning, int.MaxValue, times: 2),
            new CountingRule("test.information", PdfValidationSeverity.Information, int.MaxValue, times: 2),
            new ReportingRule("test.kept", PdfValidationSeverity.Error, 1));
        using var document = PdfDocument.Open(SoundFile());

        var report = new PdfValidator(new PdfValidatorOptions { Profile = profile }).Validate(document);

        report.Findings.Should().ContainSingle();
        report.ErrorCount.Should().Be(int.MaxValue);
        report.WarningCount.Should().Be(int.MaxValue);
        report.InformationCount.Should().Be(int.MaxValue);
        report.SuppressedCount.Should().Be(int.MaxValue);
    }

    [Fact]
    public void An_analysis_counting_past_what_an_int_holds_stays_at_its_greatest()
    {
        // One finding more than an int counts, a few seconds of counting: a file of some 4 GB of invalid kids holds as many.
        var findings = new ProbeFindings(room: 1);

        for (var counted = 0L; counted <= int.MaxValue; counted++)
        {
            findings.Take();
        }

        findings.Total.Should().Be(int.MaxValue);
        findings.Unkept.Should().Be(int.MaxValue);
    }

    [Theory]
    [MemberData(nameof(FaultyFiles))]
    public void The_report_at_any_capacity_is_the_report_of_every_finding_cut_to_its_first(string file)
    {
        // #168: each analysis writes no more findings of a kind than the report had room for when it was made, and counts
        // the rest, which its rule counts in turn. That holds the report whole only while the room shrinks as rules report,
        // each rule reports all its analysis wrote in order before counting the rest, and none reads what was written for
        // what was found: at every capacity from none to one past every finding, the report is the one that keeps every
        // finding, cut to its first, with the same counts and the same rules named.
        var (bytes, limits) = Faulty.Value[file];
        var whole = Validate(bytes, limits, int.MaxValue);
        var total = whole.Findings.Count;

        whole.SuppressedCount.Should().Be(0);
        total.Should().Be(whole.ErrorCount + whole.WarningCount + whole.InformationCount);

        for (var capacity = 0; capacity <= total + 1; capacity++)
        {
            var report = Validate(bytes, limits, capacity);
            var because = $"{file} validated at a capacity of {capacity}, of {total} findings";

            report.Findings.Should().Equal(whole.Findings.Take(capacity), because);
            report.ErrorCount.Should().Be(whole.ErrorCount, because);
            report.WarningCount.Should().Be(whole.WarningCount, because);
            report.InformationCount.Should().Be(whole.InformationCount, because);
            report.SuppressedCount.Should().Be(total - Math.Min(capacity, total), because);
            ValidationProfile.Structural.RuleIds.Where(report.Contains).Should().Equal(
                ValidationProfile.Structural.RuleIds.Where(whole.Contains), because);
        }

        static PdfValidationReport Validate(byte[] bytes, PdfReaderLimits limits, int capacity)
        {
            using var document = PdfDocument.Open(bytes, new PdfReaderOptions { Limits = limits });
            return new PdfValidator(new PdfValidatorOptions { FindingCapacity = capacity }).Validate(document);
        }
    }

    [Fact]
    public void Two_validations_of_a_document_give_the_same_findings_in_the_same_order()
    {
        var profile = Profile(
            new ReportingRule("test.first", PdfValidationSeverity.Warning, 3),
            new ReportingRule("test.second", PdfValidationSeverity.Error, 2));
        var validator = new PdfValidator(new PdfValidatorOptions { Profile = profile });
        using var document = PdfDocument.Open(SoundFile());

        var first = validator.Validate(document);
        var second = validator.Validate(document);

        second.Findings.Should().Equal(first.Findings);
    }

    [Fact]
    public void Validation_leaves_the_document_open_and_readable()
    {
        using var document = PdfDocument.Open(SoundFile());

        new PdfValidator().Validate(document);

        document.GetObject(new PdfObjectId(1)).AsDictionary().Required().IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    [Fact]
    public void Validating_a_disposed_document_throws()
    {
        var document = PdfDocument.Open(SoundFile());
        document.Dispose();

        var validating = () => new PdfValidator().Validate(document);

        validating.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Validating_a_disposed_document_throws_before_any_rule_runs()
    {
        // Whatever the rules read: one that never touches the file must not validate a closed document either.
        var document = PdfDocument.Open(SoundFile());
        document.Dispose();
        var profile = Profile(new ReportingRule("test.blind", PdfValidationSeverity.Warning, 1));

        var validating = () => new PdfValidator(new PdfValidatorOptions { Profile = profile }).Validate(document);

        validating.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Validating_no_document_throws()
    {
        var validating = () => new PdfValidator().Validate(null!);

        validating.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Options_refuse_a_negative_capacity_and_a_missing_profile()
    {
        var negative = () => new PdfValidatorOptions { FindingCapacity = -1 };
        var missing = () => new PdfValidatorOptions { Profile = null! };

        negative.Should().Throw<ArgumentOutOfRangeException>();
        missing.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_profile_refuses_a_rule_it_already_holds()
    {
        var building = () => Profile(
            new ReportingRule("test.twice", PdfValidationSeverity.Warning, 1),
            new ReportingRule("test.twice", PdfValidationSeverity.Error, 1));

        building.Should().Throw<ArgumentException>().WithMessage("*test.twice*");
    }

    [Fact]
    public void A_profile_needs_a_name_and_a_version_of_at_least_one()
    {
        var unnamed = () => new ValidationProfile("", 1, []);
        var unversioned = () => new ValidationProfile("test", 0, []);

        unnamed.Should().Throw<ArgumentException>();
        unversioned.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_guard_reached_during_validation_throws_when_the_document_was_opened_to_throw()
    {
        // ADR 36: the caller who asked for the exception gets it, from validation as from any other read.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "[" + string.Concat(Enumerable.Repeat(" 1234567890", 40)) + " ]")
            .BuildClassic(rootNumber: 1);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 128 }, ThrowOnLimit = true };
        var profile = Profile(new ResolvingRule(new PdfObjectId(3)));
        using var document = PdfDocument.Open(file, options);

        var validating = () => new PdfValidator(new PdfValidatorOptions { Profile = profile }).Validate(document);

        validating.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be("MaxObjectLength");
    }

    [Fact]
    public void A_guard_reached_during_validation_is_the_reader_s_to_report_when_the_document_was_not_opened_to_throw()
    {
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "[" + string.Concat(Enumerable.Repeat(" 1234567890", 40)) + " ]")
            .BuildClassic(rootNumber: 1);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 128 } };
        var profile = Profile(new ResolvingRule(new PdfObjectId(3)));
        using var document = PdfDocument.Open(file, options);

        var report = new PdfValidator(new PdfValidatorOptions { Profile = profile }).Validate(document);

        report.Findings.Should().BeEmpty();
        document.Diagnostics.Contains(PdfDiagnosticCodes.LimitObject).Should().BeTrue();
    }

    [Fact]
    public void A_finding_and_its_location_read_as_one_line()
    {
        var atOffset = new PdfValidationFinding(
            "file.eof-missing", PdfValidationSeverity.Warning, PdfValidationLocation.AtPosition(1234), "Missing.", "Append it.");
        var atObject = new PdfValidationFinding(
            "object.test", PdfValidationSeverity.Error, PdfValidationLocation.OfObject(new PdfObjectId(12, 1)), "Broken.", null);

        atOffset.ToString().Should().Be("Warning file.eof-missing at offset 1234: Missing.");
        atObject.ToString().Should().Be("Error object.test at object 12 1: Broken.");
        PdfValidationLocation.OfObject(new PdfObjectId(7, 2), 99).ToString().Should().Be("object 7 2, at offset 99");
        default(PdfValidationLocation).ToString().Should().Be("the document");
        default(PdfValidationLocation).IsDocument.Should().BeTrue();
        PdfValidationLocation.AtPosition(0).IsDocument.Should().BeFalse();
        PdfValidationLocation.OfObject(new PdfObjectId(12, 1)).IsDocument.Should().BeFalse();
    }

    [Fact]
    public void A_location_on_a_page_counts_pages_from_zero_and_names_them_from_one()
    {
        var onPage = PdfValidationLocation.OnPage(2, new PdfObjectId(12));
        var atOffset = PdfValidationLocation.OnPage(0, new PdfObjectId(7, 2), 99);

        onPage.PageIndex.Should().Be(2);
        onPage.Object.Should().Be(new PdfObjectId(12));
        onPage.Position.Should().BeNull();
        onPage.IsDocument.Should().BeFalse();
        onPage.ToString().Should().Be("page 3, object 12 0");
        atOffset.ToString().Should().Be("page 1, object 7 2, at offset 99");
        PdfValidationLocation.OfObject(new PdfObjectId(12)).PageIndex.Should().BeNull();
    }

    [Fact]
    public void Rule_messages_name_each_kind_of_value_a_file_writes()
    {
        RuleText.Kind(PdfInteger.Create(1)).Should().Be("a number");
        RuleText.Kind(new PdfReal(1.5)).Should().Be("a number");
        RuleText.Kind(PdfName.Get("A")).Should().Be("a name");
        RuleText.Kind(new PdfString(ReadOnlyMemory<byte>.Empty)).Should().Be("a string");
        RuleText.Kind(new PdfArray()).Should().Be("an array");
        RuleText.Kind(new PdfDictionary()).Should().Be("a dictionary");
        RuleText.Kind(new PdfStream(new PdfDictionary(), PdfStreamData.FromMemory(ReadOnlyMemory<byte>.Empty))).Should().Be("a stream");
        RuleText.Kind(PdfBoolean.True).Should().Be("a boolean");
        RuleText.Kind(new PdfReference(new PdfObjectId(1))).Should().Be("a reference");
        RuleText.Kind(PdfNull.Instance).Should().Be("null");
        RuleText.Pages(1).Should().Be("1 page");
        RuleText.Pages(2).Should().Be("2 pages");
        FileQuote.Name(PdfName.Get("A\0B")).Should().Be("/A#00B");
    }

    [Theory]
    [InlineData("/A/B/C/D/E/F/G/H", "/A/B/C/D/E/F/G/H")]
    [InlineData("/A/B/C/D/E/F/G/H/I", "/A/B/C/D (1 step) /F/G/H/I")]
    [InlineData("[0]/A[1]/B[2]/C[3]/D[4]/E[5]", "[0]/A[1]/B (3 steps) /D[4]/E[5]")]
    [InlineData("/A#2FB/C#5BD/E/F/G/H/I/J/K", "/A#2FB/C#5BD/E/F (1 step) /H/I/J/K")]
    [InlineData("/A/B/C/D/E/F/G/H/I/J/K/L/M/N/O/P/Q/R/S/T", "/A/B/C/D (12 steps) /Q/R/S/T")]
    public void A_path_of_more_than_eight_steps_keeps_its_first_and_last_four(string path, string written)
    {
        // A quoted key writes a solidus or a bracket as #xx, so only a step starts with one.
        RuleText.Path(new StringBuilder(path)).Should().Be(written);
    }

    [Fact]
    public void A_location_refuses_a_negative_offset()
    {
        var atOffset = () => PdfValidationLocation.AtPosition(-1);
        var atObject = () => PdfValidationLocation.OfObject(new PdfObjectId(1), -1);
        var onPage = () => PdfValidationLocation.OnPage(0, new PdfObjectId(1), -1);

        atOffset.Should().Throw<ArgumentOutOfRangeException>();
        atObject.Should().Throw<ArgumentOutOfRangeException>();
        onPage.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_location_refuses_a_negative_page()
    {
        var onPage = () => PdfValidationLocation.OnPage(-1, new PdfObjectId(1));

        onPage.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Severities_are_ordered_from_information_to_error()
    {
        // Callers compare them: "at least a warning" is severity >= Warning.
        Enum.GetValues<PdfValidationSeverity>().Should().Equal(
            PdfValidationSeverity.Information, PdfValidationSeverity.Warning, PdfValidationSeverity.Error);
        ((int)PdfValidationSeverity.Information).Should().BeLessThan((int)PdfValidationSeverity.Warning);
        ((int)PdfValidationSeverity.Warning).Should().BeLessThan((int)PdfValidationSeverity.Error);
    }

    internal static byte[] SoundFile() =>
        new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>")
            .BuildClassic(rootNumber: 1);

    private static ValidationProfile Profile(params IValidationRule[] rules) => new("test", 1, rules);

    public static TheoryData<string> FaultyFiles => [.. Faulty.Value.Keys];

    /// <summary>
    /// Files with several faults of each kind the analyses keep, a file each — the page tree, the objects, the index, the
    /// headers a guard cut, the object streams —, files whose sections a rule that writes its own findings judges, the
    /// small versions of the hostile files #168 measured, and every file crafted while measuring #128 and #237, with the
    /// limits each is opened under.
    /// </summary>
    private static readonly Lazy<Dictionary<string, (byte[] File, PdfReaderLimits Limits)>> Faulty = new(() =>
    {
        var files = new Dictionary<string, (byte[] File, PdfReaderLimits Limits)>(StringComparer.Ordinal)
        {
            ["page tree faults"] = (PageTreeFaults(), PdfReaderLimits.Default),
            ["object faults"] = (ObjectFaults(), PdfReaderLimits.Default),
            ["index faults"] = (IndexFaults(), PdfReaderLimits.Default),
            ["headers past the object guard"] = (HeadersPastTheGuard(), PdfReaderLimits.Default with { MaxObjectLength = 16 * 1024 }),
            ["object stream faults"] = (ObjectStreamFaults(), PdfReaderLimits.Default),
            ["section faults"] = (SectionFaults(), PdfReaderLimits.Default),
            ["a trailer without its keyword"] = (SectionFaults(keyword: false), PdfReaderLimits.Default),
            ["a table the section guard cuts"] = (LongTable(), PdfReaderLimits.Default with { MaxXRefSectionLength = 2048 }),
            ["older sections the object guard hides"] = (OlderSectionsBehindAnObject(), PdfReaderLimits.Default with { MaxObjectLength = 1024 }),
            ["invalid kids"] = (HostileInputTests.Kids(12), PdfReaderLimits.Default),
            ["invalid kids in three nodes"] = (HostileInputTests.Kids(4, nodes: 3), PdfReaderLimits.Default),
            ["kids naming object 0"] = (HostileInputTests.Kids(6, kid: "0 0 R "), PdfReaderLimits.Default),
            ["null kids in three nodes"] = (HostileInputTests.Kids(5, nodes: 3, kid: "null "), PdfReaderLimits.Default),
            ["pages written in the array"] = (HostileInputTests.KidsWrittenInTheArray(7), PdfReaderLimits.Default),
            ["nodes written in the array"] = (
                HostileInputTests.KidsWrittenInTheArray(3, "<< /Kids [<<>> << /Kids [<<>> null] >>] >> "), PdfReaderLimits.Default),
            ["entries outside the file"] = (HostileInputTests.EntriesOutsideTheFile(9), PdfReaderLimits.Default),
            ["entries at object 1"] = (HostileInputTests.EntriesAtObjectOne(9), PdfReaderLimits.Default),
            ["members the header does not list"] = (HostileInputTests.MembersTheHeaderDoesNotList(9), PdfReaderLimits.Default),
            ["members at another index"] = (HostileInputTests.ObjectStreams(2, 0, 5, everyIndexWrong: true), PdfReaderLimits.Default),
            ["object streams the index lacks"] = (HostileInputTests.ObjectStreamsTheIndexLacks(9), PdfReaderLimits.Default),
            ["objects referring to nothing"] = (HostileInputTests.ObjectsHolding(9), PdfReaderLimits.Default),
        };

        foreach (var (name, shape) in ConvergenceShapes.All)
        {
            files.Add("crafted for #128 and #237: " + name, (shape.File, shape.Limits));
        }

        return files;
    });

    /// <summary>
    /// A page tree with several faults of each kind the walk finds — kids that loop back, nodes and pages listed again,
    /// kids that are no page, nodes without kids, counts that are wrong or missing, parents that are wrong or missing,
    /// media boxes missing or no rectangle, resources missing — and three pages it does not list, its index sound.
    /// </summary>
    private static byte[] PageTreeFaults() => Objects(
        (1, "<< /Type /Catalog /Pages 2 0 R >>"),
        (2, "<< /Type /Pages /Parent 1 0 R /Count 99 /Kids [3 0 R 3 0 R 4 0 R 2 0 R 0 null 0 0 R 7 0 R 8 0 R 9 0 R 10 0 R 11 0 R 11 0 R <<>> << /Type /Page /MediaBox [0 0 5] >> 12 0 R 12 0 R 18 0 R] >>"),
        (3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] /Resources << >> >>"),
        (4, "<< /Type /Page /Parent 5 0 R >>"),
        (6, "<< /Type /Page /Parent 13 0 R /MediaBox [0 0 0 0] >>"),
        (8, "<< /Type /Pages /Parent 2 0 R >>"),
        (9, "<< /Type /Pages /Parent 2 0 R /Kids 13 0 R /Count 5 >>"),
        (10, "<< /Type /Pages /Parent 2 0 R /Kids 13 0 R /Count 1 >>"),
        (11, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 x 5] /Resources << >> >>"),
        (12, "<< /Type /Pages /Kids [12 0 R 14 0 R] /Count 0 >>"),
        (13, "[6 0 R]"),
        (14, "<< /Type /Page /Parent 12 0 R /MediaBox (box) >>"),
        (15, "<< /Type /Page /Parent 2 0 R /Contents 3 0 R >>"),
        (16, "<< /Type /Page /Parent 2 0 R >>"),
        (17, "<< /Type /Page /Contents 3 0 R >>"),
        (18, "<< /Type /Pages /Parent 2 0 R /Kids 5 /Count 0 >>"));

    /// <summary>
    /// Objects the catalog lists, three of each: referring to objects the file lacks, holding names with a null character,
    /// and written without <c>endobj</c>.
    /// </summary>
    private static byte[] ObjectFaults()
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /All [3 0 R 4 0 R 5 0 R 6 0 R 7 0 R 8 0 R 9 0 R 10 0 R 11 0 R] >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };

        for (var number = 3; number <= 5; number++)
        {
            rows[number] = file.Object(number, "<< /M 99 0 R /N 98 0 R >>");
            rows[number + 3] = file.Object(number + 3, "<< /A#00B 1 /C [/D#00] >>");
        }

        for (var number = 9; number <= 11; number++)
        {
            rows[number] = file.Position;
            file.Raw(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< /K {number} >>\n"));
        }

        file.Table(12, rows, "/Size 12 /Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// A table whose entries are wrong three of each way: the generation the object is written with, the offset a few bytes
    /// past its header, the offset outside the file.
    /// </summary>
    private static byte[] IndexFaults()
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };

        for (var number = 3; number <= 5; number++)
        {
            rows[number] = file.Object(number, "<< /K 1 >>", generation: 1);
            rows[number + 3] = file.Object(number + 3, "<< /K 2 >>") + 4;
            rows[number + 6] = 0x7FF_FFF0 + number;
        }

        file.Table(12, rows, "/Size 12 /Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>Three entries whose objects' headers lie inside a run of white space longer than the object guard it is opened under.</summary>
    private static byte[] HeadersPastTheGuard()
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var run = file.Position;
        file.Raw(new string('\0', 64 * 1024) + "x\n");

        for (var number = 3; number <= 5; number++)
        {
            rows[number] = run + ((number - 3) * 1000L);
        }

        file.Table(6, rows, "/Size 6 /Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// Object streams wrong three ways: three whose <c>/N</c> names the object each holds, circular and unreadable alike; two
    /// whose <c>/N</c> and <c>/First</c> are written as reals; and three the index places objects in and lacks.
    /// </summary>
    internal static byte[] ObjectStreamFaults()
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
        };

        foreach (var stream in (int[])[10, 20, 30])
        {
            rows[stream] = (1, file.Object(stream, string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Type /ObjStm /N {stream + 1} 0 R /First 5 /Length 7 >>\nstream\n{stream + 1} 0 0 \nendstream")), 0);
            rows[stream + 1] = (2, stream, 0);
        }

        foreach (var stream in (int[])[40, 50])
        {
            rows[stream] = (1, file.Object(stream, string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Type /ObjStm /N 1.0 /First 5.0 /Length 7 >>\nstream\n{stream + 1} 0 0 \nendstream")), 0);
            rows[stream + 1] = (2, stream, 0);
        }

        for (var member = 61; member <= 63; member++)
        {
            rows[member] = (2, member + 10, 0);
        }

        file.CrossReferenceStream(80, 81, rows, "/Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// A chain of three classic sections, apart, the oldest first, its trailer written with a syntax error, each of the
    /// others naming the one before a few bytes past where it starts; with <paramref name="keyword"/> false, a newest
    /// section whose trailer follows its rows without the trailer keyword, which ends the chain there.
    /// </summary>
    private static byte[] SectionFaults(bool keyword = true)
    {
        var text = new StringBuilder("%PDF-1.7\n");
        var catalog = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = text.Length;
        text.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

        var previous = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 3\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \n");
        text.Append("trailer\n<< /Size 3 /Root 1 0 R /Info ) >>\n");

        for (var section = 0; section < (keyword ? 2 : 3); section++)
        {
            text.Append('%').Append(' ', 300).Append('\n');
            var at = text.Length;
            var named = section < 2 ? previous + 2 + section : previous;
            text.Append(CultureInfo.InvariantCulture, $"xref\n0 1\n0000000000 65535 f \n{(section < 2 ? "trailer\n" : string.Empty)}");
            text.Append(CultureInfo.InvariantCulture, $"<< /Size 3 /Root 1 0 R /Prev {named} >>\n");
            previous = at;
        }

        text.Append(CultureInfo.InvariantCulture, $"startxref\n{previous}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.ToString());
    }

    /// <summary>A table of three hundred rows, some 6 KB, which a section guard of 2 KB cuts before its trailer.</summary>
    private static byte[] LongTable()
    {
        var builder = new TestPdfBuilder().WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>").WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        for (var number = 3; number < 300; number++)
        {
            builder.WithObject(number, "null");
        }

        return builder.BuildClassic(rootNumber: 1);
    }

    /// <summary>
    /// An update whose trailer names the older section through <c>/Prev 9 0 R</c>, object 9's header longer than an object
    /// guard of 1 KB: the older sections are hidden from the reader.
    /// </summary>
    private static byte[] OlderSectionsBehindAnObject()
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>"),
        };
        var older = file.Table(4, rows, "/Size 4 /Root 1 0 R");
        var four = file.Object(4, "(an update)");
        var nine = file.Position;
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"9 0{new string(' ', 3000)}obj\n{older}\nendobj\n"));
        var newer = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n4 1\n{four:D10} 00000 n \n9 1\n{nine:D10} 00000 n \ntrailer\n<< /Size 10 /Root 1 0 R /Prev 9 0 R >>\nstartxref\n{newer}\n%%EOF\n"));
        return file.Bytes;
    }

    /// <summary>A document whose objects are <paramref name="objects"/>, its catalog object 1, under a classic table.</summary>
    private static byte[] Objects(params (int Number, string Body)[] objects)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>();
        var size = 0;

        foreach (var (number, body) in objects)
        {
            rows[number] = file.Object(number, body);
            size = Math.Max(size, number + 1);
        }

        file.Table(size, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {size} /Root 1 0 R"));
        return file.Bytes;
    }

    /// <summary>Reports a fixed number of findings, numbered, at the document's start.</summary>
    private sealed class ReportingRule(string id, PdfValidationSeverity severity, int count) : IValidationRule
    {
        public string Id => id;

        public PdfValidationSeverity Severity => severity;

        public void Check(ValidationContext context)
        {
            for (var number = 1; number <= count; number++)
            {
                context.Report(this, PdfValidationLocation.AtPosition(0), $"Finding {number} of {id}.", remedy: null);
            }
        }
    }

    /// <summary>
    /// Writes a fixed number of findings, numbered, at the document's start, each once the report has room for it, and
    /// notes the room it saw before each.
    /// </summary>
    private sealed class AskingRule(string id, PdfValidationSeverity severity, int count) : IValidationRule
    {
        public string Id => id;

        public PdfValidationSeverity Severity => severity;

        public List<int> Rooms { get; } = [];

        public int Written { get; private set; }

        public void Check(ValidationContext context)
        {
            for (var number = 1; number <= count; number++)
            {
                Rooms.Add(context.Room);

                if (context.HasRoomFor(this))
                {
                    Written++;
                    context.Report(this, PdfValidationLocation.AtPosition(0), $"Finding {number} of {id}.", remedy: null);
                }
            }
        }
    }

    /// <summary>Counts, as many times as asked, findings its analysis did not write.</summary>
    private sealed class CountingRule(string id, PdfValidationSeverity severity, int count, int times = 1) : IValidationRule
    {
        public string Id => id;

        public PdfValidationSeverity Severity => severity;

        public void Check(ValidationContext context)
        {
            for (var time = 0; time < times; time++)
            {
                context.Count(this, count);
            }
        }
    }

    /// <summary>Resolves one object and reports nothing: what reading it costs is the reader's.</summary>
    private sealed class ResolvingRule(PdfObjectId target) : IValidationRule
    {
        public string Id => "test.resolving";

        public PdfValidationSeverity Severity => PdfValidationSeverity.Information;

        public void Check(ValidationContext context) => _ = context.Document.GetObject(target);
    }
}
