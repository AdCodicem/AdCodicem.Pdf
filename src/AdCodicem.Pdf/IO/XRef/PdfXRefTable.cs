using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO.XRef;

/// <summary>
/// The index of a document: where every object is, and the trailer that names the roots.
/// </summary>
/// <remarks>
/// <para>
/// The reader keeps one for the whole document. Once it has changed the one the chain gave — an object found away
/// from its entry, or a rebuild —, it also keeps a copy of that one as it was, which
/// <see cref="PdfFileReader.ChainIndex"/> serves.
/// </para>
/// <para>
/// The entries lie in two arrays, in the order the table first recorded their numbers, which is the order it
/// enumerates them in. A number finds its entry through a third array, one slot for each number below its length —
/// where numbers run as most files write them, from 0 up with few gaps —, and past it through a map that hashes numbers
/// through the process's seed (<see cref="SeededComparer"/>): no lookup walks a chain a file chose, whatever numbers it
/// writes (#155). The slots stretch to a number only while they stay no longer than twice the entries the table holds or
/// has made room for, and <see cref="DenseSlack"/> more: the numbers of a file that spreads them out go to the map, and
/// cost what they hold.
/// </para>
/// <para>
/// An entry holds 36 bytes, and its slot 4 more. A section makes room for the rows it holds before they are read, for no
/// more objects than the newest section's <c>/Size</c> numbers (<see cref="Reserve"/>): 40 bytes an object numbered in
/// order, so a hundred thousand objects hold 4 MB whatever the objects themselves weigh. Room left to grow — by a
/// rebuild, or rows written shorter than 19 bytes — doubles, holding up to twice that and allocating about as much again
/// on the way. A number in the map holds about 20 bytes more than a slot would. The copy is sized to the count, not grown.
/// </para>
/// </remarks>
internal sealed class PdfXRefTable : IEnumerable<KeyValuePair<int, XRefEntry>>
{
    /// <summary>
    /// How many slots past twice the entries the table keeps for numbers it lacks: enough for a small file's free numbers
    /// and gaps, and no more than 4 KB whatever a file numbers.
    /// </summary>
    internal const int DenseSlack = 1024;

    /// <summary>The numbers, in the order the table first recorded them.</summary>
    private int[] _numbers = [];

    /// <summary>The entries, where their numbers lie in <see cref="_numbers"/>.</summary>
    private XRefEntry[] _entries = [];

    private int _count;

    /// <summary>For each number below its length, one more than where its entry lies; 0 for a number the table lacks.</summary>
    private int[] _slots = [];

    /// <summary>Where the entry of each number past <see cref="_slots"/> lies; null until one is.</summary>
    private Dictionary<int, int>? _beyond;

    /// <summary>Changes as a number is added or the table cleared, ending an enumeration begun before.</summary>
    private int _version;

    /// <summary>
    /// The offsets of the entries in use written directly, sorted the first time <see cref="FirstOffsetAfter"/> is asked
    /// and kept up to date from then on; null until then, so that an index nothing searches costs nothing more.
    /// </summary>
    private SortedOffsets? _offsets;

    /// <summary>Gets the trailer, merged across every section of the chain.</summary>
    public PdfDictionary Trailer { get; } = new();

    /// <summary>Gets the number of indexed objects.</summary>
    public int Count => _count;

    /// <summary>Gets how many entries the table holds room for.</summary>
    public int Capacity => _entries.Length;

    /// <summary>Gets the indexed object numbers, in the order the table first recorded them.</summary>
    public IEnumerable<int> Numbers
    {
        get
        {
            foreach (var (number, _) in this)
            {
                yield return number;
            }
        }
    }

    /// <summary>Looks up an object number.</summary>
    public bool TryGet(int number, out XRefEntry entry)
    {
        var at = Find(number);

        if (at < 0)
        {
            entry = default;
            return false;
        }

        entry = _entries[at];
        return true;
    }

    /// <summary>
    /// Records an entry unless the object number is already known.
    /// </summary>
    /// <remarks>
    /// Sections are read newest first, so the first definition seen is the current one. This is what makes
    /// incremental updates work: later sections in the chain describe older states of the document.
    /// </remarks>
    public bool TryAdd(int number, XRefEntry entry)
    {
        if (Find(number) >= 0)
        {
            return false;
        }

        Add(number, entry);
        Track(entry);
        return true;
    }

