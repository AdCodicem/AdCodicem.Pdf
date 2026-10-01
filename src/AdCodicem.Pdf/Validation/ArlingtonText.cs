using System.Globalization;
using System.Text;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation.Arlington;

namespace AdCodicem.Pdf.Validation;

/// <summary>
/// The messages of the rules <see cref="ArlingtonWalk"/> serves: the Arlington type and the key, the first
/// occurrence, and how many there were.
/// </summary>
/// <remarks>
/// A message names the model's type, since the file names none, and never an ISO table: the model's notes cite
/// ISO 32000-2's, which are not 32000-1's.
/// </remarks>
internal static class ArlingtonText
{
    /// <summary>How many plain values a message lists before it only counts them.</summary>
    private const int ValuesListed = 4;

    /// <summary>Writes the message of a tally whose first occurrence lies <paramref name="where"/>.</summary>
    public static string Message(ArlingtonWalk.Tally tally, string where, byte version)
    {
        var type = tally.Row.Object.ToString();
        var key = Key(tally);
        var single = tally.Count == 1;
        var subject = single ? $"{Capitalize(where)}, {Article(type)} {type} in the Arlington model," : $"{Capitalize(Article(type))} {type}";
        var model = single ? "the model" : "the Arlington model";
        var objects = tally.Row.Object.IsArray ? "arrays" : "objects";
        var count = tally.Count.ToString(CultureInfo.InvariantCulture);

        switch (tally.Rule)
        {
            case ArlingtonWalk.Rule.KeyMissing when tally.Length >= 0:
                var holds = Elements(tally.Length);
                return single
                    ? $"{subject} lacks {key}, which {model} requires: it holds {holds}."
                    : $"{subject} lacks {key}, which {model} requires; {count} {objects} do, the first {where}, which holds {holds}.";

            case ArlingtonWalk.Rule.KeyMissing:
                return single
                    ? $"{subject} lacks {key}, which {model} requires."
                    : $"{subject} lacks {key}, which {model} requires; {count} {objects} do, the first {where}.";

            case ArlingtonWalk.Rule.ValueTypeWrong:
                var fault = $"{subject} has {key} as {Kind(tally.Detail)}, where {model} wants {Types(tally.Row.Types)}";
                return single ? fault + "." : $"{fault}; {count} {objects} give {Slot(tally, key)} a type it does not allow, the first {where}.";

            case ArlingtonWalk.Rule.TypeValueWrong:
                var value = tally.Detail is PdfName name ? FileQuote.Name(name) : "a value";
                var wrong = $"{subject} has {key} {value}, where {model} wants {Values(tally.Row)}";
                return single ? wrong + "." : $"{wrong}; {count} {objects} give {key} a value it does not list, the first {where}.";

            default:
                var deprecated = ArlingtonVersion.Format(tally.Row.DeprecatedIn);
                var declared = ArlingtonVersion.Format(version);
                return single
                    ? $"{subject} has {key}, which {model} says PDF {deprecated} deprecates; the file declares PDF {declared}."
                    : $"{subject} has {key}, which {model} says PDF {deprecated} deprecates, and the file declares PDF {declared}; {count} {objects} do, the first {where}.";
        }
    }

    /// <summary>The remedy of a rule.</summary>
    public static string Remedy(ArlingtonWalk.Rule rule) => rule switch
    {
        ArlingtonWalk.Rule.KeyMissing => "Add the entry, with a value ISO 32000 allows for it.",
        ArlingtonWalk.Rule.ValueTypeWrong => "Give the entry a value of a type ISO 32000 allows for it, or remove it where it is optional.",
        ArlingtonWalk.Rule.TypeValueWrong => "Write the value ISO 32000 gives this kind of object, or remove the entry where it is optional.",
        _ => "Remove the entry, unless readers of earlier versions of PDF need it.",
    };

