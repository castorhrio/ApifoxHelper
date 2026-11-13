# 真机证据归档（真机到位当天第一份要读的文件）

> **空目录 = 未达成。** 九个归档子目录（`journey/`、`100/`、`125/`、`200/`、`mixed-dpi/`、`scale-change/`、
> `fonts/`、`apifox-write/`、`smoke/`）**第二百零四轮起已连同 `.gitkeep` 占位进仓**，但每一个都是空的——
> 空目录 = 未达成，所以 G-D5 与 SHARED-3/4/7/8 至今仍未达成：没有 Windows 真机，现有 GUI 证据全部来自
> Linux 容器里的 wine 模拟器。目录先建好只是为了让机器到手当天不卡在「目录不存在」这一步。
>
> **这份文件解决什么**：`docs/NOTES.md` 的「G-D5 真机复核清单（可勾选）」是一张**动作单**（跑什么、量什么、
> 放哪里、什么算过），但它是给人读的登记。真机到位那天真正要回答的是更细的一层：**每个目录里该有哪几个文件、
> 每个文件里该有哪几个数字、缺哪个数就算没跑**。这一层写在这里，动作单那一节只指过来。
>
> **怎么用（四步，别跳）**：
> 1. 先读下面那张表，按行号对着 `docs/NOTES.md` 的 G-D5 动作单跑——行号一一对应，第 N 行就是动作单第 N 条。
> 2. 每跑完一条，把该行「产出物」列点到的文件**全部**放进该行「归档目录」列点到的目录里；文件名照模式写，
>    不要另起一套命名（机械门按目录名与文件数判，不按内容判）。
> 3. 九个目录**全部**齐了、且每一行「什么算过」都成立，才允许回 `docs/NOTES.md` 把对应行从 `[ ]` 改成 `[x]`。
>    **只跑一半就勾，机械门会红**（`ApifoxHelper.Tests/RealMachineEvidenceTests.cs` 的
>    `CheckedAction_RequiresItsArchiveDirectoryToHoldFiles`：勾了哪一条，那一条的归档目录里就必须真有文件）。
> 4. **不用挨个翻目录**：跑 `bash .winecheck/collect-real-machine.sh` 一次核完九个归档目录。它判「有没有东西」
>    的机械定义就是上面第 1 步那句话——**目录里除了隐藏占位（`.gitkeep` 这类以 `.` 开头的文件）之外有文件才算有**；
>    只装着 `.gitkeep` 的目录照样报「还差东西」，并把下面那张表为它点到的产出物文件名逐个打出来
>    （`100/`、`125/`、`200/` 三档各拿自己的档位展开 `<档位>`）。退出码 `0` = 都不空、`1` = 还有空的（已点名）、
>    `2` = 核对本身跑不动（清单读不到 / 解析不出七条动作 / 磁盘上的目录名与清单对不上）。
>    **它不判内容对不对，也不替你打勾**——「放得下放不下」「那三个数落得对不对」仍要人看，勾仍要人回写 `docs/NOTES.md`。
>    逐目录「放什么」由脚本从下面那张表现读现打，**这里不抄第三份清单**（抄的会漂；同一份表已经是动作单与本文件的对账口径）。

> **机器到手前的一条命令（第一百九十八轮；第二百零二轮收成脚本）**：九个归档子目录不再手抄一条 `mkdir`——
> 跑 `bash .winecheck/prepare-real-machine.sh` 一次做完第 0 条的 ①②③（三档冒烟 / 构建与用例 / 建九个目录），
> 最后把 `docs/NOTES.md` 的七条动作单打印出来（④）。**目录名只有一份出处**：脚本里那条
> `mkdir -p docs/evidence/real-machine/{…}`，与下面产出物清单逐字一致。
> **目录在仓里这件事现在有实物**（第二百零四轮）：九个目录各带一个 `.gitkeep` 占位提交进仓，
> 由 `ApifoxHelper.Tests/RealMachineEvidenceTests.cs` 的
> `RealMachineEvidenceTests.EveryArchiveDirectory_ExistsOnDisk` 判——删掉任一个目录、或目录里没有 `.gitkeep`
> 占位，都即红（git 不跟踪空目录，没有占位的话 clone 下来目录又没了）。命令与清单是否一致另由
> `ApifoxHelper.Tests/AcceptanceChecklistTests.cs` 的
> `AcceptanceChecklistTests.BeforeMachineArrives_EveryItemCarriesItsExecutionStatus` 判：清单里少一个目录名、
> 或脚本里多一个清单没有的目录名，都即红——两套名字等于两套判据。

