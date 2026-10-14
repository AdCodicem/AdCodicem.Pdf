using AdCodicem.Pdf.IO.XRef;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The room the index makes for the rows a section is about to give, before it records them (#164).
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

    private static int Capacity(PdfXRefTable table) => table.Entries.EnsureCapacity(0);
}
