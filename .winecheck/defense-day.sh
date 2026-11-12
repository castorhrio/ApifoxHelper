#!/bin/bash
# 用途：到期日当天一条命令走完降级答辩（第二百三十七轮，简报 P0-1）。
#
# 为什么要有这一支：当天要做的四件事一直在四支脚本里各占一处——due-date.sh 判今天该走哪一支、
# preflight.sh 核临场那四件事、demo.sh 跑三档并出讲稿、讲稿要人自己打开 narration-<档位>.txt 照着念。
# 每一支都有自己的 README 一节，但**没有任何一条命令告诉当天的人先跑哪一支**：顺序散在四份说明里，
# 而那天在场的时间只有几分钟。这一支不重做那四步里的任何判断，只把顺序定死、一次跑完，
# 任一步非零即停并点名停在第几步（停的那一步自己的输出原样在上面，解法在那几句里）。
#
# 四步（顺序固定，不跳步）：
#   ① due-date.sh     判今天是不是到期日；「已过」是退出码 1，于是第 ① 步就把这一条挡住（见下）
#   ② preflight.sh    临场预检四条：pwsh / 源指纹 / 三档跑过 / 九个归档目录在位
#   ③ demo.sh         按 TIERS 各跑一次冒烟、逐档出讲稿（缺省三档 96 / 120 / 192）
#   ④ 念序摘要 + 结尾固定一句    念序从 narration-<档位>.txt 现读，那句口径从 demo.sh 现读
#
# 四样东西本脚本都不抄第二份：讲稿落点与档位（从 demo.sh 的 OUT / TIERS 缺省现读）、
# 结尾那一句口径（从 demo.sh 的录制口径第 4 条现读）、到期日（连日期字面量都不出现，由 due-date.sh 现读
# docs/NOTES.md 的 G-D5 那一格）、「当天」那句标志（与 due-date.sh 打出的那一句逐字对齐，见第 ① 步）。
# 抄第二份的那种漂移，正是这一族门一直在防的。
#
# 为什么不直接调 collect-real-machine.sh：preflight.sh 的第 ④ 条已经在调它，而且只把退出码 2
# （核对本身跑不动）算失败——真机 0 台时那九个目录本来就该是空的（退出码 1）。这一支若再调它一次
# 并按「任一步非零即停」处理，就会把「归档还空着」这个**当天的正常状态**读成停链理由。
#
# 不判什么：不判内容对不对（讲稿里那几个数对不对要人看）、不判真机跑没跑（真机 0 台，G-D5 仍 [ ]）、
#   不替人打勾，也不判当天该选「按降级答辩执行」还是「延后」——那是答辩负责人的决定。
#   到期日已过时第 ① 步就停，第 ③ 步因此不会跑：那天该做的是改判与回写，不是再烧一遍三档。
#
# 可覆盖的环境变量：REPO（本支只用来打一行抬头）。OUT 与 TIERS 由本支从 demo.sh 现读缺省后 export，
#   好让第 ④ 步读的文件与第 ③ 步写的确实是同一批；其余（PUBLISH / NOTES / TODAY / READING /
#   SMOKE_EXE / EVIDENCE / README / PWSH / WINEPREFIX）原样透传给那三支，本支不重新定义它们的缺省。
#
# 退出码：0 = 四步走完；1 = 停在某一步（转发的就是那一步自己的退出码，上面已点名停在第几步）；
#   2 = 停在第 ① 步且 due-date.sh 判「核对本身跑不动」；
#   3 = 这一支自己跑不动（要调的三支不在位 / 从 demo.sh 读不到缺省或那句口径 / 讲稿读不到）。
set -u
HERE=$(cd "$(dirname "$0")" && pwd)
REPO=${REPO:-$(cd "$HERE/.." && pwd)}

MISSING=""
for SCRIPT in due-date.sh preflight.sh demo.sh; do
  [ -f "$HERE/$SCRIPT" ] || MISSING="$MISSING $SCRIPT"
done
if [ -n "$MISSING" ]; then
  echo "这一支跑不动：$HERE 里缺 ${MISSING# }——它串的就是这几支，缺任何一支都走不完整条四步。"
  exit 3
fi

OUT_DEFAULT=$(sed -n 's/^OUT=${OUT:-\(.*\)}$/\1/p' "$HERE/demo.sh" | head -1)
TIERS_DEFAULT=$(sed -n 's/^TIERS=${TIERS:-\(.*\)}$/\1/p' "$HERE/demo.sh" | head -1)
if [ -z "$OUT_DEFAULT" ] || [ -z "$TIERS_DEFAULT" ]; then
  echo "这一支跑不动：从 $HERE/demo.sh 读不到 OUT / TIERS 的缺省值——第 ④ 步要知道讲稿落在哪、有几份，"
  echo "而这两个数只有 demo.sh 一份出处；在这里写死一份，改了那一支的人不会知道这里还留着一份旧的。"
  exit 3
fi
OUT=${OUT:-$OUT_DEFAULT}
TIERS=${TIERS:-$TIERS_DEFAULT}
export OUT TIERS

echo "== 到期日当天降级答辩（$REPO）=="
echo "   四步：① 到期日核对 → ② 临场预检四条 → ③ 按档位跑冒烟出讲稿 → ④ 念序摘要与结尾固定一句"
echo "   任一步非零即停，并点名停在第几步；那一步自己的输出原样在上面，解法在那几句里。"

