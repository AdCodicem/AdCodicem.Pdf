using System.Collections;

namespace AdCodicem.Pdf.Objects;

/// <summary>Represents a PDF dictionary.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The PDF specification calls this object a dictionary; renaming it would make every reader of the specification translate.")]
public sealed class PdfDictionary : PdfObject, IEnumerable<KeyValuePair<PdfName, PdfObject>>
{
    private readonly Dictionary<PdfName, PdfObject> _entries;

    /// <summary>Initialises an empty dictionary.</summary>
    public PdfDictionary() => _entries = [];

    /// <summary>Initialises an empty dictionary with room for <paramref name="capacity"/> entries.</summary>
    public PdfDictionary(int capacity) => _entries = new Dictionary<PdfName, PdfObject>(capacity);

    /// <summary>Gets the number of entries.</summary>
    public int Count => _entries.Count;

    /// <summary>Gets the keys of the dictionary.</summary>
    public Dictionary<PdfName, PdfObject>.KeyCollection Keys => _entries.Keys;

    /// <summary>
    /// Gets or sets an entry, without resolving indirect references. Setting <see langword="null"/>
    /// removes the entry, which is how a PDF dictionary expresses absence.
    /// </summary>
    public PdfObject? this[PdfName key]
    {
        get => _entries.GetValueOrDefault(key);
        set
        {
            ArgumentNullException.ThrowIfNull(key);
            if (value is null)
            {
                _entries.Remove(key);
            }
            else
            {
                _entries[key] = value;
            }
        }
    }

    /// <summary>Tries to get an entry, without resolving indirect references.</summary>
    public bool TryGetValue(PdfName key, out PdfObject value) => _entries.TryGetValue(key, out value!);

    /// <summary>Determines whether the dictionary contains <paramref name="key"/>.</summary>
    public bool ContainsKey(PdfName key) => _entries.ContainsKey(key);

    /// <summary>Sets an entry.</summary>
    public void Set(PdfName key, PdfObject value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        _entries[key] = value;
    }

    /// <summary>Removes an entry.</summary>
    public bool Remove(PdfName key) => _entries.Remove(key);

    /// <summary>Returns a non-allocating enumerator over the entries.</summary>
    public Dictionary<PdfName, PdfObject>.Enumerator GetEnumerator() => _entries.GetEnumerator();

    IEnumerator<KeyValuePair<PdfName, PdfObject>> IEnumerable<KeyValuePair<PdfName, PdfObject>>.GetEnumerator() =>
        _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _entries.GetEnumerator();

    /// <inheritdoc/>
    public override string ToString()
    {
        var type = this[PdfName.Type] as PdfName;
        return type is null ? $"<<dictionary of {_entries.Count}>>" : $"<</Type {type} … {_entries.Count} entries>>";
    }
}
