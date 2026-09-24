using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdCodicem.Pdf.TestSupport;

/// <summary>What the manifest says about one corpus document.</summary>
public sealed record CorpusExpectation
{
    /// <summary>Page count, established by an independent tool, or null when nothing is recoverable.</summary>
    public int? Pages { get; init; }

    /// <summary>Whether the file is well formed: a clean document must produce no repair and no warning.</summary>
    public bool Clean { get; init; } = true;

    /// <summary>Whether the reader is expected to rebuild the cross-reference index.</summary>
    public bool IndexRebuilt { get; init; }

    /// <summary>Diagnostic codes the reader must report for this document.</summary>
    public string[] RequiredDiagnostics { get; init; } = [];

    /// <summary>
    /// Whether <c>qpdf --check</c> finds nothing wrong with the file.
    /// </summary>
    /// <remarks>
    /// Recorded per document by running that exact command when the corpus is built, never inferred.
    /// Damage and rejection are not the same thing: junk before the header shifts every offset, and qpdf
    /// adjusts to it without a word — while an encrypted document it has no password for is refused
    /// although nothing is wrong with it.
    /// </remarks>
    public bool? RefereeCheckSucceeds { get; init; }

    /// <summary>Whether the document is encrypted, and with which password.</summary>
    public bool Encrypted { get; init; }

    public string? Password { get; init; }

    /// <summary>Text that extraction must find, once extraction exists (M10).</summary>
    public string[]? TextContains { get; init; }

    /// <summary>Embedded files the document carries (M10).</summary>
    public string[]? Attachments { get; init; }

    /// <summary>Interactive form fields the document declares (M11).</summary>
    public string[]? FormFields { get; init; }

    /// <summary>False for image-only documents, which must report no text rather than noise (M10).</summary>
    public bool? HasExtractableText { get; init; }

    /// <summary>The conformance level the document claims, to be verified once the conformance profiles exist (M12).</summary>
    public string? ClaimsConformance { get; init; }

    /// <summary>Whether an independent validator (veraPDF) upholds the claimed conformance level (M12).</summary>
    public bool? ConformanceValid { get; init; }

    /// <summary>
    /// Why the library cannot yet meet this document's expectations, and the milestone that will: the
    /// acceptance tests skip the document with this reason instead of failing.
    /// </summary>
    /// <remarks>
    /// A known gap is written down where the next session will read it — never deleted, never silent
    /// (docs/corpus.md). The expectations themselves stay as the independent tool established them; only
    /// the assertion waits for the milestone.
    /// </remarks>
    public string? Unsupported { get; init; }
}

/// <summary>One entry of the corpus manifest.</summary>
public sealed record CorpusDocument
{
    public required string File { get; init; }

    public required string Title { get; init; }

    public string UseCase { get; init; } = "unknown";

    public string Producer { get; init; } = "unknown";

    public string Origin { get; init; } = "generated";

    public string Licence { get; init; } = "unknown";

    public string[] Features { get; init; } = [];

    public CorpusExpectation Expect { get; init; } = new();

    [JsonIgnore]
    public string Name => System.IO.Path.GetFileName(File);
}

/// <summary>
/// The corpus of real documents, as described by its manifest.
/// </summary>
/// <remarks>
/// Tests read the manifest rather than hard-coding file names, so adding a document to the corpus adds it
/// to every acceptance test at once — and a document nobody asserts anything about cannot hide in the tree.
/// </remarks>
public static class Corpus
{
    private static readonly Lazy<(string Root, IReadOnlyList<CorpusDocument> Documents)> Loaded = new(Load);

    private static readonly JsonSerializerOptions ManifestOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Gets the corpus directory.</summary>
    public static string Root => Loaded.Value.Root;

    /// <summary>Gets every document described by the manifest.</summary>
    public static IReadOnlyList<CorpusDocument> Documents => Loaded.Value.Documents;

    /// <summary>Gets the relative path of every document, for use as test data.</summary>
    public static IReadOnlyList<string> Paths => [.. Documents.Select(document => document.File)];

    /// <summary>Gets the paths of the documents carrying any of the given features.</summary>
    public static IReadOnlyList<string> PathsWithFeature(params string[] features) =>
        [.. Documents.Where(document => features.Any(document.Features.Contains)).Select(document => document.File)];

    /// <summary>Gets the paths of the documents matching a predicate.</summary>
    public static IReadOnlyList<string> PathsWhere(Func<CorpusDocument, bool> predicate) =>
        [.. Documents.Where(predicate).Select(document => document.File)];

    /// <summary>Returns the manifest entry for a relative path.</summary>
    public static CorpusDocument Get(string file) =>
        Documents.FirstOrDefault(document => document.File == file)
        ?? throw new InvalidOperationException($"'{file}' is not in the corpus manifest.");

    /// <summary>Returns the absolute path of a corpus document.</summary>
    /// <remarks>
    /// A manifest entry names a document relative to the corpus root. An entry that is rooted, or that
    /// climbs out of the corpus, is refused rather than followed: a private manifest is hand-written and
    /// the tests open whatever it names.
    /// </remarks>
    public static string PathOf(string file) => Resolve(Root, file);

    /// <summary>Reads a corpus document.</summary>
    public static byte[] Read(string file) => System.IO.File.ReadAllBytes(PathOf(file));

    private static (string, IReadOnlyList<CorpusDocument>) Load()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = System.IO.Path.Combine(directory.FullName, "tests", "corpus", "manifest.json");
            if (System.IO.File.Exists(candidate))
            {
                var root = System.IO.Path.GetDirectoryName(candidate)!;
                var documents = ReadManifest(candidate)
                    ?? throw new InvalidOperationException("The corpus manifest has no documents.");

                // Documents we may use but not redistribute (origin "remote") are fetched on demand into
                // tests/corpus/remote/, which git ignores (ADR 32). Only those actually fetched join the
                // corpus, so a run without the network tests exactly what is committed.
                documents.RemoveAll(document =>
                    document.Origin == "remote" && !System.IO.File.Exists(Resolve(root, document.File)));

                // An optional private manifest lets a team test against confidential documents without
                // committing them: tests/corpus/private/ and private.json are ignored by git, and
                // everything simply runs on the public corpus when they are absent.
                var privateManifest = System.IO.Path.Combine(root, "private.json");
                if (System.IO.File.Exists(privateManifest) && ReadManifest(privateManifest) is { } confidential)
                {
                    documents.AddRange(confidential);
                }

                return (root, documents);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "tests/corpus/manifest.json was not found. Build the corpus with tests/corpus/build/build_corpus.py.");
    }

    private static string Resolve(string root, string file)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);

        var resolved = System.IO.Path.GetFullPath(System.IO.Path.Join(root, file));

        if (System.IO.Path.IsPathRooted(file) ||
            !resolved.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"'{file}' is not a path inside the corpus.");
        }

        return resolved;
    }

    private static List<CorpusDocument>? ReadManifest(string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.GetProperty("documents").Deserialize<List<CorpusDocument>>(ManifestOptions);
    }
}
