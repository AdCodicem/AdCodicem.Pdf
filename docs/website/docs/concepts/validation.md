---
title: Validation
sidebar_position: 4
description: What validation answers that reading does not, where the line between a warning and an error lies, and how the Arlington model is used.
---

# Validation

Reading answers "can I get at this document?". Validation answers another question: **is it sound?** The
reader opens damaged files and works around what it can; the validator says what is wrong with a file,
whether or not the reader could work around it — the verdict you want before accepting a third-party
document into your system. [Validate a document before accepting it](../guides/validate-a-received-document.md)
shows how; the [reference](../reference/validation.md) describes the report, and
[Validation rules](../reference/validation-rules.md) every rule.

:::info Being built

Validation arrives rule by rule, in the order the [M02 milestone](/project/milestones/M02) gives. The
structural profile checks the file's **structure and cross-references** — its header, `startxref`, its
trailer, each cross-reference section and each entry of its index —, the **objects** its trailer reaches —
references to objects the file lacks, `endobj`, names, and each object's **shape** against the Arlington PDF Model —
and its **page tree**; streams, fonts and the rest follow.

:::

## Findings are not diagnostics

`PdfDocument.Diagnostics` is the reader's account of what **it did** to read the file: a repair, a limit
reached. A finding is the validator's verdict on **the file**. They have separate types, separate
severities — the reader's `Repair` says what it did, not how wrong the file is — and codes that never
collide: no rule identifier is ever a diagnostic code. A rule may base its verdict on what the reader
noticed; the rules table says when one does.

## Where a warning ends and an error begins

What separates a warning from an error is whether the file reads as it was written
([ADR 45](/project/adr/a-findings-severity-says-whether-the-file-reads-as-written)), which the validator checks
against what the reader did rather than guesses about other readers. An error means the reader cannot vouch that it
reads what was written: it rebuilt the index by scanning the file, lost part of it, or chose what the file does not
designate. A warning means the file breaks the specification and is read all the same, as it was evidently meant.

It errs toward `Warning`: a validator that calls sound files broken teaches its users to ignore it. Output from
Chromium, LibreOffice, Word and the other producers in the test corpus earns no error; some of it earns a warning —
Microsoft Print to PDF names the line feed before each object as its offset, and Word's hybrid files miscount a
`/Size` —, which every reader reads past.

## A report you can rely on

Rules run in the profile's order, and findings are kept in the order they were found, so two validations of the same
document give the same report: it can be stored, compared and asserted on. Each rule reports at one severity,
always, and its identifier is public API, so that a filter written against it keeps working.

The report is bounded. A hostile file can have a fault in every object; a report that kept every finding would let
the file decide how much memory validating it takes. It keeps a fixed number, and still counts every one.

## Profiles

A profile is an ordered set of rules with a name and a version. The structural profile holds the rules that apply to
any PDF, whatever it claims to conform to, and lives in the core package, with the validator
([ADR 36](/project/adr/validation-lives-in-the-core-conformance-in-a-satellite)). Conformance — PDF/A, PDF/UA — is a
profile too, and comes with its own package, since what it checks only matters to documents that claim it.

## Object shapes: the Arlington PDF Model

Four rules check every object the trailer reaches against the
[Arlington PDF Model](https://github.com/pdf-association/arlington-pdf-model), the PDF Association's machine-readable
description of every object ISO 32000 defines — each key, its types, whether it is required, the versions that bring
it in and deprecate it: whether a key the object's type requires is missing, whether a value has a type its key
allows, whether a `/Type` or `/Subtype` is one the model lists, and whether a key is deprecated in the version the
file declares ([ADR 44](/project/adr/object-shape-rules-generated-from-the-arlington-model)).

Each object is given its type in the model by how it is reached — the catalog's `/Pages` is the page tree's root, a
page's `/Annots` holds annotations, whose `/Subtype` says which — and is checked once; one whose type the file leaves
ambiguous is not checked, rather than judged against a guess. The rules are
**version-aware**: a key is required, or deprecated, as of the version the file declares — its header's, or its
catalog's `/Version` when that is later. A file whose header declares no version is not judged on what depends on
one, whatever its catalog says.

The model describes ISO 32000-2. Where it asks more than ISO 32000-1, the version nearly every file declares, the
library follows ISO 32000-1: such rows are overridden by name, each with the words of the specification that justify
it, and what a file breaks that ISO 32000-1 does require is reported. Where a page tree rule already reports a fault,
these four say nothing more of it: one fault, one finding. Each fault is reported once per type and key, at
the first object, with how many objects have it — a thousand structure elements without `/P` are one finding, not a
thousand. The [rules table](../reference/validation-rules.md#object-shapes-from-the-arlington-pdf-model) lists the
overrides, and what these rules leave unchecked for now.

The model is compiled into the library as static tables, at a pinned commit: nothing is read from disk, and the tables
cost no allocation to look up. Its notice, under the Apache License 2.0, travels in the package's `NOTICE` file.

## Reading, limits and exceptions

The validator reads through the document you give it, lazily, like any other caller: it reads only what
its rules inspect, what it resolves joins the document's cache, and anything the reader notices on the way
joins `document.Diagnostics`.

The cross-reference rules read a few dozen bytes where each entry of the file's index places its object —
its header, never its body — and each object stream once, without keeping it. They judge the index as the
file wrote it, not as the reader corrected it: reading objects between two validations does not change the
report, and nothing the reader does while it reads the chain changes the index either.

The object and page tree rules walk what the trailer reaches, once: the page tree through its `/Kids`, and every
object a reference leads to, each resolved a single time and read through the document's cache — stream
dictionaries, never stream data. They keep what they found wrong and a set of the object numbers they met; on a
thousand-page document, opening it and validating it allocates about 2.6 MB. Pages are counted as the tree lists
them, as qpdf counts them: a kid that is null, or that names an object the file lacks, takes the place of a page
with nothing on it. Walking a damaged file can make the reader rebuild its index, as reading it would; the
cross-reference rules still judge the index the file wrote. Where the index is sound and whole, one rule also looks
at every object it holds, to name the pages the tree leaves out.

The document's [reader limits](reader-limits.md) apply to validation too. An object the reader cut at a
limit is not a fault of the file, so a rule meeting one says, as information, that it could not check it whole
rather than calling the file broken; opening the document with a raised limit lets it check the rest. Validation
throws only for the caller's own mistakes, and for a limit when the document was opened to throw on one.
