# 49. Previews weekly, when a package input changed

Date: 2026-10-03

## Status

Accepted on 2026-10-03, by the maintainer, each choice below put to them and settled (#227). It supersedes the
preview track of [30](0030-previews-on-every-merge-stable-releases-on-demand.md), whose stable track stands, and
amends [31](0031-versioned-documentation-stable-lines-and-the-preview.md): the site is no longer redeployed by every
merge.

## Context

ADR 30 made every push to `main` publish a preview: `release.yml` built and tested the whole solution, packed it at
`<last release, patch bumped>-preview.<run number>` and pushed it with `--skip-duplicate`, then redeployed the site
(ADR 31). It accepted the preview count as the price of previews on a public feed, and said where to look "if the
preview count ever becomes a nuisance". Measured on 2026-10-03, over the 57 runs of `release.yml`:

- **Volume.** 48 previews, `0.1.1-preview.10` to `.57`, in 15 days, from 2026-09-19 to 2026-10-03, on a registry
  that keeps every one of them for good.
- **Most changed nothing.** Compared with the version before it, by what the package is built from (the list
  below), 20 of the 47 were the same package under a new number. Their assemblies agree: those of `preview.14` and
  `.15` are the same size and differ in 154 bytes, the version strings and the module identifier. Replayed over the
  same pushes, a gate on those inputs publishes 27; a weekly preview behind the same gate, 2.
- **The numbers misled.** The latest preview was `0.1.1-preview.57` while the 13 `feat` commits since `v0.1.0` make
  the next release `0.2.0`. And the number named a run, not a commit: `preview.49` came from a dispatch, and a
  dispatch of the same commit would have published another.
- **No provenance.** No attestation, for a preview or for a release; nothing tied a package on nuget.org to the run
  that built it but the commit in its nuspec.
- **The job that published built everything.** It restored 71 packages, 610 MB, iText, PDFsharp, Testcontainers and
  BenchmarkDotNet among them, and ran the whole suite while holding `id-token: write`, which any step could exchange
  for a NuGet key (#177). Packing `src/` restores one package, 2.7 MB.
- **A branch could publish.** The preview job tested no ref, and a dispatch from any ref was documented (#178).

What ADR 30 set out to keep still holds: merging stays cheap, and what `main` holds can be tried without building
it. ADR 30 rejected computing the next release's number because it meant running semantic-release on every merge;
once a week, that cost is nothing.

## Decision

**We will publish previews from a workflow of their own, `preview.yml`, every week and on dispatch, and only when
something a package is built from changed since the version nuget.org has from the nearest commit. The packages move
together: all of them at one version, or none. A push to `main` publishes nothing.**

### When

On a schedule, Monday at 07:15 in Paris (`timezone: Europe/Paris`, so summer time does not move it; not on the hour,
when scheduled runs are most often delayed), and on `workflow_dispatch`. From `main` only: the first job stops a
dispatch from any other branch. One run at a time (`concurrency: preview`, `queue: max`), never cancelled, since a
push stopped halfway is exactly the partial set the next run has to complete, and no waiting run dropped for a later
one. There is no input to force a publish.

### The jobs

1. **compute the version** installs semantic-release's npm packages without install scripts and runs
   `.github/scripts/next-version.mjs` (ADR [48](0048-one-version-for-every-package-independent-of-dotnet.md)). Its
   only outputs are two strings, the version and the release type: none of the jobs that decide, test, pack or push
   runs third-party JavaScript.
2. **decide what to publish** runs `.github/scripts/preview-gate.sh`. It takes the release type only as major, minor
   or patch, and computes the version again from it, the last stable tag and the commits since.
3. **build and test** runs both suites on the commit the plan chose, while **pack** packs it through
   `release-pack.sh`. Both first check that the commit is the one the run started on or an ancestor of it.
4. **publish to nuget.org**, in the `nuget` environment, checks the set, attests it and pushes it.
5. **publish documentation** redeploys the site.

### What counts as a package input

`preview-gate.sh` holds the list: `src/`, the solution filter the pack goes through included; `README.md`,
`assets/icon.png`, `NOTICE` and `tools/AdCodicem.Pdf.Arlington/model/LICENSE`, which the core packs;
`Directory.Build.props`, `Directory.Build.targets`, `Directory.Build.rsp`, `global.json`, `nuget.config` in any case,
`.gitattributes`; and `.github/scripts/release-pack.sh`, which holds the pack command line. `Directory.Packages.props`
counts when anything outside its `<PackageVersion>` elements changes, or the version of a package in the restore
graph of the projects under `src/`. Today that graph holds `Microsoft.NET.ILLink.Tasks` alone, so none of the three
changes the file had between previews — FsCheck, JsonSchema.Net, the comparison libraries — would have published.

Nothing else reaches a package: not `docs/`, `tests/`, `samples/`, `benchmarks/` or the rest of `tools/`, whose
generated Arlington tables are committed under `src/`; not the workflows; not `LICENSE`, which the license expression
replaces; not `.editorconfig`, which changes diagnostics only. The gate is conservative: a change to a comment, or to
a `SuppressMessage` justification, which the compiler drops, still publishes, as it did between `preview.19` and
`.20`. A file that comes to change what a package carries has to join the list.

### Publish, repair or none

The gate decides from nuget.org, not from the previous run: a run can stop after part of its push, a stable release
publishes too, and nuget.org is the one record that survives both. Its **base** is, of the versions nuget.org has from
the last stable tag merged into `HEAD` on, across every package ID, the one packed from the commit nearest to `HEAD`,
read off the `<repository commit="…"/>` the SDK's SourceLink writes into each nuspec: all 48 previews carry it. Not
the highest in SemVer order, which a reverted feature's preview would hold for good.

- **repair**: a package the base commit packed lacks the base version, so a push stopped midway. The run packs the
  base commit again at the base version, tests included, and pushes only the IDs nuget.org lacks. Whatever `HEAD`
  changed since waits for the next run.
- **publish**: a package input changed between the base commit and `HEAD`.
- **none**: nothing a package is built from changed.

Every lookup that fails fails the run rather than guessing: a guess that publishes adds a version to nuget.org for
good, and a failed run publishes nothing and shows in the Actions tab.

### The version

The version the next stable release would get (ADR 48), suffixed `-preview.<N>`, `N` counting the commits since the
last stable tag: `0.2.0-preview.294` leads to `0.2.0`, and the same commit always gets the same number, which is what
lets a later run complete a version an earlier one left half published. With no commit that would release anything,
the next patch. The plan does not take the version on trust from the job that ran third-party JavaScript: it
computes it again, and fails on any other. A version nuget.org already has fails the plan; one below the newest on
nuget.org, after a revert, is published with a warning.

The first preview under this decision, `0.2.0-preview.<N>`, sorts above every `0.1.1-preview.<run>` before it.

### All or nothing

One package ships today; the second, `AdCodicem.Pdf.Tool`, comes with M06. The scripts are written for several from
now on, and held to it by a test harness of fake packages:

- **Checked twice before the first push**, by `verify-packages.sh` in the pack job and again in the publish job: one
  version of the `X.Y.Z-preview.N` shape in every file name and every nuspec, every packable project under `src/` and
  nothing else, each with its symbol package. A version lost on the way packs `0.1.0-alpha` rather than failing;
  this is what stops it.
- **Pushed in dependency order** by `push-packages.sh`, which stops a pass at its first failure, so that whatever a
  stopped push leaves on nuget.org can be restored: no package is published before one it depends on. Each `.nupkg`
  is pushed with `--no-symbols`, then its `.snupkg` on its own: `--skip-duplicate` never sends the symbols of a
  package it skips. Each pass pushes only what nuget.org lacks; three passes, a minute and two apart.
- **Completed at the same version**, by those passes, by **Re-run failed jobs**, which pushes the same bytes, or by the
  next run, in repair mode.
- **Listed before the job ends.** The job waits up to 30 minutes until nuget.org lists every package and serves every
  symbol package, so that the next run, the documentation's label and `release.yml` read a nuget.org that has caught
  up. A symbol package still missing fails the job, which a re-run completes.

### What reaches the push

- **Only the packable projects**, restored, built and packed in a job of their own with no credential.
- **The bytes the pack job produced.** The publish job downloads the artifact by the id the pack job's upload
  returned, not by name, and checks every file against the SHA-256 digests the pack job wrote as a job output, which
  no other job can write.
- **A plan that is still current.** Before logging in, the publish job runs `preview-gate.sh --recheck`: if nuget.org
  now gives another base than the plan saw, a re-run of an old run stops rather than push a stale version.

### Provenance

Every `.nupkg`, `.snupkg` and assembly inside them is the subject of a SLSA build provenance attestation, signed
through Sigstore with the job's OIDC token, before the push. nuget.org re-signs each `.nupkg`, which changes its
digest, but leaves the files inside as they were packed, and NuGet extracts them unchanged: an assembly restored from
nuget.org verifies with `gh attestation verify` (`SECURITY.md`). The build is deterministic apart from the version
strings, so the assembly is what the commit builds.

### The documentation

- On the schedule and on dispatch, every run that decided redeploys the site, whether it published, repaired or found
  nothing to do, labeled with the version on nuget.org that describes the commit it builds: the version just
  published, or the base. A repair that leaves `HEAD` with package changes nuget.org does not have yet builds the base
  commit instead.
- **A push to `main` redeploys the site when no package input changed since the base**, and only then. The site also
  publishes the project documents, `docs/status.md` and the roadmap among them, which change in nearly every session;
  a weekly deployment alone would leave them up to a week behind. A push that changed a package input leaves the site
  as it is, so that `/preview/` never describes a `main` nobody can install, and says so in its summary: Monday's
  preview, or a dispatch, deploys it.
- A run in which any job failed, or was cancelled, deploys nothing.
- `docs.yml` is called only: its `ref` and `preview-version` inputs are required, and it has neither a dispatch nor a
  fallback that would look the label up on nuget.org (`scripts/preview-version.mjs` is gone). It checks the commit it
  is given before checking it out, and a deployment older than the live site stands down.
- A stable version as the label shows no preview section, as right after a release (ADR 31).

### Rejected

- **A preview per push, gated on the same inputs.** 27 previews in 15 days instead of 48; each is permanent, and a
  change landing over several pull requests would publish every intermediate state. A dispatch gives a preview at
  once when one is wanted.
- **A weekly site only.** The project documents would lag by up to a week.
- **A nightly preview behind an approval.** An approval given every day stops being a decision. The approval stays
  where it decides something: the stable release, in `nuget-stable`.
- **A feed of its own for previews.** GitHub Packages asks for a token to restore even a public package, and Azure
  Artifacts serves a public feed only from a public project, which Azure DevOps is retiring. Trying a preview would
  stop being `dotnet add package --prerelease`.
- **`semantic-release --dry-run` for the version.** It checks that it may push, and `@semantic-release/github` wants
  a token: a job that only reads passes neither.
- **The run number as `N`**: a dispatch of the same commit gets another number, and a version left half published
  could not be computed again.
- **Comparing the packed bytes with nuget.org's.** The assembly carries its version and commit, so no two previews
  match byte for byte, and the decision would need a pack before it could be made.
- **A glob push with `--skip-duplicate`.** It skips the symbol package of every package it skips, and pushes in file
  order, which puts a satellite before the core it depends on.
- **A path filter on the workflow** (`on.push.paths`). It sees one push's changes, not what nuget.org lacks; a failed
  run is never decided again; and a merge made with `GITHUB_TOKEN` triggers no workflow at all: the auto-merges of
  #112 and #113, on 2026-09-28, started no run of `release.yml`, where the maintainer's merge of #114 between them did.
- **A force input, or unlisting a preview.** The first would publish what the gate refused; the second hides a version
  that stays installable by number.

## Consequences

At most one preview a week, plus dispatches. A change that ships waits up to a week for its preview unless someone
dispatches one. The first run after this lands publishes `0.2.0-preview.<N>`, since this change touches
`Directory.Build.props`, `release-pack.sh` and the solution filter under `src/`.

nuget.org's Trusted Publishing policies name a workflow file and an environment: `preview.yml` with `nuget`,
`release.yml` with `nuget-stable` (`docs/releasing.md`). The `preview.yml` policy has to exist before this merges;
the old `release.yml` with `nuget` one is deleted once `preview.yml` has published.

The `nuget` environment's deployment branches should be limited to `main`, a setting no branch can rewrite (#178):
the version job's ref check stops a dispatch, not a branch that edits the workflow.

GitHub disables a public repository's schedule after 60 days without activity, and notifies a scheduled run's failure
to whoever last changed its cron line. `docs/releasing.md` has both remedies.

The SDK is not a package input: the workflows install the latest 10.0 patch, so a new SDK changes the next preview's
bytes without publishing one by itself.

A repair packs again; the assembly is deterministic apart from its version strings, which the repair reproduces, but
the attestation of a repair names the run that repaired, not the one that first packed.
