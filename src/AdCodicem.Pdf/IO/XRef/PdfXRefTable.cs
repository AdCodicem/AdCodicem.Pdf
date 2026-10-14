using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO.XRef;

/// <summary>
/// The index of a document: where every object is, and the trailer that names the roots.
/// </summary>
/// <remarks>
/// The reader keeps one for the whole document. Once it has changed the one the chain gave — an object found away
/// from its entry, or a rebuild —, it also keeps a copy of that one as it was, which
/// <see cref="PdfFileReader.ChainIndex"/> serves. An index holds 52 bytes a slot of its map. A section makes room for the
/// rows it holds before they are read, for no more objects than the newest section's <c>/Size</c> numbers, to the next
/// prime in .NET's table (<see cref="Reserve"/>): 52 to 63 bytes an object, so a hundred thousand objects hold 5 to 6 MB
/// whatever the objects themselves weigh. A map left to grow — by a rebuild, or rows written shorter than 19 bytes —
/// doubles, holding 52 to 108 bytes an object and allocating about as much again on the way. The copy is sized
/// to the count, not grown: about 60 bytes an object more.
/// </remarks>
internal sealed class PdfXRefTable
{
    private readonly Dictionary<int, XRefEntry> _entries;

    /// <summary>
    /// The offsets of the entries in use written directly, sorted the first time <see cref="FirstOffsetAfter"/> is asked
    /// and kept up to date from then on; null until then, so that an index nothing searches costs nothing more.
    /// </summary>
    private SortedOffsets? _offsets;

    public PdfXRefTable() => _entries = [];

    private PdfXRefTable(Dictionary<int, XRefEntry> entries) => _entries = entries;

    /// <summary>Gets the trailer, merged across every section of the chain.</summary>
    public PdfDictionary Trailer { get; } = new();

    /// <summary>Gets the number of indexed objects.</summary>
    public int Count => _entries.Count;

    /// <summary>Gets how many entries the table holds room for.</summary>
    public int Capacity => _entries.EnsureCapacity(0);

    /// <summary>Gets the indexed object numbers with their entries.</summary>
    public Dictionary<int, XRefEntry> Entries => _entries;

    /// <summary>Looks up an object number.</summary>
    public bool TryGet(int number, out XRefEntry entry) => _entries.TryGetValue(number, out entry);

    /// <summary>
    /// Records an entry unless the object number is already known.
    /// </summary>
    /// <remarks>
    /// Sections are read newest first, so the first definition seen is the current one. This is what makes
    /// incremental updates work: later sections in the chain describe older states of the document.
    /// </remarks>
    public bool TryAdd(int number, XRefEntry entry)
    {
        if (!_entries.TryAdd(number, entry))
        {
            return false;
        }

        Track(entry);
        return true;
    }

    /// <summary>Records an entry, replacing any existing one.</summary>
    public void Set(int number, XRefEntry entry)
    {
        _entries[number] = entry;
        Track(entry);
    }

    /// <summary>
    /// Makes room for <paramref name="count"/> entries more than the table holds, and no more than
    /// <paramref name="most"/> in all, before they are recorded.
    /// </summary>
    /// <remarks>
    /// A count is a section's claim, which the caller bounds by the rows the section holds and by the entries the index
    /// may take. Room is never added by less than the map holds already: a chain of many sections, each making room for
    /// its own rows, would otherwise resize the map at each one, and past the largest prime .NET keeps in its table, at
    /// 7,199,369, every resize copies the whole map.
    /// </remarks>
    public void Reserve(int count, int most)
    {
        var capacity = Capacity;
        var needed = (int)Math.Min((long)_entries.Count + count, most);

        if (needed > capacity)
        {
            _entries.EnsureCapacity((int)Math.Min(Math.Max(needed, 2L * capacity), most));
        }
    }

    /// <summary>Removes every entry.</summary>
    public void Clear()
    {
        _entries.Clear();
        _offsets = null;
    }

    /// <summary>Removes every entry, and lets go of the room they took, for a table nothing will read again.</summary>
    public void Release()
    {
        Clear();
        _entries.TrimExcess();
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

            foreach (var entry in _entries.Values)
            {
                count += PlacesInFile(entry) ? 1 : 0;
            }

            var offsets = new long[count];
            var index = 0;

            foreach (var entry in _entries.Values)
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

    /// <summary>Copies the entries, and not the trailer, into a table of their own.</summary>
    public PdfXRefTable CopyEntries() => new(new Dictionary<int, XRefEntry>(_entries));

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
}
