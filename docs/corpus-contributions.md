# Contributing documents to the corpus

What to hand over, in what shape, and where to put it. The policy behind all this — why the corpus exists
and what closing a milestone requires of it — is in `docs/corpus.md`.

## Why your documents and not more of ours

The corpus covers four producers we can drive in a container — Chromium's Skia backend, LibreOffice,
ReportLab and qpdf — three office writers driven on Windows — Word's Save as PDF, the Microsoft Print to
PDF driver and the PDF24 printer — and 146 third-party files found in public sources under attribution-only
licences: conformance fixtures from veraPDF and BFO, government documents from five countries and the EU,
specimen invoices from ERP and invoicing tools, regression files from other PDF libraries, and the Open
Preservation Foundation's own test files. Another 242 that we may use but not redistribute are fetched on
demand and tested every night — 88 of them the iPRES 2017 hand-built set, copied out of its authors' archive.
`docs/corpus-sources.md` says where each came from, which other libraries' corpora were examined, and why
most of them could not be used.

What public sources cannot give us is what arrives in a real inbox: a bank statement out of a Java stack,
a supplier's Factur-X straight from its ERP with real data, a contract signed through Yousign or Adobe Sign,
a scan as the office copier wrote it, or a file that broke something last week. Those almost always carry
someone's address or account details, so they cannot be found — only contributed, and anonymised. Each one
that enters the corpus stays there, checked on every commit, for the life of the project.

The first three third-party files we added found a real defect within a minute — a validly compressed
empty stream being read as a decoding failure. The search that brought the next seventy-six found a test
that could not read a page count qpdf printed with a warning, and a reader that calls a sound stream
truncated when its end falls just past an 8 KB window. The pass after it, eighty-two more, found the same
window cutting objects longer than 8 KB — both since fixed —, and each cross-reference section read
through a window of up to 64 KB, whatever its size. The fourth, the Open Preservation Foundation's format-corpus, found a `/Prev` a
few bytes off that makes the reader drop a whole cross-reference section without a word. That is the
return on this.

## What is wanted

Priority **1** blocks a milestone, **2** materially improves one, **3** is opportunistic.

| # | Document | Why it matters | Unblocks | Priority |
|---|----------|----------------|----------|----------|
| W01 | **Microsoft Word → PDF**, both variants: "Save as PDF" *and* "Microsoft Print to PDF" | Two different writers, both ubiquitous; the print driver in particular produces structures nothing else does | M1, M2 | 1 |
| W02 | **Adobe** output: Acrobat, Distiller or an InDesign export | The reference implementation's own conventions, including its take on object streams and metadata | M1, M2 | 1 |
| W03 | **A scan from your office copier** (Xerox, Canon, Ricoh, Konica…), ideally one with an OCR text layer and one without | Image-only pages, CCITT and JPEG encodings, and the invisible-text layer OCR adds — extraction must tell them apart | M2, M15 | 1 |
| W04 | **An invoice or statement received from a supplier or bank** | These come out of Java stacks (iText, JasperReports, SAP, Crystal) we cannot run here, and they are the highest-volume documents the library will read | M1, M2, M15 | 1 |
| W05 | **A PDF signed electronically** (Yousign, DocuSign, Universign, a qualified provider…) | The only way to prove that conservative repair and incremental update leave a signature intact | M3, M5 | 1 |
| W06 | **Anything that broke one of your tools** — failed to open, opened wrong, printed wrong | Impossible to synthesise faithfully, and the most valuable file in any corpus | M1, M2, M5 | 1 |
| W07 | **A real Factur-X or ZUGFeRD invoice** from a supplier's ERP | Real ones differ from published samples in the details that matter: attachment relationship, XMP extension schema, PDF/A-3 claim | M14 | 2 |
| W08 | **An interactive form you actually use** — an administrative form, a Cerfa, an internal one | Field appearances, calculated fields, and the XFA forms that Adobe's tooling still emits | M16 | 2 |
| W09 | **A document in a non-Latin script**: Arabic or Hebrew (right to left), Greek, Cyrillic, or CJK | Shaping, bidirectional text and CID font handling cannot be judged on French alone | M8, M15 | 2 |
| W10 | **An old archive document**, PDF 1.2 to 1.4, ideally pre-2005 | Encodings, font formats and structures that no current producer emits but that archives are full of | M1, M2 | 2 |
| W11 | **A very large document**: a heavy scan, a catalogue, a plan | Memory behaviour is a promise, and promises need a document that would break a careless implementation | M23 | 3 |
| W12 | **A PDF/A produced by someone else's tooling**, with its validation report if you have one | An independent opinion on conformance, to check ours against | M20 | 3 |

Nothing here needs to be pretty, recent, or a good example. A file is interesting because of how it was
made, not because of what it says.

### What is already in, and what is still missing

After three passes over public sources on 2026-09-24, and a fourth over the Open Preservation Foundation's
format-corpus on 2026-09-25 (`docs/corpus-sources.md`), the corpus holds something
for every line, and for most of them several producers. The third pass followed the leads held back by
their licence, so most of what it found is remote: fetched on demand, never committed (ADR 32). What is
still wanted is what only a contribution can bring: documents that went through a real inbox, and that we
may commit.

| # | In the corpus | Still wanted |
|---|---------------|--------------|
| W01 | Word 2010, 2019 and Microsoft 365 Save as PDF; Word for Mac through Quartz; Print to PDF from Word and from Excel, untouched; our own invoice through Save as PDF, the print driver and PDF24; remote: Word 2010 and 2019 pages signed in PAdES, a Word for Microsoft 365 form finished in Acrobat, an Excel for Office 365 sheet under 24 signatures, a Print to PDF poster that an Adobe tool later updated; the OPF's Pages '09 through Quartz | — |
| W02 | PDFMaker 5, 9.1, 10, 21 and 25 for Word; Distiller 2 to 10; InDesign; PageMaker; Illustrator; LiveCycle Designer; Acrobat Pro DC and Reader X re-saves; usage rights; remote: PDFMaker 8.1, 11 and 23, FrameMaker through Distiller 6 and 10, Web Capture, Image Conversion, Photoshop, InDesign CS6, an Acrobat 9 portfolio with 3D models, dynamic XFA from LiveCycle ES 9 and 10; the OPF's PDFMaker 7, 8, 10.1 and 11 and Acrobat 11 Image Conversion, InDesign CS (3.0), and from the remote corpus IBM ID Workbench, Xyvision and PageMaker 6.5 | — |
| W03 | Two Xerox copiers with their own OCR layer, Xerox JBIG2 without text, an HP MFP with Acrobat's OCR, a scanner's CCITT G3 file, a 1999 CCITT import, ABBYY FineReader 8 OCR under CCITT G4 pages, a JBIG2 scan completed in DocuSign; remote: a Konica Minolta bizhub scan untouched, with the copier's own OCR, a Ricoh MP C3003 scan through 3-Heights, a Ricoh MP 5054 scan completed in DocuSign, a Canon scanner's own OCR in a damaged file, Kodak Capture and Epson scans, OmniPage and AbleDocs OCR in tagged scans | A copier file untouched since the copier wrote it that we may commit: the Xerox ones were later re-saved, and the Konica, Canon and Epson files are remote, the Canon one damaged |
| W04 | JasperReports 7 on OpenPDF, iText 2.1.7 then PDFBox (weclapp), PDFBox 3 (Swiss QR-bill), PDFlib's Java binding, Apache FOP, PDFMaker 21 (a UK government sample invoice) — all with fictitious data; remote, real output: SAP NetWeaver (a statement and an invoice), Axapta, Scoro, a card statement from PDFlib on z/OS, a bank's AFP batch processor, a central bank's JasperReports on iText 2.1.0 | **A real invoice or statement from a supplier or bank** we may commit: every real one found is remote. Crystal Reports and Oracle: the two Oracle invoices found each gave a person's phone number |
| W05 | A GPO certification, DILA's Dictao signature, a qualified seal renewed by timestamps over two years, a PAdES B-LTA seal, two Acrobat Reader signatures, **DocuSign's envelope seal on a GSA contract form**, node-signpdf's signature and unsigned placeholder; remote: Adobe Sign, Yousign's qualified seal, DocuSign twice more, Universign document timestamps, Foxit PhantomPDF certification and approval signatures, PAdES B-B to B-LTA, Slovak and Hungarian qualified seals, the Spanish gazette's seal, 24 signatures and a timestamp in one file, a certified dynamic XFA form, legacy `adbe.x509.rsa_sha1` and MD5 references | **A Yousign, Universign or Adobe Sign signature we may commit**: those found are remote, Universign's only as timestamps. A signature under a person's own certificate: those examined carried an e-mail address, a phone number or an identity number |
| W06 | A really damaged GovDocs1 file, a GovDocs1 error file, a scanner file that broke pikepdf, forms that broke pdf.js, SafeDocs lexer and dialect tests, a signature `/Reason` that broke node-signpdf; remote: files that broke pdf.js, PDFBox, PDFium, pdfplumber, PdfPig, OCRmyPDF and EU DSS — truncations, a zeroed block, junk after `%%EOF`, a stale tail, object-stream indexes wrapped at 16 bits, corrupt Flate data and fonts; the OPF's Cabinet of Horrors (a byte missing, a header naming PDF 1.8, an image's /Height altered, an image pointing nowhere) and GovDocs1 files qpdf cannot finish; remote: 45 files filed against JHOVE under the error it raised — a MacBinary header, a data: URI prefix, a null kid, kids pointing at missing objects, a lost tail —, a fact sheet broken throughout by a text-mode transfer; remote: the iPRES 2017 hand-built set, 88 files each with one deviation from ISO 32000-1's structure | Anything that broke your own tools |
| W07 | Factur-X from weclapp and from Dynamics 365 (demo data), ZUGFeRD from GnuAccounting and from the Mustang library, the factur-x Python library's output; remote: the FNFE-MPE's French example, intarsys's EN 16931 and XRECHNUNG samples, DWC's generator through WeasyPrint, Symtrax, Konik, 4s4u's additional data, an Order-X purchase order, a UBL payload made hybrid by iText 9 | A Factur-X a supplier's ERP actually sent, anonymised: the two found, from Oracle Reports and from Business Central, carried a person's contact details |
| W08 | Blank XFA forms (IRS, USCIS, DoD, a Cerfa), JavaScript AcroForms (USCIS I-9, HMRC), a calculation order, an OmniForm form, tagged forms; remote: dynamic XFA from LiveCycle ES 9 and 10, encrypted, one of them certified, OPM's OF-306 with date JavaScript and signature fields, an Acrobat radio-button form with NULs in its names, an XFA form filled with fictitious values inside a portfolio | A form that is filled in — with fictitious values — that we may commit |
| W09 | Arabic, Russian, Greek, Traditional Chinese (2004 and 2017), vertical Japanese without ToUnicode; remote: Hebrew (a US Census guide, a USDA fact sheet), Arabic shaped into CID fonts by WeasyPrint, Chinese font names in GBK bytes, PDF 2.0 UTF-8 strings | Korean; Hebrew and shaped Arabic we may commit |
| W10 | PDF 1.2 to 1.4 from 1995–2004: PDFWriter 3.02 and 4.05, Distiller 2, 3 (Windows and Mac), 4 and 6, PDFMaker 5, Acrobat 3, RC4-40; remote: a 1998 PDFWriter 3.02 file with its line ends stripped; the OPF's GovDocs1 files from Distiller 4 and 5 and groff; remote: Xyvision's Parlance Publisher, and the 1994 Transportation Statistics report, whose /Producer names Distiller 1.0.2 for Macintosh over a PDF 1.3 update | A file from before 1996, PDF 1.0 or 1.1 |
| W11 | Nothing committed, by design; remote: the US Code's Title 42 (9,302 pages), a USGS topographic map (one 63 MB page) and USGS Professional Paper 1 (147 MB of JPEG 2000 scans), fetched and tested every night; beside them a 4.7 MB portfolio, a 10.6 MB tagged scan, a CCITT image that decodes to 153 MB, 24 signatures in 48 updates | — (the map is read whole under a raised decoding limit, `readerLimits`, since ADR 34) |
| W12 | PDF/A from PDFlib, Antenna House, Distiller, 3-Heights, BFO, Mustang, Aspose, OpenOffice and Word via PDFMaker, and two false claims, one from the factur-x Python library, each with veraPDF's verdict; remote: PDF/A-1a from callas pdfaPilot and Oracle Outside In, PDF/A-1b from Ghostscript, PDF/A-3 from Symtrax, Konik, intarsys and iText 9, PDF/A-4f and PDF/A-3u from WeasyPrint, claims veraPDF rejects from PDFMaker 11 and a gazette decree re-signed through iText, PDF/X-3 from Photoshop, PDF/UA-1 from AbleDocs and InDesign; the OPF's OpenOffice.org 3.2 PDF/A-1a and Acrobat 11 Image Conversion PDF/A-1b | A PDF/A-3 from a real ERP with its validation report, that we may commit |

