using AdCodicem.Pdf.Validation.Arlington;

namespace AdCodicem.Pdf.Arlington;

/// <summary>
/// Reads the grammar of the model's cells, as <c>INTERNAL_GRAMMAR.md</c> defines it, as far as the tables need:
/// the columns <c>Type</c>, <c>SinceVersion</c>, <c>DeprecatedIn</c>, <c>Required</c>, <c>Inheritable</c>,
/// <c>PossibleValues</c> and <c>Link</c>. What it does not recognize in those columns it refuses, with a
/// <see cref="FormatException"/> the caller places in its file and row.
/// </summary>
/// <remarks>
/// Version and extension wrappers (<c>fn:SinceVersion(1.6,X)</c>, <c>fn:IsPDFVersion(1.2,X)</c>,
/// <c>fn:Deprecated(2.0,X)</c>, <c>fn:BeforeVersion(1.3,X)</c>, <c>fn:Extension(E,X)</c>) are stripped leniently
/// around a type, a link or a value: what they wrap is kept whatever the version, as the prototype that was measured
/// on the corpus did. Any other predicate is left unevaluated.
/// </remarks>
internal static class Grammar
{
    /// <summary>The model's columns, in the order of every file's header.</summary>
    public static readonly IReadOnlyList<string> Columns =
    [
        "Key", "Type", "SinceVersion", "DeprecatedIn", "Required", "IndirectReference", "Inheritable", "DefaultValue",
        "PossibleValues", "SpecialCase", "Link", "Note",
    ];

