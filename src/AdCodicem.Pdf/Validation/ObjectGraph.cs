using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation;

/// <summary>
/// The objects reachable from the trailer, walked once: which of them refer to objects the file lacks, hold a name
/// with a null character, or do not end with <c>endobj</c>.
/// </summary>
/// <remarks>
/// <para>
/// The walk follows every reference of every dictionary, array and stream dictionary from the trailer — not the
/// trailer's <c>/Prev</c> or <c>/XRefStm</c>, which name sections rather than objects —, and resolves each object
/// once, by its number, through the document: memory follows the reader's cache and a set of object numbers, and no
/// stream's data is read. Cycles through <c>/Parent</c>, <c>/P</c> or a destination are legal and everywhere; the set
/// of numbers already met stops them.
/// </para>
/// <para>
/// It keeps what it found wrong, counted by the object that holds it, and works out where in that object the first
/// fault lies only for the objects that have one, once the walk is done, by reading each of them again: what is held
/// meanwhile is the reader's cache and the counts. Validation settles the index before its first rule, so that no read in
/// the walk rebuilds it, and the object read again is the one the walk read (#237); were it another, read through an index
/// rebuilt since, the finding would say what the walk counted without saying where.
/// </para>
/// </remarks>
internal sealed class ObjectGraph
{
    private readonly PdfDocument _document;
    private readonly PdfFileReader _reader;
    private readonly PageTreeWalk _pages;
    private readonly Stack<Item> _work = new();
    private readonly HashSet<int> _visited = [];
    private readonly HashSet<int> _missing = [];
    private readonly SortedDictionary<int, int> _missingReferences = [];
    private readonly SortedDictionary<int, int> _nullCharacterNames = [];

    private ObjectGraph(PdfDocument document, PageTreeWalk pages)
    {
        _document = document;
        _reader = document.Reader;
        _pages = pages;
    }

    /// <summary>Gets the objects, one finding each, that refer to objects the file lacks.</summary>
    public List<ProbeFinding> MissingReferences { get; } = [];

    /// <summary>Gets the objects, one finding each, that hold a name with a null character.</summary>
    public List<ProbeFinding> NullCharacterNames { get; } = [];

    /// <summary>Gets the objects whose value <c>endobj</c> does not follow.</summary>
    public List<ProbeFinding> MissingEndObj { get; } = [];

    /// <summary>Walks the objects of <paramref name="document"/> reachable from its trailer.</summary>
    public static ObjectGraph Run(PdfDocument document, PageTreeWalk pages)
    {
        var graph = new ObjectGraph(document, pages);
        graph.Walk();
        graph.Describe();
        return graph;
    }

    private void Walk()
    {
        PushDictionary(_document.Trailer, owner: 0, Role.Trailer);

        while (_work.TryPop(out var item))
        {
            switch (item.Value)
            {
                case PdfReference reference:
                    Follow(reference, item);
                    break;

                case PdfStream stream:
                    PushDictionary(stream.Dictionary, item.Owner, item.Role);
                    break;

                case PdfDictionary dictionary:
                    PushDictionary(dictionary, item.Owner, item.Role);
                    break;

                case PdfArray array:
                    var role = ElementRole(item.Role);

                    foreach (var element in array)
                    {
                        _work.Push(new Item(element, item.Owner, role));
                    }

                    break;

                case PdfName name when HasNullCharacter(name):
                    Tally(_nullCharacterNames, item.Owner);
                    break;
            }
        }
    }

    private void Follow(PdfReference reference, Item item)
    {
        var number = reference.Id.Number;

        if (_missing.Contains(number))
        {
            CountMissing(item);
            return;
        }

        if (!_visited.Add(number))
        {
            return;
        }

        var value = _reader.GetObject(reference.Id);

        if (value is PdfNull)
        {
            // Null for an object the index holds in use is a literal null, or one the reader could not produce, which
            // the cross-reference rules report; only an object the file lacks is this walk's.
            if (_reader.GetPresence(number) == ObjectPresence.Missing)
            {
                _missing.Add(number);
                CountMissing(item);
            }

            return;
        }

        _work.Push(new Item(value, number, RoleOf(number)));
    }

