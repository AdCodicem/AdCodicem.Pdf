using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Validation;
using Json.Schema;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The corpus manifest is held to its JSON schema, <c>tests/corpus/manifest.schema.json</c>, and the schema to
/// what reads the manifest: the model in <c>Corpus.cs</c>, and the codes and rule identifiers of the library.
/// </summary>
/// <remarks>
/// The model refuses what it does not know inside <c>expect</c> and <c>readerLimits</c>, and
/// <c>fetch_remote.py</c> checks the remote entries it downloads; the schema covers every entry, at every level,
/// fetched or not, and gives an editor the same rules as the tests. It cannot see across entries — a file
/// listed twice, an archive pinned two ways —: those stay with the code.
/// </remarks>
public class CorpusManifestSchemaTests
{
    private static readonly Lazy<JsonSchema> Schema = new(() => JsonSchema.FromText(File.ReadAllText(SchemaPath)));

    private static readonly Lazy<JsonObject> SchemaDocument = new(() => JsonNode.Parse(File.ReadAllText(SchemaPath))!.AsObject());

    private static string SchemaPath => Path.Combine(Corpus.Root, "manifest.schema.json");

    [Fact]
    public void The_manifest_follows_its_schema()
    {
        var problems = Problems(File.ReadAllText(Path.Combine(Corpus.Root, "manifest.json")));

        problems.Should().BeEmpty("tests/corpus/manifest.json must follow tests/corpus/manifest.schema.json");
    }

    [Fact]
    public void A_private_manifest_follows_the_same_schema()
    {
        var path = Path.Combine(Corpus.Root, "private.json");
        Assert.SkipUnless(File.Exists(path), "No private manifest here: tests/corpus/private.json is ignored by git.");

        Problems(File.ReadAllText(path)).Should().BeEmpty("private.json has the manifest's format");
    }

