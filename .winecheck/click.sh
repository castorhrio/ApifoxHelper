#!/bin/bash
export DISPLAY=:99
W=$(xdotool search --name "Apifox 助手" | head -1)
xdotool windowactivate "$W" 2>/dev/null
sleep 1
xdotool mousemove --window "$W" "$1" "$2" click 1
sleep "${3:-4}"
D=$(xdotool search --name "新建配置" | head -1)
echo "dlg=$D"
if [ -n "$D" ]; then
  xdotool getwindowgeometry "$D"
  import -window "$D" /tmp/dlg.png
  identify /tmp/dlg.png
  tesseract /tmp/dlg.png stdout -l chi_sim 2>/dev/null | head -40
fi
