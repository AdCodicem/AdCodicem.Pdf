using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The comparer every hash table keyed by numbers takes: numbers a file chose to share a bucket, or a hash, spread as
/// random keys do (#155).
/// </summary>
public class SeededComparerTests
{
    [Fact]
    public void Numbers_a_table_s_size_apart_spread_over_its_buckets()
    {
        // An int is its own hash: a thousand numbers 1,103 apart share a bucket of a table of 1,103, and hashed through the
        // process's seed they spread as random keys do, over about 650 of them.
        var comparer = SeededComparer.Instance;
        var size = new HashSet<int>(1_000).EnsureCapacity(0);
        var buckets = new HashSet<uint>();

        for (var k = 1; k <= 1_000; k++)
        {
            buckets.Add((uint)comparer.GetHashCode(3 + (k * size)) % (uint)size);
        }

        buckets.Count.Should().BeGreaterThan(500, "{0} numbers in {1} buckets spread as random keys do", 1_000, size);
        comparer.Equals(7, 7).Should().BeTrue();
        comparer.Equals(7, 8).Should().BeFalse();
    }

    [Fact]
    public void Offsets_whose_halves_exclusive_or_alike_do_not_share_a_hash()
    {
        // A long hashes as its halves exclusive-ored: 0 and 2^32 + 1 share a hash outright, in a table of any size.
        long near = 0;
        var far = (1L << 32) + 1;
        near.GetHashCode().Should().Be(far.GetHashCode(), "the runtime's own hash folds the halves");

        var comparer = SeededComparer.Instance;

        comparer.GetHashCode(near).Should().NotBe(comparer.GetHashCode(far));
        comparer.Equals(far, far).Should().BeTrue();
        comparer.Equals(near, far).Should().BeFalse();
    }

    [Fact]
    public void Pairs_holding_offsets_whose_halves_exclusive_or_alike_do_not_share_a_hash()
    {
        // A tuple hashes as its items do: two holding offsets that fold alike, and items alike, share a hash.
        long near = 0;
        var far = (1L << 32) + 1;
        (near, 5).GetHashCode().Should().Be((far, 5).GetHashCode(), "a tuple hashes its items as they hash");

        var comparer = SeededComparer.Instance;

        comparer.GetHashCode((near, 5)).Should().NotBe(comparer.GetHashCode((far, 5)));
        comparer.GetHashCode((near, true)).Should().NotBe(comparer.GetHashCode((far, true)));
        comparer.GetHashCode((near, EndObjState.Present)).Should().NotBe(comparer.GetHashCode((far, EndObjState.Present)));
        comparer.GetHashCode((PdfLimit.Object, near)).Should().NotBe(comparer.GetHashCode((PdfLimit.Object, far)));

        comparer.Equals((far, 5), (far, 5)).Should().BeTrue();
        comparer.Equals((far, 5), (far, 6)).Should().BeFalse();
        comparer.Equals((near, 5), (far, 5)).Should().BeFalse();
        comparer.Equals((far, true), (far, true)).Should().BeTrue();
        comparer.Equals((far, true), (far, false)).Should().BeFalse();
        comparer.Equals((near, true), (far, true)).Should().BeFalse();
        comparer.Equals((far, EndObjState.Present), (far, EndObjState.Present)).Should().BeTrue();
        comparer.Equals((far, EndObjState.Present), (near, EndObjState.Present)).Should().BeFalse();
        comparer.Equals((PdfLimit.Object, far), (PdfLimit.Object, far)).Should().BeTrue();
        comparer.Equals((PdfLimit.Object, far), (PdfLimit.Object, near)).Should().BeFalse();
        comparer.Equals((PdfLimit.Object, far), (PdfLimit.StreamLength, far)).Should().BeFalse();
    }

