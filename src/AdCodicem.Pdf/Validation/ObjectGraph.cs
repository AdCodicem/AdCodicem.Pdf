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
/// trailer's <c>/Prev</c> or <c>/XRefStm</c>, which name sections rather than objects —, and resolves each object once,
/// by its number, through the document — a reference that names a generation the object does not carry is resolved too,
/// under that generation, as resolving it asks for it, and reads as null (<see cref="ClaimAgain"/>, #185) —: memory
/// follows the reader's cache and a set of object numbers, and no stream's data is read. Cycles through <c>/Parent</c>,
/// <c>/P</c> or a destination are legal and everywhere; the set of numbers already met stops them.
/// </para>
/// <para>
/// Each object is walked whole when it is read, in the order it is written, and claims the objects its references name
/// that no reference named before: what the walk keeps of an object still to read is its number, with the object that
/// named it first and the role of that reference — 16 bytes —, never a value written in the object that named it
/// (#168); and of the references met to it before it is read, counts by holder and one reference, no more than
/// <see cref="MaxWaitingCounts"/> counts in all, past which a reference from a holder that has no count yet has the object
/// read, so that they grow neither with the references nor with the generations they name (<see cref="ClaimAgain"/>,
/// #185). A chain of references so holds what the reader keeps, whatever the objects along
/// it hold beside the reference and wherever they write it. The objects claimed are read last claimed first, so that a
/// tree of objects is read as a depth-first walk of what it holds would read it, the last value first. Walking one
/// object keeps a place for each level of nesting it is in, which the parser bounds in an object read from a file; a
/// value a caller built nests as deep as it was built, and one that holds itself is walked without end, its array of
/// containers growing a level at a time until memory runs out (#310). The places are kept in an array, not on the
/// stack, by the walk and by the searches that place a fault once it is done alike, so that no depth a caller built
/// overflows the stack (#168).
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
    /// <summary>
    /// How many counts by holder the walk keeps of the references met to objects it has yet to read before it reads such an
    /// object at the next one instead, which tells that object's references there and then (<see cref="ClaimAgain"/>).
    /// </summary>
    /// <remarks>
    /// No guard (ADR 34, amended 2026-11-26): it changes when the walk reads an object, not what it reads nor what it
    /// reports, and a valid file may reach it. A file whose objects many holders name before the walk reads them — each
    /// holder naming each of them, as a table of references between thousands of objects does — would keep a count for each
    /// pair: memory as large as the references, valid or not (#185). Past it, a reference from a holder that has no count
    /// yet has the object read; one from a holder that has one adds to it.
    /// </remarks>
    internal const int MaxWaitingCounts = 16_384;

    private readonly PdfDocument _document;
    private readonly PdfFileReader _reader;
    private readonly PageTreeWalk _pages;

    /// <summary>The objects claimed and not read yet, by number: never their values, nor those of the objects that named them.</summary>
    private readonly Stack<Pending> _work = new();
    private readonly HashSet<int> _visited = new(SeededComparer.Instance);
    private readonly HashSet<int> _missing = new(SeededComparer.Instance);

    /// <summary>The objects claimed and not read yet, by number, with the generation the reference that claimed each names.</summary>
    private readonly Dictionary<int, int> _claimed = new(SeededComparer.Instance);

    /// <summary>
    /// What references met to objects claimed and not read yet leave to tell, by number: counts by holder and the first
    /// reference that names the generation the object carries, as <see cref="ClaimAgain"/> keeps them.
    /// </summary>
    private readonly Dictionary<int, Waiting> _waiting = new(SeededComparer.Instance);

    /// <summary>How many counts by holder <see cref="_waiting"/> keeps in all, which <see cref="MaxWaitingCounts"/> bounds.</summary>
    private int _waitingCounts;

    /// <summary>
    /// The objects read only through references that name a generation they do not carry, which read as null: none of
    /// them is walked until a reference naming the generation it carries is met.
    /// </summary>
    private readonly HashSet<int> _unwalked = new(SeededComparer.Instance);
    private readonly SortedDictionary<int, int> _missingReferences = [];

    /// <summary>
    /// Whether the chain of references from an object whose value is a reference reaches no value, by the object's number,
    /// told once the object is read; its keys are the links of chains, which hold no reference of their own to judge: a chain
    /// is judged at the object that holds its first reference, as a resolution reads it (#173). Null until such an object is
    /// read: no valid file holds one.
    /// </summary>
    private Dictionary<int, bool>? _chains;

    /// <summary>
    /// The objects read before the walk reached them, still claimed, found to hold no reference as their value: told once,
    /// not read again for each reference met to them. Null until one is.
    /// </summary>
    private HashSet<int>? _plainWhileWaiting;
    private readonly SortedDictionary<int, int> _cutReferences = [];
    private readonly SortedDictionary<int, int> _nullCharacterNames = [];

    /// <summary>
    /// The containers of the value being walked or searched, the first <see cref="_depth"/>, from the outermost to the one
    /// being read: one a level of nesting, each let go of when it is left, so that none outlives the walk of the object it
    /// is in, nor the search.
    /// </summary>
    private Scope[] _scopes = new Scope[8];
    private int _depth;

    private ObjectGraph(PdfDocument document, PageTreeWalk pages, int room)
    {
        _document = document;
        _reader = document.Reader;
        _pages = pages;
        MissingReferences = new(room);
        CutChains = new(room);
        NullCharacterNames = new(room);
        MissingEndObj = new(room);
    }

    /// <summary>Gets the objects, one finding each, that refer to objects the file lacks.</summary>
    public ProbeFindings MissingReferences { get; }

    /// <summary>Gets the objects, one finding each, that refer to objects whose chain of references reaches no value.</summary>
    public ProbeFindings CutChains { get; }

    /// <summary>Gets the objects, one finding each, that hold a name with a null character.</summary>
    public ProbeFindings NullCharacterNames { get; }

    /// <summary>Gets the objects whose value <c>endobj</c> does not follow.</summary>
    public ProbeFindings MissingEndObj { get; }

    /// <summary>Gets the most counts by holder the walk kept at once, which <see cref="MaxWaitingCounts"/> bounds.</summary>
    internal int MostWaitingCounts { get; private set; }

    /// <summary>Gets how many counts by holder the walk still keeps once it is done: none.</summary>
    internal int WaitingCountsLeft => _waitingCounts;

    /// <summary>Gets how many objects the walk read before it reached them because it kept <see cref="MaxWaitingCounts"/> counts.</summary>
    internal int ReadEarly { get; private set; }

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
            var claimedUnder = pending.Id.Generation;
            var value = _reader.GetObject(pending.Id);
            _claimed.Remove(number);

            if (_waiting.Remove(number, out var waiting))
            {
                _waitingCounts -= waiting.Counted?.Count ?? 0;
            }

            // A reference that names a generation the object does not carry reads as null, and names an object the file
            // lacks (7.3.10): the one that claimed it, and each met while it waited. The object is walked through the first
            // that names the generation it carries, if one does.
            if (_reader.TryGetCarriedGeneration(number, out var carried) &&
                !TellGenerations(ref pending, ref value, carried, waiting))
            {
                _unwalked.Add(number);
                continue;
            }

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

            // An object whose value is a reference is a link of a chain, which a resolution follows: one whose chain reaches no
            // value reads as null for each reference to it — the one the walk took it through, and those counted while it
            // waited, which name the generation it carries where the claim does (#173).
            if (value is PdfReference && StartsCutChain(pending.Id))
            {
                CountCut(pending.Role, pending.Owner);

                if (claimedUnder == pending.Id.Generation && waiting?.Counted is { } byOwner)
                {
                    AddCuts(byOwner);
                }
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

            case PdfName name when HasNullCharacter(name):
                Tally(_nullCharacterNames, owner);
                break;

            default:
                Open(value, role, length: 0);
                break;
        }
    }

    /// <summary>
    /// Enters <paramref name="value"/> when it is a container, for its values to be met in their turn: a dictionary, or a
    /// stream's, keeps <paramref name="role"/> to give its entries theirs, an array the role its elements take.
    /// </summary>
    /// <param name="value">The value met.</param>
    /// <param name="role">What the page tree walk and the other rules make of it.</param>
    /// <param name="length">The length of the path that leads to it, which a search writes; 0 for the walk, which writes none.</param>
    private void Open(PdfObject value, Role role, int length)
    {
        switch (value)
        {
            case PdfStream stream:
                Enter(new Scope { Entries = stream.Dictionary.GetEnumerator(), Role = role, Length = length });
                break;

            case PdfDictionary dictionary:
                Enter(new Scope { Entries = dictionary.GetEnumerator(), Role = role, Length = length });
                break;

            case PdfArray array:
                Enter(new Scope { Array = array, Role = ElementRole(role), Length = length });
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

    /// <summary>Leaves every container a search is in, once it has found what it sought, and lets go of them.</summary>
    private void LeaveAll()
    {
        Array.Clear(_scopes, 0, _depth);
        _depth = 0;
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
            ClaimAgain(reference, owner, role);
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
        _claimed[number] = reference.Id.Generation;
    }

    /// <summary>
    /// Meets <paramref name="reference"/> again, to an object a reference claimed before. While the reader has not read
    /// the object, a reference that names the claim's generation waits, counted by the object that holds it, for that
    /// generation to be told when the walk reaches the object; one that names another, or one past
    /// <see cref="MaxWaitingCounts"/> counts kept, has the object read now by its number, as resolving the reference reads it
    /// — of two generations, only reading it says which one names it —, and the references counted so far are told with it.
    /// Once the reader has read the object, each reference is told when it is met: one that names a generation the object
    /// does not carry is counted as one to an object the file lacks, and resolved, as resolving it asks for it; one that
    /// names the generation it carries leads the walk to it where only references naming another did.
    /// </summary>
    private void ClaimAgain(PdfReference reference, int owner, Role role)
    {
        var number = reference.Id.Number;
        var generation = reference.Id.Generation;
        var waiting = _claimed.TryGetValue(number, out var claimedUnder);
        var known = _reader.TryGetCarriedGeneration(number, out var carried);

        if (waiting && !known && generation == claimedUnder)
        {
            if ((role & Role.Uncounted) != 0)
            {
                return;
            }

            var counts = WaitingFor(number);

            if (counts.Counted is { } counted && counted.TryGetValue(owner, out var count))
            {
                counted[owner] = count + 1;
                return;
            }

            if (_waitingCounts < MaxWaitingCounts)
            {
                (counts.Counted ??= new(SeededComparer.Instance)).Add(owner, 1);
                _waitingCounts++;
                MostWaitingCounts = Math.Max(MostWaitingCounts, _waitingCounts);
                return;
            }

            ReadEarly++;
        }

        var resolved = !known && waiting;

        if (!known && (!waiting || !ResolveToTell(reference.Id, out carried)))
        {
            return;
        }

        if (resolved)
        {
            TellWaiting(number, claimedUnder, carried);
        }

        if (carried != generation)
        {
            // Read once only: the object read for it read the reference too.
            if (!resolved)
            {
                _ = _reader.GetObject(reference.Id);
            }

            CountMissing(role, owner);
        }
        else if (waiting && claimedUnder != carried)
        {
            // The first takes the claim's place when the walk reaches the object, and is told then; the others now.
            var first = WaitingFor(number);

            if (first.Named is null)
            {
                first.Named = (owner, role);
            }
            else if (StartsCutChainOnceRead(reference.Id, waiting))
            {
                CountCut(role, owner);
            }
        }
        else if (_unwalked.Remove(number))
        {
            _work.Push(new Pending(reference.Id, owner, role));
            _claimed[number] = generation;
        }
        else if (StartsCutChainOnceRead(reference.Id, waiting))
        {
            CountCut(role, owner);
        }
    }

    /// <summary>
    /// Reads, by its number, the object <paramref name="id"/> names, as resolving the reference reads it, and gives the
    /// generation the object carries; false for one read as null for want of a definition, which carries none.
    /// </summary>
    private bool ResolveToTell(PdfObjectId id, out int carried)
    {
        _ = _reader.GetObject(id);
        return _reader.TryGetCarriedGeneration(id.Number, out carried);
    }

    /// <summary>
    /// Tells, once waiting object <paramref name="number"/> is read before the walk reaches it and found to carry
    /// <paramref name="carried"/>, the references counted while it waited under the claim's generation,
    /// <paramref name="claimedUnder"/>: references to an object the file lacks where that generation is not the object's, and
    /// nothing otherwise. Their counts are let go; the claim itself is told when the walk reaches the object.
    /// </summary>
    private void TellWaiting(int number, int claimedUnder, int carried)
    {
        if (!_waiting.TryGetValue(number, out var waiting) || waiting.Counted is not { } byOwner)
        {
            return;
        }

        if (claimedUnder != carried)
        {
            AddCounts(byOwner);
        }
        else if (StartsCutChainOnceRead(new PdfObjectId(number, carried), waiting: true))
        {
            AddCuts(byOwner);
        }

        _waitingCounts -= byOwner.Count;
        waiting.Counted = null;
    }

    /// <summary>Adds the counts <paramref name="byOwner"/> holds to the references to objects the file lacks, by holder.</summary>
    private void AddCounts(Dictionary<int, int> byOwner)
    {
        foreach (var (owner, count) in byOwner)
        {
            _missingReferences[owner] = _missingReferences.TryGetValue(owner, out var counted) ? counted + count : count;
        }
    }

    /// <summary>
    /// Adds the counts <paramref name="byOwner"/> holds to the references to a chain of references that reaches no value, by
    /// holder, but for a holder that is a link of a chain itself.
    /// </summary>
    private void AddCuts(Dictionary<int, int> byOwner)
    {
        foreach (var (owner, count) in byOwner)
        {
            if (_chains?.ContainsKey(owner) != true)
            {
                _cutReferences[owner] = _cutReferences.TryGetValue(owner, out var counted) ? counted + count : count;
            }
        }
    }

    /// <summary>
    /// Determines whether object <paramref name="id"/>, read, whose value is a reference, starts a chain that reaches no
    /// value, as <see cref="PdfFileReader.IsChainCut"/> says once for each such object.
    /// </summary>
    private bool StartsCutChain(PdfObjectId id)
    {
        _chains ??= new(SeededComparer.Instance);

        if (!_chains.TryGetValue(id.Number, out var cut))
        {
            cut = _reader.IsChainCut(id, out _);
            _chains.Add(id.Number, cut);
        }

        return cut;
    }

    /// <summary>
    /// Determines whether the object <paramref name="id"/> names, which the reader has read, starts a chain that reaches no
    /// value: told once the walk reaches it; for one read before the walk reached it, still <paramref name="waiting"/>, its
    /// value is asked again, as resolving a reference asks.
    /// </summary>
    private bool StartsCutChainOnceRead(PdfObjectId id, bool waiting)
    {
        if (_chains?.TryGetValue(id.Number, out var cut) == true)
        {
            return cut;
        }

        if (!waiting || _plainWhileWaiting?.Contains(id.Number) == true)
        {
            return false;
        }

        if (_reader.GetObject(id) is PdfReference)
        {
            return StartsCutChain(id);
        }

        (_plainWhileWaiting ??= new(SeededComparer.Instance)).Add(id.Number);
        return false;
    }

    /// <summary>Gives what references met to waiting object <paramref name="number"/> leave to tell, kept from the first.</summary>
    private Waiting WaitingFor(int number)
    {
        if (!_waiting.TryGetValue(number, out var waiting))
        {
            waiting = new Waiting();
            _waiting.Add(number, waiting);
        }

        return waiting;
    }

    /// <summary>
    /// Tells the references to object <paramref name="pending"/> names, which carries <paramref name="carried"/>: the one that
    /// claimed it and, under the same generation, those <paramref name="waiting"/> counted after it, which are counted as
    /// references to an object the file lacks where that generation is not the object's; the walk then takes the place and
    /// value of the first reference met that names the generation the object carries, if one did. False where none names it.
    /// </summary>
    private bool TellGenerations(ref Pending pending, ref PdfObject value, int carried, Waiting? waiting)
    {
        if (pending.Id.Generation == carried)
        {
            return true;
        }

        CountMissing(pending.Role, pending.Owner);

        if (waiting?.Counted is { } byOwner)
        {
            AddCounts(byOwner);
        }

        if (waiting?.Named is not { } named)
        {
            return false;
        }

        pending = new Pending(new PdfObjectId(pending.Id.Number, carried), named.Owner, named.Role);
        value = _reader.GetObject(pending.Id);
        return true;
    }

    private void CountMissing(Role role, int owner)
    {
        if ((role & Role.Uncounted) == 0)
        {
            Tally(_missingReferences, owner);
        }
    }

    /// <summary>
    /// Counts a reference to a chain of references that reaches no value, written in object <paramref name="owner"/> with
    /// <paramref name="role"/>: not one another rule judges, nor one a link of a chain holds, which the chain's first holder
    /// stands for.
    /// </summary>
    private void CountCut(Role role, int owner)
    {
        if ((role & Role.Uncounted) == 0 && _chains?.ContainsKey(owner) != true)
        {
            Tally(_cutReferences, owner);
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
            var found = TryFind(value, owner == 0 ? Role.Trailer : RoleOf(owner), first, cut: false, out var target);
            var holder = Holder(owner);
            var carried = -1;
            var defined = found && NamesAnotherGeneration(target, out carried)
                ? Invariant($", the object numbered {target.Number} carrying generation {carried}")
                : string.Empty;

            MissingReferences.Keep(new ProbeFinding(
                LocationOf(owner),
                (count, found) switch
                {
                    (1, true) when defined.Length > 0 => Invariant($"{holder} refers to object {target.Number} {target.Generation} under {Path(first)}, which the file lacks: the object numbered {target.Number} carries generation {carried}, and the reference reads as null."),
                    (1, true) => Invariant($"{holder} refers to object {target.Number} {target.Generation} under {Path(first)}, which the file lacks: the reference reads as null."),
                    (_, true) => Invariant($"{holder} holds {count} references to objects the file lacks, which read as null; the first refers to object {target.Number} {target.Generation} under {Path(first)}{defined}."),
                    (1, false) => $"{holder} refers to an object the file lacks: the reference reads as null.",
                    _ => Invariant($"{holder} holds {count} references to objects the file lacks, which read as null."),
                }));
        }

        foreach (var (owner, count) in _cutReferences)
        {
            if (!CutChains.Take())
            {
                continue;
            }

            var first = new StringBuilder();
            var found = TryFind(ValueOf(owner), owner == 0 ? Role.Trailer : RoleOf(owner), first, cut: true, out var target);
            var holder = Holder(owner);
            var chain = string.Empty;

            if (found)
            {
                _ = _reader.IsChainCut(target, out var loopsBackTo);
                chain = PdfFileReader.DescribeChainCut(loopsBackTo);
            }

            CutChains.Keep(new ProbeFinding(
                LocationOf(owner),
                (count, found) switch
                {
                    (1, true) => Invariant($"{holder} refers to object {target.Number} {target.Generation} under {Path(first)}, whose chain of references {chain}: the reference reads as null."),
                    (_, true) => Invariant($"{holder} holds {count} references whose chains of references reach no value, which read as null; the first refers to object {target.Number} {target.Generation} under {Path(first)}, whose chain of references {chain}."),
                    (1, false) => $"{holder} refers to an object whose chain of references reaches no value: the reference reads as null.",
                    _ => Invariant($"{holder} holds {count} references whose chains of references reach no value, which read as null."),
                }));
        }

        foreach (var (owner, count) in _nullCharacterNames)
        {
            if (!NullCharacterNames.Take())
            {
                continue;
            }

            var first = new StringBuilder();
            var name = FindNullCharacter(ValueOf(owner), owner == 0 ? Role.Trailer : Role.None, first, out var isKey);
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
            // An object only references to another generation of its number led to is not reachable: they read as null.
            if (!_unwalked.Contains(number) && _reader.IsEndObjMissing(number, out var position) && MissingEndObj.Take())
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
    /// counted — or, <paramref name="cut"/>, to a chain of references that reaches no value —, and writes where it lies into
    /// <paramref name="path"/>; the references of other objects are not followed.
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
    /// <para>
    /// It reads <paramref name="value"/> as the walk does, in a loop over the containers it is in, each with the length of
    /// the path that leads to it: a call a level overflowed a stack of 1 MB some 1,600 nodes down a page tree a caller
    /// nested, which nothing catches (#168).
    /// </para>
    /// </remarks>
    private bool TryFind(PdfObject value, Role role, StringBuilder path, bool cut, out PdfObjectId target)
    {
        if (Matches(value, role, cut, out target))
        {
            return true;
        }

        Open(value, role, path.Length);

        while (_depth > 0)
        {
            ref var scope = ref _scopes[_depth - 1];
            path.Length = scope.Length;
            PdfObject next;
            Role nextRole;

            if (scope.Array is { } array)
            {
                if (scope.Next >= array.Count)
                {
                    Leave(ref scope);
                    continue;
                }

                path.Append(CultureInfo.InvariantCulture, $"[{scope.Next}]");
                next = array[scope.Next++];
                nextRole = scope.Role;
            }
            else if (scope.Entries.MoveNext())
            {
                var (key, entry) = scope.Entries.Current;

                if (IsSectionKey(key, scope.Role))
                {
                    continue;
                }

                FileQuote.AppendName(path, key);
                next = entry;
                nextRole = EntryRole(key, entry, scope.Role);
            }
            else
            {
                Leave(ref scope);
                continue;
            }

            if (Matches(next, nextRole, cut, out target))
            {
                LeaveAll();
                return true;
            }

            // Last: entering a container may move the scopes.
            Open(next, nextRole, path.Length);
        }

        return false;
    }

    /// <summary>
    /// Determines whether <paramref name="value"/>, with <paramref name="role"/>, is a reference to an object the file
    /// lacks that the walk counted — or, <paramref name="cut"/>, to a chain of references that reaches no value —, and gives
    /// the object it names.
    /// </summary>
    private bool Matches(PdfObject value, Role role, bool cut, out PdfObjectId target)
    {
        if (value is PdfReference reference && (role & Role.Uncounted) == 0 &&
            (cut
                ? !NamesAnotherGeneration(reference.Id, out _) && _chains?.TryGetValue(reference.Id.Number, out var chain) == true && chain
                : _missing.Contains(reference.Id.Number) || NamesAnotherGeneration(reference.Id, out _)))
        {
            target = reference.Id;
            return true;
        }

        target = default;
        return false;
    }

    /// <summary>
    /// Finds, in file order, the first name with a null character inside <paramref name="value"/>, where it lies, and
    /// whether it is a key of the dictionary <paramref name="path"/> leads to.
    /// </summary>
    /// <param name="value">The value of an object, or the trailer.</param>
    /// <param name="role"><see cref="Role.Trailer"/> for the trailer, whose <c>/Prev</c> and <c>/XRefStm</c> name sections.</param>
    /// <param name="path">Where the path to the name is written.</param>
    /// <param name="isKey">Whether the name is a key.</param>
    /// <remarks>It reads <paramref name="value"/> in a loop, as <see cref="TryFind"/> does, and for the same reason.</remarks>
    private PdfName? FindNullCharacter(PdfObject value, Role role, StringBuilder path, out bool isKey)
    {
        isKey = false;

        if (value is PdfName name && HasNullCharacter(name))
        {
            return name;
        }

        Open(value, role, path.Length);

        while (_depth > 0)
        {
            ref var scope = ref _scopes[_depth - 1];
            path.Length = scope.Length;
            PdfObject next;

            if (scope.Array is { } array)
            {
                if (scope.Next >= array.Count)
                {
                    Leave(ref scope);
                    continue;
                }

                path.Append(CultureInfo.InvariantCulture, $"[{scope.Next}]");
                next = array[scope.Next++];
            }
            else if (scope.Entries.MoveNext())
            {
                var (key, entry) = scope.Entries.Current;

                if (HasNullCharacter(key))
                {
                    isKey = true;
                    LeaveAll();
                    return key;
                }

                if (IsSectionKey(key, scope.Role))
                {
                    continue;
                }

                FileQuote.AppendName(path, key);
                next = entry;
            }
            else
            {
                Leave(ref scope);
                continue;
            }

            if (next is PdfName found && HasNullCharacter(found))
            {
                LeaveAll();
                return found;
            }

            // Last: entering a container may move the scopes. The trailer's own keys alone name sections, not those of a
            // dictionary written in it.
            Open(next, Role.None, path.Length);
        }

        return null;
    }

    private PdfObject ValueOf(int owner) =>
        owner == 0 ? _document.Trailer : _reader.GetIndexedObject(owner);

    /// <summary>
    /// Determines whether <paramref name="id"/> names a generation other than <paramref name="carried"/>, the one the object
    /// of its number carries, read by the walk.
    /// </summary>
    private bool NamesAnotherGeneration(PdfObjectId id, out int carried) =>
        _reader.TryGetCarriedGeneration(id.Number, out carried) && carried != id.Generation;

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

    /// <summary>What references met to an object claimed and not read yet leave to tell once it is read.</summary>
    private sealed class Waiting
    {
        /// <summary>
        /// Gets or sets how many references under the claim's generation each object that holds them counts — references
        /// another rule judges are not counted —; null while none is kept, before the first and once they are told.
        /// </summary>
        public Dictionary<int, int>? Counted { get; set; }

        /// <summary>
        /// Gets or sets the first reference met that names the generation the object carries where the claim does not — the
        /// object that holds it and its role —; null until one does.
        /// </summary>
        public (int Owner, Role Role)? Named { get; set; }
    }

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

        /// <summary>
        /// The length of the path that leads to the container, which a search cuts back to before it writes the step to each
        /// of its values; 0 for the walk, which writes no path.
        /// </summary>
        public int Length;
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
