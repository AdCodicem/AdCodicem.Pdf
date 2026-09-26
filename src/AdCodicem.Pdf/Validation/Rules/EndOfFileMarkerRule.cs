namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.FileEofMissing"/>: a file ends with an end-of-file marker, <c>%%EOF</c>.
/// </summary>
/// <remarks>
/// ISO 32000 wants the marker alone on the file's last line. Readers are more forgiving and look for it in the
/// last 1,024 bytes — the tolerance Adobe's PDF Reference documented for Acrobat —, so bytes a transfer added
/// after it do not stop them. Without it anywhere near the end, the file was most likely cut short, or had
/// something appended; the reader may still find every object, which is why this is a warning and not an
/// error. A second marker — each incremental update ends with one — is legal and says nothing.
/// </remarks>
internal sealed class EndOfFileMarkerRule : IValidationRule
{
    /// <summary>
    /// How far from the end the marker may sit. Not a reader guard (invariant 12): a file valid under ISO 32000
    /// ends with the marker, so it always lies within these bytes; they are the tolerance readers extend to
    /// what follows it.
    /// </summary>
    internal const int SearchLength = 1024;

    private static ReadOnlySpan<byte> Marker => "%%EOF"u8;

    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.FileEofMissing;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var source = context.Source;
        var length = source.Length;
        var searched = (int)Math.Min(SearchLength, length);

        using (var tail = source.GetWindow(length - searched, searched))
        {
            if (tail.Memory.Span.IndexOf(Marker) >= 0)
            {
                return;
            }
        }

        context.Report(
            this,
            PdfValidationLocation.AtPosition(length),
            $"The file does not end with an end-of-file marker: no %%EOF in its last {searched} bytes.",
            "Append %%EOF on a line of its own at the end of the file, after checking that nothing was cut off before it.");
    }
}
