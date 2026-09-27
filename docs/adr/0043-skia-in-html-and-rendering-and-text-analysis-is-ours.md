# 43. Skia in `.Html` and `.Rendering`, and text analysis is ours

Date: 2026-09-26

## Status

Accepted on 2026-09-26, by the maintainer. It amends [5](0005-skiasharp-and-harfbuzzsharp-allowed-in-html-only.md)
on two points the feature survey found wrong, and leaves the rest of it standing.

Amended on 2026-09-27, by the maintainer, while M15 was specified: one generator produces every Unicode
property table, and each is compiled into the assembly that reads it (the Decision's last bullet).

## Context

ADR 5 allows SkiaSharp and HarfBuzzSharp "in `.Html` only", and credits HarfBuzz with "bidirectional text".
Two things contradict it:

- **Rasterization.** ADR 16 and the roadmap (M25) plan Skia rasterization in `AdCodicem.Pdf.Rendering`, and
  `docs/architecture.md`'s package table already gives that satellite a SkiaSharp dependency. ADR 5's
  wording forbids it.
- **Bidirectional text.** HarfBuzz shapes a run of text in one direction and one script; it does not resolve
  embedding levels or reorder runs. The Unicode Bidirectional Algorithm (UAX #9) is the caller's job, and so
  is finding line-break opportunities (UAX #14). As written, ADR 5 leaves both unplanned while appearing to
  cover them — and Arabic or Hebrew names on French invoices, or IBANs and URLs that must break inside a
  narrow cell, are the first defects a user sees.

## Decision

We will allow Skia where it is needed, and own the text analysis HarfBuzz does not do.

- **SkiaSharp** is allowed in `AdCodicem.Pdf.Html` and `AdCodicem.Pdf.Rendering`, and never in the core.
  The image codecs the core and `AdCodicem.Pdf.Imaging` need are ours (ADR 42), not Skia's.
- **HarfBuzzSharp** stays in `AdCodicem.Pdf.Html`, for shaping: ligatures, kerning, complex scripts, OpenType
  features.
- **UAX #9 bidirectional analysis and UAX #14 line breaking are ours**, in `AdCodicem.Pdf.Html`, from
  property tables generated as static span data, run before HarfBuzz shapes each run (M12.2).
- **The Unicode property tables** — `Script`, `Bidi_Class`, the bracket and mirroring pairs, `Line_Break`,
  `Grapheme_Cluster_Break`, case folding and decompositions — are produced by one build-time generator, M08's,
  from the Unicode Character Database of the version `CharUnicodeInfo` implements, and each is compiled into
  the assembly whose code reads it: the core holds `Script` (M08) and what extraction and search need (M15),
  `Bidi_Class` among them, since reordering extracted right-to-left text by position is not UAX #9; `.Html`
  holds what UAX #9 and UAX #14 need. A table both need is compiled into both from the same generated source,
  never made public API of the core.

## Consequences

- M25 can use Skia without contradicting a decision.
- M12.2 carries the bidirectional algorithm and line breaking explicitly, with their own tests against the
  Unicode conformance files.
- **Rejected** — Skia everywhere (it would reach the core, which ADR 5 rightly forbids); a third-party bidi
  or line-break package (small algorithms, whose tables we can generate and whose allocation profile we must
  control in the layout loop, invariant 3).
- **What would reopen it** — a managed rasterizer good enough to replace Skia in `.Rendering`.
