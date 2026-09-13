---
title: Diagnostics
sidebar_position: 2
---

# Diagnostics

Real PDFs are frequently not conforming. A reader that refuses them fails where every reader on the
market succeeds; a reader that silently patches them leaves you guessing. This library does the third
thing: it repairs what it can and **returns an account of what it did**.

```csharp
using var document = PdfDocument.Open("received-from-supplier.pdf");

if (document.Diagnostics.HasRepairs)
{
    foreach (var entry in document.Diagnostics)
    {
        Console.WriteLine($"{entry.Severity} {entry.Code} at {entry.Position}: {entry.Message}");
    }
}
```

The report is a returned value, not a log: it can be inspected, serialised, asserted on in your own tests,
and used to decide whether to accept a file into your system.

## Severities

| Severity | Meaning |
|---|---|
| `Information` | Worth knowing, changes nothing |
| `Repair` | The file was not conforming and the reader worked around it |
| `Warning` | Something is wrong and the result may not be what you expect |
| `ConformanceLoss` | A guarantee such as PDF/A or PDF/UA was lost by an operation |

## Codes

Codes are stable: they are part of the public contract, because callers filter on them.

| Code | Raised when |
|---|---|
| `xref.rebuilt` | The index was rebuilt by scanning the whole file |
| `xref.offset-adjusted` | An object was not where the index said, and was found nearby |
| `xref.chain-cycle` | The chain of previous sections looped |
| `xref.entry-out-of-range` | An entry pointed outside the file |
| `stream.length-invalid` | A stream's declared length did not match where its data ended |
| `stream.truncated` | A stream ran past the end of the file |
| `syntax.unexpected-token` | A token was found where a value was expected |
| `syntax.truncated-object` | The file ended in the middle of an object |
| `syntax.depth-exceeded` | Nesting went deeper than the reader will follow |
| `object.redefined` | An object was defined more than once; the last definition won |
| `filter.failed` | A filter could not be applied, and the data was left encoded |
| `filter.unsupported` | The file names a filter the library does not implement |

## Exceptions, by contrast

Exceptions are reserved for what makes the operation impossible: the input is not a PDF
(`PdfFormatException`), or it is encrypted and cannot be opened with the credentials given
(`PdfEncryptedException`). Anything the reader can work around is a diagnostic, never an exception.
