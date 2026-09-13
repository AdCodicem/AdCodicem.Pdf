namespace AdCodicem.Pdf.IO.XRef;

/// <summary>What a cross-reference entry says about where an object lives.</summary>
internal enum XRefEntryKind : byte
{
    /// <summary>The object number is not in use.</summary>
    Free,

    /// <summary>The object is written at a byte offset in the file.</summary>
    Regular,

    /// <summary>The object is stored inside an object stream.</summary>
    Compressed,
}

/// <summary>One entry of the cross-reference index.</summary>
internal readonly record struct XRefEntry(
    XRefEntryKind Kind,
    long Offset,
    int Generation,
    int ObjectStreamNumber,
    int IndexInObjectStream)
{
    /// <summary>An entry for an unused object number.</summary>
    public static XRefEntry Free { get; } = new(XRefEntryKind.Free, 0, 0, 0, 0);

    /// <summary>Creates an entry for an object written directly in the file.</summary>
    public static XRefEntry Regular(long offset, int generation) =>
        new(XRefEntryKind.Regular, offset, generation, 0, 0);

    /// <summary>Creates an entry for an object stored inside an object stream.</summary>
    public static XRefEntry Compressed(int objectStreamNumber, int index) =>
        new(XRefEntryKind.Compressed, 0, 0, objectStreamNumber, index);
}
