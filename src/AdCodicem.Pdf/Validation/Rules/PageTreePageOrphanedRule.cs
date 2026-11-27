using System.Globalization;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.PageTreePageOrphaned"/>: says which pages the file holds that its page tree does not
/// list.
/// </summary>
/// <remarks>
/// <para>
/// A page object the tree does not list is shown by no reader. Nothing forbids it — an edit that removed a page from
/// the tree without freeing its object leaves one, and objects nothing refers to are common —, so this is information
/// (ADR 45): the page may be what someone meant to keep. A page is an object of <c>/Type /Page</c> with a
/// <c>/Parent</c> or a <c>/Contents</c>: a marked-content property list typed <c>/Page</c>, as DocuSign writes, is not
/// one.
/// </para>
/// <para>
/// It inspects every object the file's index holds in use — those an object stream holds together, stream by stream —, but
/// those one of the reader's limits cut, and reads no content. It does so only where that index is
/// sound and whole — the chain read to its end, every entry leading to its object, every object stream readable, the
/// page tree read whole —: elsewhere loading an object may rebuild the index and bring back what an update deleted,
/// and a page the tree seems not to list may lie below a node the reader could not produce.
/// </para>
/// </remarks>
internal sealed class PageTreePageOrphanedRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.PageTreePageOrphaned;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Information;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var tree = context.PageTree;

        if (tree.Partial || !IsIndexSound(context) || context.Document.Reader.ChainIndex is not { } index)
        {
            return;
        }

        var numbers = index.CopyNumbers();
        Array.Sort(numbers);

        // The objects an object stream holds are read together, stream by stream, before those written directly: read in
        // number order, objects that lie in turn in streams the reader cannot keep together would have it decode a whole
        // stream for each (#181). The pages are reported in number order all the same.
        List<(int Stream, int Index, int Number)>? packed = null;

        foreach (var number in numbers)
        {
            if (Inspected(context, tree, number, out var entry) && entry.Kind == XRefEntryKind.Compressed)
            {
                (packed ??= []).Add((entry.ObjectStreamNumber, entry.IndexInObjectStream, number));
            }
        }

        // The pages object streams hold, in number order once all are read.
        List<(int Number, bool Parent)>? found = null;

        if (packed is not null)
        {
            packed.Sort();

            foreach (var (_, _, number) in packed)
            {
                // Looked up again for each object: one read meanwhile may make the reader copy the chain's index.
                if (Inspected(context, tree, number, out var entry) && IsPage(context, number, entry, out var parent))
                {
                    (found ??= []).Add((number, parent));
                }
            }

            found?.Sort();
        }

        var next = 0;

        foreach (var number in numbers)
        {
            PdfObjectId id;
            bool parent;

            if (found is not null && next < found.Count && found[next].Number == number)
            {
                id = new PdfObjectId(number);
                parent = found[next++].Parent;
            }
            else if (Inspected(context, tree, number, out var entry) && entry.Kind == XRefEntryKind.Regular &&
                IsPage(context, number, entry, out parent))
            {
                id = new PdfObjectId(number, entry.Generation);
            }
            else
            {
                continue;
            }

            // A page the report has no room for is counted, its finding not written: one an object (#168).
            if (!context.HasRoomFor(this))
            {
                continue;
            }

            context.Report(
                this,
                PdfValidationLocation.OfObject(id),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Object {number} is a page, of /Type /Page with {(parent ? "a /Parent" : "a /Contents")}, that the page tree does not list: no reader shows it."),
                remedy: null);
        }
    }

    /// <summary>
    /// Determines whether object <paramref name="number"/> is one to inspect — in use in the chain's index, and neither a page
    /// nor a node of the tree —, and gives its entry there.
    /// </summary>
    private static bool Inspected(ValidationContext context, PageTreeWalk tree, int number, out XRefEntry entry)
    {
        entry = default;
        return number > 0 && !tree.IsPage(number) && !tree.Nodes.Contains(number) &&
            context.Document.Reader.ChainIndex?.TryGet(number, out entry) == true && entry.Kind != XRefEntryKind.Free;
    }

    /// <summary>
    /// Reads object <paramref name="number"/>, which <paramref name="entry"/> places, and determines whether it is a page the
    /// tree does not list, and whether it has a <c>/Parent</c>.
    /// </summary>
    private static bool IsPage(ValidationContext context, int number, XRefEntry entry, out bool parent)
    {
        parent = false;

        // An object a limit cut is not judged: its /Type may be what the cut left of /Pages. Nor is one whose value is a
        // reference: a link of a chain the tree may list its page through, which is the page (the object it leads to).
        var value = context.Document.Reader.GetIndexedObject(number);

        if (value is PdfReference || value.AsDictionary() is not { } page || context.Document.Reader.IsCutAtLimit(number) ||
            !page.IsOfType(PdfName.Page))
        {
            return false;
        }

        parent = page.ContainsKey(PdfName.Parent);
        return parent || page.ContainsKey(PdfName.Contents);
    }

    /// <summary>
    /// Determines whether the file's own index is sound and whole, so that loading any object it holds reads what the
    /// file wrote and rebuilds nothing.
    /// </summary>
    private static bool IsIndexSound(ValidationContext context)
    {
        var structure = context.Structure;

        if (!structure.ChainRead || structure.ChainCutAt >= 0 || structure.LoopOffset >= 0 || context.Document.WasRepaired)
        {
            return false;
        }

        // A naming again reads nothing: what it names is judged where it was read, or looked for (#156).
        foreach (var section in structure.Sections)
        {
            if (section.State is not (XRefSectionState.Read or XRefSectionState.Relocated or XRefSectionState.NamedAgain) ||
                section.Incomplete || section.CutByLimit || section.ChainHiddenByGuard is not null)
            {
                return false;
            }
        }

        // What the probe counted, not what it wrote: an entry past the room the report had is as broken (#168).
        var probe = context.Probe;
        return probe.Broken.Total == 0 && probe.BrokenObjectStreams.Count == 0 && probe.NotChecked.Total == 0;
    }
}
