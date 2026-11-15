namespace AdCodicem.Pdf.IO;

/// <summary>How the rebuild's scan for objects took a place it records (<see cref="ScanMemo"/>).</summary>
internal enum ScannedPlaceKind : byte
{
    /// <summary>
    /// Taken between objects, where the scan reads tokens: outside every object it delimited, and outside the comments and
    /// strings it swept between them; or past where a load ends the data of a stream that lost its <c>endstream</c>: before
    /// where the load's search for it stops, in what a declared length short of the data leaves of it, which is not swept
    /// (<see cref="PdfFileReader.DataEndWithoutEndStream"/>), or where the scan took it as the bytes give it, in what it read
    /// as that data before it met the object that holds the stream's indirect <c>/Length</c>
    /// (<see cref="ScanMemo.Builder.TakeClean"/>).
    /// </summary>
    Clean,

    /// <summary>
    /// Taken as the bytes give it, no comment nor string swept there: in the data of a stream whose length the scan does
    /// not confirm — it confirms only a direct <c>/Length</c> an <c>endstream</c> follows, as far as the bytes tell —,
    /// before the first <c>endstream</c> after it or where a load ends that data as far as the file's bytes tell — the
    /// header a load ends it at included, where the declared length runs past it —, or in what a string that runs past its
    /// object's end runs into. Of the places the scan took in such data before it learned, past them, that a load ends it
    /// sooner (<see cref="ScanMemo.Builder.TakeClean"/>), those from where the data of a stream it met there starts, or
    /// from where it read anew before an object whose string runs past its end, stay so.
    /// </summary>
    Raw,
}
