// DocFX writes cross-references between generated pages as file names — `AdCodicem.Pdf.Objects.md` —
// which Docusaurus fails to resolve, dots in the name confusing its file-link handling. Rewriting them as
// extension-less sibling links makes the generated reference navigable instead of a set of dead ends.
// Run automatically after the metadata step; see package.json.

import { readdir, readFile, writeFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import path from "node:path";

const directory = path.join(import.meta.dirname, "..", "docs", "api");

if (!existsSync(directory)) {
  console.error(`No generated API reference at ${directory}; run the DocFX metadata step first.`);
  process.exit(1);
}

const files = (await readdir(directory)).filter((name) => name.endsWith(".md"));
let rewritten = 0;

for (const name of files) {
  const file = path.join(directory, name);
  const original = await readFile(file, "utf8");

  // Only rewrite links whose target actually exists, so a genuinely broken one still shows up as broken.
  const updated = original.replace(/\]\((?!https?:|#)(?:\.\/)?([^)\s]+\.md)(#[^)]*)?\)/g, (match, target, anchor) =>
    files.includes(target) ? `](${target.slice(0, -".md".length)}${anchor ?? ""})` : match,
  );

  if (updated !== original) {
    await writeFile(file, updated, "utf8");
    rewritten += 1;
  }
}

console.log(`API reference: ${rewritten} of ${files.length} pages had cross-links rewritten.`);
