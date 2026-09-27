namespace AdCodicem.Pdf.Validation;

/// <summary>
/// The identifiers of the validation rules. They are part of the public contract: callers filter on them and
/// repair keys its remedies off them, so renaming one is a breaking change once a stable release carries it.
/// </summary>
/// <remarks>
/// An identifier is <c>family.name</c>, both in lowercase kebab case. The family is never a profile's name,
/// since a rule runs in every profile that includes it. One identifier names one rule, which always reports at
/// the same severity, and no identifier equals one of <see cref="Diagnostics.PdfDiagnosticCodes"/>: findings
/// and diagnostics are different things. <c>docs/validation-rules.md</c> lists every identifier.
/// </remarks>
public static class PdfValidationRuleIds
{
    /// <summary>
    /// No <c>%PDF-</c> header in the first 1,024 bytes of the file, where readers look for it: some refuse the
    /// file. <see cref="PdfValidationSeverity.Warning"/>.
    /// </summary>
    public const string FileHeaderMissing = "file.header-missing";

    /// <summary>
    /// The <c>%PDF-</c> header does not start at the file's first byte: something precedes it.
    /// <see cref="PdfValidationSeverity.Warning"/>.
    /// </summary>
    public const string FileHeaderOffset = "file.header-offset";

    /// <summary>
    /// The header names no version of PDF — 1.0 to 1.7, or 2.0. <see cref="PdfValidationSeverity.Warning"/>.
    /// </summary>
    public const string FileHeaderVersionInvalid = "file.header-version-invalid";

    /// <summary>
    /// No <c>%%EOF</c> marker in the last 1,024 bytes of the file: the file was cut short, or something was
    /// appended after its end. <see cref="PdfValidationSeverity.Warning"/>.
    /// </summary>
    public const string FileEofMissing = "file.eof-missing";

    /// <summary>
    /// No <c>startxref</c> followed by an offset at the end of the file: the file gives no way into its
    /// cross-reference index, which readers rebuild each in their own way. <see cref="PdfValidationSeverity.Error"/>.
    /// </summary>
    public const string FileStartXRefMissing = "file.startxref-missing";

    /// <summary>
    /// The offset <c>startxref</c> gives holds no cross-reference section. <see cref="PdfValidationSeverity.Error"/>.
    /// </summary>
    public const string FileStartXRefWrong = "file.startxref-wrong";

    /// <summary>
    /// A cross-reference table is not followed by its trailer: the <c>trailer</c> keyword is missing.
    /// <see cref="PdfValidationSeverity.Error"/>.
    /// </summary>
    public const string FileTrailerMissing = "file.trailer-missing";

    /// <summary>
    /// A trailer is not a well-formed dictionary. <see cref="PdfValidationSeverity.Error"/>.
    /// </summary>
    public const string FileTrailerMalformed = "file.trailer-malformed";

    /// <summary>
    /// The trailer's <c>/Root</c> is missing, is not a reference, or does not lead to a document catalog.
    /// <see cref="PdfValidationSeverity.Error"/>.
    /// </summary>
    public const string FileRootInvalid = "file.root-invalid";

    /// <summary>
    /// A section's <c>/Size</c> is missing, or is not one more than the highest object number the section and
    /// those it updates use. <see cref="PdfValidationSeverity.Warning"/>.
    /// </summary>
    public const string FileSizeWrong = "file.size-wrong";

    /// <summary>
    /// A cross-reference section the chain names is there and cannot be read, or holds fewer rows than it
    /// declares. <see cref="PdfValidationSeverity.Error"/>.
    /// </summary>
    public const string XRefSectionMalformed = "xref.section-malformed";

    /// <summary>
    /// A cross-reference section a <c>/Prev</c> or an <c>/XRefStm</c> names is neither there nor near it, or what
    /// names it is not an offset. <see cref="PdfValidationSeverity.Error"/>.
    /// </summary>
    public const string XRefSectionNotFound = "xref.section-not-found";

    /// <summary>
    /// A cross-reference section a <c>/Prev</c> or an <c>/XRefStm</c> names starts a few bytes from where it is
    /// named. <see cref="PdfValidationSeverity.Warning"/>.
    /// </summary>
    public const string XRefSectionShifted = "xref.section-shifted";

    /// <summary>
    /// The chain of cross-reference sections names a section it has already read, and the sections before the loop
    /// are out of reach. <see cref="PdfValidationSeverity.Error"/>.
    /// </summary>
    public const string XRefChainLoop = "xref.chain-loop";

    /// <summary>
    /// An in-use entry places its object where it is not, nor near it; or in an object stream that does not hold
    /// it. <see cref="PdfValidationSeverity.Error"/>.
    /// </summary>
    public const string XRefEntryBroken = "xref.entry-broken";

    /// <summary>
    /// An in-use entry places its object a few bytes from where it is, or at another index of its object stream.
    /// <see cref="PdfValidationSeverity.Warning"/>.
    /// </summary>
    public const string XRefEntryShifted = "xref.entry-shifted";

    /// <summary>
    /// An entry's generation is not the one its object is written with: readers that match generations read the
    /// object as missing. <see cref="PdfValidationSeverity.Warning"/>.
    /// </summary>
    public const string XRefGenerationMismatch = "xref.generation-mismatch";

    /// <summary>
    /// An object stream entries place objects in is missing, is not an object stream, or cannot be read.
    /// <see cref="PdfValidationSeverity.Error"/>.
    /// </summary>
    public const string XRefObjectStreamBroken = "xref.object-stream-broken";

    /// <summary>
    /// Offsets of the file name the white space before what they designate rather than its first byte — reported
    /// once for the file. <see cref="PdfValidationSeverity.Warning"/>.
    /// </summary>
    public const string XRefOffsetImprecise = "xref.offset-imprecise";

    /// <summary>
    /// In-use objects are numbered above the trailer's <c>/Size</c>, which the specification makes a conforming
    /// reader ignore — reported once for the file. <see cref="PdfValidationSeverity.Warning"/>.
    /// </summary>
    public const string XRefObjectPastSize = "xref.object-past-size";

    /// <summary>
    /// Part of the index could not be checked: one of the reader's limits stopped it, or the document is
    /// encrypted and its object streams are readable only once decrypted. <see cref="PdfValidationSeverity.Information"/>.
    /// </summary>
    public const string XRefCheckedInPart = "xref.checked-in-part";
}
