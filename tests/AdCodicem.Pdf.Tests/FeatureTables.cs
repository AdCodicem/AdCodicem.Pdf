using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Reads <c>docs/features/features.json</c> and <c>docs/features/comparison.json</c>, and renders the tables the
/// README and the documentation site show from them. The data is the source; the tables are never edited by hand.
/// </summary>
internal static partial class FeatureTables
{
    public const string ReadmeStart = "<!-- features:start -->";
    public const string ReadmeEnd = "<!-- features:end -->";

    private const string SiteUrl = "https://adcodicem.github.io/AdCodicem.Pdf/features";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static string RepositoryRoot => Path.GetFullPath(Path.Combine(Corpus.Root, "..", ".."));

    public static string ReadmePath => Path.Combine(RepositoryRoot, "README.md");

    public static string SitePagePath => Path.Combine(RepositoryRoot, "docs", "website", "docs", "features.md");

    private static string DataDirectory => Path.Combine(RepositoryRoot, "docs", "features");

    public static FeatureCatalog LoadFeatures() => Load<FeatureCatalog>("features.json");

    public static Comparison LoadComparison() => Load<Comparison>("comparison.json");

    /// <summary>The milestones of <c>docs/roadmap.md</c>'s index table, by identifier, with their state.</summary>
    public static Dictionary<string, string> RoadmapStates()
    {
        var states = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(Path.Combine(RepositoryRoot, "docs", "roadmap.md")))
        {
            var row = RoadmapRow().Match(line);
            if (row.Success)
            {
                states.Add(row.Groups["id"].Value, row.Groups["state"].Value);
            }
        }

