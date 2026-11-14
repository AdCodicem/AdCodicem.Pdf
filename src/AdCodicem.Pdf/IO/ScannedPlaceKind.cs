namespace AdCodicem.Pdf.IO;

/// <summary>How the rebuild's scan for objects took a place it records (<see cref="ScanMemo"/>).</summary>
internal enum ScannedPlaceKind : byte
{
    /// <summary>
    /// Taken between objects, where the scan reads tokens: outside every object it delimited, and outside the comments and
    /// strings it swept between them.
    /// </summary>
    Clean,

    /// <summary>
    /// Taken as the bytes give it, no comment nor string swept there: in the data of a stream whose length the scan could
    /// not confirm, before the first <c>endstream</c> after it or where a load ends that data as far as the file's bytes
    /// tell, or in what a string that runs past its object's end runs into.
    /// </summary>
    Raw,
}
