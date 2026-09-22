// Prints the preview a deployment should document when it was not told which one: the newest version on
// nuget.org, if that is a preview newer than the newest stable release this checkout documents. Prints
// nothing otherwise — right after a stable release, for instance, when the newest preview is older than it.
//
// A deployment that follows a preview is handed that preview's version and never gets here: nuget.org can
// take minutes to list a package it has just accepted.

import { compareVersions, isPrerelease, previewIsCurrent, readReleases } from "./versions.mjs";

const index = "https://api.nuget.org/v3-flatcontainer/adcodicem.pdf/index.json";

const response = await fetch(index);
if (!response.ok) {
  console.error(`nuget.org answered ${response.status} for ${index}.`);
  process.exit(1);
}

const { versions } = await response.json();
const newest = versions.toSorted(compareVersions).at(-1);

if (newest && isPrerelease(newest) && previewIsCurrent(newest, readReleases())) {
  console.log(newest);
}
