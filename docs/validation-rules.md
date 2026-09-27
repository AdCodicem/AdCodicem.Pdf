# Validation rules

Every rule the validator runs, by its identifier. The identifiers are public API: callers filter on them and
repair keys its remedies off them, so one is renamed only by a stable release that says so — and never by a
preview's whim once a stable release carries it ([ADR 36](adr/0036-validation-lives-in-the-core-conformance-in-a-satellite.md)).

An identifier is `family.name`, both in lowercase kebab case. One identifier names one rule, which always
reports at the same severity, and no identifier is ever one of the reader's diagnostic codes: a diagnostic
says what the reader did to read the file, a finding what is wrong with the file.

| Severity | Means |
|---|---|
| `Error` | The document is broken: the reader cannot vouch that it reads what was written — it rebuilt the index by scanning the file, lost part of what the file holds, or chose what the file does not designate. |
| `Warning` | The document breaks the specification, and is read all the same as it was evidently meant, the rest of the file confirming that reading: the reader reads it, and a stricter reader may not. |
| `Information` | Worth knowing; nothing is wrong, or something could not be checked. |

The line between a warning and an error is whether the file reads as it was written
([ADR 45](adr/0045-a-findings-severity-says-whether-the-file-reads-as-written.md)), checked against what the
reader did rather than argued about readers the project does not run. When in doubt, a rule says `Warning`: a
validator that calls sound files broken teaches its users to ignore it.

## The structural profile

`ValidationProfile.Structural`, named `structural`, version 1: the rules that hold for any PDF, whatever it
claims to conform to. It is the default profile of `PdfValidator`, and runs its rules in the order below.

References are to ISO 32000-1:2008.

### File

| Rule | Severity | Meaning | Reads |
|---|---|---|---|
| `file.header-missing` | Warning | No `%PDF-` header in the first 1,024 bytes of the file (7.5.2). Acrobat refuses such a file; the reader, as qpdf, reads it without one. Located at the file's start, or where a header lies further in. | Where the reader found the header, in the first 4,096 bytes |
| `file.header-offset` | Warning | The header does not start at the file's first byte: a transfer prepended something, and every offset of the file is counted from the header rather than from the file's start. The reader counts from the header, as other readers do. | The header's position |
| `file.header-version-invalid` | Warning | The header names no version of PDF — 1.0 to 1.7, or 2.0 —, or none at all. Readers open the file whatever version it names. | What follows `%PDF-` |
| `file.eof-missing` | Warning | No `%%EOF` marker in the last 1,024 bytes of the file. ISO 32000 wants it alone on the file's last line; readers look for it that far from the end, so bytes a transfer appended after it do not matter. Without it, the file was most likely cut short, or had something appended: the reader may still find every object, which is why this is not an error. A second marker, as each incremental update writes, is legal. The finding is located at the end of the file. | The file's last 1,024 bytes |
| `file.startxref-missing` | Error | No `startxref` followed by an offset in the file's last 4,096 bytes (7.5.5): the reader rebuilds the index by scanning, and cannot vouch that it reads what was written — which copy of an object an update left wins, whether a deleted object comes back. | The file's tail, as the reader read it |
| `file.startxref-wrong` | Error | The offset `startxref` gives holds no cross-reference section: the index is out of reach, and is rebuilt by scanning. White space before the section is `xref.offset-imprecise`'s; a section that is there and cannot be read is `xref.section-malformed`'s. | The first section of the chain |
| `file.trailer-missing` | Error | A cross-reference table's rows are not followed by the `trailer` keyword: they run into a dictionary, or the file ends. qpdf, as the reader, then rebuilds the index. | Each classic table of the chain |
| `file.trailer-malformed` | Error | A trailer is not a well-formed dictionary: the `trailer` keyword is not followed by one, or the reader read it despite syntax errors — a key that is not a name, a dictionary left unclosed —, so an entry may have been lost with the syntax. A cross-reference stream's dictionary is held to the same. | Each trailer of the chain, as the reader parsed it |
| `file.root-invalid` | Error | The trailer's `/Root` is missing, is not a reference, or does not lead to a document catalog (Table 15). The reader then looks for an object of `/Type /Catalog` among those the index holds — and rebuilds the index only when none is one —: the catalog it finds is its choice, not the file's. Not judged when the trailer could not be read at all, nor when the index was rebuilt as the document opened. | The trailer the chain gave, merged as readers merge it, and `/Root` before the reader looked for a catalog |
| `file.size-wrong` | Warning | A section's `/Size` is missing, or is not one more than the highest object number it and the sections it updates use (Tables 15 and 17, 7.5.6, 7.5.8.4). A hybrid file's cross-reference stream meets one of Table 17's two readings or the other — one more than the highest number it and the sections it updates use, or the `/Size` of the table that names it —, and either is accepted. A `/Size` that leaves in-use objects numbered above it is `xref.object-past-size`'s. Not judged when the chain lost a section, or one of the reader's limits cut it. | Each section's trailer, and the highest number each section indexes |

