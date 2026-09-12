using System.Collections.Concurrent;

namespace AdCodicem.Pdf.Objects;

/// <summary>
/// Represents a PDF name such as <c>/Type</c>. Names are interned, so equality is usually a reference
/// comparison and dictionary lookups stay cheap.
/// </summary>
public sealed class PdfName : PdfObject, IEquatable<PdfName>
{
    private static readonly ConcurrentDictionary<string, PdfName> Interned = new(StringComparer.Ordinal);

    private PdfName(string value) => Value = value;

    /// <summary>Gets the name without its leading solidus.</summary>
    public string Value { get; }

    /// <summary>Returns the interned name for <paramref name="value"/>.</summary>
    public static PdfName Get(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Interned.GetOrAdd(value, static v => new PdfName(v));
    }

    /// <inheritdoc/>
    public bool Equals(PdfName? other) =>
        ReferenceEquals(this, other) || (other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal));

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as PdfName);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc/>
    public override string ToString() => "/" + Value;

    /// <summary>Compares two names for equality.</summary>
    public static bool operator ==(PdfName? left, PdfName? right) =>
        ReferenceEquals(left, right) || (left is not null && left.Equals(right));

    /// <summary>Compares two names for inequality.</summary>
    public static bool operator !=(PdfName? left, PdfName? right) => !(left == right);

#pragma warning disable CS1591 // Well-known names; their meaning is the PDF specification's.
    public static readonly PdfName Type = Get("Type");
    public static readonly PdfName Subtype = Get("Subtype");
    public static readonly PdfName Length = Get("Length");
    public static readonly PdfName Length1 = Get("Length1");
    public static readonly PdfName Filter = Get("Filter");
    public static readonly PdfName DecodeParms = Get("DecodeParms");
    public static readonly PdfName Root = Get("Root");
    public static readonly PdfName Info = Get("Info");
    public static readonly PdfName Encrypt = Get("Encrypt");
    public static readonly PdfName Size = Get("Size");
    public static readonly PdfName Prev = Get("Prev");
    public static readonly PdfName XRefStm = Get("XRefStm");
    public static readonly PdfName Index = Get("Index");
    public static readonly PdfName W = Get("W");
    public static readonly PdfName XRef = Get("XRef");
    public static readonly PdfName ObjStm = Get("ObjStm");
    public static readonly PdfName N = Get("N");
    public static readonly PdfName First = Get("First");
    public static readonly PdfName Extends = Get("Extends");
    public static readonly PdfName ID = Get("ID");
    public static readonly PdfName Catalog = Get("Catalog");
    public static readonly PdfName Pages = Get("Pages");
    public static readonly PdfName Page = Get("Page");
    public static readonly PdfName Kids = Get("Kids");
    public static readonly PdfName Count = Get("Count");
    public static readonly PdfName Parent = Get("Parent");
    public static readonly PdfName MediaBox = Get("MediaBox");
    public static readonly PdfName CropBox = Get("CropBox");
    public static readonly PdfName Resources = Get("Resources");
    public static readonly PdfName Contents = Get("Contents");
    public static readonly PdfName Rotate = Get("Rotate");
    public static readonly PdfName Annots = Get("Annots");
    public static readonly PdfName Font = Get("Font");
    public static readonly PdfName XObject = Get("XObject");
    public static readonly PdfName Version = Get("Version");
    public static readonly PdfName Metadata = Get("Metadata");
    public static readonly PdfName StructTreeRoot = Get("StructTreeRoot");
    public static readonly PdfName MarkInfo = Get("MarkInfo");
    public static readonly PdfName Lang = Get("Lang");
    public static readonly PdfName OutputIntents = Get("OutputIntents");
    public static readonly PdfName Names = Get("Names");
    public static readonly PdfName Outlines = Get("Outlines");
    public static readonly PdfName Dests = Get("Dests");
    public static readonly PdfName FlateDecode = Get("FlateDecode");
    public static readonly PdfName LZWDecode = Get("LZWDecode");
    public static readonly PdfName ASCII85Decode = Get("ASCII85Decode");
    public static readonly PdfName ASCIIHexDecode = Get("ASCIIHexDecode");
    public static readonly PdfName RunLengthDecode = Get("RunLengthDecode");
    public static readonly PdfName DCTDecode = Get("DCTDecode");
    public static readonly PdfName JPXDecode = Get("JPXDecode");
    public static readonly PdfName JBIG2Decode = Get("JBIG2Decode");
    public static readonly PdfName CCITTFaxDecode = Get("CCITTFaxDecode");
    public static readonly PdfName Crypt = Get("Crypt");
    public static readonly PdfName Predictor = Get("Predictor");
    public static readonly PdfName Colors = Get("Colors");
    public static readonly PdfName BitsPerComponent = Get("BitsPerComponent");
    public static readonly PdfName Columns = Get("Columns");
    public static readonly PdfName EarlyChange = Get("EarlyChange");
#pragma warning restore CS1591
}
