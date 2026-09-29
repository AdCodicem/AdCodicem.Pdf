namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The tests that compare the time the reader takes on inputs of two sizes: they run alone, after the rest of the
/// suite, since whatever else runs beside them weighs on one measure more than on the one it is compared with.
/// </summary>
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
