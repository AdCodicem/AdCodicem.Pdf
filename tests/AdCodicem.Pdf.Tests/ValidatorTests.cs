using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
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
        ValidationProfile.Structural.RuleIds.Should().Equal(PdfValidationRuleIds.FileEofMissing);
        ValidationProfile.Structural.ToString().Should().Be("structural 1");
    }

    [Fact]
    public void Findings_come_in_the_profile_s_order_then_in_the_order_each_rule_found_them()
    {
        var profile = Profile(
            new ReportingRule("test.first", PdfValidationSeverity.Warning, 2),
            new ReportingRule("test.second", PdfValidationSeverity.Error, 1),
            new ReportingRule("test.third", PdfValidationSeverity.Information, 2));
        using var document = PdfDocument.Open(SoundFile());

        var report = new PdfValidator(new PdfValidatorOptions { Profile = profile }).Validate(document);

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
        PdfValidationLocation.OfObject(new PdfObjectId(7), 99).ToString().Should().Be("object 7 0, at offset 99");
        default(PdfValidationLocation).ToString().Should().Be("the document");
        default(PdfValidationLocation).IsDocument.Should().BeTrue();
        PdfValidationLocation.AtPosition(0).IsDocument.Should().BeFalse();
    }

    [Fact]
    public void A_location_refuses_a_negative_offset()
    {
        var atOffset = () => PdfValidationLocation.AtPosition(-1);
        var atObject = () => PdfValidationLocation.OfObject(new PdfObjectId(1), -1);

        atOffset.Should().Throw<ArgumentOutOfRangeException>();
        atObject.Should().Throw<ArgumentOutOfRangeException>();
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

    /// <summary>Resolves one object and reports nothing: what reading it costs is the reader's.</summary>
    private sealed class ResolvingRule(PdfObjectId target) : IValidationRule
    {
        public string Id => "test.resolving";

        public PdfValidationSeverity Severity => PdfValidationSeverity.Information;

        public void Check(ValidationContext context) => _ = context.Document.GetObject(target);
    }
}
