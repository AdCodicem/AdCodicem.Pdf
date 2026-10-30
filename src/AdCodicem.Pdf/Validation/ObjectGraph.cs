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
/// Each object is walked whole when it is read, in the order it is written, and claims the objects its references name
/// that no reference named before: what the walk keeps of an object still to read is its number, with the object that
/// named it first and the role of that reference — 16 bytes —, never a value written in the object that named it (#168).
/// A chain of references so holds what the reader keeps, whatever the objects along it hold beside the reference and
/// wherever they write it. The objects claimed are read last claimed first, so that a tree of objects is read as a
/// depth-first walk of what it holds would read it, the last value first. Walking one object keeps a place for each level
/// of nesting it is in, which the parser bounds in an object read from a file; a value a caller built nests as deep as it
/// was built, and one that holds itself is walked without end (#310).
/// </para>
/// <para>
/// The walk runs on a settled index (<see cref="PdfFileReader.SettleIndex"/>), as validation makes it before its first
/// rule: the objects the file lacks are then the ones the index lacked when it was settled, so that a reference to one is
/// told when it is met, without a load — the object is asked for all the same, as resolving the reference asks for it —,
/// and counted at each place it is written. On an index a load could still rebuild, an object claimed when the index did not
/// lack it — held it, or a guard left it unknown — may be lacked once it is read: it is counted then for the reference that
/// claimed it, and for each one met after, not for those met in between.
/// </para>
/// <para>
/// It keeps what it found wrong, counted by the object that holds it, and works out where in that object the first
/// fault lies only for the objects that have one, once the walk is done, by reading each of them again: what is held
/// meanwhile is the reader's cache and the counts. Validation settles the index before its first rule, so that no read in
/// the walk rebuilds it, and the object read again is the one the walk read (#237); were it another, read through an index
/// rebuilt since, the finding would say what the walk counted without saying where.
/// </para>
/// <para>
/// It is made with the room the report has, and describes, of each kind of fault, the objects that hold one up to that
/// room, in the order of their numbers: those past it are counted, neither read again nor described, the report having no
/// room for their findings (#168). What it holds of them is a count per object.
/// </para>
/// </remarks>
internal sealed class ObjectGraph
{
    private readonly PdfDocument _document;
    private readonly PdfFileReader _reader;
    private readonly PageTreeWalk _pages;

    /// <summary>The objects claimed and not read yet, by number: never their values, nor those of the objects that named them.</summary>
    private readonly Stack<Pending> _work = new();
    private readonly HashSet<int> _visited = new(SeededComparer.Instance);
    private readonly HashSet<int> _missing = new(SeededComparer.Instance);
    private readonly SortedDictionary<int, int> _missingReferences = [];
    private readonly SortedDictionary<int, int> _nullCharacterNames = [];

    /// <summary>
    /// The containers of the value being walked, the first <see cref="_depth"/>, from the outermost to the one being read:
    /// one a level of nesting, each let go of when it is left, so that none outlives the walk of the object it is in.
    /// </summary>
    private Scope[] _scopes = new Scope[8];
    private int _depth;

    private ObjectGraph(PdfDocument document, PageTreeWalk pages, int room)
    {
        _document = document;
        _reader = document.Reader;
        _pages = pages;
        MissingReferences = new(room);
        NullCharacterNames = new(room);
        MissingEndObj = new(room);
    }

    /// <summary>Gets the objects, one finding each, that refer to objects the file lacks.</summary>
    public ProbeFindings MissingReferences { get; }

    /// <summary>Gets the objects, one finding each, that hold a name with a null character.</summary>
    public ProbeFindings NullCharacterNames { get; }

    /// <summary>Gets the objects whose value <c>endobj</c> does not follow.</summary>
    public ProbeFindings MissingEndObj { get; }

    /// <summary>
    /// Walks the objects of <paramref name="document"/> reachable from its trailer, describing of each kind of fault the
    /// first <paramref name="room"/> objects that hold one and counting the rest: the room the report has when the walk is
    /// made, or every object outside a validation.
    /// </summary>
    public static ObjectGraph Run(PdfDocument document, PageTreeWalk pages, int room = int.MaxValue)
    {
        var graph = new ObjectGraph(document, pages, room);
        graph.Walk();
        graph.Describe();
        return graph;
    }

    private void Walk()
    {
        Scan(_document.Trailer, owner: 0, Role.Trailer);

        while (_work.TryPop(out var pending))
        {
            var number = pending.Id.Number;
            var value = _reader.GetObject(pending.Id);

            // Null for an object the index holds in use is a literal null, or one the reader could not produce, which the
            // cross-reference rules report: there is nothing in it to walk. One the file lacks was told when it was met,
            // the index being settled; one lacked only now is lacked by an index a load rebuilt since it was claimed, which
            // a settled index never is, and is counted for the reference that claimed it.
            if (value is PdfNull && _reader.GetPresence(number) == ObjectPresence.Missing)
            {
                _missing.Add(number);
                CountMissing(pending.Role, pending.Owner);
                continue;
            }

            Scan(value, number, RoleOf(number));
        }
    }

