using System.Text;
using AdCodicem.Pdf.IO;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The copies of the tail of an object's endobj a search for a lost <c>endstream</c> keeps where a header a later read finds
/// may start: one taken at each place as the reads reach it, kept while the reads after carry a number that starts there, and
/// given back as the tail stood there, whatever the search read past it (#261).
/// </summary>
public class EndObjTailCopiesTests
{
    /// <summary>Where the data the tests read starts in the file.</summary>
    private const long DataStart = 100;

    [Fact]
    public void A_copy_is_taken_at_each_of_six_places_a_read_reaches_as_the_tail_stood_there()
    {
        // Six numbers, the most a read carries, start in one read: each copy has read the data up to its place, and no further,
        // an endobj seen or not as the data before it holds one.
        var data = Encoding.Latin1.GetBytes("abc 1 2 endobj\n3 4 5 6 xyz");
        long[] places = [DataStart + 4, DataStart + 6, DataStart + 15, DataStart + 17, DataStart + 19, DataStart + 21];
        Span<EndObjTail> room = stackalloc EndObjTail[HeaderText.MostNumbersCarried];
        Span<long> roomAt = stackalloc long[HeaderText.MostNumbersCarried];
        var copies = new EndObjTailCopies(room, roomAt);
        var tail = new EndObjTail(DataStart);

        copies.ReadTo(ref tail, data, DataStart, DataStart + data.Length, places);

        tail.UpTo.Should().Be(DataStart + data.Length);

        foreach (var place in places)
        {
            copies.TryGet(place, out var copy).Should().BeTrue();
            copy.UpTo.Should().Be(place);
            copy.Seen.Should().Be(place > DataStart + 8);
        }

        copies.TryGet(DataStart + 5, out _).Should().BeFalse("no number starts there");
    }

    [Fact]
    public void Copies_are_kept_for_the_places_the_next_read_still_carries_and_dropped_for_the_others()
    {
        // The first read takes copies at three places; the second carries the first and the third of them, and a place of its
        // own: the copies taken before are kept, as the tail stood then, the second is dropped, and the new place is copied.
        var data = Encoding.Latin1.GetBytes("1 2 3 endobj 4 5");
        Span<EndObjTail> room = stackalloc EndObjTail[HeaderText.MostNumbersCarried];
        Span<long> roomAt = stackalloc long[HeaderText.MostNumbersCarried];
        var copies = new EndObjTailCopies(room, roomAt);
        var tail = new EndObjTail(DataStart);

        copies.ReadTo(ref tail, data.AsSpan(0, 8), DataStart, DataStart + 6, [DataStart, DataStart + 2, DataStart + 4]);
        copies.ReadTo(ref tail, data.AsSpan(6), DataStart + 6, DataStart + data.Length, [DataStart, DataStart + 4, DataStart + 13]);

        copies.TryGet(DataStart, out var first).Should().BeTrue();
        first.UpTo.Should().Be(DataStart);
        copies.TryGet(DataStart + 2, out _).Should().BeFalse("the next read no longer carries the number there");
        copies.TryGet(DataStart + 4, out var third).Should().BeTrue();
        third.UpTo.Should().Be(DataStart + 4);
        third.Seen.Should().BeFalse();
        copies.TryGet(DataStart + 13, out var fourth).Should().BeTrue();
        fourth.UpTo.Should().Be(DataStart + 13);
        fourth.Seen.Should().BeTrue();
    }

    [Fact]
    public void A_place_the_tail_passed_with_no_copy_kept_there_is_given_no_copy()
    {
        // Every number a read carries from before it was carried into it, its copy kept: a place the tail passed that has none
        // is given none, whatever the room holds, however full, rather than a copy taken past it.
        var data = Encoding.Latin1.GetBytes("1 2 3 4 5 6 7 8");
        Span<EndObjTail> room = stackalloc EndObjTail[HeaderText.MostNumbersCarried];
        Span<long> roomAt = stackalloc long[HeaderText.MostNumbersCarried];
        var copies = new EndObjTailCopies(room, roomAt);
        var tail = new EndObjTail(DataStart);
        long[] six = [DataStart, DataStart + 2, DataStart + 4, DataStart + 6, DataStart + 8, DataStart + 10];

        copies.ReadTo(ref tail, data.AsSpan(0, 12), DataStart, DataStart + 12, six);
        copies.ReadTo(ref tail, data.AsSpan(12), DataStart + 12, DataStart + data.Length, [.. six[..5], DataStart + 11]);

        copies.TryGet(DataStart + 11, out _).Should().BeFalse();
        copies.TryGet(DataStart + 13, out _).Should().BeFalse();
        copies.TryGet(DataStart + 8, out var kept).Should().BeTrue();
        kept.UpTo.Should().Be(DataStart + 8);
    }

    [Fact]
    public void A_place_the_tail_passed_with_no_copy_kept_there_is_not_given_the_copy_kept_after_it()
    {
        // The first read takes a copy at the second number only, past an endobj; the next carries the first number too, which
        // the tail passed with no copy kept: it is given none, rather than the copy kept further on, which has seen an endobj
        // the data before the first number does not hold, and that copy stays at its own place.
        var data = Encoding.Latin1.GetBytes("1 endobj 2 3");
        Span<EndObjTail> room = stackalloc EndObjTail[HeaderText.MostNumbersCarried];
        Span<long> roomAt = stackalloc long[HeaderText.MostNumbersCarried];
        var copies = new EndObjTailCopies(room, roomAt);
        var tail = new EndObjTail(DataStart);

        copies.ReadTo(ref tail, data.AsSpan(0, 11), DataStart, DataStart + 10, [DataStart + 9]);
        copies.ReadTo(ref tail, data.AsSpan(10), DataStart + 10, DataStart + data.Length, [DataStart, DataStart + 9]);

        copies.TryGet(DataStart, out _).Should().BeFalse();
        copies.TryGet(DataStart + 9, out var kept).Should().BeTrue();
        kept.UpTo.Should().Be(DataStart + 9);
        kept.Seen.Should().BeTrue();
    }

    [Fact]
    public void A_place_given_twice_is_kept_once_and_the_copy_of_the_place_after_it_stays()
    {
        // The reader gives each place once; one given twice is kept once all the same: the copies are written over in place as
        // the places are looked at, and a second copy of the first place would take the room of the copy kept at the next one.
        var data = Encoding.Latin1.GetBytes("1 endobj 2 3");
        Span<EndObjTail> room = stackalloc EndObjTail[HeaderText.MostNumbersCarried];
        Span<long> roomAt = stackalloc long[HeaderText.MostNumbersCarried];
        var copies = new EndObjTailCopies(room, roomAt);
        var tail = new EndObjTail(DataStart);

        copies.ReadTo(ref tail, data.AsSpan(0, 11), DataStart, DataStart + 10, [DataStart, DataStart + 9]);
        copies.ReadTo(ref tail, data.AsSpan(10), DataStart + 10, DataStart + data.Length, [DataStart, DataStart, DataStart + 9]);

        copies.TryGet(DataStart, out var first).Should().BeTrue();
        first.UpTo.Should().Be(DataStart);
        first.Seen.Should().BeFalse();
        copies.TryGet(DataStart + 9, out var second).Should().BeTrue();
        second.UpTo.Should().Be(DataStart + 9);
        second.Seen.Should().BeTrue();
    }
}
