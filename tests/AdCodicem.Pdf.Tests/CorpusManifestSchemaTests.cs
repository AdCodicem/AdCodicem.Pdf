using System.Globalization;
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
        // Each case is one mistake away from a sound entry, and must fail on the keyword that names that mistake:
        // a case refused for some other reason would leave its own rule untested.
        var (build, keyword) = Refused[entry];

        Problems(Manifest(build())).Should().Contain(
            problem => problem.Contains($": {keyword}: ", StringComparison.Ordinal), $"the schema refuses {entry} under '{keyword}'");
    }

    [Theory]
    [MemberData(nameof(RefusedManifests))]
    public void The_schema_refuses_a_manifest_with(string manifest)
    {
        var (json, keyword) = RefusedWholes[manifest];

        Problems(json).Should().Contain(
            problem => problem.Contains($": {keyword}: ", StringComparison.Ordinal), $"the schema refuses a manifest with {manifest}");
    }

    [Theory]
    [InlineData("file")]
    [InlineData("title")]
    [InlineData("useCase")]
    [InlineData("producer")]
    [InlineData("origin")]
    [InlineData("licence")]
    [InlineData("features")]
    [InlineData("expect")]
    public void An_entry_cannot_leave_out(string key)
    {
        // The model gives each of these a default, so the schema is the only thing that notices one left out.
        Problems(Manifest(Committed().With(entry => entry.Remove(key)))).Should().Contain(
            problem => problem.Contains(": required: ", StringComparison.Ordinal), $"an entry needs '{key}'");
    }

    [Theory]
    [InlineData("clean")]
    [InlineData("indexRebuilt")]
    [InlineData("requiredDiagnostics")]
    [InlineData("refereeCheckSucceeds")]
    public void A_committed_or_remote_entry_cannot_leave_out_the_expectation(string key)
    {
        Problems(Manifest(Committed().With(entry => entry["expect"]!.AsObject().Remove(key)))).Should().Contain(
            problem => problem.Contains(": required: ", StringComparison.Ordinal), $"an expectation needs '{key}'");
    }

    [Fact]
    public void The_schema_describes_the_expectations_the_model_reads_and_no_other()
    {
        Properties("expectation").Should().BeEquivalentTo(ModelProperties(typeof(CorpusExpectation)));
    }

    [Fact]
    public void The_schema_describes_every_reader_limit_and_each_must_raise_its_default()
    {
        Properties("readerLimits").Should().BeEquivalentTo(ModelProperties(typeof(CorpusReaderLimits)));
        Properties("readerLimits").Should().BeEquivalentTo(
            ModelProperties(typeof(PdfReaderLimits)), "every guard of PdfReaderLimits can be raised from the manifest");

        foreach (var property in typeof(PdfReaderLimits).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            var minimum = Definition("readerLimits")["properties"]?[name]?["exclusiveMinimum"];

            minimum.Should().NotBeNull($"readerLimits.{name} must be above PdfReaderLimits.Default.{property.Name}");
            minimum!.GetValue<long>().Should().Be(
                Convert.ToInt64(property.GetValue(PdfReaderLimits.Default), CultureInfo.InvariantCulture),
                $"{name} must raise PdfReaderLimits.Default.{property.Name}");
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

    public static TheoryData<string> RefusedManifests => [.. RefusedWholes.Keys];

    /// <summary>Entries the schema must accept, each a small variation on a sound one.</summary>
    private static readonly Dictionary<string, Func<JsonObject>> Accepted = new()
    {
        ["a committed document"] = Committed,
        ["a remote document"] = Remote,
        ["a remote document taken out of an archive"] = () => Remote().With(entry =>
            entry["source"]!["archive"] = Archive("10.22000-53/data/dataset/Test_Corpus/T04_002_eof-missing.pdf")),
        ["a committed document taken out of an archive, its member named after the URL"] = () => Committed().With(entry =>
            entry["source"] = Source("https://pdfa.org/files/reference.zip (member PDFUA-Ref-2-02_Invoice.pdf)", bytes: null)),
        ["a private document, named as its owner likes"] = () => Committed().With(entry =>
            entry["file"] = "private/Contrats 2026/Contrat signé.pdf"),
        ["a private document without the referee's verdict, which no script writes for it"] = () => Committed().With(entry =>
        {
            entry["file"] = "private/contracts/a-contract.pdf";
            entry["expect"]!.AsObject().Remove("refereeCheckSucceeds");
        }),
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
        ["a document without a catalogue"] = () => Committed().With(entry =>
        {
            entry["expect"]!["catalogRecoverable"] = false;
            entry["expect"]!["pages"] = null;
        }),
    };

    /// <summary>
    /// Entries the schema must refuse, each one mistake away from a sound one, with the keyword that names the
    /// mistake.
    /// </summary>
    private static readonly Dictionary<string, (Func<JsonObject> Entry, string Keyword)> Refused = new()
    {
        // Keys nobody reads.
        ["an unknown key on an entry"] = (() => Committed().With(entry => entry["feature"] = new JsonArray("xref-table")), "additionalProperties"),
        ["an unknown expectation"] = (() => Committed().With(entry => entry["expect"]!["finding"] = new JsonArray()), "additionalProperties"),
        ["an unknown key in a source"] = (() => Remote().With(entry => entry["source"]!["hash"] = Hash), "additionalProperties"),
        ["an unknown key in an archive"] = (() => Remote().With(entry =>
            entry["source"]!["archive"] = Archive("a.pdf").With(archive => archive["path"] = "a.pdf")), "additionalProperties"),
        ["an unknown reader limit"] = (() => Committed().With(entry =>
            entry["readerLimits"] = new JsonObject { ["maxObjectLenght"] = 99999999 }), "additionalProperties"),

        // Values the library does not know.
        ["a diagnostic code the reader does not have"] = (() => Committed().With(entry =>
            entry["expect"]!["requiredDiagnostics"] = new JsonArray("xref.rebuild")), "enum"),
        ["a finding no rule reports"] = (() => Committed().With(entry => entry["expect"]!["findings"] = new JsonArray("file.eof")), "enum"),
        ["a use case outside the categories"] = (() => Committed().With(entry => entry["useCase"] = "letter"), "enum"),
        ["an origin outside the four"] = (() => Committed().With(entry => entry["origin"] = "vendored"), "enum"),
        ["a script that builds nothing"] = (() => Committed().With(entry => entry["builtBy"] = "make_corpus.py"), "enum"),
        ["a conformance level that is not one"] = (() => Committed().With(entry => entry["expect"]!["claimsConformance"] = "PDF/A"), "pattern"),

        // Lists.
        ["a diagnostic code listed twice"] = (() => Committed().With(entry =>
            entry["expect"]!["requiredDiagnostics"] = new JsonArray("xref.rebuilt", "xref.rebuilt")), "uniqueItems"),
        ["a finding declared twice"] = (() => Committed().With(entry =>
            entry["expect"]!["findings"] = new JsonArray("file.eof-missing", "file.eof-missing")), "uniqueItems"),
        ["a feature listed twice"] = (() => Committed().With(entry => entry["features"] = new JsonArray("xref-table", "xref-table")), "uniqueItems"),
        ["no feature"] = (() => Committed().With(entry => entry["features"] = new JsonArray()), "minItems"),
        ["a feature with a space"] = (() => Committed().With(entry => entry["features"] = new JsonArray("xref table")), "pattern"),
        ["a feature with a no-break space"] = (() => Committed().With(entry => entry["features"] = new JsonArray("xref\u00a0table")), "pattern"),
        ["a feature ending in a line feed"] = (() => Committed().With(entry => entry["features"] = new JsonArray("xref-table\n")), "pattern"),

        // Plain values.
        ["an empty title"] = (() => Committed().With(entry => entry["title"] = ""), "minLength"),
        ["a negative page count"] = (() => Committed().With(entry => entry["expect"]!["pages"] = -1), "minimum"),
        ["catalogRecoverable set to true"] = (() => Committed().With(entry => entry["expect"]!["catalogRecoverable"] = true), "const"),
        ["a page count for a file without a catalogue"] = (() => Committed().With(entry =>
            entry["expect"]!["catalogRecoverable"] = false), "type"),
        ["a conformance verdict without the claim it judges"] = (() => Committed().With(entry =>
            entry["expect"]!["conformanceValid"] = true), "dependentRequired"),
        ["a password without encryption"] = (() => Committed().With(entry => entry["expect"]!["password"] = "secret"), "dependentRequired"),
        ["an unsupported reason naming no milestone"] = (() => Committed().With(entry =>
            entry["expect"]!["unsupported"] = "the reader does not do this yet"), "pattern"),

        // Reader limits.
        ["a reader limit that does not raise its default"] = (() => Committed().With(entry =>
            entry["readerLimits"] = new JsonObject { ["maxObjectLength"] = 16777216 }), "exclusiveMinimum"),
        ["no reader limit in readerLimits"] = (() => Committed().With(entry => entry["readerLimits"] = new JsonObject()), "minProperties"),
        ["a raised reader limit beside a skip"] = (() => Committed().With(entry =>
        {
            entry["readerLimits"] = new JsonObject { ["maxDecodedStreamLength"] = 536870912 };
            entry["expect"]!["unsupported"] = "M13: read it whole.";
        }), "not"),

        // Paths.
        ["a file outside the corpus folders"] = (() => Committed().With(entry => entry["file"] = "elsewhere/document.pdf"), "pattern"),
        ["a corpus file that is not a PDF"] = (() => Committed().With(entry => entry["file"] = "documents/invoice/an-invoice.txt"), "pattern"),
        ["a corpus path in capitals"] = (() => Committed().With(entry => entry["file"] = "documents/Invoice/An-Invoice.PDF"), "pattern"),
        ["a private file that is not a PDF"] = (() => Committed().With(entry => entry["file"] = "private/contracts/a-contract.docx"), "pattern"),
        ["a path through a dot segment"] = (() => Committed().With(entry => entry["file"] = "documents/../remote/x/a.pdf"), "not"),
        ["a path with an empty segment"] = (() => Committed().With(entry => entry["file"] = "documents//an-invoice.pdf"), "not"),
        ["a private path climbing out"] = (() => Committed().With(entry => entry["file"] = "private/../../elsewhere.pdf"), "not"),

        // Remote documents.
        ["a remote document without a source"] = (() => Remote().With(entry => entry.Remove("source")), "required"),
        ["a remote document without its size"] = (() => Remote().With(entry => entry["source"]!.AsObject().Remove("bytes")), "required"),
        ["a remote document outside remote/"] = (() => Remote().With(entry => entry["file"] = "vendor/somewhere/document.pdf"), "pattern"),
        ["a remote document not under remote/ and its source"] = (() => Remote().With(entry => entry["file"] = "remote/document.pdf"), "pattern"),
        ["a remote name with two dots in a row"] = (() => Remote().With(entry => entry["file"] = "remote/somewhere/a..b.pdf"), "not"),
        ["a committed document in remote/"] = (() => Committed().With(entry => entry["file"] = "remote/somewhere/document.pdf"), "not"),
        ["a remote URL with a member named after it"] = (() => Remote().With(entry =>
            entry["source"]!["url"] = "https://example.org/archive.zip (member document.pdf)"), "pattern"),

        // Sources.
        ["a source without its retrieval date"] = (() => Committed().With(entry =>
            entry["source"] = Source("https://example.org/a.pdf", bytes: null).With(source => source.Remove("retrieved"))), "required"),
        ["a source that is not a web address"] = (() => Committed().With(entry =>
            entry["source"] = Source("ftp://example.org/a.pdf", bytes: null)), "pattern"),
        ["a SHA-256 in capitals"] = (() => Remote().With(entry => entry["source"]!["sha256"] = Hash.ToUpperInvariant()), "pattern"),
        ["a SHA-256 ending in a line feed"] = (() => Remote().With(entry => entry["source"]!["sha256"] = Hash + "\n"), "pattern"),
        ["a SHA-256 given as a number"] = (() => Remote().With(entry => entry["source"]!["sha256"] = 5), "type"),
        ["a retrieval date in another format"] = (() => Remote().With(entry => entry["source"]!["retrieved"] = "26/09/2026"), "pattern"),

        // Archives.
        ["an archive without its SHA-256"] = (() => Remote().With(entry =>
            entry["source"]!["archive"] = Archive("a.pdf").With(archive => archive.Remove("sha256"))), "required"),
        ["an archive without its size"] = (() => Remote().With(entry =>
            entry["source"]!["archive"] = Archive("a.pdf").With(archive => archive.Remove("bytes"))), "required"),
        ["an archive without its member"] = (() => Remote().With(entry =>
            entry["source"]!["archive"] = Archive("a.pdf").With(archive => archive.Remove("member"))), "required"),
        ["an archive SHA-256 in capitals"] = (() => Remote().With(entry =>
            entry["source"]!["archive"] = Archive("a.pdf").With(archive => archive["sha256"] = Hash.ToUpperInvariant())), "pattern"),
        ["an archive of no bytes"] = (() => Remote().With(entry =>
            entry["source"]!["archive"] = Archive("a.pdf").With(archive => archive["bytes"] = 0)), "minimum"),
        ["an archive member that climbs out"] = (() => Remote().With(entry => entry["source"]!["archive"] = Archive("../escape.pdf")), "pattern"),
        ["an archive member that climbs out further in"] = (() => Remote().With(entry =>
            entry["source"]!["archive"] = Archive("a/../../escape.pdf")), "pattern"),
        ["an archive member given as an absolute path"] = (() => Remote().With(entry =>
            entry["source"]!["archive"] = Archive("/etc/document.pdf")), "pattern"),
        ["an archive member with an empty segment"] = (() => Remote().With(entry => entry["source"]!["archive"] = Archive("a//b.pdf")), "pattern"),
        ["an archive member through a dot segment"] = (() => Remote().With(entry => entry["source"]!["archive"] = Archive("./a.pdf")), "pattern"),
        ["an archive member with a backslash"] = (() => Remote().With(entry =>
            entry["source"]!["archive"] = Archive("..\\escape.pdf")), "pattern"),
        ["an archive member with a control character"] = (() => Remote().With(entry =>
            entry["source"]!["archive"] = Archive("a\u0001b.pdf")), "pattern"),
    };

    /// <summary>Whole manifests the schema must refuse, with the keyword that names the mistake.</summary>
    private static readonly Dictionary<string, (string Json, string Keyword)> RefusedWholes = new()
    {
        ["an unknown top-level key"] = ("""{"$schema":"./manifest.schema.json","document":[],"documents":[]}""", "additionalProperties"),
        ["no documents"] = ("""{"$schema":"./manifest.schema.json","producers":{}}""", "required"),
        ["a producer version that is not text"] = ("""{"producers":{"qpdf":12},"documents":[]}""", "type"),
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

    private static JsonObject Archive(string member) => new() { ["sha256"] = Hash, ["bytes"] = 613888, ["member"] = member };

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
}
