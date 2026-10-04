# Releasing

## Package identifiers

Confirmed, and all seven were unclaimed on nuget.org when checked on 2026-09-13. ADR 36 renamed the
validation satellite on 2026-09-26, before it was ever published: the rule engine and the structural profile
live in the core, and the satellite carries the PDF/A and PDF/UA profiles only. The revision of the roadmap
the same day added six more, all unclaimed on nuget.org when checked that day, and all under the reserved
prefix; the font set's package followed on 2026-09-27, unclaimed that day too.

| Package | Contents | Ships from |
|---|---|---|
| `AdCodicem.Pdf` | Object model, reader, validation and its structural profile; then writer, revisions, pages, fonts, logical structure | M01 |
| `AdCodicem.Pdf.Tool` | The command-line tool | M06 |
| `AdCodicem.Pdf.Fonts` | The OFL font set — Liberation Sans, Serif and Mono — as WOFF2 | M08 |
| `AdCodicem.Pdf.Barcodes` | Vector barcodes and payment codes | M10 |
| `AdCodicem.Pdf.Html` | HTML parsing, CSS engine, layout, painting | M12 |
| `AdCodicem.Pdf.AspNetCore` | Dependency injection and `IResult` integration | M12.6 |
| `AdCodicem.Pdf.FacturX` | Factur-X and ZUGFeRD | M14 |
| `AdCodicem.Pdf.CaseFile` | Legal case files | M18 |
| `AdCodicem.Pdf.Conformance` | PDF/A and PDF/UA profiles for the validation engine | M20 |
| `AdCodicem.Pdf.Imaging` | Image decoders and lossless encoders | M22 |
| `AdCodicem.Pdf.Compare` | Comparison and templates | M24 |
| `AdCodicem.Pdf.Rendering` | Rasterization | M25 |
| `AdCodicem.Pdf.Signing` | PAdES, long-term signatures, signature validation | M26 |
| `AdCodicem.Pdf.Docx` | DOCX to HTML | M31 |

The first packages shipped on **2026-09-19**: `AdCodicem.Pdf` `0.1.1-preview.10` and following, previews from
`main`. The thirteen other identifiers are still unclaimed, and ship with the milestones above.

