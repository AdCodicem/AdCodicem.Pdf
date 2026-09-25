# Project status

A living file, updated **at the end of every session**. It describes the real state, not intentions.
Keep it short: summarise the journal once it passes a dozen entries — the detailed history is in git, not
here.

## At a glance

- **Current milestone**: M2 — Document validation (`docs/milestones/M2.md`), not started
- **Last milestone closed**: **M1 — Object model and tolerant reading**
- **Builds**: yes, with no warnings — **Tests**: 481 unit (4 skipped by design: two corpus
  documents recorded as unsupported until M2) + 302 integration (skipped without Docker);
  with the remote corpus fetched (all 153 of its documents, on the runner), 806 unit (35 skipped by
  design, on documents recorded as unsupported until M2 or until T21, T23, T24 or T25 is fixed) + 563
  integration — **CI**: green on `main`, `OpenSSF Scorecard` included
- **Corpus**: 168 committed documents, 23.0 MB — 19 generated here, 3 from Word and PDF24 on Windows, 146
  third-party files under attribution-only licences (56 of them from the Open Preservation Foundation's
  format-corpus, added on 2026-09-25; see `docs/corpus-sources.md`). Beside it, a **remote corpus** of 153
  documents we may use but not redistribute — never committed, fetched at a pinned SHA-256 and tested every
  night by the `Remote corpus` workflow (ADR 32, accepted). All 321 are described in one file,
  `tests/corpus/manifest.json`. Pull request [#25](https://github.com/AdCodicem/AdCodicem.Pdf/pull/25),
  which brought the remote corpus, was merged on 2026-09-24. Its first nightly run, on `main` on
  2026-09-25, passed every test (538 unit, 361 integration) but failed its last step: the Internet
  Archive's copy of the Massachusetts COVID-19 dashboard timed out three times, so it ran without that
  one document. The same file downloads without trouble from here; one slow night is not yet a trend.
  Run 2, dispatched by hand on `claude/format-corpus-integration-udjoo6` the same day, fetched all 153
  documents and passed 806 unit
  tests (35 skipped by design) and 563 integration tests
- **Supply-chain score**: **6.6/10** as published on `6bacbb2`, up from 5.5. The branch below is measured
  to take it to **7.1**; everything above that needs repository settings or people, not code — see T15,
  T18 and T19
- **Pull requests**: versioned documentation (ADR 31) is [#23](https://github.com/AdCodicem/AdCodicem.Pdf/pull/23),
  merged once green: after it, every merge redeploys the site.
  [#22](https://github.com/AdCodicem/AdCodicem.Pdf/pull/22), the site repair, is merged and deployed.
  [#11](https://github.com/AdCodicem/AdCodicem.Pdf/pull/11), [#1](https://github.com/AdCodicem/AdCodicem.Pdf/pull/1)
  and Dependabot's action bumps are merged too; the preview/stable release split (ADR 30), the baseline fix
  and the Scorecard action ref are all on `main`
- **`main` has a ruleset since 2026-09-22** ("Default": pull request with one code-owner approval, linear
  history, CodeQL, coverage). Only repository admins bypass it — and the stable release pushes its
  `chore(release)` commit to `main` as GitHub Actions. **The first stable release will fail at that push**
  until the ruleset grants GitHub Actions a bypass, as T15 said it would have to. Nothing is published
  when it fails: the push comes before the packages do
- **Tagged**: `v0.1.0` on `2808d2f` — the starting point semantic-release continues from. No package exists
  for it, by design.
- **Nothing on `main` is red any more.** The publishing-identity guard that used to stop `Release` (T11)
  went with the merge of #11, and `Documentation` deployed successfully on 2026-09-19 (run 2).
- **The site at <https://adcodicem.github.io/AdCodicem.Pdf/> works**, for the first time: until 2026-09-22
  every user-facing page showed its own compiled JavaScript as text. Repaired by #22 and redeployed by
  hand the same evening; the site check passes on all 74 published pages. **In flight**: versioned
  documentation — stable lines behind a selector, the latest preview behind a button, redeployed with
  every preview (ADR 31).
- **`OpenSSF Scorecard` is fixed and proven**: the `v2.4.4` pin merged, run 13 went green, and
  `api.scorecard.dev` now serves a report — so the README badge finally has a number behind it.
- **Published**: [`AdCodicem.Pdf`](https://www.nuget.org/packages/AdCodicem.Pdf) is on nuget.org —
  `0.1.1-preview.10` through `0.1.1-preview.20` at least (2026-09-22), previews from `main`, the first packages this project has
  ever shipped. Trusted publishing works end to end; nothing long-lived is stored anywhere.
  `dotnet add package AdCodicem.Pdf --prerelease`
- **No stable release yet**: `v0.1.0` is a tag with nothing behind it, by design, and the stable path has
  still never run. Every merge into `main` now publishes a preview on its own.
- **Coverage**: 82.61% on Codecov, linked and uploading without a token. Its GitHub App is not installed,
  which Codecov warns about on every pull request (T14).
- **Branches**: five merged pull requests left their head branches on the remote.
  `claude/dependabot-prs-review-p3mx79`, `claude/scorecard-pipeline-47mgrj` and
  `claude/scorecard-improvement-4ckfxd` hold nothing `main` does not, and are deletable as they stand.
  `claude/nuget-pdf-html-dotnet-msyz8z` and `claude/package-preview-deployment-h0lakt` each kept commits
  pushed after their merge; what was still true on them is in this change, so they are deletable too once
  it lands

### Current measurements (BenchmarkDotNet, ShortRun)

| Operation | Document | Time | Allocated |
|---|---|---|---|
| Indexing | synthetic, 1000 pages, ~4 MB | 229 µs | 393 KB |
| Indexing, then reading every page | synthetic, 1000 pages, ~4 MB | 6.2 ms | 5.9 MB |
| Indexing and walking the page tree | real ReportLab document, 1000 pages | — | 2.4 MB |

The gap between the first two rows is the library's promise: opening a document does not read its content.
Indexing costs roughly 200 bytes per object, whatever the objects weigh. The third row is asserted as a
budget in CI (`CorpusReadingTests`), so an allocation regression fails the build.

## Next concrete step

M2 — document validation (`docs/milestones/M2.md`). Its first slice is the findings, the report and the
rule engine end to end with a single trivial rule; the acceptance to keep in view is that no well-formed
corpus document from any of the four producers earns an error-severity finding, and that a PDF/A-invalid
file earns no *structural* one.

**The milestones were renumbered** when validation and repair were inserted: validation is now M2 (right
after reading) and repair M4 (right after writing). Numbers in commits older than 2026-09-13 refer to the
previous ordering, where M2 was writing and M3 assembly.

## Journal

### 2026-09-25 — The OPF format-corpus, file by file: 123 more documents, 56 of them committed
- **The question**: the Open Preservation Foundation's format-corpus holds 293 PDFs; why had only nine
  entered? Because the passes of 2026-09-24 hunted for the W lines and took from it only what filled one;
  the other 284 had never been examined, turned down or listed. The survey row calling the corpus usable
  was also incomplete, and is corrected. **The request**: take in all that can be, committed where
  possible, remote otherwise — the diversity of producers is the point.
- **The screen**: all 284, by script and by eye — every page's text, metadata, annotations, form and XFA
  values, attachments (spreadsheets, Word files, videos), signatures, the strings of every object and
  every revision, and page images where there is no text layer, which caught an e-mail address printed
  inside a scan. One independent verifier then re-screened the 131 files admitted, OCR included, told to
  reject on doubt: it turned down six — a nurse's extension split over two lines, an e-mail with a space
  after the `@`, contact details printed only in two scans, an essay on its author's own health, a
  classroom-only licence —, moved a Census section with copyrighted tables to the remote corpus, and
  flagged two more that were excluded on doubt: an iPhone video carrying where and when its identifiable
  author filmed it, and a fact sheet too mangled to be read whole.
- **56 committed** (6.2 MB): the Cabinet of Horrors' 16 remaining files and Acrobat 11's three Image
  Conversion pages, the Save As corpus (OpenOffice.org 3.2 and 3.3, LibreOffice 3.5, RC4-128 with and
  without an open password), Pages '09, iBooks Author, calibre through pyPdf and PoDoFo, an InDesign CS
  flyer, and 15 GovDocs1 files that are federal staff's work (VA, Census, USGS, Congress, the Law Library).
- **67 remote** (216 MB): two Cabinet files, 20 GovDocs1 files too large or not shown to be federal staff's
  work, and 45 files filed against JHOVE, each under the error it raised.
- **161 refused**: 68 for personal data — authors' e-mail addresses in 49 JHOVE files, named staff's
  direct lines in GovDocs1 ones —, two for a restriction of purpose, one that could not be read whole, one
  AppleDouble fork that is not a PDF, and **all 89 OPF copies of the iPRES 2017 hand-built set**: git stripped five carriage returns from
  87 of its 88 test files and from the page they derive from, as the authors' archive on RADAR (CC BY-SA
  4.0) shows byte for byte; the 89th, `minimal_test.pdf`, is intact but holds only a header. The originals
  are the best external test suite M2 could have, but RADAR serves them only as one tar; T26 and ADR 33
  record what fetching them takes.
- **What the reader made of them**: none of the 131 first admitted crashed or hung it; of the 123 kept, 113
  open as their entries say, and ten are
  recorded as unsupported. **T25** (new): a trailer's `/Prev` 12 bytes past the older section's `xref`
  keyword makes the reader drop that section — 4,106 entries of IBM's QMF manual — without a diagnostic,
  where qpdf reports it and rebuilds. T21, T23 and T24 each gained a document: T24 from the other side, a
  273 KB table read again from its start each time its window grows. Five files wait for M2, now named in
  its acceptance conditions: two catalogues without `/Type`, a null kid in a page tree, and two trees whose
  kids point at missing objects — qpdf, poppler and PDFium count those as blank pages, pikepdf and the
  reader skip them.
- **Expectations**: pages by `qpdf --show-npages` (pikepdf, used before, disagrees with it on five damaged
  files), verdicts by qpdf 11.9.1 in the integration container, text by pdftotext, PDF/A by veraPDF 1.30.2.
  qpdf says nothing about a MacBinary header or a `data:` URI before `%PDF`; those two files expect the
  offset adjustment anyway. Two files expect the rebuild only after a full read, as qpdf checks every
  offset at opening and the reader does not.
- **Tests**: 481 unit and 302 integration without the remote corpus; with 151 of its 153 documents, 804
  and 560, all green; with all 153, on the runner (`Remote corpus` run 2), 806 and 563, all green.
  Fetching the remote corpus from this session got 151 of 153: the two GitHub issue attachments of
  the third pass answer 403 to a cloud session, whose GitHub proxy lets it reach only its own repositories.
  The runner is not bound that way: the first nightly run fetched both.
- **Afterwards**: the first two `Remote corpus` runs, recorded under *At a glance*. T25 checked against
  `PdfFileReader.TryReadXRefChain`, which returns success when a later section fails as long as an earlier
  one was read; its row now says the drop is silent only when the `/Prev` lands inside the file. The RADAR
  lead became **T26** and a proposed **ADR 33**: the tar is the 2017 deposit, whose MD5 RADAR publishes; it
  holds 88 test files — one more than the OPF's copy, `T04_019`, a trailer pointing at the wrong
  cross-reference offset — and neither reference file; its files hold more than their authors' deviation
  (stale offsets in 43 of 59 header and body files). Two independent checkers reviewed the first draft
  against the tar, the code and the corpus rules; their corrections are in. M2 names the set as the input
  its structural profile is still missing.

### 2026-09-25 — The nightly fuzzing campaign filled its runner's disk
- **Symptom**: run 11 of `Fuzzing` failed after 33 minutes with its step still "in progress" and no
  log to download: the runner was lost, not a test.
- **Cause**: `FuzzingTests.Survives` built its assertion messages with `Save(input, what)` inside the
  interpolated reason. That string is built before the assertion runs, so **every** mutated input was
  written to the temp directory, passing or not. The file name carries the seed, so nothing was
  overwritten. With the corpus going from 27 documents to 112, 20,000 mutations per seed wrote tens of
  gigabytes. Reproduced locally: 30 GB in `/tmp` before the disk filled, and the tests then failed on
  I/O.
- **Fix**: the input is saved only once a budget is exceeded. Measured again at 20,000 iterations over the
  112-document corpus: 137 of 137 passed in 17 min 30 s, with no file written. That leaves room under
  the workflow's 60-minute limit.

### 2026-09-24 — The leads held back by their licence: 82 more documents, 76 of them remote
- **The request**: now that ADR 32 gives a place to files we may not redistribute, follow the leads
  `corpus-sources.md` had held back for their licence, and reconsider the five files once refused for size
  alone. Eleven research groups: W05 signatures (pdfcpu's test data, EU DSS's test resources, Foxit's files
  in `pdfium_tests`, node-signpdf, the BOE's sealed gazette, a hunt for commercial e-signatures), W04 real
  invoices and statements, W07 vendors' Factur-X and ZUGFeRD samples, government leads, W06 files that
  broke pdf.js, PDFBox, PDFium and pdfplumber, and ShareAlike sets. They examined 113 candidates and
  proposed 80, each then adversarially verified — bytes downloaded again and hashed, licence, personal
  data, expectations against qpdf, poppler and veraPDF —; 79 survived, and 158 further leads were set
  aside, each with its reason.
- **Six committed**, each under an attribution-only licence and under 2 MB, after a final independent
  review of bytes, licence and personal data: node-signpdf's own two files (MIT), a signature whose
  `/Reason` contains the keyword `trailer` and an unsigned placeholder; the ZUGFeRD corpus maintainer's
  factur-x Python output with a false PDF/A-3b claim (Apache-2.0); the Federal Reserve Board's SR 01-15
  attachment, FineReader 8 OCR under CCITT pages (the Board's public-domain notice); the UK OZEV sample
  invoice (OGL v3); and a GSA Standard Form 30 completed in DocuSign (US federal, public domain) — **the
  first commercial e-signature the public corpus holds**.
- **76 remote, 86 in all**: a Konica copier's untouched scan with its own OCR, the file W03 lacked; Adobe
  Sign, Yousign, two more DocuSign files and Universign timestamps; PAdES
  B-B to B-LTA, qualified seals, 24 signatures in 48 updates; real invoices and statements from SAP,
  Axapta, Scoro, PDFlib on z/OS and a bank's AFP batch processor; eight vendor samples of Factur-X,
  ZUGFeRD and Order-X; files that broke pdf.js, PDFBox, PDFium, pdfplumber, PdfPig and OCRmyPDF; and USGS
  Professional Paper 1 (147 MB), so all three W11 references are now fetched and tested every night. The
  committed corpus is 112 files, 16.8 MB: 19 generated, 93 from elsewhere.
- **The five refused for size alone, reconsidered** under the new rule: the Open Preservation Foundation's
  signed 3D portfolio (CC0, 4.7 MB) went remote, as did PDF/UA Reference Suite 2-09, a tagged scan of
  10.6 MB, and 2-08, a textbook chapter of 2.3 MB whose content is a publisher's that the suite's CC BY
  cannot be shown to cover. The NIST request for quotation scanned on a Canon SC1011 was refused: it prints
  two named staff members' direct phone lines and own e-mail addresses, and Acrobat had re-saved it anyway.
  Suite member 2-01, a Danish magazine of 12.9 MB, was refused: it gives health conditions of named people,
  children among them. And 2-06, a 1.65 MB brochure first held back for naming a photographed person, went
  remote: its cover photograph carries a third party's copyright the suite licence cannot cover.
- **The maintainer's rulings on borderline cases**, now rules in `corpus-contributions.md`: a software
  library author's own e-mail inside that library's copyright string compiled into the file is a library
  credit, and acceptable; an anonymous photograph whose caption describes a health condition is
  anonymised, and acceptable; images of named people's handwritten signatures are treated like names,
  acceptable even in a committed file — which moved the SF 30 from remote to vendored; fabricated test
  e-mail constants at real domains are fictitious data. Remote use is acceptable under terms that restrict
  reuse — conditions beyond attribution, non-commercial reproduction, fair use only, no modification or
  commercial use — but not "educational use only": California Geological Survey Note 17 was excluded for
  it.
- **Excluded, notably**: the Mustang library's Oracle Reports invoice, whose XML
  gives an employee's direct line and e-mail; Adobe's well-known signed sample, whose signer certificate
  carries an employee's own e-mail. Most of the EU DSS files set aside failed the same way: personal
  signing certificates carry e-mail addresses, phone numbers and national identity numbers.
- **What the reader made of them** — the library is not changed on this branch; gaps are recorded, as
  ever. **T23** (new): an indirect object longer than the reader's 8 KB window is cut at the window's edge
  — a DSS `/VRI` dictionary of 10,112 bytes in the file with 24 signatures, 36 structure arrays of about
  8.7 KB in the 2015 BOE law — where qpdf reads them whole. Both entries are unsupported until it is
  fixed, before M2 closes. **T24** (new): each cross-reference section is read through a window of up to
  64 KB whatever its size, so opening a 218 KB signed web capture with three sections reads 117 KB —
  bounded, but proportional to the number of sections; that entry is unsupported, for M13.
- **Recorded without a new debt row**: two files where the reader is silent while qpdf reports damage — an
  `/Info` without `endobj`, names containing `#00` — are unsupported until M2, like the two hand-written
  ones before them. PDFBOX-3947, a zeroed block that destroyed one object stream: the cross-reference
  reads as written, and the reader rebuilds the index when it meets the destroyed stream, and reports it —
  so the entry expects no rebuild at opening and the rebuild diagnostic after a full read. Two truncated
  PDFBox files record no page count: qpdf 11.9.1 finds no `/Root` in one; on the other, qpdf 11.9.1,
  qpdf 12 and PDFium disagree (11, 4 and 8 pages).
- **The laziness test** now honours `unsupported`, like the other acceptance tests, and skips an encrypted
  document visibly until M11 brings decryption. It no longer applies to documents whose index must be
  rebuilt: a rebuild scans the file by definition. With the remote corpus fetched, 539 unit and
  363 integration tests pass; without it, 346 and 208.

### 2026-09-24 — Size is a recommendation; over 2 MB a document goes remote
- **The maintainer's rule**: the corpus search refused files over 2 MB, and so lost five that would have
  filled real gaps — a PDF portfolio, a Canon scan with its own OCR, a tagged scan, two PDF/UA files. Size
  no longer turns a document down. Keeping committed files small is a recommendation; a document over
  2 MB is not committed, whatever its licence, and goes to the remote corpus, fetched from its public URL.
  The private corpus stays for confidential documents only, never for heavy ones.
- **Where it is written**: `corpus.md`, `corpus-contributions.md`, `tests/corpus/README.md`, `CLAUDE.md`,
  and a second dated amendment to ADR 32 ("too large to commit" means over 2 MB). `corpus-sources.md`
  keeps the rule the search applied, with the revision beside it. `corpus.md`'s vendor row still said
  "no real person's name anywhere", from before the personal-data rule was relaxed; corrected.
- **Enforced**: `A_committed_document_weighs_at_most_2_MB` fails on a committed file over the threshold.
  The largest today is the generated 1,000-page journal, 1.56 MB.

### 2026-09-24 — One corpus manifest instead of four files
- **Why**: 87 of the manifest's 106 entries were copies of `vendor.json` and `contributed.json` — two
  records of the same truth, every addition a double diff, and nothing to stop a correction landing in the
  copy. `remote.json` held a tenth of the corpus in the same format. The maintainer chose one file.
- **What changed**: `tests/corpus/manifest.json` now describes all 116 documents; the three listings are
  gone, their entries moved without a change (checked field by field). `build_corpus.py` marks the entries
  it writes `"builtBy": "build_corpus.py"` and replaces only those — `origin` could not tell it, since the
  Microsoft print-driver file is `derived` but comes from `build_word.ps1` — and gives every other entry
  nothing but the referee's verdict; `--committed-only` and `--remote` now only refresh verdicts in place,
  and both left the manifest byte-identical in the container. `fetch_remote.py` reads the entries of origin
  `remote`, and `Corpus` drops those whose file was not fetched. `private.json` stays apart: git ignores it.
- **A new check in the main job**: `Remote_documents_are_pinned_and_kept_where_git_ignores_them` fails on a
  remote entry outside `remote/`, a file under `remote/` not marked remote, or a missing URL or SHA-256 —
  the nightly job used to be the first to read those entries. 333 and 196 tests pass without the remote
  files, 355 and 216 with them. ADR 32 carries a dated amendment: the decision is unchanged, only the file.

### 2026-09-24 — ADR 32 accepted and implemented: the remote corpus
- **The decision**: documents we may use but not redistribute — bug-report attachments, vendors' samples,
  ShareAlike sets, files too large to commit — are described in `tests/corpus/remote.json`, fetched on
  demand at a pinned SHA-256, and never committed, nor anything derived from them. Accepted by the
  maintainer the day it was proposed.
- **What implements it**: `build/fetch_remote.py` (standard library only; refuses a changed file instead
  of trusting it; tells an unavailable document from a refused one), `build_corpus.py --remote` (the
  referee's verdict, as for every other entry), `Corpus` merging the remote entries whose file is present,
  `tests/corpus/remote/` ignored by git, and a `Remote corpus` workflow — nightly and on dispatch — that
  runs both suites over what it fetched and reports a missing document as such, not as a failing test.
- **The first ten documents**, from the leads the search held back: the Ricoh copier scan and the SAP
  NetWeaver statement kept by pdf.js, the FNFE-MPE's and intarsys's Factur-X samples, three ShareAlike
  files (pdfTeX, Google Docs, WeasyPrint in Arabic), the Census Bureau's Hebrew guide, and two of W11's
  heavy references. Expectations established by qpdf, poppler and veraPDF like everyone else's.
- **Observed, not assumed**: all ten fetched from their real sources and matched their hashes; a pinned
  hash changed on purpose was refused and the file removed; a second run downloaded nothing. With them,
  354 unit and 216 integration tests pass (the topographic map skipped with its T21 reason); without them,
  the suite is exactly as before, 332 and 196. The US Code's 9,302 pages open clean in 0.8 s.
- **Not yet observed**: the workflow itself, which only runs from `main`'s schedule or a dispatch.

### 2026-09-24 — The wanted documents, searched for on the internet
- **The request**: find in public sources the documents `corpus-contributions.md` asks for (W01–W12),
  free of rights and free of real people's data, and see what other PDF libraries keep. The rules were
  settled with the maintainer before anything was downloaded, then twice more along the way: ADR 23
  licences only; no real person's name anywhere, superseded revisions included, with typeface-designer
  credits, public figures and historical figures tolerated; publisher's bytes only; specimens allowed.
- **38 third-party documents entered `tests/corpus/vendor`**, 6.6 MB, from 69 downloaded candidates and
  47 adversarially verified ones. Every line of the wanted list now has something; what public sources
  cannot give — a real Java-stack invoice, a commercial e-signature, a Factur-X from an ERP, a copier's
  own OCR — stays wanted, and says so in `corpus-contributions.md`. Provenance (URL, date, SHA-256) is in
  `vendor.json`, attribution in `NOTICE`, the whole story in `docs/corpus-sources.md`.
- **W01 was also produced here**: `build/build_word.ps1` writes the corpus invoice through Word's Save as
  PDF, the Microsoft Print to PDF driver and the PDF24 printer. Both printers stamp the Windows account
  that printed: the Microsoft driver's display name in `/Author` (overwritten in place, same length, file
  recorded as derived), Ghostscript's `%%For` in `/Author` and the XMP (set in the PostScript before PDF24
  converts it). **The PDF24 file draws a box for every capital E** of the regular face: the driver's
  PostScript lacks the glyph while ToUnicode still says E — a genuine W06 file from an office desktop.
- **Most other libraries' corpora cannot be reused**: bug-report attachments under a code licence
  (pdf.js, PDFium, PDFBox, PdfPig, pypdf…), copyleft (Poppler, MuPDF, iText, and `py-pdf/sample-files` under
  CC BY-SA). pikepdf's per-file `REUSE.toml` is the one complete provenance record found.
- **The corpus found three things of ours.** `build_corpus.py` deleted the whole of `documents/` before
  regenerating, so a contributed document would have vanished with its entry: `contributed.json` now lists
  what the script must keep, and `--committed-only` refreshes the manifest with nothing but qpdf. The
  integration test read qpdf's page count from standard output and standard error together, and failed on
  any file qpdf warns about. And the reader falsely reports a truncated stream at a window boundary (T21).
- **Recorded, not fixed**: two hand-written files on which the reader is silent while qpdf reports damage
  (a trailer without `/Size`, a page given a stream body) are `unsupported` until M2 — a manifest field
  that did not exist, and now skips the test with the reason. `M2.md` gains the corresponding acceptance
  conditions and the anomalies of the vendored files (stale hint tables, a `/Size` off by one, an xref
  stream without its own entry, undefined references, malformed font XMP).
- **Referees**: pikepdf for pages, qpdf 11.9.1 in the integration container for the verdict, poppler's
  `pdftotext` 24.02 for text — it disagrees with xpdf on one Arabic ligature —, veraPDF 1.30.2 for PDF/A:
  thirteen of fifteen new claims upheld, two rejected.
- **The rule on names was then relaxed**, at the maintainer's request: a person's name alone, a Windows user
  ID or a photograph no longer disqualifies a file; a person's phone number, postal address or own e-mail
  still does, as does health, bank or identity data; fabricated and specimen data is always fine. Every
  file turned down for a name alone was examined again, and a critic went through the 117 earlier
  rejections for the ones missed. **38 more documents entered**, 6.1 MB, filling what the first pass had
  left open: copiers with their own OCR, JasperReports and other Java writers, PDFMaker 5 to 25, PDFWriter,
  a Mac Distiller, Print to PDF from Word, weclapp and Dynamics 365 Factur-X, DILA's Dictao signature, a
  qualified seal renewed by timestamps, XFA forms the reader can open today. What stays wanted is what
  only an inbox holds: a real supplier or bank document, a commercial e-signature.
- **For what cannot be redistributed** — ShareAlike sets, bug-report attachments, vendors' samples, W11's
  heavy files — the maintainer chose fetching on demand: [ADR 32](adr/0032-documents-that-cannot-be-redistributed-are-fetched-on-demand.md),
  proposed, not implemented. A manifest of URLs and hashes, a separate job, nothing committed.

### 2026-09-22 — Documentation for every release, and for the preview
- **The request**: a preview package should come with its documentation; the site should open on the
  stable version, with a button to the latest preview; every stable release should keep its documentation,
  behind a selector. Settled in ADR 31, with the choices it made along the way.
- **Docusaurus's own versioning**, not a site per release: one build serves everything, and a frozen
  version stays correctable. The stable release freezes the user documentation and the generated API
  reference in its release commit (`scripts/version-docs.mjs`, run from `.releaserc.json`'s prepare step).
- **One entry per line**: per minor below 1.0, per major from 1.0 — 1.0 opening the `1` line. A later
  release on a line replaces its copy, deletions included (the script unstages the old copy from git, since
  the release commit only adds what exists), and the selector shows the line's latest release. All lines
  are kept.
- **The preview has a button, not a selector entry**, and only while a preview is newer than the latest
  stable release. Right after a release, the newest preview on nuget.org is *older* than it; a button to it
  would lead backwards, so it disappears until the next merge. Before the first release the preview is the
  whole site. The rules are in `scripts/versions.mjs` and pinned by `npm test` (12 cases, run in CI).
- **Deployment**: `release.yml` calls `docs.yml` after every preview from `main`, with the preview's
  version — nuget.org can take minutes to list a new package, so it is not asked — and after every stable
  release, from the release commit, which the run did not start from. A manual run asks nuget.org.
- **Verified by simulation**, since no stable release exists: freezing `0.1.1`, then `0.2.0`, then `0.2.1`
  over it (a stale page and a local edit both gone, as they should be), then refusing `0.2.1` again and a
  preview. Built each way — two lines and a preview, no preview after a release, no stable at all — with
  the site check passing on every page, and the selector, the button in both directions, the banners and
  the namespace sidebar of a frozen API reference looked at in a browser.
- **Caught by looking, then made checkable**: the preview's banner appeared on every project document too —
  their unversioned plugin's only version is also named `current`. Fixed, and the site check now fails on
  a version banner under `/project/` (it reported 41 pages before the fix, none after).
- **Found on the way**: `main` gained a ruleset today, and GitHub Actions is not among its bypass actors —
  the first stable release will fail when it pushes its commit (at a glance, above).

### 2026-09-22 — The site had never shown a single user-facing page
- **What a visitor saw**: the introduction, both concept pages and all thirty API pages printed as
  `export const frontMatter = …` followed by a screen of `_jsx(…)` calls. Only the project documents
  rendered. It had been that way since the first deployment on 2026-09-19.
- **Why**: moving the site into `docs/website` left the project documents' plugin pointed at `docs/` —
  which now contained the site. Docusaurus scopes each docs plugin's MDX loader to its whole directory;
  `include` and `exclude` choose pages, not what the loader compiles. So every user page went through two
  loaders, and the second rendered the first one's output as Markdown. The page itself said so: it imported
  metadata from `…/docusaurus-plugin-content-docs/project/`, the wrong plugin.
- **Fix**: the project documents are copied into `docs/website/project` (ignored by git) before every build
  and published from there; edit links still point at `docs/`. ADR 28 carries the amendment.
- **Why nobody noticed**: the build passed, every link resolved, every URL returned 200 — and T12 below was
  closed on "the site serves 44 pages", which counted responses and never looked at one. Every build now
  ends with `check-site.mjs`, which reads the built pages for compiled MDX, HTML printed as text and
  unresolved DocFX references, and fails the build on any. Run against a saved copy of the published site,
  it reports 64 problems in 74 pages; against the repaired build, none.
- **The API reference, tidied while there**: DocFX's inline `<a id>` anchors had been the title, sidebar
  label and table-of-contents text of every page — they are explicit heading ids now; six `<see cref>`
  DocFX could not resolve had left empty elements, and now link to what they name; cross-links carrying an
  anchor were not being rewritten; the sidebar groups types under their namespace; generated pages no
  longer offer an edit link to a file that exists only during the build; and analyser suppressions are
  filtered out of declarations. Printing them exposed two `Justification` strings in `PdfDictionary` and
  `PdfStream` mangled by an old search-and-replace ("calls this a public sealed class pdfdictionary :
  pdfobject"), now rewritten.
- **Stale content**: the introduction still said nothing was on nuget.org; it now gives the `--prerelease`
  command and says which package exists and which milestone brings each of the others. The diagnostics
  page claimed seventy-five thousand mutations on every release; a normal test run does about three
  thousand, and the page now says so.

### 2026-09-19 — What two abandoned branches still knew

- Five merged pull requests left their branches behind. Three carried nothing `main` does not have and
  were identified for deletion; two did not, and this is what they held.
- **`main` was wrong about its own release.** It said no package had ever been published. nuget.org's flat
  container says otherwise: `0.1.1-preview.10` through `.13`. T11 called trusted publishing *untested*
  when four runs had already been through the OIDC exchange. The status file is the one document that
  must never be optimistic *or* pessimistic, and it had drifted the second way.
- **The coverage upload has never found the file it was given.** Run 84's log: `Some files were not found`
  for the `bin/Release/net10.0/TestResults/` path, then `Found 1 coverage files to report` pointing at
  `TestResults/` in the working directory. Codecov's CLI searches when the file it was handed is missing,
  and `fail_ci_if_error: false` means the day it stops searching, nothing turns red — the badge just
  freezes. Fixed by naming `--results-directory` and pointing the upload there.
- **T14 was stale in the other direction**, but only partly, and the branch overstated it. Codecov is
  linked, the upload runs tokenless (`Token length: 0`) and the badge reads 82.61% — so "not linked, badge
  empty" is done. The branch went further and called the Codecov **GitHub App** installed; it is not. The
  bot comments as `codecov-commenter` rather than `codecov[bot]`, and says so itself on every pull
  request. T14 is narrowed to that, not struck out — a distinction worth the correction, since it is the
  difference between "coverage is handled" and "coverage happens to work".
- **One claim on those branches was checked and rejected**: the `NUGET_USER` secret still had to be
  added. `main` had already replaced that whole design with `NUGET_ACCOUNT` and no secret at all, so it
  was not carried over.
- **A second was rejected, then turned out to be right for the wrong reason.** The branch said Pages was
  enabled; the site returned 404, so I kept T12 as written. T12 blamed the settings — "GitHub Pages is
  not enabled" — and that was the stale part. Pages *was* enabled. What had never happened was a
  deployment: `docs.yml` runs only with a stable release or its own manual trigger, run 1 had failed
  back when the settings really were missing, and nothing re-attempted it since. Dispatching it
  succeeded first time and the site is live. The lesson is the ordinary one: a 404 confirms the symptom,
  not the diagnosis.
- The prefix reservation is now real debt rather than a future chore: packages exist under `AdCodicem.`
  and nothing stops someone publishing beside them. T20, and the procedure is an email, not a button.

### 2026-09-19 — Fuzzing read 0 because of what it looks for, and I had read the check wrong
- **The correction first.** The entry below states that Scorecard detects no .NET fuzzer and that the 0
  is simply wrong. That is false, and reading `checks/raw/fuzzing.go` rather than recalling it says so:
  there is a `clients.CSharp` entry, and it greps `*.cs` for `using FsCheck;`, `using FsCheck.Xunit;`,
  `using FsCheck.NUnit;` or `using Expecto.ExpectoFsCheck;`. The check is narrower than "is this project
  fuzzed" — it asks whether the project does **property-based testing with FsCheck** — but it is not
  blind to this stack, and the 0 was earned.
- **So the fix is a test technique this codebase was missing, not a token.** `PropertyTests` holds six
  properties over generated data: the lexer terminates on any bytes and keeps every token offset inside
  the buffer; the parser answers for any bytes without letting its position escape the input; a text
  string survives `FromText` and `ToText`; the hand-written integer and real parsers agree with the
  framework everywhere both will answer; and the seeding itself replays.
- **They complement the mutation campaign rather than repeat it.** Mutation fuzzing starts from real
  documents and damages them, so it explores the neighbourhood of files that exist. A generator starts
  from nothing and reaches an empty buffer, a file of nothing but delimiters, a number carrying forty
  signs. Different instruments, different defects.
- **Checked that they have teeth before trusting them.** Three were deliberately falsified — the integer
  parser compared against `value + 1`, the round-trip against `value + "x"`, the lexer bound inverted —
  and all three failed, shrank to a minimal counter-example and printed a replay pair. A property that
  cannot fail is decoration.
- **Deterministic in the suite, exploratory at night.** A fixed seed means a commit can never be failed
  by luck; `ADCODICEM_PROPERTY_SEED` and `ADCODICEM_PROPERTY_TESTS` let the nightly campaign run 50 000
  cases per property from a different seed each time, which `fuzz.yml` now does alongside the mutations.
  FsCheck rejects an even gamma outright, so that half of the random state stays a constant.
- **FsCheck without `FsCheck.Xunit`**: the runner integration still pins `xunit.extensibility.execution`
  below 3.0.0 and would drag xUnit v2 into a v3 suite. The library itself is runner-agnostic, so a
  property runs inside an ordinary fact. No advisory against `FsCheck` 3.4.0 or `FSharp.Core` 5.0.2, so
  Vulnerabilities stays at 10.
- **Measured effect: 6.6 → 7.1**, Fuzzing 0 → 10 at weight 5. Confirmed by the detection rules rather
  than assumed: C# is 84% of what GitHub reports for this repository, far above the check's
  `average / 4` prominence threshold; `*.cs` matches a nested path because the matcher falls back to the
  file name; and `tests/` is not one of the `testdata/` or `src/test/` prefixes the walker skips.

### 2026-09-19 — The badge had a number at last, and it read 5.5
- The first Scorecard run that ever started (run 13, on `1bfde6d`) published a report: **5.5 out of 10**.
  Before anything was touched, its own arithmetic was reproduced from the published per-check scores and
  the documented risk weights — Critical 10, High 7.5, Medium 5, Low 2.5 — and it lands on 5.46, which is
  what `api.scorecard.dev` rounds to 5.5. That is what makes the rest of this entry a measurement rather
  than a hope: every fix below is worth a known number of points.
- **Pinned-Dependencies, 1/10 — the whole of it.** Not one of the 39 action references in this repository
  was pinned: 33 GitHub-owned and 6 third-party, every one on a floating tag. A tag is a name its owner
  can repoint at other code, and every run after that picks the new code up in silence. All 39 are now
  commit hashes, each carrying the version as a trailing comment — which is what Dependabot reads to know
  what the hash stands for, and what it rewrites alongside the hash when it bumps one. Each hash was
  resolved from `git ls-remote` and **checked against the annotated tag's dereferenced commit**, since a
  tag object's own SHA is not the commit and pinning it would not resolve.
- **Vulnerabilities, 5/10 — all five in the documentation site.** Two in `qs`, two in `serialize-javascript`
  and one in `uuid`, every one a transitive dependency of Docusaurus that nothing here asks for by name.
  npm's own advice was to take `@docusaurus/core` *down* to 3.5.2; three `overrides` clear all five without
  moving Docusaurus at all. `npm audit` now reports nothing, and the site still builds.
- **Security-Policy, 4/10 — and the missing 6 points were one link.** Scorecard gives 1 point for text,
  3 for saying something about disclosure and timelines, and 6 for *linked* content. `SECURITY.md` was a
  page of careful prose containing no URL whatsoever: it told the reader to find the Security tab. It now
  links the advisory form itself, which is better writing before it is a better score.
- **Measured effect: 5.5 → 6.6.** Pinned-Dependencies 1 → 9, Vulnerabilities 5 → 10, Security-Policy
  4 → 10. Unproven until the next run on `main` publishes, for the same reason as last time.
- **The last point on Pinned-Dependencies was refused, with a reason.** It is `dotnet restore` without
  `--locked-mode`, which means committing `packages.lock.json`. Worth exactly 10 of the check's 144
  weighted units — 0.05 of the displayed score, which rounds away entirely — and
  `RestorePackagesWithLockFile` set in `Directory.Build.props` breaks restore outright (`NETSDK1013`, an
  empty `TargetFramework`), though it works set per project. Central package management with transitive
  pinning already gives most of what a lock file is for. Recorded as T17 rather than forced through.
- **What is left is not code.** Branch-Protection and Code-Review are 0 and weigh 7.5 each: together
  they are worth about 1.5 points, and both need the `main` ruleset of T15 plus pull requests someone
  approves. Maintained is 0 because the repository is younger than 90 days, which only time fixes.
  Contributors is 0 because there is one of us. CII-Best-Practices needs a registration (T18) and
  Signed-Releases is unscored only because no release exists yet (T19). ~~Fuzzing reads 0 and will stay
  there: Scorecard detects no .NET fuzzer, so the number stays wrong.~~ **Wrong, and corrected the same
  day** — the check does cover C#, and the entry below says how.

### 2026-09-19 — The supply-chain badge had never been earned
- `OpenSSF Scorecard` was **red on every run it has ever had** — twelve of them, back to the day the
  workflow was added — and nothing said so, because the failure is not a check that fails but a workflow
  that never starts: `ossf/scorecard-action@v2` does not resolve. The action tags releases (`v2.4.4` is the
  current one) and publishes no floating major, so GitHub stops at *Prepare all required actions*. Six
  seconds, no step run, no SARIF, nothing uploaded to code scanning and nothing published to
  `api.scorecard.dev` — which is why the README badge has been empty since it was added.
- **Dependabot could not have caught it either**: the `github-actions` ecosystem bumped six other actions
  in this repository while leaving this one alone, because an unresolvable ref gives it no version to
  compare. An exact tag puts the action back under Dependabot's eye, which is the part that keeps the fix
  from decaying.
- Read against the action's own source at `v2.4.4` rather than assumed: `repo_token` defaults to
  `${{ github.token }}`, so the four job permissions already granted are what it needs; publication is
  refused only for a private repository or a ref other than the default branch. `workflow_dispatch` was
  added on the strength of that second rule — a report can now be asked for, provided the run is started
  on `main`.
- **Unproven until it runs.** The workflow only triggers on `main`, so merging is the first real attempt.
  The thing to check afterwards is not the green tick but the badge: a green run that publishes nothing
  looks exactly like a green run that does.

### 2026-09-19 — A preview was asked for, and the guard held
- The preview deployment was launched by re-running `Release` on the tip of `main` (`2798fb2`, run 9,
  attempt 2). Build and the whole suite pass in 25 seconds; the run then stops at **Check the publishing
  identity is configured**, because `NUGET_USER` is not set on the `nuget` environment. Nothing was packed,
  nothing was pushed to nuget.org, no OIDC key was even requested.
- **The secret was the wrong shape for what it held.** The trusted publishing policy was already configured
  on nuget.org; what was missing was the answer to *which account*, which `NuGet/login` requires as `user`
  because OIDC proves a run is authorised without saying who receives the key. That answer is `AdCodicem` —
  the owner of this repository, the prefix of every package, public on every page nuget.org will serve.
  Keeping a public name in a secret hid nothing and bought a setup step that fails silently much later.
  It is now `NUGET_ACCOUNT` in `release.yml`, stated once at the top, and **both guards are gone** with the
  thing they guarded against.
- **A preview had no manual trigger**, which is why the attempt above had to be a re-run — and a re-run
  keeps its run number, so it republishes the same `-preview.<n>` version rather than producing a new one.
  `workflow_dispatch` now asks *what to publish*, and defaults to `preview`. The stable path is the one
  that tags, writes to `main` and cannot be withdrawn, so it is the one you have to select. ADR 30 is
  extended rather than reopened: publishing a preview should not have required a merge, since taking that
  pressure off the merge is what the record was for.

### 2026-09-16 — Dependabot's six action bumps merged
- Six **major** GitHub Actions bumps, which `dependabot-auto-merge.yml` deliberately leaves for a human.
  Each was read against its own release notes; what that reading settled is on the squash commits, where
  it belongs. The three Pages bumps were reasoned rather than observed at the time; the deployment of
  2026-09-19 has since exercised all three.
- `Conventional commits` was red on all six: Dependabot wrote "Bump" with a capital and `subject-case`
  refuses it. **Corrected at the squash, not relaxed in the configuration** — which fixed the source too,
  since Dependabot copies the style of recent commits and its last rebase came back lowercase on its own.

### 2026-09-15 — The starting tag, and the trap it would have sprung
- `v0.1.0` tagged on `2808d2f`, the tip of `main`, so the first stable release continues in `0.x` instead of
  being declared `1.0.0`. No workflow runs on a tag, so pushing it changed nothing on its own.
- **Checked before relying on it, and it would have failed.** Both release paths passed the last tag as
  the baseline for package validation, which downloads that version from nuget.org to compare the public
  API — and nothing was ever published as `0.1.0`. `dotnet pack` with that baseline fails with `NU1101`,
  reproduced locally. The first preview and the first stable release would both have died at packing, the
  moment the publishing identity was configured.
- `.github/scripts/published-baseline.sh` now decides the baseline: the last release's version if nuget.org
  has a package for it, nothing if nothing has been published, and a failed run if nuget.org cannot be
  asked. Every branch exercised; the stable release's exact prepare command, rendered by lodash as
  semantic-release renders it, now packs `0.2.0` cleanly against the real tag.

### 2026-09-15 — Publishing and releasing are no longer the same event
- A merge into `main` now publishes a **preview** package and nothing else: no tag, no changelog, no
  GitHub Release, no redeployed site. The **stable release is a manual run** of the same workflow, and it
  alone versions, tags, writes `CHANGELOG.md`, publishes and deploys the documentation. ADR
  [30](adr/0030-previews-on-every-merge-stable-releases-on-demand.md).
- Both paths stay in `release.yml` on purpose: a nuget.org trusted-publishing policy is pinned to a
  workflow **file name**, so one file is one policy to register rather than two to keep in step.
- A preview is `<last release, patch bumped>-preview.<run number>` — after `v0.1.0`, `0.1.1-preview.12`.
  It says where the preview sits rather than predicting the next release: if a `feat:` takes the release
  to `0.2.0`, every `0.1.1-preview.n` still sorts between the two. Working the exact number out would
  mean running semantic-release on every merge to answer a question only the release asks.
- The site is deployed by the release rather than by the tip of `main`, so what is documented online is
  what is installable. `docs.yml` keeps a manual trigger for a documentation fix that cannot wait.
- The `-alpha` suffix now hangs on whether a build was handed a version at all, rather than on
  `GITHUB_REF_TYPE` — a check that meant something when releases came from tag builds and nothing since.

### 2026-09-14 — The repository brought to the standard toolchain
- Merged into `main` at the end of the session. The first `Release` run then proved its own safety net:
  with no previous tag semantic-release would have called this 1.0.0, and the workflow refused to
  reach it — **tag `v0.1.0` before configuring the publishing identity**, or the first automated
  release leaves 0.x on its own.
- Community, security and supply-chain files; dependency automation, coverage, a formatting gate and API
  compatibility validation; versions and releases derived from the commit history; a generated API
  reference, a runnable sample, and the standard layout (`benchmarks/`, `docs/website`).
- The decision log became **twenty-nine records** in `docs/adr/`, each with its context, its rejected
  alternatives and what would reopen it. The index maps every record back to the `Dnn` identifier that
  older commits cite, so nothing written before this stops resolving.
- The conventional-commits check had been failing on every push for a reason it did not name: version 6
  of the action refuses a `.js` configuration file and wants `.mjs`, and reports it as "you have commit
  messages with errors". Since semantic-release computes the version from those same messages, a check
  that cannot run is not cosmetic. Once it ran it refused one commit from 2026-09-13, whose subject
  started with a capital; the message was corrected and the branch re-pushed, so
  `config-conventional` stays enforced in full rather than relaxed to accommodate it.
- **CodeQL's first run paid for itself**: a buffering stream never disposed, a dead assignment in the
  cross-reference reader, and `GetWindow` testing its own type instead of asking the source whether it
  can serve bytes without copying. All three fixed; the third left the design better than it found it.
  Four quality queries are excluded in `.github/codeql/codeql-config.yml`, each with its reason — one of
  them flags every call to `Path.Combine` whatever its arguments, so the single place a path arrives from
  outside the repository is guarded in code instead.

### 2026-09-14 — M1 closed, and what fuzzing found on its first run
- Mutation fuzzing of the reader and the parser, seeded from the corpus: bit flips, corrupted digits,
  truncation, spliced bytes and broken keywords, each input asserted to end in a result or a typed
  exception within a time and an allocation budget. Seeds are deterministic, so a failure replays from
  the number printed in the message, and the offending bytes are written out.
- **It found a process-killing defect within a minute.** A mutated invoice drove `LoadRegularObject` and
  `RelocateAndLoad` into mutual recursion — the index named one offset, the neighbourhood search answered
  with another that failed to parse the same way, and the two called each other 3 978 times until the
  stack ran out. A file that kills the process is exactly what invariant 4 forbids, and no hand-written
  test had thought to try it. Relocation is now three counted attempts with no path back into loading.
- A campaign of 75 000 mutated inputs then ran clean in 76 seconds. A nightly workflow runs 20 000
  mutations per seed document.
- **M1 is closed**: every exit criterion ticked, corpus acceptance green in CI, both test levels in place,
  documentation published.

### 2026-09-13 — The build keeps no warnings
- `TreatWarningsAsErrors` on, analysis at `latest-recommended`, code style enforced in the build, XML
  documentation required on the public API (D28). T01 and T02 closed.
- Four real defects in production code, all worth the trouble: an override that did not chain to
  `base.Dispose`, and two return types wider than what the method can return. Ten more in the tests:
  culture-dependent parsing and formatting, a `JsonSerializerOptions` rebuilt on every call, a type owning
  an undisposed stream, a constant array allocated per call.
- Three suppressions, each at the symbol that triggers it and each with a reason: `PdfDictionary` and
  `PdfStream` keep the specification's vocabulary, and an xUnit collection definition is named after its
  collection. One scoped exception in `.editorconfig`: test names carry underscores because they are
  sentences.

### 2026-09-13 — Two test levels and a documentation site
- Test stack settled (D25): xUnit v3, **AwesomeAssertions** in place of Shouldly, **NSubstitute** where an
  interaction is what needs asserting — the first such test pins that the parser resolves an indirect
  `/Length` exactly once, which is a statement about a call and not about a value.
- Shared fixtures extracted into `tests/AdCodicem.Pdf.TestSupport`, so both suites read one manifest.
- `tests/AdCodicem.Pdf.IntegrationTests` runs the independent referees in **containers** (D26). qpdf now
  cross-checks every corpus document: its page count against the manifest, and its own verdict on which
  documents are damaged. Where no Docker daemon exists the 47 tests skip with the reason attached rather
  than failing, which is what makes the suite usable in a sandbox.
- A **Docusaurus site** in `website/` (D27) publishes the user-facing documentation and `docs/` unchanged,
  deployed to GitHub Pages. Its build runs in CI, so a project document that does not build is caught
  before the default branch. Docusaurus 3.9 had to be taken to 3.10: the older release pairs with a
  webpack whose progress-plugin schema it violates, and the build fails on a validation error that says
  nothing about the cause.
- The **definition of done** now has six points in `docs/roadmap.md`, and every milestone carries a
  Documentation section: unit tests, integration tests and documentation are conditions of closing a
  milestone, not follow-up work (invariant 11).
- The referee earned its keep on its first CI run by contradicting an assumption of mine: I had asserted
  that every damaged document makes qpdf complain, and **junk before the header does not** — qpdf shifts
  every offset silently and reports a sound file. The manifest now records `refereeCheckSucceeds` per
  document, filled in by running the very command the container runs, so the expectation is observed
  rather than assumed. A first attempt to author it through pikepdf was worse than useless: `Pdf.check()`
  does not exist in pikepdf 10, and a broad `except` turned that into "the referee rejects everything".

### 2026-09-13 — Packaging settled, and the wanted-documents specification
- Package identifiers confirmed and checked as unclaimed: `AdCodicem.Pdf` plus `.Validation`, `.Html`,
  `.AspNetCore`, `.FacturX`, `.Rendering`, `.Signing` (D23). The `AdCodicem.` prefix is to be reserved on
  nuget.org with the first publish.
- Publication switched to **trusted publishing** (D24): the release workflow exchanges a GitHub OIDC token
  for a nuget.org key valid one hour and usable once, so no long-lived secret exists. `docs/releasing.md`
  records the exact policy fields — a mismatch on the workflow file name or the environment is what breaks
  this setup, and it breaks it silently until someone reads the error.
- `docs/corpus-contributions.md` specifies the twelve document types still wanted (W01 to W12), with the
  milestone each unblocks, what makes a sample usable, an anonymisation checklist, and the difference
  between the public and private corpora.

### 2026-09-13 — Validation and repair milestones, and a defect the corpus found
- Two milestones inserted at the user's request: **M2 document validation**, right after reading, and
  **M4 repair**, right after writing — repair produces a sound file, so it needs the writer. Everything
  after them shifted by two; the roadmap now runs to M14.
- M2 is specified as a rule engine with stable finding identifiers, of which PDF/A and PDF/UA become
  profiles in M12 (D20). M4 is specified as findings-driven and conservative by default, writing an
  incremental update so signed bytes survive (D21).
- Eight conformance fixtures from the veraPDF corpus vendored under CC BY 4.0 with a NOTICE (D22), taking
  the corpus to 27 documents. `git clone` of public repositories works through the sandbox proxy, so
  public corpora need no manual help.
- **Those third-party files immediately found a real defect**: a validly compressed *empty* stream — an
  empty content stream, an empty appearance, both commonplace — decodes to zero bytes, which the Flate
  filter was reading as failure and answering with the compressed bytes plus a spurious warning. Success
  is now reported explicitly rather than inferred from the length of the output, and filter diagnostics
  carry the offset of the stream they concern, because "a Flate stream could not be decoded" with no
  location is not actionable.

### 2026-09-13 — The corpus of real documents
- 19 documents built by four real producers — Chromium (Skia backend), LibreOffice, ReportLab, qpdf —
  covering invoices, a multi-page report, a contract, an interactive form, a scanned page, a PDF/A-2b
  export, a linearised file, an object-stream rewrite, an AES-256 encrypted file, a 1000-page document,
  and five copies damaged on purpose. 2.7 MB, committed, reproducible via `tests/corpus/build`.
- Expectations in the manifest are established by an **independent tool**, never by our own reader,
  including what qpdf can still recover from each damaged file.
- `CorpusReadingTests` turns the M1 acceptance conditions into 45 executable tests: every document opens
  as described, damaged ones report their damage, well-formed ones produce no repair and no warning, page
  counts match across all four producers, opening never reads content, and indexing the 1000-page document
  holds inside a measured 4 MB budget.
- Three real defects in the expectations surfaced immediately, and were worth the exercise: a wrong
  `/Length` is only noticed when the stream is actually read (the lazy reader working as designed, so the
  test now reads everything before judging), shifting every offset breaks `startxref` itself so the whole
  index is rebuilt rather than relocated object by object, and a process-wide allocation counter is
  meaningless in a parallel suite.

### 2026-09-13 — English throughout, and corpus-based acceptance
- All project documentation rewritten in English (D19). The convention is now: everything in English, code
  and documentation alike.
- Every milestone now carries **acceptance conditions** expressed against real documents, and `docs/corpus.md`
  defines where those documents come from, how they are catalogued and what closing a milestone requires
  (D18, invariant 10).
- The corpus can be produced in-container and committed: Chromium's Skia backend, LibreOffice, and Python
  producers from pypi give genuinely different cross-reference shapes, font handling and object stream use.

### 2026-09-13 — M1, slices 1 to 7
- COS object model, tolerant lexer and parser, decoding filters, all four index shapes (classic table,
  cross-reference stream, object stream, `/Prev` chain, hybrid files), lazy resolution with a bounded
  cache, repair by scanning, structured diagnostics.
- Hardening: every allocation a file could dictate is bounded (a stream length clamped to the real file
  size, an object stream's object count clamped to what its header could hold), and cycles — references,
  `/Prev`, an object stream containing itself — all terminate.
- A file with no usable object is refused with a typed exception rather than opened empty.
- 73 tests, including a class devoted to hostile input with a per-test time budget.
- Tests build their PDFs byte by byte with exact offsets, then damage them on purpose: no network
  dependency and no binaries in the repository. Real documents come next, as the corpus.
- Tooling: the .NET 10 SDK installs from the Ubuntu archive; `dotnet test` now requires
  Microsoft.Testing.Platform (opted into via `global.json`) and the `--solution` form.

### 2026-09-12 — Framing and foundations
- Scope settled in two steps: HTML → PDF generation first, then extended to full manipulation of existing
  documents. Nineteen decisions recorded in `docs/adr/`.
- Documentation frame established: `CLAUDE.md` (session frame), `architecture.md`, `decisions.md`
  (since converted into `docs/adr/`),
  `roadmap.md` (M0 to M12), `corpus.md`, `milestones/` (per-milestone specification), this file.
- Solution skeleton: core, HTML engine, ASP.NET Core integration, tests, benchmarks; `net10.0` target,
  central package management.
- Environment: the .NET SDK is absent from web sessions and Microsoft's servers are blocked by the network
  policy. Worked around through the Ubuntu archive (`dotnet-sdk-10.0`), automated by the `SessionStart` hook.

## Debt and open points

| # | Subject | Decision expected |
|---|---------|-------------------|
| ~~T01~~ | ~~`TreatWarningsAsErrors` is off while the foundations settle~~ | Done: on across the solution, analysis at `latest-recommended` |
| ~~T02~~ | ~~XML documentation (`CS1591`) is not enforced on the public API~~ | Done: required, and the public API already satisfied it |
| ~~T03~~ | ~~The real-document corpus is not built yet~~ | Done: `tests/corpus`, 27 documents, four producers plus vendored fixtures; 68 since 2026-09-24 |
| T10 | **Narrowed on 2026-09-24**: Word, PDFMaker, Acrobat, InDesign, LiveCycle, PDFWriter, copier scans with their own OCR, Java writers, ERP Factur-X samples, PDF 1.2 archives, signatures — DocuSign's among them since the third pass — and other producers' PDF/A are now in the corpus, found in public sources (`docs/corpus-sources.md`). Still missing from what may be committed is what only an inbox holds: a real invoice or statement from a supplier or bank (real ones are in the remote corpus only), a Yousign, Universign or Adobe Sign signature, a copier file untouched since the copier wrote it, Hebrew | Contributions, per the "still wanted" column of `docs/corpus-contributions.md`; the remote corpus (ADR 32) for files that can be used but not redistributed |
| T21 | **The reader reports a truncated stream that is not.** When a stream's data ends inside the parser's 8 KB window but its `endstream` falls past the window's end, `PdfObjectParser.ReadStream` finds no `endstream` in the window and reports `stream.truncated`, cutting the stream at the window. Found on object 49 of the USGS Washington West topographic map (W11 reference, `docs/corpus-sources.md`): data from 68 to 8,185 in a 8,192-byte window; qpdf reads it cleanly. The same file also earns a `filter.failed` on its 14.9 MB Flate image, not yet explained. The map is now in the remote corpus, recorded as unsupported with this reason, so the fix is checked against it every night. Since 2026-09-25 also the FDA hospital-bed guidance from GovDocs1 (remote): objects 604 and 2053, streams of about 8.1 KB whose `/Length` is right | A synthetic regression test (a stream ending 1 to 10 bytes before 8 KB), then treat an `endstream` beyond the window like data beyond it when a stream-data provider exists |
| T22 | W11 has no committed document, by decision. All three of its references — 9,302 pages, one 63 MB page, and since the third pass of 2026-09-24 the heavy scan (USGS Professional Paper 1, 147 MB of JPEG 2000) — are in the remote corpus (ADR 32) and tested every night, but not in the main CI job | M13: state its memory budgets against the remote documents, and close only on a green `Remote corpus` run |
| T23 | **The reader cuts an indirect object longer than its 8 KB window at the window's edge.** Found on two remote documents: object 458 of the EU DSS file with 24 signatures and a document timestamp, a DSS `/VRI` dictionary of 10,112 bytes, reported as a truncated object exactly 8 KB in; and object 14 of the BOE's 2015 law, a structure array of 8,694 bytes, reported as unexpected tokens at the same point, with 35 arrays like it. qpdf reads all of them whole. `PdfFileReader.TryParseObjectAt` does grow its window when the parser says an object ran out, but the parser has warned into the document's diagnostics by then. The same window as T21, met by an object rather than a stream. Both entries are recorded as unsupported with this reason, so the fix is checked against them every night. Since 2026-09-25 also the VA Kernel guide (object 10913, 14,188 bytes) and a JHOVE poster (object 2307, 8,248 bytes), both remote | **Before M2 closes** — a validator cannot build on invented syntax errors: a synthetic regression test (a dictionary and an array a few bytes over 8 KB), then find why a window that turns out too small still leaves a diagnostic behind, or is not grown at all |
| T24 | **Opening reads each cross-reference section through a window of up to 64 KB, whatever the section's size.** Bounded, but proportional to the number of sections rather than to their size: opening the 218 KB signed Web Capture file from pdfcpu's test data, which has three sections, reads 117 KB — more than the quarter of the file the laziness test allows. The entry is recorded as unsupported with this reason | M13, with the other budgets: start a section's window small and grow it, as object windows already do — and keep what was read when it grows: the VHA coding handbook from GovDocs1 (2026-09-25) has one 273 KB table, read at 64 KB, then 256 KB, then to its end, 683 KB in all for a 2.2 MB file; also recorded as unsupported |
| T25 | **A `/Prev` that misses its section drops it in silence.** `PdfFileReader.TryReadXRefChain` returns success as soon as one section was read, so when a later `/Prev` does not land on `xref` or on a cross-reference stream the older section is simply left out, with no rebuild — and with no diagnostic either when the offset falls inside the file; one past its end earns `xref.entry-out-of-range`. Found on IBM's QMF manual from GovDocs1 (remote): `/Prev 1569328` falls 12 bytes past the keyword, and the 4,106 entries of the main table are lost; qpdf reports `xref not found` and rebuilds. Recorded as unsupported with this reason | **Before M2 closes** — a validator cannot report what the reader hides: a synthetic regression test (a `/Prev` a few bytes off, and one pointing nowhere), then report the failed section and search near it or rebuild, as the reader already does for an object a few bytes off |
| T26 | **The remote corpus cannot take a file out of an archive, so the one external test suite for M2's structural profile stays out of reach.** The iPRES 2017 hand-built set ([doi:10.22000/53](https://doi.org/10.22000/53), CC BY-SA 4.0, so remote) is 88 files derived from one page, each with one deviation from ISO 32000-1's structure, and a spreadsheet giving each file's category and deviation, JHOVE 1.16.5's verdict and whether Acrobat XI Pro opens it. The OPF's copies are unusable, and RADAR serves the originals only inside one BagIt tar of 613,888 bytes, whose MD5 is the archive checksum RADAR publishes: the 2017 deposit itself, so it can be pinned. `fetch_remote.py` fetches one file per URL, and reads `source.bytes` as the download's ceiling. RADAR's terms for data users ask nothing beyond the licence and say nothing of automated download | Proposed: **before M2's slice 2**, so the file and cross-reference rules meet the set as they are written. [ADR 33](adr/0033-a-remote-document-may-be-a-member-of-a-pinned-archive.md), proposed, gives the design — the archive and the member pinned separately, one download per run whose failure is remembered, a member copied out of a tar opened in `r:` mode and never extracted by its own path — and how the 88 entries are written: expectations from qpdf, the authors' case as a feature, unsupported until M2 for 75 files and M10 for 13 content-operator files. Accepting it is the decision expected |
| ~~T11~~ | ~~Publishing is configured but untested~~ | Done, and **observed**: four previews are on nuget.org, pushed through the OIDC exchange. No secret is involved — the account is `NUGET_ACCOUNT` in `release.yml` |
| ~~T12~~ | ~~GitHub Pages is not enabled, so the site builds but does not publish~~ | Done, and the diagnosis was wrong: Pages was enabled; no deployment had ever been *run*. Dispatched `Documentation` on 2026-09-19, it went green first time, and the site served 44 pages plus the API reference — **served, not rendered**: every user-facing page was broken, which only a look at one would have shown (2026-09-22). The three Pages action bumps of 2026-09-16 are now observed rather than reasoned |
| T13 | The integration suite has one referee (qpdf); veraPDF, pdftotext and a rasteriser join it as their milestones arrive | M10, M12, M14 |
| T14 | Codecov is linked and the badge reads 82.61%, uploaded tokenless (`Token length: 0` in run 84) — so the original entry, "not linked, badge stays empty", is closed. What is left is narrower: the Codecov **GitHub App** is not installed, so it comments as `codecov-commenter` rather than `codecov[bot]` and warns on every pull request that uploads and comments are not reliably processed | Install the Codecov GitHub App on the repository |
| T15 | **Half done, and the missing half now blocks the stable release**: the "Default" ruleset on `main` exists since 2026-09-22 (pull request, one code-owner approval, linear history, CodeQL, coverage), bypassed by repository admins only. GitHub Actions is not a bypass actor, so the stable release's push of its `chore(release)` commit will be refused. The original entry, for the record: auto-merge **is** allowed on the repository; what was missing is a ruleset on `main`, so it still accepted direct pushes and the Dependabot auto-merge workflow had no required check to wait for. **Measured cost**: Scorecard's `Branch-Protection` is 0/10 at weight 7.5, and `Code-Review` is 0/10 at the same weight because nothing here has ever been approved — together about **1.5 points** of the overall score, the largest block left | A branch ruleset on `main` requiring the five pull-request checks, non-strict, **with a bypass for GitHub Actions** — `@semantic-release/git` pushes the `chore(release)` commit straight to `main`, and a ruleset without that bypass fails the stable release in `prepare` |
| T16 | The API baseline is one version for the whole solution, checked against `AdCodicem.Pdf` only. A satellite first shipped in a later release — `AdCodicem.Pdf.Validation` in M2 — has no package at that version, and its pack fails with `NU1101` exactly as `v0.1.0` would have | In M2, before `AdCodicem.Pdf.Validation` is packable: make the baseline per package |
| T17 | One dependency in CI is still unpinned: `dotnet restore` in `ci.yml` has no `--locked-mode`, because no `packages.lock.json` is committed. Measured at 10 of Pinned-Dependencies' 144 weighted units — 0.05 of the displayed score — and `RestorePackagesWithLockFile` in `Directory.Build.props` fails the restore with `NETSDK1013` | When it buys something beyond the check: set the property **per project**, where it works, commit the six lock files, and add `--locked-mode` to `ci.yml` |
| T18 | No OpenSSF Best Practices badge, so `CII-Best-Practices` is 0/10 at weight 2.5 — about 0.26 of the overall score | Register the project at [bestpractices.dev](https://www.bestpractices.dev), answer the questionnaire, put the badge in `README.md` |
| T19 | `Signed-Releases` is unscored (-1) only because no release exists. The moment one does it becomes a scored High check, and nothing in `release.yml` attaches a signature or a provenance bundle to the GitHub Release | Before the first stable release: attest the packages and upload the bundle as a release asset, so the check has a `.intoto.jsonl` to find |
| T20 | The `AdCodicem.` prefix is not reserved on nuget.org. Now that packages exist under it, anyone else can publish `AdCodicem.Anything`, and ours are not marked as coming from a verified owner | Email account@nuget.org with the owner display name and the glob `AdCodicem.*` — there is no self-service button; `docs/releasing.md` has the criteria |
| T04 | An OFL font set must be embedded for default rendering | During M6 |
| T05 | A public API test (a baseline of exported signatures) | Put in place at the start of M7 |
| T06 | `PdfString.ToText` reads Latin-1 rather than full PDFDocEncoding (the 32 positions 0x80-0x9F differ) | Before the first public release |
| T07 | The object cache evicts FIFO rather than LRU; names are interned through an intermediate string | M13, with measurements |
| ~~T08~~ | ~~Fuzzing of the lexer and parser is not set up~~ | Done: in the suite per commit, and a nightly campaign |
| T09 | A memory budget is now enforced in CI; a throughput budget is not | Throughput budget in M13 |
