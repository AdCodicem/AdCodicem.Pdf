# Project status

A living file, updated **at the end of every session**. It describes the real state, not intentions.
Keep it short: summarise the journal once it passes a dozen entries — the detailed history is in git, not
here.

## At a glance

- **Current milestone**: M2 — Document validation (`docs/milestones/M2.md`), not started
- **Last milestone closed**: **M1 — Object model and tolerant reading**
- **Builds**: yes, with no warnings — **Tests**: 251 unit (4 skipped by design: two corpus documents
  recorded as unsupported until M2) + 123 integration (skipped without Docker) — **CI**:
  green, `OpenSSF Scorecard` included: it started for the first time on 2026-09-19 and published a report
- **Corpus**: 68 documents, 9.7 MB — 19 generated here, 3 from Word and PDF24 on Windows, 46 third-party
  files under attribution-only licences (38 added on 2026-09-24, see `docs/corpus-sources.md`). On the
  branch `claude/corpus-third-party-documents`, in a draft pull request, not yet on `main`
- **Supply-chain score**: **6.6/10** as published on `6bacbb2`, up from 5.5. The branch below is measured
  to take it to **7.1**; everything above that needs repository settings or people, not code — see T15,
  T18 and T19
- **Pull requests**: versioned documentation (ADR 31) is [#23](https://github.com/AdCodicem/AdCodicem.Pdf/pull/23),
  merged once green: after it, every merge redeploys the site.
  [#22](https://github.com/AdCodicem/AdCodicem.Pdf/pull/22), the site repair, is merged and deployed.
  [#11](https://github.com/AdCodicem/AdCodicem.Pdf/pull/11), [#1](https://github.com/AdCodicem/AdCodicem.Pdf/pull/1)
  and Dependabot's action bumps are merged too; the preview/stable release split (ADR 30), the baseline fix
  and the Scorecard action ref are all on `main`
- **`main` has a ruleset since 2026-09-22** ("Default": pull request with one code-owner approval, linear
  history, CodeQL, coverage). Only repository admins bypass it — and the stable release pushes its
  `chore(release)` commit to `main` as GitHub Actions. **The first stable release will fail at that push**
  until the ruleset grants GitHub Actions a bypass, as T15 said it would have to. Nothing is published
  when it fails: the push comes before the packages do
- **Tagged**: `v0.1.0` on `2808d2f` — the starting point semantic-release continues from. No package exists
  for it, by design.
- **Nothing on `main` is red any more.** The publishing-identity guard that used to stop `Release` (T11)
  went with the merge of #11, and `Documentation` deployed successfully on 2026-09-19 (run 2).
- **The site at <https://adcodicem.github.io/AdCodicem.Pdf/> works**, for the first time: until 2026-09-22
  every user-facing page showed its own compiled JavaScript as text. Repaired by #22 and redeployed by
  hand the same evening; the site check passes on all 74 published pages. **In flight**: versioned
  documentation — stable lines behind a selector, the latest preview behind a button, redeployed with
  every preview (ADR 31).
- **`OpenSSF Scorecard` is fixed and proven**: the `v2.4.4` pin merged, run 13 went green, and
  `api.scorecard.dev` now serves a report — so the README badge finally has a number behind it.
- **Published**: [`AdCodicem.Pdf`](https://www.nuget.org/packages/AdCodicem.Pdf) is on nuget.org —
  `0.1.1-preview.10` through `0.1.1-preview.20` at least (2026-09-22), previews from `main`, the first packages this project has
  ever shipped. Trusted publishing works end to end; nothing long-lived is stored anywhere.
  `dotnet add package AdCodicem.Pdf --prerelease`
- **No stable release yet**: `v0.1.0` is a tag with nothing behind it, by design, and the stable path has
  still never run. Every merge into `main` now publishes a preview on its own.
- **Coverage**: 82.61% on Codecov, linked and uploading without a token. Its GitHub App is not installed,
  which Codecov warns about on every pull request (T14).
- **Branches**: five merged pull requests left their head branches on the remote.
  `claude/dependabot-prs-review-p3mx79`, `claude/scorecard-pipeline-47mgrj` and
  `claude/scorecard-improvement-4ckfxd` hold nothing `main` does not, and are deletable as they stand.
  `claude/nuget-pdf-html-dotnet-msyz8z` and `claude/package-preview-deployment-h0lakt` each kept commits
  pushed after their merge; what was still true on them is in this change, so they are deletable too once
  it lands

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

### 2026-09-24 — The wanted documents, searched for on the internet
- **The request**: find in public sources the documents `corpus-contributions.md` asks for (W01–W12),
  free of rights and free of real people's data, and see what other PDF libraries keep. The rules were
  settled with the maintainer before anything was downloaded, then twice more along the way: ADR 23
  licences only; no real person's name anywhere, superseded revisions included, with typeface-designer
  credits, public figures and historical figures tolerated; publisher's bytes only; specimens allowed.
- **38 third-party documents entered `tests/corpus/vendor`**, 6.6 MB, from 69 downloaded candidates and
  47 adversarially verified ones. Every line of the wanted list now has something; what public sources
  cannot give — a real Java-stack invoice, a commercial e-signature, a Factur-X from an ERP, a copier's
  own OCR — stays wanted, and says so in `corpus-contributions.md`. Provenance (URL, date, SHA-256) is in
  `vendor.json`, attribution in `NOTICE`, the whole story in `docs/corpus-sources.md`.
- **W01 was also produced here**: `build/build_word.ps1` writes the corpus invoice through Word's Save as
  PDF, the Microsoft Print to PDF driver and the PDF24 printer. Both printers stamp the Windows account
  that printed: the Microsoft driver's display name in `/Author` (overwritten in place, same length, file
  recorded as derived), Ghostscript's `%%For` in `/Author` and the XMP (set in the PostScript before PDF24
  converts it). **The PDF24 file draws a box for every capital E** of the regular face: the driver's
  PostScript lacks the glyph while ToUnicode still says E — a genuine W06 file from an office desktop.
- **Most other libraries' corpora cannot be reused**: bug-report attachments under a code licence
  (pdf.js, PDFium, PDFBox, PdfPig, pypdf…), copyleft (Poppler, MuPDF, iText, and `py-pdf/sample-files` under
  CC BY-SA). pikepdf's per-file `REUSE.toml` is the one complete provenance record found.
- **The corpus found three things of ours.** `build_corpus.py` deleted the whole of `documents/` before
  regenerating, so a contributed document would have vanished with its entry: `contributed.json` now lists
  what the script must keep, and `--committed-only` refreshes the manifest with nothing but qpdf. The
  integration test read qpdf's page count from standard output and standard error together, and failed on
  any file qpdf warns about. And the reader falsely reports a truncated stream at a window boundary (T21).
- **Recorded, not fixed**: two hand-written files on which the reader is silent while qpdf reports damage
  (a trailer without `/Size`, a page given a stream body) are `unsupported` until M2 — a manifest field
  that did not exist, and now skips the test with the reason. `M2.md` gains the corresponding acceptance
  conditions and the anomalies of the vendored files (stale hint tables, a `/Size` off by one, an xref
  stream without its own entry, undefined references, malformed font XMP).
- **Referees**: pikepdf for pages, qpdf 11.9.1 in the integration container for the verdict, poppler's
  `pdftotext` 24.02 for text — it disagrees with xpdf on one Arabic ligature —, veraPDF 1.30.2 for PDF/A:
  seven claims upheld, two rejected.

### 2026-09-22 — Documentation for every release, and for the preview
- **The request**: a preview package should come with its documentation; the site should open on the
  stable version, with a button to the latest preview; every stable release should keep its documentation,
  behind a selector. Settled in ADR 31, with the choices it made along the way.
- **Docusaurus's own versioning**, not a site per release: one build serves everything, and a frozen
  version stays correctable. The stable release freezes the user documentation and the generated API
  reference in its release commit (`scripts/version-docs.mjs`, run from `.releaserc.json`'s prepare step).
- **One entry per line**: per minor below 1.0, per major from 1.0 — 1.0 opening the `1` line. A later
  release on a line replaces its copy, deletions included (the script unstages the old copy from git, since
  the release commit only adds what exists), and the selector shows the line's latest release. All lines
  are kept.
- **The preview has a button, not a selector entry**, and only while a preview is newer than the latest
  stable release. Right after a release, the newest preview on nuget.org is *older* than it; a button to it
  would lead backwards, so it disappears until the next merge. Before the first release the preview is the
  whole site. The rules are in `scripts/versions.mjs` and pinned by `npm test` (12 cases, run in CI).
- **Deployment**: `release.yml` calls `docs.yml` after every preview from `main`, with the preview's
  version — nuget.org can take minutes to list a new package, so it is not asked — and after every stable
  release, from the release commit, which the run did not start from. A manual run asks nuget.org.
- **Verified by simulation**, since no stable release exists: freezing `0.1.1`, then `0.2.0`, then `0.2.1`
  over it (a stale page and a local edit both gone, as they should be), then refusing `0.2.1` again and a
  preview. Built each way — two lines and a preview, no preview after a release, no stable at all — with
  the site check passing on every page, and the selector, the button in both directions, the banners and
  the namespace sidebar of a frozen API reference looked at in a browser.
- **Caught by looking, then made checkable**: the preview's banner appeared on every project document too —
  their unversioned plugin's only version is also named `current`. Fixed, and the site check now fails on
  a version banner under `/project/` (it reported 41 pages before the fix, none after).
- **Found on the way**: `main` gained a ruleset today, and GitHub Actions is not among its bypass actors —
  the first stable release will fail when it pushes its commit (at a glance, above).

### 2026-09-22 — The site had never shown a single user-facing page
- **What a visitor saw**: the introduction, both concept pages and all thirty API pages printed as
  `export const frontMatter = …` followed by a screen of `_jsx(…)` calls. Only the project documents
  rendered. It had been that way since the first deployment on 2026-09-19.
- **Why**: moving the site into `docs/website` left the project documents' plugin pointed at `docs/` —
  which now contained the site. Docusaurus scopes each docs plugin's MDX loader to its whole directory;
  `include` and `exclude` choose pages, not what the loader compiles. So every user page went through two
  loaders, and the second rendered the first one's output as Markdown. The page itself said so: it imported
  metadata from `…/docusaurus-plugin-content-docs/project/`, the wrong plugin.
- **Fix**: the project documents are copied into `docs/website/project` (ignored by git) before every build
  and published from there; edit links still point at `docs/`. ADR 28 carries the amendment.
- **Why nobody noticed**: the build passed, every link resolved, every URL returned 200 — and T12 below was
  closed on "the site serves 44 pages", which counted responses and never looked at one. Every build now
  ends with `check-site.mjs`, which reads the built pages for compiled MDX, HTML printed as text and
  unresolved DocFX references, and fails the build on any. Run against a saved copy of the published site,
  it reports 64 problems in 74 pages; against the repaired build, none.
- **The API reference, tidied while there**: DocFX's inline `<a id>` anchors had been the title, sidebar
  label and table-of-contents text of every page — they are explicit heading ids now; six `<see cref>`
  DocFX could not resolve had left empty elements, and now link to what they name; cross-links carrying an
  anchor were not being rewritten; the sidebar groups types under their namespace; generated pages no
  longer offer an edit link to a file that exists only during the build; and analyser suppressions are
  filtered out of declarations. Printing them exposed two `Justification` strings in `PdfDictionary` and
  `PdfStream` mangled by an old search-and-replace ("calls this a public sealed class pdfdictionary :
  pdfobject"), now rewritten.
- **Stale content**: the introduction still said nothing was on nuget.org; it now gives the `--prerelease`
  command and says which package exists and which milestone brings each of the others. The diagnostics
  page claimed seventy-five thousand mutations on every release; a normal test run does about three
  thousand, and the page now says so.

### 2026-09-19 — What two abandoned branches still knew

- Five merged pull requests left their branches behind. Three carried nothing `main` does not have and
  were identified for deletion; two did not, and this is what they held.
- **`main` was wrong about its own release.** It said no package had ever been published. nuget.org's flat
  container says otherwise: `0.1.1-preview.10` through `.13`. T11 called trusted publishing *untested*
  when four runs had already been through the OIDC exchange. The status file is the one document that
  must never be optimistic *or* pessimistic, and it had drifted the second way.
- **The coverage upload has never found the file it was given.** Run 84's log: `Some files were not found`
  for the `bin/Release/net10.0/TestResults/` path, then `Found 1 coverage files to report` pointing at
  `TestResults/` in the working directory. Codecov's CLI searches when the file it was handed is missing,
  and `fail_ci_if_error: false` means the day it stops searching, nothing turns red — the badge just
  freezes. Fixed by naming `--results-directory` and pointing the upload there.
- **T14 was stale in the other direction**, but only partly, and the branch overstated it. Codecov is
  linked, the upload runs tokenless (`Token length: 0`) and the badge reads 82.61% — so "not linked, badge
  empty" is done. The branch went further and called the Codecov **GitHub App** installed; it is not. The
  bot comments as `codecov-commenter` rather than `codecov[bot]`, and says so itself on every pull
  request. T14 is narrowed to that, not struck out — a distinction worth the correction, since it is the
  difference between "coverage is handled" and "coverage happens to work".
- **One claim on those branches was checked and rejected**: the `NUGET_USER` secret still had to be
  added. `main` had already replaced that whole design with `NUGET_ACCOUNT` and no secret at all, so it
  was not carried over.
- **A second was rejected, then turned out to be right for the wrong reason.** The branch said Pages was
  enabled; the site returned 404, so I kept T12 as written. T12 blamed the settings — "GitHub Pages is
  not enabled" — and that was the stale part. Pages *was* enabled. What had never happened was a
  deployment: `docs.yml` runs only with a stable release or its own manual trigger, run 1 had failed
  back when the settings really were missing, and nothing re-attempted it since. Dispatching it
  succeeded first time and the site is live. The lesson is the ordinary one: a 404 confirms the symptom,
  not the diagnosis.
- The prefix reservation is now real debt rather than a future chore: packages exist under `AdCodicem.`
  and nothing stops someone publishing beside them. T20, and the procedure is an email, not a button.

### 2026-09-19 — Fuzzing read 0 because of what it looks for, and I had read the check wrong
- **The correction first.** The entry below states that Scorecard detects no .NET fuzzer and that the 0
  is simply wrong. That is false, and reading `checks/raw/fuzzing.go` rather than recalling it says so:
  there is a `clients.CSharp` entry, and it greps `*.cs` for `using FsCheck;`, `using FsCheck.Xunit;`,
  `using FsCheck.NUnit;` or `using Expecto.ExpectoFsCheck;`. The check is narrower than "is this project
  fuzzed" — it asks whether the project does **property-based testing with FsCheck** — but it is not
  blind to this stack, and the 0 was earned.
- **So the fix is a test technique this codebase was missing, not a token.** `PropertyTests` holds six
  properties over generated data: the lexer terminates on any bytes and keeps every token offset inside
  the buffer; the parser answers for any bytes without letting its position escape the input; a text
  string survives `FromText` and `ToText`; the hand-written integer and real parsers agree with the
  framework everywhere both will answer; and the seeding itself replays.
- **They complement the mutation campaign rather than repeat it.** Mutation fuzzing starts from real
  documents and damages them, so it explores the neighbourhood of files that exist. A generator starts
  from nothing and reaches an empty buffer, a file of nothing but delimiters, a number carrying forty
  signs. Different instruments, different defects.
- **Checked that they have teeth before trusting them.** Three were deliberately falsified — the integer
  parser compared against `value + 1`, the round-trip against `value + "x"`, the lexer bound inverted —
  and all three failed, shrank to a minimal counter-example and printed a replay pair. A property that
  cannot fail is decoration.
- **Deterministic in the suite, exploratory at night.** A fixed seed means a commit can never be failed
  by luck; `ADCODICEM_PROPERTY_SEED` and `ADCODICEM_PROPERTY_TESTS` let the nightly campaign run 50 000
  cases per property from a different seed each time, which `fuzz.yml` now does alongside the mutations.
  FsCheck rejects an even gamma outright, so that half of the random state stays a constant.
- **FsCheck without `FsCheck.Xunit`**: the runner integration still pins `xunit.extensibility.execution`
  below 3.0.0 and would drag xUnit v2 into a v3 suite. The library itself is runner-agnostic, so a
  property runs inside an ordinary fact. No advisory against `FsCheck` 3.4.0 or `FSharp.Core` 5.0.2, so
  Vulnerabilities stays at 10.
- **Measured effect: 6.6 → 7.1**, Fuzzing 0 → 10 at weight 5. Confirmed by the detection rules rather
  than assumed: C# is 84% of what GitHub reports for this repository, far above the check's
  `average / 4` prominence threshold; `*.cs` matches a nested path because the matcher falls back to the
  file name; and `tests/` is not one of the `testdata/` or `src/test/` prefixes the walker skips.

### 2026-09-19 — The badge had a number at last, and it read 5.5
- The first Scorecard run that ever started (run 13, on `1bfde6d`) published a report: **5.5 out of 10**.
  Before anything was touched, its own arithmetic was reproduced from the published per-check scores and
  the documented risk weights — Critical 10, High 7.5, Medium 5, Low 2.5 — and it lands on 5.46, which is
  what `api.scorecard.dev` rounds to 5.5. That is what makes the rest of this entry a measurement rather
  than a hope: every fix below is worth a known number of points.
- **Pinned-Dependencies, 1/10 — the whole of it.** Not one of the 39 action references in this repository
  was pinned: 33 GitHub-owned and 6 third-party, every one on a floating tag. A tag is a name its owner
  can repoint at other code, and every run after that picks the new code up in silence. All 39 are now
  commit hashes, each carrying the version as a trailing comment — which is what Dependabot reads to know
  what the hash stands for, and what it rewrites alongside the hash when it bumps one. Each hash was
  resolved from `git ls-remote` and **checked against the annotated tag's dereferenced commit**, since a
  tag object's own SHA is not the commit and pinning it would not resolve.
- **Vulnerabilities, 5/10 — all five in the documentation site.** Two in `qs`, two in `serialize-javascript`
  and one in `uuid`, every one a transitive dependency of Docusaurus that nothing here asks for by name.
  npm's own advice was to take `@docusaurus/core` *down* to 3.5.2; three `overrides` clear all five without
  moving Docusaurus at all. `npm audit` now reports nothing, and the site still builds.
- **Security-Policy, 4/10 — and the missing 6 points were one link.** Scorecard gives 1 point for text,
  3 for saying something about disclosure and timelines, and 6 for *linked* content. `SECURITY.md` was a
  page of careful prose containing no URL whatsoever: it told the reader to find the Security tab. It now
  links the advisory form itself, which is better writing before it is a better score.
- **Measured effect: 5.5 → 6.6.** Pinned-Dependencies 1 → 9, Vulnerabilities 5 → 10, Security-Policy
  4 → 10. Unproven until the next run on `main` publishes, for the same reason as last time.
- **The last point on Pinned-Dependencies was refused, with a reason.** It is `dotnet restore` without
  `--locked-mode`, which means committing `packages.lock.json`. Worth exactly 10 of the check's 144
  weighted units — 0.05 of the displayed score, which rounds away entirely — and
  `RestorePackagesWithLockFile` set in `Directory.Build.props` breaks restore outright (`NETSDK1013`, an
  empty `TargetFramework`), though it works set per project. Central package management with transitive
  pinning already gives most of what a lock file is for. Recorded as T17 rather than forced through.
- **What is left is not code.** Branch-Protection and Code-Review are 0 and weigh 7.5 each: together
  they are worth about 1.5 points, and both need the `main` ruleset of T15 plus pull requests someone
  approves. Maintained is 0 because the repository is younger than 90 days, which only time fixes.
  Contributors is 0 because there is one of us. CII-Best-Practices needs a registration (T18) and
  Signed-Releases is unscored only because no release exists yet (T19). ~~Fuzzing reads 0 and will stay
  there: Scorecard detects no .NET fuzzer, so the number stays wrong.~~ **Wrong, and corrected the same
  day** — the check does cover C#, and the entry below says how.

### 2026-09-19 — The supply-chain badge had never been earned
- `OpenSSF Scorecard` was **red on every run it has ever had** — twelve of them, back to the day the
  workflow was added — and nothing said so, because the failure is not a check that fails but a workflow
  that never starts: `ossf/scorecard-action@v2` does not resolve. The action tags releases (`v2.4.4` is the
  current one) and publishes no floating major, so GitHub stops at *Prepare all required actions*. Six
  seconds, no step run, no SARIF, nothing uploaded to code scanning and nothing published to
  `api.scorecard.dev` — which is why the README badge has been empty since it was added.
- **Dependabot could not have caught it either**: the `github-actions` ecosystem bumped six other actions
  in this repository while leaving this one alone, because an unresolvable ref gives it no version to
  compare. An exact tag puts the action back under Dependabot's eye, which is the part that keeps the fix
  from decaying.
- Read against the action's own source at `v2.4.4` rather than assumed: `repo_token` defaults to
  `${{ github.token }}`, so the four job permissions already granted are what it needs; publication is
  refused only for a private repository or a ref other than the default branch. `workflow_dispatch` was
  added on the strength of that second rule — a report can now be asked for, provided the run is started
  on `main`.
- **Unproven until it runs.** The workflow only triggers on `main`, so merging is the first real attempt.
  The thing to check afterwards is not the green tick but the badge: a green run that publishes nothing
  looks exactly like a green run that does.

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
  it belongs. The three Pages bumps were reasoned rather than observed at the time; the deployment of
  2026-09-19 has since exercised all three.
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
| ~~T03~~ | ~~The real-document corpus is not built yet~~ | Done: `tests/corpus`, 27 documents, four producers plus vendored fixtures; 68 since 2026-09-24 |
| T10 | **Narrowed on 2026-09-24**: Word, Acrobat, InDesign, LiveCycle, copier scans, PDF 1.2 archives, signatures and other producers' PDF/A are now in the corpus, found in public sources (`docs/corpus-sources.md`). Still missing is what only an inbox holds: a real Java-stack invoice, a commercial e-signature, a Factur-X from an ERP, a copier's own OCR layer | Contributions, per the "still wanted" column of `docs/corpus-contributions.md` |
| T21 | **The reader reports a truncated stream that is not.** When a stream's data ends inside the parser's 8 KB window but its `endstream` falls past the window's end, `PdfObjectParser.ReadStream` finds no `endstream` in the window and reports `stream.truncated`, cutting the stream at the window. Found on object 49 of the USGS Washington West topographic map (W11 reference, `docs/corpus-sources.md`): data from 68 to 8,185 in a 8,192-byte window; qpdf reads it cleanly. The same file also earns a `filter.failed` on its 14.9 MB Flate image, not yet explained | A synthetic regression test (a stream ending 1 to 10 bytes before 8 KB), then treat an `endstream` beyond the window like data beyond it when a stream-data provider exists |
| T22 | W11 has no committed document, by decision: three public references are hashed in `docs/corpus-sources.md`. The memory promise needs one in CI | M13: fetch one on demand, pinned by SHA-256, as pdf.js and PDFBox do — or commit a smaller heavy case |
| ~~T11~~ | ~~Publishing is configured but untested~~ | Done, and **observed**: four previews are on nuget.org, pushed through the OIDC exchange. No secret is involved — the account is `NUGET_ACCOUNT` in `release.yml` |
| ~~T12~~ | ~~GitHub Pages is not enabled, so the site builds but does not publish~~ | Done, and the diagnosis was wrong: Pages was enabled; no deployment had ever been *run*. Dispatched `Documentation` on 2026-09-19, it went green first time, and the site served 44 pages plus the API reference — **served, not rendered**: every user-facing page was broken, which only a look at one would have shown (2026-09-22). The three Pages action bumps of 2026-09-16 are now observed rather than reasoned |
| T13 | The integration suite has one referee (qpdf); veraPDF, pdftotext and a rasteriser join it as their milestones arrive | M10, M12, M14 |
| T14 | Codecov is linked and the badge reads 82.61%, uploaded tokenless (`Token length: 0` in run 84) — so the original entry, "not linked, badge stays empty", is closed. What is left is narrower: the Codecov **GitHub App** is not installed, so it comments as `codecov-commenter` rather than `codecov[bot]` and warns on every pull request that uploads and comments are not reliably processed | Install the Codecov GitHub App on the repository |
| T15 | **Half done, and the missing half now blocks the stable release**: the "Default" ruleset on `main` exists since 2026-09-22 (pull request, one code-owner approval, linear history, CodeQL, coverage), bypassed by repository admins only. GitHub Actions is not a bypass actor, so the stable release's push of its `chore(release)` commit will be refused. The original entry, for the record: auto-merge **is** allowed on the repository; what was missing is a ruleset on `main`, so it still accepted direct pushes and the Dependabot auto-merge workflow had no required check to wait for. **Measured cost**: Scorecard's `Branch-Protection` is 0/10 at weight 7.5, and `Code-Review` is 0/10 at the same weight because nothing here has ever been approved — together about **1.5 points** of the overall score, the largest block left | A branch ruleset on `main` requiring the five pull-request checks, non-strict, **with a bypass for GitHub Actions** — `@semantic-release/git` pushes the `chore(release)` commit straight to `main`, and a ruleset without that bypass fails the stable release in `prepare` |
| T16 | The API baseline is one version for the whole solution, checked against `AdCodicem.Pdf` only. A satellite first shipped in a later release — `AdCodicem.Pdf.Validation` in M2 — has no package at that version, and its pack fails with `NU1101` exactly as `v0.1.0` would have | In M2, before `AdCodicem.Pdf.Validation` is packable: make the baseline per package |
| T17 | One dependency in CI is still unpinned: `dotnet restore` in `ci.yml` has no `--locked-mode`, because no `packages.lock.json` is committed. Measured at 10 of Pinned-Dependencies' 144 weighted units — 0.05 of the displayed score — and `RestorePackagesWithLockFile` in `Directory.Build.props` fails the restore with `NETSDK1013` | When it buys something beyond the check: set the property **per project**, where it works, commit the six lock files, and add `--locked-mode` to `ci.yml` |
| T18 | No OpenSSF Best Practices badge, so `CII-Best-Practices` is 0/10 at weight 2.5 — about 0.26 of the overall score | Register the project at [bestpractices.dev](https://www.bestpractices.dev), answer the questionnaire, put the badge in `README.md` |
| T19 | `Signed-Releases` is unscored (-1) only because no release exists. The moment one does it becomes a scored High check, and nothing in `release.yml` attaches a signature or a provenance bundle to the GitHub Release | Before the first stable release: attest the packages and upload the bundle as a release asset, so the check has a `.intoto.jsonl` to find |
| T20 | The `AdCodicem.` prefix is not reserved on nuget.org. Now that packages exist under it, anyone else can publish `AdCodicem.Anything`, and ours are not marked as coming from a verified owner | Email account@nuget.org with the owner display name and the glob `AdCodicem.*` — there is no self-service button; `docs/releasing.md` has the criteria |
| T04 | An OFL font set must be embedded for default rendering | During M6 |
| T05 | A public API test (a baseline of exported signatures) | Put in place at the start of M7 |
| T06 | `PdfString.ToText` reads Latin-1 rather than full PDFDocEncoding (the 32 positions 0x80-0x9F differ) | Before the first public release |
| T07 | The object cache evicts FIFO rather than LRU; names are interned through an intermediate string | M13, with measurements |
| ~~T08~~ | ~~Fuzzing of the lexer and parser is not set up~~ | Done: in the suite per commit, and a nightly campaign |
| T09 | A memory budget is now enforced in CI; a throughput budget is not | Throughput budget in M13 |
