namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The feature tables of the README and of the documentation site are generated from <c>docs/features/</c>, and
/// that data is held to the roadmap: a feature names milestones that exist, says what their state allows, and
/// every milestone delivers something the tables list. A comparison entry answers every capability, with a source.
/// </summary>
/// <remarks>
/// To regenerate the tables after editing the data, run this class with <c>ADCODICEM_UPDATE_FEATURE_TABLES=1</c>:
/// the two rendering tests then write the README section and the site page instead of comparing them.
/// </remarks>
public class FeatureTablesTests
{
    private static readonly string[] States = ["available", "in-progress", "planned"];

    private static readonly string[] Answers = ["yes", "partial", "add-on", "no", "n/a", "unknown"];

    private static bool Updating => Environment.GetEnvironmentVariable("ADCODICEM_UPDATE_FEATURE_TABLES") == "1";

    [Fact]
    public void The_readme_shows_the_headline_features_the_data_describes()
    {
        var readme = File.ReadAllText(FeatureTables.ReadmePath);
        var expected = FeatureTables.SpliceReadme(readme, FeatureTables.RenderReadmeSection(FeatureTables.LoadFeatures()));

        if (Updating)
        {
            File.WriteAllText(FeatureTables.ReadmePath, expected);
            return;
        }

        readme.Should().Be(expected, "README.md's feature table is generated from docs/features/features.json; " +
            "run FeatureTablesTests with ADCODICEM_UPDATE_FEATURE_TABLES=1 to regenerate it");
    }

    [Fact]
    public void The_site_page_is_generated_from_the_data()
    {
        var expected = FeatureTables.RenderSitePage(FeatureTables.LoadFeatures(), FeatureTables.LoadComparison());

        if (Updating)
        {
            File.WriteAllText(FeatureTables.SitePagePath, expected);
            return;
        }

        File.ReadAllText(FeatureTables.SitePagePath).Should().Be(expected,
            "docs/website/docs/features.md is generated from docs/features/; " +
            "run FeatureTablesTests with ADCODICEM_UPDATE_FEATURE_TABLES=1 to regenerate it");
    }

    [Fact]
    public void Feature_identifiers_are_distinct()
    {
        FeatureTables.LoadFeatures().Groups.SelectMany(group => group.Features).Select(feature => feature.Id)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_feature_names_milestones_of_the_roadmap_and_a_known_state()
    {
        var roadmap = FeatureTables.RoadmapStates();

        foreach (var feature in FeatureTables.LoadFeatures().Groups.SelectMany(group => group.Features))
        {
            feature.Status.Should().BeOneOf(States, $"'{feature.Id}' has a state the tables can show");
            feature.Milestones.Should().NotBeEmpty($"'{feature.Id}' is delivered by some milestone");
            roadmap.Keys.Should().Contain(feature.Milestones, $"'{feature.Id}' names milestones of docs/roadmap.md");
        }
    }

    [Fact]
    public void A_feature_state_agrees_with_the_state_of_its_milestones()
    {
        var roadmap = FeatureTables.RoadmapStates();

        foreach (var feature in FeatureTables.LoadFeatures().Groups.SelectMany(group => group.Features))
        {
            var states = feature.Milestones.Select(milestone => roadmap[milestone]).ToList();

            switch (feature.Status)
            {
                case "available":
                    states.Should().OnlyContain(state => state == "done" || state == "in progress",
                        $"'{feature.Id}' cannot be available before its milestones have begun");
                    break;
                case "in-progress":
                    states.Should().Contain("in progress", $"'{feature.Id}' is in progress only while one of its milestones is");
                    break;
                default:
                    states.Should().NotContain("done", $"'{feature.Id}' cannot still be planned once its milestones are done");
                    break;
            }
        }
    }

    [Fact]
    public void Every_milestone_after_the_foundations_delivers_a_listed_feature()
    {
        var listed = FeatureTables.LoadFeatures().Groups
            .SelectMany(group => group.Features)
            .SelectMany(feature => feature.Milestones)
            .ToHashSet(StringComparer.Ordinal);

        FeatureTables.RoadmapStates().Keys.Where(milestone => milestone != "M0")
            .Should().OnlyContain(milestone => listed.Contains(milestone),
                "a milestone added to the roadmap adds what it delivers to docs/features/features.json");
    }

    [Fact]
    public void Every_capability_is_answered_for_every_compared_product_with_a_source()
    {
        var comparison = FeatureTables.LoadComparison();
        var products = comparison.Products.Select(product => product.Id).ToHashSet(StringComparer.Ordinal);

        comparison.Matrix.Products.Should().OnlyContain(id => products.Contains(id), "the matrix compares products the page describes");

        foreach (var capability in comparison.Matrix.Capabilities)
        {
            capability.Values.Keys.Should().BeEquivalentTo(comparison.Matrix.Products, $"'{capability.Id}' is answered for every product");

            foreach (var (product, answer) in capability.Values)
            {
                answer.Value.Should().BeOneOf(Answers, $"'{capability.Id}' for '{product}'");

                if (answer.Value != "unknown")
                {
                    answer.Source.Should().StartWith("https://", $"'{capability.Id}' for '{product}' is a claim about someone else's product, and needs a source");
                }
            }
        }
    }

    [Fact]
    public void Every_described_product_has_its_license_pricing_model_and_sources()
    {
        foreach (var product in FeatureTables.LoadComparison().Products.Where(product => product.Id != "adcodicem"))
        {
            product.License.Should().NotBeNullOrWhiteSpace($"'{product.Id}' states its license");
            product.PricingModel.Should().NotBeNullOrWhiteSpace($"'{product.Id}' states its pricing model");
            product.Sources.Should().NotBeEmpty($"'{product.Id}' cites where its description comes from");
            product.Sources.Should().OnlyContain(url => url.StartsWith("https://", StringComparison.Ordinal), $"'{product.Id}' cites web sources");
        }
    }

    [Fact]
    public void The_comparison_cites_features_that_exist()
    {
        var features = FeatureTables.LoadFeatures().Groups
            .SelectMany(group => group.Features)
            .Select(feature => feature.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var capability in FeatureTables.LoadComparison().Matrix.Capabilities)
        {
            capability.Ours.Should().NotBeEmpty($"'{capability.Id}' says which of our features answers it");
            capability.Ours.Should().OnlyContain(id => features.Contains(id), $"'{capability.Id}' cites features of docs/features/features.json");
        }
    }
}
