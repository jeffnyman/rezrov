#!/usr/bin/env bash
# Shapes the Mac package of the graphical program: a rezrov.app bundle
# and a shim beside it.
#
# macOS reads a program's name and its Dock icon out of a bundle, which
# is a directory of a fixed shape rather than a file format. Shipped as
# a loose executable, as this program was, it has neither: the Dock and
# the application switcher show the toolkit's default name, and the
# icon the window sets for itself has nowhere to go, because windows
# there have no title bar to put one in.
#
# The icon is built here rather than committed, because an .icns is a
# container of the same picture at every size macOS asks for, and the
# picture is the only part of that worth keeping under version
# control. There is no 1024 pixel entry: the artwork is 512, and an
# entry claiming a size it does not have would only be the same
# picture blown up. macOS does that itself, when something asks for a
# size that is not there.
#
# The shim is there because a bundle is opened rather than run. "open
# -a rezrov.app --args story.z5" does not pass the shell's working
# directory along, so a relative path to a story would not be found,
# and typing the way into the bundle by hand is a lot to ask. Running
# the executable inside the bundle directly is enough to be treated as
# the bundled program, since macOS finds the bundle from the path of
# what is running.
#
# Usage: bundle-mac-gui.sh <gui directory> <icon png> <version> <out>

set -euo pipefail

gui="$1"
icon="$2"
version="$3"
out="$4"

app="$out/rezrov.app"

# iconutil wants a directory named .iconset, and it wants nothing in it
# but the entries it knows, so it gets one of its own away from
# everything else.
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
iconset="$scratch/rezrov.iconset"

mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" "$iconset"

# Whatever the publish produced apart from symbols, for the reason the
# release workflow gives: which native libraries the toolkit needs is
# the toolkit's business and may change without this file hearing.
find "$gui" -maxdepth 1 -type f ! -name '*.pdb' ! -name '*.dbg' \
  -exec cp {} "$app/Contents/MacOS/" \;

chmod +x "$app/Contents/MacOS/rezrov-gui"

# The names are iconutil's and the numbers are the pixels each one
# means. Three sizes are asked for twice, because the @2x of one entry
# is the same picture as the plain entry of the next one up, and
# iconutil wants both names.
for entry in \
  16:icon_16x16 \
  32:icon_16x16@2x \
  32:icon_32x32 \
  64:icon_32x32@2x \
  128:icon_128x128 \
  256:icon_128x128@2x \
  256:icon_256x256 \
  512:icon_256x256@2x \
  512:icon_512x512
do
  size="${entry%%:*}"
  name="${entry#*:}"
  sips -z "$size" "$size" "$icon" --out "$iconset/$name.png" > /dev/null
done

iconutil --convert icns "$iconset" --output "$app/Contents/Resources/rezrov.icns"

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleName</key>
	<string>rezrov</string>
	<key>CFBundleDisplayName</key>
	<string>rezrov</string>
	<key>CFBundleExecutable</key>
	<string>rezrov-gui</string>
	<key>CFBundleIdentifier</key>
	<string>io.github.jeffnyman.rezrov</string>
	<key>CFBundleIconFile</key>
	<string>rezrov</string>
	<key>CFBundlePackageType</key>
	<string>APPL</string>
	<key>CFBundleInfoDictionaryVersion</key>
	<string>6.0</string>
	<key>CFBundleShortVersionString</key>
	<string>${version}</string>
	<key>CFBundleVersion</key>
	<string>${version}</string>
	<key>LSApplicationCategoryType</key>
	<string>public.app-category.games</string>
	<key>NSHighResolutionCapable</key>
	<true/>
</dict>
</plist>
PLIST

plutil -lint "$app/Contents/Info.plist"

# The shim keeps the working directory, so "./rezrov-gui story.z5"
# means what it always did.
cat > "$out/rezrov-gui" <<'SHIM'
#!/bin/sh
# Plays a story with the program inside rezrov.app, from wherever you
# are standing, which is what opening the bundle would not do.
exec "$(dirname "$0")/rezrov.app/Contents/MacOS/rezrov-gui" "$@"
SHIM

chmod +x "$out/rezrov-gui"
