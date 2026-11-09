#!/bin/bash
export WINEPREFIX=$HOME/wine-ah
export WINEDEBUG=-all
export DISPLAY=:99
EXE=/tmp/ah-pub/ApifoxHelper.exe
DATA=$HOME/ah-data
for pid in $(pgrep -f "$EXE"); do
  if [ "$pid" != "$$" ] && [ "$pid" != "$PPID" ]; then kill "$pid" 2>/dev/null; fi
done
sleep 2
nohup /usr/lib/x86_64-linux-gnu/wine/wine "$EXE" --data "$DATA" >/tmp/app.log 2>&1 &
sleep "${1:-25}"
W=$(xdotool search --name "Apifox 助手" | head -1)
echo "win=$W"
if [ -n "$W" ]; then
  import -window "$W" /tmp/win.png
  identify /tmp/win.png
  tesseract /tmp/win.png stdout -l chi_sim 2>/dev/null
fi
