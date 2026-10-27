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

A document whose license lets the project copy it verbatim is fetched from the project's own copy (ADR 51):
source.url then names a commit or a release of AdCodicem/AdCodicem.Pdf.Corpus, source.copy records the
license review that allowed it, and source.publisher keeps the publisher's URL, tried when the copy cannot be
had. A copy that no longer matches its pins is refused, not replaced by the publisher's file: it is the
project's own mistake, and it is reported as one.

A download is accepted only if its SHA-256 is the one the manifest pins. A file that has changed at its source
is refused, never accepted by updating the hash: it is a new document, to be reviewed as one.

A host that fails is retried with patience: after the back-off below, or after the delay a 429 or a 503 asks
for in Retry-After, capped. Every failed attempt is logged as it happens, a later success included, and the
job summary counts them by host.

    python3 fetch_remote.py          fetch what is missing, verify what is present
    python3 fetch_remote.py --list   report each document's state, fetch nothing
    python3 fetch_remote.py --files  print each remote document's path under tests/corpus, fetch nothing
    python3 fetch_remote.py --pins   print one SHA-256 of every remote document's pin: the cache's key

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
from email.utils import parsedate_to_datetime
from pathlib import Path
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "manifest.json"
USER_AGENT = "AdCodicem.Pdf-corpus/1.0 (+https://github.com/AdCodicem/AdCodicem.Pdf; test corpus, ADR 32)"
ATTEMPTS = 4
# The waits between attempts, in seconds. The outages the job met lasted minutes, not seconds (#204): a host
# refusing connections, an archive timing out, both answering again a few minutes later. Three waits of 15 s,
# 1 min and 2 min give a host more than three minutes to come back, while a host down for the whole run costs a
# document seven minutes at worst (four 60 s timeouts and the waits), and every later document of that host one
# attempt (see Hosts), so the job's hour is never at stake.
BACKOFF = (15, 60, 120)
# A 429 or a 503 may ask for its own delay in Retry-After. It is honored up to the longest back-off: a host never
# holds one document longer than an outage would, and one asking for more is not waited for beyond that.
RETRY_AFTER_CAP = BACKOFF[-1]
CHUNK = 1024 * 1024
SHA256 = re.compile(r"[0-9a-f]{64}")
DATE = re.compile(r"[0-9]{4}-[0-9]{2}-[0-9]{2}")
# The project's own copies (ADR 51): a file at a commit of the corpus repository, or an asset of one of its
# releases, which are immutable - both as permanent as the URLs ADR 32 accepts.
COPY = re.compile(r"https://(raw\.githubusercontent\.com/AdCodicem/AdCodicem\.Pdf\.Corpus/[0-9a-f]{40}/"
                  r"|github\.com/AdCodicem/AdCodicem\.Pdf\.Corpus/releases/download/[A-Za-z0-9._-]+/)[!-~]+")
URL = re.compile(r"https?://[!-~]+")


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


def check_copy(name: str, source: dict, url: str) -> None:
    """A copy of the project's own comes with the review that allowed it and the publisher it was copied from."""
    copy = source.get("copy")
    publisher = source.get("publisher")
    if copy is None and publisher is None:
        if COPY.fullmatch(url):
            raise InvalidManifest(f"'{name}': a URL of the project's copy needs source.copy, the review that allowed it")
        return
    if not isinstance(copy, dict) or not isinstance(copy.get("reviewed"), str) \
            or not DATE.fullmatch(copy["reviewed"]) or not isinstance(copy.get("terms"), str) or not copy["terms"]:
        raise InvalidManifest(f"'{name}': source.copy needs the date it was reviewed and the terms that allow the copy")
    if not isinstance(publisher, str) or not URL.fullmatch(publisher):
        raise InvalidManifest(f"'{name}': source.publisher, the URL the copy was taken from, is required with a copy")
    if not COPY.fullmatch(url):
        raise InvalidManifest(f"'{name}': with source.copy, source.url names a commit or a release of the copy")


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
        if not isinstance(url, str) or not URL.fullmatch(url):
            raise InvalidManifest(f"'{name}': source.url is required")
        if not hex64(source.get("sha256")):
            raise InvalidManifest(f"'{name}': source.sha256 is required, as 64 lower-case hex digits")
        if not positive(source.get("bytes")):
            raise InvalidManifest(f"'{name}': source.bytes is required, the document's exact size")
        if not entry.get("license"):
            raise InvalidManifest(f"'{name}': the license must say why the document is remote rather than vendored")
        if "expect" not in entry:
            raise InvalidManifest(f"'{name}': a document nobody asserts anything about is not part of the corpus")
        check_copy(name, source, url)

        archive = source.get("archive")
        pin = None
        if archive is not None:
            if not isinstance(archive, dict) or not hex64(archive.get("sha256")) \
                    or not positive(archive.get("bytes")) or not valid_member(archive.get("member")):
                raise InvalidManifest(f"'{name}': source.archive needs a sha256, a size in bytes and a relative member path")
            pin = (archive["sha256"], archive["bytes"], source.get("publisher"))
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


