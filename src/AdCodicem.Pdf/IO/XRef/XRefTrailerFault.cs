namespace AdCodicem.Pdf.IO.XRef;

/// <summary>What is wrong with the trailer of a cross-reference section, if anything.</summary>
internal enum XRefTrailerFault : byte
{
    /// <summary>Nothing: the trailer is a well-formed dictionary, or the section was not read far enough to tell.</summary>
    None,

    /// <summary>A classic table's rows are not followed by the <c>trailer</c> keyword.</summary>
    Missing,

    /// <summary>The <c>trailer</c> keyword is not followed by a dictionary.</summary>
    NotADictionary,

    /// <summary>The dictionary was read despite syntax errors, or runs to the end of the file unclosed.</summary>
    Malformed,
}
