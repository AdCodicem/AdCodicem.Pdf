#!/usr/bin/env bash
# Installe le SDK .NET 10 si nécessaire.
#
# Les sessions Claude Code sur le web démarrent sans SDK .NET, et la politique réseau bloque les serveurs
# de distribution de Microsoft (builds.dotnet.microsoft.com, dot.net) : le script officiel dotnet-install.sh
# échoue donc en 403. L'archive Ubuntu, elle, est joignable et fournit dotnet-sdk-10.0.
set -uo pipefail

if command -v dotnet >/dev/null 2>&1; then
    echo "SDK .NET déjà présent : $(dotnet --version 2>/dev/null)"
    exit 0
fi

if ! command -v apt-get >/dev/null 2>&1; then
    echo "SDK .NET absent et apt-get indisponible : installer le SDK .NET 10 manuellement." >&2
    exit 0
fi

echo "Installation de dotnet-sdk-10.0 depuis l'archive Ubuntu (quelques minutes)..."
if DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends dotnet-sdk-10.0 >/tmp/setup-dotnet.log 2>&1; then
    echo "SDK .NET installé : $(dotnet --version 2>/dev/null)"
else
    echo "Échec de l'installation du SDK .NET — voir /tmp/setup-dotnet.log" >&2
fi
exit 0
