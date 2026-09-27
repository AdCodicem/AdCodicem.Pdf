# 42. Image codecs and scans

Date: 2026-09-26

## Status

Accepted on 2026-09-26, by the maintainer. It places the image codecs no milestone owned, and sets the
policy for anything that alters an image in a document that may be evidence. M22 implements it; M7, M19,
M23 and M25 depend on it.

## Context

Case-file exhibits are mostly scans, and the corpus holds them in CCITT, JBIG2, JPEG and JPEG 2000. M1 passes
those filters through undecoded. Every later feature that touches a scan's pixels needs a decoder:
rasterisation (M25), pixel redaction (M19), blank-page detection, downsampling and recompression (M23), and
exporting images in a usable form (M15). Skia decodes JPEG but neither JBIG2 nor CCITT, and SkiaSharp does not
decode JPEG 2000, so relying on it would leave most scans untouchable.

Decoders are also where PDF readers have been attacked: the FORCEDENTRY exploit of 2021 went through a JBIG2
decoder. Invariant 4 applies to them with full force.

Two further facts shape the policy. A lossy change to a scanned exhibit can change what it says — pattern-
matching JBIG2 compression famously substituted digits in scanned documents in 2013 — so no lossy step can
be silent in a library that assembles evidence. And text recognition needs an engine — native or
model-based — that invariant 1 keeps out of the core and that invariant 6 accepts only when its version is
pinned by the caller.

## Decision

We will split the codecs by size and risk, and make every lossy step the caller's explicit choice.

- **CCITT G3 and G4 decoding in the core** (M22) — small, needed wherever a fax-era scan's pixels matter: image
  export (M15), blank pages (M22), pixel redaction (M19), rasterisation (M25); and bounded like every filter.
  M7's image pages need none: they pass CCITT strips through undecoded.
- **The `AdCodicem.Pdf.Imaging` satellite** — managed, dependency-free, AOT-compatible — holds the JBIG2 (with
  global segments), JPEG 2000 and JPEG (CMYK and YCCK included) decoders, and the lossless CCITT G4 and JBIG2
  generic encoders. Every decoder is bounded under ADR 34 and fuzzed from the day it is written.
- **Lossy steps are opt-in and reported**: downsampling, JPEG re-encoding and conversion to one bit per pixel
  happen only when the caller asks, and each appears in the operation's report with the image it changed.
  **JBIG2 is written lossless only**; lossy and pattern-matching JBIG2 are never produced.
- **OCR**: the core writes an invisible, positioned text layer from recognised words given as hOCR, ALTO or
  TSV; engines sit behind an `IOcrEngine` interface outside the core, supplied by the caller. A first-party
  engine satellite is an open question, answered when callers ask.

## Consequences

- The core stays small; the attack surface of the large decoders is in a package a caller adds knowingly.
- Rasterisation, redaction of pixels, optimisation of scans and blank-page detection share one fuzzed codec
  set rather than each finding its own.
- **Rejected** — every codec in the core (a larger core and a larger attack surface for readers that never
  touch pixels); relying on Skia (JBIG2, CCITT and JPEG 2000 scans untouchable); a first-party Tesseract
  satellite now (native binaries per platform and a model licence to maintain before anyone asked).
- **What would reopen it** — a measurement showing a managed decoder too slow for M23's budgets, which ADR 35
  would then govern; or a demand for a first-party OCR engine.
