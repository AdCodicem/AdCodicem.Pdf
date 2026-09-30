# Samples

Each sample is a runnable console project referencing `src/` directly rather than a published package, so
a change that breaks the API breaks the samples in the same build.

| Sample | What it shows |
|---|---|
| [`FirstSteps`](FirstSteps) | The program the tutorial [Open, inspect and validate a PDF](../docs/website/docs/tutorials/first-steps.md) builds; a test holds the tutorial's code and output to it |
| [`ReadDocument`](ReadDocument) | Opening a document, reading its diagnostics, walking its page tree |

```bash
dotnet run --project samples/FirstSteps
dotnet run --project samples/ReadDocument -- tests/corpus/documents/damaged/invoice-no-xref.pdf
```

Point `ReadDocument` at a damaged file to see the interesting half of its output: the reader repairs what it can and
tells you exactly what it did.