    private void CountMissing(Item item)
    {
        if ((item.Role & Role.Uncounted) == 0)
        {
            Tally(_missingReferences, item.Owner);
        }
    }

    private void PushDictionary(PdfDictionary dictionary, int owner, Role role)
    {
        foreach (var (key, value) in dictionary)
        {
            if (HasNullCharacter(key))
            {
                Tally(_nullCharacterNames, owner);
            }

            if (IsSectionKey(key, role))
            {
                continue;
            }

            _work.Push(new Item(value, owner, EntryRole(key, value, role)));
        }
    }

    /// <summary>
    /// Gives what the page tree walk made of object <paramref name="number"/>: an array of kids of its own whose kids it
    /// judged, a node whose kids it judged, or the catalog a root of the tree it judged is written in.
    /// </summary>
    private Role RoleOf(int number) =>
        (_pages.JudgedKidsArrays.Contains(number) ? Role.Kids : Role.None)
        | (_pages.JudgedKidsOf(number) ? Role.Node : Role.None)
        | (_pages.JudgedRootIn == number ? Role.Catalog : Role.None);

    /// <summary>
    /// Gives the role of the value under <paramref name="key"/> of a dictionary whose role is <paramref name="role"/>: the
    /// <c>/Kids</c> array written in a node the walk judged holds kids it judged, the <c>/Pages</c> written in the catalog
    /// a root it judged, and a reference to nothing under the trailer's <c>/Root</c> is file.root-invalid's.
    /// </summary>
    private Role EntryRole(PdfName key, PdfObject value, Role role) =>
        (role & Role.Node) != 0 && key == PdfName.Kids && value is PdfArray ? Role.Kids
        : (role & Role.Catalog) != 0 && key == PdfName.Pages && value is PdfDictionary or PdfStream ? Role.Node
        : (role & Role.Trailer) != 0 && key == PdfName.Root ? Role.Uncounted | (_pages.JudgedRootIn == 0 ? Role.Catalog : Role.None)
        : Role.None;

    /// <summary>
    /// Gives the role of an element of an array whose role is <paramref name="role"/>: in an array of kids the walk judged,
    /// a kid — a reference to nothing page-tree.kid-invalid's, a dictionary a node or a page the walk entered —; elsewhere,
    /// a value as any other, an array nested among kids included, which the walk judges as a kid and not what it holds.
    /// </summary>
    private static Role ElementRole(Role role) => (role & Role.Kids) != 0 ? Role.Uncounted | Role.Node : Role.None;

    private void Describe()
    {
        foreach (var (owner, count) in _missingReferences)
        {
            var value = ValueOf(owner);
            var first = new StringBuilder();
            var found = TryFindMissing(value, owner, owner == 0 ? Role.Trailer : RoleOf(owner), first, out var target);
            var holder = Holder(owner);

            MissingReferences.Add(new ProbeFinding(
                LocationOf(owner),
                (count, found) switch
                {
                    (1, true) => Invariant($"{holder} refers to object {target.Number} {target.Generation} under {Path(first)}, which the file lacks: the reference reads as null."),
                    (_, true) => Invariant($"{holder} holds {count} references to objects the file lacks, which read as null; the first refers to object {target.Number} {target.Generation} under {Path(first)}."),
                    (1, false) => $"{holder} refers to an object the file lacks: the reference reads as null.",
                    _ => Invariant($"{holder} holds {count} references to objects the file lacks, which read as null."),
                }));
        }

        foreach (var (owner, count) in _nullCharacterNames)
        {
            var first = new StringBuilder();
            var name = FindNullCharacter(ValueOf(owner), owner == 0, first, out var isKey);
            var holder = Holder(owner);
            var where = (isKey, first.Length) switch
            {
                (true, 0) => "as a key of its dictionary",
                (true, _) => $"as a key under {RuleText.Path(first)}",
                (false, 0) => "as its value",
                _ => $"under {RuleText.Path(first)}",
            };

            NullCharacterNames.Add(new ProbeFinding(
                LocationOf(owner),
                (count, name) switch
                {
                    (1, not null) => $"{holder} holds the name {FileQuote.Name(name)}, {where}, and a name cannot contain a null character.",
                    (_, not null) => Invariant($"{holder} holds {count} names with a null character, which a name cannot contain; the first is {FileQuote.Name(name)}, {where}."),
                    (1, null) => $"{holder} holds a name with a null character, which a name cannot contain.",
                    _ => Invariant($"{holder} holds {count} names with a null character, which a name cannot contain."),
                }));
        }

        var visited = new int[_visited.Count];
        _visited.CopyTo(visited);
        Array.Sort(visited);

        foreach (var number in visited)
        {
            if (_reader.IsEndObjMissing(number, out var position))
            {
                var id = IdOf(number);
                MissingEndObj.Add(new ProbeFinding(
                    _pages.Locate(id, position),
                    Invariant($"Object {number} does not end with endobj: what follows its value is something else, or the end of the file.")));
            }
        }
    }

