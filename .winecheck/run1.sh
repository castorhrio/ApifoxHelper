#!/bin/bash
# 用途：在 wine 下启动已发布的 ApifoxHelper.exe，截图主窗口并（可选）OCR 出文字。
# 依赖：wine（实测 wine-10.0）、Xvfb（DISPLAY 默认 :99）、xdotool、ImageMagick（import/identify）、
#       tesseract（可选，中文需 chi_sim 语言包）。
# 前置：先发布 Windows 版（命令见 .winecheck/README.md），产物默认落在 /tmp/ah-pub。
# 可覆盖的环境变量：WINEPREFIX / EXE / DATA / DISPLAY / WINE_BIN / SHOT。
set -u
export WINEPREFIX=${WINEPREFIX:-$HOME/.wine-ah}
export WINEDEBUG=${WINEDEBUG:--all}
export DISPLAY=${DISPLAY:-:99}
WINE_BIN=${WINE_BIN:-wine}
EXE=${EXE:-/tmp/ah-pub/ApifoxHelper.exe}
DATA=${DATA:-$HOME/ah-data}
SHOT=${SHOT:-/tmp/ah-shot.png}
LOG=${LOG:-/tmp/ah-app.log}

pkill -f "$EXE" 2>/dev/null
sleep 1
setsid nohup "$WINE_BIN" "$EXE" --data "$DATA" >"$LOG" 2>&1 < /dev/null &
sleep "${1:-30}"
W=$(xdotool search --name "Apifox 助手" | head -1)
echo "win=$W"
if [ -n "$W" ]; then
  xdotool getwindowgeometry "$W"
  import -window "$W" "$SHOT"
  identify "$SHOT"
  tesseract "$SHOT" stdout -l chi_sim 2>/dev/null | head -40
fi
