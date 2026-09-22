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
    /// <summary>Initialises a stream from its dictionary and its encoded data.</summary>
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

    /// <inheritdoc/>
    public override string ToString()
    {
        var subtype = Dictionary[PdfName.Subtype] as PdfName;
        var type = subtype ?? Dictionary[PdfName.Type] as PdfName;
        return type is null ? $"<<stream of {RawLength} bytes>>" : $"<<{type} stream of {RawLength} bytes>>";
    }
}
