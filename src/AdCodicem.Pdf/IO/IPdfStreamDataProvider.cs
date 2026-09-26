using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO;

/// <summary>
/// Produces the data object for a stream found at a given place in a file.
/// </summary>
/// <remarks>
/// This is what keeps stream contents out of memory: the parser records where the bytes are and hands the
/// range to the provider, which returns something that will read them if, and only if, they are needed.
/// </remarks>
internal interface IPdfStreamDataProvider
{
    /// <summary>Creates stream data for <paramref name="length"/> bytes at <paramref name="absoluteOffset"/>.</summary>
    PdfStreamData Create(long absoluteOffset, int length);

    /// <summary>
    /// Determines whether an <c>endstream</c> keyword starts at <paramref name="absoluteOffset"/>, after at
    /// most a few bytes of white space. The parser asks when that keyword may lie past the end of its buffer.
    /// </summary>
    bool IsEndStreamAt(long absoluteOffset);
}