    /// <summary>Names the key or element of the first occurrence: <c>/Rotate</c>, <c>element 2</c>.</summary>
    private static string Key(ArlingtonWalk.Tally tally) =>
        tally.Key is { } key ? FileQuote.Name(key)
        : tally.Element >= 0 ? string.Create(CultureInfo.InvariantCulture, $"element {tally.Element}")
        : "/" + tally.Row.Key;

    /// <summary>Names what the other occurrences share: the same key, or, for a wildcard or a repeating group, any.</summary>
    private static string Slot(ArlingtonWalk.Tally tally, string key) =>
        tally.Row.IsWildcard || tally.Row.IsRepeating
            ? (tally.Row.Object.IsArray ? "an element" : "a key it leaves open")
            : key;

    private static string Elements(int count) =>
        count == 1 ? "1 element" : string.Create(CultureInfo.InvariantCulture, $"{count} elements");

    /// <summary>Says what kind of value a file wrote, telling an integer from a real and a stream from a dictionary.</summary>
    private static string Kind(PdfObject? value) => value switch
    {
        PdfInteger => "an integer",
        PdfReal => "a real number",
        PdfBoolean => "a boolean",
        PdfName => "a name",
        PdfString => "a string",
        PdfArray => "an array",
        PdfStream => "a stream",
        PdfDictionary => "a dictionary",
        _ => "null",
    };

    /// <summary>Says the types a row allows: <c>an integer</c>, <c>a dictionary or a stream</c>.</summary>
    private static string Types(ArlingtonTypes types)
    {
        var words = new List<string>();

        for (var type = ArlingtonType.Array; type <= ArlingtonType.StringText; type++)
        {
            if ((types & (ArlingtonTypes)(1u << (int)type)) != 0 && Word(type) is var word && !words.Contains(word))
            {
                words.Add(word);
            }
        }

        return Join(words);
    }

    private static string Word(ArlingtonType type) => type switch
    {
        ArlingtonType.Array => "an array",
        ArlingtonType.Bitmask or ArlingtonType.Integer => "an integer",
        ArlingtonType.Boolean => "a boolean",
        ArlingtonType.Date => "a date",
        ArlingtonType.Dictionary => "a dictionary",
        ArlingtonType.Matrix => "a matrix",
        ArlingtonType.Name => "a name",
        ArlingtonType.NameTree => "a name tree",
        ArlingtonType.Null => "null",
        ArlingtonType.Number => "a number",
        ArlingtonType.NumberTree => "a number tree",
        ArlingtonType.Rectangle => "a rectangle",
        ArlingtonType.Stream => "a stream",
        _ => "a string",
    };

    /// <summary>Says the names a row lists: <c>/Pages</c>, <c>/Page or /Template</c>, or how many when there are many.</summary>
    private static string Values(ArlingtonRow row)
    {
        var names = new List<string>();

        for (var index = 0; index < row.ValueCount; index++)
        {
            var value = row.GetValue(index);

            if (value.Type == ArlingtonType.Name)
            {
                names.Add("/" + value.Text);
            }
        }

        return names.Count <= ValuesListed
            ? Join(names)
            : string.Create(CultureInfo.InvariantCulture, $"one of the {names.Count} names it lists");
    }

    private static string Join(List<string> words)
    {
        if (words.Count == 0)
        {
            return "another value";
        }

        var text = new StringBuilder(words[0]);

        for (var index = 1; index < words.Count; index++)
        {
            text.Append(index == words.Count - 1 ? " or " : ", ").Append(words[index]);
        }

        return text.ToString();
    }

    /// <summary>The article of a type's name: <c>an</c> before a vowel or an X (<c>an XObjectFormType1</c>).</summary>
    private static string Article(string name) =>
        name.Length > 0 && "AEIOUX".Contains(name[0], StringComparison.Ordinal) ? "an" : "a";

    private static string Capitalize(string text) =>
        text.Length == 0 || char.IsUpper(text[0]) ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
