using System.Globalization;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.FileSizeWrong"/>: each section's <c>/Size</c> is one more than the highest object
/// number the section and those it updates use.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-1 requires <c>/Size</c> in every trailer (Table 15) and in every cross-reference stream (Table 17),
/// where it "shall be equivalent to the Size entry in a trailer dictionary"; an update's counts the objects of the
/// sections before it (7.5.6), and a hybrid file's trailer those of the stream its <c>/XRefStm</c> names (7.5.8.4); a
/// hybrid file's stream meets one of Table 17's two readings or the other (see <see cref="NamingTableSize"/>).
/// Readers use the newest trailer's, and only to size what they read: a wrong one is a warning.
/// </para>
/// <para>
/// A hybrid file's stream that more than one table names counts for each, and its own <c>/Size</c> is judged where the
/// oldest of them names it, the revision that wrote it (#156).
/// </para>
/// <para>
/// A <c>/Size</c> that leaves in-use objects numbered above it is
/// <see cref="PdfValidationRuleIds.XRefObjectPastSize"/>'s instead, which says the specification makes a conforming
/// reader ignore those objects. The rule needs the whole chain to count the objects each section updates: a chain
/// that lost a section — a hybrid file's stream among them, where its table's <c>/XRefStm</c> names none (#156) —, or
/// read one only in part — the rows it lost, or the older sections a trailer the end of the file cut would name, may
/// number the objects the <c>/Size</c> counts (#207, #210) —, that ends in a loop — which leaves the section the last
/// <c>/Prev</c> should have named unknown, its objects among those a <c>/Size</c> may count (#156) —, or that one of the
/// reader's limits cut, is not judged.
/// </para>
/// </remarks>
internal sealed class SizeWrongRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.FileSizeWrong;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var structure = context.Structure;
        var sections = structure.Sections;

        if (!structure.ChainRead || structure.ChainCutAt >= 0 || structure.LoopOffset >= 0 || !IsWhole(sections))
        {
            return;
        }

        // The highest number each section and the older ones use: the chain runs from the newest section to the
        // oldest, each /XRefStm after the table that names it. A cross-reference stream is an object of the
        // revision it closes, counted even when it gives itself no entry; a hybrid file's /XRefStm stream is not —
        // the newer table that names it indexes it. A hybrid file's stream that more than one table names — a writer that
        // copies /XRefStm into the next trailer — is counted for each, as part of each revision whose table names it, and
        // its own /Size is judged once, where the oldest of them names it: the revision that wrote it, before which no
        // table could name it (#156).
        var highest = new int[sections.Count];
        var running = -1;

        // Where each hybrid file's stream is judged: the first of its namings met from the oldest section on, made only for
        // a chain that holds such a stream.
        Dictionary<XRefSectionRecord, int>? judgedAt = null;

        for (var index = sections.Count - 1; index >= 0; index--)
        {
            var section = Counted(sections[index]);
            var own = section is { Kind: XRefSectionKind.Stream, IsHybridStream: false } ? section.StreamObjectNumber : -1;
            running = Math.Max(running, Math.Max(section?.HighestNumber ?? -1, own));
            highest[index] = running;

            if (section is { IsHybridStream: true })
            {
                judgedAt ??= new(ReferenceEqualityComparer.Instance);
                judgedAt.TryAdd(section, index);
            }
        }

        var pastSize = context.Probe.PastSizeCount > 0 && structure.SizeAsWritten is PdfInteger merged ? merged.Value : -1;

        for (var index = 0; index < sections.Count; index++)
        {
            var section = Counted(sections[index]);

            if (section is not { Trailer: { } trailer } || highest[index] < 0 || (section.IsHybridStream && judgedAt![section] != index))
            {
                continue;
            }

            var expected = highest[index] + 1L;
            var size = trailer.GetRaw(PdfName.Size);

            // A count that is right, or Table 17's other reading of it — the /Size of the table that names the stream there
            // —; or one that leaves objects numbered above it, which the error rule says.
            if (size is PdfInteger { Value: >= 0 } count &&
                (count.Value == expected || count.Value == NamingTableSize(sections[index]) || (count.Value < expected && count.Value == pastSize)))
            {
                continue;
            }

            // Past the report's room, the section is counted, its finding not written (#168): there are as many as the chain
            // has sections.
            if (!context.HasRoomFor(this))
            {
                continue;
            }

            context.Report(
                this,
                PdfValidationLocation.AtPosition(section.TrailerLocation),
                Describe(section, size, expected),
                "Set /Size to one more than the highest object number the section and those it updates use.");
        }
    }

    /// <summary>
    /// Says what is wrong with the <c>/Size</c> <paramref name="section"/> gives, <paramref name="size"/> as the file wrote
    /// it, where <paramref name="expected"/> is one more than the highest number it and the sections it updates use.
    /// </summary>
    private static string Describe(XRefSectionRecord section, PdfObject? size, long expected)
    {
        var subject = section.Kind == XRefSectionKind.Stream
            ? string.Create(CultureInfo.InvariantCulture, $"The cross-reference stream at offset {section.Offset}")
            : string.Create(CultureInfo.InvariantCulture, $"The trailer of the cross-reference table at offset {section.Offset}");

        return size switch
        {
            null => $"{subject} has no /Size.",
            PdfInteger { Value: >= 0 } count => string.Create(
                CultureInfo.InvariantCulture,
                $"{subject} gives /Size {count.Value}, where the highest object number it and the sections it updates use, {expected - 1}, makes it {expected}."),
            PdfReference reference => string.Create(
                CultureInfo.InvariantCulture,
                $"{subject} writes its /Size as the reference {reference.Id.Number} {reference.Id.Generation} R, where the count of objects is written directly."),
            PdfReal real when real.AsInteger() is not null =>
                $"{subject} gives its /Size as a real number, where the count of objects is an integer.",
            _ => $"{subject} gives a /Size that is not a count of objects.",
        };
    }

    /// <summary>
    /// Gets the <c>/Size</c> of the table whose <c>/XRefStm</c> <paramref name="section"/> records — the table that names a
    /// hybrid file's stream there, first or again —, or -1 when the record is no such naming, or its table gives no count.
    /// </summary>
    /// <remarks>
    /// Table 17 asks two things of a cross-reference stream's <c>/Size</c>: one more than the highest number it and
    /// the sections it updates use, and "equivalent to the Size entry in a trailer dictionary". A hybrid file's
    /// stream cannot always meet both — its own object is indexed by the newer table that names it —, and writers
    /// meet one or the other: either is accepted.
    /// </remarks>
    private static long NamingTableSize(XRefSectionRecord section) =>
        section.NamingTrailer?.GetRaw(PdfName.Size) is PdfInteger { Value: >= 0 } size ? size.Value : -1;

    /// <summary>
    /// Gets the section whose rows <paramref name="section"/> counts: the section itself, or, for an <c>/XRefStm</c> that
    /// names a hybrid file's stream again, that stream (#156); null for one that names anything else again, which counts
    /// nothing.
    /// </summary>
    private static XRefSectionRecord? Counted(XRefSectionRecord section) =>
        section.State != XRefSectionState.NamedAgain ? section
        : section.ReadAs is { IsHybridStream: true } stream ? stream
        : null;

    /// <summary>
    /// Determines whether the chain gave every section it names, whole: none lost, read only in part, or cut by one of the
    /// reader's limits — a hybrid file's table whose <c>/XRefStm</c> names no stream of its revision among them, whose
    /// stream is lost (#156).
    /// </summary>
    private static bool IsWhole(IReadOnlyList<XRefSectionRecord> sections)
    {
        foreach (var section in sections)
        {
            if (section.State is XRefSectionState.NotFound or XRefSectionState.Malformed || section.Incomplete ||
                section.CutByLimit || section.ChainHiddenByGuard is not null || section.NamesNoStream is not null)
            {
                return false;
            }
        }

        return true;
    }
}
