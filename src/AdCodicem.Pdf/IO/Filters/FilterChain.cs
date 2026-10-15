using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO.Filters;

/// <summary>
/// The filters a stream's data is decoded through, each with the values of its parameters that decoding it reads,
/// resolved before any of the data is read; and what ends the chain, when something does before its last element.
/// </summary>
/// <remarks>
/// <para>
/// Resolving a value can load an object, and loading one an object stream holds decodes that object stream. Were the
/// values resolved as each filter ran, the stream being decoded would hold its output while that object stream decoded,
/// and a chain of such loads would hold one decoded output for each stream on it (#165). Resolved first, a chain of loads
/// holds no decoded data of the streams that started it: each decodes once every load it needs is over.
/// </para>
/// <para>
/// Only what decoding reads is resolved: the chain's names up to the first that ends it — an image filter, a filter
/// the library does not know, an element that names no filter —, and of the parameters, LZW's <c>/EarlyChange</c>, a
/// Flate or LZW step's <c>/Predictor</c>, and its <c>/Colors</c>, <c>/BitsPerComponent</c> and <c>/Columns</c> when the
/// predictor is past 1. Each of the others can be a reference, and resolving it could load, or rebuild, what nothing
/// asked for.
/// </para>
/// <para>
/// The parameters of a step are paired with it as qpdf pairs them, so that the shapes ISO 32000-1 (7.3.8.2) does not
/// allow still decode as they were written to: the element at the step's place of a <c>/DecodeParms</c> array — the
/// first, beside a single name —, or a <c>/DecodeParms</c> dictionary for every step of the chain.
/// </para>
/// <para>
/// What was resolved is kept in less room than the file wrote it in. The <c>/Filter</c> array is read again as the steps
/// run, so a name it holds costs nothing more; only the name an element written as a reference led to is kept. A
/// dictionary that serves every step is read once for Flate and once for LZW, and of an array of parameters, only the
/// values that are not those of no parameters at all are kept, each with its step's place. The array can hold millions
/// of elements — an object stream's member is read from as much data as the stream decodes to —, and a chain that kept
/// an entry for each would cost several times the array on every decode.
/// </para>
/// </remarks>
internal sealed class FilterChain
{
    private static readonly FilterChain NoFilter = new(new Reading());

    /// <summary>What <c>/Filter</c> leads to: a name, an array, or anything else when it names no filter at all.</summary>
    private readonly PdfObject? _filters;

    /// <summary>The names the elements written as references lead to, in the chain's order.</summary>
    private readonly PdfName[]? _names;

    /// <summary>The values of an array's parameters that are not those of no parameters at all, by step, in order.</summary>
    private readonly Placed[]? _placed;

    private readonly int _placedCount;

    /// <summary>The values each Flate step decodes with when no array places them.</summary>
    private readonly FilterParameters _flate;

    /// <summary>The values each LZW step decodes with when no array places them.</summary>
    private readonly FilterParameters _lzw;

    /// <summary>How many elements of the <c>/Filter</c> array stand before the end of the chain.</summary>
    private readonly int _length;

    private FilterChain(in Reading reading)
    {
        _filters = reading.Filters;
        _names = reading.Names;
        _placed = reading.Placed;
        _placedCount = reading.PlacedCount;
        _flate = reading.Flate;
        _lzw = reading.Lzw;
        _length = reading.Length;
        LastIndex = reading.LastIndex;
        End = reading.End;
        EndIndex = reading.EndIndex;
        EndValue = reading.EndValue;
    }

    /// <summary>Gets where in the <c>/Filter</c> array the last step decoding runs stands, or -1 when it runs none.</summary>
    public int LastIndex { get; }

    /// <summary>Gets what ends the chain before its last element, if anything does.</summary>
    public FilterChainEnd End { get; }

    /// <summary>
    /// Gets where in the <c>/Filter</c> array the element that ends the chain stands: 0 for a single name, -1 when no
    /// element does.
    /// </summary>
    public int EndIndex { get; }

    /// <summary>
    /// Gets the value that ends the chain: the name of a filter the library does not know, the element that names no
    /// filter, or the <c>/Filter</c> entry that is neither a name nor an array; null when nothing ends it, or an image filter does.
    /// </summary>
    public PdfObject? EndValue { get; }

    /// <summary>
    /// Resolves what the filters of the stream <paramref name="dictionary"/> describes are, and the values of their
    /// parameters decoding reads.
    /// </summary>
    /// <param name="dictionary">The stream's dictionary.</param>
    public static FilterChain Resolve(PdfDictionary dictionary)
    {
        var reader = default(Resolving);
        TryRead(dictionary, ref reader, readParameters: true, keep: true, out var reading);
        return reading.Filters is null ? NoFilter : new FilterChain(reading);
    }

