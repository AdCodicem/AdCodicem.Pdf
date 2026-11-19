namespace AdCodicem.Pdf.IO;

/// <summary>How the rebuild's scan for objects took a place it records (<see cref="ScanMemo"/>).</summary>
internal enum ScannedPlaceKind : byte
{
    /// <summary>
    /// Taken between objects, where the scan reads tokens: outside every object it delimited, and outside the comments and
    /// strings it swept between them; or taken as the bytes give it, in the data of a stream whose length the scan does not
    /// confirm, which it reads up to its first <c>endstream</c>, past where a load ends that data where the bytes show
    /// that no <c>endstream</c> follows the length — a direct <c>/Length</c>
    /// (<see cref="PdfFileReader.DataEndWithoutEndStream"/>), the length the object that holds an indirect one gives, the
    /// scan meeting that object before the place or past it, or none, the scan ending its wait for that object without
    /// it, the header a load then ends the data at included, or an <c>xref</c> or a <c>trailer</c> keyword that starts its
    /// line, which the scan took before that header (<see cref="PdfFileReader.EndAwaitedData"/>) —, where no comment nor
    /// string between objects holds it, as a sweep from that end tells, on the line that holds the data's first
    /// <c>endstream</c> too, past where the scan stops reading the data as the bytes give them
    /// (<see cref="PdfFileReader.KindAt"/>). Some places hand-made shapes put in such data, or in a comment or a string
    /// there, are held so too (#392).
    /// </summary>
    Clean,

    /// <summary>
    /// Taken as the bytes give it, no comment nor string swept there: in the data of a stream whose length the scan does
    /// not confirm — it confirms a direct <c>/Length</c> an <c>endstream</c> follows, and an indirect one whose object the
    /// file's bytes give where one follows the length it holds, as far as the bytes tell —, before the first
    /// <c>endstream</c> after it or where a load ends that data as far as the file's bytes tell — the header a load ends it
    /// at included, where the declared length runs past it —, or, past that end, where a comment or a string between
    /// objects holds it, on the line that holds the data's first <c>endstream</c> too; in the data of an attached file,
    /// whatever length the bytes give, short of where its length ends that data, the end of its object or an
    /// <c>endstream</c> following it, past its first <c>endstream</c> too (<see cref="PdfFileReader.EndAttachedData"/>); or in what
    /// a string that runs past its object's end runs into. Of the places the scan took in such data before it learned, past
    /// them, that a load ends it sooner (<see cref="PdfFileReader.TakeCleanSwept"/>), those from where the data of a stream
    /// it met there starts, or from where it read anew before an object whose string runs past its end, stay so. Some of
    /// these places, in hand-made shapes, are held as taken between objects instead (#392).
    /// </summary>
    Raw,
}
