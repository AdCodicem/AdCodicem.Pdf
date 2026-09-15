# Releasing

## Package identifiers

Confirmed, and all seven were unclaimed on nuget.org when checked on 2026-09-13:

| Package | Contents | Ships from |
|---|---|---|
| `AdCodicem.Pdf` | Object model, reader, writer, pages, fonts, logical structure | M1 |
| `AdCodicem.Pdf.Validation` | The validation rule engine and its profiles | M2 |
| `AdCodicem.Pdf.Html` | HTML parsing, CSS engine, layout, painting | M7 |
| `AdCodicem.Pdf.AspNetCore` | Dependency injection and `IResult` integration | M7.6 |
| `AdCodicem.Pdf.FacturX` | Factur-X and ZUGFeRD | M12 |
| `AdCodicem.Pdf.Rendering` | Rasterisation | M14 |
| `AdCodicem.Pdf.Signing` | PAdES | M14 |

**Reserve the `AdCodicem.` prefix** on nuget.org once the first package is published
(Account → Reserve ID prefix, or by contacting support). It stops anyone else publishing under the name
and marks the packages as coming from a verified owner.

## Versioning — computed from the commits

Versions are not chosen; they are derived. The *moment* of a release is chosen — see **Releasing** below.
semantic-release reads the commits since the last tag and decides: `fix:` bumps the patch, `feat:` the
minor, a `!` or a `BREAKING CHANGE:` footer the major. Nothing releasable in the commits means no release
at all, which is the correct outcome for a run over documentation changes.

This is why the conventional-commit check on pull requests is not a style rule. A malformed message does
not look untidy — it produces no release, silently.

**All seven packages share one version** and are published together, even when only one changed. They
are tightly coupled — the satellites exist to extend the core — and one number keeps the API-compatibility
baseline unambiguous. Independent versioning would need `multi-semantic-release` and a different workflow
shape.

### Staying below 1.0

semantic-release declares `1.0.0` for a first release when it finds no previous tag. To stay in `0.x`,
the starting point is tagged once, by hand — **done on 2026-09-15**, `v0.1.0` on `2808d2f`:

```bash
git tag v0.1.0 && git push origin v0.1.0
```

From then on it continues from that tag — `fix:` gives `0.1.1`, `feat:` gives `0.2.0` — and the move to
`1.0.0` happens when a breaking change says so, which is the right moment for it.

That tag has **no package behind it**: nothing was ever published as `0.1.0`. It matters because package
validation compares the public API with the last release, downloads that baseline from nuget.org, and fails
the build with `NU1101` when nuget.org does not have it. Both release paths therefore ask
`.github/scripts/published-baseline.sh` for the baseline instead of trusting the tag: it returns the last
release's version only if nuget.org has a package for it, returns nothing when nothing has been published
yet, and fails the run when nuget.org cannot be asked — a compatibility check that quietly switches itself
off on a network error is not a check.

`VersionPrefix` in `Directory.Build.props` only matters for a build nobody handed a version to — a local
`dotnet pack` gives `0.1.0-alpha`, and the `-alpha` is there to make an accidentally published local build
obvious. Both release paths pass `-p:Version` explicitly, which overrides prefix and suffix alike.

## Publishing: trusted publishing, not API keys

Publication uses [trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing):
the workflow asks GitHub for a short-lived OIDC token, nuget.org validates it against a policy and returns
an API key valid for one hour and usable once. No long-lived secret exists to leak, rotate or store.

### One-time setup on nuget.org

**Done** (2026-09-15) — not yet proven by a publish: the first run that exchanges a token is what confirms
the fields below match. Sign in, then **your username → Trusted Publishing → add a policy**:

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
- The policy is tied to its owner. If it belongs to an organisation and the person who created it leaves,
  it goes inactive until they are added back.

### One-time setup on GitHub

**Not done yet** (2026-09-15): the `nuget` environment exists — the first release run created it — but holds
no secret, so the release stops at its guard before asking nuget.org for anything. Add one secret to it
(Settings → Environments → `nuget`):

