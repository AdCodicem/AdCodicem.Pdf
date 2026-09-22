// Reads the built site the way a visitor would, and fails when a page shows its own machinery.
//
// The build succeeding proves little: in September 2026 every page of the user documentation was
// published as its own compiled JavaScript, printed as text, and the build, every link and every HTTP
// status were fine. What is looked for, in the text a reader sees — scripts, styles and code samples
// excluded, since a sample may legitimately show markup:
//
// - compiled MDX: a page whose content was compiled twice renders the output of the first pass;
// - escaped markup: an HTML tag printed as text, as DocFX's heading anchors were in the API reference;
// - an unresolved DocFX cross-reference, which a browser renders as nothing at all;
// - a version banner on a project document, which belongs to no version.
//
// Run automatically after every build; see package.json. Given a directory, it checks that instead — a
// saved copy of the published site, for instance.

import { readdir, readFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import path from "node:path";

const root = process.argv[2] ? path.resolve(process.argv[2]) : path.join(import.meta.dirname, "..", "build");

if (!existsSync(root)) {
  console.error(`No built site at ${root}; run the build first.`);
  process.exit(1);
}

const checks = [
  {
    reason: "compiled MDX rendered as text",
    pattern: /\b_jsxs?\(|\b_createMdxContent\b|\buseMDXComponents\b|export const (?:frontMatter|contentTitle|toc)\b/,
  },
  {
    reason: "an HTML tag printed as text",
    pattern:
      /&lt;\/?(?:a|abbr|b|br|code|div|em|i|img|li|ol|p|pre|span|strong|sub|sup|table|td|th|tr|ul|xref)(?:\s|\/|&gt;)/,
  },
  {
    reason: "an unresolved DocFX cross-reference",
    pattern: /<xref\b/,
  },
  {
    // The project documents are unversioned, but their plugin's only version is named like the preview.
    reason: "a version banner on an unversioned project document",
    pattern: /\btheme-doc-version-banner\b/,
    appliesTo: (page) => page.split(path.sep)[0] === "project",
  },
];

async function* htmlFiles(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) yield* htmlFiles(full);
    else if (entry.name.endsWith(".html")) yield full;
  }
}

const skippedElement = /<(script|style|pre|code)\b[^>]*>/gi;

// What a reader sees as prose: everything but scripts, styles, and code — inline or in a block. Each
// skipped element runs to its own closing tag, the way an HTML parser treats raw text: a script is not
// scanned for tags, and a code block's inner <code> ends with the <pre> that holds it.
function visibleText(html) {
  const lower = html.toLowerCase();
  let text = "";
  let position = 0;

  while (position < html.length) {
    skippedElement.lastIndex = position;
    const opening = skippedElement.exec(html);
    if (!opening) {
      text += html.slice(position);
      break;
    }

    text += html.slice(position, opening.index);

    const closing = lower.indexOf(`</${opening[1].toLowerCase()}`, skippedElement.lastIndex);
    const end = closing < 0 ? -1 : lower.indexOf(">", closing);
    position = end < 0 ? html.length : end + 1;
  }

  return text;
}

const failures = [];
let pages = 0;

for await (const file of htmlFiles(root)) {
  pages += 1;
  const page = path.relative(root, file);
  const text = visibleText(await readFile(file, "utf8"));

  for (const { reason, pattern, appliesTo } of checks) {
    if (appliesTo && !appliesTo(page)) continue;

    const found = pattern.exec(text);
    if (found) {
      const excerpt = text.slice(Math.max(0, found.index - 60), found.index + 80).replace(/\s+/g, " ");
      failures.push(`${page}: ${reason}\n    …${excerpt}…`);
    }
  }
}

if (pages === 0) {
  console.error("The built site has no pages.");
  process.exit(1);
}

if (failures.length > 0) {
  console.error(`${failures.length} problem(s) in ${pages} built pages:\n`);
  for (const failure of failures) console.error(`  ${failure}`);
  process.exit(1);
}

console.log(`Site check: ${pages} built pages, none showing compiled MDX, escaped markup or a misplaced banner.`);
