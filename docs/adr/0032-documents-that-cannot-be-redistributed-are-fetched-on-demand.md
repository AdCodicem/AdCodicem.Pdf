# 32. Documents that cannot be redistributed are fetched on demand, never committed

Date: 2026-09-24

## Status

Accepted on 2026-09-24. Complements [23](0023-third-party-corpus-documents-are-vendored-only-under-attribu.md),
which it leaves unchanged: what is committed still needs an attribution-only licence.

Implemented by the entries of origin `remote` in `tests/corpus/manifest.json`,
`tests/corpus/build/fetch_remote.py`, `build_corpus.py --remote`, the filter in
`tests/AdCodicem.Pdf.TestSupport/Corpus.cs` and the `Remote corpus` workflow
(`.github/workflows/remote-corpus.yml`).

Amended on 2026-09-24, the day it was accepted: the remote entries moved from `remote.json` into the
single corpus manifest, marked by `"origin": "remote"`, when `vendor.json` and `contributed.json` were
folded into it too. The decision below is unchanged; only the file that holds the entries is. Where it
says `remote.json`, read the manifest's remote entries.

## Context

ADR 23 commits third-party documents only under attribution-only licences, and says in one sentence that
large corpora are "fetched on demand for local investigation rather than committed". Nothing implements
that sentence.

The search of 2026-09-24 (`docs/corpus-sources.md`) found that what the corpus still lacks sits mostly in
files it cannot redistribute:

- **Attachments to public bug reports** — an SAP NetWeaver form, a Ricoh copier scan with its OCR layer,
  hundreds of files that broke pdf.js, PDFium or PDFBox. Nobody licensed them; they are simply published.
- **Vendors' samples** — Factur-X and ZUGFeRD examples from the FNFE-MPE, FeRD, intarsys or Symtrax, under
  "All Rights Reserved" or behind a registration form.
- **ShareAlike sets** — `py-pdf/sample-files`, much of OCRmyPDF's and pikepdf's test data, the PDF
  Association's PDF 2.0 examples. They could be committed, but ShareAlike then reaches every derivative
  we commit — a damaged copy made by `build_corpus.py`, a repaired or merged golden file from M3 to M5, an
  image on the site — and nothing in the corpus pipeline keeps a derivative apart from its source.
- **Documents too large to commit** — the W11 references for M13 (T22), 37 to 147 MB each.

Other projects faced the same problem and did not commit: pdf.js keeps a `.link` file per document and
checks each download against an MD5 in its manifest; PDFBox downloads JIRA attachments at build time,
pinned by SHA-512. A mirror hosted by the project (hayro's) is not an answer: hosting a copy is
redistributing it.

The corpus also promises that tests never depend on the network (`docs/corpus.md`), and a suite that
fails when a third-party server does is worse than no suite.

## Decision

We will describe documents we may use but not redistribute in a second manifest, fetch them on demand,
and test them in a separate job — and we will commit none of them, nor anything derived from them.

- **`tests/corpus/remote.json`** has the corpus manifest's entry format, with `source.url` and
  `source.sha256` mandatory and `licence` saying why the file is remote rather than vendored (ShareAlike,
  no licence, all rights reserved, size). URLs point at immutable locations — a repository commit, an
  Internet Archive `id_` copy, a permanent publisher URI.
- **`build/fetch_remote.py`** downloads the files into `tests/corpus/remote/`, ignored by git, and refuses
  any file whose SHA-256 differs from the manifest's. A changed file is never accepted by updating the
  hash: it is a new document, reviewed as one.
- **`Corpus` merges the remote entries whose file is present**, as it already does for `private.json`. The
  main suite fetches nothing, finds nothing, and runs as today.
- **A separate workflow** — nightly, and by manual dispatch — fetches the remote corpus and runs the
  acceptance tests over it. A download failure is reported as such, not as a test failure.
- **Expectations are established as for any other document**, with independent tools, and never weakened.
- **Nothing fetched is committed**: not the file, not a damaged or repaired variant, not a rendering.
  Derived variants are made in the job and discarded with it.
- **The manifest itself is public**, so its titles and `textContains` strings carry no personal data, and
  it lists only publicly accessible files: nothing behind a login, nothing whose terms forbid automated
  download.
- **A milestone may name remote documents in its acceptance conditions.** It is then closed only on a green
  run of the remote job, recorded in `docs/status.md`.

## Consequences

- **The gaps a contribution was the only way to fill become testable**: the SAP output, the Ricoh scan,
  vendors' Factur-X files, ShareAlike sets, and W11's heavy documents for M13.
- **No licence reaches the repository.** Using a file for tests means copying it onto a test machine,
  which is lower-risk than redistributing it but not nothing; publicly posted files used for testing, with
  their source cited, are the scope.
- **One job depends on the network**, and third-party URLs rot. Immutable URLs and the Internet Archive
  limit that; a document that disappears is marked unavailable, never replaced by a copy of ours.
- **Contributors without network access see less**, and a remote failure surfaces a night later rather
  than on the pull request that caused it — which is why a milestone relying on remote documents must
  record a green remote run before it closes.
- **What would reopen this**: a rights holder objecting to use in tests, a remote job too flaky to be read,
  or openly licensed replacements for the documents it exists to reach.