## What makes a usable sample

- **Small, preferably.** One to five pages is plenty, and a few hundred kilobytes is ideal: everyone who
  clones the repository downloads the committed corpus. It is a recommendation, never a reason to turn a
  document down. A file over 2 MB is simply not committed: it joins the remote corpus, fetched from its
  public URL (below), so it has to be online somewhere first. The private corpus is for confidential
  documents, never for heavy ones. For W11 the weight is the point.
- **Whole.** Do not re-save it, do not "clean" it in Acrobat, do not run it through another tool. The
  producer's original bytes are the entire value; a file re-saved by another program is a sample of that
  other program.
- **Accompanied by a sentence.** What it is, where it came from, and — for W06 — what went wrong and in
  which tool. Two lines are enough, and they become the manifest entry.

## Before you hand anything over

This repository is **public**. Everything committed to `tests/corpus/documents` or
`tests/corpus/vendor` is published, indexed and irrevocable. Run through this list first:

- [ ] **Visible content**: no one's phone number, postal address or e-mail address, no account or
      identity numbers, no health data, and no amounts you would not print in a newspaper. A person's
      name alone is acceptable, and so is an image of a handwritten signature, which counts as a name.
      Fabricated or specimen data is always fine, an invented e-mail address at a real domain included;
      so is an anonymous photograph, even captioned with a health condition, and a software library's
      copyright line compiled into the file, even with its author's e-mail address. Redacting in a PDF
      viewer often only draws a black rectangle over text that is still there — check by selecting the
      text underneath, or by extracting it.
- [ ] **Metadata**: `/Info` and XMP carry author, company, the local file path and sometimes the template
      used. Say so if you want them stripped; I will do it and record that the file was modified.
      A print driver writes its own: **Microsoft Print to PDF puts the display name of the Windows
      account that printed into `/Author`**, whatever the document's properties say; Ghostscript-based
      printers such as PDF24 copy the account's user name from the PostScript into `/Author` and the XMP;
      and Word's Save as PDF copies the document's Author property, which defaults to the Office user's
      name.
- [ ] **Hidden material**: attachments, annotations and their authors, form field values, layers turned
      off, earlier versions kept by incremental updates. A PDF can hold several years of edits: two
      public files turned down for this corpus named a civil servant only in an `/Info` dictionary a later
      revision had replaced. An XFA form also keeps its designer's file paths — `C:\Users\<name>\…` — in
      its configuration packet.
- [ ] **Licence and permission**: you must be entitled to publish it. A supplier's invoice is your
      document but their layout — when in doubt, it goes in the private corpus instead.

If any box cannot be ticked, the file is still useful: put it in the private corpus, which is never
published.

## Where to put it

### Public — the file can be published

```
tests/corpus/documents/<use-case>/<producer>-<what-it-is>.pdf
```

Use cases are `invoice`, `report`, `contract`, `form`, `scan`, `archival`, `damaged`, `stress`.
Names are lower case with hyphens, and name the producer first: `word-print-driver-invoice.pdf`,
`acrobat-contract-signed.pdf`.

Then describe it in `tests/corpus/manifest.json` — by hand: the build script replaces only the entries it
writes itself, marked `builtBy` — and run `build_corpus.py --committed-only` to record the referee's
verdict (`tests/corpus/README.md` has the one-line container command). Or tell me the two lines about the file and
I will write the entry, establish the expectations with an independent tool, and add it to the acceptance
tests.

A third party's file — a sample from another project's test suite, a public-domain government document —
goes under `tests/corpus/vendor/<source>/` instead, described in the same manifest, and only under an
attribution-only licence recorded in `tests/corpus/NOTICE` (ADR 23).

### Public but not redistributable — fetched on demand

A file anyone can download but nobody may republish — a bug-report attachment, a vendor's sample, a
ShareAlike document, any file over 2 MB whatever its licence — is not committed at all. It is described in
`tests/corpus/manifest.json` with origin `remote`, its URL and its SHA-256, fetched by
`build/fetch_remote.py`, and tested every
night by the `Remote corpus` workflow (ADR 32). Terms that restrict reuse do not keep a file out —
conditions beyond attribution, non-commercial reproduction only, fair use only, no modification or
commercial use — but "educational use only" does. Send the URL rather than the file; `tests/corpus/README.md` says how an entry is written. A
document of yours that is not public belongs in the private corpus, not here: the manifest is published,
and it lists only files anyone can already download.

### Private — the file cannot be published

```
tests/corpus/private/<anything>.pdf     described by tests/corpus/private.json
```

Both are ignored by git. The test suite merges them when they are present and runs on the public corpus
when they are not, so a private corpus never breaks anyone else's build. Same manifest format.

### Handing files over

Committing them on a branch is simplest. Attaching them in conversation also works — I will place them,
write the entries and run the acceptance suite. Either way, say which of the two destinations applies:
I will not publish a file into the public corpus on my own judgement.

## The manifest entry

One entry per document. `expect` is what the tests assert; `readerLimits`, when there is one, is how the
document is opened; everything else is provenance. `tests/corpus/manifest.schema.json` gives every field its
type, its allowed values and a description, and an editor that reads JSON Schema applies it as you type.

```jsonc
{
  "file": "documents/invoice/word-print-driver-invoice.pdf",
  "title": "Invoice printed through the Microsoft PDF print driver",
  "useCase": "invoice",
  "producer": "Microsoft Print to PDF, Windows 11 24H2",
  "origin": "contributed",
  "licence": "Owned by AdCodicem, published with permission",
  "features": ["xref-table", "type0-subset", "print-driver"],
  "expect": {
    "pages": 2,
    "clean": true,
    "indexRebuilt": false,
    "requiredDiagnostics": [],
    "refereeCheckSucceeds": true,
    "textContains": ["Facture", "Total TTC"]
  }
}
```

Rules about `expect`:

- **`refereeCheckSucceeds` is filled in by the build script**, which runs the very command the integration
  tests run — `qpdf --check` — rather than guessing what it ought to say. Damage and rejection differ:
  junk before the header shifts every offset and qpdf adjusts without complaint, while an encrypted
  document it has no password for is refused although nothing is wrong with it.
- **Establish the rest with an independent tool**, never with our own reader. An expectation derived from the
  code under test proves nothing. `pikepdf`, `qpdf --check` and `pdftotext` are the referees in use.
- **`findings` lists the validation rules the document earns** (M2), by identifier, and is left out when it
  earns none — which a sound document should. Establish each from the file itself: `file.eof-missing` when
  no `%%EOF` lies in its last 1,024 bytes. The test holds every document to exactly its list.
- **`catalogRecoverable: false` is for a file with no catalogue at all** — no object in it is one, and the
  referee finds none either. The tests then expect the reader to open the file and hand back no catalogue
  rather than invent one. Every other entry leaves the field out.
- **A valid document that exceeds a default reader limit gets `readerLimits`, not `unsupported`.** Establish
  the size that crosses the limit independently — the referee reads the document whole, and zlib or qpdf
  measures what a stream decodes to —, then give the smallest round value that reads it: 512 MB for a
  313 MB image, never `Unbounded`. The field sits beside `expect`, since it is a setting rather than an
  observation (ADR 34).
- **Never weaken it to make a test pass.** Either the library is wrong and gets fixed, or the expectation
  was wrong and gets corrected with the reason in the commit message.

For a W06 file — one that broke something — say what the correct behaviour is, even if we cannot reach it
yet. An expectation the library fails today is recorded as unsupported, with the milestone that will
address it. Silence is what we are trying to avoid.

## What happens next

A contributed document is added to the manifest, runs in the acceptance suite from that commit onwards,
and is named in `docs/status.md` if it changes anything. If it finds a defect, the fix and a regression
test land with it. If it exposes something out of scope for the current milestone, it becomes a recorded
expectation pointing at the milestone that owns it.

## Gaps by milestone (identified 2026-09-26)

When the roadmap was revised, each milestone's specification said what its acceptance needs that the corpus
does not hold yet: the "Corpus" section of each file in `docs/milestones/`. The table below gathers them.
**None has been added yet**: this is the list to work from, milestone by milestone, not a record of work done.
"Generate here" means one of the producers the container runs (Chromium, LibreOffice, ReportLab, qpdf and
the Python producers); "Remote corpus" means a document that may be used but not redistributed (ADR 32).
Priority follows the scale above. M22, M23 and M25 to M31 are not in the table: their specifications were
written, or are still to be written, after the gaps were gathered, and their own "Corpus" sections are the
reference.

