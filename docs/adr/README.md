# Architecture decisions

One file per decision, in the format Michael Nygard proposed, recorded so that the reasoning behind the
current shape of the project stays discoverable instead of living in closed pull request threads.

A settled decision is not reopened without new evidence — that is what writing them down is for.

| # | Decision | Was |
|---|----------|-----|
| [1](0001-record-architecture-decisions.md) | Record architecture decisions | — |
| [2](0002-fully-managed-rendering.md) | Fully managed rendering | `D01` |
| [3](0003-full-scope-generation-and-manipulation.md) | Full scope: generation and manipulation | `D02` |
| [4](0004-business-documents-with-a-modern-css-subset.md) | Business documents with a modern CSS subset | `D03` |
| [5](0005-skiasharp-and-harfbuzzsharp-allowed-in-html-only.md) | SkiaSharp and HarfBuzzSharp allowed, in `.Html` only | `D04` |
| [6](0006-our-own-pdf-writer-rather-than-skias-pdf-backend.md) | Our own PDF writer rather than Skia's PDF backend | `D05` |
| [7](0007-conformance-designed-in-from-the-start.md) | Conformance designed in from the start | `D06` |
| [8](0008-facade-immutable-options-dependency-injection.md) | Facade, immutable options, dependency injection | `D07` |
| [9](0009-a-dependency-free-core-plus-satellites.md) | A dependency-free core plus satellites | `D08` |
| [10](0010-net100-only-c-14.md) | `net10.0` only, C# 14 | `D09` |
| [11](0011-fonts-an-explicit-registry-an-embedded-ofl-set-optional-web.md) | Fonts: an explicit registry, an embedded OFL set, optional web fonts | `D10` |
| [12](0012-github-actions-published-to-nugetorg.md) | GitHub Actions, published to nuget.org | `D11` |
| [13](0013-lazy-reading-output-as-a-full-rewrite-or-an-incremental-upda.md) | Lazy reading; output as a full rewrite or an incremental update | `D12` |
| [14](0014-a-tolerant-reader-with-a-diagnostic-report.md) | A tolerant reader with a diagnostic report | `D13` |
| [15](0015-full-text-extraction-tagged-structure-preferred.md) | Full text extraction, tagged structure preferred | `D14` |
| [16](0016-rasterization-as-a-satellite-after-the-foundations.md) | Rasterization as a satellite, after the foundations | `D15` |
| [17](0017-conformance-actively-preserved-plus-a-built-in-validator.md) | Conformance actively preserved, plus a built-in validator | `D16` |
| [18](0018-signing-space-reserved.md) | Signing: space reserved | `D17` |
| [19](0019-milestones-are-accepted-on-real-documents.md) | Milestones are accepted on real documents | `D18` |
| [20](0020-everything-is-written-in-english.md) | Everything is written in English | `D19` |
| [21](0021-validation-is-a-rule-engine-and-conformance-is-a-profile-of.md) | Validation is a rule engine, and conformance is a profile of it | `D20` |
| [22](0022-repair-is-driven-by-findings-and-conservative-by-default.md) | Repair is driven by findings, and conservative by default | `D21` |
| [23](0023-third-party-corpus-documents-are-vendored-only-under-attribu.md) | Third-party corpus documents are vendored only under attribution-only licenses | `D22` |
| [24](0024-package-identifiers-and-a-reserved-prefix.md) | Package identifiers, and a reserved prefix | `D23` |
| [25](0025-trusted-publishing-rather-than-an-api-key.md) | Trusted publishing rather than an API key | `D24` |
| [26](0026-test-stack-xunit-v3-awesomeassertions-nsubstitute.md) | Test stack: xUnit v3, AwesomeAssertions, NSubstitute | `D25` |
| [27](0027-integration-tests-run-the-referees-in-containers.md) | Integration tests run the referees in containers | `D26` |
| [28](0028-a-documentation-site-published-from-the-repository.md) | A documentation site, published from the repository | `D27` |
| [29](0029-warnings-are-errors-and-suppressions-are-local-and-justified.md) | Warnings are errors, and suppressions are local and justified | `D28` |
| [30](0030-previews-on-every-merge-stable-releases-on-demand.md) | Previews on every merge, stable releases on demand | — |
| [31](0031-versioned-documentation-stable-lines-and-the-preview.md) | Versioned documentation: stable lines, and the preview beside them | — |
| [32](0032-documents-that-cannot-be-redistributed-are-fetched-on-demand.md) | Documents that cannot be redistributed are fetched on demand, never committed | — |
| [33](0033-a-remote-document-may-be-a-member-of-a-pinned-archive.md) | A remote document may be a member of a pinned archive | — |
| [34](0034-every-valid-pdf-is-readable-and-the-readers-guards-are.md) | Every valid PDF is readable, and the reader's guards are options | — |
| [35](0035-unsafe-code-where-a-measurement-asks-for-it.md) | Unsafe code, where a measurement asks for it | — |
| [36](0036-validation-lives-in-the-core-conformance-in-a-satellite.md) | Validation lives in the core, and the conformance profiles in a satellite | — |
| [37](0037-out-of-scope-active-content-and-pdf-to-office.md) | Out of scope: active content, and PDF to Office | — |
| [38](0038-the-html-engine-loads-resources-deny-by-default.md) | The HTML engine loads resources deny-by-default | — |
| [39](0039-forward-references-late-filled-xobjects-and-two-bounded-passes.md) | Forward references: late-filled XObjects, and two bounded passes | — |
| [40](0040-the-caller-chooses-pdf-1-7-or-2-0-output.md) | The caller chooses PDF 1.7 or PDF 2.0 output | — |
| [41](0041-cryptography-lives-in-satellites.md) | Cryptography lives in satellites | — |
| [42](0042-image-codecs-and-scans.md) | Image codecs and scans | — |
| [43](0043-skia-in-html-and-rendering-and-text-analysis-is-ours.md) | Skia in `.Html` and `.Rendering`, and text analysis is ours | — |
| [44](0044-object-shape-rules-generated-from-the-arlington-model.md) | Object-shape rules generated from the Arlington model | — |
| [45](0045-a-findings-severity-says-whether-the-file-reads-as-written.md) | A finding's severity says whether the file reads as written | — |
| [46](0046-every-milestone-ends-with-an-adversarial-review.md) | Every milestone ends with an adversarial review by a fresh session | — |
| [47](0047-the-user-documentation-follows-diataxis.md) | The user documentation follows Diátaxis | — |

