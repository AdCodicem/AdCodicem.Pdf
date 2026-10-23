namespace AdCodicem.Pdf.IO;

/// <summary>
/// Says what the file holds right past the end of a parser's buffer, where a window's edge leaves a token undecided.
/// </summary>
/// <remarks>
/// A parser given no larger window — one <see cref="Documents.PdfReaderLimits.MaxObjectLength"/> ends — asks it of a
/// keyword its window's edge reaches, which may be the start of a longer one: an object a window ends exactly with reads
/// whole, as under a larger window (#174). It is all the parser asks of the file while the cross-reference chain is read,
/// which reads no stream's data.
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
