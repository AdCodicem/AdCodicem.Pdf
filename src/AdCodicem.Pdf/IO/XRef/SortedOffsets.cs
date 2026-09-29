namespace AdCodicem.Pdf.IO.XRef;

/// <summary>
/// Offsets kept sorted as they are added, so that the first one after a position is found without looking at them all.
/// </summary>
/// <remarks>
/// Offsets added wait in a list until the next lookup sorts them into a run of their own. A run is merged into the one
/// before it while that one is no more than twice as large, as a binary counter carries: each run is then more than
/// twice the next, so a lookup searches a logarithm of them, and an offset is merged a logarithm of times over its life.
/// An index that grows between lookups — a chain of sections being read, objects found away from their entries — then
/// costs what it adds, not what it holds, whatever order the file interleaves the two in.
/// </remarks>
internal sealed class SortedOffsets
{
    private readonly List<long[]> _runs = [];

    /// <summary>The offsets added since the last lookup; null when there are none, so that none keeps its storage.</summary>
    private List<long>? _added;

    /// <summary>Starts with no offsets.</summary>
    public SortedOffsets()
    {
    }

    /// <summary>
    /// Starts with <paramref name="offsets"/>, in any order: the array is sorted in place and kept as the first run, so
    /// that an index sorted whole costs one array the size of its offsets.
    /// </summary>
    public SortedOffsets(long[] offsets)
    {
        if (offsets.Length > 0)
        {
            _runs.Add(SortDistinct(offsets));
        }
    }

    /// <summary>Gets how many runs the offsets sorted so far are kept in.</summary>
    internal int RunCount => _runs.Count;

    /// <summary>Adds an offset.</summary>
    public void Add(long offset) => (_added ??= []).Add(offset);

    /// <summary>Gets the smallest offset greater than <paramref name="position"/>, or <see cref="long.MaxValue"/> when none is.</summary>
    public long FirstAfter(long position)
    {
        Sort();

        var first = long.MaxValue;

        foreach (var run in _runs)
        {
            // Runs hold each offset once, so the one found, or the place it would go, is followed by the first greater.
            var index = Array.BinarySearch(run, position);
            index = index >= 0 ? index + 1 : ~index;

            if (index < run.Length && run[index] < first)
            {
                first = run[index];
            }
        }

        return first;
    }

    /// <summary>Sorts the offsets added since the last lookup into a run, and merges runs until each is more than twice the next.</summary>
    private void Sort()
    {
        if (_added is null)
        {
            return;
        }

        var run = SortDistinct(_added.ToArray());
        _added = null;

        while (_runs.Count > 0 && _runs[^1].Length <= 2L * run.Length)
        {
            run = Merge(_runs[^1], run);
            _runs.RemoveAt(_runs.Count - 1);
        }

        _runs.Add(run);
    }

    /// <summary>Sorts <paramref name="offsets"/> in place and gives them each once: the array itself, unless some repeat.</summary>
    private static long[] SortDistinct(long[] offsets)
    {
        Array.Sort(offsets);
        var count = 0;

        for (var i = 0; i < offsets.Length; i++)
        {
            if (count == 0 || offsets[count - 1] != offsets[i])
            {
                offsets[count++] = offsets[i];
            }
        }

        return count == offsets.Length ? offsets : offsets[..count];
    }

    /// <summary>Merges two sorted runs into one, each offset once.</summary>
    private static long[] Merge(long[] left, long[] right)
    {
        var merged = new long[left.Length + right.Length];
        var i = 0;
        var j = 0;
        var count = 0;

        while (i < left.Length || j < right.Length)
        {
            var next = j >= right.Length || (i < left.Length && left[i] <= right[j]) ? left[i++] : right[j++];

            if (count == 0 || merged[count - 1] != next)
            {
                merged[count++] = next;
            }
        }

        return count == merged.Length ? merged : merged[..count];
    }
}
