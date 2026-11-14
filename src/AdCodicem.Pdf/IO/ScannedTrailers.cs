namespace AdCodicem.Pdf.IO;

/// <summary>Where the trailers a rebuild merges lie, as the scan for objects meets them.</summary>
internal sealed class ScannedTrailers
{
    /// <summary>Gets where the dictionary after each <c>trailer</c> keyword starts: right past the keyword.</summary>
    public List<long> Keywords { get; } = [];

    /// <summary>Gets where the header of each cross-reference stream starts.</summary>
    public List<long> Streams { get; } = [];
}
