namespace AdCodicem.Pdf.Objects;

/// <summary>Represents an indirect reference such as <c>12 0 R</c>.</summary>
public sealed class PdfReference : PdfObject
{
    /// <summary>
    /// How many references a resolution follows, the value of the last object it reads examined: past them, a chain of
    /// objects whose values are references reads as null, as one that loops does (<see cref="Resolve"/>).
    /// </summary>
    /// <remarks>
    /// No guard (ADR 34): no valid file reaches it. An indirect object's value is one of the eight basic types of object
    /// (ISO 32000-1, 7.3.1 and 7.3.10), which a reference is not: a valid file never chains references at all; a hostile
    /// one might chain them forever.
    /// </remarks>
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
    /// <remarks>
    /// A chain of objects whose values are references is followed through 32 references, the value of the last object read
    /// taken; one that loops, or is longer, reads as null, and the document it was read from reports it under
    /// <see cref="Diagnostics.PdfDiagnosticCodes.ObjectReferenceChainCut"/>.
    /// </remarks>
    public override PdfObject Resolve() => ResolveThrough(MaxChainLength, this);

    /// <summary>
    /// Resolves the reference through <paramref name="links"/> references at most, this one the first, the value of the last
    /// object read examined: null past them, the chain cut, which the source is told of as the chain from
    /// <paramref name="start"/> — this reference, or the one a value read before it was found through, which the links it
    /// was followed through count, as a stream's <c>/Length</c> is resolved —; no object past the last whose value is taken
    /// is read.
    /// </summary>
    internal PdfObject ResolveThrough(int links, PdfReference start)
    {
        PdfObject current = this;

        for (var link = 0; link < links; link++)
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

        if (current is not PdfReference)
        {
            return current;
        }

        (start.Source as IPdfReferenceChainObserver)?.ChainCut(start);
        return PdfNull.Instance;
    }

    /// <inheritdoc/>
    public override string ToString() => Id.ToString();

    /// <summary>
    /// Determines whether <see cref="Resolve"/> cuts the chain this reference starts: the object <see cref="Target"/> gives
    /// holds a reference still, the chain looping or too long. It loads the objects resolving the reference loads.
    /// </summary>
    internal bool ReachesNoValue() => Source?.GetObject(Target()) is PdfReference;

    /// <summary>
    /// Gives the object whose value <see cref="Resolve"/> gives: this reference's, or the last one a chain of objects whose
    /// values are references leads to, as far as <see cref="Resolve"/> follows it: a chain too long for it, or that loops,
    /// ends at its 32nd object, the last whose value <see cref="Resolve"/> examines, which holds a reference still. It loads
    /// the objects before that one, which resolving the reference loads too.
    /// </summary>
    /// <param name="objects">How many objects the chain is followed through: fewer than a resolution, for one who asks less.</param>
    internal PdfObjectId Target(int objects = MaxChainLength)
    {
        var reference = this;

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
