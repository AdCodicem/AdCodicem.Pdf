---
title: Validation
sidebar_position: 4
---

# Validation

Reading answers "can I get at this document?". Validation answers another question: **is it sound?** The
reader opens damaged files and works around what it can; the validator says what is wrong with a file,
whether or not the reader could work around it — the verdict you want before accepting a third-party
document into your system.

```csharp
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Validation;

var validator = new PdfValidator();                 // stateless: one instance serves every thread

using var document = PdfDocument.Open("received-from-supplier.pdf");
var report = validator.Validate(document);

if (report.HasErrors)
{
    // The reader cannot vouch that it reads this document as written: refuse it, or send it to repair.
}

foreach (var finding in report.Findings)
{
    Console.WriteLine(finding);   // Warning file.eof-missing at offset 48213: The file does not end with …
    Console.WriteLine(finding.Remedy);
}
```

:::info Being built

Validation arrives rule by rule, in the order the [M02 milestone](/project/milestones/M02) gives. The
structural profile checks the file's **structure and cross-references** — its header, `startxref`, its
trailer, each cross-reference section and each entry of its index —, the **objects** its trailer reaches —
references to objects the file lacks, `endobj`, names — and its **page tree**; the shape of each object, streams,
fonts and the rest follow. The [rules table](/project/validation-rules) lists every rule that exists. Like
everything in a preview, the API may still change.

:::

## Findings

A finding is what one rule found:

| Member | What it is |
|---|---|
| `RuleId` | The rule's identifier, such as `file.eof-missing` — stable, and what you filter on |
| `Severity` | `Error`, `Warning` or `Information`, always the same for a given rule |
| `Location` | Where it applies: an object, a byte offset in the file, a page, or the document as a whole |
| `Message` | What is wrong, for a person to read |
| `Remedy` | What would put it right, as a hint, or null — so that a report reads as a plan |

Rule identifiers are `family.name`, both in lowercase kebab case: `file.eof-missing`,
`xref.entry-shifted`, `page-tree.count-mismatch`. They are public API: renaming one is a breaking change,
so you can filter on them safely. The [rules table](/project/validation-rules) gives each one's severity and
meaning.

A finding about a page, or about an object a page holds, gives `Location.PageIndex`: the page's index from 0, in
the order of the page tree. Written out, a location counts pages from 1 as people do — `page 3, object 12 0`.

## Severities

| Severity | Means | What to do |
|---|---|---|
| `Error` | The document is broken: the reader cannot vouch that it reads what was written — it rebuilt the index by scanning the file, lost part of it, or chose what the file does not designate | Refuse it, or repair it |
| `Warning` | It breaks the specification, and is read all the same as it was evidently meant: the reader reads it, and a stricter reader may not | Accept it, and keep the report |
| `Information` | Worth knowing; nothing is wrong, or something could not be checked | Nothing |

What separates a warning from an error is whether the file reads as it was written, which the validator
checks against what the reader did rather than guesses about other readers. It errs toward `Warning`: a
validator that calls sound files broken teaches its users to ignore it. Output from Chromium, LibreOffice,
Word and the other producers in the test corpus earns no error; some of it earns a warning — Microsoft Print
to PDF names the line feed before each object as its offset, and Word's hybrid files miscount a `/Size` —,
which every reader reads past.

## Findings are not diagnostics

`PdfDocument.Diagnostics` is the reader's account of what **it did** to read the file: a repair, a limit
reached. A finding is the validator's verdict on **the file**. They have separate types, separate
severities — the reader's `Repair` says what it did, not how wrong the file is — and codes that never
collide: no rule identifier is ever a diagnostic code. A rule may base its verdict on what the reader
noticed; the rules table says when one does.

## The report

`PdfValidationReport` holds the findings in the order they were found — rules run in the profile's order —
so two validations of the same document give the same report. It also counts them by severity
(`ErrorCount`, `WarningCount`, `InformationCount`), and `Contains(ruleId)` says whether a rule reported
anything.

The report is bounded. A hostile file can have a fault in every object; the report keeps at most
`PdfValidatorOptions.FindingCapacity` findings, 1,000 by default, and still counts every one —
`SuppressedCount` says how many it did not keep.

## Options and profiles

```csharp
var validator = new PdfValidator(new PdfValidatorOptions
{
    Profile = ValidationProfile.Structural,   // the default
    FindingCapacity = 100,
});
```

A profile is an ordered set of rules with a name and a version. `ValidationProfile.Structural` holds the
rules that apply to any PDF, whatever it claims to conform to; `RuleIds` lists them. The PDF/A and PDF/UA
profiles will come with the `AdCodicem.Pdf.Conformance` package. You cannot yet write rules of your own.

## Reading, limits and exceptions

The validator reads through the document you give it, lazily, like any other caller: it reads only what
its rules inspect, what it resolves joins the document's cache, and anything the reader notices on the way
joins `document.Diagnostics`. The document stays open, and belongs to one thread at a time.

The cross-reference rules read a few dozen bytes where each entry of the file's index places its object —
its header, never its body — and each object stream once, without keeping it. They judge the index as the
file wrote it, not as the reader corrected it: reading objects between two validations does not change the
report.

The object and page tree rules walk what the trailer reaches, once: the page tree through its `/Kids`, and every
object a reference leads to, each resolved a single time and read through the document's cache — stream
dictionaries, never stream data. They keep what they found wrong and a set of the object numbers they met; on a
thousand-page document, opening it and validating it allocates about 2.4 MB. Pages are counted as the tree lists
them, as qpdf counts them: a kid that is null, or that names an object the file lacks, takes the place of a page
with nothing on it. Walking a damaged file can make the reader rebuild its index, as reading it would; the
cross-reference rules still judge the index the file wrote. Where the index is sound and whole, one rule also looks
at every object it holds, to name the pages the tree leaves out.

The document's [reader limits](reader-limits.md) apply to validation too. An object the reader cut at a
limit is not a fault of the file, and a rule meeting one reports at most, as information, that it could not
check it whole; open the document with a raised limit to check the rest. If you opened it with
`ThrowOnLimit`, validation throws `PdfLimitExceededException` like any other read — the one exception it
lets through. Otherwise it reports, and throws only for your own mistakes: a null or disposed document.
