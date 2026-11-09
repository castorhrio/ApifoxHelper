#!/bin/bash
export DISPLAY=:99
D=$(xdotool search --name "新建配置" | head -1)
echo "dlg=$D"
xdotool windowactivate "$D"; sleep 1
click() { xdotool mousemove --window "$D" "$1" "$2" click 1; sleep 1; }
typ() { xdotool type --delay 40 "$1"; sleep 1; }
click 300 32;  typ 'probe-config'
click 300 68;  typ 'Z:\home\ubuntu\ah-ws'
click 300 181; typ 'Z:\home\ubuntu\ah-ws\Probe.Api\Probe.Api.csproj'
sleep 1
import -window "$D" /tmp/dlg2.png
tesseract /tmp/dlg2.png stdout -l chi_sim 2>/dev/null | head -30
