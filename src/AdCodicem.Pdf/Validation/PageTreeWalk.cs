using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation;

/// <summary>
/// The document's page tree, walked once from the catalog's <c>/Pages</c>: how many pages it lists, where each page
/// lies, and what is wrong with its structure.
/// </summary>
/// <remarks>
/// <para>
/// Pages are counted as the tree lists them, the way qpdf walks it: a kid that is null, names an object the file
/// lacks, or is neither a page nor a node takes the place of a page, one with nothing on it; a node or a page listed
/// twice counts each time it is listed; a kid that loops back to a node above it counts for nothing. A node is a
/// dictionary with a <c>/Kids</c> array or of <c>/Type /Pages</c>; any other dictionary the tree lists is a page.
/// </para>
/// <para>
/// The walk is iterative and visits each indirect node once, so it keeps a frame per node on its current path and a
/// few bytes per object it met: nothing is sized by what the file declares. It resolves the tree's nodes and pages
/// through the document, and reads no content. What it cannot judge because the reader could not produce an object —
/// an object stream an encryption hides, an entry that leads nowhere — it leaves to the rules that report those, and
/// says the walk is partial.
/// </para>
/// <para>
/// A node or a page written inside another object rather than as an object of its own is named by where it is
/// written, as the Arlington walk and the object graph name such a value: by its path from that object, <c>the page
/// object under /Kids[1] of page tree node 2</c>, and located at that object.
/// </para>
/// <para>
/// M02 counts pages this way, and M06's page API is to materialize the pages it counts.
/// </para>
/// </remarks>
internal sealed class PageTreeWalk
{
    private readonly PdfFileReader _reader;
    private readonly List<Frame> _stack = [];
    private readonly HashSet<int> _visited = [];
    private readonly HashSet<int> _onPath = [];
    private readonly Dictionary<int, long> _pagesBelow = [];
    private long _pages;

    /// <summary>What a node is called in the findings.</summary>
    private const string NodeKind = "page tree node";

    /// <summary>What a page is called in the findings.</summary>
    private const string PageKind = "page object";

    private PageTreeWalk(PdfFileReader reader) => _reader = reader;

    /// <summary>Gets the kids that loop back to the node listing them or to a node above it.</summary>
    public List<ProbeFinding> Cycles { get; } = [];

    /// <summary>Gets the kids that name a node or a page the tree lists elsewhere.</summary>
    public List<ProbeFinding> RepeatedNodes { get; } = [];

    /// <summary>Gets the nodes that have no <c>/Kids</c> array.</summary>
    public List<ProbeFinding> NodesWithoutKids { get; } = [];

    /// <summary>Gets the kids that are neither a page nor a node the tree can read, or that are not referred to.</summary>
    public List<ProbeFinding> InvalidKids { get; } = [];

    /// <summary>Gets the nodes whose <c>/Count</c> is missing or is not the number of pages below them.</summary>
    public List<ProbeFinding> CountMismatches { get; } = [];

    /// <summary>Gets the nodes and pages whose <c>/Parent</c> is missing or is not the node listing them, and a root that has one.</summary>
    public List<ProbeFinding> ParentFaults { get; } = [];

    /// <summary>Gets the pages without a media box, and the media boxes that are no rectangle.</summary>
    public List<ProbeFinding> MediaBoxFaults { get; } = [];

    /// <summary>Gets the pages without resources, their own or inherited.</summary>
    public List<ProbeFinding> ResourcesMissing { get; } = [];

    /// <summary>Gets how many pages the tree lists, each null or missing kid counted as a page.</summary>
    public int PageCount => (int)Math.Min(_pages, int.MaxValue);

    /// <summary>
    /// Gets a value indicating whether part of the tree could not be read for the reader's own reasons: its root, or a
    /// kid, the index holds in use and the reader could not produce.
    /// </summary>
    public bool Partial { get; private set; }

    /// <summary>Gets the object numbers of the tree's nodes.</summary>
    public HashSet<int> Nodes { get; } = [];

    /// <summary>Gets the object numbers of the arrays of kids the walk read that are objects of their own.</summary>
    public HashSet<int> KidsArrays { get; } = [];

