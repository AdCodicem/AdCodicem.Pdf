using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.IntegrationTests;

/// <summary>
/// Checks the structural profile's verdicts on each corpus document's file structure and cross-references against
/// qpdf's, running in a container.
/// </summary>
/// <remarks>
/// <para>
/// qpdf is the corpus's referee, and has no validation profile of its own: what it says is what it did. When it
/// throws the file's index away — "Attempting to reconstruct cross-reference table" —, something in the file's
/// structure is wrong, and the validator must say what, in the <c>file</c> or <c>xref</c> family. And when the
/// validator calls that structure an error — the reader cannot vouch that it reads the file as written (ADR 45) —,
/// qpdf must find fault with the file too: its <c>--check</c> exits with something other than 0.
/// </para>
/// <para>
/// A warning is not held to the second direction: a file every reader reads as it was meant may pass qpdf's check,
/// and the validator still says what is wrong with it. On the corpus of 2026-09-27 the two agree on every document
/// in both directions, without an exception to name.
/// </para>
/// </remarks>
[Collection(RefereeCollection.Name)]
public class ValidationRefereeTests(RefereeContainer referee)
{
    /// <summary>What qpdf prints when it gives up on a file's index and scans the file for its objects.</summary>
    private const string Reconstructs = "Attempting to reconstruct cross-reference table";

    public static TheoryData<string> AllDocuments => Theory(Corpus.Paths);

    [Theory]
    [MemberData(nameof(AllDocuments))]
    public async Task A_document_whose_index_qpdf_throws_away_earns_a_file_or_cross_reference_finding(string file)
    {
        Assert.SkipWhen(referee.Unavailable is not null, referee.Unavailable ?? string.Empty);

        var entry = Corpus.Get(file);
        var (_, output) = await Check(entry);

        if (!output.Contains(Reconstructs, StringComparison.Ordinal))
        {
            return;
        }

        Validate(entry).Findings.Should().Contain(
            finding => IsStructural(finding), $"qpdf rebuilds the index of {file}, and said:\n{output}");
    }

    [Theory]
    [MemberData(nameof(AllDocuments))]
    public async Task A_structural_error_is_a_document_qpdf_finds_fault_with(string file)
    {
        Assert.SkipWhen(referee.Unavailable is not null, referee.Unavailable ?? string.Empty);

        var entry = Corpus.Get(file);
        var errors = Validate(entry).Findings
            .Where(finding => IsStructural(finding) && finding.Severity == PdfValidationSeverity.Error)
            .Select(finding => finding.ToString())
            .ToList();

        if (errors.Count == 0)
        {
            return;
        }

        var (exitCode, output) = await Check(entry);

        exitCode.Should().NotBe(0, $"the validator calls {file} broken — {string.Join("; ", errors)} — and qpdf said:\n{output}");
    }

    private static bool IsStructural(PdfValidationFinding finding) =>
        finding.RuleId.StartsWith("file.", StringComparison.Ordinal) || finding.RuleId.StartsWith("xref.", StringComparison.Ordinal);

    /// <summary>Runs <c>qpdf --check</c> on a document, with its password when it has one.</summary>
    private async Task<(long? ExitCode, string Output)> Check(CorpusDocument entry)
    {
        var path = RefereeContainer.PathInContainer(entry.File);
        var (exitCode, stdout, stderr) = entry.Expect.Password is { } password
            ? await referee.RunAsync("qpdf", "--check", $"--password={password}", path)
            : await referee.RunAsync("qpdf", "--check", path);

        return (exitCode, stdout + stderr);
    }

    /// <summary>
    /// Validates a document as the corpus's own tests do: under its raised reader limits, if any, and encrypted or
    /// not — its structure is readable without its key.
    /// </summary>
    private static PdfValidationReport Validate(CorpusDocument entry)
    {
        var limits = PdfReaderLimits.Default;

        if (entry.ReaderLimits is { } raised)
        {
            limits = limits with
            {
                MaxDecodedStreamLength = raised.MaxDecodedStreamLength ?? limits.MaxDecodedStreamLength,
                MaxObjectLength = raised.MaxObjectLength ?? limits.MaxObjectLength,
                MaxXRefSectionLength = raised.MaxXRefSectionLength ?? limits.MaxXRefSectionLength,
                MaxXRefSectionCount = raised.MaxXRefSectionCount ?? limits.MaxXRefSectionCount,
                MaxTrailerLength = raised.MaxTrailerLength ?? limits.MaxTrailerLength,
            };
        }

        using var document = PdfDocument.Open(Corpus.Read(entry.File), new PdfReaderOptions { Limits = limits, ThrowOnEncrypted = false });
        return new PdfValidator().Validate(document);
    }

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