## Decisions too small for a record of their own

- Milestones are **numbered in the order they are worked** (since 2026-09-26): a milestone inserted later
  renumbers those after it, everywhere in the repository, and `docs/roadmap.md` keeps the mapping from the
  numbers older commits use.
- The layout engine works in **CSS pixels**; conversion to points (`× 0.75`) happens only when painting.
- The PDF coordinate system starts bottom-left and layout works top-left: the conversion lives in exactly
  one place, in painting.
- PDF names are interned; common integers are cached.
- The command-line tool, `AdCodicem.Pdf.Tool` (M06), installs the command **`adpdf`** (since 2026-09-27).
- A stream copied between documents travels **encoded**, with no decompress/recompress cycle.
- A dictionary entry whose value is null is dropped on parse: the specification says it is equivalent to an
  absent entry, and every later stage is spared a null it would have to ignore.
- A key a dictionary gives more than once keeps the **last** value given, and a null given last removes it, as an
  absent entry (since 2026-10-03, [#172](https://github.com/AdCodicem/AdCodicem.Pdf/issues/172)): ISO 32000-1
  (7.3.7) forbids the repeat and says nothing of which value counts. qpdf, pdf.js, PDFBox, MuPDF, pdfium, poppler and
  veraPDF's parser read it so, pypdf and Ghostscript keep the first. Before then, `<< /B 3 /B null >>` read as
  `<< /B 3 >>`: so do pypdf and Ghostscript, keeping the first value, while every reader that keeps the last reads B
  absent; and `/B 5 0 R`, object 5 null, never read as 3. Keys compare as they read, so `/F#69lter` repeats `/Filter`.
  A key given with no value, before `>>` or `endobj`, is one given null; a value a guard's cut leaves short removes
  nothing. Each repeat is reported, `syntax.key-repeated`, a null on either side included.
- CodeQL runs as GitHub's **default setup**, on every pull request and every push to `main`, with the query
  suite chosen in the repository's settings; the repository keeps no CodeQL workflow or configuration of its
  own (since 2026-09-26, T35). Until then a workflow ran the security *and* quality suites with four queries
  excluded as wrong for this codebase — `cs/path-combine`, `cs/catch-of-all-exceptions`,
  `cs/linq/missed-where`, `cs/complex-block`. If the default setup raises one of them, the alert is dismissed
  in the Security tab with that reason (git history keeps the configuration that gave each one), rather than
  configured away again.
- The document `/ID` is derived from content, or supplied by the caller, never random — determinism comes
  first, and a random identifier would make fingerprint tests impossible.