    /// <summary>
    /// Gets the object numbers of the nodes and pages whose <c>/Parent</c> the walk could not judge, for the node listing
    /// them was written in its own parent's <c>/Kids</c> and has no number to be named by. The walk judges the
    /// <c>/Parent</c> of every other node and page it entered, the root's included; the exceptions are kept rather than
    /// the rule, so that memory does not grow with the pages.
    /// </summary>
    public HashSet<int> ParentsUnjudged { get; } = [];

    /// <summary>Determines whether the walk judged the <c>/Parent</c> of object <paramref name="number"/>.</summary>
    public bool JudgedParent(int number) =>
        (Nodes.Contains(number) || PageIndexes.ContainsKey(number)) && !ParentsUnjudged.Contains(number);

    /// <summary>Gets the object number of each page of the tree, with the index it is first listed at.</summary>
    public Dictionary<int, int> PageIndexes { get; } = [];

    /// <summary>Walks the page tree of <paramref name="document"/>.</summary>
    public static PageTreeWalk Run(PdfDocument document)
    {
        var walk = new PageTreeWalk(document.Reader);

        if (document.Catalog is { } catalog && catalog.GetRaw(PdfName.Pages) is { } root)
        {
            // The catalog is an object of its own, or a dictionary written in the trailer, which file.root-invalid reports.
            var catalogPlace = document.Trailer.GetRaw(PdfName.Root) is PdfReference reference
                ? Place.Of(reference.Id)
                : new Place(default, default, "/Root");

            walk.Walk(root, catalogPlace);
        }

        return walk;
    }

    /// <summary>Gives the location of object <paramref name="id"/>, with its page when it is one of the tree's.</summary>
    public PdfValidationLocation Locate(PdfObjectId id, long? position = null) =>
        PageIndexes.TryGetValue(id.Number, out var page)
            ? PdfValidationLocation.OnPage(page, id, position)
            : PdfValidationLocation.OfObject(id, position);

    private void Walk(PdfObject rootValue, Place catalog)
    {
        var place = rootValue is PdfReference reference ? Place.Of(reference.Id) : catalog.Inside("/Pages");
        var id = place.Id;
        var root = rootValue.Resolve();

        if (root is PdfNull)
        {
            // A /Pages that names an object the file lacks is object.reference-missing's to report; one the index holds
            // and the reader could not produce is the index's, and one a guard left out of the index the guard's: either
            // leaves the tree unknown.
            Partial = id.Number > 0 && _reader.GetPresence(id.Number) is ObjectPresence.Unproduced or ObjectPresence.Unindexed;
            return;
        }

        var node = root switch
        {
            PdfDictionary dictionary => dictionary,
            PdfStream stream => stream.Dictionary,
            _ => null,
        };

        if (node is null)
        {
            return;
        }

        if (id.Number > 0)
        {
            _visited.Add(id.Number);
        }

        if (Given(node, PdfName.Parent) is not null)
        {
            ParentFaults.Add(new ProbeFinding(
                Location(place),
                $"The root of the page tree, {Where(place)}, has a /Parent, which only the nodes below it may have."));
        }

        if (!IsNode(node))
        {
            // A catalog whose /Pages is a page: a tree of one page, which the shape rules judge.
            EnterPage(node, place, parent: null);
            return;
        }

        EnterNode(node, place, parent: null);

        while (_stack.Count > 0)
        {
            var frame = _stack[^1];

            if (frame.Next >= frame.Kids.Count)
            {
                Leave(frame);
                continue;
            }

            var index = frame.Next++;
            Visit(frame, index, frame.Kids[index]);
        }
    }

