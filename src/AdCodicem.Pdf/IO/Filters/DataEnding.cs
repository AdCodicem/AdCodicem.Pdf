namespace AdCodicem.Pdf.IO.Filters;

/// <summary>How the data of a filter whose format closes it with an end marker ended.</summary>
internal enum DataEnding
{
    /// <summary>At its end marker, or where decoding stopped before the end of the data: its end says nothing.</summary>
    Marked,

    /// <summary>Without its end marker, after a whole unit: all of it decoded, and none of it is known to be lost.</summary>
    Unmarked,

    /// <summary>
    /// Without its end marker, inside a unit, which was completed as the format completes a last one — or, of data the reader
    /// cut, dropped as the cut's.
    /// </summary>
    UnmarkedInsideUnit,

    /// <summary>Without its end marker, after a single ASCII85 digit, which encodes no byte and was dropped.</summary>
    UnmarkedAfterLoneDigit,

    /// <summary>
    /// Without its end-of-data code, inside an LZW code: a whole byte no code completes, or bits a padding would leave
    /// zero, which were dropped.
    /// </summary>
    UnmarkedInsideCode,

    /// <summary>
    /// Without its end-of-data byte, inside a RunLength run of bytes written as they are, which it does not all hold:
    /// those it holds were kept.
    /// </summary>
    UnmarkedInsideRun,

    /// <summary>Without its end-of-data byte, after the length of a RunLength run that repeats a byte, without that byte.</summary>
    UnmarkedBeforeRepeatedByte,
}
