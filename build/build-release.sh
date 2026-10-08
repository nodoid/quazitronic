#!/usr/bin/env bash
# Builds signed release packages for every platform into release/.
#
#   build/build-release.sh [all|android|ios|macos|windows]
#
# Needs (macOS host): .NET 10 SDK with android/ios workloads, Xcode, the orictron provisioning
# profiles installed, the Apple Distribution + 3rd Party Mac Developer Installer identities in
# the keychain, and signing/android.properties (created by build/create-android-keystore.sh).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

VERSION=$(sed -n 's:.*<OrictronVersion>\(.*\)</OrictronVersion>.*:\1:p' Directory.Build.props)
BUILD=$(sed -n 's:.*<OrictronBuildNumber>\(.*\)</OrictronBuildNumber>.*:\1:p' Directory.Build.props)
OUT="$ROOT/releases"  # release builds always live in <repo>/releases (file names carry the version)
WORK="$ROOT/artifacts/work"
TARGET="${1:-all}"

# Personal account settings (identities, team, publisher) live in git-ignored signing/local.properties;
# see build/local.properties.example.
LOCAL="$ROOT/signing/local.properties"
[ -f "$LOCAL" ] || { echo "Missing $LOCAL - copy build/local.properties.example"; exit 1; }
# shellcheck disable=SC1090
source "$LOCAL"
APPLE_DIST="$APPLE_DIST_IDENTITY"
MAC_INSTALLER="$MAC_INSTALLER_IDENTITY"
ENTITLEMENTS="$WORK/Entitlements.plist"
MAC_PROFILE_NAME="rel-quazitronic-mac"
IOS_PROFILE_NAME="rel-quazitronic"
NAME="Orictron"

# Always build releases from clean: an incremental Android build once packaged stale assemblies.
rm -rf "$WORK" src/*/bin/Release src/*/obj/Release
mkdir -p "$OUT" "$WORK"
sed -e "s/__TEAM_ID__/$APPLE_TEAM_ID/g" build/macos/Entitlements.plist > "$ENTITLEMENTS"
echo "Orictron $VERSION ($BUILD) -> $OUT"

