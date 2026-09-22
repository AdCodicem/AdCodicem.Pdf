// Which releases the site documents, and how a release maps onto an entry of the version selector. Shared by
// the site configuration, the release-time snapshot and the deployment, so the three cannot disagree.
//
// Two files hold the state, both written by the stable release and committed with it:
//
// - versions.json — Docusaurus's own list of frozen versions, newest first. A version here is a *line*:
//   `0.3` below 1.0, where every minor may break the API, and `1`, `2`… from 1.0 on, where only a major may;
// - releases.json — the latest release of each line, `{ "0.3": "0.3.2" }`, which is what the selector shows.

import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

// Not import.meta.dirname: the site configuration imports this module through jiti, which rewrites
// import.meta.url and nothing else.
const site = fileURLToPath(new URL("..", import.meta.url));

const pattern = /^(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?$/;

function parse(version) {
  const match = pattern.exec(version);
  if (!match) throw new Error(`'${version}' is not a version this project publishes.`);
  const [, major, minor, patch, prerelease] = match;
  return { core: [Number(major), Number(minor), Number(patch)], prerelease: prerelease?.split(".") ?? [] };
}

/** Whether a version is a prerelease — a preview, here. */
export const isPrerelease = (version) => parse(version).prerelease.length > 0;

/** The selector entry a stable release belongs to: its minor line below 1.0, its major from 1.0 on. */
export function lineOf(version) {
  const { core, prerelease } = parse(version);
  if (prerelease.length > 0) throw new Error(`'${version}' is a preview; only stable releases are frozen.`);
  const [major, minor] = core;
  return major === 0 ? `0.${minor}` : `${major}`;
}

/** Semantic-versioning precedence: negative when a comes first, positive when b does. */
export function compareVersions(a, b) {
  const left = parse(a);
  const right = parse(b);

  for (let i = 0; i < 3; i += 1) {
    if (left.core[i] !== right.core[i]) return left.core[i] - right.core[i];
  }

  // Same core: the release sorts after all of its prereleases.
  if (left.prerelease.length === 0 || right.prerelease.length === 0) {
    return right.prerelease.length - left.prerelease.length;
  }

  for (let i = 0; i < Math.min(left.prerelease.length, right.prerelease.length); i += 1) {
    const x = left.prerelease[i];
    const y = right.prerelease[i];
    if (x === y) continue;
    const numeric = /^\d+$/;
    if (numeric.test(x) && numeric.test(y)) return Number(x) - Number(y);
    if (numeric.test(x)) return -1;
    if (numeric.test(y)) return 1;
    return x < y ? -1 : 1;
  }

  return left.prerelease.length - right.prerelease.length;
}

function readJson(name, fallback) {
  const file = path.join(site, name);
  return existsSync(file) ? JSON.parse(readFileSync(file, "utf8")) : fallback;
}

/** The documented stable lines, newest first, each with its latest release. */
export function readReleases() {
  const latest = readJson("releases.json", {});
  return readJson("versions.json", []).map((line) => ({ line, version: latest[line] ?? line }));
}

/**
 * Whether the preview section should be published beside the stable ones: only when the preview is newer
 * than the newest stable release. Right after a release, main *is* the release, and the newest preview on
 * nuget.org is older than it — a button to it would lead backwards.
 */
export function previewIsCurrent(preview, releases) {
  if (!preview) return false;
  if (releases.length === 0) return true;
  return compareVersions(preview, releases[0].version) > 0;
}
