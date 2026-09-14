using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

// Opens a document, reports what the reader noticed about it, and counts its pages — which is as far as
// the library goes today. Run it against anything: the interesting output comes from files that are
// slightly wrong, which is most of them.
//
//   dotnet run --project samples/ReadDocument -- some-file.pdf

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run --project samples/ReadDocument -- <file.pdf>");
    return 1;
}

using var document = PdfDocument.Open(args[0]);

Console.WriteLine($"PDF version     {document.Version}");
Console.WriteLine($"Objects         {document.ObjectCount}");
Console.WriteLine($"Index rebuilt   {document.WasRepaired}");
Console.WriteLine($"Pages           {CountPages(document)}");

if (document.Diagnostics.Count == 0)
{
    Console.WriteLine("Diagnostics     none — the file is well formed");
}
else
{
    Console.WriteLine("Diagnostics");
    foreach (var entry in document.Diagnostics)
    {
        Console.WriteLine($"  {entry}");
    }
}

return 0;

// The page tree is walked here rather than by the library: pages arrive with milestone 5.
static int CountPages(PdfDocument document)
{
    var root = document.Catalog.GetDictionary(PdfName.Pages);
    return root is null ? 0 : Count(root, [], 0);

    static int Count(PdfDictionary node, HashSet<PdfDictionary> visited, int depth)
    {
        if (depth > 64 || !visited.Add(node))
        {
            return 0;
        }

        var kids = node.GetArray(PdfName.Kids);
        if (kids is null)
        {
            return node.IsOfType(PdfName.Page) ? 1 : 0;
        }

        var total = 0;
        for (var index = 0; index < kids.Count; index++)
        {
            if (kids.Resolved(index).AsDictionary() is { } kid)
            {
                total += Count(kid, visited, depth + 1);
            }
        }

        return total;
    }
}
