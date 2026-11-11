#!/bin/bash
# 用途：真机不可得时的「降级答辩」一键复跑演示（第一百六十七轮，简报 P0-1 的降级交付）。
#       按 wine 三档缩放各跑一次冒烟，再把 docs/NOTES.md「降级答辩脚本」逐屏表的每一行
#       与那一档的报告对上一次，逐档产出一份答辩时照着念的讲稿：
#       屏 / 讲什么 / 引用了哪一段 / 这一段在该档通过几条失败几条 / 结论口径。
#
#       屏单不在本脚本里抄一份：行是从逐屏表读的，表改了脚本跟着改；表里引用的段在报告里
#       找不到、或某一行格式不对（列数不是 5）时脚本如实报错并非零退出。
#       「逐屏都对得上」这句话的出处是这次对账，不是脚本自己的自述。
#
# 依赖与前置同 .winecheck/README.md：wine 10.0 + Xvfb + 已发布的冒烟器。
# 可覆盖的环境变量：WINEPREFIX / WINE_BIN / DISPLAY / SMOKE_EXE / REPO / NOTES / OUT / TIERS / PUBLISH。
set -u
export WINEPREFIX=${WINEPREFIX:-$HOME/.wine-ah}
export WINEDEBUG=${WINEDEBUG:--all}
export DISPLAY=${DISPLAY:-:99}
WINE_BIN=${WINE_BIN:-wine}
SMOKE_EXE=${SMOKE_EXE:-/tmp/ah-smoke-pub/ApifoxHelper.Smoke.exe}
REPO=${REPO:-$(cd "$(dirname "$0")/.." && pwd)}
NOTES=${NOTES:-$REPO/docs/NOTES.md}
OUT=${OUT:-/tmp/ah-demo}
TIERS=${TIERS:-96 120 192}
PUBLISH=${PUBLISH:-0}
mkdir -p "$OUT"

[ -f "$NOTES" ] || { echo "找不到逐屏表的出处：$NOTES"; exit 1; }

# 第一百六十六轮实测：误用默认前缀时每一扇窗口都构造不出来（20 通过 / 115 失败，
# 全是「Error creating window handle」）。那是环境差异，把它读成产品回归会带偏一整场答辩。
echo "wine 前缀：$WINEPREFIX"
case "$WINEPREFIX" in
  */.wine) echo "  注意：这是默认前缀，通常没有可用的桌面环境，报告里会整片「Error creating window handle」。" ;;
esac

# 念出来的数必须出自当前源码。第一百六十九轮先按 mtime 判过一次，那条路不成立：发布产物是 obj 的
# 硬链接，增量构建不刷新它的时间戳，结果「按脚本给的补救做完仍然被拦」——一个解不开的闸门比没有更糟。
# 现按源码**内容**判：PUBLISH=1 强制重建并把指纹记在产物旁边，没给 PUBLISH 时逐字比那份指纹。
# 测试工程不参与：它不编进这份产物，改了它不该要求重发冒烟器。
source_fingerprint() {
  find "$REPO" -name bin -prune -o -name obj -prune -o \
    -name '*.cs' -not -path "$REPO/ApifoxHelper.Tests/*" -print0 \
    | LC_ALL=C sort -z | xargs -0 sha1sum | sha1sum | cut -d' ' -f1
}

STAMP="$(dirname "$SMOKE_EXE")/smoke-source.sha"
PUBLISH_CMD="dotnet build $REPO/ApifoxHelper.Smoke/ApifoxHelper.Smoke.csproj -c Release -r win-x64 -t:Rebuild && dotnet publish $REPO/ApifoxHelper.Smoke/ApifoxHelper.Smoke.csproj -c Release -r win-x64 --self-contained -o $(dirname "$SMOKE_EXE") && source_fingerprint > $STAMP"

if [ "$PUBLISH" = "1" ]; then
  echo "重发冒烟器（PUBLISH=1，强制重建）：先 build -t:Rebuild，再 publish，最后记源码指纹。"
  dotnet build "$REPO/ApifoxHelper.Smoke/ApifoxHelper.Smoke.csproj" -c Release -r win-x64 -t:Rebuild || exit 1
  dotnet publish "$REPO/ApifoxHelper.Smoke/ApifoxHelper.Smoke.csproj" \
    -c Release -r win-x64 --self-contained -o "$(dirname "$SMOKE_EXE")" || exit 1
  source_fingerprint > "$STAMP" || { echo "源码指纹写不进 $STAMP（产物目录不可写？）"; exit 1; }
elif [ ! -f "$SMOKE_EXE" ]; then
  echo "冒烟器不在 $SMOKE_EXE。设 PUBLISH=1 让本脚本发，或按 .winecheck/README.md 先发布。"
  exit 1
