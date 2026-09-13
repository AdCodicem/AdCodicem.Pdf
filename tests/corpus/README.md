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