        return states;
    }

    /// <summary>The headline features, as the README shows them between its two markers.</summary>
    public static string RenderReadmeSection(FeatureCatalog catalog)
    {
        var text = new StringBuilder();
        text.Append(ReadmeStart).Append('\n');
        text.Append("| What it does | State |\n|---|---|\n");

        foreach (var feature in catalog.Groups.SelectMany(group => group.Features).Where(feature => feature.Headline))
        {
            text.Append("| ").Append(feature.Name).Append(" | ").Append(State(feature)).Append(" |\n");
        }

        text.Append('\n')
            .Append("Every feature, planned ones included, and how the library compares with other PDF libraries: [Features and comparison](")
            .Append(SiteUrl).Append("), as of ").Append(catalog.AsOf).Append(".\n");
        text.Append(ReadmeEnd);
        return text.ToString();
    }

    /// <summary>The whole site page: its template, with each placeholder replaced by the table it names.</summary>
    public static string RenderSitePage(FeatureCatalog catalog, Comparison comparison)
    {
        var template = File.ReadAllText(Path.Combine(DataDirectory, "features.page.md"));
        var features = catalog.Groups
            .SelectMany(group => group.Features)
            .ToDictionary(feature => feature.Id, StringComparer.Ordinal);

        return template
            .Replace("{{asOf}}", comparison.AsOf, StringComparison.Ordinal)
            .Replace("{{features}}", RenderFeatureGroups(catalog), StringComparison.Ordinal)
            .Replace("{{glance}}", RenderGlance(comparison), StringComparison.Ordinal)
            .Replace("{{matrix}}", RenderMatrix(comparison, features), StringComparison.Ordinal)
            .Replace("{{sources}}", RenderSources(comparison), StringComparison.Ordinal);
    }

    /// <summary>Replaces what lies between the README's markers, keeping everything else as it is.</summary>
    public static string SpliceReadme(string readme, string section)
    {
        var start = readme.IndexOf(ReadmeStart, StringComparison.Ordinal);
        var end = readme.IndexOf(ReadmeEnd, StringComparison.Ordinal);

        if (start < 0 || end < start)
        {
            throw new InvalidOperationException($"README.md must contain {ReadmeStart} and {ReadmeEnd}, in that order.");
        }

        return string.Concat(readme.AsSpan(0, start), section, readme.AsSpan(end + ReadmeEnd.Length));
    }

    public static string State(Feature feature) => feature.Status switch
    {
        "available" => "✅ Available",
        "in-progress" => $"🚧 In progress — {string.Join(", ", feature.Milestones)}",
        _ => $"📅 Planned — {string.Join(", ", feature.Milestones)}",
    };

    private static string RenderFeatureGroups(FeatureCatalog catalog)
    {
        var text = new StringBuilder();

        foreach (var group in catalog.Groups)
        {
            text.Append("### ").Append(group.Title).Append("\n\n");
            text.Append("| Feature | State |\n|---|---|\n");

            foreach (var feature in group.Features)
            {
                text.Append("| ").Append(feature.Name).Append(" | ").Append(State(feature)).Append(" |\n");
            }

            text.Append('\n');
        }

        return text.ToString().TrimEnd('\n');
    }

    private static string RenderGlance(Comparison comparison)
    {
        var text = new StringBuilder();
        text.Append("| Product | Stack | License | Pricing model | Runs on | HTML to PDF | Status |\n");
        text.Append("|---|---|---|---|---|---|---|\n");

        foreach (var product in comparison.Products)
        {
            text.Append("| [").Append(product.Name).Append("](").Append(product.Url).Append(") | ")
                .Append(product.Stack).Append(" | ")
                .Append(product.License).Append(" | ")
                .Append(product.PricingModel).Append(" | ")
                .Append(product.Runtime).Append(" | ")
                .Append(product.HtmlToPdf).Append(" | ")
                .Append(product.Maintenance).Append(" |\n");
        }

        return text.ToString().TrimEnd('\n');
    }

    private static string RenderMatrix(Comparison comparison, Dictionary<string, Feature> features)
    {
        var products = comparison.Matrix.Products.Select(id => comparison.Products.Single(product => product.Id == id)).ToList();
        var text = new StringBuilder();

        text.Append("| Capability | AdCodicem.Pdf |");
        foreach (var product in products)
        {
            text.Append(' ').Append(product.ShortName ?? product.Name).Append(" |");
        }

        text.Append("\n|---|---|").Append(string.Concat(Enumerable.Repeat("---|", products.Count))).Append('\n');

        foreach (var capability in comparison.Matrix.Capabilities)
        {
            text.Append("| ").Append(capability.Name).Append(" | ").Append(Ours(capability, features)).Append(" |");

            foreach (var product in products)
            {
                text.Append(' ').Append(Symbol(capability.Values[product.Id].Value)).Append(" |");
            }

            text.Append('\n');
        }

        var notes = comparison.Matrix.Capabilities.Where(capability => !string.IsNullOrEmpty(capability.OursNote)).ToList();

        if (notes.Count > 0)
        {
            text.Append("\nOn AdCodicem.Pdf's column:\n\n");

            foreach (var capability in notes)
            {
                text.Append("- ").Append(capability.Name).Append(": ").Append(capability.OursNote).Append('\n');
            }
        }

        return text.ToString().TrimEnd('\n');
    }

    private static string RenderSources(Comparison comparison)
    {
        var text = new StringBuilder();

        foreach (var product in comparison.Products.Where(product => product.Id != "adcodicem"))
        {
            text.Append("**").Append(product.Name).Append("** — ").Append(product.BestAt);

            if (product.Sources.Count > 0)
            {
                text.Append(" Sources: ");
                text.AppendJoin(", ", product.Sources.Select((url, index) => $"[{(index + 1).ToString(CultureInfo.InvariantCulture)}]({url})"));
                text.Append('.');
            }

            text.Append("\n\n");

            if (!comparison.Matrix.Products.Contains(product.Id, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (var capability in comparison.Matrix.Capabilities)
            {
                var answer = capability.Values[product.Id];
                text.Append("- ").Append(capability.Name).Append(": ").Append(Symbol(answer.Value));

                if (answer.Note.Length > 0)
                {
                    text.Append(" ").Append(answer.Note);
                }

                if (answer.Source.Length > 0)
                {
                    text.Append(" ([source](").Append(answer.Source).Append("))");
                }

                text.Append('\n');
            }

            text.Append('\n');
        }

        return text.ToString().TrimEnd('\n');
    }

    /// <summary>Our own cell: what the capability's features say, available only when all of them are.</summary>
    private static string Ours(Capability capability, Dictionary<string, Feature> features)
    {
        var mine = capability.Ours.Select(id => features[id]).ToList();

        if (mine.TrueForAll(feature => feature.Status == "available"))
        {
            return "✅";
        }

        var milestones = string.Join(", ", mine
            .Where(feature => feature.Status != "available")
            .SelectMany(feature => feature.Milestones)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(milestone => int.Parse(milestone.AsSpan(1), CultureInfo.InvariantCulture)));

        return mine.Exists(feature => feature.Status == "in-progress") ? $"🚧 {milestones}" : $"📅 {milestones}";
    }

    private static string Symbol(string value) => value switch
    {
        "yes" => "✅",
        "partial" => "◐",
        "add-on" => "➕",
        "no" => "—",
        "n/a" => "n/a",
        _ => "?",
    };

    private static T Load<T>(string name) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(DataDirectory, name)), Options)
        ?? throw new InvalidOperationException($"docs/features/{name} is empty.");

    [GeneratedRegex(@"^\| (?<id>M\d+) \| .+ \| (?:S|M|L|XL) \| .* \| (?<state>done|in progress|to do) \|$")]
    private static partial Regex RoadmapRow();

    internal sealed record FeatureCatalog(
        [property: JsonPropertyName("$comment")] string? Comment,
        string AsOf,
        IReadOnlyList<FeatureGroup> Groups);

    internal sealed record FeatureGroup(string Title, IReadOnlyList<Feature> Features);

    internal sealed record Feature(string Id, string Name, IReadOnlyList<string> Milestones, string Status, bool Headline = false);

    internal sealed record Comparison(
        [property: JsonPropertyName("$comment")] string? Comment,
        string AsOf,
        IReadOnlyList<Product> Products,
        Matrix Matrix);

    internal sealed record Product(
        string Id,
        string Name,
        string? ShortName,
        string Url,
        string Stack,
        string License,
        string PricingModel,
        string Runtime,
        string HtmlToPdf,
        string Maintenance,
        string BestAt,
        IReadOnlyList<string> Sources);

    internal sealed record Matrix(IReadOnlyList<string> Products, IReadOnlyList<Capability> Capabilities);

    internal sealed record Capability(
        string Id,
        string Name,
        IReadOnlyList<string> Ours,
        IReadOnlyDictionary<string, Answer> Values,
        string? OursNote = null);

    internal sealed record Answer(string Value, string Note, string Source);
}
