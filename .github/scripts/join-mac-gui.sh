#!/usr/bin/env bash
# Joins the two Mac publishes of the graphical program into one
# universal directory.
#
# The program itself is built for one architecture at a time and is
# joined with lipo, as the two text programs are. The native libraries
# beside it come from their NuGet packages and are usually universal
# already, in which case joining them would fail and either copy will
# do.
#
# A publish also leaves symbols behind, and on a Mac those are a .dSYM,
# which is a directory rather than a file. Only what runs is joined, so
# anything that is not a plain file is passed over.
#
# Usage: join-mac-gui.sh <arm64 directory> <x64 directory> <output>

set -euo pipefail

arm64="$1"
x64="$2"
out="$3"

mkdir -p "$out"

for file in "$arm64"/*; do
  [ -f "$file" ] || continue

  name="$(basename "$file")"
  case "$name" in *.pdb|*.dbg) continue;; esac

  if lipo -info "$file" 2>/dev/null | grep -q '^Architectures in the fat file'; then
    cp "$file" "$out/$name"
  elif lipo -info "$file" > /dev/null 2>&1; then
    lipo -create -output "$out/$name" "$file" "$x64/$name"
  else
    cp "$file" "$out/$name"
  fi
done

lipo -info "$out"/* 2>/dev/null || true
