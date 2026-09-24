# Test corpus

Real documents from real producers, plus copies damaged on purpose. The policy — where documents come
from, what the manifest promises and what closing a milestone requires — is in `docs/corpus.md`.

```
sources/          the HTML documents the corpus is generated from
build/            the generation scripts and their pinned Python requirements
documents/        the committed PDF files, by use case
vendor/           third-party files, by source, used unmodified under the terms in NOTICE
remote/           documents we may use but not redistribute, put there by build/fetch_remote.py (ADR 32);
                  ignored by git, never committed
manifest.json     the index: one entry per document, with what tests must observe
```

`manifest.json` describes every document once, whoever produced it. `build/build_corpus.py` writes the
entries of what it generates, marked `"builtBy": "build_corpus.py"`, and replaces only those. Every other
entry is written by hand — third-party files, documents from `build/build_word.ps1` or from the field,
remote documents — and the script adds nothing to it but the referee's verdict.

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

It regenerates only what it produces. Every other document is left alone, and so is its entry, but for the
referee's verdict, which the script refreshes.

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

**Refreshing the referee's verdict on committed documents only** — after adding a file under `vendor/` or
`documents/` with its entry — needs nothing but qpdf, and is best run in the container the integration tests use, so the
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
   `documents/<use-case>/` — or, for a third party's file, under `vendor/<source>/`, with its licence added
   to `NOTICE` — and describe it in `manifest.json`, by hand, after the entries marked `builtBy`. Keep it
   small if you can; over 2 MB it is not committed at all, but described as a remote document (below).
2. Write its entry, including what tests must observe: page count, whether it is well formed,
   which diagnostics the reader must report, and whatever later milestones will assert (text, attachments,
   form fields, conformance level).
3. Establish the page count with an **independent tool**, never with our own reader — an expectation
   derived from the code under test proves nothing.
4. Record the origin and the licence. Contributed documents must carry no confidential content.
5. Record the referee's verdict with `build_corpus.py --committed-only`, as above.

`CorpusReadingTests.The_corpus_manifest_describes_every_document_present` fails if a file is added without
a manifest entry: a document nobody asserts anything about is clutter, not coverage.

## Confidential documents

This repository is public, so anything committed here is published. Documents that cannot be published go
in `tests/corpus/private/`, described by `tests/corpus/private.json` in the same format as the manifest —
the one entry list kept apart, because git must ignore it.
Both are ignored by git; the test suite merges them when they are there and runs on the public corpus when
they are not, so a private corpus never breaks anyone else's build.

That is the place for documents from the field — files from Word, Acrobat, a scanner, a supplier's ERP, or
anything that broke somebody's tooling. If a document can be anonymised enough to publish, it is worth far
more in the public corpus: strip it, check what its metadata still says about its origin, and add it under
`documents/` with its provenance.

## Remote documents

Some public files are worth testing against but cannot be committed: attachments to other projects' bug
reports, vendors' samples under "all rights reserved", ShareAlike sets whose licence would reach every
derivative we make, and any document over 2 MB, whatever its licence. ADR 32 keeps them out of git and in the
tests: the manifest describes them with origin `remote` and a mandatory `source.url` and `source.sha256`,
and the test suite leaves out those whose file has not been fetched. The main job still reads their
entries: `CorpusReadingTests.Remote_documents_are_pinned_and_kept_where_git_ignores_them` fails on a remote
entry outside `remote/`, on a file under `remote/` not marked remote, and on a missing URL or SHA-256.

```bash
python3 tests/corpus/build/fetch_remote.py          # fetch what is missing, verify what is present
python3 tests/corpus/build/fetch_remote.py --list   # each document's state, nothing fetched
dotnet test --project tests/AdCodicem.Pdf.Tests/AdCodicem.Pdf.Tests.csproj -c Release
```

A download whose SHA-256 differs from the pinned one is refused: the file changed at its source, so it is a
new document, reviewed as one — never accepted by updating the hash. The `Remote corpus` workflow does the
same every night and by manual dispatch, and reports an unavailable document as such rather than as a
failing test.

To add one, choose an immutable URL — a repository commit, an Internet Archive `id_` copy, a permanent
publisher URI — and write the entry as for any other document, with its file under `remote/<source>/`,
origin `remote`, expectations established with independent tools, and `licence` saying why the file is
remote rather than vendored. Titles and `textContains` strings are
published with the manifest, so they carry no personal data. Then fetch it and record the referee's
verdict, in the container the integration tests use:

```bash
python3 tests/corpus/build/fetch_remote.py
docker run --rm -v "$PWD/tests/corpus:/corpus" alpine:3.21 \
  sh -c "apk add --no-cache qpdf python3 >/dev/null && python3 /corpus/build/build_corpus.py --remote"
```

Nothing under `remote/` is committed, nor anything derived from it — a damaged or repaired variant, a
rendering. Moving the directory aside to test without it? Move it out of the repository: a renamed
directory is no longer ignored.
