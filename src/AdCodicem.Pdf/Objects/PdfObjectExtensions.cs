namespace AdCodicem.Pdf.Objects;

/// <summary>
/// Typed access to the object graph.
/// </summary>
/// <remarks>
/// Every accessor here resolves indirect references on the way. Reading a dictionary entry without
/// resolving it is the single most common defect in code that manipulates PDF, because it works on the
/// files you tested with and fails on the ones you did not.
/// </remarks>
public static class PdfObjectExtensions
{
    /// <summary>Resolves an object, tolerating <see langword="null"/>.</summary>
    public static PdfObject? Resolved(this PdfObject? value) => value?.Resolve();

    /// <summary>
    /// Returns the value as a dictionary, resolving references. A stream yields its own dictionary,
    /// because a stream is a dictionary that happens to carry data.
    /// </summary>
    public static PdfDictionary? AsDictionary(this PdfObject? value) => value?.Resolve() switch
    {
        PdfDictionary dictionary => dictionary,
        PdfStream stream => stream.Dictionary,
        _ => null,
    };

    /// <summary>Returns the value as an array, resolving references.</summary>
    public static PdfArray? AsArray(this PdfObject? value) => value?.Resolve() as PdfArray;

    /// <summary>Returns the value as a stream, resolving references.</summary>
    public static PdfStream? AsStream(this PdfObject? value) => value?.Resolve() as PdfStream;

    /// <summary>Returns the value as a name, resolving references.</summary>
    public static PdfName? AsName(this PdfObject? value) => value?.Resolve() as PdfName;

    /// <summary>Returns the value as a string object, resolving references.</summary>
    public static PdfString? AsString(this PdfObject? value) => value?.Resolve() as PdfString;

    /// <summary>Returns the value as an integer, resolving references. Reals with no fractional part qualify.</summary>
    public static long? AsInteger(this PdfObject? value) => value?.Resolve() switch
    {
        PdfInteger integer => integer.Value,
        PdfReal real when double.IsInteger(real.Value) && real.Value is >= long.MinValue and <= long.MaxValue =>
            (long)real.Value,
        _ => null,
    };

    /// <summary>Returns the value as a number, resolving references.</summary>
    public static double? AsNumber(this PdfObject? value) => value?.Resolve() switch
    {
        PdfInteger integer => integer.Value,
        PdfReal real => real.Value,
        _ => null,
    };

    /// <summary>Returns the value as a boolean, resolving references.</summary>
    public static bool? AsBoolean(this PdfObject? value) => (value?.Resolve() as PdfBoolean)?.Value;

    /// <summary>Returns the value as text, resolving references.</summary>
    public static string? AsText(this PdfObject? value) => (value?.Resolve() as PdfString)?.ToText();

    /// <summary>Gets a dictionary entry, resolving references.</summary>
    public static PdfObject? Get(this PdfDictionary? dictionary, PdfName key) =>
        dictionary is not null && dictionary.TryGetValue(key, out var value) ? value.Resolve() : null;

    /// <summary>Gets a dictionary entry without resolving references, which the writer needs to preserve sharing.</summary>
    public static PdfObject? GetRaw(this PdfDictionary? dictionary, PdfName key) =>
        dictionary is not null && dictionary.TryGetValue(key, out var value) ? value : null;

    /// <summary>Gets a dictionary entry as a dictionary.</summary>
    public static PdfDictionary? GetDictionary(this PdfDictionary? dictionary, PdfName key) =>
        dictionary.GetRaw(key).AsDictionary();

    /// <summary>Gets a dictionary entry as an array.</summary>
    public static PdfArray? GetArray(this PdfDictionary? dictionary, PdfName key) => dictionary.GetRaw(key).AsArray();

    /// <summary>Gets a dictionary entry as a stream.</summary>
    public static PdfStream? GetStream(this PdfDictionary? dictionary, PdfName key) => dictionary.GetRaw(key).AsStream();

    /// <summary>Gets a dictionary entry as a name.</summary>
    public static PdfName? GetName(this PdfDictionary? dictionary, PdfName key) => dictionary.GetRaw(key).AsName();

    /// <summary>Gets a dictionary entry as text.</summary>
    public static string? GetText(this PdfDictionary? dictionary, PdfName key) => dictionary.GetRaw(key).AsText();

    /// <summary>Gets a dictionary entry as an integer.</summary>
    public static long? GetInteger(this PdfDictionary? dictionary, PdfName key) => dictionary.GetRaw(key).AsInteger();

    /// <summary>Gets a dictionary entry as an integer, or <paramref name="defaultValue"/> when absent or ill-typed.</summary>
    public static long GetInteger(this PdfDictionary? dictionary, PdfName key, long defaultValue) =>
        dictionary.GetRaw(key).AsInteger() ?? defaultValue;

    /// <summary>Gets a dictionary entry as a number.</summary>
    public static double? GetNumber(this PdfDictionary? dictionary, PdfName key) => dictionary.GetRaw(key).AsNumber();

    /// <summary>Gets a dictionary entry as a number, or <paramref name="defaultValue"/> when absent or ill-typed.</summary>
    public static double GetNumber(this PdfDictionary? dictionary, PdfName key, double defaultValue) =>
        dictionary.GetRaw(key).AsNumber() ?? defaultValue;

    /// <summary>Gets a dictionary entry as a boolean, or <paramref name="defaultValue"/> when absent or ill-typed.</summary>
    public static bool GetBoolean(this PdfDictionary? dictionary, PdfName key, bool defaultValue) =>
        dictionary.GetRaw(key).AsBoolean() ?? defaultValue;

    /// <summary>Determines whether the dictionary declares the given <c>/Type</c>.</summary>
    public static bool IsOfType(this PdfDictionary? dictionary, PdfName type) => dictionary.GetName(PdfName.Type) == type;
}
