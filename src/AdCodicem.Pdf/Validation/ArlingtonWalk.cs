using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation.Arlington;

namespace AdCodicem.Pdf.Validation;

/// <summary>
/// The objects reachable from the trailer, each given its type in the Arlington PDF Model and checked against it
/// once: the keys the model requires, the types of the values, the values of <c>/Type</c> and <c>/Subtype</c>, and
/// the keys the file's version deprecates.
/// </summary>
/// <remarks>
/// <para>
/// The walk is breadth-first, from the trailer, along the model's <c>Link</c> column, with an explicit queue: nothing
/// recurses. The trailer is never checked — its entries are the <c>file.*</c> rules' —, and only the entries that lead
/// into the document are followed: <c>/Encrypt</c>, <c>/Info</c> and <c>/Root</c>, not the arrays a trailer or a
/// cross-reference stream holds of its own, nor <c>/Prev</c> or <c>/XRefStm</c>. A dictionary's keys are taken in
/// ordinal order, an array's elements in order, so that two validations of a file give the same findings in the same
/// order.
/// </para>
/// <para>
/// A value the model links to several objects is typed by the candidates of its kind (the arrays for an array, the
/// others for a dictionary or a stream): the discriminator plan the generator worked out decides on an exact plain
/// value, and otherwise the candidates are scored, a tie leaving the value unchecked. An indirect object is typed by
/// the first context that types it, and checked once; one a context leaves untyped may still be typed by a later
/// one; a later context that disagrees is not heard. Nothing is typed through a key that points back up the graph
/// (a <c>/Parent</c>, whatever row allows it, the <c>/P</c> of a structure element or an annotation, an outline item's
/// <c>/Prev</c>): such a key is checked, and the object it names left to the context that reaches it top down.
/// </para>
/// <para>
/// A reference to an object the file lacks, or that the reader could not produce — an object stream an encryption
/// hides, an object cut at a limit —, counts as present and is not checked: those are
/// <see cref="PdfValidationRuleIds.ObjectReferenceMissing"/>'s and the cross-reference rules' faults. In a
/// dictionary, <c>null</c> is absence (ISO 32000-1, 7.3.7); in an array, only a <c>null</c> written there is checked.
/// Nothing is judged of an object a reader limit cut, nor of what is written inside it: what it lacks may lie past the
/// limit, and an ancestor so cut gives any key an object would inherit from it.
/// </para>
/// <para>
/// Memory follows what is walked: a byte per object number met, a queue of the objects still to check, and a step per
/// value written inside another object that is checked. What is wrong is tallied per rule, per row of the model — a
/// type and a key, or the wildcard that stands for every key the type does not name —: the first occurrence and how
/// many objects break the row, however many of their keys or elements do, and messages are written once the walk is
/// over.
/// </para>
/// <para>
/// Work is bounded by the file whatever it holds: an object is typed once, and weighed once per set of candidates it
/// ties on; an indirect node of a name or number tree, and an indirect array of its <c>/Kids</c>, <c>/Names</c> or
/// <c>/Nums</c>, is expanded once, and a node written inside another lies in one object walked once; the ancestors an
/// inherited key is looked for in are remembered per object and key, so that a long chain of <c>/Parent</c> is walked
/// once.
/// </para>
/// </remarks>
internal sealed class ArlingtonWalk
{
    /// <summary>The scores of the study's section 3.1, which the prototype measured on the corpus.</summary>
    private const int DiscriminatorMatch = 80;
    private const int DiscriminatorMismatch = -40;
    private const int FirstElementMatch = 60;
    private const int FirstElementMismatch = -20;
    private const int ValueMatch = 4;
    private const int ValueMismatch = -3;
    private const int RequiredMissing = -6;

    /// <summary>The flag of <see cref="_numbers"/> that marks an object already typed, and so checked or queued.</summary>
    private const byte Typed = 0x80;

    private static readonly PdfName AcroForm = PdfName.Get("AcroForm");
    private static readonly PdfName DefaultAppearance = PdfName.Get("DA");
    private static readonly PdfName Nums = PdfName.Get("Nums");

    /// <summary>The trailer's entries that lead into the document, in ordinal order: the rest are the trailer's own.</summary>
    private static readonly PdfName[] Followed = [PdfName.Encrypt, PdfName.Info, PdfName.Root];
    private static readonly Comparison<KeyValuePair<PdfName, PdfObject>> ByKey =
        static (left, right) => string.CompareOrdinal(left.Key.Value, right.Key.Value);

    private readonly PdfDocument _document;
    private readonly PdfFileReader _reader;
    private readonly PageTreeWalk _pages;
    private readonly byte _applicable;
    private readonly Queue<Work> _queue = new();
    private readonly Dictionary<int, byte> _numbers = [];
    private readonly List<Step> _steps = [];
    private readonly Dictionary<int, int> _tallyIndex = [];
    private readonly List<Tally> _tallies = [];
    private readonly Dictionary<long, bool> _inherits = [];
    private readonly HashSet<int> _chain = [];
    private readonly List<int> _chainNumbers = [];
    private readonly HashSet<int> _treeNodes = [];
    private readonly HashSet<long> _ties = [];
    private readonly Stack<TreeNode> _tree = new();
    private KeyValuePair<PdfName, PdfObject>[] _entries = [];
    private KeyValuePair<PdfName, PdfObject>[] _candidateEntries = [];
    private bool _acroFormDefaultAppearance;

    private ArlingtonWalk(PdfDocument document, PageTreeWalk pages, byte version)
    {
        _document = document;
        _reader = document.Reader;
        _pages = pages;
        Version = version;
        _applicable = version == ArlingtonVersion.None ? (byte)20 : version;
    }

