"""A fake NuGet feed for push-scenarios.sh: the v3 index, the push endpoints, a flat container and the symbol
endpoint push-packages.sh reads. Usage: fake_feed.py <state-dir> <port>. Behavior is scripted per package through
<state-dir>/script.json, reread on every request:
  {"fail": {"<id>.nupkg": [500, 409], "<id>.snupkg": [500]},   statuses answered to the next pushes, in order
   "list_after": 2,                                               lookups before a pushed package is listed
   "flat_status": 200}                                            the flat container's status
Every push is appended to log.txt as "<kind> <id> <version> <status>"."""
import io, json, os, re, sys, zipfile
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

root = sys.argv[1]; port = int(sys.argv[2])
state = {"published": {}, "symbols": set(), "lookups": {}}

def script():
    with open(os.path.join(root, "script.json")) as f: return json.load(f)

def log(line):
    with open(os.path.join(root, "log.txt"), "a") as f: f.write(line + "\n")

class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args): pass
    def send(self, code, body=b"", ctype="application/json"):
        self.send_response(code); self.send_header("Content-Type", ctype); self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)
    def do_GET(self):
        base = f"http://127.0.0.1:{port}"
        if self.path == "/v3/index.json":
            return self.send(200, json.dumps({"version": "3.0.0", "resources": [
                {"@id": base + "/api/v2/package", "@type": "PackagePublish/2.0.0"},
                {"@id": base + "/api/v2/symbolpackage", "@type": "SymbolPackagePublish/4.9.0"},
                {"@id": base + "/flat/", "@type": "PackageBaseAddress/3.0.0"}]}).encode())
        m = re.match(r"^/flat/([^/]+)/index.json$", self.path)
        if m:
            s = script(); status = s.get("flat_status", 200)
            if status != 200: return self.send(status)
            pid = m.group(1); listed = []
            for (i, v) in state["published"]:
                if i == pid:
                    n = state["lookups"].get((i, v), 0) + 1; state["lookups"][(i, v)] = n
                    if n > s.get("list_after", 0): listed.append(v)
            return self.send(200, json.dumps({"versions": listed}).encode()) if listed or any(i == pid for (i, _) in state["published"]) else self.send(404)
        m = re.match(r"^/sym/([^/]+)/([^/]+)$", self.path)
        if m:
            return self.send(200 if (m.group(1).lower(), m.group(2).lower()) in state["symbols"] else 404)
        self.send(404)
    def do_PUT(self):
        if self.headers.get("Content-Length"):
            body = self.rfile.read(int(self.headers["Content-Length"]))
        else:
            body = b""
            while True:
                size = int(self.rfile.readline().strip().split(b";")[0], 16)
                if size == 0:
                    self.rfile.readline(); break
                body += self.rfile.read(size); self.rfile.readline()
        start = body.index(b"PK\x03\x04"); data = body[start:]
        nuspec = None
        with zipfile.ZipFile(io.BytesIO(data)) as z:
            for name in z.namelist():
                if name.endswith(".nuspec"): nuspec = z.read(name).decode()
        pid = re.search(r"<id>([^<]+)</id>", nuspec).group(1).lower(); ver = re.search(r"<version>([^<]+)</version>", nuspec).group(1).lower()
        kind = "snupkg" if "symbolpackage" in self.path else "nupkg"
        s = script(); queue = s.setdefault("fail", {}).get(f"{pid}.{kind}", [])
        if queue:
            status = queue.pop(0)
            with open(os.path.join(root, "script.json"), "w") as f: json.dump(s, f)
        else:
            status = 201
        if kind == "nupkg" and (pid, ver) in state["published"] and status == 201: status = 409
        if status in (201, 409):
            if kind == "nupkg": state["published"].setdefault((pid, ver), True)
            else: state["symbols"].add((pid, ver))
        log(f"{kind} {pid} {ver} {status}")
        self.send(status)

ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
