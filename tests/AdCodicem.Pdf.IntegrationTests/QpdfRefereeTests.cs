using System.Text.Json;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.IntegrationTests;

/// <summary>
/// Checks our reading of the corpus against qpdf's, running in a container.
/// </summary>
/// <remarks>
/// These tests do not exercise our code: they establish that the manifest tells the truth. If qpdf and
/// the manifest disagree about how many pages a document has, or about whether it is damaged, then every
/// unit test asserting against that manifest is built on sand.
/// </remarks>
[Collection(RefereeCollection.Name)]
public class QpdfRefereeTests(RefereeContainer referee)
{
    [Theory]
    [MemberData(nameof(AllDocuments))]
    public async Task Qpdfs_verdict_is_the_one_the_manifest_records(string file)
    {
        Assert.SkipWhen(referee.Unavailable is not null, referee.Unavailable ?? string.Empty);

        var expected = Corpus.Get(file).Expect.RefereeCheckSucceeds;
        var (exitCode, stdout, stderr) = await referee.RunAsync("qpdf", "--check", RefereeContainer.PathInContainer(file));

        // The manifest records what this very command answered when the corpus was built. A disagreement
        // means the corpus has drifted, or the referee's version has — either way, something to look at
        // rather than something to assume.
        (exitCode == 0).Should().Be(
            expected!.Value,
            $"qpdf --check on {file} said:\n{stdout}{stderr}");
    }

    [Theory]
    [MemberData(nameof(DocumentsWithKnownPageCount))]
    public async Task The_manifest_page_count_is_what_an_independent_tool_counts(string file)
    {
        Assert.SkipWhen(referee.Unavailable is not null, referee.Unavailable ?? string.Empty);

        var entry = Corpus.Get(file);
        var expected = entry.Expect.Pages;

        // A catalog the trailer does not designate is the reader's choice, which file.root-invalid reports: qpdf
        // takes the /Root the file gives and walks what it names, so the pages it counts are another document's.
        Assert.SkipWhen(
            entry.Expect.Findings.Contains(PdfValidationRuleIds.FileRootInvalid),
            $"{file}: the reader counts the pages of a catalog it chose, which qpdf does not look for");

        // qpdf's walk of the page tree, not --show-npages, which gives the root's /Count: a /Count that lies is a
        // fault the validator reports, and the pages are the ones the tree lists (M02).
        var (exitCode, stdout, stderr) = await referee.RunAsync(
            "qpdf", "--json=2", "--json-key=pages", RefereeContainer.PathInContainer(file));

        // Exit code 3 is a count delivered with warnings on standard error, which is still a count: files
        // from the field often carry a /Size one too large or a stale linearization hint. Exit code 2 is a tree
        // qpdf could not walk at all — a loop, a kid it cannot read —, whose faults the validator's findings name.
        Assert.SkipWhen(exitCode == 2, $"qpdf could not walk the pages of {file}:\n{stderr}");
        using var json = JsonDocument.Parse(stdout);
        json.RootElement.GetProperty("pages").GetArrayLength().Should().Be(expected!.Value, $"the manifest claims {expected} pages for {file}");
    }

    public static TheoryData<string> AllDocuments =>
        Theory(Corpus.PathsWhere(document => document.Expect.RefereeCheckSucceeds is not null));

    public static TheoryData<string> DocumentsWithKnownPageCount =>
        Theory(Corpus.PathsWhere(document =>
            document.Expect is { Pages: not null, Encrypted: false }));

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