    /// <summary>Gets the keys the model requires that are absent, one finding per type and key.</summary>
    public List<ProbeFinding> KeysMissing { get; } = [];

    /// <summary>Gets the values of a type the model does not allow, one finding per type and key.</summary>
    public List<ProbeFinding> ValueTypesWrong { get; } = [];

    /// <summary>Gets the <c>/Type</c> and <c>/Subtype</c> values the model does not list, one finding per type and key.</summary>
    public List<ProbeFinding> TypeValuesWrong { get; } = [];

    /// <summary>Gets the keys the file's version deprecates, one finding per type and key.</summary>
    public List<ProbeFinding> KeysDeprecated { get; } = [];

    /// <summary>
    /// Gets the version the file declares, as <see cref="ArlingtonVersion"/> codes it, or
    /// <see cref="ArlingtonVersion.None"/> when it declares none the walk can use.
    /// </summary>
    public byte Version { get; }

    /// <summary>Gets how many objects, indirect or written inside another, were checked.</summary>
    public int Checked { get; private set; }

    /// <summary>Gets how many values the discriminator plan typed among several candidates.</summary>
    public int ChosenByPlan { get; private set; }

    /// <summary>Gets how many values scoring typed among several candidates.</summary>
    public int ChosenByScore { get; private set; }

    /// <summary>
    /// Gets how many values were left untyped because two candidates scored alike: an object once for the candidates
    /// it tied on, however many contexts link them.
    /// </summary>
    public int Ties { get; private set; }

    /// <summary>Walks the objects of <paramref name="document"/> reachable from its trailer, and checks them.</summary>
    public static ArlingtonWalk Run(PdfDocument document, PageTreeWalk pages)
    {
        var walk = new ArlingtonWalk(document, pages, DeclaredVersion(document));
        walk.Walk();
        walk.Describe();
        return walk;
    }

    /// <summary>
    /// Works out the version the file declares: its header's, or its catalog's <c>/Version</c> when that is later
    /// (ISO 32000-1, 7.2.2 and 7.7.2), never rounded; none when the header names none of the nine versions of PDF.
    /// </summary>
    /// <remarks>
    /// <see cref="PdfDocument.Version"/> is not used: it says 1.4 of a file without a header. The header is the one the
    /// reader recorded as it opened the file, which the <c>file.header-*</c> rules judge. A catalog's <c>/Version</c>
    /// only says the file is later than its header (7.7.2): without a header's version to be later than, the file
    /// declares none these rules rely on, whatever its catalog says.
    /// </remarks>
    internal static byte DeclaredVersion(PdfDocument document)
    {
        var structure = document.Reader.Structure;

        if (structure.HeaderPosition < 0 ||
            structure.HeaderVersion is not { } header ||
            !ArlingtonVersion.TryParse(header, out var version))
        {
            return ArlingtonVersion.None;
        }

        if (document.Catalog?.GetName(PdfName.Version) is { } name &&
            ArlingtonVersion.TryParse(name.Value, out var catalog) &&
            catalog > version)
        {
            version = catalog;
        }

        return version;
    }

    private void Walk()
    {
        var catalog = _document.Catalog;

        if (catalog?.GetRaw(AcroForm)?.Resolve() is PdfDictionary form && form.GetRaw(DefaultAppearance) is { } appearance)
        {
            _acroFormDefaultAppearance = Classify(appearance, out _, out _) != ValueClass.Null;
        }

        FollowTrailer();

        while (_queue.TryDequeue(out var work))
        {
            if (work.Type.Index is ArlingtonModel.XRefStream or ArlingtonModel.LinearizationParameterDict)
            {
                // The index's and the linearization's own: the file family's to judge.
                continue;
            }

            if (_reader.IsCutAtLimit(work.Holder.Number))
            {
                // Read only as far as one of the reader's limits let it be, or written inside an object that was: what
                // it lacks may lie past the limit.
                continue;
            }

            var value = work.Direct ?? _reader.GetObject(work.Holder);

            switch (value)
            {
                case PdfArray array:
                    Checked++;
                    CheckArray(work, array);
                    break;

                case PdfStream stream:
                    Checked++;
                    CheckDictionary(work, stream.Dictionary);
                    break;

                case PdfDictionary dictionary:
                    Checked++;
                    CheckDictionary(work, dictionary);
                    break;
            }
        }
    }

    /// <summary>
    /// Follows the trailer's links into the document, as <c>FileTrailer</c> or <c>XRefStream</c> gives them, without
    /// checking it: its <c>/ID</c>, a cross-reference stream's <c>/W</c>, <c>/Index</c> or <c>/DecodeParms</c>, are the
    /// file family's to judge.
    /// </summary>
    private void FollowTrailer()
    {
        var trailer = _document.Trailer;
        var type = ArlingtonModel.GetObject(
            trailer.GetRaw(PdfName.Type) is PdfName { Value: "XRef" } ? ArlingtonModel.XRefStream : ArlingtonModel.FileTrailer);

        foreach (var key in Followed)
        {
            if (trailer.GetRaw(key) is not { } raw || !type.TryFindRow(key.Value, out var row))
            {
                continue;
            }

            if (key == PdfName.Root && !LeadsToCatalog(raw))
            {
                // A /Root that leads to no catalog the reader could use is file.root-invalid's: nothing to type there.
                continue;
            }

            var kind = Classify(raw, out var resolved, out var number);

            if (row.TryMatch(Accepting(kind), out var slot) && row.TryGetLink(slot, out var link) && IsContainer(kind))
            {
                Discover(raw, resolved, kind, number, link, holder: default, parentStep: -1, key, element: -1);
            }
        }
    }

