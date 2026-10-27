# 51. The project keeps a copy of the remote documents a license lets it copy

Date: 2026-10-27

## Status

Accepted on 2026-10-27, on the maintainer's decision of 2026-10-25, settling
[#291](https://github.com/AdCodicem/AdCodicem.Pdf/issues/291) together with
[#204](https://github.com/AdCodicem/AdCodicem.Pdf/issues/204). It amends
[32](0032-documents-that-cannot-be-redistributed-are-fetched-on-demand.md), whose "a mirror hosted by the project is
not an answer" it narrows to the documents no license lets the project copy, and
[33](0033-a-remote-document-may-be-a-member-of-a-pinned-archive.md), whose archive it copies whole. Implemented by the
repository [AdCodicem/AdCodicem.Pdf.Corpus](https://github.com/AdCodicem/AdCodicem.Pdf.Corpus), `source.copy` and
`source.publisher` in `tests/corpus/manifest.json` and its schema, `download_with_fallback` in
`tests/corpus/build/fetch_remote.py`, and the tests beside it.

## Context

ADR 32 fetches every remote document from the host that published it. Some of those hosts fail, and a failure loses
everything they serve:

- **The iPRES set depends on one host.** Its 88 documents, more than a third of the remote corpus, are members of a
  single 613,888-byte archive on www.radar-service.eu (ADR 33). Three `Remote corpus` runs lost all 88 at once, the
  archive timing out on each of three attempts: run 42 (2026-10-13), and runs 78 and 79 (2026-10-25). Run 42 lost 805
  acceptance tests and 759 referee checks with them. Between losses the host answers.
- **No other copy of that archive is usable.** Its DOI resolves to RADAR; the OPF format-corpus's copies lost their
  carriage returns to git and lack `T04_019` (ADR 33).
- **Many remote documents are remote for a reason that does not forbid copying them.** ShareAlike would reach the
  derivatives the corpus pipeline commits, so a CC BY-SA document is not vendored; a public-domain or CC0 document over
  2 MB is not committed for its size. Neither reason forbids hosting the file itself, unmodified, apart from the
  derivatives.

ADR 32 rejected a mirror because hosting a copy is redistributing it. That holds for a document whose license does not
allow redistribution. It does not hold for one whose license does, provided the copy keeps that license and its
attribution, and nothing derived from it lands beside it.

## Decision

We will host, in a repository of its own, a byte-identical copy of each remote document whose license allows verbatim
redistribution, and fetch it from there, its publisher kept as the fallback.

- **The repository is [AdCodicem/AdCodicem.Pdf.Corpus](https://github.com/AdCodicem/AdCodicem.Pdf.Corpus)**, public.
  It holds the documents unmodified, under their own licenses: a `REUSE.toml` names each file's license and holders,
  `LICENSES/` holds their texts, and its README names each document's source, license and attribution. It has no
  license of its own, and the main repository, MIT, holds none of its documents.
- **Nothing enters it without an entry in the manifest**, and nothing derived from a document is ever committed there
  or here: the damaged and repaired variants stay in the job that makes them (ADR 32).
- **Each document's license is read again before it is copied, for that very file**, and the review is recorded in its
  entry, `source.copy`: the date and the terms. A license announced for a set does not always cover each member — the
  PDF/UA Reference Suite's members print copyrights the suite's license cannot be shown to cover — so a doubt leaves
  the document at its publisher.
- **The copy is pinned like any remote document**: the same SHA-256 and size, `source.url` naming a commit of the
  repository or an asset of one of its immutable releases. The publisher's URL is kept in `source.publisher`.
- **A file over 100 MB**, GitHub's limit for a file in git, is an asset of an immutable release of that repository.
- **The archive of ADR 33 is copied whole**, so its pins and its members' stay as they are.
- **The fetcher tries the copy, then the publisher.** Only a copy that cannot be had falls back. A copy that no longer
  matches its pins is refused and reported, not hidden behind the publisher's file: it is the project's own mistake.
- **ADR 32's "a mirror hosted by the project is not an answer" still applies to every other document.** Those stay at
  their publisher, and the Actions cache (#204) is their remedy.

## Consequences

- **102 of the 242 remote documents moved to the copy, in 15 files**, about 257 MB: the iPRES archive (88 documents),
  ten ShareAlike or CC0 documents and four US federal works, Professional Paper 1 as a release asset. Of the 109 a
  license was announced to cover, seven stayed at their publisher after the review: a page reproducing an
  advertisement's text, Adobe's portfolio under a curator's waiver, and five federal captures carrying third-party
  photographs, drawings, letters or text (`docs/corpus-sources.md`).
- **A host's outage no longer loses what the project may copy.** The iPRES archive leaves RADAR; the copied documents
  come from GitHub, which already serves most of the remote corpus.
- **The copies are a redistribution, with its duties.** Each attribution must stay accurate, and a document whose
  license turns out not to cover it is removed from the repository — its git history included — and returns to its
  publisher.
- **The copy can fail too.** The publisher fallback keeps the outage of one host from losing a document, and the cache
  keeps most runs off both.
- **Two repositories move together.** Adding a copied document means a commit there, then the manifest's entry here
  naming that commit: the manifest pins the copy, never the other way round.
- **What would reopen this**: a rights holder objecting to the copy, a license found not to cover a copied file, or
  GitHub no longer serving the repository's files at a commit.
