namespace AdCodicem.Pdf.IO.XRef;

/// <summary>What a load of an object finds of its header where an entry of the index places it.</summary>
internal enum HeaderPlace : byte
{
    /// <summary>The object's header, at the offset the entry gives, after any white space or comment.</summary>
    AtOffset,

    /// <summary>No header of the object at the offset, and one near it, where a load relocates the object.</summary>
    Nearby,

    /// <summary>Neither: a load finds the object nowhere the entry leads, and rebuilds the index to look for it.</summary>
    NotFound,

    /// <summary>
    /// A header that still runs past <see cref="Documents.PdfReaderLimits.MaxObjectLength"/>, where a load reaches that
    /// guard: a document opened to throw on one throws there, and another reports it and finds the object nowhere else.
    /// </summary>
    PastGuard,

    /// <summary>Deciding would read more than the caller's allowance had left: what a load finds is not known.</summary>
    Undecided,
}
