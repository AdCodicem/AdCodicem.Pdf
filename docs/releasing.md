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

**Done** (2026-09-15) and **proven** on 2026-09-19, when the first preview was pushed through the OIDC
exchange. Sign in, then **your username → Trusted Publishing → add a policy**:

| Field | Value |
|---|---|
| Repository owner | `AdCodicem` |
| Repository | `AdCodicem.Pdf` |
| Workflow file | `release.yml` — the file name only, no path |
| Environment | `nuget` — the workflow declares `environment: nuget`, so the policy must match |
| Scopes | Push new packages and new versions, glob `AdCodicem.Pdf*` |

Two things worth knowing:

- A policy on a **private** repository starts *temporarily active for seven days*. It becomes permanent
  after the first successful publish, which gives nuget.org the repository and owner identifiers it needs
  to pin the policy against a repository being deleted and recreated under the same name. If no publish
  happens in those seven days the policy goes inactive; the window can be restarted.
- The policy is tied to its owner. If it belongs to an organization and the person who created it leaves,
  it goes inactive until they are added back.

### One-time setup on GitHub

Create the `nuget` environment (Settings → Environments). The workflow declares `environment: nuget`, and
the policy above names the same environment, so the two must agree — that is all the environment is for.

**No secret is involved.** `NuGet/login` requires a `user`, because OIDC proves the run is authorized
without saying which account the short-lived key belongs to, and that account name is `AdCodicem` — the
owner of this repository, the prefix of every package, and public on every page nuget.org serves for them.
It is therefore written in `release.yml` as `NUGET_ACCOUNT`, once, at the top. A secret would have hidden
nothing and added a step that fails months later, in a workflow nobody is watching, with an error about
publishing when the cause is an empty setting.

Consider requiring a reviewer on the `nuget` environment so a release cannot publish unattended.

## Releasing

Two paths out of the repository, both in `release.yml` — one file, because a trusted-publishing policy is
tied to a workflow file name and one policy is enough for both.

### Every merge publishes a preview

A push to `main` builds, runs the whole suite, publishes a **prerelease** package, and redeploys the site
with that preview's documentation under `/preview`. Nothing is tagged, no changelog is written, no GitHub
Release is opened. Merging stays cheap, and what is on `main` is always installable — and documented:

```bash
dotnet add package AdCodicem.Pdf --prerelease
```

A preview can also be asked for without merging: **Actions → Release → Run workflow**, leaving **What to
publish** on `preview`. It packs whichever ref you pick, so a branch can be tried on a real feed before it
lands — at the price of a version on nuget.org that matches no commit on `main`, permanently. Prefer the
merge unless there is a reason not to. A preview packed from another branch is not documented on the site:
its pages would replace `main`'s, and the Pages environment deploys from `main` alone.

Do not re-run a past run to get a fresh preview: a re-run keeps its run number, so it republishes the same
version, which `--skip-duplicate` accepts and ignores. A new run is what produces a new number.

A preview is numbered `<last release, patch bumped>-preview.<run number>` — after `v0.1.0`, the previews
are `0.1.1-preview.12`, `0.1.1-preview.13`, and so on. That number says **where the preview sits**, not
what the next release will be called: if the commits since the tag contain a `feat:`, the stable release
will be `0.2.0`, and every `0.1.1-preview.n` still sorts correctly between `0.1.0` and `0.2.0`. The run
number only ever increases, so previews never collide, and nothing has to be deleted from nuget.org —
which is just as well, because nothing can be.

### What a preview promises: nothing

A preview is a build of `main` offered for trying out, and **carries no guarantee**. Its API, its behavior
and any of its features may change or disappear in the next preview, without notice and without a
deprecation period. Compatibility promises — semantic versioning, and the public API checked against the
last stable release by package validation — hold **between stable releases only**. An application that
depends on a preview should pin its exact version, and read the commits before moving to the next (ADR 30).

This is what lets work on `main` reshape or withdraw an API that no stable release has shipped: a preview
already on nuget.org is never a reason to keep one.

### The stable release is a decision, and it is taken by hand

