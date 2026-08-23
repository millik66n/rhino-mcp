#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
configuration="${1:-Release}"
stage_dir="$(mktemp -d)"
output_dir="$repo_dir/dist"
trap 'rm -rf "$stage_dir"' EXIT

dotnet build "$repo_dir/native/RhinoMCP.Plugin/RhinoMCP.Plugin.csproj" -c "$configuration"
dotnet build "$repo_dir/native/RhinoMCP.Grasshopper/RhinoMCP.Grasshopper.csproj" -c "$configuration"

mkdir -p "$stage_dir/net7.0" "$output_dir"
cp "$repo_dir/native/RhinoMCP.Plugin/bin/$configuration/net7.0/RhinoMCP.rhp" "$stage_dir/net7.0/"
cp "$repo_dir/native/RhinoMCP.Grasshopper/bin/$configuration/net7.0/RhinoMCP.Grasshopper.gha" "$stage_dir/net7.0/"
cp "$repo_dir/native/package/manifest.yml" "$stage_dir/"

if command -v yak >/dev/null 2>&1; then
  (cd "$stage_dir" && yak build --platform any)
  mv "$stage_dir"/*.yak "$output_dir/"
else
  package="$output_dir/rhino-mcp-easy-0.2.0-rh8_0-any.yak"
  (cd "$stage_dir" && zip -qr "$package" .)
  echo "yak CLI not found; created the equivalent package archive: $package"
fi