### M3

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| A signed twin of our own contract (documents/contract/chromium-contract-fr.pdf): a PAdES B-B approval signature and a DocMDP P=2 certification, from a fictitious test PKI, as a derived variant | The roadmap's M3 acceptance names 'the signed contract document', but the corpus's contract is unsigned; the vendored signed files carry the behaviour, yet none is a modern signature over a document we can regenerate | vendor/us-federal/itext-govinfo-us-code-certified.pdf, vendor/fr-licence-ouverte/fop-dictao-dila-signed-joafe-notice.pdf, vendor/pyhanko/acrobat-reader-signed-twice.pdf, vendor/node-signpdf/skia-chrome74-node-signpdf-reason-contains-trailer.pdf, vendor/lu-legilux/*, vendor/us-federal/docusign-pdfkit-gsa-sf30-contract-modification.pdf (all useCase contract) | Generate here | 2 |
| A committed PDF 2.0 document with UTF-8 text strings in /Info, outline titles and page labels, and an ISO_ developer-extension entry | Exercises the 2.0 input path, T06 and the /Extensions union on committed files; the only UTF-8 file is remote (CC BY-SA) and the committed 2.0 fixtures carry little metadata | remote/pdf20examples/handwritten-pdf20-utf8-strings.pdf (remote), vendor/verapdf/pdfa4-metadata-pass.pdf, vendor/verapdf/pdf20-version-mismatch.pdf | Generate here | 2 |
| A committed incremental update glued to the %%EOF marker (no line end), and a committed signed hybrid-reference file | The incremental writer must append a line end first and follow a hybrid file's shape; both cases exist only in the remote corpus, which the main CI job never sees | remote/pdfcpu/word2019-diia-test-pades-b-b.pdf, -b-lt.pdf, -b-lta.pdf | Generate here | 3 |
| A committed document with one encoded stream larger than the writer's 1 MB copy buffer (about 1.2 MB, incompressible image) | The piecewise copy of stream data meets a real stream past its buffer only in remote documents | remote/usgs/us-topo-washington-west-2023.pdf (14.9 MB image), remote/usgs/omnipage-usgs-professional-paper-1-1902.pdf | Generate here | 3 |

### M4

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| Crafted attack fixtures derived from a signed document by recorded transformations: shadow hide, replace (by font and by form-field appearance), hide-and-replace; an incremental saving attack after an approval signature; signature wrapping (gap larger than /Contents); universal signature forgery (/ByteRange or /Contents missing, null, empty) | The roadmap's M4 acceptance names crafted shadow-attack fixtures, and no real corpus document carries a forbidden post-signature change | none; the published PDF-Insecurity exploit files (pdf-insecurity.org, Ruhr-Universität Bochum) are a second source whose licence is unverified, so remote at best | Generate here | 1 |
| Certified documents with later changes, allowed and forbidden: DocMDP P=2 then a field filled and a countersignature; P=1 then a DSS and a document time stamp; an annotation added under P=2 and under P=3; a FieldMDP-locked field changed beside an unlocked one | No committed certification is followed by any change (the GPO certification closes the last revision), so allowed-versus-forbidden classification has no real test material | vendor/us-federal/itext-govinfo-us-code-certified.pdf (certification is the last revision); remote/canada/livecycle-es10-ircc-imm1344-certified-dynamic-xfa.pdf (only usage rights added after certification) | Generate here | 1 |
| pyHanko's verdicts recorded in the manifest for every committed and remote entry: revision count, and per signature the revision closed, the coverage level and the modification level of what follows (new expect.revisions and expect.signatures fields) | Acceptance must compare against an independent referee's opinion recorded in advance, never against our own reader | none (refereeCheckSucceeds holds qpdf's verdict only) | Generate here | 1 |
| The pyHanko-signed twin of documents/contract/chromium-contract-fr.pdf (shared with M3) | The reproducible base from which every crafted M4 fixture is derived | documents/contract/chromium-contract-fr.pdf (unsigned) | Generate here | 1 |
| A real document annotated or countersigned in Acrobat after someone else signed it | Acrobat rewrites /Info and XMP on every save, so real updates mix change classes (metadata with annotations or signatures) that crafted fixtures keep apart; the classification must not call real countersigned files tampered | vendor/pyhanko/acrobat-reader-signed-twice.pdf; remote/eu-dss/excel365-nowina-25-signatures-dss-vri.pdf | Contribution | 2 |
| A signature dictionary stored inside an object stream | Its coverage (Malformed) and the signature.dictionary-in-object-stream rule are designed but met in no file | none | Generate here | 3 |

### M6

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| Two fillings of one AcroForm with different, fictitious values | The roadmap acceptance 'merging two forms that both define a field of the same name keeps both values' needs values; no committed form is filled, let alone filled twice | documents/form/reportlab-subscription-form.pdf (blank); the real name collisions between vendor/fr-licence-ouverte/libreoffice-cerfa-13983-form.pdf and pdfmaker-acrobat-cerfa-12156-form.pdf (unfilled) | Generate here | 1 |
| A contract whose appendices are third-party PDFs, with bookmarks and cross-references between body and appendices | The roadmap's 'contract appendices' do not exist: chromium-contract-fr.pdf has no appendix, no link and no bookmark; the acceptance substitutes a set of separate files | documents/contract/chromium-contract-fr.pdf, vendor/uk-ogl/word2019-ccs-contract-schedule.pdf, vendor/pdf-association/indesign13-pdfua1-german-book-chapter.pdf | Generate here | 2 |
| A page tree that inherits /Rotate, /CropBox and /Resources from intermediate /Pages nodes | Only MediaBox is inherited in the committed corpus, in two files, one of them damaged | vendor/opf-format-corpus/pdfmaker9-word-distiller-pdfa1b-test-document.pdf (MediaBox on the root), vendor/opf-format-corpus/distiller3-dea-cfr-damaged.pdf (MediaBox on intermediate nodes) | Public source | 2 |
| Page labels in upper roman and letters, with real prefixes (A-1, Annexe 2-) | Committed labels are decimal or lower roman; letters and prefixes are what case files use | vendor/opf-format-corpus/word9-distiller405-usgs-nwql-volatile-organics-methods.pdf, vendor/pdf-association/handwritten-utf16le-strings.pdf | Generate here | 2 |
| Two tagged documents whose role maps map one custom type differently, and colliding class names | The role-map and class-map collision rules have no real case | indesign13-pdfua1-german-book-chapter.pdf, indesign15-pdfua1-form.pdf, pdfmaker9-word-distiller-pdfa1b-test-document.pdf (role and class maps that do not conflict) | Contribution | 3 |
| A document with a catalogue /URI base and relative URI links | The rule resolving a part's relative links against its own base before merging has no real case | none | Generate here | 3 |
| An intact document with article threads | Threads exist only in a damaged remote file | remote/pdfbox/distiller6-zeroed-object-stream-pdfbox3947.pdf (damaged) | Public source | 3 |
| A PDF/UA-1 document from a producer other than InDesign and PDFlib | The merged-structure acceptance rests on two producers | pdflib-pps-kraxi-pdfa2a-pdfua1-invoice.pdf, indesign13-pdfua1-german-book-chapter.pdf, indesign15-pdfua1-form.pdf; remote AbleDocs members | Public source | 3 |

### M7

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| Image files (not PDFs) as corpus documents, and a manifest and schema that can describe them | The roadmap acceptance 'every image file in the corpus becomes a page' has no image file to run on; manifest.schema.json admits only .pdf paths | none as files; 56 JPEG, 70 CCITT and one JP2 stream inside committed PDFs | Generate here | 1 |
| JPEGs in all eight EXIF orientations, with and without JFIF density | Orientation by matrix (mirrored values included) has no input; the corpus's only orientation tag says upright | the EXIF-tagged JPEG in vendor/fr-licence-ouverte/fop-dictao-dila-signed-joafe-notice.pdf (orientation 1) | Public source | 1 |
| PNGs of every colour type, bit depth, interlacing and transparency (PngSuite) | PNG IDAT pass-through and its lossless paths have no input at all | none | Public source | 1 |
| Multi-frame TIFFs: CCITT G3 1-D/2-D, G4, Modified Huffman, LZW, Deflate with predictor, PackBits, several strips, FillOrder 2, MinIsBlack, JPEG in TIFF | TIFF is what copiers deliver; only CCITT strips taken out of PDFs exist | CCITT streams of finereader8-frb-sr0115-examiner-guidance.pdf, acrobat3-import-irs-1040-1988-scan.pdf, pikepdf/scanner-ccitt-endofline.pdf | Generate here | 1 |
| A set of pieces cross-linked by GoToR (page and named destinations) and by relative or file: URIs with #page= and #nameddest= | The GoToR/URI rewrite has no real input; only one Launch-to-PDF pair exists | vendor/opf-format-corpus/pdfmaker9-word-distiller-external-link.pdf (Launch to text_only_pdfa1b.pdf = pdfmaker9-word-distiller-pdfa1b-test-document.pdf) | Generate here | 1 |
| CMYK and YCCK JPEGs with an Adobe APP14 marker, and a JPEG with a multi-chunk ICC profile | The inverted /Decode and ICCBased paths have no input; corpus JPEGs are RGB or greyscale without profiles | the 56 RGB/greyscale JPEG streams in committed PDFs | Generate here | 2 |
| JPEG 2000 files: a raw codestream, a JP2 with alpha, a JPX-branded file | One committed JP2 cannot cover colour and alpha cases | vendor/opf-format-corpus/imagemagick-false-pdfa1b-jpx.pdf; remote acrobat8-jpx-precincts-issue5475.pdf and omnipage-usgs-professional-paper-1-1902.pdf | Generate here | 2 |
| A committed portfolio with folders, a schema, PDF and non-PDF members | The only portfolio is remote, so unpacking is tested nightly only | remote/opf-format-corpus/acrobat9-portfolio-signed-3d.pdf | Generate here | 2 |
| A scan batch with blank separator pages | The separator strategy has no real batch | committed CCITT scans (qpdf can insert empty pages as a derived variant) | Generate here | 2 |
| A real single-sided duplex scan delivered as fronts and reversed backs | Collation is proven only on halves qpdf derives at test time | vendor/us-federal/finereader8-frb-sr0115-examiner-guidance.pdf, acrobat3-import-irs-1040-1988-scan.pdf | Contribution | 3 |
| A document with GoToE links into an attached PDF | GoToE resolution has no real input | none (bfo-pdfa2b-embedded-pdf.pdf carries a PDF attachment but no GoToE) | Contribution | 3 |

### M8

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| The roadmap's acceptance sample (accented French with fi/fl ligatures, CJK, a Cyrillic name, a supplementary-plane character, U+202F before a colon) as Chromium and LibreOffice render it | M8's first acceptance is judged on our own output alone; a real producer's rendering of the same text says what 'extracts exactly' means for them and whether their ligature ToUnicode and fallbacks do better than ours | documents/invoice/chromium-invoice-fr.pdf (accents, type0-subset), vendor/us-federal/indesign-irs-pub1-russian.pdf (Cyrillic), vendor/us-federal/indesign-irs-pub1-chinese-traditional.pdf (CJK) | Generate here | 2 |
| A committed document embedding a CFF-flavoured OpenType face from a current producer | Only the GPO's Myriad Pro is FontFile3 /OpenType; the 12 CID-keyed CFF programs come from InDesign, Distiller and PDFMaker, so the CFF subsetter's CID-keyed output has no modern peer to compare with | vendor/us-federal/itext-govinfo-us-code-certified.pdf; vendor/us-federal/indesign-irs-pub1-chinese-traditional.pdf; vendor/opf-format-corpus/indesign-cs-puppet-guild-event-flyer.pdf | Generate here | 2 |
| Glyphs of the supplementary planes embedded with a ToUnicode that writes surrogate pairs | Only strings, not glyphs, carry supplementary-plane characters in the corpus, and the one emoji font is remote | remote/pdf20examples/handwritten-pdf20-utf8-strings.pdf (strings only); remote/us-states/powerbi-pdfium-acrobat-covid-dashboard-nine-updates.pdf (emoji-font) | Generate here | 2 |
| A document whose text came from a TrueType collection (.ttc) member | Collections are a registry feature; nothing shows how producers subset and embed a collection member | none | Contribution | 3 |
| A document with a restricted-licence (OS/2 fsType bit 1) face that a producer embedded anyway, or refused to | The embedding-permission rule has no real case to be compared with | vendor/eu-publications/pdflib-oj-exchange-rates-greek.pdf (EUAlbertina embedded in full under an editable-embedding permission) | Public source | 3 |

### M9

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| A rotated page carrying internal links, a markup annotation with its popup, and a form field | The roadmap's 'flatten /Rotate with annotations and links transformed alike' has no committed page that is both rotated and annotated: the four rotated documents carry nothing but DocuSign's signed widget | vendor/opf-format-corpus/pdfmaker7-powerpoint-va-cancer-database-course.pdf (53 pages at 90°, no annotations); vendor/us-federal/docusign-pdfkit-gsa-sf30-contract-modification.pdf (270°, one widget); vendor/opf-format-corpus/reader10-openoffice32-annotated-object-streams.pdf and documents/report/chromium-report-fr.pdf (annotations and links, unrotated) | Generate here | 1 |
| A letterhead page on its own (logo, address block, coloured band) to underlay | The overlay/underlay acceptance needs the business case it exists for; no corpus document is a letterhead alone | none (the generated invoices carry a header, not a separate letterhead) | Generate here | 1 |
| A document whose pages share one content stream | Legal, a known trap for anything that edits content in place; the wrapper must keep it shared, and the committed corpus has none (pikepdf survey) | none | Generate here | 2 |
| A PDF/A document veraPDF upholds with a CMYK output intent | The one committed CMYK intent sits on a claim veraPDF rejects, so refusing RGB marks is tested only on an already non-conforming document | vendor/eu-publications/distiller10-eu-consolidated-regulation-2015.pdf (CMYK intent, claim rejected); remote/ocrmypdf/photoshop-cc2015-pdfx3-cmyk.pdf (PDF/X-3, not PDF/A) | Public source | 2 |
| A tagged PDF 2.0 document, ideally PDF/UA-2 | The PageNum and Bates artifact subtypes exist only in PDF 2.0 output; no committed tagged document is 2.0 | vendor/verapdf/pdfa4-metadata-pass.pdf (PDF 2.0, PDF/A-4, not a tagged business document); remote/pdf20examples/handwritten-pdf20-utf8-strings.pdf | Public source | 2 |
| An exhibit stamped by the tools French lawyers use (Kleos, Hub-Avocat, Acrobat's stamp tool) | Position, wording and per-piece numbering conventions are taken from the tools' documentation, not their output; M18's presets want the real thing | none | Contribution | 3 |
| A document Bates-numbered by Acrobat | Acrobat marks its Bates numbers with its own /PieceInfo and artifacts; our removal must leave them, and the corpus has only Acrobat headers and footers (remote) and PDFStamp (remote) | remote/us-states/powerbi-pdfium-acrobat-covid-dashboard-nine-updates.pdf; remote/opf-format-corpus/jhove-hul-28-dvipdfm-pdfstamp-physics-article.pdf | Contribution | 3 |
| A committed large-format page (A1, A0) from a CAD or GIS producer | Fit to A4 meets huge pages only in the remote corpus | remote/usgs/us-topo-washington-west-2023.pdf; remote/pdfjs/aspose-cad-stale-tail-issue9418.pdf; vendor/opf-format-corpus/imagemagick-false-pdfa1b-jpx.pdf (2717 × 3701 pt) | Public source | 3 |

### M10

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| The reference payload set the roadmap's first M10 acceptance condition names (every symbology, QR level and mode, Data Matrix encodation and shape, PDF417 level, numeric/text/binary/UTF-8 payloads, each at capacity), and a decision on where it lives: tests/AdCodicem.Pdf.TestSupport as test data, or a manifest able to describe non-PDF fixtures | The roadmap writes the acceptance against 'a reference set of payloads' that does not exist; the manifest schema admits .pdf only, the same question M7 raises for image files | none | Generate here | 1 |
| Swiss QR-bills in every variant: QRR with a QR-IBAN, SCOR, NON, EUR, no amount, no debtor (corner marks), each heading language, one with billing information and two alternative schemes | One specimen cannot exercise the reference-type matrix, blank fields or headings; each of our slips must be compared with an independent producer's | vendor/swissqrbill/pdfbox-swiss-qr-bill-a4.pdf (one specimen, SCOR-less alternative-procedure bill from SwissQRBill through PDFBox 3) | Generate here | 1 |
| An invoice carrying an EPC (GiroCode) QR code from a real invoicing system or ERP | Payload parity with segno proves the guideline, not the field choices and placement real invoices use | none: no committed invoice carries a code (documents/invoice/*, vendor/zugferd/*, vendor/docentric/*) | Contribution | 2 |
| Linear and 2D codes painted by other producers: ReportLab's barcode module (Code 128, EAN-13, QR, Data Matrix), LibreOffice's QR/barcode generator, a GS1-128 parcel label | A document already carrying a code is the case a stamp must not disturb, and the input any later barcode recognition would start from | vendor/swissqrbill/pdfbox-swiss-qr-bill-a4.pdf only | Generate here | 3 |
| A government form whose pages carry a printed PDF417 (e.g. a USCIS paper-forms barcode), public domain, screened for personal data | The commonest received document with a 2D code; stamping beside it must leave it readable | vendor/us-federal/livecycle-uscis-ar11-xfa-form.pdf and designer-distiller23-uscis-i9-javascript-form.pdf (no barcode field; no XFA `<barcode>` in any committed form, checked 2026-09-26) | Public source | 3 |

### M11

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| Documents annotated in real reviewing tools with every markup subtype — underline, strike-out, squiggly, free text (plain, callout, typewriter), stamps (standard, custom, image), square, circle, line with endings, polygon, polyline, ink, caret, replies and review states — from Acrobat first, then Foxit, PDF-XChange, macOS Preview, pdf.js's editor, Okular. Fallbacks: PDFium's BSD hand-written .in fixtures (annots, ink_annot, line_annot, polygon_annot, links_highlights_annots, annotation_highlight_rollover_ap), LibreOffice's comment export, pdf.js's editor driven in Chromium | The committed corpus has one highlight, one note and one file attachment; flattening and removal must meet what reviewers' tools write (their appearances, /RC, quad order), not only our own output | vendor/opf-format-corpus/reader10-openoffice32-annotated-object-streams.pdf, pdfmaker10-word-file-attachment-annotation.pdf, pdfmaker9-word-distiller-embedded-quicktime.pdf; remote quartz-word-samhsa-prevention-pathways-fact-sheet.pdf (damaged) | Contribution | 1 |
| Third-party annotations WITHOUT appearance streams, of several subtypes: PDFium's hand-written annotation_markup_multiline_no_ap.in (BSD), pypdf's annotation classes (which write none), and the committed annotations with /AP removed by a recorded pikepdf transformation | 'Appearance streams generated for every subtype' must be proven on others' annotations; M21 relies on the same generator | none: every committed markup annotation has an appearance (popups excepted, legally) | Generate here | 1 |
| A decrypted twin of vendor/us-federal/livecycle-uscis-ar11-xfa-form.pdf, derived with qpdf --decrypt and recorded in build_corpus.py | Its PrintOnly/ViewOnly groups with /BaseState /OFF and /AS, and its two links on a layer, are the only committed layers whose state differs between screen and print — but the file is AES-128 encrypted, readable only from M16 | vendor/uk-ogl/pdfmaker21-ozev-sample-invoice.pdf (Acrobat Watermark group with /AS, same state for every event) | Generate here | 1 |
| Acrobat's own print-only watermark (shown when printing, hidden on screen) and a Watermark annotation with /FixedPrint | Both print-only forms M11 writes should be checked against the reference implementation's own output; the committed Acrobat watermarks show and print alike | vendor/uk-ogl/pdfmaker21-ozev-sample-invoice.pdf, vendor/uk-ogl/indesign-acrobat-hmcts-n208-form.pdf | Contribution | 2 |
| Layers with visibility expressions (/VE), radio-button groups, locked groups, alternate configurations (/Configs) and a nested /Order with labels | None is committed; the PDFMaker memberships use /OCGs and /P only | the five PDFMaker 7/8 files with OCMDs; remote remote/usgs/us-topo-washington-west-2023.pdf (31 layers, /Order to be examined) | Generate here | 2 |
| A DocMDP P=3 certification followed by an annotation, and one without (the same crafted pyHanko fixtures M4 lists) | The allowed case of M4's permission table has no document; M11's 'annotations allowed where the certification allows them' row depends on it | vendor/us-federal/itext-govinfo-us-code-certified.pdf (P=1); remote remote/eu-dss/pdfmaker11-nbu-sk-qualified-seal-docmdp-fieldmdp.pdf (P=2) | Generate here | 2 |
| A tagged document whose markup annotations are tagged (Annot elements with OBJR) | Removal and flattening inside the structure tree are otherwise proven on our own tagging only; committed tagged annotations are links | vendor/pdf-association/indesign13-pdfua1-german-book-chapter.pdf (tagged links with OBJR) | Contribution | 2 |
| Annotations with NoZoom and NoRotate on rotated pages, from Acrobat | Flattening's counter-rotation is otherwise proven on the unrotated Reader X page and our own rotated pages only | vendor/opf-format-corpus/reader10-openoffice32-annotated-object-streams.pdf (flags 28 on an unrotated page) | Contribution | 3 |
| Free text and stamps in right-to-left and complex scripts, written by Acrobat | M8's draw-and-report path for unshaped text should be checked against what arrives; M12.6's HTML-rendered stamps will need a reference | none | Contribution | 3 |

### M12

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| The three reference sources rendered by WeasyPrint 70 and committed | A second engine's output of the same content, committed; the corpus's WeasyPrint files are remote (ShareAlike or unlicensed) | remote/py-pdf-sample-files/weasyprint-arabic.pdf, remote/mustang/dwc-weasyprint-facturx-extended-pdfa4f.pdf | Generate here | 2 |

### M12.1

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| Third-party HTML templates made for paged media with the PDF their own engine made (e.g. the WeasyPrint project's samples, Paged.js examples) | Real templates we did not write are the only test of the declared CSS level against what template authors use | none (only our three sources exist) | Public source | 2 |

### M12.2, M12.6

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| A paged-media edition of the report (running header with the section title, 'page X sur Y', table of contents with target-counter() and dotted leader(), a named landscape annex, page groups, a break-before: right chapter) with WeasyPrint's rendering | Chromium implements no GCPM, so the three reference sources exercise none of M12.2's paged media or M12.6's cross-references; WeasyPrint is the independent paged-media engine a container can run | tests/corpus/sources/report-fr.html and documents/report/chromium-report-fr.pdf (linked TOC without page numbers, no running content) | Generate here | 1 |

### M12.2

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| A seeded, repeatable long report source for 10, 100 and 1,000 pages, as a streamed source and as one string | The memory acceptance needs a document whose length is a parameter | documents/stress/reportlab-journal-1000-pages.pdf (a PDF, no HTML source) | Generate here | 1 |
| A French invoice source with Arabic and Hebrew client names and addresses, an IBAN and amounts inside right-to-left paragraphs, dir=auto cells, in OFL Noto faces, with Chromium's rendering committed | The bidi acceptance otherwise rests on manifest strings alone; W09 still lacks Hebrew and shaped Arabic that may be committed | vendor/us-federal/indesign-irs-pub1-arabic.pdf; remote/census/indesign-census-2020-hebrew-guide.pdf; remote/usda/pdfmaker23-usda-title-vi-fact-sheet-hebrew.pdf; remote/py-pdf-sample-files/weasyprint-arabic.pdf | Generate here | 1 |
| The contract justified and hyphenated (hyphens: auto, French and German) as Chromium and WeasyPrint set it | Hyphenation has no real rendering to compare with; Pyphen is the only referee | documents/contract/chromium-contract-fr.pdf (justified, not hyphenated) | Generate here | 2 |
| Korean text, and a CJK paragraph long enough to exercise line-break: strict | W09 still lacks Korean; CJK line-breaking strictness has no real text | vendor/us-federal/indesign-irs-pub1-chinese-traditional.pdf | Contribution | 3 |

### M12.3

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| A multi-page invoice source (120+ lines over 3+ pages, repeated thead/tfoot, carried-forward subtotals, fixed footer, per-invoice page X of Y) with Chromium and LibreOffice renderings | The long-invoice case is absent; the corpus invoice is one page, so repeated footers and carried-forward subtotals have nothing to be accepted on | tests/corpus/sources/invoice-fr.html, documents/invoice/chromium-invoice-fr.pdf (1 page) | Generate here | 1 |
| A real multi-page invoice or statement from an ERP report writer that carries subtotals forward (a reporter / report / Übertrag) | Shows the convention our -adc-sum vendor extension imitates, as ERPs actually print it | vendor/jasper-modular/openpdf-jasperreports-financial-statement.pdf, remote/pdfminer/afp-batch-processor-bank-statement-2b.pdf (neither known to carry subtotals forward) | Contribution | 2 |

### M12.5

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| Charts as chart libraries write them in SVG (matplotlib's SVG backend, a Vega-Lite chart via vl-convert) with Chromium's rendering | The SVG acceptance needs real tool output: nested groups, transforms, clip paths, text labels | none (no SVG-sourced chart in the corpus) | Generate here | 1 |
| Image files as HTML meets them: WebP lossy and lossless, animated GIF, BMP, PNG with alpha / 16-bit / interlaced, CMYK JPEG with ICC profile | The Skia path and M7 pass-through need files, not streams inside PDFs (EXIF orientations are already M7's gap) | dct-image, flate-image and jpx streams inside corpus PDFs | Generate here | 2 |

### M12.6

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| One thousand seeded invoice records (JSON Lines) with varied line counts for the invoice template | The CI-enforced throughput budget of the roadmap needs a batch | none (invoice-fr.html is a single record) | Generate here | 1 |
| A one-page letterhead PDF (vector logo, address block, coloured band) | The @page background and `<img src=...pdf>` rows need the business case they exist for (also M9's gap) | none | Generate here | 1 |
| Puppeteer probes: the invoice and report printed by Chromium through pinned Puppeteer with header/footer templates and each option combination the mapping names | The page.pdf() mapping is accepted against what Puppeteer actually prints | documents/*/chromium-*-fr.pdf (printed with --print-to-pdf, no header/footer templates) | Generate here | 1 |

