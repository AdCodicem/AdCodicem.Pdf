# 17. Conformance actively preserved, plus a built-in validator

Date: 2026-09-12

## Status

Accepted

## Context

Every operation the library offers on a document — merging, stamping, annotating, filling, splitting — can
break a PDF/A or PDF/UA claim the input made, and a claim that is no longer true is worse than none: archives,
portals and accessibility checkers trust it. A merge that concatenates its parts loses what made each conform:
its structure tree, its output intent, its metadata and extension schemas, the fonts its pages need.

## Decision

We will preserve conformance actively: when merging, structure trees, `OutputIntents`, metadata and fonts are
genuinely recombined, so that the output conforms to what its parts claimed; an operation that cannot keep a
claim refuses, or removes the claim and reports the loss, and never leaves one the library has reason to doubt
(invariant 7). And we will ship a PDF/A and PDF/UA validator with the library, so that a claim — ours or a
third party's — can be checked, not only carried.

## Consequences

- **Accepted caveat** — a complete PDF/A validator is hundreds of rules. It ships in stages, covering first what
the library produces itself, then third-party documents.

---

Recorded as `D16` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
