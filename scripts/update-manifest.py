import datetime
import json
import sys
from pathlib import Path

version, checksum, source_url, changelog = sys.argv[1:5]
manifest_path = Path("manifest.json")
manifest = json.loads(manifest_path.read_text()) if manifest_path.exists() else []
if not manifest:
    manifest.append({
        "guid": "4b1c2a8e-7c39-4d2e-9a51-0f6c3e8d2b17",
        "name": "Chrono",
        "description": "Disney+-style universe hubs: timeline, phases and release order for the MCU, Star Wars and any other franchise.",
        "overview": "Disney+-style universe hubs for Jellyfin",
        "owner": "JiiimmyyN",
        "category": "General",
        "versions": [],
    })

plugin = manifest[0]
plugin["versions"] = [v for v in plugin["versions"] if v["version"] != version]
plugin["versions"].insert(0, {
    "version": version,
    "changelog": changelog,
    "targetAbi": "12.0.0.0",
    "sourceUrl": source_url,
    "checksum": checksum,
    "timestamp": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
})
manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
