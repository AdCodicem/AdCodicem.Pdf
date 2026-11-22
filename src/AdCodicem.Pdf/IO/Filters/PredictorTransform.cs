namespace AdCodicem.Pdf.IO.Filters;

/// <summary>
/// Undoes the predictor applied before compression.
/// </summary>
/// <remarks>
/// Predictors make data compress better by storing differences rather than values. Cross-reference
/// streams almost always use the PNG "up" predictor, so this is on the critical path of opening any
/// modern document.
/// </remarks>
internal static class PredictorTransform
{
    /// <summary>
    /// Undoes the predictor into <paramref name="result"/>, or returns false, the data untouched, when the
    /// parameters describe rows the data cannot hold even once, and the data was not cut.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The parameters come from the file. A /Columns of sixty million would size two rows of 240 MB for a
    /// stream of twelve bytes, and one whose row length overflows would loop for ever or throw; the row
    /// length is computed in 64 bits without overflow and weighed against the data before anything is allocated, so that
    /// every buffer is bounded by the data, which a guard bounds. ISO 32000-1 (Table 8) sets no bound on
    /// <c>/Colors</c>, and none is set here: a pixel of a thousand components decodes as one of three.
    /// </para>
    /// <para>
    /// A TIFF predictor (2) adds to each sample the one of the same component before it in its row, modulo 2 to the
    /// power of the bits a component holds (TIFF 6.0, section 14): samples of 1, 2 and 4 bits are packed from the high bit
    /// of each byte, and the bits that pad a row to a byte are left alone, and those of 16 bits are big-endian; the rows
    /// are undone in place. A PNG predictor (10 to 15) undoes each row as the filter type its first byte names says,
    /// one past 4, which PNG does not define, read as unfiltered, as MuPDF, qpdf and PDFium read it; a pixel's bytes,
    /// which the left and upper-left neighbours are counted in, are rounded up, as PNG counts them.
    /// </para>
    /// <para>
    /// A last row the data holds in part is kept and undone as far as it goes — each byte a predictor predicts is
    /// predicted from bytes before it —, and said in <paramref name="faults"/> where the data was not cut.
    /// </para>
    /// </remarks>
    /// <param name="data">The data as its filter decoded it.</param>
    /// <param name="predictor">The step's <c>/Predictor</c>.</param>
    /// <param name="colors">The step's <c>/Colors</c>.</param>
    /// <param name="bitsPerComponent">The step's <c>/BitsPerComponent</c>.</param>
    /// <param name="columns">The step's <c>/Columns</c>.</param>
    /// <param name="cut">
    /// Whether the reader cut the data short at one of its limits. Data the reader cut ends where it stopped, not
    /// where a row does, and data shorter than one row says nothing of the parameters.
    /// </param>
    /// <param name="result">The data, its predictor undone.</param>
    /// <param name="faults">What the data holds that the predictor's format does not allow.</param>
    public static bool TryApply(
        byte[] data, int predictor, int colors, int bitsPerComponent, int columns, bool cut, out byte[] result, out PredictorFaults faults)
    {
        result = data;
        faults = default;

        if (predictor <= 1 || data.Length == 0)
        {
            return true;
        }

        colors = Math.Max(colors, 1);
        bitsPerComponent = bitsPerComponent switch
        {
            1 or 2 or 4 or 8 or 16 => bitsPerComponent,
            _ => 8,
        };
        columns = Math.Max(columns, 1);

        // A row's samples number 2^62 at most; past what a long holds in bits, a row is longer than any data, and taken as
        // one short of that, which leaves room for a PNG row's filter type.
        var samples = (long)columns * colors;
        var rowBits = samples <= (long.MaxValue - 7) / bitsPerComponent ? samples * bitsPerComponent : long.MaxValue - 15;
        var rowLength = (rowBits + 7) / 8;

        // A TIFF-predicted row is the row itself; a PNG-predicted one carries a byte saying how it was
        // predicted.
        var stride = predictor == 2 ? rowLength : rowLength + 1;

        if (!cut && stride > data.Length)
        {
            return false;
        }

        var held = data.Length % stride;
        faults = new PredictorFaults(cut || held == 0 ? 0 : (int)held, stride, 0, 0, 0, 0);

        if (predictor == 2)
        {
            for (long rowStart = 0; rowStart < data.Length; rowStart += rowLength)
            {
                UndoTiffRow(data.AsSpan((int)rowStart, (int)Math.Min(rowLength, data.Length - rowStart)), colors, bitsPerComponent, samples);
            }

            return true;
        }

        // Every index a PNG row is read at is below the data's length: a pixel of as many bytes or more leaves every byte
        // without a left neighbour, as one of exactly that many does, and no file tells them apart.
        var bytesPerPixel = (int)Math.Min(((long)colors * bitsPerComponent + 7) / 8, data.Length);
        result = ApplyPng(data, bytesPerPixel, rowLength, ref faults);
        return true;
    }