echo
echo "-- ① 到期日核对（due-date.sh）--"
# 当天标志与 due-date.sh 打出的那一句逐字对齐（词表不在这里发明）：
# WineCheckScriptsTests.DefenseDayScript_KeepsOneSourceOfTheDateThePathsAndTheClosingSentence
# 从 due-date.sh 的「当天」那一支现读这句再对——改了那一支的措辞，这条门当场红。
# 认不出那一句时，下面走的是「不重说」那一句——它在未到 / 当天两种情况下都成立，
# 于是措辞漂了也不会把「当天」念成「未到」。
TODAY_MARKER="今天是到期日"
DUE_OUT=$(bash "$HERE/due-date.sh" 2>&1)
RC=$?
printf '%s\n' "$DUE_OUT"
if [ "$RC" != "0" ]; then
  echo "停在第 ① 步：due-date.sh 退出码 $RC——上面那几句就是它点名的原因。"
  echo "  退出码 1 = 到期日已过：那天该改判并按降级路径回写 G-D5 那一格，不该再跑这一条；"
  echo "  退出码 2 = 核对本身跑不动（NOTES 读不到 / G-D5 那一行里没有到期日）：先解这一条再跑这一支。"
  exit "$RC"
fi
if printf '%s\n' "$DUE_OUT" | grep -q "$TODAY_MARKER"; then
  echo "   ① 过：今天就是到期日——第 ③ 步出的那几份讲稿（档位 $TIERS）就是当天要念的那几份。"
else
  echo "   ① 过：due-date.sh 退出码 0（未到 / 当天都该继续干活），上面那一句已写明是哪一种，本脚本不重说。"
fi

echo
echo "-- ② 临场预检四条（preflight.sh）--"
bash "$HERE/preflight.sh"
RC=$?
if [ "$RC" != "0" ]; then
  echo "停在第 ② 步：preflight.sh 退出码 $RC——它上面已点名是四条里的哪一条、怎么解。"
  echo "  编号口径先说清：上面那些「① / ② / ③ / ④ 未通过」是 preflight 自己那四条预检的编号，"
  echo "  不是这一支的四步编号；这一支只报「停在第几步」，看那几行就行。"
  echo "  带着预检没过的环境跑第 ③ 步，念出来的数属于上一版代码或另一台机器；先解那一条再跑这一支。"
  exit "$RC"
fi
echo "   ② 过：四条都过，可以往下跑冒烟（档位 $TIERS）。"

echo
echo "-- ③ 跑冒烟与讲稿（demo.sh：档位 $TIERS，落点 $OUT）--"
bash "$HERE/demo.sh"
RC=$?
if [ "$RC" != "0" ]; then
  echo "停在第 ③ 步：demo.sh 退出码 $RC——上面那几句是它点名的（哪一屏、哪一段对不上，还是冒烟器不属于当前源码）。"
  echo "  冒烟器不属于当前源码时的解法就是让它自己重发：PUBLISH=1 bash .winecheck/defense-day.sh。"
  echo "  带着失败行念「wine 量到」就是假绿，所以这一步不过时不再往下走。"
  exit "$RC"
fi
echo "   ③ 过：档位 $TIERS 跑完，讲稿在 $OUT。"

echo
echo "-- ④ 念序摘要与结尾固定一句--"
CLOSING=$(awk '
  !f && /结尾固定一句：/ { f = 1; sub(/^ *[0-9]+\. 结尾固定一句：/, ""); s = $0; next }
  f && (/^CALIBER$/ || /^ *[0-9]+\./) { exit }
  f { sub(/^ +/, "", $0); s = (s ~ /[；：，。]$/) ? s $0 : s " " $0 }
  END { gsub(/  +/, " ", s); if (s != "") print s }' "$HERE/demo.sh")
if [ -z "$CLOSING" ]; then
  echo "停在第 ④ 步：从 $HERE/demo.sh 读不到「结尾固定一句」那一条口径。"
  echo "  那一句只有一份出处（demo.sh 的录制口径第 4 条），在这里抄第二份就会漂；读不到就不念那句。"
  exit 3
fi

FIRST_TIER=1
for TIER in $TIERS; do
  NARRATION="$OUT/narration-$TIER.txt"
  if [ ! -f "$NARRATION" ]; then
    echo "停在第 ④ 步：第 ③ 步刚跑完却读不到 $NARRATION——讲稿不在，当天就没有可照念的屏序（先查 $OUT 可写否）。"
    exit 3
  fi
  SCREENS=$(grep -c -E '^[0-9]+\. ' "$NARRATION" || true)
  echo "   档位 $TIER：$SCREENS 屏 · 讲稿 $NARRATION"
  if [ "$FIRST_TIER" = "1" ]; then
    echo "   念序（屏单三档同一份逐屏表，这里取档位 $TIER 的讲稿现读；每屏念「屏 / 讲什么 / 通过几条失败几条」）："
    grep -E '^[0-9]+\. ' "$NARRATION" | sed 's/^/     /'
    FIRST_TIER=0
  fi
done
echo "   结尾固定一句（照念，出处 demo.sh 的录制口径第 4 条）：$CLOSING"

echo
echo "四步走完。当天剩下的两件事这一支不代做："
echo "  ① 判定（按降级答辩执行 / 延后并给出新的到位日期）由答辩负责人作出，"
echo "     见 docs/NOTES.md「到期日当天执行单」开头那三行（①谁在何时决定）；"
echo "  ② 回写两处——docs/NOTES.md 的 G-D5 那一格与 README.md「已知限制」第 1 条，"
echo "     两处措辞必须逐字一致（同页第 3 步）；勾选框只由真机证据关闭，这一支不替人打勾。"
exit 0
