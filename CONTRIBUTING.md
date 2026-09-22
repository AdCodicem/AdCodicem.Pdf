# Contributing

Thanks for considering a contribution. This project follows GitHub Flow: `main` is always releasable,
work happens on short-lived branches, and changes land through pull requests.

## Setup

```bash
git clone https://github.com/AdCodicem/AdCodicem.Pdf.git
cd AdCodicem.Pdf
dotnet restore
dotnet build
dotnet test --solution AdCodicem.Pdf.slnx
```

The integration tests run independent tools — qpdf today, more later — inside containers via
[Testcontainers](https://dotnet.testcontainers.org/). Docker must be running; where it is not, those
tests skip with the reason attached rather than failing, so a machine without a daemon still gives a
usable run.

## The most useful contribution is a document

This library reads files produced by software we cannot run: Word's print driver, Acrobat, a scanner's
firmware, a supplier's ERP. A single real document that breaks something is worth more than a patch.
[`docs/corpus-contributions.md`](docs/corpus-contributions.md) says what is wanted, what to check before
handing anything over, and where it goes — including a private corpus for documents that cannot be
published.

## Commit messages — Conventional Commits

Versions and releases are generated from commit history, so commit messages must follow
[Conventional Commits](https://www.conventionalcommits.org/):

```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

Common types: `feat` (bumps minor), `fix` (bumps patch), `docs`, `perf`, `refactor`, `test`, `build`,
`chore` (no release). A breaking change is marked with `!` after the type, or a `BREAKING CHANGE:`
footer, and bumps the major version.

```
feat(reader): resolve object streams lazily
fix: stop relocation from recursing into itself
feat!: drop the synchronous Open overloads
```

A CI check validates this on every pull request — a malformed message blocks the merge, and worse,
would silently produce no release.

Merging does not release. A merge into `main` publishes a **preview** package, so a change is installable
as soon as it lands; a stable release is a deliberate, manual run of the `Release` workflow, and that is
what writes the changelog and the tag. [`docs/releasing.md`](docs/releasing.md) has the detail.

## What a change must carry

The project's [definition of done](docs/roadmap.md) applies to contributions too:

1. **Tests.** Unit tests for the behaviour, and its degenerate and hostile cases. Anything an independent
   tool should confirm belongs in the integration suite.
2. **No new warnings.** The build treats them as errors. A rule that is genuinely wrong here is suppressed
   where it fires, with a written justification — never by a global `NoWarn`.
3. **Documentation.** Public API needs XML comments, and anything a consumer can call needs a page on the
   site under `docs/website/docs` — the preview's documentation, frozen by the next stable release.
4. **`dotnet format`** before pushing; CI verifies it.
5. **A workflow change pins its actions by commit hash**: `uses: owner/action@<40 hex> # vX.Y.Z`, never
   a tag. A tag is a moving target — whoever can write to the action's repository can repoint it at
   other code, and every run that follows picks it up silently. The trailing comment is how Dependabot
   knows which version the hash stands for; it rewrites hash and comment together when it bumps one.

## Reading the codebase

Start with [`CLAUDE.md`](CLAUDE.md) — it is the working frame, and it holds the architecture invariants
that are not negotiable, chief among them that nothing loads a whole document into memory and that every
byte read from a third-party file is treated as hostile. [`ARCHITECTURE.md`](ARCHITECTURE.md) maps the
layout; [`docs/adr/`](docs/adr/) records why things are the way they are.

## Pull requests

1. Branch from `main`, keep the change focused.
2. Make sure `dotnet test --solution AdCodicem.Pdf.slnx` is green and the build has no warnings.
3. Open the PR; its template asks for the rest.
