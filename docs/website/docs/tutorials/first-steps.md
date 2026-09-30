---
title: Open, inspect and validate a PDF
description: Write a one-page PDF, open it, cut it short as a failed download would, and watch the reader repair it and the validator judge it.
---

# Open, inspect and validate a PDF

In this tutorial we will write a small PDF, open it with AdCodicem.Pdf, damage it the way a download cut short
damages a file, and watch what the reader and the validator say about each version. By the end, you will have used
the three things the library does today: it reads a document lazily, it tells you what it had to repair, and it
tells you what is wrong with a file.

It takes about fifteen minutes. You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and a terminal,
and nothing else: the program writes the PDF itself.

## Create the project

Create a console application and add the library to it:

```bash
dotnet new console -o FirstSteps
cd FirstSteps
dotnet add package AdCodicem.Pdf --prerelease
```

The library only publishes previews for now, which is what `--prerelease` asks for.

Open `Program.cs` and delete everything in it.

## Write a PDF

A PDF is mostly text: numbered objects, then an index that gives the byte offset where each object starts. Put
this at the top of `Program.cs`:

```csharp
using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

var text = """
    %PDF-1.7
    1 0 obj
    << /Type /Catalog /Pages 2 0 R >>
    endobj
    2 0 obj
    << /Type /Pages /Kids [3 0 R] /Count 1 >>
    endobj
    3 0 obj
    << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>
    endobj
    xref
    0 4
    0000000000 65535 f
    0000000010 00000 n
    0000000062 00000 n
    0000000122 00000 n
    trailer
    << /Size 4 /Root 1 0 R >>
    startxref
    213
    %%EOF
    """.ReplaceLineEndings("\r\n");

byte[] sound = Encoding.ASCII.GetBytes(text);
```

That is a complete document: a catalog, a page tree, one blank A4 page, and the index. The offsets in the index
count two bytes at the end of each line, which is why `ReplaceLineEndings` makes every line end the same way,
whatever your editor saves. The last `using` is for the end of the tutorial.

## Open it

Add these lines below the others:

```csharp
using var document = PdfDocument.Open(sound);

var pages = document.Catalog.GetDictionary(PdfName.Pages);
Console.WriteLine($"Version:     {document.Version}");
Console.WriteLine($"Pages:       {pages.GetInteger(PdfName.Count)}");
Console.WriteLine($"Repaired:    {document.WasRepaired}");
Console.WriteLine($"Diagnostics: {document.Diagnostics.Count}");
```

Run the program:

```bash
dotnet run
```

```text title="Output"
Version:     1.7
Pages:       1
Repaired:    False
Diagnostics: 0
```

The reader found the version in the header, and the page count in the page tree. Notice that it repaired nothing
and noticed nothing: `Diagnostics` is where it writes down anything it had to work around, and for this file the
list is empty.

## Cut it short

Files arriving from elsewhere are often damaged. A common case is a download that stopped before the end, which
loses the index, since a PDF keeps it last. Add these lines below the others to make such a file, and open it:

```csharp
byte[] cutShort = Encoding.ASCII.GetBytes(text[..text.IndexOf("xref", StringComparison.Ordinal)]);

using var damaged = PdfDocument.Open(cutShort);

Console.WriteLine();
Console.WriteLine($"Repaired:    {damaged.WasRepaired}");
foreach (var entry in damaged.Diagnostics)
{
    Console.WriteLine(entry);
}
```

Run it again:

```bash
dotnet run
```

```text title="Output"
Version:     1.7
Pages:       1
Repaired:    False
Diagnostics: 0

Repaired:    True
Repair xref.rebuilt: The cross-reference index was rebuilt by scanning the file.
Repair trailer.root-recovered: The trailer's /Root does not lead to a document catalog; the catalog was found as object 1.
```

The reader did not refuse the file. It rebuilt the lost index by scanning the file for objects, and found the
catalog among them, since the trailer that names it was lost too. Each line it wrote says how serious it is
(`Repair`), gives a code that stays the same from one release to the next (`xref.rebuilt`), and explains what
happened.

## Validate both

The reader tells you what it did. To know what is wrong with a file, ask the validator. Add these lines at the end
of the program:

```csharp
var validator = new PdfValidator();

Console.WriteLine();
Console.WriteLine(validator.Validate(document));

var report = validator.Validate(damaged);
Console.WriteLine(report);
foreach (var finding in report.Findings)
{
    Console.WriteLine(finding);
    Console.WriteLine($"  Remedy: {finding.Remedy}");
}
```

Run it one last time:

```bash
dotnet run
```

```text title="Output"
Version:     1.7
Pages:       1
Repaired:    False
Diagnostics: 0

Repaired:    True
Repair xref.rebuilt: The cross-reference index was rebuilt by scanning the file.
Repair trailer.root-recovered: The trailer's /Root does not lead to a document catalog; the catalog was found as object 1.

structural 1: errors 0, warnings 0, information 0
structural 1: errors 1, warnings 1, information 0
Warning file.eof-missing at offset 213: The file does not end with an end-of-file marker: no %%EOF in its last 213 bytes.
  Remedy: Append %%EOF on a line of its own at the end of the file, after checking that nothing was cut off before it.
Error file.startxref-missing at offset 213: The file does not give the offset of its cross-reference section: no startxref in its last 213 bytes.
  Remedy: Write startxref and the offset of the file's last cross-reference section before %%EOF; if the index itself is lost, rewrite the file.
```

The sound file has no findings. The damaged one has two, each with a rule identifier, a location in the file and a
remedy. One is an **error**: the reader could read the file, but it had to rebuild the index, so it cannot vouch
that it reads the file as it was written. That is the verdict to act on before accepting a document from outside.

## What you have done

You wrote a PDF, opened it without reading more of it than you asked for, saw the reader repair a damaged copy and
say exactly what it did, and had the validator judge both. The complete program is the
[`FirstSteps` sample](https://github.com/AdCodicem/AdCodicem.Pdf/tree/main/samples/FirstSteps) in the repository.

From here:

- to check the documents your application receives, follow
  [Validate a document before accepting it](../guides/validate-a-received-document.md) and
  [Find out what the reader repaired](../guides/find-what-the-reader-repaired.md);
- to understand what you saw, read [Diagnostics](../concepts/diagnostics.md), [Validation](../concepts/validation.md)
  and [Lazy reading](../concepts/lazy-reading.md);
- to look up a code or a rule, see [Diagnostic codes](../reference/diagnostics.md) and
  [Validation rules](../reference/validation-rules.md).
