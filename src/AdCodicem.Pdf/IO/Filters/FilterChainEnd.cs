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
}
