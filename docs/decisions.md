# Architecture decisions

A settled decision is not reopened without new evidence. This file exists so the same debate is not had
twice: it records the choice, the reason, and what was rejected.

Format: decision — reason — rejected alternatives — what would reopen it.

---

**D01 — Fully managed rendering.** AngleSharp for HTML5 parsing; the CSS engine, the layout engine and the
PDF writer are ours.
*Reason*: it is the only option compatible with the frugality goal. A Chromium instance costs 150 to 300 MB
and hundreds of milliseconds to start; a managed engine renders an invoice in a few megabytes and a few
milliseconds, and deploys into a distroless container.
*Rejected*: wrapping Chromium or Playwright (footprint and deployment complexity), binding a native engine
such as PDFium (per-RID dependencies, incompatible with AOT).
*Would reopen it*: a demonstrated need to render arbitrary web pages with JavaScript — which would be met
by adding a satellite backend, not by changing the core.

**D02 — Full scope: generation and manipulation.** Decided after an initial framing of "generation only".
*Consequence*: manipulation requires a complete parser and a read/write object model. That is not a module
bolted onto the writer, it is the lower half of the library, and both paths share one object model.

**D03 — Business documents with a modern CSS subset.** The full box model, tables, paged media, flexbox,
simple grid, inline SVG. No JavaScript.
*Reason*: it covers invoices, reports, contracts and labels — the whole of the intended use — for a
fraction of the cost of a complete web engine.

**D04 — SkiaSharp and HarfBuzzSharp allowed, in `.Html` only.** HarfBuzz for shaping (ligatures, kerning,
complex scripts, bidirectional text), Skia for image decoding and SVG fallback rendering.
*Reason*: writing a quality shaper takes years, and the result would be typographically worse. Confining
these to the HTML package keeps the core dependency-free.
*Rejected*: everything managed (disproportionate cost), Skia everywhere (contaminates the core).

**D05 — Our own PDF writer rather than Skia's PDF backend.**
*Reason*: Skia produces an opaque PDF — no PDF/A, no tagged structure, no control over compression, no
streaming output. Conformance and streaming are the heart of the promise.
*Rejected*: `SKDocument.CreatePdf` (quick to ship, a dead end afterwards).

**D06 — Conformance designed in from the start.** Headers and footers, numbering, links, bookmarks;
PDF/A-3 and Factur-X; tagged PDF/UA.
*Reason*: the DOM → box → marked-content traceability that PDF/UA demands cannot be retrofitted without
rewriting layout. It is therefore present from the first milestone, even though full emission of the
structure tree comes later.

**D07 — Facade, immutable options, dependency injection.** A thread-safe `IPdfRenderer` singleton, options
as records, `AddAdCodicemPdf()`.
*Rejected*: a fluent builder as the primary API — it can be layered on top later; the reverse is not true.

**D08 — A dependency-free core plus satellites.** See the package table in `architecture.md`.
*Reason*: a consumer who only manipulates PDFs should not pull in AngleSharp or a native binary.

**D09 — `net10.0` only, C# 14.**
*Reason*: unhindered access to recent performance APIs, and no conditional compilation.
*Rejected*: multi-targeting `net8.0` (a double matrix, performance APIs unavailable). To be reconsidered
only if a real consumer is stuck on LTS.

**D10 — Fonts: an explicit registry, an embedded OFL set, optional web fonts.**
*Reason*: a container has no fonts installed, and depending on system fonts makes rendering irreproducible.
The embedded set guarantees that the first attempt works. Fetching remote `@font-face` resources is
possible but **off by default**: a network call during rendering is neither deterministic nor safe.

**D11 — GitHub Actions, published to nuget.org.** The repository is on GitHub, and its CI can be inspected
from a development session, which is not true of Azure Pipelines. Publication uses trusted publishing, not
an API key — see D24.

**D12 — Lazy reading; output as a full rewrite or an incremental update.**
*Reason*: it is what allows a document of several hundred megabytes to be manipulated in a few megabytes of
memory, and an existing signature not to be invalidated.
*Rejected*: loading everything into memory (what most .NET libraries do, and contrary to the goal); a pure
streaming pipeline (rules out reordering, global deduplication and forms).

**D13 — A tolerant reader with a diagnostic report.** Cross-reference rebuilding, recovery from malformed
objects, and a structured report of anomalies and repairs.
*Reason*: real PDFs are frequently non-conforming, and a strict reader fails where every reader on the
market succeeds. The report also lets a caller track the quality of incoming files.

