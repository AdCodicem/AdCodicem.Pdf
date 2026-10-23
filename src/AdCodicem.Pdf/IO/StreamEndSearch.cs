namespace AdCodicem.Pdf.IO;

/// <summary>What a search of the file for a stream's <c>endstream</c> found.</summary>
/// <param name="Length">
/// The length of the data up to the first <c>endstream</c> after its start, the end-of-line before the keyword left
/// out; null when none lies before where the search stopped.
/// </param>
/// <param name="NextObject">
/// Where the search stopped short of the end of the file: the first object header the file's bytes hold after the start
/// of the data, when one comes before any <c>endstream</c> and before the next object an index places, and that object
/// otherwise; null when neither does, and the search ran to the end of the file.
/// </param>
/// <param name="EndObj">
/// What follows the <c>endstream</c> found; <see cref="EndObjState.Unknown"/> when none was, and where the object ends
/// is unknown.
/// </param>
/// <param name="Searched">
/// Whether the file was searched as far as the search stops: past the bytes the parser holds, it is not once the
/// document's searches have read as much of it as they may, and then nothing past them was found.
/// </param>
internal readonly record struct StreamEndSearch(int? Length, long? NextObject, EndObjState EndObj, bool Searched = true)
{
    /// <summary>
    /// Gets where the data ends, in the file, when no <c>endstream</c> was found and the object's own <c>endobj</c>, which
    /// only white space separates from where the search stopped, ends it, the end-of-line before it left out; -1 when none
    /// does.
    /// </summary>
    public long ObjectEnd { get; init; } = -1;

    /// <summary>
    /// Gets a value indicating whether, no <c>endstream</c> found, an <c>endobj</c> follows the declared length, white
    /// space and comments between, before where the search stopped.
    /// </summary>
    public bool EndObjAfterDeclared { get; init; }

    /// <summary>
    /// Gets a value indicating whether, no <c>endstream</c> found, the bytes searched hold an <c>endobj</c> keyword
    /// anywhere: the object may then end inside what is taken as its data, and whether it ends with <c>endobj</c> is
    /// unknown.
    /// </summary>
    public bool EndObjSeen { get; init; }
}
