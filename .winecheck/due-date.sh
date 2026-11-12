#!/bin/bash
# 用途：到期日当天唯一能照着走的核对（第二百二十五轮，简报 P0-1）。
#
# 为什么要有这一支：到期日（docs/NOTES.md 的 G-D5 行里那一个，本脚本现读、不抄第二份）这件事在仓里被写进了三处（那一行、
# 「到期日判定（细则）」子段、「到期日当天执行单」那页），但**没有任何东西能在当天告诉人
# 「现在该走降级还是该判过期」**——两条既有门判的是「到期日不是过去的也不是未来的」
# （AcceptanceChecklistTests.Gd5DueDate_IsNotStaleOrFuture ①②④）与「那一段在位且日期一致」
# （Gd5_ExpiryVerdict_IsRecordedWithTheDegradationRunPath），**都判不到「今天是不是到期日」**。
# 当天的人只能自己看日历。这一支把那一格里的日期现读出来，与系统日期比，三个分支各给一句
# 当天能照着走的话。
#
# 判什么：G-D5 那一行里记的到期日（现读，不抄第二份）与今天的关系——
#   未到 → 打「距到期还有 N 天，按现有路径继续」，退出 0；
#   当天 → 打「今天是到期日，走降级答辩」三步，退出 0；
#   已过 → 打「到期日已过，G-D5 应改判『已过期』」，退出 1。
# 不判什么：**不判真机跑没跑**（那台机器仍是 0 台）、**不替人打勾**、**不改 G-D5 那一格**
#   （勾选框只由真机证据关闭）。它只回答「今天该走哪一支」。
#
# 词表与兄弟门逐字对齐：已过那一支打出来的「已过期」与「降级」两个词，正是
# AcceptanceChecklistTests.Gd5DueDate_IsNotStaleOrFuture ③ 在到期日过去之后读的那两个词。
# 第二百一十一轮踩过这个坑（预登记写「已到期」而兄弟门读「已过期」，那天红的会是格子），
# 所以这一支不自己发明措辞，由 DueDateScriptTests 从兄弟门源码里现读那两个词来对。
#
# 可覆盖的环境变量：REPO / NOTES / TODAY。
#
# 退出码：0 = 未到或当天（两种都该继续干活）；1 = 到期日已过（该改判「已过期」）；
#         2 = 核对本身跑不动（NOTES 读不到 / G-D5 行找不到 / 行里没有到期日 / TODAY 不是日期）。
set -u
HERE=$(cd "$(dirname "$0")" && pwd)
REPO=${REPO:-$(cd "$HERE/.." && pwd)}
NOTES=${NOTES:-$REPO/docs/NOTES.md}
TODAY=${TODAY:-$(date +%F)}

echo "== 到期日核对（$NOTES）=="
echo "   三个分支：未到 → 继续；当天 → 走降级答辩；已过 → 改判（见下）。今天按 $TODAY 算。"

if [ ! -f "$NOTES" ]; then
  echo "   核对跑不动：读不到 $NOTES——到期日没有出处，判不了今天该走哪一支。"
  exit 2
fi

# G-D5 那一行：与 AcceptanceChecklistTests 挑那一格的判据同一个模式（勾与没勾都命中）。
ROW=$(grep -E '^\| \[.\] \| G-D5' "$NOTES" | head -1)
if [ -z "$ROW" ]; then
  echo "   核对跑不动：$NOTES 里找不到 G-D5 那一行（\`| [ ] | G-D5 …\`）——到期日没有出处。"
  exit 2
fi

# 到期日：与 AcceptanceChecklistTests.DueDatePattern 同一个取法（「到期日」之后 8 个字以内的
# 第一个 YYYY-MM-DD）。用 grep -o 取最左一处，不用 sed 的贪婪 `.*到期日`——那一格后面还有
# 「到期日判定」四个字，贪婪取法会从最后一处「到期日」开始找，找不到日期就整行不匹配。
DUE=$(printf '%s\n' "$ROW" \
  | grep -oE '到期日[^0-9]{0,8}[0-9]{4}-[0-9]{2}-[0-9]{2}' \
  | head -1 \
  | grep -oE '[0-9]{4}-[0-9]{2}-[0-9]{2}')
