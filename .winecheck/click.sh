#!/bin/bash
# 用途：在「新建配置」对话框上按坐标点一下，然后截图并 OCR（核对点击落到了哪个控件）。
# 参数：$1 = 相对窗口的 x，$2 = y，$3 = 点击后等待秒数（默认 4）。
# 依赖与前置同 run1.sh。
set -u
export WINEPREFIX=${WINEPREFIX:-$HOME/.wine-ah}
export WINEDEBUG=${WINEDEBUG:--all}
export DISPLAY=${DISPLAY:-:99}
SHOT=${SHOT:-/tmp/ah-dialog.png}

W=$(xdotool search --name "Apifox 助手" | head -1)
[ -z "$W" ] && { echo "主窗口未找到"; exit 1; }
xdotool windowactivate "$W" 2>/dev/null
sleep 1
xdotool mousemove --window "$W" "$1" "$2" click 1
sleep "${3:-4}"
D=$(xdotool search --name "新建配置" | head -1)
echo "dlg=$D"
if [ -n "$D" ]; then
  xdotool getwindowgeometry "$D"
  import -window "$D" "$SHOT"
  identify "$SHOT"
  tesseract "$SHOT" stdout -l chi_sim 2>/dev/null | head -40
fi