### M12.7

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| A legal memorandum source with footnotes (per-page numbering, a note long enough to continue, notes in a table) with WeasyPrint's rendering | M12.7's footnotes have no source; WeasyPrint is the peer that implements GCPM footnotes | none | Generate here | 1 |
| General terms of sale in three small-print columns over two pages with spanning headings, and Chromium's rendering | The standard back-of-invoice multicol case, beyond the report's short two-column annex | documents/report/chromium-report-fr.pdf page 4 (two-column-text annex) | Generate here | 1 |
| A real French memorandum or 'conclusions' with footnotes exported by Word | Word's footnote layout is what French legal readers expect | documents/invoice/word-invoice-fr.pdf (Word, no footnotes) | Contribution | 2 |
| Output of commercial paged-media engines (Prince, PDFreactor, Antenna House) on the paged-media report | A second peer for GCPM semantics where WeasyPrint and the specification leave room (footnotes, page groups) | none | Contribution | 3 |

### M13

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| Our engine's renderings of the three reference sources (documents/invoice/adcodicem-invoice-fr.pdf, documents/report/adcodicem-report-fr.pdf, documents/contract/adcodicem-contract-fr.pdf), committed untagged by M12, then regenerated tagged by M13 | Every M13 acceptance row (veraPDF PDF/UA-1, structure order vs source order, two-column annex reading order) is written against them | documents/invoice/chromium-invoice-fr.pdf, documents/report/chromium-report-fr.pdf, documents/contract/chromium-contract-fr.pdf (Chromium's, untagged) | Generate here | 1 |
| An accessibility reference source (sources/accessibility-fr.html + images): img with and without alt, SVG with `<title>`, figure/figcaption, nested ul/ol/dl, a table with caption, rowspan, colspan, scope and headers breaking across pages, footnotes, a target-counter() contents, links broken across lines, abbr, q, code, blockquote, lang passages in English, German and Arabic, generated content with and without alt text, a barcode | The three existing sources have no image, figure, footnote, nested list, complex table, abbreviation or foreign-language passage; 'every image carries alternative text' cannot be tested on them | sources/report-fr.html (nav list of links, thead table, ol, two-column annex); sources/invoice-fr.html | Generate here | 1 |
| A layout-div version of the invoice (sources/invoice-fr-divs.html) with the -adc- stylesheet that tags it | The CSS-override acceptance compares its structure with the semantic invoice's | sources/invoice-fr.html | Generate here | 1 |
| A set of faulty templates derived from the reference sources: no alt, h1 then h3, empty link, no `<title>`, no lang, a remapped standard type | PDF/UA-1 conflicts under Refuse and RemoveClaim must be shown exactly on documents a template author would write | none | Generate here | 1 |
| veraPDF's PDF/UA-1 verdict recorded in the manifest for every document claiming PDF/UA (a field beside conformanceValid, which covers PDF/A only) | The referee's pinned container must be calibrated on third-party UA claims before it judges ours; the three upheld claims are stated in prose only | vendor/pdf-association/pdflib-pps-kraxi-pdfa2a-pdfua1-invoice.pdf, indesign13-pdfua1-german-book-chapter.pdf, indesign15-pdfua1-form.pdf (claims, no recorded UA verdict) | Generate here | 2 |
| The reference sources rendered tagged by Chromium and exported by LibreOffice 24.2 with its PDF/UA option | Other producers' tagging decisions on the same content, to compare ours with and to arbitrate when veraPDF and poppler disagree | documents/*/chromium-*-fr.pdf and documents/*/libreoffice-*-fr.pdf (untagged) | Generate here | 2 |
| A committed PDF/UA-1 document from a producer other than Adobe, PDFlib and AbleDocs | The structure reader's calibration and M6's merge of tagged outputs rest on two committed producers | the three vendor/pdf-association PDF/UA-1 files (PDFlib, InDesign x2) | Generate here | 3 |
| A tagged PDF/UA-1 document in a right-to-left script (Arabic or Hebrew) | The slice-7 decision on right-to-left ActualText is checked on our output only | vendor/us-federal/indesign-irs-pub1-arabic.pdf (tagged, /Lang wrongly en-US, no UA claim) | Public source | 3 |

