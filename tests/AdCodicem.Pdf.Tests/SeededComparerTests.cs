using AdCodicem.Pdf.IO;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The comparer every hash table keyed by numbers takes: numbers a file chose to share a bucket, or a hash, spread as
/// random keys do (#155).
/// </summary>
public class SeededComparerTests
{
    [Fact]
    public void Numbers_a_table_s_size_apart_spread_over_its_buckets()
    {
        // An int is its own hash: a thousand numbers 1,103 apart share a bucket of a table of 1,103, and hashed through the
        // process's seed they spread as random keys do, over about 650 of them.
        var comparer = SeededComparer.Instance;
        var size = new HashSet<int>(1_000).EnsureCapacity(0);
        var buckets = new HashSet<uint>();

        for (var k = 1; k <= 1_000; k++)
        {
            buckets.Add((uint)comparer.GetHashCode(3 + (k * size)) % (uint)size);
        }

        buckets.Count.Should().BeGreaterThan(500, "{0} numbers in {1} buckets spread as random keys do", 1_000, size);
        comparer.Equals(7, 7).Should().BeTrue();
        comparer.Equals(7, 8).Should().BeFalse();
    }

    [Fact]
    public void Offsets_whose_halves_exclusive_or_alike_do_not_share_a_hash()
    {
        // A long hashes as its halves exclusive-ored: 0 and 2^32 + 1 share a hash outright, in a table of any size.
        long near = 0;
        var far = (1L << 32) + 1;
        near.GetHashCode().Should().Be(far.GetHashCode(), "the runtime's own hash folds the halves");

        var comparer = SeededComparer.Instance;

        comparer.GetHashCode(near).Should().NotBe(comparer.GetHashCode(far));
        comparer.Equals(far, far).Should().BeTrue();
        comparer.Equals(near, far).Should().BeFalse();
    }

    [Fact]
    public void Pairs_holding_offsets_whose_halves_exclusive_or_alike_do_not_share_a_hash()
    {
        // A tuple hashes as its items do: two holding offsets that fold alike, and items alike, share a hash.
        long near = 0;
        var far = (1L << 32) + 1;
        (near, 5).GetHashCode().Should().Be((far, 5).GetHashCode(), "a tuple hashes its items as they hash");

        var comparer = SeededComparer.Instance;

        comparer.GetHashCode((near, 5)).Should().NotBe(comparer.GetHashCode((far, 5)));
        comparer.GetHashCode((near, true)).Should().NotBe(comparer.GetHashCode((far, true)));
        comparer.GetHashCode((near, EndObjState.Present)).Should().NotBe(comparer.GetHashCode((far, EndObjState.Present)));
        comparer.GetHashCode((PdfLimit.Object, near)).Should().NotBe(comparer.GetHashCode((PdfLimit.Object, far)));

        comparer.Equals((far, 5), (far, 5)).Should().BeTrue();
        comparer.Equals((far, 5), (far, 6)).Should().BeFalse();
        comparer.Equals((far, true), (far, true)).Should().BeTrue();
        comparer.Equals((far, true), (far, false)).Should().BeFalse();
        comparer.Equals((far, EndObjState.Present), (far, EndObjState.Present)).Should().BeTrue();
        comparer.Equals((far, EndObjState.Present), (near, EndObjState.Present)).Should().BeFalse();
        comparer.Equals((PdfLimit.Object, far), (PdfLimit.Object, far)).Should().BeTrue();
        comparer.Equals((PdfLimit.Object, far), (PdfLimit.Object, near)).Should().BeFalse();
    }
}
