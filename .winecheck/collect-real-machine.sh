#!/bin/bash
# 用途：真机证据归档核对——一条命令回答「九个归档目录里到底有没有东西」，
#       并对还空的目录逐条点名「哪一条动作没做完、该往里放哪些文件名」。
#
# 为什么要有这一支：docs/NOTES.md 的 G-D5 动作单与 docs/evidence/real-machine/README.md
# 早就写着「空目录 = 未达成」，可这句话至今只是文档里的自觉。真机到手当天要跑七条动作、
# 往九个目录里放二十几个文件，跑到哪一步、哪个目录还缺东西，全靠人挨个翻目录；
# 而最坏的读法不是翻得慢，是「目录里有两张图就以为这条齐了」。这一支把「有没有东西」
# 从自觉变成一条可跑的核对——它不加任何新产品能力，只是把已有验收清单的最后一段路铺实。
#
# 判什么：一个目录里除了隐藏占位（.gitkeep 这类）之外有没有文件。有 = 有东西，没有 = 还差东西。
# 不判什么：**不判内容对不对**——截图有没有裁切、读数那三个数落得对不对、字段名写没写，
#          这些都要人看（本机也没有那台机器）。它也**不打勾**：勾由人按那份 README 的
#          「什么算过」回写 docs/NOTES.md，这一支只保证不许「半勾」。
#
# 目录名与「该放什么」只有一份出处：docs/evidence/real-machine/README.md 的产出物清单。
# 本脚本从那张表逐行读，不在这里抄第二份文件名（抄的会漂；同一纪律见 prepare-real-machine.sh
# 从 docs/NOTES.md 读动作单而不另抄一份）。清单里写 <档位> 占位符的那一行，按目录名展开。
#
# 真机当天的用法：跑完一条动作就跑一次这一支，看还差哪几个目录；九个都不空了，
# 再回 docs/NOTES.md 逐条判「什么算过」并回写勾选。
#
# 可覆盖的环境变量：REPO / EVIDENCE / README。
#
# 退出码：0 = 九个归档目录都有东西；
#         1 = 还有目录空着（上面已逐条点名，G-D5 仍不能勾）；
#         2 = 核对本身跑不动（清单读不到、表解析不出七条动作、目录集合凑不满九个或重名、清单与磁盘的目录名对不上）。
# 1 与 2 分开是刻意的：1 说的是「活儿还没干完」，2 说的是「这条核对失效了」。把 2 读成 1
# 会白改一天目录，把 1 读成 2 会带着一个坏掉的核对进答辩。
set -u
REPO=${REPO:-$(cd "$(dirname "$0")/.." && pwd)}
EVIDENCE=${EVIDENCE:-$REPO/docs/evidence/real-machine}
README=${README:-$EVIDENCE/README.md}

echo "== 真机证据归档核对（$EVIDENCE）=="
echo "   只判「有没有东西」：除隐藏占位（.gitkeep 这类）之外有文件就算有；内容对不对要人看，本脚本不判。"

echo
echo "-- ① 读产出物清单（$README）--"
[ -f "$README" ] || {
  echo "① 未通过：找不到产出物清单：$README——「该放什么」没有出处，这一支就退化成只看空不空。"
  exit 2
}

