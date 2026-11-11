using AdCodicem.Pdf.IO;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// A source whose reads give fewer bytes than asked where a test says, before the end of the file as well as at it, as a
/// caller's source may: <see cref="PdfFileSource.Read"/> returns the count read, and the reader takes the bytes it is
/// given as all the file holds there (#52).
/// </summary>
/// <param name="data">The file.</param>
/// <param name="give">
/// How many bytes a read gives, from where it starts in the file and how many of those it asked for the file holds; a
/// count outside zero to that many is taken as the nearer bound.
/// </param>
internal sealed class ShortCountSource(byte[] data, Func<long, int, int> give) : PdfFileSource
{
    /// <summary>Gets how many reads gave fewer bytes than the file holds of what they asked for.</summary>
    public int ShortReads { get; private set; }

    /// <inheritdoc/>
    public override long Length => data.Length;

    /// <inheritdoc/>
    public override int Read(long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset >= data.Length)
        {
            return 0;
        }

        var held = (int)Math.Min(buffer.Length, data.Length - offset);
        var count = Math.Clamp(give(offset, held), 0, held);
        ShortReads += count < held ? 1 : 0;
        data.AsSpan((int)offset, count).CopyTo(buffer);
        return count;
    }
}
