#!/usr/bin/env bash
# Build Cluster Console.app and a drag-to-Applications DMG from a dotnet publish folder.
set -euo pipefail

publish_dir=""
version=""
rid=""
icon_png=""
output_dir=""

usage() {
  echo "Usage: bundle.sh --publish-dir DIR --version VER --rid osx-arm64|osx-x64 --icon PNG --output-dir DIR" >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --publish-dir) publish_dir="$2"; shift 2 ;;
    --version) version="$2"; shift 2 ;;
    --rid) rid="$2"; shift 2 ;;
    --icon) icon_png="$2"; shift 2 ;;
    --output-dir) output_dir="$2"; shift 2 ;;
    *) usage ;;
  esac
done

[[ -n "$publish_dir" && -n "$version" && -n "$rid" && -n "$icon_png" && -n "$output_dir" ]] || usage
[[ -d "$publish_dir" ]] || { echo "publish dir not found: $publish_dir" >&2; exit 1; }
[[ -f "$icon_png" ]] || { echo "icon not found: $icon_png" >&2; exit 1; }

script_dir="$(cd "$(dirname "$0")" && pwd)"
executable="MaksIT.ClusterConsole.UI"
app_name="MaksIT Cluster Console.app"
work="$(mktemp -d "${TMPDIR:-/tmp}/cluster-console-macos.XXXXXX")"
app="$work/$app_name"
host="$publish_dir/$executable"

[[ -f "$host" ]] || { echo "app host missing (UseAppHost=true): $host" >&2; exit 1; }

mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
sed "s/{{VERSION}}/$version/g" "$script_dir/Info.plist.template" > "$app/Contents/Info.plist"
cp -a "$publish_dir/." "$app/Contents/MacOS/"
chmod +x "$app/Contents/MacOS/$executable"

iconset="$work/ClusterConsole.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$icon_png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
done
sips -z 32 32 "$icon_png" --out "$iconset/icon_16x16@2x.png" >/dev/null
sips -z 64 64 "$icon_png" --out "$iconset/icon_32x32@2x.png" >/dev/null
sips -z 256 256 "$icon_png" --out "$iconset/icon_128x128@2x.png" >/dev/null
sips -z 512 512 "$icon_png" --out "$iconset/icon_256x256@2x.png" >/dev/null
sips -z 1024 1024 "$icon_png" --out "$iconset/icon_512x512@2x.png" >/dev/null
iconutil -c icns "$iconset" -o "$app/Contents/Resources/ClusterConsole.icns"

mkdir -p "$output_dir"
dmg_root="$work/dmg"
mkdir -p "$dmg_root"
cp -a "$app" "$dmg_root/"
ln -s /Applications "$dmg_root/Applications"

dmg_name="maksit-cluster-console-${version}-${rid}.dmg"
hdiutil create \
  -volname "MaksIT Cluster Console" \
  -srcfolder "$dmg_root" \
  -ov \
  -format UDZO \
  "$output_dir/$dmg_name"

echo "Wrote $output_dir/$dmg_name"
