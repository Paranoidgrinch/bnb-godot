#!/bin/bash
# The ten sources, into ./src, named the way the rest of the tools expect them.
#
# Two things this does that a plain wget loop does not, both learned the hard way: it checks each
# download against the server's own Content-Length (a 99 MB WAV over a slow line times out at
# two thirds and leaves a file that looks fine to `ls`), and it checks the finished file against
# the SHA-256 recorded in assets/music/SOURCES.md — so "I re-fetched the sources" means the same
# bytes the shipped .ogg files were built from, or it says so.
#
# Where a lossless source exists it is preferred: every runtime file is re-encoded to Vorbis, and
# starting from someone else's MP3 would make that a second generation of loss for no reason.
set -u
cd "$(dirname "$0")" || exit 1
mkdir -p src
cd src || exit 1

fetch() {
  local url="$1" out="$2" want size
  want=$(curl -sS -L -I -A "Mozilla/5.0" "$url" | grep -i '^content-length:' | tail -1 | tr -dc '0-9')
  if [ -s "$out" ] && [ "$(stat -c%s "$out")" = "$want" ]; then
    echo "have  $out"
    return
  fi
  curl -sS -L -A "Mozilla/5.0" -o "$out" --max-time 1800 --retry 3 --retry-all-errors "$url" || {
    echo "FAIL  $out"; return 1; }
  size=$(stat -c%s "$out" 2>/dev/null || echo 0)
  [ "$size" = "$want" ] && echo "ok    $out" || echo "SHORT $out ($size of $want bytes)"
}

fetch "https://opengameart.org/sites/default/files/Viktor%20Kraus%20-%20Secret%20Sanctum.wav" secret_sanctum.wav
fetch "https://opengameart.org/sites/default/files/waystone_inn_0.wav"                        waystone_inn.wav
fetch "https://opengameart.org/sites/default/files/The%20Savvy%20Merchant.ogg"                savvy_merchant.ogg
# ⚠ OpenGameArt lists this one's MP3 twice. The two files are byte-identical — there is no
# separate loop version to prefer, and only one is worth fetching.
fetch "https://opengameart.org/sites/default/files/Victoriana%20Loop_2.mp3"                   victoriana_loop.mp3
fetch "https://opengameart.org/sites/default/files/Dark%20chamber.mp3"                        dark_chamber.mp3
fetch "https://opengameart.org/sites/default/files/the_eternal_sands.wav"                     eternal_sands.wav
fetch "https://opengameart.org/sites/default/files/Dark%20Descent_0.mp3"                      dark_descent.mp3
# The author's own slug really is spelled "descecrated"; that typo is the address that resolves.
fetch "https://opengameart.org/sites/default/files/The%20Desecrated%20Temple_2.mp3"           desecrated_temple.mp3
fetch "https://opengameart.org/sites/default/files/land_of_the_great_gods.ogg"                land_of_the_great_gods.ogg
# This one ships as a zip; the WAV inside it is the source, not the zip.
fetch "https://opengameart.org/sites/default/files/forest_whisper_theme.zip"                  forest_whisper_theme.zip
if [ -s forest_whisper_theme.zip ]; then
  unzip -o -q forest_whisper_theme.zip -d forest_whisper
  cp "forest_whisper/Forest Whisper Theme/Forest Whisper.wav" forest_whisper_theme.wav
fi

echo
echo "checking against the recorded checksums"
sha256sum -c --quiet - <<'SUMS' && echo "all ten match assets/music/SOURCES.md"
1861188f72ad71c2155723d4c351a82a7cc7da7375552e91091d900ab50eed33  secret_sanctum.wav
006528b53ef8bd1a41302c8f48d4869f6d163beb1c568966e51322a057435352  waystone_inn.wav
90e0e472dcee3c86b27d9c2b5756911dfe84f378f2309277d323235007e4f707  savvy_merchant.ogg
058dbff5fa03de97598231827525827380be04112d004673421876fa0a81156e  victoriana_loop.mp3
2b5d4fb921b8c84c4e7f9efb07323b67ce02df445d81ab22c148fd0502667164  dark_chamber.mp3
cd834fafd028bb882500c49e7e766d50f9de337d644951bfdc4e68b09b91fadf  forest_whisper_theme.wav
3e2f22bf1a39dbffcf8ca6a55be9ed1c32d8c6417b36045fe11763fe447581bd  eternal_sands.wav
a835debcb0d86077000ecf33440b9d04b0d715c1bd78e222010dc6838538803e  dark_descent.mp3
52a0ed919f65bc9ced41578c69ad350c41514d1c91467563c9c37c6733d4ad11  desecrated_temple.mp3
4b9c543eb5f09aa484a11b49432685d5cb65aeef6d25237e9e532f846ea76837  land_of_the_great_gods.ogg
SUMS
