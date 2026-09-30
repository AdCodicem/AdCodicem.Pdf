# AdCodicem.Pdf.Arlington

The generator of the object-shape rules' tables
([ADR 44](../../docs/adr/0044-object-shape-rules-generated-from-the-arlington-model.md)): it reads the
[Arlington PDF Model](https://github.com/pdf-association/arlington-pdf-model), vendored in `model/`, applies
`overrides.tsv`, and writes `src/AdCodicem.Pdf/Validation/Arlington/ArlingtonModel.g.cs`, which is committed. Not
packed, and never run by the build: `ArlingtonGeneratorTests` regenerates the tables in memory on every run of the unit
suite, and fails unless they are the committed file byte for byte.

```bash
dotnet run --project tools/AdCodicem.Pdf.Arlington -- generate               # after an edit to overrides.tsv
dotnet run --project tools/AdCodicem.Pdf.Arlington -- verify                 # the lock, and the tables against the model
dotnet run --project tools/AdCodicem.Pdf.Arlington -- update --from <clone>  # vendor the commit a clone has checked out
```

| Path | What it is |
|---|---|
| `model/tsv/latest/*.tsv`, `model/LICENSE`, `model/NOTICE.txt` | The model's files at the pinned commit, byte for byte as upstream publishes them (Apache License 2.0) |
| `model/model.lock` | The commit, then each vendored file's SHA-256, in the form `sha256sum -c` reads |
| `overrides.tsv` | The reviewed departures from the model, each with its reason and evidence |

An update to the model is a reviewed change: `update` rewrites the lock and the tables, the corpus tests show every
finding it adds or removes, an override the new commit makes useless must go, and the commit named in the root
`NOTICE` changes with it. What the rules read of the model, the overrides and what is left silent are in
[`docs/website/docs/reference/validation-rules.md`](../../docs/website/docs/reference/validation-rules.md).
