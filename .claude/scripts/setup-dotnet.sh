#!/usr/bin/env bash
# Installs the .NET SDK of the feature band global.json pins, 10.0.4xx, the one CI and the releases build with (#463).
#
# Claude Code web sessions start without a .NET SDK. The C# compilers of two feature bands write some methods
# differently, and what the runtime inlines follows (#374): a session compiles with the band CI uses, or what it
# measures is not what ships. The Ubuntu archive ships only the 10.0.1xx band; it is installed first all the same, for
# the native libraries the SDK needs (ICU among them) and as the SDK a session falls back on. Then Microsoft's installer,
# dotnet-install.sh from builds.dotnet.microsoft.com, installs the latest 10.0.4xx into ~/.dotnet, which a link in
# /usr/local/bin puts before the archive's on the PATH.
#
# The container's package index can be older than the archive: once the archive has moved to a newer .NET patch, the
# versions the index names are gone and every download answers 404 (#234). The install is tried first as is, which
# costs nothing when the index is current; when it fails, the index is refreshed and the install tried once more.
#
# The network policy has blocked Microsoft's hosts before (dot.net still answers with a redirect it does not follow).
# Where the band cannot be installed the hook says so: global.json refuses the archive's band, so nothing builds until
# it is.
set -uo pipefail

band="10.0.4"
channel="10.0.4xx"
install_dir="$HOME/.dotnet"
installer="https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh"
log=/tmp/setup-dotnet.log

has_band() {
    "$1" --list-sdks 2>/dev/null | grep -q "^${band}"
}

link() {
    ln -sf "$install_dir/dotnet" /usr/local/bin/dotnet
    echo ".NET SDK of the pinned band ready: $(dotnet --version 2>/dev/null)"
}

if command -v dotnet >/dev/null 2>&1 && has_band dotnet; then
    echo ".NET SDK already present: $(dotnet --version 2>/dev/null)"
    exit 0
fi

: >"$log"

if [ -x "$install_dir/dotnet" ] && has_band "$install_dir/dotnet"; then
    link
    exit 0
fi

archive() {
    DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends dotnet-sdk-10.0 >>"$log" 2>&1
}

if ! [ -x /usr/lib/dotnet/dotnet ] && command -v apt-get >/dev/null 2>&1; then
    echo "Installing dotnet-sdk-10.0 from the Ubuntu archive, for its native libraries (this takes a few minutes)..."
    if ! archive; then
        echo "The install failed; refreshing the package index and trying again..."
        apt-get update -qq >>"$log" 2>&1 && archive
    fi
fi

echo "Installing the .NET SDK $channel from builds.dotnet.microsoft.com..."
if curl -fsSL --retry 3 -o /tmp/dotnet-install.sh "$installer" >>"$log" 2>&1 \
    && bash /tmp/dotnet-install.sh --channel "$channel" --install-dir "$install_dir" --no-path >>"$log" 2>&1 \
    && has_band "$install_dir/dotnet"; then
    link
    exit 0
fi

# Still exit 0: a session without the SDK can do everything but build, and a failing hook would say less. The end of
# the log is shown here, where the session reads it, rather than only left in the file.
echo "Failed to install the .NET SDK $channel, which global.json pins: nothing builds until it is. The end of $log:" >&2
tail -n 15 "$log" >&2
exit 0
