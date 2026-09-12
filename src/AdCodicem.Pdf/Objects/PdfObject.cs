namespace AdCodicem.Pdf.Objects;

/// <summary>
/// Base type for every value that can appear in the object graph of a PDF file.
/// </summary>
/// <remarks>
/// Values are deliberately close to the file format: resolving indirect references, decoding streams and
/// interpreting semantics are the caller's business, so that a document can be manipulated without ever
/// materialising more of it than the operation needs.
/// </remarks>
public abstract class PdfObject
{
    private protected PdfObject()
    {
    }

    /// <summary>
    /// Follows indirect references until a direct object is reached. Direct objects return themselves.
    /// </summary>
    public virtual PdfObject Resolve() => this;
}

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

/// <summary>Represents a PDF boolean.</summary>
public sealed class PdfBoolean : PdfObject
{
    /// <summary>The boolean <c>true</c>.</summary>
    public static readonly PdfBoolean True = new(true);

    /// <summary>The boolean <c>false</c>.</summary>
    public static readonly PdfBoolean False = new(false);

    private PdfBoolean(bool value) => Value = value;

    /// <summary>Gets the boolean value.</summary>
    public bool Value { get; }

    /// <summary>Returns the shared instance for <paramref name="value"/>.</summary>
    public static PdfBoolean Get(bool value) => value ? True : False;

    /// <inheritdoc/>
    public override string ToString() => Value ? "true" : "false";
}
