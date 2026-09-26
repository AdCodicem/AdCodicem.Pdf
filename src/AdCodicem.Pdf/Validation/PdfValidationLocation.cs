using System.Globalization;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation;

/// <summary>
/// Where a validation finding applies: an object of the document, a byte offset in the file, or both. The
/// default value designates the document as a whole.
/// </summary>
/// <remarks>
/// A finding nobody can locate is not actionable, so every rule says where it looked. A page, when a finding
/// concerns one, joins the location with the first rule that reports on pages.
/// </remarks>
public readonly record struct PdfValidationLocation
{
    private PdfValidationLocation(PdfObjectId? objectId, long? position)
    {
        Object = objectId;
        Position = position;
    }

    /// <summary>Gets the object the finding concerns, or null when it concerns no single object.</summary>
    public PdfObjectId? Object { get; }

    /// <summary>
    /// Gets the byte offset in the file the finding relates to, or null when it relates to no position. An
    /// offset equal to the file's length designates its end.
    /// </summary>
    public long? Position { get; }

    /// <summary>Gets a value indicating whether the location designates the document as a whole.</summary>
    public bool IsDocument => Object is null && Position is null;

    /// <inheritdoc/>
    public override string ToString() => (Object, Position) switch
    {
        ({ } id, { } position) => string.Create(CultureInfo.InvariantCulture, $"object {id.Number} {id.Generation}, at offset {position}"),
        ({ } id, null) => string.Create(CultureInfo.InvariantCulture, $"object {id.Number} {id.Generation}"),
        (null, { } position) => string.Create(CultureInfo.InvariantCulture, $"offset {position}"),
        _ => "the document",
    };

    /// <summary>A location at a byte offset of the file.</summary>
    internal static PdfValidationLocation AtPosition(long position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        return new PdfValidationLocation(null, position);
    }

    /// <summary>A location at an object, and at the offset it was read from when that is known.</summary>
    internal static PdfValidationLocation OfObject(PdfObjectId id, long? position = null)
    {
        if (position is { } offset)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset, nameof(position));
        }

        return new PdfValidationLocation(id, position);
    }
}