    private void Visit(Frame frame, int index, PdfObject kid)
    {
        // A kid is told from one written in the array by being a reference, not by its number: "0 0 R" names object 0,
        // which heads the free list, and reads as null as any object the file lacks does.
        var reference = kid as PdfReference;
        var place = reference is not null ? Place.Of(reference.Id) : frame.KidPlace(index);
        var id = place.Id;

        if (reference is not null)
        {
            if (_onPath.Contains(id.Number))
            {
                frame.Uncountable = true;
                Cycles.Add(new ProbeFinding(
                    Location(frame.Place),
                    Invariant($"{Kid(frame, index)} names object {id.Number}, {(id.Number == frame.Id.Number ? "which is that node itself" : "a node above it")}: the tree loops back on itself there, and what it should have listed is unknown.")));
                return;
            }

            if (!_visited.Add(id.Number))
            {
                var counted = _pagesBelow.TryGetValue(id.Number, out var below) ? below : 1;
                _pages += counted;
                RepeatedNodes.Add(new ProbeFinding(
                    Location(frame.Place),
                    Invariant($"{Kid(frame, index)} names object {id.Number}, which the tree already lists: {(counted == 1 ? "its page is" : $"its {RuleText.Pages(counted)} are")} counted again.")));
                return;
            }
        }

        switch (kid.Resolve())
        {
            case PdfNull when reference is not null:
                switch (_reader.GetPresence(id.Number))
                {
                    case ObjectPresence.Unproduced or ObjectPresence.Unindexed:
                        // The entry's fault, which the cross-reference rules report, or a guard's, which its report names:
                        // the kid may have been a node, and the counts above it cannot be judged.
                        Partial = true;
                        frame.Uncountable = true;
                        _pages++;
                        return;

                    case ObjectPresence.Missing:
                        InvalidSlot(frame, index, Invariant($"names object {id.Number}, which the file lacks"));
                        return;

                    default:
                        InvalidSlot(frame, index, Invariant($"names object {id.Number}, which is null"));
                        return;
                }

            case PdfNull:
                InvalidSlot(frame, index, "is null");
                return;

            case PdfStream stream:
                InvalidKids.Add(new ProbeFinding(
                    Location(reference is not null ? place : frame.Place),
                    reference is not null
                        ? Invariant($"{Kid(frame, index)} names object {id.Number}, a stream rather than a dictionary: the reader reads the stream's dictionary in its place.")
                        : $"{Kid(frame, index)} is a stream written in the array, where the tree wants an indirect reference to a dictionary: the reader reads the stream's dictionary in its place."));
                Enter(stream.Dictionary, place, frame);
                return;

            case PdfDictionary dictionary:
                if (reference is null)
                {
                    InvalidKids.Add(new ProbeFinding(
                        Location(frame.Place),
                        $"{Kid(frame, index)} is a dictionary written in the array, where the tree wants an indirect reference to one."));
                }

                Enter(dictionary, place, frame);
                return;

            case var other:
                InvalidSlot(
                    frame,
                    index,
                    reference is not null
                        ? Invariant($"names object {id.Number}, which is {RuleText.Kind(other)}, neither a page nor a node")
                        : $"is {RuleText.Kind(other)}, neither a page nor a node");
                return;
        }
    }

    /// <summary>Records a kid that stands for a page the tree cannot read, and counts it as one with nothing on it.</summary>
    private void InvalidSlot(Frame frame, int index, string what)
    {
        var page = _pages++;

        InvalidKids.Add(new ProbeFinding(
            PageLocation(page, frame.Place),
            $"{Kid(frame, index)} {what}: it counts as a page with nothing on it."));
    }

    private void Enter(PdfDictionary dictionary, Place place, Frame parent)
    {
        if (IsNode(dictionary))
        {
            CheckParent(dictionary, place, parent, NodeKind);
            EnterNode(dictionary, place, parent);
        }
        else
        {
            EnterPage(dictionary, place, parent);
        }
    }

    private void EnterNode(PdfDictionary node, Place place, Frame? parent)
    {
        var id = place.Id;

        if (id.Number > 0)
        {
            Nodes.Add(id.Number);
        }

        var ownBox = Given(node, PdfName.MediaBox);

        if (ownBox is not null)
        {
            CheckMediaBox(ownBox, Location(place), place, NodeKind);
        }

        var kidsValue = Given(node, PdfName.Kids);

        if (kidsValue?.Resolve() is not PdfArray kids)
        {
            // A /Kids that names an object the file lacks is object.reference-missing's, and one the reader could not
            // produce the index's; either way, nothing below this node can be counted, nor the nodes above it judged.
            if (kidsValue is null || kidsValue.Resolve() is not PdfNull)
            {
                NodesWithoutKids.Add(new ProbeFinding(
                    Location(place),
                    kidsValue is null
                        ? $"{Subject(place, NodeKind)} has no /Kids: it lists no page."
                        : $"{Subject(place, NodeKind)} has {RuleText.Kind(kidsValue.Resolve())} for /Kids, not an array: it lists no page."));
            }

            if (id.Number > 0)
            {
                _pagesBelow[id.Number] = 0;
            }

            if (parent is not null)
            {
                parent.Uncountable = true;
            }

            // It lists no page, and its /Count is still required (ISO 32000-1, 7.7.3.2).
            if (Given(node, PdfName.Count) is null)
            {
                CountMissing(place, below: 0, countable: true);
            }

            return;
        }

        var kidsArray = default(PdfObjectId);

        if (kidsValue is PdfReference kidsReference)
        {
            KidsArrays.Add(kidsReference.Id.Number);
            kidsArray = kidsReference.Id;
        }

        _stack.Add(new Frame(node, place, kids, kidsArray)
        {
            PagesBefore = _pages,
            MediaBox = ownBox ?? parent?.MediaBox,
            HasResources = Given(node, PdfName.Resources) is not null || parent?.HasResources == true,
        });

        if (id.Number > 0)
        {
            _onPath.Add(id.Number);
        }
    }

