using System.Buffers;

namespace AdCodicem.Pdf.IO.Filters;

/// <summary>Decodes the <c>RunLengthDecode</c> filter.</summary>
internal static class RunLengthFilter
{
    public static byte[] Decode(ReadOnlySpan<byte> data)
    {
        var output = new ArrayBufferWriter<byte>(Math.Max(16, data.Length * 2));
        var index = 0;

        while (index < data.Length)
        {
            var control = data[index++];

            if (control == 128)
            {
                break;
            }

            if (control < 128)
            {
                var count = control + 1;
                var available = Math.Min(count, data.Length - index);
                output.Write(data.Slice(index, available));
                index += available;
                continue;
            }

            if (index >= data.Length)
            {
                break;
            }

            var repeat = 257 - control;
            var value = data[index++];
            var span = output.GetSpan(repeat)[..repeat];
            span.Fill(value);
            output.Advance(repeat);
        }

        return output.WrittenSpan.ToArray();
    }
}
