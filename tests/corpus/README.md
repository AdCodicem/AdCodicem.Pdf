# Test corpus

Real documents from real producers, plus copies damaged on purpose. The policy — where documents come
from, what the manifest promises and what closing a milestone requires — is in `docs/corpus.md`.

```
sources/          the HTML documents the corpus is generated from
build/            the generation scripts and their pinned Python requirements
documents/        the committed PDF files, by use case
vendor/           third-party files, by source, used unmodified under the terms in NOTICE
manifest.json     the index: one entry per document, with what tests must observe
vendor.json       the entries of the files under vendor/
contributed.json  the entries of the files under documents/ that build_corpus.py cannot produce
```

`manifest.json` is written by `build/build_corpus.py`, never by hand: it is the generated entries plus
those of `vendor.json` and `contributed.json`, each with the referee's verdict added.

## Regenerating

Documents are **committed**, not generated at test time: producer output changes with producer version,
and a suite that shifts underneath you is worse than no suite. Regenerate deliberately, review the diff,
and say in the commit message why the corpus changed.

```bash
pip install -r tests/corpus/build/requirements.txt
apt-get install -y --no-install-recommends qpdf   # the referee that authors part of the manifest
python3 tests/corpus/build/build_corpus.py
```

The script needs Chromium and LibreOffice Writer on the machine. In a Claude Code web session:

```bash
apt-get install -y --no-install-recommends libreoffice-writer   # Chromium is already at /opt/pw-browsers
```

It regenerates only what it produces. Files described by `vendor.json` and `contributed.json` are left
alone, and their entries merged into the manifest.

**Office desktop writers** — Word's Save as PDF, the Microsoft Print to PDF driver, and the PDF24 printer
(PScript5 PostScript converted by Ghostscript) — run only on Windows, so their documents come from
`build/build_word.ps1` rather than from the script above:

```powershell
./tests/corpus/build/build_word.ps1   # Windows with Word and PDF24 Creator; prints the versions to record
```

Both printers stamp the Windows account that printed into `/Author`, whatever the document says. For PDF24
the account arrives as the PostScript's `%%For` comment, which the script sets to `AdCodicem` before PDF24
converts the job, so the PDF is untouched. The Microsoft driver offers no such intermediate: the script
overwrites its `/Author` token in place with a neutral value of the same length, so no offset moves, and
records the file as derived. Any output that still names the account is deleted.

**Refreshing the entries of committed documents only** — after adding a file under `vendor/` or
`documents/` — needs nothing but qpdf, and is best run in the container the integration tests use, so the
recorded verdict is that exact qpdf's:

```bash
docker run --rm -v "$PWD/tests/corpus:/corpus" alpine:3.21 \
  sh -c "apk add --no-cache qpdf python3 >/dev/null && python3 /corpus/build/build_corpus.py --committed-only"
```

## Adding a document

Contributing a file from the field — Word, Acrobat, a scanner, a supplier's ERP, or something that broke
your tooling? `docs/corpus-contributions.md` is the specification: what is wanted, what to check before
handing it over, and which of the public and private corpora it belongs in.


1. Add a generator to `build_corpus.py` if it can be produced. Otherwise, drop it under
   `documents/<use-case>/` and describe it in `contributed.json` — or, for a third party's file, under
   `vendor/<source>/`, described in `vendor.json`, with its licence added to `NOTICE`.
2. Write its entry, including what tests must observe: page count, whether it is well formed,
   which diagnostics the reader must report, and whatever later milestones will assert (text, attachments,
   form fields, conformance level).
3. Establish the page count with an **independent tool**, never with our own reader — an expectation
   derived from the code under test proves nothing.
4. Record the origin and the licence. Contributed documents must carry no confidential content.
5. Merge the entry into `manifest.json` with `build_corpus.py --committed-only`, as above.

`CorpusReadingTests.The_corpus_manifest_describes_every_document_present` fails if a file is added without
a manifest entry: a document nobody asserts anything about is clutter, not coverage.

## Confidential documents

This repository is public, so anything committed here is published. Documents that cannot be published go
in `tests/corpus/private/`, described by `tests/corpus/private.json` in the same format as the manifest.
Both are ignored by git; the test suite merges them when they are there and runs on the public corpus when
they are not, so a private corpus never breaks anyone else's build.

That is the place for documents from the field — files from Word, Acrobat, a scanner, a supplier's ERP, or
anything that broke somebody's tooling. If a document can be anonymised enough to publish, it is worth far
more in the public corpus: strip it, check what its metadata still says about its origin, and add it under
`documents/` with its provenance.
