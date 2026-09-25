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
import sys
import tarfile
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import fetch_remote  # noqa: E402

PDF = b"%PDF-1.4\n%%EOF\n"


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def tar_of(members: dict[str, bytes], links: dict[str, str] | None = None, mode: str = "w") -> bytes:
    buffer = io.BytesIO()
    with tarfile.open(fileobj=buffer, mode=mode) as archive:
        for name, data in members.items():
            info = tarfile.TarInfo(name)
            info.size = len(data)
            archive.addfile(info, io.BytesIO(data))
        for name, target in (links or {}).items():
            info = tarfile.TarInfo(name)
            info.type = tarfile.SYMTYPE
            info.linkname = target
            archive.addfile(info)
    return buffer.getvalue()


class Server:
    """Serves fixed responses by path and counts the requests each path received."""

    def __init__(self) -> None:
        self.routes: dict[str, tuple[int, bytes]] = {}
        self.requests: dict[str, int] = {}
        server = self

        class Handler(BaseHTTPRequestHandler):
            def do_GET(self) -> None:  # noqa: N802 - the name http.server calls
                server.requests[self.path] = server.requests.get(self.path, 0) + 1
                status, body = server.routes.get(self.path, (404, b""))
                self.send_response(status)
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
        self.root = Path(directory.name)
        patches = [
            mock.patch.object(fetch_remote, "ROOT", self.root),
            mock.patch.object(fetch_remote, "MANIFEST", self.root / "manifest.json"),
            # A retried outage must cost attempts, not wall-clock time.
            mock.patch.object(fetch_remote.time, "sleep", lambda seconds: None),
            mock.patch.dict(os.environ, {"no_proxy": "127.0.0.1", "NO_PROXY": "127.0.0.1"}),
        ]
        for patch in patches:
            patch.start()
            self.addCleanup(patch.stop)

    def entry(self, name: str, url: str, data: bytes, archive: dict | None = None) -> dict:
        source = {"url": url, "sha256": sha(data), "bytes": len(data)}
        if archive is not None:
            source["archive"] = archive
        return {"file": f"remote/test/{name}.pdf", "origin": "remote", "licence": "test", "source": source,
                "expect": {}}

    def archived(self, names: list[str], tar: bytes, path: str = "/bag.tar") -> list[dict]:
        pin = {"sha256": sha(tar), "bytes": len(tar)}
        return [self.entry(name.lower().replace("_", "-"), self.server.base + path, PDF + name.encode(),
                           {**pin, "member": f"bag/data/{name}.pdf"}) for name in names]

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
        return sorted(str(path.relative_to(self.root)) for path in self.root.rglob("*") if path.is_file())

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
        self.assertIn("not an uncompressed tar", output)

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
        for member in ("../outside.pdf", "/absolute.pdf", "bag\\data.pdf", "bag//data.pdf", ""):
            with self.subTest(member=member):
                entry = self.archived(["T01_001"], b"tar")[0]
                entry["source"]["archive"]["member"] = member
                self.assert_invalid([entry], "relative member path")

    def test_every_entry_naming_an_archive_pins_it_the_same_way(self) -> None:
        first, second = self.archived(["T01_001", "T02_002"], b"tar")
        second["source"]["archive"]["sha256"] = sha(b"another tar")
        self.assert_invalid([first, second], "pinned differently")

    def test_a_url_is_either_a_document_or_an_archive(self) -> None:
        member = self.archived(["T01_001"], b"tar")[0]
        document = self.entry("document", member["source"]["url"], PDF)
        self.assert_invalid([member, document], "pinned differently")

    def test_a_member_is_taken_once(self) -> None:
        first, second = self.archived(["T01_001", "T02_002"], b"tar")
        second["source"]["archive"]["member"] = first["source"]["archive"]["member"]
        self.assert_invalid([first, second], "taken twice")


if __name__ == "__main__":
    unittest.main()
