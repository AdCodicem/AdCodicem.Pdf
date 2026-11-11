using System.Text;
using AdCodicem.Pdf.IO;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Where a search for a lost <c>endstream</c> ends a stream's data, driven read by read as the search drives it: at the first
/// <c>endstream</c>, its end-of-line left out, unless an object header comes first (#174, #220, #261).
/// </summary>
public class StreamEndsTests
{
    /// <summary>Where the data starts in the file the tests read it from.</summary>
    private const long DataStart = 100;

    [Theory]
    [InlineData("12 % endstream\n0 obj", false, -1, 0)]
    [InlineData("12 % endstream\n0 obj", true, 5, -1)]
    [InlineData("12 % endstream\nobj", false, 5, -1)]
    [InlineData("\nendstream", false, 0, -1)]
    [InlineData("\nendstream", true, 0, -1)]
    [InlineData("\r\nendstream", false, 0, -1)]
    [InlineData("\r\nendstream", true, 1, -1)]
    [InlineData("x\rendstream", false, 1, -1)]
    [InlineData("x\rendstream", true, 2, -1)]
    public void The_data_ends_at_its_first_endstream_its_end_of_line_left_out_unless_a_header_comes_first(
        string data, bool deferred, long found, long header)
    {
        // An end-of-line before the keyword is not the data's, a line feed always and a carriage return but where the stream
        // is a cross-reference stream whose /Length the chain could not read, whose last row may end in one (#220). A header
        // whose tokens a comment holding the keyword parts stops the search first (#261), but where the stream is such a
        // cross-reference stream, which no header stops; an obj past the keyword's line that ends no header, one number
        // alone before it, leaves the keyword to end the data.
        var (end, at) = Search(data, deferred);

        end.Should().Be(found < 0 ? -1 : DataStart + found);
        at.Should().Be(header < 0 ? -1 : DataStart + header);
    }

    /// <summary>
    /// Searches <paramref name="data"/>, which starts at <see cref="DataStart"/>, in one read that ends where the search does,
    /// and gives where the data ends and where the header that stopped the search starts, -1 for either that is none.
    /// </summary>
    private static (long Found, long Header) Search(string data, bool deferred)
    {
        var bytes = Encoding.Latin1.GetBytes(data);
        Span<byte> carried = stackalloc byte[HeaderText.CarriedLength];
        Span<long> carriedAt = stackalloc long[HeaderText.CarriedLength];
        var text = new HeaderText(carried, carriedAt, DataStart);
        var pending = default(PendingEndStream);
        text.Read(bytes, DataStart);
        var found = StreamEnds.SearchStretch(text, endsRange: true, deferred, ref pending, out var header);
        return (found, header);
    }
}
