#!/bin/bash
# The nine tracks of 2026-11-08 (title, Acts I-IV, elite, boss, shop, campfire), from the user's own download
# (free-stock-music.com, licence files beside each MP3). No loop cut: each track plays whole, starts quiet and
# ends quiet, so the wrap back to its start is silence into silence.
#
#   ./fade.sh <dir with act1/ act2/ ... title/> <out dir>
#
# Per track: trim leading and trailing silence (-60 dB), fade in 4 s, fade out over the last 8 s, then a
# two-pass flat gain to -16 LUFS integrated / -1.5 dBTP (loudnorm linear=true), Vorbis 160 kb/s at 48 kHz.
set -eu
src="$1"; out="$2"; mkdir -p "$out"
declare -A N=([title]=music_title_unraveling [act1]=music_act1_lurking_sloth [act2]=music_act2_the_long_dark
  [act3]=music_act3_fairy_of_the_forest [act4]=music_act4_the_legend_of_narmer [boss]=music_boss_endless_storm
  [elite]=music_elite_hearts_of_iron [shop]=music_shop_adventure [campfire]=music_campfire_the_things_that_keep_us_here)
TRIM="silenceremove=start_periods=1:start_threshold=-60dB,areverse,silenceremove=start_periods=1:start_threshold=-60dB,areverse"
for d in "${!N[@]}"; do
  w="$out/$d.wav"
  ffmpeg -v error -y -i "$(ls "$src/$d"/*.mp3)" -af "$TRIM" -ar 48000 "$w"
  len=$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$w")
  fades="afade=t=in:d=4,afade=t=out:st=$(python3 -c "print($len-8)"):d=8"
  read -r I TP LRA TH OFF <<<"$(ffmpeg -hide_banner -i "$w" -af "$fades,loudnorm=I=-16:TP=-1.5:LRA=20:print_format=json" \
    -f null - 2>&1 | sed -n '/^{/,/^}/p' | python3 -c "import json,sys;d=json.load(sys.stdin);print(d['input_i'],d['input_tp'],d['input_lra'],d['input_thresh'],d['target_offset'])")"
  ffmpeg -v error -y -i "$w" -af "$fades,loudnorm=I=-16:TP=-1.5:LRA=20:measured_I=$I:measured_TP=$TP:measured_LRA=$LRA:measured_thresh=$TH:offset=$OFF:linear=true,aresample=48000" \
    -c:a libvorbis -b:a 160k "$out/${N[$d]}.ogg"
  rm "$w"
  echo "$d -> ${N[$d]}.ogg"
done
