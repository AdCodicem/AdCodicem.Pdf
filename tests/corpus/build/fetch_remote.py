#!/usr/bin/env python3
"""Fetches the remote corpus: the documents we may use in tests but not redistribute (ADR 32).

tests/corpus/manifest.json describes them among the other documents, with origin "remote", the URL each is
fetched from and the SHA-256 and size it must have. They land in tests/corpus/remote/, which git ignores:
nothing fetched is ever committed, nor anything derived from it. The test suite leaves out the remote entries
whose file is absent, so a machine that never runs this script tests exactly the committed corpus.

A document published only inside an archive (ADR 33) names that archive in source.url and pins it in
source.archive (its SHA-256, its size and the member to take out of it); source.sha256 and source.bytes still
pin the document itself. The archive is downloaded at most once per run, only if one of its members is
needed, into a temporary directory deleted at the end; the member is copied out of it, never extracted by
its own path.

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
import tarfile
import tempfile
import time
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "manifest.json"
USER_AGENT = "AdCodicem.Pdf-corpus/1.0 (+https://github.com/AdCodicem/AdCodicem.Pdf; test corpus, ADR 32)"
ATTEMPTS = 3
CHUNK = 1024 * 1024
SHA256 = re.compile(r"[0-9a-f]{64}")


class InvalidManifest(Exception):
    pass


class Oversized(Exception):
    """The source serves more bytes than the manifest pins: the file changed, it did not go missing."""


def positive(value: object) -> bool:
    return isinstance(value, int) and not isinstance(value, bool) and value > 0


def hex64(value: object) -> bool:
    return isinstance(value, str) and SHA256.fullmatch(value) is not None


def valid_member(name: object) -> bool:
    """A member is named relative to the archive's root, in POSIX form, and never climbs out of it."""
    if not isinstance(name, str) or not name or name.startswith("/") or "\\" in name:
        return False
    if any(ord(character) < 32 or ord(character) == 127 for character in name):
        return False
    return all(part not in ("", ".", "..") for part in name.split("/"))


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
    pins: dict[str, tuple | None] = {}
    members: set[tuple[str, str]] = set()
    for entry in documents:
        name = entry.get("file", "")
        source = entry.get("source") or {}
        if not isinstance(name, str) or not isinstance(source, dict):
            raise InvalidManifest(f"'{name}': file is a path and source an object")
        if not re.fullmatch(r"remote/[a-z0-9-]+/[a-z0-9.-]+\.pdf", name) or ".." in name:
            raise InvalidManifest(f"'{name}': a remote document lives at remote/<source>/<name>.pdf")
        if name in seen:
            raise InvalidManifest(f"'{name}' is listed twice")
        seen.add(name)
        url = source.get("url", "")
        if not isinstance(url, str) or not re.fullmatch(r"https?://\S+", url):
            raise InvalidManifest(f"'{name}': source.url is required")
        if not hex64(source.get("sha256")):
            raise InvalidManifest(f"'{name}': source.sha256 is required, as 64 lower-case hex digits")
        if not positive(source.get("bytes")):
            raise InvalidManifest(f"'{name}': source.bytes is required, the document's exact size")
        if not entry.get("licence"):
            raise InvalidManifest(f"'{name}': the licence must say why the document is remote rather than vendored")
        if "expect" not in entry:
            raise InvalidManifest(f"'{name}': a document nobody asserts anything about is not part of the corpus")

        archive = source.get("archive")
        pin = None
        if archive is not None:
            if not isinstance(archive, dict) or not hex64(archive.get("sha256")) \
                    or not positive(archive.get("bytes")) or not valid_member(archive.get("member")):
                raise InvalidManifest(f"'{name}': source.archive needs a sha256, a size in bytes and a relative member path")
            pin = (archive["sha256"], archive["bytes"])
            if (url, archive["member"]) in members:
                raise InvalidManifest(f"'{name}': member '{archive['member']}' of {url} is taken twice")
            members.add((url, archive["member"]))
        # One URL serves one thing: a document, or one archive pinned the same way by every entry naming it.
        if url in pins and pins[url] != pin:
            raise InvalidManifest(f"'{name}': {url} is pinned differently by another entry")
        pins[url] = pin
    return documents


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(CHUNK), b""):
            digest.update(block)
    return digest.hexdigest()


def download(url: str, target: Path, ceiling: int) -> str:
    """Streams a URL into target, returning its SHA-256. Raises on a transport failure, Oversized past the ceiling."""
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    digest = hashlib.sha256()
    received = 0
    with urllib.request.urlopen(request, timeout=60) as response, target.open("wb") as out:  # noqa: S310
        for block in iter(lambda: response.read(CHUNK), b""):
            received += len(block)
            if received > ceiling:
                raise Oversized(f"the source now serves more than the pinned {ceiling:,} bytes")
            digest.update(block)
            out.write(block)
    return digest.hexdigest()


def transient(error: Exception) -> bool:
    if isinstance(error, urllib.error.HTTPError):
        return error.code == 429 or error.code >= 500
    return isinstance(error, (urllib.error.URLError, TimeoutError, ConnectionError))


