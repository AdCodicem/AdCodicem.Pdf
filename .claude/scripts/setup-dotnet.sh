#!/usr/bin/env bash
# Installs the .NET 10 SDK when it is missing.
#
# Claude Code web sessions start without a .NET SDK, and the network policy blocks Microsoft's
# distribution hosts (builds.dotnet.microsoft.com, dot.net), so the official dotnet-install.sh fails with
# a 403. The Ubuntu archive is reachable and ships dotnet-sdk-10.0.
set -uo pipefail

if command -v dotnet >/dev/null 2>&1; then
    echo ".NET SDK already present: $(dotnet --version 2>/dev/null)"
    exit 0
fi

if ! command -v apt-get >/dev/null 2>&1; then
    echo ".NET SDK missing and apt-get unavailable: install the .NET 10 SDK manually." >&2
    exit 0
fi

echo "Installing dotnet-sdk-10.0 from the Ubuntu archive (this takes a few minutes)..."
if DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends dotnet-sdk-10.0 >/tmp/setup-dotnet.log 2>&1; then
    echo ".NET SDK installed: $(dotnet --version 2>/dev/null)"
else
    echo "Failed to install the .NET SDK - see /tmp/setup-dotnet.log" >&2
fi
exit 0
