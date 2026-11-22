using System.Globalization;
using AdCodicem.Pdf.Diagnostics;
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
/// step reads a predictor, and a <c>/Crypt</c> step's <c>/Name</c>. Each of the others can be a reference, and resolving
/// it could load, or rebuild, what nothing asked for.
/// </para>
/// <para>
/// A name is read as the filter it abbreviates where it is one of the abbreviations ISO 32000-1 allows only in an inline
/// image's dictionary (Table 94), as qpdf, MuPDF, pdf.js and PDFium read it on any stream (#250): every reader of the
/// chain reads it so, the check of a cross-reference stream before its rows, <c>HasImageFilter</c> and the validator's
/// walks alike. A parameter's value ISO 32000-1 does not allow is read as other readers read it (#162). What the chain
/// reads otherwise than the file wrote it is kept, in the order it was read, for decoding to report (<see cref="Report"/>):
/// each abbreviation (<see cref="PdfDiagnosticCodes.FilterNameAbbreviated"/>), and each value it replaced
/// (<see cref="PdfDiagnosticCodes.FilterParametersInvalid"/>), with its place. What it keeps stays within what the guard
/// on filters lets run: seven entries for an element at most, and one for the <c>/DecodeParms</c> entry.
/// </para>
/// <para>
/// The parameters of a step are paired with it as qpdf pairs them, so that the shapes ISO 32000-1 (7.3.8.2) does not
/// allow still decode as they were written to: the element at the step's place of a <c>/DecodeParms</c> array — the
/// first, beside a single name —, or a <c>/DecodeParms</c> dictionary for every step of the chain.
/// </para>
/// <para>
/// The chain ends, too, at the element past <see cref="Documents.PdfReaderLimits.MaxFilterChainLength"/>, the reader's
/// guard: ISO 32000 sets no bound, and each filter of a <c>/Filter</c> array of millions costs decoding a few
/// microseconds, some of them a table of tens of kilobytes, whatever data it is given. A <c>/Crypt</c> filter counts
/// towards it, though decoding runs no step for it.
/// </para>
/// <para>
/// It ends, as well, at a value that leads to an object one of the reader's guards cut, or left out of the index: the
/// <c>/Filter</c> entry or an element of it, <c>/DecodeParms</c> or a value of a step's parameters. What the cut left of
/// it — null, a name it parted, a value it may yet go on past — is the guard's, and no filter the file gave: the step it
/// is read for, and those after it, do not run, and the data is left as the steps before them decode it, the guard
/// reported where it was reached (#263).
/// </para>
/// <para>
/// What was resolved is kept only where the file did not write it plainly. The <c>/Filter</c> array is read again as the
/// steps run, so a name it holds costs nothing more; the name an element written as a reference led to is kept, a slot
/// each. A dictionary that serves every step is read once for Flate and once for LZW, and of an array of parameters, only
/// the values that are not those of no parameters at all are kept, each with its step's place. The array can hold
/// millions of elements — an object stream's member is read from as much data as the stream decodes to —, and a chain
/// that kept an entry for each would cost several times the array on every decode. What it keeps for references stays
/// within what the guard lets run, 64 entries by default.
/// </para>
/// </remarks>
internal sealed class FilterChain
{
    private static readonly FilterChain NoFilter = new(new Reading());

    /// <summary>The entry of a <c>/Crypt</c> step's parameters that names its crypt filter (ISO 32000-1, Table 14).</summary>
    private static readonly PdfName CryptFilterKey = PdfName.Get("Name");

    /// <summary>The crypt filter that passes its data along as it is, a <c>/Crypt</c> step's when it names none.</summary>
    private static readonly PdfName Identity = PdfName.Get("Identity");

    /// <summary>The entries of a step's parameters dictionary decoding may read, every one it reads.</summary>
    private static readonly PdfName[] ParametersRead =
        [PdfName.EarlyChange, PdfName.Predictor, PdfName.Colors, PdfName.BitsPerComponent, PdfName.Columns, CryptFilterKey];

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