def retry_after(error: Exception) -> float | None:
    """
    The delay a 429 or a 503 asks for, in seconds or as an HTTP date, capped at RETRY_AFTER_CAP; None when it
    asks for none, or in a form that cannot be read.
    """
    if not isinstance(error, urllib.error.HTTPError) or error.code not in (429, 503) or error.headers is None:
        return None
    value = (error.headers.get("Retry-After") or "").strip()
    if re.fullmatch(r"[0-9]{1,10}", value):
        delay = float(value)
    else:
        try:
            when = parsedate_to_datetime(value)
        except (TypeError, ValueError, IndexError):
            return None
        if when.tzinfo is None:
            return None
        delay = when.timestamp() - time.time()
    return min(max(delay, 0.0), RETRY_AFTER_CAP)


def described(error: Exception) -> str:
    if isinstance(error, urllib.error.HTTPError):
        return f"HTTP {error.code}"
    return f"{type(error).__name__}: {error}"


class Hosts:
    """
    What each host did during one run: the attempts that failed, counted for the job summary, and the hosts
    that wore out every attempt on a document. Such a host is most likely down, so each later document it
    serves gets one attempt - enough to notice it is back, without holding the job, or the host, for minutes
    more per document.
    """

    def __init__(self) -> None:
        self.failed: dict[str, int] = {}
        self._down: set[str] = set()

    def attempts(self, host: str) -> int:
        return 1 if host in self._down else ATTEMPTS

    def failure(self, host: str) -> None:
        self.failed[host] = self.failed.get(host, 0) + 1

    def down(self, host: str) -> None:
        self._down.add(host)


def download_pinned(url: str, target: Path, sha256: str, size: int, what: str, label: str,
                    hosts: Hosts) -> tuple[str, str]:
    """
    Downloads url into target and checks it against its pins. Returns ("fetched", "") with target in place,
    or (state, detail) with target removed, where state is unavailable or refused. Each failed attempt is
    printed as it happens, naming label, the attempt and what went wrong.
    """
    host = urlsplit(url).hostname or url
    attempts = hosts.attempts(host)
    for attempt in range(1, attempts + 1):
        try:
            digest = download(url, target, size)
            break
        except Oversized as error:
            target.unlink(missing_ok=True)
            return "refused", f"{what}: {error}: a changed file is a new document, reviewed as one"
        except Exception as error:  # noqa: BLE001 - every failure is reported, none is fatal to the others
            target.unlink(missing_ok=True)
            hosts.failure(host)
            last = attempt == attempts or not transient(error)
            asked = None if last else retry_after(error)
            if isinstance(error, urllib.error.HTTPError):
                error.close()
            wait = "" if last else (f"; retrying in {asked:.0f} s, as Retry-After asks" if asked is not None
                                    else f"; retrying in {BACKOFF[attempt - 1]} s")
            print(f"attempt     {label} from {host}, {attempt} of {attempts}: {described(error)}{wait}", flush=True)
            if last:
                if transient(error) and attempts == ATTEMPTS:
                    hosts.down(host)
                return "unavailable", f"{what}: {type(error).__name__}: {error}"
            time.sleep(asked if asked is not None else BACKOFF[attempt - 1])

    if digest != sha256:
        target.unlink(missing_ok=True)
        return "refused", (f"{what}: the source now serves sha256 {digest}, the manifest pins {sha256}: "
                           "a changed file is a new document, reviewed as one")
    if target.stat().st_size != size:
        actual = target.stat().st_size
        target.unlink(missing_ok=True)
        return "refused", f"{what}: size {actual} differs from the pinned {size}"
    return "fetched", ""


def download_with_fallback(source: dict, target: Path, sha256: str, size: int, what: str, label: str,
                           hosts: Hosts) -> tuple[str, str]:
    """
    Downloads from source.url, then, if it cannot be had at all, from source.publisher. Only an unavailable
    copy falls back: a refused one is the project's own copy gone wrong, and is reported rather than hidden.
    """
    state, detail = download_pinned(source["url"], target, sha256, size, what, label, hosts)
    publisher = source.get("publisher")
    if state != "unavailable" or publisher is None:
        return state, detail
    print(f"fallback    {label}: the copy is unavailable, trying its publisher", flush=True)
    state, fallback = download_pinned(publisher, target, sha256, size, what, label, hosts)
    if state == "fetched":
        return state, f"from its publisher, the copy being unavailable ({detail})"
    return state, f"{fallback}; the copy too ({detail})"