    /// <summary>Records an entry, replacing any existing one, which keeps its place in the order.</summary>
    public void Set(int number, XRefEntry entry)
    {
        var at = Find(number);

        if (at < 0)
        {
            Add(number, entry);
        }
        else
        {
            _entries[at] = entry;
        }

        Track(entry);
    }

    /// <summary>
    /// Makes room for <paramref name="count"/> entries more than the table holds, and no more than
    /// <paramref name="most"/> in all, before they are recorded.
    /// </summary>
    /// <remarks>
    /// A count is a section's claim, which the caller bounds by the rows the section holds and by the entries the index
    /// may take. Room is never added by less than the table holds already: a chain of many sections, each making room for
    /// its own rows, would otherwise copy the entries at each one.
    /// </remarks>
    public void Reserve(int count, int most)
    {
        var capacity = Capacity;
        var needed = (int)Math.Min((long)_count + count, most);

        if (needed > capacity)
        {
            Resize((int)Math.Min(Math.Max(needed, 2L * capacity), most));
        }
    }

    /// <summary>Removes every entry.</summary>
    public void Clear()
    {
        Array.Clear(_slots);
        _beyond = null;
        _count = 0;
        _version++;
        _offsets = null;
    }

    /// <summary>Removes every entry, and lets go of the room they took, for a table nothing will read again.</summary>
    public void Release()
    {
        Clear();
        _numbers = [];
        _entries = [];
        _slots = [];
    }

    /// <summary>Copies the indexed object numbers, in the order the table first recorded them.</summary>
    public int[] CopyNumbers() => _numbers.AsSpan(0, _count).ToArray();

    /// <summary>Enumerates the numbers and their entries in the order the table first recorded the numbers.</summary>
    /// <remarks>A number added, or the table cleared, ends the enumeration at its next step.</remarks>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<KeyValuePair<int, XRefEntry>> IEnumerable<KeyValuePair<int, XRefEntry>>.GetEnumerator() => GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Gets where the entry of <paramref name="number"/> lies, or -1.</summary>
    private int Find(int number)
    {
        if ((uint)number < (uint)_slots.Length)
        {
            return _slots[number] - 1;
        }

        return _beyond is not null && _beyond.TryGetValue(number, out var at) ? at : -1;
    }

    /// <summary>Records a number the table lacks, last in the order.</summary>
    private void Add(int number, XRefEntry entry)
    {
        if (_count == _entries.Length)
        {
            Resize(Math.Max(4, (int)Math.Min(2L * _count, Array.MaxLength)));
        }

        var at = _count++;
        _numbers[at] = number;
        _entries[at] = entry;
        _version++;

        if ((uint)number >= (uint)_slots.Length)
        {
            Stretch(number);
        }

        if ((uint)number < (uint)_slots.Length)
        {
            _slots[number] = at + 1;
        }
        else
        {
            (_beyond ??= new Dictionary<int, int>(SeededComparer.Instance)).Add(number, at);
        }
    }

    /// <summary>
    /// Stretches the slots to <paramref name="number"/> while they stay within twice the entries the table holds or has
    /// made room for, and <see cref="DenseSlack"/>, doubling at least, and moves into them the numbers of the map they
    /// reach.
    /// </summary>
    /// <remarks>
    /// The room counts as entries: a chain whose newest sections number the highest objects, as updates do, makes room
    /// for the rows it is about to read before their numbers come down to the slots. Room is made only for rows the file
    /// holds (<see cref="Reserve"/>), so the slots follow the file's bytes whatever its <c>/Size</c> claims.
    /// </remarks>
    private void Stretch(int number)
    {
        var most = (2L * Math.Max(_count, _entries.Length)) + DenseSlack;

        if (number < 0 || number >= most)
        {
            return;
        }

        var length = (int)Math.Min(Math.Max(number + 1L, 2L * _slots.Length), most);
        var slots = new int[length];
        _slots.CopyTo(slots, 0);
        _slots = slots;

        if (_beyond is null)
        {
            return;
        }

        // A number the map holds that the slots now reach moves into them: below their length, a number is in its slot
        // or not in the table. The map grows smaller by what it holds below the new length.
        var moved = false;

        foreach (var (held, at) in _beyond)
        {
            if ((uint)held < (uint)length)
            {
                slots[held] = at + 1;
                moved = true;
            }
        }

        if (moved)
        {
            var beyond = new Dictionary<int, int>(SeededComparer.Instance);

            foreach (var (held, at) in _beyond)
            {
                if ((uint)held >= (uint)length)
                {
                    beyond.Add(held, at);
                }
            }

            _beyond = beyond.Count > 0 ? beyond : null;
        }
    }

