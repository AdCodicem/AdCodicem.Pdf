using AdCodicem.Pdf.Validation.Arlington;

namespace AdCodicem.Pdf.Arlington;

/// <summary>One row with the cells the tables use interpreted, per type where the cell is per type.</summary>
internal sealed class ParsedRow
{
    private ParsedRow(SourceRow source) => Source = source;

    /// <summary>Gets the row as written.</summary>
    public SourceRow Source { get; }

    /// <summary>Gets the types, in the model's order.</summary>
    public List<ArlingtonType> Types { get; private init; } = [];

    /// <summary>Gets the ISO version that introduced the key, or <see cref="ArlingtonVersion.ExtensionOnly"/>.</summary>
    public byte Since { get; private init; }

    /// <summary>Gets the version that deprecated the key, or <see cref="ArlingtonVersion.None"/>.</summary>
    public byte Deprecated { get; private init; }

    /// <summary>Gets whether the key is required.</summary>
    public ArlingtonRequirement Requirement { get; private init; }

    /// <summary>Gets the first version a version-only requirement holds in.</summary>
    public byte RequiredFrom { get; private init; }

    /// <summary>Gets the version a version-only requirement stops holding in.</summary>
    public byte RequiredBefore { get; private init; }

    /// <summary>Gets whether the key is inheritable.</summary>
    public bool Inheritable { get; private init; }

    /// <summary>Gets, per type, the objects a value of that type may be.</summary>
    public List<List<string>> Links { get; private init; } = [];

    /// <summary>Gets, per type, its plain values, or null.</summary>
    public List<SortedSet<string>?> Values { get; private init; } = [];

    /// <summary>Interprets a row, and refuses a cell it does not recognize, naming the file, the line and the column.</summary>
    public static ParsedRow Parse(SourceRow source)
    {
        var column = "Type";

        try
        {
            var types = Grammar.ParseTypes(source["Type"]);
            column = "SinceVersion";
            var since = Grammar.ParseSince(source["SinceVersion"]);
            column = "DeprecatedIn";
            var deprecated = Grammar.ParseDeprecated(source["DeprecatedIn"]);
            column = "Required";
            var (requirement, from, before) = Grammar.ParseRequired(source["Required"]);
            column = "Inheritable";
            var inheritable = Grammar.ParseBoolean(source["Inheritable"]);
            column = "Link";
            var links = Grammar.ParseLinks(source["Link"], types.Count);
            column = "PossibleValues";
            var values = Grammar.ParsePlainValues(source["PossibleValues"], types.Count);

            return new ParsedRow(source)
            {
                Types = types,
                Since = since,
                Deprecated = deprecated,
                Requirement = requirement,
                RequiredFrom = from,
                RequiredBefore = before,
                Inheritable = inheritable,
                Links = links,
                Values = values,
            };
        }
        catch (FormatException exception)
        {
            var editor = source.EditorOf(column);
            var where = editor is null ? string.Empty : $" as {editor} edits it";
            throw new InvalidDataException($"{source}, column {column}{where}: {exception.Message}", exception);
        }
    }
}