    /// <summary>Whether the trailer's <c>/Root</c> leads to a dictionary the reader takes for a catalog: <c>/Type /Catalog</c>, or a <c>/Pages</c>.</summary>
    private bool LeadsToCatalog(PdfObject raw)
    {
        var root = raw is PdfReference reference ? _reader.GetObject(reference.Id) : raw;
        return root is PdfDictionary dictionary &&
            (dictionary.GetRaw(PdfName.Type)?.Resolve() is PdfName { Value: "Catalog" } || dictionary.GetRaw(PdfName.Pages) is not null);
    }

    private void CheckDictionary(in Work work, PdfDictionary dictionary)
    {
        var type = work.Type;
        var rows = type.RowCount;
        var count = Fill(dictionary, ref _entries);
        var hasWildcard = type.TryGetWildcard(out var wildcard);
        var present = 0UL;
        var position = 0;

        for (var index = 0; index < count; index++)
        {
            var (key, raw) = _entries[index];

            while (position < rows && ArlingtonModel.Compare(type.GetRow(position).Key.Bytes, key.Value) < 0)
            {
                position++;
            }

            var exact = position < rows && ArlingtonModel.Compare(type.GetRow(position).Key.Bytes, key.Value) == 0;
            var kind = Classify(raw, out var resolved, out var number);

            if (kind == ValueClass.Null)
            {
                // null in a dictionary is absence (ISO 32000-1, 7.3.7).
                continue;
            }

            if (exact)
            {
                present |= 1UL << position;
            }

            if (kind == ValueClass.Unknown)
            {
                continue;
            }

            if (exact)
            {
                CheckValue(work, type.GetRow(position), raw, resolved, kind, number, key, element: -1);
            }
            else if (hasWildcard)
            {
                CheckValue(work, wildcard, raw, resolved, kind, number, key, element: -1);
            }
        }

        for (position = 0; position < rows; position++)
        {
            if ((present & (1UL << position)) != 0)
            {
                continue;
            }

            var row = type.GetRow(position);

            if (!IsRequired(row) ||
                Silenced(work, row, ArlingtonOverride.KeyMissingSilent) ||
                (row.IsInheritable && Inherits(dictionary, row.Key)) ||
                ((row.Overrides & ArlingtonOverride.InheritsAcroFormDefaultAppearance) != 0 && _acroFormDefaultAppearance))
            {
                continue;
            }

            Record(Rule.KeyMissing, row, work, key: null, element: -1, detail: null, length: -1);
        }

        Array.Clear(_entries, 0, count);
    }

    private void CheckArray(in Work work, PdfArray array)
    {
        var type = work.Type;
        var length = array.Count;
        var fixedCount = type.FixedElementCount;
        var repeating = type.RepeatingCount;
        var repeatFrom = repeating > 0 ? type.GetRow(fixedCount).ElementIndex : int.MaxValue;
        var hasWildcard = type.TryGetWildcard(out var wildcard);

        for (var position = length; position < fixedCount; position++)
        {
            var row = type.GetRow(position);

            if (IsRequired(row) && !Silenced(work, row, ArlingtonOverride.KeyMissingSilent))
            {
                Record(Rule.KeyMissing, row, work, key: null, element: position, detail: null, length: length);
            }
        }

        var member = 0;

        for (var index = 0; index < length; index++)
        {
            var raw = array[index];
            var kind = Classify(raw, out var resolved, out var number);

            if (index < fixedCount)
            {
                CheckElement(work, type.GetRow(index), raw, resolved, kind, number, index);
                continue;
            }

            if (repeating > 0 && index >= repeatFrom)
            {
                // A member of the repeating group that is optional and absent is skipped: the element is the next
                // member's (the study's section 1.3, where counting modulo the group's size misread Chromium's arrays).
                var matched = false;

                for (var tries = 0; tries < repeating; tries++)
                {
                    var candidate = type.GetRow(fixedCount + member);

                    if (kind == ValueClass.Unknown || (kind == ValueClass.Null && number != 0) || candidate.TryMatch(Accepting(kind), out _))
                    {
                        CheckElement(work, candidate, raw, resolved, kind, number, index);
                        member = (member + 1) % repeating;
                        matched = true;
                        break;
                    }

                    if (candidate.Requirement == ArlingtonRequirement.Yes)
                    {
                        break;
                    }

                    member = (member + 1) % repeating;
                }

                if (!matched)
                {
                    CheckElement(work, type.GetRow(fixedCount + member), raw, resolved, kind, number, index);
                    member = (member + 1) % repeating;
                }

                continue;
            }

            if (hasWildcard)
            {
                CheckElement(work, wildcard, raw, resolved, kind, number, index);
            }
        }
    }

    /// <summary>
    /// Checks an element of an array: one that names nothing the reader could produce is not checked, and neither is
    /// a reference to a <c>null</c> object, whose fault — if any — lies with the object (the prototype's policy 5).
    /// </summary>
    private void CheckElement(in Work work, ArlingtonRow row, PdfObject raw, PdfObject? resolved, ValueClass kind, int number, int index)
    {
        if (kind == ValueClass.Unknown || (kind == ValueClass.Null && number != 0))
        {
            return;
        }

        CheckValue(work, row, raw, resolved, kind, number, key: null, element: index);
    }

