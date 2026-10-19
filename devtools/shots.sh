#!/usr/bin/env bash
# Developer captures: a screenshot of the VS window every N seconds into media/shots/seq (devtools/shots.sh <count> <seconds>)
n=${1:-12}; every=${2:-3}
export UM_FFMPEG_WIN="$(cygpath -w ~/vsr-work/dl/ffmpeg/ffmpeg-master-latest-win64-gpl/bin/ffmpeg.exe)"
cd "$(dirname "$0")/.." && mkdir -p media/shots/seq
for i in $(seq -w 1 $n); do "$LOCALAPPDATA/Melty/modding-kit/bin/um" win shot --exe VampireSurvivors.exe media/shots/seq/s$i.png --scale 0.5 >/dev/null 2>&1; sleep $every; done
echo done
