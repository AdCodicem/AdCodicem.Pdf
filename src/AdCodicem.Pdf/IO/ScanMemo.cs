using AdCodicem.Pdf.Documents;

namespace AdCodicem.Pdf.IO;

/// <summary>
/// What the rebuild's scan for objects takes of a file, kept for the document's life so that the file is scanned for it once,
/// a rebuild after it taking its headers from it: each object header and each <c>xref</c> keyword the scan takes, sorted by
/// where each starts, each with how the scan took it (<see cref="ScannedPlaceKind"/>), and where the trailers it notes lie
/// (<see cref="Trailers"/>). The scan records it through a <see cref="Builder"/>, which completes it; once complete, it
/// changes no more.
/// </summary>
/// <remarks>
/// <para>
/// A place is held in a slot of 16 bytes: where it starts, the object number, and the generation with the kind. Places of
/// one number, generation and kind that the scan takes one after another at one distance — a header written again and
/// again, as hostile files repeat one by the million — are held as a run, in two slots however many they are. The slots
/// are one array, grown by doubling as the scan adds to it, never past the bound, and trimmed once it ends. A memo that
/// is whole keeps the trailers too (<see cref="Trailers"/>): 8 bytes for each <c>trailer</c> keyword, which takes no
/// slot, and 8 for each cross-reference stream's header beside its slot, in lists trimmed once the scan ends, which keep
/// at most a ninth more room than they use. A place so costs 24 bytes at most, for a cross-reference stream's header, and
/// 16 for any other header or an <c>xref</c> keyword, that room aside. A query is a binary search over the slots, and
/// allocates nothing.
/// </para>
/// <para>
/// The places are held sorted by position, and the scan takes them in that order but for a header whose tokens run back
/// across places it took before it, which only bytes the scan takes as they come hold — a comment line of headers in
/// unconfirmed stream data before the line of an <c>obj</c> keyword —: such a header is put in its place, a run it falls
/// within split around it, and the memo no longer holds the places in the order the scan took them
/// (<see cref="InScanOrder"/>).
/// </para>
/// <para>
/// It holds at most <see cref="PdfReaderLimits.MaxIndexEntries"/> places, <c>trailer</c> keywords counted with them. A guard
/// (ADR 34): the places grow with the headers and keywords a file writes, which a valid file writes as many of as it
/// likes — an incrementally updated file writes each object it changes again —, so that only a file that holds that many
/// reaches it. The bound is the index's, so that the one guard a caller sizes for what the reader holds of a document's
/// objects sizes the memo too, at less than the index's own cost: 24 bytes a place at most, where the index takes 40
/// bytes and more for each number it may hold. The two do not count alike — the index each number once, the memo every
/// header, a number defined again included, and every keyword —, so that a file of many definitions of few numbers, or
/// of many keywords, fills the memo with an index far from full. Once it holds that many, the memo takes no more, and
/// is whole only before where the first place it could not hold lies (<see cref="WholeUntil"/>). The memo reports
/// nothing: a rebuild scans the file again where it is not whole, and serves what it would have, and the guard is
/// reported by a caller that has to go without its word past there (<see cref="PdfFileReader.ReachScanMemoBound"/>).
/// </para>
/// </remarks>
internal sealed class ScanMemo
{
    /// <summary>The bits of a slot's traits that hold a header's generation.</summary>
    private const int GenerationMask = 0xFFFF;

    /// <summary>The bit of a slot's traits that says the scan took its places as the bytes give them.</summary>
    private const int RawFlag = 1 << 16;

    /// <summary>The bit of a slot's traits that says its places are <c>xref</c> keywords, not headers.</summary>
    private const int KeywordFlag = 1 << 17;

    /// <summary>The bit of a slot's traits that says it starts a run, whose end the next slot holds.</summary>
    private const int RunFlag = 1 << 18;

    /// <summary>
    /// The traits of the slot that ends a run: where its last place starts, and, for value, the distance between its places.
    /// </summary>
    private const int RunEndFlag = 1 << 19;

    /// <summary>How many slots a builder makes room for first.</summary>
    private const int FirstCapacity = 64;

    /// <summary>The slots, sorted by where each starts, as many as the memo uses.</summary>
    private readonly Slot[] _slots;

