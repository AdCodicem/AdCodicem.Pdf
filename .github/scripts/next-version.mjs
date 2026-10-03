#!/usr/bin/env node
// Prints the version semantic-release would give the next stable release of a commit, and the preview
// version derived from it, without a token and without the remote.
//
// Usage: node .github/scripts/next-version.mjs [rev]      (rev defaults to HEAD)
//
// `semantic-release --dry-run` cannot answer this in preview.yml: before it analyzes a single commit it
// checks that it may push (`git push --dry-run`), and @semantic-release/github wants a token. A job that
// only reads the repository passes neither. So this script runs the one step that decides the version,
// the commit analyzer, with the configuration release.yml gives it:
//
//   - the `@semantic-release/commit-analyzer` entry of .releaserc.json, options included, merged over
//     the top-level options as semantic-release merges them;
//   - the commit analyzer, its preset and semver as semantic-release itself loads them, resolved from
//     semantic-release's own directory, so that package-lock.json pins all three;
//   - the last release chosen as semantic-release chooses it on a release branch: the highest tag that
//     matches `tagFormat`, is a valid version, is not a prerelease and is merged into `rev`;
//   - the commits since that tag, their raw message trimmed, minus `[skip release]`.
//
// The configuration is read at `rev`, so the answer for a commit never changes: a later run can compute
// again the version of a preview that an earlier run left half published.
//
// It refuses a shallow clone, and a history with no stable tag: either would make it answer 1.0.0, a
// preview that would outrank every 0.x version on nuget.org for good. This repository has one, v0.1.0,
// the baseline tagged by hand (docs/releasing.md).
//
// Output, as key=value lines for $GITHUB_OUTPUT:
//   last_tag, last_version  the last stable release
//   release_type            major, minor or patch; empty when no commit would release anything
//   next_version            the next stable version; the next patch when release_type is empty
//   height                  commits from last_tag to rev
//   preview_version         <next_version>-preview.<height>
import { createRequire } from "node:module";
import { execFileSync } from "node:child_process";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const rev = process.argv[2] ?? "HEAD";
const root = execFileSync("git", ["rev-parse", "--show-toplevel"], {
  cwd: dirname(fileURLToPath(import.meta.url)),
  encoding: "utf8",
}).trim();
const git = (...args) => execFileSync("git", args, { cwd: root, encoding: "utf8", maxBuffer: 1 << 28 });
const fail = (message) => {
  console.error(`::error::${message}`);
  process.exit(1);
};
if (git("rev-parse", "--is-shallow-repository").trim() !== "false") {
  fail("Shallow clone: the tags and the commits since the last release are missing. Check out with fetch-depth: 0.");
}

// semantic-release resolves its plugins from its own directory first (lib/plugins/utils.js, loadPlugin),
// and the analyzer resolves its preset from its own. Resolving from the same place loads the very files
// release.yml runs, wherever npm hoisted them.
const fromRoot = createRequire(join(root, "package.json"));
const semanticReleaseDir = dirname(fromRoot.resolve("semantic-release/package.json"));
const fromSemanticRelease = createRequire(join(semanticReleaseDir, "lib", "plugins", "utils.js"));
const load = async (id) => import(pathToFileURL(fromSemanticRelease.resolve(id)).href);
const { default: semver } = await load("semver");
const { analyzeCommits } = await load("@semantic-release/commit-analyzer");

// Only the shape release.yml uses is supported; anything else fails rather than guesses.
const config = JSON.parse(git("show", `${rev}:.releaserc.json`));
for (const unsupported of ["extends", "analyzeCommits"]) {
  if (unsupported in config) fail(`.releaserc.json: "${unsupported}" is not supported by next-version.mjs.`);
}
const tagFormat = config.tagFormat ?? "v${version}";
const entry = (config.plugins ?? []).find(
  (plugin) => (Array.isArray(plugin) ? plugin[0] : plugin) === "@semantic-release/commit-analyzer"
);
if (!entry) fail(".releaserc.json lists no @semantic-release/commit-analyzer.");
const STEPS = ["verifyConditions", "analyzeCommits", "verifyRelease", "generateNotes", "prepare", "publish", "addChannel", "success", "fail"];
const globalOptions = Object.fromEntries(
  Object.entries(config).filter(([key]) => key !== "plugins" && !STEPS.includes(key))
);
// lib/plugins/normalize.js: the plugin receives { ...options, ...pluginConfig }.
const pluginConfig = { ...globalOptions, ...(Array.isArray(entry) ? entry[1] ?? {} : {}) };

// lib/branches/get-tags.js and lib/get-last-release.js, for a release branch. The pattern is
// semantic-release's: tagFormat with ${version} turned into (.+), anchored at the start only.
const escape = (text) => text.replace(/[\\^$.*+?()[\]{}|]/g, "\\$&");
const tagPattern = new RegExp(`^${escape(tagFormat.replace("${version}", " ")).replace(" ", "(.+)")}`);
const last = git("tag", "--merged", rev)
  .split("\n")
  .map((tag) => tag.trim())
  .map((tag) => ({ tag, version: tag.match(tagPattern)?.[1] }))
  .filter(({ version }) => version && semver.valid(semver.clean(version)) && !semver.prerelease(version))
  .sort((a, b) => semver.rcompare(a.version, b.version))[0];
if (!last) fail(`No stable tag matching ${tagFormat} is merged into ${rev}.`);

// lib/git.js getCommits: `git log <from>..<to>`, the raw body (%B) trimmed. -z separates the records with
// NUL, which a commit message cannot contain.
const range = `${last.tag}..${rev}`;
const commits = git("log", "-z", "--format=%H%n%B", range)
  .split("\0")
  .filter(Boolean)
  .map((record) => {
    const newline = record.indexOf("\n");
    return { hash: record.slice(0, newline), message: record.slice(newline + 1).trim() };
  })
  // lib/definitions/plugins.js, analyzeCommits.preprocess.
  .filter(({ message }) => !/\[skip\s+release]|\[release\s+skip]/i.test(message));
const height = Number(git("rev-list", "--count", range).trim());

const silent = { log() {}, error() {}, warn() {}, success() {} };
const releaseType = await analyzeCommits(pluginConfig, { commits, logger: silent, cwd: root, env: process.env });

// lib/get-next-version.js, for a release branch with a previous release.
const nextVersion = semver.inc(last.version, releaseType ?? "patch");

const output = {
  last_tag: last.tag,
  last_version: last.version,
  release_type: releaseType ?? "",
  next_version: nextVersion,
  height,
  preview_version: `${nextVersion}-preview.${height}`,
};
for (const [key, value] of Object.entries(output)) console.log(`${key}=${value}`);