    [Fact]
    public void No_hash_table_in_the_sources_is_keyed_by_numbers_without_the_seeded_comparer()
    {
        // Every hash table keyed by an int, a long or a tuple holding one, whose type the source writes, is built with
        // SeededComparer among its own arguments, a copy included: built with [], new() or no comparer, it would hash
        // numbers a file chose as themselves (#155). A name declared with such a type and no table, given [] or new()
        // later, is held too, and so are the LINQ operators that hash. This reading names the file and the line; what it
        // cannot see — a table built where its type is not written, as an out var or an expression body gives it — the
        // reading of the compiled library below refuses.
        var violations = new List<string>();

        foreach (var path in Directory.EnumerateFiles(Path.Combine(FeatureTables.RepositoryRoot, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            violations.AddRange(SourceRules.Violations(Path.GetRelativePath(FeatureTables.RepositoryRoot, path), File.ReadAllText(path)));
        }

        violations.Should().BeEmpty("a hash table keyed by numbers takes SeededComparer.Instance");
    }

    [Theory]
    [InlineData("private readonly HashSet<int> _seen = [];")]
    [InlineData("private readonly Dictionary<long, string> _at = new();")]
    [InlineData("var seen = new HashSet<int>();")]
    [InlineData("var copy = new HashSet<(long DataStart, int Number)>(_other);")]
    [InlineData("public Dictionary<int, int> Indexes { get; } = [];")]
    [InlineData("private HashSet<int>? _lazy;\nvoid M() => (_lazy ??= []).Add(1);")]
    [InlineData("var numbers = list.ToHashSet();")]
    [InlineData("Mark(new HashSet<long>(_faults), new HashSet<int>(_needing, SeededComparer.Instance));")]
    [InlineData("private readonly ConcurrentDictionary<int, string> _byNumber = new();")]
    [InlineData("var pairs = left.Join(right, l => l, r => r, (l, r) => l);")]
    public void The_reading_of_the_sources_refuses_a_table_keyed_by_numbers_built_without_the_comparer(string source)
    {
        SourceRules.Violations("Sample.cs", source).Should().ContainSingle();
    }

    [Theory]
    [InlineData("private readonly HashSet<int> _seen = new(SeededComparer.Instance);")]
    [InlineData("var copy = new HashSet<(long DataStart, int Number)>(_other, SeededComparer.Instance);")]
    [InlineData("private readonly Dictionary<PdfName, PdfObject> _entries = [];")]
    [InlineData("private readonly SortedDictionary<int, int> _tally = [];")]
    [InlineData("public IReadOnlyDictionary<int, int> View => _view;")]
    [InlineData("/// A HashSet<int> built with [] would be refused.")]
    [InlineData("private HashSet<int>? _lazy;\nvoid M() => (_lazy ??= new(SeededComparer.Instance)).Add(1);")]
    public void The_reading_of_the_sources_lets_a_seeded_table_and_other_keys_be(string source)
    {
        SourceRules.Violations("Sample.cs", source).Should().BeEmpty();
    }

    [Fact]
    public void No_method_of_the_library_builds_a_table_keyed_by_numbers_without_the_seeded_comparer()
    {
        // Read in the compiled library, whatever the source wrote — [], new(), an out var given a set, an expression body,
        // an argument, a copy —: every hash table built over a key a file can steer takes SeededComparer.Instance as its
        // comparer, and no LINQ or frozen operator hashes such keys. A key is steerable when it is an integer wider than a
        // byte, a tuple holding one, or a struct of the library hashed as the compiler writes a record struct's hash.
        var violations = new List<string>();

        foreach (var (method, constructor) in CompiledRules.TablesBuilt(typeof(SeededComparer).Assembly))
        {
            violations.Add($"{method.DeclaringType!.FullName}.{method.Name}: {CompiledRules.Describe(constructor)}");
        }

        violations.Should().BeEmpty("a hash table keyed by numbers takes SeededComparer.Instance");
    }

    [Fact]
    public void The_reading_of_the_compiled_library_refuses_a_table_built_without_the_comparer()
    {
        // The check above sees what a source reading cannot: an out var given [], an expression body, a copy among others.
        var refused = CompiledRules.TablesBuilt(typeof(SeededComparerTests).Assembly)
            .Where(found => found.Method.DeclaringType == typeof(Unseeded))
            .Select(found => found.Method.Name)
            .Distinct()
            .ToList();

        refused.Should().BeEquivalentTo(nameof(Unseeded.OutVar), nameof(Unseeded.ExpressionBody), nameof(Unseeded.Copies), nameof(Unseeded.Struct));
    }

    /// <summary>Tables the compiled reading refuses, and one it lets be.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The shapes are written as code might write them, to be read compiled.")]
    private static class Unseeded
    {
        public static HashSet<int> OutVar(Dictionary<int, HashSet<int>> tables)
        {
            if (!tables.TryGetValue(1, out var numbers))
            {
                numbers = [];
                tables[1] = numbers;
            }

            return numbers;
        }

        public static HashSet<long> ExpressionBody() => [];

        public static (HashSet<int> Seeded, HashSet<int> Copied) Copies(HashSet<int> source) =>
            (new HashSet<int>(source, SeededComparer.Instance), new HashSet<int>(source));

        public static HashSet<Pair> Struct() => [];

        public static HashSet<int> Seeded() => new(SeededComparer.Instance);
    }

    private readonly record struct Pair(int First, int Second);

    /// <summary>What the reading of the compiled library refuses.</summary>
    private static class CompiledRules
    {
        private const BindingFlags Declared =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => code.Value);

        private static readonly HashSet<string> HashingOperators =
        [
            "ToHashSet", "ToDictionary", "ToLookup", "Distinct", "DistinctBy", "GroupBy", "GroupJoin", "Join", "LeftJoin",
            "RightJoin", "Union", "UnionBy", "Intersect", "IntersectBy", "Except", "ExceptBy", "CountBy", "AggregateBy",
            "ToFrozenDictionary", "ToFrozenSet", "ToImmutableDictionary", "ToImmutableHashSet",
        ];

        /// <summary>
        /// Gets each table over a steerable key a method of <paramref name="assembly"/> builds without SeededComparer.Instance
        /// handed to it last, and each hashing operator it calls with no comparer.
        /// </summary>
        public static IEnumerable<(MethodBase Method, MethodBase Constructor)> TablesBuilt(Assembly assembly)
        {
            var seeded = typeof(SeededComparer).GetProperty(nameof(SeededComparer.Instance))!.GetMethod!;

            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
                {
                    if (method.GetMethodBody()?.GetILAsByteArray() is not { } il)
                    {
                        continue;
                    }

                    MethodBase? previous = null;

                    foreach (var (code, called) in Instructions(method, il))
                    {
                        if (code == OpCodes.Newobj && called is ConstructorInfo constructor && Builds(constructor.DeclaringType!)
                            && !(TakesComparer(constructor) && seeded.Equals(previous)))
                        {
                            yield return (method, constructor);
                        }
                        else if ((code == OpCodes.Call || code == OpCodes.Callvirt) && called is MethodInfo operation
                            && HashingOperators.Contains(operation.Name) && !TakesComparer(operation)
                            && operation.DeclaringType!.Namespace is "System.Linq" or "System.Collections.Frozen" or "System.Collections.Immutable")
                        {
                            yield return (method, operation);
                        }

                        previous = called;
                    }
                }
            }
        }

        public static string Describe(MethodBase constructor) =>
            constructor.DeclaringType is { IsGenericType: true } type
                ? $"{type.Name}<{string.Join(", ", type.GetGenericArguments().Select(argument => argument.Name))}>.{constructor.Name}"
                : $"{constructor.DeclaringType?.Name}.{constructor.Name}";

        /// <summary>Determines whether <paramref name="type"/> is a hash table whose key a file can steer.</summary>
        private static bool Builds(Type type)
        {
            if (type == typeof(System.Collections.Hashtable))
            {
                return true;
            }

            if (!type.IsGenericType)
            {
                return false;
            }

            var definition = type.GetGenericTypeDefinition();
            var table = definition == typeof(Dictionary<,>) || definition == typeof(HashSet<>)
                || definition == typeof(System.Collections.Concurrent.ConcurrentDictionary<,>) || definition == typeof(OrderedDictionary<,>);
            return table && Steerable(type.GetGenericArguments()[0]);
        }

        private static bool Steerable(Type key)
        {
            if (key.IsGenericParameter || key.IsEnum)
            {
                return false;
            }

            if (key == typeof(int) || key == typeof(long) || key == typeof(uint) || key == typeof(ulong) || key == typeof(short)
                || key == typeof(ushort) || key == typeof(nint) || key == typeof(nuint))
            {
                return true;
            }

            if (key.IsGenericType && key.FullName!.StartsWith("System.ValueTuple`", StringComparison.Ordinal))
            {
                return Array.Exists(key.GetGenericArguments(), Steerable);
            }

            // A struct of ours hashed as the compiler writes a record struct's hash: a fixed combination a file inverts.
            return key.IsValueType && key.Assembly != typeof(int).Assembly
                && key.GetMethod(nameof(GetHashCode), Type.EmptyTypes) is { } hash
                && hash.DeclaringType == key && hash.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute));
        }

        private static bool TakesComparer(MethodBase method) =>
            Array.Exists(
                method.GetParameters(),
                parameter => parameter.ParameterType.IsGenericType
                    && parameter.ParameterType.GetGenericTypeDefinition() == typeof(IEqualityComparer<>));

        /// <summary>Reads <paramref name="il"/>'s instructions, with the method each one calls or builds, if any.</summary>
        private static IEnumerable<(OpCode Code, MethodBase? Called)> Instructions(MethodBase method, byte[] il)
        {
            var typeArguments = method.DeclaringType!.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
            var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
            var at = 0;

            while (at < il.Length)
            {
                var value = (short)il[at++];

                if (value == 0xFE)
                {
                    value = unchecked((short)(0xFE00 | il[at++]));
                }

                var code = OpCodesByValue[value];
                MethodBase? called = null;

                switch (code.OperandType)
                {
                    case OperandType.InlineMethod:
                        called = method.Module.ResolveMethod(BitConverter.ToInt32(il, at), typeArguments, methodArguments);
                        at += 4;
                        break;
                    case OperandType.InlineSwitch:
                        at += 4 + (4 * BitConverter.ToInt32(il, at));
                        break;
                    case OperandType.InlineI8 or OperandType.InlineR:
                        at += 8;
                        break;
                    case OperandType.InlineVar:
                        at += 2;
                        break;
                    case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                        at += 1;
                        break;
                    case OperandType.InlineNone:
                        break;
                    default:
                        at += 4;
                        break;
                }

                yield return (code, called);
            }
        }
    }

    /// <summary>What the reading of the sources refuses, by file and line.</summary>
    private static class SourceRules
    {
        private static readonly Regex Hashing = new(
            @"\.(?:ToHashSet|ToDictionary|ToLookup|Distinct|DistinctBy|GroupBy|GroupJoin|Join|LeftJoin|RightJoin|Union|UnionBy|Intersect|IntersectBy|Except|ExceptBy|CountBy|AggregateBy|ToFrozenDictionary|ToFrozenSet|ToImmutableDictionary|ToImmutableHashSet)\(",
            RegexOptions.CultureInvariant);

        public static bool IsNumeric(Type key) =>
            key == typeof(int) || key == typeof(long)
            || (key.IsGenericType && key.FullName!.StartsWith("System.ValueTuple", StringComparison.Ordinal)
                && Array.Exists(key.GetGenericArguments(), IsNumeric));

        public static IEnumerable<string> Violations(string path, string source)
        {
            var text = WithoutComments(source);
            var declared = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (at, isNew, key, name, statement) in Tables(text))
            {
                if (!IsNumeric(key))
                {
                    continue;
                }

                var built = isNew || statement.Contains("= [", StringComparison.Ordinal) || Regex.IsMatch(statement, @"=\s*new\s*\(");

                if (built && !statement.Contains("SeededComparer", StringComparison.Ordinal))
                {
                    yield return $"{path}:{Line(text, at)}: {statement.Trim()}";
                }
                else if (!built && name is not null)
                {
                    declared.Add(name);
                }
            }

            foreach (var name in declared)
            {
                foreach (Match assigned in Regex.Matches(text, $@"(?<![\w.]){Regex.Escape(name)}\s*(?:\?\?)?=\s*(?:\[|new\s*\()"))
                {
                    var end = text.IndexOfAny([';', ')'], assigned.Index + assigned.Length);
                    var statement = text[assigned.Index..(end < 0 ? text.Length : end + 1)];

                    if (!statement.Contains("SeededComparer", StringComparison.Ordinal))
                    {
                        yield return $"{path}:{Line(text, assigned.Index)}: {statement.Trim()}";
                    }
                }
            }

            foreach (Match call in Hashing.Matches(text))
            {
                yield return $"{path}:{Line(text, call.Index)}: {call.Value} hashes without SeededComparer";
            }
        }

        /// <summary>
        /// Finds each Dictionary or HashSet type written out: where, whether a new builds it, its key, the name it declares,
        /// and its statement to the semicolon.
        /// </summary>
        private static IEnumerable<(int At, bool IsNew, string Key, string? Name, string Statement)> Tables(string text)
        {
            foreach (Match type in Regex.Matches(text, @"(?<![\w.])(?:Dictionary|HashSet|ConcurrentDictionary|OrderedDictionary|FrozenDictionary|FrozenSet|ImmutableDictionary|ImmutableHashSet)<"))
            {
                var open = type.Index + type.Length;
                var close = Matching(text, open - 1, '<', '>');

                if (close < 0)
                {
                    continue;
                }

                var arguments = text[open..close];
                var key = TopLevelFirst(arguments).Trim();
                var before = text[..type.Index].TrimEnd();
                var isNew = before.EndsWith("new", StringComparison.Ordinal);
                var end = StatementEnd(text, close + 1);

                // What a new hands the table is its own arguments: a comparer another argument of the same statement takes
                // is not this table's.
                var parenthesis = isNew ? text.IndexOf('(', close) : -1;
                var built = parenthesis >= 0 && string.IsNullOrWhiteSpace(text[(close + 1)..parenthesis])
                    ? Matching(text, parenthesis, '(', ')')
                    : -1;
                var statement = isNew
                    ? text[(before.Length - 3)..(built >= 0 ? built + 1 : end)]
                    : text[type.Index..end];
                var name = Regex.Match(text[(close + 1)..], @"^\??\s+(?<name>@?\w+)").Groups["name"];
                yield return (type.Index, isNew, key, name.Success ? name.Value : null, statement);
            }
        }

        /// <summary>
        /// Gets where the statement going on at <paramref name="from"/> ends: past its semicolon, or past a property's
        /// accessors and the initializer that follows them, if one does.
        /// </summary>
        private static int StatementEnd(string text, int from)
        {
            var semicolon = text.IndexOf(';', from);
            var brace = text.IndexOf('{', from);

            if (brace < 0 || (semicolon >= 0 && semicolon < brace) || !Regex.IsMatch(text[(brace + 1)..], @"^\s*(?:get|set|init)\b"))
            {
                return semicolon < 0 ? text.Length : semicolon + 1;
            }

            var accessors = Matching(text, brace, '{', '}');
            var initializer = Regex.Match(text[(accessors + 1)..], @"^\s*=[^;]*;");
            return accessors + 1 + (initializer.Success ? initializer.Length : 0);
        }

        private static bool IsNumeric(string key)
        {
            var names = Regex.Matches(key, @"\b(?:int|long|Int32|Int64)\b");
            return names.Count > 0 && (key.StartsWith('(') || key is "int" or "long" or "Int32" or "Int64");
        }

        private static string TopLevelFirst(string arguments)
        {
            var depth = 0;

            for (var i = 0; i < arguments.Length; i++)
            {
                depth += arguments[i] is '<' or '(' ? 1 : arguments[i] is '>' or ')' ? -1 : 0;

                if (depth == 0 && arguments[i] == ',')
                {
                    return arguments[..i];
                }
            }

            return arguments;
        }

        private static int Matching(string text, int open, char opening, char closing)
        {
            var depth = 0;

            for (var i = open; i < text.Length; i++)
            {
                depth += text[i] == opening ? 1 : text[i] == closing ? -1 : 0;

                if (depth == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string WithoutComments(string source) =>
            Regex.Replace(Regex.Replace(source, @"/\*.*?\*/", m => new string('\n', m.Value.Count(c => c == '\n')), RegexOptions.Singleline), @"//[^\n]*", string.Empty);

        private static int Line(string text, int at) => text.AsSpan(0, at).Count('\n') + 1;
    }
}