    /// <summary>
    /// Checks one present value against its row — deprecation, type, <c>/Type</c> or <c>/Subtype</c> value — then
    /// follows the row's link for the type the value was read as.
    /// </summary>
    private void CheckValue(
        in Work work, ArlingtonRow row, PdfObject raw, PdfObject? resolved, ValueClass kind, int number, PdfName? key, int element)
    {
        if (Version != ArlingtonVersion.None && row.IsDeprecatedIn(Version) && !Silenced(work, row, ArlingtonOverride.KeyDeprecatedSilent))
        {
            Record(Rule.KeyDeprecated, row, work, key, element, detail: null, length: -1);
        }

        if (!row.TryMatch(Accepting(kind), out var slot))
        {
            if (!Silenced(work, row, ArlingtonOverride.ValueTypeWrongSilent))
            {
                Record(Rule.ValueTypeWrong, row, work, key, element, detail: resolved ?? Resolve(raw), length: -1);
            }

            return;
        }

        if (key is not null &&
            kind == ValueClass.Name &&
            row.IsDiscriminator &&
            !Silenced(work, row, ArlingtonOverride.TypeValueWrongSilent) &&
            (row.Key.Is("Type") || row.Key.Is("Subtype")) &&
            row.HasValuesFor(slot) &&
            (resolved ?? Resolve(raw)) is PdfName name &&
            !row.HasValue(slot, name.Value))
        {
            Record(Rule.TypeValueWrong, row, work, key, element, detail: name, length: -1);
        }

        // A /Parent points back up the graph whatever row describes it: one a wildcard row allows is no less a back-link.
        if (!IsContainer(kind) || row.IsBackLink || key == PdfName.Parent || !row.TryGetLink(slot, out var link))
        {
            return;
        }

        if (slot is ArlingtonType.NameTree or ArlingtonType.NumberTree)
        {
            WalkTree(raw, work.Holder, work.Step, key, element, slot == ArlingtonType.NameTree, link);
            return;
        }

        Discover(raw, resolved, kind, number, link, work.Holder, work.Step, key, element);
    }

    /// <summary>
    /// Types a value by the link's candidates and queues it to be checked, unless it is an object already typed, or
    /// no candidate is of its kind, or two candidates score alike.
    /// </summary>
    private void Discover(
        PdfObject raw, PdfObject? resolved, ValueClass kind, int number, ArlingtonLink link, PdfObjectId holder, int parentStep, PdfName? key, int element)
    {
        if (number != 0 && (_numbers[number] & Typed) != 0)
        {
            return;
        }

        // An object two candidates tied on ties again in every context that links the same candidates: remembering it
        // keeps an object many contexts name from being weighed as many times.
        var tie = number != 0 && link.IsCandidateSet ? ((long)number << 16) | (uint)link.CandidateSet : -1;

        if (tie >= 0 && _ties.Contains(tie))
        {
            return;
        }

        var value = resolved ?? Resolve(raw);
        var ties = Ties;

        if (!TryChoose(value, kind, link, out var type))
        {
            if (tie >= 0 && Ties != ties)
            {
                _ties.Add(tie);
            }

            return;
        }

        if (number != 0)
        {
            _numbers[number] |= Typed;
            _queue.Enqueue(new Work(type, ((PdfReference)raw).Id, Step: -1, Direct: null));
            return;
        }

        _queue.Enqueue(new Work(type, holder, AddStep(parentStep, key, element), value));
    }

