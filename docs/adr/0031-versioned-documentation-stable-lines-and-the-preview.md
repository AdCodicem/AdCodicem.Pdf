# 31. Versioned documentation: stable lines, and the preview beside them

Date: 2026-09-22

## Status

Accepted. Supersedes the documentation-site part of [30](0030-previews-on-every-merge-stable-releases-on-demand.md).

## Context

ADR 30 deployed the site with the stable release only, so that what is documented online is what
`dotnet add package` installs. That settled one question by giving up two others.

Someone trying a preview has no documentation for it. Previews are published on every merge precisely so
that finished work can be used before it is released, and the site describes the release instead: a new
API reaches nuget.org weeks before its page reaches anyone.

Someone who depends on an older release has no documentation for it either. The site holds one version,
the latest; the day a release changes the API, the pages for the one before it are gone.

And no release has been cut yet, so the site describes nothing installable without `--prerelease`. The
first publication had to be dispatched by hand, at the tip of `main`, which is the opposite of what
ADR 30 intended.

## Decision

We will version the user documentation, and deploy the site on every preview as well as on every stable
release.

- **A stable line is frozen by the release that opens it.** Below 1.0 a line is a minor version (`0.3`),
  since every minor may break the API; from 1.0 on it is a major (`1`, `2`). The release copies the user
  documentation and the generated API reference into `versioned_docs/version-<line>`, in the release
  commit. A later release on the same line — a patch below 1.0, a minor or a patch from 1.0 on — replaces
  that copy rather than adding one.
- **The selector lists the stable lines**, each labelled with its latest release, the newest served at the
  root of the site. All of them are kept.
- **The preview is the working tree**, served under `/preview` and reached by its own navbar button, never
  from the selector. Its banner names the preview package it documents. It is published only while a
  preview is newer than the latest stable release: right after a release there is none, and the button
  disappears until the next merge. Before the first stable release the preview is the site, at the root,
  under a banner that says so.
- **The project documents are not versioned.** The roadmap, the status and the decisions describe the
  project, not a release, and always come from `main`.
- **Every preview published from `main` redeploys the site**, and so does every stable release — from the
  release commit, which carries the new frozen copy.

Docusaurus's own versioning does the freezing: one build serves every version, and a frozen version is a
directory that can still be corrected by hand.

## Consequences

- **What is released is still what the default page describes.** The root is the latest stable line, and
  a reader has to choose the preview to see `main`.
- **A preview's documentation is online as soon as the preview is.** A merge now redeploys the site; the
  cost is one Pages deployment per merge, and a deployment of the preview section that can disagree with
  the stable one for as long as `main` is ahead.
- **Frozen documentation is committed.** Each line adds its pages and its generated API reference to the
  repository, and the release commit carries them. With one line per minor below 1.0 and one per major
  after, that stays small; pruning a line is deleting its directory and its entry in `versions.json`.
- **A frozen line can be corrected**, by editing its copy in `versioned_docs/`. The next release on that
  line overwrites the correction, which is right: it re-freezes from `main`, where the fix should be too.
- **Rejected: building each release's site separately** and adding it to a `gh-pages` branch. It serves
  exactly the bytes of each release, but a published version could never be corrected, Pages would have
  to deploy from a branch rather than from Actions, and the version selector would have to be written.
- **Rejected: the preview as an entry of the selector.** It is one mechanism fewer, but the selector is
  where a reader goes to find the version they installed, and a preview is not one.