class Archives:
    """
    The archives of one run: each downloaded at most once, the first time one of its members is needed, and
    its outcome — failure included — kept for every other member, so an outage costs one set of retries.
    """

    def __init__(self, directory: Path, hosts: Hosts) -> None:
        self._directory = directory
        self._hosts = hosts
        self._opened: dict[str, tarfile.TarFile | tuple[str, str]] = {}
        self.notes: dict[str, str] = {}

    def get(self, source: dict) -> tarfile.TarFile | tuple[str, str]:
        url, archive = source["url"], source["archive"]
        if url not in self._opened:
            path = self._directory / f"archive-{len(self._opened)}"
            state, detail = download_with_fallback(source, path, archive["sha256"], archive["bytes"], "the archive",
                                                   f"the archive {url}", self._hosts)
            if state != "fetched":
                self._opened[url] = (state, detail)
            else:
                self.notes[url] = detail
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


def fetch(entry: dict, archives: Archives, hosts: Hosts) -> tuple[str, str]:
    """Returns (state, detail) where state is present, fetched, unavailable or refused."""
    source = entry["source"]
    target = ROOT / entry["file"]
    if target.exists():
        if sha256_of(target) == source["sha256"]:
            return "present", ""
        target.unlink()

    target.parent.mkdir(parents=True, exist_ok=True)
    partial = target.with_name(target.name + ".part")
    if source.get("archive") is None:
        state, detail = download_with_fallback(source, partial, source["sha256"], source["bytes"], "the document",
                                               entry["file"], hosts)
    else:
        opened = archives.get(source)
        if isinstance(opened, tuple):
            return opened
        state, detail = copy_member(opened, entry, partial)
        if state == "fetched":
            detail = archives.notes.get(source["url"], "")

    if state != "fetched":
        partial.unlink(missing_ok=True)
        return state, detail
    partial.replace(target)
    return "fetched", detail


def report(rows: list[tuple[str, str, str]], hosts: Hosts) -> None:
    width = max(len(name) for name, _, _ in rows)
    for name, state, detail in rows:
        print(f"{state:<11} {name:<{width}}  {detail}".rstrip())
    for host, count in sorted(hosts.failed.items()):
        print(f"{count} failed attempt{'s' if count > 1 else ''} on {host}")

    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as out:
            out.write("### Remote corpus (ADR 32)\n\n")
            if hosts.failed:
                out.write("Failed attempts, a later success included:\n\n| Host | Failed attempts |\n|---|---|\n")
                for host, count in sorted(hosts.failed.items()):
                    out.write(f"| {host} | {count} |\n")
            else:
                out.write("No attempt failed.\n")
            out.write("\n| State | Document | Detail |\n|---|---|---|\n")
            for name, state, detail in rows:
                out.write(f"| {state} | `{name}` | {detail.replace('|', '/')} |\n")
            out.write("\n")


def pins(entries: list[dict]) -> str:
    """
    One SHA-256 over every remote document's path and pinned SHA-256: it changes when a document does, and only
    then - not when a URL moves to a copy that serves the same bytes.
    """
    digest = hashlib.sha256()
    for name, sha256 in sorted((entry["file"], entry["source"]["sha256"]) for entry in entries):
        digest.update(f"{name} {sha256}\n".encode())
    return digest.hexdigest()


def main() -> int:
    try:
        entries = load_entries()
    except (InvalidManifest, KeyError, json.JSONDecodeError) as error:
        print(f"tests/corpus/manifest.json is invalid: {error}", file=sys.stderr)
        return 2

    arguments = sys.argv[1:]
    if "--pins" in arguments:
        print(pins(entries))
        return 0
    if "--files" in arguments:
        for entry in entries:
            print(entry["file"])
        return 0

    rows = []
    hosts = Hosts()
    if "--list" in arguments:
        for entry in entries:
            target = ROOT / entry["file"]
            state = "missing" if not target.exists() else (
                "present" if sha256_of(target) == entry["source"]["sha256"] else "stale")
            archive = entry["source"].get("archive")
            where = entry["source"]["url"] + (f" ({archive['member']})" if archive else "")
            rows.append((entry["file"], state, where))
    else:
        with tempfile.TemporaryDirectory(prefix="remote-corpus-") as directory:
            archives = Archives(Path(directory), hosts)
            try:
                for entry in entries:
                    state, detail = fetch(entry, archives, hosts)
                    rows.append((entry["file"], state, detail))
            finally:
                archives.close()

    report(rows, hosts)
    failed = [row for row in rows if row[1] not in ("present", "fetched")]
    print(f"{len(rows) - len(failed)} of {len(rows)} remote documents available")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
