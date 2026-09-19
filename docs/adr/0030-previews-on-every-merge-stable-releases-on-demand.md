# 30. Previews on every merge, stable releases on demand

Date: 2026-09-15

## Status

Accepted

## Context

Until now, merging a pull request into `main` released: semantic-release worked out a version from the
commits, tagged it, wrote the changelog, opened a GitHub Release and published to nuget.org. That makes
every merge a public event. Two consequences follow, and both are unwanted for a library at this stage.

A release becomes something to be careful about rather than something to decide. The pressure lands on the
merge — a `fix:` merged on a Friday evening is a version on nuget.org, and nuget.org forgets nothing: a
published version can be unlisted, never withdrawn. The natural defence is to batch work into long-lived
branches, which is precisely what trunk-based development exists to avoid.

At the same time, work that is finished and merged should be usable. Asking people to build from source
to try a fix that is already on `main`, or waiting for the next release before anyone can confirm it,
wastes the feedback that an early-stage library needs most.

Two audiences, two different questions: *what can I try today* and *what can I depend on*. One event was
being made to answer both.

A constraint shapes the answer. A nuget.org trusted-publishing policy is pinned to a **workflow file
name**, so every additional publishing workflow file is another policy for someone to register and keep
in step.

## Decision

We will separate publishing from releasing, in one workflow file.

- **A push to `main` publishes a preview.** It builds, runs the whole suite, and pushes a prerelease
  package numbered `<last release, patch bumped>-preview.<run number>`. Nothing is tagged, no changelog is
  written, no GitHub Release is opened, and the documentation site is not redeployed.
- **A stable release is a manual run** of the same workflow (`workflow_dispatch`, with a dry-run option).
  It alone works out the version from the commits, writes `CHANGELOG.md`, tags, opens the GitHub Release,
  publishes the stable packages, and deploys the documentation site.

*Added 2026-09-19, without reopening the above:* a manual run now chooses between the two, and defaults to
the preview. The decision left "publish a preview" reachable only by merging, which makes a merge the way
to ask for a package — the pressure this record set out to take off the merge. The stable path is the one
that has to be deliberate, so it is the one you have to select.

Both live in `release.yml` so that one trusted-publishing policy covers them.

The preview number states where a preview sits rather than predicting what it will be called. After
`v0.1.0` the previews are `0.1.1-preview.n`; if the commits contain a `feat:`, the stable release is
`0.2.0`, and `0.1.1-preview.n` still sorts between the two. Deriving the exact future number would mean
running semantic-release on every merge to ask a question only the release needs answered.

## Consequences

- **Merging is cheap again.** A merge publishes something installable and nothing irreversible. The
  decision to release is taken deliberately, by a person, at a moment they chose.
- **The documented site matches the released package.** It is deployed by the release, not by the tip of
  `main`, so what is described online is what `dotnet add package` gives you. The cost is that a
  documentation fix does not appear until the next release — `docs.yml` keeps its own manual trigger for
  when that will not do.
- **nuget.org accumulates preview versions**, one per merge, permanently. That is the price of previews on
  a public feed, and the run number keeps them ordered and collision-free. A separate feed (GitHub
  Packages) would keep nuget.org clean at the cost of a second policy, a second set of credentials and a
  `nuget.config` for consumers; if the preview count ever becomes a nuisance, that is the alternative to
  reach for.
- **A release can now be forgotten.** Nothing publishes a stable version unless someone asks for one. The
  changelog and the tags are the only record that says so, which is why the release run writes both.
- **Renaming `release.yml` still breaks publishing**, and now breaks both paths at once — the policy is
  pinned to that file name (ADR [25](0025-trusted-publishing-rather-than-an-api-key.md)).
