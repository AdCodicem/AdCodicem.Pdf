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

## Versioning

Strict SemVer. `VersionPrefix` lives in `Directory.Build.props`; builds outside a tag carry the `alpha`
suffix, and the release workflow takes the version from the tag itself.

```bash
git tag v0.1.0 && git push origin v0.1.0
```

While the major version is 0 the API may move between minor versions, but every break is recorded in
`docs/decisions.md` and in the release notes.

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

1. Make sure `main` is green and `docs/status.md` reflects reality.
2. Set `VersionPrefix` in `Directory.Build.props` to the version being released.
3. Tag `vX.Y.Z` and push the tag.
4. The workflow builds, runs the whole suite, packs, exchanges the OIDC token and pushes.
5. Check the package on nuget.org, then reserve the ID prefix if this was the first publish.

If the push fails with an authorisation error, the mismatch is almost always between the policy and the
workflow: the file name, the environment, or the account name in `NUGET_USER`.
