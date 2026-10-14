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
/// </remarks>
internal sealed class FilterChain
{
    private static readonly FilterChain NoFilter = new([], 0, FilterChainEnd.None, index: -1, value: null);

    private readonly FilterStep[] _steps;

    private FilterChain(FilterStep[] steps, int count, FilterChainEnd end, int index, PdfObject? value)
    {
        _steps = steps;
        Count = count;
        End = end;
        EndIndex = index;
        EndValue = value;
    }

    /// <summary>Gets how many filters decoding runs.</summary>
    public int Count { get; }

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

    /// <summary>Gets the filter decoding runs <paramref name="index"/>th.</summary>
    public ref readonly FilterStep this[int index] => ref _steps.AsSpan(0, Count)[index];

    /// <summary>
    /// Resolves what the filters of the stream <paramref name="dictionary"/> describes are, and the values of their
    /// parameters decoding reads.
    /// </summary>
    /// <param name="dictionary">The stream's dictionary.</param>
    /// <param name="readParameters">Whether the parameters are read, or only the names of the filters.</param>
    public static FilterChain Resolve(PdfDictionary dictionary, bool readParameters)
    {
        var reader = default(Resolving);
        TryRead(dictionary, ref reader, readParameters, out var chain);
        return chain!;
    }

    /// <summary>
    /// Reads, through <paramref name="reader"/>, what the filters of the stream <paramref name="dictionary"/> describes are,
    /// and the values of their parameters decoding reads, in the order decoding needs them.
    /// </summary>
    /// <param name="dictionary">The stream's dictionary.</param>
    /// <param name="reader">Follows each value to what it leads to, and can end the reading.</param>
    /// <param name="readParameters">Whether the parameters are read, or only the names of the filters.</param>
    /// <param name="chain">The filters, or null when <paramref name="reader"/> ended the reading.</param>
    /// <returns>False when <paramref name="reader"/> ended the reading.</returns>
    public static bool TryRead<TReader>(PdfDictionary dictionary, ref TReader reader, bool readParameters, out FilterChain? chain)
        where TReader : struct, IFilterValueReader
    {
        chain = null;

        if (!reader.TryRead(PdfName.Filter, dictionary.GetRaw(PdfName.Filter), entry: true, out var filters))
        {
            return false;
        }

        // A /Filter that is null, or leads to null or to an object the file lacks, is one the dictionary does not have
        // (7.3.7, 7.3.10): the data is not encoded.
        if (filters is null or PdfNull)
        {
            chain = NoFilter;
            return true;
        }

        var array = filters as PdfArray;

        if (filters is not PdfName && array is null)
        {
            chain = new FilterChain([], 0, FilterChainEnd.NotAFilterEntry, index: -1, filters);
            return true;
        }

        var length = array?.Count ?? 1;
        var steps = new FilterStep[Math.Min(length, 4)];
        var count = 0;
        var parametersRead = false;
        PdfObject? parameters = null;

        for (var index = 0; index < length; index++)
        {
            var element = filters;

            if (array is not null && !reader.TryRead(PdfName.Filter, array[index], entry: false, out element))
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
                chain = new FilterChain(steps, count, end, index, end == FilterChainEnd.Image ? null : element);
                return true;
            }

            var filter = (PdfName)element!;
            var step = new FilterStep(filter);

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

                var written = parameters is PdfArray parameterArray
                    ? index < parameterArray.Count ? parameterArray[index] : null
                    : parameters;

                if ((parameters is PdfArray && !reader.TryRead(PdfName.DecodeParms, written, entry: false, out written)) ||
                    !TryReadParameters(filter, written.AsDictionary(), ref reader, ref step))
                {
                    return false;
                }
            }

            if (count == steps.Length)
            {
                Array.Resize(ref steps, Math.Min(length, steps.Length * 2));
            }

            steps[count++] = step;
        }

        chain = new FilterChain(steps, count, FilterChainEnd.None, index: -1, value: null);
        return true;
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

    /// <summary>Determines whether the library decodes the filter <paramref name="name"/>, rather than leaving its data encoded.</summary>
    private static bool IsSupported(PdfName name) =>
        name == PdfName.FlateDecode || name == PdfName.LZWDecode || name == PdfName.ASCII85Decode ||
        name == PdfName.ASCIIHexDecode || name == PdfName.RunLengthDecode || name == PdfName.Crypt;

    /// <summary>Reads the values of a Flate or LZW step's parameters that decoding it reads.</summary>
    private static bool TryReadParameters<TReader>(PdfName filter, PdfDictionary? parameters, ref TReader reader, ref FilterStep step)
        where TReader : struct, IFilterValueReader
    {
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

            step = step with { EarlyChange = (int)(earlyChange.AsInteger() ?? 1) };
        }

        if (!reader.TryRead(PdfName.DecodeParms, parameters.GetRaw(PdfName.Predictor), entry: false, out var predictor))
        {
            return false;
        }

        step = step with { Predictor = (int)(predictor.AsInteger() ?? 1) };

        // Without a predictor the other parameters mean nothing, and are not read.
        if (step.Predictor <= 1)
        {
            return true;
        }

        if (!reader.TryRead(PdfName.DecodeParms, parameters.GetRaw(PdfName.Colors), entry: false, out var colors) ||
            !reader.TryRead(PdfName.DecodeParms, parameters.GetRaw(PdfName.BitsPerComponent), entry: false, out var bitsPerComponent) ||
            !reader.TryRead(PdfName.DecodeParms, parameters.GetRaw(PdfName.Columns), entry: false, out var columns))
        {
            return false;
        }

        step = step with
        {
            Colors = (int)(colors.AsInteger() ?? 1),
            BitsPerComponent = (int)(bitsPerComponent.AsInteger() ?? 8),
            Columns = (int)Math.Clamp(columns.AsInteger() ?? 1, 1, int.MaxValue),
        };
        return true;
    }
}