## 产出物清单（与 `docs/NOTES.md` G-D5 动作单逐行对应）

| 动作 | 归档目录 | 产出物（文件名模式） | 每个文件里必须有的数字 | 什么算过 | 量测项（动作单该行「量什么」必须逐项点到） |
| --- | --- | --- | --- | --- | --- |
| 1 | `docs/evidence/real-machine/journey/` | `journey-1-新建配置.png`、`journey-2-新建任务.png`、`journey-3-扫描.png`、`journey-4-注释缺口.png`、`task.json`、`symbols.json` | 每张截图旁记：客户区宽高、当前缩放档（100 / 125 / 200）、该站「下一步」按钮上的文字；`task.json` 记任务名与阶段数，`symbols.json` 记注释位点总数（示例：客户区宽 1400px、高 860px；阶段数 4；注释位点总数 37） | 四站逐站可达、每站只给一个「下一步」且那颗按钮在界面上真实存在；全程不开浏览器、不敲命令行、不改文件 | 逐站可达、只给一个「下一步」、真实存在 |
| 2 | `docs/evidence/real-machine/100/`、`docs/evidence/real-machine/125/`、`docs/evidence/real-machine/200/` | 每档四张：`<档位>-主窗口.png`、`<档位>-新建配置.png`、`<档位>-补充说明.png`、`<档位>-模型服务.png` | 每档每一屏四个数：客户区宽高、说明行文字宽 / 可用宽、状态行文字宽 / 可用宽、是否出现横向滚动条（有 / 无）、底部按钮是否被裁（是 / 否）（wine 基线示例：可用宽 988 / 1243 / 1576px，真机读数照这个格式写） | 三档都无裁切、无横向滚动、按钮都在可视区；「放得下 / 放不下」的判断与 wine 三档（可用宽 988 / 1243 / 1576px）同一档 | 客户区宽高、文字宽、可用宽、横向滚动条、底部按钮是否被裁 |
| 3 | `docs/evidence/real-machine/mixed-dpi/` | `mixed-dpi-拖动前.png`、`mixed-dpi-拖动后.png`、`mixed-dpi-读数.txt` | `mixed-dpi-读数.txt` 一行三个数：因子（该屏 DPI ÷ 96）、客户区宽、说明行可用宽；被屏幕可用区截断时把截断前后两个数都写下来（示例：因子 2.0、客户区宽 2800px、说明行可用宽 2760px）；**再把 `smoke-report.txt` 里那两行 DPI 上下文（控件自报 / 进程）原文一起抄进这一份**——两个数与模拟器两组读数都不对应时，按 `.winecheck/README.md`「125% / 200% 两档的读数不同」那一节写回本条（本条 = 换屏拖到另一档缩放那一支） | 拖动后客户区宽 ≈ 设计宽 × 副屏因子；三种读数（仍是创建那一刻的宽 / 放大两次 / 按新因子重算）按动作单第 3 条那三支记，只有第三支可勾 | 窗体尺寸、控件宽、说明行可用宽、按副屏那一档重算 |
| 4 | `docs/evidence/real-machine/scale-change/` | `scale-change-已开着.png`、`scale-change-新建.png`、`scale-change-读数.txt` | 两屏各自的因子、客户区宽、说明行可用宽（示例：两屏都写 因子 2.0、客户区宽 2800px）；**同样抄下 `smoke-report.txt` 里那两行 DPI 上下文（控件自报 / 进程）原文**——它与第 3 条那两行是同一份报告里的同一对行，只是这一支是原地改档位、不改屏 | 两屏落在同一个数（都按改后的那一档）；两屏相同但不是 设计 × 新因子 要当缺陷记，不当通过 | 是否跟随、新因子还是旧因子 |
| 5 | `docs/evidence/real-machine/fonts/` | `fonts-计数行与差异文本.png`（放大）、`fonts-渲染字体名.txt` | `fonts-渲染字体名.txt` 三行：界面控件、进程默认字体、对话框各自实际命中的字体名；截图里记数字列在筛选前后的横向位置（示例：数字列横向位置 412px → 412px） | 三类文字的实际命中字体名与正文不同、且同一串数字逐字符等宽（截图里量数字列左右边界，筛选 / 刷新前后差 0px）；落到回退项就写实际命中的那一项，不写成「字体不对」 | 实际渲染名、数字列、左右跳 |
| 6 | `docs/evidence/real-machine/apifox-write/` | `plan.json`、`journal.jsonl`、`remote-before.json`、`remote-after.json`、`partial-failure.txt` | 计划内资源条数与远端资源条数、每个动作的指纹与最终状态、部分失败后停在第几个动作（示例：计划内 2 条、远端 2 条、停在第 2 个动作） | 只写了计划内资源、执行前重读并核对指纹、创建防重与 ID 回绑、未确认的高风险项不进计划且没有任何删除动作；部分失败那一支是硬门 | 计划内资源、指纹、创建防重、删除动作 |
| 7 | `docs/evidence/real-machine/smoke/` | `smoke-report.txt` | 报告总通过 / 失败数、每一段的段名（段标题带 `[RunXxx]`）、进程退出码（示例：退出码 0、失败数 0、段数 7 段） | 退出码 0 且 0 失败；同一段在真机与 wine 两处判定一致（段数与通过数不要求等于 wine 那一档的数） | 通过/失败数、段名、同一段的判断不同 |

