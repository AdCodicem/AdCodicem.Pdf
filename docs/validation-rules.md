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
| `file.root-invalid` | Error | The trailer's `/Root` is missing or does not lead to a document catalog (Table 15), a value that is not a reference included — unless it is a catalog written in the trailer itself, which the reader takes as it is ([#111]). The reader then looks for an object of `/Type /Catalog` among those the index holds — and rebuilds the index only when none is one —: the catalog it finds is its choice, not the file's. Not judged when the trailer could not be read at all, nor when the index was rebuilt as the document opened. | The trailer the chain gave, merged as readers merge it, and `/Root` before the reader looked for a catalog |
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
| `xref.object-stream-circular` | Error | An object stream needs, to be read, an object only reading it can give: its `/Length`, `/Filter`, `/DecodeParms`, `/N` or `/First` names an object it holds — directly, through objects written in the file, or through another object stream whose own keys need it in turn (7.5.7). The reader decodes the stream without that value, as qpdf reports a loop, and cannot vouch for what it decoded ([#51]). One finding per stream; `xref.object-stream-broken` says nothing more of it. | Each object stream's dictionary and the objects its decoding keys name; nothing is decoded |
| `xref.object-stream-broken` | Error | An object stream the index places objects in is not in the index, is not an object stream, or does not decode to the header its `/N` and `/First` describe: every object placed in it is lost. One finding per stream. An object stream whose own entry is broken is `xref.entry-broken`'s. | Each object stream once, without keeping it |
| `xref.offset-imprecise` | Warning | Offsets name the white space before what they designate rather than its first byte (7.5.4, 7.5.5): Microsoft Print to PDF names the line feed before each object, and some tools the one before `xref`. Every reader skips the white space. One finding for the file, with how many offsets and the first of them. | The sections of the chain, and each object's header |
| `xref.object-past-size` | Warning | In-use objects are numbered above the trailer's `/Size`, which Table 15 makes a conforming reader ignore ("shall be ignored and defined to be missing"). The entries, the objects' headers and the references agree against `/Size`, and the reader reads those objects, as qpdf does while warning. One finding for the file, with how many objects and the first of them; `file.size-wrong` then says nothing more of that `/Size`. | The index and the trailer's `/Size` |
| `xref.checked-in-part` | Information | Part of the index was not checked: one of the reader's limits stopped it reading a section, a trailer or an object stream whole, or the chain past `MaxXRefSectionCount` sections — raising the limit lets the rest be checked —, or the document is encrypted and its object streams are readable only once decrypted (M16). | What the reader and the probe of the index recorded |

The entries the `xref.entry-*`, `xref.generation-mismatch`, `xref.object-stream-broken`, `xref.offset-imprecise`
and `xref.object-past-size` rules check are those of the index the file's chain gave, as the reader first read
it — not one it has corrected or rebuilt since, so that what was read before validating changes nothing. When
the chain gave no index, the file rules have said why, and no entry is checked.

### Objects

| Rule | Severity | Meaning | Reads |
|---|---|---|---|
| `object.reference-missing` | Warning | An object reachable from the trailer refers to an object the file does not define — none in its index, or a free entry —, which is null (7.3.10): the reader reads it so, rebuilding nothing, and what the reference was to give is lost. One finding per object that holds such references, naming the first. An object the index holds and the reader cannot produce is `xref.entry-broken`'s or `xref.object-stream-broken`'s, a kid of the page tree `page-tree.kid-invalid`'s, and the trailer's `/Root` `file.root-invalid`'s. | Every object reachable from the trailer, once each |
| `object.endobj-missing` | Warning | An object reachable from the trailer does not end with `endobj` (7.3.10): another token, or the end of the file, follows its value. The reader reads the value as far as it goes, as qpdf does while reporting it. An empty object, `2 0 obj endobj`, has its `endobj`; what follows a stream whose data runs past the 8 KB window the reader first reads through is not seen until [#55]. | What follows each reachable object's value, as the reader read it |
| `object.name-null-character` | Warning | An object reachable from the trailer holds a name with a null character, written `#00`, which a name cannot contain (7.3.5). The reader keeps the name; qpdf refuses it and reads dictionaries without the keys so named. One finding per object, naming the first. | Every name of every reachable object |

The object rules walk what is reachable from the trailer — every dictionary, array and stream dictionary, not the
trailer's `/Prev` or `/XRefStm` —, resolving each object once through the document and reading no stream's data. An
object nothing reachable refers to is not judged by them.

### Page tree

| Rule | Severity | Meaning | Reads |
|---|---|---|---|
| `page-tree.cycle` | Error | A kid names the node listing it, or a node above it (7.7.3): the tree loops, the reader counts no page for the kid, qpdf reports the loop, and what the tree should have listed there is unknown — as with `xref.chain-loop`. | The page tree, walked once |
| `page-tree.node-repeated` | Warning | A kid names a node or a page the tree lists elsewhere, away from its own path. It is counted each time it is listed, as qpdf does. | The page tree |
| `page-tree.kids-missing` | Warning | A node, of `/Type /Pages`, has no `/Kids` array (Table 29): it lists no page. | The page tree |
| `page-tree.kid-invalid` | Warning | A kid is null, names an object the file lacks, names a literal null, is neither a dictionary nor a stream — each takes the place of a page with nothing on it, as qpdf, poppler and PDFium count it —, is a stream, read through its dictionary, or is a dictionary written in the array rather than referred to (Table 29). A kid the index holds and the reader cannot produce is the index's fault, and counts as a page all the same. | The page tree |
| `page-tree.count-mismatch` | Warning | A node has no `/Count`, or one that is not the number of pages below it (Table 29). The reader counts the pages the tree lists and does not trust `/Count`; readers that take the root's for the number of pages disagree. Not judged above a loop, a node without `/Kids` or a kid the reader cannot produce; a `/Count` that is not an integer is a type fault. | The page tree |
| `page-tree.parent-wrong` | Warning | A node or a page has no `/Parent`, or one that is not the node listing it; or the root has one (Tables 29 and 30). The reader, as qpdf, walks the tree through `/Kids` and inherits along that path. | The page tree |
| `page-tree.mediabox-invalid` | Warning | A page has no `/MediaBox`, its own or inherited, or one that is not four numbers enclosing an area (Table 30, 7.9.5): its size is unknown, and qpdf gives it a US Letter sheet's. Any two opposite corners make a rectangle, so a box written from its upper corner is sound. Missing, reported for each page; malformed, where it is written. | The page tree, and each media box |
| `page-tree.resources-missing` | Warning | A page has no `/Resources`, its own or inherited (Table 30), where an empty dictionary says it uses none. | The page tree |
| `page-tree.page-orphaned` | Information | An object of `/Type /Page`, with a `/Parent` or a `/Contents`, that the page tree does not list: no reader shows it. Nothing forbids one — an edit leaves them —; a marked-content property list typed `/Page`, as DocuSign writes, is not a page. Judged only where the file's own index is sound and whole and the tree was read whole, since elsewhere loading an object may bring back what an update deleted. | Every object the file's index holds in use, once each, without its content |

The page tree is walked once from the catalog's `/Pages`, through `/Kids`, as qpdf walks it: a node is a dictionary
with a `/Kids` array or of `/Type /Pages`, any other dictionary the tree lists is a page, and pages are counted as the
tree lists them — a kid that is null, or names an object the file lacks, counts as a page with nothing on it, a node
or page listed twice counts twice, a kid that loops back counts nothing. `/MediaBox` and `/Resources` are inherited
along that path; one that names an object the file lacks counts as given, the reference being
`object.reference-missing`'s. A finding about a page gives its index in that order, as `PdfValidationLocation.PageIndex`.
A `/Pages` that names nothing is `object.reference-missing`'s, and leaves no tree to judge.

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
- **A catalog written in the trailer** — `/Root` a direct dictionary that is a catalog, where Table 15 asks for an
  indirect reference. The reader takes it as it is, so `file.root-invalid`, whose error is the reader choosing a
  catalog the file does not name, does not apply; a warning of its own is left for later ([#111]).
- **A gap in the numbering** — an object number below `/Size` with no entry — matters only when something refers
  to it, which `object.reference-missing` reports.
- **An object nothing reachable refers to** — an incremental update's leftovers, an old `/Info`, an unused font — is
  common and legal; only a page left out of the tree is reported, as `page-tree.page-orphaned`.
- **A page-like object reachable only through a destination or an annotation, with neither `/Parent` nor
  `/Contents`** — pikepdf's `handwritten-cyclic-toc.pdf` holds one —: not a page of the tree, and too bare to be told
  from a stray dictionary typed `/Page`.
- **A reference written `0 0 R`** — to object 0, never in use — is read as two integers and a stray keyword, which
  the reader reports as a syntax diagnostic; `object.reference-missing` will report it once the reader reads it as a
  reference ([#117]).
- **An array or dictionary still open at the end of the data** — the reader takes it as it stands, and only
  `object.endobj-missing` reports an object that runs to the end of the file; a finding of its own waits on the
  reader ([#119]).

## Families

The structural profile's rules take one of these families, as M02 adds them slice by slice: `file`, `xref`,
`object`, `page-tree`, `stream`, `font`, `resource`, `annotation`, `metadata`, `security`. The PDF/A and
PDF/UA profiles of the `AdCodicem.Pdf.Conformance` package (M20) take families of their own.

[#51]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/51
[#55]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/55
[#107]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/107
[#108]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/108
[#111]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/111
[#117]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/117
[#119]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/119
