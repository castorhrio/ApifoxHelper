#!/bin/bash
# 用途：答辩当天的临场预检（第二百二十三轮，简报 P0-1）。
#
# 为什么要有这一支：答辩当天的执行顺序（docs/NOTES.md「答辩当天执行顺序」7 步）与降级路径
# 都演练过，但**临场才会暴露的失败点**一直没人提前核：pwsh 在不在 PATH 里（第二百零七轮的教训：
# 判据成立、结论不成立）、当前源码与读数记录里的 source= 是不是同一版、三档演示到底跑过没有、
# 九个归档目录还在不在。这四件事任何一件不对，现场念出来的数就不是当前源码的数——
# 而现场没有时间查。这一支把四件事收成一条命令，逐条点名到哪一步。
#
# 判什么：① pwsh 找得到（~/.dotnet/tools/pwsh 或 PATH）；② 当前源码指纹 == 读数记录里的 source=；
#         ③ 三档演示跑过且通过（读数记录有 tier= 行、每档 failed=0 且 exit=0、冒烟器旁边有源码指纹）；
#         ④ 九个归档目录在位（调 collect-real-machine.sh，退出码 2 才算不在位）。
# 不判什么：**不判内容对不对**（截图有没有裁切、读数落得对不对要人看），也**不判真机跑没跑**——
#         第 ④ 条只判「目录在位」，九个目录都还空着（真机 0 台）不算预检失败，那是 G-D5 的账。
#
# 可覆盖的环境变量：REPO / READING / SMOKE_EXE / EVIDENCE / README / PWSH。
#
# 退出码：0 = 四条都过，可以开讲；1 = 有一条没过（上面已逐条点名是哪一步、怎么解）。
set -u
HERE=$(cd "$(dirname "$0")" && pwd)
REPO=${REPO:-$(cd "$HERE/.." && pwd)}
READING=${READING:-$REPO/.winecheck/reading.txt}
SMOKE_EXE=${SMOKE_EXE:-/tmp/ah-smoke-pub/ApifoxHelper.Smoke.exe}
EVIDENCE=${EVIDENCE:-$REPO/docs/evidence/real-machine}
README=${README:-$EVIDENCE/README.md}
PWSH=${PWSH:-}

FAILED=""
fail() {
  echo "   $1 未通过：$2"
  FAILED="$FAILED $1"
}

echo "== 答辩当天预检（$REPO）=="
echo "   四条：① pwsh 找得到 ② 源指纹与读数记录一致 ③ 三档演示跑过且通过 ④ 九个归档目录在位"
echo "   任一条不过即非零退出并点名到哪一步；这一支只判「能不能开讲」，不判内容对不对。"

# ① pwsh：第二百零七轮的教训——「PATH 里没有」不等于「机器上没有」。
# 容器里装了 dotnet 全局工具 powershell，落在 ~/.dotnet/tools/pwsh 而不在 PATH。
echo
echo "-- ① pwsh 是否找得到（~/.dotnet/tools/pwsh 或 PATH）--"
PWSH_FOUND=""
if [ -n "$PWSH" ] && [ -x "$PWSH" ]; then
  PWSH_FOUND="$PWSH"
elif [ -x "$HOME/.dotnet/tools/pwsh" ]; then
  PWSH_FOUND="$HOME/.dotnet/tools/pwsh"
elif command -v pwsh >/dev/null 2>&1; then
  PWSH_FOUND=$(command -v pwsh)
fi
if [ -n "$PWSH_FOUND" ]; then
  echo "   ① 找到：$PWSH_FOUND"
else
  fail "①" "pwsh 既不在 PATH 也不在 ~/.dotnet/tools/pwsh——Windows 侧那支讲稿（demo.ps1）现场跑不起来。解一次：dotnet tool install --global PowerShell（装完落在 ~/.dotnet/tools/pwsh，PATH 里没有它也算找到）"
fi

# ② 源指纹：与 demo.sh 同一取法（源码内容、排除 bin/obj 与测试工程），不另立第二份口径。
source_fingerprint() {
  find "$REPO" -name bin -prune -o -name obj -prune -o \
    -name '*.cs' -not -path "$REPO/ApifoxHelper.Tests/*" -print0 \
    | LC_ALL=C sort -z | xargs -0 sha1sum | sha1sum | cut -d' ' -f1
}

