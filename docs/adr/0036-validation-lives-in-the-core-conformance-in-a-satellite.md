# 36. Validation lives in the core, and the conformance profiles in a satellite

Date: 2026-09-26

## Status

Accepted on 2026-09-26, by the maintainer, before M2's first public type was written. It amends
[21](0021-validation-is-a-rule-engine-and-conformance-is-a-profile-of.md), whose rule engine it places and
whose rule identifiers it gives a grammar, and [24](0024-package-identifiers-and-a-reserved-prefix.md), whose
`.Validation` satellite becomes `.Conformance`. It follows [8](0008-facade-immutable-options-dependency-injection.md)
for the validator's shape and [34](0034-every-valid-pdf-is-readable-and-the-readers-guards-are.md) for what a
guard reached means to it.

Implemented by the `AdCodicem.Pdf.Validation` namespace of the core — `PdfValidator`, `PdfValidatorOptions`,
`ValidationProfile`, `PdfValidationReport`, `PdfValidationFinding`, `PdfValidationSeverity`,
`PdfValidationLocation`, `PdfValidationRuleIds` —, `docs/validation-rules.md`, and `CorpusValidationTests`.

## Context

M2 creates the finding model, the rule engine and the structural profile, and ADR 21 makes rule identifiers
public from the day they ship. Every merge into `main` publishes a preview to nuget.org (ADR 30). Before the
first public type was written, the project's own documents disagreed on where those types live:

- `docs/roadmap.md`, `docs/releasing.md`, the site's introduction and ADR 24 put "the validation rule engine
  and its profiles" in a satellite, `AdCodicem.Pdf.Validation`, shipped in M2;
- `docs/roadmap.md` also puts `PdfRepair` (M4) **in the core**, driven by the M2 findings, and ADR 22 says
  every repair is justified by a finding;
- invariant 1 forbids the core any dependency, so a core `PdfRepair` cannot consume findings a satellite
  defines;
- `docs/architecture.md` described the satellite as the PDF/A and PDF/UA validator only.

Two readings, and only one of them is possible. The structural rules have a second reason to sit beside the
reader: from M2's second slice on they need facts the reader keeps to itself — the cross-reference index and
its entries, where `startxref` pointed, the trailer as the file wrote it before the reader merged its
sections. Those are internal, and should stay so; a satellite could reach them only through new public API
made for one consumer, or through `InternalsVisibleTo` granted to a published assembly.

The first slice raised four more questions whose answers become public API: the grammar of a rule
identifier, and how it sits beside the seventeen public codes of `PdfDiagnosticCodes` (`xref.*`,
`stream.*`, `limit.*`…); the entry point; what the validator does when the document was opened with
`PdfReaderOptions.ThrowOnLimit`, since M2 said the validator never throws; and whether callers may write
rules of their own.

## Decision

We will build validation into the core, and keep a satellite for the conformance profiles only.

- **Where.** The finding model, the rule engine and the structural profile are part of `AdCodicem.Pdf`, in
  the namespace `AdCodicem.Pdf.Validation`. M2 ships no new package. The PDF/A and PDF/UA profiles (M12) go
  to a satellite named **`AdCodicem.Pdf.Conformance`**: the identifier `AdCodicem.Pdf.Validation` was never
  published — nuget.org answered 404 for it on 2026-09-26 — and keeping it for the satellite would name an
  assembly like a namespace of the core that it does not contain.
- **Findings are the verdict; diagnostics are the reader's account.** `PdfDiagnostics` says what the reader
  did to read a file; a `PdfValidationFinding` says what is wrong with the file. They keep separate types
  and separate scales: `PdfValidationSeverity` is `Information`, `Warning`, `Error`, in that order, and the
  bar for `Error` is M2's — readers will disagree about the document. A rule may read the reader's
  diagnostics; a finding never reuses a diagnostic code.
- **Rule identifiers** are two segments, `family.name`, each in lowercase kebab case
  (`^[a-z][a-z0-9]*(-[a-z0-9]+)*\.[a-z][a-z0-9]*(-[a-z0-9]+)*$`): `file.eof-missing`,
  `page-tree.count-mismatch`. The family is the structural profile's — `file`, `xref`, `object`,
  `page-tree`, `stream`, `font`, `resource`, `annotation`, `metadata`, `security` — and never a profile's
  name, since a rule runs in every profile that includes it; M12's rules take families of their own. One
  identifier names one rule, with one severity. No identifier equals a reader diagnostic code; a test holds
  both rules, and `docs/validation-rules.md` lists every identifier with its severity, its meaning, and the
  diagnostic codes a rule reads, if any.
