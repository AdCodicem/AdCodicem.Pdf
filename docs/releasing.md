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

The first packages shipped on **2026-09-19**: `AdCodicem.Pdf` `0.1.1-preview.10` through
`0.1.1-preview.13`, previews from `main`. The other six identifiers are still unclaimed, and ship with the
milestones above.

**Reserve the `AdCodicem.*` prefix** on nuget.org — now due, since packages exist under it. It stops anyone
else publishing under the name, and marks the packages as coming from a verified owner — on nuget.org and
in Visual Studio.

There is no button for it. The [procedure](https://learn.microsoft.com/nuget/nuget-org/id-prefix-reservation)
is an email to **account@nuget.org** giving the owner's **display name** on nuget.org — the same account
that owns the trusted-publishing policy — and the prefix requested. Send it from the address registered on
that account; the team may ask identifying questions before accepting. The prefix is requested *private*
(the default): a *public* prefix keeps the verified mark but lets anyone publish under it.

What nuget.org weighs: that the prefix clearly identifies its owner, is not a common word and is at least
four characters, and that packages under it carry consistent identifying metadata and a licence declared
with the `license` element rather than `licenseUrl`. `Directory.Build.props` already gives every package
`Authors` = `AdCodicem` and `PackageLicenseExpression` = `MIT`; there is no icon, so the rule on embedded
icons does not apply. Packages the owner already published under the prefix get the mark retroactively.
What waiting risks is someone else publishing under the name first: a reservation leaves other owners'
existing packages in place.

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
- The policy is tied to its owner. If it belongs to an organisation and the person who created it leaves,
  it goes inactive until they are added back.

### One-time setup on GitHub

Create the `nuget` environment (Settings → Environments). The workflow declares `environment: nuget`, and
the policy above names the same environment, so the two must agree — that is all the environment is for.

**No secret is involved.** `NuGet/login` requires a `user`, because OIDC proves the run is authorised
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

A push to `main` builds, runs the whole suite, and publishes a **prerelease** package. Nothing is tagged,
no changelog is written, no GitHub Release is opened, the site is not redeployed. Merging stays cheap, and
what is on `main` is always installable:

```bash
dotnet add package AdCodicem.Pdf --prerelease
```

A preview can also be asked for without merging: **Actions → Release → Run workflow**, leaving **What to
publish** on `preview`. It packs whichever ref you pick, so a branch can be tried on a real feed before it
lands — at the price of a version on nuget.org that matches no commit on `main`, permanently. Prefer the
merge unless there is a reason not to.

Do not re-run a past run to get a fresh preview: a re-run keeps its run number, so it republishes the same
version, which `--skip-duplicate` accepts and ignores. A new run is what produces a new number.

A preview is numbered `<last release, patch bumped>-preview.<run number>` — after `v0.1.0`, the previews
are `0.1.1-preview.12`, `0.1.1-preview.13`, and so on. That number says **where the preview sits**, not
what the next release will be called: if the commits since the tag contain a `feat:`, the stable release
will be `0.2.0`, and every `0.1.1-preview.n` still sorts correctly between `0.1.0` and `0.2.0`. The run
number only ever increases, so previews never collide, and nothing has to be deleted from nuget.org —
which is just as well, because nothing can be.

### The stable release is a decision, and it is taken by hand

**Actions → Release → Run workflow**, setting **What to publish** to `stable`. The dropdown defaults to
`preview`, deliberately: the stable path tags, writes to `main` and cannot be taken back, so it is chosen
rather than reached by clicking through. That run, and only that run:

1. works the version out from the commits since the last tag;
2. writes `CHANGELOG.md`, commits it, and tags `vX.Y.Z`;
3. packs and pushes the stable packages to nuget.org;
4. opens the GitHub Release with the generated notes;
5. deploys the documentation site, so what is online is what is released.

Tick **dry run** to see the version and the notes it would produce and stop there: nothing is published,
tagged, or deployed, and no publishing key is even requested. It applies to the stable path only — a
preview has no version to work out and nothing to undo but the publish itself.

If the run reports no release, read the commits: `docs:`, `chore:`, `test:`, `refactor:` and `build:`
deliberately release nothing. If the push fails with an authorisation error, the mismatch is almost always
between the policy and the workflow: the file name, the environment, or the account name in `NUGET_ACCOUNT`.

Two things the stable run needs on `main`: permission to push the changelog commit and the tag. If branch
protection is turned on, either allow the `github-actions` actor to bypass it, or accept that the release
cannot record itself.

The first publish has happened; reserving the ID prefix is what is left, and it is tracked as T20.

## Also configured by hand, once

| What | Where | Needed for |
|---|---|---|
| Codecov | The repository is linked on codecov.io — **done**; it reports 82.61%. The Codecov **GitHub App** is **not** installed, and Codecov warns on each pull request that without it uploads and comments are not reliably processed (T14) | Coverage upload in CI. A public repository uploads without a token; the `CODECOV_TOKEN` secret is read if one exists, and becomes necessary only if the repository goes private or Codecov stops accepting tokenless uploads |
| "Allow auto-merge" | Settings → General | Dependabot auto-merge |
| Branch protection on `main` with CI as a required check | Settings → Branches | Auto-merge cannot merge a red build |
| Discussions, Sponsors | Settings → Features, and the GitHub account | The discussion template and `FUNDING.yml` |

## The documentation site

`docs/website` is a Docusaurus site publishing both the user-facing documentation and the project documents in
`docs/`. CI builds it on every push, so a document that does not build never reaches the default branch;
`.github/workflows/docs.yml` deploys it to GitHub Pages **with the stable release**, so the site describes
the version people can install rather than the tip of `main`. It also keeps its own **Run workflow**
button, for a documentation fix that should not wait for the next release.

One manual step, once: **Settings → Pages → Source: GitHub Actions**. Until then the deployment job fails
with a permissions error, and the site simply is not published — nothing else breaks.

The site lands at `https://adcodicem.github.io/AdCodicem.Pdf/`. If the repository is ever renamed, the
`baseUrl` in `docs/website/docusaurus.config.js` has to follow.

Updating the site is part of the definition of done for every milestone, not a separate chore — see
`docs/roadmap.md`.
