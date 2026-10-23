namespace AdCodicem.Pdf.IO.Filters;

/// <summary>What ends a chain of filters before its last element.</summary>
internal enum FilterChainEnd
{
    /// <summary>Nothing: every element of the chain is a filter decoding runs.</summary>
    None,

    /// <summary>An image filter, whose data is left as the image it is.</summary>
    Image,

    /// <summary>A filter the library does not know.</summary>
    Unsupported,

    /// <summary>An element of the <c>/Filter</c> array that names no filter.</summary>
    NotAName,

    /// <summary>A <c>/Filter</c> entry that is neither a name nor an array.</summary>
    NotAFilterEntry,

    /// <summary>An element past the most filters the reader's guard lets a stream's data pass through.</summary>
    Limit,

    /// <summary>
    /// A value decoding reads — the <c>/Filter</c> entry or an element of it, <c>/DecodeParms</c> or a value of a step's
    /// parameters — that leads to an object one of the reader's guards cut, or left out of the index: what it holds is
    /// the guard's, and the step it is read for, and those after it, do not run (#263).
    /// </summary>
    Guard,
}
