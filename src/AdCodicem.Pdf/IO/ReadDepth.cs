namespace AdCodicem.Pdf.IO;

/// <summary>
/// How deep a read went, kept with what it gave — an object, an object stream's contents, an exception — so that it serves
/// a later read only where that read, made then, would give the same (#163).
/// </summary>
/// <remarks>
/// The loads a read makes nest under the loads already in flight when it starts, its base: a read whose loads went
/// <see cref="Extent"/> levels below its base, none of them cut, gives the same from any base that leaves those levels
/// within <see cref="Documents.PdfReaderLimits.MaxNestedLoads"/>. One the bound cut, or the thread's stack, gives what it
/// gave only from the base it was made from, the cut falling at the same load.
/// </remarks>
/// <param name="Extent">How many levels below its base the read's loads went.</param>
/// <param name="CutBase">The base of a read the loads' nesting or the stack cut on its way; -1 for one nothing cut.</param>
internal readonly record struct ReadDepth(int Extent, int CutBase)
{
    /// <summary>The depth of what nothing nests: a read that made no load, or one kept before depths were.</summary>
    public static ReadDepth None { get; } = new(0, -1);

    /// <summary>
    /// Gets a value indicating whether the thread's stack cut the read: what it gave is kept for the read in progress
    /// alone, another thread, or the same with more room, reading otherwise.
    /// </summary>
    public bool ByStack { get; init; }

    /// <summary>
    /// Determines whether what a read kept at this depth serves a read made from <paramref name="depthBase"/> loads in flight,
    /// under a nesting of <paramref name="bound"/> loads.
    /// </summary>
    public bool Serves(int depthBase, int bound) => CutBase >= 0 ? CutBase == depthBase : Extent <= bound - depthBase;
}
