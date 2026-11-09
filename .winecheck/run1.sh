#!/bin/bash
export WINEPREFIX=$HOME/wine-ah
export WINEDEBUG=-all
export DISPLAY=:99
EXE=/tmp/ah-pub/ApifoxHelper.exe
DATA=$HOME/ah-data
pkill -f "$EXE" 2>/dev/null
sleep 1
setsid nohup /usr/lib/x86_64-linux-gnu/wine/wine "$EXE" --data "$DATA" >/tmp/app.log 2>&1 < /dev/null &
sleep "${1:-30}"
W=$(xdotool search --name "Apifox 助手" | head -1)
echo "win=$W"
if [ -n "$W" ]; then
  xdotool getwindowgeometry "$W"
  import -window "$W" /tmp/s1.png
  identify /tmp/s1.png
  tesseract /tmp/s1.png stdout -l chi_sim 2>/dev/null | head -40
fi