def download_pinned(url: str, target: Path, sha256: str, size: int, what: str) -> tuple[str, str]:
    """
    Downloads url into target and checks it against its pins. Returns ("fetched", "") with target in place,
    or (state, detail) with target removed, where state is unavailable or refused.
    """
    for attempt in range(1, ATTEMPTS + 1):
        try:
            digest = download(url, target, size)
            break
        except Oversized as error:
            target.unlink(missing_ok=True)
            return "refused", f"{what}: {error}: a changed file is a new document, reviewed as one"
        except Exception as error:  # noqa: BLE001 - every failure is reported, none is fatal to the others
            target.unlink(missing_ok=True)
            if attempt == ATTEMPTS or not transient(error):
                return "unavailable", f"{what}: {type(error).__name__}: {error}"
            time.sleep(5 * attempt)

    if digest != sha256:
        target.unlink(missing_ok=True)
        return "refused", (f"{what}: the source now serves sha256 {digest}, the manifest pins {sha256}: "
                           "a changed file is a new document, reviewed as one")
    if target.stat().st_size != size:
        actual = target.stat().st_size
        target.unlink(missing_ok=True)
        return "refused", f"{what}: size {actual} differs from the pinned {size}"
    return "fetched", ""


class Archives:
    """
    The archives of one run: each downloaded at most once, the first time one of its members is needed, and
    its outcome — failure included — kept for every other member, so an outage costs one set of retries.
    """

    def __init__(self, directory: Path) -> None:
        self._directory = directory
        self._opened: dict[str, tarfile.TarFile | tuple[str, str]] = {}

    def get(self, url: str, archive: dict) -> tarfile.TarFile | tuple[str, str]:
        if url not in self._opened:
            path = self._directory / f"archive-{len(self._opened)}"
            state, detail = download_pinned(url, path, archive["sha256"], archive["bytes"], "the archive")
            if state != "fetched":
                self._opened[url] = (state, detail)
            else:
                opened = None
                try:
                    # Uncompressed only: its headers are then bounded by the pinned size, and a compressed
                    # archive would be a new review rather than an accident. The SHA-256 matched first. Every
                    # header is read here, so a damaged one refuses the archive rather than ending the run.
                    opened = tarfile.open(path, mode="r:")  # noqa: SIM115 - closed in close()
                    opened.getmembers()
                    self._opened[url] = opened
                except (tarfile.TarError, OSError) as error:
                    if opened is not None:
                        opened.close()
                    self._opened[url] = ("refused", f"the archive: not a readable uncompressed tar ({error})")
        return self._opened[url]

    def close(self) -> None:
        for opened in self._opened.values():
            if isinstance(opened, tarfile.TarFile):
                opened.close()


def copy_member(archive: tarfile.TarFile, entry: dict, target: Path) -> tuple[str, str]:
    """
    Copies one member out of an opened archive into target — never extracted by its own path — and checks it
    against the entry's pins.
    """
    source = entry["source"]
    name = source["archive"]["member"]
    try:
        member = archive.getmember(name)
    except KeyError:
        return "refused", f"member {name} is not in the pinned archive"
    if not member.isreg():
        return "refused", f"member {name} is not a regular file"
    if member.size != source["bytes"]:
        return "refused", f"member {name}: size {member.size} differs from the pinned {source['bytes']}"

    digest = hashlib.sha256()
    try:
        stream = archive.extractfile(member)
        if stream is None:
            return "refused", f"member {name} cannot be read"
        with stream, target.open("wb") as out:
            remaining = source["bytes"]
            while remaining > 0:
                block = stream.read(min(CHUNK, remaining))
                if not block:
                    break
                remaining -= len(block)
                digest.update(block)
                out.write(block)
    except (tarfile.TarError, OSError) as error:
        target.unlink(missing_ok=True)
        return "refused", f"member {name} cannot be read ({error})"
    if digest.hexdigest() != source["sha256"]:
        target.unlink(missing_ok=True)
        return "refused", (f"member {name}: sha256 {digest.hexdigest()}, the manifest pins {source['sha256']}: "
                           "a changed file is a new document, reviewed as one")
    return "fetched", ""


def fetch(entry: dict, archives: Archives) -> tuple[str, str]:
    """Returns (state, detail) where state is present, fetched, unavailable or refused."""
    source = entry["source"]
    target = ROOT / entry["file"]
    if target.exists():
        if sha256_of(target) == source["sha256"]:
            return "present", ""
        target.unlink()

    target.parent.mkdir(parents=True, exist_ok=True)
    partial = target.with_name(target.name + ".part")
    archive = source.get("archive")
    if archive is None:
        state, detail = download_pinned(source["url"], partial, source["sha256"], source["bytes"], "the document")
    else:
        opened = archives.get(source["url"], archive)
        if isinstance(opened, tuple):
            return opened
        state, detail = copy_member(opened, entry, partial)

    if state != "fetched":
        partial.unlink(missing_ok=True)
        return state, detail
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
    if "--list" in sys.argv[1:]:
        for entry in entries:
            target = ROOT / entry["file"]
            state = "missing" if not target.exists() else (
                "present" if sha256_of(target) == entry["source"]["sha256"] else "stale")
            archive = entry["source"].get("archive")
            where = entry["source"]["url"] + (f" ({archive['member']})" if archive else "")
            rows.append((entry["file"], state, where))
    else:
        with tempfile.TemporaryDirectory(prefix="remote-corpus-") as directory:
            archives = Archives(Path(directory))
            try:
                for entry in entries:
                    state, detail = fetch(entry, archives)
                    rows.append((entry["file"], state, detail))
            finally:
                archives.close()

    report(rows)
    failed = [row for row in rows if row[1] not in ("present", "fetched")]
    print(f"{len(rows) - len(failed)} of {len(rows)} remote documents available")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
