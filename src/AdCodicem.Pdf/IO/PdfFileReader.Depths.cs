namespace AdCodicem.Pdf.IO;

internal sealed partial class PdfFileReader
{
    /// <summary>
    /// Gives the key of what a read of object <paramref name="number"/> kept: from base <paramref name="cutBase"/>, where the
    /// loads' nesting or a transient cut reached it, or -1 where nothing did (<see cref="ReadDepth.CutBase"/>).
    /// </summary>
    private static long DepthKey(int number, int cutBase) => ((long)number << 32) | (uint)cutBase;

    /// <summary>Gives the object number <paramref name="key"/>, a <see cref="DepthKey"/>, keeps something of.</summary>
    private static int NumberOf(long key) => (int)(key >> 32);
}
