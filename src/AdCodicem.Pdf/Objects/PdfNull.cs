namespace AdCodicem.Pdf.Objects;

/// <summary>Represents the PDF <c>null</c> object, and the value of any reference that cannot be resolved.</summary>
public sealed class PdfNull : PdfObject
{
    /// <summary>The single instance of <see cref="PdfNull"/>.</summary>
    public static readonly PdfNull Instance = new();

    private PdfNull()
    {
    }

    /// <inheritdoc/>
    public override string ToString() => "null";
}
