using AdCodicem.Pdf.IO.XRef;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The room the index makes for the rows a section is about to give, before it records them (#164), and how it finds a
/// number's entry, in a slot or in its seeded map (#155).
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
        // A chain of many sections each making room for its own rows would otherwise resize the map at each one.
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

        table.CopyNumbers()[..10].Should().Equal(Enumerable.Range(3_000, 10));
        table.TryAdd(3_005, XRefEntry.Free).Should().BeFalse("a number moved into its slot is still held");
    }

    [Fact]
    public void Numbers_spread_further_apart_than_twice_their_count_stay_in_the_map()
    {
        // Four thousand numbers 3 apart: the slots stop where they would hold more than twice the entries and the slack,
        // and the numbers past them are found through the map.
        var table = new PdfXRefTable();

        for (var k = 0; k < 4_000; k++)
        {
            table.TryAdd(3 * k, XRefEntry.Regular(k, 0));
        }

        for (var k = 0; k < 4_000; k++)
        {
            table.TryGet(3 * k, out var entry).Should().BeTrue();
            entry.Offset.Should().Be(k);
            table.TryGet((3 * k) + 1, out _).Should().BeFalse();
        }
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
}
