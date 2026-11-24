using AdCodicem.Pdf.IO;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Holds the store the reader keeps what its cut reads gave in (#163): what it weighs, what it lets go of first, and that
/// the order it lets go in does not grow with entries let go and kept again.
/// </summary>
public class BoundedStoreTests
{
    [Fact]
    public void An_entry_kept_again_under_its_key_weighs_once()
    {
        var store = new BoundedStore<string>(10);

        store.Keep(Key(1, 0), "a");
        store.Keep(Key(1, 0), "b");

        store.Count.Should().Be(1);
        store.Weight.Should().Be(1);
        store.TryGet(Key(1, 0), out var kept).Should().BeTrue();
        kept.Should().Be("b");
    }

    [Fact]
    public void An_entry_that_weighs_nothing_takes_no_place_in_the_order()
    {
        // Kept for good, it is let go of by name alone: the order the others are let go in never names it.
        var store = new BoundedStore<string>(10, value => value.Length);

        store.Keep(Key(1, 0), string.Empty);
        store.Keep(Key(2, 0), string.Empty);

        store.Named.Should().Be(0);
        store.Weight.Should().Be(0);
        store.Count.Should().Be(2);
    }

    [Fact]
    public void An_entry_kept_again_heavier_lets_go_of_the_oldest_other_and_keeps_itself()
    {
        // The order still names the entry's first place as it is kept again: the eviction it makes room by passes over it.
        var store = new BoundedStore<string>(2, value => value.Length);
        store.Keep(Key(1, 0), "x");
        store.Keep(Key(2, 0), "y");

        store.Keep(Key(1, 0), "zz");

        store.TryGet(Key(1, 0), out var kept).Should().BeTrue();
        kept.Should().Be("zz");
        store.TryGet(Key(2, 0), out _).Should().BeFalse();
        store.Weight.Should().Be(2);
    }

    [Fact]
    public void An_entry_kept_again_weighing_nothing_is_not_let_go_by_the_order_its_first_place_left()
    {
        var store = new BoundedStore<string>(1, value => value.Length);
        store.Keep(Key(1, 0), "x");
        store.Keep(Key(1, 0), string.Empty);
        store.Keep(Key(2, 0), "y");

        store.Keep(Key(3, 0), "z");

        store.TryGet(Key(1, 0), out _).Should().BeTrue("an entry that weighs nothing is let go of by name alone");
        store.TryGet(Key(2, 0), out _).Should().BeFalse();
        store.TryGet(Key(3, 0), out _).Should().BeTrue();
    }

    [Fact]
    public void Entries_let_go_and_kept_again_grow_the_order_to_twice_the_entries_and_sixteen_then_no_further()
    {
        // Entries a and b are kept, a again; nineteen others are each kept and let go; c is kept, b let go, d kept. The order
        // then names 23 keys for the 3 entries kept, past twice as many and sixteen: it is rebuilt to name each kept entry
        // once, at the last place it was kept, so that a heavy entry lets go of a first, then of c.
        var store = new BoundedStore<string>(100, value => value.Length);
        store.Keep(Key(1, 0), "a");
        store.Keep(Key(2, 0), "b");
        store.Keep(Key(1, 0), "a");

        for (var number = 10; number < 29; number++)
        {
            store.Keep(Key(number, 0), "x");
            store.RemoveIf(Key(number, 0), static _ => true);
        }

        store.Keep(Key(3, 0), "c");
        store.Named.Should().Be(23, "an order naming twice the entries and sixteen is not rebuilt");
        store.RemoveIf(Key(2, 0), static _ => true);

        store.Keep(Key(4, 0), "d");

        store.Named.Should().Be(3, "the order names each entry kept once, once rebuilt");
        store.Keep(Key(5, 0), new string('e', 98));
        store.TryGet(Key(1, 0), out _).Should().BeFalse("a was kept before c");
        store.TryGet(Key(3, 0), out _).Should().BeTrue();
        store.TryGet(Key(4, 0), out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Letting_go_of_an_object_s_entries_lets_go_of_their_weight_and_of_the_object(bool butOne)
    {
        var store = new BoundedStore<string>(100, value => value.Length);
        store.Keep(Key(1, 0), "aa");
        store.Keep(Key(1, 1), "bbb");
        store.Keep(Key(2, 0), "c");

        if (butOne)
        {
            store.RemoveAllBut(1, Key(1, 1));
        }
        else
        {
            store.RemoveAll(1);
        }

        store.Weight.Should().Be(butOne ? 4 : 1);
        store.Count.Should().Be(butOne ? 2 : 1);
        store.Objects.Should().Be(butOne ? 2 : 1);
        store.TryGet(Key(1, 0), out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("let go of but one")]
    [InlineData("let go of for room")]
    public void An_object_whose_last_entry_is_let_go_of_is_no_longer_listed(string how)
    {
        var store = new BoundedStore<string>(how == "let go of for room" ? 2 : 100);
        store.Keep(Key(1, 0), "a");
        store.Keep(Key(1, 1), "b");

        if (how == "let go of but one")
        {
            store.RemoveAllBut(1, Key(1, 1));
            store.RemoveIf(Key(1, 1), static _ => true);
        }
        else
        {
            store.Keep(Key(2, 0), "c");
            store.Keep(Key(2, 1), "d");
        }

        store.Objects.Should().Be(how == "let go of for room" ? 1 : 0);
        store.TryGet(Key(1, 0), out _).Should().BeFalse();
        store.TryGet(Key(1, 1), out _).Should().BeFalse();
    }

    [Fact]
    public void Letting_go_of_an_object_the_store_lists_no_key_of_changes_nothing()
    {
        var store = new BoundedStore<string>(10);
        store.Keep(Key(1, 0), "a");

        store.RemoveAll(2);
        store.RemoveAllBut(2, Key(2, 0));
        store.RemoveIf(Key(1, 0), static _ => false);

        store.Count.Should().Be(1);
        store.Weight.Should().Be(1);
    }

    [Fact]
    public void Clearing_the_store_lets_go_of_every_entry_its_weight_and_its_order()
    {
        var store = new BoundedStore<string>(10);
        store.Keep(Key(1, 0), "a");
        store.Keep(Key(2, 0), "b");

        store.Clear();

        (store.Count, store.Weight, store.Named, store.Objects).Should().Be((0, 0L, 0, 0));
    }

    private static long Key(int number, int part) => ((long)number << 32) | (uint)part;
}