| Secret | Value |
|---|---|
| `NUGET_USER` | The nuget.org **account name** — the profile name, not an email address |

That is the only secret involved, and it is not a credential: it names which account's policy to match.
Consider requiring a reviewer on the `nuget` environment so a tag cannot publish unattended.

## Releasing

Two paths out of the repository, both in `release.yml` — one file, because a trusted-publishing policy is
tied to a workflow file name and one policy is enough for both.

### Every merge publishes a preview

A push to `main` builds, runs the whole suite, and publishes a **prerelease** package. Nothing is tagged,
no changelog is written, no GitHub Release is opened, the site is not redeployed. Merging stays cheap, and
what is on `main` is always installable:

```bash
dotnet add package AdCodicem.Pdf --prerelease
```

A preview is numbered `<last release, patch bumped>-preview.<run number>` — after `v0.1.0`, the previews
are `0.1.1-preview.12`, `0.1.1-preview.13`, and so on. That number says **where the preview sits**, not
what the next release will be called: if the commits since the tag contain a `feat:`, the stable release
will be `0.2.0`, and every `0.1.1-preview.n` still sorts correctly between `0.1.0` and `0.2.0`. The run
number only ever increases, so previews never collide, and nothing has to be deleted from nuget.org —
which is just as well, because nothing can be.

### The stable release is a decision, and it is taken by hand

**Actions → Release → Run workflow.** That run, and only that run:

1. works the version out from the commits since the last tag;
2. writes `CHANGELOG.md`, commits it, and tags `vX.Y.Z`;
3. packs and pushes the stable packages to nuget.org;
4. opens the GitHub Release with the generated notes;
5. deploys the documentation site, so what is online is what is released.

Tick **dry run** to see the version and the notes it would produce and stop there: nothing is published,
tagged, or deployed, and no publishing key is even requested.

If the run reports no release, read the commits: `docs:`, `chore:`, `test:`, `refactor:` and `build:`
deliberately release nothing. If the push fails with an authorisation error, the mismatch is almost always
between the policy and the workflow: the file name, the environment, or the account name in `NUGET_USER`.

Two things the stable run needs on `main`: permission to push the changelog commit and the tag. If branch
protection is turned on, either allow the `github-actions` actor to bypass it, or accept that the release
cannot record itself.

After the first publish, check the package on nuget.org and reserve the ID prefix.

## Also configured by hand, once

| What | Where | Needed for |
|---|---|---|
| The Codecov GitHub App | Installed on the repository — **done**; it links the repository by itself | Coverage upload in CI. A public repository uploads without a token; a `CODECOV_TOKEN` repository secret is read if one exists, and becomes necessary only if the repository goes private or Codecov stops accepting tokenless uploads |
| "Allow auto-merge" | Settings → General | Dependabot auto-merge |
| Branch protection on `main` with CI as a required check | Settings → Branches | Auto-merge cannot merge a red build |
| Discussions, Sponsors | Settings → Features, and the GitHub account | The discussion template and `FUNDING.yml` |

## The documentation site

`docs/website` is a Docusaurus site publishing both the user-facing documentation and the project documents in
`docs/`. CI builds it on every push, so a document that does not build never reaches the default branch;
`.github/workflows/docs.yml` deploys it to GitHub Pages **with the stable release**, so the site describes
the version people can install rather than the tip of `main`. It also keeps its own **Run workflow**
button, for a documentation fix that should not wait for the next release.

One manual step, once: **Settings → Pages → Source: GitHub Actions** — **done**; the `github-pages`
environment allows deployments from `main` only. Before it was done, the deployment job failed with a
permissions error and the site simply was not published — nothing else broke.

The site lands at `https://adcodicem.github.io/AdCodicem.Pdf/`. If the repository is ever renamed, the
`baseUrl` in `docs/website/docusaurus.config.js` has to follow.

Updating the site is part of the definition of done for every milestone, not a separate chore — see
`docs/roadmap.md`.
