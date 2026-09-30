using System.Globalization;
using System.Text.RegularExpressions;

namespace AdCodicem.Pdf.IntegrationTests;

/// <summary>
/// Reads the index <c>qpdf --show-xref</c> prints, and finds in a file's bytes the copy of an object it places, for the
/// referees that compare one object as qpdf and the reader each read it.
/// </summary>
internal static partial class QpdfIndex
{
    /// <summary>
    /// Returns each object the index places in the file itself, rather than in an object stream: its number, its
    /// generation, and where its header lies, counted from the start of the file as the reader counts.
    /// </summary>
    /// <remarks>
    /// A rebuilt index can list one number under two generations — groff's object 304, NUREG's 284 and 285 —: each is
    /// an entry of its own, and the one to ask qpdf for is the one whose copy the reader read (<see cref="DataStartsAt"/>).
    /// </remarks>
    /// <param name="xref">What <c>qpdf --show-xref</c> printed.</param>
    /// <param name="bytes">The file's bytes, whose <c>%PDF-</c> qpdf counts its offsets from.</param>
    public static List<(int Number, int Generation, long Offset)> Entries(string xref, byte[] bytes)
    {
        var header = HeaderOffset(bytes);

        return UncompressedEntry().Matches(xref)
            .Select(match => (
                Number: int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                Generation: int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                Offset: header + long.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)))
            .ToList();
    }

    /// <summary>
    /// Determines whether the object whose header starts at <paramref name="objectOffset"/> is the stream whose data
    /// starts at <paramref name="dataStart"/>: the first <c>stream</c> keyword after its header, and the end-of-line after
    /// it, end there.
    /// </summary>
    public static bool DataStartsAt(byte[] bytes, long objectOffset, long dataStart)
    {
        if (objectOffset < 0 || objectOffset >= dataStart || dataStart > bytes.Length)
        {
            return false;
        }

        var span = bytes.AsSpan((int)objectOffset, (int)(dataStart - objectOffset));
        var searchFrom = 0;

        while (searchFrom < span.Length)
        {
            var index = span[searchFrom..].IndexOf("stream"u8);

            if (index < 0)
            {
                return false;
            }

            var afterKeyword = searchFrom + index + "stream".Length;

            if (afterKeyword < span.Length && span[afterKeyword] is (byte)'\r' or (byte)'\n')
            {
                return span.Length - afterKeyword is 1 or 2;
            }

            searchFrom = afterKeyword;
        }

        return false;
    }

    /// <summary>Where <c>%PDF-</c> starts, in the first kilobyte as readers look for it: qpdf's offsets count from there.</summary>
    public static long HeaderOffset(byte[] bytes)
    {
        var header = bytes.AsSpan(0, Math.Min(bytes.Length, 1024)).IndexOf("%PDF-"u8);
        return Math.Max(header, 0);
    }

    [GeneratedRegex(@"^(\d+)/(\d+): uncompressed; offset = (\d+)", RegexOptions.Multiline)]
    private static partial Regex UncompressedEntry();
}