**Actions → Release → Run workflow**, setting **What to publish** to `stable`. The dropdown defaults to
`preview`, deliberately: the stable path tags, writes to `main` and cannot be taken back, so it is chosen
rather than reached by clicking through. That run, and only that run:

1. works the version out from the commits since the last tag;
2. freezes the user documentation for the release's line into `docs/website/versioned_docs` (see
   [the documentation site](#the-documentation-site) below);
3. writes `CHANGELOG.md`, commits it with the frozen documentation, and tags `vX.Y.Z`;
4. packs and pushes the stable packages to nuget.org;
5. opens the GitHub Release with the generated notes;
6. redeploys the site from the release commit, so its root is the version just released.

Tick **dry run** to see the version and the notes it would produce and stop there: nothing is published,
tagged, or deployed, and no publishing key is even requested. It applies to the stable path only — a
preview has no version to work out and nothing to undo but the publish itself.

If the run reports no release, read the commits: `docs:`, `chore:`, `test:`, `refactor:` and `build:`
deliberately release nothing. If the push fails with an authorization error, the mismatch is almost always
between the policy and the workflow: the file name, the environment, or the account name in `NUGET_ACCOUNT`.

Two things the stable run needs on `main`: permission to push the changelog commit and the tag. If branch
protection is turned on, either allow the `github-actions` actor to bypass it, or accept that the release
cannot record itself.

The first publish has happened; reserving the ID prefix is what is left, and it is tracked as T20.

## Also configured by hand, once

| What | Where | Needed for |
|---|---|---|
| Codecov | The repository is linked on codecov.io, and the Codecov **GitHub App** is installed — **done** since 2026-09-27: it reports on each pull request as `codecov[bot]` | Coverage upload in CI. A public repository uploads without a token; the `CODECOV_TOKEN` secret is read if one exists, and becomes necessary only if the repository goes private or Codecov stops accepting tokenless uploads |
| "Allow auto-merge" | Settings → General | Dependabot auto-merge |
| Branch protection on `main` with CI as a required check | Settings → Branches | Auto-merge cannot merge a red build |
| Discussions, Sponsors | Settings → Features, and the GitHub account | The discussion template and `FUNDING.yml` |

## The documentation site

`docs/website` is a Docusaurus site publishing both the user-facing documentation and the project documents in
`docs/`. CI builds it on every push, so a document that does not build never reaches the default branch,
and every build ends by reading its own pages for anything rendered wrong (`scripts/check-site.mjs`).

### What the site serves

The user documentation is **versioned** (ADR 31); the project documents are not, and always come from
`main`.

| Where | What | Comes from |
|---|---|---|
| `/` | The latest stable line | `versioned_docs/version-<newest line>` |
| `/0.2/`, `/1/`… | Every older stable line, under a "no longer maintained" banner | `versioned_docs/version-<line>` |
| `/preview/` | The preview, behind the navbar's **Preview** button | `docs/website/docs`, the working tree |
| `/project/` | Roadmap, status, decisions, milestones | `docs/`, copied at build time |

A **line** is a minor version below 1.0 (`0.3`) and a major from 1.0 on (`1`), and the selector labels it
with its latest release. `versions.json` lists the lines, newest first; `releases.json` maps each to its
latest release. Both are written by the stable release — never by hand, except to prune a line.

The preview section exists only while a preview is newer than the latest stable release. Right after a
release there is none, and the **Preview** button disappears until the next merge publishes one. Before
the first stable release, the preview is the whole site, at the root, under a banner saying so.

### When it is deployed

`.github/workflows/docs.yml` builds every version at once and deploys to GitHub Pages. `release.yml` calls
it after every preview published from `main`, handing it the preview's version, and after every stable
release, handing it the release commit. Its own **Run workflow** button redeploys `main` without a package
— for a correction to a frozen version, typically; it asks nuget.org which preview to label the preview
section with, unless it is given one.

### Changing the documentation

- **For the next release**: edit `docs/website/docs`. It shows under `/preview` after the merge, and is
  frozen by the next stable release.
- **For a version already released**: edit its copy in `docs/website/versioned_docs/version-<line>`, then
  merge, or dispatch `Documentation`. Make the same change in `docs/website/docs` if it still applies: the
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
