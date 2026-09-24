# Where the third-party corpus documents come from

On 2026-09-24 the public internet was searched for the documents `docs/corpus-contributions.md` asks for
(W01 to W12), and the test corpora of other PDF libraries were examined for the same purpose. This page
records the rules applied, what entered `tests/corpus/vendor`, what was turned down and why, and what the
search taught us. `tests/corpus/vendor.json` holds the per-file detail: source URL, retrieval date,
SHA-256 of the published bytes, and every expectation.

## The rules a document had to meet

A file entered the corpus only if it met all four. A file that failed one was rejected, not repaired.

1. **An attribution-only licence that covers this file** (ADR 23): public domain — including United
   States federal works under 17 U.S.C. 105 —, CC0, CC BY, the Licence Ouverte, the UK Open Government
   Licence, the EU's reuse decision 2011/833/EU, Japan's Public Data License, MIT, BSD or Apache-2.0.
   ShareAlike, copyleft, non-commercial, no-derivatives and "no licence stated" were all refused. A code
   licence on a repository does not cover the bug-report attachments committed into it.
2. **No real person's name anywhere**: visible text, `/Info`, XMP, annotation authors, form values, XFA
   packets, signing certificates, embedded files, and the objects of superseded revisions that
   incremental updates keep in the file. Three things are tolerated, as decided with the maintainer:
   typeface-designer credits inside embedded font programs (present in almost every PDF, our own Word
   output included), people known to the general public, and historical figures. Clearly fictitious
   specimen data — John Doe, Contoso, *Entenhausen* — is fine; a name that cannot be shown fictitious is
   treated as real.
3. **The publisher's own bytes**, never a re-saved copy: each file was downloaded again and its SHA-256
   compared, from a permanent URL where one exists (a repository commit, the EU's Cellar URIs, the IRS
   prior-year archive), or from the Internet Archive's unmodified `id_` copy when the publisher has
   withdrawn it.
4. **Small**: at most 2 MB, usually a few hundred kilobytes. W11, where size is the point, is recorded
   below by reference and not committed.

Embedded fonts stay inside the documents under their own embedding permission: subsets always, complete
fonts when their `fsType` allows embedding.

## How the search ran

- **Survey**: some ninety PDF libraries and public corpora, in every language, were examined for test
  PDFs, the licence that actually covers them, and how provenance is tracked. The findings are below.
- **Hunt**: 69 candidates were downloaded and inspected — producer chain, cross-reference shape,
  signatures and certificate subjects, form fields, fonts, image filters, extracted text, `qpdf --check`.
- **Adversarial verification**: 47 shortlisted files went to independent verifiers told to reject on
  doubt. They re-read the full text of every page and every revision, re-fetched each licence statement,
  and re-downloaded each file. 12 were rejected; 4 of those were readmitted after the maintainer's rulings
  on font credits, complete fonts and historical figures; one more was dropped by a final scan of every
  revision's metadata. **38 entered the corpus**, 6.6 MB in all.
- **Referees**: page counts and attachments from pikepdf; the `qpdf --check` verdict from qpdf 11.9.1 in
  the integration tests' container; text from poppler's `pdftotext` 24.02; conformance from veraPDF 1.30.2.
- **Our reader** opened every one of the 239 real PDFs downloaded along the way: 209 opened clean, 15
  encrypted ones were refused with the typed exception M9 will replace, 15 opened with repairs or warnings,
  and none crashed or hung. At least one of those warnings is a false alarm of the reader's own — a stream
  whose `endstream` falls just past its 8 KB window is reported truncated (T21 in `docs/status.md`) — and
  the others were not all checked against qpdf: a Cerfa reported truncated may be the same defect.

## What entered the corpus