    /// <summary>
    /// Walks a name tree or a number tree from its root, through <c>/Kids</c>, discovering the values of its
    /// <c>/Names</c> or <c>/Nums</c> with the row's link. The nodes are not checked.
    /// </summary>
    /// <remarks>
    /// An indirect node, or an indirect array of <c>/Kids</c>, <c>/Names</c> or <c>/Nums</c>, is expanded the first time
    /// any tree meets it, and never again; a node written inside another lies inside one indirect object, expanded once.
    /// However its <c>/Kids</c> loop or share arrays, the work is bounded by the file's objects. A node or an array a
    /// reader limit cut is not expanded, as an object so cut is not checked.
    /// </remarks>
    private void WalkTree(PdfObject root, PdfObjectId holder, int step, PdfName? key, int element, bool names, ArlingtonLink link)
    {
        if (!Enters(root))
        {
            return;
        }

        _tree.Push(new TreeNode(root, holder, step, key, element));

        while (_tree.TryPop(out var node))
        {
            var kind = Classify(node.Raw, out var resolved, out var number);

            if (kind is not (ValueClass.Dictionary or ValueClass.Stream) || IsCut(number))
            {
                continue;
            }

            if (DictionaryOf(resolved ?? Resolve(node.Raw)) is not { } dictionary)
            {
                continue;
            }

            var nodeHolder = node.Holder;
            int nodeStep;

            if (number != 0)
            {
                nodeHolder = ((PdfReference)node.Raw).Id;
                nodeStep = -1;
            }
            else
            {
                nodeStep = AddStep(node.ParentStep, node.Key, node.Element);
            }

            if (dictionary.GetRaw(PdfName.Kids) is { } kidsValue &&
                Enters(kidsValue) &&
                Classify(kidsValue, out var kidsResolved, out var kidsNumber) == ValueClass.Array &&
                !IsCut(kidsNumber) &&
                (kidsResolved ?? Resolve(kidsValue)) is PdfArray kids)
            {
                var (kidsHolder, kidsStep, kidsKey) = Within(kidsValue, nodeHolder, nodeStep, PdfName.Kids);

                for (var index = kids.Count - 1; index >= 0; index--)
                {
                    var kid = kids[index];

                    if (Enters(kid))
                    {
                        _tree.Push(new TreeNode(kid, kidsHolder, kidsStep, kidsKey, index));
                    }
                }
            }

            var leavesKey = names ? PdfName.Names : Nums;

            if (dictionary.GetRaw(leavesKey) is { } leavesValue &&
                Enters(leavesValue) &&
                Classify(leavesValue, out var leavesResolved, out var leavesNumber) == ValueClass.Array &&
                !IsCut(leavesNumber) &&
                (leavesResolved ?? Resolve(leavesValue)) is PdfArray leaves)
            {
                var (leavesHolder, leavesStep, leavesName) = Within(leavesValue, nodeHolder, nodeStep, leavesKey);

                for (var index = 1; index < leaves.Count; index += 2)
                {
                    var leaf = leaves[index];
                    var leafKind = Classify(leaf, out var leafResolved, out var leafNumber);

                    if (IsContainer(leafKind))
                    {
                        Discover(leaf, leafResolved, leafKind, leafNumber, link, leavesHolder, leavesStep, leavesName, index);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Whether a tree's node or array is to be expanded: one written inside another object always, for it lies in an
    /// object expanded once; an indirect one the first time any tree meets it, which is remembered.
    /// </summary>
    private bool Enters(PdfObject value) => value is not PdfReference reference || _treeNodes.Add(reference.Id.Number);

    /// <summary>Whether an object, which <see cref="Classify"/> has resolved, was cut by a reader limit; never for a direct value.</summary>
    private bool IsCut(int number) => number != 0 && _reader.IsCutAtLimit(number);

    /// <summary>
    /// Says where the elements of a tree node's array lie: in the array's own object when it is referred to, under the
    /// node's key otherwise.
    /// </summary>
    private static (PdfObjectId Holder, int Step, PdfName? Key) Within(PdfObject array, PdfObjectId holder, int step, PdfName key) =>
        array is PdfReference reference ? (reference.Id, -1, null) : (holder, step, key);

    /// <summary>
    /// Chooses among the link's candidates of the value's kind: the only one; else the one the discriminator plan
    /// names by an exact plain value; else the one that scores best, none when two score alike.
    /// </summary>
    private bool TryChoose(PdfObject value, ValueClass kind, ArlingtonLink link, out ArlingtonObject chosen)
    {
        var isArray = kind == ValueClass.Array;
        var total = link.CandidateCount;
        var ofKind = 0;
        chosen = default;

        for (var index = 0; index < total; index++)
        {
            var candidate = link.GetCandidate(index);

            if (candidate.IsArray == isArray)
            {
                ofKind++;
                chosen = candidate;
            }
        }

        if (ofKind <= 1)
        {
            return ofKind == 1;
        }

        var array = value as PdfArray;
        var dictionary = array is null ? DictionaryOf(value) : null;

        if (array is null && dictionary is null)
        {
            chosen = default;
            return false;
        }

        var count = dictionary is null ? 0 : Fill(dictionary, ref _candidateEntries);

        try
        {
            if (link.TryGetPlan(isArray, out var planKey) && PlanValue(array, count, planKey) is { } plain)
            {
                var hits = 0;

                for (var index = 0; index < total; index++)
                {
                    var candidate = link.GetCandidate(index);

                    if (candidate.IsArray == isArray && TryFindRow(candidate, planKey, out var row) && row.HasValueOfAnyType(plain))
                    {
                        hits++;
                        chosen = candidate;
                    }
                }

                if (hits == 1)
                {
                    ChosenByPlan++;
                    return true;
                }
            }

            var best = int.MinValue;
            var tie = false;

            for (var index = 0; index < total; index++)
            {
                var candidate = link.GetCandidate(index);

                if (candidate.IsArray != isArray)
                {
                    continue;
                }

                var score = array is null ? Score(count, candidate) : Score(array, candidate);

                if (score > best)
                {
                    best = score;
                    chosen = candidate;
                    tie = false;
                }
                else if (score == best)
                {
                    tie = true;
                }
            }

            if (tie)
            {
                Ties++;
                chosen = default;
                return false;
            }

            ChosenByScore++;
            return true;
        }
        finally
        {
            Array.Clear(_candidateEntries, 0, count);
        }
    }

    /// <summary>The value a discriminator plan reads: element 0 or 1 of an array, or a key of a dictionary; null when it has none.</summary>
    private PdfObject? PlanValue(PdfArray? array, int count, ArlingtonName key)
    {
        PdfObject? raw = null;

        if (array is not null)
        {
            if (key.TryGetIndex(out var index) && index < array.Count)
            {
                raw = array[index];
            }
        }
        else
        {
            for (var entry = 0; entry < count; entry++)
            {
                if (ArlingtonModel.Compare(key.Bytes, _candidateEntries[entry].Key.Value) == 0)
                {
                    raw = _candidateEntries[entry].Value;
                    break;
                }
            }
        }

        if (raw is null)
        {
            return null;
        }

        var kind = Classify(raw, out var resolved, out _);
        return kind is ValueClass.Null or ValueClass.Unknown ? null : resolved ?? Resolve(raw);
    }

    /// <summary>Scores a dictionary, whose entries <see cref="_candidateEntries"/> holds sorted, as <paramref name="candidate"/>.</summary>
    private int Score(int count, ArlingtonObject candidate)
    {
        var score = 0;
        var rows = candidate.RowCount;
        var entry = 0;

        for (var position = 0; position < rows; position++)
        {
            var row = candidate.GetRow(position);
            var name = row.Key.Bytes;

            while (entry < count && ArlingtonModel.Compare(name, _candidateEntries[entry].Key.Value) > 0)
            {
                entry++;
            }

            if (row.IsWildcard || row.IsRepeating)
            {
                continue;
            }

            var kind = ValueClass.Null;
            PdfObject? raw = null;
            PdfObject? resolved = null;

            if (entry < count && ArlingtonModel.Compare(name, _candidateEntries[entry].Key.Value) == 0)
            {
                raw = _candidateEntries[entry].Value;
                kind = Classify(raw, out resolved, out _);
            }

            if (kind == ValueClass.Null)
            {
                // "Unconditional": required whatever the version, for an ISO key. A test on the version would favor
                // candidates newer than the file, whose keys it does not require (the prototype's choice 2).
                if (row.IsUnconditionallyRequired && !row.IsInheritable)
                {
                    score += RequiredMissing;
                }

                continue;
            }

            if (kind == ValueClass.Unknown || row.ValueCount == 0)
            {
                continue;
            }

            var match = Matches(row, raw!, resolved, kind);
            score += row.IsDiscriminator
                ? (match ? DiscriminatorMatch : DiscriminatorMismatch)
                : (match ? ValueMatch : ValueMismatch);
        }

        return score;
    }

    /// <summary>Scores an array as <paramref name="candidate"/>, by its fixed elements.</summary>
    private int Score(PdfArray array, ArlingtonObject candidate)
    {
        var score = 0;
        var fixedCount = candidate.FixedElementCount;

        for (var index = 0; index < fixedCount; index++)
        {
            var row = candidate.GetRow(index);

            if (index >= array.Count)
            {
                if (row.IsUnconditionallyRequired)
                {
                    score += RequiredMissing;
                }

                continue;
            }

            if (row.ValueCount == 0)
            {
                continue;
            }

            var raw = array[index];
            var kind = Classify(raw, out var resolved, out _);
            var match = Matches(row, raw, resolved, kind);

            score += index == 0
                ? (match ? FirstElementMatch : FirstElementMismatch)
                : (match ? ValueMatch : ValueMismatch);
        }

        return score;
    }

    /// <summary>Whether a value is one of the plain values its row lists for the type the value is read as.</summary>
    private bool Matches(ArlingtonRow row, PdfObject raw, PdfObject? resolved, ValueClass kind) =>
        kind is ValueClass.Name or ValueClass.Integer or ValueClass.Boolean &&
        row.TryMatch(Accepting(kind), out var slot) &&
        row.HasValue(slot, resolved ?? Resolve(raw));

    /// <summary>
    /// Whether an ancestor of <paramref name="dictionary"/>, through <c>/Parent</c>, gives <paramref name="key"/>.
    /// </summary>
    /// <remarks>
    /// The chain stops at an object met before on it, so a loop of parents ends it; and what each object's ancestors
    /// give is remembered, so that many objects under one long chain walk it once. An ancestor a reader limit cut gives
    /// the key: it may lie past the limit.
    /// </remarks>
    private bool Inherits(PdfDictionary dictionary, ArlingtonName key)
    {
        _chain.Clear();
        _chainNumbers.Clear();
        var found = false;
        var parent = dictionary.GetRaw(PdfName.Parent);

        while (parent is not null)
        {
            var kind = Classify(parent, out var resolved, out var number);

            if (kind is not (ValueClass.Dictionary or ValueClass.Stream))
            {
                break;
            }

            if (number != 0)
            {
                if (_inherits.TryGetValue(Memo(number, key), out var known))
                {
                    found = known;
                    break;
                }

                if (!_chain.Add(number))
                {
                    break;
                }

                _chainNumbers.Add(number);

                if (_reader.IsCutAtLimit(number))
                {
                    found = true;
                    break;
                }
            }

            // An ancestor a rebuild of the index turned into something else says nothing of the key: as one a limit
            // cut, it is taken to give it.
            if (DictionaryOf(resolved ?? Resolve(parent)) is not { } ancestor)
            {
                found = true;
                break;
            }

            if (Find(ancestor, key) is { } given && Classify(given, out _, out _) != ValueClass.Null)
            {
                found = true;
                break;
            }

            parent = ancestor.GetRaw(PdfName.Parent);
        }

        foreach (var number in _chainNumbers)
        {
            _inherits[Memo(number, key)] = found;
        }

        return found;

        static long Memo(int number, ArlingtonName key) => ((long)number << 16) | (uint)key.Id;
    }

    /// <summary>The value of the key named <paramref name="key"/>, found by its text, without making a name of it.</summary>
    private static PdfObject? Find(PdfDictionary dictionary, ArlingtonName key)
    {
        var bytes = key.Bytes;

        foreach (var (name, value) in dictionary)
        {
            if (ArlingtonModel.Compare(bytes, name.Value) == 0)
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryFindRow(ArlingtonObject type, ArlingtonName key, out ArlingtonRow row)
    {
        var rows = type.RowCount;

        for (var position = 0; position < rows; position++)
        {
            row = type.GetRow(position);

            if (row.Key == key)
            {
                return true;
            }
        }

        row = default;
        return false;
    }

    /// <summary>
    /// Whether a rule stays silent on a row of the object checked, as a "covered by" row of <c>overrides.tsv</c> asks:
    /// wherever the rule that covers it reports the fault. A page tree rule reports it only on what the page tree's walk
    /// judged, as the model types it: a page the tree lists, a node, one of their arrays of kids, and a <c>/Parent</c>
    /// only where it could compare it with the node listing the object. A page the tree does not list, which a
    /// destination names, is the generated rules' to judge. A page or a node written inside another object is taken
    /// for a kid the tree lists, whose <c>/Parent</c> names no node.
    /// </summary>
    private bool Silenced(in Work work, ArlingtonRow row, ArlingtonOverride silence)
    {
        var overrides = row.Overrides;

        if ((overrides & silence) == 0)
        {
            return false;
        }

        if ((overrides & ArlingtonOverride.SilentWherePageTreeJudges) == 0)
        {
            return true;
        }

        var parent = row.Key.Is("Parent");
        var number = work.Holder.Number;

        if (work.Direct is not null)
        {
            return !parent;
        }

        if (parent)
        {
            return _pages.JudgedParent(number);
        }

        return row.Object.Index switch
        {
            ArlingtonModel.PageObject => _pages.PageIndexes.ContainsKey(number),
            ArlingtonModel.PageTreeNode or ArlingtonModel.PageTreeNodeRoot => _pages.Nodes.Contains(number),
            _ => _pages.KidsArrays.Contains(number),
        };
    }

    /// <summary>
    /// Whether the model requires the row's key of a file of this version: <c>TRUE</c> from its ISO version on (2.0
    /// when the version is unknown), never for an extension-only key; a version-only predicate only when the version
    /// is known.
    /// </summary>
    private bool IsRequired(ArlingtonRow row) =>
        !row.IsWildcard && !row.IsRepeating && row.Requirement switch
        {
            ArlingtonRequirement.Yes => !row.IsExtensionOnly && row.Since <= _applicable,
            ArlingtonRequirement.InVersions => Version != ArlingtonVersion.None && row.IsRequiredIn(Version),
            _ => false,
        };

    /// <summary>
    /// Says what a value is, resolving a reference once per object number: its class, <see cref="ValueClass.Null"/>
    /// for a <c>null</c> the file writes, <see cref="ValueClass.Unknown"/> for one the file lacks or the reader could
    /// not produce.
    /// </summary>
    /// <param name="raw">The value as its holder holds it.</param>
    /// <param name="resolved">The value resolved, when this call resolved it; null when it was known already.</param>
    /// <param name="number">The object number of a reference, or 0 for a direct value.</param>
    private ValueClass Classify(PdfObject raw, out PdfObject? resolved, out int number)
    {
        if (raw is not PdfReference reference)
        {
            number = 0;
            resolved = raw;
            return ClassOf(raw);
        }

        number = reference.Id.Number;

        if (_numbers.TryGetValue(number, out var known))
        {
            resolved = null;
            return (ValueClass)(known & ~Typed);
        }

        var value = _reader.GetObject(reference.Id);
        var kind = value is PdfNull
            ? (_reader.GetPresence(number) == ObjectPresence.Defined ? ValueClass.Null : ValueClass.Unknown)
            : ClassOf(value);

        _numbers[number] = (byte)kind;
        resolved = value;
        return kind;
    }

    private PdfObject Resolve(PdfObject raw) => raw is PdfReference reference ? _reader.GetObject(reference.Id) : raw;

    private static ValueClass ClassOf(PdfObject value) => value switch
    {
        PdfBoolean => ValueClass.Boolean,
        PdfInteger => ValueClass.Integer,
        PdfReal => ValueClass.Real,
        PdfName => ValueClass.Name,
        PdfString => ValueClass.String,
        PdfArray => ValueClass.Array,
        PdfStream => ValueClass.Stream,
        PdfDictionary => ValueClass.Dictionary,
        _ => ValueClass.Null,
    };

    /// <summary>The types a value of <paramref name="kind"/> can be: <see cref="ArlingtonModel.TypesAccepting"/>, by class.</summary>
    private static ArlingtonTypes Accepting(ValueClass kind) => kind switch
    {
        ValueClass.Boolean => ArlingtonTypes.Boolean,
        ValueClass.Integer => ArlingtonTypes.Integer | ArlingtonTypes.Bitmask | ArlingtonTypes.Number,
        ValueClass.Real => ArlingtonTypes.Number,
        ValueClass.Name => ArlingtonTypes.Name,
        ValueClass.String => ArlingtonTypes.String | ArlingtonTypes.StringAscii | ArlingtonTypes.StringByte | ArlingtonTypes.StringText | ArlingtonTypes.Date,
        ValueClass.Array => ArlingtonTypes.Array | ArlingtonTypes.Rectangle | ArlingtonTypes.Matrix,
        ValueClass.Stream => ArlingtonTypes.Stream,
        ValueClass.Dictionary => ArlingtonTypes.Dictionary | ArlingtonTypes.NameTree | ArlingtonTypes.NumberTree,
        ValueClass.Null => ArlingtonTypes.Null,
        _ => ArlingtonTypes.None,
    };

    private static bool IsContainer(ValueClass kind) => kind is ValueClass.Array or ValueClass.Dictionary or ValueClass.Stream;

    /// <summary>
    /// Gives the dictionary of <paramref name="value"/>, its own or a stream's, or null when a rebuild of the reader's
    /// index, after the walk first met the object as a dictionary, made it something else.
    /// </summary>
    private static PdfDictionary? DictionaryOf(PdfObject value) => value switch
    {
        PdfDictionary dictionary => dictionary,
        PdfStream stream => stream.Dictionary,
        _ => null,
    };

    /// <summary>Copies a dictionary's entries into <paramref name="buffer"/>, in ordinal order of their keys.</summary>
    private static int Fill(PdfDictionary dictionary, ref KeyValuePair<PdfName, PdfObject>[] buffer)
    {
        var count = dictionary.Count;

        if (buffer.Length < count)
        {
            buffer = new KeyValuePair<PdfName, PdfObject>[Math.Max(count, buffer.Length * 2)];
        }

        var index = 0;

        foreach (var entry in dictionary)
        {
            buffer[index++] = entry;
        }

        buffer.AsSpan(0, count).Sort(ByKey);
        return count;
    }

    private int AddStep(int parent, PdfName? key, int element)
    {
        _steps.Add(new Step(parent, key, element));
        return _steps.Count - 1;
    }

    private void Record(Rule rule, ArlingtonRow row, in Work work, PdfName? key, int element, PdfObject? detail, int length)
    {
        var id = ((int)rule << 16) | row.Index;

        if (_tallyIndex.TryGetValue(id, out var index))
        {
            // Objects are counted, not occurrences: a wildcard or a repeating group can break a row several times in
            // one object, whose occurrences are recorded one after the other, as it is checked.
            var tally = _tallies[index];

            if (tally.LastHolder != work.Holder || tally.LastStep != work.Step)
            {
                tally.Count++;
                tally.LastHolder = work.Holder;
                tally.LastStep = work.Step;
            }

            return;
        }

        _tallyIndex.Add(id, _tallies.Count);
        _tallies.Add(new Tally(rule, row, work.Holder, work.Step, key, element, detail, length));
    }

    /// <summary>Writes the findings, once the walk is over: one per rule, type and key, at its first occurrence.</summary>
    private void Describe()
    {
        foreach (var tally in _tallies)
        {
            var finding = new ProbeFinding(LocationOf(tally.Holder), ArlingtonText.Message(tally, Where(tally), Version));

            switch (tally.Rule)
            {
                case Rule.KeyMissing:
                    KeysMissing.Add(finding);
                    break;
                case Rule.ValueTypeWrong:
                    ValueTypesWrong.Add(finding);
                    break;
                case Rule.TypeValueWrong:
                    TypeValuesWrong.Add(finding);
                    break;
                default:
                    KeysDeprecated.Add(finding);
                    break;
            }
        }
    }

    /// <summary>Names where a tally's first occurrence lies: <c>object 12 0</c>, <c>the dictionary under /Resources of object 12 0</c>.</summary>
    private string Where(Tally tally)
    {
        var owner = tally.Holder.Number > 0
            ? string.Create(CultureInfo.InvariantCulture, $"object {tally.Holder.Number} {tally.Holder.Generation}")
            : "the trailer";

        if (tally.Step < 0)
        {
            return owner;
        }

        var path = new List<int>();

        for (var step = tally.Step; step >= 0; step = _steps[step].Parent)
        {
            path.Add(step);
        }

        var text = new StringBuilder();

        for (var index = path.Count - 1; index >= 0; index--)
        {
            var step = _steps[path[index]];

            if (step.Key is not null)
            {
                FileQuote.AppendName(text, step.Key);
            }

            if (step.Element >= 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"[{step.Element}]");
            }
        }

        var what = tally.Row.Object.IsArray ? "array" : "dictionary";

        // A value written in an array that is an object of its own lies at an index, not under a key.
        return text.Length > 0 && text[0] == '['
            ? $"the {what} at {RuleText.Path(text)} in {owner}"
            : $"the {what} under {RuleText.Path(text)} of {owner}";
    }

    private PdfValidationLocation LocationOf(PdfObjectId holder)
    {
        if (holder.Number > 0)
        {
            return _pages.Locate(holder);
        }

        var sections = _reader.Structure.Sections;
        return sections.Count > 0 && sections[0].TrailerLocation >= 0
            ? PdfValidationLocation.AtPosition(sections[0].TrailerLocation)
            : default;
    }

    /// <summary>The generated rules, in the order the profile runs them.</summary>
    internal enum Rule
    {
        KeyMissing,
        ValueTypeWrong,
        TypeValueWrong,
        KeyDeprecated,
    }

    /// <summary>What a value is, as the rules judge it: its class, or why it is not judged.</summary>
    internal enum ValueClass : byte
    {
        /// <summary>A <c>null</c> the file writes, directly or as an object.</summary>
        Null,

        /// <summary>A reference to an object the file lacks, or that the reader could not produce.</summary>
        Unknown,

        Boolean,
        Integer,
        Real,
        Name,
        String,
        Array,
        Dictionary,
        Stream,
    }

    /// <summary>A rule's findings on one row of the model: the first occurrence, and how many there were.</summary>
    internal sealed class Tally(
        Rule rule, ArlingtonRow row, PdfObjectId holder, int step, PdfName? key, int element, PdfObject? detail, int length)
    {
        public Rule Rule { get; } = rule;

        public ArlingtonRow Row { get; } = row;

        /// <summary>Gets the indirect object the first occurrence lies in; object 0 for the trailer.</summary>
        public PdfObjectId Holder { get; } = holder;

        /// <summary>Gets the step, in the walk's list, that leads from the holder to the value checked; -1 for the holder itself.</summary>
        public int Step { get; } = step;

        /// <summary>Gets the key of the first occurrence, as the file writes it; null for an array's element or a missing key.</summary>
        public PdfName? Key { get; } = key;

        /// <summary>Gets the element of the first occurrence, for an array; -1 otherwise.</summary>
        public int Element { get; } = element;

        /// <summary>Gets the value of the first occurrence, for the rules that say what it was.</summary>
        public PdfObject? Detail { get; } = detail;

        /// <summary>Gets the length of an array that lacks a required element; -1 otherwise.</summary>
        public int Length { get; } = length;

        /// <summary>Gets or sets how many objects break the row.</summary>
        public int Count { get; set; } = 1;

        /// <summary>Gets or sets the holder of the last object counted.</summary>
        public PdfObjectId LastHolder { get; set; } = holder;

        /// <summary>Gets or sets the step of the last object counted, which with its holder tells it from any other.</summary>
        public int LastStep { get; set; } = step;
    }

    /// <summary>
    /// An object to check: its type, the indirect object that holds it (object 0 for the trailer), and — for a value
    /// written inside another object — the value and the step that leads to it.
    /// </summary>
    private readonly record struct Work(ArlingtonObject Type, PdfObjectId Holder, int Step, PdfObject? Direct);

    /// <summary>A step from an object to a value written inside it: a key, an element, or a key's element.</summary>
    private readonly record struct Step(int Parent, PdfName? Key, int Element);

    /// <summary>A node of a name or number tree still to walk, and where it lies.</summary>
    private readonly record struct TreeNode(PdfObject Raw, PdfObjectId Holder, int ParentStep, PdfName? Key, int Element);
}