    private void Leave(Frame frame)
    {
        _stack.RemoveAt(_stack.Count - 1);
        _onPath.Remove(frame.Id.Number);

        var below = _pages - frame.PagesBefore;

        if (frame.Id.Number > 0)
        {
            _pagesBelow[frame.Id.Number] = below;
        }

        if (frame.Uncountable && _stack.Count > 0)
        {
            _stack[^1].Uncountable = true;
        }

        switch (Given(frame.Node, PdfName.Count))
        {
            case null:
                // Missing whether or not the pages below can be counted.
                CountMissing(frame.Place, below, countable: !frame.Uncountable);
                break;

            case var count when !frame.Uncountable && count.Resolve() is PdfInteger { Value: var declared } && declared != below:
                CountMismatches.Add(new ProbeFinding(
                    Location(frame.Place),
                    Invariant($"{Subject(frame.Place, NodeKind)} gives /Count {declared}, and {RuleText.Pages(below)} {Lie(below)} below it.")));
                break;
        }
    }

    /// <summary>Records a node without <c>/Count</c>, saying how many pages lie below it when they can be counted.</summary>
    private void CountMissing(Place place, long below, bool countable) =>
        CountMismatches.Add(new ProbeFinding(
            Location(place),
            countable
                ? Invariant($"{Subject(place, NodeKind)} has no /Count; {RuleText.Pages(below)} {Lie(below)} below it.")
                : $"{Subject(place, NodeKind)} has no /Count, and how many pages lie below it is unknown."));

    private void EnterPage(PdfDictionary page, Place place, Frame? parent)
    {
        var id = place.Id;
        var index = _pages++;

        if (index <= int.MaxValue && id.Number > 0)
        {
            PageIndexes.TryAdd(id.Number, (int)index);
        }

        var location = PageLocation(index, place);

        if (parent is not null)
        {
            CheckParent(page, place, parent, PageKind);
        }

        var ownBox = Given(page, PdfName.MediaBox);

        if (ownBox is not null)
        {
            CheckMediaBox(ownBox, location, place, PageKind);
        }
        else if (parent?.MediaBox is null)
        {
            MediaBoxFaults.Add(new ProbeFinding(
                location,
                $"{Subject(place, PageKind)} has no /MediaBox, and no page tree node above it gives one: the page's size is unknown."));
        }

        if (Given(page, PdfName.Resources) is null && parent?.HasResources != true)
        {
            ResourcesMissing.Add(new ProbeFinding(
                location,
                $"{Subject(place, PageKind)} has no /Resources, and no page tree node above it gives any."));
        }
    }

    private void CheckParent(PdfDictionary dictionary, Place place, Frame parent, string what)
    {
        var id = place.Id;

        // A kid written in the array, or a node written in its own parent's, has no number to be named by.
        if (id.Number <= 0 || parent.Id.Number <= 0)
        {
            if (id.Number > 0)
            {
                ParentsUnjudged.Add(id.Number);
            }

            return;
        }

        var fault = dictionary.GetRaw(PdfName.Parent) switch
        {
            null => "has no /Parent",
            PdfReference named when named.Id.Number == parent.Id.Number => null,
            PdfReference named => Invariant($"gives /Parent {named.Id.Number} {named.Id.Generation} R"),
            var other => $"has {RuleText.Kind(other)} for /Parent",
        };

        if (fault is not null)
        {
            ParentFaults.Add(new ProbeFinding(
                Locate(id),
                Invariant($"{Subject(place, what)} {fault}, and page tree node {parent.Id.Number} lists it.")));
        }
    }

