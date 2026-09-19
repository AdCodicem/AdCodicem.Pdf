# Project status

A living file, updated **at the end of every session**. It describes the real state, not intentions.
Keep it short: summarise the journal once it passes a dozen entries — the detailed history is in git, not
here.

## At a glance

- **Current milestone**: M2 — Document validation (`docs/milestones/M2.md`), not started
- **Last milestone closed**: **M1 — Object model and tolerant reading**
- **Builds**: yes, with no warnings — **Tests**: 163 unit + 48 integration (skipped without Docker) — **CI**: green
- **Pull requests**: [#11](https://github.com/AdCodicem/AdCodicem.Pdf/pull/11) open — publishing unblocked
  and a manual preview trigger; **merging it publishes the first package**.
  [#1](https://github.com/AdCodicem/AdCodicem.Pdf/pull/1) and Dependabot's six action bumps (#2 to #7) are
  merged; the preview/stable release split (ADR 30) and the baseline fix are on `main`
- **Tagged**: `v0.1.0` on `2808d2f` — the starting point semantic-release continues from. No package exists
  for it, by design.
- **Red on `main`, by design and not by defect**: `Release` stops at its own publishing-identity guard
  (T11), **confirmed by running it on 2026-09-19** — and `Documentation` builds the site but cannot deploy
  it until Pages is enabled (T12). The guard is removed on the branch below; `main` still carries it.
- **No package has been published yet**, preview or stable. The trusted publishing policy now exists on
  nuget.org and the publishing account is named in the workflow, so the next push to `main` is the first
  real attempt — and the first to reach the OIDC exchange.
- **Branch**: `claude/package-preview-deployment-h0lakt` — the publishing account moved out of the secrets, a manual preview trigger, and this journal entry. Not merged, so nothing has run with it yet

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
  it belongs. The three Pages bumps are reasoned rather than observed — see T12.
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
| ~~T03~~ | ~~The real-document corpus is not built yet~~ | Done: `tests/corpus`, 27 documents, four producers plus vendored fixtures |
| T10 | The corpus has no document from Word, Acrobat, InDesign, a real scanner or a Java stack — the producers we cannot run here | Specified as W01 to W12 in `docs/corpus-contributions.md`; waiting on documents from the field |
| T11 | The nuget.org trusted publishing policy is configured; the publishing account is now named in `release.yml` and no secret is involved. **Untested**: no run has yet reached the OIDC exchange, so nothing has confirmed the policy actually matches | Merge the branch — the merge is itself the first preview. If the exchange is refused, the mismatch is the workflow file name, the environment, or the account name; `docs/releasing.md` lists the fields |
| T12 | GitHub Pages is not enabled for the repository, so the documentation site builds but does not publish. `Documentation` stops at `configure-pages`, so nothing downstream of it has ever run — the `configure-pages`, `upload-pages-artifact` and `deploy-pages` bumps of 2026-09-16 included | Settings → Pages → Source: GitHub Actions |
| T13 | The integration suite has one referee (qpdf); veraPDF, pdftotext and a rasteriser join it as their milestones arrive | M10, M12, M14 |
| T14 | Codecov is not linked, so the coverage upload in CI has no token and the badge stays empty | Link the repository on codecov.io, add `CODECOV_TOKEN` |
| T15 | Auto-merge **is** allowed on the repository; what is missing is a ruleset on `main`, so it still accepts direct pushes and the Dependabot auto-merge workflow has no required check to wait for | A branch ruleset on `main` requiring the five pull-request checks, non-strict, **with a bypass for GitHub Actions** — `@semantic-release/git` pushes the `chore(release)` commit straight to `main`, and a ruleset without that bypass fails the stable release in `prepare` |
| T16 | The API baseline is one version for the whole solution, checked against `AdCodicem.Pdf` only. A satellite first shipped in a later release — `AdCodicem.Pdf.Validation` in M2 — has no package at that version, and its pack fails with `NU1101` exactly as `v0.1.0` would have | In M2, before `AdCodicem.Pdf.Validation` is packable: make the baseline per package |
| T04 | An OFL font set must be embedded for default rendering | During M6 |
| T05 | A public API test (a baseline of exported signatures) | Put in place at the start of M7 |
| T06 | `PdfString.ToText` reads Latin-1 rather than full PDFDocEncoding (the 32 positions 0x80-0x9F differ) | Before the first public release |
| T07 | The object cache evicts FIFO rather than LRU; names are interned through an intermediate string | M13, with measurements |
| ~~T08~~ | ~~Fuzzing of the lexer and parser is not set up~~ | Done: in the suite per commit, and a nightly campaign |
| T09 | A memory budget is now enforced in CI; a throughput budget is not | Throughput budget in M13 |
