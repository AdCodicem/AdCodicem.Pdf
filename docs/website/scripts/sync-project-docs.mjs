// The project's own documents live in docs/, which also contains this site. Pointing a docs plugin at
// docs/ directly is what broke the published site: Docusaurus scopes its MDX loader to a plugin's whole
// directory, whatever `include` says, so every page of the user documentation was compiled twice — once
// by each plugin — and the second pass rendered the first pass's JavaScript as text.
//
// Copying the documents into a directory of their own, outside the user documentation, gives each plugin
// a tree the other cannot see. The copy is ignored by git and rebuilt before every start and build; the
// edit links still point at the originals. Run automatically; see package.json.

import { cp, mkdir, readdir, rm } from "node:fs/promises";
import path from "node:path";

const source = path.join(import.meta.dirname, "..", "..");
const destination = path.join(import.meta.dirname, "..", "project");

// What the site publishes, relative to docs/. Everything else there — the site itself, the DocFX
// configuration — is not a project document.
const directories = ["adr", "milestones"];

// The blank templates are for writing records, not for reading.
const excluded = new Set(["adr/adr-template.md", "milestones/_template.md"]);

// Rebuilt from scratch, so a document deleted or renamed in docs/ does not survive as a stale page.
await rm(destination, { recursive: true, force: true });
await mkdir(destination, { recursive: true });

let copied = 0;

async function copyMarkdown(relativeDirectory, recursive) {
  const entries = await readdir(path.join(source, relativeDirectory), { withFileTypes: true });

  for (const entry of entries) {
    const relative = path.posix.join(relativeDirectory.split(path.sep).join("/"), entry.name);

    if (entry.isDirectory()) {
      if (recursive) await copyMarkdown(relative, true);
      continue;
    }

    if (!entry.name.endsWith(".md") || excluded.has(relative)) continue;

    await mkdir(path.join(destination, path.dirname(relative)), { recursive: true });
    await cp(path.join(source, relative), path.join(destination, relative));
    copied += 1;
  }
}

await copyMarkdown("", false);
for (const directory of directories) await copyMarkdown(directory, true);

console.log(`Project documents: ${copied} copied from docs/ into ${path.relative(process.cwd(), destination)}.`);
