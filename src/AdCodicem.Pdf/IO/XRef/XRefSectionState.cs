namespace AdCodicem.Pdf.IO.XRef;

/// <summary>What became of a cross-reference section the chain named.</summary>
internal enum XRefSectionState : byte
{
    /// <summary>Read where it was named.</summary>
    Read,

    /// <summary>Not where it was named, and read a few bytes from there.</summary>
    Relocated,

    /// <summary>Nothing that reads as a section where it was named, nor near it; or it was named by something that is not an offset.</summary>
    NotFound,

    /// <summary>
    /// A section is where it was named — an <c>xref</c> keyword, or a cross-reference stream — but it could not be
    /// read.
    /// </summary>
    Malformed,
}
