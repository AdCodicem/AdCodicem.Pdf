"""The rules fetch_remote.py enforces, checked against a local server rather than the network (ADR 32, ADR 33).

The remote corpus itself is fetched only by the nightly job; these tests run on every change, so a fetcher
that would accept a changed file, extract a member by its own path, or retry an outage once per member is
caught before it reaches that job.

    python3 -m unittest discover -s tests/corpus/build -p "test_*.py"
"""

from __future__ import annotations

import hashlib
import io
import json
import os
import re
import sys
import tarfile
import tempfile
import threading
import time
import unittest
from email.utils import formatdate
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import fetch_remote  # noqa: E402

PDF = b"%PDF-1.4\n%%EOF\n"
# The pattern of the project's copies, kept before the tests point it at their local server.
REAL_COPY = fetch_remote.COPY


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def tar_of(members: dict[str, bytes], links: dict[str, str] | None = None, mode: str = "w",
           specials: dict[str, bytes] | None = None, form: int = tarfile.DEFAULT_FORMAT) -> bytes:
    """A tar of regular members, symbolic links (name -> target) and special members (name -> tarfile type)."""
    buffer = io.BytesIO()
    with tarfile.open(fileobj=buffer, mode=mode, format=form) as archive:
        for name, data in members.items():
            info = tarfile.TarInfo(name)
            info.size = len(data)
            archive.addfile(info, io.BytesIO(data))
        for name, target in (links or {}).items():
            info = tarfile.TarInfo(name)
            info.type = tarfile.SYMTYPE
            info.linkname = target
            archive.addfile(info)
        for name, kind in (specials or {}).items():
            info = tarfile.TarInfo(name)
            info.type = kind
            if kind == tarfile.LNKTYPE:
                info.linkname = next(iter(members))
            archive.addfile(info)
    return buffer.getvalue()


class Server:
    """
    Serves fixed responses by path and counts the requests each path received. A route is a status and a body,
    with headers if it needs any, or a list of them answered in turn, the last one repeated.
    """

    def __init__(self) -> None:
        self.routes: dict[str, tuple | list[tuple]] = {}
        self.requests: dict[str, int] = {}
        server = self

        class Handler(BaseHTTPRequestHandler):
            def do_GET(self) -> None:  # noqa: N802 - the name http.server calls
                server.requests[self.path] = server.requests.get(self.path, 0) + 1
                route = server.routes.get(self.path, (404, b""))
                if isinstance(route, list):
                    route = route[min(server.requests[self.path], len(route)) - 1]
                status, body, *headers = route
                self.send_response(status)
                for name, value in (headers[0] if headers else {}).items():
                    self.send_header(name, value)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def log_message(self, *args: object) -> None:
                pass

        self._httpd = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.base = f"http://127.0.0.1:{self._httpd.server_address[1]}"
        threading.Thread(target=self._httpd.serve_forever, daemon=True).start()

    def close(self) -> None:
        self._httpd.shutdown()
        self._httpd.server_close()


