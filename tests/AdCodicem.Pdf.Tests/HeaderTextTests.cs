using System.Text;
using AdCodicem.Pdf.IO;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The rule for object headers that a search for a lost <c>endstream</c> stops at, driven read by read as the search drives
/// it: what each read carries into the next — runs of white space and digits, what precedes the line the next read starts
/// in, that line lexed so far — decides every header as one read of all the data does (#174, #261).
/// </summary>
public class HeaderTextTests
{
    /// <summary>Where the data starts in the file the tests read it from.</summary>
    private const long DataStart = 100;

    [Theory]
    [InlineData("12 0\n\n\n\n\n\nobj", 0, new[] { 0, 5 })]
    [InlineData("12 0\nxx 4\n0 obj", 8, new[] { 0, 5, 10 })]
    [InlineData("x12\n\n\n\n\n0 obj", -1, new[] { 0, 4 })]
    [InlineData("(12\n\n\n\n\n0 obj", 1, new[] { 0, 4 })]
    [InlineData("12\nxx\n0 obj", -1, new[] { 0, 3 })]
    [InlineData("12 % a comment\n0 obj", 0, new[] { 0, 6 })]
    [InlineData("12 % a comment that runs on\n0 obj", 0, new[] { 0, 6, 12, 18 })]
    [InlineData("12 0 % a comment\nobj", 0, new[] { 0, 3, 8 })]
    [InlineData("12 (a % string\n0 obj", -1, new[] { 0, 6, 9 })]
    [InlineData("12 <41 % 42\n0 obj", -1, new[] { 0, 6 })]
    [InlineData("12 >\n> % c\n0 obj", -1, new[] { 0, 4, 6 })]
    [InlineData("12\n% a comment\n0 obj", 0, new[] { 0, 3, 8 })]
    [InlineData("12\n% one\n% two\n0\n% three\nobj", 0, new[] { 0, 3, 9, 15, 17, 23 })]
    [InlineData("12\r% one\r0 % two\robj", 0, new[] { 0, 3, 8, 13 })]
    [InlineData("4 % c\n12 0 obj", 6, new[] { 0, 6 })]
    [InlineData("4 % c\n\n12\n0 obj", 7, new[] { 0, 6, 7 })]
    [InlineData("% 7\n0 obj", -1, new[] { 0, 2 })]
    [InlineData("00012 00 % c\nobj", 0, new[] { 0, 2, 5 })]
    [InlineData("012 0 % c\nobj", 0, new[] { 0, 2 })]
    [InlineData("007 0 % c\nobj", 0, new[] { 0, 1 })]
    [InlineData("12 70000 % c\nobj", -1, new[] { 0, 4 })]
    [InlineData("12 999999999999 % c\nobj", -1, new[] { 0, 5 })]
    [InlineData("% a\n12 % c\n0 obj", 4, new[] { 0, 3 })]
    [InlineData("x\n(%) 12\n0 obj", 6, new[] { 0 })]
    [InlineData("x\n(%) 12x\n0 obj", -1, new[] { 0 })]
    [InlineData("(a\\) b) 12 % c\n0 obj", 8, new[] { 0 })]
    [InlineData("(a\\) b) 12 % c\n0 obj", 8, new[] { 0, 3 })]
    [InlineData("(a\\nb) 12 % c\n0 obj", 7, new[] { 0, 3 })]
    [InlineData("5 12 % c\n0 obj", 2, new[] { 0 })]
    public void A_header_is_found_wherever_the_reads_part_its_lines_as_one_read_finds_it(string data, long header, int[] starts)
    {
        // Each read starts where the array says, and repeats the four bytes before the next read's start, so that obj and the
        // byte after it are whole in one read: the header, or none, is the one a single read of the data finds.
        var expected = header < 0 ? -1 : DataStart + header;

        FirstHeader(data, starts).Should().Be(expected);
        FirstHeader(data, [0]).Should().Be(expected);
    }

    /// <summary>
    /// Gives where the first object header of <paramref name="data"/> starts, which starts at <see cref="DataStart"/>, read a
    /// part at a time: each from where <paramref name="starts"/> says to four bytes past where the next starts, the last to
    /// the end of the data, each carrying into the next what it holds; -1 when there is none.
    /// </summary>
    internal static long FirstHeader(string data, int[] starts)
    {
        var bytes = Encoding.Latin1.GetBytes(data);
        Span<byte> carried = stackalloc byte[HeaderText.CarriedLength];
        Span<long> carriedAt = stackalloc long[HeaderText.CarriedLength];
        var text = new HeaderText(carried, carriedAt, DataStart);

        for (var read = 0; read < starts.Length; read++)
        {
            var start = starts[read];
            var end = read + 1 < starts.Length ? Math.Min(bytes.Length, starts[read + 1] + 4) : bytes.Length;
            text.Read(bytes.AsSpan(start, end - start), DataStart + start);
            var header = text.FindHeader(end - start, endsRange: end == bytes.Length);

            if (header >= 0 || end == bytes.Length)
            {
                return header;
            }

            text.CarryTo(DataStart + starts[read + 1]);
        }

        return -1;
    }
}
