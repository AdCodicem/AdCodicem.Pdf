namespace AdCodicem.Pdf.IO;

/// <summary>
/// The reader's guards a valid document can reach, one for each property of
/// <see cref="Documents.PdfReaderLimits"/>.
/// </summary>
internal enum PdfLimit
{
    /// <summary>What one stream may decode to.</summary>
    DecodedStream,

    /// <summary>How long an object may be, the data of a stream aside.</summary>
    Object,

    /// <summary>How long a classic cross-reference section may be.</summary>
    XRefSectionLength,

    /// <summary>How many cross-reference sections a chain may hold.</summary>
    XRefSectionCount,

    /// <summary>How long a trailer, or a cross-reference stream's dictionary, may be.</summary>
    Trailer,
}
