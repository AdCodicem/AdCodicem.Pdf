namespace AdCodicem.Pdf.IO;

/// <summary>An object header the rebuild's scan for objects took (<see cref="ScanMemo"/>).</summary>
/// <param name="Position">
/// Where the header starts in the file: its number's token, at the first sign of a run of signs the number is written with.
/// </param>
/// <param name="Number">The object number the header gives.</param>
/// <param name="Generation">The generation the header gives.</param>
/// <param name="Kind">How the scan took the header.</param>
internal readonly record struct ScannedHeader(long Position, int Number, int Generation, ScannedPlaceKind Kind);
