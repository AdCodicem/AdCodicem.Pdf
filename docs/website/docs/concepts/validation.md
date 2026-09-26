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
    // Readers will disagree about this document: refuse it, or send it to repair.
}

foreach (var finding in report.Findings)
{
    Console.WriteLine(finding);   // Warning file.eof-missing at offset 48213: The file does not end with …
    Console.WriteLine(finding.Remedy);
}
```

:::info Being built

Validation arrives rule by rule, in the order the [M2 milestone](/project/milestones/M2) gives. The
structural profile holds **one rule** so far; the [rules table](/project/validation-rules) lists every rule
that exists. Like everything in a preview, the API may still change.

:::

## Findings

A finding is what one rule found:

| Member | What it is |
|---|---|
| `RuleId` | The rule's identifier, such as `file.eof-missing` — stable, and what you filter on |
| `Severity` | `Error`, `Warning` or `Information`, always the same for a given rule |
| `Location` | Where it applies: an object, a byte offset in the file, or the document as a whole |
| `Message` | What is wrong, for a person to read |
| `Remedy` | What would put it right, as a hint, or null — so that a report reads as a plan |

Rule identifiers are `family.name`, both in lowercase kebab case: `file.eof-missing`, and later
`xref.broken-offset` or `page-tree.count-mismatch`. They are public API: renaming one is a breaking change,
so you can filter on them safely. The [rules table](/project/validation-rules) gives each one's severity and
meaning.

## Severities

| Severity | Means | What to do |
|---|---|---|
| `Error` | The document is broken: readers will disagree about what it contains | Refuse it, or repair it |
| `Warning` | It works, but it is wrong: readers accept it, and a stricter one may not | Accept it, and keep the report |
| `Information` | Worth knowing; nothing is wrong | Nothing |

The validator errs towards `Warning`: a validator that calls sound files broken teaches its users to ignore
it. Output from Chromium, LibreOffice, Word and the other producers in the test corpus earns no error.

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

The document's [reader limits](reader-limits.md) apply to validation too. An object the reader cut at a
limit is not a fault of the file, and a rule meeting one reports at most, as information, that it could not
check it whole; open the document with a raised limit to check the rest. If you opened it with
`ThrowOnLimit`, validation throws `PdfLimitExceededException` like any other read — the one exception it
lets through. Otherwise it reports, and throws only for your own mistakes: a null or disposed document.
