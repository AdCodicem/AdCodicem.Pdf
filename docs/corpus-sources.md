# Where the third-party corpus documents come from

On 2026-09-24 the public internet was searched for the documents `docs/corpus-contributions.md` asks for
(W01 to W12), and the test corpora of other PDF libraries were examined for the same purpose. This page
records the rules applied, what entered `tests/corpus/vendor` and what entered the remote corpus — used
without being redistributed —, what was turned down and why, and what the search taught us.
`tests/corpus/manifest.json` holds the per-file detail: source URL, retrieval date, SHA-256 of the
published bytes, and every expectation.

## The rules a document had to meet

A file entered the corpus only if it met all four. A file that failed one was rejected, not repaired.

1. **An attribution-only licence that covers this file** (ADR 23): public domain — including United
   States federal works under 17 U.S.C. 105 —, CC0, CC BY, the Licence Ouverte, the UK Open Government
   Licence, the EU's reuse decision 2011/833/EU, Japan's Public Data License, MIT, BSD or Apache-2.0.
   ShareAlike, copyleft, non-commercial, no-derivatives and "no licence stated" were all refused. A code
   licence on a repository does not cover the bug-report attachments committed into it. A file that fails
   this rule may still be used without being redistributed, in the remote corpus
   ([ADR 32](adr/0032-documents-that-cannot-be-redistributed-are-fetched-on-demand.md)); *In the remote
   corpus* below says which terms that admits.
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

   The third pass brought four more rulings. An image of a named person's handwritten signature counts as
   the name: acceptable, even in a committed file. A software library author's own e-mail address inside
   that library's copyright string, compiled into the file, is a library credit and acceptable, as a
   typeface designer's credit inside a font is. An anonymous photograph whose caption describes a health
   condition discloses no one's health. A fabricated e-mail address used as a test constant is fictitious
   data, even at a real domain.
3. **The publisher's own bytes**, never a re-saved copy: each file was downloaded again and its SHA-256
   compared, from a permanent URL where one exists (a repository commit, the EU's Cellar URIs, the IRS
   prior-year archive), or from the Internet Archive's unmodified `id_` copy when the publisher has
   withdrawn it.
4. **Small**: at most 2 MB, usually a few hundred kilobytes. W11, where size is the point, is recorded
   below by reference and not committed.

   The maintainer has since turned this rule into a threshold. Size no longer turns a document down: a
   file over 2 MB that meets the other rules goes to the remote corpus (ADR 32) instead of being
   committed. Five files had been refused on size alone, and were then reconsidered on that basis. Three
   went to the remote corpus: the Open Preservation Foundation's signed 3D portfolio (CC0, 4.7 MB), and
   two members of the PDF/UA Reference Suite, a tagged scan (10.6 MB) and a textbook chapter (2.3 MB),
   both a publisher's content, which the suite's CC BY cannot be shown to cover. Two were refused again, for
   personal data this time: a NIST request for quotation scanned on a Canon SC1011 prints two named staff
   members' direct phone lines and own e-mail addresses — and Acrobat had re-saved it anyway, so it was
   not the untouched copier file W03 wants —, and the suite's Danish magazine gives the health conditions
   of named people, children among them.

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
- **Third pass, the leads held back by their licence**: that evening, eleven research groups took up
  the leads this page listed as held back by their licence, and looked for more of their kind. They
  examined 113 candidates and recorded 158 further leads they left, each with its reason. Verifiers, again
  told to reject on doubt, upheld 79 of the 80 files proposed, and the integration set aside two more, one
  of which the maintainer then admitted. With the files once refused for size and a PDF/UA brochure
  reconsidered, **82 entered**: 6 committed and 76 remote. The committed corpus now holds 112 documents, 16.8 MB.
- **Referees**: page counts and attachments from pikepdf; the `qpdf --check` verdict from qpdf 11.9.1 in
  the integration tests' container; text from poppler's `pdftotext` 24.02; conformance from veraPDF 1.30.2.
- **Our reader** opened every one of the 239 real PDFs downloaded along the way: 209 opened clean, 15
  encrypted ones were refused with the typed exception M11 will replace, 15 opened with repairs or warnings,
  and none crashed or hung. At least one of those warnings is a false alarm of the reader's own — a stream
  whose `endstream` falls just past its 8 KB window is reported truncated (T21 in `docs/status.md`) — and
  the others were not all checked against qpdf: a Cerfa reported truncated may be the same defect.
  In the third pass the reader was run over every file taken in, against expectations qpdf had set. None
  crashed or hung it. It fell short on five, recorded as unsupported rather than fixed on this branch: an
  object longer than its 8 KB window is cut at the edge (T23, two files), each cross-reference section is
  read through a window of up to 64 KB whatever its size (T24, one file), and two files leave it silent
  where qpdf reports damage.

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

### Third pass: the leads held back by their licence