## 第 3、4 条三个数的具名落点（真机当天照抄，不现场编）

> **这一节解决什么**（第二百一十八轮，简报 P1-1）：产出物清单第 3、4 行说「一行三个数：因子 / 客户区宽 /
> 说明行可用宽」，`docs/NOTES.md` 的「DPI 变更『结果三个数』的照抄三列」说「从哪读」——但**这三个数各自落进
> 哪个文件的哪一行、写成什么格式**仍要现场想。现场想 = 现场发明：同一份读数文件里 `factor=2.0` 与
> `因子：2.0` 两种写法并存，下一个人（或答辩现场的自己）读不出哪个是哪个，而归档核对只判「有没有东西」，
> 判不出「写没写对行」。
>
> **口径与仓内算式同一份**：因子 = 该屏 DPI ÷ 96（`Desktop/Native/DpiScaleFactor.cs` 的 `FromScreenDpi`），
> 保留两位小数；客户区宽 = 设计宽 × 因子、远离零取整（同一文件 `PlanRescale` 回的那个 `ClientWidth`，
> 即 `ScaledExtent` 的口径）；说明行可用宽记逻辑像素，不换算物理像素。**三个数各自只有一个行名**：
> 同一个数在第 3、4 条里用同一个行名，不许一处 `factor=`、另一处 `缩放因子=`。
>
> **机械门**：`ApifoxHelper.Tests/RealMachineEvidenceTests.cs` 的
> `RealMachineEvidenceTests.DpiChangeNumbers_HaveOneNamedLandingLineEach` 判四件事——① 第 3、4 条各三个数
> 一行不缺（顺序也判）；② 每个数只有一个落点行名，三个数的行名互不相同；③ 落点文件必须是产出物清单该行
> 点到的那个 `.txt` 读数文件；④ 格式列必须点到仓内算式（因子点 `DpiScaleFactor.FromScreenDpi`、客户区宽点
> `PlanRescale`、说明行可用宽点「逻辑像素」）。**它不判真机有没有跑**，也不判抄回来的数对不对。