    private ScanMemo(Slot[] slots, int count, long fullAt, long wholeUntil, bool inScanOrder, ScannedTrailers? trailers)
    {
        _slots = slots;
        Count = count;
        FullAt = fullAt;
        WholeUntil = wholeUntil;
        InScanOrder = inScanOrder;
        Trailers = trailers;
    }

    /// <summary>Gets how many places the memo holds, <c>trailer</c> keywords counted.</summary>
    public int Count { get; }

    /// <summary>
    /// Gets a value indicating whether the memo holds every place the scan takes: it never had to leave one out for
    /// <see cref="PdfReaderLimits.MaxIndexEntries"/>.
    /// </summary>
    public bool IsWhole => FullAt < 0;

    /// <summary>
    /// Gets where the first place the memo could not hold starts, the scan taking it once the memo held as many as it may;
    /// -1 while it is whole.
    /// </summary>
    public long FullAt { get; }

    /// <summary>
    /// Gets the position before which the memo holds every place the scan takes: <see cref="long.MaxValue"/> while it is
    /// whole and the scan read the file to its end; otherwise where the first place it could not hold starts, or where a
    /// window the source gave shorter than asked ended the data the scan read (#52) — or, where they start sooner, where the
    /// bytes the scan's last object left to read anew start, since a header the scan takes afterward, or one whose
    /// <c>obj</c> lies past the data read, may run back to them.
    /// </summary>
    public long WholeUntil { get; }

    /// <summary>
    /// Gets a value indicating whether the memo holds its places in the order the scan took them, so that a rebuild can take
    /// its headers as its scan would: a header put back among places taken before it breaks the order.
    /// </summary>
    public bool InScanOrder { get; }

    /// <summary>Gets where the trailers the scan notes lie, where the memo is whole; null otherwise.</summary>
    public ScannedTrailers? Trailers { get; }

    /// <summary>
    /// Gets the headers the memo holds that start at <paramref name="from"/> or past it and before <paramref name="to"/>,
    /// in the order of their positions.
    /// </summary>
    public HeaderRange HeadersWithin(long from, long to) => new(_slots, from, to);

    /// <summary>Determines whether the memo holds a header that starts at <paramref name="position"/>, and gives it.</summary>
    public bool TryGetHeaderAt(long position, out ScannedHeader header)
    {
        if (!TryFind(position, out var number, out var traits) || (traits & KeywordFlag) != 0)
        {
            header = default;
            return false;
        }

        header = new ScannedHeader(position, number, traits & GenerationMask, KindOf(traits));
        return true;
    }

    /// <summary>
    /// Determines whether the memo holds an <c>xref</c> keyword that starts at <paramref name="position"/>, and gives how the
    /// scan took it.
    /// </summary>
    public bool TryGetXRefKeywordAt(long position, out ScannedPlaceKind kind)
    {
        if (!TryFind(position, out _, out var traits) || (traits & KeywordFlag) == 0)
        {
            kind = default;
            return false;
        }

        kind = KindOf(traits);
        return true;
    }

    /// <summary>
    /// Finds the place that starts at <paramref name="position"/>, and gives its value — its object number — and its
    /// traits.
    /// </summary>
    private bool TryFind(long position, out int value, out int traits)
    {
        ReadOnlySpan<Slot> slots = _slots;
        var index = After(slots, position) - 1;
        value = 0;
        traits = 0;

        if (index < 0)
        {
            return false;
        }

        var slot = slots[index];

        if ((slot.Traits & RunEndFlag) != 0)
        {
            // The last place of a run: its first slot holds what its places are.
            if (slot.Start != position)
            {
                return false;
            }

            slot = slots[index - 1];
        }
        else if ((slot.Traits & RunFlag) != 0)
        {
            if ((position - slot.Start) % slots[index + 1].Value != 0)
            {
                return false;
            }
        }
        else if (slot.Start != position)
        {
            return false;
        }

        value = slot.Value;
        traits = slot.Traits;
        return true;
    }