    /// <summary>
    /// Resolves what ends the chain of filters the stream <paramref name="dictionary"/> describes, reading none of their
    /// parameters and keeping nothing.
    /// </summary>
    /// <param name="dictionary">The stream's dictionary.</param>
    public static FilterChainEnd EndOf(PdfDictionary dictionary)
    {
        var reader = default(Resolving);
        TryRead(dictionary, ref reader, readParameters: false, keep: false, out var reading);
        return reading.End;
    }

    /// <summary>
    /// Follows, through <paramref name="reader"/>, every value decoding the stream <paramref name="dictionary"/> describes
    /// reads before any of its data, in the order decoding reads them, and keeps nothing.
    /// </summary>
    /// <param name="dictionary">The stream's dictionary.</param>
    /// <param name="reader">Follows each value to what it leads to, and can end the reading.</param>
    /// <returns>False when <paramref name="reader"/> ended the reading.</returns>
    public static bool TryFollow<TReader>(PdfDictionary dictionary, ref TReader reader)
        where TReader : struct, IFilterValueReader =>
        TryRead(dictionary, ref reader, readParameters: true, keep: false, out _);

    /// <summary>Gets the steps decoding runs, in order.</summary>
    public Steps GetEnumerator() => new(this);

    /// <summary>The steps of a chain, each with the values of its parameters, as decoding runs them.</summary>
    /// <param name="chain">The chain.</param>
    public struct Steps(FilterChain chain)
    {
        private int _index = -1;
        private int _name;
        private int _placed;

        /// <summary>Gets the current step.</summary>
        public FilterStep Current { get; private set; }

        /// <summary>Moves to the next step, if there is one.</summary>
        public bool MoveNext()
        {
            if (_index + 1 >= chain._length)
            {
                return false;
            }

            _index++;

            // Every element before the end is a name, or a reference that led to one, which was kept in order.
            var element = chain._filters is PdfArray array ? array[_index] : chain._filters;
            var name = element as PdfName ?? chain._names![_name++];
            FilterParameters parameters;

            if (_placed < chain._placedCount && chain._placed![_placed].Index == _index)
            {
                parameters = chain._placed[_placed++].Parameters;
            }
            else
            {
                parameters = name == PdfName.LZWDecode ? chain._lzw : chain._flate;
            }

            Current = new FilterStep(_index, name, parameters);
            return true;
        }
    }

    /// <summary>
    /// Reads, through <paramref name="reader"/>, what the filters of the stream <paramref name="dictionary"/> describes are,
    /// and the values of their parameters decoding reads, in the order decoding needs them.
    /// </summary>
    /// <param name="dictionary">The stream's dictionary.</param>
    /// <param name="reader">Follows each value to what it leads to, and can end the reading.</param>
    /// <param name="readParameters">Whether the parameters are read, or only the names of the filters.</param>
    /// <param name="keep">Whether what decoding needs again is kept, or only what ends the chain.</param>
    /// <param name="reading">What was read.</param>
    /// <returns>False when <paramref name="reader"/> ended the reading.</returns>
    private static bool TryRead<TReader>(PdfDictionary dictionary, ref TReader reader, bool readParameters, bool keep, out Reading reading)
        where TReader : struct, IFilterValueReader
    {
        reading = new Reading();

        if (!reader.TryRead(PdfName.Filter, dictionary.GetRaw(PdfName.Filter), entry: true, out var filters))
        {
            return false;
        }

        // A /Filter that is null, or leads to null or to an object the file lacks, is one the dictionary does not have
        // (7.3.7, 7.3.10): the data is not encoded.
        if (filters is null or PdfNull)
        {
            return true;
        }

        reading.Filters = filters;
        var array = filters as PdfArray;

        if (filters is not PdfName && array is null)
        {
            reading.End = FilterChainEnd.NotAFilterEntry;
            reading.EndValue = filters;
            return true;
        }

        var length = array?.Count ?? 1;
        var parametersRead = false;
        var flateRead = false;
        var lzwRead = false;
        PdfObject? parameters = null;

        for (var index = 0; index < length; index++)
        {
            var written = array is null ? filters : array[index];
            var element = written;

            if (array is not null && !reader.TryRead(PdfName.Filter, written, entry: false, out element))
            {
                return false;
            }

            var end = element switch
            {
                not PdfName => FilterChainEnd.NotAName,
                PdfName name when PdfFilterPipeline.IsImageFilter(name) => FilterChainEnd.Image,
                PdfName name when !IsSupported(name) => FilterChainEnd.Unsupported,
                _ => FilterChainEnd.None,
            };

            if (end != FilterChainEnd.None)
            {
                reading.End = end;
                reading.EndIndex = index;
                reading.EndValue = end == FilterChainEnd.Image ? null : element;
                return true;
            }

            var filter = (PdfName)element!;

            if (keep && written is not PdfName)
            {
                Append(ref reading.Names, ref reading.NameCount, filter);
            }

            if (readParameters && (filter == PdfName.FlateDecode || filter == PdfName.LZWDecode))
            {
                if (!parametersRead)
                {
                    if (!reader.TryRead(PdfName.DecodeParms, dictionary.GetRaw(PdfName.DecodeParms), entry: true, out parameters))
                    {
                        return false;
                    }

                    parametersRead = true;
                }

                if (parameters is PdfArray parameterArray)
                {
                    var placed = index < parameterArray.Count ? parameterArray[index] : null;

                    if (!reader.TryRead(PdfName.DecodeParms, placed, entry: false, out placed) ||
                        !TryReadParameters(filter, placed.AsDictionary(), ref reader, out var values))
                    {
                        return false;
                    }

                    if (keep && values != new FilterParameters())
                    {
                        Append(ref reading.Placed, ref reading.PlacedCount, new Placed(index, values));
                    }
                }
                else if (filter == PdfName.FlateDecode ? !flateRead : !lzwRead)
                {
                    // A dictionary serves every step: its values are read once for each kind of step that reads them.
                    if (!TryReadParameters(filter, parameters.AsDictionary(), ref reader, out var values))
                    {
                        return false;
                    }

                    if (filter == PdfName.FlateDecode)
                    {
                        (reading.Flate, flateRead) = (values, true);
                    }
                    else
                    {
                        (reading.Lzw, lzwRead) = (values, true);
                    }
                }
            }

            reading.LastIndex = index;
            reading.Length++;
        }

        return true;
    }

