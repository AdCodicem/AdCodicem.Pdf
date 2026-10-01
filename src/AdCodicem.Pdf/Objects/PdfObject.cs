namespace AdCodicem.Pdf.Objects;

/// <summary>
/// Base type for every value that can appear in the object graph of a PDF file.
/// </summary>
/// <remarks>
/// Values are deliberately close to the file format: resolving indirect references, decoding streams and
/// interpreting semantics are the caller's business, so that a document can be manipulated without ever
/// materializing more of it than the operation needs.
/// </remarks>
public abstract class PdfObject
{
    private protected PdfObject()
    {
    }

    /// <summary>
    /// Follows indirect references until a direct object is reached. Direct objects return themselves.
    /// </summary>
    public virtual PdfObject Resolve() => this;
}