    /// <summary>
    /// Walks <paramref name="value"/>, written in object <paramref name="owner"/> or, 0, in the trailer, whole and in the
    /// order it is written: the names and references it holds are met, and the containers it holds walked in their turn.
    /// </summary>
    private void Scan(PdfObject value, int owner, Role role)
    {
        Meet(value, owner, role);

        while (_depth > 0)
        {
            ref var scope = ref _scopes[_depth - 1];
            PdfObject next;
            Role nextRole;

            if (scope.Array is { } array)
            {
                if (scope.Next >= array.Count)
                {
                    Leave(ref scope);
                    continue;
                }

                next = array[scope.Next++];
                nextRole = scope.Role;
            }
            else if (scope.Entries.MoveNext())
            {
                var (key, entry) = scope.Entries.Current;

                if (HasNullCharacter(key))
                {
                    Tally(_nullCharacterNames, owner);
                }

                if (IsSectionKey(key, scope.Role))
                {
                    continue;
                }

                next = entry;
                nextRole = EntryRole(key, entry, scope.Role);
            }
            else
            {
                Leave(ref scope);
                continue;
            }

            // Last: meeting a container enters it, which may move the scopes.
            Meet(next, owner, nextRole);
        }
    }

    /// <summary>
    /// Meets <paramref name="value"/>, written in object <paramref name="owner"/> with <paramref name="role"/>: a reference
    /// is claimed, a container entered, and a name with a null character counted.
    /// </summary>
    private void Meet(PdfObject value, int owner, Role role)
    {
        switch (value)
        {
            case PdfReference reference:
                Claim(reference, owner, role);
                break;

            case PdfStream stream:
                Enter(new Scope { Entries = stream.Dictionary.GetEnumerator(), Role = role });
                break;

            case PdfDictionary dictionary:
                Enter(new Scope { Entries = dictionary.GetEnumerator(), Role = role });
                break;

            case PdfArray array:
                Enter(new Scope { Array = array, Role = ElementRole(role) });
                break;

            case PdfName name when HasNullCharacter(name):
                Tally(_nullCharacterNames, owner);
                break;
        }
    }

    private void Enter(Scope scope)
    {
        if (_depth == _scopes.Length)
        {
            Array.Resize(ref _scopes, _depth * 2);
        }

        _scopes[_depth++] = scope;
    }

    /// <summary>Leaves the innermost container, <paramref name="scope"/>, and lets go of it.</summary>
    private void Leave(ref Scope scope)
    {
        scope = default;
        _depth--;
    }

    /// <summary>
    /// Claims the object <paramref name="reference"/> names, written in object <paramref name="owner"/> with
    /// <paramref name="role"/>, for the walk to read later, unless a reference named it before; or counts the reference
    /// when it names an object the file lacks, each time it is met.
    /// </summary>
    private void Claim(PdfReference reference, int owner, Role role)
    {
        var number = reference.Id.Number;

        if (_missing.Contains(number))
        {
            CountMissing(role, owner);
            return;
        }

        if (!_visited.Add(number))
        {
            return;
        }

        // A settled index lacks an object as it did when it was settled, which a load does not change: the reference is
        // null, and the object is asked for all the same, as resolving the reference asks for it — without a load. On an
        // index a load could still rebuild, one that finds the object leaves it claimed, to be read.
        if (_reader.GetPresence(number) == ObjectPresence.Missing && _reader.GetObject(reference.Id) is PdfNull)
        {
            _missing.Add(number);
            CountMissing(role, owner);
            return;
        }

        _work.Push(new Pending(reference.Id, owner, role));
    }

    private void CountMissing(Role role, int owner)
    {
        if ((role & Role.Uncounted) == 0)
        {
            Tally(_missingReferences, owner);
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
            // Past the room the report had, an object is counted, neither read again nor described.
            if (!MissingReferences.Take())
            {
                continue;
            }

            var value = ValueOf(owner);
            var first = new StringBuilder();
            var found = TryFindMissing(value, owner, owner == 0 ? Role.Trailer : RoleOf(owner), first, out var target);
            var holder = Holder(owner);

            MissingReferences.Keep(new ProbeFinding(
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
            if (!NullCharacterNames.Take())
            {
                continue;
            }

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

            NullCharacterNames.Keep(new ProbeFinding(
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
            if (_reader.IsEndObjMissing(number, out var position) && MissingEndObj.Take())
            {
                var id = IdOf(number);
                MissingEndObj.Keep(new ProbeFinding(
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

    /// <summary>An object claimed and not read yet, and the reference that claimed it.</summary>
    /// <param name="Id">The object, as the reference names it.</param>
    /// <param name="Owner">The number of the indirect object the reference is written in, or 0 for the trailer.</param>
    /// <param name="Role">What the page tree walk and the other rules make of the reference.</param>
    private readonly record struct Pending(PdfObjectId Id, int Owner, Role Role);

    /// <summary>A container of the value being walked, and how far the walk has read it.</summary>
    private struct Scope
    {
        /// <summary>The array, or null for a dictionary or a stream's dictionary.</summary>
        public PdfArray? Array;

        /// <summary>The dictionary's entries, past the last one met.</summary>
        public Dictionary<PdfName, PdfObject>.Enumerator Entries;

        /// <summary>The index of the array's next element.</summary>
        public int Next;

        /// <summary>The dictionary's role, or the role of the array's elements.</summary>
        public Role Role;
    }

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
