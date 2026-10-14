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
    /// The parameters come from the file. A /Columns of sixty million would size two rows of 240 MB for a
    /// stream of twelve bytes, and one whose row length overflows would loop for ever or throw; the row
    /// length is computed without overflow and weighed against the data before anything is allocated.
    /// </remarks>
    /// <param name="data">The data as its filter decoded it.</param>
    /// <param name="predictor">The step's <c>/Predictor</c>.</param>
    /// <param name="colors">The step's <c>/Colors</c>.</param>
    /// <param name="bitsPerComponent">The step's <c>/BitsPerComponent</c>.</param>
    /// <param name="columns">The step's <c>/Columns</c>.</param>
    /// <param name="cut">
    /// Whether the reader cut the data short at one of its limits. Data the reader cut ends where it stopped, not
    /// where a row does: the part of its last row it holds is undone as far as it goes — each byte a predictor
    /// predicts is predicted from bytes before it —, and data shorter than one row says nothing of the parameters.
    /// </param>
    /// <param name="result">The data, its predictor undone.</param>
    public static bool TryApply(byte[] data, int predictor, int colors, int bitsPerComponent, int columns, bool cut, out byte[] result)
    {
        result = data;

        if (predictor <= 1 || data.Length == 0)
        {
            return true;
        }

        colors = Math.Clamp(colors, 1, 32);
        bitsPerComponent = bitsPerComponent switch
        {
            1 or 2 or 4 or 8 or 16 => bitsPerComponent,
            _ => 8,
        };
        columns = Math.Max(columns, 1);

        var bytesPerPixel = Math.Max(1, colors * bitsPerComponent / 8);
        var rowLength = (((long)columns * colors * bitsPerComponent) + 7) / 8;

        // A TIFF-predicted row is the row itself; a PNG-predicted one carries a byte saying how it was
        // predicted.
        if (!cut && rowLength + (predictor == 2 ? 0 : 1) > data.Length)
        {
            return false;
        }

        result = predictor == 2
            ? ApplyTiff(data, colors, bitsPerComponent, rowLength, cut)
            : ApplyPng(data, bytesPerPixel, rowLength, cut);
        return true;
    }

    private static byte[] ApplyTiff(byte[] data, int colors, int bitsPerComponent, long rowLength, bool cut)
    {
        if (bitsPerComponent != 8)
        {
            // Sub-byte TIFF prediction is vanishingly rare; leaving the data alone beats corrupting it.
            return data;
        }

        var rowStart = 0;

        for (; rowStart + rowLength <= data.Length; rowStart += (int)rowLength)
        {
            for (var index = colors; index < rowLength; index++)
            {
                data[rowStart + index] = (byte)(data[rowStart + index] + data[rowStart + index - colors]);
            }
        }

        // A row the data holds whole is undone above; a partial one left uncut stays as it is (#162).
        if (cut)
        {
            for (var index = rowStart + colors; index < data.Length; index++)
            {
                data[index] = (byte)(data[index] + data[index - colors]);
            }
        }

        return data;
    }

    private static byte[] ApplyPng(byte[] data, int bytesPerPixel, long rowLength, bool cut)
    {
        // Every PNG-predicted row carries one extra byte saying how it was predicted. The rows the data holds whole are
        // undone, and of the data cut short, also the part of its last row after the byte that says how it was predicted.
        var stride = rowLength + 1;
        var rows = (int)(data.Length / stride);
        var partial = cut ? (int)Math.Max(0, data.Length - (rows * stride) - 1) : 0;
        var width = rows > 0 ? (int)rowLength : partial;
        var output = new byte[(rows * width) + partial];
        var previous = new byte[width];
        var current = new byte[width];

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
                    // An unknown predictor byte: treat the row as unfiltered rather than abandon the stream.
                    break;
            }

            Array.Copy(current, 0, output, row * width, length);
            (previous, current) = (current, previous);
        }

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
