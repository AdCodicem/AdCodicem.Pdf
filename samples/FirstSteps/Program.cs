// The program the tutorial "Open, inspect and validate a PDF" builds, step by step:
// docs/website/docs/tutorials/first-steps.md. Every C# block of the tutorial is in this file, and what it prints
// is what the tutorial shows; FirstStepsTutorialTests holds the two together.
//
//   dotnet run --project samples/FirstSteps

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

using var document = PdfDocument.Open(sound);

var pages = document.Catalog.GetDictionary(PdfName.Pages);
Console.WriteLine($"Version:     {document.Version}");
Console.WriteLine($"Pages:       {pages.GetInteger(PdfName.Count)}");
Console.WriteLine($"Repaired:    {document.WasRepaired}");
Console.WriteLine($"Diagnostics: {document.Diagnostics.Count}");

byte[] cutShort = Encoding.ASCII.GetBytes(text[..text.IndexOf("xref", StringComparison.Ordinal)]);

using var damaged = PdfDocument.Open(cutShort);

Console.WriteLine();
Console.WriteLine($"Repaired:    {damaged.WasRepaired}");
foreach (var entry in damaged.Diagnostics)
{
    Console.WriteLine(entry);
}

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