**D14 — Full text extraction, tagged structure preferred.** Positioned glyphs, grouping into lines and
paragraphs, table detection, and, when the document is tagged, following its structure tree rather than
heuristics.
*Note*: table detection is heuristic by nature; the API must expose a confidence score rather than imply an
exact result.

**D15 — Rasterisation as a satellite, after the foundations.** It will reuse the content stream interpreter
written for extraction (M10). Until then, visual tests rely on an external tool in CI.

**D16 — Conformance actively preserved, plus a built-in validator.** When merging, structure trees,
`OutputIntents`, metadata and fonts are genuinely recombined.
*Accepted caveat*: a complete PDF/A validator is hundreds of rules. It ships in stages, covering first what
the library produces itself, then third-party documents.

**D17 — Signing: space reserved.** The writer must be able to produce incremental updates and to leave an
existing signature intact. PAdES follows, behind an `IPdfSigner` abstraction so signing can be delegated to
an HSM or a qualified provider.

**D18 — Milestones are accepted on real documents.** Every milestone states acceptance conditions verified
against `tests/corpus`, a set of documents produced by real generators and contributed from the field,
rather than only on files we wrote by hand.
*Reason*: hand-built test files prove the code handles what we imagined. Producers emit what they emit.
*Consequence*: the corpus and its manifest are part of the library's contract; see `docs/corpus.md`.

**D19 — Everything is written in English.** Code, public API, XML documentation, project documentation,
commit messages, diagnostics and exception messages.
*Reason*: the package is public, and a mixed-language repository forces every contributor to switch
languages between a file and its documentation.

**D20 — Validation is a rule engine, and conformance is a profile of it.** Validation lands immediately
after reading (M2), as the structural profile of a rule engine whose findings carry stable identifiers.
PDF/A and PDF/UA arrive later (M12) as further profiles, not as a separate validator.
*Reason*: repair, conformance and every later guarantee are expressed in terms of findings. Two validators
with two vocabularies would mean two answers to "is this document sound?".
*Consequence*: rule identifiers are public API from the day they ship, and `docs/validation-rules.md`
documents them.

**D21 — Repair is driven by findings, and conservative by default.** Every change is justified by a
validation finding, recorded in a report, and applied by preference as an incremental update that leaves
the original bytes in place.
*Reason*: repair is where a library quietly destroys data. A change nobody asked for is a corruption, and
rewriting a file wholesale invalidates signatures and any external byte-range reference.
*Rejected*: repairing as a side effect of reading (the caller must choose), and a single "fix everything"
mode with no account of what it did.

**D22 — Third-party corpus documents are vendored only under attribution-only licences.** A curated subset
of the veraPDF corpus (CC BY 4.0) is committed with a NOTICE; ShareAlike collections are not vendored, and
large corpora are fetched on demand for local investigation rather than committed.
*Reason*: test data ships inside an MIT repository, so its licence must not reach back into the software.

**D23 — Package identifiers, and a reserved prefix.** `AdCodicem.Pdf` for the core, then
`.Validation`, `.Html`, `.AspNetCore`, `.FacturX`, `.Rendering` and `.Signing`. All seven were unclaimed
when checked on 2026-09-13.
*Consequence*: the `AdCodicem.` prefix is to be reserved on nuget.org with the first publish, so nobody
else can publish under the name and consumers see a verified owner.

**D24 — Trusted publishing rather than an API key.** The release workflow asks GitHub for a short-lived
OIDC token, which nuget.org exchanges for a key valid one hour and usable once.
*Reason*: there is then no long-lived credential to leak, rotate or store — the failure mode of every
API-key setup. The only stored value is the nuget.org account name, which is not a credential.
*Consequence*: the policy on nuget.org is pinned to the repository, the workflow file name and the
`nuget` environment, so renaming `release.yml` or the environment breaks publishing until the policy is
updated. `docs/releasing.md` records the exact fields.

---

## Minor but durable technical decisions

- The layout engine works in **CSS pixels**; conversion to points (`× 0.75`) happens only when painting.
- The PDF coordinate system starts bottom-left and layout works top-left: the conversion lives in exactly
  one place, in painting.
- PDF names are interned; common integers are cached.
- A stream copied between documents travels **encoded**, with no decompress/recompress cycle.
- A dictionary entry whose value is null is dropped on parse: the specification says it is equivalent to an
  absent entry, and every later stage is spared a null it would have to ignore.
- The document `/ID` is derived from content, or supplied by the caller, never random — determinism comes
  first, and a random identifier would make fingerprint tests impossible.
