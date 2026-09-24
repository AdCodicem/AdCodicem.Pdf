# Contributing documents to the corpus

What to hand over, in what shape, and where to put it. The policy behind all this — why the corpus exists
and what closing a milestone requires of it — is in `docs/corpus.md`.

## Why your documents and not more of ours

The corpus covers four producers we can drive in a container — Chromium's Skia backend, LibreOffice,
ReportLab and qpdf — three office writers driven on Windows — Word's Save as PDF, the Microsoft Print to
PDF driver and the PDF24 printer — and 84 third-party files found in public sources under attribution-only
licences: conformance fixtures from veraPDF and BFO, government documents from five countries and the EU,
specimen invoices from ERP and invoicing tools, and regression files from other PDF libraries.
`docs/corpus-sources.md` says where each came from, which other libraries' corpora were examined, and why
most of them could not be used.

What public sources cannot give us is what arrives in a real inbox: a bank statement out of a Java stack,
a supplier's Factur-X straight from its ERP with real data, a contract signed through Yousign or DocuSign,
a scan as the office copier wrote it, or a file that broke something last week. Those almost always carry
someone's address or account details, so they cannot be found — only contributed, and anonymised. Each one
that enters the corpus stays there, checked on every commit, for the life of the project.

The first three third-party files we added found a real defect within a minute — a validly compressed
empty stream being read as a decoding failure. The search that brought the next seventy-six found a test
that could not read a page count qpdf printed with a warning, and a reader that calls a sound stream
truncated when its end falls just past an 8 KB window. That is the return on this.

## What is wanted

Priority **1** blocks a milestone, **2** materially improves one, **3** is opportunistic.

| # | Document | Why it matters | Unblocks | Priority |
|---|----------|----------------|----------|----------|
| W01 | **Microsoft Word → PDF**, both variants: "Save as PDF" *and* "Microsoft Print to PDF" | Two different writers, both ubiquitous; the print driver in particular produces structures nothing else does | M1, M2 | 1 |
| W02 | **Adobe** output: Acrobat, Distiller or an InDesign export | The reference implementation's own conventions, including its take on object streams and metadata | M1, M2 | 1 |
| W03 | **A scan from your office copier** (Xerox, Canon, Ricoh, Konica…), ideally one with an OCR text layer and one without | Image-only pages, CCITT and JPEG encodings, and the invisible-text layer OCR adds — extraction must tell them apart | M2, M10 | 1 |
| W04 | **An invoice or statement received from a supplier or bank** | These come out of Java stacks (iText, JasperReports, SAP, Crystal) we cannot run here, and they are the highest-volume documents the library will read | M1, M2, M10 | 1 |
| W05 | **A PDF signed electronically** (Yousign, DocuSign, Universign, a qualified provider…) | The only way to prove that conservative repair and incremental update leave a signature intact | M3, M4 | 1 |
| W06 | **Anything that broke one of your tools** — failed to open, opened wrong, printed wrong | Impossible to synthesise faithfully, and the most valuable file in any corpus | M1, M2, M4 | 1 |
| W07 | **A real Factur-X or ZUGFeRD invoice** from a supplier's ERP | Real ones differ from published samples in the details that matter: attachment relationship, XMP extension schema, PDF/A-3 claim | M12 | 2 |
| W08 | **An interactive form you actually use** — an administrative form, a Cerfa, an internal one | Field appearances, calculated fields, and the XFA forms that Adobe's tooling still emits | M11 | 2 |
| W09 | **A document in a non-Latin script**: Arabic or Hebrew (right to left), Greek, Cyrillic, or CJK | Shaping, bidirectional text and CID font handling cannot be judged on French alone | M6, M10 | 2 |
| W10 | **An old archive document**, PDF 1.2 to 1.4, ideally pre-2005 | Encodings, font formats and structures that no current producer emits but that archives are full of | M1, M2 | 2 |
| W11 | **A very large document**: a heavy scan, a catalogue, a plan | Memory behaviour is a promise, and promises need a document that would break a careless implementation | M13 | 3 |
| W12 | **A PDF/A produced by someone else's tooling**, with its validation report if you have one | An independent opinion on conformance, to check ours against | M12 | 3 |

Nothing here needs to be pretty, recent, or a good example. A file is interesting because of how it was
made, not because of what it says.

### What is already in, and what is still missing

After two passes over public sources on 2026-09-24 (`docs/corpus-sources.md`), the corpus holds something
for every line, and for most of them several producers. What is still wanted is what only a contribution
can bring: documents that went through a real inbox.