find_profile() { # $1 = profile name, $2 = extension
  local dir f
  for dir in "$HOME/Library/Developer/Xcode/UserData/Provisioning Profiles" "$HOME/Library/MobileDevice/Provisioning Profiles"; do
    for f in "$dir"/*."$2"; do
      [ -e "$f" ] || continue
      if [ "$(security cms -D -i "$f" 2>/dev/null | plutil -extract Name raw - -o - 2>/dev/null)" = "$1" ]; then
        echo "$f"; return 0
      fi
    done
  done
  return 1
}

# Fails if the .pkg (or any file in its payload) carries extended attributes such as com.apple.quarantine,
# which App Store Connect rejects (error 91109).
check_pkg_xattrs() {
  local pkg="$1" tmp payloads found doubles files
  tmp="$(mktemp -d)"
  xattr -c "$pkg"
  (cd "$tmp" && xar -xf "$pkg")
  payloads="$(find "$tmp" -maxdepth 2 -name Payload -type f)"
  [ -n "$payloads" ] || { echo "ERROR: no Payload found in $pkg"; rm -rf "$tmp"; exit 1; }
  mkdir "$tmp/extracted-files"
  while IFS= read -r p; do (cd "$tmp/extracted-files" && gunzip -dc "$p" | cpio -i --quiet); done <<< "$payloads"
  files="$(find "$tmp/extracted-files" -type f | wc -l | tr -d ' ')"
  found="$(xattr -lr "$tmp/extracted-files" 2>/dev/null)"
  doubles="$(find "$tmp/extracted-files" -name '._*' | wc -l | tr -d ' ')"
  rm -rf "$tmp"
  [ "$files" -gt 0 ] || { echo "ERROR: nothing extracted from $pkg"; exit 1; }
  if [ -n "$found" ] || [ "$doubles" != "0" ]; then
    echo "ERROR: $pkg contains extended attributes (e.g. com.apple.quarantine):"; echo "$found"; exit 1
  fi
  echo "Checked $files files: no quarantine or other extended attributes in $(basename "$pkg")"
}

build_android() {
  echo "== Android"
  local props="$ROOT/signing/android.properties"
  [ -f "$props" ] || { echo "Missing $props - run build/create-android-keystore.sh"; exit 1; }
  # shellcheck disable=SC1090
  source "$props"
  dotnet publish src/Quazitronic.Android -c Release -o "$WORK/android" \
    -p:AndroidKeyStore=true \
    -p:AndroidSigningKeyStore="$ROOT/signing/$KEYSTORE_FILE" \
    -p:AndroidSigningKeyAlias="$KEY_ALIAS" \
    -p:AndroidSigningStorePass="env:ORICTRON_STORE_PASS" \
    -p:AndroidSigningKeyPass="env:ORICTRON_STORE_PASS"
  cp "$WORK/android/uk.co.allthejohnsons.quazitronic-Signed.aab" "$OUT/$NAME-$VERSION-android.aab"
  cp "$WORK/android/uk.co.allthejohnsons.quazitronic-Signed.apk" "$OUT/$NAME-$VERSION-android.apk"
}

build_ios() {
  echo "== iOS"
  if ! find_profile "$IOS_PROFILE_NAME" mobileprovision >/dev/null; then
    echo "SKIPPED: provisioning profile '$IOS_PROFILE_NAME' (App Store, App ID uk.co.allthejohnsons.quazitronic) is not installed."
    echo "         Create it in the Apple Developer portal, double-click it, then re-run: build/build-release.sh ios"
    return 0
  fi
  dotnet publish src/Quazitronic.iOS -c Release -o "$WORK/ios" \
    -p:ArchiveOnBuild=false -p:BuildIpa=true
  cp "$(find "$WORK/ios" -name '*.ipa' | head -1)" "$OUT/$NAME-$VERSION-ios.ipa"
}

build_macos() {
  echo "== macOS (Mac App Store, Apple silicon)"
  local pub="$WORK/macos-publish" app="$WORK/Orictron.app"
  rm -rf "$pub" "$app"
  # Single-file keeps managed .dlls inside the (signed) executable; codesign rejects loose
  # non-Mach-O files in Contents/MacOS. Native dylibs stay beside it and are signed individually.
  dotnet publish src/Quazitronic.Desktop -c Release -r osx-arm64 --self-contained true \
    -p:UseAppHost=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -o "$pub"

  mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
  cp -R "$pub/" "$app/Contents/MacOS/"
  rm -f "$app/Contents/MacOS/"*.pdb
  ls "$app/Contents/MacOS"
  sed -e "s/__VERSION__/$VERSION/" -e "s/__BUILD__/$BUILD/" build/macos/Info.plist > "$app/Contents/Info.plist"
  cp build/macos/Orictron.icns "$app/Contents/Resources/Orictron.icns"
  local profile
  if ! profile=$(find_profile "$MAC_PROFILE_NAME" provisionprofile); then
    # No Mac App Store profile yet: ad-hoc sign so the app runs locally / for testers on Apple silicon.
    echo "NOTE: profile '$MAC_PROFILE_NAME' not installed - building an ad-hoc signed app (not for the Mac App Store)."
    find "$app/Contents/MacOS" -type f \( -name '*.dylib' -o -name '*.so' \) -print0 | xargs -0 -n1 codesign --force --sign - 2>/dev/null
    codesign --force --sign - --entitlements build/macos/Entitlements-adhoc.plist "$app/Contents/MacOS/Orictron"
    codesign --force --sign - --entitlements build/macos/Entitlements-adhoc.plist "$app"
    codesign --verify --deep --strict "$app"
    (cd "$WORK" && rm -f "$OUT/$NAME-$VERSION-macos-adhoc.zip" && ditto -c -k --keepParent "$(basename "$app")" "$OUT/$NAME-$VERSION-macos-adhoc.zip")
    return 0
  fi
  cp -X "$profile" "$app/Contents/embedded.provisionprofile"
  # The App Store rejects any extended attribute (e.g. com.apple.quarantine on a downloaded profile,
  # error 91109), so strip them all from the bundle before signing.
  xattr -cr "$app"

  # Sign inside-out: every native library, then the executable and bundle with entitlements.
  find "$app/Contents/MacOS" -type f \( -name '*.dylib' -o -name '*.so' \) -print0 |
    xargs -0 -n1 codesign --force --timestamp=none --sign "$APPLE_DIST" 2>/dev/null
  codesign --force --timestamp=none --sign "$APPLE_DIST" --entitlements "$ENTITLEMENTS" \
    "$app/Contents/MacOS/Orictron"
  codesign --force --timestamp=none --sign "$APPLE_DIST" --entitlements "$ENTITLEMENTS" "$app"
  codesign --verify --deep --strict "$app"

  productbuild --component "$app" /Applications --sign "$MAC_INSTALLER" "$OUT/$NAME-$VERSION-macos.pkg"
  check_pkg_xattrs "$OUT/$NAME-$VERSION-macos.pkg"
}

build_windows() {
  # Always MSIX, always x64 and arm64. Upload both .msix files to Partner Center together.
  echo "== Windows (MSIX: x64 + arm64)"
  local makemsix="$ROOT/tools/msix/makemsix"
  [ -x "$makemsix" ] || build/windows/build-makemsix.sh
  rm -f "$OUT/$NAME-$VERSION-windows-"*
  for arch in x64 arm64; do
    local pub="$WORK/windows-$arch"
    rm -rf "$pub"
    dotnet publish src/Quazitronic.Desktop -c Release -r "win-$arch" --self-contained true -o "$pub"
    rm -f "$pub/"*.pdb
    # Package layout: app files + manifest + tiles (unqualified names: no resources.pri needed).
    sed -e "s/__VERSION__/$VERSION/" -e "s/__ARCH__/$arch/" -e "s/__IDENTITY__/$WINDOWS_IDENTITY/" \
      -e "s/__PUBLISHER__/$WINDOWS_PUBLISHER/" -e "s/__PUBLISHER_NAME__/$WINDOWS_PUBLISHER_NAME/" build/windows/AppxManifest.xml > "$pub/AppxManifest.xml"
    mkdir -p "$pub/Assets"
    for f in build/windows/Assets/*.scale-200.png; do
      cp "$f" "$pub/Assets/$(basename "$f" .scale-200.png).png"
    done
    cp build/windows/Assets/*targetsize* "$pub/Assets/"
    "$makemsix" pack -d "$pub" -p "$OUT/$NAME-$VERSION-windows-$arch.msix"
  done
}

case "$TARGET" in
  android) build_android ;;
  ios) build_ios ;;
  macos) build_macos ;;
  windows) build_windows ;;
  all) build_android; build_ios; build_macos; build_windows ;;
  *) echo "unknown target $TARGET"; exit 1 ;;
esac

# Finished packages live only in release/; drop the intermediate copies.
rm -rf "$WORK"

echo "Done:"; ls -lh "$OUT"
