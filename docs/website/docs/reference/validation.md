---
title: Validation
description: The validator, its options and profiles, and the report it returns — findings, severities and locations.
---

# Validation

The types of `AdCodicem.Pdf.Validation`. Every rule the validator runs is in [Validation rules](validation-rules.md);
what validation is for, and how it differs from reading, is explained in [Validation](../concepts/validation.md).

:::info Being built

Validation arrives rule by rule, in the order the [M02 milestone](/project/milestones/M02) gives. Like everything in
a preview, the API may still change.

:::

## `PdfValidator`

| Member | What it is |
|---|---|
| `PdfValidator(options)` | A validator with these options; `PdfValidatorOptions.Default` when null |
| `Options` | The options it validates with |
| `Validate(document)` | Validates an open document against `Options.Profile`, and returns a `PdfValidationReport` |

A validator holds no state beyond its options: one instance serves every thread, and can be registered as a
singleton. The document stays open, and belongs to one thread at a time, validation included.

`Validate` reports on any document the reader could open, and throws only:

| Exception | When |
|---|---|
| `ArgumentNullException` | The document is null |
| `ObjectDisposedException` | The document was disposed |
| `PdfLimitExceededException` | Validation reached one of the document's [reader limits](reader-limits.md), and the document was opened with `ThrowOnLimit` |

Without `ThrowOnLimit`, a rule that meets an object the reader cut at a limit reports at most, as information, that it
could not check it whole. What the reader notices while validation reads joins `document.Diagnostics`.

## `PdfValidatorOptions`

An immutable record.

| Property | Default | What it is |
|---|---|---|
| `Profile` | `ValidationProfile.Structural` | The profile documents are validated against; null is refused with an `ArgumentNullException` |
| `FindingCapacity` | 1,000 | The most findings a report keeps; beyond it, findings are counted and not kept. Zero keeps none and still counts them; a negative value is refused with an `ArgumentOutOfRangeException` |

## `ValidationProfile`

An ordered set of rules with a name and a version.

| Member | What it is |
|---|---|
| `ValidationProfile.Structural` | The rules that apply to any PDF, whatever it claims to conform to: named `structural`, version 1 |
| `Name` | The profile's name |
| `Version` | The profile's version |
| `RuleIds` | The identifiers of its rules, in the order they run |
| `ToString()` | `Name Version`, such as `structural 1` |

The PDF/A and PDF/UA profiles will come with the `AdCodicem.Pdf.Conformance` package. You cannot yet write rules of
your own.

## `PdfValidationReport`

| Member | What it is |
|---|---|
| `ProfileName`, `ProfileVersion` | The profile the document was validated against |
| `Findings` | The findings kept, in the order they were found — rules run in the profile's order —, so two validations of the same document give the same report |
| `ErrorCount`, `WarningCount`, `InformationCount` | Every finding, by severity, kept or not |
| `SuppressedCount` | How many findings were counted but not kept, past `FindingCapacity` |
| `HasErrors`, `HasWarnings` | Whether any finding is an error, or a warning |
| `Contains(ruleId)` | Whether a rule reported anything |
| `ToString()` | `profile version: errors n, warnings n, information n` |

```text
structural 1: errors 1, warnings 1, information 0
```

## `PdfValidationFinding`

What one rule found.

| Member | What it is |
|---|---|
| `RuleId` | The rule's identifier, such as `file.eof-missing` — stable, and what you filter on; `PdfValidationRuleIds` holds each as a constant |
| `Severity` | A `PdfValidationSeverity`, always the same for a given rule |
| `Location` | Where it applies: a `PdfValidationLocation`, below |
| `Message` | What is wrong, for a person to read, quoting the file as [Diagnostics](diagnostics.md#how-a-message-quotes-the-file) says |
| `Remedy` | What would put it right, as a hint, or null — so that a report reads as a plan |
| `ToString()` | `Severity RuleId at Location: Message` |

```text
Warning object.type-value-wrong at page 1, object 3 0: Object 3 0, a PageObject in the Arlington model, has /Type /Font, where the model wants /Page or /Template.
```

Rule identifiers are `family.name`, both in lowercase kebab case: `file.eof-missing`, `xref.entry-shifted`,
`page-tree.count-mismatch`. They are public API: renaming one is a breaking change, so you can filter on them
safely. No rule identifier is ever a [diagnostic code](diagnostics.md#codes).

## `PdfValidationSeverity`

| Severity | Means | What to do |
|---|---|---|
| `Error` | The document is broken: the reader cannot vouch that it reads what was written — it rebuilt the index by scanning the file, lost part of it, or chose what the file does not designate | Refuse it, or repair it |
| `Warning` | It breaks the specification, and is read all the same as it was evidently meant: the reader reads it, and a stricter reader may not | Accept it, and keep the report |
| `Information` | Worth knowing; nothing is wrong, or something could not be checked | Nothing |

## `PdfValidationLocation`

A record struct; its default value designates the document as a whole.

| Member | What it is |
|---|---|
| `Object` | The object the finding concerns, a `PdfObjectId`, or null |
| `Position` | The byte offset in the file the finding relates to, or null; an offset equal to the file's length designates its end |
| `PageIndex` | The page the finding concerns, from 0, in the order of the page tree, or null. A finding about a page, or about an object a page holds, always gives it |
| `IsDocument` | Whether the location designates the document as a whole |

`ToString()` counts pages from 1, as people do:

| Location | Written |
|---|---|
| An object | `object 12 0` |
| An object at an offset | `object 12 0, at offset 4810` |
| An offset | `offset 48213` |
| A page | `page 3, object 12 0` |
| The document | `the document` |

A finding on a value the trailer holds, written in it rather than as an object of its own, is located at the trailer the
reader read: the first section's, or, when the index was rebuilt, the newest trailer the rebuild found, at its `trailer`
keyword; at the document when the reader read none. A key only an older trailer gives is located there too.
