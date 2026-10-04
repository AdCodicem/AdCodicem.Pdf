```csharp title="Program.cs" {5,15}
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Validation;

// Opened lazily: an object is read when it is first asked for.
using var document = PdfDocument.Open(args[0]);

// What the reader worked around to open the file.
foreach (var entry in document.Diagnostics)
{
    Console.WriteLine(entry);
}

// What is wrong with the file, rule by rule, each with its remedy.
var report = new PdfValidator().Validate(document);
foreach (var finding in report.Findings)
{
    Console.WriteLine($"{finding}\n  Remedy: {finding.Remedy}");
}
```
