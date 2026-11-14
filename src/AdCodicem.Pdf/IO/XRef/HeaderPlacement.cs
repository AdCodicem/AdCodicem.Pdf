namespace AdCodicem.Pdf.IO.XRef;

/// <summary>What a load of an object finds of its header where an entry of the index places it, and where.</summary>
/// <param name="Place">What a load finds.</param>
/// <param name="Position">
/// Where the header the load takes starts — at the offset, past the white space or comment before it, or near it —; for
/// <see cref="HeaderPlace.PastGuard"/>, where the guard was reached; else the offset the entry gives.
/// </param>
/// <param name="Generation">The generation the header the load takes gives, as the parser reads it; -1 when it takes none.</param>
/// <param name="NumberAtOffset">
/// The number of the object whose header lies at the offset; 0 when none does; <see cref="PdfFileReader.HeaderCutsNumber"/>
/// when the header read there starts on a number the offset cuts.
/// </param>
/// <param name="GuardAtOffset">
/// Whether what lies at the offset still runs past <see cref="Documents.PdfReaderLimits.MaxObjectLength"/>, where a load
/// reaches that guard before it looks near the offset.
/// </param>
/// <param name="Vouched">
/// For <see cref="HeaderPlace.Nearby"/>, whether the search near the offset vouches for the header it chose
/// (<see cref="PdfFileReader.PlaceHeader"/>); true for any other place.
/// </param>
/// <param name="Kind">For <see cref="HeaderPlace.Nearby"/>, how the rebuild's scan takes the header chosen.</param>
internal readonly record struct HeaderPlacement(
    HeaderPlace Place,
    long Position,
    long Generation,
    long NumberAtOffset,
    bool GuardAtOffset,
    bool Vouched = true,
    ScannedPlaceKind Kind = ScannedPlaceKind.Clean);
