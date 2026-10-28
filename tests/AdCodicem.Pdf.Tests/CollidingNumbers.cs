using System.Globalization;
using System.Text;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Files whose object numbers are chosen to fall in one bucket of a .NET hash table keyed by them, each beside the same
/// file numbered in order (#155).
/// </summary>
/// <remarks>
/// An <see cref="int"/> is its own hash, and .NET puts a key in the bucket its hash leaves modulo the table's size, a
/// prime from a list anyone can read: numbers a multiple of that prime apart share one bucket, and every lookup walks
/// them all. The size each file aims at is the one the table that file filled had before #155.
/// </remarks>
internal static class CollidingNumbers
{
    /// <summary>
    /// How many buckets the index had made room for once it had read the rows of <see cref="Table"/>, before #155.
    /// </summary>
    public const int TableSize = 62_851;

    /// <summary>
    /// Gets <paramref name="count"/> object numbers 3 more than a multiple of <paramref name="size"/>, as many as fit
    /// in an <see cref="int"/>; or, <paramref name="colliding"/> false, as many numbered in order from 4.
    /// </summary>
    public static int[] Numbers(int count, int size, bool colliding)
    {
        var numbers = new List<int>(count);

        for (var k = 1L; numbers.Count < count; k++)
        {
            var number = colliding ? 3 + (k * size) : 3 + k;

            if (number > int.MaxValue)
            {
                break;
            }

            numbers.Add((int)number);
        }

        return [.. numbers];
    }

    /// <summary>
    /// Gets how many buckets a .NET map keyed by numbers holds once it has grown, from empty, to
    /// <paramref name="count"/> entries.
    /// </summary>
    public static int GrownSize(int count)
    {
        var map = new Dictionary<int, int>();

        for (var i = 0; i < count; i++)
        {
            map.Add(i, i);
        }

        return map.EnsureCapacity(0);
    }

    /// <summary>
    /// Writes a classic table that indexes a catalog and an empty page tree, then each of <paramref name="numbers"/> in
    /// a subsection of its own, free.
    /// </summary>
    public static byte[] Table(int[] numbers)
    {
        var file = new StringBuilder("%PDF-1.7\n");
        var catalog = file.Length;
        file.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = file.Length;
        file.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        var xref = file.Length;
        file.Append(CultureInfo.InvariantCulture, $"xref\n0 3\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \n");

        foreach (var number in numbers)
        {
            file.Append(CultureInfo.InvariantCulture, $"{number} 1\n0000000000 00001 f \n");
        }

        file.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {numbers.Max() + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(file.ToString());
    }

    /// <summary>
    /// Writes a catalog, an empty page tree and an object null under each of <paramref name="numbers"/>, with no table
    /// to index them: the reader rebuilds the index from the objects it finds.
    /// </summary>
    public static byte[] Unindexed(int[] numbers)
    {
        var file = new StringBuilder("%PDF-1.7\n");
        file.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        file.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

        foreach (var number in numbers)
        {
            file.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\nnull\nendobj\n");
        }

        file.Append("trailer\n<< /Root 1 0 R >>\n%%EOF\n");
        return Encoding.ASCII.GetBytes(file.ToString());
    }

    /// <summary>
    /// Writes a catalog, an empty page tree, an integer under each of <paramref name="numbers"/>, and object 3, an array of
    /// <paramref name="references"/> references to them in turn; a classic table indexes each in a subsection of its own.
    /// </summary>
    public static byte[] Referenced(int[] numbers, int references)
    {
        var file = new StringBuilder("%PDF-1.7\n");
        var rows = new StringBuilder();
        var catalog = file.Length;
        file.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = file.Length;
        file.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        var array = file.Length;
        AppendArray(file, numbers, references);

        foreach (var number in numbers)
        {
            rows.Append(CultureInfo.InvariantCulture, $"{number} 1\n{file.Length:D10} 00000 n \n");
            file.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n1\nendobj\n");
        }

        var xref = file.Length;
        file.Append(CultureInfo.InvariantCulture, $"xref\n0 4\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \n{array:D10} 00000 n \n");
        file.Append(rows);
        file.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {numbers.Max() + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(file.ToString());
    }

    /// <summary>
    /// Writes a catalog whose <c>/Extra</c> reaches object 3, an array of <paramref name="references"/> references to each
    /// of <paramref name="numbers"/> in turn, and a classic table that marks each number free in a subsection of its own.
    /// </summary>
    public static byte[] ReachedFree(int[] numbers, int references)
    {
        var file = new StringBuilder("%PDF-1.7\n");
        var catalog = file.Length;
        file.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /Extra 3 0 R >>\nendobj\n");
        var pages = file.Length;
        file.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        var array = file.Length;
        AppendArray(file, numbers, references);
        var xref = file.Length;
        file.Append(CultureInfo.InvariantCulture, $"xref\n0 4\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \n{array:D10} 00000 n \n");

        foreach (var number in numbers)
        {
            file.Append(CultureInfo.InvariantCulture, $"{number} 1\n0000000000 00001 f \n");
        }

        file.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {numbers.Max() + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(file.ToString());
    }

    private static void AppendArray(StringBuilder file, int[] numbers, int references)
    {
        file.Append("3 0 obj\n[");

        for (var i = 0; i < references; i++)
        {
            file.Append(numbers[i % numbers.Length]).Append(" 0 R ");
        }

        file.Append("]\nendobj\n");
    }
}
