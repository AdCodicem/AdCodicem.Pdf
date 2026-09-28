using System.Globalization;
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

    /// <summary>Gets the object number of each page of the tree, with the index it is first listed at.</summary>
    public Dictionary<int, int> PageIndexes { get; } = [];

    /// <summary>Walks the page tree of <paramref name="document"/>.</summary>
    public static PageTreeWalk Run(PdfDocument document)
    {
        var walk = new PageTreeWalk(document.Reader);

        if (document.Catalog is { } catalog && catalog.GetRaw(PdfName.Pages) is { } root)
        {
            walk.Walk(root);
        }

        return walk;
    }

    /// <summary>Gives the location of object <paramref name="id"/>, with its page when it is one of the tree's.</summary>
    public PdfValidationLocation Locate(PdfObjectId id, long? position = null) =>
        PageIndexes.TryGetValue(id.Number, out var page)
            ? PdfValidationLocation.OnPage(page, id, position)
            : PdfValidationLocation.OfObject(id, position);

    private void Walk(PdfObject rootValue)
    {
        var id = rootValue is PdfReference reference ? reference.Id : default;
        var root = rootValue.Resolve();

        if (root is PdfNull)
        {
            // A /Pages that names an object the file lacks is object.reference-missing's to report; one the index holds
            // and the reader could not produce is the index's, and leaves the tree unknown.
            Partial = id.Number > 0 && _reader.GetPresence(id.Number) == ObjectPresence.Unproduced;
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

        if (node.GetRaw(PdfName.Parent) is not null)
        {
            ParentFaults.Add(new ProbeFinding(
                Location(id),
                Invariant($"The root of the page tree, {Describe(id)}, has a /Parent, which only the nodes below it may have.")));
        }

        if (!IsNode(node))
        {
            // A catalog whose /Pages is a page: a tree of one page, which the shape rules judge.
            EnterPage(node, id, parent: null);
            return;
        }

        EnterNode(node, id, parent: null);

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
        var id = kid is PdfReference reference ? reference.Id : default;

        if (id.Number > 0)
        {
            if (_onPath.Contains(id.Number))
            {
                frame.Uncountable = true;
                Cycles.Add(new ProbeFinding(
                    Location(frame.Id),
                    Invariant($"{Kid(frame, index)} names object {id.Number}, {(id.Number == frame.Id.Number ? "which is that node itself" : "a node above it")}: the tree loops back on itself there, and what it should have listed is unknown.")));
                return;
            }

            if (!_visited.Add(id.Number))
            {
                var counted = _pagesBelow.TryGetValue(id.Number, out var below) ? below : 1;
                _pages += counted;
                RepeatedNodes.Add(new ProbeFinding(
                    Location(frame.Id),
                    Invariant($"{Kid(frame, index)} names object {id.Number}, which the tree already lists: {(counted == 1 ? "its page is" : $"its {RuleText.Pages(counted)} are")} counted again.")));
                return;
            }
        }

        switch (kid.Resolve())
        {
            case PdfNull when id.Number > 0:
                switch (_reader.GetPresence(id.Number))
                {
                    case ObjectPresence.Unproduced:
                        // The entry's fault, which the cross-reference rules report: the kid may have been a node, and
                        // the counts above it cannot be judged.
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
                    Location(id.Number > 0 ? id : frame.Id),
                    Invariant($"{Kid(frame, index)} names object {id.Number}, a stream rather than a dictionary: the reader reads the stream's dictionary in its place.")));
                Enter(stream.Dictionary, id, frame);
                return;

            case PdfDictionary dictionary:
                if (id.Number <= 0)
                {
                    InvalidKids.Add(new ProbeFinding(
                        Location(frame.Id),
                        $"{Kid(frame, index)} is a dictionary written in the array, where the tree wants an indirect reference to one."));
                }

                Enter(dictionary, id, frame);
                return;

            case var other:
                InvalidSlot(
                    frame,
                    index,
                    id.Number > 0
                        ? Invariant($"names object {id.Number}, which is {RuleText.Kind(other)}, neither a page nor a node")
                        : $"is {RuleText.Kind(other)}, neither a page nor a node");
                return;
        }
    }

    /// <summary>Records a kid that stands for a page the tree cannot read, and counts it as one with nothing on it.</summary>
    private void InvalidSlot(Frame frame, int index, string what)
    {
        var page = _pages++;
        var location = page <= int.MaxValue
            ? PdfValidationLocation.OnPage((int)page, frame.Id)
            : PdfValidationLocation.OfObject(frame.Id);

        InvalidKids.Add(new ProbeFinding(
            location,
            $"{Kid(frame, index)} {what}: it counts as a page with nothing on it."));
    }

    private void Enter(PdfDictionary dictionary, PdfObjectId id, Frame parent)
    {
        if (IsNode(dictionary))
        {
            CheckParent(dictionary, id, parent, "Page tree node");
            EnterNode(dictionary, id, parent);
        }
        else
        {
            EnterPage(dictionary, id, parent);
        }
    }

    private void EnterNode(PdfDictionary node, PdfObjectId id, Frame? parent)
    {
        if (id.Number > 0)
        {
            Nodes.Add(id.Number);
        }

        var ownBox = node.GetRaw(PdfName.MediaBox);

        if (ownBox is not null)
        {
            CheckMediaBox(ownBox, Location(id), id);
        }

        var kidsValue = node.GetRaw(PdfName.Kids);

        if (kidsValue?.Resolve() is not PdfArray kids)
        {
            // A /Kids that names an object the file lacks is object.reference-missing's; either way, nothing below this
            // node can be counted, nor the nodes above it judged.
            if (kidsValue is not PdfReference)
            {
                NodesWithoutKids.Add(new ProbeFinding(
                    Location(id),
                    kidsValue is null
                        ? $"{Describe(id, "Page tree node")} has no /Kids: it lists no page."
                        : $"{Describe(id, "Page tree node")} has {RuleText.Kind(kidsValue.Resolve())} for /Kids, not an array: it lists no page."));
            }

            if (id.Number > 0)
            {
                _pagesBelow[id.Number] = 0;
            }

            if (parent is not null)
            {
                parent.Uncountable = true;
            }

            return;
        }

        _stack.Add(new Frame(node, id, kids)
        {
            PagesBefore = _pages,
            MediaBox = ownBox ?? parent?.MediaBox,
            HasResources = node.GetRaw(PdfName.Resources) is not null || parent?.HasResources == true,
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

        if (frame.Uncountable)
        {
            if (_stack.Count > 0)
            {
                _stack[^1].Uncountable = true;
            }

            return;
        }

        switch (frame.Node.GetRaw(PdfName.Count))
        {
            case null:
                CountMismatches.Add(new ProbeFinding(
                    Location(frame.Id),
                    Invariant($"{Describe(frame.Id, "Page tree node")} has no /Count; {RuleText.Pages(below)} {Lie(below)} below it.")));
                break;

            case var count when count.Resolve() is PdfInteger { Value: var declared } && declared != below:
                CountMismatches.Add(new ProbeFinding(
                    Location(frame.Id),
                    Invariant($"{Describe(frame.Id, "Page tree node")} gives /Count {declared}, and {RuleText.Pages(below)} {Lie(below)} below it.")));
                break;
        }
    }

    private void EnterPage(PdfDictionary page, PdfObjectId id, Frame? parent)
    {
        var index = _pages++;
        var location = PdfValidationLocation.OfObject(id);

        if (index <= int.MaxValue)
        {
            if (id.Number > 0)
            {
                PageIndexes.TryAdd(id.Number, (int)index);
            }

            location = PdfValidationLocation.OnPage((int)index, id);
        }

        if (parent is not null)
        {
            CheckParent(page, id, parent, "Page object");
        }

        var ownBox = page.GetRaw(PdfName.MediaBox);

        if (ownBox is not null)
        {
            CheckMediaBox(ownBox, location, id);
        }
        else if (parent?.MediaBox is null)
        {
            MediaBoxFaults.Add(new ProbeFinding(
                location,
                $"{Describe(id, "Page object")} has no /MediaBox, and no page tree node above it gives one: the page's size is unknown."));
        }

        if (page.GetRaw(PdfName.Resources) is null && parent?.HasResources != true)
        {
            ResourcesMissing.Add(new ProbeFinding(
                location,
                $"{Describe(id, "Page object")} has no /Resources, and no page tree node above it gives any."));
        }
    }

    private void CheckParent(PdfDictionary dictionary, PdfObjectId id, Frame parent, string what)
    {
        // A kid written in the array, or a node written in its own parent's, has no number to be named by.
        if (id.Number <= 0 || parent.Id.Number <= 0)
        {
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
                Invariant($"{Describe(id, what)} {fault}, and page tree node {parent.Id.Number} lists it.")));
        }
    }

    private void CheckMediaBox(PdfObject box, PdfValidationLocation location, PdfObjectId id)
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
                    ? Invariant($"{MediaBoxOf(id)} is an array of {other.Count} values, not a rectangle.")
                    : $"{MediaBoxOf(id)} is {RuleText.Kind(resolved)}, not a rectangle."));
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
                        $"{MediaBoxOf(id)} holds {RuleText.Kind(other)} among its corners, not a rectangle of four numbers."));
                    return;
            }
        }

        // Any two opposite corners make a rectangle (ISO 32000-1, 7.9.5), in whichever order they come.
        if (corners[0] == corners[2] || corners[1] == corners[3])
        {
            MediaBoxFaults.Add(new ProbeFinding(
                location,
                Invariant($"{MediaBoxOf(id)}, [{corners[0]} {corners[1]} {corners[2]} {corners[3]}], encloses no area.")));
        }
    }

    private PdfValidationLocation Location(PdfObjectId id) =>
        id.Number > 0 ? Locate(id) : default;

    private static string Lie(long count) => count == 1 ? "lies" : "lie";

    private static string MediaBoxOf(PdfObjectId id) => $"The /MediaBox of {Describe(id)}";

    private static bool IsNode(PdfDictionary dictionary) =>
        dictionary.GetRaw(PdfName.Kids)?.Resolve() is PdfArray || dictionary.GetName(PdfName.Type) == PdfName.Pages;

    private static string Kid(Frame frame, int index) =>
        Invariant($"The kid at index {index} of {Describe(frame.Id, "page tree node").ToLowerInvariant()}");

    private static string Describe(PdfObjectId id, string what = "object") =>
        id.Number > 0 ? Invariant($"{what} {id.Number}") : $"a {what.ToLowerInvariant()} written in its parent's /Kids";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>A node of the tree the walk is below, and what the pages under it inherit.</summary>
    private sealed class Frame(PdfDictionary node, PdfObjectId id, PdfArray kids)
    {
        public PdfDictionary Node { get; } = node;

        public PdfObjectId Id { get; } = id;

        public PdfArray Kids { get; } = kids;

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
