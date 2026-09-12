namespace AdCodicem.Pdf.IO.Filters;

/// <summary>Bounds every filter respects, so that a hostile file cannot exhaust memory.</summary>
internal static class PdfFilterLimits
{
    /// <summary>
    /// Largest amount of data a single stream may decode to. A compressed stream of a few kilobytes can
    /// expand to gigabytes; refusing to follow it that far is the difference between a diagnostic and a
    /// denial of service.
    /// </summary>
    public const int MaxDecodedLength = 256 * 1024 * 1024;
}