| # | In the corpus | Still wanted |
|---|---------------|--------------|
| W01 | Word 2010, 2019 and Microsoft 365 Save as PDF; Word for Mac through Quartz; Print to PDF from Word and from Excel, untouched; our own invoice through Save as PDF, the print driver and PDF24 | — |
| W02 | PDFMaker 5, 9.1, 10, 21 and 25 for Word; Distiller 2 to 10; InDesign; PageMaker; Illustrator; LiveCycle Designer; Acrobat Pro DC and Reader X re-saves; usage rights | — |
| W03 | Two Xerox copiers with their own OCR layer, Xerox JBIG2 without text, an HP MFP with Acrobat's OCR, a scanner's CCITT G3 file, a 1999 CCITT import; remote: a Ricoh MP C3003 scan through 3-Heights | A copier file untouched since the copier wrote it (the Xerox ones were later re-saved), from another brand — Canon, Ricoh, Konica |
| W04 | JasperReports 7 on OpenPDF, iText 2.1.7 then PDFBox (weclapp), PDFBox 3 (Swiss QR-bill), PDFlib's Java binding, Apache FOP — all with fictitious data; remote: a real SAP NetWeaver statement | **A real invoice or statement from a supplier or bank** we may commit: SAP, Crystal, a bank's own stack |
| W05 | A GPO certification, DILA's Dictao signature, a qualified seal renewed by timestamps over two years, a PAdES B-LTA seal, two Acrobat Reader signatures | **A commercial e-signature**: Yousign, DocuSign, Universign, Adobe Sign |
| W06 | A really damaged GovDocs1 file, a GovDocs1 error file, a scanner file that broke pikepdf, forms that broke pdf.js, SafeDocs lexer and dialect tests | Anything that broke your own tools |
| W07 | Factur-X from weclapp and from Dynamics 365 (demo data), ZUGFeRD from GnuAccounting and from the Mustang library; remote: the FNFE-MPE's French example and intarsys's EN 16931 sample | A Factur-X a supplier's ERP actually sent, anonymised |
| W08 | Blank XFA forms (IRS, USCIS, DoD, a Cerfa), JavaScript AcroForms (USCIS I-9, HMRC), a calculation order, an OmniForm form, tagged forms | A form that is filled in — with fictitious values |
| W09 | Arabic, Russian, Greek, Traditional Chinese (2004 and 2017), vertical Japanese without ToUnicode; remote: Hebrew (a US Census guide) and Arabic shaped into CID fonts by WeasyPrint | Korean; Hebrew and shaped Arabic we may commit |
| W10 | PDF 1.2 to 1.4 from 1995–2004: PDFWriter 3.02 and 4.05, Distiller 2, 3 (Windows and Mac), 4 and 6, PDFMaker 5, Acrobat 3, RC4-40 | A file from before 1996, PDF 1.0 or 1.1 |
| W11 | Nothing committed, by design; remote: the US Code's Title 42 (9,302 pages) and a USGS topographic map (one 63 MB page), fetched and tested every night | The heavy scan (USGS Professional Paper 1, 147 MB), left for M13 to fetch; the map's acceptance waits on T21 |
| W12 | PDF/A from PDFlib, Antenna House, Distiller, 3-Heights, BFO, Mustang, Aspose, OpenOffice and Word via PDFMaker, and a false claim, each with veraPDF's verdict | A PDF/A-3 from a real ERP with its validation report |

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
      name alone is acceptable, and fabricated or specimen data is always fine. Redacting in a PDF viewer
      often only draws a black rectangle over text that is still there — check by selecting the text
      underneath, or by extracting it.
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
night by the `Remote corpus` workflow (ADR 32). Send the URL rather than the file; `tests/corpus/README.md`
says how an entry is written. A document of yours that is not public belongs in the private corpus, not
here: the manifest is published, and it lists only files anyone can already download.

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

One entry per document. Everything except `expect` is provenance; `expect` is what the tests assert.

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

Two rules about `expect`:

- **`refereeCheckSucceeds` is filled in by the build script**, which runs the very command the integration
  tests run — `qpdf --check` — rather than guessing what it ought to say. Damage and rejection differ:
  junk before the header shifts every offset and qpdf adjusts without complaint, while an encrypted
  document it has no password for is refused although nothing is wrong with it.
- **Establish the rest with an independent tool**, never with our own reader. An expectation derived from the
  code under test proves nothing. `pikepdf`, `qpdf --check` and `pdftotext` are the referees in use.
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
