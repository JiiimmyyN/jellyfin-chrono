#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
[ -f dev.env ] || cp dev.env.example dev.env
source dev.env

auth='MediaBrowser Client="chrono-dev", Device="setup", DeviceId="chrono-dev-setup", Version="1.0"'

until curl -sf "$JELLYFIN_URL/System/Info/Public" >/dev/null; do sleep 2; done

if [ "$(curl -s "$JELLYFIN_URL/System/Info/Public" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("StartupWizardCompleted"))')" != "True" ]; then
  curl -sf -X POST "$JELLYFIN_URL/Startup/Configuration" -H "Authorization: $auth" -H 'Content-Type: application/json' \
    -d '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'
  curl -sf "$JELLYFIN_URL/Startup/User" -H "Authorization: $auth" >/dev/null
  curl -sf -X POST "$JELLYFIN_URL/Startup/User" -H "Authorization: $auth" -H 'Content-Type: application/json' \
    -d "{\"Name\":\"$JELLYFIN_ADMIN_USER\",\"Password\":\"$JELLYFIN_ADMIN_PASSWORD\"}"
  curl -sf -X POST "$JELLYFIN_URL/Startup/Complete" -H "Authorization: $auth"
  echo "Startup wizard completed."
fi

token=$(curl -sf -X POST "$JELLYFIN_URL/Users/AuthenticateByName" -H "Authorization: $auth" -H 'Content-Type: application/json' \
  -d "{\"Username\":\"$JELLYFIN_ADMIN_USER\",\"Pw\":\"$JELLYFIN_ADMIN_PASSWORD\"}" | python3 -c 'import json,sys; print(json.load(sys.stdin)["AccessToken"])')
h="$auth, Token=\"$token\""

existing=$(curl -sf "$JELLYFIN_URL/Library/VirtualFolders" -H "Authorization: $h")
add_library() {
  local name=$1 type=$2 path=$3
  if ! grep -q "\"Name\":\"$name\"" <<<"$existing"; then
    curl -sf -X POST "$JELLYFIN_URL/Library/VirtualFolders?name=$name&collectionType=$type&refreshLibrary=false" -H "Authorization: $h" \
      -H 'Content-Type: application/json' -d "{\"LibraryOptions\":{\"PathInfos\":[{\"Path\":\"$path\"}],\"EnableRealtimeMonitor\":false}}"
    echo "Added library $name."
  fi
}
add_library Movies movies /media/movies
add_library Shows tvshows /media/shows
curl -sf -X POST "$JELLYFIN_URL/Library/Refresh" -H "Authorization: $h"
echo "Library scan started. Token: stored in dev/.token"
echo "$token" > .token
