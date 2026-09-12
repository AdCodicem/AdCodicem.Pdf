namespace AdCodicem.Pdf.Objects;

/// <summary>
/// Identifies an indirect object inside a PDF file by its object number and generation number.
/// </summary>
/// <param name="Number">The object number. Object numbers start at 1; zero identifies the head of the free list.</param>
/// <param name="Generation">The generation number, almost always zero but never safe to ignore in a key.</param>
public readonly record struct PdfObjectId(int Number, int Generation = 0)
{
    /// <summary>Gets a value indicating whether this identifier designates no object.</summary>
    public bool IsEmpty => Number <= 0;

    /// <inheritdoc/>
    public override string ToString() => $"{Number} {Generation} R";
}
