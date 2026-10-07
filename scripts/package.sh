#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
version="${1:-0.1.0.0}"
(cd web && pnpm install --frozen-lockfile && pnpm build)
rm -rf artifacts/plugin artifacts/package
dotnet publish src/Jellyfin.Plugin.Chrono -c Release -o artifacts/plugin -p:Version="$version" -p:AssemblyVersion="$version" -p:FileVersion="$version"
mkdir -p artifacts/package
zip_name="chrono_${version}.zip"
(cd artifacts/plugin && zip -q "../package/$zip_name" Jellyfin.Plugin.Chrono.dll)
md5sum "artifacts/package/$zip_name" | cut -d' ' -f1 > "artifacts/package/$zip_name.md5"
echo "artifacts/package/$zip_name"
