using System.Collections;
using System.Reflection;
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
        comparer.Equals((far, true), (far, true)).Should().BeTrue();
        comparer.Equals((far, true), (far, false)).Should().BeFalse();
        comparer.Equals((far, EndObjState.Present), (far, EndObjState.Present)).Should().BeTrue();
        comparer.Equals((far, EndObjState.Present), (near, EndObjState.Present)).Should().BeFalse();
        comparer.Equals((PdfLimit.Object, far), (PdfLimit.Object, far)).Should().BeTrue();
        comparer.Equals((PdfLimit.Object, far), (PdfLimit.Object, near)).Should().BeFalse();
    }

    [Fact]
    public void No_hash_table_in_the_sources_is_keyed_by_numbers_without_the_seeded_comparer()
    {
        // Every Dictionary or HashSet keyed by an int, a long or a tuple holding one is built with SeededComparer, a copy
        // included: built with [], new() or no comparer, it would hash numbers a file chose as themselves (#155). A name
        // declared with such a type and given [] or new() later is held too, and so is any LINQ operator that hashes.
        // What escapes this reading — a table built where its type is not written — the next test meets at run time.
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
    public void Every_hash_table_the_reader_holds_keyed_by_numbers_takes_the_seeded_comparer()
    {
        // A document that opens through a rebuild, decodes object streams and is validated fills the reader's tables; each
        // one keyed by numbers, and each one such a table holds, compares through SeededComparer.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] /Resources << >> >>")
            .BuildClassic(rootNumber: 1);
        using var document = PdfDocument.Open(file);
        _ = new PdfValidator().Validate(document);
        var tables = 0;

        foreach (var field in typeof(PdfFileReader).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            tables += Check(field.Name, field.GetValue(document.Reader));
        }

        tables.Should().BeGreaterThanOrEqualTo(20, "the reader keeps twenty tables keyed by numbers");

        static int Check(string name, object? value)
        {
            if (value is null || !value.GetType().IsGenericType)
            {
                return 0;
            }

            var definition = value.GetType().GetGenericTypeDefinition();

            if (definition != typeof(Dictionary<,>) && definition != typeof(HashSet<>))
            {
                return 0;
            }

            var key = value.GetType().GetGenericArguments()[0];

            if (!SourceRules.IsNumeric(key))
            {
                return 0;
            }

            var comparer = value.GetType().GetProperty("Comparer")!.GetValue(value);
            comparer.Should().BeSameAs(SeededComparer.Instance, "{0} is keyed by {1}", name, key);
            var held = 1;

            if (definition == typeof(Dictionary<,>))
            {
                foreach (var inner in ((IDictionary)value).Values)
                {
                    held += Check(name + "[]", inner);
                }
            }

            return held;
        }
    }

    /// <summary>What the reading of the sources refuses, by file and line.</summary>
    private static class SourceRules
    {
        private static readonly Regex Hashing = new(
            @"\.(?:ToHashSet|ToDictionary|ToLookup|Distinct|DistinctBy|GroupBy|Union|UnionBy|Intersect|IntersectBy|Except|ExceptBy|CountBy|AggregateBy)\(",
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
            foreach (Match type in Regex.Matches(text, @"(?<![\w.])(?:Dictionary|HashSet)<"))
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
                var statement = text[(isNew ? before.Length - 3 : type.Index)..end];
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