    /// <summary>What the chain reads otherwise than the file wrote it, in the order it was read.</summary>
    private readonly Fault[]? _faults;

    private readonly int _faultCount;

    private FilterChain(in Reading reading)
    {
        _filters = reading.Filters;
        _names = reading.Names;
        _placed = reading.Placed;
        _placedCount = reading.PlacedCount;
        _flate = reading.Flate;
        _lzw = reading.Lzw;
        _length = reading.Length;
        _faults = reading.Faults;
        _faultCount = reading.FaultCount;
        LastIndex = reading.LastIndex;
        End = reading.End;
        EndIndex = reading.EndIndex;
        EndValue = reading.EndValue;
    }

    /// <summary>
    /// Gets the entries of a step's parameters dictionary decoding may read: LZW's <c>/EarlyChange</c>, a Flate or LZW
    /// step's <c>/Predictor</c>, <c>/Colors</c>, <c>/BitsPerComponent</c> and <c>/Columns</c>, and a <c>/Crypt</c> step's
    /// <c>/Name</c>. What keeps a parameters dictionary for what decoding reads of it — the validator's walk of the object
    /// streams' dependencies (#277) — needs nothing else of it; a filter that reads another entry adds it here.
    /// </summary>
    /// <remarks>
    /// That walk keeps, of each of these entries, only a reference, which it follows, an integer or a real: decoding reads
    /// every one of them but <c>/Name</c> through <see cref="PdfObjectExtensions.AsInteger"/>, which reads a value of any
    /// other kind as it reads an entry absent (#181), and <c>/Name</c> as a name, which says what to report and decides
    /// nothing decoding reads after it, so that the walk, which follows what decoding loads, needs none of it but a
    /// reference. A filter that reads one of them as another kind, and reads on as it says, widens what the walk keeps.
    /// </remarks>
    internal static ReadOnlySpan<PdfName> ParameterKeys => ParametersRead;

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
    /// filter, or the <c>/Filter</c> entry that is neither a name nor an array; null when nothing ends it, an image filter
    /// or the guard does.
    /// </summary>
    public PdfObject? EndValue { get; }

    /// <summary>
    /// Resolves what the filters of the stream <paramref name="dictionary"/> describes are, and the values of their
    /// parameters decoding reads.
    /// </summary>
    /// <param name="dictionary">The stream's dictionary.</param>
    /// <param name="maxFilters">The most filters the stream's data may pass through: the chain ends at the element past them.</param>
    public static FilterChain Resolve(PdfDictionary dictionary, int maxFilters)
    {
        var reading = Read(dictionary, maxFilters, keep: true);
        return reading.Filters is null && reading.End == FilterChainEnd.None ? NoFilter : new FilterChain(reading);
    }

    /// <summary>
    /// Resolves what ends the chain of filters the stream <paramref name="dictionary"/> describes, as decoding resolves it,
    /// and keeps nothing: the parameters decoding reads are read too, since a value among them that leads to an object a
    /// guard cut, or left out of the index, ends the chain where it is read (#263).
    /// </summary>
    /// <param name="dictionary">The stream's dictionary.</param>
    /// <param name="maxFilters">The most filters the stream's data may pass through: the chain ends at the element past them.</param>
    public static FilterChainEnd EndOf(PdfDictionary dictionary, int maxFilters) =>
        Read(dictionary, maxFilters, keep: false).End;

    /// <summary>
    /// Follows, through <paramref name="reader"/>, every value decoding the stream <paramref name="dictionary"/> describes
    /// reads before any of its data, in the order decoding reads them, and keeps nothing.
    /// </summary>
    /// <param name="dictionary">The stream's dictionary.</param>
    /// <param name="maxFilters">The most filters the stream's data may pass through: the chain ends at the element past them.</param>
    /// <param name="reader">Follows each value to what it leads to, and can end the reading.</param>
    /// <returns>False when <paramref name="reader"/> ended the reading.</returns>
    public static bool TryFollow<TReader>(PdfDictionary dictionary, int maxFilters, ref TReader reader)
        where TReader : struct, IFilterValueReader =>
        TryRead(dictionary, maxFilters, ref reader, keep: false, out _);

