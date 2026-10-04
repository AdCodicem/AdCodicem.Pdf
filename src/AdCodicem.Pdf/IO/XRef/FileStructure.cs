using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO.XRef;

/// <summary>
/// What the reader saw of a file's own structure while opening it: its header, what <c>startxref</c> gave, each
/// cross-reference section the chain named and what became of it, and the trailer's <c>/Root</c> and <c>/Size</c>
/// before the reader did anything about them.
/// </summary>
/// <remarks>
/// <para>
/// The validation rules judge the file as it was written, not as the reader repaired it, and this is where they
/// find it. It is filled while the document opens and never changes afterwards, so two validations of the same
/// document see the same structure, whatever was read in between.
/// </para>
/// <para>
/// It costs a record per section of the chain — at most <see cref="Documents.PdfReaderLimits.MaxXRefSectionCount"/>
/// of them, and one more for each <c>/XRefStm</c> — and nothing per object.
/// </para>
/// </remarks>
internal sealed class FileStructure
{
    private readonly List<XRefSectionRecord> _sections = [];

    /// <summary>Gets where <c>%PDF-</c> starts, or -1 when the reader did not find it where it looks.</summary>
    public long HeaderPosition { get; set; } = -1;

    /// <summary>
    /// Gets what follows <c>%PDF-</c>, its digits and dots as written — empty when nothing does —, or null when
    /// there is no header.
    /// </summary>
    public string? HeaderVersion { get; set; }

    /// <summary>Gets where the last <c>startxref</c> keyword of the file's tail starts, or -1 when there is none.</summary>
    public long StartXRefPosition { get; set; } = -1;

    /// <summary>Gets the offset <c>startxref</c> gives, as written, or -1 when it gives none.</summary>
    public long StartXRef { get; set; } = -1;

    /// <summary>Gets the sections the chain named, newest first, each <c>/XRefStm</c> after the table that named it.</summary>
    public IReadOnlyList<XRefSectionRecord> Sections => _sections;

    /// <summary>
    /// Gets a value indicating whether the chain gave an index — its first section read —, so that the index the
    /// document started from is the file's own.
    /// </summary>
    public bool ChainRead { get; set; }

    /// <summary>
    /// Gets where the chain named, through <see cref="LoopNamedBy"/>, a section it had already read; -1 when it
    /// never did.
    /// </summary>
    /// <remarks>
    /// A position, the header's offset added; one whose sum a long cannot hold reads as <see cref="long.MaxValue"/>, past
    /// the end of the file (#125). <see cref="LoopWrittenOffset"/> is the offset as written.
    /// </remarks>
    public long LoopOffset { get; set; } = -1;

    /// <summary>Gets the offset the chain looped back through, as the file writes it, counted from its header; -1 when it never did.</summary>
    public long LoopWrittenOffset { get; set; } = -1;

    /// <summary>
    /// Gets a value indicating whether the chain read a section at the offset it looped back through, rather than only
    /// naming it there; false when it never looped.
    /// </summary>
    public bool LoopOffsetRead { get; set; }

    /// <summary>Gets what named the section the chain looped back to.</summary>
    public string? LoopNamedBy { get; set; }

    /// <summary>Gets where what looped back is written: the section whose trailer names the section again.</summary>
    public long LoopNamedFrom { get; set; } = -1;

    /// <summary>
    /// Gets where the chain went on when <see cref="Documents.PdfReaderLimits.MaxXRefSectionCount"/> stopped it,
    /// or -1 when it was read to its end.
    /// </summary>
    /// <remarks>
    /// A position, the header's offset added; one whose sum a long cannot hold reads as <see cref="long.MaxValue"/>, past
    /// the end of the file (#125). <see cref="ChainCutWrittenOffset"/> is the offset as written.
    /// </remarks>
    public long ChainCutAt { get; set; } = -1;

    /// <summary>Gets the offset the chain went on at when it was cut, as the file writes it, counted from its header; -1 when it was not cut.</summary>
    public long ChainCutWrittenOffset { get; set; } = -1;

    /// <summary>Gets where the section whose <c>/Prev</c> names <see cref="ChainCutAt"/> starts; -1 when the chain was not cut.</summary>
    public long ChainCutNamedFrom { get; set; } = -1;

    /// <summary>Gets a value indicating whether the trailer the chain gave holds any entry at all.</summary>
    public bool TrailerRead { get; set; }

    /// <summary>Gets the trailer's <c>/Root</c> as the chain gave it, before the reader looked for a catalog of its own.</summary>
    public PdfObject? RootAsWritten { get; set; }

    /// <summary>Gets what <see cref="RootAsWritten"/> resolved to when the document opened, or null when it is no reference.</summary>
    public PdfObject? RootResolved { get; set; }

    /// <summary>Gets a value indicating whether <see cref="RootAsWritten"/> led to a catalog when the document opened.</summary>
    public bool RootUsable { get; set; } = true;

    /// <summary>Gets the object the reader found to be the catalog when <c>/Root</c> did not lead to one; 0 when it did not look, or found none.</summary>
    public int CatalogFoundAs { get; set; }

    /// <summary>Gets the trailer's <c>/Size</c> as the chain gave it.</summary>
    public PdfObject? SizeAsWritten { get; set; }

    /// <summary>Records a section the chain named.</summary>
    public void Add(XRefSectionRecord section) => _sections.Add(section);
}
