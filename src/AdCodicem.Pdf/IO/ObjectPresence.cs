namespace AdCodicem.Pdf.IO;

/// <summary>Whether the file holds an object, and whether the reader could produce it.</summary>
internal enum ObjectPresence : byte
{
    /// <summary>The index holds the object in use, and the reader read it — a literal <c>null</c> included.</summary>
    Defined,

    /// <summary>
    /// Neither the index the chain gave nor the one the reader reads with holds the object in use: a reference to it
    /// is null (ISO 32000-1, 7.3.10).
    /// </summary>
    Missing,

    /// <summary>
    /// The index holds the object in use, and the reader could not produce it: nothing where the entry places it,
    /// an object stream that cannot serve it, or one an encryption the library cannot undo yet hides.
    /// </summary>
    Unproduced,
}
