namespace AdCodicem.Pdf.IO;

/// <summary>An object header the rebuild's scan for objects took (<see cref="ScanMemo"/>).</summary>
/// <param name="Position">
/// Where the header starts in the file: its number's token, at the first sign of a run of signs the number is written with.
/// </param>
/// <param name="Number">The object number the header gives.</param>
/// <param name="Generation">The generation the header gives.</param>
/// <param name="Kind">How the scan took the header.</param>
/// <param name="HoldsNoValue">
/// Whether the object holds no value as a load reads it, as far as the first bytes from its header tell: a rebuild that
/// replays the memo keeps the definition before such a one, as one that scans the file does (#351).
/// </param>
internal readonly record struct ScannedHeader(long Position, int Number, int Generation, ScannedPlaceKind Kind, bool HoldsNoValue = false);
