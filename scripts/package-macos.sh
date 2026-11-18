#!/bin/sh
# Packs a published macOS build of one app of ThixOffice into <Name>.app, a zip (the update of installed versions) and a
# disk image (the first installation: drag the app into "Programme").
#
#   scripts/package-macos.sh <app name> <publish folder> <rid: osx-arm64|osx-x64> <version> <build number> <output folder>
#   e.g. scripts/package-macos.sh ThixWrite publish/osx-arm64 osx-arm64 nightly-12-20261118 12 artifacts
#
# Runs on macOS (iconutil, codesign, ditto, hdiutil). Everything per app comes from its name: the id (lower case), the
# bundle id info.schmoldt.<id>, Major/Minor from Directory.Build.props (<Name>Major), the icons from branding/<id>/png
# (tools/render-icons.js) and the document types from scripts/macos-document-types/<id>.plist. CFBundleGetInfoString is
# "<Name> <full version>" – ThixLauncher reads the installed version from it. Without an Apple Developer ID the app is
# signed ad hoc (needed on Apple Silicon); Gatekeeper then asks once (right click > Öffnen).
set -eu

name="$1"
publish="$2"
rid="$3"
version="$4"
build="$5"
out="$6"

root="$(cd "$(dirname "$0")/.." && pwd)"
id="$(printf '%s' "$name" | tr '[:upper:]' '[:lower:]')"
arch="${rid#osx-}"
major=$(sed -n "s:.*<${name}Major>\(.*\)</${name}Major>.*:\1:p" "$root/Directory.Build.props")
minor=$(sed -n "s:.*<${name}Minor>\(.*\)</${name}Minor>.*:\1:p" "$root/Directory.Build.props")
if [ -z "$major" ] || [ -z "$minor" ]; then
  echo "Directory.Build.props has no ${name}Major / ${name}Minor" >&2
  exit 1
fi

types="$root/scripts/macos-document-types/${id}.plist"
if [ ! -f "$types" ]; then
  echo "The document types of $name are missing: $types" >&2
  exit 1
fi

work="$(mktemp -d)"
app="$work/${name}.app"

mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" "$out"
cp -R "$publish/." "$app/Contents/MacOS/"
chmod +x "$app/Contents/MacOS/${name}"

# The app icon and the document icon from the rendered branding (the macOS variants keep Apple's margin around the tile).
make_icns() {
  prefix="$1"
  target="$2"
  iconset="$work/$(basename "$target" .icns).iconset"
  mkdir -p "$iconset"
  for size in 16 32 128 256 512; do
    cp "$root/branding/${id}/png/${prefix}-${size}.png" "$iconset/icon_${size}x${size}.png"
    double=$((size * 2))
    cp "$root/branding/${id}/png/${prefix}-${double}.png" "$iconset/icon_${size}x${size}@2x.png"
  done
  iconutil -c icns "$iconset" -o "$target"
}
make_icns "${id}-mac" "$app/Contents/Resources/${name}.icns"
make_icns "${id}-document-mac" "$app/Contents/Resources/${name}Document.icns"

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key>
  <string>info.schmoldt.${id}</string>
  <key>CFBundleName</key>
  <string>${name}</string>
  <key>CFBundleDisplayName</key>
  <string>${name}</string>
  <key>CFBundleExecutable</key>
  <string>${name}</string>
  <key>CFBundleIconFile</key>
  <string>${name}</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>${major}.${minor}.${build}</string>
  <key>CFBundleVersion</key>
  <string>${build}</string>
  <key>CFBundleGetInfoString</key>
  <string>${name} ${version}</string>
  <key>NSHumanReadableCopyright</key>
  <string>© Matthias Schmoldt</string>
  <key>LSMinimumSystemVersion</key>
  <string>13.0</string>
  <key>LSApplicationCategoryType</key>
  <string>public.app-category.productivity</string>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>CFBundleDevelopmentRegion</key>
  <string>de</string>
  <key>CFBundleDocumentTypes</key>
$(sed -e '/<!--/,/-->/d' -e "s/@DOCUMENT_ICON@/${name}Document/g" "$types")
</dict>
</plist>
PLIST
plutil -lint "$app/Contents/Info.plist"

# Ad-hoc signature: Apple Silicon runs only signed code. A Developer ID signature + notarization can replace it later.
codesign --force --deep --sign - "$app"
codesign --verify --deep --strict "$app"

# Update package (MacAppBundle unpacks it with ditto) and disk image for the first installation.
ditto -c -k --keepParent "$app" "$out/${name}-macOS-${arch}.zip"

dmg_root="$work/dmg"
mkdir -p "$dmg_root"
cp -R "$app" "$dmg_root/"
ln -s /Applications "$dmg_root/Programme"
hdiutil create -volname "${name}" -srcfolder "$dmg_root" -ov -format UDZO "$out/${name}-macOS-${arch}.dmg" >/dev/null

rm -rf "$work"
ls -l "$out"
