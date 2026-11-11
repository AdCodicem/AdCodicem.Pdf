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
    [InlineData("x 1111111111 2147483647 0 obj", 13, new[] { 0, 23 })]
    [InlineData("1234567890 1234567890\n0 obj", 11, new[] { 0, 22 })]
    [InlineData("1111111111 2147483647\n0 obj", 11, new[] { 0, 22 })]
    public void A_header_is_found_wherever_the_reads_part_its_lines_as_one_read_finds_it(string data, long header, int[] starts)
    {
        // Each read starts where the array says, and repeats the four bytes before the next read's start, so that obj and the
        // byte after it are whole in one read: the header, or none, is the one a single read of the data finds. Two runs of
        // ten digits, the most a run is carried as, fill the room carried for them and are carried whole, before the read or
        // before the line it starts in.
        var expected = header < 0 ? -1 : DataStart + header;

        FirstHeader(data, starts).Should().Be(expected);
        FirstHeader(data, [0]).Should().Be(expected);
    }

    [Theory]
    [InlineData("12 0 obj", 0)]
    [InlineData("% c\n0 obj", -1)]
    public void What_precedes_the_data_is_its_start_whatever_the_room_the_search_is_given_held(string data, long header)
    {
        // The room may be rented, or have served a search before: until a read is carried into it, it stands for the start of
        // the data alone, which a number after it starts a token at, and which a walk back past a comment line takes no number
        // from, whatever digits and positions it held.
        var bytes = Encoding.Latin1.GetBytes(data);
        Span<byte> carried = stackalloc byte[HeaderText.CarriedLength];
        Span<long> carriedAt = stackalloc long[HeaderText.CarriedLength];
        carried.Fill((byte)'7');
        carriedAt.Fill(7);
        var text = new HeaderText(carried, carriedAt, DataStart);
        text.Read(bytes, DataStart);

        text.FindHeader(bytes.Length, endsRange: true).Should().Be(header < 0 ? -1 : DataStart + header);
    }

    [Theory]
    [InlineData("2147483648", new int[0], 0)]
    [InlineData("2147483649", new int[0], 0)]
    [InlineData("99999999999999999999", new[] { 10 }, 0)]
    [InlineData("0002147483649", new[] { 3 }, 0)]
    [InlineData("x 2147483649 % c", new[] { 6 }, 2)]
    public void A_line_lexed_through_any_reads_takes_a_run_of_digits_past_any_object_number_for_the_first_value_past_it(
        string line, int[] cuts, long start)
    {
        // A header asks of a run of digits only whether it is an object number or a generation: past 2,147,483,647, the
        // largest object number, every run is one value, 2,147,483,648, however many digits it has, zeros lead it or reads
        // part it, and whether or not a comment follows it (#261).
        var bytes = Encoding.Latin1.GetBytes(line);
        var tokens = default(LineTokens);
        var from = 0;

        foreach (var cut in cuts)
        {
            tokens.Read(bytes.AsSpan(from, cut - from), from);
            from = cut;
        }

        tokens.Read(bytes.AsSpan(from), from);
        tokens.Finish();

        tokens.Last.Should().Be(new LineTokens.Token(LineTokens.Kind.Digits, 2_147_483_648, start));
    }

    [Theory]
    [InlineData("1 2\n3 4 % c 5 6", new[] { 0 }, new[] { 0, 2, 4, 6, 12, 14 })]
    [InlineData("1 2\n3 4 % 7 8 9", new[] { 0 }, new[] { 0, 2, 4, 6, 12, 14 })]
    [InlineData("12345 678", new[] { 0 }, new[] { 0, 6 })]
    [InlineData("x 12 0 % c", new[] { 0, 5 }, new[] { 2, 5 })]
    [InlineData("12\n% one\n% two\n0\n% three", new[] { 0, 3, 9, 15, 17 }, new[] { 0, 15 })]
    [InlineData("(a) /b <41>", new[] { 0 }, new int[0])]
    [InlineData("(5 6", new[] { 0 }, new[] { 1, 3 })]
    [InlineData("x 12 y z ", new[] { 0, 4 }, new int[0])]
    [InlineData("123 456\ny\n", new[] { 0, 8 }, new int[0])]
    public void A_read_gives_where_each_number_it_carries_into_the_next_starts_once_the_nearest_the_data_s_start_first(
        string data, int[] starts, int[] carried)
    {
        // A header the next reads find that starts before them starts at a number the reads carry, where the search keeps the
        // tail of the object's endobj as it stood (#261): the runs of digits at the read's end, the numbers before the line it
        // ends in, and that line's last two tokens. Six at most: two runs and two numbers here, a third run of digits, the
        // byte before the runs, no number starts at; every digit of a number names its start. What a read carried before
        // and the next no longer carries is not given, however much of it the room still holds.
        CarriedStarts(data, starts).Should().Equal(carried.Select(start => DataStart + start));
    }

    [Fact]
    public void A_byte_carried_into_a_read_is_placed_where_its_run_starts_and_a_byte_of_the_read_where_it_lies()
    {
        // 12, three spaces and 0, read and carried into the read after them: counted back from that read's first byte come
        // the 0, the spaces as one, 12's digits and the space that stands for the start of the data, each placed where the
        // run it stands for starts in the file; the read's own bytes are placed where they lie.
        var bytes = Encoding.Latin1.GetBytes("12   0 obj");
        Span<byte> carried = stackalloc byte[HeaderText.CarriedLength];
        Span<long> carriedAt = stackalloc long[HeaderText.CarriedLength];
        var text = new HeaderText(carried, carriedAt, DataStart);
        text.Read(bytes.AsSpan(0, 6), DataStart);
        text.CarryTo(DataStart + 6);
        text.Read(bytes.AsSpan(6), DataStart + 6);
        var positions = new long[7];

        for (var index = -5; index < 2; index++)
        {
            positions[index + 5] = text.PositionOf(index) - DataStart;
        }

        positions.Should().Equal(0, 0, 0, 2, 5, 6, 7);
    }

    [Theory]
    [InlineData("12\nx\n0 obj", 5)]
    [InlineData("12\nx\n 0 obj", 6)]
    public void What_precedes_a_line_after_one_ending_in_no_number_is_carried_placed_nowhere_with_white_space_it_starts_with(
        string data, int last)
    {
        // 12 ends the first line, and the read after it carries 12's place for what precedes the second line. The second ends
        // in x, no number: counted back from the first byte of the read the third line starts in come a space for the end of
        // line before that line, as one run with any white space the line starts with, and the space before it, which no
        // number places. Each is placed at -1, nowhere in the file, whatever places their room held from the reads before.
        var bytes = Encoding.Latin1.GetBytes(data);
        Span<byte> carried = stackalloc byte[HeaderText.CarriedLength];
        Span<long> carriedAt = stackalloc long[HeaderText.CarriedLength];
        var text = new HeaderText(carried, carriedAt, DataStart);
        text.Read(bytes.AsSpan(0, 3), DataStart);
        text.CarryTo(DataStart + 3);
        text.Read(bytes.AsSpan(3, last - 3), DataStart + 3);
        text.CarryTo(DataStart + last);
        text.Read(bytes.AsSpan(last), DataStart + last);

        new[] { text.PositionOf(-2), text.PositionOf(-1), text.PositionOf(0) }.Should().Equal(-1, -1, DataStart + last);
    }

    [Fact]
    public void A_byte_carried_across_an_end_of_line_is_placed_alike_whatever_the_room_the_search_is_given_held()
    {
        // 1 and 2 end a line, and white space starts the next, where the read after them starts: the runs carried into that
        // read are read back through the two numbers carried before the line, each written with a space after it, and the
        // line's white space runs back into the space after 2. The room may be rented, or have served a search before: every
        // byte carried is placed from what the reads held, none from what the room held.
        CarriedPositions("1 2\n  ", held: 0).Should().Equal(CarriedPositions("1 2\n  ", held: 7));
    }

    /// <summary>
    /// Gives where each number <paramref name="data"/>, which starts at <see cref="DataStart"/>, carries into a read after its
    /// end, read a part at a time as <see cref="FirstHeader"/> reads it.
    /// </summary>
    private static long[] CarriedStarts(string data, int[] starts)
    {
        var bytes = Encoding.Latin1.GetBytes(data);
        Span<byte> carried = stackalloc byte[HeaderText.CarriedLength];
        Span<long> carriedAt = stackalloc long[HeaderText.CarriedLength];
        Span<long> found = stackalloc long[HeaderText.MostNumbersCarried];
        var text = new HeaderText(carried, carriedAt, DataStart);

        for (var read = 0; read < starts.Length; read++)
        {
            var start = starts[read];
            var end = read + 1 < starts.Length ? Math.Min(bytes.Length, starts[read + 1] + 4) : bytes.Length;
            text.Read(bytes.AsSpan(start, end - start), DataStart + start);
            text.CarryTo(DataStart + (read + 1 < starts.Length ? starts[read + 1] : bytes.Length));
        }

        return found[..text.CarriedStarts(found)].ToArray();
    }

    /// <summary>
    /// Gives where each of the five bytes <paramref name="data"/>, which starts at <see cref="DataStart"/>, carries into the
    /// read after its end is placed in the file, the room for the places carried holding <paramref name="held"/> throughout
    /// before the search starts.
    /// </summary>
    private static long[] CarriedPositions(string data, long held)
    {
        var bytes = Encoding.Latin1.GetBytes(data);
        Span<byte> carried = stackalloc byte[HeaderText.CarriedLength];
        Span<long> carriedAt = stackalloc long[HeaderText.CarriedLength];
        carriedAt.Fill(held);
        var text = new HeaderText(carried, carriedAt, DataStart);
        text.Read(bytes, DataStart);
        text.CarryTo(DataStart + bytes.Length);
        text.Read("0 obj"u8, DataStart + bytes.Length);
        var positions = new long[5];

        for (var index = -5; index < 0; index++)
        {
            positions[index + 5] = text.PositionOf(index);
        }

        return positions;
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
