using System.Collections;
using System.Globalization;

namespace AdCodicem.Pdf.Objects;

/// <summary>Represents a PDF dictionary.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The PDF specification calls this object a dictionary; renaming it would make every reader of the specification translate.")]
public sealed class PdfDictionary : PdfObject, IEnumerable<KeyValuePair<PdfName, PdfObject>>
{
    private readonly Dictionary<PdfName, PdfObject> _entries;

    /// <summary>Initializes an empty dictionary.</summary>
    public PdfDictionary() => _entries = [];

    /// <summary>Initializes an empty dictionary with room for <paramref name="capacity"/> entries.</summary>
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

    /// <summary>
    /// Adds an entry unless the dictionary has one for <paramref name="key"/> already, and says whether it did: one lookup,
    /// where asking first and setting after takes two.
    /// </summary>
    internal bool TryAdd(PdfName key, PdfObject value) => _entries.TryAdd(key, value);

    /// <summary>Returns a non-allocating enumerator over the entries.</summary>
    public Dictionary<PdfName, PdfObject>.Enumerator GetEnumerator() => _entries.GetEnumerator();

    IEnumerator<KeyValuePair<PdfName, PdfObject>> IEnumerable<KeyValuePair<PdfName, PdfObject>>.GetEnumerator() =>
        _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _entries.GetEnumerator();

    /// <summary>
    /// Describes the dictionary for a debugger or a log by its <c>/Type</c> and its count of entries, as in
    /// <c>&lt;&lt;/Type /Page ... 3 entries&gt;&gt;</c>, or by its count alone when its <c>/Type</c> is no name.
    /// </summary>
    /// <remarks>
    /// The type is quoted as <see cref="PdfName.ToString"/> quotes a name: printable ASCII, bounded, whatever the file
    /// wrote. The entries themselves are not described.
    /// </remarks>
    public override string ToString()
    {
        var count = _entries.Count;
        return this[PdfName.Type] is PdfName type
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"<</Type {type} ... {count} {(count == 1 ? "entry" : "entries")}>>")
            : string.Create(CultureInfo.InvariantCulture, $"<<dictionary of {count}>>");
    }
}
