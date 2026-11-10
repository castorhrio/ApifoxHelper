#!/bin/bash
# 用途：在 wine 下启动主窗口 → 点「新建配置」→ 截图并 OCR 新建配置对话框（核对排版与字段是否可填）。
# 依赖：wine / Xvfb / xdotool / ImageMagick / tesseract，前置（先发布到 /tmp/ah-pub）同 run_app.sh。
# 可覆盖环境变量：WINEPREFIX / WINE_BIN / WINEDEBUG / EXE / DATA / DISPLAY / SHOT / DSHOT / LOG。
set -u
export WINEPREFIX=${WINEPREFIX:-$HOME/.wine-ah}
export WINEDEBUG=${WINEDEBUG:--all}
export DISPLAY=${DISPLAY:-:99}
WINE_BIN=${WINE_BIN:-wine}
EXE=${EXE:-/tmp/ah-pub/ApifoxHelper.exe}
DATA=${DATA:-$HOME/ah-data}
SHOT=${SHOT:-/tmp/ah-main.png}
DSHOT=${DSHOT:-/tmp/ah-dialog.png}
LOG=${LOG:-/tmp/ah-app.log}

pkill -f "ApifoxHelper.exe --data" 2>/dev/null
sleep 1
setsid nohup "$WINE_BIN" "$EXE" --data "$DATA" >"$LOG" 2>&1 < /dev/null &
sleep "${1:-25}"
W=$(xdotool search --name "Apifox 助手" | head -1)
echo "win=$W"
[ -z "$W" ] && { echo "主窗口未找到"; exit 1; }
xdotool getwindowgeometry "$W"
import -window "$W" "$SHOT"
identify "$SHOT"
# 空态里的「新建配置」按钮：坐标随 DPI/字体变化，换机器后要重新量（默认 1024×721 窗口）。
xdotool windowactivate "$W" 2>/dev/null
sleep 1
xdotool mousemove --window "$W" "${2:-512}" "${3:-430}" click 1
sleep 4
D=$(xdotool search --name "新建配置" | head -1)
echo "dlg=$D"
[ -z "$D" ] && { echo "对话框未出现"; exit 1; }
xdotool getwindowgeometry "$D"
import -window "$D" "$DSHOT"
identify "$DSHOT"
tesseract "$DSHOT" stdout -l chi_sim 2>/dev/null | head -40
