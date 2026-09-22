// Freezes the user documentation for a stable release, as the entry its line has in the version selector.
//
// Called by the stable release (see .releaserc.json) with the version it is about to publish, after the
// API reference has been generated, so the frozen copy documents exactly the API being released. The
// release commit then carries the copy, and every later build of the site serves it unchanged.
//
// A release on a line that is already documented — a patch below 1.0, a minor or a patch from 1.0 on —
// replaces that line's copy rather than adding an entry: the selector lists lines, labelled with their
// latest release.

import { execFileSync } from "node:child_process";
import { rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { compareVersions, lineOf, readReleases } from "./versions.mjs";

const site = path.join(import.meta.dirname, "..");
const version = process.argv[2];

if (!version) {
  console.error("Usage: node scripts/version-docs.mjs <version being released>");
  process.exit(1);
}

let line;
try {
  line = lineOf(version);
} catch (error) {
  console.error(error.message);
  process.exit(1);
}

const releases = readReleases();

// Releases come from main, so they only move forward; a release older than one already documented would
// overwrite newer documentation with older.
if (releases.length > 0 && compareVersions(version, releases[0].version) <= 0) {
  console.error(`${version} is not newer than ${releases[0].version}, the newest documented release.`);
  process.exit(1);
}

const kept = releases.filter((release) => release.line !== line);

if (kept.length !== releases.length) {
  const snapshot = [`versioned_docs/version-${line}`, `versioned_sidebars/version-${line}-sidebars.json`];

  // Unstaged from git as well as deleted from disk: a page the new release no longer has — a type that was
  // removed — must leave the release commit too, and the release only adds what exists.
  execFileSync("git", ["rm", "-r", "--cached", "--quiet", "--ignore-unmatch", ...snapshot], { cwd: site });
  for (const entry of snapshot) rmSync(path.join(site, entry), { recursive: true, force: true });

  writeFileSync(path.join(site, "versions.json"), `${JSON.stringify(kept.map((r) => r.line), null, 2)}\n`);
}

// Docusaurus copies docs/ and the sidebar, and puts the line first in versions.json. Its CLI is run with
// this Node directly rather than through npx, which on Windows would need a shell.
const cli = path.join(site, "node_modules", "@docusaurus", "core", "bin", "docusaurus.mjs");
execFileSync(process.execPath, [cli, "docs:version", line], { cwd: site, stdio: "inherit" });

const latest = Object.fromEntries([[line, version], ...kept.map((release) => [release.line, release.version])]);
writeFileSync(path.join(site, "releases.json"), `${JSON.stringify(latest, null, 2)}\n`);

console.log(`Documentation frozen for ${version}, as the ${line} entry of the version selector.`);
