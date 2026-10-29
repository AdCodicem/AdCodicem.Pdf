using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;
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
        // Every hash table keyed by a number, a tuple holding one or a pair, whose type the source writes, is built
        // with SeededComparer among its own arguments, or in its initializer where it has none, a copy included: built
        // with [], new() or no comparer, it would hash numbers a file chose as themselves, or a pair holding a
        // reference, a floating point number or padding by its first field (#155). A concurrent, frozen or immutable
        // one built with [] or new(), which enumerates by hash, is refused seeded or not; one an operator or a factory
        // builds is the compiled reading's. A name declared with such a type and no table, given [] or new() later, is
        // held too, so are the tables an array's initializer builds, and a LINQ operator that hashes names a comparer
        // among its own arguments, whatever its key. Comments and literals are not read. This reading names the file
        // and the line; what it cannot see — a table built where its type is not written, as an out var or an
        // expression body gives it, a key it cannot tell a number, the comparer an operator is given — the reading of
        // the compiled library below refuses.
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
    [InlineData("var numbers = await pages.ToHashSetAsync();")]
    [InlineData("Mark(new HashSet<long>(_faults), new HashSet<int>(_needing, SeededComparer.Instance));")]
    [InlineData("private readonly ConcurrentDictionary<int, string> _byNumber = new();")]
    [InlineData("private readonly ConcurrentDictionary<int, string> _byNumber = new(SeededComparer.Instance);")]
    [InlineData("var tables = new ConcurrentDictionary<long, int>[] { new(SeededComparer.Instance) };")]
    [InlineData("var pairs = left.Join(right, l => l, r => r, (l, r) => l);")]
    [InlineData("Use(new HashSet<int> { 1 }, new HashSet<int>(SeededComparer.Instance));")]
    [InlineData("private HashSet<int>? _x;\nvoid M(int a, int b) { _x = new(Math.Max(a, b)); }")]
    [InlineData("var seen = new HashSet<int?>();")]
    [InlineData("var seen = new Dictionary<char, int>();")]
    [InlineData("var seen = new HashSet<Tuple<int, string>>();")]
    [InlineData("var names = list.Distinct();")]
    [InlineData("var q = \"\\\"\"; var seen = new HashSet<int>();")]
    [InlineData("var q = @\"\\\"; var seen = new HashSet<int>();")]
    [InlineData("var q = @\"\"\"\";\nvar seen = new HashSet<int>();")]
    [InlineData("var m = $\"{(x ? \"it's\" : \"b\")}\"; var seen = new HashSet<int>();")]
    [InlineData("var m = $\"{(x ? \"a//b\" : \"b\")}\"; var seen = new HashSet<int>();")]
    [InlineData("var m = $$\"\"\"{{(x ? \"a\" : \"b\")}}\"\"\"; var seen = new HashSet<int>();")]
    [InlineData("var seen = new HashSet<(int, string)?>();")]
    [InlineData("var seen = new HashSet<Tuple<int, string>?>();")]
    [InlineData("var seen = new Dictionary<((int, int)?, string), int>();")]
    [InlineData("var c = '\\U00000041'; var d = '\"'; var seen = new HashSet<int>();")]
    [InlineData("var m = $\"{x:0\\\"}\"; var seen = new HashSet<int>();")]
    [InlineData("var tables = new HashSet<int>[] { new() };")]
    [InlineData("var tables = new HashSet<int>[] { new(SeededComparer.Instance), new() };")]
    [InlineData("var tables = new Dictionary<int, int>[] { new(SeededComparer.Instance), [] };")]
    [InlineData("Use(new HashSet<int>[] { new() }, SeededComparer.Instance);")]
    [InlineData("var tables = new HashSet<int>[] { new(n > 0 ? n : 1, SeededComparer.Instance), new() };")]
    [InlineData("var tables = new HashSet<int>[] { new(items.Where(x => x > 0), SeededComparer.Instance), new() };")]
    [InlineData("var tables = new HashSet<int>[] { new(a >> 1, SeededComparer.Instance), new() };")]
    [InlineData("var tables = new HashSet<int>[] { new(a < b ? 1 : 2), new(SeededComparer.Instance) };")]
    [InlineData("var tables = new HashSet<int>[,] { { new(SeededComparer.Instance), new() } };")]
    [InlineData("var tables = new HashSet<int>[] { flag ? new() : null };")]
    [InlineData("var tables = new HashSet<int>[] { flag ? new(SeededComparer.Instance) : new() };")]
    [InlineData("var tables = new HashSet<int>[] { other ?? new() };")]
    [InlineData("var tables = new HashSet<int>[] { (new()) };")]
    [InlineData("var tables = new HashSet<int>?[] { new(SeededComparer.Instance), new() };")]
    [InlineData("var tables = new HashSet<int>? [] { new(SeededComparer.Instance), new() };")]
    [InlineData("HashSet<int>[] tables = [new(), new(SeededComparer.Instance)];")]
    [InlineData("private readonly HashSet<int>[] _tables = [[]];")]
    [InlineData("public HashSet<int>[] Tables { get; } = [new()];")]
    [InlineData("HashSet<int>[]? tables = [new()];")]
    [InlineData("public HashSet<int>[]? Tables { get; } = [new()];")]
    [InlineData("HashSet<int>[]? tables = [new(SeededComparer.Instance), new()];")]
    [InlineData("HashSet<int>[][] tables = [[new(SeededComparer.Instance), new()]];")]
    [InlineData("var tables = new HashSet<int>[][] { [new(SeededComparer.Instance), new()] };")]
    [InlineData("private ConcurrentDictionary<int, string> _byNumber;\nvoid M() { _byNumber = new(SeededComparer.Instance); }")]
    [InlineData("private ConcurrentDictionary<int, string>? _byNumber;\nvoid M() => (_byNumber ??= new(SeededComparer.Instance)).Clear();")]
    [InlineData("var odd = Path.Distinct();")]
    [InlineData("var odd = @string.Join(Path, x => x, y => y, (x, y) => x);")]
    [InlineData("var seen = new HashSet<short>();")]
    [InlineData("var seen = new HashSet<ushort>();")]
    [InlineData("var seen = new HashSet<char>();")]
    [InlineData("var seen = new HashSet<uint>();")]
    [InlineData("var seen = new HashSet<long>();")]
    [InlineData("var seen = new HashSet<ulong>();")]
    [InlineData("var seen = new HashSet<nint>();")]
    [InlineData("var seen = new HashSet<nuint>();")]
    [InlineData("var seen = new HashSet<float>();")]
    [InlineData("var seen = new HashSet<double>();")]
    [InlineData("var seen = new HashSet<decimal>();")]
    [InlineData("var seen = new HashSet<Half>();")]
    [InlineData("var seen = new HashSet<Int16>();")]
    [InlineData("var seen = new HashSet<UInt16>();")]
    [InlineData("var seen = new HashSet<Char>();")]
    [InlineData("var seen = new HashSet<Int32>();")]
    [InlineData("var seen = new HashSet<UInt32>();")]
    [InlineData("var seen = new HashSet<Int64>();")]
    [InlineData("var seen = new HashSet<UInt64>();")]
    [InlineData("var seen = new HashSet<IntPtr>();")]
    [InlineData("var seen = new HashSet<UIntPtr>();")]
    [InlineData("var seen = new HashSet<Int128>();")]
    [InlineData("var seen = new HashSet<UInt128>();")]
    [InlineData("var seen = new HashSet<Single>();")]
    [InlineData("var seen = new HashSet<Double>();")]
    [InlineData("var seen = new HashSet<Decimal>();")]
    [InlineData("var seen = new HashSet<BigInteger>();")]
    [InlineData("var seen = new HashSet<Rune>();")]
    [InlineData("var seen = new HashSet<NFloat>();")]
    [InlineData("var seen = new HashSet<Complex>();")]
    [InlineData("var seen = pages.ToHashSetAsync();")]
    [InlineData("var seen = pages.ToDictionaryAsync(p => p);")]
    [InlineData("var seen = pages.ToLookupAsync(p => p);")]
    [InlineData("var seen = list.ToHashSet();")]
    [InlineData("var seen = list.ToDictionary(n => n);")]
    [InlineData("var seen = list.ToLookup(n => n);")]
    [InlineData("var seen = list.Distinct();")]
    [InlineData("var seen = list.DistinctBy(n => n);")]
    [InlineData("var seen = list.GroupBy(n => n);")]
    [InlineData("var seen = list.GroupJoin(other, n => n, o => o, (n, _) => n);")]
    [InlineData("var seen = list.Join(other, n => n, o => o, (n, _) => n);")]
    [InlineData("var seen = list.LeftJoin(other, n => n, o => o, (n, _) => n);")]
    [InlineData("var seen = list.RightJoin(other, n => n, o => o, (_, o) => o);")]
    [InlineData("var seen = list.Union(other);")]
    [InlineData("var seen = list.UnionBy(other, n => n);")]
    [InlineData("var seen = list.Intersect(other);")]
    [InlineData("var seen = list.IntersectBy(other, n => n);")]
    [InlineData("var seen = list.Except(other);")]
    [InlineData("var seen = list.ExceptBy(other, n => n);")]
    [InlineData("var seen = list.CountBy(n => n);")]
    [InlineData("var seen = list.AggregateBy(n => n, 0, (sum, n) => sum + n);")]
    [InlineData("var seen = list.ToFrozenDictionary(n => n);")]
    [InlineData("var seen = list.ToFrozenSet();")]
    [InlineData("var seen = list.ToImmutableDictionary(n => n);")]
    [InlineData("var seen = list.ToImmutableHashSet();")]
    [InlineData("var seen = new HashSet<int>(EqualityComparer<int>.Default);")]
    [InlineData("var seen = new HashSet<int>(Seeded.Default);")]
    [InlineData("var seen = new HashSet<KeyValuePair<long, int>>();")]
    [InlineData("var seen = new HashSet<KeyValuePair<string, string>>();")]
    [InlineData("var seen = new HashSet<global::System.Collections.Generic.KeyValuePair<long, int>>();")]
    [InlineData("var seen = new HashSet<ValueTuple<long, int>>();")]
    [InlineData("var seen = new HashSet<global::System.Int64>();")]
    [InlineData("var seen = new HashSet<global::System.Tuple<long, string>>();")]
    [InlineData("var seen = new HashSet<System.Numerics.BigInteger>();")]
    [InlineData("var seen = new HashSet<global::System.Nullable<long>>();")]
    [InlineData("private HashSet<int>? _x;\nvoid M() { _x = new(EqualityComparer<int>.Default); }")]
    [InlineData("private HashSet<int>? _x;\nvoid M() { _x = new(Seeded.Default); }")]
    [InlineData("var groups = list.GroupBy(x => x.CompareTo(0));")]
    [InlineData("var byNumber = new OrderedDictionary<int, string>();")]
    [InlineData("FrozenSet<int> f = [1, 2];")]
    [InlineData("ImmutableHashSet<int> s = [1, 2];")]
    [InlineData("ImmutableDictionary<int, string> d = [];")]
    [InlineData("public Dictionary<int, int> Indexes { get; } = new();")]
    [InlineData("private HashSet<int>? @lock;\nvoid M() { @lock = new(); }")]
    [InlineData("public HashSet<int> P { init; get; } = [];")]
    [InlineData("var seen = new HashSet<Tuple<string, int>>();")]
    [InlineData("var seen = new HashSet<System.Int32>();")]
    [InlineData("var seen = new HashSet<System.Tuple<int, string>>();")]
    [InlineData("var seen = new HashSet<(int @in, string @out)>();")]
    [InlineData("var byPair = new Dictionary<Tuple<int, string>, int>();")]
    [InlineData("HashSet<int>[] tables = [[], new(SeededComparer.Instance)];")]
    [InlineData("var seen = new HashSet<int>( // not SeededComparer.Instance\n);")]
    [InlineData("var seen = new HashSet<int>(/* SeededComparer.Instance */);")]
    [InlineData("var a = \"\"; var seen = new HashSet<int>();")]
    [InlineData("var p = @\"a\"\"b\"; var seen = new HashSet<int>();")]
    [InlineData("var s = \"{\"; var seen = new HashSet<int>();")]
    [InlineData("var m = $\"{x switch { 1 => \"a\", _ => \"b\" }}\"; var seen = new HashSet<int>();")]
    [InlineData("var odd = sortedPath.Join(right, l => l, r => r, (l, r) => l);")]
    [InlineData("var a = list.Distinct(); var b = names.Distinct(StringComparer.Ordinal);")]
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
    [InlineData("var at = new HashSet<int>(text.IndexOf(')'), SeededComparer.Instance);")]
    [InlineData("var at = new HashSet<int>(text.Count(\")\"), SeededComparer.Instance);")]
    [InlineData("var at = new HashSet<int>(text.Split(';').Length, SeededComparer.Instance);")]
    [InlineData("private HashSet<int>? _x;\nvoid M(int a, int b) { _x = new(Math.Max(a, b), SeededComparer.Instance); }")]
    [InlineData("var names = list.Distinct(StringComparer.Ordinal);")]
    [InlineData("var quoted = \"new HashSet<int>()\";")]
    [InlineData("var quoted = @\"new HashSet<int>()\";")]
    [InlineData("var quoted = \"\"\"\n    new HashSet<int>()\n    \"\"\";")]
    [InlineData("var seen = new HashSet<byte>();")]
    [InlineData("var q = @\"\"\"\"; var seen = new HashSet<int>(SeededComparer.Instance);")]
    [InlineData("var m = $\"{{new HashSet<int>()}}\";")]
    [InlineData("var joined = string.Join(\", \", parts);")]
    [InlineData("var path = Path.Join(root, name);")]
    [InlineData("var path = System.IO.Path.Join(root, name);")]
    [InlineData("var joined = global::System.String.Join(\", \", parts);")]
    [InlineData("var m = $\"{x:N0}\"; var seen = new HashSet<int>(SeededComparer.Instance);")]
    [InlineData("var m = $\"{global::System.Math.Abs(x)}\"; var seen = new HashSet<int>(SeededComparer.Instance);")]
    [InlineData("var seen = new HashSet<(string Name, bool Double)>(StringComparer.Ordinal);")]
    [InlineData("var tables = new HashSet<int>[4];")]
    [InlineData("private static readonly ConcurrentDictionary<string, PdfName> Interned = new(StringComparer.Ordinal);")]
    [InlineData("private readonly OrderedDictionary<int, string> _ordered = new(SeededComparer.Instance);")]
    [InlineData("var tables = new HashSet<int>[] { new(SeededComparer.Instance), seen, new(source, SeededComparer.Instance) };")]
    [InlineData("var tables = new HashSet<int>[] { new(n > 0 ? n : 1, SeededComparer.Instance) };")]
    [InlineData("var tables = new HashSet<int>[] { flag ? new(SeededComparer.Instance) : null };")]
    [InlineData("var tables = new HashSet<int>[] { new(Make(new(1, 2)), SeededComparer.Instance) };")]
    [InlineData("var tables = new HashSet<int>[][] { new HashSet<int>[] { new(SeededComparer.Instance) } };")]
    [InlineData("var tables = new HashSet<int>?[4];")]
    [InlineData("HashSet<int>[] tables = [];")]
    [InlineData("private readonly HashSet<int>[] _tables = [];")]
    [InlineData("HashSet<int>[] tables = [new(SeededComparer.Instance)];")]
    [InlineData("public HashSet<int>[] Tables { get; } = [new(SeededComparer.Instance)];")]
    [InlineData("HashSet<int>[]? tables = [new(SeededComparer.Instance)];")]
    [InlineData("class A { private HashSet<int> _t; }\nclass B { private ConcurrentDictionary<int, string> _t; }\nclass C { void M() { _t = new(SeededComparer.Instance); } }")]
    [InlineData("HashSet<int>[]? tables = [];")]
    [InlineData("HashSet<int>[][] tables = [[]];")]
    [InlineData("var tables = new HashSet<int>[][] { [] };")]
    [InlineData("HashSet<int>[][] tables = [[new(SeededComparer.Instance)]];")]
    [InlineData("void Use(HashSet<int>[] tables, int[] numbers) { numbers = [1, 2]; }")]
    [InlineData("var c = '\\''; var seen = new HashSet<int>(SeededComparer.Instance);")]
    public void The_reading_of_the_sources_lets_a_seeded_table_and_other_keys_be(string source)
    {
        SourceRules.Violations("Sample.cs", source).Should().BeEmpty();
    }

    [Fact]
    public void The_reading_of_the_sources_names_the_line_a_table_is_built_on()
    {
        // Past comments and literals that span lines, the line named is the one the table is built on.
        var source = "// one\n/* two\n three */\nvar text = @\"four\nfive\";\nvar seen = new HashSet<int>();";

        SourceRules.Violations("Sample.cs", source).Should().ContainSingle().Which.Should().StartWith("Sample.cs:6: ");
    }

    [Fact]
    public void No_method_of_the_library_builds_a_table_keyed_by_numbers_without_the_seeded_comparer()
    {
        // Read in the compiled library, whatever the source wrote — [], new(), an out var given a set, an expression
        // body, an argument, a copy, a base constructor, a LINQ operator, a method group —: every hash collection built
        // over a key a file can steer takes SeededComparer.Instance as the comparer of its key, taken last, handed to
        // it straight and on every path that reaches it. A key is steerable when it is a number — an integer wider than
        // a byte, a character or a Rune, a floating point, decimal or complex number —, nullable or not, a tuple or an
        // anonymous type holding one, a type of the library hashed as the compiler writes a record's hash, a fixed
        // combination of its members a file inverts, or a record that is not sealed, which a record so hashed may
        // derive from, a type deriving from a tuple holding one, a pair or another struct that keeps .NET's default
        // hash, or hides it with new, which a table does not call, and which hashes one holding a reference, a floating
        // point number, padding or a field with an equality of its own by its first non-null field alone, or what a
        // boxed number may be keyed as, any object, a value type or any interface, behind which a boxed tuple, record
        // or struct may stand too, which no comparer of a number seeds: an identity table is keyed by a reference type
        // of the library with ReferenceEqualityComparer.Instance, never by object. A collection that enumerates by hash
        // — concurrent, frozen or immutable, its factories, its builder and an immutable one's Empty included, a
        // StringDictionary or an obsolete XmlSchemaCollection, however built — is refused whatever its key and
        // comparer: a hash that goes through a seed, now or after a later change, would order what it gives (invariant
        // 6), and no key is argued one by one. The one named and let be is PdfName's interning table, keyed by strings,
        // which its static constructor builds once, ahead of every branch target and straight into a static field, and
        // the library reaches, through its type or a generic interface of it, only to find or add one name. An
        // enumeration's values are the library's own, and how the hash of a type the library hashes by hand spreads is
        // held by its own tests, as PdfObjectId's and PdfDiagnostic's are. The library builds no collection over a type
        // parameter, whatever its comparer: its callers choose its key, and no reading follows them; nor does it give
        // such a table type where a new() constraint, Activator, Lazy, LazyInitializer, a ConditionalWeakTable's
        // GetOrCreateValue, a BindingList or Marshal.PtrToStructure builds it, a Lazy or a BindingList one of them
        // builds counting as the table it builds in turn, nor build a type parameter it is given, whatever its
        // constraints, its own new() compiling to Activator, since what its callers give it is not all read — no new()
        // constraint of .NET 10's shared framework can stand for a table, and one of the library's is read in its own
        // body —, nor deserialize JSON into a type argument, which builds every table that type holds with .NET's
        // default comparer unless its options or the type say otherwise, out of the reading's sight: the library reads
        // no JSON.
        var violations = new List<string>();

        foreach (var (method, built) in CompiledRules.TablesBuilt(typeof(SeededComparer).Assembly.GetTypes(), typeof(PdfName)))
        {
            violations.Add($"{method.DeclaringType!.FullName}.{method.Name}: {CompiledRules.Describe(built)}");
        }

        violations.Should().BeEmpty("a hash table keyed by numbers takes SeededComparer.Instance");
    }

    [Fact]
    public void The_reading_of_the_compiled_library_refuses_a_table_built_without_the_comparer()
    {
        // The check above sees what a source reading cannot: an out var given [], an expression body, a comparer handed
        // on one path only, a base constructor, a factory, a collection expression lowered to one, a method group, a
        // table over a type parameter whatever its comparer, a table type a new() constraint, or one or any type
        // parameter that the library's own new(), Activator, Lazy, LazyInitializer, a ConditionalWeakTable, a
        // BindingList or Marshal.PtrToStructure builds, or a Lazy or a BindingList one of them builds, a JSON
        // deserializer whatever its type argument, a table keyed by any object, a value type or any interface, a type
        // deriving from a tuple, read through the tuple it hashes as, a record that is not sealed, a non-generic hash
        // table, and a collection that enumerates by hash, seeded or not: the order the process's seed gives it would
        // reach what the library reports or writes (invariant 6). The samples hold, one per condition, shapes the
        // source reading sees as well: every number type and every hashing operator among them.
        var refused = Refused(typeof(Unseeded));

        refused.Should().BeEquivalentTo(
            nameof(Unseeded.OutVar),
            nameof(Unseeded.ExpressionBody),
            nameof(Unseeded.Copies),
            nameof(Unseeded.StructKey),
            nameof(Unseeded.RecordKey),
            nameof(Unseeded.NullableKey),
            nameof(Unseeded.TupleKey),
            nameof(Unseeded.CharKey),
            nameof(Unseeded.DoubleKey),
            nameof(Unseeded.DefaultComparer),
            nameof(Unseeded.NullComparer),
            nameof(Unseeded.OnePath),
            nameof(Unseeded.Coalesced),
            nameof(Unseeded.Concurrent),
            nameof(Unseeded.Ordered),
            nameof(Unseeded.Untyped),
            nameof(Unseeded.Generic),
            nameof(Unseeded.Created),
            nameof(Unseeded.Operator),
            nameof(Unseeded.OperatorDefault),
            nameof(Unseeded.Frozen),
            nameof(Unseeded.FrozenLiteral),
            nameof(Unseeded.ImmutableRange),
            nameof(Unseeded.ImmutableEmpty),
            nameof(Unseeded.ImmutableCreated),
            nameof(Unseeded.ValueTupleKey),
            nameof(Unseeded.AnonymousKey),
            nameof(Unseeded.KeyedOperator),
            nameof(Unseeded.GenericSized),
            nameof(Unseeded.GenericNull),
            nameof(Unseeded.GenericDefault),
            nameof(Unseeded.ValueComparer),
            nameof(Unseeded.MethodGroup),
            nameof(Unseeded.Constructed),
            nameof(Unseeded.NullableTupleKey),
            nameof(Unseeded.Builder),
            nameof(Unseeded.ConcurrentSeeded),
            nameof(Unseeded.FrozenSeeded),
            nameof(Unseeded.ImmutableSeeded),
            nameof(Unseeded.FactorySeeded),
            nameof(Unseeded.KeyComparerSeeded),
            nameof(Unseeded.BuilderSeeded),
            nameof(Unseeded.FrozenIdentifiers),
            nameof(Unseeded.ConcurrentIdentifiers),
            nameof(Unseeded.ImmutablePairs),
            nameof(Unseeded.FrozenNames),
            nameof(Unseeded.ObjectKey),
            nameof(Unseeded.InterfaceKey),
            nameof(Unseeded.Hybrid),
            nameof(Unseeded.SpecializedOrdered),
            nameof(Unseeded.CaseInsensitive),
            nameof(Unseeded.OperatorAsync),
            nameof(Unseeded.ValueTypeKey),
            nameof(Unseeded.PairKey),
            nameof(Unseeded.NamedPairKey),
            nameof(Unseeded.PairsDistinct),
            nameof(Unseeded.PaddedKey),
            nameof(Unseeded.DisposableKey),
            nameof(Unseeded.HidingKey),
            nameof(Unseeded.TupleChildKey),
            nameof(Unseeded.GenericTupleChildKey),
            nameof(Unseeded.TupleGrandchildKey),
            nameof(Unseeded.HandHashedBaseKey),
            nameof(Unseeded.OperatorDictionaryAsync),
            nameof(Unseeded.OperatorLookupAsync),
            nameof(Unseeded.VirtualMade),
            nameof(Unseeded.RuneKey),
            nameof(Unseeded.NFloatKey),
            nameof(Unseeded.ComplexKey),
            nameof(Unseeded.RunesCounted),
            nameof(Unseeded.LookedUp),
            nameof(Unseeded.DistinctByKey),
            nameof(Unseeded.Grouped),
            nameof(Unseeded.GroupJoined),
            nameof(Unseeded.Joined),
            nameof(Unseeded.LeftJoined),
            nameof(Unseeded.RightJoined),
            nameof(Unseeded.United),
            nameof(Unseeded.UnitedBy),
            nameof(Unseeded.Intersected),
            nameof(Unseeded.IntersectedBy),
            nameof(Unseeded.Excepted),
            nameof(Unseeded.ExceptedBy),
            nameof(Unseeded.CountedBy),
            nameof(Unseeded.AggregatedBy),
            nameof(Unseeded.ShortKey),
            nameof(Unseeded.UShortKey),
            nameof(Unseeded.UIntKey),
            nameof(Unseeded.ULongKey),
            nameof(Unseeded.NIntKey),
            nameof(Unseeded.NUIntKey),
            nameof(Unseeded.Int128Key),
            nameof(Unseeded.UInt128Key),
            nameof(Unseeded.BigIntegerKey),
            nameof(Unseeded.HalfKey),
            nameof(Unseeded.FloatKey),
            nameof(Unseeded.DecimalKey),
            nameof(Unseeded.AnonymousSet),
            nameof(Unseeded.MixedTupleKey),
            nameof(Unseeded.Hinted),
            nameof(Unseeded.Strings),
            nameof(Unseeded.CreatedStrings),
            nameof(Unseeded.LazyStrings),
            nameof(Unseeded.Properties),
            nameof(Unseeded.Derived),
            nameof(Unseeded.EmptyIdentifiers),
            nameof(Unseeded.EmptyByAddress),
            nameof(Unseeded.VirtualGroup),
            nameof(Unseeded.CreatedIdentifiers),
            nameof(Unseeded.LazyNames),
            nameof(Unseeded.AnonymousIdentifiers),
            nameof(Unseeded.UnhashedKeys),
            nameof(Unseeded.NamedKeys),
            nameof(Unseeded.ObjectByReference),
            nameof(Unseeded.FrozenLimits),
            nameof(Unseeded.IndexedKeys),
            nameof(Unseeded.LimitPairs),
            nameof(Unseeded.LimitsCompared),
            nameof(Unseeded.StringKeyed),
            nameof(Unseeded.Given),
            nameof(Unseeded.ByReference),
            nameof(Unseeded.ActivatorOpen),
            nameof(Unseeded.CreatedHashtable),
            nameof(Unseeded.LazyValue),
            nameof(Unseeded.LazyInitialized),
            nameof(Unseeded.LazyMetadata),
            nameof(Unseeded.LazyOpen),
            nameof(Unseeded.WeakCreated),
            nameof(Unseeded.BoundList),
            nameof(Unseeded.BoundAdded),
            nameof(Unseeded.Marshalled),
            nameof(Unseeded.Deserialized),
            nameof(Unseeded.Fetched),
            nameof(Unseeded.ReadAsJson),
            nameof(Unseeded.ActivatorBare),
            nameof(Unseeded.LazyInitializedBare),
            nameof(Unseeded.MarshalledBare),
            nameof(Unseeded.WeakOpen),
            nameof(Unseeded.BoundOpen),
            nameof(Unseeded.BoundAddedOpen),
            nameof(Unseeded.StoredOnly),
            nameof(Unseeded.LazyOfLazy),
            nameof(Unseeded.ActivatorLazy),
            nameof(Unseeded.MadeBound),
            nameof(Unseeded.Gotten),
            nameof(Unseeded.Entries),
            nameof(Unseeded.Extended),
            nameof(Unseeded.Schemas),
            nameof(Unseeded.BoundTables),
            nameof(Unseeded.CreatedDictionary),
            nameof(Unseeded.LimitOffsetKey),
            nameof(Unseeded.LongFirstKey),
            nameof(Unseeded.TripleKey),
            nameof(Unseeded.EquatableKey),
            nameof(Unseeded.DeserializedAsync),
            nameof(Unseeded.ReadAsJsonStream),
            nameof(Unseeded.LazyInitializedGuarded),
            nameof(Unseeded.LazyThreadSafe),
            nameof(Unseeded.PairedSecond),
            nameof(Unseeded.PairedFirst),
            nameof(Unseeded.MakeSecond),
            nameof(Unseeded.MakeFirst),
            nameof(Unseeded.Implemented),
            nameof(Unseeded.LazyMetadataNumber),
            nameof(Unseeded.CreatedPairs),
            nameof(Unseeded.ParallelDistinct),
            nameof(Unseeded.ValidatingSchemas),
            nameof(Unseeded.AnonymousPairs),
            "EntryDictionary",
            nameof(Unseeded.LazyConstrained),
            nameof(Unseeded.ActivatorLazyOpen),
            nameof(Unseeded.Make),
            nameof(Unseeded.MakeHinted),
            "Wrapper",
            "Holder",
            nameof(Unseeded.NestedCreation),
            nameof(Unseeded.WrappedCreation),
            "IntDictionary",
            "NumberCollection",
            "Cache",
            "ForwardingDictionary",
            "DerivedStrings");
    }

    [Fact]
    public void The_reading_of_the_compiled_library_lets_a_seeded_table_and_other_keys_be()
    {
        Refused(typeof(Seeded)).Should().BeEmpty();
    }

    [Fact]
    public void The_reading_of_the_compiled_library_lets_be_one_interning_table_and_nothing_that_enumerates_it()
    {
        // PdfName's interning table is the one collection that enumerates by hash let be. A stand-in holds each
        // condition: the first table from strings to the type its static constructor builds and stores straight into a
        // static field is let be; a second, one keyed to another type, one stored elsewhere, one another method or
        // another type builds, a frozen or immutable one, and a call to, or a method group of, a member that enumerates
        // it, views it, copies it, hands it out or names its pairs, in an array, a reference or a type argument
        // included, at any depth, and a static field declared on a type built over its pairs or a view of it, are
        // refused, and finding or adding one entry, through its type or an interface of it, is not. One built in a
        // loop, which may run it more than once, is refused too, whether the loop's branch lands on the construction or
        // ahead of it. Its static constructor is read after its methods, and the other type before it, so that a
        // condition left out lets be another table than the one it should, which the constructor's parameters tell
        // apart.
        var refused = CompiledRules.TablesBuilt([typeof(Interning), typeof(Interned)], typeof(Interned))
            .Select(found => $"{found.Method.DeclaringType!.Name}.{found.Method.Name}: {CompiledRules.Describe(found.Built)}({Parameters(found.Built)})");

        refused.Should().BeEquivalentTo(
            "Interning..cctor: ConcurrentDictionary`2<String, Interned>..ctor(IEqualityComparer`1)",
            "Interned.Built: ConcurrentDictionary`2<String, Interned>..ctor(IEqualityComparer`1)",
            "Interned.Replace: ConcurrentDictionary`2<String, Interned>..ctor(IEqualityComparer`1)",
            "Interned.Strings: ImmutableHashSet.ToImmutableHashSet(IEnumerable`1, IEqualityComparer`1)",
            "Interned.Keys: ConcurrentDictionary`2<String, Interned>.get_Keys()",
            "Interned.Snapshot: ConcurrentDictionary`2<String, Interned>.ToArray()",
            "Interned.Valued: ConcurrentDictionary`2<String, Interned>.get_Values()",
            "Interned.CopiedTo: ICollection`1<KeyValuePair`2>.CopyTo(KeyValuePair`2[], Int32)",
            "Interned.Listed: Enumerable.ToList(IEnumerable`1)",
            "Interned.Enumerated: IEnumerable`1<KeyValuePair`2>.GetEnumerator()",
            "Interned.Viewed: IReadOnlyDictionary`2<String, Interned>.get_Keys()",
            "Interned.Copied: Dictionary`2<String, Interned>..ctor(IDictionary`2)",
            "Interned.Handed: Interned.View()",
            "Interned.Pairs: List`1<KeyValuePair`2>.get_Count()",
            "Interned.Joined: Interned.JoinOf()",
            "Interned.Deferred: IEnumerable`1<KeyValuePair`2>.GetEnumerator()",
            "Interned.Given: Interned.Give(IReadOnlyDictionary`2&)",
            "Interned.Arrayed: Interned.CountViews(IReadOnlyDictionary`2[])",
            "Interned.Fetched: Interned.TryGetValue(ConcurrentDictionary`2&)",
            "Interned.NoPairs: ImmutableList`1<KeyValuePair`2>.Empty()",
            "Interned.NoPairs: ImmutableList`1<KeyValuePair`2>.get_Count()",
            "Interned.NoPairsAt: ImmutableList`1<KeyValuePair`2>.Empty()",
            "Interned.NoPairsAt: Interned.CountPairs(ImmutableList`1&)",
            "Interned.CountPairs: ImmutableList`1<KeyValuePair`2>.get_Count()",
            "Interned.ViewGroup: Interned.View()",
            "Interned.Nested: List`1<List`1>.get_Count()",
            "Interned.Jaggeds: Interned.Jagged(IReadOnlyDictionary`2[][])",
            "Interned.Typed: Interned.CountOf()",
            "Interned.Postponed: Interned.Later()",
            "Interned.Paired: KeyValuePair`2<String, Interned>..ctor(String, Interned)",
            "Interned.FromArray: IList`1<ConcurrentDictionary`2>.get_Item(Int32)",
            "Interned..cctor: ConcurrentDictionary`2<String, Int32>..ctor(IEqualityComparer`1)",
            "Interned..cctor: ConcurrentDictionary`2<String, Interned>..ctor()",
            "Interned..cctor: ConcurrentDictionary`2<String, Interned>..ctor(IEqualityComparer`1)");

        CompiledRules.TablesBuilt([typeof(Looped)], typeof(Looped))
            .Select(found => $"{found.Method.Name}: {CompiledRules.Describe(found.Built)}")
            .Should().Equal(".cctor: ConcurrentDictionary`2<String, Looped>..ctor");
        CompiledRules.TablesBuilt([typeof(LoopedCompared)], typeof(LoopedCompared))
            .Select(found => $"{found.Method.Name}: {CompiledRules.Describe(found.Built)}")
            .Should().Equal(".cctor: ConcurrentDictionary`2<String, LoopedCompared>..ctor");
        CompiledRules.TablesBuilt([typeof(LoopedLong)], typeof(LoopedLong))
            .Select(found => $"{found.Method.Name}: {CompiledRules.Describe(found.Built)}")
            .Should().Equal(".cctor: ConcurrentDictionary`2<String, LoopedLong>..ctor");
        CompiledRules.TablesBuilt([typeof(LoopedSwitch)], typeof(LoopedSwitch))
            .Select(found => $"{found.Method.Name}: {CompiledRules.Describe(found.Built)}")
            .Should().Equal(".cctor: ConcurrentDictionary`2<String, LoopedSwitch>..ctor");
        CompiledRules.TablesBuilt([typeof(LoopedSwitchBare)], typeof(LoopedSwitchBare))
            .Select(found => $"{found.Method.Name}: {CompiledRules.Describe(found.Built)}")
            .Should().Equal(".cctor: ConcurrentDictionary`2<String, LoopedSwitchBare>..ctor");
        CompiledRules.TablesBuilt([typeof(Instanced)], typeof(Instanced))
            .Select(found => $"{found.Method.Name}: {CompiledRules.Describe(found.Built)}")
            .Should().Equal(".ctor: ConcurrentDictionary`2<String, Instanced>..ctor");
        CompiledRules.TablesBuilt([typeof(Boxed)], typeof(Boxed))
            .Select(found => $"{found.Method.Name}: {CompiledRules.Describe(found.Built)}")
            .Should().Equal(".cctor: ConcurrentDictionary`2<String, Boxed>..ctor");

        static string Parameters(MemberInfo built) =>
            built is MethodBase called ? string.Join(", ", called.GetParameters().Select(parameter => parameter.ParameterType.Name)) : "";
    }

    [Fact]
    public void The_reading_of_the_compiled_library_reads_through_the_probes_code_coverage_puts()
    {
        // Code coverage, as CI runs the tests, puts a probe at each sequence point, which may fall between the
        // comparer's getter and the table's constructor: the probe is not code the source wrote, and the table stays
        // seeded. Any other instruction there is read as it stands, a tracker's field stored otherwise than a probe
        // stores it included, so that a probe another instrumenter writes otherwise refuses the table rather than
        // hiding what follows it. A branch that lands on a probe lands on what follows it, so that a comparer handed on
        // one path only, the probe at the join, stays refused; and a probe takes out itself alone, so that a table
        // built with no comparer right after one stays refused. A local an instruction addresses by two bytes is read
        // as two, and a constant of eight bytes as eight, so that the rest of an operand, read as instructions, does
        // not hide the table built after it.
        var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Probed"), AssemblyBuilderAccess.Run).DefineDynamicModule("Probed");
        var tracker = module.DefineType("StaticManagedTrackerTemplate_Sample", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var begin = tracker.DefineField("Begin", typeof(nint), FieldAttributes.Public | FieldAttributes.Static);
        var hits = tracker.DefineField("Hits", typeof(byte[]), FieldAttributes.Public | FieldAttributes.Static);
        tracker.CreateType();
        var samples = module.DefineType("Samples", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var seeded = typeof(SeededComparer).GetProperty(nameof(SeededComparer.Instance))!.GetMethod!;
        var constructor = typeof(HashSet<int>).GetConstructor([typeof(IEqualityComparer<int>)])!;
        Emit("Probed", il => il.Emit(OpCodes.Ldsfld, begin), il => il.Emit(OpCodes.Ldc_I4, 7), il => il.Emit(OpCodes.Add), il => il.Emit(OpCodes.Ldc_I4_1), il => il.Emit(OpCodes.Stind_I1));
        Emit("First", il => il.Emit(OpCodes.Ldsfld, begin), il => il.Emit(OpCodes.Ldc_I4_1), il => il.Emit(OpCodes.Stind_I1));
        Emit("Wedged", il => il.Emit(OpCodes.Ldc_I4, 7), il => il.Emit(OpCodes.Pop));
        Emit("Misshapen", il => il.Emit(OpCodes.Ldsfld, hits), il => il.Emit(OpCodes.Ldc_I4, 7), il => il.Emit(OpCodes.Ldc_I4_1), il => il.Emit(OpCodes.Stelem_I1));
        var branched = samples.DefineMethod("OnePathProbed", MethodAttributes.Public | MethodAttributes.Static, typeof(HashSet<int>), [typeof(bool)]).GetILGenerator();
        var handed = branched.DefineLabel();
        var join = branched.DefineLabel();
        branched.Emit(OpCodes.Ldarg_0);
        branched.Emit(OpCodes.Brtrue, handed);
        branched.Emit(OpCodes.Ldnull);
        branched.Emit(OpCodes.Br, join);
        branched.MarkLabel(handed);
        branched.Emit(OpCodes.Call, seeded);
        branched.MarkLabel(join);
        branched.Emit(OpCodes.Ldsfld, begin);
        branched.Emit(OpCodes.Ldc_I4_1);
        branched.Emit(OpCodes.Stind_I1);
        branched.Emit(OpCodes.Newobj, constructor);
        branched.Emit(OpCodes.Ret);
        var bare = samples.DefineMethod("ProbeThenBare", MethodAttributes.Public | MethodAttributes.Static, typeof(HashSet<int>), Type.EmptyTypes).GetILGenerator();
        bare.Emit(OpCodes.Ldsfld, begin);
        bare.Emit(OpCodes.Ldc_I4, 3);
        bare.Emit(OpCodes.Add);
        bare.Emit(OpCodes.Ldc_I4_1);
        bare.Emit(OpCodes.Stind_I1);
        bare.Emit(OpCodes.Newobj, typeof(HashSet<int>).GetConstructor(Type.EmptyTypes)!);
        bare.Emit(OpCodes.Ret);
        var many = samples.DefineMethod("ManyLocals", MethodAttributes.Public | MethodAttributes.Static, typeof(HashSet<int>), Type.EmptyTypes).GetILGenerator();
        var last = many.DeclareLocal(typeof(int));

        while (last.LocalIndex < 0x0E01)
        {
            last = many.DeclareLocal(typeof(int));
        }

        many.Emit(OpCodes.Ldc_I4_0);
        many.Emit(OpCodes.Stloc, last);
        many.Emit(OpCodes.Newobj, typeof(HashSet<int>).GetConstructor(Type.EmptyTypes)!);
        many.Emit(OpCodes.Ret);
        var weighed = samples.DefineMethod("AfterDouble", MethodAttributes.Public | MethodAttributes.Static, typeof(HashSet<int>), Type.EmptyTypes).GetILGenerator();
        weighed.Emit(OpCodes.Ldc_R8, 0.5);
        weighed.Emit(OpCodes.Pop);
        weighed.Emit(OpCodes.Newobj, typeof(HashSet<int>).GetConstructor(Type.EmptyTypes)!);
        weighed.Emit(OpCodes.Ret);
        var type = samples.CreateType();

        CompiledRules.TablesBuilt([type]).Select(found => found.Method.Name)
            .Should().BeEquivalentTo("Wedged", "Misshapen", "OnePathProbed", "ProbeThenBare", "ManyLocals", "AfterDouble");

        void Emit(string name, params Action<ILGenerator>[] between)
        {
            var il = samples.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(HashSet<int>), Type.EmptyTypes).GetILGenerator();
            il.Emit(OpCodes.Call, seeded);

            foreach (var emit in between)
            {
                emit(il);
            }

            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Gets the samples of <paramref name="samples"/> the compiled reading refuses: a method by its name, a type nested in
    /// it by the type's.
    /// </summary>
    private static List<string> Refused(Type samples) =>
        CompiledRules.TablesBuilt([samples, .. samples.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)])
            .Select(found => found.Method.DeclaringType == samples ? found.Method.Name : found.Method.DeclaringType!.Name.Split('`')[0])
            .Distinct()
            .ToList();

    /// <summary>Hash collections the compiled reading refuses, each sample its own method or type.</summary>
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

        public static HashSet<Pair> StructKey() => [];

        public static HashSet<Numbered> RecordKey() => [];

        public static HashSet<int?> NullableKey() => [];

        public static HashSet<Tuple<int, int>> TupleKey() => [];

        public static HashSet<char> CharKey() => [];

        public static HashSet<double> DoubleKey() => [];

        public static HashSet<int> DefaultComparer() => new(EqualityComparer<int>.Default);

        public static Dictionary<long, int> NullComparer(int capacity) => new(capacity, null);

        public static HashSet<int> OnePath(bool seeded) => new(seeded ? SeededComparer.Instance : null);

        public static HashSet<int> Coalesced(IEqualityComparer<int>? comparer) => new(comparer ?? SeededComparer.Instance);

        public static ConcurrentDictionary<int, string> Concurrent() => new();

        public static OrderedDictionary<long, string> Ordered() => [];

        public static Hashtable Untyped() => [];

        public static HashSet<T> Generic<T>() => [];

        public static HashSet<int> Created() => Activator.CreateInstance<HashSet<int>>();

        public static HashSet<int> Operator(IEnumerable<int> numbers) => numbers.ToHashSet();

        public static IEnumerable<int> OperatorDefault(IEnumerable<int> numbers) => numbers.Distinct(EqualityComparer<int>.Default);

        public static FrozenSet<int> Frozen(IEnumerable<int> numbers) => numbers.ToFrozenSet();

        public static FrozenSet<long> FrozenLiteral() => [1, 2];

        public static ImmutableHashSet<long> ImmutableRange(IEnumerable<long> numbers) => ImmutableHashSet.CreateRange(numbers);

        public static ImmutableHashSet<int> ImmutableEmpty() => ImmutableHashSet<int>.Empty.Add(1);

        public static ImmutableDictionary<int, int> ImmutableCreated() => ImmutableDictionary.Create<int, int>();

        public static HashSet<(long, int)> ValueTupleKey() => [];

        public static int AnonymousKey(int number) => ImmutableHashSet.Create(new { A = number, B = number }).Count;

        public static Dictionary<int, string> KeyedOperator(IEnumerable<string> names) => names.ToDictionary(name => name.Length);

        public static HashSet<T> GenericSized<T>(int capacity) => new(capacity);

        public static HashSet<T> GenericNull<T>() => new((IEqualityComparer<T>?)null);

        public static HashSet<T> GenericDefault<T>() => new(EqualityComparer<T>.Default);

        public static ImmutableDictionary<int, int> ValueComparer() => ImmutableDictionary.Create<int, int>(null, SeededComparer.Instance);

        public static HashSet<(long, int)?> NullableTupleKey() => [];

        public static ImmutableHashSet<int>.Builder Builder() => ImmutableHashSet.CreateBuilder<int>();

        public static ConcurrentDictionary<int, string> ConcurrentSeeded() => new(SeededComparer.Instance);

        public static FrozenSet<int> FrozenSeeded(IEnumerable<int> numbers) => numbers.ToFrozenSet(SeededComparer.Instance);

        public static ImmutableHashSet<long> ImmutableSeeded(IEnumerable<long> numbers) => numbers.ToImmutableHashSet(SeededComparer.Instance);

        public static ImmutableHashSet<int> FactorySeeded() => ImmutableHashSet.Create<int>(SeededComparer.Instance);

        public static ImmutableDictionary<int, string> KeyComparerSeeded() => ImmutableDictionary.Create<int, string>(SeededComparer.Instance);

        public static ImmutableHashSet<int>.Builder BuilderSeeded() => ImmutableHashSet.CreateBuilder<int>(SeededComparer.Instance);

        public static FrozenSet<PdfObjectId> FrozenIdentifiers(IEnumerable<PdfObjectId> identifiers) => identifiers.ToFrozenSet();

        public static ConcurrentDictionary<PdfObjectId, int> ConcurrentIdentifiers() => new();

        public static ImmutableHashSet<(PdfObjectId, string)> ImmutablePairs(IEnumerable<(PdfObjectId, string)> pairs) => pairs.ToImmutableHashSet();

        public static FrozenDictionary<PdfName, int> FrozenNames(Dictionary<PdfName, int> names) => names.ToFrozenDictionary();

        public static HashSet<object> ObjectKey() => [];

        public static Dictionary<IComparable, int> InterfaceKey() => [];

        public static int Hybrid() => new System.Collections.Specialized.HybridDictionary().Count;

        public static int SpecializedOrdered() => new System.Collections.Specialized.OrderedDictionary().Count;

        public static int CaseInsensitive() => System.Collections.Specialized.CollectionsUtil.CreateCaseInsensitiveHashtable().Count;

        public static ValueTask<HashSet<int>> OperatorAsync(IAsyncEnumerable<int> numbers) => numbers.ToHashSetAsync();

        public static HashSet<ValueType> ValueTypeKey() => [];

        public static HashSet<KeyValuePair<long, int>> PairKey() => [];

        public static HashSet<KeyValuePair<string, string>> NamedPairKey() => [];

        public static HashSet<IDisposable> DisposableKey() => [];

        public static HashSet<Hiding> HidingKey() => [];

        public static HashSet<TupleChild> TupleChildKey() => [];

        public static HashSet<GenericTupleChild<long>> GenericTupleChildKey() => [];

        public static HashSet<TupleGrandchild> TupleGrandchildKey() => [];

        public static HashSet<HandHashedBase> HandHashedBaseKey() => [new HandHashedDerived("name", 0)];

        public static int PairsDistinct(IEnumerable<KeyValuePair<long, int>> pairs) => pairs.Distinct().Count();

        public static HashSet<Padded> PaddedKey() => [];

        public static ValueTask<Dictionary<int, int>> OperatorDictionaryAsync(IAsyncEnumerable<int> numbers) => numbers.ToDictionaryAsync(n => n);

        public static ValueTask<ILookup<int, int>> OperatorLookupAsync(IAsyncEnumerable<int> numbers) => numbers.ToLookupAsync(n => n);

        public static int VirtualMade(IMaker maker) => maker.Make<HashSet<int>>().Count;

        public static HashSet<System.Text.Rune> RuneKey() => [];

        public static HashSet<System.Runtime.InteropServices.NFloat> NFloatKey() => [];

        public static HashSet<System.Numerics.Complex> ComplexKey() => [];

        public static int RunesCounted(IEnumerable<System.Text.Rune> runes) => runes.CountBy(rune => rune).Count();

        public static int LookedUp(int[] numbers, int[] others) => numbers.ToLookup(n => n).Count;

        public static int DistinctByKey(int[] numbers, int[] others) => numbers.DistinctBy(n => n).Count();

        public static int Grouped(int[] numbers, int[] others) => numbers.GroupBy(n => n).Count();

        public static int GroupJoined(int[] numbers, int[] others) => numbers.GroupJoin(others, n => n, o => o, (n, _) => n).Count();

        public static int Joined(int[] numbers, int[] others) => numbers.Join(others, n => n, o => o, (n, _) => n).Count();

        public static int LeftJoined(int[] numbers, int[] others) => numbers.LeftJoin(others, n => n, o => o, (n, _) => n).Count();

        public static int RightJoined(int[] numbers, int[] others) => numbers.RightJoin(others, n => n, o => o, (_, o) => o).Count();

        public static int United(int[] numbers, int[] others) => numbers.Union(others).Count();

        public static int UnitedBy(int[] numbers, int[] others) => numbers.UnionBy(others, n => n).Count();

        public static int Intersected(int[] numbers, int[] others) => numbers.Intersect(others).Count();

        public static int IntersectedBy(int[] numbers, int[] others) => numbers.IntersectBy(others, n => n).Count();

        public static int Excepted(int[] numbers, int[] others) => numbers.Except(others).Count();

        public static int ExceptedBy(int[] numbers, int[] others) => numbers.ExceptBy(others, n => n).Count();

        public static int CountedBy(int[] numbers, int[] others) => numbers.CountBy(n => n).Count();

        public static int AggregatedBy(int[] numbers, int[] others) => numbers.AggregateBy(n => n, 0, (sum, n) => sum + n).Count();

        public static HashSet<short> ShortKey() => [];

        public static HashSet<ushort> UShortKey() => [];

        public static HashSet<uint> UIntKey() => [];

        public static HashSet<ulong> ULongKey() => [];

        public static HashSet<nint> NIntKey() => [];

        public static HashSet<nuint> NUIntKey() => [];

        public static HashSet<Int128> Int128Key() => [];

        public static HashSet<UInt128> UInt128Key() => [];

        public static HashSet<System.Numerics.BigInteger> BigIntegerKey() => [];

        public static HashSet<Half> HalfKey() => [];

        public static HashSet<float> FloatKey() => [];

        public static HashSet<decimal> DecimalKey() => [];

        public static int AnonymousSet(IEnumerable<int> numbers) => numbers.Select(n => new { N = n }).ToHashSet().Count;

        public static HashSet<(long, string)> MixedTupleKey() => [];

        public static int Hinted() => MakeHinted<HashSet<int>>(SeededComparer.Instance).Count;

        public static T MakeHinted<T>(object hint)
            where T : new()
        {
            GC.KeepAlive(hint);
            return new();
        }

        public static int Strings() => new System.Collections.Specialized.StringDictionary().Count;

        public static int CreatedStrings() => Activator.CreateInstance<System.Collections.Specialized.StringDictionary>().Count;

        public static int LazyStrings() => new Lazy<System.Collections.Specialized.StringDictionary>().Value.Count;

        public static int Properties() => new System.Data.PropertyCollection().Count;

        public static int Derived() => new DerivedStrings().Count;

        public static ImmutableHashSet<PdfObjectId> EmptyIdentifiers() => ImmutableHashSet<PdfObjectId>.Empty.Add(default);

        public static int EmptyByAddress() => Count(in ImmutableHashSet<int>.Empty);

        public static HashSet<int> VirtualGroup(IMaker maker)
        {
            Func<HashSet<int>> group = maker.Make<HashSet<int>>;
            return group();
        }

        public static int CreatedIdentifiers() => Activator.CreateInstance<ConcurrentDictionary<PdfObjectId, int>>().Count;

        public static int LazyNames()
        {
            ConcurrentDictionary<PdfName, int>? names = null;
            return LazyInitializer.EnsureInitialized(ref names).Count;
        }

        public static int AnonymousIdentifiers(PdfObjectId identifier) => ImmutableHashSet.Create(new { Identifier = identifier }).Count;

        public static FrozenSet<Unhashed> UnhashedKeys(IEnumerable<Unhashed> keys) => keys.ToFrozenSet();

        public static FrozenSet<NamedKey> NamedKeys(IEnumerable<NamedKey> keys) => keys.ToFrozenSet();

        public static int ObjectByReference() => new HashSet<object>(ReferenceEqualityComparer.Instance).Count;

        public static FrozenSet<PdfLimit> FrozenLimits(IEnumerable<PdfLimit> limits) => limits.ToFrozenSet();

        public static FrozenSet<IndexedKey> IndexedKeys(IEnumerable<IndexedKey> keys) => keys.ToFrozenSet();

        public static ImmutableHashSet<(PdfLimit, PdfLimit)> LimitPairs(IEnumerable<(PdfLimit, PdfLimit)> pairs) => pairs.ToImmutableHashSet();

        public static ConcurrentDictionary<PdfLimit, int> LimitsCompared(IEqualityComparer<PdfLimit> comparer) => new(comparer);

        public static ConcurrentDictionary<string, int> StringKeyed() => new(StringComparer.Ordinal);

        public static HashSet<(T, string)> Given<T>(IEqualityComparer<(T, string)> comparer) => new(comparer);

        public static HashSet<List<T>> ByReference<T>() => new(ReferenceEqualityComparer.Instance);

        public static HashSet<T> ActivatorOpen<T>() => Activator.CreateInstance<HashSet<T>>();

        public static int CreatedHashtable() => Activator.CreateInstance<Hashtable>().Count;

        public static int LazyValue() => new Lazy<HashSet<int>>().Value.Count;

        public static int LazyInitialized()
        {
            HashSet<int>? seen = null;
            return LazyInitializer.EnsureInitialized(ref seen).Count;
        }

        public static int LazyMetadata() => new Lazy<HashSet<int>, HashSet<int>>(new HashSet<int>(SeededComparer.Instance)).Value.Count;

        public static T LazyOpen<T>() => new Lazy<T>().Value;

        public static int WeakCreated(System.Runtime.CompilerServices.ConditionalWeakTable<object, HashSet<int>> table, object key) =>
            table.GetOrCreateValue(key).Count;

        public static int BoundList() => new System.ComponentModel.BindingList<HashSet<int>>().Count;

        public static int BoundAdded(System.ComponentModel.BindingList<HashSet<int>> list) => list.AddNew().Count;

        public static int Marshalled(IntPtr pointer) => System.Runtime.InteropServices.Marshal.PtrToStructure<Hashtable>(pointer)?.Count ?? 0;

        public static int Deserialized() => System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, int>>("{}")!.Count;

        public static Task<int> Fetched(HttpClient client) =>
            System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<int>(client, new Uri("http://localhost/"));

        public static Task<int> ReadAsJson(HttpContent content) => System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<int>(content);

        public static T ActivatorBare<T>() => Activator.CreateInstance<T>();

        public static T LazyConstrained<T>()
            where T : new() => new Lazy<T>().Value;

        public static T ActivatorLazyOpen<T>() => Activator.CreateInstance<Lazy<T>>().Value;

        public static T LazyInitializedBare<T>()
            where T : class
        {
            T? value = null;
            return LazyInitializer.EnsureInitialized(ref value);
        }

        public static T? MarshalledBare<T>(IntPtr pointer) => System.Runtime.InteropServices.Marshal.PtrToStructure<T>(pointer);

        public static T WeakOpen<T>(System.Runtime.CompilerServices.ConditionalWeakTable<object, T> table, object key)
            where T : class => table.GetOrCreateValue(key);

        public static System.ComponentModel.BindingList<T> BoundOpen<T>() => [];

        public static T BoundAddedOpen<T>(System.ComponentModel.BindingList<T> list) => list.AddNew();

        public static void StoredOnly() => Holder<HashSet<int>>.Other = null;

        public static int LazyOfLazy() => new Lazy<Lazy<HashSet<int>>>().Value.Value.Count;

        public static int ActivatorLazy() => Activator.CreateInstance<Lazy<HashSet<int>>>().Value.Count;

        public static int MadeBound() => Make<System.ComponentModel.BindingList<HashSet<int>>>().Count;

        public static int NestedCreation() => Make<Wrapper<HashSet<int>>>() is null ? 0 : 1;

        public static int WrappedCreation() => new Wrapper<HashSet<int>>().Inner.Count;

        public static HashSet<int> MethodGroup(IEnumerable<int> numbers)
        {
            Func<IEnumerable<int>, HashSet<int>> build = Enumerable.ToHashSet;
            return build(numbers);
        }

        public static int Constructed() => Make<HashSet<int>>().Count;

        public static T Make<T>()
            where T : new() => new();

        public static int Subclasses() => new IntDictionary().Count + new NumberCollection().Count + new Cache<int>().Seen.Count;

        public static int Entries() => new EntryDictionary().Count;

        public static int Gotten() => new System.Diagnostics.TraceSource("probe").Attributes.Count;

        public static int Extended()
        {
            using var table = new System.Data.DataTable();
            return table.ExtendedProperties.Count;
        }

#pragma warning disable CS0618 // Obsolete: a sample of what the reading refuses, a Hashtable it enumerates.
        public static int Schemas() => new System.Xml.Schema.XmlSchemaCollection().Count;
#pragma warning restore CS0618

        public static int BoundTables() => new System.ComponentModel.BindingList<Hashtable>().Count;

        public static int CreatedDictionary() => Activator.CreateInstance<Dictionary<long, string>>().Count;

        public static HashSet<(PdfLimit, long)> LimitOffsetKey() => [];

        public static HashSet<LongFirst> LongFirstKey() => [];

        public static HashSet<(long, int, int)> TripleKey() => [];

        public static HashSet<IEquatable<long>> EquatableKey() => [];

        public static ValueTask<int> DeserializedAsync(Stream stream) => System.Text.Json.JsonSerializer.DeserializeAsync<int>(stream);

        public static IAsyncEnumerable<int> ReadAsJsonStream(HttpContent content) =>
            System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsAsyncEnumerable<int>(content);

        public static int LazyInitializedGuarded()
        {
            HashSet<int>? seen = null;
            var initialized = false;
            object? gate = null;
            return LazyInitializer.EnsureInitialized(ref seen, ref initialized, ref gate)!.Count;
        }

        public static int LazyThreadSafe() => new Lazy<HashSet<int>>(isThreadSafe: true).Value.Count;

        public static int PairedSecond() => MakeSecond<int, HashSet<int>>().Count;

        public static int PairedFirst() => MakeFirst<HashSet<int>, int>().Count;

        public static TSecond MakeSecond<TFirst, TSecond>()
            where TSecond : new() => new();

        public static TFirst MakeFirst<TFirst, TSecond>()
            where TFirst : new() => new();

        public static int Implemented(System.Xml.Serialization.XmlSerializerImplementation implementation) =>
            implementation.ReadMethods?.Count ?? 0;

        public static int LazyMetadataNumber() => new Lazy<HashSet<int>, int>(0).Value.Count;

        public static int CreatedPairs<T>() => Activator.CreateInstance<HashSet<(T, string)>>().Count;

        public static int ParallelDistinct(int[] numbers) => numbers.AsParallel().Distinct().Count();

        public static int AnonymousPairs(IEnumerable<string> names) => names.Select(name => new { Name = name, Offset = 0L }).ToHashSet().Count;

#pragma warning disable CS0618 // Obsolete: a sample of what the reading refuses, a getter returning a hash table.
        public static int ValidatingSchemas(System.Xml.XmlReader reader)
        {
            using var validating = new System.Xml.XmlValidatingReader(reader);
            return validating.Schemas.Count;
        }
#pragma warning restore CS0618

        private sealed class IntDictionary : Dictionary<int, string>;

        /// <summary>A table derived from a non-generic one, which its base constructor builds.</summary>
        private sealed class DerivedStrings : System.Collections.Specialized.StringDictionary;

        /// <summary>
        /// A typed dictionary over the Hashtable its base keeps, keyed by any object and enumerated by hash.
        /// </summary>
        private sealed class EntryDictionary : System.Collections.DictionaryBase;

        private sealed class NumberCollection : KeyedCollection<int, string>
        {
            protected override int GetKeyForItem(string item) => item.Length;
        }

        private sealed class Cache<TKey>
            where TKey : notnull
        {
            public Dictionary<TKey, int> Seen { get; } = [];
        }

        /// <summary>A table over a type parameter, handed the comparer its callers choose, which no reading follows.</summary>
        private sealed class ForwardingDictionary<T>(IEqualityComparer<T>? comparer) : Dictionary<T, string>(comparer)
            where T : notnull;

        /// <summary>What a virtual generic method builds for its type argument, through <c>new()</c>.</summary>
        public interface IMaker
        {
            T Make<T>()
                where T : new();
        }

        private static int Count(in ImmutableHashSet<int> set) => set.Count;

        /// <summary>What its type argument is, built through <c>new()</c>.</summary>
        public sealed class Wrapper<TInner>
            where TInner : new()
        {
            public TInner Inner { get; } = new();
        }

        /// <summary>
        /// What its type argument is, built through <c>new()</c> by its static initializer, which any use of a static
        /// field of an instantiation runs, a store alone included.
        /// </summary>
        public static class Holder<T>
            where T : class, new()
        {
            public static readonly T Value = new();

            public static object? Other;
        }
    }

    /// <summary>Hash collections the compiled reading lets be.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The shapes are written as code might write them, to be read compiled.")]
    private static class Seeded
    {
        public static HashSet<int> Constructor() => new(SeededComparer.Instance);

        public static HashSet<GenericTupleChild<string>> StringTupleChildren() => [];

        public static HashSet<SealedHandHashed> SealedHandHashedKeys() => [];

        public static HashSet<HandHashedOverCompiler> HandHashedOverCompilerKeys() => [];

        public static Dictionary<long, int> Sized(int capacity) => new(capacity, SeededComparer.Instance);

        public static HashSet<(long, int)> Copy(HashSet<(long, int)> source) => new(source, SeededComparer.Instance);

        public static HashSet<int> Lazy(ref HashSet<int>? lazy) => lazy ??= new(SeededComparer.Instance);

        public static HashSet<int> Operator(IEnumerable<int> numbers) => numbers.ToHashSet(SeededComparer.Instance);

        public static List<string> Names(IEnumerable<string> names) => [.. names.Distinct()];

        public static int OtherKeys() =>
            new HashSet<string>(StringComparer.Ordinal).Count + new HashSet<byte>().Count + new HashSet<bool>().Count
            + new HashSet<PdfLimit>().Count + new HashSet<PdfObjectId>().Count + new HashSet<(string, bool)>().Count;

        public static int Lookup(ImmutableDictionary<int, int> map) => map.GetValueOrDefault(1);

        public static int EmptyFrozen() => FrozenSet<int>.Empty.Count;

        public static int SortedByName() => System.Collections.Specialized.CollectionsUtil.CreateCaseInsensitiveSortedList().Count;

        public static int ByReference() =>
            new Dictionary<PdfObject, int>(ReferenceEqualityComparer.Instance).Count + new Dictionary<PdfObjectId, int>().Count
            + new HashSet<PdfName>().Count;

        public static int Made() => Unseeded.Make<HashSet<string>>().Count + Unseeded.Make<Unseeded.Wrapper<List<int>>>().Inner.Count;

        public static int Wrapped() => new Lazy<HashSet<int>>(new HashSet<int>(SeededComparer.Instance)).Value.Count;

        public static int TablesOfTables() =>
            new Dictionary<int, HashSet<int>>(SeededComparer.Instance).Count + new List<HashSet<int>>().Count + new Lazy<List<int>>().Value.Count;

        public static int Factories()
        {
            HashSet<int>? seen = null;
            var lazy = new Lazy<HashSet<int>>(() => new HashSet<int>(SeededComparer.Instance));
            return LazyInitializer.EnsureInitialized(ref seen, () => new HashSet<int>(SeededComparer.Instance)).Count + lazy.Value.Count;
        }

        public static int ActivatorLazyList() => Activator.CreateInstance<Lazy<List<int>>>().Value.Count;

        public static int LazyMetadataFactory() => new Lazy<HashSet<int>, int>(() => new HashSet<int>(SeededComparer.Instance), 0).Value.Count;

        public static int WeakGiven(System.Runtime.CompilerServices.ConditionalWeakTable<object, HashSet<int>> table, object key) =>
            table.GetValue(key, static _ => new HashSet<int>(SeededComparer.Instance)).Count;

        public static bool BoundAllowing(System.ComponentModel.BindingList<HashSet<int>> list) => list.AllowNew;

        public static void MarshalledInto(IntPtr pointer, Hashtable structure) => System.Runtime.InteropServices.Marshal.PtrToStructure(pointer, structure);

        public static string Serialized() => System.Text.Json.JsonSerializer.Serialize(new Dictionary<int, int>(SeededComparer.Instance));

        public static Task<HttpResponseMessage> Posted(HttpClient client) =>
            System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(client, new Uri("http://localhost/"), 1);
    }

    private readonly record struct Pair(int First, int Second);

    /// <summary>
    /// A key with no hash of its own, keeping .NET's default: packed tight with no reference, it hashes its bytes through the
    /// seed; holding a reference, a floating point number or padding, it would hash its first field alone.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1815:Override equals and operator equals on value types",
        Justification = "The sample is a key that keeps the hash a struct has by default.")]
    private readonly struct Unhashed(int value)
    {
        public int Value { get; } = value;
    }

    /// <summary>
    /// A key hiding .NET's default hash with a method of its own, which a table does not call: it is hashed by its first
    /// field alone, a reference.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1815:Override equals and operator equals on value types",
        Justification = "The sample is a key that keeps the hash a struct has by default.")]
    private readonly struct Hiding(string name, long offset)
    {
        public string Name { get; } = name;

        public long Offset { get; } = offset;

        public new int GetHashCode() => HashCode.Combine(Name, Offset);
    }

    /// <summary>A key deriving from a tuple of numbers, which hashes as the tuple does.</summary>
    private sealed class TupleChild(long first, int second) : Tuple<long, int>(first, second);

    /// <summary>
    /// A key deriving from a tuple whose only number is its first item, which hashes as the tuple does.
    /// </summary>
    private sealed class LongFirst(long first, string second) : Tuple<long, string>(first, second);

    /// <summary>A key deriving from a tuple of its type argument, which hashes as the tuple does.</summary>
    private sealed class GenericTupleChild<T>(T first, T second) : Tuple<T, T>(first, second);

    /// <summary>A class deriving from a tuple of its type argument and a number, which other keys derive from.</summary>
    private class TupleChildBase<T>(T first, long second) : Tuple<T, long>(first, second);

    /// <summary>
    /// A key deriving from a tuple of a string and a number through a class of its own, which hashes as the tuple does,
    /// neither its base type nor its own type arguments the tuple it hashes as.
    /// </summary>
    private sealed class TupleGrandchild(string first, long second) : TupleChildBase<string>(first, second);

    /// <summary>A record hashed by hand that is not sealed: a record derived from it, hashed as the compiler writes, may stand behind it.</summary>
    private record HandHashedBase(string Name)
    {
        public override int GetHashCode() => Name.Length;
    }

    /// <summary>A record derived from one hashed by hand, whose hash the compiler writes over its offset.</summary>
    private sealed record HandHashedDerived(string Name, long Offset) : HandHashedBase(Name);

    /// <summary>A sealed record hashed by hand, which its own tests hold.</summary>
    private sealed record SealedHandHashed(string Name)
    {
        public override int GetHashCode() => Name.Length;
    }

    /// <summary>A record whose hash the compiler writes, which a record hashed by hand derives from.</summary>
    private record CompilerHashedBase(string Name);

    /// <summary>
    /// A sealed record hashed by hand, which its own tests hold, deriving from one whose hash the compiler writes: the
    /// attribute the compiler puts on that hash is not its own.
    /// </summary>
    private sealed record HandHashedOverCompiler(string Name, long Offset) : CompilerHashedBase(Name)
    {
        public override int GetHashCode() => Name.Length;
    }

    /// <summary>A key keeping .NET's default hash with padding after its first field, which alone it is hashed by.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1815:Override equals and operator equals on value types",
        Justification = "The sample is a key that keeps the hash a struct has by default.")]
    private readonly struct Padded(int first, long second)
    {
        public int First { get; } = first;

        public long Second { get; } = second;
    }

    /// <summary>A key hashing a string, whose hash goes through the seed, through object's GetHashCode.</summary>
    private sealed class NamedKey(string name)
    {
        public override bool Equals(object? obj) => obj is NamedKey other && other.Name == Name;

        public override int GetHashCode() => Name.GetHashCode();

        private string Name { get; } = name;
    }

    /// <summary>
    /// A key hashed by its index alone, as the Arlington model's are, which calls nothing: a collection that enumerates
    /// by hash is refused over it all the same.
    /// </summary>
    private readonly struct IndexedKey(int index) : IEquatable<IndexedKey>
    {
        private readonly int _index = index;

        public bool Equals(IndexedKey other) => other._index == _index;

        public override bool Equals(object? obj) => obj is IndexedKey other && Equals(other);

        public override int GetHashCode() => _index;
    }

    private sealed record Numbered(int First, int Second);

    /// <summary>A stand-in for PdfName: its interning table, the one let be, and what the reading refuses beside it.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The shapes are written as code might write them, to be read compiled.")]
    private sealed class Interned
    {
        private static readonly ConcurrentDictionary<string, int> Counted = new(StringComparer.Ordinal);

        private static readonly ConcurrentDictionary<string, Interned>[] Shards = [new()];

        private static readonly ConcurrentDictionary<string, Interned> Table = new(StringComparer.Ordinal);

        private static readonly ConcurrentDictionary<string, Interned> Second = new(StringComparer.Ordinal);

        private static ConcurrentDictionary<string, Interned>? replaced;

        public static Interned Get(string value) => Table.GetOrAdd(value, static _ => new Interned());

        public static int Snapshot() => Table.ToArray().Length;

        public static int Valued() => Table.Values.Count;

        public static void CopiedTo()
        {
            ICollection<KeyValuePair<string, Interned>> pairs = Table;
            pairs.CopyTo(new KeyValuePair<string, Interned>[4], 0);
        }

        public static bool Find(string value) =>
            Table.TryGetValue(value, out _) || Table.ContainsKey(value) || Table.TryAdd(value, Table[value])
            || Second.ContainsKey(value) || Counted.ContainsKey(value) || Shards[0].ContainsKey(value) || replaced?.ContainsKey(value) is true;

        public static bool Looked(string value)
        {
            IReadOnlyDictionary<string, Interned> view = Table;
            return view.ContainsKey(value) && view.TryGetValue(value, out _) && view[value] is not null;
        }

        public static void Replace() => replaced = new(StringComparer.Ordinal);

        public static int Viewed()
        {
            IReadOnlyDictionary<string, Interned> view = Table;
            return view.Keys.Count();
        }

        public static Dictionary<string, Interned> Copied() => new(Table);

        public static IReadOnlyDictionary<string, Interned> View() => Table;

        public static bool Handed() => View() is not null;

        public static int Pairs(List<KeyValuePair<string, Interned>> pairs) => pairs.Count;

        public static string Joined() => JoinOf<KeyValuePair<string, Interned>>();

        public static IEnumerator Deferred()
        {
            IEnumerable<KeyValuePair<string, Interned>> pairs = Table;
            Func<IEnumerator> enumerate = pairs.GetEnumerator;
            return enumerate();
        }

        public static void Give(out IReadOnlyDictionary<string, Interned> view) => view = Table;

        public static bool Given()
        {
            Give(out var view);
            return view is not null;
        }

        public static bool TryGetValue(out ConcurrentDictionary<string, Interned> table)
        {
            table = Table;
            return true;
        }

        public static bool Fetched() => TryGetValue(out _);

        public static int CountViews(IReadOnlyDictionary<string, Interned>[] views) => views.Length;

        public static int NoPairs() => ImmutableList<KeyValuePair<string, Interned>>.Empty.Count;

        public static int NoPairsAt() => CountPairs(in ImmutableList<KeyValuePair<string, Interned>>.Empty);

        public static Func<object> ViewGroup() => View;

        public static int Nested(List<List<KeyValuePair<string, Interned>>> pairs) => pairs.Count;

        public static int Jagged(IReadOnlyDictionary<string, Interned>[][] views) => views.Length;

        public static int Jaggeds() => Jagged(new IReadOnlyDictionary<string, Interned>[1][]);

        public static int Typed() => CountOf<List<KeyValuePair<string, Interned>>>();

        public static bool Postponed() => Later() is not null;

        public static KeyValuePair<string, Interned> Paired(string name) => new(name, Get(name));

        public static ConcurrentDictionary<string, Interned> FromArray()
        {
            IList<ConcurrentDictionary<string, Interned>> list = Shards;
            return list[0];
        }

        public static int Arrayed() => CountViews(new IReadOnlyDictionary<string, Interned>[1]);

        private static string JoinOf<T>() => string.Join(",", (IEnumerable<T>)Table);

        private static int CountPairs(in ImmutableList<KeyValuePair<string, Interned>> pairs) => pairs.Count;

        private static int CountOf<T>() => typeof(T).Name.Length;

        private static Lazy<IReadOnlyDictionary<string, Interned>>? Later() => null;

        public static ConcurrentDictionary<string, Interned> Built() => new(StringComparer.Ordinal);

        public static ImmutableHashSet<string> Strings(IEnumerable<string> names) => names.ToImmutableHashSet(StringComparer.Ordinal);

        public static int Keys() => Table.Keys.Count;

        public static List<KeyValuePair<string, Interned>> Listed() => Table.ToList();

        public static IEnumerator<KeyValuePair<string, Interned>> Enumerated()
        {
            IEnumerable<KeyValuePair<string, Interned>> pairs = Table;
            return pairs.GetEnumerator();
        }
    }

    /// <summary>
    /// A stand-in whose static constructor builds its table at the head of a loop, which may run the construction again: the
    /// loop's branch lands on the construction itself.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1810:Initialize reference type static fields inline",
        Justification = "The sample is a static constructor that builds its table in a loop.")]
    private sealed class Looped
    {
        private static readonly ConcurrentDictionary<string, Looped>[] Shards = new ConcurrentDictionary<string, Looped>[2];

        private static readonly ConcurrentDictionary<string, Looped> Table;

        static Looped()
        {
            var i = 0;

            do
            {
                Table = new();
                Shards[i] = Table;
            }
            while (++i < Shards.Length);
        }

        public static Looped Get(string value) => Table.GetOrAdd(value, static _ => new Looped());
    }

    /// <summary>
    /// A stand-in whose static constructor builds its table into a field of an object a static field holds, not straight
    /// into a static field of its own.
    /// </summary>
    private sealed class Boxed
    {
        private static readonly Holder Box = new();

        static Boxed() => Box.Table = new(StringComparer.Ordinal);

        public static bool Has(string value) => Box.Table!.ContainsKey(value);

        private sealed class Holder
        {
            public ConcurrentDictionary<string, Boxed>? Table;
        }
    }

    /// <summary>
    /// A stand-in whose static constructor builds its table in a loop, whose branch lands ahead of the construction, on
    /// the comparer it is given.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1810:Initialize reference type static fields inline",
        Justification = "The sample is a static constructor that builds its table in a loop.")]
    private sealed class LoopedCompared
    {
        private static readonly ConcurrentDictionary<string, LoopedCompared>[] Shards = new ConcurrentDictionary<string, LoopedCompared>[2];

        private static readonly ConcurrentDictionary<string, LoopedCompared> Table;

        static LoopedCompared()
        {
            var i = 0;

            do
            {
                Table = new(StringComparer.Ordinal);
                Shards[i] = Table;
            }
            while (++i < Shards.Length);
        }

        public static LoopedCompared Get(string value) => Table.GetOrAdd(value, static _ => new LoopedCompared());
    }

    /// <summary>
    /// A stand-in whose static constructor builds its table in a loop long enough that its branch back takes the long form.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1810:Initialize reference type static fields inline",
        Justification = "The sample is a static constructor that builds its table in a loop.")]
    private sealed class LoopedLong
    {
        private static readonly ConcurrentDictionary<string, LoopedLong>[] Shards = new ConcurrentDictionary<string, LoopedLong>[2];

        private static readonly ConcurrentDictionary<string, LoopedLong> Table;

        private static readonly int Sum;

        static LoopedLong()
        {
            var i = 0;

            do
            {
                Table = new(StringComparer.Ordinal);
                Shards[i] = Table;
                Sum += i;
                Sum += i;
                Sum += i;
                Sum += i;
                Sum += i;
                Sum += i;
                Sum += i;
                Sum += i;
                Sum += i;
                Sum += i;
                Sum += i;
                Sum += i;
                Sum += i;
                Sum += i;
                Sum += i;
                Sum += i;
            }
            while (++i < Shards.Length);
        }

        public static int Total => Sum;

        public static LoopedLong Get(string value) => Table.GetOrAdd(value, static _ => new LoopedLong());
    }

    /// <summary>A stand-in whose static constructor builds its table in a loop that a switch closes.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1810:Initialize reference type static fields inline",
        Justification = "The sample is a static constructor that builds its table in a loop.")]
    private sealed class LoopedSwitch
    {
        private static readonly ConcurrentDictionary<string, LoopedSwitch>[] Shards = new ConcurrentDictionary<string, LoopedSwitch>[5];

        private static readonly ConcurrentDictionary<string, LoopedSwitch> Table;

        static LoopedSwitch()
        {
            var i = 0;

            for (; ; )
            {
                Table = new(StringComparer.Ordinal);
                Shards[i] = Table;

                switch (i++)
                {
                    case 0:
                        continue;
                    case 1:
                        continue;
                    case 2:
                        continue;
                    case 3:
                        continue;
                    default:
                        return;
                }
            }
        }

        public static LoopedSwitch Get(string value) => Table.GetOrAdd(value, static _ => new LoopedSwitch());
    }

    /// <summary>
    /// A stand-in whose static constructor builds its table at the head of a loop a switch closes, its first case leaving the
    /// loop: each of the switch's targets is read, and where it lands.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1810:Initialize reference type static fields inline",
        Justification = "The sample is a static constructor that builds its table in a loop.")]
    private sealed class LoopedSwitchBare
    {
        private static readonly ConcurrentDictionary<string, LoopedSwitchBare>[] Shards = new ConcurrentDictionary<string, LoopedSwitchBare>[5];

        private static readonly ConcurrentDictionary<string, LoopedSwitchBare> Table;

        static LoopedSwitchBare()
        {
            var i = 0;

            for (; ; )
            {
                Table = new();
                Shards[i] = Table;

                switch (i++)
                {
                    case 0:
                        return;
                    case 1:
                        continue;
                    case 2:
                        continue;
                    case 3:
                        continue;
                    default:
                        return;
                }
            }
        }

        public static LoopedSwitchBare Get(string value) => Table.GetOrAdd(value, static _ => new LoopedSwitchBare());
    }

    /// <summary>A stand-in whose instance constructor builds its table, again at each instance, into a static field.</summary>
    private sealed class Instanced
    {
        private static ConcurrentDictionary<string, Instanced>? table;

        private Instanced() => table = new(StringComparer.Ordinal);

        public static Instanced Make() => new();

        public static bool Has(string value) => table?.ContainsKey(value) is true;
    }

    /// <summary>Another type, whose static constructor builds the stand-in's table.</summary>
    private static class Interning
    {
        private static readonly ConcurrentDictionary<string, Interned> Table = new(StringComparer.Ordinal);

        public static bool Has(string value) => Table.ContainsKey(value);
    }

    /// <summary>What the reading of the compiled library refuses.</summary>
    private static class CompiledRules
    {
        private const BindingFlags Declared =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => code.Value);

        /// <summary>
        /// The instructions that build or call what they name, take a method group of it, virtual or not, or load or
        /// store a static field, or take its address. A use of a static field of a generic type runs its static
        /// initializer for the type arguments it names; a call, a construction or a method group of a member of one is
        /// read for what that member builds for them. A virtual method group is read for what reaches the interning
        /// table, and for a table a <c>new()</c> constraint builds for its type argument, as an interface's or a
        /// virtual generic method may, and for a non-generic hash table a method returns under its own type, as a
        /// virtual getter may; no hashing operator, generic factory or table constructor is virtual.
        /// </summary>
        private static readonly HashSet<OpCode> Considered =
            [OpCodes.Newobj, OpCodes.Call, OpCodes.Callvirt, OpCodes.Ldftn, OpCodes.Ldvirtftn, OpCodes.Ldsfld, OpCodes.Ldsflda, OpCodes.Stsfld];

        /// <summary>The tables a constructor builds, a base constructor included.</summary>
        private static readonly HashSet<Type> Tables =
        [
            typeof(Dictionary<,>), typeof(HashSet<>), typeof(ConcurrentDictionary<,>), typeof(OrderedDictionary<,>),
            typeof(KeyedCollection<,>),
        ];

        /// <summary>
        /// The non-generic hash tables, and any type derived from one: keyed by any object, a boxed number hashing as
        /// itself, which no comparer of a number can seed, or, as a StringDictionary is, by strings in a Hashtable,
        /// which enumerates by hash; a DictionaryBase keeps such a Hashtable, and an XmlSchemaCollection, obsolete, one
        /// keyed by strings. Their constructors, a base constructor included, and any method that returns one under its
        /// own type, as CollectionsUtil's factories and framework getters such as a TraceSource's Attributes do.
        /// </summary>
        private static readonly HashSet<Type> NonGeneric =
        [
            typeof(Hashtable), typeof(System.Collections.Specialized.HybridDictionary),
            typeof(System.Collections.Specialized.OrderedDictionary), typeof(System.Collections.Specialized.StringDictionary),
            typeof(System.Collections.DictionaryBase),
#pragma warning disable CS0618 // Obsolete: named to refuse it, since it keeps its schemas in a Hashtable it enumerates.
            typeof(System.Xml.Schema.XmlSchemaCollection),
#pragma warning restore CS0618
        ];

        /// <summary>The calls on the interning table that find or add one entry, and enumerate nothing.</summary>
        private static readonly HashSet<string> Lookups = ["GetOrAdd", "TryAdd", "TryGetValue", "ContainsKey", "get_Item"];

        /// <summary>The frozen and immutable hash collections, which their factories, and an immutable one's <c>Empty</c>, build.</summary>
        private static readonly HashSet<Type> HashCollections =
            [typeof(FrozenSet<>), typeof(FrozenDictionary<,>), typeof(ImmutableHashSet<>), typeof(ImmutableDictionary<,>)];

        /// <summary>The classes whose generic methods build a frozen or immutable hash collection, or its builder.</summary>
        private static readonly HashSet<Type> Factories =
            [typeof(FrozenSet), typeof(FrozenDictionary), typeof(ImmutableHashSet), typeof(ImmutableDictionary)];

        /// <summary>
        /// The LINQ operators that hash what they are given, or the keys a selector gives, synchronous or asynchronous. The
        /// deferred asynchronous ones take the comparer last, as the synchronous ones do; ToHashSetAsync, ToDictionaryAsync
        /// and ToLookupAsync take their cancellation token after it, so that one is refused even seeded, a shape the
        /// library does not use.
        /// </summary>
        private static readonly HashSet<string> HashingOperators =
        [
            "ToHashSet", "ToDictionary", "ToLookup", "Distinct", "DistinctBy", "GroupBy", "GroupJoin", "Join", "LeftJoin",
            "RightJoin", "Union", "UnionBy", "Intersect", "IntersectBy", "Except", "ExceptBy", "CountBy", "AggregateBy",
            "ToHashSetAsync", "ToDictionaryAsync", "ToLookupAsync",
        ];

        private static readonly HashSet<Type> Numbers =
        [
            typeof(short), typeof(ushort), typeof(char), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(nint),
            typeof(nuint), typeof(Int128), typeof(UInt128), typeof(System.Numerics.BigInteger), typeof(Half), typeof(float),
            typeof(double), typeof(decimal), typeof(System.Text.Rune), typeof(System.Runtime.InteropServices.NFloat),
            typeof(System.Numerics.Complex),
        ];

        /// <summary>
        /// Gets each hash collection over a steerable key a method of <paramref name="types"/> builds without the
        /// comparer of its key, taken last, being SeededComparer.Instance handed to it straight — the instruction
        /// before it the property's getter, and it no branch's target, so that no other path reaches it. A method group
        /// takes no comparer. A collection over a type parameter is refused whatever its comparer: its key is whatever
        /// its callers choose, and so is the comparer they hand it, which no reading follows. A table type given where
        /// <c>new()</c>, Lazy, LazyInitializer, a ConditionalWeakTable's GetOrCreateValue, a BindingList or
        /// Marshal.PtrToStructure builds it, or Activator, takes no comparer either, nor does a Lazy or a BindingList
        /// one of them builds, nor any type parameter, whatever its constraints, which the library never builds since
        /// what its callers give it is not all read, nor what a JSON deserializer builds, whatever its type argument,
        /// since its options or the type may choose a comparer out of the reading's sight, nor a table keyed by any
        /// object. A collection that enumerates in an order its keys' hashes set — a concurrent dictionary, a frozen or
        /// immutable hash collection, its builder or an immutable one's <c>Empty</c> — is refused whatever its key and
        /// its comparer: no key, comparer or hash can be read to say that a seed, which differs from one process to the
        /// next, orders nothing it gives. One is let be, the interning table of <paramref name="interning"/>, PdfName
        /// for the library: a concurrent dictionary from strings to it, the first its static constructor builds ahead
        /// of every branch target and stores straight into a static field, reached through its type or a generic
        /// interface of it only to find or add one entry; a call to, or a method group of, a member that takes it,
        /// gives it, views it or names its pairs, in an array, a reference or a type argument included, at any depth,
        /// or a static field declared on a type built over its pairs or a view of it, is refused. Through object, a
        /// non-generic interface or an expression tree, which names its members by token, it is not followed.
        /// </summary>
        public static IEnumerable<(MethodBase Method, MemberInfo Built)> TablesBuilt(IEnumerable<Type> types, Type? interning = null)
        {
            var seeded = typeof(SeededComparer).GetProperty(nameof(SeededComparer.Instance))!.GetMethod!;
            var table = interning is null ? null : typeof(ConcurrentDictionary<,>).MakeGenericType(typeof(string), interning);
            Type[] reaching = table is null
                ? []
                : [table, typeof(KeyValuePair<,>).MakeGenericType(typeof(string), interning!), .. table.GetInterfaces().Where(view => view.IsGenericType)];
            var interned = false;

            foreach (var type in types)
            {
                foreach (var method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
                {
                    if (method.GetMethodBody()?.GetILAsByteArray() is not { } il)
                    {
                        continue;
                    }

                    var (instructions, targets) = Read(method, il);

                    for (var i = 0; i < instructions.Count; i++)
                    {
                        var (offset, code, member) = instructions[i];

                        if (member is null || !Considered.Contains(code))
                        {
                            continue;
                        }

                        // The interning table is reached, through its type or a generic interface of it, only to find or add
                        // one entry: a call to, or a method group of, a member declared on, instantiated over, taking or
                        // giving the table, a generic interface of it or its pairs, in an array, a reference or a type
                        // argument included, or a static field declared on one of them or on a type built over one, as what
                        // enumerates it, views it, copies it or hands it out is, is refused, but the lookups declared on one
                        // of them and the table's constructors, which the rule below reads.
                        if (reaching.Length > 0 && Mentions(member, reaching) && !(member is ConstructorInfo && member.DeclaringType == table)
                            && !(member is MethodInfo && reaching.Contains(member.DeclaringType!) && Lookups.Contains(member.Name)))
                        {
                            yield return (method, member);
                            continue;
                        }

                        if (!Builds(member, out var key))
                        {
                            continue;
                        }

                        // Over a type parameter, the comparer of the key cannot be the seeded one handed straight; in a
                        // collection that enumerates by hash, no comparer is enough.
                        var handed = key is not null && member is MethodBase called && code != OpCodes.Ldftn
                            && !EnumeratesByHash(member) && TakesComparerLast(called, key) && i > 0 && !targets.Contains(offset)
                            && seeded.Equals(instructions[i - 1].Member);

                        // The interning table: the first its type's static constructor builds ahead of every branch target,
                        // which a loop would bring it back to, and stores straight into a static field; no other.
                        if (!interned && member.DeclaringType == table && method is ConstructorInfo { IsStatic: true }
                            && method.DeclaringType == interning && !targets.Any(target => target <= offset)
                            && i + 1 < instructions.Count && instructions[i + 1].Code == OpCodes.Stsfld)
                        {
                            interned = true;
                        }
                        else if (!handed)
                        {
                            yield return (method, member);
                        }
                    }
                }
            }
        }

        public static string Describe(MemberInfo built) =>
            built.DeclaringType is { IsGenericType: true } type
                ? $"{type.Name}<{string.Join(", ", type.GetGenericArguments().Select(argument => argument.Name))}>.{built.Name}"
                : $"{built.DeclaringType?.Name}.{built.Name}";

        /// <summary>
        /// Determines whether <paramref name="member"/> builds a hash collection over a key a file can steer or a type
        /// parameter stands for, or one that enumerates by hash whatever its key, and gets that key: null for what no
        /// comparer can seed — a non-generic hash table or a type derived from one, keyed by any object or, as a
        /// <see cref="System.Collections.Specialized.StringDictionary"/> is, by strings in a hash table that enumerates
        /// by hash, built by its constructor or returned by a method under its own type, as
        /// <see cref="System.Collections.Specialized.CollectionsUtil"/> and some framework getters do, or what
        /// <see cref="BuildsTypeArgument"/> finds built for a type argument. A generic table keyed by any object gets
        /// that key, which <see cref="Steerable"/> counts.
        /// </summary>
        private static bool Builds(MemberInfo member, out Type? key)
        {
            var type = member.DeclaringType!;
            key = null;

            if ((member is ConstructorInfo && IsNonGeneric(type))
                || (member is MethodInfo { ReturnType: var built } && IsNonGeneric(built))
                || BuildsTypeArgument(member))
            {
                return true;
            }

            key = member switch
            {
                ConstructorInfo when type.IsGenericType && Tables.Contains(type.GetGenericTypeDefinition()) => type.GetGenericArguments()[0],
                FieldInfo { IsStatic: true } when type.IsGenericType && HashCollections.Contains(type.GetGenericTypeDefinition()) => type.GetGenericArguments()[0],
                MethodInfo { IsGenericMethod: true } operation
                    when (Factories.Contains(type) && IsHashCollection(operation.ReturnType))
                        || (type.Namespace == "System.Linq" && HashingOperators.Contains(operation.Name)) => operation.GetGenericArguments()[KeyIndex(operation)],
                _ => null,
            };

            return key is not null && (key.ContainsGenericParameters || Steerable(key) || EnumeratesByHash(member));
        }

        /// <summary>
        /// Determines whether <paramref name="member"/> builds a table for a type argument it is given, where a
        /// <c>new()</c> constraint it is given a table type for lets it, or, for that type or any type parameter, as
        /// the library's own <c>new()</c>, which compiles to Activator, and the framework's builders do without a
        /// factory or a value — Activator, Lazy, LazyInitializer, a ConditionalWeakTable's GetOrCreateValue, a
        /// BindingList for AddNew and Marshal.PtrToStructure, those a sweep of .NET 10's shared framework found that
        /// may build a table, none handing it a comparer —; or whether it deserializes JSON into a type argument, which
        /// builds every table that type holds with .NET's default comparer unless its options or the type say
        /// otherwise, out of the reading's sight: the library reads no JSON.
        /// </summary>
        private static bool BuildsTypeArgument(MemberInfo member)
        {
            var type = member.DeclaringType!;

            if (member is MethodInfo { IsGenericMethod: true } generic)
            {
                var factoryless = !Array.Exists(generic.GetParameters(), parameter => parameter.ParameterType.Name.StartsWith("Func`", StringComparison.Ordinal));

                if ((type == typeof(Activator) || (type == typeof(LazyInitializer) && factoryless)
                        || (type == typeof(System.Runtime.InteropServices.Marshal) && generic.Name == nameof(System.Runtime.InteropServices.Marshal.PtrToStructure)
                            && generic.GetParameters().Length == 1))
                    && BuildsTable(generic.GetGenericArguments()[0]))
                {
                    return true;
                }

                // A JSON deserializer builds the type it is given and every table that type holds, at any depth, with
                // .NET's default comparer unless its options or the type say otherwise, which cannot be read where it
                // is called: refused whatever that type, since the library reads no JSON.
                if ((type.FullName == "System.Text.Json.JsonSerializer" && generic.Name.StartsWith("Deserialize", StringComparison.Ordinal))
                    || (type.FullName is "System.Net.Http.Json.HttpClientJsonExtensions" or "System.Net.Http.Json.HttpContentJsonExtensions"
                        && generic.Name.Contains("FromJson", StringComparison.Ordinal)))
                {
                    return true;
                }

                if (Constructed(generic.GetGenericMethodDefinition().GetGenericArguments(), generic.GetGenericArguments()))
                {
                    return true;
                }
            }

            if (!type.IsGenericType)
            {
                return false;
            }

            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();

            // Lazy builds its value without a factory, but for the value it is handed, built where it is; Lazy<T,
            // TMetadata> passes a constructor that takes only metadata on to Lazy<T>'s. The parameters are read on the
            // constructor's definition, so that metadata of the value's own type is not taken for the value.
            if (member is ConstructorInfo constructor && (definition == typeof(Lazy<>) || definition == typeof(Lazy<,>))
                && !Array.Exists(Definition(constructor).GetParameters(), parameter => parameter.ParameterType.Name.StartsWith("Func`", StringComparison.Ordinal)
                    || parameter.ParameterType == definition.GetGenericArguments()[0])
                && BuildsTable(arguments[0]))
            {
                return true;
            }

            // A ConditionalWeakTable builds the value of a key that has none for GetOrCreateValue; a BindingList builds
            // an item for AddNew, which its non-generic interface reaches out of sight, so it is refused where it is
            // built too.
            if ((definition == typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>)
                    && member.Name == nameof(System.Runtime.CompilerServices.ConditionalWeakTable<object, object>.GetOrCreateValue) && BuildsTable(arguments[1]))
                || (definition == typeof(System.ComponentModel.BindingList<>)
                    && member is ConstructorInfo or MethodInfo { Name: nameof(System.ComponentModel.BindingList<object>.AddNew) } && BuildsTable(arguments[0])))
            {
                return true;
            }

            return Constructed(definition.GetGenericArguments(), arguments);
        }

        /// <summary>
        /// Determines whether what a builder builds for <paramref name="type"/> may be a table: it builds one, or it is
        /// a type parameter, whatever its constraints. The library builds no type it is given in generic code, which
        /// takes a factory instead: what its callers give a type parameter, through a dispatch, a constrained call or a
        /// default implementation as well as a call, is not all read.
        /// </summary>
        private static bool BuildsTable(Type type) => CreatesTable(type) || type.IsGenericParameter;

        /// <summary>
        /// Gets the constructor of its type's generic definition that <paramref name="constructor"/> instantiates.
        /// </summary>
        private static ConstructorInfo Definition(ConstructorInfo constructor) =>
            Array.Find(
                constructor.DeclaringType!.GetGenericTypeDefinition().GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                candidate => candidate.MetadataToken == constructor.MetadataToken)!;

        /// <summary>Determines whether a type parameter that <c>new()</c> lets build its argument is given one that builds a table.</summary>
        private static bool Constructed(Type[] parameters, Type[] arguments)
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].GenericParameterAttributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint)
                    && CreatesTable(arguments[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether building <paramref name="type"/> builds a hash table over a steerable key or a type
        /// parameter, a non-generic hash table or a type derived from one, or a concurrent dictionary whatever its key:
        /// it is one, a type whose <c>new()</c> constraint builds one for it, or a Lazy or a BindingList over what may
        /// be one, which another builder builds through its parameterless constructor, the Lazy building its value
        /// without a factory and the BindingList an item for AddNew.
        /// </summary>
        private static bool CreatesTable(Type type) =>
            IsNonGeneric(type)
            || (type.IsGenericType && Tables.Contains(type.GetGenericTypeDefinition()) && type.GetGenericArguments()[0] is var key
                && (key.ContainsGenericParameters || Steerable(key) || type.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>)))
            || (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Lazy<>) || type.GetGenericTypeDefinition() == typeof(System.ComponentModel.BindingList<>))
                && BuildsTable(type.GetGenericArguments()[0]))
            || (type.IsGenericType && Constructed(type.GetGenericTypeDefinition().GetGenericArguments(), type.GetGenericArguments()));

        /// <summary>
        /// Determines whether the hash collection <paramref name="member"/> builds enumerates in an order its keys' hashes
        /// set: a concurrent dictionary, or a frozen or immutable hash collection or its builder, which a factory or an
        /// immutable one's <c>Empty</c> builds. A dictionary, a set and an ordered dictionary enumerate in the order their
        /// adds and removes set.
        /// </summary>
        private static bool EnumeratesByHash(MemberInfo member) =>
            (member is MethodInfo && Factories.Contains(member.DeclaringType!))
            || (member.DeclaringType is { IsGenericType: true } type
                && (type.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>) || (member is FieldInfo && HashCollections.Contains(type.GetGenericTypeDefinition()))));

        /// <summary>Determines whether <paramref name="type"/> is a non-generic hash table, or derives from one.</summary>
        private static bool IsNonGeneric(Type type) => NonGeneric.Any(table => table.IsAssignableFrom(type));

        /// <summary>
        /// Determines whether <paramref name="member"/> is declared on, instantiated over, takes or gives one of
        /// <paramref name="types"/>, or an array, a reference or a generic type built over one: what reaches a table, its
        /// pairs or a view of it.
        /// </summary>
        private static bool Mentions(MemberInfo member, Type[] types) =>
            Holds(member.DeclaringType!, types)
            || (member is MethodInfo { IsGenericMethod: true } generic && Array.Exists(generic.GetGenericArguments(), argument => Holds(argument, types)))
            || (member is MethodBase called && Array.Exists(called.GetParameters(), parameter => Holds(parameter.ParameterType, types)))
            || (member is MethodInfo { ReturnType: var given } && Holds(given, types));

        /// <summary>
        /// Determines whether <paramref name="type"/> is one of <paramref name="types"/>, or an array, a reference or a generic
        /// type built over one.
        /// </summary>
        private static bool Holds(Type type, Type[] types) =>
            types.Contains(type) || (type.HasElementType && Holds(type.GetElementType()!, types))
            || (type.IsGenericType && Array.Exists(type.GetGenericArguments(), argument => Holds(argument, types)));

        /// <summary>Determines whether <paramref name="type"/> is a frozen or immutable hash collection, or its builder.</summary>
        private static bool IsHashCollection(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() is var definition
            && (HashCollections.Contains(definition) || (definition.DeclaringType is { } outer && HashCollections.Contains(outer)));

        /// <summary>Gets what <paramref name="type"/> compares, if it is an equality comparer.</summary>
        private static Type? ComparedBy(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEqualityComparer<>) ? type.GetGenericArguments()[0] : null;

        /// <summary>Gets where a hashing operator's or factory's key lies among its type arguments: the one for keys, or the first.</summary>
        private static int KeyIndex(MethodInfo operation) =>
            Math.Max(Array.FindIndex(operation.GetGenericMethodDefinition().GetGenericArguments(), argument => argument.Name == "TKey"), 0);

        private static bool Steerable(Type key)
        {
            if (Nullable.GetUnderlyingType(key) is { } value)
            {
                return Steerable(value);
            }

            // A number, or what a boxed one may be keyed as — any object, a value type, an interface —, which hashes as the
            // number does; any interface, since a boxed tuple, record or struct keeping the default hash may stand behind it.
            if (Numbers.Contains(key) || key == typeof(object) || key == typeof(ValueType) || key.IsInterface)
            {
                return true;
            }

            // A tuple, which hashes as its items do, so that items that hash alike, a long's folded halves, make two tuples
            // hash alike, seed or not; or an anonymous type, whose hash the compiler writes as a fixed combination of its
            // members' hashes.
            if (IsTupleOrAnonymous(key))
            {
                return Array.Exists(key.GetGenericArguments(), Steerable);
            }

            // A struct that keeps .NET's default hash, a pair among them, or hides it with new, which a table does not
            // call: one holding a reference, a floating point number, padding or a field with an equality of its own is
            // hashed by its first non-null field alone, so that keys alike there share a hash whatever the seed.
            var hash = HashOf(key);

            if (key.IsValueType && !key.IsEnum && hash.DeclaringType == typeof(ValueType))
            {
                return true;
            }

            // A type deriving from a tuple, which hashes as the tuple it derives from: steerable when that tuple holds
            // an item that is.
            if (hash.DeclaringType != key && hash.DeclaringType is { } inherited && IsTupleOrAnonymous(inherited))
            {
                return Array.Exists(inherited.GetGenericArguments(), Steerable);
            }

            // A record that is not sealed, whatever its own hash: a record derived from it, whose hash the compiler writes,
            // may stand behind it.
            if (!key.IsValueType && !key.IsSealed && key.GetMethod("<Clone>$") is not null)
            {
                return true;
            }

            // A record's hash, as the compiler writes it: a fixed combination of its members a file inverts.
            return !key.IsEnum && hash.DeclaringType == key && hash.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false);
        }

        /// <summary>
        /// Gets the hash a table calls for <paramref name="key"/>: the most derived override of object's GetHashCode, which a
        /// method hiding it with <c>new</c> is not.
        /// </summary>
        private static MethodInfo HashOf(Type key)
        {
            for (var type = key; type is not null; type = type.BaseType)
            {
                if (type.GetMethod(nameof(GetHashCode), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, Type.EmptyTypes) is { } method
                    && method.GetBaseDefinition().DeclaringType == typeof(object))
                {
                    return method;
                }
            }

            return typeof(object).GetMethod(nameof(GetHashCode))!;
        }

        /// <summary>
        /// Determines whether <paramref name="key"/> is a tuple or an anonymous type, hashed as the hashes of its
        /// members: an anonymous type by the name the compiler gives it, which no source can declare.
        /// </summary>
        private static bool IsTupleOrAnonymous(Type key) =>
            key.IsGenericType && ((key.GetGenericTypeDefinition() is { Namespace: "System" } tuple
                    && (tuple.Name.StartsWith("ValueTuple`", StringComparison.Ordinal) || tuple.Name.StartsWith("Tuple`", StringComparison.Ordinal)))
                || key.Name.StartsWith("<>f__AnonymousType", StringComparison.Ordinal));

        /// <summary>
        /// Determines whether <paramref name="method"/> takes the comparer of <paramref name="key"/> last: as a hashing
        /// operator declares it, the comparer of its key's type parameter, so that one of another type parameter given the
        /// same type is not taken for it.
        /// </summary>
        private static bool TakesComparerLast(MethodBase method, Type key)
        {
            if (method is MethodInfo { IsGenericMethod: true } operation && operation.DeclaringType!.Namespace == "System.Linq")
            {
                var definition = operation.GetGenericMethodDefinition();
                return definition.GetParameters() is [.., var declared]
                    && ComparedBy(declared.ParameterType) == definition.GetGenericArguments()[KeyIndex(operation)];
            }

            return method.GetParameters() is [.., var last] && ComparedBy(last.ParameterType) == key;
        }

        /// <summary>
        /// Reads <paramref name="il"/>'s instructions — where each starts, and the method it calls or builds or the field it
        /// loads, if any — and where its branches lead.
        /// </summary>
        private static (List<(int Offset, OpCode Code, MemberInfo? Member)> Instructions, HashSet<int> Targets) Read(MethodBase method, byte[] il)
        {
            var typeArguments = method.DeclaringType!.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
            var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
            var instructions = new List<(int Offset, OpCode Code, MemberInfo? Member)>();
            var targets = new HashSet<int>();
            var at = 0;

            while (at < il.Length)
            {
                var offset = at;
                var value = (short)il[at++];

                if (value == 0xFE)
                {
                    value = unchecked((short)(0xFE00 | il[at++]));
                }

                var code = OpCodesByValue[value];
                MemberInfo? member = null;

                switch (code.OperandType)
                {
                    case OperandType.InlineMethod:
                        member = method.Module.ResolveMethod(BitConverter.ToInt32(il, at), typeArguments, methodArguments);
                        at += 4;
                        break;
                    case OperandType.InlineField:
                        member = method.Module.ResolveField(BitConverter.ToInt32(il, at), typeArguments, methodArguments);
                        at += 4;
                        break;
                    case OperandType.ShortInlineBrTarget:
                        targets.Add(at + 1 + (sbyte)il[at]);
                        at += 1;
                        break;
                    case OperandType.InlineBrTarget:
                        targets.Add(at + 4 + BitConverter.ToInt32(il, at));
                        at += 4;
                        break;
                    case OperandType.InlineSwitch:
                        var cases = BitConverter.ToInt32(il, at);
                        var next = at + 4 + (4 * cases);

                        for (var k = 0; k < cases; k++)
                        {
                            targets.Add(next + BitConverter.ToInt32(il, at + 4 + (4 * k)));
                        }

                        at = next;
                        break;
                    case OperandType.InlineI8 or OperandType.InlineR:
                        at += 8;
                        break;
                    case OperandType.InlineVar:
                        at += 2;
                        break;
                    case OperandType.ShortInlineI or OperandType.ShortInlineVar:
                        at += 1;
                        break;
                    case OperandType.InlineNone:
                        break;
                    default:
                        at += 4;
                        break;
                }

                instructions.Add((offset, code, member));
            }

            return (WithoutProbes(instructions, targets), targets);
        }

        /// <summary>
        /// Gets <paramref name="instructions"/> without the probes code coverage puts at sequence points, as CI runs the
        /// tests under it — a load of its tracker's field, the probe's index added unless it is the first, a byte of one
        /// stored —, which nothing the source wrote does. A branch to a probe leads on to what follows it. Only that shape
        /// is a probe: any other instruction is read as it stands, so that a probe written otherwise refuses the table it
        /// stands before, rather than hiding what follows it.
        /// </summary>
        private static List<(int Offset, OpCode Code, MemberInfo? Member)> WithoutProbes(
            List<(int Offset, OpCode Code, MemberInfo? Member)> instructions, HashSet<int> targets)
        {
            var kept = new List<(int Offset, OpCode Code, MemberInfo? Member)>(instructions.Count);

            for (var i = 0; i < instructions.Count; i++)
            {
                var length = ProbeLength(instructions, i);

                if (length == 0)
                {
                    kept.Add(instructions[i]);
                    continue;
                }

                var reached = false;

                for (var k = i; k < i + length; k++)
                {
                    reached |= targets.Contains(instructions[k].Offset);
                }

                if (reached && i + length < instructions.Count)
                {
                    targets.Add(instructions[i + length].Offset);
                }

                i += length - 1;
            }

            return kept;
        }

        /// <summary>
        /// Gets how many instructions the probe starting at <paramref name="at"/> takes — a load of the tracker's field, an
        /// index and its addition unless it is the first, a one and its store as a byte —, or nothing if none starts there.
        /// </summary>
        private static int ProbeLength(List<(int Offset, OpCode Code, MemberInfo? Member)> instructions, int at)
        {
            if (instructions[at] is not { Code: var load, Member: FieldInfo { DeclaringType.Name: var tracker } }
                || load != OpCodes.Ldsfld || !tracker.StartsWith("StaticManagedTrackerTemplate", StringComparison.Ordinal))
            {
                return 0;
            }

            var next = at + 1;

            if (next + 1 < instructions.Count && instructions[next].Code.Name!.StartsWith("ldc.i4", StringComparison.Ordinal)
                && instructions[next + 1].Code == OpCodes.Add)
            {
                next += 2;
            }

            return next + 1 < instructions.Count && instructions[next].Code == OpCodes.Ldc_I4_1 && instructions[next + 1].Code == OpCodes.Stind_I1
                ? next + 2 - at
                : 0;
        }
    }

    /// <summary>What the reading of the sources refuses, by file and line.</summary>
    private static class SourceRules
    {
        /// <summary>A number a key may be, by the name the source gives its type.</summary>
        private const string Number =
            @"\b(?:short|ushort|char|int|uint|long|ulong|nint|nuint|float|double|decimal|Half|Int16|UInt16|Char|Int32|UInt32|Int64|UInt64|IntPtr|UIntPtr|Int128|UInt128|Single|Double|Decimal|BigInteger|Rune|NFloat|Complex)\b";

        private static readonly Regex Hashing = new(
            @"\.(?:ToHashSetAsync|ToDictionaryAsync|ToLookupAsync|ToHashSet|ToDictionary|ToLookup|Distinct|DistinctBy|GroupBy|GroupJoin|Join|LeftJoin|RightJoin|Union|UnionBy|Intersect|IntersectBy|Except|ExceptBy|CountBy|AggregateBy|ToFrozenDictionary|ToFrozenSet|ToImmutableDictionary|ToImmutableHashSet)\(",
            RegexOptions.CultureInvariant);

        public static IEnumerable<string> Violations(string path, string source)
        {
            var text = WithoutComments(source);
            var declared = new Dictionary<string, bool>(StringComparer.Ordinal);

            foreach (var (at, isNew, key, name, statement, byHash) in Tables(text))
            {
                if (!IsNumeric(key))
                {
                    continue;
                }

                var built = isNew || statement.Contains("= [", StringComparison.Ordinal) || Regex.IsMatch(statement, @"=\s*new\s*\(");

                // A table that enumerates by hash is refused seeded or not, as the compiled reading refuses it.
                if (built && (byHash || !statement.Contains("SeededComparer", StringComparison.Ordinal)))
                {
                    yield return $"{path}:{Line(text, at)}: {statement.Trim()}";
                }
                else if (!built && name is not null)
                {
                    declared[name] = declared.TryGetValue(name, out var was) ? was && byHash : byHash;
                }
            }

            foreach (var (name, byHash) in declared)
            {
                foreach (Match assigned in Regex.Matches(text, $@"(?<![\w.]){Regex.Escape(name)}\s*(?:\?\?)?=\s*(?:\[|new\s*\()"))
                {
                    // What the table is given runs to the bracket or parenthesis that closes the one it opens with.
                    var open = assigned.Index + assigned.Length - 1;
                    var end = text[open] == '[' ? Matching(text, open, '[', ']') : Matching(text, open, '(', ')');
                    var statement = text[assigned.Index..(end < 0 ? text.Length : end + 1)];

                    if (byHash || !statement.Contains("SeededComparer", StringComparison.Ordinal))
                    {
                        yield return $"{path}:{Line(text, assigned.Index)}: {statement.Trim()}";
                    }
                }
            }

            // An operator's key is not written where it is called: one that hashes names its comparer among its own
            // arguments, which the compiled reading holds to the seeded one where the key is a number, and refuses whatever
            // it is for a frozen or immutable operator, which enumerates by hash. Text that string.Join or Path.Join joins
            // is not hashed.
            foreach (Match call in Hashing.Matches(text))
            {
                if (call.Value == ".Join(" && Regex.IsMatch(
                    text.AsSpan(Math.Max(0, call.Index - 40), Math.Min(40, call.Index)).ToString(),
                    @"(?<![\w@.])(?:(?:global::)?System\.(?:IO\.)?)?(?:string|String|Path)\s*$"))
                {
                    continue;
                }

                var open = call.Index + call.Length - 1;
                var close = Matching(text, open, '(', ')');

                if (!text.AsSpan(open, (close < 0 ? text.Length : close + 1) - open).Contains("Comparer", StringComparison.Ordinal))
                {
                    yield return $"{path}:{Line(text, call.Index)}: {call.Value} hashes without a comparer";
                }
            }
        }

        /// <summary>
        /// Finds each Dictionary or HashSet type written out: where, whether a new builds it, its key, the name it declares,
        /// what it is given — a new's own arguments, or its initializer where it has none; a declaration's statement to the
        /// semicolon, or past a property's accessors to its initializer —, and whether it enumerates by hash, concurrent,
        /// frozen or immutable. An array type yields each table its initializer builds instead.
        /// </summary>
        private static IEnumerable<(int At, bool IsNew, string Key, string? Name, string Statement, bool ByHash)> Tables(string text)
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
                var key = TopLevel(arguments)[0].Trim();
                var byHash = !type.Value.StartsWith("Dictionary", StringComparison.Ordinal) && !type.Value.StartsWith("HashSet", StringComparison.Ordinal)
                    && !type.Value.StartsWith("OrderedDictionary", StringComparison.Ordinal);
                var before = text[..type.Index].TrimEnd();
                var isNew = before.EndsWith("new", StringComparison.Ordinal);
                var after = Skip(text, close + 1);

                if (after < text.Length && text[after] == '?' && Skip(text, after + 1) is var nullable && nullable < text.Length && text[nullable] == '[')
                {
                    after = nullable;
                }

                if (after < text.Length && text[after] == '[')
                {
                    // An array of tables builds none, but for the tables its initializer builds, each with its own
                    // arguments: those it builds target-typed are read here, those it names the type of where the type is
                    // written. A new array's initializer follows its ranks; a declared one's is the collection expression
                    // its name, or its property's accessors, are given.
                    var rank = after;
                    var ranks = 0;

                    while (rank < text.Length && text[rank] == '[' && Matching(text, rank, '[', ']') is var shut and >= 0)
                    {
                        ranks++;
                        rank = Skip(text, shut + 1);

                        if (rank < text.Length && text[rank] == '?')
                        {
                            rank = Skip(text, rank + 1);
                        }
                    }

                    var given = Regex.Match(text[rank..], isNew ? @"^\{" : @"^@?\w+\s*(?:\{[^{}]*\}\s*)?=\s*\[");

                    if (given.Success && rank + given.Length - 1 is var initializer
                        && Matching(text, initializer, text[initializer], text[initializer] == '{' ? '}' : ']') is var last and >= 0)
                    {
                        foreach (var (table, at) in TargetTyped(text, initializer + 1, last, ranks - 1))
                        {
                            yield return (at, true, key, null, table, byHash);
                        }
                    }

                    continue;
                }

                var end = StatementEnd(text, close + 1);

                // What a new hands the table is its own arguments, or its initializer where it has none: a comparer another
                // argument of the same statement takes is not this table's.
                var opening = isNew ? text.IndexOfAny(['(', '{'], close) : -1;
                var built = opening >= 0 && string.IsNullOrWhiteSpace(text[(close + 1)..opening])
                    ? Matching(text, opening, text[opening], text[opening] == '(' ? ')' : '}')
                    : -1;
                var statement = isNew
                    ? text[(before.Length - 3)..(built >= 0 ? built + 1 : end)]
                    : text[type.Index..end];
                var name = Regex.Match(text[(close + 1)..], @"^\??\s+(?<name>@?\w+)").Groups["name"];
                yield return (type.Index, isNew, key, name.Success ? name.Value : null, statement, byHash);
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

        /// <summary>
        /// Determines whether <paramref name="key"/> is a number, nullable or not, written alone or after its namespace, a
        /// tuple one of whose elements is, by the type each element gives rather than the name it may follow it with, or a
        /// pair whatever it holds, which keeps .NET's default hash: one holding a reference, a floating point number or
        /// padding is hashed by its first non-null field alone.
        /// </summary>
        private static bool IsNumeric(string key)
        {
            key = key.Trim().TrimEnd('?').TrimEnd();

            if (Regex.Match(key, @"^(?:global::)?(?:System\.)?Nullable<(?<inner>.*)>$") is { Success: true } nullable)
            {
                return IsNumeric(nullable.Groups["inner"].Value);
            }

            if (key.StartsWith('(') && key.EndsWith(')'))
            {
                return Array.Exists(TopLevel(key[1..^1]), element => IsNumeric(Regex.Replace(element.Trim(), @"\s+@?\w+$", string.Empty)));
            }

            if (Regex.IsMatch(key, @"^(?:global::)?(?:System\.)?(?:Collections\.Generic\.)?KeyValuePair<.*>$"))
            {
                return true;
            }

            return Regex.Match(key, @"^(?:global::)?(?:System\.)?(?:Value)?Tuple<(?<items>.*)>$") is { Success: true } tuple
                ? Array.Exists(TopLevel(tuple.Groups["items"].Value), IsNumeric)
                : Regex.IsMatch(key, $@"^(?:global::)?(?:System\.(?:Numerics\.|Text\.|Runtime\.InteropServices\.)?)?{Number}\??$");
        }

        /// <summary>
        /// Gets the tables an array's initializer between <paramref name="from"/> and <paramref name="to"/> builds
        /// target-typed, with where each starts: an element that is a collection expression, and a new( an element starts
        /// with or a conditional or coalescing operator gives, to the parenthesis that closes it, not one another holds. A
        /// row of a multidimensional array is read through, and so is a collection expression while
        /// <paramref name="rows"/>, the ranks of a jagged array left past its elements', remain.
        /// </summary>
        private static IEnumerable<(string Table, int At)> TargetTyped(string text, int from, int to, int rows)
        {
            foreach (var (element, at) in Elements(text, from, to))
            {
                if (element.StartsWith('{') || (rows > 0 && element.StartsWith('[')))
                {
                    var row = element.StartsWith('{')
                        ? TargetTyped(text, at + 1, Matching(text, at, '{', '}'), rows)
                        : TargetTyped(text, at + 1, Matching(text, at, '[', ']'), rows - 1);

                    foreach (var table in row)
                    {
                        yield return table;
                    }
                }
                else if (element.StartsWith('['))
                {
                    yield return (element, at);
                }
                else
                {
                    var past = at;

                    foreach (Match created in Regex.Matches(element, @"(?:^|\?\??|:)\s*\(*\s*(?<new>new\s*\()"))
                    {
                        var start = at + created.Groups["new"].Index;

                        if (start >= past)
                        {
                            var shut = Matching(text, start + created.Groups["new"].Length - 1, '(', ')');
                            past = shut < 0 ? to : shut + 1;
                            yield return (text[start..past], start);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gets the elements of an initializer between <paramref name="from"/> and <paramref name="to"/>, split at the commas
        /// no parenthesis, bracket or brace holds, trimmed, with where each starts. Angle brackets are not counted: in an
        /// initializer they are more often a comparison, a shift or a lambda's arrow than a type's arguments, and an element
        /// that names a type cut at its arguments' comma starts with no target-typed new.
        /// </summary>
        private static List<(string Element, int At)> Elements(string text, int from, int to)
        {
            var elements = new List<(string, int)>();
            var depth = 0;
            var start = from;

            for (var i = from; i <= to; i++)
            {
                if (i == to || (depth == 0 && text[i] == ','))
                {
                    var element = text[start..i];
                    var trimmed = element.TrimStart();
                    elements.Add((trimmed.TrimEnd(), start + (element.Length - trimmed.Length)));
                    start = i + 1;
                }
                else
                {
                    depth += text[i] is '(' or '[' or '{' ? 1 : text[i] is ')' or ']' or '}' ? -1 : 0;
                }
            }

            return elements;
        }

        /// <summary>Gets where the first character at or past <paramref name="at"/> that is not white space is.</summary>
        private static int Skip(string text, int at)
        {
            while (at < text.Length && char.IsWhiteSpace(text[at]))
            {
                at++;
            }

            return at;
        }

        /// <summary>Splits <paramref name="arguments"/> at the commas no bracket or parenthesis holds.</summary>
        private static string[] TopLevel(string arguments)
        {
            var parts = new List<string>();
            var depth = 0;
            var from = 0;

            for (var i = 0; i < arguments.Length; i++)
            {
                depth += arguments[i] is '<' or '(' ? 1 : arguments[i] is '>' or ')' ? -1 : 0;

                if (depth == 0 && arguments[i] == ',')
                {
                    parts.Add(arguments[from..i]);
                    from = i + 1;
                }
            }

            parts.Add(arguments[from..]);
            return [.. parts];
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

        /// <summary>
        /// Blanks <paramref name="source"/>'s comments and the text of its string and character literals, keeping its
        /// length and its lines: what they hold is not code, and a parenthesis or a semicolon in them ends nothing. The holes
        /// of an interpolated string are code, read as such.
        /// </summary>
        private static string WithoutComments(string source)
        {
            var text = source.ToCharArray();
            Code(source, text, 0, hole: false);
            return new string(text);
        }

        /// <summary>
        /// Reads code from <paramref name="at"/>, blanking its comments and literals, to the end, or to the brace that closes
        /// the interpolation hole it is the code of; gets where it stopped.
        /// </summary>
        private static int Code(string source, char[] text, int at, bool hole)
        {
            var depth = 0;
            var nesting = 0;

            while (at < source.Length)
            {
                var next = at + 1 < source.Length ? source[at + 1] : '\0';

                switch (source[at])
                {
                    case '(' or '[':
                        nesting++;
                        at++;
                        break;
                    case ')' or ']':
                        nesting--;
                        at++;
                        break;
                    case ':' when hole && depth == 0 && nesting <= 0 && next != ':' && (at == 0 || source[at - 1] != ':'):
                        // A hole's format clause is text, up to the brace that closes the hole.
                        var format = source.IndexOf('}', at) is var brace and >= 0 ? brace : source.Length;
                        return Blank(text, at + 1, format);
                    case '/' when next == '/':
                        at = Blank(text, at, source.IndexOf('\n', at) is var line and >= 0 ? line : source.Length);
                        break;
                    case '/' when next == '*':
                        at = Blank(text, at, source.IndexOf("*/", at + 2, StringComparison.Ordinal) is var close and >= 0 ? close + 2 : source.Length);
                        break;
                    case '"':
                        at = StringLiteral(source, text, at);
                        break;
                    case '\'':
                        at = CharacterLiteral(source, text, at);
                        break;
                    case '{' when hole:
                        depth++;
                        at++;
                        break;
                    case '}' when hole:
                        if (depth-- == 0)
                        {
                            return at;
                        }

                        at++;
                        break;
                    default:
                        at++;
                        break;
                }
            }

            return at;
        }

        /// <summary>
        /// Blanks the string literal whose first quote is at <paramref name="at"/>, but for its holes, and gets where it
        /// ends: a raw one at a run of as many quotes as opened it, a verbatim one at a quote not doubled, any other at a
        /// quote no backslash escapes, or at its line's end. A hole opens at a brace not doubled, or at a raw string's run
        /// of as many braces as its dollars.
        /// </summary>
        private static int StringLiteral(string source, char[] text, int at)
        {
            var dollars = 0;
            var verbatim = false;

            for (var before = at - 1; before >= 0 && source[before] is '$' or '@'; before--)
            {
                dollars += source[before] == '$' ? 1 : 0;
                verbatim |= source[before] == '@';
            }

            var quotes = Run(source, at, '"');
            var raw = !verbatim && quotes >= 3;

            if (!raw && !verbatim && quotes == 2)
            {
                return at + 2;
            }

            var i = at + (raw ? quotes : 1);

            while (i < source.Length)
            {
                var c = source[i];

                if (c == '"' && raw && Run(source, i, '"') >= quotes)
                {
                    return i + quotes;
                }

                if (c == '"' && !raw)
                {
                    if (!verbatim || Run(source, i, '"') == 1)
                    {
                        return i + 1;
                    }

                    i = Blank(text, i, i + 2);
                }
                else if (c == '\\' && !raw && !verbatim)
                {
                    i = Blank(text, i, Math.Min(i + 2, source.Length));
                }
                else if (c == '\n' && !raw && !verbatim)
                {
                    return i;
                }
                else if (c == '{' && dollars > 0)
                {
                    var braces = Run(source, i, '{');
                    var opens = raw ? braces >= dollars : braces % 2 == 1;
                    i = Blank(text, i, i + braces - (opens ? (raw ? dollars : 1) : 0));

                    if (opens)
                    {
                        var hole = i + (raw ? dollars : 1);
                        i = Code(source, text, hole, hole: true) + (raw ? dollars : 1);
                    }
                }
                else
                {
                    i = Blank(text, i, i + 1);
                }
            }

            return i;
        }

        /// <summary>
        /// Blanks the character literal whose apostrophe is at <paramref name="at"/> and gets where it ends; an apostrophe
        /// no other closes within the longest such literal, '\U0000FFFF', opens none.
        /// </summary>
        private static int CharacterLiteral(string source, char[] text, int at)
        {
            var i = at + (at + 1 < source.Length && source[at + 1] == '\\' ? 3 : 2);

            while (i < source.Length && i - at < 12 && source[i] is not '\'' and not '\n')
            {
                i++;
            }

            return i < source.Length && source[i] == '\'' ? Blank(text, at + 1, i) + 1 : at + 1;
        }

        private static int Blank(char[] text, int from, int to)
        {
            for (var i = from; i < Math.Min(to, text.Length); i++)
            {
                text[i] = text[i] == '\n' ? '\n' : ' ';
            }

            return to;
        }

        private static int Run(string source, int at, char repeated)
        {
            var end = at;

            while (end < source.Length && source[end] == repeated)
            {
                end++;
            }

            return end - at;
        }

        private static int Line(string text, int at) => text.AsSpan(0, at).Count('\n') + 1;
    }
}