| File under `tests/corpus/vendor/` | W | What it brings |
|---|---|---|
| `us-federal/xerox-workcentre-treasury-imf-report-scan.pdf` | W03 | Copier firmware output: JBIG2 with global segments, no text layer |
| `us-federal/hp-mfp-acrobat-ocr-nih-report.pdf` | W03 | An HP MFP scan with an invisible OCR layer added by Acrobat 11 |
| `us-federal/acrobat3-import-irs-1040-1988-scan.pdf` | W03, W10 | PDF 1.2, CCITT G4, imported by Acrobat 3.0 in 1999, NUL-terminated `/Info` strings |
| `us-federal/distiller2-irs-9465-1996-rc4-40.pdf` | W10 | Distiller 2.0, 40-bit RC4 with an empty user password, an AcroForm from 1998 |
| `us-federal/distiller3-irs-ss4-1995-form.pdf` | W10 | Distiller 3.0, an Acrobat 3 AcroForm, LZW streams |
| `us-federal/distiller4-opm-fehb-election-form-2000.pdf` | W10 | Distiller 4.0, TrueType turned into Type 1 subsets, Arial not embedded |
| `us-federal/indesign-irs-pub1-arabic.pdf` | W09, W02 | Right-to-left Arabic in Type 1 (CFF) subsets, `/Lang` wrongly `en-US` |
| `us-federal/indesign-irs-pub1-russian.pdf` | W09 | Cyrillic through `/Differences` encodings, six incremental updates, a hybrid index |
| `us-federal/indesign-irs-pub1-chinese-traditional.pdf` | W09 | CIDFontType2 and CIDFontType0C subsets |
| `us-federal/livecycle-uscis-ar11-xfa-form.pdf` | W08 | An XFA form with calculate scripts, Reader usage rights, AES-128 |
| `us-federal/itext-govinfo-us-code-certified.pdf` | W05 | A GPO certification signature (DocMDP) in an incremental update, written by iText 7 (Java) |
| `opf-format-corpus/distiller3-dea-cfr-damaged.pdf` | W06, W10 | Real damage from GovDocs1: a wrong `startxref` and wrong stream lengths |
| `opf-format-corpus/imagemagick-false-pdfa1b-jpx.pdf` | W12 | A false PDF/A-1b claim over a JPEG 2000 image |
| `uk-ogl/word2019-ccs-contract-schedule.pdf` | W01 | Word 2019's hybrid cross-reference, non-embedded Arial |
| `uk-ogl/indesign-acrobat-hmcts-n208-form.pdf` | W02, W08 | InDesign 15.1, Acrobat usage rights, optional content, a CCITT image |
| `uk-ogl/distiller10-hmrc-gift-aid-letter.pdf` | W02 | Distiller 10.1.8 behind the PScript5 driver |
| `uk-ogl/pagemaker-distiller5-hmrc-iht205-form.pdf` | W06, W08 | JavaScript form calculations, RC4-128; it broke pdf.js (issue 13132) |
| `fr-licence-ouverte/word2010-meae-paris-call-survey.pdf` | W01 | Word 2010, `/Lang` `fr-FR` over English text |
| `fr-licence-ouverte/word365-dila-text-drawn-as-images.pdf` | W01 | Word 365 drew the text as soft-masked images, having failed to embed the font |
| `fr-licence-ouverte/print-to-pdf-excel-agec2025.pdf` | W01 | Microsoft Print to PDF output published untouched, `/Author` empty |
| `fr-licence-ouverte/libreoffice-cerfa-13983-form.pdf` | W08 | A Cerfa: a LibreOffice form re-saved four times by other tools |
| `eu-publications/pdflib-oj-exchange-rates-greek.pdf` | W09, W12 | Greek, PDF/A-2a from PDFlib, complete EUAlbertina fonts |
| `eu-publications/antenna-house-oj-exchange-rates-2019.pdf` | W12 | PDF/A-1a from Antenna House: empty `/Lang`, references to objects the xref lacks, malformed font XMP |
| `eu-publications/distiller10-eu-consolidated-regulation-2015.pdf` | W12, W02 | A PDF/A-1a claim with a CMYK output intent, which veraPDF rejects |
| `eu-publications/3heights-eu-consolidated-regulation-2026.pdf` | W12 | PDF/A-2a from a converter rather than a producer |
| `lu-legilux/antenna-house-legilux-memorial-pades-lta.pdf` | W05 | A qualified electronic seal: PAdES B-LTA, DSS, a document timestamp, three incremental updates |
| `jp-nta/indesign-distiller18-nta-gift-tax-vertical.pdf` | W06, W09 | Vertical Japanese without ToUnicode, AES-128; it broke pdf.js (issue 11526) |
| `pikepdf/scanner-ccitt-endofline.pdf` | W06, W03 | A scanner's CCITT G3 image with `/EndOfLine true`; it broke pikepdf (issue 601) |
| `pikepdf/handwritten-cyclic-toc.pdf` | W06 | Cyclic destinations, no trailer `/Size`; it crashed pikepdf (issue 677). Unsupported until M2 |
| `pdf-association/handwritten-type3-recursion.pdf` | W06 | A Type 3 recursion cycle: a denial-of-service probe for invariant 4 |
| `pdf-association/handwritten-dict-is-stream.pdf` | W06 | A page object given a stream body; qpdf finds no page. Unsupported until M2 |
| `pdf-association/handwritten-indexed-colour-out-of-range.pdf` | W06 | Indexed colour addressed out of range |
| `pyhanko/acrobat-reader-signed-twice.pdf` | W05 | Two Acrobat Reader signatures with timestamps, test certificates |
| `zugferd/mustang-zugferd2-en16931-invoice.pdf` | W07, W12 | ZUGFeRD 2.0 EN 16931, PDF/A-3u, an `/AF` attachment |
| `zugferd/mustang-zugferd1-basic-invoice.pdf` | W07, W12 | The legacy ZUGFeRD 1.0 namespace and attachment name |
| `zugferd/fop26-xrechnung-visualisation.pdf` | W04 | Apache FOP 2.6 with base-14 fonts |
| `docentric/fop-factur-x-visualisation.pdf` | W04, W12 | Apache FOP, a PDF/A-3b claim without the XML attached |
| `bfo/bfo-pdfa2b-embedded-pdf.pdf` | W12 | PDF/A-2b carrying another PDF, whose `%%EOF` sits inside a stream |