    /// <summary>
    /// Undoes a TIFF-predicted row, or the part of one the data holds, in place: each sample but those of its first pixel
    /// adds the sample <paramref name="colors"/> before it, modulo 2 to the power of <paramref name="bitsPerComponent"/>.
    /// </summary>
    private static void UndoTiffRow(Span<byte> row, int colors, int bitsPerComponent, long samples)
    {
        switch (bitsPerComponent)
        {
            case 8:
                for (var index = colors; index < row.Length; index++)
                {
                    row[index] += row[index - colors];
                }

                break;

            case 16:
                // A pair the row holds in part is no sample, and is left as it is.
                for (var index = 2L * colors; index + 1 < row.Length; index += 2)
                {
                    var at = (int)index;
                    var before = at - (2 * colors);
                    var value = ((row[at] << 8) | row[at + 1]) + ((row[before] << 8) | row[before + 1]);
                    row[at] = (byte)(value >> 8);
                    row[at + 1] = (byte)value;
                }

                break;

            default:
                // 1, 2 or 4 bits: no more samples than the row holds whole, its padding past the last left alone.
                var mask = (1 << bitsPerComponent) - 1;
                var count = Math.Min(samples, (long)row.Length * 8 / bitsPerComponent);

                for (long sample = colors; sample < count; sample++)
                {
                    var bit = sample * bitsPerComponent;
                    var shift = 8 - bitsPerComponent - (int)(bit % 8);
                    ref var target = ref row[(int)(bit / 8)];
                    var value = ((target >> shift) + Sample(row, sample - colors, bitsPerComponent)) & mask;
                    target = (byte)((target & ~(mask << shift)) | (value << shift));
                }

                break;
        }
    }

    /// <summary>Reads sample <paramref name="sample"/> of a row of samples of 1, 2 or 4 bits, packed from the high bit.</summary>
    private static int Sample(ReadOnlySpan<byte> row, long sample, int bitsPerComponent)
    {
        var bit = sample * bitsPerComponent;
        return row[(int)(bit / 8)] >> (8 - bitsPerComponent - (int)(bit % 8));
    }

    private static byte[] ApplyPng(byte[] data, int bytesPerPixel, long rowLength, ref PredictorFaults faults)
    {
        // Every PNG-predicted row carries one extra byte saying how it was predicted. The rows the data holds whole are
        // undone, and the part of a last row it holds after that byte.
        var stride = rowLength + 1;
        var rows = (int)(data.Length / stride);
        var partial = (int)Math.Max(0, data.Length - (rows * stride) - 1);
        var width = rows > 0 ? (int)rowLength : partial;
        var output = new byte[(rows * width) + partial];
        var previous = new byte[width];
        var current = new byte[width];
        var unknown = 0;
        var firstUnknown = 0;
        var firstTag = 0;

        for (var row = 0; row < rows + (partial > 0 ? 1 : 0); row++)
        {
            var source = (int)(row * stride);
            var length = row < rows ? width : partial;
            var filter = data[source];
            Array.Copy(data, source + 1, current, 0, length);

            switch (filter)
            {
                case 0:
                    break;

                case 1:
                    for (var i = bytesPerPixel; i < length; i++)
                    {
                        current[i] += current[i - bytesPerPixel];
                    }

                    break;

                case 2:
                    for (var i = 0; i < length; i++)
                    {
                        current[i] += previous[i];
                    }

                    break;

                case 3:
                    for (var i = 0; i < length; i++)
                    {
                        var left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                        current[i] += (byte)((left + previous[i]) / 2);
                    }

                    break;

                case 4:
                    for (var i = 0; i < length; i++)
                    {
                        var left = i >= bytesPerPixel ? current[i - bytesPerPixel] : (byte)0;
                        var upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : (byte)0;
                        current[i] += Paeth(left, previous[i], upLeft);
                    }

                    break;

                default:
                    // A filter type PNG does not define: the row is read as unfiltered rather than the stream abandoned.
                    if (unknown++ == 0)
                    {
                        firstUnknown = row + 1;
                        firstTag = filter;
                    }

                    break;
            }

            Array.Copy(current, 0, output, row * width, length);
            (previous, current) = (current, previous);
        }

        faults = faults with
        {
            Rows = rows + (partial > 0 ? 1 : 0),
            UnknownTags = unknown,
            FirstUnknownRow = firstUnknown,
            FirstUnknownTag = firstTag,
        };
        return output;
    }

    private static byte Paeth(byte left, byte up, byte upLeft)
    {
        var estimate = left + up - upLeft;
        var distanceLeft = Math.Abs(estimate - left);
        var distanceUp = Math.Abs(estimate - up);
        var distanceUpLeft = Math.Abs(estimate - upLeft);

        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft)
        {
            return left;
        }

        return distanceUp <= distanceUpLeft ? up : upLeft;
    }
}