    /// <summary>Adds <paramref name="item"/> to <paramref name="items"/>, which grows as a list does.</summary>
    private static void Append<T>(ref T[]? items, ref int count, T item)
    {
        if (items is null)
        {
            items = new T[4];
        }
        else if (count == items.Length)
        {
            Array.Resize(ref items, (int)Math.Min(2L * count, Array.MaxLength));
        }

        items[count++] = item;
    }

    /// <summary>Determines whether the library decodes the filter <paramref name="name"/>, rather than leaving its data encoded.</summary>
    private static bool IsSupported(PdfName name) =>
        name == PdfName.FlateDecode || name == PdfName.LZWDecode || name == PdfName.ASCII85Decode ||
        name == PdfName.ASCIIHexDecode || name == PdfName.RunLengthDecode || name == PdfName.Crypt;

    /// <summary>Reads the values of a Flate or LZW step's parameters that decoding it reads.</summary>
    private static bool TryReadParameters<TReader>(PdfName filter, PdfDictionary? parameters, ref TReader reader, out FilterParameters values)
        where TReader : struct, IFilterValueReader
    {
        values = new FilterParameters();

        if (parameters is null)
        {
            return true;
        }

        if (filter == PdfName.LZWDecode)
        {
            if (!reader.TryRead(PdfName.DecodeParms, parameters.GetRaw(PdfName.EarlyChange), entry: false, out var earlyChange))
            {
                return false;
            }

            values = values with { EarlyChange = (int)(earlyChange.AsInteger() ?? 1) is 0 ? 0 : 1 };
        }

        if (!reader.TryRead(PdfName.DecodeParms, parameters.GetRaw(PdfName.Predictor), entry: false, out var predictor))
        {
            return false;
        }

        values = values with { Predictor = (int)(predictor.AsInteger() ?? 1) };

        // Without a predictor the other parameters mean nothing, and are not read.
        if (values.Predictor <= 1)
        {
            return true;
        }

        if (!reader.TryRead(PdfName.DecodeParms, parameters.GetRaw(PdfName.Colors), entry: false, out var colors) ||
            !reader.TryRead(PdfName.DecodeParms, parameters.GetRaw(PdfName.BitsPerComponent), entry: false, out var bitsPerComponent) ||
            !reader.TryRead(PdfName.DecodeParms, parameters.GetRaw(PdfName.Columns), entry: false, out var columns))
        {
            return false;
        }

        values = values with
        {
            Colors = (int)(colors.AsInteger() ?? 1),
            BitsPerComponent = (int)(bitsPerComponent.AsInteger() ?? 8),
            Columns = (int)Math.Clamp(columns.AsInteger() ?? 1, 1, int.MaxValue),
        };
        return true;
    }

    /// <summary>The values of the parameters of the step at <paramref name="Index"/>.</summary>
    /// <param name="Index">Where in the chain the step stands.</param>
    /// <param name="Parameters">The values.</param>
    private readonly record struct Placed(int Index, FilterParameters Parameters);

    /// <summary>What reading a chain found, before it is kept.</summary>
    private struct Reading()
    {
        public PdfObject? Filters;
        public int Length;
        public int LastIndex = -1;
        public FilterChainEnd End;
        public int EndIndex = -1;
        public PdfObject? EndValue;
        public PdfName[]? Names;
        public int NameCount;
        public Placed[]? Placed;
        public int PlacedCount;
        public FilterParameters Flate = new();
        public FilterParameters Lzw = new();
    }

    /// <summary>Follows each value as <see cref="PdfReference.Resolve"/> does, and never ends the reading.</summary>
    private struct Resolving : IFilterValueReader
    {
        public readonly bool TryRead(PdfName key, PdfObject? written, bool entry, out PdfObject? value)
        {
            value = written?.Resolve();
            return true;
        }
    }
}
