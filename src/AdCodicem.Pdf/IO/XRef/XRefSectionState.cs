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
    /// <remarks>
    /// The <c>xref</c> keyword, or a stream whose dictionary gives <c>/Type /XRef</c>, is the section, and is not looked for
    /// nearby (#281). An object that only gives a <c>/W</c> may be something else: the section is looked for nearby first,
    /// and, with nothing there, stays malformed where it gives no <c>/Type</c>, and is <see cref="NotFound"/> where it gives
    /// another, an object that is no section — but for one a guard cut a value of, the guard's (#263).
    /// </remarks>
    Malformed,

    /// <summary>
    /// Named by an <c>/XRefStm</c> at a position the chain had reached before, or that its own table's <c>/Prev</c> names
    /// too: nothing was read for it, what is there having been read, or looked for, once, where the chain reached it, and
    /// <see cref="XRefSectionRecord.ReadAs"/> is its record (#156).
    /// </summary>
    NamedAgain,
}
