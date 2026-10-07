#!/usr/bin/env bash
# Rebuilds tools/msix/makemsix (Microsoft's open-source MSIX SDK, with packing) on macOS.
# build-release.sh uses it to create real .msix packages and the .msixbundle without a Windows PC.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SRC="$(mktemp -d)/msix-packaging"
git clone --depth 1 https://github.com/microsoft/msix-packaging.git "$SRC"
(cd "$SRC" && ./makemac.sh --pack --skip-samples --skip-tests -arch "$(uname -m)")
mkdir -p "$ROOT/tools/msix"
cp "$SRC/.vs/bin/makemsix" "$SRC/.vs/lib/libmsix.dylib" "$ROOT/tools/msix/"
install_name_tool -add_rpath @executable_path "$ROOT/tools/msix/makemsix"
echo "Built $ROOT/tools/msix/makemsix"
