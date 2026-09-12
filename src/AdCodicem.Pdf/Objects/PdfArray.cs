using System.Collections;

namespace AdCodicem.Pdf.Objects;

/// <summary>Represents a PDF array.</summary>
public sealed class PdfArray : PdfObject, IReadOnlyList<PdfObject>
{
    private readonly List<PdfObject> _items;

    /// <summary>Initialises an empty array.</summary>
    public PdfArray() => _items = [];

    /// <summary>Initialises an empty array with room for <paramref name="capacity"/> entries.</summary>
    public PdfArray(int capacity) => _items = new List<PdfObject>(capacity);

    /// <summary>Initialises an array containing <paramref name="items"/>.</summary>
    public PdfArray(IEnumerable<PdfObject> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
    }

    /// <summary>Gets the entry at <paramref name="index"/>, without resolving indirect references.</summary>
    public PdfObject this[int index] => _items[index];

    /// <inheritdoc/>
    public int Count => _items.Count;

    /// <summary>Gets the entry at <paramref name="index"/>, following indirect references.</summary>
    public PdfObject Resolved(int index) => _items[index].Resolve();

    /// <summary>Appends an entry.</summary>
    public void Add(PdfObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
    }

    /// <summary>Inserts an entry at <paramref name="index"/>.</summary>
    public void Insert(int index, PdfObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Insert(index, item);
    }

    /// <summary>Replaces the entry at <paramref name="index"/>.</summary>
    public void Set(int index, PdfObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items[index] = item;
    }

    /// <summary>Removes the entry at <paramref name="index"/>.</summary>
    public void RemoveAt(int index) => _items.RemoveAt(index);

    /// <summary>Removes every entry.</summary>
    public void Clear() => _items.Clear();

    /// <summary>Returns a non-allocating enumerator over the entries.</summary>
    public List<PdfObject>.Enumerator GetEnumerator() => _items.GetEnumerator();

    IEnumerator<PdfObject> IEnumerable<PdfObject>.GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc/>
    public override string ToString() => $"[array of {_items.Count}]";
}