    /// <summary>
    /// Gives the value of <paramref name="key"/>, or null when the dictionary lacks it or gives it <c>null</c> — written
    /// so, or an object the file defines as <c>null</c> —, which in a dictionary is absence (ISO 32000-1, 7.3.7).
    /// </summary>
    /// <remarks>
    /// A reference to an object the file lacks, or that the reader could not produce, counts as given: the reference
    /// is <c>object.reference-missing</c>'s, the entry the cross-reference rules'.
    /// </remarks>
    private PdfObject? Given(PdfDictionary dictionary, PdfName key) =>
        dictionary.GetRaw(key) switch
        {
            null or PdfNull => null,
            PdfReference reference when reference.Resolve() is PdfNull && _reader.GetPresence(reference.Id.Number) == ObjectPresence.Defined => null,
            var value => value,
        };

    private void CheckMediaBox(PdfObject box, PdfValidationLocation location, Place place, string what)
    {
        // A /MediaBox that names an object the file lacks is object.reference-missing's, or the index's.
        var resolved = box.Resolve();

        if (resolved is PdfNull)
        {
            return;
        }

        if (resolved is not PdfArray { Count: 4 } array)
        {
            MediaBoxFaults.Add(new ProbeFinding(
                location,
                resolved is PdfArray other
                    ? Invariant($"{MediaBoxOf(place, what)} is an array of {other.Count} values, not a rectangle.")
                    : $"{MediaBoxOf(place, what)} is {RuleText.Kind(resolved)}, not a rectangle."));
            return;
        }

        Span<double> corners = stackalloc double[4];

        for (var index = 0; index < 4; index++)
        {
            switch (array.Resolved(index))
            {
                case PdfInteger integer:
                    corners[index] = integer.Value;
                    break;

                case PdfReal real when double.IsFinite(real.Value):
                    corners[index] = real.Value;
                    break;

                case var other:
                    MediaBoxFaults.Add(new ProbeFinding(
                        location,
                        $"{MediaBoxOf(place, what)} holds {RuleText.Kind(other)} among its corners, not a rectangle of four numbers."));
                    return;
            }
        }

        // Any two opposite corners make a rectangle (ISO 32000-1, 7.9.5), in whichever order they come.
        if (corners[0] == corners[2] || corners[1] == corners[3])
        {
            MediaBoxFaults.Add(new ProbeFinding(
                location,
                Invariant($"{MediaBoxOf(place, what)}, [{array.Resolved(0)} {array.Resolved(1)} {array.Resolved(2)} {array.Resolved(3)}], encloses no area.")));
        }
    }

    /// <summary>Gives the location of what lies at <paramref name="place"/>: the object it is, or the one it is written in.</summary>
    private PdfValidationLocation Location(Place place) =>
        place.Holder.Number > 0 ? Locate(place.Holder) : TrailerLocation();

    /// <summary>
    /// Gives the location of the page counted at <paramref name="index"/>, written at <paramref name="place"/>: on that page,
    /// at the page's object or the one it is written in.
    /// </summary>
    private PdfValidationLocation PageLocation(long index, Place place) =>
        (index <= int.MaxValue, place.Holder.Number > 0) switch
        {
            (true, true) => PdfValidationLocation.OnPage((int)index, place.Holder),
            (false, true) => PdfValidationLocation.OfObject(place.Holder),
            _ => TrailerLocation(),
        };

    /// <summary>
    /// Locates what is written in the trailer — a page tree under a catalog written there —, as the Arlington walk does:
    /// at the newest trailer the reader merged, or at the document.
    /// </summary>
    private PdfValidationLocation TrailerLocation() =>
        _reader.TrailerLocation is >= 0 and var trailer ? PdfValidationLocation.AtPosition(trailer) : default;

    /// <summary>
    /// Names what lies at <paramref name="place"/>, a <paramref name="what"/>, to open a sentence: <c>Page tree node 2</c>,
    /// <c>The page object under /Kids[1] of page tree node 2</c>.
    /// </summary>
    private string Subject(Place place, string what) => RuleText.Capitalize(Describe(place, what));

