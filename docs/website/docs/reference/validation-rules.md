---
title: Validation rules
description: Every rule the validator runs, by its identifier, with its severity, what it reports and what it checks.
---

# Validation rules

Every rule the validator runs, by its identifier. The identifiers are public API: callers filter on them and
repair keys its remedies off them, so one is renamed only by a stable release that says so — and never by a
preview's whim once a stable release carries it ([ADR 36](/project/adr/validation-lives-in-the-core-conformance-in-a-satellite)).

An identifier is `family.name`, both in lowercase kebab case. One identifier names one rule, which always
reports at the same severity, and no identifier is ever one of the reader's diagnostic codes: a diagnostic
says what the reader did to read the file, a finding what is wrong with the file.

| Severity | Means |
|---|---|
| `Error` | The document is broken: the reader cannot vouch that it reads what was written — it rebuilt the index by scanning the file, lost part of what the file holds, or chose what the file does not designate. |
| `Warning` | The document breaks the specification, and is read all the same as it was evidently meant, the rest of the file confirming that reading: the reader reads it, and a stricter reader may not. |
| `Information` | Worth knowing; nothing is wrong, or something could not be checked. |

The line between a warning and an error is whether the file reads as it was written
([ADR 45](/project/adr/a-findings-severity-says-whether-the-file-reads-as-written)), checked against what the
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
| `object.endobj-missing` | Warning | An object reachable from the trailer does not end with `endobj` (7.3.10): another token, or the end of the file, follows its value. The reader reads the value as far as it goes, as qpdf does while reporting it. An empty object, `2 0 obj endobj`, has its `endobj`. After a stream whose data runs past the 8 KB window the reader first reads through, the file is asked for the bytes after its `endstream` since [#55]. More than a few dozen bytes of white space or comment before the next token leave what follows unseen, and a stream whose declared length the reader kept for want of an `endstream`, or whose data an `endobj` follows without one, leaves where the object ends unknown: neither is judged. | What follows each reachable object's value, as the reader read it |
| `object.name-null-character` | Warning | An object reachable from the trailer holds a name with a null character, written `#00`, which a name cannot contain (7.3.5). The reader keeps the name; qpdf refuses it and reads dictionaries without the keys so named. One finding per object, naming the first. | Every name of every reachable object |
| `object.key-missing` | Warning | An object reachable from the trailer lacks a key the [Arlington PDF Model](#object-shapes-from-the-arlington-pdf-model) requires of its type — absent, or `null`, which in a dictionary is the same (7.3.7): a key the model requires once the version the file declares has it, or one it requires in some versions only, such as a form XObject's `/Name` in PDF 1.0. An inheritable key is looked for up the `/Parent` chain, and a field's `/DA` in the interactive form's too; a key only an extension of ISO 32000 defines is never required. An array shorter than the elements it requires lacks each required element it does not hold, one finding per element. [One finding per type and key](#object-shapes-from-the-arlington-pdf-model), at the first object, with how many lack it. | Every object reachable from the trailer, typed along the model's links, once each |
| `object.value-type-wrong` | Warning | A value is of a type the Arlington PDF Model does not allow for its key, judged by the value's class: a real, even `400.0`, where an integer belongs; a string where a name does; a stream where a dictionary does, or the reverse, which is reported on the object that holds it and not walked into. A `null` written in an array is judged like any other value; one reached through a reference is not judged. One finding per type and key. | As `object.key-missing` |
| `object.type-value-wrong` | Warning | A `/Type` or `/Subtype` is a name the Arlington PDF Model does not list for the object's type: a root of `/Type /Pagez`, a page of `/Type /Font`, an action of `/Type /A`. A row whose values the model leaves open is not judged. One finding per type and key. | As `object.key-missing` |
| `object.key-deprecated` | Information | A key the Arlington PDF Model deprecates in the version the file declares, or in an earlier one: ISO 32000-2's deprecated keys, which a file may still hold and conform. Not judged when the file declares no version. One finding per type and key. | As `object.key-missing` |

The object rules walk what is reachable from the trailer — every dictionary, array and stream dictionary, not the
trailer's `/Prev` or `/XRefStm` —, resolving each object once through the document and reading no stream's data. An
object nothing reachable refers to is not judged by them.

### Page tree

| Rule | Severity | Meaning | Reads |
|---|---|---|---|
| `page-tree.cycle` | Error | A kid names the node listing it, or a node above it (7.7.3): the tree loops, the reader counts no page for the kid, qpdf reports the loop, and what the tree should have listed there is unknown — as with `xref.chain-loop`. | The page tree, walked once |
| `page-tree.node-repeated` | Warning | A kid names a node or a page the tree lists elsewhere, away from its own path. It is counted each time it is listed, as qpdf does. | The page tree |
| `page-tree.kids-missing` | Warning | A node, of `/Type /Pages`, has no `/Kids` array (Table 29) — none, `null`, or something else, written so or through a reference —: it lists no page. A `/Kids` that names an object the file lacks is `object.reference-missing`'s, one the reader cannot produce the index's. | The page tree |
| `page-tree.kid-invalid` | Warning | A kid is null, names an object the file lacks, names a literal null, is neither a dictionary nor a stream — each takes the place of a page with nothing on it, as qpdf, poppler and PDFium count it —, is a stream, read through its dictionary, or is a dictionary written in the array rather than referred to (Table 29). A kid the index holds and the reader cannot produce is the index's fault, and counts as a page all the same. | The page tree |
| `page-tree.count-mismatch` | Warning | A node has no `/Count` — none, or `null` —, or one that is not the number of pages below it (Table 29). The reader counts the pages the tree lists and does not trust `/Count`; readers that take the root's for the number of pages disagree. A missing `/Count` is reported on every node; a wrong one is not judged above a loop, a node without `/Kids` or a kid the reader cannot produce; a `/Count` that is not an integer is a type fault. | The page tree |
| `page-tree.parent-wrong` | Warning | A node or a page has no `/Parent`, or one that is not the node listing it; or the root has one (Tables 29 and 30). The reader, as qpdf, walks the tree through `/Kids` and inherits along that path. | The page tree |
| `page-tree.mediabox-invalid` | Warning | A page has no `/MediaBox`, its own or inherited, or one that is not four numbers enclosing an area (Table 30, 7.9.5): its size is unknown, and qpdf gives it a US Letter sheet's. Any two opposite corners make a rectangle, so a box written from its upper corner is sound. Missing, reported for each page; malformed, where it is written. | The page tree, and each media box |
| `page-tree.resources-missing` | Warning | A page has no `/Resources`, its own or inherited (Table 30), where an empty dictionary says it uses none. | The page tree |
| `page-tree.page-orphaned` | Information | An object of `/Type /Page`, with a `/Parent` or a `/Contents`, that the page tree does not list: no reader shows it. Nothing forbids one — an edit leaves them —; a marked-content property list typed `/Page`, as DocuSign writes, is not a page. Judged only where the file's own index is sound and whole and the tree was read whole, since elsewhere loading an object may bring back what an update deleted. | Every object the file's index holds in use, once each, without its content |

The page tree is walked once from the catalog's `/Pages`, through `/Kids`, as qpdf walks it: a node is a dictionary
with a `/Kids` array or of `/Type /Pages`, any other dictionary the tree lists is a page, and pages are counted as the
tree lists them — a kid that is null, or names an object the file lacks, counts as a page with nothing on it, a node
or page listed twice counts twice, a kid that loops back counts nothing. `/MediaBox` and `/Resources` are inherited
along that path; one that names an object the file lacks counts as given, the reference being
`object.reference-missing`'s. A `/Kids`, `/Count`, `/MediaBox` or `/Resources` that is `null`, written so or an object
the file defines as `null`, is absent (7.3.7). A finding about a page gives its index in that order, as `PdfValidationLocation.PageIndex`.
A `/Pages` that names nothing is `object.reference-missing`'s, and leaves no tree to judge.

### Object shapes, from the Arlington PDF Model

`object.key-missing`, `object.value-type-wrong`, `object.type-value-wrong` and `object.key-deprecated` are generated,
not written by hand ([ADR 44](/project/adr/object-shape-rules-generated-from-the-arlington-model)). The
[Arlington PDF Model](https://github.com/pdf-association/arlington-pdf-model) is the PDF Association's machine-readable
description of every object ISO 32000-2 defines: each key, its types, whether it is required, the versions that
introduce and deprecate it, and the objects its value may be. Its 613 objects and 3,983 rows, at commit
`c48b363e9b78902deea03e958693c09339248a3a`, are vendored under `tools/AdCodicem.Pdf.Arlington/model/` byte for byte as
upstream publishes them, a lock holding each file's SHA-256. The generator, `tools/AdCodicem.Pdf.Arlington`, reduces
them to static tables compiled into the core — `src/AdCodicem.Pdf/Validation/Arlington/ArlingtonModel.g.cs`, one line
per row of the model —, applying the overrides below; the validator reads no file at run time. The model is Copyright
2020 PDF Association, Inc., under the Apache License 2.0, and `NOTICE`, in the repository and in the package, carries
its notice.

**The version.** The version a file declares is its header's, or its catalog's `/Version` when that is later (7.2.2,
7.7.2), never rounded. A file whose header names none of the nine versions — 1.0 to 1.7, or 2.0 — declares none for
these rules, whatever its catalog says, since a catalog's `/Version` only says how much later than the header the file
is: what depends on the version, a key required in some versions only or a deprecation, is not judged, and the rest
takes the keys of PDF 2.0.

**How an object gets its type.** The walk starts at the trailer, which it does not check — its entries are the file
rules', and its `/ID` the metadata rules' (slice 5, [#61]) —, following only what leads into the document: `/Root`,
`/Info` and `/Encrypt`, not the arrays a trailer or a cross-reference stream holds of its own. It goes breadth-first along the model's links, with a queue rather than recursion: the catalog is a
`Catalog`, its `/Pages` a `PageTreeNodeRoot`, a kid a `PageTreeNode` or a `PageObject`. Where the model links a value to
several objects — an annotation to one of thirty kinds —, the candidates of the value's kind stay (the arrays for an
array, the others for a dictionary or a stream), and the key or first element the generator found to tell them apart by
a fixed value decides where it can: `/Subtype /Link` makes an `AnnotLink`. Otherwise each candidate is scored by the
fixed values the object matches and the required keys it lacks, a discriminator — `/Type`, `/Subtype`, `/S`,
`/FunctionType`, `/ShadingType`, `/PatternType`, `/HalftoneType`, `/FT`, `/TransformMethod`, `/CFM`, an array's first
element — weighing most, and **a tie leaves the object unchecked** rather than judged against a guess.

- An indirect object is typed by the first context that types it, and checked once; a later context that disagrees is
  not heard, and one a tie left untyped may still be typed by a later one.
- A key that points back up the graph — any `/Parent`, the `/P` of a structure element or of an annotation, an outline
  item's `/Prev` — is checked but not followed: the object it names is typed from above, where a damaged file cannot
  mislead the walk.
- Name and number trees are walked through `/Kids`, `/Names` and `/Nums`, and their values typed as the model's row
  says: a node or an array that is an object of its own is expanded once, however many nodes name it, and one written
  inside another lies in an object expanded once, so that loops and shared arrays cost no more than the file holds.
- An array's fixed elements, its `*` elements and its repeating groups are matched in order; an optional member of a
  group that does not match is skipped, rather than shifting every element after it.
- A value that names an object the file lacks, or one the reader could not produce — held in an object stream it could
  not decode, or could not decrypt before M16 —, is present, and is not checked: that is `object.reference-missing`'s
  or the cross-reference rules' business. An object a reader limit cut — at `MaxObjectLength`, or a member of an object
  stream whose data `MaxDecodedStreamLength` cut — is not judged either, nor what is written inside it: what it lacks
  may lie past the limit. An ancestor so cut gives any key an object would inherit from it.
- Keys are taken in ordinal order and elements in order, so that two validations give the same findings in the same
  order.

**What each type of the model accepts**, judged by the value's class, never by a conversion — a real is not an
integer, even `400.0`, and a stream is not a dictionary, though it has one:

| The model's type | Accepts |
|---|---|
| `integer`, `bitmask` | An integer |
| `number` | An integer or a real |
| `boolean`, `name` | A boolean, a name |
| `string`, `string-byte`, `string-ascii`, `string-text`, `date` | A string |
| `array`, `rectangle`, `matrix` | An array |
| `dictionary`, `name-tree`, `number-tree` | A dictionary that is not a stream |
| `stream` | A stream |
| `null` | A `null` written in an array: in a dictionary, `null` is absence (7.3.7) |

**One finding per type and key.** A generated rule reports once for each row of the model it finds broken: at the
first object in the walk's order — with the page's index when that object is a page —, saying how many objects break
it, however many of their keys or elements do. A row that stands for every key a type does not name, as the model
writes `*`, gathers them all into one finding, which names the first. The message names the model's type and the key, and the value's path inside the object that
holds it: "Object 12 0, a PageObject in the Arlington model, has /Rotate as a real number, where the model wants an
integer." — "A StructElem lacks /P, which the Arlington model requires; 564 objects do, the first object 45 0."

**Updating the model** is a reviewed change.
`dotnet run --project tools/AdCodicem.Pdf.Arlington -- update --from <clone>` vendors it at the commit a clone of its
repository has checked out, rewrites the lock and regenerates the tables; `generate` regenerates them after an edit to
`overrides.tsv`; `verify` checks the lock and the tables. `ArlingtonGeneratorTests` does the same on every run of the
unit suite: it regenerates the tables in memory from the vendored files and fails unless they are the committed file
byte for byte. The diff then shows the model's rows and the tables' lines that change, the corpus tests every finding
the update adds or removes, and `NOTICE`, which names the commit, changes with it.

#### Overrides

Where the model asks more than ISO 32000-1 — the version nearly every file declares —, the row is overridden by name in
`tools/AdCodicem.Pdf.Arlington/overrides.tsv`, with the words of ISO 32000-1 that justify it and the corpus documents
that showed it. A genuine ISO 32000-1 violation is not overridden: it stays a finding, declared in the corpus
manifest. The generator refuses an override that no longer changes the pinned model, so that one upstream has adopted
goes.

| Row of the model | What changes | Rule | Why: ISO 32000-1 | Seen in |
|---|---|---|---|---|
| `XObjectFormType1/Resources` | Required in PDF 2.0 only, where the model requires it from 1.2 | `object.key-missing` | 8.10.2, Table 95: "(Optional but strongly recommended; PDF 1.2)" | 9 clean documents, all signature appearances: cairo, Excel 365, FOP 2.2, InDesign with iTextSharp, Word 2010, Antenna House, Foxit ×2, a hand-written file |
| `Resource/Encoding` | Not deprecated, where the model deprecates it in 1.2 | `object.key-deprecated` | Table 33 has no `Encoding`: the row is PDF 1.0's named-encoding resource, whose deprecation ISO 32000-1 does not state; Acrobat's AcroForm `/DR` reuses the name for its PDFDocEncoding dictionary | 33 clean documents and 8 others, every one in an AcroForm `/DR` |
| `FontDescriptor*/FontWeight`, the five descriptors | A number, where the model wants an integer | `object.value-type-wrong` | 9.8.1, Table 122: "FontWeight number (Optional; PDF 1.5; …)" | 2 clean documents: Distiller 10 and PDFBox 3.0.6 write `400.0` |
| `SignatureBuildDataDict`, `…AppDict`, `…SigQDict` `/V` | Not deprecated, where the model deprecates it in 1.7 | `object.key-deprecated` | 12.8.1, Table 252 leaves `Prop_Build`'s contents to Adobe's PDF Signature Build Dictionary Specification, and deprecates nothing in it | 6 clean documents: PDFMaker 11, LiveCycle ES10 ×2, Designer 6.4 and 6.5, InDesign 15.1 with Acrobat |
| `SignatureBuildDataAppDict/REx` | A name or a text string, where the model wants a text string | `object.value-type-wrong` | The same: ISO 32000-1 types nothing in `Prop_Build` | 2 clean documents from govinfo.gov, iText 7.2.3 |
| `Field*/DA`: `FieldBtnCheckbox`, `FieldBtnPush`, `FieldBtnRadio`, `FieldChoice`, `FieldTx` | Also taken from the interactive form's `/DA`, where the model inherits it through `/Parent` only | `object.key-missing` | 12.7.2, Table 218: "A document-wide default value for the DA attribute of variable text fields" | 1 clean document: a PDFMaker 21 CERFA form |
| `ArrayOfOptContentOrders`, element 0 | Optional, where the model requires it | `object.key-missing` | 8.11.4.3, Table 101: "Each nested array may optionally have as its first element a text string"; nothing requires a nested array to hold an element | 1 clean document: Esri ArcSOC 10.8.1 writes an empty sub-array in `/Order` |
| `XObjectFormType1/FormType` and `/Matrix` | Optional in every version, where the model requires them before 1.3 | `object.key-missing` | 8.10.2, Table 95: each "(Optional)", with its default | 2 clean documents declaring PDF 1.2: IRS forms from Distiller 2.0 and 3.0 |
| `XObjectFormType1/Name` | Required in PDF 1.0 only, where the model requires it before 1.3 | `object.key-missing` | 8.10.2, Table 95: "(Required in PDF 1.0; optional otherwise)" | The same two, and an IBM XPP manual whose 21 forms have no `/Name` |
| `OptContentCreatorInfo/SubType` | A name or a text string, where the model wants a name | `object.value-type-wrong` | 8.11.4.4, Table 102: "Additional entries may be included"; `/SubType` is no ISO 32000-1 key but the model's row for a misspelled `/Subtype`, and ISO 32000-1 types no additional entry. The `/Subtype` such a dictionary lacks stays `object.key-missing`'s | 2 clean documents: Esri ArcSOC 10.8.1 and ArcMap 10.2.2 write `/SubType (Layer)` |

Checked against ISO 32000-1 and **not** overridden:

- letting an `/Order` sub-array hold arrays. Table 101 describes the nested arrays as "Arrays of optional content
  groups" whose only other element is a first text label; a sub-array nested in a sub-array is not described, so
  Esri's stays a declared finding;
- letting a Type 3 font's `/Encoding` be a name. Table 112 types it "name or dictionary", but the same row reads
  "(Required) An encoding dictionary whose Differences array shall specify the complete character encoding for this
  font": the AFP Batch Processor's `/WinAnsiEncoding` stays a declared finding.

The maintainer reviewed the last three overrides of the table and these two refusals on 2026-09-29, each against the
text of ISO 32000-1, and confirmed all five.

**Left to a hand-written rule.** One fault, one finding: where a rule written by hand reports a fault, the generated
rules stay silent on it, by a named row of `overrides.tsv` whose reason reads "covered by" that rule. The page tree's
rules judge what their walk entered, so their silences hold there alone: on a page the tree lists, as the model types
it, a node, an array of kids, and a `/Parent` the walk could compare with the node listing the object. A page the tree
does not list — one only a destination names —, or the `/Parent` of a page under a node written in its parent's
`/Kids`, is the generated rules' to judge.

| Row of the model | Generated rules silent | Covered by |
|---|---|---|
| `PageObject/MediaBox` | `object.key-missing`, `object.value-type-wrong` | `page-tree.mediabox-invalid` |
| `PageTreeNode/MediaBox`, `PageTreeNodeRoot/MediaBox` | `object.value-type-wrong` | `page-tree.mediabox-invalid` |
| `PageObject/Resources` | `object.key-missing`; a `/Resources` of the wrong type stays `object.value-type-wrong`'s | `page-tree.resources-missing` |
| `PageObject/Parent` | `object.value-type-wrong`; the model requires `/Parent` through a predicate the rules do not evaluate | `page-tree.parent-wrong` |
| `PageTreeNode/Parent` | `object.key-missing`, `object.value-type-wrong` | `page-tree.parent-wrong` |
| `PageTreeNode/Kids`, `PageTreeNodeRoot/Kids` | `object.key-missing`, `object.value-type-wrong` | `page-tree.kids-missing` |
| `PageTreeNode/Count`, `PageTreeNodeRoot/Count` | `object.key-missing`; a `/Count` that is not an integer stays `object.value-type-wrong`'s | `page-tree.count-mismatch` |
| `ArrayOfPageTreeNodeKids/*` | `object.value-type-wrong` | `page-tree.kid-invalid` |

What no hand-written rule reports stays the generated rules': a catalog without `/Type` or without `/Pages`, a node or
a page without `/Type` or of the wrong one — `/Pagez`, a page typed `/Font`.

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

The object-shape rules read part of the Arlington PDF Model, and leave the rest silent until a rule of its own is
worth its noise:

- **What the model says under a condition** — a key required only when another has some value, a value allowed only
  under some condition, the model's `SpecialCase` column. No predicate is evaluated but those on the version alone: such
  a key is taken as not required, such a value as allowed. A page's `/Parent`, which the model requires so, is
  `page-tree.parent-wrong`'s.
- **Whether a value is direct or indirect** — the model's `IndirectReference` column, such as a structure element's
  `/P`, which ISO 32000-1 wants an indirect reference.
- **What a string holds** — byte, ASCII and text strings and dates are all satisfied by any string; a date's syntax is
  left to the metadata rules (slice 5).
- **A rectangle's or a matrix's length, a bitmask's range** — any array is a rectangle or a matrix, any integer a
  bitmask; `page-tree.mediabox-invalid` judges the one rectangle a page cannot do without.
- **The rules inside name and number trees** — `/Limits`, and the order of the keys. Their values are checked.
- **Values other than `/Type` and `/Subtype`** — a `/PageLayout` or a `/Tabs` outside the names the model lists —, and
  **deprecated values**, where the model deprecates a value rather than a key: only keys are judged deprecated.
- **A key newer than the version the file declares** — a PDF 1.5 key in a file that declares 1.4. On the corpus's
  sound files it is the noisiest of the model's rules, and it would have to know the extensions that bring a key in
  before ISO 32000 does, as PDF/A-3 brings `/AF` into PDF 1.7 files. A newer key still conforms, so the structural
  profile stays silent on it; where a claim bounds the version — PDF/A-1 on PDF 1.4 — it is M20's to judge ([#123],
  moved there on 2026-09-29).
- **An object two contexts type differently** — the first to type it wins, and the second is not heard. iPRES
  `t02-03-007`'s page names the page tree's root as its `/Resources`: the root, typed as the tree's root already, is not
  judged again as a resource dictionary, and a dictionary is what `/Resources` wants.
- **An object whose type a tie leaves open** — it is not checked, rather than judged against a guess: fewer than one in
  a hundred of the objects the corpus's walks meet, half of them destination arrays. A font without `/Subtype`, or
  with one no font has (iPRES `t02-04-01-005`, `-006`), is among them: the subtype is what tells a font's kinds apart.
- **The trailer, a cross-reference stream's dictionary and the linearization dictionary** — the file family's, and
  linearization's ([#108]); the trailer's `/ID`, the metadata rules' (slice 5, [#61]).
- **What the reader could not read** — the objects in an encrypted file's object streams, unreadable before M16, which
  `xref.checked-in-part` already says; an object a reader limit cut.

## Families

The structural profile's rules take one of these families, as M02 adds them slice by slice: `file`, `xref`,
`object`, `page-tree`, `stream`, `font`, `resource`, `annotation`, `metadata`, `security`. The PDF/A and
PDF/UA profiles of the `AdCodicem.Pdf.Conformance` package (M20) take families of their own.

[#51]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/51
[#55]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/55
[#61]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/61
[#107]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/107
[#108]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/108
[#111]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/111
[#117]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/117
[#119]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/119
[#123]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/123
