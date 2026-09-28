using System.Text.RegularExpressions;
using AdCodicem.Pdf.Validation.Arlington;

namespace AdCodicem.Pdf.Arlington;

/// <summary>What an override does.</summary>
internal enum OverrideKind
{
    /// <summary>Replaces a cell of the model before the tables are built: the tables hold the edited row.</summary>
    Edit,

    /// <summary>
    /// Leaves the row as the model has it and marks it so that the generated rules the override names stay silent on
    /// that key: a hand-written rule reports the fault (<c>covered by …</c>).
    /// </summary>
    Silence,

    /// <summary>Marks the row with a source of inheritance the model does not encode, which the walk applies.</summary>
    Inheritance,
}

/// <summary>One line of <c>overrides.tsv</c>.</summary>
internal sealed class OverrideEntry
{
    public required int Line { get; init; }

    public required IReadOnlyList<string> Objects { get; init; }

    public required IReadOnlyList<string> Keys { get; init; }

    public required string Column { get; init; }

    public required string Value { get; init; }

    public required IReadOnlyList<string> Rules { get; init; }

    public required string Reason { get; init; }

    public required string Evidence { get; init; }

    /// <summary>Gets what the override does.</summary>
    public OverrideKind Kind => Column switch
    {
        OverrideFile.SilenceColumn => OverrideKind.Silence,
        OverrideFile.InheritanceColumn => OverrideKind.Inheritance,
        _ => OverrideKind.Edit,
    };

    /// <summary>Gets the flags the override sets on its rows.</summary>
    public ArlingtonOverride Flags => Kind switch
    {
        OverrideKind.Silence => Rules.Aggregate(
            OverrideFile.IsPageTreeRule(OverrideFile.CoveringRule(Reason)) ? ArlingtonOverride.SilentWherePageTreeJudges : ArlingtonOverride.None,
            static (flags, rule) => flags | OverrideFile.SilenceOf(rule)),
        OverrideKind.Inheritance => ArlingtonOverride.InheritsAcroFormDefaultAppearance,
        _ => ArlingtonOverride.None,
    };

    /// <summary>Gets every object and key the override applies to.</summary>
    public IEnumerable<(string Object, string Key)> Targets =>
        Objects.SelectMany(o => Keys.Select(k => (o, k)));

    /// <summary>Says where the override is written.</summary>
    public override string ToString() => $"overrides.tsv, line {Line}";
}

/// <summary>
/// Reads <c>tools/AdCodicem.Pdf.Arlington/overrides.tsv</c>: the reviewed departures from the model, one per line.
/// </summary>
/// <remarks>
/// <para>
/// The columns are <c>Object Key Column Value Rule Reason Evidence</c>. <c>Object</c> and <c>Key</c> may each list
/// several names separated by commas; the override applies to every pair. Lines that start with <c>#</c> are
/// comments.
/// </para>
/// <para>
/// <c>Column</c> says what the override does. A column of the model (<c>Type</c>, <c>SinceVersion</c>,
/// <c>DeprecatedIn</c>, <c>Required</c>, <c>Inheritable</c>, <c>PossibleValues</c>, <c>Link</c>) replaces that cell
/// with <c>Value</c>, written in the model's own grammar, before the tables are built. <c>Findings</c> with the value
/// <c>none</c> keeps the row and silences the generated rules <c>Rule</c> lists on that key, because a hand-written
/// rule reports the fault: its <c>Reason</c> reads <c>covered by &lt;rule&gt;</c>. <c>Inheritance</c> with the value
/// <c>AcroForm/DA</c> lets the key be inherited from the interactive form dictionary's <c>/DA</c>, which the walk
/// does. <c>Rule</c> names the generated rules the override changes: <c>object.key-missing</c>,
/// <c>object.value-type-wrong</c>, <c>object.type-value-wrong</c>, <c>object.key-deprecated</c>.
/// </para>
/// </remarks>
internal static partial class OverrideFile
{
    /// <summary>The header line.</summary>
    public const string Header = "Object\tKey\tColumn\tValue\tRule\tReason\tEvidence";

    /// <summary>The column that silences generated rules.</summary>
    public const string SilenceColumn = "Findings";

    /// <summary>The only value of <see cref="SilenceColumn"/>.</summary>
    public const string SilenceValue = "none";

    /// <summary>The column that adds a source of inheritance.</summary>
    public const string InheritanceColumn = "Inheritance";

    /// <summary>The only source of inheritance the walk knows.</summary>
    public const string AcroFormDefaultAppearance = "AcroForm/DA";

    /// <summary>The generated rule that reports a missing required key.</summary>
    public const string KeyMissing = "object.key-missing";

    /// <summary>The generated rule that reports a value of a type the model does not allow.</summary>
    public const string ValueTypeWrong = "object.value-type-wrong";

    /// <summary>The generated rule that reports a <c>/Type</c> or <c>/Subtype</c> value the model does not allow.</summary>
    public const string TypeValueWrong = "object.type-value-wrong";

    /// <summary>The generated rule that reports a deprecated key.</summary>
    public const string KeyDeprecated = "object.key-deprecated";

