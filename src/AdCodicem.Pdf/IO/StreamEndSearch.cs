namespace AdCodicem.Pdf.IO;

/// <summary>What a search of the file for a stream's <c>endstream</c> found.</summary>
/// <param name="Length">
/// The length of the data up to the first <c>endstream</c> after its start, the end-of-line before the keyword left
/// out; null when none lies before where the search stopped.
/// </param>
/// <param name="NextObject">
/// The offset of the next object an index places after the start of the data, where the search stopped; null when
/// none does, and the search ran to the end of the file.
/// </param>
/// <param name="EndObj">
/// What follows the <c>endstream</c> found; <see cref="EndObjState.Unknown"/> when none was, and where the object ends
/// is unknown.
/// </param>
/// <param name="Searched">
/// Whether the file was searched at all: it is not once the document's searches have read as much of it as they may,
/// and then nothing was found.
/// </param>
internal readonly record struct StreamEndSearch(int? Length, long? NextObject, EndObjState EndObj, bool Searched = true);