| 动作 | 数 | 落点文件 | 落点行名 | 写什么格式 |
| --- | --- | --- | --- | --- |
| 第 3 条 | 因子 | `docs/evidence/real-machine/mixed-dpi/mixed-dpi-读数.txt` | `factor=` | `factor=2.00`——该屏 DPI ÷ 96、两位小数（与 `DpiScaleFactor.FromScreenDpi` 同一算式）；被屏幕可用区截断时截断前后各一行 |
| 第 3 条 | 客户区宽 | `docs/evidence/real-machine/mixed-dpi/mixed-dpi-读数.txt` | `client-width=` | `client-width=2800`——逻辑像素，与 `DpiScaleFactor.PlanRescale` 回的 `ClientWidth` 同一口径（设计宽 × 因子、远离零取整） |
| 第 3 条 | 说明行可用宽 | `docs/evidence/real-machine/mixed-dpi/mixed-dpi-读数.txt` | `caption-available=` | `caption-available=2760`——逻辑像素，取说明行控件的可用宽（文字宽写在同一个值的括号里，不另起行名） |
| 第 4 条 | 因子 | `docs/evidence/real-machine/scale-change/scale-change-读数.txt` | `factor=` | 改档前后各一行、行名相同：`factor=1.00`（改档前）、`factor=2.00`（改档后），按出现顺序读；算式同 `DpiScaleFactor.FromScreenDpi`（该屏 DPI ÷ 96、两位小数） |
| 第 4 条 | 客户区宽 | `docs/evidence/real-machine/scale-change/scale-change-读数.txt` | `client-width=` | 两屏各一行：`client-width=1020`、`client-width=2040`（与 `DpiScaleFactor.PlanRescale` 的 `ClientWidth` 同一口径） |
| 第 4 条 | 说明行可用宽 | `docs/evidence/real-machine/scale-change/scale-change-读数.txt` | `caption-available=` | 两屏各一行：`caption-available=988`、`caption-available=1976`（逻辑像素） |

## 首程四站逐站预期（真机到位当天照着核对，不现场设计）

> **这一节解决什么**：产出物清单第 1 行说「四站各一张截图 + `task.json` 与 `symbols.json` 副本」，但真机到位当天
> 最容易卡住的不是拍照，是**每一站到底读哪一行、点哪颗按钮、屏幕上应该出现什么**——现场设计这一步，一天就废了。
> 这一节把四站逐站写成可核对清单：读哪一行 / 看哪个控件 / 点到哪一步 / 预期屏幕主张 / 归档文件名。
> 当天只做两件事：照着点、照着拍。
>
> **判据**：每站「预期屏幕主张」成立才算这一站走通；不成立就当场记下实际看到的那一行文字与那颗按钮的标签，
> 按「仍未量到 + 缺什么」写进 `docs/NOTES.md` 的 G-D5 第 1 条下面，**不许改写成通过**。
>
> **机械门**：`ApifoxHelper.Tests/RealMachineEvidenceTests.cs` 的
> `RealMachineEvidenceTests.JourneyStations_EachHasItsExpectedClaimAndArchiveFile` 判四站各一行、每行六格不空、
> 每行点到的归档文件名逐字出现在产出物清单第 1 行里（两边各写一套名字即红）。

| 站 | 读哪一行 | 看哪个控件 | 点到哪一步 | 预期屏幕主张 | 归档文件名 |
| --- | --- | --- | --- | --- | --- |
| 一 | 左侧导航「项目设置」 | 项目列表与「新建配置」按钮 | 点「新建配置」→ 填名称与项目根路径 → 点「保存」 | 保存后回到项目列表，刚建的配置出现在列表里；界面上只给一个「下一步」，且那颗按钮真的在（不是灰的、不是画上去的） | `journey-1-新建配置.png` |
| 二 | 左侧导航「任务」 | 任务列表与「新建任务」按钮 | 点「新建任务」→ 选刚建的配置 → 点「创建并扫描」 | 任务出现在列表里，阶段条停在「扫描源码注释」之前；界面上只给一个「下一步」 | `journey-2-新建任务.png` |
| 三 | 任务详情的阶段条 | 阶段条与「开始扫描」按钮 | 点「开始扫描」→ 等状态行从「执行中」变成完成 | 状态行如实写出扫描结果（接口数与注释位点总数），阶段条推进到下一站；全程没有弹出浏览器、没有要求敲命令行 | `journey-3-扫描.png` |
| 四 | 任务详情的「查看注释缺口」按钮 | 注释缺口页的计数行与列表 | 点「查看注释缺口」→ 读计数行 | 计数行写出注释位点总数，与 `symbols.json` 里的条数一致；列表里每一行都能点开看代码证据 | `journey-4-注释缺口.png` |

## 九个目录未达成时的降级说法（答辩现场照念，不现场组织）

