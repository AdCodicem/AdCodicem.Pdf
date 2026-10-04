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
/// fault lies only for the objects that have one.
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
        PushDictionary(_document.Trailer, owner: 0, isNode: false);

        while (_work.TryPop(out var item))
        {
            switch (item.Value)
            {
                case PdfReference reference:
                    Follow(reference, item);
                    break;

                case PdfStream stream:
                    PushDictionary(stream.Dictionary, item.Owner, item.TopLevel && _pages.Nodes.Contains(item.Owner));
                    break;

                case PdfDictionary dictionary:
                    PushDictionary(dictionary, item.Owner, item.TopLevel && _pages.Nodes.Contains(item.Owner));
                    break;

                case PdfArray array:
                    foreach (var element in array)
                    {
                        _work.Push(new Item(element, item.Owner, TopLevel: false, item.Uncounted));
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

        _work.Push(new Item(value, number, TopLevel: true, Uncounted: false));
    }

    private void CountMissing(Item item)
    {
        if (!item.Uncounted)
        {
            Tally(_missingReferences, item.Owner);
        }
    }

    private void PushDictionary(PdfDictionary dictionary, int owner, bool isNode)
    {
        foreach (var (key, value) in dictionary)
        {
            if (HasNullCharacter(key))
            {
                Tally(_nullCharacterNames, owner);
            }

            if (owner == 0 && IsSectionKey(key))
            {
                continue;
            }

            _work.Push(new Item(value, owner, TopLevel: false, Uncounted: IsUncounted(key, value, owner, isNode)));
        }
    }

    /// <summary>
    /// Determines whether a reference to nothing in <paramref name="value"/>, under <paramref name="key"/>, is another
    /// rule's: the kids a node of the page tree lists are page-tree.kid-invalid's, and the trailer's <c>/Root</c>
    /// file.root-invalid's. A <c>/Kids</c> that is itself a reference to nothing is this walk's.
    /// </summary>
    private static bool IsUncounted(PdfName key, PdfObject value, int owner, bool isNode) =>
        (isNode && key == PdfName.Kids && value is PdfArray) || (owner == 0 && key == PdfName.Root);

    private void Describe()
    {
        foreach (var (owner, count) in _missingReferences)
        {
            var value = ValueOf(owner);
            var first = new StringBuilder();
            TryFindMissing(value, owner, isNode: _pages.Nodes.Contains(owner), first, out var target);
            var holder = Holder(owner);

            MissingReferences.Add(new ProbeFinding(
                LocationOf(owner),
                count == 1
                    ? Invariant($"{holder} refers to object {target.Number} {target.Generation} under {Path(first)}, which the file lacks: the reference reads as null.")
                    : Invariant($"{holder} holds {count} references to objects the file lacks, which read as null; the first refers to object {target.Number} {target.Generation} under {Path(first)}.")));
        }

        foreach (var (owner, count) in _nullCharacterNames)
        {
            var first = new StringBuilder();
            var name = FindNullCharacter(ValueOf(owner), owner, first, out var isKey);
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
                count == 1
                    ? $"{holder} holds the name {FileQuote.Name(name!)}, {where}, and a name cannot contain a null character."
                    : Invariant($"{holder} holds {count} names with a null character, which a name cannot contain; the first is {FileQuote.Name(name!)}, {where}.")));
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
    /// Finds, in file order, the first reference inside <paramref name="value"/> to an object the file lacks, and
    /// writes where it lies into <paramref name="path"/>; the references of other objects are not followed.
    /// </summary>
    /// <remarks>
    /// Whether one was found is said apart from what it names: a reference to object 0, which heads the free list and
    /// is never in use, is one to an object the file lacks like any other.
    /// </remarks>
    private bool TryFindMissing(PdfObject value, int owner, bool isNode, StringBuilder path, out PdfObjectId target)
    {
        switch (value)
        {
            case PdfReference reference when _missing.Contains(reference.Id.Number):
                target = reference.Id;
                return true;

            case PdfStream stream:
                return TryFindMissing(stream.Dictionary, owner, isNode, path, out target);

            case PdfDictionary dictionary:
                foreach (var (key, entry) in dictionary)
                {
                    if ((owner == 0 && IsSectionKey(key)) || IsUncounted(key, entry, owner, isNode))
                    {
                        continue;
                    }

                    var length = path.Length;
                    FileQuote.AppendName(path, key);

                    if (TryFindMissing(entry, owner, isNode: false, path, out target))
                    {
                        return true;
                    }

                    path.Length = length;
                }

                break;

            case PdfArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    var length = path.Length;
                    path.Append(CultureInfo.InvariantCulture, $"[{index}]");

                    if (TryFindMissing(array[index], owner, isNode: false, path, out target))
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
    private static PdfName? FindNullCharacter(PdfObject value, int owner, StringBuilder path, out bool isKey)
    {
        isKey = false;

        switch (value)
        {
            case PdfName name when HasNullCharacter(name):
                return name;

            case PdfStream stream:
                return FindNullCharacter(stream.Dictionary, owner, path, out isKey);

            case PdfDictionary dictionary:
                foreach (var (key, entry) in dictionary)
                {
                    if (HasNullCharacter(key))
                    {
                        isKey = true;
                        return key;
                    }

                    if (owner == 0 && IsSectionKey(key))
                    {
                        continue;
                    }

                    var length = path.Length;
                    FileQuote.AppendName(path, key);

                    if (FindNullCharacter(entry, owner, path, out isKey) is { } found)
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

                    if (FindNullCharacter(array[index], owner, path, out isKey) is { } found)
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

        // At the trailer the reader merged last, read by the chain or found by a rebuild; at the document when it merged
        // none (#126).
        var trailer = _reader.TrailerLocation;
        return trailer >= 0 ? PdfValidationLocation.AtPosition(trailer) : default;
    }

    private static string Holder(int owner) =>
        owner == 0 ? "The trailer" : string.Create(CultureInfo.InvariantCulture, $"Object {owner}");

    private static string Path(StringBuilder path) => path.Length == 0 ? "its value" : RuleText.Path(path);

    private static bool IsSectionKey(PdfName key) => key == PdfName.Prev || key == PdfName.XRefStm;

    private static bool HasNullCharacter(PdfName name) => name.Value.Contains('\0', StringComparison.Ordinal);

    private static void Tally(SortedDictionary<int, int> tally, int owner) =>
        tally[owner] = tally.TryGetValue(owner, out var count) ? count + 1 : 1;

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>A value still to visit, the object that holds it, and where in that object it lies.</summary>
    /// <param name="Value">The value.</param>
    /// <param name="Owner">The number of the indirect object that holds it, or 0 for the trailer.</param>
    /// <param name="TopLevel">Whether the value is the owner's own value, not one nested in it.</param>
    /// <param name="Uncounted">Whether a reference to nothing in the value is another rule's to report.</param>
    private readonly record struct Item(PdfObject Value, int Owner, bool TopLevel, bool Uncounted);
}