ROWS=$(awk '
  BEGIN { FS = "|" }
  /^\| [0-9]+ \|/ {
    act = $2; gsub(/[^0-9]/, "", act)
    cell = $3
    while (match(cell, /docs\/evidence\/real-machine\/[^/`]+\//)) {
      dir = substr(cell, RSTART + 27, RLENGTH - 28)
      cell = substr(cell, RSTART + RLENGTH)
      f = $4; names = ""
      while (match(f, /`[^`]+`/)) {
        name = substr(f, RSTART + 1, RLENGTH - 2)
        f = substr(f, RSTART + RLENGTH)
        if (index(name, ".") > 0) names = names (names == "" ? "" : "、") name
      }
      gsub(/<档位>|<dpi>/, dir, names)
      printf "%s\t%s\t%s\n", dir, act, names
    }
  }' "$README") || {
  echo "① 未通过：读 $README 时 awk 本身就失败了（不可读，或不是文本）。"
  exit 2
}

if [ -z "$ROWS" ]; then
  echo "① 未通过：清单里一行都没解析出来（产出物那张表被改成散文了？没有表就无从核对）。"
  exit 2
fi
ACTIONS=$(printf '%s\n' "$ROWS" | cut -f2 | sort -u | wc -l | tr -d ' ')
if [ "$ACTIONS" != "7" ]; then
  echo "① 未通过：清单解析出 $ACTIONS 条动作（应为 7 条）——表被改过，先核对表，再拿这一支出结论。"
  exit 2
fi

# 下面两条数的是**目录集合本身**（上一条只数「认出几条动作」，数不到目录）。各挡一种：
# 重名挡「九个名字仍齐、但同一个目录被点了两次」，数量地板挡「行数凑得够、目录却少了一个或多了一个」。
# 9 是**数量**地板、不是第二份清单：目录名仍只从上面那张表读出来现算，与
# ApifoxHelper.Tests/RealMachineEvidenceTests.ExpectedDirectories 那九个名字的逐个对齐由那条门判。
DUP_DIRS=$(printf '%s\n' "$ROWS" | cut -f1 | sort | uniq -d)
if [ -n "$DUP_DIRS" ]; then
  echo "① 未通过：清单把同一个归档目录点到两次：$(printf '%s ' $DUP_DIRS)——一条动作的产出物散进两个名字、或两条动作抢同一个名字，当天都得自己猜哪个才是落点。"
  exit 2
fi

# 「某一行的目录格被改成不匹配的形状、整行解析不出目录」不在这里单列一条：动作号在表里逐行唯一，
# 那种改法会先让上一条「应为 7 条」红。这一条只补上一条数不到的那一种：行数凑得够、目录却少了或多了。
DIR_COUNT=$(printf '%s\n' "$ROWS" | cut -f1 | sort -u | grep -c .)
if [ "$DIR_COUNT" != "9" ]; then
  echo "① 未通过：清单解析出 $DIR_COUNT 个归档目录（地板 9 个：三档 DPI 各占一个）。现算出的集合：$(printf '%s\n' "$ROWS" | cut -f1 | sort -u | tr '\n' ' ')——动作数 7 与重名那两条都没中，这一条说的就是「行数凑得够、目录格却少了一个或多了一个」：回到 $README 的「归档目录」那一列逐个点数，并与 docs/NOTES.md 动作单的「归档到哪里」对齐。"
  exit 2
fi
echo "   清单点到 7 条动作、$(printf '%s\n' "$ROWS" | wc -l | tr -d ' ') 个归档目录（三档 DPI 各占一个，故多于 7）。"

echo
echo "-- ② 逐目录核对（目录名以清单为准，不自己起名）--"
LISTED=":"
ABSENT=""
ORPHANS=""
PENDING=""
DONE_COUNT=0
PENDING_COUNT=0

while IFS=$'\t' read -r DIR ACT NAMES; do
  [ -n "$DIR" ] || continue
  LISTED="$LISTED$DIR:"
  if [ ! -d "$EVIDENCE/$DIR" ]; then
    ABSENT="$ABSENT$DIR/ "
    continue
  fi
  COUNT=$(ls -1A "$EVIDENCE/$DIR" 2>/dev/null | grep -cv '^\.' || true)
  if [ "$COUNT" -gt 0 ]; then
    DONE_COUNT=$((DONE_COUNT + 1))
    # 分隔符不能用 tr：它按字节处理多字节字符，「、」只吐出最后一个字节（0xE3），念出来是碎片。
    FILES=$(ls -1A "$EVIDENCE/$DIR" | grep -v '^\.' | awk 'NR > 1 { printf "、" } { printf "%s", $0 }')
    echo "   [已有 $COUNT 件] $DIR/ ← 动作 $ACT：$FILES"
  else
    PENDING_COUNT=$((PENDING_COUNT + 1))
    PENDING="$PENDING $DIR/"
    echo "   [还差东西] $DIR/ ← 动作 $ACT 还没归档（里面只有占位，或什么都没有）"
    printf '%s\n' "$NAMES" | sed 's/^/            该放：/'
  fi
done <<< "$ROWS"

if [ -n "$ABSENT" ]; then
  echo "② 未通过：清单点到的目录在磁盘上不存在：$ABSENT——先跑 bash .winecheck/prepare-real-machine.sh 的第 ③ 步建目录；目录名只有一份出处，别自己起。"
  exit 2
fi

for ENTRY in "$EVIDENCE"/*/; do
  [ -d "$ENTRY" ] || continue
  NAME=$(basename "$ENTRY")
  case "$LISTED" in
    *":$NAME:"*) ;;
    *) ORPHANS="$ORPHANS$NAME/ " ;;
  esac
done
if [ -n "$ORPHANS" ]; then
  echo "② 未通过：磁盘上有清单没点到的目录：$ORPHANS——要么按清单改名归档，要么先改清单；两套目录名等于两套判据，归档到没人认的名字上等于没归档。"
  exit 2
fi

echo
echo "-- ③ 汇成一句话 --"
echo "   有东西的目录 $DONE_COUNT / $((DONE_COUNT + PENDING_COUNT)) 个。"
if [ "$PENDING_COUNT" -gt 0 ]; then
  echo "   还差东西的目录：$PENDING"
  echo "归档核对未收口：$PENDING_COUNT 个目录还空着——空目录 = 未达成，docs/NOTES.md 的 G-D5 与 SHARED-3/4/7/8 都不能勾。"
  exit 1
fi

echo "归档核对通过：$DONE_COUNT 个目录都不空了。这一句到此为止——勾不勾仍由人按 $README 的「什么算过」判："
echo "本脚本不判内容对不对，也不替你写回 docs/NOTES.md。"
exit 0
