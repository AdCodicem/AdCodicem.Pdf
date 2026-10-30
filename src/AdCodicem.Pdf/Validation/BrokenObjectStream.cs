using System.Globalization;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation;

/// <summary>
/// An object stream <see cref="CrossReferenceProbe"/> found unable to serve the objects the index places in it, kept as a
/// few numbers and the reader's fault rather than as a finding written: its rule passes over the streams the dependency
/// walk finds circular, which the probe cannot know, and writes the finding of each other one only while the report has
/// room for it (#168).
/// </summary>
/// <param name="Stream">The number of the object stream.</param>
/// <param name="Kind">What keeps it from serving its objects.</param>
/// <param name="Objects">How many objects the index places in it.</param>
/// <param name="Position">Where the index places it in the file, or -1 when it places it nowhere in the file.</param>
/// <param name="Detail">
/// The object stream the index places it in, for <see cref="BrokenObjectStreamKind.InAnotherStream"/>; how many of its
/// objects are lost, for <see cref="BrokenObjectStreamKind.ReadInPart"/>.
/// </param>
/// <param name="Fault">
/// What the reader found wrong as it read the stream's header, for the kinds it read the header for:
/// <see cref="BrokenObjectStreamKind.NotAnObjectStream"/>, <see cref="BrokenObjectStreamKind.Unreadable"/> and
/// <see cref="BrokenObjectStreamKind.ReadInPart"/>.
/// </param>
internal readonly record struct BrokenObjectStream(
    int Stream,
    BrokenObjectStreamKind Kind,
    int Objects,
    long Position = -1,
    int Detail = 0,
    string? Fault = null)
{
    /// <summary>Gets where the finding applies: at the object stream, where the index places it when that is in the file.</summary>
    public PdfValidationLocation Location =>
        Position >= 0
            ? PdfValidationLocation.OfObject(new PdfObjectId(Stream), Position)
            : PdfValidationLocation.OfObject(new PdfObjectId(Stream));

    /// <summary>Writes what keeps the stream from serving its objects, as its finding says it.</summary>
    public string Describe() => Kind switch
    {
        BrokenObjectStreamKind.NotHeld =>
            Invariant($"The index places {RuleText.Objects(Objects)} in object stream {Stream}, which it does not hold as an object."),
        BrokenObjectStreamKind.InAnotherStream =>
            Invariant($"The index places {RuleText.Objects(Objects)} in object stream {Stream}, which it places in object stream {Detail} in turn: an object stream cannot be stored in another."),
        BrokenObjectStreamKind.NotAnObjectStream =>
            Invariant($"Object {Stream}, where the index places {RuleText.Objects(Objects)}, {Fault}."),
        BrokenObjectStreamKind.Unreadable =>
            Invariant($"Object stream {Stream}, where the index places {RuleText.Objects(Objects)}, cannot be read: {Fault}."),
        _ when Objects == 1 =>
            Invariant($"Object stream {Stream}, where the index places 1 object, can be read only in part: {Fault}; it is not listed before that, and lost."),
        _ =>
            Invariant($"Object stream {Stream}, where the index places {RuleText.Objects(Objects)}, can be read only in part: {Fault}; {Detail} of them {(Detail == 1 ? "is" : "are")} not listed before that, and lost."),
    };

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