    /// <summary>
    /// Finds, in file order, the first reference inside <paramref name="value"/> to an object the file lacks that the walk
    /// counted, and writes where it lies into <paramref name="path"/>; the references of other objects are not followed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What it counts is what the walk counts: each value takes its <paramref name="role"/> as the walk gives it, a
    /// reference another rule's passed over.
    /// </para>
    /// <para>
    /// Whether one was found is said apart from what it names: a reference to object 0, which heads the free list and
    /// is never in use, is one to an object the file lacks like any other.
    /// </para>
    /// </remarks>
    private bool TryFindMissing(PdfObject value, int owner, Role role, StringBuilder path, out PdfObjectId target)
    {
        switch (value)
        {
            case PdfReference reference when (role & Role.Uncounted) == 0 && _missing.Contains(reference.Id.Number):
                target = reference.Id;
                return true;

            case PdfStream stream:
                return TryFindMissing(stream.Dictionary, owner, role, path, out target);

            case PdfDictionary dictionary:
                foreach (var (key, entry) in dictionary)
                {
                    if (IsSectionKey(key, role))
                    {
                        continue;
                    }

                    var length = path.Length;
                    FileQuote.AppendName(path, key);

                    if (TryFindMissing(entry, owner, EntryRole(key, entry, role), path, out target))
                    {
                        return true;
                    }

                    path.Length = length;
                }

                break;

            case PdfArray array:
                var elements = ElementRole(role);

                for (var index = 0; index < array.Count; index++)
                {
                    var length = path.Length;
                    path.Append(CultureInfo.InvariantCulture, $"[{index}]");

                    if (TryFindMissing(array[index], owner, elements, path, out target))
                    {
                        return true;
                    }

                    path.Length = length;
                }

                break;
        }

        target = default;
        return false;
    }

