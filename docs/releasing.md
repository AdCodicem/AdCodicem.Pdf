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

Versions are not chosen; they are derived. semantic-release reads the commits since the last tag and
decides: `fix:` bumps the patch, `feat:` the minor, a `!` or a `BREAKING CHANGE:` footer the major.
Nothing releasable in the commits means no release at all, which is the correct outcome for a branch of
documentation changes.

This is why the conventional-commit check on pull requests is not a style rule. A malformed message does
not look untidy — it produces no release, silently.

**All seven packages share one version** and are published together, even when only one changed. They
are tightly coupled — the satellites exist to extend the core — and one number keeps the API-compatibility
baseline unambiguous. Independent versioning would need `multi-semantic-release` and a different workflow
shape.

### Staying below 1.0

semantic-release declares `1.0.0` for a first release when it finds no previous tag. To stay in `0.x`,
tag the starting point once, by hand, before the first automated release:

```bash
git tag v0.1.0 && git push origin v0.1.0
```

From then on it continues from that tag — `fix:` gives `0.1.1`, `feat:` gives `0.2.0` — and the move to
`1.0.0` happens when a breaking change says so, which is the right moment for it.

`VersionPrefix` in `Directory.Build.props` only matters for local builds; the release passes the computed
version explicitly.

## Publishing: trusted publishing, not API keys

Publication uses [trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing):
the workflow asks GitHub for a short-lived OIDC token, nuget.org validates it against a policy and returns
an API key valid for one hour and usable once. No long-lived secret exists to leak, rotate or store.

### One-time setup on nuget.org

Sign in, then **your username → Trusted Publishing → add a policy**:

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

Create the `nuget` environment (Settings → Environments) and add one secret to it:

| Secret | Value |
|---|---|
| `NUGET_USER` | The nuget.org **account name** — the profile name, not an email address |

That is the only secret involved, and it is not a credential: it names which account's policy to match.
Consider requiring a reviewer on the `nuget` environment so a tag cannot publish unattended.

## Releasing

There is no release procedure. Merging a pull request into `main` is the procedure:

1. The commits decide whether anything is released, and what the version is.
2. `release.yml` builds, runs the whole suite, exchanges the OIDC token for a short-lived key, then
   semantic-release packs, pushes to nuget.org, writes `CHANGELOG.md`, tags, and opens a GitHub Release.
3. Check the package on nuget.org, and reserve the ID prefix if this was the first publish.

If the push fails with an authorisation error, the mismatch is almost always between the policy and the
workflow: the file name, the environment, or the account name in `NUGET_USER`.

If nothing is published and you expected something, read the commits: `docs:`, `chore:`, `test:`,
`refactor:` and `build:` deliberately release nothing.

## Also configured by hand, once

| What | Where | Needed for |
|---|---|---|
| `CODECOV_TOKEN` secret | Link the repository on codecov.io, then repository secrets | Coverage upload in CI |
| "Allow auto-merge" | Settings → General | Dependabot auto-merge |
| Branch protection on `main` with CI as a required check | Settings → Branches | Auto-merge cannot merge a red build |
| Discussions, Sponsors | Settings → Features, and the GitHub account | The discussion template and `FUNDING.yml` |

## The documentation site

`website/` is a Docusaurus site publishing both the user-facing documentation and the project documents in
`docs/`. CI builds it on every push, so a document that does not build never reaches the default branch;
`.github/workflows/docs.yml` deploys it to GitHub Pages when `docs/` or `website/` changes on `main`.

One manual step, once: **Settings → Pages → Source: GitHub Actions**. Until then the deployment job fails
with a permissions error, and the site simply is not published — nothing else breaks.

The site lands at `https://adcodicem.github.io/AdCodicem.Pdf/`. If the repository is ever renamed, the
`baseUrl` in `website/docusaurus.config.js` has to follow.

Updating the site is part of the definition of done for every milestone, not a separate chore — see
`docs/roadmap.md`.
