namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The files crafted while measuring #128 and #237, in which what the validator found depended on what was read before it:
/// a rebuild of the index set off at one point of a walk or another, a note kept of a definition a rebuild replaced, a value
/// read through the index the reader holds now rather than the chain's.
/// </summary>
internal static partial class ConvergenceShapes
{
    /// <summary>A catalog whose page tree is object 2.</summary>
    private const string Catalog = "<< /Type /Catalog /Pages 2 0 R >>";

    /// <summary>A page tree whose one page is object 3.</summary>
    private const string Pages = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";

    /// <summary>A page whose parent is object 2, with a media box and resources, which no rule finds fault with.</summary>
    private const string Page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>";

    private static readonly Lazy<Dictionary<string, ConvergenceShape>> Shapes = new(Collect);

    /// <summary>Gets every shape, by name.</summary>
    public static IReadOnlyDictionary<string, ConvergenceShape> All => Shapes.Value;

    private static Dictionary<string, ConvergenceShape> Collect()
    {
        var shapes = new Dictionary<string, ConvergenceShape>(StringComparer.Ordinal);

        foreach (var shape in (IEnumerable<ConvergenceShape>)[.. ObjectStreamShapes(), .. TrailerShapes(), .. RecordShapes(), .. LiveReadShapes()])
        {
            shapes.Add(shape.Name, shape);
        }

        return shapes;
    }
}
