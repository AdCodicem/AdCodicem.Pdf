namespace AdCodicem.Pdf.IO;

/// <summary>
/// Says what the file holds right past the end of a parser's buffer, where a window's edge leaves a token undecided.
/// </summary>
/// <remarks>
/// A parser whose window does not end the data asks it of an <c>endobj</c> its window's edge reaches, which may be the start
/// of a longer keyword: an object a window ends exactly with — the last one
/// <see cref="Documents.PdfReaderLimits.MaxObjectLength"/> allows, say — reads whole, as under a larger window (#174). It is
/// all a parser asks of the file when it reads an object for the chain without loading it, which reads no stream's data.
/// </remarks>
internal interface IPdfFileEdge
{
    /// <summary>
    /// Determines whether a token that reaches the end of the parser's buffer at <paramref name="absoluteOffset"/> ends there:
    /// the file ends there, or no regular character follows. The parser asks of an <c>endobj</c> its window's edge reaches,
    /// which may be the start of a longer keyword (#174).
    /// </summary>
    bool TokenEndsAt(long absoluteOffset);
}
