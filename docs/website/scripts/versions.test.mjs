// The rules that decide what the version selector lists and whether the preview button appears.
// Run with `npm test`.

import { test } from "node:test";
import assert from "node:assert/strict";
import { compareVersions, isPrerelease, lineOf, previewIsCurrent } from "./versions.mjs";

test("below 1.0 a release belongs to its minor line, since every minor may break the API", () => {
  assert.equal(lineOf("0.1.0"), "0.1");
  assert.equal(lineOf("0.3.7"), "0.3");
  assert.equal(lineOf("0.12.0"), "0.12");
});

test("from 1.0 on a release belongs to its major line, 1.0 included", () => {
  assert.equal(lineOf("1.0.0"), "1");
  assert.equal(lineOf("1.4.2"), "1");
  assert.equal(lineOf("2.0.1"), "2");
});

test("a preview has no line: only stable releases are frozen", () => {
  assert.throws(() => lineOf("0.3.1-preview.42"), /preview/);
});

test("a version this project does not publish is refused rather than guessed", () => {
  assert.throws(() => lineOf("v0.3.0"), /not a version/);
  assert.throws(() => lineOf("0.3"), /not a version/);
});

test("versions order by major, minor and patch numerically, not as text", () => {
  assert.ok(compareVersions("0.10.0", "0.9.0") > 0);
  assert.ok(compareVersions("1.0.0", "0.99.99") > 0);
  assert.ok(compareVersions("0.3.2", "0.3.10") < 0);
  assert.equal(compareVersions("0.3.2", "0.3.2"), 0);
});

test("a preview sorts before the release it leads to, and after the one it follows", () => {
  assert.ok(compareVersions("0.3.1-preview.42", "0.3.1") < 0);
  assert.ok(compareVersions("0.3.1-preview.42", "0.3.0") > 0);
});

test("previews order by run number numerically", () => {
  assert.ok(compareVersions("0.3.1-preview.9", "0.3.1-preview.10") < 0);
  assert.ok(compareVersions("0.3.1-preview.10", "0.3.1-preview.9") > 0);
});

test("a preview is recognised by its prerelease suffix", () => {
  assert.equal(isPrerelease("0.3.1-preview.42"), true);
  assert.equal(isPrerelease("0.3.1"), false);
});

const releases = [
  { line: "0.3", version: "0.3.0" },
  { line: "0.2", version: "0.2.4" },
];

test("the preview is published when it is newer than the newest stable release", () => {
  assert.equal(previewIsCurrent("0.3.1-preview.43", releases), true);
});

test("right after a release the newest preview is older than it, and is not published", () => {
  assert.equal(previewIsCurrent("0.2.5-preview.40", releases), false);
});

test("before any stable release the preview is all there is", () => {
  assert.equal(previewIsCurrent("0.1.1-preview.14", []), true);
});

test("no preview at all publishes none", () => {
  assert.equal(previewIsCurrent("", releases), false);
  assert.equal(previewIsCurrent(undefined, releases), false);
});
