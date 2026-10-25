namespace AdCodicem.Pdf.IO;

/// <summary>
/// The reader's guards a valid document can reach, one for each property of
/// <see cref="Documents.PdfReaderLimits"/>.
/// </summary>
internal enum PdfLimit
{
    /// <summary>What one stream may decode to.</summary>
    DecodedStream,

    /// <summary>How much of one stream's encoded data may be read at once.</summary>
    StreamLength,

    /// <summary>How long an object may be, the data of a stream aside.</summary>
    Object,

    /// <summary>How long a classic cross-reference section may be.</summary>
    XRefSectionLength,

    /// <summary>How many cross-reference sections a chain may hold.</summary>
    XRefSectionCount,

    /// <summary>How many object numbers the index may hold.</summary>
    IndexEntries,

    /// <summary>How many filters a stream's data may pass through.</summary>
    FilterChain,

    /// <summary>
    /// How long a trailer, or a cross-reference stream's dictionary, may be: the last, which every switch over the guards
    /// takes as its default arm.
    /// </summary>
    Trailer,
}