### Cross-references

| Rule | Severity | Meaning | Reads |
|---|---|---|---|
| `xref.section-malformed` | Error | A cross-reference section the chain names is there — an `xref` keyword, or a cross-reference stream — and cannot be read, or holds fewer rows than it declares: a subsection header that is not two integers, a row that is not an offset, a generation and `n` or `f`, a `/W` that is not three field widths. The objects it leaves out are found, if at all, by scanning the file. A section one of the reader's limits cut is not malformed (ADR 34). | Each section of the chain |
| `xref.section-not-found` | Error | A section a `/Prev` or an `/XRefStm` names is neither there nor within 512 bytes of it, or what names it is not an offset — tiff2pdf writes `/Prev 576066 0 R`. The objects only it indexes are missing from the index. The section `startxref` names is `file.startxref-wrong`'s. | Each section of the chain |
| `xref.section-shifted` | Warning | A section a `/Prev` or an `/XRefStm` names starts a few bytes from where it is named, and the reader found it there. | Each section of the chain |
| `xref.chain-loop` | Error | The chain names a section it has already read: the reader stops there, and the section the chain should have gone on to is unknown, what only it indexed found, if at all, by rebuilding the index — as for `xref.section-not-found`. | The chain |
| `xref.entry-broken` | Error | An in-use entry places its object neither at its offset nor within 512 bytes of it, or in an object stream whose header does not list it. One finding per entry, located at the object and the offset the entry gives. | Each object's header, a few dozen bytes; each object stream's header |
| `xref.entry-shifted` | Warning | An in-use entry places its object a few bytes from where it is — the object's own header confirming it —, or at another index of its object stream than the one its header gives. One finding per entry. | As `xref.entry-broken` |
| `xref.generation-mismatch` | Warning | An in-use entry gives a generation other than the one its object is written with. The references and the object's header agree against the entry, and the reader reads the object; qpdf reads a reference to it as a reference to nothing. | Each object's header |
| `xref.object-stream-broken` | Error | An object stream the index places objects in is not in the index, is not an object stream, or does not decode to the header its `/N` and `/First` describe: every object placed in it is lost. One finding per stream. An object stream whose own entry is broken is `xref.entry-broken`'s. | Each object stream once, without keeping it |
| `xref.offset-imprecise` | Warning | Offsets name the white space before what they designate rather than its first byte (7.5.4, 7.5.5): Microsoft Print to PDF names the line feed before each object, and some tools the one before `xref`. Every reader skips the white space. One finding for the file, with how many offsets and the first of them. | The sections of the chain, and each object's header |
| `xref.object-past-size` | Warning | In-use objects are numbered above the trailer's `/Size`, which Table 15 makes a conforming reader ignore ("shall be ignored and defined to be missing"). The entries, the objects' headers and the references agree against `/Size`, and the reader reads those objects, as qpdf does while warning. One finding for the file, with how many objects and the first of them; `file.size-wrong` then says nothing more of that `/Size`. | The index and the trailer's `/Size` |
| `xref.checked-in-part` | Information | Part of the index was not checked: one of the reader's limits stopped it reading a section, a trailer or an object stream whole, or the chain past `MaxXRefSectionCount` sections — raising the limit lets the rest be checked —, or the document is encrypted and its object streams are readable only once decrypted (M16). | What the reader and the probe of the index recorded |

The entries the `xref.entry-*`, `xref.generation-mismatch`, `xref.object-stream-broken`, `xref.offset-imprecise`
and `xref.object-past-size` rules check are those of the index the file's chain gave, as the reader first read
it — not one it has corrected or rebuilt since, so that what was read before validating changes nothing. When
the chain gave no index, the file rules have said why, and no entry is checked.

### Silent on purpose

Some faults the structural profile does not report, each for a reason:

- **A classic table row that is not twenty bytes long** — an offset one digit short (iPRES `t03-008`), a single
  end-of-line byte. The reader reads rows as tokens, as pdf.js does; qpdf accepts them with a warning. A rule for
  the fixed row width is left for later ([#107]).
- **An update's trailer without `/Root`** where the file is linearized: ISO 32000-1 (F.3.11) makes the main
  cross-reference table's trailer hold `/Size` alone. That 40 of the corpus's linearized files also put `/ID` there,
  which F.3.11 forbids, is the business of a linearization family, not of this profile ([#108]).
- **The linearization dictionary and hint tables** — stale hint tables after an incremental update are common and
  harmless: a linearization family, later ([#108]).
- **A gap in the numbering** — an object number below `/Size` with no entry — matters only when something refers
  to it, which the object-graph rules report.

## Families

The structural profile's rules take one of these families, as M02 adds them slice by slice: `file`, `xref`,
`object`, `page-tree`, `stream`, `font`, `resource`, `annotation`, `metadata`, `security`. The PDF/A and
PDF/UA profiles of the `AdCodicem.Pdf.Conformance` package (M20) take families of their own.

[#107]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/107
[#108]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/108