    /// <summary>Gets the steps decoding runs, in order.</summary>
    public Steps GetEnumerator() => new(this);

    /// <summary>
    /// Gives the filter <paramref name="name"/> names: the filter an abbreviation ISO 32000-1 allows only in an inline
    /// image's dictionary (Table 94) stands for — <c>/AHx</c>, <c>/A85</c>, <c>/LZW</c>, <c>/Fl</c>, <c>/RL</c>,
    /// <c>/CCF</c>, <c>/DCT</c> —, or the name itself.
    /// </summary>
    /// <param name="name">A name a <c>/Filter</c> gives.</param>
    internal static PdfName FullName(PdfName name) => name.Value switch
    {
        "AHx" => PdfName.ASCIIHexDecode,
        "A85" => PdfName.ASCII85Decode,
        "LZW" => PdfName.LZWDecode,
        "Fl" => PdfName.FlateDecode,
        "RL" => PdfName.RunLengthDecode,
        "CCF" => PdfName.CCITTFaxDecode,
        "DCT" => PdfName.DCTDecode,
        _ => name,
    };

    /// <summary>
    /// Reports what the chain read otherwise than the file wrote it, in the order it read it, at
    /// <paramref name="data"/>'s position: each filter named by its abbreviation, a repair
    /// (<see cref="PdfDiagnosticCodes.FilterNameAbbreviated"/>), and each value it replaced, a warning
    /// (<see cref="PdfDiagnosticCodes.FilterParametersInvalid"/>) — but a crypt filter a <c>/Crypt</c> step names in data
    /// that may be encrypted (<see cref="PdfStreamData.MayBeEncrypted"/>), where the name is the security handler's to
    /// read.
    /// </summary>
    /// <param name="diagnostics">Receives the reports.</param>
    /// <param name="data">The stream's data.</param>
    public void Report(PdfDiagnostics? diagnostics, PdfStreamData data)
    {
        if (diagnostics is null)
        {
            return;
        }

        for (var index = 0; index < _faultCount; index++)
        {
            var fault = _faults![index];

            if (fault.Kind == FaultKind.Abbreviated)
            {
                diagnostics.Repair(PdfDiagnosticCodes.FilterNameAbbreviated, Describe(fault), data.Position);
            }
            else if (fault.Kind != FaultKind.CryptFilterNamed || !data.MayBeEncrypted)
            {
                diagnostics.Warn(PdfDiagnosticCodes.FilterParametersInvalid, Describe(fault), data.Position);
            }
        }
    }

