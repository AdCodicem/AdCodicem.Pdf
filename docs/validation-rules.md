# Validation rules

Every rule the validator runs, by its identifier. The identifiers are public API: callers filter on them and
repair keys its remedies off them, so one is renamed only by a stable release that says so — and never by a
preview's whim once a stable release carries it ([ADR 36](adr/0036-validation-lives-in-the-core-conformance-in-a-satellite.md)).

An identifier is `family.name`, both in lowercase kebab case. One identifier names one rule, which always
reports at the same severity, and no identifier is ever one of the reader's diagnostic codes: a diagnostic
says what the reader did to read the file, a finding what is wrong with the file.

| Severity | Means |
|---|---|
| `Error` | The document is broken: readers will disagree about what it contains. |
| `Warning` | The document works, but it is wrong: readers accept it, and a stricter one may not. |
| `Information` | Worth knowing; nothing is wrong. |

When in doubt between `Error` and `Warning`, a rule says `Warning`: a validator that calls sound files broken
teaches its users to ignore it.

## The structural profile

`ValidationProfile.Structural`, named `structural`, version 1: the rules that hold for any PDF, whatever it
claims to conform to. It is the default profile of `PdfValidator`.

| Rule | Severity | Meaning | Reads |
|---|---|---|---|
| `file.eof-missing` | Warning | No `%%EOF` marker in the last 1,024 bytes of the file. ISO 32000 wants it alone on the file's last line; readers look for it that far from the end, so bytes a transfer appended after it do not matter. Without it, the file was most likely cut short, or had something appended: the reader may still find every object, which is why this is not an error. A second marker, as each incremental update writes, is legal. The finding is located at the end of the file. | The file's last 1,024 bytes |

## Families

The structural profile's rules take one of these families, as M2 adds them slice by slice: `file`, `xref`,
`object`, `page-tree`, `stream`, `font`, `resource`, `annotation`, `metadata`, `security`. The PDF/A and
PDF/UA profiles of the `AdCodicem.Pdf.Conformance` package (M12) take families of their own.
