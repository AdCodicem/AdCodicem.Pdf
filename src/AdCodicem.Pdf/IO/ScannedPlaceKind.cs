namespace AdCodicem.Pdf.IO;

/// <summary>How the rebuild's scan for objects took a place it records (<see cref="ScanMemo"/>).</summary>
internal enum ScannedPlaceKind : byte
{
    /// <summary>
    /// Taken between objects, where the scan reads tokens: outside every object it delimited, and outside the comments and
    /// strings it swept between them; or taken as the bytes give it, in the data of a stream whose length the scan does not
    /// confirm, which it reads up to its first <c>endstream</c>, past where a load ends that data where the bytes show
    /// that no <c>endstream</c> follows the length: a direct <c>/Length</c>
    /// (<see cref="PdfFileReader.DataEndWithoutEndStream"/>), the length the object that holds an indirect one gives, the
    /// scan meeting that object before the place or past it, or none, the scan ending its wait for that object without
    /// it, the header a load then ends the data at included (<see cref="ScanMemo.Builder.TakeClean"/>). There, a place a
    /// comment or a string between objects would hide is held too: the data is not swept.
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
