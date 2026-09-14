# Samples

Each sample is a runnable console project referencing `src/` directly rather than a published package, so
a change that breaks the API breaks the samples in the same build.

| Sample | What it shows |
|---|---|
| [`ReadDocument`](ReadDocument) | Opening a document, reading its diagnostics, walking its page tree |

```bash
dotnet run --project samples/ReadDocument -- tests/corpus/documents/damaged/invoice-no-xref.pdf
```

Point it at a damaged file to see the interesting half of the output: the reader repairs what it can and
tells you exactly what it did.