The third pass went through the leads the first two had held back for their licence, in eleven groups:
signatures (pdfcpu's test data, EU DSS's test resources, Foxit's files in pdfium_tests, node-signpdf, the
BOE's sealed gazette, and a hunt for commercial e-signatures), real invoices and statements, vendors'
Factur-X and ZUGFeRD samples, the government leads, files that broke pdf.js, PDFBox, PDFium and
pdfplumber, and the ShareAlike sets. Most of what it found may be used but not redistributed, and joined
the remote corpus (below). Six files could be committed after all: two that node-signpdf made itself,
which its MIT licence does cover; the ZUGFeRD corpus maintainer's own factur-x output; two government
leads whose licence was verified at the source; and, from the hunt, a GSA contract modification completed
in DocuSign — the first commercial e-signature the committed corpus holds. All six passed a last,
independent review of their bytes, licence and personal data. Three files proposed for committing went
remote instead: the USDA's Hebrew fact sheet, whose banner carries a symbol the USDA reserves; the PDFpen
file from OCRmyPDF, whose `REUSE.toml` entry the issue record contradicts; and a truncated FEMA form,
public domain but not the publisher's bytes.

| File under `tests/corpus/vendor/` | W | What it brings |
|---|---|---|
| `node-signpdf/skia-chrome74-node-signpdf-reason-contains-trailer.pdf` | W05, W06 | node-signpdf's own Chrome 74 print, signed in an incremental update whose `/Reason` contains the keyword `trailer`; `startxref` on the line end before `xref` |
| `node-signpdf/pdfkit-node-signpdf-unsigned-placeholder.pdf` | W05 | An unsigned signature placeholder from PDFKit: name tokens in `/ByteRange`, 8,192 zero bytes of `/Contents` |
| `us-federal/docusign-pdfkit-gsa-sf30-contract-modification.pdf` | W05, W03 | A commercial e-signature: DocuSign's envelope seal with a FieldMDP reference (MD5) over a JBIG2 scan on a page rotated 270°, validation data appended in an update, a creation date in year 0001 |
| `zugferd/pypdf2-facturx-python-false-pdfa3b.pdf` | W07, W12 | Factur-X BASIC from the factur-x Python library through PyPDF2, with a PDF/A-3b claim veraPDF rejects on five rules — no trailer `/ID`, no binary comment, no output intent, a bare metadata stream |
| `us-federal/finereader8-frb-sr0115-examiner-guidance.pdf` | W03, W02 | ABBYY FineReader 8 OCR beneath 300 dpi CCITT G4 page images, tagged, in a linearized hybrid-reference file |
| `uk-ogl/pdfmaker21-ozev-sample-invoice.pdf` | W02, W04 | PDFMaker 21 for Word, tagged, a "Sample" watermark in an Acrobat optional-content group set in a complete Arial |

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
measurement was taken on. All three are now in the remote corpus (below), fetched and tested every night
at these hashes: the heavy scan joined the other two in the third pass, so M13 can state its memory
budgets against every one of them.

## What other PDF libraries ship

Almost every PDF library keeps a corpus. Very few could be reused here, and the reason is nearly always
the same: the files are attachments to bug reports, committed under the repository's code licence by
someone who did not own them.

| Library or corpus | Test PDFs | What covers the PDFs | Usable here |
|---|---|---|---|
| veraPDF corpus | ~2,900 | CC BY 4.0; the Isartor folder forbids redistribution | Yes, already vendored; Isartor excluded |
| BFO PDF/A suite | 34 | CC BY 3.0 | Yes |
| PDF/UA Reference Suite (PDF Association) | 19 | CC BY 4.0 | Three members committed (2-02, 2-05, 2-10); three remote — 2-06, whose cover photograph is a third party's, and 2-08 and 2-09, publishers' textbook chapters, both over 2 MB as well; 2-01 refused for named people's health data, the papers and presentation for their authors' e-mail addresses |
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

"Usable here" means committable. Since ADR 32, the remote corpus also uses files from pdfcpu, EU DSS,
pdfium_tests, node-signpdf, pdf.js, PDFium, PDFBox, PdfPig, pdfminer.six, pdfplumber, Mustang, the ZUGFeRD
corpus, OCRmyPDF, py-pdf/sample-files, pdf20examples, pdf-differences, the PDF/UA Reference Suite and the
OPF format-corpus: see *In the remote corpus*.

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
| JasperReports invoice sample (iText 2.1.7) | Third-party template images the author's licence does not cover |
| National Park Service lesson plan (Canon iR-ADV scan) | The NPS arrowhead is excluded from the Service's public-domain statement |
| European e-Justice page (iText 5) | The EU reuse decision excludes logos, and the page embeds the portal's |
| IRS Form 1040 for 1994 (Net Distiller 1.02) | Complete Type 1 fonts whose embedding permission cannot be checked |
| NIST request for quotation scanned on a Canon SC1011 (3.9 MB) | Two staff members' direct phone lines and own e-mail addresses; and Acrobat had re-saved it, so it was not the untouched copier file W03 wants |
| PDF/UA Reference Suite 2-01, a Danish magazine | Health conditions of named people, children among them |
| California Geological Survey Note 17, linked by pdf.js | Reproduction for classroom or public education only: a restriction of purpose, which the remote corpus refuses too |
| Mustang's Oracle Reports invoice (ZUGFeRD EXTENDED) | Its attached XML gives an employee's direct line and e-mail |
| A Business Central invoice from Aspose.Words, among Mustang's tests | Its attached XML gives a real person's e-mail |
| An Oracle PL/PDF insurance invoice, in PdfPig's tests | A named contact person's mobile number |
| An AWS invoice from Apache FOP 0.95, in invoice2data's tests | The billing address is, in all likelihood, a person's home |
| Symtrax's Factur-X from SAP demo data, with a false PDF/A-3a claim | A seller account that passes every IBAN check and whose owner is unknown: excluded on doubt |
| Adobe's well-known signed sample, in pdfcpu's test data | The signer certificate carries an employee's own e-mail, printed again in the visible signature |
| A qualified signature among pdfcpu's samples | A real person's qualified certificate, with a persistent holder identifier |
| Most of the signed files in EU DSS's test resources | Real people's certificates: national identity or birth numbers, personal e-mail addresses and phones, a home address; and a Commission seal whose pages give an official's direct line |
| A notarial mortgage deed under a commercial e-seal, in EU DSS's test resources | A named person's home address and passport number, perhaps fabricated: excluded on doubt |
| Public bodies' contracts signed through DocuSign or Adobe Sign, with the audit report | The report lists each signer's e-mail and IP address |
| Ten damaged files from pdf.js, PDFBox and pdfplumber bug reports | Personal or health data: employees' e-mails on orphaned pages, a private car's registration, a household's electricity bill, a patient's blood test |

Under the first, stricter rule, a dozen more had been turned down for a name alone — a civil servant in a
superseded `/Info`, an employee's Windows path in an XFA packet, a photographed sales representative, a
`/Creator` nobody could show to be fictitious. All of them entered in the second pass. Four files had
already been readmitted in the first pass after the maintainer's rulings: the two EU Official Journal
notices whose EUAlbertina fonts are embedded in full under an editable-embedding permission, the 2026
consolidated regulation whose only name is a typeface designer's credit inside a font, and the 1786
engraving under ImageMagick's false PDF/A claim, whose only names are historical figures.

## Leads for what is still missing, held back by their licence

These files would fill a gap the corpus still has, and are in it neither committed nor remote. They are
listed so that a later session, or a rights holder asked directly, can reopen them. The third pass worked
through what this table used to hold: most of it now sits under *In the remote corpus*
([ADR 32](adr/0032-documents-that-cannot-be-redistributed-are-fetched-on-demand.md)), six files turned out
to be committable after all (*Third pass* above), and the Oracle Reports invoice was turned down for
personal data. What remains open is a note the remote corpus may not use, an invoice addressed to a private
person, and files no one has yet screened or checked — most of them in the bug-report corpora.

| W | Document | Where | What holds it back | What would clear it |
|---|---|---|---|---|
| W06, W10 | California Geological Survey Note 17: PageMaker and PDFWriter 4 for Power Macintosh (2002), re-secured with RC4-128, 28 CCITT masks tiling one map | [pdf.js `test/pdfs`](https://github.com/mozilla/pdf.js/tree/b9d5e4f96c255a35ff3b142b26f6657e17258476/test/pdfs) (issue 4926, a link to an Internet Archive capture) | Reproduction permitted "for classroom or public education purposes" only: a restriction of purpose, which the remote corpus does not accept either | The Department of Conservation's permission |
| W04 | A real Hetzner invoice, Apache FOP 1.0 | [ZUGFeRD/corpus `unstructured/`](https://github.com/ZUGFeRD/corpus/tree/master/unstructured) | Hetzner's document, addressed to a private person | Not recoverable |
| W06 | The rest of the bug-report files in pdf.js (~980), PDFium, pdfplumber, PDFBox's JIRA downloads, and the SafeDocs issue-tracker corpus (>32,000); nineteen went remote in the third pass | see the survey table above | Attachments, licensed by nobody: the remote corpus can take them, but each needs its own personal-data screen — of the 31 the third pass examined closely, it turned down 10 for personal data and 2 whose damage was made by hand | A later pass. The third pass named reserves: PDFBOX-4490 and PDFBOX-3977, screened and clean; pdf.js `issue5549` and `issue5567` for JPEG 2000, `issue8702` and `issue14814` |
| W06, W08 | The rest of the files pdf.js links to: US state documents, Canadian forms, forms attached to Bugzilla | [pdf.js `test/pdfs`](https://github.com/mozilla/pdf.js/tree/b9d5e4f96c255a35ff3b142b26f6657e17258476/test/pdfs) | Terms that are not attribution-only, so remote at best; the third pass took three, and the attached forms are often filled in by real people | A personal-data screen each |
| W08, W05 | A filled form with Reader usage rights and a signature, in EU DSS's resources (`pades-signed-filled-form.pdf`) | [EU DSS `dss-pades` resources](https://github.com/esig/dss/tree/master/dss-pades/src/test/resources) | LGPL-2.1, so remote at best; its field values were not screened in full | A full screen |
| W06 | The PDF Association's Brotli prototype, a `/BrotliDecode` filter qpdf does not know, among pdf.js's test files | [pdf.js `test/pdfs`](https://github.com/mozilla/pdf.js/tree/b9d5e4f96c255a35ff3b142b26f6657e17258476/test/pdfs) | Not a bug-report file, but its licence was not checked | A look at the PDF Association's own repository |

Two good files first held back for a name rather than a licence — the PDF Association's
`CompactedPDFSyntaxTest.pdf` and Docentric's Factur-X EXTENDED sample from Dynamics 365 — entered in the
second pass, once a name alone no longer disqualified a file.

## In the remote corpus

Eighty-six documents are used without being redistributed ([ADR 32](adr/0032-documents-that-cannot-be-redistributed-are-fetched-on-demand.md)):
the manifest pins each one, with origin `remote`, to an immutable URL and a SHA-256, and the `Remote corpus`
workflow fetches them — some 290 MB, half of it one scan — and runs both suites over them every night.
Their expectations were established like everyone else's — pages and verdicts by qpdf 11.9.1 in the
integration tests' container, text by poppler's pdftotext 24.02, conformance by veraPDF 1.30.2 — on
2026-09-24: ten during the day, 76 in the evening's third pass. For those 76, whether a file opens clean
and whether its index must be rebuilt were derived from qpdf's warnings, not from our reader; the reader
was then run against them, and where it falls short the entry is marked unsupported, with the reason and
the milestone that owes the fix.

Rule 2 holds here in full: a remote document is screened for personal data exactly as a committed one.
Rules 1, 3 and 4 are what the remote corpus relaxes, within limits the maintainer set in the third pass.
Since nothing is redistributed, terms that restrict reuse do not keep a document out — conditions beyond
attribution, non-commercial reproduction only, nothing beyond fair use, no modification or commercial
use. A restriction of purpose does: a note that may be reproduced "for classroom or public education
purposes" only was left out.

| W | Document | Why it is remote | What it adds |
|---|---|---|---|
| W01, W08 | OPM Optional Form 306, from an Internet Archive capture, since opm.gov serves whatever edition is current | No OPM reuse statement, so public domain rests on 17 U.S.C. 105 alone | A Word for Microsoft 365 document made into a tagged AcroForm by Acrobat, Producer left as Word: 40 fields with date JavaScript, two unsigned signature fields |
| W01 | A Border Force poster, from GOV.UK | OGL v3, except for three other bodies' logos it carries | Microsoft Print To PDF with the text as outlines and no font at all, then an Adobe update that rewrote `/Info` and added XMP |
| W03 | Ricoh MP C3003 scan through 3-Heights (pdf.js `issue5747.pdf`) | A bug-report attachment, licensed by nobody | A copier's CCITT page with an OCR layer in a non-embedded Identity-H font; a valid PDF/A-1b |
| W03 | A Konica Minolta bizhub C554e scan of a regional council's letter, untouched (Environment Canterbury, fetched from the Internet Archive's capture) | Reuse for personal, informational and non-commercial purposes only | The untouched copier file W03 lacked: mixed raster content — a JPEG background under CCITT image masks — and the copier's own invisible OCR in a non-embedded Arial |
| W03 | A Kodak Capture Desktop scan and an Epson device scan, from OCRmyPDF's test resources | CC BY-SA 4.0 and CC BY-SA 3.0 | One CCITT G4 page each, and structural slips: an outline whose `/First` names a missing object and whose `/Last` points at itself; a Pages node nested in another, `/Rotate` as an indirect object, no `/Info` |
| W03 | PDF/UA Reference Suite 2-09, a scanned textbook chapter remediated by AbleDocs (10.6 MB) | Over 2 MB, and the pages are a publisher's that the suite's CC BY cannot be shown to cover | A tagged scan under a PDF/UA-1 claim veraPDF upholds: 82 pages whose OCR text is drawn first and the page image painted over it as a pagination artifact, 480 formulas with spoken alt text, JBIG2 without globals, a catalog `/Lang` of `(English)` |
| W04 | SAP NetWeaver 7.40 form output (pdf.js `bug1727053.pdf`) | A Bugzilla attachment, licensed by nobody | A real statement from an ERP, the one kind of W04 file the committed corpus still lacks |
| W04 | An SAP NetWeaver 7.40 invoice and an AFP Batch Processor bank statement excerpt, from pdfminer.six | A supplier's and a bank's documents, kept under a code licence that does not cover them | SAP: indirect `/Filter` arrays, ArchiveLink comments inside `/Info`, one logo embedded twice. AFP: bitmap Type 3 fonts with a named encoding and no ToUnicode, later re-saved by an Adobe application |
| W04, W06 | An Axapta credit note and a Scoro invoice, from PdfPig | Suppliers' documents attached to bug reports; PdfPig's Apache-2.0 covers its code, not them | Axapta's own writer: an `/Info` object without `endobj`, non-embedded Identity-H fonts, a ToUnicode that turns the minus into a soft hyphen. Scoro: a Chromium page re-serialised by qpdf, then an ExifTool update that adds `/Info` and XMP |
| W04, W06 | A card statement page from PDFlib+PDI on IBM z/OS (pdf.js `issue6605.pdf`) | A bug-report attachment, licensed by nobody | EBCDIC character codes in bitmap Type 3 fonts; a Nitro Pro update leaving 115 orphaned objects |
| W04 | A central bank's rates bulletin from JasperReports on iText 2.1.0 | The bank's output in an MIT repository whose grant does not reach it; the bank's terms forbid modification and commercial use | A real seven-column table in non-embedded Helvetica |
| W05 | Three PAdES levels on one Word 2019 page — B-B, B-LT, B-LTA — signed with the Ukrainian DIIA test CA, from pdfcpu's test data | pdfcpu states no licence for its test data | The baseline levels one at a time: CAdES detached, a signature timestamp, a DSS of certificates and OCSP responses without VRI, a document timestamp lacking `/Filter`; every update glued to the `%%EOF` marker |
| W05 | Avow Systems' certified sample and Tecxoft's signed web capture, from pdfcpu's test data | Vendors' samples, no licence | DocMDP P=1 with FieldMDP and legacy MD5 object digests, embedded CRL and OCSP. The legacy `adbe.x509.rsa_sha1` subfilter: a raw PKCS#1 value, the certificate in `/Cert`, over a three-page Acrobat Web Capture 8.0 capture; unsupported for T24 (see *Traps*) |
| W05, W02, W12 | The Slovak National Security Authority's supervision scheme under its qualified seal, from EU DSS | LGPL-2.1, and no reuse licence from the authority | A certification signature (DocMDP P=2 with FieldMDP, MD5 references) saved by Acrobat 11 into a linearized file; a PDF/A-1a claim veraPDF rejects |
| W05 | Two ETSI plugtest files, from Hungary and France, in EU DSS | LGPL-2.1 | Two qualified seals, RSA then ECDSA, each followed by a document timestamp, and a Producer written as a byte-order mark before single-byte text; a PAdES-EPES signature with claimed roles under SHA-512, then two document timestamps |
| W05 | A Notepad printout re-signed over seven years, from EU DSS | LGPL-2.1 | A 2013 signature, document timestamps in 2013 and 2019, a 2020 signature in RSASSA-PSS: five incremental updates, two `startxref` one byte early |
| W05, W11 | An Excel sheet with 24 signatures and a document timestamp, from EU DSS | LGPL-2.1 | 48 incremental updates; a DSS of 31 certificates, 29 OCSP responses and 24 VRI entries. Unsupported for T23: the 10,112-byte `/VRI` dictionary is cut at 8 KB |
| W05, W06 | EU DSS's infinite-loop regression file (DSS-1872) | LGPL-2.1, over a chart under CC BY-SA | A signed update nobody can reach: `startxref` past the end of the file, wrong offsets, a signature widget that is its own `/Parent`; it hung xpdf 4.00 |
| W05, W02 | The Spanish Official State Gazette: a 2015 law, a 2026 royal decree, and a decree re-signed among EU DSS's resources | The BOE's reuse terms add conditions beyond attribution; the third is LGPL-2.1 as well | The gazette's seal — `adbe.pkcs7.sha1`, no signed attributes, an RFC 3161 timestamp as an unsigned attribute — over PDF/A-1a claims veraPDF upholds; the law is unsupported for T23 (36 structure arrays of about 8.7 KB); the re-signed decree adds a test signature, and veraPDF rejects its claim |
| W05, W03 | Commercial e-signatures: a DocuSign envelope export (EU DSS), a Maine contract amendment completed in DocuSign, an Adobe Sign test agreement (pdfcpu issue 389), a Yousign-sealed test page (qpdf issue 1469) | LGPL-2.1 over DocuSign's own form; a state work with no reuse statement; two issue attachments | Beside the committed GSA file: DocuSign's seal extended by a DSS and a Czech demo TSA's document timestamp; a Ricoh copier scan on rotated pages sealed through iTextSharp; an ETSI.CAdES.detached certification under AES-128 with an empty user password; a qualified e-seal written by a full rewrite, over read-only fields |
| W05 | Three Foxit PhantomPDF signatures, from pdfium_tests | Foxit's QA files; the repository's BSD licence covers the PDFium authors' own work | Certification and approval signatures written in a single full save, the byte range covering the whole file, CMS without signed attributes; in one, a BBox of garbage reals inside the signed bytes leaves a string open to the end of the file |
| W05 | An OpenOffice.org page signed twice by node-signpdf | The base document is W3C's test file, whose licence is not node-signpdf's | Two signatures in two updates, CMS signed attributes not in DER order, `startxref` on the line end before each `xref` |
| W05, W02, W08 | Acrobat 9's signed 3D portfolio, from the Open Preservation Foundation (4.7 MB) | CC0, but over 2 MB | The first PDF portfolio: a collection schema, folders, a Flash navigator and five embedded PDFs, among them an XFA form filled with fictitious values under a certification signature, and U3D and PRC 3D models |
| W06 | Four files from pdf.js's tests that break structure: an Aspose.CAD drawing written over an older, longer file; a Firefox print through cairo; a Canon scanner page; Aspose.Pdf for Java output | Bug-report files, licensed by nobody | A stale tail whose trailer names `/Info` as `/Root`, so qpdf and poppler find no page; 65,542 objects in one object stream, their indexes wrapped at 16 bits, so poppler loses the catalog; junk after `%%EOF` and a corrupt JPEG under Flate; `/DecodeParms [null]` on every stream and a `/ToUnicode` that is a name |
| W06 | Five more from pdf.js's tests, on fonts and images: typeset.sh, Oracle Outside In, Esri ArcMap, Acrobat 8 image conversion, iText 5 | Bug-report attachments, licensed by nobody | A FontFile2 whose deflate data is corrupt, and text before any `Tf`; a PDF/A-1a veraPDF passes over a Calibri subset with a broken `loca`; a CFF FDSelect starting at glyph 1; JPEG 2000 with several precincts per level; a CCITT G4 stencil as an explicit `/Mask` |
| W06, W11 | The Massachusetts COVID-19 dashboard of 25 October 2020, linked by pdf.js | Mass.gov forbids any copying beyond fair use | A Power BI export through PDFium, linearized and then saved nine more times by Acrobat, which kept PDFium as Producer: 24 pages at the end of a long `/Prev` chain |
| W06 | A wine merchant's product sheet, from pdf-differences' UnknownFilter set | A third party's document under the PDF Association's CC BY | A PNG stored under `/DCTDecode`; Identity-H subsets without ToUnicode; no `/Info` |
| W06 | Four files from PDFBox's JIRA downloads, damaged at their end: Amyuni PDF Converter output cut 1,950 bytes short, a Distiller 8 file with 7.6 KB of UTF-16LE text after `%%EOF`, a FrameMaker 10 budget book and a Word 2010 FEMA form each cut at 1 MB | Web-crawled bug attachments; the FEMA form is public domain, but a crawl cut short is not the publisher's bytes | A lost main cross-reference section and a `/Prev` past the end; `startxref` hidden from a 1 KB tail search; and no page count asserted for the two cut at 1 MB, since qpdf 11.9.1 finds no `/Root` in one, and qpdf 11.9.1, PDFium and qpdf 12 count 11, 8 and 4 pages in the other |
| W06, W10 | Three more from PDFBox's JIRA: a 1998 PDFWriter 3.02 file whose CR LF line ends were stripped, a Distiller 6 report with a 32 KiB block zeroed, an Acrobat form with `#00` in names | Bug attachments; the report is a public-domain USGS work, but exists damaged only there, and weighs 2.1 MB | Offsets 3 to 175 bytes off and `startxref` 184 off; one object stream destroyed, which the reader detects only when it reaches it, so the entry expects no rebuild at opening and the rebuild diagnostic after a full read; a NUL in names used as values and keys, which the reader accepts silently (unsupported until M2) |
| W06, W09 | PDFium's `bug_182.pdf`, and two pdfplumber reporters' files | Bug-report files, licensed by nobody | UTF-16BE `/Info` strings opening with a language escape; an inverted MediaBox over a Flate-over-DCT scan; Chinese font names written as GBK bytes in `#xx` escapes |
| W06 | A PDFpen page of text imprints, from OCRmyPDF | Its contributor agreed to CC BY-SA 4.0; OCRmyPDF's `REUSE.toml` now says MIT, with no MIT grant on record | Seven content streams whose `q` and `Q` fall in different streams; PDFpen's private keys and plist streams |
| W07 | The FNFE-MPE's official Factur-X example, BASIC WL, in French | All rights reserved | `factur-x.xml` under /AF, a valid PDF/A-3b, written by the factur-x Python library through PyPDF2 |
| W07 | intarsys's ZUGFeRD 2.0 EN 16931 sample | A vendor's sample, no redistribution licence | A second vendor's toolkit, `zugferd-invoice.xml`, a valid PDF/A-3b |
| W07, W12 | Two DWC FX Generator invoices through WeasyPrint, among Mustang's tests | "© DWC 2025" on the page, and no redistribution licence | Factur-X EXTENDED in PDF 2.0 under a valid PDF/A-4f claim, two RDF blocks in one XMP packet, `factur-x.xml` listed twice in `/AF`; a sibling whose XMP has no Factur-X schema, under a valid PDF/A-3u claim |
| W07 | The official Order-X example, COMFORT profile, among Mustang's tests | FNFE-MPE's and FeRD's example, no redistribution licence | A purchase order rather than an invoice: `order-x.xml`, from the factur-x Python library through PyPDF4 |
| W07, W12 | Four vendors' samples from the ZUGFeRD corpus: intarsys (ZUGFeRD 2.2, XRECHNUNG profile), Symtrax Compleo through iTextSharp 4.1.0 (MINIMUM), 4s4u's additional-data library (ZUGFeRD 1.0 EXTENDED), Konik (ZUGFeRD 1.0 BASIC) | Copies of vendors' packages with no redistribution licence; Konik's is AGPL-3.0 | `xrechnung.xml` attached by an incremental update; a valid PDF/A-3a with a whole Times New Roman embedded; a second XML attached as `/Supplement`, CR-only line ends; one subset tag shared by four font programs |
| W07, W04 | An ERP's test invoice made hybrid by iText Core 9 (quba-viewer issue 143) | An issue attachment, licensed by nobody | A `factur-x.xml` that holds a UBL invoice rather than CII |
| W08, W05 | Two Canadian dynamic XFA forms, linked by pdf.js: an immigration form (IMM 1344) and a fish export licence application | Government of Canada terms: non-commercial reproduction only | The first dynamic XFA, whose only PDF page is a "please wait" placeholder; AES-128 with an empty user password; a certification with a timestamp, then UR3 usage rights in an update; legacy `/UR` beside `/UR3` |
| W09 | WeasyPrint 54.1 Arabic (`py-pdf/sample-files`) | CC BY-SA 4.0 | Arabic shaped into CID subsets by an HTML-to-PDF engine — this project's own kind of producer |
| W09 | US Census Bureau 2020 language guide in Hebrew | No reuse statement found; possibly a contractor's translation | Hebrew right to left in Adobe Hebrew Type 1 subsets, tagged with `/Lang he` |
| W09 | USDA Title VI fact sheet in Hebrew, from an Internet Archive capture | The USDA reserves the symbol on its banner; the translation's provenance is unknown | Hebrew from Word through PDFMaker 23, in TrueType and CID subsets beside the Census guide's Type 1; Hebrew outline titles |
| W11 | United States Code 2023, Title 42 | 37.6 MB, too large to commit | 9,302 pages and a GPO signature in an incremental update; opening, decoding every stream and walking the page tree took 0.8 s on the development machine |
| W11 | US Topo map, Washington West, 2023 | 63.1 MB, too large to commit | One page with a 109.5-megapixel image; recorded as unsupported until T21, the false truncated-stream report it exposed, is fixed |
| W11, W03 | USGS Professional Paper 1 (1902), scanned in 2017 | 146.7 MB, too large to commit | 125 JPEG 2000 page images under an invisible OmniPage 19 OCR layer, tagged; linearization hints that point past the end of the file |
| W11, W06 | A tiff2pdf image of 35,000 × 35,000 pixels, from OCRmyPDF | CC BY-SA 4.0 | 10.5 KB of CCITT G4 that decodes to 153 MB, on an 8,400-point page under a PDF 1.1 header |
| W12 | Ghostscript 10 output claiming PDF/A-1b (`py-pdf/sample-files`) | CC BY-SA 4.0 | Type 1C subsets without ToUnicode, word spaces only as TJ kerning, an AdobeRGB output intent; veraPDF upholds the claim |
| W12, W06 | A Photoshop CC 2015 page claiming PDF/X-3, from OCRmyPDF | CC BY-SA 4.0 | CMYK JPEGs, a SWOP output intent, a CID subset whose ToUnicode values are byte-swapped |
| W12 | PDF/UA Reference Suite 2-08, a textbook chapter remediated by AbleDocs (2.3 MB) | Over 2 MB, and the chapter prints a publisher's and photo agencies' copyrights the suite's CC BY cannot be shown to cover | A PDF/UA-1 claim veraPDF upholds; PDF/A and PDF/E output intents with no claim; CMYK photographs, JPEG 2000 icons with JPEG 2000 soft masks; 1,018 stale hint-table warnings from qpdf |
| W12 | PDF/UA Reference Suite 2-06, the PDF Association's 2012 brochure | Its cover photograph carries a third party's copyright, and the second photograph no credit | InDesign CS6, linearized in 2019 then updated twice, the first update's `/Prev` pointing at the first-page xref stream; alt texts ending in NUL |
| — | pdfLaTeX with hyperref (`py-pdf/sample-files`) | CC BY-SA 4.0 | The TeX family, absent until now: Computer Modern Type 1 subsets, outline, xref stream |
| — | A Google Docs download (`py-pdf/sample-files`) | CC BY-SA 4.0 | Skia's Google Docs renderer, a Type 3 font beside CID subsets |
| — | wkhtmltopdf 0.12.5 on Qt 5 (`py-pdf/sample-files`) | CC BY-SA 4.0 | Another HTML-to-PDF engine: DejaVu CID subsets, one glyph per text operator in a y-down text matrix |
| — | The PDF Association's hand-written PDF 2.0 example of UTF-8 strings (`pdf20examples`) | CC BY-SA 4.0 | UTF-8 text strings, which only PDF 2.0 allows, in `/Info`, outline titles, layer names, a page-label prefix and alternate text |

Six remote entries are recorded as unsupported: the topographic map (T21), the 25-signature sheet and the
2015 BOE law (T23), the signed web capture (T24), the Axapta credit note and the `#00` form (M2). The
laziness test honours that mark, like the other acceptance tests; it skips encrypted documents visibly
until M11 brings decryption, and it no longer applies to a document whose index must be rebuilt, since a
rebuild scans the file by definition.

## Traps met along the way

- **Printers stamp who printed.** Microsoft Print to PDF writes the Windows account's display name into
  `/Author` whatever the document says; PDF24's Ghostscript copies the account name from the PostScript
  `%%For` comment into `/Author` and the XMP; Word's Save as PDF copies the document's Author property,
  which defaults to the Office user. `build/build_word.ps1` handles each, and deletes any output that still
  names the account.
- **Incremental updates remember.** An `/Info` dictionary replaced by a later revision is still in the
  file, and so is the name in it.
- **XFA remembers too**: a LiveCycle form's configuration packet keeps the designer's file paths.
- **Some URLs serve whatever is current**: `irs.gov/pub/irs-pdf/`, the Cerfa `.do` links and opm.gov's
  forms change with each edition, while `irs.gov/pub/irs-prior/` and the EU's Cellar URIs are permanent.
  EUR-Lex itself answers robots with a challenge page; the Cellar serves the same bytes.
- **The Internet Archive's `id_` URLs return the original bytes**, byte-identical to what pdf.js and
  others recorded; without `id_`, the archive rewrites the file.
- **pdfa.org refuses a bare user agent**, and several government sites answer 403 to one.
- **A registered office can be someone's home.** Notices about associations give their seat, and a small
  association's seat is often its founder's house — one gave it as "chez M. et Mme …". An association's
  stated purpose can even describe a named child's illness. Each notice had to be read, not sampled.
- **A copier's name survives a later save.** Two Xerox scans still carry the copier as Producer after an
  unidentified tool re-serialised them; the OCR layer is the copier's (its own font names, no Acrobat
  trace), but the bytes are not the copier's alone. The Canon scan NIST published had been re-saved by
  Acrobat too.
- **Some suites exist only as a zip.** The PDF/UA Reference Suite is published as one 26.8 MB zip, served
  only to a named user agent, and the fetcher takes a URL, not a zip member. Byte-identical copies of its
  members sit at pinned commits of other projects — the veraPDF regression tests and opendataloader-pdf —
  so the manifest pins those, each hash checked against the zip's own member.
- **Well-known samples are signed with an employee's own certificate.** Adobe's widely copied signed sample
  carries the signer's own e-mail in the certificate subject, printed again in the visible signature. Most
  of EU DSS's signed files carry real people's qualified certificates, with identity numbers, personal
  e-mail addresses and phones in the subject, and a public body's DocuSign or Adobe Sign audit report lists
  every signer's e-mail and IP address. A signed file is screened certificate by certificate, not only
  page by page.
- **Personal data hides where extraction does not look.** One pdf.js file keeps two page objects outside
  the page tree, holding employees' names and e-mail addresses in hex glyph codes: invisible to pdftotext
  and to a pattern scan, found only by grafting the pages back into the tree and extracting them. In two
  hybrid invoices, the personal data was in the attached XML.
- **A licence file can be contradicted by the record.** OCRmyPDF's `REUSE.toml` says MIT for its PDFpen
  file; the issue thread in which the contributor granted it says CC BY-SA 4.0. The primary record wins.
- **Truncated files make referees disagree.** On a Word file cut at 1 MiB, qpdf 11.9.1 counts 11 pages,
  PDFium 8, qpdf 12 and PDFBox 4; on a budget book cut at 1 MB, qpdf 11.9.1 finds no `/Root` at all.
  Neither entry asserts a page count.
- **The reader's fixed windows.** Real files found three bounds the synthetic ones had not reached. The
  object parser reads through an 8 KB window: a stream whose `endstream` falls just past it is reported
  truncated (T21), and an indirect object longer than the window is cut at its edge (T23) — a 10,112-byte
  DSS `/VRI` dictionary in a 25-signature file, 36 structure arrays of about 8.7 KB in a 2015 BOE law —
  where qpdf reads both whole. And each cross-reference section is read through a window of up to 64 KB,
  whatever the section's size (T24): opening a 218 KB signed web capture with three sections reads 117 KB,
  bounded, but proportional to the number of sections. T23 is to be fixed before M2 closes, T24 in M13;
  `docs/status.md` tracks all three.

## What the milestones can take from it

- **M1**: none of the 239 real PDFs downloaded in the first pass crashed or hung the reader, nor any of
  the 82 the third pass took in, and all 82 vendored files open as their entries say. Two hand-written
  files show it silent where qpdf reports damage; they are recorded as unsupported until M2. On the empty
  object of the compacted-syntax file, the reader and qpdf agree.
- **M2**: the vendored files carry anomalies every reader tolerates and a validator should name without
  calling them errors — stale linearization hint tables (some twenty files), a `/Size` one too large, an
  xref stream without its own entry, references to objects the xref lacks, font-level XMP that is not
  well-formed XML, an empty `/Lang`, a `/Lang` that contradicts the text, UTF-16LE text strings, two
  `startxref` lines, a `/MarkInfo` pointing at the page tree, `/Info` and XMP that disagree.
  `docs/milestones/M2.md` lists them among its acceptance conditions. The third pass adds T23, an indirect
  object longer than the parser's 8 KB window cut at its edge, to be fixed before M2 closes; two more
  files where the reader is silent while qpdf reports damage — an `/Info` object without `endobj`, names
  containing `#00`; an object stream destroyed by a zeroed block, which the reader finds only when it
  reaches it; and more anomalies to name — `startxref` on the line end before `xref`, an update glued to
  the `%%EOF` marker, `startxref` past the end of the file, a text string whose byte-order mark precedes
  single-byte text, UTF-16BE strings opening with a language escape, a catalog `/Lang` of `(English)`,
  `/DecodeParms [null]`, a `/ToUnicode` that is a name, an inverted MediaBox, an indirect `/Rotate`, an
  outline whose `/Last` is itself.
- **M3, M4**: signatures to keep intact through an incremental update — a GPO certification, DILA's
  Dictao signature, a qualified seal renewed by timestamps over two years, PAdES B-LTA, Acrobat signatures.
  The third pass adds each PAdES baseline level on one page (B-B, B-LT, B-LTA); DocMDP P=1 and P=2 with
  FieldMDP and legacy MD5 object digests; the legacy `adbe.x509.rsa_sha1` and `adbe.pkcs7.sha1`
  subfilters; plugtest seals each followed by a document timestamp; a file re-signed over seven years,
  ending in RSASSA-PSS; 24 signatures in 48 incremental updates; certified and signed files under AES-128;
  and commercial e-signatures — DocuSign (one committed, two remote), Adobe Sign, Yousign's seal written
  by a full rewrite. And files a signer must neither mistake nor break: an unsigned placeholder whose
  `/ByteRange` holds name tokens, a `/Reason` containing the word `trailer`, damage inside a signed byte
  range, a signed update that `startxref` cannot reach.
- **M5**: Acrobat 9's signed portfolio — a `/Collection` with a schema and folders, a Flash navigator,
  five embedded PDFs among them a certified XFA form and 3D models — is the first portfolio a case-file
  assembly will meet; beside it, the Massachusetts dashboard's chain of ten revisions.
- **M10**: extraction will meet text drawn as images, OCR layers — Acrobat's and two copiers' own, with
  horizontal scaling up to 2000 % —, vertical Japanese without ToUnicode, Arabic on which xpdf and poppler
  disagree — xpdf reads "الضرائب", poppler "الرضائب" —, the PDF24 file where ToUnicode says E and no glyph
  is drawn, a compacted-syntax file from which xpdf 4.00 extracts nothing while poppler and PDFium do,
  indirect references inside a content stream, and inline images whose keys contradict each other. The
  third pass adds tagged scans whose OCR text lies under the page image — ABBYY FineReader 8's
  (committed), and an 82-page PDF/UA-1 remediation —, so extraction must prefer the structure over what a
  renderer shows; OCR layers from OmniPage 19 and a Canon scanner; Hebrew in TrueType and CID subsets
  beside the Census guide's Type 1; a ToUnicode that turns the minus into a soft hyphen, and one whose
  values are byte-swapped; EBCDIC codes in bitmap Type 3 fonts, and bitmap Type 3 fonts with no ToUnicode
  at all; word spaces present only as TJ kerning; UTF-8 strings in PDF 2.0; a real seven-column table; and
  a poster whose text is outlines, which must yield no text rather than noise.
- **M11**: XFA forms from Designer 6.4, 6.5 and ES 9, one of them readable today; AcroForms driven by
  JavaScript; a calculation order; widgets without appearance streams. The third pass adds dynamic XFA,
  in two Canadian forms whose only PDF page is a placeholder; an XFA form filled with fictitious values and
  certified, inside the portfolio; OPM's OF-306, with date JavaScript and two unsigned signature fields;
  radio buttons whose export value holds a NUL; read-only fields under a Yousign seal.
- **M12**: veraPDF 1.30.2 upholds thirteen of the fifteen PDF/A claims among the new files, and PDFlib's
  PDF/UA-1 claim. It rejects two: the 2015 consolidated text (rules 6.4-3 and 6.2.3.3-1) and ImageMagick's
  claim (6.7.3-8, 6.1.8-1, 6.7.3-1, 6.2.3.3-1). It passes the 2019 notice despite its malformed font XMP,
  which is worth an opinion of our own. With the third pass, the corpus holds Factur-X in every profile —
  MINIMUM, BASIC WL, BASIC, EN 16931 and EXTENDED, the last three among the committed files — and
  ZUGFeRD 2.2's XRECHNUNG profile, beside ZUGFeRD 1.0 BASIC and EXTENDED with a supplementary attachment,
  an Order-X purchase order, and a `factur-x.xml` holding UBL rather than CII.
  veraPDF rejects the committed factur-x Python file's PDF/A-3b claim on five rules, and the PDF/A-1a
  claims of the Slovak seal and the re-signed BOE decree; it upholds PDF/A-4f in PDF 2.0, Symtrax's
  PDF/A-3a, the two gazettes' PDF/A-1a, Oracle Outside In's PDF/A-1a despite a broken `loca`, and three
  more PDF/UA-1 claims from the reference suite. A textbook chapter carries PDF/A and PDF/E output intents
  with no claim at all: a case for D16.
- **M13**: the W11 references above, all three fetched every night — 9,302 pages, one 63 MB page, 125
  JPEG 2000 pages in 147 MB — and beside them a 35,000-pixel square CCITT image that decodes to 153 MB
  from 10.5 KB, 65,542 objects in one object stream, 48 incremental updates, an 82-page tagged scan of
  10.6 MB; and T24, which ties the bytes read at opening to the number of cross-reference sections.

What the public sources could not provide in a file we may commit — a real invoice or statement from a
supplier or bank (six are now remote), a copier file untouched since the copier wrote it (one, from Konica,
is now remote), Hebrew (two
remote), Korean — is what `docs/corpus-contributions.md` asks contributors for. The commercial
e-signature it used to ask for is now committed: the GSA's contract modification completed in DocuSign.
