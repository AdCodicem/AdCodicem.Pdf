# 37. Out of scope: active content, and PDF to Office

Date: 2026-09-26

## Status

Accepted on 2026-09-26, by the maintainer, after the feature survey
(`docs/research/2026-09-feature-survey.md`). It completes
[2](0002-fully-managed-rendering.md) and [4](0004-business-documents-with-a-modern-css-subset.md), which
already rule out JavaScript for the HTML engine, by saying what the library does with such content when it
arrives inside a PDF.

## Context

Every comparison with another PDF library comes back to the same features, and a project that has not
written down why it lacks them re-evaluates them from scratch each time. The survey found two groups that
sit outside the business-document target and that no milestone could deliver without a second engine:

- **Active and interactive content** — executing JavaScript (document, page, field and action scripts),
  rendering dynamic XFA forms, and authoring 3D (U3D, PRC) or multimedia and rich-media content. Executing
  a script needs a JavaScript engine and a sandbox around it, and makes output depend on the script rather
  than on the inputs (invariant 6). Dynamic XFA is deprecated by ISO 32000-2, needs its own layout engine
  plus JavaScript, and only Acrobat renders it. 3D and rich media have no place in an invoice, a report, a
  contract or a case file, and PDF/A forbids them.
- **PDF to Office** — rebuilding a Word or Excel document from a PDF is a heuristic reconstruction that
  loses structure even in the products that sell it, and it is neither generating nor manipulating a PDF.
  Machine uses of a PDF's content are served by M15's Markdown and JSON exports.

The survey also weighed, and the maintainer kept in scope, print production and colour management (M29),
vertical CJK, ruby and MathML (M30), and Office to PDF by way of DOCX to HTML (M31).

## Decision

We will not execute or produce active content, and we will not convert PDF to Office formats.

- **Executing JavaScript** — never. Scripts are read, preserved on merge, reported by validation and removed
  by sanitisation (M19). Form calculations and formats are served without a script engine: the standard
  Acrobat formats are recognised by pattern (M16), and any other script is reported, never run.
- **Dynamic XFA** — detected, reported, and removable (M16); its datasets can be read. It is never rendered
  or flattened.
- **3D, multimedia and rich media** — read, preserved on merge, kept coherent when pages are removed, and
  removed by sanitisation. Never authored.
- **PDF to DOCX, XLSX, PPTX or HTML reconstruction** — not provided. M15's exports are the machine route.

"Out of scope" never means unreadable: a file carrying any of these is opened, validated and manipulated
like any other (invariant 12), and whatever the library cannot keep is reported (invariant 7).

## Consequences

- Users who need a script executed or a dynamic XFA form rendered keep Acrobat or a browser engine upstream;
  the documentation says so in its comparison page, under "when to choose something else".
- The sanitisation categories of M19 and the validation families `action.*` and `hidden.*` become the one
  place where active content is dealt with.
- **Rejected** — embedding a JavaScript engine for form calculations (a sandbox to maintain, and
  non-deterministic output); an XFA layout engine (a second engine for a deprecated format); PDF to Office as
  a satellite (a reconstruction product of its own, off the library's axis).
- **What would reopen it** — for JavaScript, the condition ADR 2 already states: a demonstrated need to run
  scripts, met by a satellite backend rather than by the core; for dynamic XFA, a public-sector form that
  business users must fill and that has no AcroForm version; for PDF to Office, a caller need that M15's
  exports demonstrably cannot meet.
