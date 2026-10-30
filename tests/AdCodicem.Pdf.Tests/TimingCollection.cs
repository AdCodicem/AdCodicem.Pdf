namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The tests that measure the whole process: the time the reader takes on inputs of two sizes, or the memory live at
/// once while it reads one. They run alone, after the rest of the suite, since whatever else runs beside them weighs on
/// one measure more than on the one it is compared with, and holds memory of its own.
/// </summary>
/// <remarks>
/// Running alone does not still the process. The shared array pools keep what a test before rented and gave back, the
/// test platform's own arrays included, and let go of it at a full collection some time later — under low memory
/// pressure, an array a thread keeps for itself half a minute after a trim first finds it unused, one in the shared stock
/// a minute after —, so that the memory of the whole process can fall by a hundred megabytes and more between two
/// measures, inside whichever test runs then. Memory measured here is bounded from above only, a bound such a fall can
/// make pass and never fail; that something is let go is watched through a weak reference to it, which nothing else in
/// the process moves (#307).
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "xUnit collection definitions are named after the collection they define.")]
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    /// <summary>The collection's name.</summary>
    public const string Name = "timing";
}
