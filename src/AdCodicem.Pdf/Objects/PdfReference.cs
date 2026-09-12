namespace AdCodicem.Pdf.Objects;

/// <summary>Represents an indirect reference such as <c>12 0 R</c>.</summary>
public sealed class PdfReference : PdfObject
{
    /// <summary>
    /// Longest chain of references followed before giving up. A conforming file never chains references
    /// at all; a hostile one might chain them forever.
    /// </summary>
    private const int MaxChainLength = 32;

    /// <summary>Initialises a reference to <paramref name="id"/> resolved through <paramref name="source"/>.</summary>
    public PdfReference(PdfObjectId id, IPdfObjectSource? source = null)
    {
        Id = id;
        Source = source;
    }

    /// <summary>Gets the identifier of the referenced object.</summary>
    public PdfObjectId Id { get; }

    /// <summary>Gets the source the reference resolves through, if any.</summary>
    public IPdfObjectSource? Source { get; }

    /// <inheritdoc/>
    public override PdfObject Resolve()
    {
        PdfObject current = this;

        for (var depth = 0; depth < MaxChainLength; depth++)
        {
            if (current is not PdfReference reference)
            {
                return current;
            }

            if (reference.Source is null)
            {
                return PdfNull.Instance;
            }

            current = reference.Source.GetObject(reference.Id);
        }

        return PdfNull.Instance;
    }

    /// <inheritdoc/>
    public override string ToString() => Id.ToString();
}