- **The entry point** is an instance: `new PdfValidator(PdfValidatorOptions)`, whose immutable options carry
  the profile — `ValidationProfile.Structural` by default — and the report's capacity. It holds no state,
  so one validator serves any number of threads and registers as a singleton; `Validate(PdfDocument)`
  validates a document the caller opened, with the reader options the caller chose, and leaves it open.
  Raising a reader limit therefore lets validation check more. Validation reads through the document like
  any other caller: what it resolves joins the document's cache, and what the reader notices on the way
  joins its diagnostics.
- **What it throws.** The validator reports; it throws only for a caller's error — a null or disposed
  document — and for the `PdfLimitExceededException` a caller asked for by opening the document with
  `ThrowOnLimit`, which it lets through as invariant 5 says. With `ThrowOnLimit` off, a rule meeting an
  object the reader cut at a limit reports at most, as information, that it was not checked whole.
- **The report is bounded.** It keeps at most `PdfValidatorOptions.FindingCapacity` findings, 1,000 by
  default, and counts every finding by severity whether kept or not, with `SuppressedCount` for those it did
  not keep: a hostile file with a fault in every object cannot make it grow without bound (invariant 4).
- **Deterministic.** Rules run in the profile's order and report in the order they find; the report carries
  no time, no path and nothing the document did not decide (invariant 6).
- **The engine stays internal** until it is deliberately made public: `IValidationRule`,
  `ValidationContext` and the construction of a profile are internal, so callers use the built-in profiles
  and cannot yet write rules. M12's satellite is the first consumer that needs them public, and decides
  their shape then.
- **A profile has a name and a version.** The structural profile is `structural`, version 1. A stable release
  that changes what the profile reports increments it; previews do not (ADR 30).

## Consequences

- `PdfRepair` (M4) can take findings as its input, as ADR 22 wants, without the core depending on anything.
- The structural rules read the reader's internals directly, and the public API grows only by what callers
  use. The price is that the core carries the rule engine and the structural profile — code a caller who
  never validates still ships. Both are managed, dependency-free and trimmed away when unused.
- Callers filter findings by rule identifier and diagnostics by code, in two collections with two
  vocabularies that never collide; a family may share its first segment with a diagnostic prefix (`xref.`,
  `stream.`), which is harmless since the two are never mixed.
- M2 creates no package, so T16 — the API baseline shared by every package — is no longer due in M2. It is
  due before the first satellite ships after a stable release, when a satellite's first pack would ask
  nuget.org for a baseline version it never had.
- A caller who opens with `ThrowOnLimit` must expect it from `Validate` as from any other read; M2's "the
  validator never throws on a document the reader could open" now carries that exception.
- Nobody can yet add a rule without changing the library; that is deliberate while the context rules see
  still changes with every slice.
- **Rejected** — the engine in a satellite: `PdfRepair` would have to leave the core, and the rules would
  need the reader's internals made public or granted to a published assembly.
- **Rejected** — keeping the satellite named `AdCodicem.Pdf.Validation`: two assemblies would offer the same
  namespace name, and a reader of `using AdCodicem.Pdf.Validation;` could not tell which package it meant.
- **Rejected** — a profile prefix (`structure.file.eof-missing`): a structural rule run within PDF/A would
  carry the wrong profile's name.
- **Rejected** — opaque codes in the style of .NET analysers (`PDFS0001`): stable, but unreadable in a
  filter or a report, and the family is what callers filter on.
- **Rejected** — catching `PdfLimitExceededException` and reporting it: it would override what the caller
  explicitly asked for, which invariant 5 does not allow.
- **Rejected** — a public rule API from the first slice: `ValidationContext` changes with each slice, and
  would be frozen before it had a second consumer.
- **Rejected** — extending `PdfDiagnosticSeverity`: `Repair` and `ConformanceLoss` describe what the reader
  or a transformation did, not how wrong a file is.
- **Amended on acceptance**: ADR 21 and 24 (a note each), `docs/architecture.md`, `docs/roadmap.md`,
  `docs/releasing.md`, the site's introduction, `README.md`, `docs/milestones/M2.md`, and T16 in
  `docs/status.md`.
- **What would reopen it**: the structural profile growing heavy enough that trimming does not remove it
  from applications that never validate, measured; or M12 showing the conformance profiles need the
  reader's internals as much as the structural one does.
