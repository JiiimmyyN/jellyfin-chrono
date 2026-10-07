#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [ -f web/package.json ]; then
  (cd web && pnpm install --frozen-lockfile >/dev/null && pnpm build >/dev/null)
fi
rm -rf artifacts/plugin
dotnet publish src/Jellyfin.Plugin.Chrono -c Release -o artifacts/plugin >/dev/null
target=dev/jellyfin/config/plugins/Chrono_0.1.0.0
mkdir -p "$target"
cp artifacts/plugin/Jellyfin.Plugin.Chrono.dll "$target/"
docker compose -f dev/docker-compose.yml restart jellyfin >/dev/null 2>&1 || docker compose -f dev/docker-compose.yml up -d >/dev/null
echo "Deployed to $target and restarted Jellyfin."
