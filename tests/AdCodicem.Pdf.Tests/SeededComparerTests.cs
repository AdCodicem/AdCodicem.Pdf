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
        // Every hash table keyed by a number or a tuple holding one, whose type the source writes, is built with
        // SeededComparer among its own arguments, or in its initializer where it has none, a copy included: built with [],
        // new() or no comparer, it would hash numbers a file chose as themselves (#155). A concurrent, frozen or immutable
        // one built with [] or new(), which enumerates by hash, is refused seeded or not; one an operator or a factory
        // builds is the compiled reading's. A name declared with such a type and no table, given [] or new() later, is held
        // too, so are the tables an array's initializer builds, and a LINQ operator that hashes
        // names a comparer among its own arguments, whatever its key. Comments and literals are not read. This reading
        // names the file and the line; what it cannot see — a table built where its type is not written, as an out var or
        // an expression body gives it, a key it cannot tell a number, the comparer an operator is given — the reading of
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
    public void No_method_of_the_library_builds_a_table_keyed_by_numbers_without_the_seeded_comparer()
    {
        // Read in the compiled library, whatever the source wrote — [], new(), an out var given a set, an expression body,
        // an argument, a copy, a base constructor, a LINQ operator, a method group —: every hash collection built over a key
        // a file can steer takes SeededComparer.Instance as the comparer of its key, taken last, handed to it straight and
        // on every path that reaches it. A key is steerable when it is a number — an integer wider than a byte, a
        // character, a floating point or decimal number —, nullable or not, a tuple or an anonymous type holding one, a
        // type of the library hashed as the compiler writes a record's hash, a fixed combination of its members a file
        // inverts, or what a boxed number may be keyed as, any object, a value type or an interface numbers implement,
        // which no comparer of a number seeds: an identity table is keyed by a reference type of the library with
        // ReferenceEqualityComparer.Instance, never by object. A collection that enumerates by hash — concurrent, frozen or
        // immutable, its factories, its builder and an immutable one's Empty included, however built — is refused whatever
        // its comparer, over a steerable key or a type of the library whose own hash calls anything, as PdfObjectId's
        // calls HashCode: the seed would order what it gives (invariant 6). Only an enumeration, or a type hashed by its
        // fields alone, as the Arlington model's are by their index, is let be there. An enumeration's values
        // are the library's own, and how the hash of a type the library hashes by hand spreads is held by its own tests, as
        // PdfObjectId's and PdfDiagnostic's are. The library builds no collection over a type parameter, whatever its
        // comparer: its callers choose its key, and no reading follows them; nor does it give a table type where new(),
        // Activator, Lazy or LazyInitializer builds it.
        var violations = new List<string>();

        foreach (var (method, built) in CompiledRules.TablesBuilt(typeof(SeededComparer).Assembly.GetTypes()))
        {
            violations.Add($"{method.DeclaringType!.FullName}.{method.Name}: {CompiledRules.Describe(built)}");
        }

        violations.Should().BeEmpty("a hash table keyed by numbers takes SeededComparer.Instance");
    }

    [Fact]
    public void The_reading_of_the_compiled_library_refuses_a_table_built_without_the_comparer()
    {
        // The check above sees what a source reading cannot: an out var given [], an expression body, a copy among others,
        // a comparer handed on one path only, a base constructor, a factory, a collection expression lowered to one, a
        // method group, a table over a type parameter whatever its comparer, a
        // table type a new() constraint, Lazy or LazyInitializer builds, and a collection that enumerates by hash, seeded or
        // not: the order the process's seed gives it would reach what the library reports or writes (invariant 6).
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
            nameof(Unseeded.EmptyIdentifiers),
            nameof(Unseeded.CreatedIdentifiers),
            nameof(Unseeded.LazyNames),
            nameof(Unseeded.AnonymousIdentifiers),
            nameof(Unseeded.UnhashedKeys),
            nameof(Unseeded.NamedKeys),
            nameof(Unseeded.ObjectByReference),
            nameof(Unseeded.Given),
            nameof(Unseeded.ByReference),
            nameof(Unseeded.ActivatorOpen),
            nameof(Unseeded.CreatedHashtable),
            nameof(Unseeded.LazyValue),
            nameof(Unseeded.LazyInitialized),
            nameof(Unseeded.NestedCreation),
            nameof(Unseeded.WrappedCreation),
            "IntDictionary",
            "NumberCollection",
            "Cache",
            "ForwardingDictionary");
    }

    [Fact]
    public void The_reading_of_the_compiled_library_lets_a_seeded_table_and_other_keys_be()
    {
        Refused(typeof(Seeded)).Should().BeEmpty();
    }

    [Fact]
    public void The_reading_of_the_compiled_library_reads_through_the_probes_code_coverage_puts()
    {
        // Code coverage, as CI runs the tests, puts a probe at each sequence point, which may fall between the comparer's
        // getter and the table's constructor: the probe is not code the source wrote, and the table stays seeded. Any other
        // instruction there is read as it stands, a tracker's field stored otherwise than a probe stores it included, so
        // that a probe another instrumenter writes otherwise refuses the table rather than hiding what follows it.
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
        var type = samples.CreateType();

        CompiledRules.TablesBuilt([type]).Select(found => found.Method.Name).Should().BeEquivalentTo("Wedged", "Misshapen");

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

        public static ImmutableHashSet<PdfObjectId> EmptyIdentifiers() => ImmutableHashSet<PdfObjectId>.Empty.Add(default);

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

        private sealed class IntDictionary : Dictionary<int, string>;

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

        /// <summary>What its type argument is, built through <c>new()</c>.</summary>
        public sealed class Wrapper<TInner>
            where TInner : new()
        {
            public TInner Inner { get; } = new();
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

        public static FrozenSet<PdfLimit> FrozenLimits(IEnumerable<PdfLimit> limits) => limits.ToFrozenSet();

        public static FrozenSet<IndexedKey> IndexedKeys(IEnumerable<IndexedKey> keys) => keys.ToFrozenSet();

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
    }

    private readonly record struct Pair(int First, int Second);

    /// <summary>A key with no hash of its own: a struct's, which goes through the seed.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1815:Override equals and operator equals on value types",
        Justification = "The sample is a key that keeps the hash a struct has by default.")]
    private readonly struct Unhashed(int value)
    {
        public int Value { get; } = value;
    }

    /// <summary>A key hashing a string, whose hash goes through the seed, through object's GetHashCode.</summary>
    private sealed class NamedKey(string name)
    {
        public override bool Equals(object? obj) => obj is NamedKey other && other.Name == Name;

        public override int GetHashCode() => Name.GetHashCode();

        private string Name { get; } = name;
    }

    /// <summary>A key hashed by its index alone, as the Arlington model's are, which calls nothing.</summary>
    private readonly struct IndexedKey(int index) : IEquatable<IndexedKey>
    {
        private readonly int _index = index;

        public bool Equals(IndexedKey other) => other._index == _index;

        public override bool Equals(object? obj) => obj is IndexedKey other && Equals(other);

        public override int GetHashCode() => _index;
    }

    private sealed record Numbered(int First, int Second);

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
        /// The instructions that build or call what they name, take a method group of it, or load a field. No hashing
        /// operator, factory or table constructor is virtual, so that none is taken as a virtual method group.
        /// </summary>
        private static readonly HashSet<OpCode> Considered =
            [OpCodes.Newobj, OpCodes.Call, OpCodes.Callvirt, OpCodes.Ldftn, OpCodes.Ldsfld];

        /// <summary>The tables a constructor builds, a base constructor included.</summary>
        private static readonly HashSet<Type> Tables =
        [
            typeof(Dictionary<,>), typeof(HashSet<>), typeof(ConcurrentDictionary<,>), typeof(OrderedDictionary<,>),
            typeof(KeyedCollection<,>),
        ];

        /// <summary>
        /// The tables keyed by any object, a boxed number hashing as itself, which no comparer of a number can seed: their
        /// constructors, a base constructor included, and the factories that build them.
        /// </summary>
        private static readonly HashSet<Type> Untyped =
            [typeof(Hashtable), typeof(System.Collections.Specialized.HybridDictionary), typeof(System.Collections.Specialized.OrderedDictionary)];

        /// <summary>The frozen and immutable hash collections, which their factories, and their <c>Empty</c>, build.</summary>
        private static readonly HashSet<Type> HashCollections =
            [typeof(FrozenSet<>), typeof(FrozenDictionary<,>), typeof(ImmutableHashSet<>), typeof(ImmutableDictionary<,>)];

        /// <summary>The classes whose generic methods build a frozen or immutable hash collection, or its builder.</summary>
        private static readonly HashSet<Type> Factories =
            [typeof(FrozenSet), typeof(FrozenDictionary), typeof(ImmutableHashSet), typeof(ImmutableDictionary)];

        /// <summary>The LINQ operators that hash what they are given, or the keys a selector gives.</summary>
        private static readonly HashSet<string> HashingOperators =
        [
            "ToHashSet", "ToDictionary", "ToLookup", "Distinct", "DistinctBy", "GroupBy", "GroupJoin", "Join", "LeftJoin",
            "RightJoin", "Union", "UnionBy", "Intersect", "IntersectBy", "Except", "ExceptBy", "CountBy", "AggregateBy",
        ];

        private static readonly HashSet<Type> Numbers =
        [
            typeof(short), typeof(ushort), typeof(char), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(nint),
            typeof(nuint), typeof(Int128), typeof(UInt128), typeof(System.Numerics.BigInteger), typeof(Half), typeof(float),
            typeof(double), typeof(decimal),
        ];

        /// <summary>
        /// Gets each hash collection over a steerable key a method of <paramref name="types"/> builds without the comparer of
        /// its key, taken last, being SeededComparer.Instance handed to it straight — the instruction before it the
        /// property's getter, and it no branch's target, so that no other path reaches it. A method group takes no comparer.
        /// A collection over a type parameter is refused whatever its comparer: its key is whatever its callers choose, and
        /// so is the comparer they hand it, which no reading follows. A table type given where <c>new()</c>, Lazy or
        /// LazyInitializer builds it, or Activator, takes no comparer either, nor does a table keyed by any object. A
        /// collection that enumerates in an order its keys' hashes set — a concurrent dictionary, a frozen or immutable hash
        /// collection, its builder or an immutable one's <c>Empty</c> — is refused whatever its comparer, over a steerable
        /// key or over a key the seed may order (<see cref="OrderedBySeed"/>), as PdfObjectId's: the seed, which differs
        /// from one process to the next, would order what it gives.
        /// </summary>
        public static IEnumerable<(MethodBase Method, MemberInfo Built)> TablesBuilt(IEnumerable<Type> types)
        {
            var seeded = typeof(SeededComparer).GetProperty(nameof(SeededComparer.Instance))!.GetMethod!;

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

                        if (member is null || !Considered.Contains(code) || !Builds(member, out var key))
                        {
                            continue;
                        }

                        // Over a type parameter, the comparer of the key cannot be the seeded one handed straight; in a
                        // collection that enumerates by hash, no comparer is enough.
                        var handed = key is not null && member is MethodBase called && code != OpCodes.Ldftn
                            && !EnumeratesByHash(member) && TakesComparerLast(called, key) && i > 0 && !targets.Contains(offset)
                            && seeded.Equals(instructions[i - 1].Member);

                        if (!handed)
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
        /// parameter stands for, or one that enumerates by hash over a key the seed may order, and gets that key:
        /// null for what no comparer can seed — a table keyed by any object, built by its constructor or by
        /// <see cref="System.Collections.Specialized.CollectionsUtil"/>, or a table built for a type argument by
        /// <c>new()</c>, Activator, Lazy or LazyInitializer.
        /// </summary>
        private static bool Builds(MemberInfo member, out Type? key)
        {
            var type = member.DeclaringType!;
            key = null;

            if ((member is ConstructorInfo && Untyped.Contains(type))
                || (type == typeof(System.Collections.Specialized.CollectionsUtil) && member is MethodInfo { ReturnType: var built } && Untyped.Contains(built))
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

            return key is not null && (key.ContainsGenericParameters || Steerable(key) || (EnumeratesByHash(member) && OrderedBySeed(key)));
        }

        /// <summary>
        /// Determines whether <paramref name="member"/> builds a table for a type argument it is given: where a
        /// <c>new()</c> constraint lets it, or as Activator, Lazy and LazyInitializer do without a factory.
        /// </summary>
        private static bool BuildsTypeArgument(MemberInfo member)
        {
            var type = member.DeclaringType!;

            if (member is MethodInfo { IsGenericMethod: true } generic)
            {
                var factoryless = !Array.Exists(generic.GetParameters(), parameter => parameter.ParameterType.Name.StartsWith("Func`", StringComparison.Ordinal));

                if ((type == typeof(Activator) || (type == typeof(LazyInitializer) && factoryless)) && CreatesTable(generic.GetGenericArguments()[0]))
                {
                    return true;
                }

                if (Constructed(generic.GetGenericMethodDefinition().GetGenericArguments(), generic.GetGenericArguments()))
                {
                    return true;
                }
            }

            // Lazy builds its value without a factory, but for the value it is handed, built where it is.
            if (member is ConstructorInfo constructor && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Lazy<>)
                && !Array.Exists(constructor.GetParameters(), parameter => parameter.ParameterType.Name.StartsWith("Func`", StringComparison.Ordinal)
                    || parameter.ParameterType == type.GetGenericArguments()[0])
                && CreatesTable(type.GetGenericArguments()[0]))
            {
                return true;
            }

            return type.IsGenericType && Constructed(type.GetGenericTypeDefinition().GetGenericArguments(), type.GetGenericArguments());
        }

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
        /// Determines whether building <paramref name="type"/> builds a hash table over a steerable key or a type parameter,
        /// a table keyed by any object, or a concurrent dictionary over a key the seed would order: it is one, or a type
        /// whose <c>new()</c> constraint builds one for it.
        /// </summary>
        private static bool CreatesTable(Type type) =>
            Untyped.Contains(type)
            || (type.IsGenericType && Tables.Contains(type.GetGenericTypeDefinition()) && type.GetGenericArguments()[0] is var key
                && (key.ContainsGenericParameters || Steerable(key)
                    || (type.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>) && OrderedBySeed(key))))
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

            // A number, or what a boxed one may be keyed as — any object, a value type, an interface numbers implement —,
            // which hashes as the number does.
            if (Numbers.Contains(key) || key == typeof(object) || key == typeof(ValueType)
                || (key.IsInterface && Numbers.Any(number => number.GetInterfaces().Contains(key))))
            {
                return true;
            }

            // A tuple, or an anonymous type, which the compiler hashes as a fixed combination of its members' hashes.
            if (IsTupleOrAnonymous(key))
            {
                return Array.Exists(key.GetGenericArguments(), Steerable);
            }

            // A record's hash, as the compiler writes it: a fixed combination of its members a file inverts.
            return !key.IsEnum && key.GetMethod(nameof(GetHashCode), Type.EmptyTypes) is { } hash
                && hash.DeclaringType == key && hash.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute));
        }

        /// <summary>
        /// Determines whether a collection that enumerates by hash would order <paramref name="key"/>, alone, nullable, in a
        /// tuple or an anonymous type, otherwise from one process to the next: any type outside the framework but an
        /// enumeration and a type whose own hash calls nothing — the Arlington model's, hashed by their index —, since a
        /// hash that calls anything may reach the seed, as PdfObjectId's does through HashCode, PdfName's through a
        /// string's hash and a struct's own through ValueType's. The framework's types other than numbers, strings among
        /// them, are not read: no table the library enumerates is keyed by them.
        /// </summary>
        private static bool OrderedBySeed(Type key)
        {
            if (Nullable.GetUnderlyingType(key) is { } value)
            {
                return OrderedBySeed(value);
            }

            if (IsTupleOrAnonymous(key))
            {
                return Array.Exists(key.GetGenericArguments(), OrderedBySeed);
            }

            if (key.IsEnum || key.IsGenericParameter
                || (key.Assembly.GetName().Name is { } assembly
                    && (assembly.StartsWith("System", StringComparison.Ordinal) || assembly.StartsWith("Microsoft", StringComparison.Ordinal))))
            {
                return false;
            }

            return key.GetMethod(nameof(GetHashCode), Type.EmptyTypes) is not { } hash || hash.DeclaringType != key
                || hash.GetMethodBody()?.GetILAsByteArray() is not { } il
                || Read(hash, il).Instructions.Exists(instruction => instruction.Member is MethodBase);
        }

        /// <summary>Determines whether <paramref name="key"/> is a tuple or an anonymous type, hashed as the hashes of its members.</summary>
        private static bool IsTupleOrAnonymous(Type key) =>
            key.IsGenericType && ((key.GetGenericTypeDefinition() is { Namespace: "System" } tuple
                    && (tuple.Name.StartsWith("ValueTuple`", StringComparison.Ordinal) || tuple.Name.StartsWith("Tuple`", StringComparison.Ordinal)))
                || (key.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)) && key.Name.Contains("AnonymousType", StringComparison.Ordinal)));

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
            @"\b(?:short|ushort|char|int|uint|long|ulong|nint|nuint|float|double|decimal|Half|Int16|UInt16|Char|Int32|UInt32|Int64|UInt64|IntPtr|UIntPtr|Int128|UInt128|Single|Double|Decimal|BigInteger)\b";

        private static readonly Regex Hashing = new(
            @"\.(?:ToHashSet|ToDictionary|ToLookup|Distinct|DistinctBy|GroupBy|GroupJoin|Join|LeftJoin|RightJoin|Union|UnionBy|Intersect|IntersectBy|Except|ExceptBy|CountBy|AggregateBy|ToFrozenDictionary|ToFrozenSet|ToImmutableDictionary|ToImmutableHashSet)\(",
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
        /// its statement to the semicolon, and whether it enumerates by hash, concurrent, frozen or immutable.
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
        /// Determines whether <paramref name="key"/> is a number, nullable or not, or a tuple one of whose elements is,
        /// by the type each element gives rather than the name it may follow it with.
        /// </summary>
        private static bool IsNumeric(string key)
        {
            key = key.Trim().TrimEnd('?').TrimEnd();

            if (key.StartsWith('(') && key.EndsWith(')'))
            {
                return Array.Exists(TopLevel(key[1..^1]), element => IsNumeric(Regex.Replace(element.Trim(), @"\s+@?\w+$", string.Empty)));
            }

            return Regex.Match(key, @"^(?:System\.)?Tuple<(?<items>.*)>$") is { Success: true } tuple
                ? Array.Exists(TopLevel(tuple.Groups["items"].Value), IsNumeric)
                : Regex.IsMatch(key, $@"^(?:System\.)?{Number}\??$");
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
