using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO;

/// <summary>
/// Produces the data object for a stream found at a given place in a file.
/// </summary>
/// <remarks>
/// This is what keeps stream contents out of memory: the parser records where the bytes are and hands the
/// range to the provider, which returns something that will read them if, and only if, they are needed. The
/// provider is also the parser's way to the rest of the file, for what its buffer does not hold: the bytes
/// after a stream's data, and the objects a stream's <c>/Length</c> names.
/// </remarks>
internal interface IPdfStreamDataProvider
{
    /// <summary>Gets the length of the file the stream data is read from.</summary>
    long SourceLength { get; }

    /// <summary>Creates stream data for <paramref name="length"/> bytes at <paramref name="absoluteOffset"/>.</summary>
    PdfStreamData Create(long absoluteOffset, int length);

    /// <summary>
    /// Determines whether an <c>endstream</c> keyword starts at <paramref name="absoluteOffset"/>, after at
    /// most a few bytes of white space. The parser asks when that keyword may lie past the end of its buffer.
    /// </summary>
    bool IsEndStreamAt(long absoluteOffset);

    /// <summary>
    /// Determines whether an <c>endstream</c> keyword starts at <paramref name="absoluteOffset"/>, as
    /// <see cref="IsEndStreamAt"/> does, and what follows it. The parser asks at the end of a stream whose data runs
    /// past its buffer, where it can see neither.
    /// </summary>
    /// <returns>Null when no <c>endstream</c> starts there; what follows it otherwise.</returns>
    EndObjState? CheckEndStream(long absoluteOffset);

    /// <summary>
    /// Looks for where the data of a stream that runs past the parser's buffer ends, when no <c>endstream</c> follows
    /// its declared length: the first <c>endstream</c> after the start of the data, before the next object the file's
    /// indexes place, or before the end of the file.
    /// </summary>
    /// <remarks>
    /// The file is searched once for each stream: asked again for the data at the same place — the stream parsed
    /// again, after the cache let it go or the index was rebuilt —, the provider answers what it found the first time.
    /// </remarks>
    /// <param name="number">
    /// The number of the object the stream is the value of, whose own entries in the indexes are no next object; 0 for
    /// a stream read alone.
    /// </param>
    /// <param name="absoluteDataStart">Where the data starts in the file.</param>
    /// <param name="buffered">The bytes of the data the parser holds, from its start, which are searched first.</param>
    StreamEndSearch FindEndStream(int number, long absoluteDataStart, ReadOnlySpan<byte> buffered);

    /// <summary>
    /// Resolves the object a stream's <c>/Length</c> names, and says whether the file holds it and whether it could be
    /// read — a reference to the stream itself, or to an object inside an object stream being decoded, cannot be.
    /// </summary>
    PdfObject ResolveLength(PdfObjectId id, out ObjectPresence presence);

    /// <summary>
    /// Determines whether what is wrong with the length of the stream whose data starts at
    /// <paramref name="absoluteDataStart"/> was reported already, by a reading of the stream that was kept: a stream
    /// parsed again is not reported again.
    /// </summary>
    bool IsLengthFaultReported(long absoluteDataStart);
}