    /// <summary>The model's columns an override may edit: the ones the tables are built from.</summary>
    public static readonly IReadOnlyList<string> EditableColumns =
        ["Type", "SinceVersion", "DeprecatedIn", "Required", "Inheritable", "PossibleValues", "Link"];

    /// <summary>The flag that silences <paramref name="rule"/>.</summary>
    public static ArlingtonOverride SilenceOf(string rule) => rule switch
    {
        KeyMissing => ArlingtonOverride.KeyMissingSilent,
        ValueTypeWrong => ArlingtonOverride.ValueTypeWrongSilent,
        TypeValueWrong => ArlingtonOverride.TypeValueWrongSilent,
        KeyDeprecated => ArlingtonOverride.KeyDeprecatedSilent,
        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "Not a generated rule."),
    };

    /// <summary>The hand-written rule a silence's reason names, <c>covered by page-tree.kids-missing</c>; empty when it names none.</summary>
    public static string CoveringRule(string reason) => CoveredBy().Match(reason) is { Success: true } match ? match.Groups[1].Value : string.Empty;

    /// <summary>
    /// Whether <paramref name="rule"/> is one of the page tree's, which judge only what their walk entered: the walk
    /// then silences the generated rules there alone (<see cref="ArlingtonOverride.SilentWherePageTreeJudges"/>).
    /// </summary>
    public static bool IsPageTreeRule(string rule) => rule.StartsWith("page-tree.", StringComparison.Ordinal);

    /// <summary>Reads the file, and refuses a line it cannot apply.</summary>
    public static List<OverrideEntry> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Contains('\r', StringComparison.Ordinal))
        {
            throw new InvalidDataException("overrides.tsv holds a carriage return: lines end with a line feed alone.");
        }

        var lines = text.Split('\n');
        var entries = new List<OverrideEntry>();
        var headerSeen = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (!headerSeen)
            {
                if (line != Header)
                {
                    throw new InvalidDataException($"overrides.tsv, line {i + 1}: the header must be '{Header.Replace('\t', ' ')}', tab-separated.");
                }

                headerSeen = true;
                continue;
            }

            entries.Add(ParseEntry(line, i + 1));
        }

        if (!headerSeen)
        {
            throw new InvalidDataException("overrides.tsv has no header.");
        }

        return entries;
    }

    private static OverrideEntry ParseEntry(string line, int number)
    {
        var cells = line.Split('\t');

        if (cells.Length != 7)
        {
            throw Refuse(number, $"{cells.Length} cells where the file has 7.");
        }

        var objects = List(cells[0], number, "Object");
        var keys = List(cells[1], number, "Key");
        var column = cells[2];
        var value = cells[3];
        var rules = List(cells[4], number, "Rule");
        var reason = cells[5].Trim();
        var evidence = cells[6].Trim();

        foreach (var name in objects)
        {
            if (!Grammar.IsIdentifier(name))
            {
                throw Refuse(number, $"'{name}' is not the name of an object.");
            }
        }

        foreach (var rule in rules)
        {
            if (rule is not (KeyMissing or ValueTypeWrong or TypeValueWrong or KeyDeprecated))
            {
                throw Refuse(number, $"'{rule}' is not one of the generated rules.");
            }
        }

        if (reason.Length == 0 || evidence.Length == 0)
        {
            throw Refuse(number, "an override states its reason and its evidence.");
        }

        switch (column)
        {
            case SilenceColumn:
                if (value != SilenceValue)
                {
                    throw Refuse(number, $"the value of {SilenceColumn} is '{SilenceValue}'.");
                }

                var covered = CoveredBy().Match(reason);

                if (!covered.Success || covered.Groups[1].Value is KeyMissing or ValueTypeWrong or TypeValueWrong or KeyDeprecated)
                {
                    throw Refuse(number, "silencing a generated rule needs a reason that reads 'covered by <the hand-written rule>'.");
                }

                break;

            case InheritanceColumn:
                if (value != AcroFormDefaultAppearance || rules is not [KeyMissing])
                {
                    throw Refuse(number, $"the only source of inheritance is '{AcroFormDefaultAppearance}', for {KeyMissing}.");
                }

                break;

            default:
                if (!EditableColumns.Contains(column))
                {
                    throw Refuse(number, $"'{column}' is neither a column of the model the tables are built from, {SilenceColumn}, nor {InheritanceColumn}.");
                }

                break;
        }

        return new OverrideEntry
        {
            Line = number,
            Objects = objects,
            Keys = keys,
            Column = column,
            Value = value,
            Rules = rules,
            Reason = reason,
            Evidence = evidence,
        };
    }

    private static List<string> List(string cell, int number, string column)
    {
        var items = new List<string>();

        foreach (var item in cell.Split(','))
        {
            var trimmed = item.Trim();

            if (trimmed.Length == 0 || items.Contains(trimmed))
            {
                throw Refuse(number, $"the {column} column lists an empty or a repeated name.");
            }

            items.Add(trimmed);
        }

        return items;
    }

    private static InvalidDataException Refuse(int line, string message) => new($"overrides.tsv, line {line}: {message}");

    [GeneratedRegex(@"^covered by ([a-z0-9-]+\.[a-z0-9-]+)\b")]
    private static partial Regex CoveredBy();
}
