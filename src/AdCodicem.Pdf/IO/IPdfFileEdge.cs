namespace AdCodicem.Pdf.IO;

/// <summary>
/// Says what the file — or an object stream's decoded data — holds right past the end of a parser's buffer, where a window's
/// edge leaves a token undecided.
/// </summary>
/// <remarks>
/// A parser whose window does not end the data asks it of an <c>endobj</c> or an <c>R</c> its window's edge reaches, which
/// may be the start of a longer keyword — <c>endobjX</c>, <c>RG</c> —: an object a window ends exactly with — the last one
/// <see cref="Documents.PdfReaderLimits.MaxObjectLength"/> allows, say — reads whole, as under a larger window (#174, #224).
/// It is all a parser asks of the file when it reads an object for the chain without loading it, which reads no stream's
/// data; an object stream member's parser asks it of the decoded data, which is in memory.
/// </remarks>
internal interface IPdfFileEdge
{
    /// <summary>
    /// Determines whether a token that reaches the end of the parser's buffer at <paramref name="absoluteOffset"/> ends there:
    /// the file ends there, or no regular character follows. The parser asks of an <c>endobj</c> or an <c>R</c> its window's
    /// edge reaches, which may be the start of a longer keyword (#174, #224).
    /// </summary>
    bool TokenEndsAt(long absoluteOffset);
}