if [ -z "$DUE" ]; then
  echo "   核对跑不动：G-D5 那一行里没有「到期日 YYYY-MM-DD」这样的固定日期——判不了今天是不是到期日。"
  exit 2
fi

if ! printf '%s' "$TODAY" | grep -qE '^[0-9]{4}-[0-9]{2}-[0-9]{2}$'; then
  echo "   核对跑不动：TODAY=「$TODAY」不是 YYYY-MM-DD——日期比不了。"
  exit 2
fi

# 天数：ISO 日期按字典序比就是按时间比，所以三个分支只用字符串比较；
# 天数只在能算的时候算（这台机器的 date 不支持 -d 时如实说算不出来，不编一个数）。
DUE_S=$(date -d "$DUE" +%s 2>/dev/null || true)
TODAY_S=$(date -d "$TODAY" +%s 2>/dev/null || true)
DAYS=""
if [ -n "$DUE_S" ] && [ -n "$TODAY_S" ]; then
  DAYS=$(( (DUE_S - TODAY_S) / 86400 ))
fi

# 降级答辩那三份讲稿的落点：目录取 demo.sh 里 OUT 的缺省值（不抄第二份），
# 文件名取脚本自己拼的那个形状（narration-<档位>.txt）。
OUT_DEFAULT=$(sed -n 's/^OUT=${OUT:-\(.*\)}$/\1/p' "$HERE/demo.sh" 2>/dev/null | head -1)
if [ -n "$OUT_DEFAULT" ]; then
  NARRATION=$OUT_DEFAULT
else
  NARRATION="（demo.sh 里读不到 OUT 的缺省值，按脚本现算的落点念）"
fi

if [[ "$DUE" > "$TODAY" ]]; then
  if [ -n "$DAYS" ]; then
    echo "   未到：到期日 $DUE，今天 $TODAY，距到期还有 $DAYS 天——按现有路径继续。"
  else
    echo "   未到：到期日 $DUE，今天 $TODAY——按现有路径继续（这台机器的 date 不支持 -d，天数算不出来）。"
  fi
  echo "   真机到手就照 docs/NOTES.md「G-D5 真机复核清单（可勾选）」逐条跑；这一支不判真机跑没跑。"
  exit 0
fi

if [[ "$DUE" == "$TODAY" ]]; then
  echo "   今天是到期日 $DUE：走降级答辩——"
  echo "     ① PUBLISH=1 bash .winecheck/demo.sh（三档各跑一次冒烟，逐档出讲稿）"
  echo "     ② 念 $NARRATION/narration-<档位>.txt（96 / 120 / 192 三份）"
  echo "     ③ 如实标 G-D5 未达成（回写落点见 docs/NOTES.md G-D5 行）"
  echo "   先跑 bash .winecheck/collect-real-machine.sh 看是否仍报 有东西 0 / 9、退出码 1——"
  echo "   那是「到期日判定（细则）」那一段的触发条件；这一支不替你判那一条。"
  exit 0
fi

# 已过：措辞与兄弟门 Gd5DueDate_IsNotStaleOrFuture ③ 的词表逐字对齐（「已过期」+「降级」）。
if [ -n "$DAYS" ]; then
  echo "   到期日已过：到期日 $DUE，今天 $TODAY（已过 $(( -DAYS )) 天）——G-D5 应改判「已过期」，并按「降级」路径回写。"
else
  echo "   到期日已过：到期日 $DUE，今天 $TODAY——G-D5 应改判「已过期」，并按「降级」路径回写。"
fi
echo "   回写落点见 docs/NOTES.md G-D5 行：同一格必须同时留着「已过期」与「降级」两个词——"
echo "   兄弟门 AcceptanceChecklistTests.Gd5DueDate_IsNotStaleOrFuture ③ 读的就是这两个词，缺一个就红在那一格。"
exit 1
