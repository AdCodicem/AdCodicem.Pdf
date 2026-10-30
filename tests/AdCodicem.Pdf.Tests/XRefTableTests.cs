using AdCodicem.Pdf.IO.XRef;
using FsCheck;
using FsCheck.Fluent;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The room the index makes for the rows a section is about to give, before it records them (#164), how it finds a
/// number's entry, in a slot or in its seeded map (#155), and what an enumeration of its numbers keeps through a rebuild
/// (#167).
/// </summary>
public class XRefTableTests
{
    [Fact]
    public void Room_is_made_for_the_rows_asked_into_an_empty_table()
    {
        var table = new PdfXRefTable();

        table.Reserve(1000, int.MaxValue);

        Capacity(table).Should().BeInRange(1000, 1199);
    }

    [Fact]
    public void Room_for_rows_the_table_holds_room_for_already_changes_nothing()
    {
        var table = Filled(1000);
        var capacity = Capacity(table);

        table.Reserve(capacity - 1000, int.MaxValue);
        table.Reserve(0, int.MaxValue);
        table.Reserve(-5, int.MaxValue);

        Capacity(table).Should().Be(capacity);
    }

    [Fact]
    public void Room_for_a_few_rows_more_than_the_table_holds_doubles_it_rather_than_growing_it_by_them()
    {
        // A chain of many sections each making room for its own rows would otherwise resize the index at each one.
        var table = Filled(1000);
        var capacity = Capacity(table);

        table.Reserve(capacity - 1000 + 1, int.MaxValue);

        Capacity(table).Should().BeGreaterThanOrEqualTo(2 * capacity);
    }

    [Fact]
    public void Room_is_made_for_no_more_entries_than_the_most_asked()
    {
        var table = Filled(1000);
        var capacity = Capacity(table);

        table.Reserve(capacity, capacity + 10);

        Capacity(table).Should().BeInRange(capacity + 10, (2 * capacity) - 1, "the room doubles no further than the most asked");
    }

    [Fact]
    public void Room_for_more_rows_than_doubling_gives_is_made_for_them_all()
    {
        var table = Filled(10);

        table.Reserve(100_000, int.MaxValue);

        Capacity(table).Should().BeGreaterThanOrEqualTo(100_010);
    }

    [Fact]
    public void Numbers_written_in_order_and_numbers_spread_out_are_found_alike()
    {
        // Numbers from 0 up take slots; one far past them goes to the map; both are found, and a number neither holds is not.
        var table = Filled(100);
        table.TryAdd(2_000_000_000, XRefEntry.Regular(42, 3)).Should().BeTrue();

        table.TryGet(57, out var near).Should().BeTrue();
        near.Should().Be(XRefEntry.Free);
        table.TryGet(2_000_000_000, out var far).Should().BeTrue();
        far.Should().Be(XRefEntry.Regular(42, 3));
        table.TryGet(100, out _).Should().BeFalse();
        table.TryGet(1_999_999_999, out _).Should().BeFalse();
        table.TryGet(-1, out _).Should().BeFalse();
        table.Count.Should().Be(101);
    }

    [Fact]
    public void A_number_is_recorded_once_and_replaced_in_its_place()
    {
        var table = new PdfXRefTable();

        table.TryAdd(5, XRefEntry.Free).Should().BeTrue();
        table.TryAdd(5_000_000, XRefEntry.Free).Should().BeTrue();
        table.TryAdd(5, XRefEntry.Regular(10, 0)).Should().BeFalse("the first definition seen is the current one");
        table.TryAdd(5_000_000, XRefEntry.Regular(10, 0)).Should().BeFalse();
        table.Set(5, XRefEntry.Regular(20, 1));
        table.Set(5_000_000, XRefEntry.Regular(30, 2));
        table.Set(6, XRefEntry.Regular(40, 0));

        table.Should().Equal(
            new KeyValuePair<int, XRefEntry>(5, XRefEntry.Regular(20, 1)),
            new KeyValuePair<int, XRefEntry>(5_000_000, XRefEntry.Regular(30, 2)),
            new KeyValuePair<int, XRefEntry>(6, XRefEntry.Regular(40, 0)));
        table.Numbers.Should().Equal(5, 5_000_000, 6);
        table.CopyNumbers().Should().Equal(5, 5_000_000, 6);
    }

    [Fact]
    public void Numbers_the_map_held_move_into_the_slots_once_these_reach_them()
    {
        // A chain whose newest section numbers the highest objects: they go to the map while the slots are short of them,
        // then the older sections' numbers, from 0 up, stretch the slots past them. Each stays found, in the order recorded.
        var table = new PdfXRefTable();

        for (var number = 3_000; number < 3_010; number++)
        {
            table.TryAdd(number, XRefEntry.Regular(number, 0));
        }

        for (var number = 0; number < 3_000; number++)
        {
            table.TryAdd(number, XRefEntry.Regular(number, 0));
        }

        for (var number = 0; number < 3_010; number++)
        {
            table.TryGet(number, out var entry).Should().BeTrue();
            entry.Offset.Should().Be(number);
        }

        table.MapCount.Should().Be(0, "the slots reached every number the map held");
        table.SlotCount.Should().BeGreaterThan(3_009);
        table.CopyNumbers()[..10].Should().Equal(Enumerable.Range(3_000, 10));
        table.TryAdd(3_005, XRefEntry.Free).Should().BeFalse("a number moved into its slot is still held");
    }

    [Fact]
    public void Numbers_spread_further_apart_than_the_slots_stretch_stay_in_the_map()
    {
        // Four thousand numbers 6 apart: the slots stop where they would hold more than twice the entries held or made room
        // for, and the slack, and the numbers past them are found through the map.
        const int Apart = 6;
        var table = new PdfXRefTable();

        for (var k = 0; k < 4_000; k++)
        {
            table.TryAdd(Apart * k, XRefEntry.Regular(k, 0));
        }

        for (var k = 0; k < 4_000; k++)
        {
            table.TryGet(Apart * k, out var entry).Should().BeTrue();
            entry.Offset.Should().Be(k);
            table.TryGet((Apart * k) + 1, out _).Should().BeFalse();
        }

        var bound = (2 * Math.Max(table.Count, table.Capacity)) + PdfXRefTable.DenseSlack;
        table.SlotCount.Should().BeLessThanOrEqualTo(bound);
        table.MapCount.Should().Be(Enumerable.Range(0, 4_000).Count(k => Apart * k >= table.SlotCount), "the numbers past the slots are in the map");
        table.MapCount.Should().BeGreaterThan(1_000);
    }

    [Fact]
    public void A_map_the_slots_come_to_reach_gives_back_the_room_of_the_numbers_that_leave_it()
    {
        // A newest section numbering far past the rest: 5,000 numbers go to the map, and one more lies past any slot. Room
        // made for the older rows stretches the slots over the 5,000, which leave the map; removed in place, they left it
        // room for 5,000 numbers to hold one.
        var table = new PdfXRefTable();
        table.TryAdd(2_000_000_000, XRefEntry.Free);

        for (var number = 100_000; number < 105_000; number++)
        {
            table.TryAdd(number, XRefEntry.Regular(number, 0));
        }

        table.MapCount.Should().Be(5_001);
        table.Reserve(110_000, int.MaxValue);
        table.TryAdd(0, XRefEntry.Free);

        table.MapCount.Should().Be(1, "the slots reach every number but the farthest");
        table.MapCapacity.Should().BeLessThan(8, "the map keeps room for the number it holds");

        for (var number = 100_000; number < 105_000; number++)
        {
            table.TryGet(number, out var entry).Should().BeTrue();
            entry.Offset.Should().Be(number);
        }

        table.TryGet(2_000_000_000, out _).Should().BeTrue();
    }

    [Fact]
    public void A_map_most_of_whose_numbers_stay_keeps_its_room()
    {
        // A hundred numbers leave a map of five thousand: copying those that stay at every stretch would cost more than it
        // gives back.
        var table = new PdfXRefTable();

        for (var number = 17_000; number < 17_100; number++)
        {
            table.TryAdd(number, XRefEntry.Regular(number, 0));
        }

        for (var number = 100_000; number < 104_900; number++)
        {
            table.TryAdd(number, XRefEntry.Regular(number, 0));
        }

        table.MapCount.Should().Be(5_000);
        var room = table.MapCapacity;
        table.TryAdd(17_300, XRefEntry.Free);

        table.MapCount.Should().Be(4_900, "the slots reach the hundred numbers below 17,301");
        table.MapCapacity.Should().Be(room);
        table.TryGet(17_050, out var moved).Should().BeTrue();
        moved.Offset.Should().Be(17_050);
    }

    [Fact]
    public void Room_made_for_rows_makes_their_slots_once()
    {
        // Room for a thousand rows numbered from 0 gives them a thousand slots, made once, rather than slots doubled from
        // one up to 1,024.
        var table = Filled(1000);

        table.SlotCount.Should().Be(1000);
        table.MapCount.Should().Be(0);
        table.MapCapacity.Should().Be(0, "no number lies past the slots, so no map is made");
    }

    [Fact]
    public void A_table_enumerates_through_every_interface_it_implements()
    {
        var table = Filled(3);
        var enumerator = ((System.Collections.IEnumerable)table).GetEnumerator();

        enumerator.MoveNext().Should().BeTrue();
        enumerator.Current.Should().Be(new KeyValuePair<int, XRefEntry>(0, XRefEntry.Free));
        enumerator.MoveNext().Should().BeTrue();
        enumerator.Reset();
        enumerator.MoveNext().Should().BeTrue();
        enumerator.Current.Should().Be(new KeyValuePair<int, XRefEntry>(0, XRefEntry.Free));
        ((IEnumerable<KeyValuePair<int, XRefEntry>>)table).Select(pair => pair.Key).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void An_enumeration_ends_when_a_number_is_added_or_the_table_cleared_and_not_when_an_entry_is_replaced()
    {
        var table = Filled(3);

        var replaced = () =>
        {
            foreach (var (number, _) in table)
            {
                table.Set(number, XRefEntry.Regular(number, 0));
            }
        };
        var added = () =>
        {
            foreach (var (number, _) in table)
            {
                table.Set(number + 10, XRefEntry.Free);
            }
        };
        var cleared = () =>
        {
            foreach (var _ in table)
            {
                table.Clear();
            }
        };

        replaced.Should().NotThrow();
        added.Should().Throw<InvalidOperationException>();
        cleared.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void An_enumeration_of_the_numbers_gives_those_the_table_held_at_its_first_step_whatever_is_added_or_cleared_meanwhile()
    {
        // A rebuild clears the index and fills it again while a caller may be enumerating its numbers (#167): the
        // enumeration reads those it began with, where they lie, though the numbers recorded since fill the room past
        // where it stands; and the next one reads those recorded since.
        var table = Filled(5);
        var cleared = new List<int>();

        foreach (var number in table.Numbers)
        {
            cleared.Add(number);

            if (number == 1)
            {
                table.Clear();

                foreach (var refilled in (int[])[9, 8, 7, 6])
                {
                    table.TryAdd(refilled, XRefEntry.Free);
                }
            }
        }

        var added = new List<int>();

        foreach (var number in table.Numbers)
        {
            added.Add(number);
            table.TryAdd(number + 100, XRefEntry.Free);
        }

        cleared.Should().Equal(0, 1, 2, 3, 4);
        added.Should().Equal(9, 8, 7, 6);
        table.Numbers.Should().Equal(9, 8, 7, 6, 109, 108, 107, 106);
    }

    [Fact]
    public void An_enumeration_begun_once_the_table_was_cleared_under_another_keeps_its_numbers_through_the_next_clearing()
    {
        // The first enumeration reads an array the table stopped recording into when it was cleared: ending it leaves the
        // second, which reads the table's new array, counted as under way, and the next clearing makes it a new array too
        // (#167).
        var table = Filled(4);
        var first = table.Numbers.GetEnumerator();
        first.MoveNext().Should().BeTrue();
        table.Clear();

        foreach (var refilled in (int[])[10, 11, 12, 13])
        {
            table.TryAdd(refilled, XRefEntry.Free);
        }

        first.Dispose();
        var second = new List<int>();

        foreach (var number in table.Numbers)
        {
            second.Add(number);

            if (number == 10)
            {
                table.Clear();

                foreach (var refilled in (int[])[20, 21, 22, 23])
                {
                    table.TryAdd(refilled, XRefEntry.Free);
                }
            }
        }

        second.Should().Equal(10, 11, 12, 13);
        table.Numbers.Should().Equal(20, 21, 22, 23);
    }

    [Fact]
    public void Every_enumeration_of_the_numbers_gives_those_the_table_held_at_its_first_step_whatever_happens_between_its_steps()
    {
        // Numbers added and replaced, the table cleared or released, enumerations begun, stepped and left between each
        // other's steps: each gives the numbers, in their order, that the table held at its first step, and no other (#167).
        var operations = Gen.ListOf(Gen.Zip(Gen.Choose(0, 6), Gen.Choose(0, 40))).Select(drawn => drawn.ToArray());

        Check.One(PropertySettings, Prop.ForAll(operations.ToArbitrary(), drawn =>
        {
            var table = new PdfXRefTable();
            var held = new List<int>();
            var open = new List<(IEnumerator<int> Numbers, List<int> Expected, List<int> Given)>();
            var finished = new List<(List<int> Expected, List<int> Given)>();

            foreach (var (operation, value) in drawn)
            {
                switch (operation)
                {
                    case 0:
                        if (table.TryAdd(value, XRefEntry.Free))
                        {
                            held.Add(value);
                        }

                        break;

                    case 1:
                        // Replaced in its place, or added last.
                        if (!held.Contains(value))
                        {
                            held.Add(value);
                        }

                        table.Set(value, XRefEntry.Regular(value, 0));
                        break;

                    case 2:
                        table.Clear();
                        held.Clear();
                        break;

                    case 3:
                        table.Release();
                        held.Clear();
                        break;

                    case 4:
                        open.Add((table.Numbers.GetEnumerator(), [.. held], []));
                        Step(open.Count - 1);
                        break;

                    case 5 when open.Count > 0:
                        Step(value % open.Count);
                        break;

                    case 6 when open.Count > 0:
                        // Left before its end, and disposed.
                        var left = open[value % open.Count];
                        left.Numbers.Dispose();
                        open.Remove(left);
                        finished.Add((left.Expected[..left.Given.Count], left.Given));
                        break;
                }
            }

            while (open.Count > 0)
            {
                Step(0);
            }

            return finished.TrueForAll(each => each.Given.SequenceEqual(each.Expected));

            void Step(int at)
            {
                var (numbers, expected, given) = open[at];

                if (numbers.MoveNext())
                {
                    given.Add(numbers.Current);
                    return;
                }

                numbers.Dispose();
                open.RemoveAt(at);
                finished.Add((expected, given));
            }
        }));
    }

    [Fact]
    public void Clearing_the_table_makes_a_new_array_for_its_numbers_only_while_an_enumeration_of_them_is_under_way()
    {
        // The array an enumeration reads is left to it, and the table records its numbers in a new one as long (#167): only
        // while one is under way, so that clearing allocates nothing otherwise.
        Allocated(() => Filled(1000), Clear).Should().Be(0, "no enumeration began");
        Allocated(() => Enumerated(Filled(1000)), Clear).Should().Be(0, "the enumeration ended");

        var made = Allocations.Least<PdfXRefTable, int>(() => UnderWay(Filled(1000)), Clear, out var table, out _);

        made.Should().BeInRange(4L * table.Capacity, (4L * table.Capacity) + 32, "an array of the room, for the numbers");
        Allocated(() => ClearedUnderWay(Filled(1000)), Clear).Should().Be(0, "the array the first clearing made is lent to none");
        Allocated(() => ResizedUnderWay(Filled(1000)), Clear)
            .Should().Be(0, "the array the enumeration read was replaced as the room grew, and the new one is lent to none");
        Allocated(() => UnderWay(Filled(1000)), Release).Should().Be(0, "a table released lets go of its arrays, and makes none");

        static long Allocated(Func<PdfXRefTable> prepare, Func<PdfXRefTable, int> work) =>
            Allocations.Least(prepare, work, out _, out _);

        static int Clear(PdfXRefTable table)
        {
            table.Clear();
            return table.Count;
        }

        static int Release(PdfXRefTable table)
        {
            table.Release();
            return table.Count;
        }

        static PdfXRefTable Enumerated(PdfXRefTable table)
        {
            foreach (var _ in table.Numbers)
            {
            }

            return table;
        }

        static PdfXRefTable UnderWay(PdfXRefTable table)
        {
            // Left under way, never disposed: it may step again for as long as it is held.
            table.Numbers.GetEnumerator().MoveNext().Should().BeTrue();
            return table;
        }

        static PdfXRefTable ClearedUnderWay(PdfXRefTable table)
        {
            using (var numbers = table.Numbers.GetEnumerator())
            {
                numbers.MoveNext().Should().BeTrue();
                table.Clear();
                table.TryAdd(7, XRefEntry.Free);
            }

            return table;
        }

        static PdfXRefTable ResizedUnderWay(PdfXRefTable table)
        {
            using (var numbers = table.Numbers.GetEnumerator())
            {
                numbers.MoveNext().Should().BeTrue();
                var room = table.Capacity;

                for (var number = table.Count; number <= room; number++)
                {
                    table.TryAdd(number, XRefEntry.Free);
                }
            }

            return table;
        }
    }

    [Fact]
    public void A_copy_holds_the_entries_and_none_added_after_it()
    {
        var table = Filled(3);
        table.TryAdd(1_000_000, XRefEntry.Regular(7, 0));

        var copy = table.CopyEntries();
        table.Set(1, XRefEntry.Regular(9, 0));
        table.TryAdd(2_000_000, XRefEntry.Free);

        copy.Should().Equal(
            new KeyValuePair<int, XRefEntry>(0, XRefEntry.Free),
            new KeyValuePair<int, XRefEntry>(1, XRefEntry.Free),
            new KeyValuePair<int, XRefEntry>(2, XRefEntry.Free),
            new KeyValuePair<int, XRefEntry>(1_000_000, XRefEntry.Regular(7, 0)));
        copy.Capacity.Should().Be(4, "a copy is sized to what it holds");
        copy.SlotCount.Should().Be(3, "its slots reach the highest number they hold, and no further");
        copy.MapCount.Should().Be(1);
        copy.TryGet(2_000_000, out _).Should().BeFalse();
        copy.TryAdd(5, XRefEntry.Free).Should().BeTrue("a copy grows as the table does");
    }

    [Fact]
    public void Cleared_the_table_holds_nothing_and_keeps_its_room_released_it_keeps_none()
    {
        var table = Filled(1000);
        table.TryAdd(5_000_000, XRefEntry.Free);
        var capacity = Capacity(table);

        table.Clear();

        table.Count.Should().Be(0);
        table.TryGet(7, out _).Should().BeFalse();
        table.TryGet(5_000_000, out _).Should().BeFalse();
        Capacity(table).Should().Be(capacity);
        table.TryAdd(7, XRefEntry.Free).Should().BeTrue();

        table.Release();

        table.Count.Should().Be(0);
        Capacity(table).Should().Be(0);
        table.TryGet(7, out _).Should().BeFalse();
    }

    /// <summary>A table sized for <paramref name="count"/> entries, and holding them.</summary>
    private static PdfXRefTable Filled(int count)
    {
        var table = new PdfXRefTable();
        table.Reserve(count, int.MaxValue);

        for (var number = 0; number < count; number++)
        {
            table.TryAdd(number, XRefEntry.Free);
        }

        return table;
    }

    private static int Capacity(PdfXRefTable table) => table.Capacity;

    private static Config PropertySettings =>
        Config.QuickThrowOnFailure.WithMaxTest(500).WithReplay(0x5EED_1729UL, 0x9E37_79B9_7F4A_7C15UL).WithQuietOnSuccess(true);
}
