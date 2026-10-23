namespace AdCodicem.Pdf.IO;

/// <summary>Whether the bytes after a stream's data hold its <c>endstream</c>.</summary>
internal enum StreamEndKind : byte
{
    /// <summary>Something else follows the data, after white space and comments.</summary>
    None,

    /// <summary><c>endstream</c> follows the data, after white space and comments.</summary>
    EndStream,

    /// <summary>The bytes end before they can say: in white space, a comment, or what may be the keyword's start.</summary>
    Undecided,
}
