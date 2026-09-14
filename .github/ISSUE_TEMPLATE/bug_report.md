---
name: Bug report
about: Something behaves differently from what the documentation says
title: ""
labels: bug
---

## What happened

## What you expected

## The document

The single most useful thing you can attach is the file that reproduces it. If it cannot be published,
say so — do not attach it. A reduced or synthetic version that still reproduces the problem is ideal, and
`docs/corpus-contributions.md` explains the private corpus for the rest.

If the document makes the library crash, hang, or allocate without bound, please report it privately
instead: see [SECURITY.md](../SECURITY.md). That is a denial of service, not an ordinary bug.

- Produced by (Word, Acrobat, a scanner, an ERP…):
- Approximate size and page count:

## Reproduction

```csharp
// The smallest code that shows it
```

## Diagnostics

The reader reports what it noticed; that output usually names the cause.

```csharp
foreach (var entry in document.Diagnostics) Console.WriteLine(entry);
```

```
paste here
```

## Environment

- Package version:
- .NET version:
- Operating system:
