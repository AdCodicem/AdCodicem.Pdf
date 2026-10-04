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
/// <param name="Kind">What the entry says of the object.</param>
/// <param name="Offset">Where a regular entry places the object, counted from the header as the file's offsets are.</param>
/// <param name="Generation">The generation a regular entry gives the object.</param>
/// <param name="ObjectStreamNumber">The object stream a compressed entry places the object in.</param>
/// <param name="IndexInObjectStream">The object's index in that object stream.</param>
/// <param name="FoundByReader">
/// Whether the reader found the object's header where a regular entry places it — rebuilding the index, or searching near
/// where a row placed it —, rather than taking the offset from the file: its offset names a byte of the file, though it be
/// before the header, and is negative then (#125).
/// </param>
internal readonly record struct XRefEntry(
    XRefEntryKind Kind,
    long Offset,
    int Generation,
    int ObjectStreamNumber,
    int IndexInObjectStream,
    bool FoundByReader = false)
{
    /// <summary>An entry for an unused object number.</summary>
    public static XRefEntry Free { get; } = new(XRefEntryKind.Free, 0, 0, 0, 0);

    /// <summary>Creates an entry for an object written directly in the file, at the offset a row of the file gives.</summary>
    public static XRefEntry Regular(long offset, int generation) =>
        new(XRefEntryKind.Regular, offset, generation, 0, 0);

    /// <summary>Creates an entry for an object written directly in the file, whose header the reader found there.</summary>
    public static XRefEntry Found(long offset, int generation) =>
        new(XRefEntryKind.Regular, offset, generation, 0, 0, FoundByReader: true);

    /// <summary>Creates an entry for an object stored inside an object stream.</summary>
    public static XRefEntry Compressed(int objectStreamNumber, int index) =>
        new(XRefEntryKind.Compressed, 0, 0, objectStreamNumber, index);
}
