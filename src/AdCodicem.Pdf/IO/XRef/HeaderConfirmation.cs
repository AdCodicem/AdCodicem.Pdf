namespace AdCodicem.Pdf.IO.XRef;

/// <summary>
/// Whether the reader vouches for the header it takes for an entry, and, where it does not, why
/// (<see cref="Validation.PdfValidationRuleIds.XRefEntryUnconfirmed"/>).
/// </summary>
internal enum HeaderConfirmation : byte
{
    /// <summary>
    /// The header lies where the entry places it, or the search near the offset vouches for the one it chose: the only one of
    /// the object its scan takes between objects there, or the one the entries next to it are shifted alike to.
    /// </summary>
    Vouched,

    /// <summary>The nearest of several headers of the object the scan takes between objects near the offset, none supported.</summary>
    Ambiguous,

    /// <summary>A header the scan takes only as the bytes give it (<see cref="ScannedPlaceKind.Raw"/>).</summary>
    Raw,

    /// <summary>A header past where the scan's memo is whole (<see cref="ScanMemo.WholeUntil"/>), taken as the bytes give it.</summary>
    PastScan,

    /// <summary>
    /// The header at a row that cuts a number (<see cref="PdfFileReader.HeaderCutsNumber"/>), read as written where the search
    /// near the row finds no header of the object.
    /// </summary>
    Glued,
}