**The `AdCodicem.*` prefix is reserved** on nuget.org: by 2026-09-26 its search API marks `AdCodicem.Pdf` as
verified. Nobody else can publish under the name, and every package under it shows a verified owner — on
nuget.org and in Visual Studio. For the record, the
[procedure](https://learn.microsoft.com/nuget/nuget-org/id-prefix-reservation) is an email to
**account@nuget.org** giving the owner's display name and the prefix; nuget.org weighs that the prefix
identifies its owner and that packages under it carry consistent metadata and a license declared with the
`license` element, which `Directory.Build.props` gives every package (`Authors`, `PackageLicenseExpression`,
an embedded `PackageIcon`).

## Licenses and notices

Every package is MIT — `PackageLicenseExpression`, from `Directory.Build.props` — and carries `README.md` and the
icon. The core package also carries data derived from someone else's work: the tables of
`src/AdCodicem.Pdf/Validation/Arlington/ArlingtonModel.g.cs`, generated from the Arlington PDF Model, which the PDF
Association publishes under the Apache License 2.0 (ADR 44). Section 4 of that license asks a redistribution to carry
the license's text and the work's notice, and to say what was changed. So:

- the root `NOTICE` names the model, the commit the tables come from and what was changed — reduced to lookup tables,
  and overridden as `tools/AdCodicem.Pdf.Arlington/overrides.tsv` records —, and quotes word for word the attribution
  notice that opens the model's `NOTICE.txt`, which is vendored whole beside its `LICENSE`;
- `src/AdCodicem.Pdf/AdCodicem.Pdf.csproj` packs `NOTICE` at the package's root, and the model's `LICENSE` as
  `licenses/arlington-pdf-model/LICENSE`: NuGet cannot rename a file as it packs it, so the text keeps its name in a
  folder of its own;
- the generated file's header says the same, for whoever reads the source.

The license expression stays `MIT`, the maintainer's choice on 2026-09-28: the package's code is MIT, and `NOTICE`
attributes the data. When the model is updated (`docs/website/docs/reference/validation-rules.md`), the commit `NOTICE` names changes with
the lock. A satellite that one day carries third-party material packs its notice the same way, and `NOTICE` gains a
paragraph for it.

## Versioning — computed from the commits

Versions are not chosen; they are derived. The *moment* of a release is chosen — see **Releasing** below.
semantic-release reads the commits since the last tag and decides: `fix:` and `perf:` bump the patch,
`feat:` the minor, and a `!` or a `BREAKING CHANGE:` footer the minor while the major is 0, the major
after (ADR 48). Nothing releasable in the commits means no release at all, which is the correct outcome for
a run over documentation changes.

The commit analyzer reads the commits with the `conventionalcommits` preset. Without a preset it used
`angular`, which does not know `!`: a `feat!:` released nothing. The tooling is pinned: the root
`package.json` lists semantic-release and its plugins, `package-lock.json` pins them, and every job installs
them with `npm ci --ignore-scripts`. `.github/scripts/next-version.mjs` computes the version semantic-release
would give the next release without a token or a push right, with the same analyzer at the same version; it
agrees with semantic-release over every commit since `v0.1.0`.

This is why the conventional-commit check on pull requests is not a style rule. A malformed message does
not look untidy — it produces no release, silently.

**Every package shares one version** and is published with the others, even when only one changed. They
are tightly coupled — the satellites exist to extend the core — and one number keeps the API-compatibility
baseline unambiguous. That version follows our own API, never the major of .NET or of a framework a
satellite builds on, and no package identifier names a framework major
([ADR 48](adr/0048-one-version-for-every-package-independent-of-dotnet.md)).

### Staying below 1.0

semantic-release declares `1.0.0` for a first release when it finds no previous tag. To stay in `0.x`,
the starting point is tagged once, by hand — **done on 2026-09-15**, `v0.1.0` on `2808d2f`:

```bash
git tag v0.1.0 && git push origin v0.1.0
```

From then on it continues from that tag — `fix:` gives `0.1.1`, `feat:` gives `0.2.0`, and so does a
breaking change while the major is 0. `1.0.0` is a decision of its own: the change that makes it replaces
the analyzer's `{ "breaking": true, "release": "minor" }` rule with `{ "breaking": true, "release": "major" }`
(ADR 48).

That tag has **no package behind it**: nothing was ever published as `0.1.0`. It matters because package
validation compares the public API with the last release, downloads that baseline from nuget.org, and fails
the build with `NU1101` when nuget.org does not have it. Package validation holds a patch, and a minor from
1.0 on, to the last release; a major breaks on purpose, and so does a minor below 1.0, so neither is held to
it. When it applies, `.github/scripts/release-pack.sh` asks nuget.org, package by package, whether the last
release exists, and packs the ones it lacks — the tag above, or a package added since — without a baseline,
through `ReleaseBaselineVersion` and `ProjectsWithoutBaseline` in `Directory.Build.props`. It fails the run
when nuget.org cannot be asked: a compatibility check that quietly switches itself off on a network error is
not a check.

### What is packed

Only the packable projects under `src/`, through the solution filter `src/AdCodicem.Pdf.Packages.slnf`: no
test, benchmark, sample or tool project is restored in the job whose output is published. Packing `src/`
restores one package, 2.7 MB (`Microsoft.NET.ILLink.Tasks`, which the SDK adds for trimming); the whole
solution restored 71, 610 MB, iText, PDFsharp, Testcontainers and BenchmarkDotNet among them. A solution
filter cannot glob, so `ci.yml` checks on every pull request that every packable project under `src/` is
packed (`.github/scripts/package-ids.sh`, `verify-packages.sh`), and that the core's nuspec declares no
dependency (invariant 1).

`release-pack.sh` packs every release, preview or stable, and `verify-packages.sh` checks the set before
anything is pushed: one package per packable project, all at the version asked for, in the file name and in
the nuspec, each with its `.snupkg`, and nothing else.

`VersionPrefix` in `Directory.Build.props` only matters for a build nobody handed a version to — a local
`dotnet pack` gives `0.1.0-alpha`, and the `-alpha` is there to make an accidentally published local build
obvious. `release-pack.sh` passes `-p:Version` explicitly, which overrides prefix and suffix alike. A version
lost on the way would therefore not fail the pack, it would pack `0.1.0-alpha`: that is what the check of the
set catches.

## Publishing: trusted publishing, not API keys

Publication uses [trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing):
the workflow asks GitHub for a short-lived OIDC token, nuget.org validates it against a policy and returns
an API key valid for one hour and usable once. No long-lived secret exists to leak, rotate or store.

### One-time setup on nuget.org

A policy names a workflow file and an environment, and a run of any other file, or of the same file in
another environment, is refused at the OIDC exchange. There are two, one per publishing workflow (ADR 49).
Sign in, then **your username → Trusted Publishing → add a policy**, owner `AdCodicem`, repository
`AdCodicem.Pdf`, scopes *push new packages and new versions*, glob `AdCodicem.Pdf*`:

| Workflow file | Environment | Publishes |
|---|---|---|
| `preview.yml` | `nuget` | Previews, weekly and on dispatch |
| `release.yml` | `nuget-stable` | Stable releases |

The first policy, `release.yml` with `nuget`, was **done** on 2026-09-15 and **proven** on 2026-09-19, when the
first preview was pushed through the OIDC exchange; it published every preview until `preview.yml` took them
over. Add the `preview.yml` policy before that change merges, and delete the old one once `preview.yml` has
published its first preview. Renaming either workflow, or moving its publishing job to another environment,
needs the policy changed on nuget.org first. After adding a policy, check that it reads *active*.

Two things worth knowing:

- A policy on a **private** repository starts *temporarily active for seven days*. It becomes permanent
  after the first successful publish, which gives nuget.org the repository and owner identifiers it needs
  to pin the policy against a repository being deleted and recreated under the same name. If no publish
  happens in those seven days the policy goes inactive; the window can be restarted.
- The policy is tied to its owner. If it belongs to an organization and the person who created it leaves,
  it goes inactive until they are added back.

### One-time setup on GitHub

Two environments (Settings → Environments), each named by its policy:

- **`nuget`**, for `preview.yml`'s publish job: deployment branches limited to `main`, a setting no branch
  can rewrite (#178), and **no required reviewer**, which would hold every weekly preview until someone
  clicks.
- **`nuget-stable`**, for `release.yml`: deployment branches limited to `main`, and **a required reviewer**.
  This is the approval of a stable release: the dispatch starts the workflow, and the job that holds the
  credentials waits for it.

**No secret is involved in publishing.** `NuGet/login` requires a `user`, because OIDC proves the run is
authorized without saying which account the short-lived key belongs to, and that account name is
`AdCodicem` — the owner of this repository, the prefix of every package, and public on every page nuget.org
serves for them. It is written in the workflows, not kept as a secret: a secret would have hidden nothing and
added a step that fails months later, in a workflow nobody is watching, with an error about publishing when
the cause is an empty setting.

## Releasing

Two paths out of the repository, each in its workflow ([ADR 49](adr/0049-previews-weekly-when-a-package-input-changed.md)):
previews in `preview.yml`, stable releases in `release.yml`. Nothing is published by merging.

### A preview a week, when something that ships changed

`preview.yml` publishes a preview of every package every Monday at 07:15, Paris time, and whenever it is
dispatched (**Actions → preview → Run workflow**, from `main`: a dispatch from another branch stops at its
first job). It publishes only when a package input changed since the version nuget.org has from the nearest
commit, and then every package at one version, or none.

```bash
dotnet add package AdCodicem.Pdf --prerelease
```

Each run says what it decided. **compute the version** prints the version semantic-release would give the
next release, and **decide what to publish** writes a summary, *Preview: publish*, *repair* or *none*, with
the reason and the package inputs that changed:

- **publish**: the tests run on `HEAD`, the pack job packs it at `<next release>-preview.<commits since the
  last stable tag>`, and the publish job checks, attests and pushes it, then waits until nuget.org lists it.
- **repair**: a previous push stopped midway, so nuget.org has the base version for some packages only. The
  run packs that commit again at that version, tests included, and pushes only the missing packages.
  Whatever `HEAD` changed since waits for the next run: dispatch again once the repair is green.
- **none**: nothing that ships changed. The tests, the pack and the push are skipped.

Whatever it decided, a run whose jobs all succeeded redeploys the site, labeled with the version on nuget.org
that describes the commit it builds. What counts as a package input, and why, is in ADR 49 and at the top of
`.github/scripts/preview-gate.sh`.

A **push to `main`** publishes nothing. It runs the same decision, and redeploys the site when no package
input changed since the version on nuget.org, so that the project documents stay current; when one did, the
site waits for the preview that publishes it, and the run's summary says so.

The version is the next release's, suffixed `-preview.<N>`, where `N` counts the commits since the last
stable tag: `0.2.0-preview.294` is the 294th commit after `v0.1.0`, leading to `0.2.0`. A commit always gets
the same number. The previews before ADR 49, `0.1.1-preview.<run number>`, all sort below the first one after
it. After a feature is reverted the next preview can be lower than one already published; the plan warns,
and NuGet keeps offering the higher one as the latest prerelease until a higher version ships.

#### When a preview run is red

- **The publish job failed.** Use **Re-run failed jobs** on that run. It pushes the same bytes, already
  tested and attested, and only what nuget.org still lacks. If a newer run has published since, its recheck
  refuses ("something was published since"): dispatch `preview.yml` instead, which decides again.
- **The wait for nuget.org timed out.** The push succeeded, but nuget.org had not listed every package, or
  served every symbol package, within 30 minutes. Re-run the failed job once nuget.org has caught up; it
  pushes nothing that is already there.
- **A symbol package is still missing.** Re-run the failed job, which pushes it. If nuget.org's validation
  rejected it, and e-mailed the account to say so, a re-run cannot help: upload the `.snupkg` from the run's
  `packages-<version>` artifact through nuget.org's upload page.
- **The plan failed right after a stable release** ("v… is tagged, but nuget.org has no version of these
  packages from … on"). nuget.org has not listed the release yet: wait, and dispatch again.
- **The plan failed because the version "is already on nuget.org (…), packed from another commit".** A
  preview left above a later, lower stable release, after a revert, holds the number this one computed. The
  next commit on `main` moves the number.
- **The plan failed because a version "was packed from …, which is not an ancestor of …".** A version on
  nuget.org, from the last stable tag on, was packed from a commit `main` does not have: a preview pushed from a
  branch, which the `nuget` environment limited to `main` prevents (#178), or a rewritten history. Every run
  fails until a stable release raises the floor above it, or the version is accounted for by hand; unlisting it
  does not help, since the gate reads unlisted versions too.
- **The pack failed on the API baseline (`CP0001` and the like).** The commits since the last release are
  typed as fixes, so the preview is held to that release's API. A breaking change typed `fix` is the usual
  cause: it needs a `!`.

#### Routines

- **Before a stable release**, dispatch `preview.yml` if package inputs changed since the last preview, and
  start the release once that run is green.
- **After 60 days without activity in the repository**, GitHub disables the schedule of a public
  repository's workflow. Turn it back on with **Actions → preview → Enable workflow**.
- **Failures of a scheduled run are notified to whoever last changed its cron line**, not to whoever
  dispatches it. If the run page names an actor other than you, push a commit of your own that touches the
  cron line, or watch the workflow.
- **The first time `preview.yml` publishes**, and after any change to its publish job, dispatch it rather
  than wait for Monday, then check that nuget.org has the version the run printed, that an assembly restored
  from nuget.org verifies (`SECURITY.md`), and that `/preview/` names that version.

### What a preview promises: nothing

A preview is a build of `main` offered for trying out, and **carries no guarantee**. Its API, its behavior
and any of its features may change or disappear in the next preview, without notice and without a
deprecation period. Compatibility promises — semantic versioning, and the public API checked against the
last stable release by package validation — hold **between stable releases only**. An application that
depends on a preview should pin its exact version, and read the commits before moving to the next (ADR 30).

This is what lets work on `main` reshape or withdraw an API that no stable release has shipped: a preview
already on nuget.org is never a reason to keep one.

### The stable release is a decision, and it is taken by hand

**Actions → Release → Run workflow**, from `main`. The stable path tags, writes to `main` and cannot be taken
back, so it has a workflow of its own, behind the `nuget-stable` reviewer's approval.

Before releasing, make sure what ships has been previewed: if package inputs changed since the last preview,
dispatch **preview** and wait until it is green. Its publish job ends only once nuget.org lists every
package, so a release started earlier would compare against a nuget.org that has not caught up. Then run
**Release** with **dry run** ticked and read the version it computes before running it for real.

The run has five jobs:

1. **build and test** runs both suites on the dispatched commit, without any credential.
2. **pack**, without any credential either, computes the version with `next-version.mjs`, packs the packable
   projects under `src/` through `release-pack.sh`, freezes the user documentation for the release's line
   (`npm run snapshot`, [the documentation site](#the-documentation-site) below), and uploads both, with
   their SHA-256 digests as job outputs. It packs on every run, the dry run included.
3. **was it previewed** warns on the run page, without ever blocking, when the release ships package inputs
   that no version on nuget.org carries, or when the version it compares with reached only some packages.
   It runs before the release job, so that its warning is there when the reviewer approves.
4. **publish** (or **dry run**), in `nuget-stable`, waits for the approval, then builds nothing: it checks
   the pack job's files against their digests and the set, puts the frozen documentation in place, and runs
   semantic-release. semantic-release checks that the version it computes is the one packed, writes
   `CHANGELOG.md`, commits it with the frozen documentation, tags `vX.Y.Z`, pushes the packages through
   `push-packages.sh` — dependencies first, waiting until nuget.org lists them — and drafts the GitHub
   Release with the packages attached and a table linking each to nuget.org. Every write goes through the
   release App's token; the job's own token only reads.
5. **attest provenance** attests every package and the assemblies inside them, attaches the bundle
   (`AdCodicem.Pdf.<version>.sigstore.json`) to the draft, and publishes it; then **publish documentation**
   redeploys the site from the release's tag, so its root is the version just released.

**dry run** stops after semantic-release has worked out the version and the notes: nothing is published,
tagged or deployed, and no publishing key is requested.

If the run reports no release, read the commits: `docs:`, `chore:`, `test:`, `refactor:`, `build:` and `ci:`
deliberately release nothing. If the push fails with an authorization error, the mismatch is almost always
between the policy and the workflow: the file name, the environment, or the account name `user` gives
`NuGet/login`.

#### When a release run is red

- **The tests or the pack failed.** Nothing was published, tagged or committed: correct it and dispatch
  again.
- **The attest provenance or publish documentation job failed.** Use **Re-run failed jobs**: dispatching
  the release again would find nothing to release and skip both. Do not publish the draft by hand, or the
  release goes public without its bundle.
- **semantic-release failed in its publish step, after tagging.** Nothing completes the release by itself.
  What reached nuget.org is a prefix of the set, in dependency order; the rest is in the run's
  `release-packages` artifact. Push the missing packages by hand, in the order the log prints ("Push
  order: …"), then create the GitHub Release from the tag, with that artifact's files as its assets, and say
  in its notes that it carries no attestation: only a run of `release.yml` can sign as `release.yml`. Until
  it is complete, `preview.yml` refuses to publish ("complete it by hand").

#### The release App

The ruleset on `main` takes only pull requests, and its bypass list takes repository roles, teams, GitHub
Apps and deploy keys, never the `GITHUB_TOKEN` a workflow runs with (#41). semantic-release therefore pushes
the release commit and the tag as a dedicated GitHub App:

1. Create a GitHub App (Settings → Developer settings → GitHub Apps → New), with no webhook, repository
   permissions **Contents**, **Issues** and **Pull requests** set to *Read and write*, installable on this
   account only. Generate a private key.
2. Install it on this repository only.
3. Add the App to the bypass list of the ruleset on `main` (Settings → Rules → Rulesets), mode *Always
   allow*.
4. On the `nuget-stable` environment, add the variable `RELEASE_APP_CLIENT_ID` (the App's Client ID) and the
   secret `RELEASE_APP_PRIVATE_KEY` (the whole `.pem`). On the environment rather than the repository, the
   key is readable only once the reviewer has approved the run.

Without them the release job stops at its first step, before anything is published.

## Also configured by hand, once

| What | Where | Needed for |
|---|---|---|
| Codecov | The repository is linked on codecov.io, and the Codecov **GitHub App** is installed — **done** since 2026-09-27: it reports on each pull request as `codecov[bot]` | Coverage upload in CI. A public repository uploads without a token; the `CODECOV_TOKEN` secret is read if one exists, and becomes necessary only if the repository goes private or Codecov stops accepting tokenless uploads |
| Trusted Publishing policies | nuget.org, see [above](#one-time-setup-on-nugetorg) | `preview.yml` and `release.yml` |
| Environments `nuget` and `nuget-stable` | Settings → Environments, see [above](#one-time-setup-on-github) | The two publishing jobs, and the approval of a stable release |
| The release App | See [above](#the-release-app) | The stable release's commit and tag |
| "Allow auto-merge" | Settings → General | Dependabot auto-merge |
| Squash merging allowed, with the pull request title as its default commit message | Settings → General → Pull Requests | Dependabot pull requests land as their title says |
| Required status checks **`Build and unit tests`**, **`Integration tests`**, **`Conventional commits`** and **`workflows`** | Settings → Rules → Rulesets, the ruleset on `main` | Auto-merge cannot merge a red build (#41). The checks are job names: renaming one leaves every pull request waiting for a check that never reports |
| Discussions, Sponsors | Settings → Features, and the GitHub account | The discussion template and `FUNDING.yml` |

## The .NET compatibility island

`tests/Compat` installs the package each commit packs into a trimmed `net11.0` application on the .NET 11 release
candidate, and `ci.yml`'s `compat (.NET 11)` job runs it, with the unit suite rolled forward onto the .NET 11
runtime, on every pull request and push ([ADR 48](adr/0048-one-version-for-every-package-independent-of-dotnet.md)).
To run it locally, with the .NET 11 SDK installed, after `dotnet pack src/AdCodicem.Pdf.Packages.slnf -c Release -o
artifacts/packages`:

```bash
cd tests/Compat
# A package folder of its own: every local pack is 0.1.0-alpha, and a restore would otherwise take whichever
# 0.1.0-alpha ~/.nuget/packages kept from an earlier pack.
NUGET_PACKAGES="$(mktemp -d)" dotnet publish -c Release -p:AdCodicemVersion=0.1.0-alpha -o out \
  && out/AdCodicem.Pdf.Compat ../corpus
```

Nothing bumps it but a person:

- **At each release candidate of .NET 11**, one pull request moves `tests/Compat/global.json` to the new SDK version.
- **When .NET 11 ships** (expected around 2026-11-10, not confirmed):
  - `global.json` becomes `"version": "11.0.100"`, `"rollForward": "latestFeature"`, `"allowPrerelease": false`;
  - `DOTNET_ROLL_FORWARD_TO_PRERELEASE` leaves the job;
  - `compat (.NET 11)` joins the required status checks of the ruleset on `main`, and the comment in `ci.yml` that
    says it is not one goes.
- **When .NET 12 previews arrive**, moving the island to the next major, or adding a second one, is a decision of its
  own. Renaming the job changes the required check.

## The documentation site

`docs/website` is a Docusaurus site publishing both the user-facing documentation and the project documents in
`docs/`. CI builds it on every push, so a document that does not build never reaches the default branch,
and every build ends by reading its own pages for anything rendered wrong (`scripts/check-site.mjs`).

### What the site serves

The user documentation is **versioned** (ADR 31); the project documents are not, and always come from
`main`.

| Where | What | Comes from |
|---|---|---|
| `/` | The homepage, its example read from the latest stable line | `src/pages`, and `_homepage-example.md` of `versioned_docs/version-<newest line>` |
| `/docs/` | The latest stable line | `versioned_docs/version-<newest line>` |
| `/docs/0.2/`, `/docs/1/`… | Every older stable line, under a "no longer maintained" banner | `versioned_docs/version-<line>` |
| `/docs/preview/` | The preview, behind the navbar's **Preview** button | `docs/website/docs`, the working tree |
| `/project/` | Roadmap, status, decisions, milestones | `docs/`, copied at build time |

The user documentation was served at the root of the site until 2026-10; every page it had there, in every
version, redirects to its address under `/docs`.

A **line** is a minor version below 1.0 (`0.3`) and a major from 1.0 on (`1`), and the selector labels it
with its latest release. `versions.json` lists the lines, newest first; `releases.json` maps each to its
latest release. Both are written by the stable release — never by hand, except to prune a line.

The preview section exists only while a preview is newer than the latest stable release. Right after a
release there is none, and the **Preview** button disappears until the next preview is published. Before
the first stable release, the preview is the whole documentation, at `/docs`, under a banner saying so, and the
homepage reads its example from the working tree.

### When it is deployed

`.github/workflows/docs.yml` builds every version at once and deploys to GitHub Pages. It is called only,
with the commit to build and the version on nuget.org that describes it, which labels the preview section:

- by `preview.yml`, after every run on its schedule or on dispatch, and after a push to `main` that changed
  no package input since the version on nuget.org (ADR 49);
- by `release.yml`, after every stable release, with the release's tag, whose commit carries the frozen
  documentation.

It has no **Run workflow** button: dispatching `preview.yml` is how the site is redeployed by hand. It checks
the commit it is given before it builds a line of it, and a deployment older than the live site, which can
arrive last when two run together, stands down rather than replace it (`deployment.json`, at the site's root,
names the commit live).

### Changing the documentation

- **For the next release**: edit `docs/website/docs`. It shows under `/docs/preview` once the site is
  redeployed — after the merge, or with the next preview —, and is frozen by the next stable release.
- **For a version already released**: edit its copy in `docs/website/versioned_docs/version-<line>`, then
  merge: the push redeploys the site, unless a package input is waiting for its preview, in which case the
  preview deploys it. Make the same change in `docs/website/docs` if it still applies: the
  next release on that line re-freezes from there, and overwrites the copy.
- **To see the versioned site locally**, freeze a line without committing it —
  `node scripts/version-docs.mjs 0.2.0` in `docs/website` — then build with
  `DOCS_PREVIEW_VERSION=0.2.1-preview.1 npm run build`. Delete `versioned_docs`, `versioned_sidebars`,
  `versions.json` and `releases.json` afterwards. Without `DOCS_PREVIEW_VERSION`, a build labels the
  working tree `local build`.

### Settings

One manual step, once: **Settings → Pages → Source: GitHub Actions** — **done**. Before it was done the
deployment job failed at `configure-pages` and the site simply was not published; nothing else broke. The
`github-pages` environment deploys from `main` only.

**First published on 2026-09-19**, by dispatching `Documentation` by hand; **every user-facing page was
broken until 2026-09-22** — see the status journal for why nothing caught it.

The site lands at `https://adcodicem.github.io/AdCodicem.Pdf/`. If the repository is ever renamed, the
`baseUrl` in `docs/website/docusaurus.config.js` has to follow.

Updating the site is part of the definition of done for every milestone, not a separate chore — see
`docs/roadmap.md`.
