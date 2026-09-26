using System.Collections.ObjectModel;
using AdCodicem.Pdf.Validation.Rules;

namespace AdCodicem.Pdf.Validation;

/// <summary>An ordered set of validation rules, with a name and a version.</summary>
/// <remarks>
/// The library provides the profiles; callers cannot yet assemble their own, nor write rules. The
/// <see cref="Structural"/> profile holds for any PDF, whatever it claims to conform to; the PDF/A and PDF/UA
/// profiles arrive with the <c>AdCodicem.Pdf.Conformance</c> package.
/// </remarks>
public sealed class ValidationProfile
{
    internal ValidationProfile(string name, int version, IReadOnlyList<IValidationRule> rules)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        ArgumentNullException.ThrowIfNull(rules);

        var ids = new string[rules.Count];
        var distinct = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < rules.Count; index++)
        {
            ids[index] = rules[index].Id;

            if (!distinct.Add(ids[index]))
            {
                throw new ArgumentException($"The rule '{ids[index]}' appears twice in the profile '{name}'.", nameof(rules));
            }
        }

        Name = name;
        Version = version;
        Rules = rules;
        RuleIds = new ReadOnlyCollection<string>(ids);
    }

    /// <summary>
    /// Gets the structural profile: the rules that hold for any PDF, whatever it claims to conform to.
    /// </summary>
    public static ValidationProfile Structural { get; } = new("structural", 1, [new EndOfFileMarkerRule()]);

    /// <summary>Gets the profile's name, as reports carry it.</summary>
    public string Name { get; }

    /// <summary>
    /// Gets the profile's version. A stable release that changes what the profile reports increments it;
    /// previews do not.
    /// </summary>
    public int Version { get; }

    /// <summary>Gets the identifiers of the rules the profile runs, in the order it runs them.</summary>
    public IReadOnlyList<string> RuleIds { get; }

    /// <summary>Gets the rules, in the order they run.</summary>
    internal IReadOnlyList<IValidationRule> Rules { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} {Version}";
}
