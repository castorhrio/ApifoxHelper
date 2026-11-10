#!/bin/bash
# 用途：同 run1.sh，但先按进程名清掉上一次的实例再启动（反复试跑时用）。
# 依赖、前置与可覆盖的环境变量同 run1.sh。
set -u
export WINEPREFIX=${WINEPREFIX:-$HOME/.wine-ah}
export WINEDEBUG=${WINEDEBUG:--all}
export DISPLAY=${DISPLAY:-:99}
WINE_BIN=${WINE_BIN:-wine}
EXE=${EXE:-/tmp/ah-pub/ApifoxHelper.exe}
DATA=${DATA:-$HOME/ah-data}
SHOT=${SHOT:-/tmp/ah-shot.png}
LOG=${LOG:-/tmp/ah-app.log}

for pid in $(pgrep -f "$EXE"); do
  if [ "$pid" != "$$" ] && [ "$pid" != "$PPID" ]; then kill "$pid" 2>/dev/null; fi
done
sleep 2
nohup "$WINE_BIN" "$EXE" --data "$DATA" >"$LOG" 2>&1 &
sleep "${1:-25}"
W=$(xdotool search --name "Apifox 助手" | head -1)
echo "win=$W"
if [ -n "$W" ]; then
  import -window "$W" "$SHOT"
  identify "$SHOT"
  tesseract "$SHOT" stdout -l chi_sim 2>/dev/null
fi
