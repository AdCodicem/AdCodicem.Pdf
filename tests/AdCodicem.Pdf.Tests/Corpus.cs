using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdCodicem.Pdf.Tests;

/// <summary>What the manifest says about one corpus document.</summary>
internal sealed record CorpusExpectation
{
    /// <summary>Page count, established by an independent tool, or null when nothing is recoverable.</summary>
    public int? Pages { get; init; }

    /// <summary>Whether the file is well formed: a clean document must produce no repair and no warning.</summary>
    public bool Clean { get; init; } = true;

    /// <summary>Whether the reader is expected to rebuild the cross-reference index.</summary>
    public bool IndexRebuilt { get; init; }

    /// <summary>Diagnostic codes the reader must report for this document.</summary>
    public string[] RequiredDiagnostics { get; init; } = [];

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
}

/// <summary>One entry of the corpus manifest.</summary>
internal sealed record CorpusDocument
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
internal static class Corpus
{
    private static readonly Lazy<(string Root, IReadOnlyList<CorpusDocument> Documents)> Loaded = new(Load);

    /// <summary>Gets the corpus directory.</summary>
    public static string Root => Loaded.Value.Root;

    /// <summary>Gets every document described by the manifest.</summary>
    public static IReadOnlyList<CorpusDocument> Documents => Loaded.Value.Documents;

    /// <summary>Gets the relative paths of every document, for use as test data.</summary>
    public static TheoryData<string> Paths
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var document in Documents)
            {
                data.Add(document.File);
            }

            return data;
        }
    }

    /// <summary>Gets the documents of one use case.</summary>
    public static TheoryData<string> PathsWithFeature(params string[] features)
    {
        var data = new TheoryData<string>();
        foreach (var document in Documents)
        {
            if (features.Any(feature => document.Features.Contains(feature)))
            {
                data.Add(document.File);
            }
        }

        return data;
    }

    /// <summary>Returns the manifest entry for a relative path.</summary>
    public static CorpusDocument Get(string file) =>
        Documents.FirstOrDefault(document => document.File == file)
        ?? throw new InvalidOperationException($"'{file}' is not in the corpus manifest.");

    /// <summary>Returns the absolute path of a corpus document.</summary>
    public static string PathOf(string file) => System.IO.Path.Combine(Root, file);

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
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                using var stream = System.IO.File.OpenRead(candidate);
                using var json = JsonDocument.Parse(stream);
                var documents = json.RootElement.GetProperty("documents").Deserialize<List<CorpusDocument>>(options)
                    ?? throw new InvalidOperationException("The corpus manifest has no documents.");

                return (System.IO.Path.GetDirectoryName(candidate)!, documents);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "tests/corpus/manifest.json was not found. Build the corpus with tests/corpus/build/build_corpus.py.");
    }
}
