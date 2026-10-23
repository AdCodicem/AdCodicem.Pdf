namespace AdCodicem.Pdf.IO;

/// <summary>Where the parser found the <c>endstream</c> of a stream whose length the file did not confirm.</summary>
internal enum EndStreamState : byte
{
    /// <summary>After the data, where the <c>/Length</c> did not put it, or where no usable <c>/Length</c> put anything.</summary>
    Found,

    /// <summary>
    /// Nowhere between the start of the data and the next object after it — the one an index places, or the first header
    /// the file's bytes hold, whichever is nearer —, and no <c>endobj</c> of its object ends the data: the declared length
    /// is kept when it ends before that object, and the data runs up to it otherwise.
    /// </summary>
    MissingBeforeNextObject,

    /// <summary>
    /// Nowhere between the start of the data and the end of the file, and no <c>endobj</c> of its object ends the data:
    /// the declared length is kept when the file can hold it, and the data runs to the end of the file otherwise.
    /// </summary>
    MissingBeforeEndOfFile,

    /// <summary>
    /// Nowhere before the next object or the end of the file, and its object's <c>endobj</c> plays its part: the data is
    /// the declared length the <c>endobj</c> follows, or ends where that <c>endobj</c> ends the object.
    /// </summary>
    MissingBeforeEndObj,

    /// <summary>
    /// Not looked for past the bytes the parser held: the document's searches for <c>endstream</c> read as much of the
    /// file as they may, which only searches that share a stretch of it make them do — objects that overlap, or headers
    /// the search does not take that no index as written places. The declared length is kept when it ends before the
    /// next object, and the data runs up to it otherwise.
    /// </summary>
    NotSearched,
}
