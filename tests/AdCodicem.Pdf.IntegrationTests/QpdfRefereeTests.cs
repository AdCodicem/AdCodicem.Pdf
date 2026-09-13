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
    [MemberData(nameof(CleanDocuments))]
    public async Task A_well_formed_document_passes_qpdfs_own_check(string file)
    {
        Assert.SkipWhen(referee.Unavailable is not null, referee.Unavailable ?? string.Empty);

        var (exitCode, output) = await referee.RunAsync("qpdf", "--check", RefereeContainer.PathInContainer(file));

        // qpdf answers 0 when it is happy, 3 when it has warnings, and 2 when the file has real errors.
        exitCode.Should().NotBe(2, $"qpdf reports errors in {file}:\n{output}");
    }

    [Theory]
    [MemberData(nameof(DamagedDocuments))]
    public async Task A_damaged_document_makes_qpdf_complain(string file)
    {
        Assert.SkipWhen(referee.Unavailable is not null, referee.Unavailable ?? string.Empty);

        var (exitCode, output) = await referee.RunAsync("qpdf", "--check", RefereeContainer.PathInContainer(file));

        exitCode.Should().NotBe(0, $"{file} is damaged on purpose, yet qpdf saw nothing:\n{output}");
    }

    [Theory]
    [MemberData(nameof(DocumentsWithKnownPageCount))]
    public async Task The_manifest_page_count_is_what_an_independent_tool_counts(string file)
    {
        Assert.SkipWhen(referee.Unavailable is not null, referee.Unavailable ?? string.Empty);

        var expected = Corpus.Get(file).Expect.Pages;
        var (exitCode, output) = await referee.RunAsync(
            "qpdf", "--show-npages", RefereeContainer.PathInContainer(file));

        exitCode.Should().NotBe(2, $"qpdf could not count the pages of {file}:\n{output}");
        int.Parse(output.Trim()).Should().Be(expected!.Value, $"the manifest claims {expected} pages for {file}");
    }

    public static TheoryData<string> CleanDocuments =>
        Theory(Corpus.PathsWhere(document => document.Expect is { Clean: true, Encrypted: false }));

    public static TheoryData<string> DamagedDocuments =>
        Theory(Corpus.PathsWhere(document => !document.Expect.Clean));

    public static TheoryData<string> DocumentsWithKnownPageCount =>
        Theory(Corpus.PathsWhere(document =>
            document.Expect is { Pages: not null, Encrypted: false, Clean: true }));

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
