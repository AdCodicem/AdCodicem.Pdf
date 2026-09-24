# Where the third-party corpus documents come from

On 2026-09-24 the public internet was searched for the documents `docs/corpus-contributions.md` asks for
(W01 to W12), and the test corpora of other PDF libraries were examined for the same purpose. This page
records the rules applied, what entered `tests/corpus/vendor`, what was turned down and why, and what the
search taught us. `tests/corpus/manifest.json` holds the per-file detail: source URL, retrieval date,
SHA-256 of the published bytes, and every expectation.

## The rules a document had to meet

A file entered the corpus only if it met all four. A file that failed one was rejected, not repaired.

1. **An attribution-only licence that covers this file** (ADR 23): public domain — including United
   States federal works under 17 U.S.C. 105 —, CC0, CC BY, the Licence Ouverte, the UK Open Government
   Licence, the EU's reuse decision 2011/833/EU, Japan's Public Data License, MIT, BSD or Apache-2.0.
   ShareAlike, copyleft, non-commercial, no-derivatives and "no licence stated" were all refused. A code
   licence on a repository does not cover the bug-report attachments committed into it.
2. **No real person's contact or identity data anywhere**: no one's phone number, postal address or own
   e-mail address, and no bank, identity or health data tied to a person — in the visible text, `/Info`,
   XMP, annotations, form values, XFA packets, signing certificates, embedded files, and the objects of
   superseded revisions that incremental updates keep in the file. A person's name alone is acceptable,
   and so are a Windows user ID in a path and a photograph. Fabricated, fake and specimen data of any kind
   is fine — John Doe, Contoso, *Entenhausen*, a 555 number. Organisations' addresses and switchboards are
   fine.

   The rule was decided with the maintainer, and relaxed during the search: it began as "no real person's
   name anywhere", with typeface-designer credits, public figures and historical figures as the only
   exceptions. The first 38 files were admitted under that stricter rule, and every file turned down for a
   name alone was then examined again.
3. **The publisher's own bytes**, never a re-saved copy: each file was downloaded again and its SHA-256
   compared, from a permanent URL where one exists (a repository commit, the EU's Cellar URIs, the IRS
   prior-year archive), or from the Internet Archive's unmodified `id_` copy when the publisher has
   withdrawn it.
4. **Small**: at most 2 MB, usually a few hundred kilobytes. W11, where size is the point, is recorded
   below by reference and not committed.

   The maintainer has since turned this rule into a threshold. Size no longer turns a document down: a
   file over 2 MB that meets the other rules goes to the remote corpus (ADR 32) instead of being
   committed. Five files had been refused on size alone, and were then reconsidered on that basis.

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
- **Reopening, after the rule on names was relaxed**: every file turned down for a name alone was examined
  again, and a critic went through the 117 earlier rejections for the ones missed. Two rounds re-downloaded,
  re-screened and re-licensed 70 candidates against the revised rule. **38 more entered**, 6.1 MB, filling
  most of what the first pass had left open.
- **Referees**: page counts and attachments from pikepdf; the `qpdf --check` verdict from qpdf 11.9.1 in
  the integration tests' container; text from poppler's `pdftotext` 24.02; conformance from veraPDF 1.30.2.
- **Our reader** opened every one of the 239 real PDFs downloaded along the way: 209 opened clean, 15
  encrypted ones were refused with the typed exception M9 will replace, 15 opened with repairs or warnings,
  and none crashed or hung. At least one of those warnings is a false alarm of the reader's own — a stream
  whose `endstream` falls just past its 8 KB window is reported truncated (T21 in `docs/status.md`) — and
  the others were not all checked against qpdf: a Cerfa reported truncated may be the same defect.

## What entered the corpus

### First pass

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

### Second pass, under the revised rule

