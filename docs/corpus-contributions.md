# Contributing documents to the corpus

What to hand over, in what shape, and where to put it. The policy behind all this — why the corpus exists
and what closing a milestone requires of it — is in `docs/corpus.md`.

## Why your documents and not more of ours

The corpus already covers four producers we can drive here: Chromium's Skia backend, LibreOffice,
ReportLab and qpdf, plus conformance fixtures from the veraPDF corpus. Between them they exercise
cross-reference streams and classic tables, object streams, subsetted Type0 fonts, encryption,
linearisation and PDF/A claims.

What they cannot give us is the rest of the world: Word's own writer, Acrobat, a scanner's firmware, the
Java stack behind a bank statement, a twenty-year-old archive, or a file that is subtly broken in a way
nobody would think to synthesise. Those are the files the library will actually meet, and each one that
enters the corpus stays there, checked on every commit, for the life of the project.

The first three third-party files we added found a real defect within a minute — a validly compressed
empty stream being read as a decoding failure. That is the return on this.

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

## What makes a usable sample

- **Small.** One to five pages is plenty, except W11 where the size is the point. Under 2 MB unless the
  document's weight is what we are testing.
- **Whole.** Do not re-save it, do not "clean" it in Acrobat, do not run it through another tool. The
  producer's original bytes are the entire value; a file re-saved by another program is a sample of that
  other program.
- **Accompanied by a sentence.** What it is, where it came from, and — for W06 — what went wrong and in
  which tool. Two lines are enough, and they become the manifest entry.

## Before you hand anything over

This repository is **public**. Everything committed to `tests/corpus/documents` or
`tests/corpus/vendor` is published, indexed and irrevocable. Run through this list first:

- [ ] **Visible content**: no names, addresses, account numbers, amounts you would not print in a
      newspaper. Redacting in a PDF viewer often only draws a black rectangle over text that is still
      there — check by selecting the text underneath, or by extracting it.
- [ ] **Metadata**: `/Info` and XMP carry author, company, the local file path and sometimes the template
      used. Say so if you want them stripped; I will do it and record that the file was modified.
- [ ] **Hidden material**: attachments, annotations and their authors, form field values, layers turned
      off, earlier versions kept by incremental updates. A PDF can hold several years of edits.
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

Then add its manifest entry to `tests/corpus/manifest.json`, or tell me the two lines about the file and
I will write the entry, establish the expectations with an independent tool, and add it to the acceptance
tests.

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
    "textContains": ["Facture", "Total TTC"]
  }
}
```

Two rules about `expect`:

- **Establish it with an independent tool**, never with our own reader. An expectation derived from the
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
