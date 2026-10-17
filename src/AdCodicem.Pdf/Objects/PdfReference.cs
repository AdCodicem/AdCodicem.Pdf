namespace AdCodicem.Pdf.Objects;

/// <summary>Represents an indirect reference such as <c>12 0 R</c>.</summary>
public sealed class PdfReference : PdfObject
{
    /// <summary>
    /// Longest chain of references followed before giving up. A conforming file never chains references
    /// at all; a hostile one might chain them forever.
    /// </summary>
    private const int MaxChainLength = 32;

    /// <summary>Initializes a reference to <paramref name="id"/> resolved through <paramref name="source"/>.</summary>
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

    /// <summary>
    /// Gives the object whose value <see cref="Resolve"/> gives: this reference's, or the last one a chain of objects whose
    /// values are references leads to, as far as <see cref="Resolve"/> follows it: a chain too long for it ends at its
    /// 31st object, the last whose value <see cref="Resolve"/> takes, so that no object past it is taken for the target.
    /// </summary>
    internal PdfObjectId Target()
    {
        var reference = this;

        // Resolve gives the value of a chain's 31st object at most: that object is the furthest a chain is followed to.
        for (var depth = 1; depth < MaxChainLength - 1; depth++)
        {
            if (reference.Source?.GetObject(reference.Id) is not PdfReference next)
            {
                break;
            }

            reference = next;
        }

        return reference.Id;
    }
}
