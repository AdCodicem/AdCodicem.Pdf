using System.Diagnostics;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The acceptance conditions of M1, run against real documents from real producers.
/// </summary>
/// <remarks>
/// Hand-built files prove the reader handles what we imagined; these prove it handles what Chromium,
/// LibreOffice, ReportLab and qpdf actually emit — and what a damaged one of those looks like. See
/// docs/corpus.md.
/// </remarks>
public class CorpusReadingTests
{
    private static readonly TimeSpan OpenBudget = TimeSpan.FromSeconds(20);

    [Fact]
    public void The_corpus_manifest_describes_every_document_present()
    {
        var onDisk = new[] { "documents", "vendor", "private" }
            .Select(folder => Path.Combine(Corpus.Root, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.pdf", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(Corpus.Root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order()
            .ToList();

        var described = Corpus.Documents.Select(document => document.File).Order().ToList();

        // A document nobody asserts anything about is not part of the corpus, it is clutter.
        described.ShouldBe(onDisk);
    }

    [Fact]
    public void The_corpus_covers_several_producers_and_every_use_case()
    {
        Corpus.Documents.Select(document => document.Producer.Split(' ')[0]).Distinct().Count()
            .ShouldBeGreaterThanOrEqualTo(3, "the shape of a cross-reference section is a producer's signature");

        var useCases = Corpus.Documents.Select(document => document.UseCase).Distinct().ToList();
        useCases.ShouldContain("invoice");
        useCases.ShouldContain("report");
        useCases.ShouldContain("contract");
        useCases.ShouldContain("form");
        useCases.ShouldContain("scan");
        useCases.ShouldContain("archival");
    }

    [Theory]
    [MemberData(nameof(AllDocuments))]
    public void Every_corpus_document_opens_as_its_manifest_describes(string file)
    {
        var entry = Corpus.Get(file);

        if (entry.Expect.Encrypted)
        {
            // Decryption arrives in M9; until then the refusal must be typed and immediate.
            Should.Throw<PdfEncryptedException>(() => PdfDocument.Open(Corpus.Read(file)));
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        using var document = PdfDocument.Open(Corpus.Read(file));
        stopwatch.Stop();

        stopwatch.Elapsed.ShouldBeLessThan(OpenBudget, $"opening {entry.Name} must not take unbounded time");

        document.Catalog.ShouldNotBeNull($"{entry.Name} has no document catalogue");
        document.WasRepaired.ShouldBe(entry.Expect.IndexRebuilt, $"{entry.Name}: unexpected rebuild state");

        // Judge the diagnostics on a full read, not on opening: a lying /Length is only discovered when
        // the stream it describes is actually decoded, which is the lazy reader behaving as designed.
        ReadEverything(document);

        foreach (var code in entry.Expect.RequiredDiagnostics)
        {
            document.Diagnostics.Contains(code).ShouldBeTrue(
                $"{entry.Name} should report '{code}', reported: {Describe(document.Diagnostics)}");
        }

        if (entry.Expect.Clean)
        {
            // A well-formed document must not need any repair, and must not worry the reader.
            var noise = document.Diagnostics
                .Where(entry => entry.Severity is PdfDiagnosticSeverity.Repair or PdfDiagnosticSeverity.Warning)
                .ToList();

            noise.ShouldBeEmpty($"{entry.Name} is well formed but produced {Describe(document.Diagnostics)}");
        }

        if (entry.Expect.Pages is { } expectedPages)
        {
            CountPages(document).ShouldBe(expectedPages, $"{entry.Name}: page count");
        }
    }

    [Theory]
    [MemberData(nameof(LargeDocuments))]
    public void Opening_does_not_read_the_content_of(string file)
    {
        var source = new CountingSource(Corpus.Read(file));
        var size = source.Length;

        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        document.Catalog.ShouldNotBeNull();

        var readAtOpen = source.BytesRead;

        // Indexing touches the header, the tail and the cross-reference sections — never the page content.
        readAtOpen.ShouldBeLessThan(
            size / 4,
            $"{Corpus.Get(file).Name}: opening read {readAtOpen} of {size} bytes, so content was read eagerly");
    }

    [Fact]
    public void Reading_every_page_of_the_largest_document_stays_within_its_memory_budget()
    {
        var entry = Corpus.Documents.First(document => document.Features.Contains("many-pages"));
        var bytes = Corpus.Read(entry.File);

        // Per-thread, not process-wide: the suite runs in parallel and a process-wide counter would
        // measure whatever else happens to be running.
        var before = GC.GetAllocatedBytesForCurrentThread();
        using (var document = PdfDocument.Open(bytes))
        {
            CountPages(document).ShouldBe(entry.Expect.Pages!.Value);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Indexing a thousand-page document and walking its whole page tree measured 2.4 MB, roughly
        // 2.4 KB per page: proportional to the number of objects, not to the weight of the content.
        // The budget leaves headroom for producer variation and fails loudly on a regression.
        allocated.ShouldBeLessThan(
            4 * 1024 * 1024,
            $"indexing {entry.Name} allocated {allocated / 1024} KB");
    }

    [Theory]
    [MemberData(nameof(DamagedDocuments))]
    public void Damaged_documents_are_recovered_as_far_as_an_independent_tool_recovers_them(string file)
    {
        var entry = Corpus.Get(file);

        using var document = PdfDocument.Open(Corpus.Read(file));

        document.Catalog.ShouldNotBeNull($"{entry.Name}: qpdf recovers a catalogue here, so must we");
        ReadEverything(document);
        document.Diagnostics.Count.ShouldBeGreaterThan(0, $"{entry.Name}: damage must never be silent");

        if (entry.Expect.Pages is { } expectedPages)
        {
            CountPages(document).ShouldBe(expectedPages, $"{entry.Name}: recovered page count");
        }
    }

    public static TheoryData<string> AllDocuments => Corpus.Paths;

    public static TheoryData<string> LargeDocuments => Corpus.PathsWithFeature("many-pages", "dct-image");

    public static TheoryData<string> DamagedDocuments
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var document in Corpus.Documents.Where(document => !document.Expect.Clean))
            {
                data.Add(document.File);
            }

            return data;
        }
    }

    /// <summary>
    /// Resolves every object and decodes every stream, so that anything wrong with the file has had the
    /// chance to be noticed. Returns the number of decoded bytes, to keep the work from being elided.
    /// </summary>
    private static long ReadEverything(PdfDocument document)
    {
        long decoded = 0;

        foreach (var number in document.ObjectNumbers.ToList())
        {
            if (document.GetObject(new PdfObjectId(number)) is PdfStream stream && !stream.HasImageFilter())
            {
                decoded += stream.Decode(document.Diagnostics).Length;
            }
        }

        return decoded;
    }

    /// <summary>
    /// Walks the page tree. M1 has no page API — that is M5 — so the traversal lives here, which also
    /// exercises reference resolution and inherited structure across every producer in the corpus.
    /// </summary>
    private static int CountPages(PdfDocument document)
    {
        var root = document.Catalog.GetDictionary(PdfName.Pages);
        return root is null ? 0 : CountPages(root, [], depth: 0);
    }

    private static int CountPages(PdfDictionary node, HashSet<PdfDictionary> visited, int depth)
    {
        if (depth > 64 || !visited.Add(node))
        {
            return 0;
        }

        var kids = node.GetArray(PdfName.Kids);

        if (kids is null)
        {
            return node.IsOfType(PdfName.Page) ? 1 : 0;
        }

        var total = 0;

        for (var index = 0; index < kids.Count; index++)
        {
            if (kids.Resolved(index).AsDictionary() is { } kid)
            {
                total += kid.GetArray(PdfName.Kids) is null && !kid.IsOfType(PdfName.Pages)
                    ? 1
                    : CountPages(kid, visited, depth + 1);
            }
        }

        return total;
    }

    private static string Describe(PdfDiagnostics diagnostics) =>
        diagnostics.Count == 0 ? "no diagnostics" : string.Join("; ", diagnostics.Select(entry => entry.ToString()));

    private sealed class CountingSource : PdfFileSource
    {
        private readonly PdfFileSource _inner;

        public CountingSource(byte[] data) => _inner = FromMemory(data);

        public long BytesRead { get; private set; }

        public override long Length => _inner.Length;

        public override int Read(long offset, Span<byte> buffer)
        {
            var read = _inner.Read(offset, buffer);
            BytesRead += read;
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }
        }
    }
}
