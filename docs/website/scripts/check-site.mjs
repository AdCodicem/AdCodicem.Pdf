// Reads the built site the way a visitor would, and fails when a page shows its own machinery.
//
// The build succeeding proves little: in September 2026 every page of the user documentation was
// published as its own compiled JavaScript, printed as text, and the build, every link and every HTTP
// status were fine. Two failures are looked for, in the text a reader sees — scripts, styles and code
// samples excluded, since a sample may legitimately show markup:
//
// - compiled MDX: a page whose content was compiled twice renders the output of the first pass;
// - escaped markup: an HTML tag printed as text, as DocFX's heading anchors were in the API reference.
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
    // DocFX's placeholder for a reference it could not resolve; a browser renders it as nothing at all.
    reason: "an unresolved DocFX cross-reference",
    pattern: /<xref\b/,
  },
];

async function* htmlFiles(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) yield* htmlFiles(full);
    else if (entry.name.endsWith(".html")) yield full;
  }
}

// What a reader sees as prose: everything but scripts, styles, and code — inline or in a block.
const visibleText = (html) =>
  html
    .replace(/<script\b[\s\S]*?<\/script>/g, "")
    .replace(/<style\b[\s\S]*?<\/style>/g, "")
    .replace(/<pre\b[\s\S]*?<\/pre>/g, "")
    .replace(/<code\b[\s\S]*?<\/code>/g, "");

const failures = [];
let pages = 0;

for await (const file of htmlFiles(root)) {
  pages += 1;
  const text = visibleText(await readFile(file, "utf8"));

  for (const { reason, pattern } of checks) {
    const found = pattern.exec(text);
    if (found) {
      const excerpt = text.slice(Math.max(0, found.index - 60), found.index + 80).replace(/\s+/g, " ");
      failures.push(`${path.relative(root, file)}: ${reason}\n    …${excerpt}…`);
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

console.log(`Site check: ${pages} built pages, none showing compiled MDX or escaped markup.`);
