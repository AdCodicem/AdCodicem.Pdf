using System.Globalization;

namespace AdCodicem.Pdf.Objects;

/// <summary>
/// Identifies an indirect object inside a PDF file by its object number and generation number.
/// </summary>
/// <param name="Number">The object number. Object numbers start at 1; zero identifies the head of the free list.</param>
/// <param name="Generation">The generation number, almost always zero but never safe to ignore in a key.</param>
public readonly record struct PdfObjectId(int Number, int Generation = 0)
{
    /// <summary>The largest object number the reader accepts from a file.</summary>
    /// <remarks>
    /// An internal bound, not a guard (ADR 34): <see cref="Number"/> is an <see cref="int"/>, and no valid file numbers an
    /// object past it in practice — ISO 32000-1's Annex C advises at most 8,388,607 indirect objects. A number past it is
    /// refused where it is read, never narrowed to an <see cref="int"/> that would name another object.
    /// </remarks>
    internal const int MaxNumber = int.MaxValue;

    /// <summary>The largest generation an object in use may have: 65,535, under ISO 32000-1, 7.5.4.</summary>
    /// <remarks>
    /// An internal bound, not a guard (ADR 34): only an invalid file exceeds it. A row of a cross-reference table that
    /// frees an object is exempt where it is read: producers give the head of the free list 65,536, and no object is ever
    /// served under a free row's generation.
    /// </remarks>
    internal const int MaxGeneration = 65_535;

    /// <summary>Gets a value indicating whether this identifier designates no object.</summary>
    public bool IsEmpty => Number <= 0;

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Number} {Generation} R");
}
