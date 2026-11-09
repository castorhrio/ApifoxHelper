#!/bin/bash
export DISPLAY=:99
D=$(xdotool search --name "新建配置" | head -1)
click() { xdotool mousemove --window "$D" "$1" "$2" click 1; sleep 1; }
typ() { xdotool type --delay 40 "$1"; sleep 1; }
click 300 35;  typ 'probe-config'
click 300 187; typ 'Z:\home\ubuntu\ah-ws\Probe.Api\Probe.Api.csproj'
sleep 1
import -window "$D" /tmp/dlg3.png
convert /tmp/dlg3.png -crop 680x230+0+0 +repage /tmp/top3.png
tesseract /tmp/top3.png stdout -l chi_sim 2>/dev/null
