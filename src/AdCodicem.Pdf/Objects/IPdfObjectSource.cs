namespace AdCodicem.Pdf.Objects;

/// <summary>
/// Supplies the objects an indirect reference points to.
/// </summary>
/// <remarks>
/// A reference never resolves through global state: it holds the source it came from, so objects from
/// several documents can coexist in one process, and in one object graph, without ambiguity.
/// </remarks>
public interface IPdfObjectSource
{
    /// <summary>
    /// Returns the object with the given identifier, or <see cref="PdfNull.Instance"/> when the file does
    /// not define it — which the specification requires be treated as null rather than as an error.
    /// </summary>
    PdfObject GetObject(PdfObjectId id);
}
