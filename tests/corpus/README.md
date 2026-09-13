# Test corpus

Real documents from real producers, plus copies damaged on purpose. The policy — where documents come
from, what the manifest promises and what closing a milestone requires — is in `docs/corpus.md`.

```
sources/     the HTML documents the corpus is generated from
build/       the generation script and its pinned Python requirements
documents/   the committed PDF files, by use case
manifest.json  the index: one entry per document, with what tests must observe
```

## Regenerating

Documents are **committed**, not generated at test time: producer output changes with producer version,
and a suite that shifts underneath you is worse than no suite. Regenerate deliberately, review the diff,
and say in the commit message why the corpus changed.

```bash
pip install -r tests/corpus/build/requirements.txt
python3 tests/corpus/build/build_corpus.py
```

The script needs Chromium and LibreOffice Writer on the machine. In a Claude Code web session:

```bash
apt-get install -y --no-install-recommends libreoffice-writer   # Chromium is already at /opt/pw-browsers
```

## Adding a document

1. Drop it under `documents/<use-case>/`, or add a generator to `build_corpus.py` if it can be produced.
2. Add its manifest entry, including what tests must observe: page count, whether it is well formed,
   which diagnostics the reader must report, and whatever later milestones will assert (text, attachments,
   form fields, conformance level).
3. Establish the page count with an **independent tool**, never with our own reader — an expectation
   derived from the code under test proves nothing.
4. Record the origin and the licence. Contributed documents must carry no confidential content.

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
