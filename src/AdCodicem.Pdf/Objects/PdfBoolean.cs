namespace AdCodicem.Pdf.Objects;

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
