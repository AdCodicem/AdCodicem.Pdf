#!/usr/bin/env bash
# Installs the .NET 10 SDK when it is missing.
#
# Claude Code web sessions start without a .NET SDK, and the network policy blocks Microsoft's
# distribution hosts (builds.dotnet.microsoft.com, dot.net), so the official dotnet-install.sh fails with
# a 403. The Ubuntu archive is reachable and ships dotnet-sdk-10.0.
#
# The container's package index can be older than the archive: once the archive has moved to a newer
# .NET patch, the versions the index names are gone and every download answers 404 (#234). The install
# is tried first as is, which costs nothing when the index is current; when it fails, the index is
# refreshed and the install tried once more.
set -uo pipefail

log=/tmp/setup-dotnet.log

if command -v dotnet >/dev/null 2>&1; then
    echo ".NET SDK already present: $(dotnet --version 2>/dev/null)"
    exit 0
fi

if ! command -v apt-get >/dev/null 2>&1; then
    echo ".NET SDK missing and apt-get unavailable: install the .NET 10 SDK manually." >&2
    exit 0
fi

install() {
    DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends dotnet-sdk-10.0 >>"$log" 2>&1
}

: >"$log"
echo "Installing dotnet-sdk-10.0 from the Ubuntu archive (this takes a few minutes)..."
if install; then
    echo ".NET SDK installed: $(dotnet --version 2>/dev/null)"
    exit 0
fi

echo "The install failed; refreshing the package index and trying again..."
if apt-get update -qq >>"$log" 2>&1 && install; then
    echo ".NET SDK installed: $(dotnet --version 2>/dev/null)"
    exit 0
fi

# Still exit 0: a session without the SDK can do everything but build, and a failing hook would say less.
# The end of the log is shown here, where the session reads it, rather than only left in the file.
echo "Failed to install the .NET SDK. The end of $log:" >&2
tail -n 15 "$log" >&2
exit 0
