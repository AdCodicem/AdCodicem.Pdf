using AdCodicem.Pdf.Validation.Arlington;

namespace AdCodicem.Pdf.Arlington;

/// <summary>
/// Reduces the model's rows to what the tables hold, applies the overrides, and refuses whatever it cannot encode
/// rather than dropping it.
/// </summary>
internal static class ModelCompiler
{
    /// <summary>
    /// The keys that tell a dictionary's type apart, which the walk's scoring weighs most (the study of the model,
    /// section 3.1, after TestGrammar's scoring).
    /// </summary>
    public static readonly IReadOnlyList<string> DiscriminatorKeys =
        ["Type", "Subtype", "S", "FunctionType", "ShadingType", "PatternType", "HalftoneType", "FT", "TransformMethod", "CFM"];

    /// <summary>
    /// The keys a discriminator plan may use, in the order they are tried: an array's elements 0 and 1, and a
    /// dictionary's discriminators and <c>Filter</c> (the study of the model, section 2.8).
    /// </summary>
    public static readonly IReadOnlyList<string> PlanKeys =
        ["0", "Type", "Subtype", "S", "FunctionType", "ShadingType", "PatternType", "HalftoneType", "FT", "TransformMethod", "CFM", "Filter", "1"];

    /// <summary>Reduces the model and applies the overrides.</summary>
    public static CompiledModel Compile(string commit, IEnumerable<SourceFile> files, IReadOnlyList<OverrideEntry> overrides)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(overrides);

        if (commit.Length != 40 || !commit.All(char.IsAsciiHexDigitLower))
        {
            throw new InvalidDataException($"'{commit}' is not a full commit hash of the model.");
        }

        var source = SourceModel.Read(files);
        var edits = ApplyEdits(source, overrides);
        var parsed = new SortedDictionary<string, List<ParsedRow>>(StringComparer.Ordinal);

        foreach (var (name, rows) in source)
        {
            parsed.Add(name, rows.ConvertAll(ParsedRow.Parse));
        }

        var arrays = ClassifyArrays(parsed);
        var objects = new List<CompiledObject>(parsed.Count);

        foreach (var (name, rows) in parsed)
        {
            objects.Add(CompileObject(name, arrays.Contains(name), rows, edits));
        }

