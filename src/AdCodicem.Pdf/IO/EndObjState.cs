namespace AdCodicem.Pdf.IO;

/// <summary>What follows the value of an indirect object the parser read.</summary>
internal enum EndObjState : byte
{
    /// <summary>The <c>endobj</c> keyword.</summary>
    Present,

    /// <summary>A whole token other than <c>endobj</c>: the next object's header, a stray keyword.</summary>
    Absent,

    /// <summary>
    /// The end of the bytes the parser was given, or a token they cut: what follows is unknown to it, and to its
    /// caller only if the bytes stop short of the end of the file.
    /// </summary>
    Unseen,

    /// <summary>
    /// The object is a stream whose <c>endstream</c> the parser could not find, though an <c>endobj</c> follows its
    /// data: its data was taken to run to the end of the bytes, and where the object ends is unknown.
    /// </summary>
    Unknown,
}
