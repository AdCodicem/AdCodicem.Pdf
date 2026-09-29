using AdCodicem.Pdf.IO.XRef;
using FsCheck;
using FsCheck.Fluent;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The offsets an index keeps sorted, so that a search for a stream's <c>endstream</c> stops at the next object without
/// a pass over every entry (#55).
/// </summary>
public class SortedOffsetsTests
{
    private const ulong Seed = 0x5EED_0055UL;

    /// <summary>The second half of FsCheck's random state, which must be odd.</summary>
    private const ulong Gamma = 0x9E37_79B9_7F4A_7C15UL;

    [Fact]
    public void The_first_offset_after_a_position_is_found_whatever_order_offsets_and_lookups_come_in()
    {
        // Each operation adds an offset — an even number, halved — or looks one up — an odd one; the answer is the smallest
        // added so far that is greater than the position, as a pass over them all would give it. Offsets are drawn from a
        // narrow range, so that they repeat.
        var operations = ArbMap.Default.ArbFor<short[]>().Filter(values => values is not null);

        Check.One(
            Config.QuickThrowOnFailure.WithMaxTest(300).WithReplay(Seed, Gamma).WithQuietOnSuccess(true),
            Prop.ForAll(operations, steps =>
            {
                var offsets = new SortedOffsets();
                var added = new List<long>();

                foreach (var step in steps)
                {
                    var value = step >> 1;

                    if ((step & 1) == 0)
                    {
                        offsets.Add(value);
                        added.Add(value);
                        continue;
                    }

                    var expected = long.MaxValue;

                    foreach (var offset in added)
                    {
                        if (offset > value && offset < expected)
                        {
                            expected = offset;
                        }
                    }

                    if (offsets.FirstAfter(value) != expected)
                    {
                        return false;
                    }
                }

                return true;
            }));
    }

    [Fact]
    public void Offsets_added_one_lookup_at_a_time_are_kept_in_a_logarithm_of_runs()
    {
        // The worst order for a sorted array — an offset, a lookup, another offset — rebuilds nothing whole: runs merge as
        // a binary counter carries.
        var offsets = new SortedOffsets();

        for (var i = 0; i < 100_000; i++)
        {
            offsets.Add((i * 7919L) % 100_003);
            offsets.FirstAfter(i).Should().BeGreaterThan(i);
        }

        offsets.RunCount.Should().BeLessThanOrEqualTo(17, "each run is more than twice the next, and 2^17 exceeds 100,000");
    }

    [Fact]
    public void An_offset_added_twice_is_found_once()
    {
        var offsets = new SortedOffsets();
        offsets.Add(10);
        offsets.Add(10);
        offsets.Add(30);

        offsets.FirstAfter(9).Should().Be(10);
        offsets.FirstAfter(10).Should().Be(30);
        offsets.FirstAfter(30).Should().Be(long.MaxValue);
    }

    [Fact]
    public void Offsets_given_at_once_in_any_order_are_kept_sorted_in_one_run()
    {
        // An index sorted whole the first time a search asks: its offsets in one array, sorted in place.
        var offsets = new SortedOffsets([30, 10, 20, 10]);

        offsets.RunCount.Should().Be(1);
        offsets.FirstAfter(0).Should().Be(10);
        offsets.FirstAfter(10).Should().Be(20);
        offsets.FirstAfter(20).Should().Be(30);
        offsets.FirstAfter(30).Should().Be(long.MaxValue);
        new SortedOffsets([]).FirstAfter(0).Should().Be(long.MaxValue, "an index with no offset places nothing");
    }

    [Fact]
    public void An_index_finds_the_offsets_of_objects_written_directly_and_keeps_them_up_to_date()
    {
        var index = new PdfXRefTable();
        index.TryAdd(1, XRefEntry.Regular(100, 0));
        index.TryAdd(2, XRefEntry.Compressed(9, 0));
        index.TryAdd(3, XRefEntry.Free);
        index.TryAdd(4, XRefEntry.Regular(300, 0));

        index.FirstOffsetAfter(50).Should().Be(100, "entries in object streams and free ones have no offset");
        index.FirstOffsetAfter(100).Should().Be(300);

        // Recorded after the first lookup: added, replaced, refused.
        index.TryAdd(5, XRefEntry.Regular(200, 0)).Should().BeTrue();
        index.Set(4, XRefEntry.Regular(250, 0));
        index.TryAdd(1, XRefEntry.Regular(150, 0)).Should().BeFalse();

        index.FirstOffsetAfter(100).Should().Be(200);
        index.FirstOffsetAfter(200).Should().Be(250);
        index.FirstOffsetAfter(250).Should().Be(300, "an entry replaced keeps the offset it gave");
        index.FirstOffsetAfter(300).Should().Be(long.MaxValue);

        index.Clear();
        index.TryAdd(6, XRefEntry.Regular(400, 0));
        index.FirstOffsetAfter(0).Should().Be(400, "a cleared index keeps nothing of what it held");
    }
}