    /// <summary>
    /// Finds, in file order, the first name with a null character inside <paramref name="value"/>, where it lies, and
    /// whether it is a key of the dictionary <paramref name="path"/> leads to.
    /// </summary>
    private static PdfName? FindNullCharacter(PdfObject value, bool trailer, StringBuilder path, out bool isKey)
    {
        isKey = false;

        switch (value)
        {
            case PdfName name when HasNullCharacter(name):
                return name;

            case PdfStream stream:
                return FindNullCharacter(stream.Dictionary, trailer, path, out isKey);

            case PdfDictionary dictionary:
                foreach (var (key, entry) in dictionary)
                {
                    if (HasNullCharacter(key))
                    {
                        isKey = true;
                        return key;
                    }

                    if (trailer && IsSectionKey(key))
                    {
                        continue;
                    }

                    var length = path.Length;
                    FileQuote.AppendName(path, key);

                    if (FindNullCharacter(entry, trailer: false, path, out isKey) is { } found)
                    {
                        return found;
                    }

                    path.Length = length;
                }

                return null;

            case PdfArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    var length = path.Length;
                    path.Append(CultureInfo.InvariantCulture, $"[{index}]");

                    if (FindNullCharacter(array[index], trailer: false, path, out isKey) is { } found)
                    {
                        return found;
                    }

                    path.Length = length;
                }

                return null;

            default:
                return null;
        }
    }

    private PdfObject ValueOf(int owner) =>
        owner == 0 ? _document.Trailer : _reader.GetObject(IdOf(owner));

    /// <summary>
    /// Names object <paramref name="number"/> with the generation the reader's index gives it: its row's, or, once a
    /// rebuild or a relocation has placed it, its header's (#118).
    /// </summary>
    private PdfObjectId IdOf(int number) =>
        new(number, _reader.Index.TryGet(number, out var entry) && entry.Kind == XRefEntryKind.Regular ? entry.Generation : 0);

    private PdfValidationLocation LocationOf(int owner)
    {
        if (owner > 0)
        {
            return _pages.Locate(IdOf(owner));
        }

        // At the newest trailer the reader merged, read by the chain or found by a rebuild (#126); at the document when it
        // merged none, which leaves on the trailer only a /Root the reader recovered, never located there, and what a
        // caller set on it.
        var trailer = _reader.TrailerLocation;
        return trailer >= 0 ? PdfValidationLocation.AtPosition(trailer) : default;
    }

    private static string Holder(int owner) =>
        owner == 0 ? "The trailer" : string.Create(CultureInfo.InvariantCulture, $"Object {owner}");

    private static string Path(StringBuilder path) => path.Length == 0 ? "its value" : RuleText.Path(path);

    private static bool IsSectionKey(PdfName key) => key == PdfName.Prev || key == PdfName.XRefStm;

    /// <summary>
    /// Determines whether <paramref name="key"/> of a dictionary whose role is <paramref name="role"/> names a section of
    /// the index rather than an object: the trailer's own <c>/Prev</c> or <c>/XRefStm</c>, not one written deeper in it.
    /// </summary>
    private static bool IsSectionKey(PdfName key, Role role) => (role & Role.Trailer) != 0 && IsSectionKey(key);

    private static bool HasNullCharacter(PdfName name) => name.Value.Contains('\0', StringComparison.Ordinal);

    private static void Tally(SortedDictionary<int, int> tally, int owner) =>
        tally[owner] = tally.TryGetValue(owner, out var count) ? count + 1 : 1;

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>A value still to visit, the object that holds it, and what the other rules make of it.</summary>
    /// <param name="Value">The value.</param>
    /// <param name="Owner">The number of the indirect object that holds it, or 0 for the trailer.</param>
    /// <param name="Role">What the page tree walk and the other rules make of the value.</param>
    private readonly record struct Item(PdfObject Value, int Owner, Role Role);

    /// <summary>
    /// What the page tree walk and the other rules make of a value, which decides whose a reference to nothing in it is:
    /// the kids the walk judged are page-tree.kid-invalid's, the trailer's <c>/Root</c> file.root-invalid's, and any other
    /// this walk's — a <c>/Kids</c> that is itself a reference to nothing included.
    /// </summary>
    [Flags]
    private enum Role
    {
        /// <summary>A value as any other.</summary>
        None = 0,

        /// <summary>A reference to nothing here is another rule's: a kid the walk judged, or the trailer's <c>/Root</c>.</summary>
        Uncounted = 1,

        /// <summary>An array whose elements are kids the walk judged: a node's <c>/Kids</c>, or an array of kids of its own.</summary>
        Kids = 2,

        /// <summary>A node whose kids the walk judged, read whole: the <c>/Kids</c> array written in it holds them.</summary>
        Node = 4,

        /// <summary>The catalog a root of the tree the walk judged is written in, under its <c>/Pages</c>.</summary>
        Catalog = 8,

        /// <summary>The trailer, whose <c>/Root</c> is file.root-invalid's, and may be a catalog written there.</summary>
        Trailer = 16,
    }
}
