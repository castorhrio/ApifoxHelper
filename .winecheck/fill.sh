#!/bin/bash
# 用途：在「新建配置」对话框里按坐标点输入框并真实键入名称、仓库根目录与 API 项目路径，
#       然后截图并 OCR 出对话框文字（核对字段是否真的填进去了）。
# 依赖与前置同 run_app.sh（wine / Xvfb / xdotool / ImageMagick / tesseract，先发布到 /tmp/ah-pub）。
# 这一支不自己打开对话框：先跑 shot_dialog.sh 把「新建配置」点开（主窗口与对话框都留在后台），再跑本脚本。
# 工作区路径由 AH_WS 指定（默认 $HOME/ah-ws），经 winepath 转成 Windows 路径；
# 项目相对路径由 AH_PROJECT 指定（默认 Probe.Api/Probe.Api.csproj）。
# 注意：坐标随 DPI 与字体变化，换机器/换字号后要重新量。
set -u
export WINEPREFIX=${WINEPREFIX:-$HOME/.wine-ah}
export WINEDEBUG=${WINEDEBUG:--all}
export DISPLAY=${DISPLAY:-:99}
WS=${AH_WS:-$HOME/ah-ws}
PROJ=${AH_PROJECT:-Probe.Api/Probe.Api.csproj}
SHOT=${SHOT:-/tmp/ah-dialog.png}
WS_WIN=$(winepath -w "$WS" 2>/dev/null || echo "$WS")
PROJ_WIN=$(winepath -w "$WS/$PROJ" 2>/dev/null || echo "$WS_WIN\\$PROJ")

D=$(xdotool search --name "新建配置" | head -1)
echo "dlg=$D"
[ -z "$D" ] && exit 1
xdotool windowactivate "$D"; sleep 1
click() { xdotool mousemove --window "$D" "$1" "$2" click 1; sleep 1; }
typ() { xdotool type --delay 40 "$1"; sleep 1; }
click 300 32;  typ 'probe-config'
click 300 68;  typ "$WS_WIN"
click 300 181; typ "$PROJ_WIN"
sleep 1
import -window "$D" "$SHOT"
tesseract "$SHOT" stdout -l chi_sim 2>/dev/null | head -30
