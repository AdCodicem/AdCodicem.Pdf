#!/usr/bin/env python3
"""Fetches the remote corpus: the documents we may use in tests but not redistribute (ADR 32).

tests/corpus/manifest.json describes them among the other documents, with origin "remote", the URL each is
fetched from and the SHA-256 it must have. They land in tests/corpus/remote/, which git ignores: nothing fetched
is ever committed, nor anything derived from it. The test suite leaves out the remote entries whose file is
absent, so a machine that never runs this script tests exactly the committed corpus.

A download is accepted only if its SHA-256 is the one the manifest pins. A file that has changed at its source
is refused, never accepted by updating the hash: it is a new document, to be reviewed as one.

    python3 fetch_remote.py          fetch what is missing, verify what is present
    python3 fetch_remote.py --list   report each document's state, fetch nothing

Exit status: 0 when every document is present and verified, 1 when any is unavailable or refused (the report
names them, and says which), 2 when the manifest itself is invalid. Only the standard library is used, so a
CI job needs nothing installed to run it.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "manifest.json"
USER_AGENT = "AdCodicem.Pdf-corpus/1.0 (+https://github.com/AdCodicem/AdCodicem.Pdf; test corpus, ADR 32)"
DEFAULT_CEILING = 512 * 1024 * 1024
ATTEMPTS = 3
CHUNK = 1024 * 1024


class InvalidManifest(Exception):
    pass


def load_entries() -> list[dict]:
    """
    Reads the manifest's remote entries, and refuses anything the rest of the script, or the tests, could not
    trust. A file under remote/ is remote and nothing else is: an entry marked remote elsewhere would be
    silently left out of the tests whenever its file is absent, and a remote file marked otherwise would fail
    every test run that never fetched it.
    """
    documents = []
    for entry in json.loads(MANIFEST.read_text(encoding="utf-8"))["documents"]:
        if (entry.get("origin") == "remote") != entry.get("file", "").startswith("remote/"):
            raise InvalidManifest(f"'{entry.get('file')}': origin \"remote\" and the remote/ folder go together")
        if entry.get("origin") == "remote":
            documents.append(entry)

    seen: set[str] = set()
    for entry in documents:
        name = entry.get("file", "")
        source = entry.get("source") or {}
        if not re.fullmatch(r"remote/[a-z0-9-]+/[a-z0-9.-]+\.pdf", name) or ".." in name:
            raise InvalidManifest(f"'{name}': a remote document lives at remote/<source>/<name>.pdf")
        if name in seen:
            raise InvalidManifest(f"'{name}' is listed twice")
        seen.add(name)
        if not re.fullmatch(r"https?://\S+", source.get("url", "")):
            raise InvalidManifest(f"'{name}': source.url is required")
        if not re.fullmatch(r"[0-9a-f]{64}", source.get("sha256", "")):
            raise InvalidManifest(f"'{name}': source.sha256 is required, as 64 lower-case hex digits")
        if not entry.get("licence"):
            raise InvalidManifest(f"'{name}': the licence must say why the document is remote rather than vendored")
        if "expect" not in entry:
            raise InvalidManifest(f"'{name}': a document nobody asserts anything about is not part of the corpus")
    return documents


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(CHUNK), b""):
            digest.update(block)
    return digest.hexdigest()


def download(url: str, target: Path, ceiling: int) -> str:
    """Streams a URL into target, returning its SHA-256. Raises on a transport failure or an oversized body."""
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    digest = hashlib.sha256()
    received = 0
    with urllib.request.urlopen(request, timeout=60) as response, target.open("wb") as out:  # noqa: S310
        for block in iter(lambda: response.read(CHUNK), b""):
            received += len(block)
            if received > ceiling:
                raise ValueError(f"larger than the {ceiling:,} bytes allowed")
            digest.update(block)
            out.write(block)
    return digest.hexdigest()


def transient(error: Exception) -> bool:
    if isinstance(error, urllib.error.HTTPError):
        return error.code == 429 or error.code >= 500
    return isinstance(error, (urllib.error.URLError, TimeoutError, ConnectionError))


def fetch(entry: dict) -> tuple[str, str]:
    """Returns (state, detail) where state is present, fetched, unavailable or refused."""
    source = entry["source"]
    target = ROOT / entry["file"]
    if target.exists():
        if sha256_of(target) == source["sha256"]:
            return "present", ""
        target.unlink()

    target.parent.mkdir(parents=True, exist_ok=True)
    partial = target.with_name(target.name + ".part")
    ceiling = int(source.get("bytes") or DEFAULT_CEILING)
    for attempt in range(1, ATTEMPTS + 1):
        try:
            digest = download(source["url"], partial, ceiling)
            break
        except Exception as error:  # noqa: BLE001 - every failure is reported, none is fatal to the others
            partial.unlink(missing_ok=True)
            if attempt == ATTEMPTS or not transient(error):
                return "unavailable", f"{type(error).__name__}: {error}"
            time.sleep(5 * attempt)

    if digest != source["sha256"]:
        partial.unlink(missing_ok=True)
        return "refused", (f"the source now serves sha256 {digest}, the manifest pins {source['sha256']}: "
                           "a changed file is a new document, reviewed as one")
    if "bytes" in source and partial.stat().st_size != int(source["bytes"]):
        partial.unlink(missing_ok=True)
        return "refused", f"size {partial.stat().st_size} differs from the pinned {source['bytes']}"
    partial.replace(target)
    return "fetched", ""


def report(rows: list[tuple[str, str, str]]) -> None:
    width = max(len(name) for name, _, _ in rows)
    for name, state, detail in rows:
        print(f"{state:<11} {name:<{width}}  {detail}".rstrip())

    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as out:
            out.write("### Remote corpus (ADR 32)\n\n| State | Document | Detail |\n|---|---|---|\n")
            for name, state, detail in rows:
                out.write(f"| {state} | `{name}` | {detail.replace('|', '/')} |\n")
            out.write("\n")


def main() -> int:
    try:
        entries = load_entries()
    except (InvalidManifest, KeyError, json.JSONDecodeError) as error:
        print(f"tests/corpus/manifest.json is invalid: {error}", file=sys.stderr)
        return 2

    rows = []
    for entry in entries:
        if "--list" in sys.argv[1:]:
            target = ROOT / entry["file"]
            state = "missing" if not target.exists() else (
                "present" if sha256_of(target) == entry["source"]["sha256"] else "stale")
            rows.append((entry["file"], state, entry["source"]["url"]))
        else:
            state, detail = fetch(entry)
            rows.append((entry["file"], state, detail))

    report(rows)
    failed = [row for row in rows if row[1] not in ("present", "fetched")]
    print(f"{len(rows) - len(failed)} of {len(rows)} remote documents available")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
