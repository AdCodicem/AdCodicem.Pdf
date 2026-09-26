# 39. Forward references: late-filled XObjects, and two bounded passes

Date: 2026-09-26

## Status

Accepted on 2026-09-26, by the maintainer, before M12's pagination is written. It works within
[13](0013-lazy-reading-output-as-a-full-rewrite-or-an-incremental-upda.md), whose writer only moves forward,
and M12's acceptance, which holds memory constant as page count grows.

## Context

Paged documents refer forward: "page 3 of 12" is written on page 3 before page 12 exists; "see article 7.2
on page 18" and a table of contents name pages that are laid out later; a page group's total ("invoice
page 1 of 2") is known only when the group ends. Two constraints meet here:

- the writer is forward-only (ADR 13): a page, once written, is not revisited, and only object numbers can
  be reserved ahead;
- generating a thousand-page report must hold memory constant as page count grows, measured at 10, 100 and
  1000 pages — so the engine cannot keep every laid-out page until the end.

Two known strategies each fail one case. Writing a counter into a form XObject that is reserved ahead and
filled at the end works in one pass, but only where the counter's text cannot change the layout around it —
a margin box, not a paragraph. Laying the document out twice, the first time only to learn where each
anchor lands, is exact everywhere, but doubles the CPU cost of every document that shows a page total.

## Decision

We will use each strategy where it is sound.

- **Counters in margin boxes** — `counter(pages)`, a page group's total, and any value that does not flow
  with the text — are drawn as a form XObject whose object number is reserved when the page is written, and
  whose content is written once the value is known. Its box reserves the width of the widest value the
  counter can take, from the digits' advances, so no page is laid out again. One pass.
- **References in the flow** — `target-counter()`, `target-text()`, a table of contents, an index, and a
  page total written inside the text — use two passes. The first lays out the document and keeps only a map
  from anchor to page (and to the text a `target-text()` needs), discarding every page as it goes; the
  second lays out again with the map and writes. If the second pass moves an anchor — a longer table of
  contents pushing everything down a page — the engine passes again, a bounded number of times, then writes
  and reports the references that did not settle.
- A document that has no in-flow reference is laid out once.

## Consequences

- Memory stays bounded by a page plus the anchor map, in both strategies.
- A table of contents costs a second layout; a footer's "page X of Y" does not.
- The reserved width can leave a little space after a short total; that is the price of one pass, and a
  page total rarely spans more than four digits.
- **Rejected** — always two passes (twice the CPU for the commonest case, a footer's page total); always late
  XObjects (cannot express a table of contents whose own length changes the pagination); holding every page
  until the end (breaks the constant-memory acceptance).
- **What would reopen it** — a layout feature whose forward reference neither strategy can serve, such as
  a float whose size depends on a later page number.
