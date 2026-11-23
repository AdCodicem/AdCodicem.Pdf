using System.Runtime.InteropServices;

namespace AdCodicem.Pdf.IO;

internal sealed partial class PdfFileReader
{
    /// <summary>
    /// Gives the key of what a read of object <paramref name="number"/> kept: from base <paramref name="cutBase"/>, where the
    /// loads' nesting or a transient cut reached it, or -1 where nothing did (<see cref="ReadDepth.CutBase"/>).
    /// </summary>
    private static long DepthKey(int number, int cutBase) => ((long)number << 32) | (uint)cutBase;

    /// <summary>Gives the object number <paramref name="key"/>, a <see cref="DepthKey"/>, keeps something of.</summary>
    private static int NumberOf(long key) => (int)(key >> 32);

    /// <summary>
    /// What reads kept, by a key the caller forms — an object number, or one with the base of the read that kept it
    /// (<see cref="DepthKey"/>) —, each weighing what the caller says, at most a budget at once, the oldest let go first
    /// (#163).
    /// </summary>
    /// <remarks>
    /// An entry that weighs nothing is kept until it is let go of by name: it takes no room. The order the others are let go in
    /// names each key once, rebuilt whenever it names twice as many as are kept, so that entries let go and kept again, one at
    /// a time — what the reads the thread's stack cut kept, let go as each read ends —, do not grow it. The keys of each object
    /// are listed apart, so that letting go of all of an object's costs what it kept, not what the store holds: a rebuild's
    /// expansion lets go of an object for each definition it replaces.
    /// </remarks>
    /// <typeparam name="T">What is kept.</typeparam>
    /// <param name="budget">How much the entries weigh at most, the last kept whatever it weighs.</param>
    /// <param name="weigh">Gives what an entry weighs; null for one each.</param>
    private sealed class BoundedStore<T>(long budget, Func<T, long>? weigh = null)
    {
        private readonly Dictionary<long, T> _entries = new(SeededComparer.Instance);

        /// <summary>The keys in the order they were kept: it can name a key let go already, and the next is let go in its place.</summary>
        private readonly Queue<long> _order = new();

        /// <summary>The keys kept of each object, by number.</summary>
        private readonly Dictionary<int, List<long>> _keys = new(SeededComparer.Instance);

        /// <summary>Gets what the entries kept weigh.</summary>
        public long Weight { get; private set; }

        /// <summary>Gives what is kept under <paramref name="key"/>, if anything is.</summary>
        public bool TryGet(long key, out T value) => _entries.TryGetValue(key, out value!);

        /// <summary>
        /// Keeps <paramref name="value"/> under <paramref name="key"/>, in place of what was kept there before, letting go of the
        /// oldest entries while what is kept and what it weighs exceed the budget.
        /// </summary>
        public void Keep(long key, T value)
        {
            if (_entries.Remove(key, out var previous))
            {
                Weight -= WeightOf(previous);
            }
            else
            {
                ref var listed = ref CollectionsMarshal.GetValueRefOrAddDefault(_keys, NumberOf(key), out _);
                (listed ??= []).Add(key);
            }

            var weight = WeightOf(value);
            _entries[key] = value;

            if (weight == 0)
            {
                return;
            }

            // The queue can name the key replaced: it is named again below.
            while (Weight + weight > budget && _order.TryDequeue(out var oldest))
            {
                if (oldest != key && _entries.TryGetValue(oldest, out var queued) && WeightOf(queued) > 0)
                {
                    Drop(oldest, queued);
                }
            }

            Compact();
            _order.Enqueue(key);
            Weight += weight;
        }

        /// <summary>Lets go of what is kept under <paramref name="key"/> where <paramref name="match"/> holds of it.</summary>
        public void RemoveIf(long key, Func<T, bool> match)
        {
            if (_entries.TryGetValue(key, out var value) && match(value))
            {
                Drop(key, value);
            }
        }

        /// <summary>Lets go of what is kept under every key of object <paramref name="number"/>.</summary>
        public void RemoveAll(int number)
        {
            if (!_keys.Remove(number, out var listed))
            {
                return;
            }

            foreach (var key in listed)
            {
                if (_entries.Remove(key, out var removed))
                {
                    Weight -= WeightOf(removed);
                }
            }
        }

        /// <summary>Lets go of what is kept under every key of object <paramref name="number"/> but <paramref name="key"/>.</summary>
        public void RemoveAllBut(int number, long key)
        {
            if (!_keys.TryGetValue(number, out var listed))
            {
                return;
            }

            for (var index = listed.Count - 1; index >= 0; index--)
            {
                if (listed[index] != key && _entries.Remove(listed[index], out var removed))
                {
                    Weight -= WeightOf(removed);
                    listed.RemoveAt(index);
                }
            }

            if (listed.Count == 0)
            {
                _keys.Remove(number);
            }
        }

        /// <summary>Lets go of every entry, and of the room they took.</summary>
        public void Clear()
        {
            _entries.Clear();
            _entries.TrimExcess();
            _order.Clear();
            _order.TrimExcess();
            _keys.Clear();
            _keys.TrimExcess();
            Weight = 0;
        }

        private long WeightOf(T value) => weigh is null ? 1 : weigh(value);

        /// <summary>Lets go of <paramref name="value"/>, kept under <paramref name="key"/>.</summary>
        private void Drop(long key, T value)
        {
            _entries.Remove(key);
            Weight -= WeightOf(value);
            var listed = _keys[NumberOf(key)];
            listed.Remove(key);

            if (listed.Count == 0)
            {
                _keys.Remove(NumberOf(key));
            }
        }

        /// <summary>
        /// Rebuilds the order the entries are let go in once it names twice as many keys as are kept, keeping the last time it
        /// names each key kept: where it was kept again.
        /// </summary>
        private void Compact()
        {
            if (_order.Count <= (2 * _entries.Count) + 16)
            {
                return;
            }

            var left = new Dictionary<long, int>(SeededComparer.Instance);

            foreach (var key in _order)
            {
                if (_entries.ContainsKey(key))
                {
                    left[key] = left.GetValueOrDefault(key) + 1;
                }
            }

            for (var count = _order.Count; count > 0; count--)
            {
                var key = _order.Dequeue();

                if (left.TryGetValue(key, out var times) && (left[key] = times - 1) == 0)
                {
                    _order.Enqueue(key);
                }
            }
        }
    }
}