Our own additions for W01 are in `documents/invoice/`: the same fictitious invoice through Word's Save as
PDF, the Microsoft Print to PDF driver and the PDF24 printer (`build/build_word.ps1`). The PDF24 file draws
a box for every capital E of the regular face — the driver's PostScript lacks the glyph, while ToUnicode
still says E — which makes it a W06 file as much as a W01 one.

### W11, by reference only

| Document | Size | SHA-256 on 2026-09-24 |
|---|---|---|
| United States Code, 2023 edition, Title 42 — 9,302 pages, a GPO signature in an incremental update ([govinfo](https://www.govinfo.gov/content/pkg/USCODE-2023-title42/pdf/USCODE-2023-title42.pdf)) | 37,632,691 bytes | `9384648a70cf217b7dfff872ebd699b24a59dce55c76cdc06c802810782d0350` |
| USGS Professional Paper 1 (1902) — 125 pages of JPEG 2000 up to 29 megapixels each, with an OCR layer ([USGS](https://pubs.usgs.gov/pp/0001/report.pdf)) | 146,708,859 bytes | `50b5ea3fecbbda5f74fcd4dd53f348d697c18d44f1a6ff1a42fd1f0031c5ee3d` |
| US Topo map, Washington West, 2023 — one page, a 109.5-megapixel Flate image and 31 layers ([USGS](https://prd-tnm.s3.amazonaws.com/StagedProducts/Maps/USTopo/PDF/DC/DC_Washington_West_20230612_TM_geo.pdf)) | 63,128,959 bytes | `33cfc95870f64560dcdedc616d3e666f6f99e485125f290cc9963e1db2d9f6d5` |

All three are US federal works. Publishers reissue such files, so the hash says which bytes a
measurement was taken on. Fetching one on demand for M13 — pinned by hash, as PDFBox and pdf.js do — is a
decision for that milestone.

## What other PDF libraries ship

Almost every PDF library keeps a corpus. Very few could be reused here, and the reason is nearly always
the same: the files are attachments to bug reports, committed under the repository's code licence by
someone who did not own them.

| Library or corpus | Test PDFs | What covers the PDFs | Usable here |
|---|---|---|---|
| veraPDF corpus | ~2,900 | CC BY 4.0; the Isartor folder forbids redistribution | Yes, already vendored; Isartor excluded |
| BFO PDF/A suite | 34 | CC BY 3.0 | Yes |
| PDF/UA Reference Suite (PDF Association) | 19 | CC BY 4.0 | Mostly not: authors and a photographed person are named in the files |
| PDF Association SafeDocs artefacts, pdf-differences | ~60 | Apache-2.0, CC BY 4.0 | Yes, the hand-written files; several others name their author |
| OPF format-corpus | ~290 | CC0 by folder; GovDocs1 copies carry no licence of their own | The Cabinet of Horrors and the US federal GovDocs1 files |
| pikepdf | 36 | Per file, in `REUSE.toml` | The CC0 and public-domain files; not the CC BY-SA ones |
| qpdf | ~720 | Apache-2.0 for the maintainer's own files; nothing for issue attachments | The maintainer's own files |
| pyHanko | ~130 | MIT; files generated by pyHanko against a fictitious test PKI | Yes |
| Mustang, ZUGFeRD corpus, quba-viewer | ~190 | Apache-2.0 for the repositories; nothing specific for vendors' samples | The maintainer's own output |
| Apache FOP | 26 | FOP's own output, Apache-2.0 | Yes, but no invoice among them |
| pdf.js | ~980 committed, 459 linked | Nothing stated; mostly bug attachments | Only the government files its links point to |
| PDFium, Chromium | ~1,100 | BSD for hand-written `.in` files; nothing for bug files and the Foxit QA suite | Hand-written files only; Foxit files name employees |
| Apache PDFBox, Apache Tika | ~250 | Nothing for JIRA attachments and GovDocs1 extracts | No |
| PdfPig, pypdf, pdfminer.six, pdfplumber, camelot, tabula, hayro, pdf-rs, lopdf, pdfcpu, Docnet, endesive, pdfrw | a few to ~250 each | Nothing stated, or "can be distributed freely" without a licence | No |
| Poppler, MuPDF and PyMuPDF, Ghostscript, iText, OpenPDF, EU DSS, HexaPDF, Origami, borb, img2pdf | up to ~7,000 | GPL, AGPL, LGPL or MPL | No |
| py-pdf/sample-files, PDF Association pdf20examples | ~40 | CC BY-SA 4.0 | No: ShareAlike |
| Syncfusion samples, UniPDF | — | Commercial terms, or private | No |
| QuestPDF, ReportLab, WeasyPrint, Xpdf | none | — | — |
| GovDocs1, SafeDocs CC-MAIN-2021-31, SafeDocs issue-tracker corpus, Common Crawl | millions | None: each file is its publisher's | Only files whose publisher's licence qualifies, found one by one |
| Internet Archive, Wikimedia Commons | millions | Declared per item by uploaders, unverified | With the publisher's original bytes only |

### Practices worth borrowing

- **pikepdf's `REUSE.toml`** gives every test file its own SPDX licence and copyright line, often with a
  link to the issue where the owner granted it. It is the only corpus examined whose provenance is
  complete, file by file.
- **pdf.js** commits a `.link` file holding a URL — often an Internet Archive copy — for files it may not
  redistribute, downloads them before the tests run, and checks each against an MD5 in its manifest.
  **PDFBox** does the same at build time, pinned by SHA-512. That is the model for W11.
- **py-pdf/sample-files** records the producer and creator of every file in a `files.json`, beside the
  source each was made from.
- **veraPDF** encodes the expected verdict in each file's name, so a fixture is self-describing.

`vendor.json` now records the source URL, date and SHA-256 of every vendored file, which gives this corpus
the provenance pikepdf has.

## What was turned down, and why

| Candidate | Reason |
|---|---|
| Department of Health factsheet (Acrobat PDFMaker 10) | The `/Info` of an earlier revision, still in the file, names a civil servant |
| VA "Select Agents and Toxins" (GovDocs1) | Same: three revisions, the older ones name the author |
| DD Form 293 (pikepdf, public domain) | The XFA configuration packet holds the designer's `C:\Users\<surname>` path |
| IRS Form 1040 for 2022 | The XFA packet holds a path with an employee's user identifier |
| weclapp Factur-X (Mustang) — the only real-ERP Factur-X found | `/Creator` names a person who cannot be shown fictitious |
| PDF/UA reference invoice (PDFlib, Java) | A photograph of a real person under a sample sales-representative name |
| National Park Service lesson plan (Canon iR-ADV scan) | The NPS arrowhead is excluded from the Service's public-domain statement |
| European e-Justice page (iText 5) | The EU reuse decision excludes logos, and the page embeds the portal's |
| IRS Form 1040 for 1994 (Net Distiller 1.02) | Complete Type 1 fonts whose embedding permission cannot be checked |
| CDC fact sheet in Chinese (2004) | `/Author` names a person |

Four files first rejected were readmitted after the maintainer's rulings: the two EU Official Journal
notices whose EUAlbertina fonts are embedded in full under an editable-embedding permission, the 2026
consolidated regulation whose only name is a typeface designer's credit inside a font, and the 1786
engraving under ImageMagick's false PDF/A claim, whose only names are historical figures.

## Traps met along the way

- **Printers stamp who printed.** Microsoft Print to PDF writes the Windows account's display name into
  `/Author` whatever the document says; PDF24's Ghostscript copies the account name from the PostScript
  `%%For` comment into `/Author` and the XMP; Word's Save as PDF copies the document's Author property,
  which defaults to the Office user. `build/build_word.ps1` handles each, and deletes any output that still
  names the account.
- **Incremental updates remember.** An `/Info` dictionary replaced by a later revision is still in the
  file, and so is the name in it.
- **XFA remembers too**: a LiveCycle form's configuration packet keeps the designer's file paths.
- **Some URLs serve whatever is current**: `irs.gov/pub/irs-pdf/` and the Cerfa `.do` links change with
  each edition, while `irs.gov/pub/irs-prior/` and the EU's Cellar URIs are permanent. EUR-Lex itself
  answers robots with a challenge page; the Cellar serves the same bytes.
- **The Internet Archive's `id_` URLs return the original bytes**, byte-identical to what pdf.js and
  others recorded; without `id_`, the archive rewrites the file.
- **pdfa.org refuses a bare user agent**, and several government sites answer 403 to one.

## What the milestones can take from it

- **M1**: none of the 239 real PDFs crashed or hung the reader. Two hand-written files show it silent
  where qpdf reports damage; they are recorded as unsupported until M2.
- **M2**: the vendored files carry anomalies every reader tolerates and a validator should name without
  calling them errors — stale linearization hint tables (fourteen files), a `/Size` one too large, an xref
  stream without its own entry, references to objects the xref lacks, font-level XMP that is not
  well-formed XML, an empty `/Lang`, a `/Lang` that contradicts the text. `docs/milestones/M2.md` lists
  them among its acceptance conditions.
- **M10**: extraction will meet text drawn as images, an OCR layer, vertical Japanese without ToUnicode,
  Arabic on which xpdf and poppler disagree — xpdf reads "الضرائب", poppler "الرضائب" —, and the PDF24 file
  where ToUnicode says E and no glyph is drawn.
- **M12**: veraPDF 1.30.2 upholds seven of the nine PDF/A claims among the new files and rejects two: the
  2015 consolidated text (rules 6.4-3 and 6.2.3.3-1) and ImageMagick's claim (6.7.3-8, 6.1.8-1, 6.7.3-1,
  6.2.3.3-1). It passes the 2019 notice despite its malformed font XMP, which is worth an opinion of our own.
- **M13**: the W11 references above.

What public sources could not provide — a real Java-stack invoice, a commercial e-signature, a Factur-X
straight from an ERP, a copier's own OCR — is listed as still wanted in `docs/corpus-contributions.md`.
