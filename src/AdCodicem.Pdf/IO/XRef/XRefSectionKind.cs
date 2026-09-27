namespace AdCodicem.Pdf.IO.XRef;

/// <summary>The two forms a cross-reference section takes.</summary>
internal enum XRefSectionKind : byte
{
    /// <summary>Nothing was read that says which: the section was not found.</summary>
    Unknown,

    /// <summary>A classic table: the <c>xref</c> keyword, rows of twenty bytes, and a trailer.</summary>
    Table,

    /// <summary>A cross-reference stream, whose dictionary is its trailer.</summary>
    Stream,
}
