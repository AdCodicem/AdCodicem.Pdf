namespace AdCodicem.Pdf.IO;

/// <summary>Where the parser found the <c>endstream</c> of a stream whose length the file did not confirm.</summary>
internal enum EndStreamState : byte
{
    /// <summary>After the data, where the <c>/Length</c> did not put it, or where no usable <c>/Length</c> put anything.</summary>
    Found,

    /// <summary>
    /// Nowhere between the start of the data and the next object an index places after it: the declared length is
    /// kept.
    /// </summary>
    MissingBeforeNextObject,

    /// <summary>
    /// Nowhere between the start of the data and the end of the file: the declared length is kept when the file can
    /// hold it, and the data runs to the end of the file otherwise.
    /// </summary>
    MissingBeforeEndOfFile,

    /// <summary>Nowhere before the <c>endobj</c> that follows the data, which is taken to run to the end of the file.</summary>
    MissingBeforeEndObj,

    /// <summary>
    /// Not looked for: the document's searches for <c>endstream</c> read as much of the file as they may, which only
    /// objects that overlap make them do. The declared length is kept.
    /// </summary>
    NotSearched,
}