    /// <summary>
    /// The steps of a chain, each with the values of its parameters, as decoding runs them: every filter before the end
    /// of the chain but <c>/Crypt</c>, which passes its data along as it is — the identity crypt filter's, and any other's
    /// until the security handler decrypts (M16).
    /// </summary>
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
            while (++_index < chain._length)
            {
                // Every element before the end is a name, or a reference that led to one, which was kept in order.
                var element = chain._filters is PdfArray array ? array[_index] : chain._filters;
                var name = element is PdfName written ? FullName(written) : chain._names![_name++];

                if (name == PdfName.Crypt)
                {
                    continue;
                }

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

            return false;
        }
    }

    /// <summary>
    /// Reads, through <paramref name="reader"/>, what the filters of the stream <paramref name="dictionary"/> describes are,
    /// and the values of their parameters decoding reads, in the order decoding needs them.
    /// </summary>
    /// <param name="dictionary">The stream's dictionary.</param>
    /// <param name="maxFilters">The most filters the stream's data may pass through: the chain ends at the element past them.</param>
    /// <param name="reader">Follows each value to what it leads to, and can end the reading.</param>
    /// <param name="keep">Whether what decoding needs again is kept, or only what ends the chain.</param>
    /// <param name="reading">What was read.</param>
    /// <returns>False when <paramref name="reader"/> ended the reading.</returns>
    private static bool TryRead<TReader>(
        PdfDictionary dictionary, int maxFilters, ref TReader reader, bool keep, out Reading reading)
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
        var cryptRead = false;
        PdfObject? parameters = null;

        for (var index = 0; index < length; index++)
        {
            // Past the guard, nothing is read: the element there ends the chain as a filter the library does not know does.
            if (index == maxFilters)
            {
                reading.End = FilterChainEnd.Limit;
                reading.EndIndex = index;
                return true;
            }

            var written = array is null ? filters : array[index];
            var element = written;

            if (array is not null && !reader.TryRead(PdfName.Filter, written, entry: false, out element))
            {
                return false;
            }

            if (element is PdfName read && FullName(read) is var full && !ReferenceEquals(full, read))
            {
                if (keep)
                {
                    AddFault(ref reading, new Fault(FaultKind.Abbreviated, index, read, Value: null));
                }

                element = full;
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

            if (filter == PdfName.FlateDecode || filter == PdfName.LZWDecode || filter == PdfName.Crypt)
            {
                if (!parametersRead)
                {
                    if (!reader.TryRead(PdfName.DecodeParms, dictionary.GetRaw(PdfName.DecodeParms), entry: true, out parameters))
                    {
                        return false;
                    }

                    parametersRead = true;

                    if (keep && parameters is not (null or PdfNull or PdfArray) && parameters.AsDictionary() is null)
                    {
                        AddFault(ref reading, new Fault(FaultKind.ParametersNotDictionaryOrArray, -1, Name: null, parameters));
                    }
                }

                if (parameters is PdfArray parameterArray)
                {
                    var placed = index < parameterArray.Count ? parameterArray[index] : null;

                    if (!reader.TryRead(PdfName.DecodeParms, placed, entry: false, out placed))
                    {
                        return false;
                    }

                    // A null element is a step without parameters, as ISO 32000-1 writes it (7.3.8.2); a missing one or
                    // one of another kind leaves the step without the parameters the file may have meant it to have.
                    if (keep && index >= parameterArray.Count)
                    {
                        AddFault(ref reading, new Fault(FaultKind.ElementMissing, index, filter, Value: null));
                    }
                    else if (keep && placed is not (null or PdfNull) && placed.AsDictionary() is null)
                    {
                        AddFault(ref reading, new Fault(FaultKind.ElementNotDictionary, index, filter, placed));
                    }

                    if (filter == PdfName.Crypt)
                    {
                        if (!TryReadCrypt(placed.AsDictionary(), index, ref reader, keep, ref reading))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        if (!TryReadParameters(filter, placed.AsDictionary(), index, ref reader, keep, described: true, ref reading, out var values))
                        {
                            return false;
                        }

                        if (keep && values != new FilterParameters())
                        {
                            Append(ref reading.Placed, ref reading.PlacedCount, new Placed(index, values));
                        }
                    }
                }
                else if (filter == PdfName.Crypt)
                {
                    // Of a dictionary, the name is read once: it says the same of every /Crypt step.
                    if (!cryptRead && !TryReadCrypt(parameters.AsDictionary(), -1, ref reader, keep, ref reading))
                    {
                        return false;
                    }

                    cryptRead = true;
                }
                else if (filter == PdfName.FlateDecode ? !flateRead : !lzwRead)
                {
                    // A dictionary serves every step: its values are read once for each kind of step that reads them, and
                    // what it gives ISO 32000-1 does not allow is kept once, though both kinds read the predictor's values.
                    var described = keep && !flateRead && !lzwRead;

                    if (!TryReadParameters(filter, parameters.AsDictionary(), -1, ref reader, keep, described, ref reading, out var values))
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

            reading.LastIndex = filter == PdfName.Crypt ? reading.LastIndex : index;
            reading.Length++;
        }

        return true;
    }

    /// <summary>
    /// Resolves the chain of filters the stream <paramref name="dictionary"/> describes, as decoding reads it: a value that
    /// leads to an object one of the reader's guards cut, or left out of the index, ends it where it is read, as the
    /// element past the guard on filters does — the steps before it run, and what they decode is left encoded by the rest
    /// (#263).
    /// </summary>
    private static Reading Read(PdfDictionary dictionary, int maxFilters, bool keep)
    {
        var reader = default(Resolving);

        if (!TryRead(dictionary, maxFilters, ref reader, keep, out var reading))
        {
            // The steps read whole before the value stand: the chain ends at the element that value was read for.
            reading.End = FilterChainEnd.Guard;
            reading.EndIndex = reading.Length;
            reading.EndValue = null;
        }

        return reading;
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

    /// <summary>
    /// Gives the int nearest <paramref name="value"/>, or <paramref name="absent"/> when there is none: a value past what an
    /// int holds is no other value its low bits would name.
    /// </summary>
    private static int Nearest(long? value, int absent) =>
        value is { } read ? (int)Math.Clamp(read, int.MinValue, int.MaxValue) : absent;

    /// <summary>
    /// Reads the values of a Flate or LZW step's parameters that decoding it reads, each as ISO 32000-1 (Table 8) allows
    /// it, or as other readers read one it does not: a <c>/Predictor</c> other than 1, 2 or 10 to 15 as none, and one past
    /// 15 as a PNG predictor, as PDFium splits them; a <c>/BitsPerComponent</c> other than 1, 2, 4, 8 or 16 as 8; a
    /// <c>/Colors</c> or <c>/Columns</c> below 1 as 1; an <c>/EarlyChange</c> other than 0 as 1; a value that is no integer
    /// as the entry absent. With <paramref name="keep"/>, each value so replaced is kept for decoding to report — of the
    /// values Flate and LZW both read, only with <paramref name="described"/>.
    /// </summary>
    /// <param name="filter">The step's filter.</param>
    /// <param name="parameters">The step's parameters, or null when it has none.</param>
    /// <param name="index">Where in the <c>/DecodeParms</c> array the parameters stand, or -1 for its dictionary.</param>
    /// <param name="reader">Follows each value.</param>
    /// <param name="keep">Whether what decoding needs again is kept.</param>
    /// <param name="described">Whether the values Flate and LZW both read are kept when replaced, not kept already.</param>
    /// <param name="reading">Receives what the values give that ISO 32000-1 does not allow.</param>
    /// <param name="values">The values decoding reads.</param>
    private static bool TryReadParameters<TReader>(
        PdfName filter,
        PdfDictionary? parameters,
        int index,
        ref TReader reader,
        bool keep,
        bool described,
        ref Reading reading,
        out FilterParameters values)
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

            values = values with { EarlyChange = Read(earlyChange, PdfName.EarlyChange, index, keep, ref reading) };
        }

        keep &= described;

        if (!reader.TryRead(PdfName.DecodeParms, parameters.GetRaw(PdfName.Predictor), entry: false, out var predictor))
        {
            return false;
        }

        values = values with { Predictor = Read(predictor, PdfName.Predictor, index, keep, ref reading) };

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
            Colors = Read(colors, PdfName.Colors, index, keep, ref reading),
            BitsPerComponent = Read(bitsPerComponent, PdfName.BitsPerComponent, index, keep, ref reading),
            Columns = Read(columns, PdfName.Columns, index, keep, ref reading),
        };
        return true;
    }

    /// <summary>
    /// Reads <paramref name="value"/>, the parameter <paramref name="key"/>, which decoding reads as an integer: the int
    /// nearest it, as <see cref="Allowed"/> reads it, or as the entry absent when it is of another kind. With
    /// <paramref name="keep"/>, one of another kind, or one read as another value, is kept for decoding to report.
    /// </summary>
    private static int Read(PdfObject? value, PdfName key, int index, bool keep, ref Reading reading)
    {
        var written = value.AsInteger();
        var nearest = Nearest(written, key == PdfName.BitsPerComponent ? 8 : 1);
        var read = Allowed(key, nearest);

        if (keep && (written is null ? value is not (null or PdfNull) : read != nearest))
        {
            AddFault(ref reading, new Fault(written is null ? FaultKind.ValueOfWrongKind : FaultKind.ValueReplaced, index, key, value));
        }

        return read;
    }

    /// <summary>
    /// Gives the value decoding reads the int <paramref name="value"/> of the parameter <paramref name="key"/> as: itself
    /// where ISO 32000-1 (Table 8) allows it; otherwise, of <c>/Predictor</c>, 15 past 15 — a PNG predictor, whose rows say
    /// how each was predicted — and 1, no predictor, for any other, of <c>/BitsPerComponent</c> 8, of <c>/EarlyChange</c>
    /// 1, and of <c>/Colors</c> or <c>/Columns</c> 1.
    /// </summary>
    private static int Allowed(PdfName key, int value) =>
        key == PdfName.Predictor ? (value is 1 or 2 or (>= 10 and <= 15) ? value : value > 15 ? 15 : 1) :
        key == PdfName.BitsPerComponent ? (value is 1 or 2 or 4 or 8 or 16 ? value : 8) :
        key == PdfName.EarlyChange ? (value == 0 ? 0 : 1) :
        Math.Max(value, 1);

    /// <summary>
    /// Reads the <c>/Name</c> of a <c>/Crypt</c> step's parameters, which names the crypt filter that decrypts its data,
    /// the identity filter where they give none or give no name (ISO 32000-1, 7.4.10). Decoding applies none: with
    /// <paramref name="keep"/>, a name other than <c>/Identity</c> is kept for it to report where no encryption dictionary
    /// can define it, and a value of another kind, read as absent, as one it replaced.
    /// </summary>
    private static bool TryReadCrypt<TReader>(PdfDictionary? parameters, int index, ref TReader reader, bool keep, ref Reading reading)
        where TReader : struct, IFilterValueReader
    {
        if (parameters is null)
        {
            return true;
        }

        if (!reader.TryRead(PdfName.DecodeParms, parameters.GetRaw(CryptFilterKey), entry: false, out var name))
        {
            return false;
        }

        if (!keep || name is null or PdfNull || (name is PdfName given && given == Identity))
        {
            return true;
        }

        AddFault(ref reading, new Fault(name is PdfName ? FaultKind.CryptFilterNamed : FaultKind.ValueOfWrongKind, index, CryptFilterKey, name));
        return true;
    }

    /// <summary>Keeps <paramref name="fault"/>, for decoding to report.</summary>
    private static void AddFault(ref Reading reading, Fault fault) => Append(ref reading.Faults, ref reading.FaultCount, fault);

    /// <summary>Says what the file wrote that <paramref name="fault"/> stands for, and what it was read as.</summary>
    private string Describe(in Fault fault)
    {
        var where = fault.Index < 0 ? "The /DecodeParms entry" : Invariant($"Element {fault.Index} of the /DecodeParms array");
        var key = fault.Name is null ? string.Empty : FileQuote.Name(fault.Name);
        var several = _filters is PdfArray;

        return fault.Kind switch
        {
            FaultKind.Abbreviated =>
                $"{(several ? Invariant($"Element {fault.Index} of the /Filter entry") : "The /Filter entry")} names {key}, an abbreviation ISO 32000-1 allows only in an inline image; it was read as {FileQuote.Name(FullName(fault.Name!))}.",
            FaultKind.ValueReplaced => Invariant(
                $"{where} gives {key} {fault.Value.AsInteger()}, which ISO 32000-1 does not allow; it was read as {Said(fault.Name!, Allowed(fault.Name!, Nearest(fault.Value.AsInteger(), 1)))}."),
            FaultKind.ValueOfWrongKind =>
                $"{where} gives {key} as {PdfObjectParser.KindOf(fault.Value)}, where ISO 32000-1 wants {(fault.Name == CryptFilterKey ? "a name" : "an integer")}; it was read as absent, which gives {Said(fault.Name!, fault.Name == PdfName.BitsPerComponent ? 8 : 1)}.",
            FaultKind.ParametersNotDictionaryOrArray =>
                $"{where} is {PdfObjectParser.KindOf(fault.Value)}, neither a dictionary nor an array; {(several ? "the filters decode" : "the filter decodes")} with no parameters.",
            FaultKind.ElementNotDictionary =>
                $"{where} is {PdfObjectParser.KindOf(fault.Value)}, neither a dictionary nor null; the {key} filter it is for decodes with no parameters.",
            FaultKind.ElementMissing => several
                ? Invariant($"The /DecodeParms array holds no element for the {key} filter at element {fault.Index} of the /Filter entry; it decodes with no parameters.")
                : $"The /DecodeParms array holds no element for the {key} filter; it decodes with no parameters.",
            _ => $"{where} names the crypt filter {FileQuote.Name((PdfName)fault.Value!)}, which no /Encrypt dictionary defines; the /Crypt filter passed its data along as it is.",
        };

        // What a parameter was read as, in words: a predictor by its kind, a crypt filter by its name.
        static string Said(PdfName key, int read) =>
            key == PdfName.Predictor ? (read == 1 ? "1, no predictor" : "a PNG predictor") :
            key == CryptFilterKey ? "the identity crypt filter" :
            read.ToString(CultureInfo.InvariantCulture);
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

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
        public Fault[]? Faults;
        public int FaultCount;
    }

    /// <summary>What the chain reads otherwise than the file wrote it.</summary>
    private enum FaultKind
    {
        /// <summary>A filter named by its abbreviation, read as the filter it abbreviates.</summary>
        Abbreviated,

        /// <summary>An integer ISO 32000-1 does not allow the parameter, read as another.</summary>
        ValueReplaced,

        /// <summary>A parameter's value of the wrong kind, read as the entry absent.</summary>
        ValueOfWrongKind,

        /// <summary>A <c>/DecodeParms</c> that is neither a dictionary nor an array, which gives no filter parameters.</summary>
        ParametersNotDictionaryOrArray,

        /// <summary>An element of the <c>/DecodeParms</c> array that is neither a dictionary nor null.</summary>
        ElementNotDictionary,

        /// <summary>A <c>/DecodeParms</c> array too short to reach a step that reads parameters.</summary>
        ElementMissing,

        /// <summary>A crypt filter a <c>/Crypt</c> step names other than the identity filter, which decoding does not apply.</summary>
        CryptFilterNamed,
    }

    /// <summary>Something the chain reads otherwise than the file wrote it.</summary>
    /// <param name="Kind">What it is.</param>
    /// <param name="Index">
    /// Where it stands: the element of the <c>/Filter</c> array — of an abbreviation, a missing element's step —, or of the
    /// <c>/DecodeParms</c> array, -1 for the <c>/DecodeParms</c> dictionary.
    /// </param>
    /// <param name="Name">The abbreviation, the step's filter, or the parameter's key, as <paramref name="Kind"/> says.</param>
    /// <param name="Value">The value the file wrote, as resolved, where <paramref name="Kind"/> quotes one.</param>
    private readonly record struct Fault(FaultKind Kind, int Index, PdfName? Name, PdfObject? Value);

    /// <summary>
    /// Follows each value as <see cref="PdfReference.Resolve"/> does, and ends the reading at one that leads to an object
    /// one of the reader's guards cut, or left out of the index (<see cref="PdfFileReader.LeftByGuard"/>).
    /// </summary>
    private struct Resolving : IFilterValueReader
    {
        public readonly bool TryRead(PdfName key, PdfObject? written, bool entry, out PdfObject? value)
        {
            value = written?.Resolve();

            if (written is PdfReference reference && reference.Source is PdfFileReader reader && reader.LeftByGuard(reference))
            {
                value = null;
                return false;
            }

            return true;
        }
    }
}
