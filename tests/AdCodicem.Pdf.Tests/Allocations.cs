namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Measures what work allocates on the test's thread, so that a budget of a few kilobytes holds whatever else the
/// process does meanwhile (#247, #253).
/// </summary>
/// <remarks>
/// <see cref="GC.GetAllocatedBytesForCurrentThread"/> counts this thread alone, but not exactly once a collection falls
/// in the window: the thread's count can then take up to one allocation quantum, some 8 KB, that the thread never used,
/// now and then two. A collection another test triggers falls in a window often enough once the window lasts: one that
/// walks an array of a million names, or work that allocates on the large-object heap. The excess only ever adds, so the
/// least of measures no collection paused is what the work allocated, to the byte, and a budget kept below it still
/// catches what the work would allocate on every run.
/// </remarks>
internal static class Allocations
{
    private const int Measures = 3;
    private const int Attempts = 30;

    /// <summary>
    /// What <paramref name="work"/> allocates on this thread: the least of three measures that no collection paused, or,
    /// past thirty measures, the least of all.
    /// </summary>
    /// <param name="work">The work measured, run once a measure.</param>
    /// <param name="result">What the last measure's run of <paramref name="work"/> gave.</param>
    public static long Least<TResult>(Func<TResult> work, out TResult result) =>
        Least(static () => 0, _ => work(), out _, out result);

    /// <summary>
    /// What <paramref name="work"/> allocates on this thread: the least of three measures that no collection paused, or,
    /// past thirty measures, the least of all. Each measure is given a state of its own from <paramref name="prepare"/>,
    /// made before it starts.
    /// </summary>
    /// <param name="prepare">Makes the state a measure's run of <paramref name="work"/> is given, outside the measure.</param>
    /// <param name="work">The work measured, run once a measure.</param>
    /// <param name="state">The state the last measure was given.</param>
    /// <param name="result">What the last measure's run of <paramref name="work"/> gave.</param>
    public static long Least<TState, TResult>(Func<TState> prepare, Func<TState, TResult> work, out TState state, out TResult result)
    {
        var least = long.MaxValue;
        var leastUnpaused = long.MaxValue;
        var unpaused = 0;
        state = default!;
        result = default!;

        for (var attempt = 0; attempt < Attempts && unpaused < Measures; attempt++)
        {
            state = prepare();
            var paused = GC.GetTotalPauseDuration();
            var before = GC.GetAllocatedBytesForCurrentThread();
            result = work(state);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            least = Math.Min(least, allocated);

            if (GC.GetTotalPauseDuration() == paused)
            {
                leastUnpaused = Math.Min(leastUnpaused, allocated);
                unpaused++;
            }
        }

        return unpaused > 0 ? leastUnpaused : least;
    }
}