echo
echo "-- ② 源指纹是否等于读数记录里的 source= --"
CURRENT=$(source_fingerprint)
RECORDED=""
if [ -f "$READING" ]; then
  RECORDED=$(sed -n 's/^source=//p' "$READING" | head -1)
fi
if [ ! -f "$READING" ]; then
  fail "②" "读数记录不在：$READING——没有它就没有「当前源码三档读数」的出处。解一次：PUBLISH=1 bash .winecheck/demo.sh"
elif [ -z "$RECORDED" ]; then
  fail "②" "读数记录里没有 source= 那一行（$READING）——记录被改过或写了一半，念的数没有可对账的出处。解一次：PUBLISH=1 bash .winecheck/demo.sh"
elif [ "$CURRENT" != "$RECORDED" ]; then
  fail "②" "源指纹对不上：记录里是 $RECORDED，当前源码算出来是 $CURRENT——文档念的那个数属于上一版代码。解一次：PUBLISH=1 bash .winecheck/demo.sh"
else
  echo "   ② 一致：$CURRENT"
fi

# ③ 三档演示跑过且通过：读数记录里每档 failed=0 且 exit=0，冒烟器旁边有源码指纹（PUBLISH=1 才写）。
echo
echo "-- ③ 三档演示是否跑过且通过（PUBLISH=1 bash .winecheck/demo.sh）--"
STAMP="$(dirname "$SMOKE_EXE")/smoke-source.sha"
TIER_LINES=0
BAD_TIERS=0
if [ -f "$READING" ]; then
  TIER_LINES=$(grep -c '^tier=' "$READING" || true)
  BAD_TIERS=$(awk '
    /^tier=/ {
      for (i = 1; i <= NF; i++) {
        split($i, kv, "=")
        if (kv[1] == "failed" && kv[2] != "0") bad = 1
        if (kv[1] == "exit" && kv[2] != "0") bad = 1
      }
    }
    END { print bad + 0 }' "$READING")
fi
if [ "$TIER_LINES" = "0" ]; then
  fail "③" "读数记录里一条 tier= 都没有（$READING）——三档演示没跑过，或跑挂了没留下数。解一次：PUBLISH=1 bash .winecheck/demo.sh"
elif [ "$BAD_TIERS" != "0" ]; then
  fail "③" "读数记录里有档位不是「0 失败、退出码 0」——带着失败行念「wine 量到」就是假绿。先按讲稿逐条查到底层，再 PUBLISH=1 bash .winecheck/demo.sh 复跑"
elif [ ! -f "$STAMP" ]; then
  fail "③" "冒烟器旁边没有源码指纹（$STAMP）——这一档不是 PUBLISH=1 发出来的，判不出它出自哪一版源码。解一次：PUBLISH=1 bash .winecheck/demo.sh"
else
  echo "   ③ $TIER_LINES 档全部 0 失败、退出码 0；冒烟器源码指纹在位：$STAMP"
fi

# ④ 九个归档目录在位：调 collect-real-machine.sh。它自己的三个退出码分工是
# 0 = 都有东西、1 = 还有空的（活儿没干完）、2 = 核对本身跑不动（目录不在位 / 清单读不到）。
# 预检判的是「在位」，所以 1 不算失败——真机 0 台时九个目录本来就该是空的。
echo
echo "-- ④ 九个归档目录是否在位（调 collect-real-machine.sh）--"
# 归档核对与预检必须判同一棵树：EVIDENCE / README 导出给子进程，否则它按自己的缺省去判另一处。
export EVIDENCE README
COLLECT_OUT=$(bash "$HERE/collect-real-machine.sh" 2>&1)
COLLECT_RC=$?
case "$COLLECT_RC" in
  0)
    echo "   ④ 九个目录都在位，且都有东西（真机证据已归档）"
    ;;
  1)
    echo "   ④ 九个目录都在位，但还有空着的——真机 0 台，G-D5 仍 [ ]；这一条判的是「在位」，空着不算预检失败"
    ;;
  *)
    fail "④" "归档核对本身跑不动（collect-real-machine.sh 退出码 $COLLECT_RC）——目录不在位或清单读不到。最后几行：$(printf '%s' "$COLLECT_OUT" | tail -3 | tr '\n' ' ')"
    ;;
esac

echo
if [ -n "$FAILED" ]; then
  echo "预检未通过：${FAILED# } —— 按上面点名的步骤逐条解，解完再跑一次这一支。"
  exit 1
fi
echo "预检通过：四条都过，可以开讲。"
exit 0
