using System.Globalization;

namespace AdCodicem.Pdf.Objects;

/// <summary>
/// Represents a PDF stream: a dictionary describing a sequence of bytes, plus the bytes themselves.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The PDF specification calls this object a stream; renaming it would make every reader of the specification translate.")]
public sealed class PdfStream : PdfObject
{
    /// <summary>Initializes a stream from its dictionary and its encoded data.</summary>
    public PdfStream(PdfDictionary dictionary, PdfStreamData data)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(data);
        Dictionary = dictionary;
        Data = data;
    }

    /// <summary>Gets the stream dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Gets the encoded data of the stream.</summary>
    public PdfStreamData Data { get; }

    /// <summary>Gets the number of encoded bytes.</summary>
    public int RawLength => Data.Length;

    /// <summary>Returns the encoded bytes, exactly as they appear in the file.</summary>
    public ReadOnlyMemory<byte> GetRawBytes() => Data.GetBytes();

    /// <summary>
    /// Describes the stream for a debugger or a log by its <c>/Subtype</c>, or else its <c>/Type</c>, and the count of its
    /// encoded bytes, as in <c>&lt;&lt;/Image stream of 4096 bytes&gt;&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The name is quoted as <see cref="PdfName.ToString"/> quotes one: printable ASCII, bounded, whatever the file wrote.
    /// The data is neither read nor described.
    /// </remarks>
    public override string ToString()
    {
        var type = Dictionary[PdfName.Subtype] as PdfName ?? Dictionary[PdfName.Type] as PdfName;
        var bytes = RawLength == 1 ? "byte" : "bytes";
        return type is null
            ? string.Create(CultureInfo.InvariantCulture, $"<<stream of {RawLength} {bytes}>>")
            : string.Create(CultureInfo.InvariantCulture, $"<<{type} stream of {RawLength} {bytes}>>");
    }
}
