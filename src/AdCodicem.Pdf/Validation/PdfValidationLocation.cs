using System.Globalization;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation;

/// <summary>
/// Where a validation finding applies: an object of the document, a byte offset in the file, a page, or several of
/// these. The default value designates the document as a whole.
/// </summary>
/// <remarks>
/// A finding nobody can locate is not actionable, so every rule says where it looked. A finding about a page, or
/// about an object a page holds, gives the page too.
/// </remarks>
public readonly record struct PdfValidationLocation
{
    private PdfValidationLocation(PdfObjectId? objectId, long? position, int? pageIndex)
    {
        Object = objectId;
        Position = position;
        PageIndex = pageIndex;
    }

    /// <summary>Gets the object the finding concerns, or null when it concerns no single object.</summary>
    public PdfObjectId? Object { get; }

    /// <summary>
    /// Gets the byte offset in the file the finding relates to, or null when it relates to no position. An
    /// offset equal to the file's length designates its end.
    /// </summary>
    public long? Position { get; }

    /// <summary>
    /// Gets the zero-based index of the page the finding concerns, in the order of the page tree, or null when it
    /// concerns no single page.
    /// </summary>
    /// <remarks>
    /// Pages are counted as the page tree lists them: a kid that is null, or that names an object the file lacks,
    /// takes the place of a page. <see cref="ToString"/> writes the page as people number it, from 1.
    /// </remarks>
    public int? PageIndex { get; }

    /// <summary>Gets a value indicating whether the location designates the document as a whole.</summary>
    public bool IsDocument => Object is null && Position is null && PageIndex is null;

    /// <inheritdoc/>
    public override string ToString()
    {
        var where = (Object, Position) switch
        {
            ({ } id, { } position) => string.Create(CultureInfo.InvariantCulture, $"object {id.Number} {id.Generation}, at offset {position}"),
            ({ } id, null) => string.Create(CultureInfo.InvariantCulture, $"object {id.Number} {id.Generation}"),
            (null, { } position) => string.Create(CultureInfo.InvariantCulture, $"offset {position}"),
            _ => null,
        };

        // A location on a page always names the object there as well.
        return PageIndex is { } page
            ? string.Create(CultureInfo.InvariantCulture, $"page {page + 1}, {where}")
            : where ?? "the document";
    }

    /// <summary>A location at a byte offset of the file.</summary>
    internal static PdfValidationLocation AtPosition(long position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        return new PdfValidationLocation(null, position, null);
    }

    /// <summary>A location at an object, and at the offset it was read from when that is known.</summary>
    internal static PdfValidationLocation OfObject(PdfObjectId id, long? position = null)
    {
        if (position is { } offset)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset, nameof(position));
        }

        return new PdfValidationLocation(id, position, null);
    }

    /// <summary>A location at an object on a page — the page itself, or an object it holds.</summary>
    internal static PdfValidationLocation OnPage(int pageIndex, PdfObjectId id, long? position = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);

        if (position is { } offset)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset, nameof(position));
        }

        return new PdfValidationLocation(id, position, pageIndex);
    }
}
