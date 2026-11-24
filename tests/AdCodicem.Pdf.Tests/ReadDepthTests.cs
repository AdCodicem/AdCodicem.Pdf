using AdCodicem.Pdf.IO;

namespace AdCodicem.Pdf.Tests;

/// <summary>Holds when what a read kept at a depth serves a later read (<see cref="ReadDepth"/>, #163).</summary>
public class ReadDepthTests
{
    [Theory]
    [InlineData(1, 2, 2, true)]
    [InlineData(1, 2, 1, false)]
    [InlineData(1, 2, 3, false)]
    [InlineData(1, 0, 0, true)]
    [InlineData(1, 0, 1, false)]
    public void What_a_cut_read_kept_serves_a_read_from_the_base_it_was_made_from_alone(int extent, int cutBase, int depthBase, bool serves)
    {
        // A read the loads' nesting or the stack cut gave what it gave only from the base it was made from, the cut falling
        // at the same load: from a shallower base or a deeper one it is read again, however little it went below its base.
        new ReadDepth(extent, cutBase).Serves(depthBase, bound: 64).Should().Be(serves);
    }
}