| File under `tests/corpus/vendor/` | W | What it brings |
|---|---|---|
| `zugferd/itext-pdfbox-weclapp-facturx-en16931-invoice.pdf` | W07, W04 | A real ERP's Factur-X (weclapp, a test tenant): iText 2.1.7 then PDFBox, `/MarkInfo` aliasing the page tree, a one-cent gap between page and XML |
| `docentric/aspose-d365-facturx-extended-invoice.pdf` | W07 | Dynamics 365 through Docentric AX and Aspose.PDF for .NET: Factur-X EXTENDED, PDF/A-3B |
| `zugferd/gnuaccounting-mustang10-zugferd-rc-invoice.pdf` | W07, W12 | An accounting package's ZUGFeRD from 2014, in the release-candidate namespace |
| `pdf-association/pdflib-pps-kraxi-pdfa2a-pdfua1-invoice.pdf` | W04, W12 | PDFlib's Java invoice, PDF/A-2a and PDF/UA-1, both upheld by veraPDF |
| `jasper-modular/openpdf-jasperreports-financial-statement.pdf` | W04 | JasperReports 7.0.6 on OpenPDF, fabricated data |
| `swissqrbill/pdfbox-swiss-qr-bill-a4.pdf` | W04 | A Swiss QR-bill from the SwissQRBill library through PDFBox 3, with SIX's specimen data |
| `fr-licence-ouverte/fop-dictao-dila-signed-joafe-notice.pdf` | W05 | A Journal officiel notice signed by DILA's Dictao service: SHA-1/RSA, an organisation certificate |
| `lu-legilux/fop22-legilux-memorial-seal-renewed-timestamps.pdf` | W05 | A 2018 qualified seal kept alive by document timestamps added in 2018 and 2020 |
| `us-federal/xerox-workcentre-5335-ocr-hud-fonsi-linearized.pdf` | W03 | A Xerox copier's own OCR (render mode 3, horizontal scaling up to 2000 %) over a JBIG2 page |
| `us-federal/xerox-workcentre-5755-ocr-hud-fonsi-mrc.pdf` | W03 | Mixed raster content with the copier's own OCR: a JPEG background, JBIG2 masks, a MediaBox with a negative origin |
| `uk-ogl/print-to-pdf-word-cspl-agenda.pdf` | W01 | Print to PDF from Word, untouched, the printing account in `/Author` |
| `us-federal/print-to-pdf-excel-dod-fcf-rates-2021.pdf` | W01 | Print to PDF from Excel on a government workstation, untouched |
| `opf-format-corpus/quartz-word-mac2011-lorem-ipsum.pdf` | W01 | Word for Mac through the macOS Quartz writer: indirect stream lengths, a MacRoman subset |
| `uk-ogl/pdfmaker10-word-dh-care-bill-factsheet.pdf` | W02 | PDFMaker 10 for Word, then an Acrobat incremental save that redefines the pages |
| `uk-ogl/pdfmaker25-home-office-eia.pdf` | W02 | PDFMaker 25 for Word, linearized then updated twice |
| `opf-format-corpus/pdfmaker9-word-distiller-pdfa1b-test-document.pdf` | W02, W12 | PDFMaker 9.1 and Distiller 9.5 with a PDF/A-1b claim veraPDF upholds |
| `opf-format-corpus/pdfmaker9-word-distiller-linearized-font-not-embedded.pdf` | W02 | 8 KB of Adobe linearization: a first-page `startxref 0`, a stale hint table |
| `opf-format-corpus/pdfmaker5-distiller5-va-select-agents.pdf` | W10, W06 | PDF 1.2 from PDFMaker 5 (2002), linearized then updated in 2006; a GovDocs1 error file |
| `us-federal/illustrator-irs-pub1-english.pdf` | W02 | Adobe Illustrator CS6: the English source of the vendored Arabic, Russian and Chinese editions |
| `opf-format-corpus/reader10-openoffice32-annotated-object-streams.pdf` | W02 | An OpenOffice file fully re-saved by Adobe Reader X: Adobe's streams around a third party's content |
| `opf-format-corpus/openoffice32-pdfa1a-embedded-lucida.pdf` | W12 | PDF/A-1a from OpenOffice.org 3.2, upheld by veraPDF |
| `pdf-association/indesign13-pdfua1-german-book-chapter.pdf` | W02, W12 | InDesign CC 2018 claiming PDF/UA-1: a rich tag tree, three incremental updates |
| `pdf-association/indesign15-pdfua1-form.pdf` | W08, W12 | A tagged InDesign form claiming PDF/UA-1, widgets without appearance streams |
| `us-federal/livecycle-irs-1040-2022-xfa-ur3.pdf` | W08 | An XFA form the reader can open today: not encrypted, 136 fields, usage rights |
| `pikepdf/livecycle-dod-dd293-aes128-xfa.pdf` | W08 | An XFA form under AES-128, linearized with a faulty hint table |
| `us-federal/designer-distiller23-uscis-i9-javascript-form.pdf` | W08 | A 2025 AcroForm driven by JavaScript validation, format and keystroke actions |
| `fr-licence-ouverte/pdfmaker-acrobat-cerfa-12156-form.pdf` | W08, W02 | PDFMaker 21 re-saved by Acrobat Pro DC: 406 fields, a calculation order, 494 stale hint entries |
| `fr-licence-ouverte/livecycle-es9-cerfa-14880-xfa-form.pdf` | W08 | LiveCycle Designer ES 9 XFA, usage rights applied twice |
| `us-federal/omniform-usda-rd1924-5-hidden-widgets.pdf` | W06, W08 | OmniForm output whose hidden widgets pdf.js drew outside the page (issue 4914) |
| `us-federal/pdfwriter3-copyright-office-dmca-summary-1998-rc4-40.pdf` | W10 | Acrobat PDFWriter 3.02 (1998), RC4-40, a malformed creation date |
| `us-federal/pdfwriter4-usda-dry-whey-standard-2000.pdf` | W10 | Acrobat PDFWriter 4.05 (2000): page thumbnails, ASCII85 over LZW |
| `us-federal/distiller3-mac-msha-crusher-dust-card-1997.pdf` | W10 | Distiller 3.0 for Power Macintosh (1997): non-embedded Type 1 fonts, eight content streams on a page |
| `us-federal/distiller6-cdc-west-nile-chinese-traditional.pdf` | W09, W10 | Traditional Chinese from Word through Distiller 6 (2004), ArialUnicodeMS as Identity-H |
| `pdf-association/handwritten-compacted-syntax.pdf` | W06 | Every token pairing with no whitespace, and an empty object; xpdf 4.00 extracts nothing from it |
| `pdf-association/handwritten-dual-startxref.pdf` | W06 | Two `startxref` lines, of which only the last is right |
| `pdf-association/handwritten-utf16le-strings.pdf` | W06 | Text strings in UTF-16LE, which the standard forbids and real files contain |
| `pdf-association/handwritten-content-stream-indirect-refs.pdf` | W06 | Indirect references inside a content stream |
| `pdf-association/handwritten-inline-image-abbreviations.pdf` | W06 | Inline images whose abbreviated and full keys disagree |

