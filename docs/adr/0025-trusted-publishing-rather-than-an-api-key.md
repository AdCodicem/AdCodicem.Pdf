# 25. Trusted publishing rather than an API key

Date: 2026-09-13

## Status

Accepted

## Context

There is then no long-lived credential to leak, rotate or store — the failure mode of every
API-key setup. The only stored value is the nuget.org account name, which is not a credential.

## Decision

The release workflow asks GitHub for a short-lived
OIDC token, which nuget.org exchanges for a key valid one hour and usable once.

## Consequences

- **Consequence** — the policy on nuget.org is pinned to the repository, the workflow file name and the
`nuget` environment, so renaming `release.yml` or the environment breaks publishing until the policy is
updated. `docs/releasing.md` records the exact fields.

---

Recorded as `D24` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
