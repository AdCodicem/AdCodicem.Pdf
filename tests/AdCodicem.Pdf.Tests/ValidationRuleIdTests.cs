using System.Reflection;
using System.Text.RegularExpressions;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Rule identifiers are public API from the day they ship (ADR 21), and ADR 36 gives them rules: a grammar, no
/// collision with the reader's diagnostic codes, one severity each, and a line in <c>docs/validation-rules.md</c>.
/// </summary>
public partial class ValidationRuleIdTests
{
    public static TheoryData<string> RuleIds => [.. Constants(typeof(PdfValidationRuleIds))];

    [Theory]
    [MemberData(nameof(RuleIds))]
    public void A_rule_identifier_is_a_family_and_a_name_in_lowercase_kebab_case(string ruleId)
    {
        Grammar().IsMatch(ruleId).Should().BeTrue($"'{ruleId}' is family.name, each in lowercase kebab case");
    }

    [Theory]
    [MemberData(nameof(RuleIds))]
    public void A_rule_identifier_is_never_a_reader_diagnostic_code(string ruleId)
    {
        Constants(typeof(PdfDiagnosticCodes)).Should().NotContain(ruleId, "findings and diagnostics are different things");
    }

    [Fact]
    public void Rule_identifiers_are_distinct()
    {
        var ruleIds = Constants(typeof(PdfValidationRuleIds));

        ruleIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_rule_identifier_is_run_by_a_profile_and_every_rule_has_a_public_identifier()
    {
        // Until M12 brings the conformance profiles, the structural profile is the only one.
        ValidationProfile.Structural.RuleIds.Should().BeEquivalentTo(Constants(typeof(PdfValidationRuleIds)));
    }

    [Fact]
    public void The_rules_table_documents_every_rule_at_the_severity_it_reports()
    {
        var documented = DocumentedRules();

        foreach (var rule in ValidationProfile.Structural.Rules)
        {
            documented.Should().ContainKey(rule.Id, $"docs/validation-rules.md must list '{rule.Id}'");
            documented[rule.Id].Should().Be(rule.Severity.ToString(), $"'{rule.Id}' reports at {rule.Severity}");
        }

        documented.Keys.Should().BeEquivalentTo(Constants(typeof(PdfValidationRuleIds)), "the table lists the rules that exist, and only those");
    }

    /// <summary>The identifier and severity of each row of the rules table: <c>| `file.eof-missing` | Warning | …</c>.</summary>
    private static Dictionary<string, string> DocumentedRules()
    {
        var path = Path.Combine(Corpus.Root, "..", "..", "docs", "validation-rules.md");
        var rules = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(path))
        {
            var row = TableRow().Match(line);
            if (row.Success)
            {
                rules.Add(row.Groups["id"].Value, row.Groups["severity"].Value);
            }
        }

        return rules;
    }

    private static List<string> Constants(Type type) =>
        [.. type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)];

    [GeneratedRegex(@"\A[a-z][a-z0-9]*(-[a-z0-9]+)*\.[a-z][a-z0-9]*(-[a-z0-9]+)*\z")]
    private static partial Regex Grammar();

    [GeneratedRegex(@"\A\|\s*`(?<id>[a-z0-9.-]+)`\s*\|\s*(?<severity>Error|Warning|Information)\s*\|")]
    private static partial Regex TableRow();
}
