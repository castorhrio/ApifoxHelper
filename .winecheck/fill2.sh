#!/bin/bash
# 用途：同 fill.sh 的精简版——只填名称与 API 项目路径，并只 OCR 对话框上半部分（字段对位核对用）。
# 依赖、前置与环境变量同 fill.sh。
set -u
export WINEPREFIX=${WINEPREFIX:-$HOME/.wine-ah}
export WINEDEBUG=${WINEDEBUG:--all}
export DISPLAY=${DISPLAY:-:99}
WS=${AH_WS:-$HOME/ah-ws}
PROJ=${AH_PROJECT:-Probe.Api/Probe.Api.csproj}
SHOT=${SHOT:-/tmp/ah-dialog.png}
CROP=${CROP:-/tmp/ah-dialog-top.png}
PROJ_WIN=$(winepath -w "$WS/$PROJ" 2>/dev/null || echo "$WS\\$PROJ")

D=$(xdotool search --name "新建配置" | head -1)
echo "dlg=$D"
[ -z "$D" ] && exit 1
click() { xdotool mousemove --window "$D" "$1" "$2" click 1; sleep 1; }
typ() { xdotool type --delay 40 "$1"; sleep 1; }
click 300 35;  typ 'probe-config'
click 300 187; typ "$PROJ_WIN"
sleep 1
import -window "$D" "$SHOT"
convert "$SHOT" -crop 680x230+0+0 +repage "$CROP"
tesseract "$CROP" stdout -l chi_sim 2>/dev/null