    /// <summary>
    /// Names what lies at <paramref name="place"/>, a <paramref name="what"/>: <c>page tree node 2</c> when it is an object
    /// of its own, else by its path from the object it is written in, <c>the page object under /Kids[1] of page tree node
    /// 2</c>.
    /// </summary>
    private string Describe(Place place, string what) =>
        place.Id.Number > 0 ? Invariant($"{what} {place.Id.Number}") : $"the {what} {Where(place)}";

    /// <summary>
    /// Says where what lies at <paramref name="place"/> is: <c>object 2</c>, or, for a value written inside an object, its
    /// path from that object, <c>under /Kids[1] of page tree node 2</c> — <c>at [1] in object 7</c> in an array that is an
    /// object of its own.
    /// </summary>
    private string Where(Place place)
    {
        if (place.Id.Number > 0)
        {
            return Invariant($"object {place.Id.Number}");
        }

        var holder = place.Holder.Number;
        var owner = holder <= 0
            ? "the trailer"
            : Nodes.Contains(holder) ? Invariant($"page tree node {holder}") : Invariant($"object {holder}");
        var path = RuleText.Path(new StringBuilder(place.Path));

        return path.StartsWith('[') ? $"at {path} in {owner}" : $"under {path} of {owner}";
    }

    private string MediaBoxOf(Place place, string what) =>
        place.Id.Number > 0 ? Invariant($"The /MediaBox of object {place.Id.Number}") : $"The /MediaBox of {Describe(place, what)}";

    private string Kid(Frame frame, int index) =>
        Invariant($"The kid at index {index} of {Describe(frame.Place, NodeKind)}");

    private static string Lie(long count) => count == 1 ? "lies" : "lie";

    private static bool IsNode(PdfDictionary dictionary) =>
        dictionary.GetRaw(PdfName.Kids)?.Resolve() is PdfArray || dictionary.GetName(PdfName.Type) == PdfName.Pages;

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Where a node or a page of the tree is written: an object of its own, whose <see cref="Id"/> and
    /// <see cref="Holder"/> it is; or a value written inside the object <see cref="Holder"/> names — the trailer when it
    /// names none —, at <see cref="Path"/> from it.
    /// </summary>
    private readonly record struct Place(PdfObjectId Id, PdfObjectId Holder, string? Path)
    {
        /// <summary>Gives the place of object <paramref name="id"/>.</summary>
        public static Place Of(PdfObjectId id) => new(id, id, null);

        /// <summary>Gives the place of a value written at <paramref name="step"/> inside what lies here.</summary>
        public Place Inside(string step) => new(default, Holder, Path + step);
    }

    /// <summary>A node of the tree the walk is below, and what the pages under it inherit.</summary>
    private sealed class Frame(PdfDictionary node, Place place, PdfArray kids, PdfObjectId kidsArray)
    {
        public PdfDictionary Node { get; } = node;

        /// <summary>Gets where the node is written.</summary>
        public Place Place { get; } = place;

        /// <summary>Gets the node's object number, or 0 when it is written inside another object.</summary>
        public PdfObjectId Id => Place.Id;

        public PdfArray Kids { get; } = kids;

        /// <summary>Gives the place of the kid written at <paramref name="index"/> of the node's <c>/Kids</c>.</summary>
        /// <remarks>
        /// In an array that is an object of its own, the kid is written in that object; otherwise in the node, under its
        /// <c>/Kids</c>.
        /// </remarks>
        public Place KidPlace(int index) =>
            kidsArray.Number > 0
                ? new Place(default, kidsArray, Invariant($"[{index}]"))
                : Place.Inside(Invariant($"/Kids[{index}]"));

        /// <summary>Gets or sets the index of the next kid to visit.</summary>
        public int Next { get; set; }

        /// <summary>Gets or sets how many pages the walk had counted when it entered the node.</summary>
        public long PagesBefore { get; init; }

        /// <summary>Gets the media box the node gives the pages below it, its own or inherited.</summary>
        public PdfObject? MediaBox { get; init; }

        /// <summary>Gets a value indicating whether the node gives the pages below it resources, its own or inherited.</summary>
        public bool HasResources { get; init; }

        /// <summary>Gets or sets a value indicating whether what lies below the node cannot be counted with confidence.</summary>
        public bool Uncountable { get; set; }
    }
}
