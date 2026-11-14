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
/// when the offset cuts a number, and no header of the object is read there.
/// </param>
/// <param name="GuardAtOffset">
/// Whether what lies at the offset still runs past <see cref="Documents.PdfReaderLimits.MaxObjectLength"/>, where a load
/// reaches that guard before it looks near the offset.
/// </param>
/// <param name="Confirmation">
/// For <see cref="HeaderPlace.AtOffset"/> and <see cref="HeaderPlace.Nearby"/>, whether the reader vouches for the header it
/// takes, and why not where it does not (<see cref="PdfFileReader.PlaceHeader"/>); <see cref="HeaderConfirmation.Vouched"/>
/// for any other place.
/// </param>
internal readonly record struct HeaderPlacement(
    HeaderPlace Place,
    long Position,
    long Generation,
    long NumberAtOffset,
    bool GuardAtOffset,
    HeaderConfirmation Confirmation = HeaderConfirmation.Vouched);
