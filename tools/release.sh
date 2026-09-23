#!/usr/bin/env bash
# THE RELEASE: everything a tester downloads, built from the exported game in one go.
#
#   dist/<version>/BnB-<version>-windows-setup.exe       installer (per user, no admin prompt)
#   dist/<version>/BnB-<version>-windows-portable.zip    the same files, unpack and play
#   dist/<version>/BnB-<version>-linux-x86_64.AppImage   one file, chmod +x and play
#   dist/<version>/BnB-<version>-linux-x86_64.tar.gz     the same files, for a system the AppImage will not run on
#   dist/<version>/README-ALPHA.txt                      install notes for the testers, English and German
#   dist/<version>/SHA256SUMS.txt
#
# The version is project.godot's application/config/version — the number every bug report and run recording
# names. Raise it there (and the two *_version lines in export_presets.cfg) before a build goes out.
#
# Tools, found on PATH or in ~/.local/opt (neither needs root):
#   makensis       apt-get download nsis nsis-common && dpkg -x each into ~/.local/opt/nsis-deb/root
#   appimagetool   github.com/AppImage/appimagetool/releases → ~/.local/opt/appimagetool-x86_64.AppImage
#
#   tools/release.sh               export, then package
#   tools/release.sh --no-export   package what is already in build/
#   tools/release.sh --publish     export, package, then push to itch.io (moonvine-forge/bnb-alpha)
#   (--no-export and --publish can be combined, in any order)
#
# Publishing needs butler (~/.local/opt/butler, linked into ~/.local/bin) and a one-time `butler login`.
# Each channel keeps the same link and password on the itch page; a push replaces the previous file.
#
# ⚠ A RELEASE WITHOUT THE WEBHOOKS IS REFUSED. A tester's build that cannot send runs or bug reports home is an
# alpha that teaches us nothing, and nothing on the tester's screen would say so.
# ⚠ dist/ is gitignored and never goes into this PUBLIC repository: the packages carry both webhooks.
set -euo pipefail
cd "$(dirname "$0")/.."

export_first=1
publish=0
for arg in "$@"; do
  case "$arg" in
    --no-export) export_first=0 ;;
    --publish) publish=1 ;;
    *) echo "unknown option: $arg" >&2; exit 1 ;;
  esac
done
itch_target="moonvine-forge/bnb-alpha"

version=$(sed -n 's/^config\/version="\(.*\)"$/\1/p' project.godot)
[[ -n "$version" ]] || { echo "no application/config/version in project.godot" >&2; exit 1; }
for file in bugreport.cfg runlog.cfg; do
  [[ -f "$file" ]] || { echo "refusing to release without $file (see the note at the top)" >&2; exit 1; }
done

opt="$HOME/.local/opt"
makensis=$(command -v makensis || true)
if [[ -z "$makensis" && -x "$opt/nsis-deb/root/usr/bin/makensis" ]]; then
  makensis="$opt/nsis-deb/root/usr/bin/makensis"
  export NSISDIR="$opt/nsis-deb/root/usr/share/nsis"
fi
appimagetool=$(command -v appimagetool || true)
[[ -z "$appimagetool" && -x "$opt/appimagetool-x86_64.AppImage" ]] && appimagetool="$opt/appimagetool-x86_64.AppImage"
[[ -n "$makensis" ]] || { echo "makensis not found (see the header)" >&2; exit 1; }
[[ -n "$appimagetool" ]] || { echo "appimagetool not found (see the header)" >&2; exit 1; }
if (( publish )); then
  command -v butler >/dev/null || { echo "butler not found (see the header)" >&2; exit 1; }
  [[ -f "$HOME/.config/itch/butler_creds" ]] || { echo "butler is not logged in: run 'butler login' once" >&2; exit 1; }
fi

(( export_first )) && bash tools/export.sh

name="BnB-$version"
dist="dist/$version"
stage="$dist/.stage"
rm -rf "$dist"
mkdir -p "$stage"
dist_abs=$(realpath "$dist")

# ── Windows ──────────────────────────────────────────────────────────────────────
"$makensis" -V2 \
  -DVERSION="$version" \
  -DSRCDIR="$(realpath build/windows)" \
  -DOUTFILE="$dist_abs/$name-windows-setup.exe" \
  -DICON="$(realpath assets/brand/icon.ico)" \
  packaging/windows/installer.nsi

portable="$stage/Bureaucrats-and-Broomsticks-$version-windows"
mkdir -p "$portable"
cp -r build/windows/. "$portable/"
(cd "$stage" && zip -qr9 "$dist_abs/$name-windows-portable.zip" "$(basename "$portable")")

# ── Linux ────────────────────────────────────────────────────────────────────────
appdir="$stage/AppDir"
mkdir -p "$appdir/usr/bin"
cp -r build/linux/. "$appdir/usr/bin/"
cp packaging/linux/AppRun "$appdir/AppRun"
chmod +x "$appdir/AppRun"
cp packaging/linux/bureaucrats-and-broomsticks.desktop "$appdir/"
cp assets/brand/icon.png "$appdir/bureaucrats-and-broomsticks.png"
cp assets/brand/icon.png "$appdir/.DirIcon"
ARCH=x86_64 "$appimagetool" --no-appstream "$appdir" "$dist_abs/$name-linux-x86_64.AppImage" >/dev/null 2>&1 \
  || { echo "appimagetool failed; rerun it by hand for the log:" >&2
       echo "  ARCH=x86_64 $appimagetool --no-appstream $appdir $dist_abs/$name-linux-x86_64.AppImage" >&2; exit 1; }

tarball="$stage/Bureaucrats-and-Broomsticks-$version-linux"
mkdir -p "$tarball"
cp -r build/linux/. "$tarball/"
cp assets/brand/icon.png "$tarball/icon.png"
tar -C "$stage" -czf "$dist_abs/$name-linux-x86_64.tar.gz" "$(basename "$tarball")"

# ── notes and sums ───────────────────────────────────────────────────────────────
sed "s/{VERSION}/$version/g" packaging/README-ALPHA.txt > "$dist/README-ALPHA.txt"
rm -rf "$stage"
(cd "$dist" && sha256sum BnB-* README-ALPHA.txt > SHA256SUMS.txt)

echo "release $version:"
ls -lh "$dist" | tail -n +2

# ── itch.io ──────────────────────────────────────────────────────────────────────
if (( publish )); then
  butler push "$dist/$name-windows-setup.exe"          "$itch_target:windows-installer" --userversion "$version"
  butler push "$dist/$name-windows-portable.zip"       "$itch_target:windows-portable"  --userversion "$version"
  butler push "$dist/$name-linux-x86_64.AppImage"      "$itch_target:linux-appimage"    --userversion "$version"
  butler push "$dist/$name-linux-x86_64.tar.gz"        "$itch_target:linux-tarball"     --userversion "$version"
  echo "published $version to https://moonvine-forge.itch.io/bnb-alpha"
fi
