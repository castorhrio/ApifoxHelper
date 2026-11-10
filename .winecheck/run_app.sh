#!/bin/bash
# 用途：启动已发布的 ApifoxHelper.exe（先按进程名清掉上一次的实例，反复试跑时用），截图主窗口并 OCR 文字。
# 依赖：wine（实测 wine-10.0）、Xvfb（DISPLAY 默认 :99）、xdotool、ImageMagick（import/identify）、
#       tesseract（可选，中文需 chi_sim 语言包）。
# 前置：先发布 Windows 版（命令见 .winecheck/README.md），产物默认落在 /tmp/ah-pub。
# 可覆盖的环境变量：WINEPREFIX / WINE_BIN / WINEDEBUG / EXE / DATA / DISPLAY / SHOT / LOG。
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