elif [ ! -f "$STAMP" ]; then
  echo "产物旁边没有源码指纹（$STAMP）——无法判断这一档出自哪一版源码，别拿它念当前结论。"
  echo "解一次：PUBLISH=1 bash .winecheck/demo.sh"
  exit 1
elif [ "$(source_fingerprint)" != "$(cat "$STAMP")" ]; then
  echo "冒烟器不是当前源码发出来的（源码内容指纹与 $STAMP 记的那份不一致）。"
  echo "  发的时候记的：$(cat "$STAMP")"
  echo "  当前源码算的：$(source_fingerprint)"
  echo "这一档的通过数属于上一次那版代码。解法（脚本自己就会做这三步）："
  echo "  PUBLISH=1 bash .winecheck/demo.sh"
  echo "  手工等价：$PUBLISH_CMD"
  exit 1
fi

# 逐屏表 → 制表符分隔的五列（# / 屏 / 讲什么 / 证据出处 / 结论口径）。
# 列数不对的行直接判错：少一列的行「看着是一行」，静默跳过的话这一屏就不会出现在讲稿里。
ROWS=$(awk '
  function trim(s) { gsub(/^ +| +$/, "", s); return s }
  /^## 降级答辩脚本/ { f = 1; next }
  f && /^## / { exit }
  f && /^\| [0-9]/ {
    n = split($0, c, "|")
    if (n != 7) { print "ERR 逐屏表有一行不是 5 列（实际 " (n - 2) " 列）：" $0 > "/dev/stderr"; bad = 1; next }
    printf "%s\t%s\t%s\t%s\t%s\n", trim(c[2]), trim(c[3]), trim(c[4]), trim(c[5]), trim(c[6])
  }
  END { if (bad) exit 2 }' "$NOTES") || exit 1

if [ -z "$ROWS" ]; then
  echo "$NOTES 里没有「## 降级答辩脚本」的逐屏表（讲稿没有屏单可依据）。"
  exit 1
fi

# 某一段在该档报告里的通过 / 失败条数：段标题带 [RunXxx]，报告里没有这一段就是 0 0。
seg_stat() {
  awk -v id="[$2]" '
    /^== / { on = index($0, id) > 0; next }
    on && /^PASS / { p++ }
    on && /^FAIL / { f++ }
    END { printf "%d %d", p + 0, f + 0 }' "$1"
}

TOTAL_FAIL=0
TOTAL_MISSING=0
for TIER in $TIERS; do
  REPORT="$OUT/report-$TIER.txt"
  NARRATION="$OUT/narration-$TIER.txt"
  CONSOLE="$OUT/console-$TIER.log"
  echo "== 档位 LogPixels=$TIER（≈$((100 * TIER / 96))%）=="
  rm -f "$REPORT"
  # 改 LogPixels 与跑冒烟必须落在**同一个活显示**里（第一百八十四轮实测修）。
  # 原因：注册表写入与 GUI 进程共用同一个常驻 wineserver。脚本头部的 DISPLAY 默认是 :99，
  # 而那块屏上没有 Xvfb——在死显示里先写注册表，会把 wineserver 的显示状态带坏，
  # 之后同一前缀下的每一扇窗口都构造不出来（报告整片「Error creating window handle」）。
  # 那不是产品回归，是环境差异：把它读成回归会带偏一整场答辩（同 README 记的前缀误用那一族）。
  # 写法保持可核对：这一段里仍然逐字出现 `LogPixels /t REG_DWORD /d "$TIER"` 与 `"--report=$REPORT"`。
  export WINE_BIN SMOKE_EXE
  TIER="$TIER" REPORT="$REPORT" xvfb-run -a bash -c '
    set -u
    export WINEPREFIX DISPLAY
    "$WINE_BIN" reg add "HKCU\Control Panel\Desktop" /v LogPixels /t REG_DWORD /d "$TIER" -f >/dev/null 2>&1 || exit 3
    exec "$WINE_BIN" "$SMOKE_EXE" "--report=$REPORT"
  ' >"$CONSOLE" 2>&1
  RUN_RC=$?
  if [ "$RUN_RC" = "3" ]; then
    echo "  改不了 LogPixels（注册表写入失败），这一档没量成。"; TOTAL_MISSING=$((TOTAL_MISSING + 1)); continue
  fi
  if [ ! -f "$REPORT" ]; then
    echo "  报告没落盘：$REPORT（冒烟器退出码 $RUN_RC，见 $CONSOLE）"
    TOTAL_MISSING=$((TOTAL_MISSING + 1))
    continue
  fi

  FAIL_LINES=$(grep -c '^FAIL ' "$REPORT" || true)
  PASS_LINES=$(grep -c '^PASS ' "$REPORT" || true)
  echo "  报告：$PASS_LINES 通过 / $FAIL_LINES 失败（退出码 $RUN_RC）→ $REPORT"

  : >"$NARRATION"
  {
    echo "降级答辩讲稿 · LogPixels=$TIER（≈$((100 * TIER / 96))%）"
    echo "出处：$NOTES 的「降级答辩脚本」逐屏表；数字来自 $REPORT。"
    echo "以上全部是 Linux 容器内 wine 10.0 + Xvfb 模拟器证据，不是 Windows 真机结果。"
    echo
  } >>"$NARRATION"
  TIERS_FAIL=0
  TIERS_MISSING=0
  while IFS=$'\t' read -r NUM SCREEN WHAT EVIDENCE VERDICT; do
    [ -n "$SCREEN" ] || continue
    SEGS=$(printf '%s' "$EVIDENCE" | grep -oE 'Run[A-Za-z0-9]+' | sort -u)
    {
      printf '%s. %s\n' "$NUM" "$SCREEN"
      printf '   讲什么：%s\n' "$WHAT"
    } >>"$NARRATION"
    if [ -z "$SEGS" ]; then
      # 这一屏没有冒烟段可指：只有源码与编译证据，讲的时候不演示界面、不拿别的屏冒充。
      printf '   证据出处：这一屏没有 wine 冒烟段——只有源码与编译证据，说「代码与用例在，真实端到端未跑」\n' >>"$NARRATION"
    else
      for SEG in $SEGS; do
        read -r P F <<<"$(seg_stat "$REPORT" "$SEG")"
        if [ "$P" = "0" ] && [ "$F" = "0" ]; then
          printf '   引用段：%s —— 报告里没有这一段（逐屏表与冒烟器对不上，先修这一处再答辩）\n' "$SEG" >>"$NARRATION"
          TIERS_MISSING=$((TIERS_MISSING + 1))
        else
          printf '   引用段：%s —— 通过 %s / 失败 %s\n' "$SEG" "$P" "$F" >>"$NARRATION"
          TIERS_FAIL=$((TIERS_FAIL + F))
        fi
      done
    fi
    printf '   结论口径：%s\n\n' "$VERDICT" >>"$NARRATION"
  done <<<"$ROWS"
  echo "  讲稿：$NARRATION（本档引用段合计失败 $TIERS_FAIL 条、对不上 $TIERS_MISSING 处）"
  TOTAL_FAIL=$((TOTAL_FAIL + TIERS_FAIL))
  TOTAL_MISSING=$((TOTAL_MISSING + TIERS_MISSING))
done

# 把缩放改回 100%，别把下一个用同一前缀的人留在 200% 档。
# 这一步同样要在活显示里写：否则它会把常驻 wineserver 的显示状态带坏，
# 下一个用同一前缀的人会撞上「每扇窗口都构造不出来」（与上面同一族原因）。
xvfb-run -a bash -c '
  set -u
  export WINEPREFIX DISPLAY
  "$WINE_BIN" reg add "HKCU\Control Panel\Desktop" /v LogPixels /t REG_DWORD /d 96 -f >/dev/null 2>&1 || true
' || true

cat <<'CALIBER'

录制口径（答辩现场，全文见 .winecheck/README.md「演示与录制口径」）：
  1. 三档各录一段，起点是脚本打出的「档位 LogPixels=…」那一行，屏幕固定 1600×1000（Xvfb）；
  2. 讲序就是 narration-<档位>.txt 的行序（第 1 屏到最后一屏），每屏念「屏 / 讲什么 / 通过几条」；
  3. 结论口径写「源码 + 编译证据」的那几屏不演示界面，只说「代码与用例在，真实端到端未跑」；
  4. 结尾固定一句：以上均为 Linux 容器内 wine 10.0 + Xvfb 模拟器证据，不是 Windows 真机结果；
     真机 0 台，G-D5 未达成（环境供给缺口），到期日 2026-11-20。
CALIBER

if [ "$TOTAL_FAIL" -gt 0 ] || [ "$TOTAL_MISSING" -gt 0 ]; then
  echo "演示未收口：三档累计引用段失败 $TOTAL_FAIL 条、逐屏表与报告对不上 $TOTAL_MISSING 处——按讲稿逐条查到底层再答辩，不带着失败行念「wine 量到」。"
  exit 1
fi
echo "三档逐屏对账完成：每一屏引用的段都在报告里、累计 0 失败。讲稿见 $OUT/narration-*.txt。"
