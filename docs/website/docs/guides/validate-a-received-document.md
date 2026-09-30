---
title: Validate a document before accepting it
description: Validate a third-party PDF, refuse it on an error, keep the report on a warning, and filter findings by rule.
---

# Validate a document before accepting it

This guide shows how to check a PDF you received — an upload, an attachment, a supplier's invoice — before it
enters your system, and how to act on the verdict.

## Validate the document

Create one validator and keep it: it holds no state, so a single instance serves every thread and can be registered
as a singleton. Open the document, validate it, and decide on the report:

```csharp
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Validation;

var validator = new PdfValidator();

using var document = PdfDocument.Open("received-from-supplier.pdf");
var report = validator.Validate(document);

if (report.HasErrors)
{
    // The reader cannot vouch that it reads this document as written: refuse it, or send it to repair.
}
else
{
    // Accept it. Warnings, if any, break the specification in ways every reader reads past: keep the report.
}
```

The document stays open after validation, and is yours to dispose.

## Tell the sender what is wrong

Every finding says which rule found it, where, what is wrong, and what would put it right:

```csharp
foreach (var finding in report.Findings)
{
    Console.WriteLine(finding);          // Error file.startxref-missing at offset 213: …
    Console.WriteLine(finding.Remedy);   // Write startxref and the offset of …
}
```

A finding about a page gives the page, from 0, in `finding.Location.PageIndex`; written out, it counts from 1.

## Filter by rule

Filter on the rule identifier, never on the message: identifiers are public API, and stay the same from one release
to the next. `PdfValidationRuleIds` holds each as a constant.

```csharp
if (report.Contains(PdfValidationRuleIds.FileEofMissing))
{
    // The file may have been cut short in transit.
}

foreach (var finding in report.Findings)
{
    if (finding.RuleId.StartsWith("page-tree.", StringComparison.Ordinal))
    {
        Console.WriteLine(finding);
    }
}
```

## Bound the report

A hostile file can have a fault in every object. A report keeps 1,000 findings by default and counts the rest;
lower the bound when you only need a verdict, and check `SuppressedCount` when you need to know how many you did not
keep:

```csharp
var validator = new PdfValidator(new PdfValidatorOptions { FindingCapacity = 100 });
```

`ErrorCount`, `WarningCount` and `InformationCount` count every finding, kept or not, so `HasErrors` is right however
small the bound.

## Check a large document whole

Validation reads under the document's [reader limits](../reference/reader-limits.md). When a finding says, as
information, that an object could not be checked whole, the reader cut it at a limit: open the document with that
limit raised and validate again ([Read or refuse a document that reaches a limit](handle-reader-limits.md)). If you
opened the document with `ThrowOnLimit`, `Validate` throws `PdfLimitExceededException` instead.

## See also

- [Validation](../reference/validation.md) and [Validation rules](../reference/validation-rules.md), in the
  reference: the report's members, and every rule with its severity.
- [Validation](../concepts/validation.md), explained: where a warning ends and an error begins.
- [Find out what the reader repaired](find-what-the-reader-repaired.md): what the reader did, beside the verdict.
