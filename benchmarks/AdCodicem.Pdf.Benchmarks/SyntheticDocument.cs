using System.Text;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>Builds a valid PDF of a chosen size, so the reader can be measured on something realistic.</summary>
internal static class SyntheticDocument
{
    public static byte[] Create(int pageCount, int contentBytes)
    {
        using var stream = new MemoryStream();
        var offsets = new Dictionary<int, long>();

        Write(stream, "%PDF-1.7\n");
        stream.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);

        var pageNumbers = new List<int>();
        var next = 3;
        var content = new string('A', contentBytes);

        for (var i = 0; i < pageCount; i++)
        {
            var pageNumber = next++;
            var contentNumber = next++;
            pageNumbers.Add(pageNumber);

            offsets[pageNumber] = stream.Position;
            WriteObject(
                stream,
                pageNumber,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {contentNumber} 0 R >>");

            offsets[contentNumber] = stream.Position;
            WriteObject(stream, contentNumber, $"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }

        offsets[1] = stream.Position;
        WriteObject(stream, 1, "<< /Type /Catalog /Pages 2 0 R >>");

        offsets[2] = stream.Position;
        var kids = string.Join(' ', pageNumbers.Select(number => $"{number} 0 R"));
        WriteObject(stream, 2, $"<< /Type /Pages /Count {pageCount} /Kids [{kids}] >>");

        var size = next;
        var xrefOffset = stream.Position;
        Write(stream, "xref\n");
        Write(stream, $"0 {size}\n");
        Write(stream, $"{0:D10} {65535:D5} f \r\n");

        for (var number = 1; number < size; number++)
        {
            var offset = offsets.GetValueOrDefault(number);
            Write(stream, $"{offset:D10} {0:D5} n \r\n");
        }

        Write(stream, "trailer\n");
        Write(stream, $"<< /Size {size} /Root 1 0 R >>\n");
        Write(stream, $"startxref\n{xrefOffset}\n%%EOF\n");

        return stream.ToArray();
    }

    private static void WriteObject(Stream stream, int number, string body)
    {
        Write(stream, $"{number} 0 obj\n");
        Write(stream, body);
        Write(stream, "\nendobj\n");
    }

    private static void Write(Stream stream, string text)
    {
        var bytes = Encoding.Latin1.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }
}
