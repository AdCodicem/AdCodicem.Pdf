namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The tests that measure the whole process: the time the reader takes on inputs of two sizes, or the memory live at
/// once while it reads one. They run alone, after the rest of the suite, since whatever else runs beside them weighs on
/// one measure more than on the one it is compared with, and holds memory of its own.
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
