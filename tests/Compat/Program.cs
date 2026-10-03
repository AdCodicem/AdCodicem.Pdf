// The compatibility island's application (ADR 48): the package exactly as nuget.org would serve it, installed into a
// trimmed application on the next .NET major, opening and validating every committed corpus document and holding each
// to what tests/corpus/manifest.json says of it. The unit suite, rolled forward onto the same runtime, checks the rest;
// this checks what only a consumer sees: the package's restore, the next SDK's trimming analysis of it, and its public
// API in a trimmed application.
//
// Usage: AdCodicem.Pdf.Compat <tests/corpus>. Exits 1 when any document differs from its manifest entry.
using System.Runtime.InteropServices;
using System.Text.Json;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: AdCodicem.Pdf.Compat <corpus folder>");
    return 2;
}

var corpus = args[0];
using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(corpus, "manifest.json")));
var validator = new PdfValidator();
int checkedCount = 0, refused = 0, skipped = 0;
var failures = new List<string>();

foreach (var entry in manifest.RootElement.GetProperty("documents").EnumerateArray())
{
    var file = entry.GetProperty("file").GetString()!;
    var path = Path.Combine(corpus, file);

    // Remote documents are fetched by the nightly job only (ADR 32), and private ones live on one machine.
    if (Text(entry, "origin") == "remote" || !File.Exists(path))
    {
        skipped++;
        continue;
    }

    var expect = entry.GetProperty("expect");

    if (expect.TryGetProperty("unsupported", out _))
    {
        skipped++;
        continue;
    }

    try
    {
        if (Flag(expect, "encrypted", false))
        {
            try
            {
                using var _ = PdfDocument.Open(path);
                failures.Add($"{file}: opened, where an encrypted document is refused");
            }
            catch (PdfEncryptedException)
            {
                refused++;
            }

            continue;
        }

        using var document = PdfDocument.Open(path);
        var report = validator.Validate(document);

        // Judged on a full read, as the unit suite judges them: a lying /Length or a corrupt filter is found
        // only when the stream it describes is decoded, which is the lazy reader behaving as designed.
        foreach (var number in document.ObjectNumbers.ToList())
        {
            if (document.GetObject(new PdfObjectId(number)) is PdfStream stream && !stream.HasImageFilter())
            {
                _ = stream.Decode(document.Diagnostics);
            }
        }

        if (document.WasRepaired != Flag(expect, "indexRebuilt", false))
        {
            failures.Add($"{file}: rebuilt {document.WasRepaired}, expected {!document.WasRepaired}");
        }

        if ((document.Catalog is not null) != Flag(expect, "catalogRecoverable", true))
        {
            failures.Add($"{file}: catalog {(document.Catalog is null ? "missing" : "found")}, against the manifest");
        }

        var expected = Texts(expect, "findings");
        var reported = report.Findings.Select(finding => finding.RuleId).Distinct().Order(StringComparer.Ordinal).ToList();

        if (!expected.SequenceEqual(reported))
        {
            failures.Add($"{file}: findings [{string.Join(", ", reported)}], expected [{string.Join(", ", expected)}]");
        }

        foreach (var code in Texts(expect, "requiredDiagnostics"))
        {
            if (!document.Diagnostics.Contains(code))
            {
                failures.Add($"{file}: no '{code}' diagnostic");
            }
        }

        if (Flag(expect, "clean", true) && (document.Diagnostics.HasRepairs || document.Diagnostics.HasWarnings))
        {
            failures.Add($"{file}: well formed, but the reader repaired or warned");
        }

        checkedCount++;
    }
    catch (Exception exception) when (exception is PdfException or IOException)
    {
        failures.Add($"{file}: {exception.GetType().Name}: {exception.Message}");
    }
}

Console.WriteLine(
    $"{RuntimeInformation.FrameworkDescription}: {checkedCount} documents opened and validated as their manifest says, " +
    $"{refused} encrypted ones refused, {skipped} not here or not supported, {failures.Count} differences.");

foreach (var failure in failures)
{
    Console.Error.WriteLine($"::error::{failure}");
}

return failures.Count == 0 && checkedCount > 0 ? 0 : 1;

static string? Text(JsonElement parent, string name) =>
    parent.TryGetProperty(name, out var value) ? value.GetString() : null;

static bool Flag(JsonElement parent, string name, bool byDefault) =>
    parent.TryGetProperty(name, out var value) ? value.GetBoolean() : byDefault;

static List<string> Texts(JsonElement parent, string name) =>
    parent.TryGetProperty(name, out var value)
        ? value.EnumerateArray().Select(item => item.GetString()!).Distinct().Order(StringComparer.Ordinal).ToList()
        : [];