        RefuseStaleEdits(source, arrays, edits);
        objects = ApplyWalkOverrides(objects, overrides);
        return new CompiledModel(commit, objects, CandidateSets(objects));
    }

    /// <summary>Reduces one row, as the tables hold it.</summary>
    public static CompiledRow CompileRow(ParsedRow row, bool inArray, bool edited)
    {
        var key = row.Source.Key;
        var flags = ArlingtonRowFlags.None;

        if (row.Inheritable)
        {
            flags |= ArlingtonRowFlags.Inheritable;
        }

        if (key == "*")
        {
            flags |= ArlingtonRowFlags.Wildcard;
        }
        else if (IsRepeating(key))
        {
            flags |= ArlingtonRowFlags.Repeating;
        }

        if (IsBackLink(row.Source.ObjectName, key))
        {
            flags |= ArlingtonRowFlags.BackLink;
        }

        if (!inArray && DiscriminatorKeys.Contains(key))
        {
            flags |= ArlingtonRowFlags.Discriminator;
        }

        var types = ArlingtonTypes.None;
        var links = new List<CompiledLink>();
        var values = new List<CompiledValue>();

        for (var i = 0; i < row.Types.Count; i++)
        {
            var type = row.Types[i];
            types |= (ArlingtonTypes)(1u << (int)type);

            if (row.Links[i].Count > 0)
            {
                links.Add(new CompiledLink(type, row.Links[i]));
            }

            if (row.Values[i] is { } plain)
            {
                foreach (var value in plain)
                {
                    values.Add(new CompiledValue(type, value));
                }
            }
        }

        return new CompiledRow
        {
            ObjectName = row.Source.ObjectName,
            Key = key,
            Types = types,
            Since = row.Since,
            Deprecated = row.Deprecated,
            Requirement = row.Requirement,
            RequiredFrom = row.RequiredFrom,
            RequiredBefore = row.RequiredBefore,
            Flags = flags,
            Links = links,
            Values = values,
            Edited = edited,
        };
    }

    /// <summary>
    /// Whether a key points back up the object graph, so that the walk types nothing through it: any
    /// <c>/Parent</c>, the <c>/P</c> of a structure element or an annotation, and an outline item's <c>/Prev</c>.
    /// </summary>
    /// <remarks>
    /// The prototype measured on the corpus (<c>walk.py</c>, its <c>NO_BACKLINKS</c> variant): in a sound file the
    /// target is reached from above anyway, and typing it from below types a wrong target in a damaged one.
    /// </remarks>
    public static bool IsBackLink(string objectName, string key) =>
        key == "Parent" ||
        (key == "P" && (objectName == "StructElem" || objectName.StartsWith("Annot", StringComparison.Ordinal))) ||
        (key == "Prev" && objectName == "OutlineItem");

    private static bool IsRepeating(string key) =>
        key.Length > 1 && key[^1] == '*' && key[..^1].All(char.IsAsciiDigit);

    private static bool IsIndex(string key) => key.Length > 0 && key.All(char.IsAsciiDigit);

    private static Dictionary<(string Object, string Key), EditedRow> ApplyEdits(
        SortedDictionary<string, List<SourceRow>> source,
        IReadOnlyList<OverrideEntry> overrides)
    {
        var edited = new Dictionary<(string Object, string Key), EditedRow>();

        foreach (var entry in overrides)
        {
            if (entry.Kind != OverrideKind.Edit)
            {
                continue;
            }

            foreach (var (name, key) in entry.Targets)
            {
                var rows = RowsOf(source, name, entry);
                var index = rows.FindIndex(r => r.Key == key);

                if (index < 0)
                {
                    throw new InvalidDataException($"{entry}: {name} has no key {key}.");
                }

                if (!edited.TryGetValue((name, key), out var row))
                {
                    row = new EditedRow(rows[index]);
                    edited.Add((name, key), row);
                }

                if (row.Entries.Exists(e => e.Column == entry.Column))
                {
                    throw new InvalidDataException($"{entry}: {name}/{key} has its {entry.Column} edited twice.");
                }

                row.Entries.Add(entry);
                rows[index] = rows[index].With(entry.Column, entry.Value, entry.ToString());
            }
        }

        return edited;
    }

    private static List<SourceRow> RowsOf(SortedDictionary<string, List<SourceRow>> source, string name, OverrideEntry entry) =>
        source.TryGetValue(name, out var rows) ? rows : throw new InvalidDataException($"{entry}: the model has no object {name}.");

    private static HashSet<string> ClassifyArrays(SortedDictionary<string, List<ParsedRow>> objects)
    {
        // An object is an array or not by how the model links it: from an array slot, or from a dictionary or stream
        // slot. A name tree's or number tree's values may be either, and a few objects are linked from nowhere (the
        // trailer, the cross-reference stream, the linearization dictionary): the model's naming convention decides
        // those, ArrayOf... for an array.
        var linkedAs = new Dictionary<string, (bool Array, bool Other)>(StringComparer.Ordinal);

        foreach (var rows in objects.Values)
        {
            foreach (var row in rows)
            {
                for (var i = 0; i < row.Types.Count; i++)
                {
                    foreach (var candidate in row.Links[i])
                    {
                        if (!objects.ContainsKey(candidate))
                        {
                            throw new InvalidDataException($"{row.Source}: Link names {candidate}, which the model does not have.");
                        }

                        var kind = linkedAs.GetValueOrDefault(candidate);

                        switch (row.Types[i])
                        {
                            case ArlingtonType.Array:
                                kind.Array = true;
                                break;
                            case ArlingtonType.Dictionary or ArlingtonType.Stream:
                                kind.Other = true;
                                break;
                        }

                        linkedAs[candidate] = kind;
                    }
                }
            }
        }

        var arrays = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in objects.Keys)
        {
            var kind = linkedAs.GetValueOrDefault(name);

            if (kind.Array && kind.Other)
            {
                throw new InvalidDataException($"{name} is linked both as an array and as a dictionary or stream.");
            }

            if (kind.Array || (!kind.Other && name.StartsWith("ArrayOf", StringComparison.Ordinal)))
            {
                arrays.Add(name);
            }
        }

        return arrays;
    }

    private static CompiledObject CompileObject(
        string name,
        bool isArray,
        List<ParsedRow> rows,
        Dictionary<(string Object, string Key), EditedRow> edits)
    {
        if (rows.Count > ArlingtonLayout.MaxRowsPerObject)
        {
            throw new InvalidDataException(
                $"{name} has {rows.Count} rows; the walk marks the keys of an object in one 64-bit mask, so the tables hold {ArlingtonLayout.MaxRowsPerObject} at most.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var key = row.Source.Key;

            if (!keys.Add(key))
            {
                throw new InvalidDataException($"{row.Source}: the key is given twice.");
            }

            if (key.Length == 0 || key.Any(static c => c <= ' ') || (key.Contains('*', StringComparison.Ordinal) && key != "*" && !IsRepeating(key)))
            {
                throw new InvalidDataException($"{row.Source}: '{key}' is not a key the tables can hold.");
            }

            for (var i = 0; i < row.Types.Count; i++)
            {
                if (row.Links[i].Count > 0 && ((ArlingtonTypes)(1u << (int)row.Types[i]) & ArlingtonTypes.Linkable) == 0)
                {
                    throw new InvalidDataException($"{row.Source}: Link names objects for the type {ArlingtonTypeNames.Of(row.Types[i])}, whose values are never objects.");
                }
            }
        }

        List<ParsedRow> ordered;

        if (isArray)
        {
            RefuseArrayLayout(name, rows);
            ordered = rows;
        }
        else
        {
            foreach (var row in rows)
            {
                if (IsRepeating(row.Source.Key))
                {
                    throw new InvalidDataException($"{row.Source}: a repeating group belongs to an array, and {name} is not one.");
                }
            }

            ordered = [.. rows.OrderBy(static r => r.Source.Key, StringComparer.Ordinal)];
        }

        var compiled = ordered.ConvertAll(r => CompileRow(r, isArray, edits.ContainsKey((name, r.Source.Key))));
        return new CompiledObject(name, isArray, compiled);
    }

    private static void RefuseArrayLayout(string name, List<ParsedRow> rows)
    {
        // Fixed elements 0, 1, ... first; then the repeating group, numbered on from them; then the wildcard, last.
        var position = 0;

        while (position < rows.Count && IsIndex(rows[position].Source.Key))
        {
            if (rows[position].Source.Key != position.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                throw new InvalidDataException($"{rows[position].Source}: an array's fixed elements are numbered 0, 1, 2... in order.");
            }

            position++;
        }

        var fixedCount = position;

        while (position < rows.Count && IsRepeating(rows[position].Source.Key))
        {
            if (rows[position].Source.Key != position.ToString(System.Globalization.CultureInfo.InvariantCulture) + "*")
            {
                throw new InvalidDataException($"{rows[position].Source}: an array's repeating group follows its fixed elements, numbered on from them.");
            }

            position++;
        }

        if (position < rows.Count && rows[position].Source.Key == "*")
        {
            if (position > fixedCount)
            {
                throw new InvalidDataException($"{rows[position].Source}: an array has a repeating group or a wildcard, not both.");
            }

            position++;
        }

        if (position != rows.Count)
        {
            throw new InvalidDataException($"{rows[position].Source}: '{rows[position].Source.Key}' is not an element of an array.");
        }
    }

    private static void RefuseStaleEdits(
        SortedDictionary<string, List<SourceRow>> edited,
        HashSet<string> arrays,
        Dictionary<(string Object, string Key), EditedRow> edits)
    {
        // An edit that no longer changes what the tables hold is stale: the model now says what the override said,
        // and the override must go. Each edit of a row is judged alone: the row with every edit, against the row with
        // every edit but that one. An edit the others need for the row to be read at all — the Link or PossibleValues
        // lists that go with an edited Type — is not stale: without it, the row has no meaning to compare.
        foreach (var ((name, key), row) in edits)
        {
            var inArray = arrays.Contains(name);
            var withAll = edited[name].Find(r => r.Key == key)!;
            var after = CompileRow(ParsedRow.Parse(withAll), inArray, edited: false).Describe();

            foreach (var entry in row.Entries)
            {
                var without = withAll.With(entry.Column, row.Original[entry.Column], editor: null);

                if (!TryDescribe(without, inArray, out var before))
                {
                    continue;
                }

                if (before == after)
                {
                    throw new InvalidDataException(
                        $"{entry}: {name}/{key} already has the {entry.Column} the override gives it: the override is stale and must go.");
                }
            }
        }
    }

    /// <summary>Describes what the tables would hold for <paramref name="row"/>, or says the row cannot be read.</summary>
    private static bool TryDescribe(SourceRow row, bool inArray, out string description)
    {
        try
        {
            description = CompileRow(ParsedRow.Parse(row), inArray, edited: false).Describe();
            return true;
        }
        catch (InvalidDataException)
        {
            description = string.Empty;
            return false;
        }
    }

    private static List<CompiledObject> ApplyWalkOverrides(List<CompiledObject> objects, IReadOnlyList<OverrideEntry> overrides)
    {
        var byName = objects.ToDictionary(static o => o.Name, StringComparer.Ordinal);
        var flags = new Dictionary<(string Object, string Key), ArlingtonOverride>();

        foreach (var entry in overrides)
        {
            if (entry.Kind == OverrideKind.Edit)
            {
                continue;
            }

            foreach (var (name, key) in entry.Targets)
            {
                if (!byName.TryGetValue(name, out var target))
                {
                    throw new InvalidDataException($"{entry}: the model has no object {name}.");
                }

                var row = target.Rows.FirstOrDefault(r => r.Key == key)
                    ?? throw new InvalidDataException($"{entry}: {name} has no key {key}.");

                RefuseNothingToOverride(entry, target, row);
                var existing = flags.GetValueOrDefault((name, key));

                // Where the page tree's walk judges is a condition two silences of one key may share.
                if ((existing & entry.Flags & ~ArlingtonOverride.SilentWherePageTreeJudges) != 0)
                {
                    throw new InvalidDataException($"{entry}: {name}/{key} is overridden that way twice.");
                }

                flags[(name, key)] = existing | entry.Flags;
            }
        }

        if (flags.Count == 0)
        {
            return objects;
        }

        return objects.ConvertAll(o => new CompiledObject(
            o.Name,
            o.IsArray,
            [.. o.Rows.Select(r => flags.TryGetValue((o.Name, r.Key), out var f) ? With(r, f) : r)]));
    }

    private static CompiledRow With(CompiledRow row, ArlingtonOverride overrides) => new()
    {
        ObjectName = row.ObjectName,
        Key = row.Key,
        Types = row.Types,
        Since = row.Since,
        Deprecated = row.Deprecated,
        Requirement = row.Requirement,
        RequiredFrom = row.RequiredFrom,
        RequiredBefore = row.RequiredBefore,
        Flags = row.Flags,
        Links = row.Links,
        Values = row.Values,
        Edited = row.Edited,
        Overrides = overrides,
    };

    private static void RefuseNothingToOverride(OverrideEntry entry, CompiledObject target, CompiledRow row)
    {
        // An override that silences a rule the row cannot trigger, or adds inheritance to a key that is not required,
        // has nothing left to override: the model changed under it, and the override must go.
        var required = (row.Flags & (ArlingtonRowFlags.Wildcard | ArlingtonRowFlags.Repeating)) == 0 &&
            ((row.Requirement == ArlingtonRequirement.Yes && row.Since != ArlingtonVersion.ExtensionOnly) ||
             row.Requirement == ArlingtonRequirement.InVersions);

        foreach (var rule in entry.Rules)
        {
            var canFire = entry.Kind == OverrideKind.Inheritance
                ? required && row.Key == "DA" && (row.Flags & ArlingtonRowFlags.Inheritable) != 0
                : rule switch
                {
                    OverrideFile.KeyMissing => required,
                    OverrideFile.ValueTypeWrong => row.Types != ArlingtonTypes.All,
                    OverrideFile.TypeValueWrong => !target.IsArray && row.Key is "Type" or "Subtype" && row.Values.Count > 0,
                    _ => row.Deprecated != ArlingtonVersion.None,
                };

            if (!canFire)
            {
                throw new InvalidDataException(
                    $"{entry}: {target.Name}/{row.Key} cannot give {rule} as the model stands, so the override has nothing to override and must go.");
            }
        }
    }

    private static List<CandidateSet> CandidateSets(List<CompiledObject> objects)
    {
        var byName = objects.ToDictionary(static o => o.Name, StringComparer.Ordinal);
        var sets = new List<CandidateSet>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var obj in objects)
        {
            foreach (var row in obj.Rows)
            {
                foreach (var link in row.Links)
                {
                    if (link.Candidates.Count > 1 && seen.Add(string.Join(',', link.Candidates)))
                    {
                        sets.Add(new CandidateSet(
                            link.Candidates,
                            Plan(link.Candidates, byName, arrays: true),
                            Plan(link.Candidates, byName, arrays: false)));
                    }
                }
            }
        }

        return sets;
    }

    private static string? Plan(IReadOnlyList<string> members, Dictionary<string, CompiledObject> byName, bool arrays)
    {
        // The first key, of PlanKeys, that every candidate of that kind has with plain values, no two candidates
        // sharing one: an exact match on its value then decides between them.
        var candidates = members.Select(m => byName[m]).Where(o => o.IsArray == arrays).ToList();

        if (candidates.Count < 2)
        {
            return null;
        }

        foreach (var key in PlanKeys)
        {
            var sets = new List<HashSet<string>>(candidates.Count);

            foreach (var candidate in candidates)
            {
                var row = candidate.Rows.FirstOrDefault(r => r.Key == key);

                if (row is null || row.Values.Count == 0)
                {
                    break;
                }

                sets.Add([.. row.Values.Select(static v => v.Text)]);
            }

            if (sets.Count != candidates.Count)
            {
                continue;
            }

            var disjoint = true;

            for (var i = 0; i < sets.Count && disjoint; i++)
            {
                for (var j = i + 1; j < sets.Count && disjoint; j++)
                {
                    disjoint = !sets[i].Overlaps(sets[j]);
                }
            }

            if (disjoint)
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>A row some override edited: the row as the model wrote it, and the edits.</summary>
    private sealed class EditedRow
    {
        public EditedRow(SourceRow original) => Original = original;

        public SourceRow Original { get; }

        public List<OverrideEntry> Entries { get; } = [];
    }
}
