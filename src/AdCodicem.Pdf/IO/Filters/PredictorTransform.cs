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
    /// parameters describe rows the data cannot hold even once.
    /// </summary>
    /// <remarks>
    /// The parameters come from the file. A /Columns of sixty million would size two rows of 240 MB for a
    /// stream of twelve bytes, and one whose row length overflows would loop for ever or throw; the row
    /// length is computed without overflow and weighed against the data before anything is allocated.
    /// </remarks>
    public static bool TryApply(byte[] data, int predictor, int colors, int bitsPerComponent, int columns, out byte[] result)
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
        if (rowLength + (predictor == 2 ? 0 : 1) > data.Length)
        {
            return false;
        }

        result = predictor == 2
            ? ApplyTiff(data, colors, bitsPerComponent, (int)rowLength)
            : ApplyPng(data, bytesPerPixel, (int)rowLength);
        return true;
    }

    private static byte[] ApplyTiff(byte[] data, int colors, int bitsPerComponent, int rowLength)
    {
        if (bitsPerComponent != 8)
        {
            // Sub-byte TIFF prediction is vanishingly rare; leaving the data alone beats corrupting it.
            return data;
        }

        for (var rowStart = 0; rowStart + rowLength <= data.Length; rowStart += rowLength)
        {
            for (var index = colors; index < rowLength; index++)
            {
                data[rowStart + index] = (byte)(data[rowStart + index] + data[rowStart + index - colors]);
            }
        }

        return data;
    }

    private static byte[] ApplyPng(byte[] data, int bytesPerPixel, int rowLength)
    {
        // Every PNG-predicted row carries one extra byte saying how it was predicted.
        var stride = rowLength + 1;
        var rows = data.Length / stride;
        var output = new byte[rows * rowLength];
        var previous = new byte[rowLength];
        var current = new byte[rowLength];

        for (var row = 0; row < rows; row++)
        {
            var source = row * stride;
            var filter = data[source];
            Array.Copy(data, source + 1, current, 0, rowLength);

            switch (filter)
            {
                case 0:
                    break;

                case 1:
                    for (var i = bytesPerPixel; i < rowLength; i++)
                    {
                        current[i] += current[i - bytesPerPixel];
                    }

                    break;

                case 2:
                    for (var i = 0; i < rowLength; i++)
                    {
                        current[i] += previous[i];
                    }

                    break;

                case 3:
                    for (var i = 0; i < rowLength; i++)
                    {
                        var left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                        current[i] += (byte)((left + previous[i]) / 2);
                    }

                    break;

                case 4:
                    for (var i = 0; i < rowLength; i++)
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

            Array.Copy(current, 0, output, row * rowLength, rowLength);
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