    [Fact]
    public void The_manifest_names_its_schema_so_that_an_editor_applies_it()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Corpus.Root, "manifest.json")));

        manifest.RootElement.GetProperty("$schema").GetString().Should().Be("./manifest.schema.json");
    }

    [Theory]
    [MemberData(nameof(AcceptedEntries))]
    public void The_schema_accepts(string entry)
    {
        Problems(Manifest(Accepted[entry]())).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(RefusedEntries))]
    public void The_schema_refuses(string entry)
    {
        Problems(Manifest(Refused[entry]())).Should().NotBeEmpty($"the schema refuses {entry}");
    }

    [Fact]
    public void The_schema_describes_the_expectations_the_model_reads_and_no_other()
    {
        Properties("expectation").Should().BeEquivalentTo(ModelProperties(typeof(CorpusExpectation)));
    }

    [Fact]
    public void The_schema_describes_the_reader_limits_the_model_reads_and_each_must_raise_its_default()
    {
        Properties("readerLimits").Should().BeEquivalentTo(ModelProperties(typeof(CorpusReaderLimits)));

        foreach (var property in typeof(PdfReaderLimits).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType != typeof(int))
            {
                continue;
            }

            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            var definition = Definition("readerLimits")["properties"]![name]!;

            definition["exclusiveMinimum"]!.GetValue<int>().Should().Be(
                (int)property.GetValue(PdfReaderLimits.Default)!, $"{name} must raise PdfReaderLimits.Default.{property.Name}");
        }
    }

    [Fact]
    public void The_schema_describes_the_entries_the_model_reads_beside_their_provenance()
    {
        // The model leaves provenance to the scripts and to the tests that read the manifest raw.
        string[] provenance = ["builtBy", "source"];

        Properties("document").Should().BeEquivalentTo(ModelProperties(typeof(CorpusDocument)).Concat(provenance));
    }

    [Fact]
    public void The_schema_admits_the_library_s_diagnostic_codes_and_rule_identifiers_and_no_other()
    {
        var expectation = Definition("expectation")["properties"]!;

        Enumeration(expectation["requiredDiagnostics"]!).Should().BeEquivalentTo(Constants(typeof(PdfDiagnosticCodes)));
        Enumeration(expectation["findings"]!).Should().BeEquivalentTo(Constants(typeof(PdfValidationRuleIds)));
    }

    public static TheoryData<string> AcceptedEntries => [.. Accepted.Keys];

    public static TheoryData<string> RefusedEntries => [.. Refused.Keys];

    /// <summary>Entries the schema must accept, each a small variation on a sound one.</summary>
    private static readonly Dictionary<string, Func<JsonObject>> Accepted = new()
    {
        ["a committed document"] = Committed,
        ["a remote document"] = Remote,
        ["a remote document taken out of an archive"] = () => Remote().With(entry =>
            entry["source"]!["archive"] = new JsonObject { ["sha256"] = Hash, ["bytes"] = 613888, ["member"] = "Tests/T04/t04-002.pdf" }),
        ["a committed document taken out of an archive, its member named after the URL"] = () => Committed().With(entry =>
            entry["source"] = Source("https://pdfa.org/files/reference.zip (member PDFUA-Ref-2-02_Invoice.pdf)", bytes: null)),
        ["an encrypted document and its password"] = () => Committed().With(entry =>
        {
            entry["expect"]!["encrypted"] = true;
            entry["expect"]!["password"] = "";
        }),
        ["a document read under a raised limit"] = () => Committed().With(entry =>
            entry["readerLimits"] = new JsonObject { ["maxDecodedStreamLength"] = 536870912 }),
        ["a damaged document with its diagnostics and findings"] = () => Committed().With(entry =>
        {
            entry["expect"]!["clean"] = false;
            entry["expect"]!["requiredDiagnostics"] = new JsonArray("xref.rebuilt");
            entry["expect"]!["findings"] = new JsonArray("file.eof-missing");
        }),
        ["a document the library cannot meet yet"] = () => Committed().With(entry => entry["expect"]!["unsupported"] = "M2: why, and what will."),
        ["a document with no page count to expect"] = () => Committed().With(entry => entry["expect"]!["pages"] = null),
    };

    /// <summary>Entries the schema must refuse, each one mistake away from a sound one.</summary>
    private static readonly Dictionary<string, Func<JsonObject>> Refused = new()
    {
        ["an unknown key on an entry"] = () => Committed().With(entry => entry.Rename("features", "feature")),
        ["an unknown expectation"] = () => Committed().With(entry => entry["expect"]!["finding"] = new JsonArray()),
        ["an unknown key in a source"] = () => Remote().With(entry => entry["source"]!["hash"] = Hash),
        ["an unknown reader limit"] = () => Committed().With(entry => entry["readerLimits"] = new JsonObject { ["maxObjectLenght"] = 99999999 }),
        ["a reader limit that does not raise its default"] = () => Committed().With(entry =>
            entry["readerLimits"] = new JsonObject { ["maxObjectLength"] = 16777216 }),
        ["no reader limit in readerLimits"] = () => Committed().With(entry => entry["readerLimits"] = new JsonObject()),
        ["a diagnostic code the reader does not have"] = () => Committed().With(entry =>
            entry["expect"]!["requiredDiagnostics"] = new JsonArray("xref.rebuild")),
        ["a finding no rule reports"] = () => Committed().With(entry => entry["expect"]!["findings"] = new JsonArray("file.eof")),
        ["a finding declared twice"] = () => Committed().With(entry =>
            entry["expect"]!["findings"] = new JsonArray("file.eof-missing", "file.eof-missing")),
        ["a feature listed twice"] = () => Committed().With(entry => entry["features"] = new JsonArray("xref-table", "xref-table")),
        ["a feature with a space"] = () => Committed().With(entry => entry["features"] = new JsonArray("xref table")),
        ["no feature"] = () => Committed().With(entry => entry["features"] = new JsonArray()),
        ["a use case outside the categories"] = () => Committed().With(entry => entry["useCase"] = "letter"),
        ["an origin outside the four"] = () => Committed().With(entry => entry["origin"] = "vendored"),
        ["a missing title"] = () => Committed().With(entry => entry.Remove("title")),
        ["an expectation without its clean verdict"] = () => Committed().With(entry => entry["expect"]!.AsObject().Remove("clean")),
        ["an expectation without the referee's verdict"] = () => Committed().With(entry =>
            entry["expect"]!.AsObject().Remove("refereeCheckSucceeds")),
        ["a negative page count"] = () => Committed().With(entry => entry["expect"]!["pages"] = -1),
        ["catalogRecoverable set to true"] = () => Committed().With(entry => entry["expect"]!["catalogRecoverable"] = true),
        ["a conformance verdict without the claim it judges"] = () => Committed().With(entry =>
            entry["expect"]!["conformanceValid"] = true),
        ["a conformance level that is not one"] = () => Committed().With(entry => entry["expect"]!["claimsConformance"] = "PDF/A"),
        ["a password without encryption"] = () => Committed().With(entry => entry["expect"]!["password"] = "secret"),
        ["an unsupported reason naming no milestone"] = () => Committed().With(entry =>
            entry["expect"]!["unsupported"] = "the reader does not do this yet"),
        ["a remote document without a source"] = () => Remote().With(entry => entry.Remove("source")),
        ["a remote document without its size"] = () => Remote().With(entry => entry["source"]!.AsObject().Remove("bytes")),
        ["a remote document outside remote/"] = () => Remote().With(entry => entry["file"] = "vendor/somewhere/document.pdf"),
        ["a committed document in remote/"] = () => Committed().With(entry => entry["file"] = "remote/somewhere/document.pdf"),
        ["a remote URL with a member named after it"] = () => Remote().With(entry =>
            entry["source"]!["url"] = "https://example.org/archive.zip (member document.pdf)"),
        ["a SHA-256 in capitals"] = () => Remote().With(entry => entry["source"]!["sha256"] = Hash.ToUpperInvariant()),
        ["a retrieval date in another format"] = () => Remote().With(entry => entry["source"]!["retrieved"] = "26/09/2026"),
        ["an archive without its pin"] = () => Remote().With(entry => entry["source"]!["archive"] = new JsonObject { ["member"] = "a.pdf" }),
        ["an archive member that climbs out"] = () => Remote().With(entry =>
            entry["source"]!["archive"] = new JsonObject { ["sha256"] = Hash, ["bytes"] = 1, ["member"] = "../escape.pdf" }),
        ["an archive member given as an absolute path"] = () => Remote().With(entry =>
            entry["source"]!["archive"] = new JsonObject { ["sha256"] = Hash, ["bytes"] = 1, ["member"] = "/etc/document.pdf" }),
        ["a file that is not a PDF under the corpus"] = () => Committed().With(entry => entry["file"] = "elsewhere/document.pdf"),
        ["a script that builds nothing"] = () => Committed().With(entry => entry["builtBy"] = "make_corpus.py"),
    };

    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static JsonObject Committed() => new()
    {
        ["file"] = "documents/invoice/an-invoice.pdf",
        ["title"] = "An invoice",
        ["useCase"] = "invoice",
        ["producer"] = "A producer 1.0",
        ["origin"] = "generated",
        ["licence"] = "MIT (our own source)",
        ["features"] = new JsonArray("xref-table"),
        ["expect"] = new JsonObject
        {
            ["pages"] = 1,
            ["clean"] = true,
            ["indexRebuilt"] = false,
            ["requiredDiagnostics"] = new JsonArray(),
            ["refereeCheckSucceeds"] = true,
        },
    };

    private static JsonObject Remote() => Committed().With(entry =>
    {
        entry["file"] = "remote/somewhere/a-document.pdf";
        entry["origin"] = "remote";
        entry["licence"] = "Usable, not redistributable";
        entry["source"] = Source("https://example.org/a-document.pdf", bytes: 1024);
    });

    private static JsonObject Source(string url, int? bytes)
    {
        var source = new JsonObject { ["url"] = url, ["sha256"] = Hash, ["retrieved"] = "2026-09-26" };

        if (bytes is { } size)
        {
            source["bytes"] = size;
        }

        return source;
    }

    private static string Manifest(JsonObject entry) =>
        new JsonObject { ["$schema"] = "./manifest.schema.json", ["documents"] = new JsonArray(entry) }.ToJsonString();

    /// <summary>What the schema finds wrong with a manifest, one line per failed keyword and instance location.</summary>
    private static List<string> Problems(string manifest)
    {
        using var document = JsonDocument.Parse(manifest);
        var results = Schema.Value.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

        return results.IsValid
            ? []
            : [.. (results.Details ?? [])
                .Where(detail => detail.Errors is { Count: > 0 })
                .SelectMany(detail => detail.Errors!.Select(error => $"{detail.InstanceLocation}: {error.Key}: {error.Value}"))
                .Take(50)];
    }

    private static JsonObject Definition(string name) => SchemaDocument.Value["$defs"]![name]!.AsObject();

    private static List<string> Properties(string definition) =>
        [.. Definition(definition)["properties"]!.AsObject().Select(property => property.Key)];

    /// <summary>The manifest keys a model type reads: its public properties, camel-cased as the loader reads them.</summary>
    private static List<string> ModelProperties(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))];

    private static List<string> Enumeration(JsonNode array) =>
        [.. array["items"]!["enum"]!.AsArray().Select(value => value!.GetValue<string>())];

    private static List<string> Constants(Type type) =>
        [.. type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)];
}

/// <summary>Small edits on a manifest entry, so that each case reads as the one mistake it makes.</summary>
internal static class ManifestEntryEdits
{
    public static JsonObject With(this JsonObject entry, Action<JsonObject> edit)
    {
        edit(entry);
        return entry;
    }

    public static void Rename(this JsonObject entry, string from, string to)
    {
        var value = entry[from];
        entry.Remove(from);
        entry[to] = value;
    }
}
