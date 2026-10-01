using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO;

/// <summary>
/// What the reader found of a stream whose length the file did not confirm: how its <c>/Length</c> was written, where
/// its <c>endstream</c> was, and the length it took.
/// </summary>
/// <remarks>
/// A stream whose declared length an <c>endstream</c> follows has none: the reader records one only for the streams it
/// reported, as <see cref="Diagnostics.PdfDiagnosticCodes.StreamLengthInvalid"/> or
/// <see cref="Diagnostics.PdfDiagnosticCodes.StreamTruncated"/>.
/// </remarks>
internal readonly record struct StreamLengthFault
{
    /// <summary>Gets how the <c>/Length</c> was written, or what the object it names holds.</summary>
    public required StreamLengthForm Form { get; init; }

    /// <summary>Gets the object the <c>/Length</c> names, or null when it is written in the stream's dictionary.</summary>
    public PdfObjectId? Reference { get; init; }

    /// <summary>
    /// Gets the integer the <c>/Length</c> gives, in the dictionary or through <see cref="Reference"/>, in range or
    /// not; null when it gives none.
    /// </summary>
    public long? Declared { get; init; }

    /// <summary>
    /// Gets what kind of value the <c>/Length</c>, or the object it names, holds when that is not an integer:
    /// <c>a real number</c>, <c>a name</c>, <c>a dictionary</c>.
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>
    /// Gets the value the <c>/Length</c>, or the object it names, holds when it is a real number, a name or a boolean,
    /// as the file wrote it; null for any other. The value is kept, not copied into words: a name of millions of
    /// characters that many streams take for their length costs each of them a reference, which a message quotes
    /// only when it is written.
    /// </summary>
    public PdfObject? Value { get; init; }

    /// <summary>Gets the length the reader took for the data.</summary>
    public required int Taken { get; init; }

    /// <summary>Gets the length of the data up to the <c>endstream</c> the reader found, or null when it found none.</summary>
    public int? Found { get; init; }

    /// <summary>Gets where the <c>endstream</c> was found, or where it was looked for and was not.</summary>
    public required EndStreamState EndStream { get; init; }

    /// <summary>
    /// Gets the offset of the object the search for the <c>endstream</c> stopped at — where an index places it, or where
    /// its header starts in the file's bytes —, when <see cref="EndStream"/> is
    /// <see cref="EndStreamState.MissingBeforeNextObject"/>; null otherwise.
    /// </summary>
    public long? NextObject { get; init; }

    /// <summary>Gets the offset in the file where the stream's data starts, which the diagnostic gives as its position.</summary>
    public required long DataStart { get; init; }
}