### Our own, from Windows

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
measurement was taken on. The first and third are now in the remote corpus (below), fetched and tested
every night at these hashes; the heavy scan is left for M13 to add when its memory budgets need it.

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

The manifest now records the source URL, date and SHA-256 of every third-party file, which gives this
corpus the provenance pikepdf has.

## What was turned down, and why

| Candidate | Reason |
|---|---|
| A Journal officiel des associations notice | The registered office is a named couple's home |
| Another such notice | The association's purpose describes a named child's medical care |
| PDF/UA Reference Suite papers and presentation | Their authors' own e-mail addresses are printed on the page |
| CDC Emerging Infectious Diseases article (1997, PDF 1.1) | The author's own e-mail and fax, in an "Address for correspondence" block |
| HUD charge of discrimination (Xerox, the copier's own OCR) | Health data tied to an identifiable complainant |
| Hetzner invoice (Apache FOP), in the ZUGFeRD corpus | A private customer's home address |
| GnuAccounting invoice of May 2014 | A sole proprietor's tax number in the attached XML |
| Environment Canterbury letter (Konica, the copier's own OCR) | Reuse limited to non-commercial purposes |
| JasperReports invoice sample (iText 2.1.7) | Third-party template images the author's licence does not cover |
| National Park Service lesson plan (Canon iR-ADV scan) | The NPS arrowhead is excluded from the Service's public-domain statement |
| European e-Justice page (iText 5) | The EU reuse decision excludes logos, and the page embeds the portal's |
| IRS Form 1040 for 1994 (Net Distiller 1.02) | Complete Type 1 fonts whose embedding permission cannot be checked |

Under the first, stricter rule, a dozen more had been turned down for a name alone — a civil servant in a
superseded `/Info`, an employee's Windows path in an XFA packet, a photographed sales representative, a
`/Creator` nobody could show to be fictitious. All of them entered in the second pass. Four files had
already been readmitted in the first pass after the maintainer's rulings: the two EU Official Journal
notices whose EUAlbertina fonts are embedded in full under an editable-embedding permission, the 2026
consolidated regulation whose only name is a typeface designer's credit inside a font, and the 1786
engraving under ImageMagick's false PDF/A claim, whose only names are historical figures.

## Leads for what is still missing, held back by their licence

These files would fill a gap the corpus still has, and were not committed because their licence is unclear
or excludes redistribution. They are listed so that a later session, or a rights holder asked directly,
can reopen them. None has been screened in full for personal data. [ADR 32](adr/0032-documents-that-cannot-be-redistributed-are-fetched-on-demand.md),
accepted, lets the tests use such files without redistributing them; the leads it has already taken in
are listed under *In the remote corpus* below, and the others remain open to it.

| W | Document | Where | What holds it back | What would clear it |
|---|---|---|---|---|
| W01, W08 | OPM OF-306, a Word for Microsoft 365 form later edited in Acrobat, with JavaScript fields | [opm.gov](https://www.opm.gov/forms/pdf_fill/of0306.pdf) | No OPM reuse statement was found, so public domain rests on 17 U.S.C. 105 alone — the same basis on which the OPM attachment in `us-federal/` was accepted | A consistent ruling on statute-only evidence |
| W02 | UK OZEV plug-in vehicle grant sample invoice, Acrobat PDFMaker 21 for Word | [gov.uk](https://assets.publishing.service.gov.uk/media/62a21964d3bf7f036750b0d2/plug-in-vehicle-grant-portal-sample-invoice.pdf) | OGL v3 coverage not verified | Reading the publication page's licence line |
| W03 | Scan from a Ricoh MP C3003 through 3-Heights, with a PDF/A claim | [pdf.js `issue5747.pdf`](https://github.com/mozilla/pdf.js/blob/b9d5e4f96c255a35ff3b142b26f6657e17258476/test/pdfs/issue5747.pdf) | A bug-report attachment; the repository's licence does not cover it | The reporter's permission |
| W03 | Environment Canterbury letter scanned on a Konica bizhub with its own OCR, mixed raster content | [ecan.govt.nz](https://api.ecan.govt.nz/TrimPublicAPI/documents/download/3147720) | Reuse limited to personal, informational and non-commercial purposes | Not under ADR 23 |
| W03 | Federal Reserve SR 01-15 attachment, OCR by ABBYY FineReader 8 | [federalreserve.gov](https://www.federalreserve.gov/BoardDocs/SRLetters/2001/sr0115a1.pdf) | The Board's reuse statement was not verified; a desktop scanner, not a copier | The Board's website policy |
| W04 | SAP NetWeaver 7.40 form output | [pdf.js `bug1727053.pdf`](https://github.com/mozilla/pdf.js/blob/b9d5e4f96c255a35ff3b142b26f6657e17258476/test/pdfs/bug1727053.pdf) | A Bugzilla attachment, no licence | The reporter's permission |
| W04 | A real Oracle Reports invoice among Mustang's test files | [mustangproject test resources](https://github.com/ZUGFeRD/mustangproject/tree/master/library/src/test/resources) | No licence statement for test files; provenance unknown | The maintainer's account of where it came from |
| W04 | A real Hetzner invoice, Apache FOP 1.0 | [ZUGFeRD/corpus `unstructured/`](https://github.com/ZUGFeRD/corpus/tree/master/unstructured) | Hetzner's document, addressed to a private person | Not recoverable |
| W04 | Real Estonian invoices (Scoro, Axapta) in PdfPig's tests | [PdfPig test documents](https://github.com/UglyToad/PdfPig/tree/master/src/UglyToad.PdfPig.Tests/Integration/Documents) | Third parties' invoices under a code licence; real companies and accounts | Not recoverable |
| W05 | Signed samples, including Ukrainian qualified signatures | [pdfcpu `testdata`](https://github.com/pdfcpu/pdfcpu/tree/master/pkg/testdata) | No licence stated for test data | The maintainer's statement |
| W05 | Foxit PhantomPDF certification and approval signatures (self-signed) | [pdfium_tests `fx/mulobj/new/signature`](https://pdfium.googlesource.com/pdfium_tests) | Only the repository's BSD licence, for Foxit QA files | Foxit's or Google's statement |
| W05 | Signatures from national trust services and ETSI plugtests | [EU DSS `dss-pades` resources](https://github.com/esig/dss/tree/master/dss-pades/src/test/resources) | LGPL-2.1 — clearly copyleft | Not under ADR 23 |
| W05 | Sealed Official State Gazette PDFs (Spain) | [BOE legal notice](https://www.boe.es/informacion/aviso_legal/index.php) | The reuse licence adds conditions beyond attribution | Not under ADR 23 |
| W05 | Signed samples built on a third-party base document | [node-signpdf `resources`](https://github.com/vbuch/node-signpdf/tree/HEAD/resources) | The base document is not the project's | Not recoverable |
| W07 | Vendors' Factur-X and ZUGFeRD samples (intarsys, Symtrax, FNFE-MPE, 4s4u, FeRD) | [ZUGFeRD/corpus `ZUGFeRDv2/correct`](https://github.com/ZUGFeRD/corpus/tree/master/ZUGFeRDv2/correct) | Copies of packages whose originals are "All Rights Reserved" or registration-gated | Each vendor's permission |
| W07 | FNFE-MPE's official Factur-X examples | [fnfe-mpe.org](https://fnfe-mpe.org/factur-x/factur-x_en/) | "All Rights Reserved" | FNFE-MPE's permission |
| W07 | DWC and akretion samples among Mustang's tests ("© DWC 2025" on the page) | [mustangproject test resources](https://github.com/ZUGFeRD/mustangproject/tree/master/library/src/test/resources) | Third-party files under a code licence | Each producer's permission |
| W09 | 2020 Census Hebrew language guide, InDesign, Hebrew Type 1 fonts | [census.gov](https://www2.census.gov/programs-surveys/decennial/2020/resources/language-materials/guides/Hebrew-Guide.pdf) | No Census reuse statement found, and the translation may be a contractor's; 1.68 MB | The Bureau's statement on its language materials |
| W09 | USDA NIFA Title VI fact sheet in Hebrew | [nifa.usda.gov](https://www.nifa.usda.gov/title-vi-fact-sheet-hebrew) | The site refused every connection; not inspected | Another attempt |
| W06 | Bug-report files in pdf.js (~980), PDFium, pdfplumber, PDFBox's JIRA downloads, and the SafeDocs issue-tracker corpus (>32,000) | see the survey table above | Attachments, licensed by nobody | Each reporter's permission |
| W06 | Two real-world files in pdf-differences' UnknownFilter set (one a wine label) | [pdf-differences `UnknownFilter`](https://github.com/pdf-association/pdf-differences/tree/907fe96e52b73e491489eee545c47b119bf9989b/UnknownFilter) | Third parties' documents under the PDF Association's CC BY | Not recoverable |
| W06 | US state documents and Canadian federal forms linked by pdf.js | [pdf.js `test/pdfs`](https://github.com/mozilla/pdf.js/tree/b9d5e4f96c255a35ff3b142b26f6657e17258476/test/pdfs) | State works are not public domain; Canada's reproduction terms are not attribution-only | Not under ADR 23 |

Two good files first held back for a name rather than a licence — the PDF Association's
`CompactedPDFSyntaxTest.pdf` and Docentric's Factur-X EXTENDED sample from Dynamics 365 — entered in the
second pass, once a name alone no longer disqualified a file.

## In the remote corpus

Ten documents are used without being redistributed ([ADR 32](adr/0032-documents-that-cannot-be-redistributed-are-fetched-on-demand.md)):
the manifest pins each one, with origin `remote`, to an immutable URL and a SHA-256, and the `Remote corpus`
workflow fetches them and runs both suites over them every night. Their expectations were established like
everyone else's — pages and verdicts by qpdf 11.9.1 in the integration tests' container, text by poppler's
pdftotext 24.02, conformance by veraPDF 1.30.2 — on 2026-09-24.

| W | Document | Why it is remote | What it adds |
|---|---|---|---|
| W03 | Ricoh MP C3003 scan through 3-Heights (pdf.js `issue5747.pdf`) | A bug-report attachment, licensed by nobody | A copier's CCITT page with an OCR layer in a non-embedded Identity-H font; a valid PDF/A-1b |
| W04 | SAP NetWeaver 7.40 form output (pdf.js `bug1727053.pdf`) | A Bugzilla attachment, licensed by nobody | A real statement from an ERP, the one kind of W04 file the committed corpus still lacks |
| W07 | The FNFE-MPE's official Factur-X example, BASIC WL, in French | All rights reserved | `factur-x.xml` under /AF, a valid PDF/A-3b, written by the factur-x Python library through PyPDF2 |
| W07 | intarsys's ZUGFeRD 2.0 EN 16931 sample | A vendor's sample, no redistribution licence | A second vendor's toolkit, `zugferd-invoice.xml`, a valid PDF/A-3b |
| — | pdfLaTeX with hyperref (`py-pdf/sample-files`) | CC BY-SA 4.0 | The TeX family, absent until now: Computer Modern Type 1 subsets, outline, xref stream |
| — | A Google Docs download (`py-pdf/sample-files`) | CC BY-SA 4.0 | Skia's Google Docs renderer, a Type 3 font beside CID subsets |
| W09 | WeasyPrint 54.1 Arabic (`py-pdf/sample-files`) | CC BY-SA 4.0 | Arabic shaped into CID subsets by an HTML-to-PDF engine — this project's own kind of producer |
| W09 | US Census Bureau 2020 language guide in Hebrew | No reuse statement found; possibly a contractor's translation | Hebrew right to left in Adobe Hebrew Type 1 subsets, tagged with `/Lang he` |
| W11 | United States Code 2023, Title 42 | 37.6 MB, too large to commit | 9,302 pages and a GPO signature in an incremental update; opening, decoding every stream and walking the page tree took 0.8 s on the development machine |
| W11 | US Topo map, Washington West, 2023 | 63.1 MB, too large to commit | One page with a 109.5-megapixel image; recorded as unsupported until T21, the false truncated-stream report it exposed, is fixed |

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
- **A registered office can be someone's home.** Notices about associations give their seat, and a small
  association's seat is often its founder's house — one gave it as "chez M. et Mme …". An association's
  stated purpose can even describe a named child's illness. Each notice had to be read, not sampled.
- **A copier's name survives a later save.** Two Xerox scans still carry the copier as Producer after an
  unidentified tool re-serialised them; the OCR layer is the copier's (its own font names, no Acrobat
  trace), but the bytes are not the copier's alone.

## What the milestones can take from it

- **M1**: none of the 239 real PDFs downloaded in the first pass crashed or hung the reader, and all 76
  vendored files open as their entries say. Two hand-written files show it silent where qpdf reports
  damage; they are recorded as unsupported until M2. On the empty object of the compacted-syntax file,
  the reader and qpdf agree.
- **M2**: the vendored files carry anomalies every reader tolerates and a validator should name without
  calling them errors — stale linearization hint tables (some twenty files), a `/Size` one too large, an
  xref stream without its own entry, references to objects the xref lacks, font-level XMP that is not
  well-formed XML, an empty `/Lang`, a `/Lang` that contradicts the text, UTF-16LE text strings, two
  `startxref` lines, a `/MarkInfo` pointing at the page tree, `/Info` and XMP that disagree.
  `docs/milestones/M2.md` lists them among its acceptance conditions.
- **M10**: extraction will meet text drawn as images, OCR layers — Acrobat's and two copiers' own, with
  horizontal scaling up to 2000 % —, vertical Japanese without ToUnicode, Arabic on which xpdf and poppler
  disagree — xpdf reads "الضرائب", poppler "الرضائب" —, the PDF24 file where ToUnicode says E and no glyph
  is drawn, a compacted-syntax file from which xpdf 4.00 extracts nothing while poppler and PDFium do,
  indirect references inside a content stream, and inline images whose keys contradict each other.
- **M11**: XFA forms from Designer 6.4, 6.5 and ES 9, one of them readable today; AcroForms driven by
  JavaScript; a calculation order; widgets without appearance streams.
- **M12**: veraPDF 1.30.2 upholds thirteen of the fifteen PDF/A claims among the new files, and PDFlib's
  PDF/UA-1 claim. It rejects two: the 2015 consolidated text (rules 6.4-3 and 6.2.3.3-1) and ImageMagick's
  claim (6.7.3-8, 6.1.8-1, 6.7.3-1, 6.2.3.3-1). It passes the 2019 notice despite its malformed font XMP,
  which is worth an opinion of our own.
- **M3, M4**: signatures to keep intact through an incremental update — a GPO certification, DILA's
  Dictao signature, a qualified seal renewed by timestamps over two years, PAdES B-LTA, Acrobat signatures.
- **M13**: the W11 references above.

What public sources could not provide — a real invoice or statement from a supplier or bank, a commercial
e-signature, a copier file untouched since the copier wrote it, Hebrew — is listed as still wanted in
`docs/corpus-contributions.md`.