    /// <summary>Splits <paramref name="text"/> on <paramref name="separator"/> where no bracket, parenthesis or quote is open.</summary>
    public static List<string> SplitTop(string text, char separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var quoted = false;
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '\'')
            {
                quoted = !quoted;
            }
            else if (quoted)
            {
                continue;
            }
            else if (c is '(' or '[')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                if (--depth < 0)
                {
                    throw new FormatException($"'{text}' closes a bracket it never opened.");
                }
            }
            else if (c == separator && depth == 0)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        if (depth != 0 || quoted)
        {
            throw new FormatException($"'{text}' leaves a bracket or a quote open.");
        }

        parts.Add(text[start..]);
        return parts;
    }

    /// <summary>
    /// Reads <paramref name="item"/> as a single call <c>fn:Name(arguments)</c>, the parenthesis after the name
    /// closing at its very end; false for anything else, such as <c>fn:A(x) &amp;&amp; fn:B(y)</c>.
    /// </summary>
    public static bool TryParseCall(string item, out string name, out List<string> arguments)
    {
        name = string.Empty;
        arguments = [];

        if (!item.StartsWith("fn:", StringComparison.Ordinal) || item[^1] != ')')
        {
            return false;
        }

        var open = item.IndexOf('(', StringComparison.Ordinal);

        if (open < 4 || !IsIdentifier(item.AsSpan(3, open - 3)))
        {
            return false;
        }

        var depth = 0;
        var quoted = false;

        for (var i = open; i < item.Length; i++)
        {
            var c = item[i];

            if (c == '\'')
            {
                quoted = !quoted;
            }
            else if (!quoted && c is '(' or '[')
            {
                depth++;
            }
            else if (!quoted && c is ')' or ']' && --depth == 0 && i != item.Length - 1)
            {
                return false;
            }
        }

        if (depth != 0 || quoted)
        {
            return false;
        }

        name = item[3..open];
        var inner = item[(open + 1)..^1];
        arguments = inner.Length == 0 ? [] : SplitTop(inner, ',').ConvertAll(static a => a.Trim());
        return true;
    }

    /// <summary>
    /// Strips the version and extension wrappers around <paramref name="item"/>. Returns false when the item is
    /// another predicate, which the caller does not evaluate; <paramref name="inner"/> is then the item itself. An
    /// extension named alone, <c>fn:Extension(E)</c>, wraps nothing: <paramref name="inner"/> is then null.
    /// </summary>
    public static bool TryStripWrappers(string item, out string? inner)
    {
        var current = item.Trim();

        while (current.StartsWith("fn:", StringComparison.Ordinal))
        {
            if (!TryParseCall(current, out var name, out var arguments))
            {
                inner = current;
                return false;
            }

            switch (name)
            {
                case "SinceVersion" or "Deprecated" or "BeforeVersion" or "IsPDFVersion":
                    if (arguments.Count != 2)
                    {
                        throw new FormatException($"'{current}' does not wrap one item in one version.");
                    }

                    ParseVersion(arguments[0]);
                    current = arguments[1];
                    break;

                case "Extension":
                    if (arguments.Count is < 1 or > 2 || !IsIdentifier(arguments[0]))
                    {
                        throw new FormatException($"'{current}' does not name one extension.");
                    }

                    if (arguments.Count == 1)
                    {
                        inner = null;
                        return true;
                    }

                    current = arguments[1];
                    break;

                default:
                    inner = current;
                    return false;
            }
        }

        inner = current;
        return true;
    }

    /// <summary>Reads one of the nine ISO versions.</summary>
    public static byte ParseVersion(string text) =>
        ArlingtonVersion.TryParse(text, out var version)
            ? version
            : throw new FormatException($"'{text}' is not a PDF version the tables can hold (1.0 to 1.7, or 2.0).");

    /// <summary>Reads a <c>Type</c> cell: one or more types, sorted, each perhaps wrapped.</summary>
    public static List<ArlingtonType> ParseTypes(string cell)
    {
        var types = new List<ArlingtonType>();

        foreach (var slot in SplitTop(cell, ';'))
        {
            if (!TryStripWrappers(slot, out var inner) || inner is null || !ArlingtonTypeNames.TryParse(inner, out var type))
            {
                throw new FormatException($"'{slot}' is not one of the model's 18 types.");
            }

            if (types.Count > 0 && type <= types[^1])
            {
                throw new FormatException($"'{cell}' does not list its types once each, in the model's order.");
            }

            types.Add(type);
        }

        return types;
    }

    /// <summary>
    /// Reads a <c>SinceVersion</c> cell: an ISO version; <c>fn:Eval(fn:Extension(E,v) || w)</c>, whose ISO
    /// version is <c>w</c>; or <c>fn:Extension(E)</c> and <c>fn:Extension(E,v)</c>, a key of an extension only.
    /// </summary>
    public static byte ParseSince(string cell)
    {
        if (ArlingtonVersion.TryParse(cell, out var version))
        {
            return version;
        }

        if (TryParseCall(cell, out var name, out var arguments))
        {
            if (name == "Extension" && arguments.Count is 1 or 2 && IsIdentifier(arguments[0]))
            {
                if (arguments.Count == 2)
                {
                    ParseVersion(arguments[1]);
                }

                return ArlingtonVersion.ExtensionOnly;
            }

            if (name == "Eval" && arguments.Count == 1)
            {
                var sides = arguments[0].Split("||", StringSplitOptions.TrimEntries);

                if (sides.Length == 2 &&
                    TryParseCall(sides[0], out var extension, out var extensionArguments) &&
                    extension == "Extension" && extensionArguments.Count == 2 && IsIdentifier(extensionArguments[0]) &&
                    ArlingtonVersion.TryParse(extensionArguments[1], out _) &&
                    ArlingtonVersion.TryParse(sides[1], out var iso))
                {
                    return iso;
                }
            }
        }

        throw new FormatException($"'{cell}' is not a SinceVersion the tables can reduce.");
    }

    /// <summary>Reads a <c>DeprecatedIn</c> cell: empty, or a version.</summary>
    public static byte ParseDeprecated(string cell) => cell.Length == 0 ? ArlingtonVersion.None : ParseVersion(cell);

    /// <summary>
    /// Reads a <c>Required</c> cell: <c>TRUE</c>, <c>FALSE</c>, a predicate on the version alone reduced to a range,
    /// or any other predicate, which is not evaluated.
    /// </summary>
    public static (ArlingtonRequirement Requirement, byte From, byte Before) ParseRequired(string cell)
    {
        switch (cell)
        {
            case "TRUE":
                return (ArlingtonRequirement.Yes, ArlingtonVersion.None, ArlingtonVersion.None);
            case "FALSE":
                return (ArlingtonRequirement.No, ArlingtonVersion.None, ArlingtonVersion.None);
        }

        if (!TryParseCall(cell, out var name, out var arguments) || name != "IsRequired" || arguments.Count != 1)
        {
            throw new FormatException($"'{cell}' is neither TRUE, FALSE nor fn:IsRequired(...).");
        }

        if (TryParseCall(arguments[0], out var predicate, out var predicateArguments) &&
            predicateArguments.Count == 1 &&
            ArlingtonVersion.TryParse(predicateArguments[0], out var version))
        {
            switch (predicate)
            {
                case "IsPDFVersion":
                    return (ArlingtonRequirement.InVersions, version, (byte)(version + 1));
                case "BeforeVersion":
                    return (ArlingtonRequirement.InVersions, ArlingtonVersion.None, version);
                case "SinceVersion":
                    return (ArlingtonRequirement.InVersions, version, ArlingtonVersion.Unbounded);
            }
        }

        return (ArlingtonRequirement.Conditional, ArlingtonVersion.None, ArlingtonVersion.None);
    }

    /// <summary>Reads an <c>Inheritable</c> cell.</summary>
    public static bool ParseBoolean(string cell) => cell switch
    {
        "TRUE" => true,
        "FALSE" => false,
        _ => throw new FormatException($"'{cell}' is neither TRUE nor FALSE."),
    };

    /// <summary>
    /// Reads a <c>Link</c> cell: per type, the objects a value of that type may be, wrappers stripped, each named
    /// once, in the model's order.
    /// </summary>
    public static List<List<string>> ParseLinks(string cell, int slots)
    {
        var links = new List<List<string>>(slots);

        if (cell.Length == 0)
        {
            for (var i = 0; i < slots; i++)
            {
                links.Add([]);
            }

            return links;
        }

        foreach (var slot in SlotsOf(cell, slots))
        {
            var candidates = new List<string>();

            if (slot.Length > 0)
            {
                foreach (var item in SplitTop(slot, ','))
                {
                    if (!TryStripWrappers(item, out var inner) || inner is null || !IsIdentifier(inner))
                    {
                        throw new FormatException($"'{item.Trim()}' does not name an object.");
                    }

                    if (!candidates.Contains(inner))
                    {
                        candidates.Add(inner);
                    }
                }
            }

            links.Add(candidates);
        }

        return links;
    }

    /// <summary>
    /// Reads a <c>PossibleValues</c> cell: per type, its plain values, version wrappers reduced to their union, or
    /// null where the type has none: an empty list, <c>*</c> among the values, a value that is not a plain token
    /// (an array, a string, an expression), or a predicate other than a wrapper.
    /// </summary>
    public static List<SortedSet<string>?> ParsePlainValues(string cell, int slots)
    {
        var values = new List<SortedSet<string>?>(slots);

        if (cell.Length == 0)
        {
            for (var i = 0; i < slots; i++)
            {
                values.Add(null);
            }

            return values;
        }

        foreach (var slot in SlotsOf(cell, slots))
        {
            values.Add(slot.Length == 0 ? null : PlainValuesOf(slot));
        }

        return values;
    }

    /// <summary>Whether <paramref name="text"/> is a name the model gives an object or an extension.</summary>
    public static bool IsIdentifier(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static SortedSet<string>? PlainValuesOf(string slot)
    {
        var plain = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var item in SplitTop(slot, ','))
        {
            if (!TryStripWrappers(item, out var inner))
            {
                return null;
            }

            if (inner is null)
            {
                continue;
            }

            var value = inner.Trim();

            if (value.Length == 0)
            {
                throw new FormatException($"'[{slot}]' holds an empty value.");
            }

            if (value == "*" || value[0] is '[' or '\'' or '(')
            {
                return null;
            }

            foreach (var c in value)
            {
                if (c is <= ' ' or > '~')
                {
                    throw new FormatException($"'{value}' is not a plain value the tables can hold.");
                }
            }

            plain.Add(value);
        }

        return plain.Count == 0 ? null : plain;
    }

    private static List<string> SlotsOf(string cell, int slots)
    {
        var parts = SplitTop(cell, ';');

        if (parts.Count != slots)
        {
            throw new FormatException($"'{cell}' gives {parts.Count} lists where the row's Type has {slots}.");
        }

        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i].Trim();

            if (part.Length < 2 || part[0] != '[' || part[^1] != ']')
            {
                throw new FormatException($"'{part}' is not a bracketed list.");
            }

            parts[i] = part[1..^1].Trim();
        }

        return parts;
    }
}
