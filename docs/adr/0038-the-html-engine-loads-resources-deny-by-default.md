# 38. The HTML engine loads resources deny-by-default

Date: 2026-09-26

## Status

Accepted on 2026-09-26, by the maintainer, before any of the HTML engine is written. It generalises
[11](0011-fonts-an-explicit-registry-an-embedded-ofl-set-optional-web.md), whose remote web fonts are off by
default, to every resource an HTML document can name. Implemented by M12.5; M12.6 and M18 use it.

## Context

An invoice template is filled with data the caller did not write: a customer name, an address, a product
description, sometimes a logo URL. HTML that carries such data can name any resource — an image, a
stylesheet, an SVG, a font, an attachment — and an engine that fetches what it is told to becomes a tool
against the server it runs on:

- `<img src="http://169.254.169.254/latest/meta-data/…">` reads a cloud instance's credentials into the PDF;
- `<link rel="attachment" href="file:///etc/passwd">` attaches a server file to a document sent to a
  customer;
- a redirect chain, a very large image or a thousand small ones make one render cost what the attacker
  chooses.

Invariant 4 treats everything read from a PDF as hostile; nothing yet says the same of what an HTML
document asks the engine to read. Fetching over the network also breaks invariant 6: the same inputs no
longer give the same bytes.

## Decision

We will load every resource the HTML engine needs through one resolver whose default refuses everything
the caller did not allow.

- **One seam.** An `IResourceResolver` answers for images, stylesheets, fonts, SVG documents and
  attachments alike; the engine never opens a URL or a file by itself.
- **Deny by default.** The built-in resolver allows `data:` URIs and the base locations the caller declares —
  a directory, an embedded-resource assembly, an HTTP origin — and nothing else.
- **When the network is allowed**, addresses in private, loopback, link-local and cloud-metadata ranges are
  refused after name resolution, redirects are capped and must stay within an allowed origin, and each
  response is bounded in size.
- **`file:`** is confined to a root the caller names; a path that leaves it is refused.
- **Bounds** — the number of resources a document may load, their total size, and the time spent loading
  them — are options, on by default, in the manner of ADR 34.
- **Every refusal is a diagnostic** naming the resource and the rule that refused it; a refused resource
  degrades the render (an image box left empty, a stylesheet ignored) and never fails it.

## Consequences

- A template that worked in a browser may lose images until its caller declares where they come from; the
  first-use documentation shows the one line that does it.
- Rendering stays deterministic by default: nothing the caller did not pass in can change the output.
- Caching is the resolver's business, so a batch run (M12.6) can share decoded resources across documents
  without the engine holding global state (invariant 8).
- **Rejected** — browser-like defaults with options to restrict (every deployment vulnerable until someone
  configures it); a fixed allow-list inside the engine (callers load templates from places we cannot
  foresee).
- **What would reopen it** — none foreseen for the default; the knobs may grow as callers show needs.
