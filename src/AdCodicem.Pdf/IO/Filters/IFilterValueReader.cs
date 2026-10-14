using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO.Filters;

/// <summary>
/// Follows the values a stream's filters are decoded with as the one who reads them needs: decoding resolves them; the
/// reader, checking a cross-reference stream before its rows are read, follows them only through what it has read.
/// </summary>
internal interface IFilterValueReader
{
    /// <summary>Follows <paramref name="written"/> to the value it leads to.</summary>
    /// <param name="key">The entry of the stream's dictionary the value is read for: <c>/Filter</c> or <c>/DecodeParms</c>.</param>
    /// <param name="written">The value as the file wrote it: the entry, an element of it, or a value of a step's parameters.</param>
    /// <param name="entry">Whether <paramref name="written"/> is the entry's own value, rather than one inside it.</param>
    /// <param name="value">The value it leads to, or null when it leads to none.</param>
    /// <returns>False when the reading ends here.</returns>
    bool TryRead(PdfName key, PdfObject? written, bool entry, out PdfObject? value);
}
