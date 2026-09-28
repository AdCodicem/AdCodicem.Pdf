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
/// It inspects every object the file's index holds in use, and reads no content. It does so only where that index is
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

        var numbers = new int[index.Count];
        index.Entries.Keys.CopyTo(numbers, 0);
        Array.Sort(numbers);

        foreach (var number in numbers)
        {
            // Read again for each object: one read meanwhile may make the reader copy the chain's index.
            if (number <= 0 || tree.PageIndexes.ContainsKey(number) || tree.Nodes.Contains(number) ||
                context.Document.Reader.ChainIndex?.TryGet(number, out var entry) != true ||
                entry.Kind == XRefEntryKind.Free)
            {
                continue;
            }

            var id = new PdfObjectId(number, entry.Kind == XRefEntryKind.Regular ? entry.Generation : 0);

            if (context.Document.GetObject(id).AsDictionary() is not { } page || !page.IsOfType(PdfName.Page) ||
                (!page.ContainsKey(PdfName.Parent) && !page.ContainsKey(PdfName.Contents)))
            {
                continue;
            }

            context.Report(
                this,
                PdfValidationLocation.OfObject(id),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Object {number} is a page, of /Type /Page with {(page.ContainsKey(PdfName.Parent) ? "a /Parent" : "a /Contents")}, that the page tree does not list: no reader shows it."),
                remedy: null);
        }
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

        foreach (var section in structure.Sections)
        {
            if (section.State is not (XRefSectionState.Read or XRefSectionState.Relocated) || section.Incomplete || section.CutByLimit)
            {
                return false;
            }
        }

        var probe = context.Probe;
        return probe.Broken.Count == 0 && probe.BrokenObjectStreams.Count == 0 && probe.NotChecked.Count == 0;
    }
}