### M14

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| Factur-X facts per hybrid in the manifest: flavour, profile, version or Unknown, attachment name, AFRelationship, SHA-256 of the embedded XML | Acceptance must compare with expectations established from the file and independent tools (Akretion's factur-x, qpdf --show-attachment), not from our reader; today only expect.attachments names are recorded | expect.attachments on the 16 hybrids; features such as factur-x-en16931, zugferd-1, xrechnung-profile | Generate here | 1 |
| Our own Factur-X invoices for each profile (MINIMUM to EXTENDED, XRECHNUNG) and current version, at PDF/A-3b and 3a+UA-1, plus our renditions of received XML, committed | What we produce must be as readable as what we consume; M15, M19, M26 accept on them | documents/invoice/qpdf-invoice-with-facturx-xml.pdf (a stub: undeclared ram: prefix, no PDF/A claim, no /AF) | Generate here | 1 |
| Standalone XML invoices: CII (D16B and later), UBL 2.1 invoices and credit notes, XRechnung 3 in CII and UBL, ZUGFeRD 1 — plus a place in the corpus for non-PDF fixtures | The rule differential against the KoSIT validator and the UBL rendition need XML inputs the corpus holds only inside PDFs | the XMLs embedded in the corpus hybrids (mostly CII; one UBL payload in remote/zugferd-corpus/itext9-facturx-ubl-payload.pdf) | Public source | 1 |
| Negative XML variants, one per rule family (BR-CO totals, VAT categories, BR-CL code lists, BR-DEC, BR-FR, BR-DE, each profile's restrictions), derived by recorded edits | A differential harness fed only valid invoices proves only that both sides stay silent | none (the weclapp one-cent gap is between page and XML, not an XML rule failure) | Generate here | 1 |
| Factur-X 1.08/1.09 (ZUGFeRD 2.4/2.5) samples and an EXTENDED-CTC-FR sample | The current versions, the French reform's profile, and the elements only they define | remote/mustang/dwc-weasyprint-facturx-extended-pdfa4f.pdf and -no-fx-xmp.pdf (2025) | Remote corpus | 2 |
| A Factur-X actually sent by a supplier's ERP, anonymised, that may be committed (W07) | Every real ERP hybrid found carried personal data or is remote; a committed one keeps acceptance in the main CI job | vendor/zugferd/itext-pdfbox-weclapp-facturx-en16931-invoice.pdf (test tenant), vendor/docentric/aspose-d365-facturx-extended-invoice.pdf (demo data) | Contribution | 2 |
| An XRechnung XML together with KoSIT's own visualisation of it | To compare the rendition's field coverage with the official visualisation, field by field | vendor/zugferd/fop26-xrechnung-visualisation.pdf (no XML beside it) | Public source | 2 |
| A PDF/A-3a with PDF/UA-1 Factur-X from another producer | The dual claim is otherwise compared only with PDFlib's A-2a+UA-1 invoice and Symtrax's A-3a without UA | vendor/pdf-association/pdflib-pps-kraxi-pdfa2a-pdfua1-invoice.pdf; remote/zugferd-corpus/symtrax-itextsharp-zugferd21-minimum-pdfa3a.pdf | Contribution | 3 |
| A PDF/A-3 with non-XML associated files (a source office document, a CSV) from another producer | Associated files other than invoice XML are checked on our output only | vendor/verapdf/pdfa3b-embedded-pass.pdf, vendor/bfo/bfo-pdfa2b-embedded-pdf.pdf | Generate here | 3 |

### M15

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| An untagged twin of the two-column report (Chromium's printToPDF with generateTaggedPDF false, or the tagged file with its structure tree stripped by a recorded pikepdf transformation), plus untagged multi-column pages with a known order (two and three columns, full-width headings between column sets) | Every committed multi-column page is tagged, so the roadmap's two-column acceptance would test the structure tree and never the layout analysis; the only untagged two-column article is remote | documents/report/chromium-report-fr.pdf (two-column-text, tagged); remote/opf-format-corpus/jhove-hul-129-latex-distiller705-journal-article.pdf (untagged, remote) | Generate here | 1 |
| Text inside an optional-content group that is off by default, and inside a group shown only when printing | 'Hidden layers honoured' has nothing to hide: the committed hidden groups draw boxes, the committed Watermark and HeaderFooter groups are on | vendor/pdf-association/handwritten-utf16le-strings.pdf (two groups off, boxes only); vendor/uk-ogl/pdfmaker21-ozev-sample-invoice.pdf (Watermark, on); remote handwritten-pdf20-utf8-strings.pdf and the USGS topo map's 31 layers (to be examined) | Generate here | 1 |
| Documents with hyphenated line breaks (hard hyphens at line ends and soft hyphens) in French and English: LibreOffice with automatic hyphenation from an ODT source, Chromium from HTML with &shy; | Search across hyphenated line breaks is a roadmap deliverable and acceptance, and no committed document hyphenates | remote/pdf-association/indesign-cs6-pdfua1-brochure.pdf (soft-hyphen-in-extracted-text, remote) | Generate here | 1 |
| Manifest fields M15 reads: expect.readingOrder, expect.tables, expect.images (from pdfimages -list), expect.hiddenText, expect.textlessPages, written by build_corpus.py or recorded from a person's reading with its reason | Expectations must come from the file and an independent tool, never from our reader; textContains and hasExtractableText alone cannot state order, tables, images or mixed scanned/text documents | expect.textContains, expect.hasExtractableText, expect.attachments | Generate here | 1 |
| The decrypted twin (qpdf --decrypt, a recorded transformation) of vendor/jp-nta/indesign-distiller18-nta-gift-tax-vertical.pdf, with textContains recorded from MuPDF and confirmed by a reader | The only vertical Japanese document without ToUnicode, the case the predefined CMaps exist for, is RC4-encrypted (unreadable until M16) and has no text expectation | vendor/jp-nta/indesign-distiller18-nta-gift-tax-vertical.pdf (encrypted, no textContains) | Generate here | 1 |
| textContains for the 22 committed documents that have none, the 1000-page journal among them | 'Text matches the manifest' asserts nothing on them | documents/stress/reportlab-journal-1000-pages.pdf, the qpdf derivatives, the veraPDF fixtures | Generate here | 2 |
| Korean text, and CJK text in non-embedded fonts through predefined CMaps, that may be committed | The Adobe-Korea1 map has no committed case; the non-embedded CJK fonts are Foxit's, remote | vendor/us-federal/indesign-irs-pub1-chinese-traditional.pdf (CID-keyed CFF); remote/pdfium-tests/foxit-phantompdf-certification-signature-visible.pdf (non-embedded CJK) | Contribution | 2 |
| Tables with merged cells, ruled and unruled, untagged; and a tagged table with RowSpan/ColSpan attributes | Spans are where table detection fails, and no committed table has them | documents/invoice/chromium-invoice-fr.pdf, vendor/pdf-association/pdflib-pps-kraxi-pdfa2a-pdfua1-invoice.pdf, vendor/jasper-modular/openpdf-jasperreports-financial-statement.pdf (no spans) | Generate here | 2 |
| A real supplier invoice or bank statement, untagged, with a line-item table, that may be committed | Line-item detection is proven on our own invoice and published samples; every real one found is remote | remote SAP NetWeaver, Axapta, Scoro, AFP statement; committed JasperReports and weclapp samples | Contribution | 2 |
| Text made invisible by other means than render mode: white on white, outside the crop box, clipped away | The visibility flags feed M19's sanitisation and guard against prompt injection hidden in documents sent to language models; the corpus only has render modes 3 and 7 | OCR layers in render mode 3 (hp-mfp-acrobat-ocr, Xerox copiers); remote photoshop-cc2015-pdfx3-cmyk.pdf (mode 7) | Generate here | 2 |
| Bates numbers applied by another tool (Acrobat, an e-discovery platform) | Chunk anchors are proven on M9's own marks only | none (M9 generates ours) | Contribution | 3 |
| A math-heavy article with inline formulae, from LaTeX and from Word | Formula extraction is not promised, but its failure mode (order and spacing) should be observed | remote pdfTeX dissertations (jhove-hul-138, jhove-hul-80) | Public source | 3 |

### M16

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| Decrypted twins of the six committed encrypted documents that have no unencrypted sibling: IRS 9465 (RC4-40), the PDFWriter 3 DMCA summary (RC4-40), HMRC IHT205 (RC4-128), the NTA gift-tax return, USCIS AR-11 and DoD DD 293 (AES-128) | The roadmap's 'content matches the unencrypted twin it was derived from' has nothing to compare with on these six; M11 already lists the AR-11's twin as its own priority-1 gap | vendor/us-federal/distiller2-irs-9465-1996-rc4-40.pdf, vendor/us-federal/pdfwriter3-copyright-office-dmca-summary-1998-rc4-40.pdf, vendor/uk-ogl/pagemaker-distiller5-hmrc-iht205-form.pdf, vendor/jp-nta/indesign-distiller18-nta-gift-tax-vertical.pdf, vendor/us-federal/livecycle-uscis-ar11-xfa-form.pdf, vendor/pikepdf/livecycle-dod-dd293-aes128-xfa.pdf (the encrypted originals); qpdf --decrypt as a recorded build_corpus.py transformation | Generate here | 1 |
| A document encrypted with the public-key security handler (adbe.pkcs7.s4 and s5; RC4-128, AES-128, AES-256), with its fictitious test certificate and key, and its unencrypted twin | The roadmap's acceptance 'a public-key-encrypted one is reported, never misread' has no document; the key-source seam and M26's decryption need the same fixture | none; pyHanko's MIT test data (to be screened for a public-key file), otherwise generated with pyHanko's public-key handler over its fictitious PKI; an Acrobat certificate-security file as a contribution | Public source | 1 |
| AES-256-GCM (ISO/TS 32003) and integrity-MAC (ISO/TS 32004) documents, standalone and attached to a signature, their twins, and copies tampered by a recorded byte flip inside and outside an encrypted string | Reading GCM and the MAC is a deliverable; invariant 10 wants it proven on files another implementation wrote, and the tampered copies prove content is withheld when a tag fails | none | Generate here | 1 |
| AES-256 R6 documents under non-ASCII passwords (accented, German sharp s, CJK, and one SASLprep maps) | SASLprep and the UTF-8 password path of R6 are otherwise untested; the only R6 file uses an ASCII password | documents/secured/qpdf-invoice-aes256.pdf (R6, password 'corpus') | Generate here | 1 |
| Filled versions of the committed AcroForms (subscription form, SS-4, N208, Cerfa 13983, Cerfa 12156, InDesign PDF/UA form, I-9) with fictitious values, filled by an independent tool (pdftk-java fill_form from a recorded XFDF); the hybrid XFA forms filled through their AcroForm only by pypdf, leaving the XFA stale | The roadmap's XFDF acceptance needs 'a filled corpus form'; flattening needs third-party fills; the stale hybrids are form.xfa-out-of-step's documents | the blank committed AcroForms; the filled XFA form inside remote/opf-format-corpus/acrobat9-portfolio-signed-3d.pdf (remote, XFA only) | Generate here | 1 |
| A DocMDP P=2 certified form filled after certification, one with a FieldMDP lock over some fields, and the same before the fill | Permitted and forbidden fills of a certified form have no document; M4 records the same gap | vendor/us-federal/itext-govinfo-us-code-certified.pdf (P=1, no fillable field); remote/eu-dss/pdfmaker11-nbu-sk-qualified-seal-docmdp-fieldmdp.pdf (P=2 with a lock, no form to fill) | Generate here | 1 |
| An AES-256 R5 document (Adobe extension level 3) | R5 is read but never written; no file exercises its SHA-256 key derivation | documents/secured/qpdf-invoice-aes256.pdf (R6) | Generate here | 2 |
| Committed documents with attachments-only encryption (/EFF), a /Crypt filter on a single stream, and EncryptMetadata false | The crypt-filter read paths and the write options need committed documents; only the remote Adobe Sign file covers EncryptMetadata false and the Identity filter, on the nightly run | remote/pdfcpu-issues/adobe-sign-certified-aes128-test-agreement.pdf | Generate here | 2 |
| An unencrypted wrapper document (ISO 32000-2 §7.6.7, Table 28), shaped as Microsoft Purview writes them, ideally a real Purview-protected file with fictitious content | Wrapper recognition and payload exposure must be proven on a file shaped by the real producer, not only ours | none | Generate here | 2 |
| The 1000-page journal encrypted under AES-256 and RC4-128 | Decryption's memory and throughput budgets need a large encrypted document | documents/stress/reportlab-journal-1000-pages.pdf | Generate here | 2 |
| Forms with multi-select list boxes, push buttons carrying SubmitForm and ResetForm, a rich-text field with a value, password and file-select fields, and AFPercent, AFTime, AFRange_Validate, AFSpecial_KeystrokeEx scripts | No committed form has a list box, and these field types and formats are otherwise tested on synthetic fixtures only | the committed AcroForms (text, check, radio, combo, push, signature; AFNumber, AFDate, AFSpecial, AFSimple_Calculate) | Generate here | 2 |
| A Reader-extended form filled and saved by Adobe Reader, and a hybrid XFA form filled and saved by Acrobat | Whether an incremental fill within UR3 grants keeps Reader's features, and whether Acrobat re-merges datasets it did not write, are asserted structurally only; no container runs Reader or Acrobat | vendor/uk-ogl/indesign-acrobat-hmcts-n208-form.pdf, vendor/us-federal/livecycle-irs-1040-2022-xfa-ur3.pdf (blank) | Contribution | 2 |
| FDF and XFDF exchange files written by Acrobat and by pdftk-java, beside the forms they belong to | The exchange formats have no committed samples, and the manifest describes PDFs only: docs/corpus.md must first decide where such files live | none | Generate here | 2 |
| A form whose calculation uses Acrobat's simplified field notation (/** BVCALC … EVCALC **/) | Recognising it is deferred until a real document carries one | vendor/uk-ogl/pagemaker-distiller5-hmrc-iht205-form.pdf (17 custom calculation scripts) | Contribution | 3 |
| Right-to-left and CJK values in text and comb fields of a third-party form | The shaping report of M16's core appearance generator is checked on our own fills only | vendor/jp-nta/indesign-distiller18-nta-gift-tax-vertical.pdf (CJK, no fields) | Contribution | 3 |
| A form of ten thousand fields | Fill and appearance budgets need a form larger than Cerfa 12156's 419 widgets | vendor/fr-licence-ouverte/pdfmaker-acrobat-cerfa-12156-form.pdf | Generate here | 3 |

### M17

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| Three HTML form templates: sources/subscription-form-fr.html (the ReportLab form's fields and labels), sources/onboarding-pack-fr.html (every mapped and unsupported control, fieldsets, every label source, required/readonly/disabled/maxlength, a password with a value, non-ASCII radio values, a multi-select, optgroups, reset and submit buttons, a rotated control, a textarea taller than a page, an unlabelled control), and the contract template (sources/contract-fr.html with two signature blocks and initials in a running footer) | Every M17 acceptance row is written against them; no corpus source contains a single form control | tests/corpus/sources/contract-fr.html; documents/form/reportlab-subscription-form.pdf | Generate here | 1 |
| Static renderings of the three templates by our engine as approved reference images, and their interactive renderings committed | The visual rows compare interactive with static output; M12's approved references contain no control | documents/*/adcodicem-*-fr.pdf (M12's reference renderings, no controls) | Generate here | 1 |
| The same templates rendered by WeasyPrint with pdf_forms | A second HTML engine's fields to compare names, types and rectangles with, and a third-party generated form for M16 to read | none | Generate here | 2 |
| A PDF/UA-1 form from an authoring tool with a list box, a push button and a signature field, whose claim veraPDF upholds | The tagging shape of those three field types is otherwise checked on our own output only | vendor/pdf-association/indesign15-pdfua1-form.pdf (text, combo, check, radio) | Contribution | 2 |
| Chromium's print of the three templates | How a browser prints the same controls statically, beside M12's rendering | none | Generate here | 3 |
| A form template with right-to-left values and dir="rtl" controls (Arabic, Hebrew) | Alignment (/Q) and shaped values in right-to-left scripts are checked on unit fixtures only | M13's accessibility reference source (Arabic passage), not yet committed | Generate here | 3 |

### M18

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| EML messages covering every construct the converter handles: HTML and text alternatives, inline cid: images, PDF, image and office attachments, a forwarded message/rfc822, encoded-word subjects in UTF-8 and ISO-8859-1, quoted-printable and base64, format=flowed, a remote tracking image, a multipart/signed message | The roadmap's acceptance 'the corpus e-mails convert to pieces' has nothing to run on: the corpus holds PDFs only | none | Generate here | 1 |
| Outlook MSG files with an HTML body, an RTF-encapsulated HTML body, and a plain-text body, with attachments and an embedded message | MSG parsing (MS-CFB, MS-OXMSG, LZFu, RTF de-encapsulation) needs real Outlook output as oracle input | none | Public source | 1 |
| A manifest and schema able to describe non-PDF inputs (.eml, .msg) with expectations of their own: headers, body strings, attachment names and digests | manifest.schema.json admits .pdf only and its use-case enum has no category for messages; the same change M7 needs for image files | M7's identical gap for image files | Generate here | 1 |
| French pleadings (conclusions) citing pieces in every form: 'pièce n° 12', 'pièces nos 3 à 5', '(pièce 2.1)', '12 bis', across line breaks and hyphens, in a table and a footnote, beside 'pièce adverse n° 4' and 'pièces justificatives', from Chromium, LibreOffice and Word, with the expected references recorded | Linking references to pieces is a roadmap deliverable and no corpus document cites pieces | none; M9's stamped volumes contain 'Pièce n°' only in our own stamps, which must never be linked | Generate here | 1 |
| Real e-mails as received from Outlook, Gmail's export, Thunderbird and Apple Mail, anonymised | Each client writes MIME its own way; generated messages prove the parser, not the clients' quirks | none | Contribution | 2 |
| Image files (JPEG, TIFF, PNG) as case-file pieces | Photographs and scanned TIFFs are ordinary pieces and go through M7's image pages | image streams inside committed PDFs only (M7's gap) | Generate here | 2 |
| A case file made by the tools French lawyers use (Kleos, Hub-Avocat, Acrobat by hand): a bordereau and stamped pieces | The verifier should read a real bordereau and the presets' defaults should match what practitioners file | none (M9 lists stamped pieces at the same priority) | Contribution | 3 |
| A filing a court portal refused, with the portal's message | The strongest test of a preset rule is a real rejection it would have caught | none | Contribution | 3 |
| A piece larger than the size caps the portal presets encode | The size rule meets real documents only above 2 MB, which cannot be committed | remote/opf-format-corpus/jhove-hul-117-acrobat101-student-design-report.pdf (15 MB), remote/govinfo/us-code-2023-title42.pdf | Remote corpus | 3 |

### M19

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| PII fixtures: fictitious IBANs, NIRs (with 2A/2B), SIRENs, SIRETs (La Poste's among them), e-mails, French and E.164 phone numbers, dates of birth with cues, valid and invalid twins, in body text, tables, across line breaks and hyphens, with U+202F spaces, and again in form fields, annotations, an outline item, /Info, XMP, alt text and an attached XML, from Chromium, LibreOffice and Word, with expect.detections | Detector acceptance needs documents whose truth is known; the only real-looking values in the corpus (the invoice's client SIREN 552 041 319 and IBAN FR76 3000 4008 2800 0123 4567 890) fail their check digits, and real personal data may never be committed | invoice sources (SIREN 123 456 824 and VAT FR40123456824 valid); Factur-X XML IBANs in mustang-zugferd2-en16931-invoice.pdf and aspose-d365-facturx-extended-invoice.pdf | Generate here | 1 |
| Redactions done wrong: a black rectangle over live text; a name replaced in an incremental update with the old revision still holding it; a document whose earlier revision holds a term the current one still shows | 'Absent from every earlier revision' and hidden.text-covered need the failures they exist for | vendor/us-federal/illustrator-irs-pub1-english.pdf (earlier revision draws text the current does not); remote print-to-pdf-border-force-poster-updated.pdf (/Info replaced) | Generate here | 1 |
| Concealed text: white on white, text under 1 pt, text off the page, text clipped away, text under an opaque shape | The InvisibleText category and five hidden.* rules have no committed case beyond render modes 3 and 7; also the prompt-injection channel | OCR layers in render mode 3 (Xerox, HP/Acrobat, FineReader); remote Photoshop render mode 7 | Generate here | 1 |
| SubmitForm, ImportData, GoToE, Named actions beyond navigation (Print), Hide, SetOCGState, and JavaScript in a link and an outline item | The roadmap names launch and submit actions; a pikepdf walk of the committed corpus finds one Launch action and no SubmitForm or ImportData | vendor/opf-format-corpus/pdfmaker9-word-distiller-external-link.pdf (Launch), make-pdf-javascript-openaction.pdf, the Cerfa and USCIS forms' scripts | Generate here | 1 |
| /Redact annotations left unapplied by Acrobat (OverlayText, Repeat, RO) and the same document after Acrobat applied them | Applying others' marks is in scope and Acrobat's applied result is what users compare with; only PyMuPDF's marks can be generated | none | Contribution | 2 |
| A lossless (Flate or LZW) scan with text in its pixels, with and without an OCR layer | Pixel redaction before M22 is otherwise proven on logos, photographs and Word's text-as-images, not on a scan | vendor/fr-licence-ouverte/word365-dila-text-drawn-as-images.pdf (text as soft-masked Flate images) | Generate here | 2 |
| A JPEG carrying fabricated GPS coordinates in its EXIF, made a page | hidden.image-location has no case; a phone photograph is the ordinary exhibit | EXIF without GPS in the JPEGs of livecycle-uscis-ar11-xfa-form.pdf, the two Legilux files and the DILA notice | Generate here | 2 |
| RichMedia (Flash) and 3D annotations in a committed document | The rich-media category is proven only on the remote portfolio | remote/opf-format-corpus/acrobat9-portfolio-signed-3d.pdf; committed Screen+Rendition in pdfmaker9-word-distiller-embedded-quicktime.pdf | Public source | 2 |
| A tagged PDF 2.0 document, ideally PDF/UA-2 | The /Redaction pagination-artifact subtype exists only in 2.0 output | none committed (M9 lists the same need) | Public source | 2 |
| Type 1 and CID-keyed CFF subsets whose glyphs a redaction leaves unused | Glyph scrubbing is proven on TrueType first; Type 1 is reported, CFF charstrings replaced | pdfmaker707-word-law-library-iraq-legal-history.pdf (Type 1 subsets), indesign-irs-pub1-chinese-traditional.pdf (CID-keyed CFF) | Generate here | 3 |

### M20

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| veraPDF's failed rules (specification, clause, test) per document under its claimed levels and under the 2b and ua1 differential flavours for every document, with veraPDF's version, plus a manifest field recording a disagreement with its reason | The acceptance compares rule by rule and says disagreements are recorded in the manifest; conformanceValid holds one boolean | expect.claimsConformance / expect.conformanceValid on 49 entries | Generate here | 1 |
| Every claim a document makes (PDF/A and PDF/UA together) with each verdict, as a list-valued field; includes recording the PDF/A-4 claim (pdfaid part 4, rev 2020) of vendor/verapdf/pdf20-version-mismatch.pdf, which the manifest omits | PDFlib's invoice claims PDF/A-2a and PDF/UA-1 but the schema holds one; seven PDF/UA claims exist only as features; one PDF/A-4 fail fixture escapes the claimed-document row | claimsConformance (single string); features pdfua-1-claim, pdfua1-claim, pdfua-claim, pdfua-1-claim-xmp, pdfua-claim-invalid-conformance-b | Generate here | 1 |
| Pass and fail fixtures for each (clause, test) of veraPDF's profiles for PDF/A-1a to 4f and PDF/UA-1 | Nine fixtures cannot show that a hundred-odd rules per part fire exactly where they should; the veraPDF corpus encodes the verdict in each file name | vendor/verapdf/* (8 files), vendor/bfo/bfo-pdfa2b-embedded-pdf.pdf | Public source | 1 |
| Our own PDF/A-2b, 2u, 2a and 2a+PDF/UA-1 renderings of the reference documents, committed | Our output must be judged first (ADR 17) and later milestones (M21, M26, M28) accept on it | documents/archival/libreoffice-report-pdfa2b.pdf | Generate here | 1 |
| Third-party PDF/A-2u and PDF/A-4e documents, and committed PDF/A-3a and PDF/A-4f ones | 2u has no document at all; 3a and 4f exist only remote; 4e not at all | remote/zugferd-corpus/symtrax-itextsharp-zugferd21-minimum-pdfa3a.pdf, remote/mustang/dwc-weasyprint-facturx-extended-pdfa4f.pdf | Generate here | 2 |
| The other 33 files of BFO's PDF/A-2 suite (CC BY 3.0) | A second fixture author for part 2, independent of veraPDF | vendor/bfo/bfo-pdfa2b-embedded-pdf.pdf | Public source | 2 |
| The Isartor test suite (PDF/A-1b) | Historical part-1 reference suite and the calibration of PDFBox Preflight, the second-opinion referee | vendor/verapdf/pdfa1b-annotations-pass.pdf, pdfa1b-forms-fail.pdf | Remote corpus | 2 |
| A PDF/A-2b document with a CMYK output intent that veraPDF upholds | The merge's DefaultCMYK path and cross-family conflict have only a rejected CMYK claim | vendor/eu-publications/distiller10-eu-consolidated-regulation-2015.pdf (rejected) | Generate here | 2 |
| A PDF/A-1b and a PDF/A-2b AcroForm that veraPDF upholds | Fills on PDF/A-1 files (CharSet in appearance fonts) are otherwise tested only on the failing fixture | vendor/verapdf/pdfa1b-forms-fail.pdf | Generate here | 2 |
| A signed PDF/A-2 or 3 document with veraPDF's verdict | pdfa-signature rules and the clauses left to the signing satellite have no document; every signed claim is part 1 | remote/boe/* (PDF/A-1a, signed) | Public source | 2 |
| Tagged documents with deliberately poor alternative text, a bold paragraph posing as a heading, an artifact holding real content | The review-item suspicion heuristics need documents where they must fire and must stay silent | committed tagged documents (56) | Generate here | 3 |
| Members of the PDF/UA Reference Suite 1.1 no pass has screened | More PDF/UA-1 claims from other producers | vendor/pdf-association/*pdfua1*, remote/pdf-association/* | Public source | 3 |

### M21

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| For each non-conforming corpus document, its expected conversion outcome: the level veraPDF accepts after conversion, or the hand-reviewed reasons it cannot be converted (a manifest field) | The first acceptance row compares with recorded expectations, not with our converter's output | expect.conformanceValid, expect.unsupported | Generate here | 1 |
| Documents whose missing font is freely available (DejaVu, Noto, Liberation) and not embedded, derived by removing the program with a recorded pikepdf transformation | The lossless exact-face registry path has no document: every missing font in the corpus is proprietary | *not-embedded* documents (27 committed, 20 remote) | Generate here | 1 |
| A document drawing DeviceRGB and DeviceCMYK with no output intent, and one drawing only DeviceCMYK (ReportLab) | Two branches of the output-intent decision table have no document | vendor/eu-publications/distiller10-eu-consolidated-regulation-2015.pdf, vendor/us-federal/illustrator-irs-pub1-english.pdf | Generate here | 1 |
| A CMYK ICC profile whose terms allow redistribution unmodified, vendored for the tests | The CMYK conversion branches need a profile the caller would supply | none | Public source | 1 |
| Derived variants each carrying one harmless forbidden feature: Interpolate true, TR in ExtGState, /Alternates, /OPI, PostScript XObject, reference XObject, halftone type 6, undefined operator outside BX/EX, 128-byte resource name, 40,000-byte content string | Remedies and the pages they change must be proven on documents | none | Generate here | 2 |
| Symbol and ZapfDingbats not embedded, and a symbolic TrueType not embedded | The symbolic font path must refuse without a registered face and succeed with one | non-embedded-standard-14-in-acroform-dr documents | Generate here | 2 |
| A non-PDF/A PDF attached to a document to be converted to part 2 | Recursive attachment conversion has only BFO's PDF/A attachment, which needs none | vendor/bfo/bfo-pdfa2b-embedded-pdf.pdf | Generate here | 2 |
| Real case-file exhibits we may commit: a scanned letter, an e-mail printed to PDF, a contract signed by a commercial service | The case-file acceptance row assembles corpus documents not chosen as exhibits | W03 and W05 documents in the corpus | Contribution | 2 |
| A signed document with a sub-filter part 2 forbids (adbe.x509.rsa_sha1) that the reader supports today | The only one is recorded unsupported until M23 (T24), so no acceptance row can name it | remote/pdfcpu/acrobat-web-capture8-x509-rsa-sha1-signed.pdf (unsupported, T24) | Public source | 2 |
| A committed non-embedded CJK CID font document with its exact face available (Chromium + Noto CJK, program removed) | The Identity-H exact-face path is proven only on remote files and never with the face at hand | remote non-embedded-cjk-cid-font documents (3) | Generate here | 3 |
| A committed rich-media or 3D document | The part-3 kept-aside path for 3D and RichMedia is proven only remotely | remote/opf-format-corpus/acrobat9-portfolio-signed-3d.pdf; committed pdfmaker9-word-distiller-embedded-quicktime.pdf covers video only | Public source | 3 |

### M24

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| Edited copies of the generated contract, report and invoice sources (words changed, a paragraph inserted, a clause deleted, an article moved, an amount changed, a reflow across a page break), rendered by Chromium and LibreOffice, edits recorded in build_corpus.py | 'Exactly the edits made, and nothing else' needs edits known by construction that also reflow text, which in-place edits of a finished PDF never do | tests/corpus/sources/*.html and their Chromium/LibreOffice outputs | Generate here | 1 |
| An edited copy of the 1000-page journal with scattered edits (build_reportlab_stress with a recorded seed) | The comparison's memory budget must be measured on a long document with changes, not on a document against itself | documents/stress/reportlab-journal-1000-pages.pdf | Generate here | 1 |
| More supplier invoice families: three or more invoices of one layout from a real ERP, with or without XML | The template promise is proven on one supplier family (GnuAccounting) and our own invoice | vendor/zugferd GnuAccounting family (505, 506, 502, 507); remote DWC generator pair | Contribution | 2 |
| A contract draft and the signed version returned by a counterparty, from practice | The daily redlining case; everything in the corpus is constructed or one-sided | none; signed documents with later revisions (acrobat-reader-signed-twice, legilux memorials) | Contribution | 2 |
| Multi-page invoices whose line items continue across pages | Template line items are proven on one-page tables only | vendor/jasper-modular/openpdf-jasperreports-financial-statement.pdf (4 pages); chromium-report-fr.pdf (repeated table headers) | Generate here | 2 |
| Scanned versions of committed documents, and a scan against its born-digital original (recorded Ghostscript rasterisation, marked derived) | Visual comparison on scans once M25 and M22 exist | none | Generate here | 3 |

### M28

| Need | Why | Nearest in the corpus today | Source | Priority |
|---|---|---|---|---|
| PDF/UA-2 documents from other producers with veraPDF's ua2 verdict (WeasyPrint or LibreOffice if their pinned versions write part 2; else the veraPDF corpus's PDF/UA-2 files) | The pdf-ua-2 profile cannot be accepted on our own outputs alone (invariant 10); namespace resolution has one hand-written file | remote/pdf20examples/handwritten-pdf20-utf8-strings.pdf (tagged PDF 2.0, no UA-2 claim) | Generate here | 1 |
| Documents declaring Well-Tagged PDF, reuse and accessibility (veraPDF corpus WTPDF files; PDF Association publications that declare WTPDF) | The WTPDF profiles and the declaration reader have nothing third-party to judge | none | Public source | 1 |
| Pass and fail fixtures for each clause of veraPDF's ua2, wt1r and wt1a profiles | Profiles of dozens of rules need a pair per rule | none | Public source | 1 |
| Our own PDF/UA-2, WTPDF and PDF/A-4/4f/4e renderings of the reference documents, committed | Later milestones (M30 above all) accept on them | none | Generate here | 1 |
| The PDF/A-4 claim of vendor/verapdf/pdf20-version-mismatch.pdf recorded with veraPDF's verdict (shared with M20's list-valued claim field) | The fixture escapes every claimed-document row | vendor/verapdf/pdf20-version-mismatch.pdf | Generate here | 1 |
| PDF/A-4e documents with 3D or RichMedia that veraPDF upholds, one committed | 4e's allowances are checked only on a remote portfolio whose members claim no part 4 | remote/opf-format-corpus/acrobat9-portfolio-signed-3d.pdf | Public source | 2 |
| A PDF 2.0 document using structure destinations from another producer | SD resolution and M7's rewriting are checked on our output only | none | Remote corpus | 2 |
| A PDF 2.0 document mixing the 1.7 and 2.0 namespaces with RoleMapNS from another producer | ISO/TS 32005 reading is otherwise proven on trees we built | remote/pdf20examples/handwritten-pdf20-utf8-strings.pdf | Generate here | 2 |
| A PDF 2.0 document with page-level output intents (pdf20examples, CC BY-SA, remote) | Part 4 allows them and merges must keep them consistent; none in the corpus | none | Remote corpus | 2 |
| PDF Declarations other than WTPDF's, derived with pikepdf's XMP editing | The reader's handling of declarations it does not know | none | Generate here | 3 |
