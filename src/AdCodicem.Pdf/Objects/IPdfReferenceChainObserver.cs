namespace AdCodicem.Pdf.Objects;

/// <summary>
/// Is told of each chain of references a resolution cut: one that loops, or runs past the references a resolution follows
/// (<see cref="PdfReference.MaxChainLength"/>), and so reads as null. The source a document's references resolve through
/// reports it; one a caller supplies is not told.
/// </summary>
internal interface IPdfReferenceChainObserver
{
    /// <summary>Tells that the chain of references from <paramref name="start"/> was cut, and read as null.</summary>
    void ChainCut(PdfReference start);
}
