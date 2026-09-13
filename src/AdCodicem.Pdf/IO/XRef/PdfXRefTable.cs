using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO.XRef;

/// <summary>
/// The index of a document: where every object is, and the trailer that names the roots.
/// </summary>
/// <remarks>
/// This is the only structure the reader keeps for the whole document. It holds a few dozen bytes per
/// object, so indexing a file of a hundred thousand objects costs a few megabytes whatever the objects
/// themselves weigh.
/// </remarks>
internal sealed class PdfXRefTable
{
    private readonly Dictionary<int, XRefEntry> _entries = [];

    /// <summary>Gets the trailer, merged across every section of the chain.</summary>
    public PdfDictionary Trailer { get; } = new();

    /// <summary>Gets the number of indexed objects.</summary>
    public int Count => _entries.Count;

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
    public bool TryAdd(int number, XRefEntry entry) => _entries.TryAdd(number, entry);

    /// <summary>Records an entry, replacing any existing one.</summary>
    public void Set(int number, XRefEntry entry) => _entries[number] = entry;

    /// <summary>Removes every entry.</summary>
    public void Clear() => _entries.Clear();

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
