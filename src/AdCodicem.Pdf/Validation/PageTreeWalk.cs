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
/// dictionary with a <c>/Kids</c> array or of <c>/Type /Pages</c>; any other dictionary the tree lists is a page. A count
/// past what a <see langword="long"/> holds stays at its greatest, and no <c>/Count</c> is judged past it.
/// </para>
/// <para>
/// The walk is iterative and visits each indirect node, and each array of kids that is an object of its own, once, so it
/// keeps a frame per node on its current path and a few bytes per object it met: nothing is sized by what the file
/// declares. An array of kids is no node: one a node's <c>/Kids</c> names above it loops, one walked already is counted
/// again, and a kid naming one is a kid that is neither a page nor a node. It resolves the tree's nodes and pages
/// through the document, and reads no content. What it cannot judge because the reader could not produce an object —
/// an object stream an encryption hides, an entry that leads nowhere — it leaves to the rules that report those, and
/// says the walk is partial.
/// </para>
/// <para>
/// Nothing is judged of what is written in an object one of the reader's limits cut, as the Arlington walk leaves it:
/// neither what it lacks — that may lie past the cut — nor what it holds, the value at the cut being perhaps cut itself.
/// A node so cut is taken to give what the pages below it inherit, its <c>/Count</c> and those above it are not judged —
/// not the array of kids of its own it names, read whole, counted as any other where listed again —, and the walk is
/// partial; the references its <c>/Kids</c> holds, as read, are walked, each page or node judged as any other, and the
/// values after its last reference, dictionary or stream are not counted, being perhaps what the cut left of a reference.
/// A catalog so cut leaves the walk partial too: its <c>/Pages</c> may lie past the cut.
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

    /// <summary>The nodes and pages, by number, below which the pages could not be counted with confidence.</summary>
    private readonly HashSet<int> _uncountable = [];

    /// <summary>
    /// The arrays of kids that are objects of their own the walk has walked, by number: kept apart from the nodes and
    /// pages, which a kid names, so that a kid naming an array is no node walked, and an array a kid named is still walked.
    /// </summary>
    private readonly HashSet<int> _arraysWalked = [];

    /// <summary>The arrays of kids of their own being walked on the current path.</summary>
    private readonly HashSet<int> _arraysOnPath = [];

    /// <summary>How many pages lie below each array of kids of its own walked, by number.</summary>
    private readonly Dictionary<int, long> _pagesBelowArray = [];

    /// <summary>The arrays of kids of their own below which the pages could not be counted with confidence.</summary>
    private readonly HashSet<int> _uncountableArrays = [];

    /// <summary>
    /// The pages first listed past the greatest page index, as a tree listing nodes again may list them: kept apart, being
    /// at no index.
    /// </summary>
    private readonly HashSet<int> _pagesPastIndexes = [];
    private long _pages;

    /// <summary>
    /// Whether the pages counted ran past what a <see langword="long"/> holds: a tree listing nodes again lists 2^63 pages
    /// with 63 levels, each naming the next twice. The count stays at its greatest, and no node left since is judged.
    /// </summary>
    private bool _overflowed;

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
    /// kid, the index holds in use and the reader could not produce; or a node, a page, an array of kids or the catalog
    /// one of the reader's limits cut.
    /// </summary>
    public bool Partial { get; private set; }

    /// <summary>Gets the object numbers of the tree's nodes.</summary>
    public HashSet<int> Nodes { get; } = [];

    /// <summary>Gets the object numbers of the arrays of kids the walk read that are objects of their own.</summary>
    public HashSet<int> KidsArrays { get; } = [];

    /// <summary>
    /// Gets the object numbers of the nodes one of the reader's limits cut: the walk judged nothing of what they hold, the
    /// kids written in them included.
    /// </summary>
    public HashSet<int> CutNodes { get; } = [];

    /// <summary>
    /// Determines whether the walk judged the kids written in object <paramref name="number"/>'s <c>/Kids</c>: a node it
    /// read whole. Another rule judges the references to nothing written in a node a limit cut.
    /// </summary>
    public bool JudgedKidsOf(int number) => Nodes.Contains(number) && !CutNodes.Contains(number);

    /// <summary>
    /// Gets the number of the object a root of the tree written in the catalog's <c>/Pages</c> is written in — the
    /// catalog's, or 0 when the catalog is written in the trailer — when the root is a node whose kids the walk judged, read
    /// whole; -1 otherwise. Another rule judges the references to nothing written in a root a limit cut.
    /// </summary>
    public int JudgedRootIn { get; private set; } = -1;

    /// <summary>
    /// Gets the object numbers of the arrays of kids of their own whose kids the walk judged: walked under a node, and read
    /// whole. Another rule judges the references to nothing written in an array a limit cut.
    /// </summary>
    public HashSet<int> JudgedKidsArrays { get; } = [];

    /// <summary>
    /// Gets the object numbers of the nodes and pages whose <c>/Parent</c> the walk could not judge: the node listing them
    /// has no number to be named by — written inside another object, in a <c>/Kids</c> or in the catalog's <c>/Pages</c> —,
    /// or one of the reader's limits cut them. The walk judges the <c>/Parent</c> of every other node and page it entered,
    /// the root's included; the exceptions are kept rather than the rule, so that memory does not grow with the pages.
    /// </summary>
    public HashSet<int> ParentsUnjudged { get; } = [];

    /// <summary>Determines whether the walk judged the <c>/Parent</c> of object <paramref name="number"/>.</summary>
    public bool JudgedParent(int number) => (Nodes.Contains(number) || IsPage(number)) && !ParentsUnjudged.Contains(number);

    /// <summary>
    /// Gets the object number of each page of the tree, with the index it is first listed at — every page but those first
    /// listed past the greatest index, which <see cref="IsPage"/> knows.
    /// </summary>
    public Dictionary<int, int> PageIndexes { get; } = [];

    /// <summary>Determines whether object <paramref name="number"/> is a page of the tree.</summary>
    public bool IsPage(int number) => PageIndexes.ContainsKey(number) || _pagesPastIndexes.Contains(number);

    /// <summary>Walks the page tree of <paramref name="document"/>.</summary>
    public static PageTreeWalk Run(PdfDocument document)
    {
        var walk = new PageTreeWalk(document.Reader);

        // The catalog is an object of its own, or a dictionary written in the trailer, which file.root-invalid reports.
        var catalogPlace = document.Trailer.GetRaw(PdfName.Root) is PdfReference reference
            ? Place.Of(reference.Target())
            : new Place(default, default, "/Root");

        // A catalog a limit cut may give its /Pages past the cut, or be past the cut whole, unread: the tree read, if any,
        // may not be the whole of it (#259).
        walk.Partial = walk.IsCut(catalogPlace);

        if (document.Catalog is not { } catalog)
        {
            return walk;
        }

        if (catalog.GetRaw(PdfName.Pages) is { } root)
        {
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
        var place = rootValue is PdfReference reference ? Place.Of(reference.Target()) : catalog.Inside("/Pages");
        var id = place.Id;
        var root = rootValue.Resolve();

        if (root is PdfNull)
        {
            // A /Pages that names an object the file lacks is object.reference-missing's to report; one the index holds
            // and the reader could not produce is the index's, and one a guard left out of the index the guard's: either
            // leaves the tree unknown.
            Partial |= id.Number > 0 && _reader.GetPresence(id.Number) is ObjectPresence.Unproduced or ObjectPresence.Unindexed;
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

        if (!IsCut(place) && Given(node, PdfName.Parent) is not null)
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

        if (id.Number <= 0 && !IsCut(place))
        {
            JudgedRootIn = place.Holder.Number;
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
        // which heads the free list, and reads as null as any object the file lacks does. Through a chain of references,
        // the kid is the object the chain leads to, whose value is read.
        var reference = kid as PdfReference;
        var id = reference?.Target() ?? default;
        // A kid that is a link of a chain is named with the object the chain leads to, which is what the finding is about.
        var named = reference is null
            ? string.Empty
            : reference.Id.Number == id.Number
                ? Invariant($"object {id.Number}")
                : Invariant($"object {reference.Id.Number}, which leads to object {id.Number}");

        // What a node's /Kids holds is not judged where a limit cut the array: the findings on the kid itself are, located
        // at the object it is.
        var judged = !frame.KidsCut;

        if (reference is not null)
        {
            if (_onPath.Contains(id.Number))
            {
                frame.Uncountable = true;

                if (judged)
                {
                    Cycles.Add(new ProbeFinding(
                        Location(frame.Place),
                        Invariant($"{Kid(frame, index)} names {named}, {(id.Number == frame.Id.Number ? "which is that node itself" : "a node above it")}: the tree loops back on itself there, and what it should have listed is unknown.")));
                }

                return;
            }

            if (!_visited.Add(id.Number))
            {
                if (judged)
                {
                    RepeatedNodes.Add(new ProbeFinding(
                        Location(frame.Place),
                        Invariant($"{Kid(frame, index)} names {named}, which the tree already lists: {CountedAgain(frame, id.Number)}.")));
                }
                else
                {
                    CountAgain(frame, id.Number);
                }

                return;
            }
        }
        else if (!judged && index >= frame.FragmentsFrom)
        {
            // A value written in an array a limit cut, after its last reference, dictionary or stream, may be what the cut
            // left of a reference — "12 0" of "12 0 R" —: it is not taken for a kid.
            return;
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
                        _uncountable.Add(id.Number);
                        CountPages(1);
                        return;

                    case ObjectPresence.Missing:
                        InvalidSlot(frame, index, Invariant($"names {named}, which the file lacks"));
                        return;

                    default:
                        // A chain that reads as null without ending — too long to follow, or looping — is no object the
                        // file defines as null.
                        InvalidSlot(
                            frame,
                            index,
                            _reader.GetObject(id) is PdfReference
                                ? Invariant($"names object {reference.Id.Number}, whose chain of references is too long to follow or loops, which reads as null")
                                : Invariant($"names {named}, which is null"));
                        return;
                }

            case PdfNull:
                InvalidSlot(frame, index, "is null");
                return;

            case PdfStream stream:
                var streamPlace = reference is not null ? Place.Of(id) : frame.KidPlace(index);

                if (reference is not null || judged)
                {
                    InvalidKids.Add(new ProbeFinding(
                        Location(reference is not null ? streamPlace : frame.Place),
                        reference is not null
                            ? Invariant($"{Kid(frame, index)} names {named}, a stream rather than a dictionary: the reader reads the stream's dictionary in its place.")
                            : $"{Kid(frame, index)} is a stream written in the array, where the tree wants an indirect reference to a dictionary: the reader reads the stream's dictionary in its place."));
                }

                Enter(stream.Dictionary, streamPlace, frame);
                return;

            case PdfDictionary dictionary:
                if (reference is null && judged)
                {
                    InvalidKids.Add(new ProbeFinding(
                        Location(frame.Place),
                        $"{Kid(frame, index)} is a dictionary written in the array, where the tree wants an indirect reference to one."));
                }

                Enter(dictionary, reference is not null ? Place.Of(id) : frame.KidPlace(index), frame);
                return;

            case var other:
                InvalidSlot(
                    frame,
                    index,
                    reference is not null
                        ? Invariant($"names {named}, which is {RuleText.Kind(other)}, neither a page nor a node")
                        : $"is {RuleText.Kind(other)}, neither a page nor a node");
                return;
        }
    }

    /// <summary>
    /// Counts again what lies below object <paramref name="number"/>, a page or a node the tree already walked — or, when
    /// <paramref name="array"/>, an array of kids of its own —, under <paramref name="frame"/>: one page, or the pages
    /// counted below it, which a count that could not be trusted leaves the frame's uncountable.
    /// </summary>
    /// <returns>How many pages were counted, or -1 when they could not be counted with confidence.</returns>
    private long CountAgain(Frame frame, int number, bool array = false)
    {
        var counted = (array ? _pagesBelowArray : _pagesBelow).TryGetValue(number, out var below) ? below : 1;
        CountPages(counted);

        if ((array ? _uncountableArrays : _uncountable).Contains(number))
        {
            frame.Uncountable = true;
            return -1;
        }

        return counted;
    }

    /// <summary>Counts again what lies below object <paramref name="number"/>, and says what was counted, for a finding.</summary>
    private string CountedAgain(Frame frame, int number, bool array = false) => CountAgain(frame, number, array) switch
    {
        -1 => "what lies below it is counted again, how many pages that is being unknown",
        1 => "its page is counted again",
        var counted => Invariant($"its {RuleText.Pages(counted)} are counted again"),
    };

    /// <summary>
    /// Counts <paramref name="pages"/> pages more, at most up to what a <see langword="long"/> holds, and gives how many were
    /// counted before them: the index of the first.
    /// </summary>
    private long CountPages(long pages)
    {
        var before = _pages;

        if (pages > long.MaxValue - _pages)
        {
            _pages = long.MaxValue;
            _overflowed = true;
        }
        else
        {
            _pages += pages;
        }

        return before;
    }

    /// <summary>
    /// Records a kid that stands for a page the tree cannot read, and counts it as one with nothing on it; the record is
    /// not kept where a limit cut the array.
    /// </summary>
    private void InvalidSlot(Frame frame, int index, string what)
    {
        var page = CountPages(1);

        if (!frame.KidsCut)
        {
            InvalidKids.Add(new ProbeFinding(
                PageLocation(page, frame.Place),
                $"{Kid(frame, index)} {what}: it counts as a page with nothing on it."));
        }
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
        var cut = IsCut(place);

        if (id.Number > 0)
        {
            Nodes.Add(id.Number);
        }

        Partial |= cut;

        var ownBox = Given(node, PdfName.MediaBox);

        if (ownBox is not null && !cut)
        {
            CheckMediaBox(ownBox, Location(place), place, NodeKind);
        }

        var kidsValue = Given(node, PdfName.Kids);

        if (kidsValue?.Resolve() is not PdfArray kids)
        {
            // A /Kids that names an object the file lacks is object.reference-missing's, and one the reader could not
            // produce the index's; either way, nothing below this node can be counted, nor the nodes above it judged.
            if (!cut && (kidsValue is null || kidsValue.Resolve() is not PdfNull))
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
            if (!cut && Given(node, PdfName.Count) is null)
            {
                CountMissing(place, below: 0, countable: true);
            }

            return;
        }

        var kidsArray = default(PdfObjectId);
        var walksKids = true;
        var loops = false;
        var kidsReference = kidsValue as PdfReference;

        if (kidsReference is not null)
        {
            kidsArray = kidsReference.Target();
            KidsArrays.Add(kidsArray.Number);

            // An array of kids that is an object of its own is walked once, as a node is: one on the path loops back, and
            // one walked already is counted again, not walked — nodes written in it cannot be told apart by number. The
            // node itself is entered all the same: its own /Count, and what it is counted for where it is listed again,
            // are judged as any node's.
            loops = _arraysOnPath.Contains(kidsArray.Number);
            walksKids = !loops && _arraysWalked.Add(kidsArray.Number);
        }

        // The kids lie in the node, or in their array when it is an object of its own, which a limit may have cut alone.
        var kidsCut = kidsArray.Number > 0 ? _reader.IsCutAtLimit(kidsArray.Number) : cut;
        Partial |= kidsCut;

        if (cut && id.Number > 0)
        {
            CutNodes.Add(id.Number);
        }

        if (walksKids && !kidsCut && kidsArray.Number > 0)
        {
            JudgedKidsArrays.Add(kidsArray.Number);
        }

        // A node a limit cut may give past the cut what its pages inherit, and a /Count of what lies below it.
        var frame = new Frame(node, place, walksKids ? kids : [], kidsArray)
        {
            PagesBefore = _pages,
            GivesMediaBox = ownBox is not null || parent?.GivesMediaBox == true || cut,
            HasResources = Given(node, PdfName.Resources) is not null || parent?.HasResources == true || cut,
            Cut = cut,
            KidsCut = kidsCut,
            WalksKids = walksKids,
            FragmentsFrom = kidsCut ? FragmentsFrom(kids) : kids.Count,
            Uncountable = kidsCut || loops,
        };

        _stack.Add(frame);

        if (id.Number > 0)
        {
            _onPath.Add(id.Number);
        }

        if (walksKids && kidsArray.Number > 0)
        {
            _arraysOnPath.Add(kidsArray.Number);
        }

        if (loops)
        {
            if (!cut)
            {
                Cycles.Add(new ProbeFinding(
                    Location(place),
                    Invariant($"{Subject(place, NodeKind)} gives /Kids {kidsReference!.Id.Number} {kidsReference.Id.Generation} R, an array of kids above it: the tree loops back on itself there, and what it should have listed is unknown.")));
            }
        }
        else if (!walksKids)
        {
            if (!cut)
            {
                RepeatedNodes.Add(new ProbeFinding(
                    Location(place),
                    Invariant($"{Subject(place, NodeKind)} gives /Kids {kidsReference!.Id.Number} {kidsReference.Id.Generation} R, an array of kids the tree already lists: {CountedAgain(frame, kidsArray.Number, array: true)}.")));
            }
            else
            {
                CountAgain(frame, kidsArray.Number, array: true);
            }
        }
    }

    private void Leave(Frame frame)
    {
        _stack.RemoveAt(_stack.Count - 1);
        _onPath.Remove(frame.Id.Number);

        var below = _pages - frame.PagesBefore;

        // Past what a long holds, the pages below a node left since are no number.
        if (_overflowed)
        {
            frame.Uncountable = true;
        }

        if (frame.Id.Number > 0)
        {
            _pagesBelow[frame.Id.Number] = below;

            if (frame.Doubtful)
            {
                _uncountable.Add(frame.Id.Number);
            }
        }

        // An array of kids is recorded by the node that walked it, not by one that found it walked or above it; the doubt
        // a limit's cut of that node casts is on the node, not on the array, read whole with what it lists.
        if (frame.WalksKids && frame.KidsArray.Number > 0)
        {
            _arraysOnPath.Remove(frame.KidsArray.Number);
            _pagesBelowArray[frame.KidsArray.Number] = below;

            if (frame.Uncountable)
            {
                _uncountableArrays.Add(frame.KidsArray.Number);
            }
        }

        if (frame.Doubtful && _stack.Count > 0)
        {
            _stack[^1].Uncountable = true;
        }

        switch (Given(frame.Node, PdfName.Count))
        {
            case null:
                // Missing whether or not the pages below can be counted; unknown where a limit cut the node.
                if (!frame.Cut)
                {
                    CountMissing(frame.Place, below, countable: !frame.Doubtful);
                }

                break;

            case var count when !frame.Doubtful && count.Resolve() is PdfInteger { Value: var declared } && declared != below:
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
        var cut = IsCut(place);
        var index = CountPages(1);

        // A page a limit cut may be a node whose /Kids lies past the cut: what lies below it is unknown, and so is the count
        // of the node listing it.
        Partial |= cut;

        if (cut)
        {
            parent?.Uncountable = true;

            // Listed again, it is counted again with the same doubt.
            if (id.Number > 0)
            {
                _uncountable.Add(id.Number);
            }
        }

        if (id.Number > 0 && !PageIndexes.ContainsKey(id.Number))
        {
            if (index <= int.MaxValue)
            {
                PageIndexes.Add(id.Number, (int)index);
            }
            else
            {
                _pagesPastIndexes.Add(id.Number);
            }
        }

        var location = PageLocation(index, place);

        if (parent is not null)
        {
            CheckParent(page, place, parent, PageKind);
        }

        if (cut)
        {
            return;
        }

        var ownBox = Given(page, PdfName.MediaBox);

        if (ownBox is not null)
        {
            CheckMediaBox(ownBox, location, place, PageKind);
        }
        else if (parent?.GivesMediaBox != true)
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

        // A kid written in the array, or a node written in its own parent's, has no number to be named by; and one a limit
        // cut may give its /Parent past the cut.
        if (id.Number <= 0 || parent.Id.Number <= 0 || IsCut(place))
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

    /// <summary>
    /// Determines whether what lies at <paramref name="place"/> was read only as far as one of the reader's limits let it
    /// be: the object it is, or the one it is written in, was cut — or, written in the trailer, the trailer the catalog was
    /// read from.
    /// </summary>
    private bool IsCut(Place place) =>
        place.Holder.Number > 0 ? _reader.IsCutAtLimit(place.Holder.Number) : _reader.IsWrittenInCutTrailer(PdfName.Root);

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

    /// <summary>
    /// Gives the index past the last reference, dictionary or stream of <paramref name="kids"/>: what follows it in an array
    /// a limit cut may be what the cut left of a reference.
    /// </summary>
    private static int FragmentsFrom(PdfArray kids)
    {
        for (var index = kids.Count - 1; index >= 0; index--)
        {
            if (kids[index] is PdfReference or PdfDictionary or PdfStream)
            {
                return index + 1;
            }
        }

        return 0;
    }

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

        /// <summary>Gets the node's array of kids when it is an object of its own, or a number 0.</summary>
        public PdfObjectId KidsArray { get; } = kidsArray;

        /// <summary>Gives the place of the kid written at <paramref name="index"/> of the node's <c>/Kids</c>.</summary>
        /// <remarks>
        /// In an array that is an object of its own, the kid is written in that object; otherwise in the node, under its
        /// <c>/Kids</c>.
        /// </remarks>
        public Place KidPlace(int index) =>
            KidsArray.Number > 0
                ? new Place(default, KidsArray, Invariant($"[{index}]"))
                : Place.Inside(Invariant($"/Kids[{index}]"));

        /// <summary>Gets or sets the index of the next kid to visit.</summary>
        public int Next { get; set; }

        /// <summary>Gets or sets how many pages the walk had counted when it entered the node.</summary>
        public long PagesBefore { get; init; }

        /// <summary>
        /// Gets a value indicating whether the node gives the pages below it a media box, its own or inherited, or may give
        /// one past a limit's cut.
        /// </summary>
        public bool GivesMediaBox { get; init; }

        /// <summary>
        /// Gets a value indicating whether the node gives the pages below it resources, its own or inherited, or may give
        /// them past a limit's cut.
        /// </summary>
        public bool HasResources { get; init; }

        /// <summary>Gets a value indicating whether a limit cut the object the node is written in.</summary>
        public bool Cut { get; init; }

        /// <summary>Gets a value indicating whether a limit cut the object the node's <c>/Kids</c> is written in.</summary>
        public bool KidsCut { get; init; }

        /// <summary>
        /// Gets a value indicating whether the node walks its kids: not where its array of kids of its own is above it, or
        /// was walked already, the node then listing no kid of its own.
        /// </summary>
        public bool WalksKids { get; init; }

        /// <summary>
        /// Gets the index from which a value of the node's kids is not taken for a kid, being perhaps what a limit's cut left
        /// of a reference: past the last reference, dictionary or stream of an array a limit cut, else past its end.
        /// </summary>
        public int FragmentsFrom { get; init; }

        /// <summary>
        /// Gets or sets a value indicating whether what the node's kids list cannot be counted with confidence: a kid loops
        /// back, could not be read, or may be a node; a limit cut the array; or the count ran past what a
        /// <see langword="long"/> holds.
        /// </summary>
        public bool Uncountable { get; set; }

        /// <summary>
        /// Gets a value indicating whether the node's <c>/Count</c>, and those of the nodes above it, are not judged: what
        /// its kids list cannot be counted, or a limit cut the node itself.
        /// </summary>
        public bool Doubtful => Uncountable || Cut;
    }
}
