namespace AdCodicem.Pdf.Objects;

/// <summary>Represents an indirect reference such as <c>12 0 R</c>.</summary>
public sealed class PdfReference : PdfObject
{
    /// <summary>
    /// Longest chain of references followed before giving up. A conforming file never chains references
    /// at all; a hostile one might chain them forever.
    /// </summary>
    internal const int MaxChainLength = 32;

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
    /// It loads the objects before that one, which resolving the reference loads too.
    /// </summary>
    /// <param name="objects">
    /// How many objects the chain is followed through: one more for a value read from the object a reference names and then
    /// resolved — a stream's <c>/Length</c>, a trailer's <c>/Prev</c> —, whose 32nd object gives it.
    /// </param>
    internal PdfObjectId Target(int objects = MaxChainLength - 1)
    {
        var reference = this;

        // Resolve gives the value of a chain's 31st object at most: it loads a 32nd, whose value it drops (#173), and the 31st
        // is the furthest whose value it takes.
        for (var depth = 1; depth < objects; depth++)
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
