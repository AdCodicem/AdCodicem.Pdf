// DocFX's Markdown output is written for DocFX, and three things in it read badly once Docusaurus renders
// it. This rewrites them after the metadata step; see package.json.
//
// - Headings carry their anchor as inline HTML — `# <a id="AdCodicem_Pdf_Objects_PdfName"></a> Class
//   PdfName`. Docusaurus takes a page's title, its sidebar label and its table of contents from the heading
//   text, so every one of them showed the tag. The anchor becomes an explicit heading id instead, `{#…}`,
//   which keeps every existing link to it working.
// - Cross-references between generated pages are file names — `AdCodicem.Pdf.Objects.md` — which
//   Docusaurus fails to resolve, the dots in the name confusing its file-link handling. They become
//   extension-less sibling links.
// - A `<see cref>` DocFX could not resolve on its own is left as an empty `<xref>` element, so the
//   sentence around it simply lost a word. It becomes a link to the page, and the member, it names.

import { readdir, readFile, rm, writeFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import path from "node:path";

const directory = path.join(import.meta.dirname, "..", "docs", "api");

if (!existsSync(directory)) {
  console.error(`No generated API reference at ${directory}; run the DocFX metadata step first.`);
  process.exit(1);
}

const files = (await readdir(directory)).filter((name) => name.endsWith(".md"));
const pages = new Set(files.map((name) => name.slice(0, -".md".length)));

const anchoredHeading = /^(#{1,6}) <a id="([^"]+)"><\/a> (.*)$/gm;
const fileLink = /\]\((?!https?:|#)(?:\.\/)?([^)\s]+?)\.md((?:\\?#)[^)]*)?\)/g;
const unresolvedReference = /<xref href="([^"]+)"[^>]*><\/xref>/g;

// The heading of every anchor, per page, read before anything is rewritten: an unresolved reference is
// displayed with the heading of the member it points at.
const headings = new Map();
for (const name of files) {
  const text = await readFile(path.join(directory, name), "utf8");
  const anchors = new Map();
  for (const [, , id, title] of text.matchAll(anchoredHeading)) anchors.set(id, title.trim());
  headings.set(name.slice(0, -".md".length), anchors);
}

// DocFX derives an anchor from a UID by replacing everything that is not a letter or a digit.
const anchorOf = (uid) => uid.replace(/[^A-Za-z0-9]/g, "_");

function resolveReference(match, href) {
  const uid = decodeURIComponent(href);

  // The page is the longest prefix of the UID that is a generated page: the type, for a member.
  const name = uid.replace(/\(.*$/, "");
  let page = name;
  while (page && !pages.has(page)) page = page.includes(".") ? page.slice(0, page.lastIndexOf(".")) : "";

  if (!page) {
    // Outside this library: the .NET reference has a page for every public type and member.
    if (uid.startsWith("System.")) {
      const short = name.slice(name.lastIndexOf(".") + 1);
      return `[${short}](https://learn.microsoft.com/dotnet/api/${name.toLowerCase()})`;
    }
    return match;
  }

  const typeName = page.slice(page.lastIndexOf(".") + 1);
  if (page === uid) return `[${typeName}](${page})`;

  const anchor = anchorOf(uid);
  const title = headings.get(page).get(anchor);
  return title ? `[${typeName}.${title}](${page}#${anchor})` : `[${typeName}](${page})`;
}

let rewritten = 0;

for (const name of files) {
  const file = path.join(directory, name);
  const original = await readFile(file, "utf8");

  const updated = original
    .replace(anchoredHeading, (_, level, id, title) => `${level} ${title.trim()} {#${id}}`)
    // Only rewrite links whose target actually exists, so a genuinely broken one still shows up as broken.
    .replace(fileLink, (match, target, anchor) =>
      pages.has(target) ? `](${target}${anchor ? `#${anchor.replace(/^\\?#/, "").replaceAll("\\", "")}` : ""})` : match,
    )
    .replace(unresolvedReference, resolveReference);

  if (updated !== original) {
    await writeFile(file, updated, "utf8");
    rewritten += 1;
  }
}

// DocFX's own table of contents: the sidebar is built from the pages, and a stable release would otherwise
// freeze this file with them.
await rm(path.join(directory, "toc.yml"), { force: true });

console.log(`API reference: ${rewritten} of ${files.length} pages tidied.`);
