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
    /// Determines whether an <c>endstream</c> keyword follows <paramref name="absoluteOffset"/>, after white space and
    /// comments, however many (#174), and what follows it. The parser asks at the end of a stream's declared length when its
    /// buffer cannot say: the data runs past it, or the white space after it does.
    /// </summary>
    /// <remarks>
    /// The file is read on as far as the white space and comments run, the reads past the first counted with the searches
    /// for <c>endstream</c> (<see cref="PdfFileReader.EndStreamSearchPasses"/>); once those have read as much as they may, the
    /// first read alone decides.
    /// </remarks>
    /// <returns>Null when no <c>endstream</c> follows; what follows it otherwise.</returns>
    EndObjState? CheckEndStream(long absoluteOffset);

    /// <summary>
    /// Looks for where the data of a stream ends when no <c>endstream</c> follows its declared length, or it has none: the
    /// first <c>endstream</c> after the start of the data, in the bytes the parser holds and then in the file, before the
    /// next object the file's index places — as the file wrote it, or as the reader rebuilt it as the document opened when
    /// the file wrote none —, before the first object header the file's bytes hold, and before the end of the file.
    /// </summary>
    /// <remarks>
    /// The file is searched once for each stream: asked again for the data at the same place — the stream parsed
    /// again, after the cache let it go or the index was rebuilt —, the provider answers what it found the first time.
    /// When it finds no <c>endstream</c>, it says what the object's <c>endobj</c> does instead (#174): whether one follows
    /// the declared length, where the one that ends the object before the stop lies, and whether the stretch holds one.
    /// </remarks>
    /// <param name="number">
    /// The number of the object the stream is the value of, whose own entry in the index is no next object; 0 for a
    /// stream read alone.
    /// </param>
    /// <param name="absoluteDataStart">Where the data starts in the file.</param>
    /// <param name="declaredEnd">Where the declared length ends in the file, or -1 when the stream has none it can take.</param>
    /// <param name="buffered">The bytes of the data the parser holds, from its start, which are searched first.</param>
    /// <param name="deferred">
    /// Whether the stream is a cross-reference stream whose <c>/Length</c> the chain could not read (#182): its rows may
    /// spell an object header and end in a carriage return, so neither stops nor trims the search (#220).
    /// </param>
    StreamEndSearch FindEndStream(int number, long absoluteDataStart, long declaredEnd, ReadOnlySpan<byte> buffered, bool deferred);

    /// <summary>
    /// Resolves the object a stream's <c>/Length</c> names, and says whether the file holds it and whether it could be
    /// read — a reference to the stream itself, or to an object inside an object stream being decoded, cannot be —, or
    /// whether a guard cut it, or the object a chain of references from it leads to (#224).
    /// </summary>
    PdfObject ResolveLength(PdfReference reference, out ObjectPresence presence);

    /// <summary>
    /// Determines whether what is wrong with the length of the stream whose data starts at
    /// <paramref name="absoluteDataStart"/> was reported already, by a reading of the stream that was kept: a stream
    /// parsed again is not reported again.
    /// </summary>
    bool IsLengthFaultReported(long absoluteDataStart);
}