> **这一节解决什么**（第二百六十轮，简报 P0-2）：本文件开头那句「空目录 = 未达成」说的是**判据**，
> 但没说**未达成时答辩现场怎么念**。到期日 2026-11-20 那天机器仍不可得时，现场要一句话说清
> 「这个目录对应哪条验收、没达成时 G-D5 那一格标什么」——此前这一步靠人现场组织，而现场组织出来的说法
> 每场都不一样，最坏的一种是把「没量到」念成「量过了」。这一节把九个目录逐行写成可照念的一句话。
>
> **怎么用**：答辩现场按行念第三列，一个字都不用改；被追问「凭什么」时念第四列那一条断言名
> （它真的在 `ApifoxHelper.Tests/` 里，写错一个字母当场红）。**第三列每一句都必须带 `[ ]`**——
> 那是 G-D5 那一格在未达成时的标记，念的时候不许把它念成「已达成」。
>
> **机械门**：`ApifoxHelper.Tests/RealMachineEvidenceTests.cs` 的
> `RealMachineEvidenceTests.DegradeStatements_CoverEveryArchiveDirectory` 判五件事——① 九个目录各一行、
> 不多不少（多一个清单外的目录即红）；② 每行四格不空；③ 第三列 ≤80 字且带 `[ ]`；
> ④ 第四列点到的断言名真的在测试工程里（或点到 `docs/NOTES.md` 的某一轮）；
> ⑤ 第二列写的「第 N 条」与产出物清单里声明这个目录的那一行**同一个号**（写错号即红）。
> 判据自己抓不抓得住由 `RealMachineEvidenceTests.DegradeStatementDetector_ReportsTheBreaksItMustCatch`
> 拿七发注入当场验。**它不判真机有没有跑**，也不判这些说法念出来好不好听——它只保证
> 「九个目录每一个都有一句可照念的话，且那句话指得到仓里的实物」。

| 归档目录 | 对应验收 | 未达成时的降级说法（≤80 字，照念） | 指到哪（断言 / 轮次） |
| --- | --- | --- | --- |
| `docs/evidence/real-machine/journey/` | G-D5 第 1 条 | 首程四站只在 wine 走通，真机未验；第 1 条保持 [ ]，不念「已走通」。 | `RealMachineEvidenceTests.CheckedAction_RequiresItsArchiveDirectoryToHoldFiles` |
| `docs/evidence/real-machine/100/` | G-D5 第 2 条 | 100% 档无真机截图；三档缺一档即整条不勾，第 2 条保持 [ ]。 | `RealMachineEvidenceTests.Readme_MeasureKeys_AreAllNamedByTheActionListRow` |
| `docs/evidence/real-machine/125/` | G-D5 第 2 条 | 125% 档无真机截图；同上，第 2 条保持 [ ]，不拿模拟器读数顶。 | `RealMachineEvidenceTests.DpiPlaceholder_ExpandsToAllThreeTiersAndBothCellsNameThem` |
| `docs/evidence/real-machine/200/` | G-D5 第 2 条 | 200% 档无真机截图；同上，第 2 条保持 [ ]，不拿模拟器读数顶。 | `RealMachineEvidenceTests.DpiPlaceholder_ExpandsToAllThreeTiersAndBothCellsNameThem` |
| `docs/evidence/real-machine/mixed-dpi/` | G-D5 第 3 条 | 副屏拖动未量；第 3 条保持 [ ]，念「混合 DPI 无真机读数」。 | `RealMachineEvidenceTests.DpiChangeNumbers_HaveOneNamedLandingLineEach` |
| `docs/evidence/real-machine/scale-change/` | G-D5 第 4 条 | 运行中改档未量；第 4 条保持 [ ]，念「两屏是否跟随未验」。 | `RealMachineEvidenceTests.DpiChangeNumbers_HaveOneNamedLandingLineEach` |
| `docs/evidence/real-machine/fonts/` | G-D5 第 5 条 | 字体命中名未核；第 5 条保持 [ ]，念「三类文字实际字体未验」。 | `RealMachineEvidenceTests.ActionRunbook_NamesOnlyPresetControlSlotsAndRealArchiveFiles` |
| `docs/evidence/real-machine/apifox-write/` | G-D5 第 6 条 | 真实写入未跑；第 6 条保持 [ ]，SHARED-3/4/7/8 仍 [ ]，不拿 mock 顶。 | `AcceptanceChecklistTests.Checklist_RealMachineGate_IsStillUnmetAndSaysSo` |
| `docs/evidence/real-machine/smoke/` | G-D5 第 7 条 | 真机冒烟未跑；第 7 条保持 [ ]，念「报告出自 wine 模拟器」。 | `AcceptanceChecklistTests.Gd5_ExpiryVerdict_IsRecordedWithTheDegradationRunPath` |