    /// <summary>
    /// Gives the index of the first slot that starts past <paramref name="position"/>, or at it too where
    /// <paramref name="atToo"/>; the slots' count where none does.
    /// </summary>
    private static int After(ReadOnlySpan<Slot> slots, long position, bool atToo = false)
    {
        var low = 0;
        var high = slots.Length;

        while (low < high)
        {
            var middle = (int)((uint)(low + high) >> 1);

            if (slots[middle].Start < position || (!atToo && slots[middle].Start == position))
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static int FlagOf(ScannedPlaceKind kind) => kind == ScannedPlaceKind.Raw ? RawFlag : 0;

    private static ScannedPlaceKind KindOf(int traits) => (traits & RawFlag) != 0 ? ScannedPlaceKind.Raw : ScannedPlaceKind.Clean;

    /// <summary>
    /// Records what the scan takes, place by place in the order it takes them, and completes the memo once the scan has
    /// ended (<see cref="Complete"/>), after which it is let go.
    /// </summary>
    /// <param name="bound">How many places the memo holds at most.</param>
    internal sealed class Builder(int bound)
    {
        private Slot[] _slots = [];
        private int _used;
        private int _count;
        private long _fullAt = -1;
        private long _wholeUntil = long.MaxValue;
        private bool _inScanOrder = true;

        /// <summary>Gets a value indicating whether the memo still holds every place the scan took.</summary>
        public bool IsWhole => _fullAt < 0;

        /// <summary>
        /// Adds the header the scan took at <paramref name="position"/>; false where the memo, or the place, is past what it
        /// may hold (<see cref="TryCount"/>).
        /// </summary>
        public bool TryAddHeader(long position, int number, int generation, ScannedPlaceKind kind, long floor) =>
            TryAdd(position, number, generation | FlagOf(kind), floor);

        /// <summary>
        /// Adds the <c>xref</c> keyword the scan took at <paramref name="position"/>; false where the memo, or the place, is
        /// past what it may hold (<see cref="TryCount"/>).
        /// </summary>
        public bool TryAddXRefKeyword(long position, ScannedPlaceKind kind, long floor) =>
            TryAdd(position, 0, KeywordFlag | FlagOf(kind), floor);

        /// <summary>
        /// Counts the <c>trailer</c> keyword the scan noted at <paramref name="position"/>, which the memo's
        /// <see cref="ScanMemo.Trailers"/> holds once complete; false where the memo, or the keyword, is past what it may hold
        /// (<see cref="TryCount"/>).
        /// </summary>
        public bool TryAddTrailer(long position, long floor) => TryCount(position, floor);

        /// <summary>
        /// Notes that a window the source gave shorter than asked ended the data the scan read at
        /// <paramref name="position"/>, short of where it asked to read (#52): the memo holds every place the scan takes only
        /// before there, or before <paramref name="floor"/> where sooner.
        /// </summary>
        /// <param name="position">Where the data the window holds ends.</param>
        /// <param name="floor">Where the bytes the scan's last object left to read anew start: a header whose <c>obj</c> lies
        /// past the data read, which the scan cannot take, may start as far back as them.</param>
        public void EndAt(long position, long floor) => _wholeUntil = Math.Min(_wholeUntil, Math.Min(position, floor));

        /// <summary>
        /// Completes the memo once the scan has ended: its slots trimmed to those it uses, and, where it is whole, the
        /// trailers the scan noted kept with it.
        /// </summary>
        public ScanMemo Complete(ScannedTrailers trailers)
        {
            if (_slots.Length != _used)
            {
                Array.Resize(ref _slots, _used);
            }

            if (IsWhole)
            {
                trailers.Keywords.TrimExcess();
                trailers.Streams.TrimExcess();
            }

            return new ScanMemo(_slots, _count, _fullAt, _wholeUntil, _inScanOrder, IsWhole ? trailers : null);
        }

        /// <summary>
        /// Counts one more place, at <paramref name="position"/>: false where the memo holds as many as it may, or has
        /// already left one out, which it then no longer takes.
        /// </summary>
        /// <param name="position">Where the place starts.</param>
        /// <param name="floor">Where the bytes the scan's last object left to read anew start: no place the scan takes later
        /// starts before them.</param>
        private bool TryCount(long position, long floor)
        {
            if (!IsWhole)
            {
                return false;
            }

            if (_count >= bound)
            {
                _fullAt = position;
                _wholeUntil = Math.Min(_wholeUntil, Math.Min(position, floor));
                return false;
            }

            _count++;
            return true;
        }

        private bool TryAdd(long position, int value, int traits, long floor)
        {
            if (!TryCount(position, floor))
            {
                return false;
            }

            if (_used > 0 && position <= _slots[_used - 1].Start)
            {
                Insert(position, value, traits);
            }
            else if (!TryExtendRun(position, value, traits))
            {
                Replace(_used, 0, [new Slot(position, value, traits)]);
            }

            return true;
        }

        /// <summary>
        /// Takes the place at <paramref name="position"/>, past every place held, into a run with the last: the next of a run
        /// whose distance it keeps, or the second of a place like it, the two then a run.
        /// </summary>
        private bool TryExtendRun(long position, int value, int traits)
        {
            if (_used == 0)
            {
                return false;
            }

            var last = _slots[_used - 1];

            if ((last.Traits & RunEndFlag) != 0)
            {
                var head = _slots[_used - 2];

                if (head.Value != value || head.Traits != (traits | RunFlag) || position - last.Start != last.Value)
                {
                    return false;
                }

                _slots[_used - 1].Start = position;
                return true;
            }

            if (last.Value != value || last.Traits != traits || position - last.Start > int.MaxValue)
            {
                return false;
            }

            _slots[_used - 1].Traits |= RunFlag;
            Replace(_used, 0, [new Slot(position, (int)(position - last.Start), RunEndFlag)]);
            return true;
        }

        /// <summary>
        /// Puts the place at <paramref name="position"/>, which the scan took after a place that starts at it or past it, in
        /// its place among those held: a run it falls within is split around it.
        /// </summary>
        private void Insert(long position, int value, int traits)
        {
            _inScanOrder = false;

            var index = After(_slots.AsSpan(0, _used), position);
            Span<Slot> parts = stackalloc Slot[5];
            var length = 0;
            var removed = 0;

            if (index > 0 && (_slots[index - 1].Traits & RunFlag) != 0)
            {
                // The run's places up to the new one, the new one, and the run's places past it: each part of the run a place
                // alone where it keeps one, and a run where it keeps more.
                var head = _slots[index - 1];
                var end = _slots[index];
                var lastBefore = head.Start + ((position - head.Start) / end.Value * end.Value);
                var firstAfter = lastBefore + end.Value;
                var alone = head.Traits & ~RunFlag;

                parts[length++] = lastBefore == head.Start ? new Slot(head.Start, head.Value, alone) : head;

                if (lastBefore != head.Start)
                {
                    parts[length++] = new Slot(lastBefore, end.Value, RunEndFlag);
                }

                parts[length++] = new Slot(position, value, traits);

                if (firstAfter == end.Start)
                {
                    parts[length++] = new Slot(end.Start, head.Value, alone);
                }
                else
                {
                    parts[length++] = new Slot(firstAfter, head.Value, head.Traits);
                    parts[length++] = end;
                }

                index--;
                removed = 2;
            }
            else
            {
                parts[length++] = new Slot(position, value, traits);
            }

            Replace(index, removed, parts[..length]);
        }

        /// <summary>
        /// Replaces the <paramref name="removed"/> slots at <paramref name="index"/> with <paramref name="parts"/>, making
        /// room by doubling: the slots never outnumber the places, so that the memo makes room for no more than it may hold.
        /// </summary>
        private void Replace(int index, int removed, ReadOnlySpan<Slot> parts)
        {
            var needed = _used - removed + parts.Length;

            if (needed > _slots.Length)
            {
                var doubled = Math.Min(Math.Max(FirstCapacity, 2L * _slots.Length), Math.Min(bound, Array.MaxLength));
                Array.Resize(ref _slots, (int)Math.Max(needed, doubled));
            }

            Array.Copy(_slots, index + removed, _slots, index + parts.Length, _used - index - removed);
            parts.CopyTo(_slots.AsSpan(index));
            _used = needed;
        }
    }

    /// <summary>The headers of the memo within a range (<see cref="HeadersWithin"/>), to enumerate with <c>foreach</c>.</summary>
    internal readonly ref struct HeaderRange
    {
        private readonly ReadOnlySpan<Slot> _slots;
        private readonly long _from;
        private readonly long _to;

        /// <summary>Holds the range of <paramref name="slots"/> from <paramref name="from"/> to <paramref name="to"/>.</summary>
        internal HeaderRange(ReadOnlySpan<Slot> slots, long from, long to)
        {
            _slots = slots;
            _from = from;
            _to = to;
        }

        /// <summary>Gets an enumerator of the headers within the range.</summary>
        public HeaderEnumerator GetEnumerator() => new(_slots, _from, _to);
    }

    /// <summary>Enumerates the headers of the memo within a range, a run's places one at a time.</summary>
    internal ref struct HeaderEnumerator
    {
        private readonly ReadOnlySpan<Slot> _slots;
        private readonly long _to;

        /// <summary>The next slot to read.</summary>
        private int _index;

        /// <summary>Within a run: where its next place starts.</summary>
        private long _next;

        /// <summary>Within a run: where its last place starts.</summary>
        private long _last;

        /// <summary>Within a run: the distance between its places; 0 outside one.</summary>
        private int _stride;

        /// <summary>Within a run: its places' object number.</summary>
        private int _number;

        /// <summary>Within a run: its places' traits.</summary>
        private int _traits;

        /// <summary>Starts at the first header that starts at <paramref name="from"/> or past it.</summary>
        internal HeaderEnumerator(ReadOnlySpan<Slot> slots, long from, long to)
        {
            _slots = slots;
            _to = to;
            _index = After(slots, from, atToo: true);
            Current = default;

            // A run that starts before the range and ends within it or past it: its places from the range's start on.
            if (_index > 0 && (slots[_index - 1].Traits & RunFlag) != 0)
            {
                var head = slots[_index - 1];
                var stride = slots[_index].Value;
                _index++;

                if ((head.Traits & KeywordFlag) == 0)
                {
                    var steps = (from - head.Start + stride - 1) / stride;
                    Enter(head, head.Start + (steps * stride), slots[_index - 1].Start, stride);
                }
            }
        }

        /// <summary>Gets the header the enumerator stands on.</summary>
        public ScannedHeader Current { get; private set; }

        /// <summary>Moves to the next header within the range; false past the last.</summary>
        public bool MoveNext()
        {
            while (true)
            {
                if (_stride > 0)
                {
                    if (_next <= _last)
                    {
                        if (_next >= _to)
                        {
                            return false;
                        }

                        Current = new ScannedHeader(_next, _number, _traits & GenerationMask, KindOf(_traits));
                        _next += _stride;
                        return true;
                    }

                    _stride = 0;
                }

                if (_index >= _slots.Length || _slots[_index].Start >= _to)
                {
                    return false;
                }

                var slot = _slots[_index++];

                if ((slot.Traits & RunFlag) != 0)
                {
                    var end = _slots[_index++];

                    if ((slot.Traits & KeywordFlag) == 0)
                    {
                        Enter(slot, slot.Start, end.Start, end.Value);
                    }

                    continue;
                }

                if ((slot.Traits & KeywordFlag) == 0)
                {
                    Current = new ScannedHeader(slot.Start, slot.Value, slot.Traits & GenerationMask, KindOf(slot.Traits));
                    return true;
                }
            }
        }

        /// <summary>Stands within the run that <paramref name="head"/> starts, on its place at <paramref name="next"/>.</summary>
        private void Enter(Slot head, long next, long last, int stride)
        {
            _next = next;
            _last = last;
            _stride = stride;
            _number = head.Value;
            _traits = head.Traits;
        }
    }

    /// <summary>
    /// A place, or a run's first place, or a run's end: where it starts, its value — an object number, or a run's distance —,
    /// and its traits.
    /// </summary>
    internal struct Slot(long start, int value, int traits)
    {
        /// <summary>Where the place starts; for a run's end, where its last place starts.</summary>
        public long Start = start;

        /// <summary>The object number of a header; 0 for a keyword; for a run's end, the distance between its places.</summary>
        public int Value = value;

        /// <summary>The generation, kind and role of the slot, as the flags of <see cref="ScanMemo"/> lay them out.</summary>
        public int Traits = traits;
    }
}
