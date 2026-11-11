#!/bin/bash
# 用途：真机到手前的「第 0 条」四项准备（docs/NOTES.md 的 G-D5 动作单第 0 条），
#       一条命令跑完 ①②③，最后把 ④ 的动作单打印出来。
#
#   ① 复跑三档冒烟：.winecheck/demo.sh（三档各跑一次并逐屏对账）
#   ② 复跑构建与用例：dotnet build + dotnet test
#   ③ 建好 docs/evidence/real-machine/ 的九个归档子目录
#   ④ 打印七条动作单（从 docs/NOTES.md 读，不在这里抄一份）
#
# 为什么收成一条命令：机器若在到期日（2026-11-20）当天到，现场一边建目录、一边找命令、
# 一边翻文档，一天就废了。四项在机器到手前跑完，当天只剩「照单跑」。
#
# 依赖与前置同 .winecheck/README.md（wine 10.0 + Xvfb + 已发布的冒烟器）。
# 冒烟器不是当前源码发出来的时，用 PUBLISH=1 让脚本自己重发（demo.sh 会做那三步）。
# 可覆盖的环境变量：WINEPREFIX / WINE_BIN / DISPLAY / SMOKE_EXE / REPO / NOTES / OUT / TIERS / PUBLISH。
#
# 退出码：0 = ①②③ 全过且动作单打印出来了；非零 = 卡在哪一步就停在哪一步，并打出是哪一步。
set -u
export WINEPREFIX=${WINEPREFIX:-$HOME/.wine-ah}
export WINEDEBUG=${WINEDEBUG:--all}
export DISPLAY=${DISPLAY:-:99}
REPO=${REPO:-$(cd "$(dirname "$0")/.." && pwd)}
NOTES=${NOTES:-$REPO/docs/NOTES.md}

cd "$REPO" || { echo "进不去仓库根：$REPO"; exit 1; }
[ -f "$NOTES" ] || { echo "找不到动作单的出处：$NOTES"; exit 1; }

echo "== 第 0 条：机器到手前的四项准备（仓库根 $REPO）=="
echo "   ① 三档冒烟 → ② 构建与用例 → ③ 九个归档目录 → ④ 打印动作单"
echo "   全部是 Linux 容器内 wine 模拟器证据，不是 Windows 真机结果；G-D5 仍 [ ]。"

echo
echo "-- ① 复跑三档冒烟（.winecheck/demo.sh）--"
bash "$REPO/.winecheck/demo.sh" || {
  echo "① 未通过：三档冒烟有失败行、或逐屏表与报告对不上、或冒烟器不是当前源码发出来的。"
  echo "   先修这一处再往下走——带着失败行进真机当天，等于把模拟器的账念成机器的账。"
  exit 1
}

echo
echo "-- ② 复跑构建与用例（dotnet build + dotnet test）--"
dotnet build "$REPO/ApifoxHelper.csproj" || { echo "② 未通过：构建有错误。"; exit 1; }
dotnet test "$REPO/ApifoxHelper.Tests/ApifoxHelper.Tests.csproj" || { echo "② 未通过：有用例失败。"; exit 1; }

echo
echo "-- ③ 建好九个归档子目录（空目录即可，名字以 docs/evidence/real-machine/README.md 为准）--"
mkdir -p docs/evidence/real-machine/{journey,100,125,200,mixed-dpi,scale-change,fonts,apifox-write,smoke} || {
  echo "③ 未通过：目录建不出来（docs/evidence/real-machine/ 不可写？）。"
  exit 1
}
ls -d docs/evidence/real-machine/*/ | sed 's/^/   /'

echo
echo "-- ④ 七条动作单（从 $NOTES 读，不在这里抄一份）--"
ROWS=$(awk '
  function trim(s) { gsub(/^ +| +$/, "", s); return s }
  /^## G-D5 真机复核清单/ { f = 1; next }
  f && /^## / { exit }
  f && /^\| \[[ x]\] \| [0-9]+\./ {
    n = split($0, c, "|")
    if (n != 7) { print "ERR 动作单有一行不是 5 列（实际 " (n - 2) " 列）：" $0 > "/dev/stderr"; bad = 1; next }
    printf "%s\n   归档：%s\n\n", trim(c[3]), trim(c[5])
  }
  END { if (bad) exit 2 }' "$NOTES") || exit 1

if [ -z "$ROWS" ]; then
  echo "④ 未通过：$NOTES 里没有「## G-D5 真机复核清单」的动作单表格（没有可打印的东西）。"
  exit 1
fi
printf '%s\n' "$ROWS"

COUNT=$(printf '%s\n' "$ROWS" | grep -c '^[0-9]\+\. ')
if [ "$COUNT" != "7" ]; then
  echo "④ 未通过：动作单打印出 $COUNT 条（应为 7 条）——表被改过，先核对再进真机当天。"
  exit 1
fi

echo "== 第 0 条 ①②③ 全过、④ 已打印（$COUNT 条）。真机到手当天照这七条跑，不现场设计。=="
