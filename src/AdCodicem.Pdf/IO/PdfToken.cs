namespace AdCodicem.Pdf.IO;

/// <summary>The kinds of token the PDF syntax produces.</summary>
internal enum PdfTokenKind : byte
{
    EndOfInput,
    Integer,
    Real,
    Name,
    LiteralString,
    HexString,
    ArrayStart,
    ArrayEnd,
    DictionaryStart,
    DictionaryEnd,
    BraceOpen,
    BraceClose,
    Keyword,

    /// <summary>A byte that has no business being where it was found. Skipped, with a diagnostic.</summary>
    Unknown,
}

/// <summary>
/// A token produced by <see cref="PdfLexer"/>. Payloads are slices of the input, so reading a document
/// costs nothing until an object is actually built from the tokens.
/// </summary>
internal readonly ref struct PdfToken
{
    public PdfToken(PdfTokenKind kind, int start, int end)
    {
        Kind = kind;
        Start = start;
        End = end;
    }

    public PdfToken(PdfTokenKind kind, int start, int end, ReadOnlySpan<byte> text)
        : this(kind, start, end)
    {
        Text = text;
    }

    public PdfToken(PdfTokenKind kind, int start, int end, long integer, double real)
        : this(kind, start, end)
    {
        Integer = integer;
        Real = real;
    }

    /// <summary>Gets the kind of token.</summary>
    public PdfTokenKind Kind { get; }

    /// <summary>Gets the offset of the first byte of the token within the buffer.</summary>
    public int Start { get; }

    /// <summary>Gets the offset just past the last byte of the token.</summary>
    public int End { get; }

    /// <summary>
    /// Gets the payload of the token: the name without its solidus, the keyword, or the raw contents of a
    /// string, still escaped exactly as they appear in the file.
    /// </summary>
    public ReadOnlySpan<byte> Text { get; }

    /// <summary>Gets the value of an integer token.</summary>
    public long Integer { get; }

    /// <summary>Gets the value of a real token.</summary>
    public double Real { get; }

    /// <summary>Determines whether this is the given keyword.</summary>
    public bool IsKeyword(ReadOnlySpan<byte> keyword) => Kind == PdfTokenKind.Keyword && Text.SequenceEqual(keyword);
}