## 什么算过、什么不算过（判据只有这几条）

- **空目录 = 未达成**。目录在、文件不在，等于没跑。
- **只有截图没有数字 = 未达成**；**只有数字没有截图 = 未达成**。两样都要，因为「放得下」这个判断要人看，
  而「放得下」的依据要能复核。
- **wine 的读数不能填进这些文件**。模拟器的数只能当对照基线（第 2 行那三个可用宽就是基线），
  填进归档目录就是把模拟器说成机器（同一纪律见 `ApifoxHelper.Tests/WordingHonestyTests.cs`）。
- **一条动作的产出物齐了，才允许把 `docs/NOTES.md` 那一行勾成 `[x]`**；量不成的那一条，
  在动作单该行下面补一句「仍未量到 + 缺什么」，不许留空、也不许改写成通过。
- **第 6 行跑通之后**才允许把验收表的 SHARED-3/4/7/8 从 `[ ]` 改成 `[x]`，并同步改动作单结论。

## 机械门判什么、不判什么

- 判：`ApifoxHelper.Tests/RealMachineEvidenceTests.cs` 核「**登记与产出物对得上**」——九个归档目录真实存在于磁盘上
  （`EveryArchiveDirectory_ExistsOnDisk`，第二百零四轮）、本表 7 行与
  `docs/NOTES.md` G-D5 动作单 7 行逐行对应、归档子目录两边同名、动作单每行「量什么」的每一项都被本表
  「量测项」列点到、勾成 `[x]` 的那一条其归档目录里必须真有文件。
  **第二百零八轮起还判一条行为**：`CollectScript_TreatsThePlaceholderAsEmptyAndNamesEveryDirectory`
  真的跑一遍 `bash .winecheck/collect-real-machine.sh`，核「只有 `.gitkeep` 的目录必须报还差东西」
  （占位算成东西 = 九个目录永远「已到位」，这条核对就死了）、九个目录逐个被点名、每个「还差东西」都带着
  本表为它点到的产出物文件名；并拿临时假树把「都不空 → 0」「多一个清单没点到的目录 → 2」两条判据跑活。
  脚本自己的四条失败分支由 `WineCheckScriptsTests.CollectRealMachineScript_NamesItsOwnFailureAndKeepsOneSourceOfTheFileList`
  钉住（含「脚本里不许抄第二份文件名清单」）。
- **第二百零九轮给那条核对补了两条它自己数不到的地板**（本表被改坏而「表有 7 行」仍成立的那两种）：
  **数量地板**——脚本解析出的归档目录集合必须是 9 项（写死的是**数量**，不是那九个名字），少一格即 `exit 2`
  并把现算出的集合整个念出来；**重名地板**——同一个目录被点到两次时九个名字仍齐、数量地板看不见，
  只有这一条拦得住。两种改法都在同一棵临时假树上真跑出退出码 `2`
  （`CollectScript_TreatsThePlaceholderAsEmptyAndNamesEveryDirectory` 的第 ⑥⑦ 两个场景），
  形状由 `WineCheckScriptsTests.CollectRealMachineScript_KeepsTheDirectorySetFloorAtNine` 钉；而**脚本的代码行里
  出现本表那九个目录名任何一个即红**（`CollectScript_NeedsNoneOfTheNineDirectoryNamesOnItsOwn`）——
  抄进脚本就是两份清单，改本表的人不会想起来改那支，那天它按上一版的名字报警。
- **不判：真机有没有跑。** 本机没有 Windows 图形子系统，跑没跑只有人知道；门只保证「不许半勾、
  不许把没归档的说成归档了」。有门不等于过了——G-D5 在验收表上仍是 `[ ]`。