    /// <summary>Makes room for <paramref name="capacity"/> entries, keeping those held.</summary>
    private void Resize(int capacity)
    {
        Array.Resize(ref _numbers, capacity);
        Array.Resize(ref _entries, capacity);
    }

    /// <summary>
    /// Gets the smallest offset an entry in use written directly — not in an object stream — gives that is greater
    /// than <paramref name="offset"/>, or <see cref="long.MaxValue"/> when none is. A row's negative offset places
    /// nothing, and is left out (#125).
    /// </summary>
    /// <remarks>
    /// The offsets are sorted the first time this is asked, and entries recorded since are sorted in as they come, so
    /// that each lookup costs a logarithm of the index rather than a pass over it. An entry replaced keeps the offset
    /// it gave among them: the answer is always an offset some entry of the table gave, if not the entry it holds now.
    /// </remarks>
    public long FirstOffsetAfter(long offset)
    {
        if (_offsets is null)
        {
            // One array the size of the offsets, sorted in place: sorting a large index costs what it keeps.
            var count = 0;

            var entries = _entries.AsSpan(0, _count);

            foreach (var entry in entries)
            {
                count += PlacesInFile(entry) ? 1 : 0;
            }

            var offsets = new long[count];
            var index = 0;

            foreach (var entry in entries)
            {
                if (PlacesInFile(entry))
                {
                    offsets[index++] = entry.Offset;
                }
            }

            _offsets = new SortedOffsets(offsets);
        }

        return _offsets.FirstAfter(offset);
    }

    /// <summary>
    /// Lets go of the offsets sorted so far, with any an entry replaced since gave: the next lookup, if one comes, sorts
    /// those the entries give then.
    /// </summary>
    public void ForgetSortedOffsets() => _offsets = null;

    /// <summary>Adds the offset an entry gives to the sorted ones, once they are kept.</summary>
    private void Track(XRefEntry entry)
    {
        if (PlacesInFile(entry))
        {
            _offsets?.Add(entry.Offset);
        }
    }

    /// <summary>
    /// Determines whether <paramref name="entry"/> places its object at an offset of the file: a regular one does, but for a
    /// row's negative offset, which lies before the file's start whatever precedes the header (#125); one the reader found
    /// does, before the header though it be.
    /// </summary>
    private static bool PlacesInFile(XRefEntry entry) =>
        entry.Kind == XRefEntryKind.Regular && (entry.Offset >= 0 || entry.FoundByReader);

    /// <summary>Copies the entries, and not the trailer, into a table of their own, sized to hold them.</summary>
    public PdfXRefTable CopyEntries() => new()
    {
        _numbers = _numbers.AsSpan(0, _count).ToArray(),
        _entries = _entries.AsSpan(0, _count).ToArray(),
        _count = _count,
        _slots = (int[])_slots.Clone(),
        _beyond = _beyond is null ? null : new Dictionary<int, int>(_beyond, SeededComparer.Instance),
    };

    /// <summary>Copies trailer keys that are not already known.</summary>
    public void MergeTrailer(PdfDictionary trailer)
    {
        foreach (var (key, value) in trailer)
        {
            if (!Trailer.ContainsKey(key))
            {
                Trailer.Set(key, value);
            }
        }
    }

    /// <summary>Enumerates a table's numbers and entries in the order it first recorded the numbers.</summary>
    internal struct Enumerator : IEnumerator<KeyValuePair<int, XRefEntry>>
    {
        private readonly PdfXRefTable _table;
        private readonly int _version;
        private int _at;

        internal Enumerator(PdfXRefTable table)
        {
            _table = table;
            _version = table._version;
            _at = -1;
        }

        /// <inheritdoc/>
        public readonly KeyValuePair<int, XRefEntry> Current => new(_table._numbers[_at], _table._entries[_at]);

        readonly object System.Collections.IEnumerator.Current => Current;

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">A number was added, or the table cleared, since it began.</exception>
        public bool MoveNext()
        {
            if (_version != _table._version)
            {
                throw new InvalidOperationException("The index changed while it was enumerated.");
            }

            return ++_at < _table._count;
        }

        /// <inheritdoc/>
        public void Reset() => _at = -1;

        /// <inheritdoc/>
        public readonly void Dispose()
        {
        }
    }
}