class FetchRemoteTests(unittest.TestCase):
    def setUp(self) -> None:
        self.server = Server()
        self.addCleanup(self.server.close)
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name) / "corpus"
        self.root.mkdir()
        # The fetcher's own scratch space, to prove it is left empty.
        self.scratch = Path(directory.name) / "scratch"
        self.scratch.mkdir()
        self.sleeps: list[float] = []
        patches = [
            mock.patch.object(fetch_remote, "ROOT", self.root),
            mock.patch.object(fetch_remote, "MANIFEST", self.root / "manifest.json"),
            # A retried outage must cost attempts, not wall-clock time; the waits asked for are kept to be checked.
            mock.patch.object(fetch_remote.time, "sleep", self.sleeps.append),
            # The project's copy is served here too, under /copy/.
            mock.patch.object(fetch_remote, "COPY", re.compile(re.escape(self.server.base) + r"/copy/[!-~]+")),
            mock.patch.dict(os.environ, {"no_proxy": "127.0.0.1", "NO_PROXY": "127.0.0.1"}),
            mock.patch.object(fetch_remote.tempfile, "tempdir", str(self.scratch)),
        ]
        for patch in patches:
            patch.start()
            self.addCleanup(patch.stop)
        # In CI the fetcher would append its report to the job's summary: these runs are not the corpus's.
        os.environ.pop("GITHUB_STEP_SUMMARY", None)

    def entry(self, name: str, url: str, data: bytes, archive: dict | None = None) -> dict:
        source = {"url": url, "sha256": sha(data), "bytes": len(data)}
        if archive is not None:
            source["archive"] = archive
        return {"file": f"remote/test/{name}.pdf", "origin": "remote", "license": "test", "source": source,
                "expect": {}}

    def archived(self, names: list[str], tar: bytes, path: str = "/bag.tar") -> list[dict]:
        pin = {"sha256": sha(tar), "bytes": len(tar)}
        return [self.entry(name.lower().replace("_", "-"), self.server.base + path, PDF + name.encode(),
                           {**pin, "member": f"bag/data/{name}.pdf"}) for name in names]

    def copied(self, entry: dict, path: str) -> dict:
        """The entry fetched from the project's copy at path, its publisher kept as the fallback (ADR 51)."""
        entry["source"]["publisher"] = entry["source"]["url"]
        entry["source"]["url"] = self.server.base + path
        entry["source"]["copy"] = {"reviewed": "2026-10-27", "terms": "test"}
        return entry

    def run_fetch(self, entries: list[dict], *arguments: str) -> tuple[int, str]:
        (self.root / "manifest.json").write_text(json.dumps({"documents": entries}), encoding="utf-8")
        output = io.StringIO()
        with mock.patch.object(sys, "argv", ["fetch_remote.py", *arguments]), mock.patch("sys.stdout", output):
            code = fetch_remote.main()
        return code, output.getvalue()

    def fetched(self, name: str) -> bytes | None:
        path = self.root / "remote" / "test" / f"{name}.pdf"
        return path.read_bytes() if path.exists() else None

    def files_written(self) -> list[str]:
        return sorted(path.relative_to(self.root).as_posix() for path in self.root.rglob("*") if path.is_file())

    # --- a document fetched on its own ---------------------------------------------------------------------

    def test_a_document_is_fetched_once_and_then_found_present(self) -> None:
        self.server.routes["/one.pdf"] = (200, PDF)
        entries = [self.entry("one", self.server.base + "/one.pdf", PDF)]

        self.assertEqual(self.run_fetch(entries)[0], 0)
        self.assertEqual(self.fetched("one"), PDF)

        code, output = self.run_fetch(entries)
        self.assertEqual(code, 0)
        self.assertIn("present", output)
        self.assertEqual(self.server.requests["/one.pdf"], 1)

    def test_a_document_changed_at_its_source_is_refused_and_not_kept(self) -> None:
        self.server.routes["/one.pdf"] = (200, PDF + b"changed")
        code, output = self.run_fetch([self.entry("one", self.server.base + "/one.pdf", PDF)])

        self.assertEqual(code, 1)
        self.assertIn("refused", output)
        self.assertIsNone(self.fetched("one"))

    # --- a host that fails, or asks to wait (#204) ---------------------------------------------------------

    def test_a_429_is_retried_after_the_delay_its_retry_after_asks(self) -> None:
        self.server.routes["/one.pdf"] = [(429, b"", {"Retry-After": "7"}), (200, PDF)]

        code, output = self.run_fetch([self.entry("one", self.server.base + "/one.pdf", PDF)])

        self.assertEqual(code, 0)
        self.assertEqual(self.sleeps, [7])
        self.assertIn("retrying in 7 s, as Retry-After asks", output)

    def test_a_retry_after_given_as_an_http_date_is_honored(self) -> None:
        when = formatdate(time.time() + 30, usegmt=True)
        self.server.routes["/one.pdf"] = [(503, b"", {"Retry-After": when}), (200, PDF)]

        code, _ = self.run_fetch([self.entry("one", self.server.base + "/one.pdf", PDF)])

        self.assertEqual(code, 0)
        self.assertEqual(len(self.sleeps), 1)
        self.assertTrue(25 <= self.sleeps[0] <= 30, self.sleeps)

    def test_a_retry_after_longer_than_the_cap_waits_the_cap(self) -> None:
        for asked in ("86400", formatdate(time.time() + 86400, usegmt=True)):
            with self.subTest(asked=asked):
                self.sleeps.clear()
                self.server.requests.clear()
                self.server.routes["/one.pdf"] = [(429, b"", {"Retry-After": asked}), (200, PDF)]
                (self.root / "remote" / "test" / "one.pdf").unlink(missing_ok=True)

                code, _ = self.run_fetch([self.entry("one", self.server.base + "/one.pdf", PDF)])

                self.assertEqual(code, 0)
                self.assertEqual(self.sleeps, [fetch_remote.RETRY_AFTER_CAP])

    def test_a_retry_after_in_the_past_retries_at_once(self) -> None:
        self.server.routes["/one.pdf"] = [(503, b"", {"Retry-After": "Wed, 21 Oct 2015 07:28:00 GMT"}), (200, PDF)]

        code, _ = self.run_fetch([self.entry("one", self.server.base + "/one.pdf", PDF)])

        self.assertEqual(code, 0)
        self.assertEqual(self.sleeps, [0])

    def test_a_503_without_retry_after_keeps_the_back_off(self) -> None:
        self.server.routes["/one.pdf"] = [(503, b""), (503, b""), (200, PDF)]

        code, _ = self.run_fetch([self.entry("one", self.server.base + "/one.pdf", PDF)])

        self.assertEqual(code, 0)
        self.assertEqual(self.sleeps, list(fetch_remote.BACKOFF[:2]))

    def test_a_retry_after_that_cannot_be_read_keeps_the_back_off(self) -> None:
        for asked in ("soon", "-5", "1.5", "99999999999"):
            with self.subTest(asked=asked):
                self.sleeps.clear()
                self.server.requests.clear()
                self.server.routes["/one.pdf"] = [(429, b"", {"Retry-After": asked}), (200, PDF)]
                (self.root / "remote" / "test" / "one.pdf").unlink(missing_ok=True)

                code, _ = self.run_fetch([self.entry("one", self.server.base + "/one.pdf", PDF)])

                self.assertEqual(code, 0)
                self.assertEqual(self.sleeps, [fetch_remote.BACKOFF[0]])

    def test_retry_after_is_ignored_on_a_status_that_does_not_define_it(self) -> None:
        self.server.routes["/one.pdf"] = [(500, b"", {"Retry-After": "7"}), (200, PDF)]

        code, _ = self.run_fetch([self.entry("one", self.server.base + "/one.pdf", PDF)])

        self.assertEqual(code, 0)
        self.assertEqual(self.sleeps, [fetch_remote.BACKOFF[0]])

    def test_every_failed_attempt_is_logged_and_counted_a_later_success_included(self) -> None:
        summary = self.root / "summary.md"
        self.server.routes["/one.pdf"] = [(429, b""), (502, b""), (200, PDF)]

        with mock.patch.dict(os.environ, {"GITHUB_STEP_SUMMARY": str(summary)}):
            code, output = self.run_fetch([self.entry("one", self.server.base + "/one.pdf", PDF)])

        self.assertEqual(code, 0)
        self.assertIn("remote/test/one.pdf from 127.0.0.1, 1 of 4: HTTP 429", output)
        self.assertIn("remote/test/one.pdf from 127.0.0.1, 2 of 4: HTTP 502", output)
        self.assertIn("fetched", output)
        self.assertIn("| 127.0.0.1 | 2 |", summary.read_text(encoding="utf-8"))

    def test_a_run_without_a_failed_attempt_says_so(self) -> None:
        summary = self.root / "summary.md"
        self.server.routes["/one.pdf"] = (200, PDF)

        with mock.patch.dict(os.environ, {"GITHUB_STEP_SUMMARY": str(summary)}):
            self.run_fetch([self.entry("one", self.server.base + "/one.pdf", PDF)])

        self.assertIn("No attempt failed.", summary.read_text(encoding="utf-8"))

    def test_a_network_error_is_logged_with_its_attempt(self) -> None:
        # Nothing listens on port 9 of the loopback: every attempt is refused.
        code, output = self.run_fetch([self.entry("one", "http://127.0.0.1:9/one.pdf", PDF)])

        self.assertEqual(code, 1)
        self.assertIn(f"from 127.0.0.1, {fetch_remote.ATTEMPTS} of {fetch_remote.ATTEMPTS}: URLError", output)
        self.assertEqual(len(self.sleeps), fetch_remote.ATTEMPTS - 1)

    def test_a_host_that_wore_out_every_attempt_gets_one_per_later_document(self) -> None:
        self.server.routes["/one.pdf"] = (503, b"")
        self.server.routes["/two.pdf"] = (503, b"")
        entries = [self.entry("one", self.server.base + "/one.pdf", PDF),
                   self.entry("two", self.server.base + "/two.pdf", PDF + b"2")]

        code, output = self.run_fetch(entries)

        self.assertEqual(code, 1)
        self.assertEqual(self.server.requests, {"/one.pdf": fetch_remote.ATTEMPTS, "/two.pdf": 1})
        self.assertIn("remote/test/two.pdf from 127.0.0.1, 1 of 1: HTTP 503", output)

    def test_a_document_that_is_not_found_is_not_retried(self) -> None:
        code, _ = self.run_fetch([self.entry("one", self.server.base + "/one.pdf", PDF)])

        self.assertEqual(code, 1)
        self.assertEqual(self.server.requests["/one.pdf"], 1)
        self.assertEqual(self.sleeps, [])

    # --- the project's own copy (ADR 51) -------------------------------------------------------------------

    def test_a_copied_document_is_fetched_from_the_copy(self) -> None:
        self.server.routes["/copy/one.pdf"] = (200, PDF)
        entry = self.copied(self.entry("one", self.server.base + "/one.pdf", PDF), "/copy/one.pdf")

        code, _ = self.run_fetch([entry])

        self.assertEqual(code, 0)
        self.assertEqual(self.fetched("one"), PDF)
        self.assertNotIn("/one.pdf", self.server.requests)

    def test_an_unavailable_copy_falls_back_to_its_publisher(self) -> None:
        self.server.routes["/copy/one.pdf"] = (503, b"")
        self.server.routes["/one.pdf"] = (200, PDF)
        entry = self.copied(self.entry("one", self.server.base + "/one.pdf", PDF), "/copy/one.pdf")

        code, output = self.run_fetch([entry])

        self.assertEqual(code, 0)
        self.assertEqual(self.fetched("one"), PDF)
        self.assertIn("from its publisher, the copy being unavailable", output)

    def test_a_copy_and_its_publisher_both_unavailable_name_both(self) -> None:
        self.server.routes["/copy/one.pdf"] = (503, b"")
        entry = self.copied(self.entry("one", self.server.base + "/one.pdf", PDF), "/copy/one.pdf")

        code, output = self.run_fetch([entry])

        self.assertEqual(code, 1)
        self.assertIn("HTTP Error 404", output)
        self.assertIn("the copy too", output)

    def test_a_copy_that_changed_is_refused_and_its_publisher_is_not_asked(self) -> None:
        self.server.routes["/copy/one.pdf"] = (200, PDF + b"changed")
        self.server.routes["/one.pdf"] = (200, PDF)
        entry = self.copied(self.entry("one", self.server.base + "/one.pdf", PDF), "/copy/one.pdf")

        code, output = self.run_fetch([entry])

        self.assertEqual(code, 1)
        self.assertIn("refused", output)
        self.assertIsNone(self.fetched("one"))
        self.assertNotIn("/one.pdf", self.server.requests)

    def test_an_archive_copy_falls_back_once_for_all_its_members(self) -> None:
        names = ["T01_001", "T02_002"]
        tar = tar_of({f"bag/data/{name}.pdf": PDF + name.encode() for name in names})
        self.server.routes["/copy/bag.tar"] = (503, b"")
        self.server.routes["/bag.tar"] = (200, tar)
        entries = [self.copied(entry, "/copy/bag.tar") for entry in self.archived(names, tar)]

        code, output = self.run_fetch(entries)

        self.assertEqual(code, 0)
        self.assertEqual(self.server.requests, {"/copy/bag.tar": fetch_remote.ATTEMPTS, "/bag.tar": 1})
        self.assertEqual(output.count("from its publisher"), 2)

    def test_the_cache_key_follows_the_pins_not_the_urls(self) -> None:
        before = self.run_fetch([self.entry("one", self.server.base + "/one.pdf", PDF)], "--pins")[1]
        moved = self.run_fetch([self.copied(self.entry("one", self.server.base + "/one.pdf", PDF), "/copy/one.pdf")],
                               "--pins")[1]
        changed = self.run_fetch([self.entry("one", self.server.base + "/one.pdf", PDF + b"new")], "--pins")[1]

        self.assertRegex(before, r"^[0-9a-f]{64}\n$")
        self.assertEqual(moved, before)
        self.assertNotEqual(changed, before)
        self.assertEqual(self.server.requests, {})

    def test_files_lists_every_remote_document_and_fetches_nothing(self) -> None:
        entries = [self.entry("one", self.server.base + "/one.pdf", PDF),
                   self.entry("two", self.server.base + "/two.pdf", PDF + b"2")]

        code, output = self.run_fetch(entries, "--files")

        self.assertEqual(code, 0)
        self.assertEqual(output.splitlines(), ["remote/test/one.pdf", "remote/test/two.pdf"])
        self.assertEqual(self.server.requests, {})

    # --- documents taken out of an archive (ADR 33) --------------------------------------------------------

    def test_members_come_from_a_single_download_of_their_archive(self) -> None:
        names = ["T01_001", "T02_002", "T03_003"]
        tar = tar_of({f"bag/data/{name}.pdf": PDF + name.encode() for name in names})
        self.server.routes["/bag.tar"] = (200, tar)

        code, _ = self.run_fetch(self.archived(names, tar))

        self.assertEqual(code, 0)
        self.assertEqual(self.server.requests["/bag.tar"], 1)
        for name in names:
            self.assertEqual(self.fetched(name.lower().replace("_", "-")), PDF + name.encode())

    def test_an_archive_is_not_downloaded_when_its_members_are_present(self) -> None:
        tar = tar_of({"bag/data/T01_001.pdf": PDF + b"T01_001"})
        self.server.routes["/bag.tar"] = (200, tar)
        entries = self.archived(["T01_001"], tar)
        self.run_fetch(entries)

        code, output = self.run_fetch(entries)

        self.assertEqual(code, 0)
        self.assertIn("present", output)
        self.assertEqual(self.server.requests["/bag.tar"], 1)

    def test_an_unavailable_archive_costs_one_set_of_retries_for_all_its_members(self) -> None:
        self.server.routes["/bag.tar"] = (503, b"")
        tar = tar_of({f"bag/data/T0{n}.pdf": PDF for n in range(5)})

        code, output = self.run_fetch(self.archived([f"T0{n}" for n in range(5)], tar))

        self.assertEqual(code, 1)
        self.assertEqual(output.count("unavailable"), 5)
        self.assertEqual(self.server.requests["/bag.tar"], fetch_remote.ATTEMPTS)

    def test_an_archive_changed_at_its_source_is_refused_for_every_member(self) -> None:
        tar = tar_of({"bag/data/T01_001.pdf": PDF + b"T01_001", "bag/data/T02_002.pdf": PDF + b"T02_002"})
        entries = self.archived(["T01_001", "T02_002"], tar)
        self.server.routes["/bag.tar"] = (200, tar_of({"bag/data/T01_001.pdf": PDF + b"T01_001"}))

        code, output = self.run_fetch(entries)

        self.assertEqual(code, 1)
        self.assertEqual(output.count("refused"), 2)
        self.assertIn("the archive", output)
        self.assertEqual(self.server.requests["/bag.tar"], 1)
        self.assertEqual(self.files_written(), ["manifest.json"])

    def test_a_member_absent_linked_or_of_another_size_is_refused_and_the_others_fetched(self) -> None:
        tar = tar_of({"bag/data/GOOD.pdf": PDF + b"GOOD", "bag/data/SIZE.pdf": PDF + b"SIZE, and more"},
                     links={"bag/data/LINK.pdf": "GOOD.pdf"})
        self.server.routes["/bag.tar"] = (200, tar)

        code, output = self.run_fetch(self.archived(["GOOD", "ABSENT", "LINK", "SIZE"], tar))

        self.assertEqual(code, 1)
        self.assertEqual(self.fetched("good"), PDF + b"GOOD")
        for name, reason in (("absent", "is not in the pinned archive"), ("link", "is not a regular file"),
                             ("size", "differs from the pinned")):
            self.assertIsNone(self.fetched(name))
            self.assertIn(reason, output)

    def test_a_hard_link_or_device_member_is_refused(self) -> None:
        tar = tar_of({"bag/data/GOOD.pdf": PDF + b"GOOD"},
                     specials={"bag/data/HARD.pdf": tarfile.LNKTYPE, "bag/data/CHAR.pdf": tarfile.CHRTYPE})
        self.server.routes["/bag.tar"] = (200, tar)

        code, output = self.run_fetch(self.archived(["GOOD", "HARD", "CHAR"], tar))

        self.assertEqual(code, 1)
        self.assertEqual(output.count("is not a regular file"), 2)
        self.assertIsNone(self.fetched("hard"))
        self.assertIsNone(self.fetched("char"))

    def test_an_archive_served_larger_than_pinned_is_refused_once_for_every_member(self) -> None:
        tar = tar_of({"bag/data/T01_001.pdf": PDF + b"T01_001", "bag/data/T02_002.pdf": PDF + b"T02_002"})
        self.server.routes["/bag.tar"] = (200, tar + b"\0")

        code, output = self.run_fetch(self.archived(["T01_001", "T02_002"], tar))

        self.assertEqual(code, 1)
        self.assertEqual(output.count("the archive: the source now serves more than the pinned"), 2)
        self.assertEqual(self.server.requests["/bag.tar"], 1)

    def test_a_damaged_archive_is_refused_and_the_run_goes_on(self) -> None:
        # Its hash is pinned, so the damage was reviewed; a header that cannot be read must still refuse the
        # archive's members, never end the run before the documents that follow it.
        whole = tar_of({"bag/data/T01_001.pdf": PDF + b"T01_001", "bag/data/T02_002.pdf": PDF * 200})
        tar = whole[:3 * 512 + 1000]  # cut inside the second member's data
        self.server.routes["/bag.tar"] = (200, tar)
        self.server.routes["/one.pdf"] = (200, PDF)
        entries = self.archived(["T01_001", "T02_002"], tar) + [self.entry("one", self.server.base + "/one.pdf", PDF)]

        code, output = self.run_fetch(entries)

        self.assertEqual(code, 1)
        self.assertEqual(output.count("refused"), 2)
        self.assertEqual(self.fetched("one"), PDF)

    def test_long_member_names_of_gnu_archives_are_found(self) -> None:
        # RADAR's tar uses GNU long-name headers for paths over 100 bytes.
        name = "T04_019_" + "trailer-wrong-xref-byte-offset-" * 4
        long_path = f"10.22000-53/data/dataset/Test_Corpus/{name}.pdf"
        tar = tar_of({long_path: PDF + b"long"}, form=tarfile.GNU_FORMAT)
        self.server.routes["/bag.tar"] = (200, tar)
        entry = self.entry("long", self.server.base + "/bag.tar", PDF + b"long",
                           {"sha256": sha(tar), "bytes": len(tar), "member": long_path})

        code, _ = self.run_fetch([entry])

        self.assertEqual(code, 0)
        self.assertEqual(self.fetched("long"), PDF + b"long")

    def test_only_missing_or_stale_members_are_copied_again(self) -> None:
        names = ["T01_001", "T02_002", "T03_003"]
        tar = tar_of({f"bag/data/{name}.pdf": PDF + name.encode() for name in names})
        self.server.routes["/bag.tar"] = (200, tar)
        entries = self.archived(names, tar)
        self.run_fetch(entries)
        (self.root / "remote" / "test" / "t01-001.pdf").unlink()
        (self.root / "remote" / "test" / "t02-002.pdf").write_bytes(b"stale")

        code, output = self.run_fetch(entries)

        self.assertEqual(code, 0)
        self.assertEqual(output.count("fetched"), 2)
        self.assertEqual(output.count("present"), 1)
        self.assertEqual(self.server.requests["/bag.tar"], 2)
        for name in names:
            self.assertEqual(self.fetched(name.lower().replace("_", "-")), PDF + name.encode())

    def test_the_archive_leaves_nothing_behind(self) -> None:
        tar = tar_of({"bag/data/T01_001.pdf": PDF + b"T01_001"})
        self.server.routes["/bag.tar"] = (200, tar)

        self.run_fetch(self.archived(["T01_001"], tar))

        self.assertEqual(list(self.scratch.iterdir()), [])

    def test_a_member_whose_bytes_changed_is_refused(self) -> None:
        tar = tar_of({"bag/data/T01_001.pdf": PDF + b"T01_00X"})
        self.server.routes["/bag.tar"] = (200, tar)
        entries = self.archived(["T01_001"], tar)

        code, output = self.run_fetch(entries)

        self.assertEqual(code, 1)
        self.assertIn("member bag/data/T01_001.pdf: sha256", output)
        self.assertIsNone(self.fetched("t01-001"))

    def test_a_compressed_archive_is_refused_even_when_its_pins_match(self) -> None:
        tar = tar_of({"bag/data/T01_001.pdf": PDF + b"T01_001"}, mode="w:gz")
        self.server.routes["/bag.tar"] = (200, tar)

        code, output = self.run_fetch(self.archived(["T01_001"], tar))

        self.assertEqual(code, 1)
        self.assertIn("not a readable uncompressed tar", output)

    def test_a_member_is_written_only_where_its_entry_says(self) -> None:
        # A hostile name inside the archive must never become a path: the member is copied into the
        # entry's file, and nothing else is written.
        tar = tar_of({"../../outside.pdf": PDF, "bag/data/T01_001.pdf": PDF + b"T01_001"})
        self.server.routes["/bag.tar"] = (200, tar)

        code, _ = self.run_fetch(self.archived(["T01_001"], tar))

        self.assertEqual(code, 0)
        self.assertEqual(self.files_written(), ["manifest.json", "remote/test/t01-001.pdf"])
        self.assertFalse((self.root.parent / "outside.pdf").exists())

    def test_listing_names_the_member_and_fetches_nothing(self) -> None:
        tar = tar_of({"bag/data/T01_001.pdf": PDF + b"T01_001"})
        self.server.routes["/bag.tar"] = (200, tar)

        code, output = self.run_fetch(self.archived(["T01_001"], tar), "--list")

        self.assertEqual(code, 1)
        self.assertIn("missing", output)
        self.assertIn("(bag/data/T01_001.pdf)", output)
        self.assertEqual(self.server.requests, {})

    # --- what the manifest must say ------------------------------------------------------------------------

    def assert_invalid(self, entries: list[dict], reason: str) -> None:
        (self.root / "manifest.json").write_text(json.dumps({"documents": entries}), encoding="utf-8")
        with self.assertRaises(fetch_remote.InvalidManifest) as raised:
            fetch_remote.load_entries()
        self.assertIn(reason, str(raised.exception))

    def test_a_remote_document_must_pin_its_size(self) -> None:
        entry = self.entry("one", self.server.base + "/one.pdf", PDF)
        del entry["source"]["bytes"]
        self.assert_invalid([entry], "source.bytes is required")

    def test_a_member_path_must_stay_inside_its_archive(self) -> None:
        for member in ("../outside.pdf", "/absolute.pdf", "bag\\data.pdf", "bag//data.pdf", "", "bag/\n.pdf", "bag/"):
            with self.subTest(member=member):
                entry = self.archived(["T01_001"], b"tar")[0]
                entry["source"]["archive"]["member"] = member
                self.assert_invalid([entry], "relative member path")

    def test_every_entry_naming_an_archive_pins_it_the_same_way(self) -> None:
        first, second = self.archived(["T01_001", "T02_002"], b"tar")
        second["source"]["archive"]["sha256"] = sha(b"another tar")
        self.assert_invalid([first, second], "pinned differently")

    def test_a_pin_that_is_not_text_is_refused_as_an_invalid_manifest(self) -> None:
        entry = self.archived(["T01_001"], b"tar")[0]
        entry["source"]["archive"]["sha256"] = 5
        self.assert_invalid([entry], "source.archive needs")

    def test_a_url_is_either_a_document_or_an_archive(self) -> None:
        member = self.archived(["T01_001"], b"tar")[0]
        document = self.entry("document", member["source"]["url"], PDF)
        self.assert_invalid([member, document], "pinned differently")

    def test_a_url_of_the_copy_needs_the_review_that_allowed_it(self) -> None:
        entry = self.entry("one", self.server.base + "/copy/one.pdf", PDF)
        self.assert_invalid([entry], "needs source.copy")

    def test_a_copy_needs_its_publisher(self) -> None:
        entry = self.copied(self.entry("one", self.server.base + "/one.pdf", PDF), "/copy/one.pdf")
        del entry["source"]["publisher"]
        self.assert_invalid([entry], "source.publisher")

    def test_a_publisher_alone_is_not_a_copy(self) -> None:
        entry = self.entry("one", self.server.base + "/one.pdf", PDF)
        entry["source"]["publisher"] = self.server.base + "/elsewhere.pdf"
        self.assert_invalid([entry], "source.copy needs")

    def test_a_copy_review_needs_its_date_and_terms(self) -> None:
        for copy in ({"terms": "test"}, {"reviewed": "27/10/2026", "terms": "test"}, {"reviewed": "2026-10-27"},
                     {"reviewed": "2026-10-27", "terms": ""}, "reviewed"):
            with self.subTest(copy=copy):
                entry = self.copied(self.entry("one", self.server.base + "/one.pdf", PDF), "/copy/one.pdf")
                entry["source"]["copy"] = copy
                self.assert_invalid([entry], "source.copy needs")

    def test_a_copy_is_fetched_only_from_the_copy_repository(self) -> None:
        entry = self.copied(self.entry("one", self.server.base + "/one.pdf", PDF), "/elsewhere/one.pdf")
        self.assert_invalid([entry], "names a commit or a release of the copy")

    def test_the_copy_is_a_commit_or_a_release_of_the_corpus_repository(self) -> None:
        commit = "0123456789abcdef0123456789abcdef01234567"
        accepted = [f"https://raw.githubusercontent.com/AdCodicem/AdCodicem.Pdf.Corpus/{commit}/usgs/a.pdf",
                    "https://github.com/AdCodicem/AdCodicem.Pdf.Corpus/releases/download/usgs-pp1/report.pdf"]
        refused = ["https://raw.githubusercontent.com/AdCodicem/AdCodicem.Pdf.Corpus/main/usgs/a.pdf",
                   f"https://raw.githubusercontent.com/someone/AdCodicem.Pdf.Corpus/{commit}/usgs/a.pdf",
                   "http://github.com/AdCodicem/AdCodicem.Pdf.Corpus/releases/download/usgs-pp1/report.pdf",
                   "https://github.com/AdCodicem/AdCodicem.Pdf.Corpus/raw/main/usgs/a.pdf"]
        for url in accepted:
            self.assertIsNotNone(REAL_COPY.fullmatch(url), url)
        for url in refused:
            self.assertIsNone(REAL_COPY.fullmatch(url), url)

    def test_a_member_is_taken_once(self) -> None:
        first, second = self.archived(["T01_001", "T02_002"], b"tar")
        second["source"]["archive"]["member"] = first["source"]["archive"]["member"]
        self.assert_invalid([first, second], "taken twice")


if __name__ == "__main__":
    unittest.main()
