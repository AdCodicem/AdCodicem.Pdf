# 48. One version for every package, independent of .NET

Date: 2026-10-03

## Status

Accepted on 2026-10-03, by the maintainer, each choice below put to them and settled (#227). It records what
`docs/releasing.md` already said about one version for every package, and settles what a breaking change releases
while the major is 0. It holds [10](0010-net100-only-c-14.md): one target framework.

## Context

`docs/releasing.md` says that every package shares one version and is published with the others, but no decision
recorded it, nor what that version follows. Fourteen packages are planned (ADR [24](0024-package-identifiers-and-a-reserved-prefix.md)),
and some of them sit on frameworks that ship a major every November: `AdCodicem.Pdf.AspNetCore` on ASP.NET Core,
`AdCodicem.Pdf.Html` on SkiaSharp and HarfBuzzSharp. One package ships today, `AdCodicem.Pdf`, and its nuspec declares
no dependency at all (invariant 1).

The version is computed by semantic-release from the Conventional Commits since the last stable tag. Measured on
2026-10-03, with the plugins semantic-release 24 installs, the configuration in place did not do what
`CONTRIBUTING.md` promised:

- `.releaserc.json` named no preset, so the commit analyzer used `angular`, whose header pattern has no `!`. On
  `feat!: drop x` and `feat(api)!: drop x` the analyzer answers `null`: a breaking change marked the way
  `CONTRIBUTING.md` shows, and commitlint accepts, released nothing. Only a `BREAKING CHANGE:` footer was seen.
- That footer released a major, so the first breaking change would have taken the packages from `0.x` to `1.0.0`.
  ADR [31](0031-versioned-documentation-stable-lines-and-the-preview.md) assumes the opposite: below 1.0 a line is a
  minor, "since every minor may break the API".
- semantic-release ran through `npx --yes semantic-release@24 …`, at whatever versions those ranges resolved to on
  the day, in the job that holds the publishing key.

What the next .NET major does to the package was measured too, on .NET 11 RC1 (11.0.100-rc.1.26425.128, published
2026-09-08): the unit suite built for `net10.0`, rolled forward onto the .NET 11 runtime, passes 3,100 of its 3,103
tests with the same three skipped as on .NET 10. A `net11.0` application that installs the packed package, trimmed
with warnings as errors, publishes with no ILLink warning, and opens the 168 committed corpus files exactly as the
same application does on .NET 10: 150 opened, 18 refused.

## Decision

**We will release every package under one SemVer version, computed from our own commits alone, never aligned with
the major of .NET or of any framework, and support a framework's next major in the same packages.**

- **One version for all.** semantic-release computes it once; every package is packed and pushed at it, and a
  consumer references the same version of each. A package that did not change still gets the version.
- **The version says what our API did.** A framework major is never a reason to move ours, and our own breaking
  change never waits for a framework's November.
- **No package per framework major.** No `AdCodicem.Pdf.AspNetCore10` beside an `…11`: support is stated, not
  encoded in package identifiers.
- **One target framework**, as ADR [10](0010-net100-only-c-14.md) settles. A package targets `net10.0` and so
  installs on .NET 10 or any later version. A second target would reopen ADR 10, with the measurement that shows
  the code has to differ for a framework major that has a target framework of its own; nothing has so far.
- **Dependencies are floors, never ranges.** What a satellite asks of a dependency is the version
  `Directory.Packages.props` pins, with no upper bound, as Microsoft's
  [library guidance](https://learn.microsoft.com/dotnet/standard/library-guidance/dependencies#nuget-dependency-version-ranges)
  recommends: an upper bound fails a restore that pairs the package with anything newer, breaking or not. When a
  binary break means one assembly can no longer serve both majors, the floor moves to the new one. That is a
  breaking change of ours, released as one; there is no maintenance line for the old major.
- **The commit analyzer uses the `conventionalcommits` preset**, in `.releaserc.json`, so that `!` is read as
  `CONTRIBUTING.md` says it is. The release notes use the same preset.
- **A breaking change releases a minor while the major is 0.** The first rule of the analyzer's `releaseRules` is
  `{ "breaking": true, "release": "minor" }`: a `feat!`, a `fix(api)!` or a `BREAKING CHANGE:` footer releases
  `0.y+1.0`, which opens a new documentation line (ADR 31). At 1.0 the rule is **replaced** by
  `{ "breaking": true, "release": "major" }`, not deleted. 1.0 itself is a decision, not a side effect of a commit.
- **The release tooling is pinned.** A root `package.json` lists semantic-release and the plugins
  `.releaserc.json` names, and `package-lock.json` pins them and everything they pull in. Every job installs them
  with `npm ci --ignore-scripts`; no package in the lock declares an install script. Dependabot proposes their
  updates.
- **`.github/scripts/next-version.mjs`** computes the version semantic-release would give the next release without
  a token, with the commit analyzer semantic-release itself loads, at the version the lock pins, and the analyzer
  entry of `.releaserc.json` read at that commit. Over the 292 commits from `v0.1.0` to `main` on 2026-10-03, and a
  made-up history of fourteen commits covering `feat!`, `fix(api)!`, a `BREAKING CHANGE:` footer, `[skip release]`,
  a prerelease tag and a `v1.0.0`, it agrees with semantic-release's own core run in dry run against a local remote:
  release type and version, every commit.

### The compatibility island

`tests/Compat` holds the next .NET major's application: its own `global.json`, naming the release candidate's SDK,
which CI installs exactly; its own `Directory.Build.props`, `Directory.Build.targets` and `Directory.Packages.props`,
which stop the repository's from applying; and a `nuget.config` that takes `AdCodicem.Pdf*` from
`artifacts/packages` alone, at exactly the version just packed. It is in no solution. The `compat (.NET 11)` job of
`ci.yml` runs on every pull request and push, against the packages the build job packed:

- the unit suite, built for `net10.0` as a consumer's dependency is, rolled forward onto the next runtime. With both
  runtimes installed, `DOTNET_ROLL_FORWARD=Major` stays on .NET 10 and `LatestMajor` refuses a release candidate
  unless `DOTNET_ROLL_FORWARD_TO_PRERELEASE=1` (measured), so the job reads the runtime the suite reports and fails
  on any other;
- the island's application, published trimmed with the AOT analyzer and warnings as errors, which opens and
  validates every committed corpus document, decodes every stream, and holds each to its manifest entry: the
  rebuild, the catalog, the findings, the required diagnostics, cleanliness, and the refusal of an encrypted one.
  It fails on a difference, which a deliberately wrong manifest showed.

It informs and blocks nothing until .NET 11 ships, then becomes a required check. An SDK that cannot target the next
major — the one GitHub's automatic dependency submission restores every project file with — sees an empty project
whose restore succeeds; a build on it stops with the reason.

No second target framework is added ahead of need, for the reason ADR 10 gives and because the measurements above
found nothing that differs.

### Dependabot follows the same lines

- **A week of cooldown.** Every entry proposes a version only once it has been public for seven days, so that a
  broken or compromised release has time to be pulled first; security updates are not delayed.
- **No framework major is ignored, yet.** A new major of a framework a package ships is to be supported by a
  decision — a floor, a target framework — never by a bump. Today no package ships a dependency: the core's nuspec
  declares none, which `ci.yml` checks on every pull request (invariant 1). The first satellite that ships one,
  `AdCodicem.Pdf.Html` or `AdCodicem.Pdf.AspNetCore`, adds the `ignore` rules for that framework's majors. They hold
  back a security fix published only on a new major too: Dependabot's NuGet updater applies them to security
  updates.
- **No `fix(deps)` retitling, yet.** A Dependabot update of a dependency a package ships has to release a patch,
  which its `build(deps)` prefix does not. Until a package ships one, every NuGet update is a test, benchmark or
  tooling update, and `build(deps)` releasing nothing is right. The first satellite that ships a dependency brings
  the retitling with it.
- **Its subjects pass the commit check.** Dependabot writes `Bump <dependency> from <a> to <b>`, capitalized,
  which commitlint's `subject-case` refused on 18 of its first 19 pull requests (#196); `commitlint.config.mjs`
  lets exactly that subject through, so that the Conventional commits check can be required.
- **Auto-merge by allow-list.** `dependabot-auto-merge.yml` queues only what `dependabot/fetch-metadata` classifies
  as a patch or a minor; an empty classification, which it gives when it cannot read the versions, is left to a
  person (#196).

### Rejected

- **A version per package**, with `multi-semantic-release`. The satellites exist to extend the core, and one number
  keeps the API-compatibility baseline unambiguous.
- **A major aligned with the framework's**, as Entity Framework Core providers do. They are built on its internals;
  `AdCodicem.Pdf.AspNetCore` will call ASP.NET Core's public surface. Our version would stop saying anything about
  our own API.
- **A package per framework major.** The API would be the same in each, a consumer would have to pick the right one,
  and every major would add a package identifier to publish for good.
- **Upper bounds on dependencies.** They stop an application from taking the next major even where nothing breaks,
  which is most of the time.
- **Keeping `angular` and writing `BREAKING CHANGE:` footers only.** commitlint accepts `!`, `CONTRIBUTING.md`
  shows it, and a breaking change typed with it would release nothing, silently.
- **Leaving a breaking change at major below 1.0.** The first one would have shipped `1.0.0` by accident.

## Consequences

`CONTRIBUTING.md` and `docs/releasing.md` say what a breaking change releases. Release notes and the changelog
change their headings to the `conventionalcommits` preset's.

A package that did not change between two releases still gets a new version, with the same content: that is the
price of one number.

`next-version.mjs` reads `semantic-release`'s internals (where it loads its plugins, how it merges their options):
a major of semantic-release, which Dependabot proposes and auto-merge leaves for a human, is checked against it again.

The island is maintained by hand (`docs/releasing.md`): each release candidate moves its `global.json`, and the
release of .NET 11 moves it to `11.0.100`, drops the prerelease roll-forward and makes the job required. When .NET 12
previews arrive, moving the island to the next major, or adding a second one, is a decision of its own; renaming the
job changes the required check. CodeQL's default setup downloads every SDK a `global.json` names, the island's
release candidate included.

At 1.0, `.releaserc.json` has to be edited in the same change that decides 1.0: left alone, a breaking change after
1.0 would release a minor.
