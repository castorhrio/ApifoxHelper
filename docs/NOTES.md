# Apifox 助手开发笔记

本目录是独立小工具，与 PNOS 业务项目无引用关系，不加入 PNOS 解决方案。

## 已核实的环境事实（2026-10-31）

- 本机 SDK：9.0.312、10.0.x；存在 net8.0 targeting pack（8.0.33），工具目标框架 net8.0。
- Apifox CLI：npm 包 `apifox-cli` 2.2.6，入口 `%APPDATA%\npm\node_modules\apifox-cli\bin\cli.js`，用 `node` + 参数列表直接调用，不经 cmd。
- CLI 非 TTY 时 stdout 为纯 JSON：`{ success, resource, operation, data | error{code,message,suggestion}, context, meta, agentHints }`。
- 退出码：0 成功；1 操作/API 失败（如 404000 Not found）；2 认证失败；3 输入/校验失败；4 Schema/发现失败；5 内部错误。
- 分支参数：`--branch <branchName>`（纯数字兼容旧 branchId），不传为主分支。
- `endpoint list` 分页：`--page N --page-size 500`；`meta` 含 total/returned/page/pageSize/totalPages/nextPage。
- `schema list` 不支持 `--page`。
- `endpoint update` / `schema update`：只提交传入的顶层字段；数组和嵌套对象整体替换，不按 id 合并。
- 写入前：`cli-schema validate <endpoint-create|endpoint-update|schema-create|schema-update> --file <path>`。
- `export --format apifox --no-include-api-cases --output <file>`：整分支一次导出（1075 个接口约 3 秒），
  接口在 `apiCollection` 目录树的 `items[].api`，模型在 `schemaCollection` 的 `items[]`，模型 id 形如 `#/definitions/<id>`。
- Apifox 模型 nullable 写法：`"type": ["string", "null"]`；显示顺序 `x-apifox-orders`。

## 已验证进度

- 源码扫描（PNOS 只读）：`GET /api/finance/WmsBillingClientList` 追踪到 data = `List<WmsBillingClientResponse>`，异常分支经 ToFinanceError/MapFinanceError，加载 8 个项目约 3 秒。
- 回写（临时样例项目）：CRLF+BOM 与 LF 无 BOM 文件均保留；`#if`、普通注释不变；失效 param 删除；确认后改文件被 409 阻断。
- 导出补丁：`git -c core.autocrlf=false apply` 结果与工具回写逐字节一致（开启 autocrlf 时 git 会把 LF 文件转成 CRLF）。
- 契约检查（只读，项目 5545363 主分支，人工成本 5 个接口）：扫描 2.9 秒；加载 Swagger + export 概览 + 5 个接口详情 + 引用模型约 9 秒。
- 同步计划（只读生成）：模型更新在前、接口更新在后，两个写入文件都通过真实 `cli-schema validate`；引用未确认绑定模型的接口会被排除。
- 写入门禁：CLI 2.2.6 未登记探测时，生成计划被拒绝。
- 页面：无头 Edge 截图确认任务列表、源码注释、Apifox 差异页可正常渲染；访问控制（无令牌 401、异常 Origin/Host 403）通过。

## 桌面版（2026-10-31）

- 形态：WinForms + WebView2 外壳，进程内运行原有本地服务；`net8.0-windows`、WinExe；`--headless` 保留无界面模式。
- 发布：`publish.ps1` 生成自包含单文件 `dist\win-x64\ApifoxHelper.exe`（77.6 MB，无附带文件）。
- 已验证（调试版与发布版）：窗口与页面渲染、单实例（第二次启动 0.04 秒退出并恢复已最小化的窗口）、点击关闭与外部 WM_CLOSE 两种关闭路径（作业执行中关闭会先取消作业）、缺少 WebView2 运行时的提示界面、下载弹出“另存为”并写入所选路径、发布版中 Roslyn 扫描 PNOS 正常（单文件模式下从 PATH/安装目录找到 .NET 参考程序集）。
- WebView2 包默认引用 WPF 包装程序集，已在项目文件中排除（只用 WinForms）。

## 交互打磨与图标（2026-11-01）

- 任务页：阶段条 + 唯一“下一步”（`nextStep()` 只读 `task.stages`，不改状态；写入类操作仍在各页签内确认）；页签按阶段分组，下一步所在页签显示圆点。
- 修复：差异页“生成同步预览”先无条件保存决策与补充说明，保存会递增任务版本，随后生成计划必然 409。现在决策/补充内容未变化时保存不写文件、不递增版本；页面上有未保存的确认或检查已过期时，只提供“保存并重新检查”，不能直接生成计划。
- 检查完成时在远端阶段记录 `CheckedRevision`；版本落后时任务列表与“下一步”提示重新检查。
- 源码页逐条审阅只局部更新（不重新拉取扫描结果），接受后前进到下一条；筛选与当前位置按任务保留。
- 新接口：`/api/local/projects`（列出 .csproj，跳过 bin/obj/隐藏目录与联接点，深度与数量有上限）、`/api/local/pick-folder`（只在桌面窗口可用，界面线程模态显示，同一时间一个）。
- 图标：按 logo-design 技能做了三个灰度概念（注释对勾 / 注释文档页 / 书签对勾），采用“注释对勾”；16–24 px 用去掉一笔斜杠的小尺寸版。ICO：16–64 px 为 32 位 DIB + AND 掩码，256 px 为 PNG。
- 已验证：无头 Edge 驱动页面（创建并扫描、接受并下一条、未保存确认 → 保存并重新检查、过期提示、查找 API 项目、Esc 关闭对话框），页面无脚本错误；桌面窗口中“选择目录”弹出并以窗口为所有者模态、并发请求返回 409、取消返回空；对部门模块（12 个接口）做了只读检查。

## 尚未验证（需要专用测试目标）

- 写入兼容探测、计划执行（逐项写入、回读核对、超时回读、部分失败）没有对任何 Apifox 项目实际运行。
- 模型服务生成链路只验证了请求构造与结果校验代码可编译，没有真实服务密钥。

## 交互形态重构首个切片（2026-11-07）

- D1 决策：桌面技术栈选 WinForms 原生，决策记录在 `docs/DECISIONS/D1-桌面技术栈.md`（CI 可编译、差异审阅表格/树控件、单文件分发为主要依据；明确本仓尚未在 Linux CI 实测编译，首次可用的 dotnet build 是决策生效前置验证）。
- 设计基线：`docs/DESIGN.md`（唯一视觉来源：灰阶 + 墨水蓝 #0B5394 单强调色、四档字号、五档间距、双档圆角、状态表模板）；代码侧唯一落点 `Desktop/Native/UiTokens.cs`。如实挂账：WinForms 控件圆角未自绘、真机 DPI 截图未拍。
- 原生纵切片（入口 `ApifoxHelper.exe --native`）：`Desktop/Native/` 下 NativeShellForm（原生菜单 + 左侧导航，无"在浏览器中打开"）、ProjectsPage（配置列表/新建/编辑核心字段，复用 ProfileStore 校验与 ProjectFinder）、TasksPage（任务列表 + 阶段概览 + 新建任务对话框 + 源码扫描进度/取消，复用 TaskStore/SourceScanService/JobRunner）。服务经 `HelperHost.Services`（新增只读属性）进程内解析，不经 HTTP。
- 未接入部分在界面上如实显示"本切片未接入"（生成文档核对、Apifox 检查/同步、报告、注释审阅回写）；无任何写入动作：扫描只读源码，配置/任务写入走既有原子落盘路径。
- 既有 WebView2 壳未改动行为，`--headless` 与页面层冻结不变；窗口位置记忆用独立文件 `desktop-window-native.json`。
- **验证缺口（如实记录）**：本轮运行环境的命令权限层拒绝了所有 dotnet 执行（`dotnet --list-sdks`、`dotnet build` 均返回 "No permission client configured for Bash"），因此：未编译、未运行任何 GUI；上述代码仅经静态逐行核对（对照仓内已验证的 MainForm 模式与 WinForms 标准 API）。修复后的第一件事是 Windows 上 `dotnet build` + 真机走一遍「新建配置 → 新建任务 → 扫描」。

## 原生切片编译验证与「查看注释缺口」（2026-11-07 第二轮）

- 本轮环境 dotnet 可用（Linux 容器，SDK 8.0.426 / 10.0.402，NuGet 还原正常）。上一轮"权限层拒绝所有 dotnet 执行"的阻塞不复存在。
- **Linux 编译验证通过**：net8.0-windows + WinForms 在 Linux `dotnet build` 成功（0 警告 0 错误）。csproj 增加 `EnableWindowsTargeting`（仅 Linux 生效的条件属性），Windows 构建不受影响。D1 决策的第一前置验证（CI 可编译）达成，决策记录 §6 已回写。
- **上一轮静态核对的切片实修 4 个编译错误**：`Ui.Hairline` 对 `Control` 设 `BorderStyle`（该属性在 `ListView` 上，参数改为 `ListView`）；`NativeShellForm` 缺 `Microsoft.Extensions.Logging` using（`LogError` 不可见）；`TasksPage` 构造漏传 `ProfileStore` 参数（两处调用只传了三个）。「未经编译验证」的挂账是真实的——静态逐行核对没有替代编译。
- **原生交互推进一小步**：任务详情源码注释区新增「查看注释缺口」（只读对话框 `ScanResultsDialog`）：列出扫描得到的全部注释位置（DocSlot），按状态筛选（缺失/空/失效/有效/其他问题），计数行等宽数字，逐条显示现状说明、覆盖接口、证据条数、不可回写原因；数据只读自任务目录 `symbols.json`，无任何写入动作。未扫描→引导先扫描；读取失败→内联错误与下一步。
- 状态文案与语义色调映射收口为 `Desktop/Native/TaskDisplay.cs`（纯托管，不依赖 WinForms/UiTokens），TasksPage 改为经它取文案，界面层只留"色调→令牌颜色"的开关。
- 新增测试工程 `ApifoxHelper.Tests`（net8.0 + xunit，与主工程共享 `TaskDisplay.cs` 源码，不引用主工程——其测试宿主无需 WindowsDesktop 运行时，Linux 可跑）。39 用例全部通过：
  - **令牌接线机械检查（G-D2 落地）**：`UiTokens.cs` 与 `docs/DESIGN.md` 的颜色/字号/间距逐值一致（两侧都改才算过）；`Desktop/Native` 其余文件禁 `0x` 颜色字面量、`Color.*`（`Color.White` 仅限 Ui.cs 主按钮白字）、`new Font(`；Padding/Margin 整数只能取文档档位（另允许 0=无间距、1=发丝边）。
  - **状态文案完备断言**：数据模型注释声明的每个状态值（源码/生成/远端三阶段 + DocSlot 的状态/标签/角色/符号种类）都有中文映射与语义色调；未知值原样回退；文案禁词检查（D11）。
  - **变异验证**：注入 `Color.HotPink`、`new Padding(7)`、改 `Accent` 值不更新文档，对应断言全部变红，恢复后全绿——检查"能红"经过实证，不是恒真断言。
- 测试目录在主工程目录内，主 csproj 已把 `ApifoxHelper.Tests\**` 从编译通配排除。
- **仍未验证（如实挂账）**：Windows 真机 GUI 运行（`--native` 全流程：新建配置 → 新建任务 → 扫描 → 查看缺口）、100%/200% DPI 截图（G-D5）、真机观感复核。Linux 无法运行 WinForms GUI，本轮未验证这些；「查看注释缺口」的控件交互只在代码与编译层面成立。

## 原生切片「代码证据」视图与测试解析器修复（2026-11-07 第三轮）

- **接续前确认**：上一轮工作树干净，HEAD（67f50ae）的 Linux `dotnet build` 0 警告 0 错误、39 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。
- **原生交互推进一小步（注释缺口 → 代码证据）**：`ScanResultsDialog` 新增「查看代码证据」按钮（选中条目有关联证据时可用；列表双击同效），打开只读对话框 `Desktop/Native/EvidenceDialog.cs`：按缺口条目的证据编号过滤任务目录 `evidence.json`（EvidenceStore），逐条显示证据分类、摘要、文件与行区间，选中后在下方等宽片段框查看代码原文。全程只读，无写入动作——审阅候选（F04）的前置是逐条对照代码证据，本切片把证据本身摆进原生界面。
  - 状态表：读取失败=内联错误与下一步（重新扫描）；该位置无关联证据=如实说明「没有证据的业务含义（单位、权限、默认值）不写进文档，留待人工确认」（与冻结页面同一口径）；正常=计数行 + 列表 + 片段详情。
  - 证据分类中文映射收口在 `TaskDisplay.EvidenceKindText`（route/binding/validation/guard/return/call/queryFilter/mapping/default/enumMember/existingDoc/docDiagnostic/responseData，未知值原样回退）；existingDoc/docDiagnostic 当前扫描代码不产生，映射按模型声明全集保留。
- **测试补强（39 → 66 用例）**：
  - 修复既有解析器的真实缺口：`Statuses()` 此前把紧贴 `<summary>` 的首值与紧贴 `</summary>` 的末值一起丢掉——`valid`、`summary`、`returns`、`notStarted`、`unsupported`、`interrupted`、`unavailable`、`method` 等 15 个枚举值从未被断言过（上一轮报告"39 用例"时未察觉）。两侧标记剥除后这些值全部进入断言，全部有映射（含新增的证据类型 13 个用例）。
  - 变异验证（本轮实测可红）：删除 `responseData` 映射臂后 `EvidenceKind_HasChineseText` 变红（`Assert.NotEqual() Failure: Strings are equal`），还原后全绿。注：把映射改成另一个中文词不会红——该断言只保证"有中文映射、不回退原值"，不校验具体措辞，措辞由文案口径审查（D11）人工覆盖。
- **编译警告修复**：`ScanResultsDialog` 的 `DoubleClick` lambda 在 `_evidenceButton` 赋值前引用它（CS8602 可空告警，增量构建时偶现）；按钮创建移到事件接线之前，构建恢复 0 警告。
- **验证状态**：Linux `dotnet build` 0 警告 0 错误；`ApifoxHelper.Tests` 66 用例全部通过（含令牌接线机械检查对新文件 EvidenceDialog.cs 的扫描）。**GUI 真机运行与 100%/200% DPI 截图仍未验证**（Linux 无法运行 WinForms GUI），「查看代码证据」的控件交互只在代码与编译层面成立，如实挂账。

## 原生切片「注释候选审阅」（2026-11-07 第四轮）

- **接续前确认**：上一轮工作树干净，HEAD（45c5f47）的 Linux `dotnet build` 0 警告 0 错误、66 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。
- **原生交互推进一小步（代码证据 → 候选审阅）**：这是本切片第一个有写入动作的原生交互——写入任务数据（comment-drafts.json 与任务阶段计数），仍不写源码、不访问 Apifox。
  - 任务详情源码注释区新增「规则提取候选」（后台作业，复用 `DraftService.GenerateRuleAsync`）：只生成代码可证实的约束说明（校验、默认值、守卫、枚举显示名、响应 data 类型），作业期间进度/取消与扫描共用同一套固定占位控件；`JobRunner` 单任务单作业防交叉。
  - 新增「查看注释候选」审阅对话框（`Desktop/Native/CandidatesDialog.cs`）：候选清单（位置/标注/依据/审阅状态/来源，按审阅状态筛选，等宽计数行），逐条显示候选文字、警告、待确认问题、依据与来源、引用证据；「查看代码证据」入口复用 `EvidenceDialog`，先对照证据再决定。接受/搁置/驳回经 `DraftService.UpdateAsync` 走既有校验：占位文字、待确认问题未回答不得接受、有效注释需显式勾选「覆盖」（勾选框只在目标位置为有效注释时可用）、位置已不在扫描结果时动作禁用并如实说明。审阅动作的行内状态行给结果与剩余待审阅数（OkText），服务拒绝原因原样内联展示（WarnText），不弹窗。
  - 关闭对话框后任务列表刷新候选/已接受计数；模型生成、材料导入、手工填写与源码回写仍在固定说明行如实标注"后续切片接入"。
- **TaskDisplay 增补映射**：候选审阅状态（pending/accepted/deferred/rejected/stale，色调+文案）、候选依据（code/needsConfirmation/notCovered/confirmed）、候选来源（rule/model/file/manual）、草稿生成运行结果（succeeded/partial/failed/noResult）；未知值原样回退不变。
- **DraftModels 注释可解析化**：`CommentCandidate` 类摘要改写为中文含义说明，机器可读清单（`code / needsConfirmation / …`、`pending / accepted / …`）移到对应属性的 `<summary>`——否则测试解析器会匹配到两行（`.Single()` 抛错），或再次踩"首末值漏断言"的坑。
- **测试 66 → 83 用例**：新增审阅状态（5，含色调）、依据（4）、来源（4）、运行结果（4）四组完备断言；未知值回退与文案禁词检查覆盖新映射。变异验证实测可红：删除 `ReviewStateText` 的 `stale` 映射臂 → `ReviewState_HasChineseTextAndTone(state: "stale")` 变红（`Assert.NotEqual() Failure: Strings are equal`），还原后全绿。
- **再次踩到并修复 CS8602**：`CandidatesDialog` 的 `DoubleClick` lambda 在 `_evidenceButton` 赋值前引用它（与第三轮 `ScanResultsDialog` 同型），按钮创建移到事件接线之前，构建恢复 0 警告。
- **验证状态**：Linux `dotnet build` 0 警告 0 错误；`ApifoxHelper.Tests` 83 用例全部通过（含令牌接线机械检查对新文件 CandidatesDialog.cs 的扫描）。**GUI 真机运行与 100%/200% DPI 截图仍未验证**（Linux 无法运行 WinForms GUI）；「规则提取候选 → 逐条审阅 → 接受」的全流程只在代码与编译层面成立，Windows 真机上需按「新建配置 → 新建任务 → 扫描 → 规则提取候选 → 查看注释候选 → 接受/驳回」走一遍。如实挂账。

## 原生切片「注释回写」（2026-11-07 第五轮）

- **接续前确认**：上一轮工作树干净，HEAD（2d2c2af）的 Linux `dotnet build` 0 警告 0 错误、83 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。
- **原生交互推进一小步（候选审阅 → 注释回写，F05 接入原生）**：本切片第一个写源码的原生交互——写入动作完全经既有 `CommentPlanService`（本机已验证：只改 `///` 注释行、BOM/换行保留、计划哈希绑定、写入前核对文件与证据指纹、409 阻断、失败即停、部分完成如实记录、逐文件原子替换），原生层只做预览与确认，不新增写入逻辑。
  - 任务详情源码注释区新增「注释回写」入口（有已接受候选且无作业运行时可用），打开 `Desktop/Native/ApplyPlanDialog.cs`：打开时生成注释计划（读工作区、核对指纹，加载态=状态行 + 主行动禁用），就绪后显示逐文件清单（相对路径、允许回写/只导出补丁及原因、注释处数）、跳过位置及原因、选中文件的统一 diff（等宽、只读）。
  - 「确认回写」为实心主按钮，点击弹**原生确认对话框**（覆盖式写入按设计基线 §1.4 保留原生确认；默认按钮在「取消」防回车误触）；回写后逐文件补「回写结果」列（已写入/已跳过/文件已变化/失败，语义色 + 文字双通道），状态行如实报告全部/部分/失败，部分完成不显示成全部成功。
  - 「导出补丁」走原生「另存为」（SaveFileDialog），内容与回写逐字节一致（UTF-8 无 BOM，与 Web 下载路径同一编码口径），并同样登记 `PatchExported`；「重新生成计划」在过期/失败/部分完成后可用（服务拒绝原因原样内联展示）。
  - 状态表：无已接受候选/扫描缺失=空态指引（服务文案含下一步）；计划无可写文件=列表区说明 + 跳过原因保持可见；回写被拒（计划过期）=按磁盘计划刷新界面 + 状态行保留原因。
- **TaskDisplay 增补映射**：注释计划状态（ready/applying/applied/partial/failed/expired，色调+文案）与逐文件回写结果（applied/skipped/conflict/failed，色调+文案）；未知值原样回退不变。机器可读清单复用 `CommentPlanService.cs` 中既有的 `<summary>` 枚举注释（本轮未改数据模型）。
- **测试 83 → 93 用例**：新增计划状态（6，含色调）与回写结果（4，含色调）两组完备断言；未知值回退与文案禁词检查覆盖新映射。变异验证实测可红：删除 `PlanStatusText` 的 `expired` 映射臂 → `PlanStatus_HasChineseTextAndTone("expired")` 变红，还原后全绿。
- **验证状态**：Linux `dotnet build` 0 警告 0 错误；`ApifoxHelper.Tests` 93 用例全部通过（含令牌接线机械检查对新文件 ApplyPlanDialog.cs 的扫描）。**GUI 真机运行与 100%/200% DPI 截图仍未验证**（Linux 无法运行 WinForms GUI）——「生成计划 → 预览差异 → 原生确认 → 回写源码 / 导出补丁」全流程只在代码与编译层面成立；Windows 真机需按「扫描 → 规则提取候选 → 审阅接受 → 注释回写 → 确认 → 核对源码 diff」走一遍，并核对回写后 `git diff` 只含注释行。如实挂账。
- 未改写入安全语义；未连接或写入 Apifox；`ExportPatchAsync`/`ApplyAsync` 只在用户显式操作时调用。

## 原生切片「生成文档核对」（2026-11-07 第六轮）

- **接续前确认**：上一轮工作树干净，HEAD（9767b36）的 Linux `dotnet build` 0 警告 0 错误、93 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。
- **原生交互推进一小步（注释回写 → 生成文档核对，F06 接入原生）**：旅程进入「生成文档」阶段——核对本身只读源码与生成物，写入范围限于任务数据（propagation.json 与生成阶段计数），不写源码、不访问 Apifox。
  - 任务详情「生成文档」区从「本切片未接入」换成两个动作：「核对传播」（后台作业，复用既有 `PropagationService.CheckAsync`：重新加载源码、读取各项目 XML 文档与当前 Swagger、逐条核对已确认注释的源码/XML/Swagger 三环状态；结果落盘 propagation.json 并更新生成阶段计数与状态消息；进度与取消和扫描/规则提取共用同一套固定占位控件，作业期间其余动作禁用）与「查看核对结果」（只读对话框 `Desktop/Native/PropagationDialog.cs`）。
  - 结果对话框：概要行显示核对时间与逐环节数量（等宽数字）；条目列表逐条显示位置、标注与三环状态（颜色 + 文字双通道，色调与文案对齐冻结页面口径）；选中条目显示说明文字、Swagger 对应位置与备注；下方 XML 文件清单逐项目显示 XML 路径、存在/不存在与更新时间，早于源码修改的旧生成物如实标注（旧生成物不算注释已生效）。未核对=空态指引；读取失败=内联错误与下一步；配置无 Swagger 时核对仍核对源码与 XML，Swagger 环节如实显示「不可用」。
  - 按钮门控：核对需扫描完成且有已接受候选、无作业运行；查看结果需已有核对落盘产物；定向构建不在本切片，固定说明行如实挂账「核对不构建项目，XML 与 Swagger 重新生成后才会更新。定向构建将在后续切片接入」。
- **TaskDisplay 增补映射**：传播核对源码环节（written/notWritten/missingSymbol）、XML 环节（present/different/missing/noXml/disabled）、Swagger 环节（present/different/missing/notRendered/unavailable）——色调与中文文案；未知值原样回退不变。枚举清单复用 `PropagationService.cs` 数据模型既有 `<summary>` 注释，本轮未改数据模型。
- **测试 93 → 106 用例**：新增源码（3）、XML（5）、Swagger（5）三组完备断言（含色调）；未知值回退与文案禁词检查覆盖新映射。变异验证实测可红：删除 `PropagationSwaggerText` 的 `notRendered` 映射臂 → `PropagationSwagger_HasChineseTextAndTone(state: "notRendered")` 变红（`Assert.NotEqual() Failure`），还原后全绿。
- **两处编译错误即改即验**：`XmlFileInfo.LastWrite` 是 `DateTime?`（已是本地时间，误用 `.LocalDateTime`）、`GeneratedStage.CheckedAt` 可空需先解构再格式化——本轮修复后 0 警告 0 错误。
- **验证状态**：Linux `dotnet build` 0 警告 0 错误；`ApifoxHelper.Tests` 106 用例全部通过（含令牌接线机械检查对新文件 PropagationDialog.cs 的扫描）。**GUI 真机运行与 100%/200% DPI 截图仍未验证**（Linux 无法运行 WinForms GUI）——「核对传播 → 查看核对结果」只在代码与编译层面成立；Windows 真机需按「扫描 → 规则提取候选 → 审阅接受 → 注释回写 → 重新生成 XML/Swagger → 核对传播 → 查看结果」走一遍，核对部分完成/不可用/旧生成物各态文案。如实挂账。
- 未改写入安全语义；未连接或写入 Apifox；核对作业只在用户显式点击时开始。

## 原生切片「响应契约审阅」（2026-11-07 第七轮）

- **接续前确认**：上一轮工作树干净，HEAD（2740e3e）的 Linux `dotnet build` 0 警告 0 错误、106 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。
- **原生交互推进一小步（生成文档核对 → 响应契约，F07 接入原生）**：旅程第 5 步——契约清单在扫描时由 `SourceScanService` 静态追踪落盘（responseContract.json），本轮接入其审阅交互：确认 / 拒绝 / 重置只写任务数据并递增任务版本（使旧检查与旧计划过期），不写源码、不访问 Apifox。
  - 任务详情在「生成文档」与「Apifox 差异与同步」之间新增「响应契约」区：状态行只读统计待确认/已确认/已拒绝/已阻断/已失效（有阻断=红、有待确认=橙、全部已处理=绿、没有契约=灰并说明原因），固定说明行交代数据来源与「确认后需重新检查 Apifox 差异才会生效」；「查看响应契约」在扫描完成且无作业运行时可用。
  - 审阅对话框 `Desktop/Native/ContractsDialog.cs`：契约按审阅状态筛选，列表显示接口/封装/data 类型/分支数/审阅状态（颜色 + 文字双通道）；选中条目显示封装与 data 类型（可空标注）、分支清单（HTTP 状态、条件、业务状态、data 形状与代码位置）、未解析点（阻断原因如实列出）、审阅状态与时间、引用证据编号；「查看代码证据」复用 `EvidenceDialog`（本轮为其新增按证据编号打开的构造重载，缺口视图原路径不变）。空态与冻结页面同一口径：没有需要补充的契约时说明「未配置通用响应封装，或接口返回具体类型」并指向项目设置。
  - 审阅动作可用性与冻结页面一致：阻断不能确认、已确认不能再确认、已拒绝不能再拒绝、非待确认/阻断可重置；动作后重读契约清单、保持选中并报告剩余待确认数。
- **审阅校验收敛为单一口径**：`PUT /api/tasks/{id}/response-contract` 路由原先内联校验（契约存在、阻断不能确认、状态白名单、写文件、递增版本），本轮原样提取为 `Source/ContractReviewService.ReviewAsync`（注册为单例），路由改为调用同一服务——校验与错误文案逐字保留，路由响应不变；原生对话框调用同一服务，避免两处口径漂移。这是本轮唯一触碰既有链路的改动，纯行为保持重构。
- **TaskDisplay 增补映射**：契约审阅状态（pending/confirmed/rejected/blocked/stale，色调：已确认=ok、待确认/已失效=warn、已拒绝=muted、已阻断=bad）与分支 data 形状（type/null/none/anonymous/unknown，无法确定=warn）——文案与色调齐备，未知值原样回退不变。枚举清单复用 `Source/SourceModels.cs` 数据模型既有 `<summary>` 注释，本轮未改数据模型。
- **测试 106 → 116 用例**：新增契约审阅状态（5）与 data 形状（5）两组完备断言（含色调）；未知值回退与文案禁词检查覆盖新映射。变异验证实测可红：删除 `ContractStateText` 的 `stale` 映射臂 → `ContractReviewState_HasChineseTextAndTone(state: "stale")` 变红，还原后全绿。
- **验证状态**：Linux `dotnet build` 0 警告 0 错误；`ApifoxHelper.Tests` 116 用例全部通过（含令牌接线机械检查对新文件 ContractsDialog.cs 的扫描）。**GUI 真机运行与 100%/200% DPI 截图仍未验证**（Linux 无法运行 WinForms GUI）——「查看响应契约 → 确认/拒绝/重置」只在代码与编译层面成立；Windows 真机需按「扫描（配置了通用响应封装的项目） → 查看响应契约 → 逐条对照分支与证据 → 确认/拒绝/重置 → 任务页状态行刷新」走一遍，并核对阻断契约的确认被拒绝。如实挂账。
- 未改写入安全语义（审阅校验为行为保持提取）；未连接或写入 Apifox；审阅动作只在用户显式点击时调用服务。

## 原生切片「Apifox 差异检查（只读）」（2026-11-07 第八轮）

- **接续前确认**：上一轮工作树干净，HEAD（e6f862e）的 Linux `dotnet build` 0 警告 0 错误、116 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。
- **原生交互推进一小步（响应契约 → Apifox 差异检查，F08/F09 的只读部分接入原生）**：旅程第 6 步——「Apifox 差异与同步」区从「本切片未接入」换成只读检查与结果查看；检查、离线远端导入复用既有 `CheckService`（`LoadSwaggerAsync`/`ImportOfflineRemoteAsync`/`RunAsync`），不写入远端、不生成同步计划，差异确认/计划/同步/报告仍如实挂账为后续切片。
  - 任务详情「Apifox 差异与同步」区新增三个动作（都在扫描完成且无作业运行时可用）：「开始检查 / 重新检查 / 加载 Swagger 并检查」（同一按钮按状态换文案，与冻结页面同一规则：配置了 Swagger 且未冻结时先加载冻结再检查，两个调用放进同一个后台作业「契约检查」，进度与取消共用既有固定占位控件）；「导入离线远端 JSON」（仅离线任务显示，原生 `OpenFileDialog` 选文件后经 `ImportOfflineRemoteAsync` 后台作业导入）；「查看检查结果」（任务目录已有 diff.json 时可用）。
  - 作业失败改为显示原因：进度占位行在作业失败时显示「{作业}失败：{原因}」（`JobRunner` 已对非 `UserException` 脱敏），不再只显示"结束（失败）"——离线任务未导入远端、未绑定 Apifox 项目 ID 等情况错误直接可见，失败不装干净（D9）。此改动对扫描/规则提取/传播核对等所有作业类型同样生效。
  - 结果对话框 `Desktop/Native/CheckResultsDialog.cs`（只读）：概要行显示检查时间、结构来源（Swagger/远端现状/离线导入）、快照时间与完整性（不完整如实标注）、逐状态计数（仅源码 >0 时才追加，等宽数字）；检查之后任务内容有变化时显示过期提示（结果仍如实展示）；检查问题与「远端有、源中没有（范围内）」清单（无问题时整区隐藏）；接口列表按状态筛选（全部/新增/结构变化/说明变化/无变化/阻断/仅源码），状态列颜色 + 文字双通道，接口与计数列等宽字体；选中接口显示远端 ID/名称/目录、重复身份、逐条结构差异（位置 · 变化 — 原因（风险））、保留项、逐条说明合并（位置［来源］远端值目标值 → 结果）、备注、质量提示、阻断；下方模型清单显示映射状态/模型状态/用于接口数/范围外影响数。差异确认、说明冲突选择、模型绑定与同步预览不在本切片，说明行如实交代。
- **TaskDisplay 增补映射**：接口检查状态（new/structure/docs/unchanged/blocked/sourceOnly，色调对齐冻结页面：新增/说明变化=accent、结构变化=warn、阻断/仅源码=bad）、模型状态（unchanged/changed/new/blocked/unknown）、模型映射状态（mapped/candidate/new/ambiguous）、结构来源（swagger/remote/offline）、差异变化类型（added/removed/changed/info）、差异风险（normal/incompatible/manual/blocking）、说明合并结果（fill/keepRemote/useTarget/conflict/supplement/suggestion，冲突待选=bad）、说明来源（swagger/comment/sourceDoc/supplement）；`RemoteStatusText` 补 `offline`（离线检查）映射臂——离线检查完成后任务列表列此前显示原始值 `offline`，属既有缺口本轮修复；`TaskModels.RemoteStage.Status` 注释补 `offline` 取值、`CheckModels.CheckResult.StructureSource` 注释补机器可解析取值行。未知值原样回退不变。
- **测试 116 → 153 用例**：新增接口检查状态（6）、模型状态（5）、映射状态（4）、结构来源（3）、说明合并结果（6）、说明来源（4）、差异变化（4）、差异风险（4）八组完备断言（含色调），offline 自动并入既有远端状态断言；未知值回退与文案禁词检查覆盖全部新映射。变异验证实测可红：删除 `CheckStatusText` 的 `sourceOnly` 映射臂 → `CheckEndpointStatus_HasChineseTextAndTone(status: "sourceOnly")` 变红，还原后全绿。另修正一处流程认知：仓库无 .sln，根目录 `dotnet build` 只构建主工程，测试工程需显式 `dotnet build ApifoxHelper.Tests/...` 后再 `dotnet test --no-build`。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 153 用例全部通过（含令牌接线机械检查对新文件 CheckResultsDialog.cs 的扫描）。**GUI 真机运行与 100%/200% DPI 截图仍未验证**（Linux 无法运行 WinForms GUI）——「导入离线远端 / 检查 / 查看结果」只在代码与编译层面成立，且在线检查依赖本机 Apifox CLI 登录态，本环境不可能端到端执行；Windows 真机需按「扫描 →（配置 Swagger 时）加载 Swagger 并检查 → 查看检查结果 → 逐接口对照结构差异与说明合并 →（离线任务）导入 JSON 后检查」走一遍，并核对失败路径（未绑定项目 ID、未导入离线远端）的错误文案。如实挂账。
- 未改写入安全语义（检查与导入均为既有服务的只读复用）；本轮未连接或写入 Apifox（`RunAsync` 只在用户显式点击时开始，且需真实 CLI 登录态）。

## 原生切片「报告与交接」（2026-11-07 第九轮）

- **接续前确认**：上一轮工作树干净，HEAD（3bb0e5b）的 Linux `dotnet build` 0 警告 0 错误、153 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。
- **原生交互推进一小步（Apifox 差异检查 → 报告与交接，F14 接入原生）**：旅程第 9 步——报告是唯一不依赖写入探测、离线也可用的收尾步骤（对应 3.3「只检查不同步」变体），服务层早已验证（F14 已实现（已验证）），本轮只接入交互；预览与导出只读任务数据并写出报告文件，不写源码、不访问 Apifox。
  - 「Apifox 差异与同步」区的固定说明行从「差异确认、同步与报告将在后续切片接入」改为「差异确认与同步将在后续切片接入」，其后新增「报告与交接」区：状态行如实显示任务目录已有的导出副本（report.md / handoff.md 与时间；尚无则显示「尚未导出报告」），「查看报告与导出」按钮打开 `Desktop/Native/ReportsDialog.cs`；按钮不要求先扫描（与冻结页面一致，报告随时可看），作业执行中禁用（任务文件在被改写，避免读到半份结果）。
  - 导出口径先收敛为单一落点：`PUT`/`GET /api/tasks/{id}/reports/{format}` 路由原先内联四个格式的渲染与字节处理，本轮原样提取为 `Reports/ReportExportService`（`Preview`＝报告对象 + Markdown 不落盘；`Render`＝按格式产出文件名/内容类型/字节，json/md/handoff 照旧写任务目录副本），格式 → 文件名/内容类型表落在只依赖 BCL 的 `Reports/ReportFormats.cs`；路由改为调用同一服务（行为与字节不变）。这是本轮唯一触碰既有链路的改动，纯行为保持提取——桌面「另存为」与 Web 下载从此共用同一份渲染与字节口径。
  - 报告对话框（只读预览 + 导出）：正文区显示 Markdown 源文（等宽、只读，与「导出 Markdown」内容一致），动作行按冻结页面同一主次——「前端交接说明」为实心主按钮，「导出 Markdown / 导出 HTML / 导出 JSON」为 ghost，全部经原生 `SaveFileDialog`（默认文件名取自共享格式表）写出，UTF-8 无 BOM 与 Web 下载同字节；结果与失败原因在底部状态行内联显示（服务拒绝原样展示、其他异常脱敏），不弹窗；任务数据读取失败时正文区显示指引并禁用导出（空态不留白）。
  - 本轮无新增状态枚举，TaskDisplay 未变；报告内容全部经既有 `ReportService` 脱敏，共享报告不含凭据（D 系列文案与状态双通道条款沿用既有实现）。
- **测试 153 → 160 用例**：测试工程照 TaskDisplay.cs 同法共享编译 `Reports/ReportFormats.cs`（不引用主工程，Linux 可运行），新增格式口径断言：四格式逐一钉住文件名与内容类型（json=report.json/application/json、md=report.md/text/markdown、html=report.html/text/html、handoff=handoff.md/text/markdown）+ 未知格式（preview/pdf/空）返回 null（由服务包装成「报告格式无效。」拒绝）。变异验证实测可红：删除 `TryDescribe` 的 `handoff` 映射臂 → `TryDescribe_MatchesWebDownloadCaliber(format: "handoff")` 变红，还原后全绿。
- **Linux 运行时验证（本轮新增，超出此前仅编译验证的口径）**：主工程是 net8.0-windows（Linux 无法运行），但报告链路不触碰 WinForms——在 /tmp 搭一次性 net8.0 控制台（工程文件链接主仓 `Infrastructure/`、`Tasks/`、`Profiles/`、`Source/`、`Comments/`、`Contracts/`、`Sync/CheckModels+PlanModels`、`Reports/` 等纯逻辑源文件与镜像 GlobalUsings，不进产品仓库、已清理），在真实 `FileStore`/`TaskStore` 上创建任务并执行 `Preview` 与四格式 `Render`，24 项断言全部通过：预览 Markdown 含任务名且为 CRLF、四格式文件名/内容类型与冻结页面下载一致、字节均无 BOM、md/handoff 的任务目录副本与下载字节逐字节一致、report.json 副本可解析、`preview` 格式被拒绝且文案不变。报告内容的真实数据形态（分阶段表格、未完成事项）仍以 Web 时代已验证的 `ReportService` 为准，本轮未改其逻辑。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 160 用例全部通过（含令牌接线机械检查对新文件 ReportsDialog.cs 的扫描）。**GUI 真机运行与 100%/200% DPI 截图仍未验证**（Linux 无法运行 WinForms GUI）——「查看报告与导出 → 原生另存为」的对话框交互只在代码与编译层面成立（导出的服务链路已在 Linux 真实执行）；Windows 真机需按「任一任务 → 查看报告与导出 → 核对预览与 Web 报告页一致 → 四格式分别另存为 → 打开文件核对内容与编码 → 与 Web 下载同名文件比对字节」走一遍。如实挂账。
- 未改写入安全语义（导出为既有已验证报告链路的行为保持提取）；未连接或写入 Apifox；报告文件只在用户显式「另存为」或既有 Web 下载时写出。

## 原生切片「差异确认」（2026-11-07 第十轮）

- **接续前确认**：上一轮工作树干净，HEAD（b2c4b18）的 Linux `dotnet build` 0 警告 0 错误、160 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。
- **原生交互推进一小步（Apifox 差异检查 → 差异确认，F09 决策部分接入原生）**：旅程第 6 步的后半——检查结果里需要人处理的事项现在可以在原生界面里逐项决定并保存；写入范围限于任务数据（decisions.json 与任务版本递增），不写源码、不访问 Apifox。
  - 任务详情「Apifox 差异与同步」区新增「处理差异确认」按钮（与「查看检查结果」同门控：任务目录已有 diff.json 且无作业运行），打开 `Desktop/Native/DecisionsDialog.cs`。条目清单与冻结页面同一口径，四类：
    1. **高风险变化逐项确认**（diff.Items 的 NeedsDecision，只列接口侧条目——模型侧的结构差异与冻结页面一致经共享模型更新确认处理）；
    2. **说明冲突选择**（接口与模型的 DocChanges 中 Decision=conflict 的条目，选保留远端/使用目标）；
    3. **模型绑定**（MappingState=candidate/ambiguous 的模型，绑定远端 ID 或新建）；
    4. **共享模型更新确认**（Status=changed 的模型，范围外影响面在详情中如实列出）。
  - 待处理判断只看 decisions.json 与检查结果的差集：条目状态列实时反映已保存决策（已确认/已排除/使用目标/保留远端/绑定远端 N/新建模型/待处理），保存后条目不消失，重新检查后才按新决策重算（与冻结页面「保存 → 重新检查」两步一致）。筛选支持 全部/待处理/四类各自。
  - 保存经共享 `Sync/DecisionService` 的唯一校验口径：内容变化时递增任务版本并把 previewable/awaitingConfirm 的远端阶段置为 expired（旧检查与旧计划过期）；内容未变化时不写文件、不递增（保存可重复点击），对话框按版本是否变化如实区分「已保存…重新检查后生效」与「决策内容没有变化，未修改任务」。拒绝原因（校验文案）原样内联展示，不弹窗；已保存决策读取失败时停用全部保存动作，避免用空决策覆盖既有选择。
  - 「PUT /api/tasks/{id}/decisions」路由原先内联的校验与保存原样提取为 `DecisionService.SaveAsync`（校验文案逐字保留），路由改为调用同一服务（行为与响应不变）——冻结页面与原生对话框从此单一口径。这是本轮唯一触碰既有链路的改动，纯行为保持提取。
- **TaskDisplay 增补映射**：差异确认决策值（accepted=已确认/ok、excluded=已排除/muted）与模型绑定取值（数字=「绑定远端 N」、new=新建模型、空=未确认、不可解析原样回退）；`DecisionStore.Items` 与 `ModelBindings` 的 `<summary>` 增补机器可解析取值行（`accepted / excluded`、`ID 字符串 / new`）。**再次踩到第三轮修过的解析陷阱**：值清单与中文前缀同行时首值（accepted）被解析器丢弃、从未断言——本轮已在提交前发现并改为独立纯值行，accepted/excluded 均在断言内。说明冲突选择的 keepRemote/useTarget 文案复用既有 `DocDecisionText`，未新增映射。
- **测试 160 → 164 用例**：新增决策值（2，含色调）、模型绑定取值（1）与绑定文案组合（数字/null/空/未知回退）断言；未知值回退与文案禁词检查覆盖新映射。变异验证实测可红：删除 `DecisionStateText` 的 `excluded` 臂 → `DecisionState_HasChineseTextAndTone("excluded")` 变红；删除 `ModelBindingText` 的 `new` 臂 → `ModelBindingValue_HasChineseText("new")` 变红；还原后全绿。
- **Linux 运行时验证（本轮新增，超出仅编译验证的口径）**：决策链路不触碰 WinForms，在 /tmp 以一次性 net8.0 控制台链接纯逻辑源文件（Infrastructure 的 Core/FileStore/Redactor、Tasks/、Profiles 的 ProfileStore/ProjectProfile、Contracts/、Sync 的 CheckModels+DecisionService，镜像 GlobalUsings；不进仓库、已清理），在真实 `FileStore`/`TaskStore` 上创建任务并执行共享 `DecisionService.SaveAsync`，13 项断言全部通过：三类合法决策落盘回读一致、内容变化递增任务版本、previewable 远端阶段被置为 expired、相同内容重复保存不递增、移除决策键视为变化、三类非法值（maybe/overwrite/非数字绑定）逐一拒绝且文案与原路由逐字一致、被拒保存不改动任务、new 与数字绑定合法、不存在的任务拒绝。注入变异（删除校验行）未单独做——拒绝文案断言即钉住校验存在。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 164 用例全部通过（含令牌接线机械检查对新文件 DecisionsDialog.cs 的扫描）。**GUI 真机运行与 100%/200% DPI 截图仍未验证**（Linux 无法运行 WinForms GUI）——「查看检查结果 → 处理差异确认 → 逐项确认/选择/绑定 → 保存 → 重新检查」只在代码与编译层面成立（决策保存的服务链路已在 Linux 真实执行）；Windows 真机需按该路径走一遍，并核对：保存后任务列表 Apifox 列变「计划已过期」、重复保存提示内容未变化、检查结果对话框的过期提示出现。如实挂账。
- 未改写入安全语义（决策校验为行为保持提取）；本轮未连接或写入 Apifox；决策只写任务数据，同步预览/执行如实挂账为后续切片。

## 原生切片「同步预览」（2026-11-07 第十一轮）

- **接续前确认**：上一轮工作树干净，HEAD（f211046）的 Linux `dotnet build` 0 警告 0 错误、164 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。
- **原生交互推进一小步（F12 同步预览的只读部分接入原生）**：旅程第 7 步「同步预览：勾选接口 → 生成冻结计划 → 查看逐动作内容与排除清单」现在可以在原生界面完成；计划生成复用既有已验证的 `SyncPlanService`（写入门禁、检查版本一致、逐接口重读远端核对指纹、cli-schema 校验、模型依赖排序全部不变），本层只做勾选、进度与预览，无任何写入动作。
  - 任务详情「Apifox 差异与同步」区新增「同步预览」按钮（门控：扫描完成、无作业运行、非离线、任务目录已有 diff.json），打开 `Desktop/Native/SyncPreviewDialog.cs`：
    - **勾选页**：接口清单（方法+路径等宽、名称、状态颜色+文字、待确认计数）；不可同步的状态（无变化/阻断/仅源码/离线）勾选立即弹回并整行灰显；「全选可同步（N）」只勾选状态可同步且无未确认高风险项的接口；「查看已有计划」在任务目录已有 plan.json 时可用。
    - **生成**：走共享 `JobRunner`（同一任务同一时刻一个作业的既有约束生效），进度与「停止」在对话框状态行；`SyncPlanService` 逐接口响应取消。成功切换预览页；失败/取消在状态行给原因与下一步。
    - **预览页（只读）**：目标项目/分支、CLI 版本、计划指纹（前 12 位）与生成时间；接口/模型/排除计数；计划状态（任务版本已前进时按「已过期」显示并提示重新检查后重新生成，与冻结页面同一规则）；写入清单按执行顺序（# / 动作 / 资源 / 远端 ID / 改动 / CLI 校验 / 状态）；选中动作显示写入内容——`FileStore.ReadNode` → `Redactor.RedactNode` → `JsonDefaults.Options` 序列化，与 Web `/plan/payload` 同一口径；未纳入计划清单（资源+原因）；范围外影响、警告、保留设置与执行语义固定说明；固定说明行如实挂账「执行同步与回读核对将在后续切片接入」。
  - 勾选口径提取为 `Desktop/Native/SyncSelection.cs`（不依赖 WinForms，与测试工程共享编译）：可勾选 = new/structure/docs 且非离线；「全选可同步」再排除有未确认高风险项；待确认 = NeedsDecision 且决策不是 accepted。计划阶段的最终排除判断（模型依赖、远端指纹、门禁）仍在 `SyncPlanService` 单一口径，该文件不复制写入边界。
- **TaskDisplay 增补映射**：`SyncPlan.Status` 8 值（SyncPlanStatusText/SyncPlanTone）、`PlanAction.Kind` 4 值（PlanActionKindText）、`PlanAction.State` 8 值（PlanActionStateText/PlanActionStateTone）；`PlanModels.cs` 的三个取值行已是独立纯值行，机械检查解析器可直接断言。
- **测试 164 → 188 用例**：新增 SyncPlanStatuses（8）/PlanActionKinds（4）/PlanActionStates（8）理论用例，未知值回退与文案禁词检查覆盖新映射；新文件 `SyncSelectionTests`（4 用例，对真实 CheckResult/DecisionStore 类型断言）。测试工程共享编译扩展为 TaskDisplay/SyncSelection/ReportFormats/CheckModels/DiffEngine/DocModel/Core，csproj 以 `<Using>` 镜像主工程全局 using。**变异验证实测可红**：删除 `PlanActionStateText` 的 `conflict` 臂 → 对应理论用例变红；`SyncableStatuses` 删 `docs` → SyncSelectionTests 3 用例变红；还原后全绿。
- **csproj 教训（本轮踩坑，记录在案）**：编辑测试工程 csproj 时一度把共享编译 ItemGroup 留成两份，SDK 10 的 NETSDK1022「Duplicate Compile items」报得完全正确——重复项正是被求值两次的两个 `../` 共享文件；与「SDK 是否支持 `../` 共享编译」无关。修复后 0 警告 0 错误。
- **Linux 运行时验证（本轮新增，超出仅编译验证的口径）**：同步预览链路不触碰 WinForms，在 /tmp 以一次性 net8.0 控制台链接纯逻辑源文件（Infrastructure 的 Core/FileStore/Redactor、Profiles 的 ProfileStore/ProjectProfile、Tasks/、Contracts 的 DiffEngine/DocModel、Sync 的 CheckModels/PlanModels、Desktop/Native 的 SyncSelection/TaskDisplay，镜像 GlobalUsings；不进仓库、已清理），在真实 `FileStore`/`TaskStore` 上创建在线/离线任务并写入夹具 diff.json/decisions.json/plan.json 与写入内容文件，23 项断言全部通过。**并当场抓到一个真实缺陷**：写入内容预览最初用 `JsonNode.ToString()` 渲染，默认严格转义把「已脱敏」与中文全部变成 `\uXXXX`——已改为项目唯一序列化口径 `JsonDefaults.Options`（缩进、中文原样）后渲染，运行时断言钉住「中文原样 + 敏感值已脱敏 + 缩进 + 可再解析」四点。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 188 用例全部通过（含令牌接线机械检查对新文件 SyncPreviewDialog.cs 与 SyncSelection.cs 的扫描）。**GUI 真机运行与 100%/200% DPI 截图仍未验证**（Linux 无法运行 WinForms GUI）——「同步预览 → 勾选 → 生成 → 逐动作查看/写入内容/排除清单」只在代码与编译层面成立（勾选口径、计划数据形状与脱敏渲染链路已在 Linux 真实执行；计划生成的服务链路是 F12 已有真实项目验证记录的既有代码，本轮未改动）；Windows 真机需按「任一已检查的在线任务 → 同步预览 → 勾选 → 生成同步预览 → 核对写入清单与 Web 同步页一致 → 选中动作核对写入内容脱敏」走一遍。如实挂账。
- 未改写入安全语义（`SyncPlanService` 未动一行）；本轮未连接或写入 Apifox；同步执行与回读核对（F13）如实挂账为下一片。

## 原生切片「同步执行与回读核对」（2026-11-07 第十二轮）

- **接续前确认**：上一轮工作树干净，HEAD（998e29a）的 Linux `dotnet build` 0 警告 0 错误、188 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。
- **原生交互推进一小步（同步预览 → 同步执行与回读核对，F13 接入原生）**：旅程第 8 步「同步执行：确认后逐项写入并立即回读核对，失败即停，可随时重新核对（只读）」现在可以在原生界面发起；写入完全经既有 `SyncExecutor`（计划哈希绑定、任务锁与项目/分支锁、写入前重读核对指纹、cli-schema validate、写入后立即回读核对、超时不重发先回读判断、首个失败即停止、取消只在动作之间生效、部分完成如实落盘），本层只做确认、进度与结果呈现，不新增任何写入逻辑。
  - 同步预览对话框预览页新增「确认同步 N 个接口、M 个模型」实心主按钮与「重新核对（只读）」ghost，主次与门控和冻结页面同一规则：确认同步只在计划待确认且未过期时可用（过期规则 = 计划生成后任务版本已前进，与冻结页面同一计算）；重新核对在待确认（无可核对动作）与执行中（不能同时核对）之外可用。门禁与结果口径提取为 `Desktop/Native/SyncExecution`（不依赖 WinForms，与测试工程共享编译单一口径；写入边界仍在 SyncExecutor 单一口径）。
  - 确认同步：按设计基线弹**原生确认对话框**（不可逆动作保留原生确认；默认按钮在「取消」防回车误触；文案与冻结页面同一口径：目标项目/分支、接口与模型计数、范围外影响提示、「同步期间请避免在 Apifox 中修改相同资源」）→ 共享 `JobRunner` 后台作业执行；执行中状态行显示进度（正在执行同步… N% 执行 i/N：资源）与「停止」（ghost + 危险字色、位置不变），「重新选择接口」执行期间禁用防误离。
  - 结束呈现：执行、核对或取消结束都按磁盘计划刷新界面（逐动作状态列、失败原因提示可见），状态行按计划状态如实报告——verified=已写入并回读核对通过、partial=部分完成（已核对 N 个、未完成 M 个）、failed=同步失败、canceled=已停止、interrupted=指向重新核对（只读）；部分完成不显示成全部成功。执行被门禁拒绝（计划过期、CLI 未探测、项目/分支锁占用）时先按磁盘计划刷新（可能已被置为过期并在提示行显示原因），状态行保留拒绝原因。生成同步计划的取消/失败文案与行为逐字保持上一轮口径（按作业类型分支，生成路径不读盘刷新）。
  - 「重新核对（只读）」复用 `SyncExecutor.VerifyAsync`（与 Web `/api/tasks/{id}/verify` 同一链路，按 ID/身份回读远端核对已写入动作，不写入），结束后同一套结果呈现。
  - 预览页固定说明行从「执行同步与回读核对将在后续切片接入」换成执行语义说明；每屏一个主按钮：预览页显示时隐藏选择页的「生成同步预览」，返回后恢复。
- **TaskDisplay 无新增映射**：SyncPlan.Status 8 值与 PlanAction.State 8 值的文案与色调映射上一轮已建立，本轮直接复用（执行中/部分完成/结果不确定/目标已变化等齐备）；`SyncExecution.ResultText` 对未预期状态经 `SyncPlanStatusText` 中文回退，未知值仍原样出现。
- **测试 188 → 213 用例**：新增 `SyncExecutionTests`（25）：CanExecute 理论用例（8 状态 × 是否过期，只有待确认且未过期可用）、CanVerify 理论用例（待确认/执行中不可用，其余可用）、ResultText 五组（verified=ok 且含核对通过、partial=warn 且含已核对/未完成计数且不含「全部核对」、failed=bad、canceled=warn、interrupted 指向重新核对、未预期状态中文回退且未知值原样出现）与禁词检查。**变异验证实测可红**：删除 CanExecute 的过期判断 → 过期计划用例变红；partial 文案注入「全部核对」→ DoesNotContain 断言变红；还原后全绿。
- **两处编译问题即改即验**：switch 语句的 `default` 不允许 `when` 守卫（生成失败分支改 `case "failed" when`）；`JobRunner.Start` 委托返回 `Task<object?>` 与服务层 `Task<object>` 的可空性差（CS8619，改 async/await 形式，与生成计划同型）。修复后构建恢复 0 警告 0 错误。
- **Linux 运行时验证（本轮新增，超出仅编译验证的口径）**：执行对话框的读取与判定链不触碰 WinForms，在 /tmp 以一次性 net8.0 控制台链接纯逻辑源文件（Infrastructure 的 Core/FileStore/Redactor、Profiles 的 ProfileStore/ProjectProfile、Tasks/、Contracts 的 DiffEngine/DocModel、Sync 的 CheckModels/PlanModels、Desktop/Native 的 SyncSelection/SyncExecution/TaskDisplay，镜像 GlobalUsings；不进仓库、已清理），在真实 `FileStore`/`TaskStore` 上创建任务并写入夹具 plan.json，24 项断言全部通过：计划哈希/状态/任务版本/动作字段（写入字段、模型引用、排除原因、范围外影响、保留设置）读回一致；计划新鲜可执行且待确认时重新核对禁用；任务版本前进后按过期规则禁用执行并显示「已过期/计划已过期」；部分完成结果行含已核对 2、未完成 2（失败 1 + 未执行 1）且不含「全部核对」；全部核对通过 ok、无动作通过 bad、中断指向重新核对；停止在写入前计划仍待确认、可重新确认同步。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 213 用例全部通过。**GUI 真机运行与 100%/200% DPI 截图仍未验证**（Linux 无法运行 WinForms GUI）——「确认同步（原生确认）→ 逐动作进度与停止 → 结果呈现」「重新核对（只读）」的对话框交互只在代码与编译层面成立（执行门禁与结果口径已在 Linux 真实执行）；且 SyncExecutor 的写入链路需要本机 Apifox CLI 登录态与已通过写入兼容探测的 CLI 版本，本环境不可能端到端执行，F13 仍为「已实现（待验证）」如实挂账。Windows 真机需按「已检查任务 → 同步预览 → 勾选 → 生成同步预览 → 确认同步 → 观察逐动作进度与停止 → 核对结果行与写入清单逐动作状态 → 重新核对（只读）」走一遍，并核对过期计划与未探测 CLI 版本的拒绝文案。未改写入安全语义（`SyncExecutor`/`SyncPlanService` 未动一行）；本轮未连接或写入 Apifox。

## 原生切片「定向构建」（2026-11-07 第十三轮）

- **接续前确认**：上一轮工作树干净，HEAD（b284cc1）的 Linux `dotnet build` 0 警告 0 错误、213 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。
- **原生交互推进一小步（F06 的定向构建部分接入原生）**：旅程第 4 步「可选执行定向构建（需确认），然后核对注释是否进入 XML 文档和 Swagger」的前半——「生成文档」区的固定说明行从「定向构建将在后续切片接入」换成真实入口；构建是用户确认后才执行的本机动作，工具默认不构建、不 restore、不启动服务。
  - 任务详情生成文档区新增「执行定向构建」ghost（不需要先扫描：命令只来自配置快照的 API 项目路径，与冻结页面一致）与「查看构建输出」ghost（任务目录已有 build.json 时可用）。点「执行定向构建」先弹**原生确认对话框**（标题与正文和冻结页面同一口径：`BuildStatus.ConfirmText` 给出将执行的命令并如实说明「会生成 bin/obj 输出，不会还原依赖或启动服务」；默认按钮在「取消」防回车误触），确认后经共享 `JobRunner` 调既有 `BuildService.RunAsync`（`dotnet build <API项目> --no-restore -nologo -v:minimal`，工作区根目录执行、15 分钟超时、不重发，结果脱敏后落盘 build.json——服务层一行未改）；进度与取消沿用与扫描共用的固定占位控件（作业名「定向构建」），作业结束后任务页刷新，构建区状态行显示上次构建结果。
  - 「查看构建输出」只读对话框（`Desktop/Native/BuildLogDialog.cs`）：概要行与任务页构建状态行同一口径（`BuildStatus.LastText`）、命令（等宽）、开始时间在标题栏、最近 60 行脱敏输出尾部（等宽、可横向滚动）；状态表齐备——尚未构建=空态指引、记录读取失败=内联错误与下一步、正常=概要+输出尾部。
  - 构建区状态行口径：退出码 0=完成（ok）；超时或非零退出码=未完成（bad，如实说明并指向「查看构建输出」，不显示成完成）；记录缺退出码不猜测结果（bad）；没有 build.json=muted「尚未构建」。确认文案、门禁与状态行口径提取为 `Desktop/Native/BuildStatus.cs`（不依赖 WinForms，与测试工程共享编译单一口径；构建边界仍在 `BuildService` 单一口径，本文件不复制构建逻辑）。
- **顺带修正两处陈旧挂账文案（接续检查发现）**：①「Apifox 差异与同步」区说明行仍写「执行同步与回读核对将在后续切片接入」——第十二轮已接入同步执行但该行漏更新，已换成「确认同步」门控与回读核对的现状说明；②「生成文档」区说明行按新能力改写。界面文案与实际能力的一致性纳入本轮断言（见下）。
- **测试 213 → 224 用例**：新增 `BuildStatusTests`（11）：CanStart 理论用例、确认文案断言（含命令、bin/obj、不 restore 不启动服务）、状态行五分支（尚未构建/muted、退出码 0=ok 含耗时、退出码 1=bad 不含「退出码 0」、超时=bad、缺退出码=bad 不猜测）、禁词检查。**变异验证实测可红**：禁用 `timedOut` 臂 → 超时用例变红；空记录臂误改坏文案 → 空态用例变红；还原后全绿。（踩坑记录：根目录 `dotnet build` 后直接 `dotnet test --no-build` 一度仍报 213 用例——测试工程未被根构建重编；显式 `dotnet build ApifoxHelper.Tests.csproj` 后恢复。）
- **Linux 运行时验证（本轮新增，超出仅编译验证的口径）**：构建链路不触碰 WinForms，在 /tmp 以一次性 net8.0 控制台链接纯逻辑源文件（Infrastructure 的 Core/FileStore/Redactor、Profiles 的 ProfileStore/ProjectProfile、Tasks/、Desktop/Native 的 BuildStatus，镜像 GlobalUsings；不进仓库、已清理），在真实 `FileStore`/`TaskStore` 上创建任务（配置校验要求 API 项目文件存在，按实创建占位 csproj）并按 `BuildService` 落盘形状写入 build.json，9 项断言全部通过：六字段（command/startedAt/exitCode/timedOut/seconds/outputTail）落盘回读一致且 startedAt 可解析；退出码 0=ok 含耗时；退出码 1=bad 且不含「退出码 0」；超时=bad；整数秒显示「12 秒」无小数尾；缺退出码不猜测；确认文案齐全。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 224 用例全部通过（含令牌接线机械检查对新文件 BuildLogDialog.cs 的扫描）。**GUI 真机运行与 100%/200% DPI 截图仍未验证**（Linux 无法运行 WinForms GUI）——「执行定向构建（原生确认）→ 进度 → 状态行与构建输出」的对话框交互只在代码与编译层面成立（确认文案、门禁与状态行口径已在 Linux 真实执行；`BuildService` 是既有 F06 代码，本轮一行未改且未在 GUI 之外触发任何真实构建）。Windows 真机需按「任一任务 → 生成文档区 → 执行定向构建 → 确认 → 观察进度与结果行 → 查看构建输出」走一遍，并核对失败构建（如临时改坏 csproj）的状态行不显示成完成。未改写入安全语义（`BuildService` 未动一行）；本轮未连接或写入 Apifox，未执行任何真实构建。


## 原生切片「注释草稿其余入口」（2026-11-07 第十四轮）

- **接续前确认**：上一轮工作树干净，HEAD（2177a63）的 Linux `dotnet build` 0 警告 0 错误、224 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。
- **原生交互推进一小步（F04 其余入口接入原生）**：注释草稿的四种生成入口至此全部可在原生界面使用——规则提取（第四轮）、模型生成、材料导出/结果导入、手工填写；写入范围仍限于任务数据（comment-drafts.json 与阶段计数），不写源码、不访问 Apifox；候选文字与证据校验全部经既有 `DraftService` 单一口径。
  - 任务详情源码注释区新增「模型生成候选」与「导出材料」（均在扫描完成且无作业运行时可用）：「模型生成候选」只在配置的草稿生成方式为 OpenAI 兼容模型服务时可用（与冻结页面同一规则，禁用原因在固定说明行交代），点击后先按材料口径统计位置与涉及源码文件，弹**原生确认对话框**（外部发送保留原生确认、默认按钮在「取消」，文案与冻结页面同一口径：位置数、涉及文件、已排除配置/凭据文件并脱敏、模型只返回候选文字不修改文件不调用 Apifox、候选需逐条审阅），确认后经共享 `JobRunner` 调既有 `GenerateWithModelAsync`（作业名「模型生成草稿」，进度与取消沿用固定占位控件；未保存密钥等拒绝原因经作业失败行如实显示）；「导出材料」经既有 `BuildMaterial` 后先按冻结页面同一落点写任务目录副本 material.json，再经原生「另存为」写出同一内容（UTF-8 无 BOM，默认文件名 `material-<任务ID>.json` 与冻结页面下载同名）。
  - 注释候选审阅对话框（`CandidatesDialog`）新增「导入编码助手结果」：原生选择结果 JSON，经既有 `ImportResultAsync` 校验写入候选（slotId 不在本次扫描、注释标记、占位文字等拒绝原因如实列出，最多显示 3 条与冻结页面同一口径），导入后刷新清单与计数、就地逐条审阅；没有候选的空态仍可导入。`ImportResultAsync` 返回值从匿名对象改为命名记录 `DraftImportResult`（路由 JSON 形状不变：imported/rejected/errors，Web camelCase 序列化逐键一致），原生可强类型读取。
  - 注释缺口对话框（`ScanResultsDialog`）新增「手工填写说明」与「删除失效标签」（后者只在选中失效 param 时可用）：手工填写经小输入对话框（文字校验与生成/导入共用 `DraftService.ValidateText` 单一口径，占位文字/注释标记/控制字符等内联显示、不关闭对话框），「添加为候选」经既有 `AddManualAsync` 写任务数据；两动作执行期间禁用发起按钮防重复提交。该对话框从只读变为可写任务数据，文件头与任务页注释同步更新。
  - 顺带修正两处陈旧挂账文案：源码注释区说明行的「模型生成、材料导入与手工填写将在后续切片接入」换成实际能力与门禁说明；候选对话框两处空态提示（从未生成/规则提取无结果）改为指引现有四个入口。
- **口径收敛为单一落点**：确认文案、导出文件名、导出/导入/手工完成文案与模型入口可用性提取为 `Desktop/Native/DraftEntries.cs`（不依赖 WinForms、与测试工程共享编译；候选校验仍在 DraftService 单一口径）；「材料涉及源码文件清单」与「没有需要生成草稿的注释位置。」提取为 `DraftService.EvidenceFiles` / `DraftService.NoSlotsMessage`，`POST /api/tasks/{id}/comment-drafts/material` 路由原内联表达式改为调用同一助手（纯行为保持，`Distinct()` 保序语义不变）。
- **测试 224 → 241 用例**：新增 `DraftEntriesTests`（17）：CanGenerateModel 理论用例（openai 可用；none/file/空/空指针/大小写不同不可用）、导出文件名与冻结页面同名、确认文案（位置数、涉及文件、脱敏说明、不改文件不调用 Apifox、逐条审阅）、无证据片段明示、超长文件清单截断如实显示（前 20 个列出、第 21 个不出现、其余 5 个文件略）、导出/导入完成文案（数量、首条拒绝原因、超过 3 条只显示 3 条、无错误时无冒号段）、手工完成文案指向审阅、禁词检查。**变异验证实测可红**：CanGenerateModel 恒真 → 5 用例红；ImportDoneText 去掉拒绝原因 → 2 用例红；文件清单不截断 → 截断用例红；各自还原后全绿。
- **Linux 运行时验证（本轮新增，超出仅编译验证的口径）**：草稿链路不触碰 WinForms，在 /tmp 以一次性 net8.0 控制台链接纯逻辑源文件（Infrastructure 的 Core/FileStore/JobRunner/Redactor/TextFiles、Tasks、Profiles 的 ProfileStore/ProjectProfile、Source 的 SourceModels/DocComments（带 Roslyn 包）、Comments 的 DraftModels/DraftService/ModelClient、Desktop/Native 的 DraftEntries，镜像 GlobalUsings；仅 CredentialStore（DPAPI 为 Windows 专属）与 SourceScanService.SlotEvidenceHash 以桩补齐编译签名——真实文件一行未改；不进仓库、已清理），在真实 FileStore/TaskStore/ProfileStore 上创建默认与 openai 两份配置、两个任务并写入 symbols/evidence 夹具，24 项断言全部通过：材料只含待草稿位置（失效 param 不进材料）、格式与说明齐备、按接口分组、文件清单去重保序、任务目录副本可再解析、确认文案口径；导入合法候选接受/不存在位置与注释标记被拒/不存在的证据编号忽略并警告/落盘来源 file/递增任务版本/阶段计数刷新；手工空说明被拒（文案逐字）、删除失效标签只对失效位置可用（文案逐字）、手工候选 manual+confirmed、removeStale 候选动作与空文字、位置不存在被拒、并入同一候选清单；模型生成未确认发送被拒、密钥缺失被拒（文案逐字）、未启用模型服务被拒并指向导出材料；入口可用性与配置单一口径。模型生成的实际网络调用未验证（需要真实模型服务与密钥，本轮环境不具备；生成链路服务代码本轮一行未改）。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 241 用例全部通过（含令牌接线机械检查对新文件 DraftEntries.cs 的扫描）。**GUI 真机运行与 100%/200% DPI 截图仍未验证**（Linux 无法运行 WinForms GUI）——「模型生成（确认 → 进度）→ 导入结果 → 逐条审阅」「导出材料 → 编码助手 → 导回」「缺口清单手工填写/删除失效标签」只在代码与编译层面成立（材料/导入/手工/拒绝路径服务链路已在 Linux 真实执行）。Windows 真机需按「扫描 →（未启用模型服务时模型按钮禁用）导出材料 → 另存为 → 导入编码助手结果 → 核对拒绝原因 → 手工填写 → 缺口清单删除失效标签 → 候选审阅接受」走一遍；配置了真实模型服务时核对确认对话框文件清单与发送进度。如实挂账。未改写入安全语义（`DraftService` 只改返回类型与两处提取，校验与文案逐字保留；路由行为不变）；本轮未连接或写入 Apifox，未调用任何模型服务。

## 原生切片「补充说明」（2026-11-07 第十五轮）

- **接续前确认**：上一轮工作树干净，HEAD（9470bc3）的 Linux `dotnet build` 0 警告 0 错误、241 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。
- **原生交互推进一小步（差异确认 → 补充说明，F10 接入原生）**：旅程第 6/7 步的最后一块——冻结页面差异详情里的「业务说明补充」编辑区现在有了原生对应物；写入范围限于任务数据（supplement.json 与任务版本递增），不写源码、不访问 Apifox，离线任务同样可用（补充是本机数据，报告与「只合并说明」变体依赖它）。
  - 任务详情「Apifox 差异与同步」区新增「补充说明」按钮（与「处理差异确认」同门控：扫描完成、无作业运行、任务目录已有 diff.json；不要求在线），打开 `Desktop/Native/SupplementsDialog.cs`：左侧接口清单（方法+路径等宽、名称、已保存补充 N 处），右侧按检查结果构造该接口的编辑位——接口标题、接口说明、query/path 参数、请求体、200 响应各一个说明位，输入框提示显示现状文字（与冻结页面同一取值先后：标题远端优先、说明目标优先），每位的「保留现状，本次不补充」勾选与冻结页面同一口径；header 参数不设编辑位、示例不在本对话框编辑（冻结页面同样不提供，不另造用户路径）。
  - 「保存本接口补充」为实心主按钮：只替换本接口本次编辑位的条目（保留现状 → KeepAsIs 值为空、填写文字 → 去空白、两者皆空 → 不写条目），其他接口与本接口未编辑位（含示例）原样保留；「放弃修改」ghost 还原本接口已保存内容；有未保存修改时计数行尾如实说明，切换接口丢弃未保存输入与冻结页面一致（固定说明行交代）。内容变化递增任务版本并置过期（旧检查与旧计划作废）、未变化不写文件可重复保存，状态行按版本是否变化如实区分——与「处理差异确认」同一呈现规则。保存后需重新检查，补充才会进入合并结果与同步计划，对话框不代替重新检查。
  - 已保存补充读取失败时停用保存（避免用空补充覆盖其他接口的既有内容），与决策对话框的失败防护同型。
- **服务口径单一化**：`PUT /api/tasks/{id}/supplement` 路由原先内联的校验（示例须为合法 JSON、说明超长拒绝）与保存（内容变化递增版本、previewable/awaitingConfirm 置 expired、无变化不写文件）原样提取为 `Sync/SupplementService.SaveAsync`，路由改为调用同一服务（校验文案逐字保留，行为与响应不变）——冻结页面与原生对话框单一口径，纯行为保持提取（与第十轮 DecisionService 同法）。
- **编辑位与切片口径收口为单一落点**：`Desktop/Native/SupplementEntries.cs`（不依赖 WinForms、与测试工程共享编译）——编辑位构造、既有条目定位、本接口切片合并、保存结果文案。期间确认一个真实口径事实：`DocOperation.Parameters` 是按 `in:name` 序的 SortedDictionary，检查结果序列化到冻结页面的 JSON 也是同一顺序，因此原生编辑位的参数顺序与其一致（path 在 query 之前）——先按插入序写断言测出失败，核对落盘形状后修正断言，口径以共享数据模型为准。
- **测试 241 → 248 用例**：新增 `SupplementEntriesTests`（7）：编辑位清单与位置/字段（含参数顺序与 header 排除）、现状文字取值先后、仅源码接口的空态编辑位、既有条目定位与计数、保留现状优先于输入框文字且空输入不写条目、切片替换不误伤（本接口示例、其他接口条目原样保留，原 store 不被改动）、保存文案两分支与禁词。**变异验证实测可红**：编辑位误加 header → 编辑位清单断言变红；保留现状时误存输入框文字 → keep-wins 断言变红；还原后全绿。
- **Linux 运行时验证（本轮新增，超出仅编译验证的口径）**：补充链路不触碰 WinForms，在 /tmp 以一次性 net8.0 控制台链接纯逻辑源文件（Infrastructure 的 Core/FileStore、Profiles 的 ProfileStore/ProjectProfile、Tasks、Contracts 的 DocModel/DiffEngine、Sync 的 CheckModels/SupplementService、Desktop/Native 的 SupplementEntries，镜像 GlobalUsings；不进仓库、已清理），在真实 FileStore/TaskStore/ProfileStore 上创建配置与任务、按检查落盘形状写入 diff.json，28 项断言全部通过：编辑位在真实落盘数据上成立（参数顺序、现状文字、header 排除、仅源码空态）；保存路径（版本递增、previewable 置 expired、UpdatedAt 设置、落盘与返回一致、保留现状优先、去空白）；重复保存不递增并返回已有内容；再次变化再递增；其他接口保存不误伤、同接口示例保留；拒绝路径三条文案逐字一致且不改动任务、不递增版本；合法示例通过校验按原样存储；保存文案两分支。期间修正两处验证脚本自身的口径误设（从空 store 保存会覆盖其他接口——真实流程与对话框都是先载入完整 store 再切片；原始路由按原样存储不去空白），服务行为经核对无误、未改动。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 248 用例全部通过（含令牌接线机械检查对新文件 SupplementsDialog.cs 与 SupplementEntries.cs 的扫描）。**GUI 真机运行与 100%/200% DPI 截图仍未验证**（Linux 无法运行 WinForms GUI）——「补充说明 → 逐位编辑 → 保存 → 重新检查后进入合并结果」只在代码与编译层面成立（补充保存的服务链路已在 Linux 真实执行）；Windows 真机需按「任一已检查任务 → 补充说明 → 填写接口说明与参数说明、勾选一处保留现状 → 保存 → 核对状态行 → 任务列表 Apifox 列变「计划已过期」→ 重新检查后在检查结果中看到人工补充来源的合并条目 → 再次保存相同内容提示未修改」走一遍。F10 至此与冻结页面同一能力面（人工说明与场景；示例沿冻结页面口径不在差异页编辑）。未改写入安全语义（补充校验为行为保持提取）；本轮未连接或写入 Apifox。

## 原生切片「连接与写入探测」（2026-11-07 第十六轮）

- **接续前确认**：上一轮工作树干净，HEAD（57c1fd5）的 Linux `dotnet build` 0 警告 0 错误、248 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。
- **原生交互推进一小步（首次配置旅程第 5 步，F02 接入原生）**：同步写入的总门禁——写入兼容探测与连接检查——此前只有冻结页面有入口，原生界面完全缺失；本轮补上，这是任务旅程之外唯一缺口的配置侧能力。
  - 项目配置页新增「连接与写入探测」ghost（选中配置后可用），打开 `Desktop/Native/CapabilitiesDialog.cs`：
    - **连接检查（只读）**：打开时经既有 `CompatService.CapabilitiesAsync` 一次取得 CLI 版本与路径、登录状态、项目读取（接口总数）、写入兼容（按 CLI 版本登记的探测记录）四项，徽标一律颜色 + 文字双通道；加载中占位「检查中…」、失败内联错误不弹窗；「重新检查」ghost 随时重查（探测执行期间不禁用，检查只读无冲突）。
    - **写入兼容探测**：输入 测试项目 ID（预填配置的 Apifox 项目 ID）/ 测试分支名（预填配置分支，留空为主分支）/ 测试接口 ID 与确认勾选；输入校验内联展示 + 主按钮灰掉（`CompatEntries.CanRun`，数字 + 勾选确认），不弹窗；「运行写入探测」先经原生确认对话框（默认按钮在「取消」，副作用如实说明：原样写回说明、临时修改说明并恢复、原样写回参数/请求体/响应，并指明目标项目/分支/接口），确认后经共享 JobRunner 调既有 `CompatService.ProbeAsync`——**作业作用域与冻结页面 write-probe 路由同一键（`profile:{id}`，CompatEntries.JobScope 单一口径）**，同一配置同时只允许一个探测作业；执行中输入禁用、运行钮换成「停止」（位置固定不跳位），进度行固定占位说明逐项写入需要时间。
    - **结果展示**：探测通过/未通过状态行与冻结页面同一文案（「CLI N 写入探测通过，已开放同步写入。」/「探测未通过，写入保持关闭。」），逐项检查结果在只读清单（检查项/结果/详情，结果列颜色 + 文字双通道）；失败、停止、无结果记录三种结束态分别如实显示（停止时说明测试接口的说明已在停止前恢复）；结束后自动重查连接状态，写入兼容行立即反映新登记。重开对话框时若有探测在跑，恢复执行中状态。探测的写入边界（三类往返写入、逐项回读核对、按 CLI 版本登记）全部在既有服务层单一口径，原生层一行未新增写入逻辑。
- **服务返回类型行为保持提取**：`CompatService.CapabilitiesAsync` 原返回匿名对象，原样改为命名类型 `CapabilityReport`（含 `CapabilityCli/Login/Project/WriteCompat`，与既有 `WriteCompatRecord` 同落 `Sync/PlanModels.cs`），`/api/capabilities` 路由代码不变——JSON 键名经 JsonSerializerDefaults.Web 的 camelCase 与 WhenWritingNull 逐键保持（login/project 为空时整键省略），冻结页面读取行为不变；形状由测试断言钉住。
- **切片口径收口为单一落点**：`Desktop/Native/CompatEntries.cs`（不依赖 WinForms、与测试工程共享编译）——`CanRun`（探测入口可用性）、`ProbeConfirmText`（确认文案，副作用与目标如实说明）、`ResultHeadline`（通过/未通过状态行）、`CheckBadge`/`CliBadge`/`LoginBadge`/`ProjectBadge`/`WriteCompatBadge`（徽标文字与色调，仅只读用 warn 不用 bad——门禁状态而非故障）、`JobScope`（作业作用域键与冻结页面一致）。
- **测试 248 → 274 用例**：新增 `CompatEntriesTests`（26）：CanRun 六分支、确认文案（三类副作用 + 目标 + 主分支回退 + 确认问句收尾）、结果状态行两分支（未通过不得含「已开放」）、五组徽标文字/色调、作业作用域键、禁词检查；`CapabilityReport` JSON 形状两条——以 `JsonDefaults` 序列化断言 cli/login/project/writeCompat 与 record 的全部键名（钉住 /api/capabilities 路由契约）与 login/project 省略行为。**变异验证实测可红**（四处，各自独立变红、还原后全绿）：WriteCompatBadge 的仅只读 tone 改 bad → 徽标断言红；ResultHeadline 未通过文案注入「已开放」→ 状态行断言红；CanRun 去掉确认门 → CanRun 六分支 5 用例红；`CapabilityReport.WriteCompat` 属性改名 → 形状断言编译红（测试直接引用命名类型，属性名与 JSON 键同时钉住）。期间令牌接线机械检查当场拦下对话框里一处档外缩进值 92（改用两列网格对齐），机械检查有效。
- **Linux 运行时验证（本轮新增）**：兼容链路不触碰 WinForms，在 /tmp 以一次性 net8.0 控制台链接纯逻辑源文件（Infrastructure 的 Core/FileStore/ProcessRunner/Redactor/TextFiles/JobRunner、Profiles 的 ProjectProfile/ProfileStore、Tasks、Contracts、Apifox 的 ApifoxCli/NativeUtil/RemoteSnapshot、Sync 的 CheckModels/PlanModels/SyncExecutor/CompatService，镜像 GlobalUsings；SyncExecutor 构造参数级联到的 SyncPlanService/CheckService 以编译签名桩补齐——沿用第十四轮 CredentialStore 桩的先例，真实文件一行未改；不进仓库、已清理），在真实 FileStore 上执行共享 CompatService，16 项断言全部通过：探测入口四条拒绝文案逐字一致（确认未勾选、项目 ID 缺失/非数字、接口 ID 缺失——都在任何 CLI 调用之前拒绝，Linux 无 CLI 也可真实执行）；登记门禁六项（空表未通过、失败记录不开放写入、通过记录登记后开放、同版本多条取最新、其他版本不开放、write-compat.json 落盘可重读）；CapabilitiesAsync 在无 CLI 环境（node 路径指向不存在文件确定性走「未找到 node」分支）三项（CLI 不可用并给出安装指引、不检查登录与项目、**无法确认版本时写入门禁保持关闭——即使 2.2.6 已有通过登记**）；真实报告的 JSON 形状三项（cli 不可用分支、login/project 整键省略、writeCompat 无 record）。期间踩坑一次并记录：/tmp 工程 csproj 显式 Include 了项目目录内文件被 SDK 默认包含，NETSDK1022 报重复项（与第十一轮同型坑），移除显式项即修复。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 274 用例全部通过（含令牌接线机械检查对新文件 CapabilitiesDialog.cs 与 CompatEntries.cs 的扫描）。**GUI 真机运行与 100%/200% DPI 截图仍未验证**（Linux 无法运行 WinForms GUI）——「连接与写入探测 → 确认 → 逐项检查结果 → 写入兼容行刷新」只在代码与编译层面成立（探测校验、登记门禁与 CapabilitiesAsync 服务链路已在 Linux 真实执行）；**探测对真实 Apifox 的端到端执行仍未验证**（F02 维持「已实现（待验证）」：探测从未对任何真实项目运行过，需要用户先指定专用测试项目/分支/接口）；Windows 真机需按「选中配置 → 连接与写入探测 → 核对 CLI/登录/项目读取 → 填测试接口并确认 → 运行 → 核对逐项结果与写入兼容行」走一遍。未改写入安全语义（CapabilitiesAsync 为返回类型行为保持提取，路由 JSON 逐键不变；探测写入边界在 CompatService 单一口径未动）；本轮未连接或写入 Apifox，未执行任何真实探测。

## 原生切片「模块与模板」（2026-11-07 第十七轮）

- **接续前确认**：上一轮工作树干净，HEAD（be8697c）的 Linux `dotnet build` 0 警告 0 错误、274 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。
- **原生交互推进一小步（首次配置旅程第 3 步的模板部分，F01/F16 配置侧最后缺口之一）**：项目配置对话框此前只能编辑核心字段，挂账说明「模块范围、模板与模型服务等设置不在本切片编辑」。其中**新接口模板是硬前置**：检查阶段对没有目录/模板配置的新接口直接给阻断（CheckService「新接口需要在项目设置中配置目标目录与模板后才能创建。」），同步计划也会排除新建接口——原生用户此前无法在任何一步补上这个配置，新接口同步在纯原生工作流里永远走不通；模块范围则是任务创建按模块勾选的数据来源。本轮补上两者；模型服务配置与密钥（Generation + DPAPI CredentialStore）仍如实挂账。
  - 项目配置对话框（`ProjectsPage.cs` 内 `ProfileDialog`）新增「模块与模板…」ghost，打开 `Desktop/Native/ProfileAdvancedDialog.cs`：
    - **模块范围**：多行文本「每行：模块名=路径前缀1,路径前缀2」，与冻结页面同一格式与提示文案。
    - **新接口模板**：目录 ID（留空=未指定，数字校验内联 + 拒绝「目录 ID 应为数字。」——冻结页面会把非法值静默变成 null，原生明确拒绝不静默丢值）、状态（开发中/设计中/测试中/已发布四档，与冻结页面同一取值与顺序；已有配置出现未知状态值时按原值追加一项显示，不静默改值）、服务 ID、标签（逗号分隔、去空白、过滤空项、不去重——与冻结页面一致）、鉴权 auth / 安全方案（JSON 对象）/ 公共 Header 参数（JSON 数组）三个等宽 JSON 编辑位。
    - **JSON 字段校验与冻结页面逐字同文案**：留空与「null」都是不设置；非法 JSON →「鉴权 auth 不是合法 JSON」「安全方案 不是合法 JSON」「公共 Header 参数 不是合法 JSON」（冻结页面 jsonOrNull 同文案）；合法但非对象/非数组/数组元素非对象时明确拒绝（REST 路由的 STJ 绑定同样拒绝，原生提前内联给出）。**全部字段解析通过才整体应用到配置对象**，任一项不合法就停在状态行，不写一半。
    - **从既有接口提取**：接口 ID 数字校验（拒绝文案与冻结页面同款「请输入接口 ID」），配置未选 Apifox 项目时拒绝（与路由同文案「请先选择 Apifox 项目。」），经既有 `ApifoxCli.GetEndpointAsync` 读取接口详情后按共享映射填入字段；成功状态行「已填入模板字段，请检查后保存。」与冻结页面同一口径。读取期间提取按钮禁用、状态行占位；失败内联显示（与路由同一「读取接口失败：」前缀，异常经 Redactor 脱敏）。
  - **对话框打开前先把核心字段收集到配置对象**（`ProfileDialog` 改为持有单一 `ProjectProfile` 实例：新建时同一个实例随「保存」落盘，编辑时即已有实例）——提取因此能用刚填写、尚未保存的 Apifox 项目 ID；「确定」把模块与模板应用到同一实例，随外层「保存」经 ProfileStore 规整与落盘，对话框自身不落盘。原挂账说明换成「模块范围与接口模板在「模块与模板」中编辑；模型服务等其余设置保持原值，新建时使用默认值。」
- **提取映射收口为单一落点**：REST 路由 `/api/apifox/template-from-endpoint` 内联的「接口详情 JSON → NewEndpointTemplate」映射原样提取为 `Profiles/EndpointTemplateReader.Read`（只取目录、状态、服务、鉴权、安全方案、公共 Header 参数、标签，不克隆脚本/用例/Mock），路由改为调用同一映射（纯行为保持提取）；其依赖的数值解析 `RemoteSnapshotService.ReadLong` 实现原样移到 `Infrastructure/Core.cs` 的 `JsonNodeValue.ReadLong`（只依赖 BCL，测试工程已共享编译 Core.cs），`RemoteSnapshotService.ReadLong` 改为一行委托——九处既有调用点零改动。
- **切片口径收口为单一落点**：`Desktop/Native/ProfileAdvanced.cs`（不依赖 WinForms、与测试工程共享编译）——模块行解析/格式化（与冻结页面同一行为：等号前是名称、第二段按逗号拆分，第三段及以后与冻结页面一样丢弃；无等号行是只有名称的模块）、标签解析/格式化、状态四档与未知回退、JSON 字段解析与文案、`ParseFields`（目录 ID 数字校验，任一项不合法整体拒绝）、`Apply`（写到配置对象；真正的规整与前缀补斜杠仍由 ProfileStore.Normalize 单一口径执行）、`ExtractError` 与 `FromTemplate`（提取结果回填文本；JSON 展示用 JsonDefaults.Options——2 空格缩进、中文原样，与任务资料同一序列化口径）。
- **测试 274 → 318 用例**：新增 `ProfileAdvancedTests`（44）：状态四档取值/顺序/索引/未知回退；模块行往返与冻结页面同款解析（含「多等号=a=b=c」第三段丢弃、「无等号行」名称模块）；标签去空白过滤空项且不去重；JSON 字段留空/null 不设置、三条非法文案逐字、非对象/非数组/非对象元素三条拒绝文案；目录 ID 可选数字校验与整体拒绝；Apply 写入全部字段；提取回填（完整模板、空模板回退 developing、JSON 缩进中文原样可再解析）；提取入口校验；EndpointTemplateReader 只取允许字段（scripts/cases/responses 不进入模板、header 非对象元素过滤、数字字符串目录 ID、auth 深拷贝非别名）。**变异验证实测可红**（四处，各自独立变红、还原后全绿）：状态档删掉 released 臂 → 4 用例红；JSON 非法文案改写 → 3 用例红；目录 ID 数字校验删除与 FromTemplate 状态回退改空 → 各 1 用例红；映射去掉 DeepClone → 别名断言红。
- **Linux 运行时验证（本轮新增）**：链路不触碰 WinForms，在 /tmp 以一次性 net8.0 控制台链接纯逻辑源文件（Infrastructure 的 Core/FileStore、Profiles 的 ProjectProfile/ProfileStore/EndpointTemplateReader、Desktop/Native/ProfileAdvanced，镜像 GlobalUsings；不进仓库、已清理），在真实 FileStore/ProfileStore 上执行，**43 项断言全部通过**：真实形状接口详情的提取映射（含脚本/用例不进入模板、非对象 header 元素过滤、数字字符串目录 ID）、回填文本（JSON 缩进中文原样、可再解析且与原值一致）、模块行与标签解析口径、七条 JSON/目录校验文案逐字、提取入口校验、状态四档；配置对象应用后在真实 store 上 Save → Get 重读一致（新配置取得 p 前缀标识、空名模块被规整过滤、前缀自动补斜杠、模板 JSON 中文原样落盘、重读后再格式化-解析往返一致）；解析失败整体拒绝且不改动配置对象。期间发现并修正验证脚本自身两处夹具口径误设（「空名=」行名称非空不会被规整过滤，应用「=api/x/」命中；round-trip 断言误对字符串取 Count），均为脚本错误、服务一行未改。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 318 用例全部通过（含令牌接线机械检查对新文件 ProfileAdvancedDialog.cs 与 ProfileAdvanced.cs 的扫描）。**GUI 真机运行与 100%/200% DPI 截图仍未验证**（Linux 无法运行 WinForms GUI）——「模块与模板 → 编辑/提取 → 确定 → 保存」只在代码与编译层面成立（解析口径、提取映射与保存落盘链路已在 Linux 真实执行）；**从既有接口提取对真实 Apifox 的端到端执行仍未验证**（提取走既有 CLI 只读链路，需真实项目与接口）；Windows 真机需按「编辑配置 → 模块与模板 → 填模块行 → 填接口 ID 提取 → 核对回填 → 确定 → 保存 → 重新打开核对回显」走一遍。未改写入安全语义（模板只影响新接口创建的目标内容，边界仍在 SyncPlanService/SyncExecutor；映射与数值解析均为行为保持提取）；本轮未连接或写入 Apifox。



## 原生切片「模型服务」（2026-11-07 第十八轮）

- **接续前确认**：上一轮工作树干净，HEAD（1244e1f）的 Linux `dotnet build` 0 警告 0 错误、318 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。
- **原生交互推进一小步（首次配置旅程的生成方式与模型密钥，F01/F16 配置侧最后缺口）**：项目配置对话框的挂账说明「模型服务等其余设置保持原值，新建时使用默认值」是 F01 在原生界面的最后一个缺口——「模型生成候选」（F04）按配置的 `Generation.Mode == "openai"` 才可用，模型生成还需要按配置 ID 存储的服务密钥（`DraftService`：「尚未保存模型服务密钥。」），原生用户此前无法配置两者，模型生成入口在纯原生工作流里永远不可用。本轮补上。
  - 项目配置对话框（`ProjectsPage.cs` 内 `ProfileDialog`）新增「模型服务…」ghost，打开 `Desktop/Native/GenerationDialog.cs`：
    - **生成方式**：三档下拉（仅规则提取与手工填写 / OpenAI 兼容模型服务 / 导出材料交给编码助手），与冻结页面 `GENERATION_MODES` 同取值同顺序；已有配置出现未知方式值时按原值追加一项显示，不静默改值。
    - **服务地址 / 模型**：去空白、留空即不设置（与冻结页面「|| null」同一结果）；地址行提示与冻结页面同一口径「例如 https://api.openai.com/v1；只发送用户确认的材料。」。openai 方式的地址/模型必填与 https（本机 http 除外）校验不在对话框重复实现——随外层「保存」由 `ProfileStore.Validate` 单一口径给出（「请填写模型服务地址。」「模型服务地址必须使用 https（本机地址除外）。」「请填写模型名称。」）。
    - **超时秒数 / 每批数量**：留空与冻结页面同取默认 120/30（冻结页面 `+值 || 默认`）；非数字内联拒绝「超时秒数应为数字。」「每批数量应为数字。」——冻结页面会把非法数字静默变成默认值，原生明确拒绝让用户知道值没有被采纳（沿用第十七轮目录 ID 的先例）；越界钳制（10–600、1–100）仍由 `ProfileStore.Normalize` 单一口径执行。
    - **服务密钥**：密码框（不回显）+「保存密钥」；密钥经既有 `CredentialStore`（Windows DPAPI 当前用户）加密存储，不回显、不导出、不进报告（说明行与冻结页面同一口径「用 Windows DPAPI 加密保存在本机，不会显示、导出或写入报告。」）；占位文字「已保存（输入新值可替换）/未保存」与结果文案「密钥已加密保存/密钥已清除」与冻结页面同一口径；**门控与冻结页面同一规则**——新建配置未落盘前保存按钮禁用（密钥按配置 ID 存储；空 ID 在 `Ids.Validate` 会拒绝，门控的必要性经运行时断言钉住），已保存配置保存密钥前先经 `ProfileStore.Get` 核对存在（与冻结页面路由同序）再写入；输入新值即替换、留空保存即清除；保存后清空输入框并按 `HasModelKey` 刷新占位。
  - 改动应用到配置对象的同一实例，随外层「保存」经 ProfileStore 规整落盘，对话框自身不落盘（与「模块与模板」同一模式）；打开前先收集核心字段（保持第十七轮的单一实例语义）。
- **切片口径收口为单一落点**：`Desktop/Native/GenerationEntries.cs`（不依赖 WinForms、与测试工程共享编译）——生成方式三档与索引（未知 -1，对话框按原值追加）、`ParseFields`（去空白/留空 null/数字默认与拒绝）、`Apply`（写全部五个字段）、密钥占位/说明/门控/结果文案。密钥的 DPAPI 加密存储仍在 `CredentialStore` 单一口径，本文件不复制。
- **测试 318 → 338 用例**：新增 `GenerationEntriesTests`（20）：三档取值/顺序逐字（钉住与冻结页面 GENERATION_MODES 一致）、ModeIndex 已知/未知、解析去空白/留空 null、数字留空默认 120/30、两条非法数字拒绝文案逐字、Apply 写全部字段与 null 清旧值、密钥占位/说明/门控/结果文案逐字、用户可见文案禁词检查。**变异验证实测可红**（四处，各自独立变红、还原后全绿）：改 openai 档位文案 → 三档断言红；数字校验改静默兜底 → 两条拒绝用例红；Apply 丢 Model 写入 → 两个 Apply 用例红；密钥占位两条互换 → 占位用例红。
- **Linux 运行时验证（本轮新增）**：链路不触碰 WinForms，/tmp 一次性控制台链接纯逻辑源文件（Infrastructure 的 Core/FileStore、Profiles 的 ProjectProfile/ProfileStore/CredentialStore、Desktop/Native/GenerationEntries，镜像 GlobalUsings；不进仓库、已清理），在真实 FileStore/ProfileStore/CredentialStore 上执行，**35 项断言全部通过**：解析去空白/留空 null/数字默认 120/30；五字段真实落盘回读一致；越界钳制仍由 ProfileStore 执行（超时 5→10、每批 500→100）；openai 缺地址/非 https 非 loopback/缺模型/未知方式四条真实校验文案逐字；非法数字两条拒绝文案逐字；密钥在 Linux 的真实行为——初始无密钥、读取返回 null（如实不伪造）、清空在无文件时无副作用、写入以「当前系统不支持受保护凭据存储。」真实拒绝（DPAPI 为 Windows 专属，这是真实异常路径而非模拟）且失败后不落半份状态；新建配置空 ID 直接存密钥被「无效的标识。」拒绝（对话框门控的必要性）；三档与密钥文案运行时口径。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 338 用例全部通过（含令牌接线机械检查对新文件 GenerationDialog.cs 与 GenerationEntries.cs 的扫描）。**GUI 真机运行与 100%/200% DPI 截图仍未验证**（Linux 无法运行 WinForms GUI）——「模型服务 → 编辑 → 确定 → 保存 → 重开核对回显」与「保存密钥 → 占位刷新」只在代码与编译层面成立（解析口径、落盘往返、校验文案与密钥的 Linux 真实行为已在 Linux 真实执行）；**密钥的 DPAPI 加密写入与解密为 Windows 专属**，Windows 真机需按「编辑配置 → 模型服务 → 选 openai → 填地址/模型 → 保存密钥 → 核对占位与文案 → 确定 → 保存 → 任务页核对「模型生成候选」可用」走一遍；**模型生成的实际网络调用仍未验证**（无真实模型服务与密钥，F04 维持部分实现）。未改写入安全语义（本切片不触碰任何 Apifox 写入路径；密钥存储边界仍在 CredentialStore，配置校验边界仍在 ProfileStore）；本轮未连接或写入 Apifox，未调用任何模型服务。



## 原生切片「序列化与分析」（2026-11-07 第十九轮）

- **接续前确认**：上一轮工作树干净，HEAD（65fac47）的 Linux `dotnet build` 0 警告 0 错误、338 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。
- **原生交互推进一小步（F01 配置侧最后缺口：序列化与分析、版本备注、配置导出）**：项目配置对话框的挂账说明「其余设置保持原值，新建时使用默认值」覆盖的编辑位——序列化与响应封装、代码分析、质量规则、Apifox CLI——是网页端有而原生缺失的最后一批。其中**响应封装是硬前置**：响应契约追踪（F07）按 `responseWrappers`（如 `PNOSActionResult=Data`）定位通用封装的 data 属性，序列化设置（JSON 命名/枚举字符串/非空引用类型）决定差异比对时的 JSON 名映射，分析设置（配置名/编译常量/调用深度/方法预算/XML 文档路径）决定扫描与传播核对的行为，Apifox CLI 路径与读取并发在 node 不在 PATH 时是唯一补救入口——原生用户此前无法在任何一步补上这些配置，非默认封装的项目在纯原生工作流里响应契约永远追踪不到。本轮补上；版本备注（报告「未绑定代码版本」的数据来源）与配置导出（换机迁移的 designed 路径，密钥按设计不随配置走）一并补齐。
  - 项目配置对话框（`ProjectsPage.cs` 内 `ProfileDialog`）新增「序列化与分析…」ghost，打开 `Desktop/Native/ProfileAnalysisDialog.cs`，四个小节：
    - **序列化与响应封装**：JSON 命名两档下拉（camelCase（ASP.NET Core 默认）/ 保持 C# 属性名，与冻结页面同取值同顺序；未知值按原值追加显示不静默改值）、枚举输出为字符串、非空引用类型（与 Swashbuckle SupportNonNullableReferenceTypes 一致）两个勾选、通用响应封装多行编辑（「每行：类型名=数据属性名，例如 PNOSActionResult=Data」与冻结页面同一格式：没有等号或等号后为空用 Data、等号后的第二段之后与冻结页面解构一样丢弃、空类型名的行由 ProfileStore 规整过滤）。
    - **代码分析**：分析配置名（留空取 Debug）、编译常量（分号或逗号分隔，留空 null 按目标框架推导）、调用深度与方法预算（留空与冻结页面同取默认 3/40，非数字内联拒绝「调用深度应为数字。」「方法预算应为数字。」——冻结页面会把非法数字静默变成默认值，原生明确拒绝不静默丢值）、XML 文档路径多行编辑（「项目名=路径」每行一条；**缺「=」或项目名为空的行明确拒绝**「XML 文档路径每行应为「项目名=路径」。」——冻结页面会把这些行静默丢弃，原生拒绝让用户知道值没有被采纳；同名行后者覆盖前者与冻结页面对象赋值一致）、去掉 Async 后缀（与 MvcOptions.SuppressAsyncSuffixInActionNames 一致）。
    - **质量规则**：通用 data 属性名（留空取 data）、参数说明必填、字段说明必填。
    - **Apifox CLI**：读取并发（留空默认 4，非数字内联拒绝「读取并发应为数字。」）、node 路径与 cli.js 路径（去空白、留空即自动查找）。
    - 全部字段解析通过才整体应用到配置对象，任一项不合法停在状态行不写一半（与「模块与模板」同一模式）；改动随外层「保存」经 ProfileStore 规整与校验落盘（越界钳制：调用深度 0–6、方法预算 1–200、读取并发 1–8；JSON 命名取值校验「JSON 命名策略只支持 camelCase 或 none。」），对话框自身不落盘。
  - **版本备注**进入核心字段（名称/仓库/API 项目/回写范围之后，与冻结页面同位置）：提交号、构建号或版本描述，留空时报告显示「未绑定代码版本」；随 CollectCoreFields 应用到配置对象。
  - **「导出配置（不含凭据）」**ghost（编辑已有配置时可用，与冻结页面同一门控）：导出**已保存**的配置（对话框中未保存的改动不导出，与冻结页面下载存储态同一语义），原生另存为，文件名 `apifox-helper-profile-{id}.json` 与 REST 下载同名。
- **导出字节收敛为单一落点**：`/api/profiles/{id}/export` 路由内联的文件名/内容类型/字节原样提取为 `Profiles/ProfileExport.cs`（`FileName`/`ContentType`/`Render`，只依赖 BCL），路由改为调用同一口径（纯行为保持提取，字节与文件名不变——JsonDefaults.Options 序列化，UTF-8 无 BOM，与配置落盘同一形状）；原生「另存为」与 REST 下载共用同一份文件名与字节。配置对象本身不含凭据：模型服务密钥按配置 ID 存放在 CredentialStore，从不进入配置 JSON，导出即天然不含密钥（运行时断言钉住导出内容无任何密钥字段）。
- **切片口径收口为单一落点**：`Desktop/Native/ProfileAnalysis.cs`（不依赖 WinForms、与测试工程共享编译）——JSON 命名两档与索引（未知 -1，对话框按原值追加）、封装行解析/格式化、编译常量解析（分号逗号分隔、空即 null）/格式化、XML 文档路径解析（缺「=」明确拒绝）/格式化、`ParseAnalysis`（配置名留空 Debug、数字默认与拒绝）、`ParseCli`（路径留空 null、并发数字）、`Apply`（写全部 16 个字段，data 属性名留空取 data）。钳制与命名取值校验不复制，仍在 ProfileStore 单一口径。
- **测试 338 → 369 用例**：新增 `ProfileAnalysisTests`（31）：命名两档取值/顺序逐字（钉住与冻结页面一致）与未知回退；封装解析（基本、缺等号/空等号回退 Data、第三段丢弃、空行跳过、往返一致）；编译常量分隔与空即 null；XML 文档路径解析（去空白、值含等号、同名后者覆盖、缺「=」/缺项目名两条拒绝文案逐字）；`ParseAnalysis` 默认值与两条数字拒绝文案逐字；`ParseCli` 路径与并发；`Apply` 写全部字段与留空 data 回退、null 清旧值；ProfileExport 文件名/内容类型/无 BOM/中文原样可重读。**变异验证实测可红**（四处，各自独立变红、还原后全绿）：封装缺数据属性的 Data 回退删掉 → 1 用例红；XML 文档路径缺「=」改静默丢弃 → 2 用例红；Apply 丢 XmlDocPaths 写入 → 1 用例红；导出文件名改写 → 1 用例红。期间一个教训：变异还原时对未跟踪新文件用 `git checkout --` 失效（pathspec 含未跟踪文件时整条命令失败且被静默吞掉），变异残留在 ProfileExport.cs 中被最终 grep 揪出后手工还原——变异验证的还原也要逐文件核对。
- **Linux 运行时验证（本轮新增）**：链路不触碰 WinForms，/tmp 一次性控制台链接纯逻辑源文件（Infrastructure 的 Core/FileStore、Profiles 的 ProjectProfile/ProfileStore/ProfileExport、Desktop/Native/ProfileAnalysis，镜像 GlobalUsings；不进仓库、已清理），在真实 FileStore/ProfileStore 上执行，**34 项断言全部通过**：解析 → Apply → Save → Get 往返（新建配置取得 p 前缀标识，序列化开关、封装三条、分析六字段、质量三字段、CLI 三字段落盘回读一致）；空类型名封装行在保存时被规整过滤；越界钳制仍由 ProfileStore 执行（深度 9→6、预算 500→200、并发 99→8）；非法命名策略被 ProfileStore 拒绝且文案逐字；五条解析拒绝文案逐字且被拒解析不改动配置对象；封装解析边缘（等号后纯空白经按行 trim 后回退 Data、第三段丢弃、往返一致）；导出文件名/内容类型、无 BOM、**导出字节与配置落盘字节逐字节一致**（同一 JsonDefaults 口径）、可按配置形状重读、不含任何密钥字段。期间修正验证脚本自身一处口径误设（误以为「A=  」等号后纯空白会原样保留成空串——冻结页面 `lines()` 先按行 trim 再拆分，同样回退 Data；实现一致、断言预期写错，服务一行未改）。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 369 用例全部通过（含令牌接线机械检查对新文件 ProfileAnalysisDialog.cs 与 ProfileAnalysis.cs 的扫描）。**GUI 真机运行与 100%/200% DPI 截图仍未验证**（Linux 无法运行 WinForms GUI）——「序列化与分析 → 编辑 → 确定 → 保存 → 重开核对回显」与「导出配置 → 另存为」只在代码与编译层面成立（解析口径、落盘往返、钳制与校验、导出字节已在 Linux 真实执行）；Windows 真机需按「编辑配置 → 序列化与分析 → 填封装行与分析字段 → 确定保存 → 任务页核对响应契约与扫描行为 → 导出配置核对 JSON」走一遍。F01 至此与冻结页面同一配置能力面（全部编辑位 + 导出）。未改写入安全语义（导出为只读动作，配置校验边界仍在 ProfileStore）；本轮未连接或写入 Apifox，未调用任何模型服务。

## 原生切片「扫描总览与降级警告」（2026-11-07 第二十轮）

- **接续前确认**：上一轮工作树干净，HEAD（63411b0）的 Linux `dotnet build` 0 警告 0 错误、369 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。
- **原生交互推进一小步（F03 输出的原生呈现补齐）**：方案 F03 的输出明确包含「未解析符号与外部类型清单、扫描警告」，且处理规则是「明确降级，不按同名猜实现」；冻结页面源码页显示覆盖率总览（比例 + 有效/总数 + 分析等级 + 扫描时间）与「分析警告与降级」面板（manifest.warnings + scan.unresolvedSymbols + scan.externalTypes）。原生界面此前两者都不可见：任务页扫描行只有「接口/位置/有效/候选」计数，用户在纯原生工作流里永远不知道一次扫描发生过降级（引用不完整、条件编译未展开、动态派发未解析、外部类型不可回写）——降级只有被看见才算「明确降级」，这是诚实呈现的红线缺口。本轮补上：
  - 注释缺口对话框（`Desktop/Native/ScanResultsDialog.cs`）顶部筛选行下新增**注释覆盖率行**：`注释覆盖率 50% · 有效 4 / 8 个应对外说明的位置（空标签与占位文字不计为完成）· 分析等级 Debug · 扫描于 2026-11-07 19:38`。比例算法与冻结页面同一（比例先四舍五入到 4 位再换算百分数，一位小数）；分析等级与扫描时间来自 source-manifest.json，缺失（旧任务无清单文件）时整段省略，不显示空标签。
  - 按钮行新增**「分析警告与降级（N）」ghost**（有可见条目才显示），打开只读的 `Desktop/Native/ScanWarningsDialog.cs`：引导行说明降级语义（未解析符号不按同名猜实现、外部类型上的注释位置不可回写），逐行列出扫描警告（原样保留）、未解析符号（「未解析：」前缀）、外部类型（「外部类型（不可回写）：」前缀）——前缀与冻结页面同一。
  - **数据口径**：覆盖率与降级数据来自扫描落盘的 symbols.json（ScanOutput 的 Coverage/UnresolvedSymbols/ExternalTypes）与 source-manifest.json（SourceManifest 的 AnalysisLevel/ScannedAt/Warnings），两者由同一次扫描写入；对话框把两个文件的读取放在同一次 try 里，任一读取失败都按「扫描结果不可读」处理，不显示半份总览。纯只读展示，不写源码、不访问 Apifox。
  - **一处与冻结页面的有意差异（已记录）**：冻结页面折叠面板标题计数只算 warnings + unresolvedSymbols，只有外部类型时会显示「分析警告与降级（0）」；原生按可见条目计数（三类合计），不显示自相矛盾的 0。
- **切片口径收口为单一落点**：`Desktop/Native/ScanOverview.cs`（不依赖 WinForms、与测试工程共享编译）——`CoverageText`（覆盖率行完整文案，等级/时间段按缺失省略）、`DegradationItems`（三类清单合并与前缀，null 清单按无处理——旧形状 symbols 缺字段反序列化为空清单）、`Headline`（标题计数）。对话框只做读取与展示，口径不落在界面代码里。
- **测试 369 → 385 用例**：新增 `ScanOverviewTests`（16）：覆盖率行完整文案逐字（含「空标签与占位文字不计为完成」括注、分析等级、扫描时间）、五档比例四舍五入（66.7/33.3/28.6/100/0）、缺清单段省略、空等级按缺失、降级清单四条与顺序、null 清单、未解析与外部类型前缀各自钉住、标题计数、仅外部类型不出现「（0）」、文案禁词。**变异验证实测可红**（四处，各自独立变红、还原后全绿）：删外部类型分支（3 用例红）、删「空标签与占位文字不计为完成」括注（2 用例红）、删「分析等级」标签（1 用例红）、删标题计数（2 用例红）。
- **Linux 运行时验证（本轮新增）**：链路不触碰 WinForms，/tmp 一次性控制台链接纯逻辑源文件（Infrastructure 的 Core/FileStore、Profiles 的 ProjectProfile/ProfileStore、Tasks 的 TaskModels/TaskFiles/TaskStore、Source 的 SourceModels、Desktop/Native/ScanOverview，镜像 GlobalUsings；不进仓库、已清理），在真实 FileStore/ProfileStore/TaskStore 上创建配置与任务、按 SourceScanService 同一写入路径落盘 symbols.json 与 source-manifest.json 夹具，**14 项断言全部通过**：两个文件读回形状一致（Coverage 计数、未解析/外部类型清单、等级/时间/警告）；覆盖率行在真实读回值上的完整文案逐字；降级清单四条与顺序；标题按可见条目计数且仅外部类型不出现「（0）」；缺清单任务读取为 null、覆盖率行如实省略等级与时间段；旧形状 symbols 缺字段反序列化为空清单、无降级条目（入口隐藏）；100% 与零位置的边界显示。期间发现并修正验证脚本自身一处断言预期误设（`Headline(0)` 含「（0）」是字符串函数的全域行为，真实不变量是 0 条目时入口不显示——脚本错误，服务一行未改）；另两处夹具口径（工作区目录与 API 项目文件必须真实存在才通过 ProfileStore 校验）按真实校验补齐。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 385 用例全部通过（含令牌接线机械检查对 ScanResultsDialog.cs 改动与 ScanOverview.cs 的扫描）。**GUI 真机运行与 100%/200% DPI 截图仍未验证**（Linux 无法运行 WinForms GUI）——「查看注释缺口 → 覆盖率行 → 分析警告与降级清单」只在代码与编译层面成立（覆盖率与降级口径已在 Linux 真实落盘数据上执行）；Windows 真机需按「任一已扫描任务 → 查看注释缺口 → 核对覆盖率行与分析等级/扫描时间 → 打开分析警告与降级 → 核对清单条目与前缀」走一遍。未改写入安全语义（纯只读展示）；本轮未连接或写入 Apifox。

## 原生切片「Apifox 目标选择：读取项目列表 / 读取分支」（2026-11-07 第二十一轮）

- **接续前确认**：上一轮工作树干净，HEAD（cfe4d1c）的 Linux `dotnet build` 0 警告 0 错误、385 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。
- **原生交互推进一小步（F01 的目标选择辅助）**：方案 3.1 要求「选择有权限的 Apifox 项目和已有分支」，冻结页面配置表单为此提供「读取项目列表」「读取分支」两个按钮（经 `/api/apifox/projects`、`/api/apifox/branches` 只读路由拉取后在下拉框中选择回填），原生配置对话框的「Apifox 项目 ID」与「分支」此前是纯文本框——用户必须凭记忆手填数字 ID，与冻结页面同一配置能力面出现缺口，本轮补上：
  - 配置对话框（`Desktop/Native/ProjectsPage.cs` 的 ProfileDialog）两个输入框改为可下拉的 ComboBox（保留手输），行尾新增「读取项目列表」「读取分支」ghost；输入框下方新增说明行「与业务系统中的「项目」无关；读取列表需要先保存配置；分支留空表示主分支」（与冻结页面字段提示同一口径，引用记号按中文排版规则用「」）。
  - **门控与校验与冻结页面同一顺序同一文案**：未保存（新建）的配置不能读取（「请先保存配置」——列表按已保存配置的 CLI 设置调用，与冻结页面路由按 profileId 从磁盘读取同一语义）；读取分支先要求项目 ID 已填（「请先填写项目 ID」）、再要求为数字（「项目 ID 无效。」，与路由同文案）；项目列表无前置输入要求。
  - **列表行为与冻结页面同一**：项目下拉项「名称（ID） · 团队名」（无团队不缀后缀），选中回填项目 ID 与项目名（`ProjectName` 随「保存」落盘，与冻结页面把 projectName 写入配置对象同一行为）；分支下拉项「名称（主分支） · ID」，已归档分支不进列表，选中主分支则分支名留空、分支 ID 置 null，其余回填分支名与分支 ID；手动输入不受下拉影响；重新读取覆盖上一次列表；读取期间按钮禁用、状态行显示进行中/完成（含数量）/失败（CLI 失败描述原样），全部在状态行内联不弹窗。
  - 纯只读：列表读取经既有 `ApifoxCli.ListProjectsAsync/ListBranchesAsync`（与 REST 路由同一链路），选择只改配置对象内存值，落盘仍走既有 `ProfileStore.Save`（含「Apifox 项目 ID 应为数字。」保存校验兜底），不访问 Apifox 写入、不写源码。
- **切片口径收口为单一落点**：`Desktop/Native/ApifoxPicker.cs`（不依赖 WinForms、与测试工程共享编译）——`ProjectRows/BranchRows`（行解析与路由同一字段：teamName 回退 team.name、isMain/isArchived 缺失按 false、非对象元素跳过）、`VisibleBranches`（归档过滤）、`ProjectText/BranchText`（列表项文字与冻结页面同格式）、`BranchValue`（主分支 → 留空）、`BranchGate`（校验顺序：已保存 → 已填 → 数字）、四条门控/失败文案与两条完成文案。对话框只做读取与回填，口径不落在界面代码里。
- **测试 385 → 403 用例**：新增 `ApifoxPickerTests`（18）：行解析（id/name/teamName、team.name 回退、缺省空值、非对象跳过、null/空数组）、分支旗标缺省 false、归档过滤保主分支、项目/分支文字逐字、主分支值为空、门控顺序与文案逐字、失败前缀与路由逐字、完成文案含数量与下一步。**变异验证实测可红**（五处，各自独立变红、还原后全绿）：删归档过滤（1 用例红）、主分支值改分支名（1 用例红）、删数字校验（2 用例红）、项目文字删团队名后缀（1 用例红）、删 team.name 回退（1 用例红）。
- **Linux 运行时验证（本轮新增）**：链路不触碰 WinForms，/tmp 一次性控制台链接纯逻辑源文件（Infrastructure 的 Core/FileStore、Profiles 的 ProjectProfile/ProfileStore、Desktop/Native/ApifoxPicker，镜像 GlobalUsings；不进仓库、已清理），在真实 FileStore/ProfileStore 上以按 CLI 输出形状构造的夹具执行，**23 项断言全部通过**：项目/分支行解析（team.name 回退、无团队 null、归档过滤剩 2、主分支在列）、列表项文字逐字、门控顺序四条逐字、真实配置新建保存 → 按下拉选择回填（项目 ID/项目名/分支名/分支 ID）→ 落盘重读逐值一致、选主分支后分支名与分支 ID 落盘为 null（「留空表示主分支」的落盘语义）、手填非数字项目 ID 被既有保存校验「Apifox 项目 ID 应为数字。」逐字拒绝、未保存配置 `Get` 被「项目配置不存在。」拒绝（证明「请先保存配置」门控对应真实存储行为）。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 403 用例全部通过（含令牌接线机械检查对 ProfileDialog 改动的扫描——新增控件只用 Ui.Ghost/Ui.Caption/UiTokens，间距无档外值）。**GUI 真机运行与 100%/200% DPI 截图仍未验证**（Linux 无法运行 WinForms GUI）——「读取项目列表 → 下拉选择 → 读取分支 → 主分支留空」只在代码与编译层面成立（口径与落盘链路已在 Linux 真实执行）；对真实 Apifox 的列表读取（需已登录的 CLI 与有权限的项目）仍待真机验证，本轮未连接 Apifox。未改写入安全语义（纯只读读取 + 配置对象回填经既有保存校验落盘）。

## 原生切片「长作业完成提醒：任务栏闪烁」（2026-11-07 第二十二轮）

- **接续前确认**：上一轮工作树干净，HEAD（4835580）的 Linux `dotnet build` 0 警告 0 错误、403 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。路由面对照确认：冻结页面调用的全部 REST 能力至此已接入原生（`/api/apifox/folders` 与逐接口 coverage 两条路由冻结页面自身也未调用，不构成对等缺口），本轮按方案 2.1 的原生工作流要素清单推进——「系统通知」是其中唯一未落地的一项。
- **原生交互推进一小步（「系统通知」的会话型落地）**：壳与各对话框对作业完成的感知只存在于轮询界面里——用户在长构建、模型生成或远端检查执行期间切到别的窗口，完成后没有任何唤回信号。方案 2.1 把「系统通知」列为原生工作流要素，2.2 形态边界又裁定本工具无常驻托盘，因此采用 Windows 标准的任务栏闪烁（FlashWindowEx），不引入 NotifyIcon、不弹窗、不抢焦点：
  - `Infrastructure/JobRunner` 新增 `JobCompleted` 事件：作业到达终态（succeeded/failed/canceled）后在 finally 中触发一次（终态与 FinishedAt 落定之后），线程池线程触发、订阅方自行调度到 UI 线程；页面轮询不依赖该事件，服务层除该附加只读事件外一行未改。
  - 壳窗口（`NativeShellForm`）订阅事件，经 `BeginInvoke` 到 UI 线程后按共享口径判定：应用内任一打开窗口（含对话框，`Application.OpenForms` 全量检查 `ContainsFocus`）都不持有焦点、退出未开始、且作业终态为 succeeded（结果待审阅）或 failed（需要看失败原因）时闪烁任务栏（FLASHW_ALL | FLASHW_TIMERNOFG，持续到窗口回前台）；窗口 `OnActivated` 时 FLASHW_STOP 显式停止；canceled 不提醒——停止是用户自己的动作，结果符合预期，回任务页可见「已停止」。订阅在窗口关闭完成时解除。
  - 提醒只闪任务栏，不承载文字；进度与结果的呈现仍在任务页与各对话框的既有状态行（如实报告，不因提醒改变口径）。
- **切片口径收口为单一落点**：`Desktop/Native/AttentionCue.cs`（不依赖 WinForms、与测试工程共享编译）——`ShouldFlash(appFocused, jobState, shutdownStarted)`，规则一句话：用户离开 + 退出未开始 + 终态为 succeeded/failed 才提醒。对话框只做订阅与闪烁，口径不落在界面代码里。
- **测试 403 → 415 用例**：新增 `AttentionCueTests`（12）：四类状态 × 在场/离开 × 退出中的组合（succeeded/failed 离开提醒、canceled/running/未知不提醒、在场一律不提醒、退出中一律不提醒）+ 两条接线机械检查（壳订阅事件并调用口径、激活停止闪烁；JobRunner 在终态落定之后才触发事件——口径存在但没人调用等于没有）。**变异验证实测可红**（六处，各自独立变红、还原后全绿）：删 failed 臂（1 红）、canceled 改为提醒（1 红）、删焦点门控（2 红）、删退出门控（2 红）、壳不订阅事件（1 红）、JobRunner 不触发事件（1 红）。
- **Linux 运行时验证（本轮新增）**：链路中除 WinForms 闪烁调用外的全部逻辑不触碰 WinForms，/tmp 一次性控制台链接纯逻辑源文件（Infrastructure 的 Core/Redactor/JobRunner、Desktop/Native/AttentionCue，镜像 GlobalUsings，Logging.Abstractions 取本机 NuGet 缓存；不进仓库、已清理），在真实 JobRunner 上执行，**20 项断言全部通过**：成功 / 业务失败（UserException 消息透传）/ 未预期异常（错误经 Redactor 脱敏落 Error——既有行为不变）/ 用户取消四类终态各触发一次事件；事件触发时 State 已是终态、FinishedAt 已落、事件参数与 `Get(id)` 同一实例；门控九种组合；同作用域「一个作业」约束保持（409 与文案不变）、取消后同作用域可再次启动。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 415 用例全部通过（含令牌接线机械检查——曾当场拦下 FLASHW 常量的 0x 十六进制字面量误判为颜色值，改十进制常量后通过）。**任务栏闪烁的 Windows 真机观感与 GUI 真机运行仍未验证**（Linux 无法运行 WinForms GUI）——「作业完成 → 任务栏闪烁 → 激活停止」只在代码与编译层面成立（事件语义与门控口径已在 Linux 真实 JobRunner 上执行）；Windows 真机需按「启动任一长作业（如定向构建）→ 切到别的应用 → 等待完成 → 核对任务栏闪烁 → 点回窗口核对闪烁停止、不弹窗不抢焦点」走一遍。未改写入安全语义（附加只读事件 + 纯提醒）；本轮未连接或写入 Apifox。

## 原生切片「设计基线落实与机械检查扩展：列表行选中态/首屏空态 Title 档/Esc/G-D4」（2026-11-07 第二十三轮）

- **接续前确认**：上一轮工作树干净，HEAD（ad2698d）的 Linux `dotnet build` 0 警告 0 错误、415 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。路由面对照确认：冻结页面的全部 REST 能力与方案 2.1 原生工作流要素（菜单/快捷键/文件选择/确认对话框/系统通知/多窗口）至此均已接入，本轮按设计基线（docs/DESIGN.md）与设计验收条款找缺口——找到三个「规格已写、代码未落」的真实缺口：列表行选中态、Title 档使用点、CapabilitiesDialog 的 Esc 键盘习惯，外加 G-D4 机械检查这一验收门槛尚未建立。
- **列表行选中态（设计基线 §6「不用系统高亮蓝」的落实）**：全部 18 处 ListView 此前都用 WinForms 默认渲染，选中行是系统高亮蓝，与基线「选中行底 `Bg` + 左侧 3px `Accent` 标记」直接相悖（D2「灰阶 + 恰一个强调色」的可见破口）。新共享工厂 `Ui.List`（`Desktop/Native/Ui.cs`）：OwnerDraw 自绘选中态（选中行 Bg 底 + 3px Accent 左标记、未选行 Panel 底、发丝边包裹沿用既有 Ui.Hairline），逐子项显式设置的语义色（Muted/Ok/Warn/Bad）保留——「颜色+文字」双通道不能因自绘丢失（CheckResults 差异风险、ApplyPlan 跳过原因、SyncPreview 排除原因等大量依赖此项）；表头 e.DrawDefault 沿用系统绘制（与既有外观零差异）；同步预览勾选列表（CheckBoxes=true）在自绘模式下系统不再画复选框，由工厂经 CheckBoxRenderer 补画（命中区仍在 ListView 原状态图标布局位置，对位细节挂账真机复核）；HideSelection 统一为 false（此前 17/18 处即此值，同步排除清单等 1 处顺势对齐）。18 处构造全部替换为 `Ui.List()`，机械检查断言列表只能经工厂创建——变异时发现目标类型 `new()` 写法不含 `new ListView` 字样会绕过首版检查，改为「ListView 字段赋值必须以 Ui.List( 开头」的精确断言后堵住。
- **颜色判定口径收口为单一落点**：`Desktop/Native/ListRows.cs`（不依赖 WinForms、与测试工程共享编译）——`MarkerWidth=3`（与导航选中标记同宽，规格同出 §6）与 `TextColor`（子项显式语义色优先、所在项语义色次之、其余一律正文色；系统回退色一律按正文处理）。绘制在 `Ui.List`，口径不在界面代码里。
- **首屏空态接 Title 档**：DESIGN.md §3 把 Title 档（28px）的使用点声明为「首屏大标题（如空态引导）」，但代码里从未使用——四档字号存在一个死级别，违反本文「无死级别、每级有使用点」。项目设置/检查任务两处首屏空态标题从 Section 升到 Title 档（新 `Ui.Title` 工厂，文案不动）。「每级有使用点」落成机械检查（四档工厂在界面代码至少各有一处调用），死级别回归当场变红。
- **CapabilitiesDialog 补 Esc**：D12 键盘习惯条款「对话框 Enter=确认、Esc=取消」——其余对话框全部经 AcceptButton/CancelButton 接线，连接与写入探测对话框是唯一键盘无路可出的对话框。补 ProcessDialogKey（Esc=关闭；与标题栏 × 同语义：探测作业在服务层继续执行、结果按 CLI 版本登记、重开可查）。
- **G-D4 机械检查建立（TypographyTests）**：方案 §6.2 #11/#12 与设计验收 D10/D11 中源码可判的条款落成断言——禁假斜体（全源码无 Italic）、中文引用只用「」（无弯引号，期间修正 NativeShellForm 注释里一处弯引号）、AI 腔禁词（赋能/闭环/一站式/无缝/智能/极致）、字体栈与 DESIGN.md §3 逐项同序（WorkSwitch 先例的有序回退）、字号下限 12px（档表最小值断言）、四档每级有使用点。行首行尾禁则、正文行长、中英文空隙属渲染期属性，源码判不了，如实挂账随 G-D5 真机复核。
- **测试 415 → 439 用例**：新增 `ListRowsTests`（10）与 `TypographyTests`（14）。**变异验证实测可红**（九处，各自独立变红、还原后全绿）：语义色项级回退删除（1 红）、OwnerDraw 接线删除（1 红）、选中标记绘制删除（1 红——期间发现原断言只查字符串存在未钉绘制，已加强为断言 FillRectangle(Accent, …, MarkerWidth, …) 本身）、语义色集合丢 WarnText（1 红）、Esc 接线删除（1 红）、工厂绕过（1 红，含 new() 盲区修复后重验）、Title 全无使用点（1 红——只还原一处时 TasksPage 仍在用，档位未死，测试正确地保持绿）、字体栈乱序（1 红）、文案禁词（1 红）。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 439 用例全部通过（含既有令牌接线机械检查对 Ui.cs/ListRows.cs 新增代码的扫描——无就地颜色/字体/间距值、无 FromArgb 绕色）。**列表自绘选中态的真机观感、复选框命中区对位、选中行行尾空白区底色仍未验证**（Linux 无法运行 WinForms GUI）——接线与口径已在 Linux 以 24 项新断言钉住；Windows 真机需按「项目设置/检查任务空态看 Title 档标题 → 选中列表行核对 Bg 底 + 左侧 3px 蓝标记（无系统高亮蓝）→ 查看检查结果核对语义色行仍在 → 同步预览勾选列表核对复选框显示与点击 → 连接与写入探测按 Esc 关闭」走一遍。未改写入安全语义（纯呈现层）；本轮未连接或写入 Apifox。


## 原生切片「下一步引导：任务列表列 + 详情横幅」（2026-11-07 第二十四轮）

- **接续前确认**：上一轮工作树干净，HEAD（a4ca77f）的 Linux `dotnet build` 0 警告 0 错误、439 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：方案 3.2 要求「任务按『源码注释 → 生成文档 → Apifox』三个阶段推进，界面始终给出唯一『下一步』」，冻结页面按此实现了 `nextStep()` 纯函数（任务列表「下一步」列 + 任务详情横幅 + 页签 dot 提示），原生界面此前只有各分区独立的状态行——任务选好后用户要自己判断先做哪一步，与方案 3.2 的工作流级要求有真实差距，本轮补上。
- **引导口径收口为单一落点**：`Desktop/Native/NextStep.cs`（不依赖 WinForms、与测试工程共享编译）——`Compute(CheckTask)` 与冻结页面 nextStep 同一规则同一文案：远端执行中/被中断/待确认最紧迫（同步行先于一切）；未检查按源码 → 生成文档 → Apifox 顺序引导（notStarted → 回写失败 → 计划就绪 → 生成文档待核对/部分完成 → 已接受候选生成计划 → 审阅候选 → 补缺口 → 离线导入 → 检查 Apifox）；已检查后源码侧回写失败、未应用补丁、生成文档未核对仍然优先（它们会改变比较目标），随后按远端状态引导（过期/版本落后 → 待确认 → 可预览有变化 → 部分完成/失败 → 离线完成 → 已核对 → 一致）。标题与说明文字承载含义，`Tone`（空/ok/warn/bad）只是辅助信号、由界面按设计令牌映射为文字色；「检查」按钮文案按 Swagger 冻结判定取「开始检查」或「加载 Swagger 并检查」；源码阶段的说明（如候选过期提示）作为引导文字前缀如实带出；服务给出的中断/失败消息原样透传，不替换成通用文案。动作以种类名（startScan/openApply/openSync/……共 11 种）表述，本文件不执行动作。
- **原生接线（TasksPage）**：任务详情顶部新增引导横幅（「下一步」标签 + 标题按语义色调着色 + 说明行 + 动作 ghost 按钮），动作映射到本页既有入口（开始扫描、规则提取候选、注释回写、核对传播、候选审阅、离线导入、开始检查、差异确认、同步预览、查看报告、查看检查结果）——只做引导不改变任何状态，写入类操作仍在各自区域与对话框确认；入口自身的门控与异常处理（状态行内联）不变，作业执行中横幅按钮与各区按钮同一纪律禁用（每屏实心主按钮仍只有「新建任务」，横幅动作为 ghost）。任务列表新增「下一步」列（在「配置」之后），标题与色调取同一口径——列表层就能看到哪个任务在等什么，不必逐个选中。任务页检查按钮文案与检查前置共用的 Swagger 冻结判定收敛为 `NextStep.NeedsSwaggerLoad` 单一口径（删除 TasksPage 私有重复实现，行为不变）。与冻结页面的两处有意差异（已记录）：原生详情页无页签，横幅动作不因「已在该页签」隐藏；「补全注释」的主动作「去生成草稿」映射为「规则提取候选」（原生无独立草稿页签，规则提取是不需要模型服务的默认生成路径，入口与本页既有按钮同一处理）。
- **测试 439 → 463 用例**：新增 `NextStepTests`（24：二十三条口径断言 + 一条接线机械检查——横幅/列表列由共享口径驱动、动作经映射调既有入口、作业中禁用、检查按钮与前置共用 NeedsSwaggerLoad 且私有重复实现已删）。测试工程共享编译新增 `Tasks/TaskModels.cs`（只依赖 BCL 与已共享的 ProjectProfile）。**变异验证实测可红**（六处，各自独立变红、还原后全绿）：删 CheckedRevision 落后的过期判定（1 红）、待确认色调 warn 改空（1 红）、CheckLabel 恒为「开始检查」（1 红）、待确认计数改静态文案（1 红）、删 executing 最优先臂（1 红）、删源码说明前缀（1 红——ReviewingWithoutAccepted 捕获）。期间两处断言预期误设均为脚本错误、实现与冻结页面一致（notStarted 优先于生成文档引导——夹具漏设 scanned；file 类型未冻结应为 True——断言写反），修正的是测试不是实现。
- **Linux 运行时验证（本轮新增）**：链路不触碰 WinForms，/tmp 一次性控制台链接纯逻辑源文件（Infrastructure 的 Core/FileStore、Profiles 的 ProjectProfile、Tasks 的 TaskModels、Desktop/Native/NextStep，镜像 GlobalUsings；不进仓库、已清理），在真实 FileStore 上按 TaskStore 同一读写路径落盘/回读 task.json（FileStore.WriteJson/ReadJson<CheckTask>，camelCase 真实形状），**21 项断言全部通过**：生命周期十站引导逐字（新任务、缺口 3、已接受 2、计划就绪、生成文档待核对、待确认计数「阻断 1 · 说明冲突 2 · 可同步变化 6」、CheckedRevision 落后按过期、可预览有变化、完成消息透传、离线导入与离线完成）、camelCase 落盘形状、NeedsSwaggerLoad 三态（未配置/离线/配置未冻结）。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 463 用例全部通过（含令牌接线机械检查对 TasksPage.cs 改动与 NextStep.cs 新文件的扫描）。**引导横幅与「下一步」列的 Windows 真机观感仍未验证**（Linux 无法运行 WinForms GUI）——口径与落盘链路已在 Linux 真实执行；Windows 真机需按「新建任务看横幅提示扫描 → 扫描后看缺口引导 → 接受候选看计划引导 → 回写后看核对引导 → 检查后看待确认引导 → 列表「下一步」列与选中任务横幅一致」走一遍。未改写入安全语义（纯引导，不改变任何状态）；本轮未连接或写入 Apifox，未调用任何模型服务。


## 原生切片「输入框形态：1px 发丝边 + 聚焦强调色描边」（2026-11-07 第二十五轮）

- **接续前确认**：上一轮工作树干净，HEAD（4f5e2a9）的 Linux `dotnet build` 0 警告 0 错误、463 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：方案 2.1 原生工作流要素与冻结页面 REST 能力此前已全部接入（第二十四轮确认），本轮按设计基线（docs/DESIGN.md）§6 控件形态逐条找缺口——「输入框：`Panel` 底 + 1px `Line`；聚焦描边 `Accent`」规格已写、代码未落：全部 35 处可编辑输入框（项目配置、新建任务、连接与写入探测、模型服务、模块与模板、序列化与分析、候选回答、手工填写说明）都用 WinForms 默认 3D 边框、聚焦无任何反馈，与第二十三轮修掉的「列表行系统高亮蓝」同类，是 §6 最后一个未落地的控件形态条款（菜单/按钮/状态行/列表行均已落地）。
- **口径收口为单一落点**：`Desktop/Native/FieldChrome.cs`（不依赖 WinForms、与测试工程共享编译，同 ListRows 先例）——`Border(focused, line, accent)`：聚焦描边 `Accent`、常态 `Line` 发丝边。绘制在 `Ui.Field` 工厂（`Desktop/Native/Ui.cs`）的 1px 描边面板（`FieldBox : Panel`，Padding(1) + Line 底色，与 HairlineCore 同法），聚焦经 `GotFocus`/`LostFocus` 重绘；内外尺寸双向同步（文本框改宽高时面板跟随、面板被行助手设宽设高时文本框跟随，重入护栏防互相触发），既有 `AddRow` 的 `field.Width = Math.Max(field.Width, 480)`、`Row(300, …)`、`_modules.Height = 72` 等布局代码零改动继续成立。
- **全部构造站点经工厂**：可编辑输入框 35 处改 `Ui.Input(...)`（单行、多行模块/封装/XML 路径编辑、mono JSON 编辑位、密码位）+ 放置处 `Ui.Field(...)` 包裹（原 3D 边框退出）；`ProfileAdvancedDialog` 的 `MultilineJson()` 局部助手并入工厂参数后删除。只读预览框（注释回写差异、构建输出、检查结果、契约/决策/传播详情、证据片段、报告预览、同步写入内容、缺口降级清单等 15 处）收敛为 `Ui.Preview(...)` 统一镀铬，沿用 `Ui.Hairline` 发丝边（只读无聚焦描边，非 §6 输入框）。机械检查断言：Desktop/Native 除 Ui.cs 外禁止任何 `new TextBox`/目标类型 `new()`，TextBox 声明必须以 `Ui.Input(`/`Ui.Preview(` 开头（同第二十三轮对 ListView 的精确断言，含目标类型 new() 盲区）；含 `Ui.Input(` 的文件必有 `Ui.Field(`（无边框输入框在 Bg 底上不可见）。
- **顺带修复一个真实缺陷**：注释回写对话框（ApplyPlanDialog）跳过位置清单把同一个 `ListViewItem` 加了两次（`Items.Add(skip.Display)` 已加入列表、行尾又 `Items.Add(item)`），每条跳过原因会显示两行；改为 `new ListViewItem(...)` 构造后加入一次（与上方文件清单循环同型）。
- **如实挂账**：「校验失败内联 `BadText` 文字 + 描边 `BadText`」中的描边部分未落地——校验失败字段定位在共享解析单一口径内（`GenerationEntries.ParseFields`、`ProfileAdvanced.ParseFields` 等以逐字文案抛 `UserException`，对话框拿不到失败字段；改共享解析签名会动 Web 路由共用的口径），逐字段描红需要一条字段定位链路，另切片接入；当前校验失败仍为状态行内联文字（这一半已在全域成立）。ComboBox 下拉与 CheckedListBox 不在 §6 输入框范围（WinForms 自定义边框需自绘或 OwnerDraw，无真机验证不冒险），DESIGN.md §8 记录。
- **测试 463 → 472 用例**：新增 `FieldChromeTests`（9：聚焦/常态两向口径断言、DESIGN.md §6 条款在文断言、工厂绕过检查、输入框必有 Field 包裹检查、聚焦接线/镀铬/尺寸同步源码断言、Preview ReadOnly 断言、关键对话框放置点抽查、只读框 Hairline 抽查）。**变异验证实测可红**（五处，各自独立变红、还原后全绿，还原一律手工逆编辑——对已改动文件用 git checkout 会冲掉本轮切片）：FieldChrome 聚焦优先级翻转（2 红：聚焦/常态两向）、删 GotFocus 接线（1 红）、对话框加裸 `new TextBox()`（1 红）、放置点去掉 `Ui.Field(`（1 红）、Preview 丢 `ReadOnly = true`（1 红）。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 472 用例全部通过（含既有令牌接线机械检查对新增代码的扫描——无就地颜色/字体/间距值，Padding(1) 发丝边为 §5 允许值）。本轮无纯逻辑服务链路新增（切片是呈现层镀铬），FieldChrome 两向口径已由单元断言钉住，无需 /tmp 控制台运行时验证。**发丝边常态观感、聚焦描边出现与消失、尺寸同步在 100%/200% DPI 的真机观感仍未验证**（Linux 无法运行 WinForms GUI）——接线与口径已在 Linux 以断言钉住；Windows 真机需按「项目设置 → 编辑配置：输入框为 1px 浅灰发丝边（无 3D 凹边）→ 点入输入框看描边变强调色蓝 → Tab 离开描边复原 → 模块与模板：拖宽窗口/看多行 JSON 编辑位与模块编辑框高度正常 → 注释回写：跳过位置清单每条只显示一行」走一遍。未改写入安全语义（纯呈现层）；本轮未连接或写入 Apifox，未调用任何模型服务。


## 原生切片「校验失败描边：字段定位链路」（2026-11-07 第二十六轮）

- **接续前确认**：上一轮工作树干净，HEAD（4403587）的 Linux `dotnet build` 0 警告 0 错误、472 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：上一轮（第二十五轮）挂账明确「校验失败描边 `BadText`」需一条字段定位链路——共享解析单一口径（`GenerationEntries.ParseFields`、`ProfileAdvanced.ParseFields`、`ProfileAnalysis` 各解析方法）以逐字文案抛 `UserException`，对话框取不到失败字段；本轮即接这条链路（设计基线 §6「校验失败内联 `BadText` 文字 + 描边 `BadText`，不弹窗」的文字一半已全域成立，本轮补形状一半）。
- **字段定位链路（非破坏）**：`UserException` 增加可选 `Field` 属性（配置 JSON 的 camelCase 属性名，默认 null）——既有构造与调用点零改动，REST 错误响应只取 `Message`（`Api/ErrorHandling.cs`），网络 JSON 形状不变；三个共享解析文件在拒绝路径以命名参数附加字段标识，**文案逐字不变**：`GenerationEntries`（`timeoutSeconds`/`maxSlotsPerRequest`）、`ProfileAdvanced`（`folderId`/`auth`/`securityScheme`/`headerParameters`，`ParseObject`/`ParseArray` 增加 field 参数）、`ProfileAnalysis`（`maxTraceDepth`/`maxMethodsPerEndpoint`/`xmlDocPaths`/`readConcurrency`）；字段标识以公共常量单一落点（对话框、测试共用，不复制字符串）。
- **描边口径**：`FieldChrome.Border(focused, invalid, line, accent, bad)` 三态判定，**失败 > 聚焦 > 常态**——聚焦失败字段正是要修它的时刻，错误信号不能被聚焦色盖掉；`Ui.FieldBox.SetInvalid(bool)` 置位/解除，内层 `TextChanged` 自动解除（用户改值后旧判定不再适用；文字状态行由对话框自行管理）；「颜色 + 文字」双通道保持：描红是形状通道，说明文字仍在状态行。
- **对话框接入（共享解析口径覆盖面）**：「模型服务」「模块与模板」「序列化与分析」三个配置对话框把会解析失败的输入框经本地 `Field(input, fieldId)` 助手登记（标识 → `FieldBox`），解析失败 catch 中 `MarkInvalidField(ex)` 按 `UserException.Field` 描红对应输入框；不会解析失败的字段（服务地址/模型/编译常量/封装行/node 路径等）维持原 `Ui.Field` 直包。`CapabilitiesDialog` 的探测输入走 `CanRun` 布尔门控（按钮禁用、无抛出路径）、候选/手工说明走 `DraftService.ValidateText`（单输入框、文案就在状态行），均无字段歧义，不属本轮口径面。
- **测试 472 → 475 用例**：`FieldChromeTests` +3（失败描边两态——聚焦与否都是 BadText、三对话框描红接线机械检查、`ProfileAnalysis` 字段标识即 JSON 属性名），既有拒绝文案用例全部补 `Field` 断言（消息与字段标识双重钉住）。**变异验证五处实测可红**（各自独立变红、还原后全绿，还原一律手工逆编辑）：FieldChrome 失败/聚焦优先级翻转（1 红）；`Ui.FieldBox` 改值解除接线删除（1 红）；`GenerationEntries` 丢弃字段标识（2 红：超时/每批）；GenerationDialog 描红调用删除（1 红）；ProfileAdvancedDialog 字段登记删除（1 红）。**期间变异抓出两个机械检查盲区并修复**：接线断言用子串 `Contains` 时被注释行骗过（`// inner.TextChanged += ...` 仍含子串）——`Ui.cs` 改值解除断言与三对话框描红调用断言改为行首正则（注释行不再命中）。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 475 用例全部通过（含令牌接线机械检查对新增代码的扫描——无就地颜色/字体/间距值）。共享解析链路（拒绝文案 + 字段标识）由测试工程直接编译生产源码断言（非副本），无需 /tmp 控制台另验；对话框描红的呈现与改值解除是 WinForms 行为，Linux 无法运行 GUI。**失败描边出现/解除、失败 > 聚焦的优先级、改值自动解除的真机观感仍未验证**（G-D5 复核）——Windows 真机需按「模型服务：超时秒数填 abc → 确定看状态行文案 + 超时秒数框描红、其余框不红 → 改成 90 后描红即消失 → 确定；模块与模板：鉴权 auth 填 {bad → 确定看该框描红 → 从接口提取回填合法 JSON 后描红消失」走一遍。未改写入安全语义（`UserException.Field` 为可选元数据，服务层校验与门禁一行未动；REST JSON 形状不变）；本轮未连接或写入 Apifox，未调用任何模型服务。


## 原生切片「校验失败描边：外层保存的核心字段定位」（2026-11-07 第二十七轮）

- **接续前确认**：上一轮工作树干净，HEAD（38c699c）的 Linux `dotnet build` 0 警告 0 错误、475 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：第二十六轮挂账明确「`ProfileStore` 保存校验（核心字段与越界钳制）尚无字段标识，对话框主体与外层「保存」的失败仍为状态行文字」——用户在外层「保存」被拒时（空名称、相对路径工作区、非 .csproj、回写范围越界、Swagger 位置缺失等最常见的一批失败）不知道该修哪个框，本轮把第二十六轮建立的字段定位链路延伸到外层「保存」。
- **字段定位链路延伸（非破坏）**：`ProfileStore.Validate` 全部拒绝路径以命名参数附加字段标识，**文案逐字不变**；标识为公共常量单一落点（`ProfileStore.FieldName/FieldWorkspaceRoot/FieldApiProject/FieldAllowedSourcePaths/FieldProjectId/FieldSwaggerType/FieldSwaggerLocation/FieldGenerationMode/FieldBaseUrl/FieldModel/FieldNamingPolicy`，取配置 JSON 的 camelCase 属性名，对话框与测试共用不复制字符串）。REST 错误响应只取 `Message`（`Api/ErrorHandling.cs`），`Field` 不进入网络形状；既有调用点零改动。防御性分支（Swagger 类型/生成方式无效——界面路径不可达，仅配置文件损坏时出现）同样带出字段标识：规则全体一致（每个拒绝都指明出错字段），对话框未登记该标识时查找自然落空、只显示状态行。
- **对话框接入（外层「保存」）**：`ProfileDialog` 按第二十六轮同一模式登记字段标识——四个可被保存校验拒绝的镀铬文本框（名称 → `name`、仓库根目录 → `workspaceRoot`、允许回写范围 → `allowedSourcePaths`、Swagger 位置 → `location`）；`Save()` 的 `UserException` catch 中 `MarkInvalidField(ex)` 描红对应输入框，文字仍在状态行（双通道不变），改值自动解除（`FieldBox` 既有接线）。API 项目与 Apifox 目标是 ComboBox，不在 §6 输入框范围（第二十五轮起的挂账继续），失败只显示状态行文字。
- **测试 475 → 495 用例**：新增 `ProfileStoreTests`（19）与 `SaveDialog_MarksFailedCoreFields` 接线机械检查（1）。`ProfileStoreTests` 在真实 `FileStore`/`ProfileStore` 上执行（临时目录、真实工作区与 .csproj 夹具）：字段标识即配置 JSON 属性名（十一常量逐一）、十二条拒绝路径的文案逐字 + 字段标识双重断言、被拒不落半份状态（profiles 目录无残留文件）、合法保存钳制落盘并可重读（钳制仍由 `Normalize` 单一口径，10–600/1–8/0–6 各验一值）。**保存校验文案此前只在 /tmp 一次性脚本中断言（第十六、十八轮运行时验证），本轮起成为仓内常驻用例**；`ProfileStore`/`FileStore` 以共享源码编入测试工程（两者只依赖 BCL 与已共享的 `Core.cs`，Linux 可执行——链路验证不再依赖一次性工程）。**变异验证五处实测可红**（各自独立变红、还原后全绿，还原一律手工逆编辑）：ProfileStore 丢弃 name 字段标识（1 红）、丢弃 Swagger 位置字段标识（1 红）、ProjectsPage 删除 Save 内描红调用（1 红——锚定在 Save 方法体内的行首正则捕获）、注销允许回写范围的字段登记（1 红）、字段标识常量改值不再是配置属性名（1 红）。期间修正测试自身一处 `Repo.Source` 返回值误用（编译期即暴露）。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 495 用例全部通过（含令牌接线机械检查对 ProjectsPage.cs/ProfileStore.cs 改动的扫描）。保存校验拒绝路径（文案 + 字段标识 + 无半份状态）在 Linux 真实文件系统上执行；描红的呈现与改值解除是 WinForms 行为，**失败描红出现/解除、失败 > 聚焦优先级的 Windows 真机观感仍未验证**（G-D5 复核）——Windows 真机需按「新建配置：名称留空直接保存 → 状态行文案 + 名称框描红 → 填入名称后描红消失；仓库根目录填相对路径保存 → 该框描红；Swagger 来源选文件、位置留空保存 → 位置框描红」走一遍。未改写入安全语义（字段标识为可选元数据，`Validate`/`Normalize` 校验逻辑与门禁一行未动；REST JSON 形状不变）；本轮未连接或写入 Apifox，未调用任何模型服务。


## 原生切片「新建任务的指定接口与版本备注」（2026-11-07 第二十八轮）

- **接续前确认**：上一轮工作树干净，HEAD（164b3c5）的 Linux `dotnet build` 0 警告 0 错误、495 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：方案 3.2「新建任务：选择配置与模块」与冻结页面 newTask 表单（`Web/app.js` 的 `endpointKeys: lines(f.keys.value)`、`sourceVersionNote: f.note.value`）对照原生 `NewTaskDialog`——原生只有模块与路径前缀两个范围维度，**「指定接口」（`endpointKeys`，每行一个「GET /路径」，与路径前缀取并集——按接口身份精确圈定扫描范围）与「版本备注」（任务级 `sourceVersionNote`，留空回退配置级备注，报告「未绑定代码版本」提示的数据来源）两项输入在原生缺失**，原生用户无法按接口圈定任务范围；本轮补上。
- **共享口径（唯一落点）**：新增 `Desktop/Native/TaskCreation.cs`（不依赖 WinForms，与测试工程共享编译）——`LineValues`（按 \n 分行、逐行去空白含行尾 \r、丢弃空行，与冻结页面 `lines()` 同一结果）、`VersionNote`（去空白，空白交 null——由 `TaskStore.Create` 回退到配置级备注）、`KeysHint`/`NotePlaceholder`（与冻结页面输入框占位文字同一字面）、`NoteFallbackHint`（「留空则用项目配置里的版本备注。」——回退语义在 TaskStore，说明文字如实交代）；「GET /路径」的规整（方法大写、路径补斜杠）、非法行丢弃与去重仍在 `TaskStore.Create` 单一口径，原生层不复制。
- **对话框接入**：`NewTaskDialog` 新增多行「指定接口」输入（`Ui.Input(multiline: true)` + 垂直滚动条，内层定高 62 由 `Ui.Field` 尺寸双向同步带着描边面板变高）与「版本备注」输入（占位文字），字段顺序与冻结页面一致（模块 → 路径前缀 → 指定接口 → 版本备注 → 离线模式），两个输入框都经工厂出厂并包 1px 描边（DESIGN.md §6）；`Confirm` 的 `CreateTaskRequest` 增设 `EndpointKeys = TaskCreation.LineValues(...)` 与 `SourceVersionNote = TaskCreation.VersionNote(...)`。期间踩坑一次并记录：字段初始化器里 `Ui.Input(...) { Height = 62 }` 不是合法 C#——对象初始化器只能跟在 `new` 表达式后，不能跟在方法调用结果后（CS1003），改在构造函数里设高。
- **测试 495 → 508 用例**：`TaskStore` 以共享源码编入测试工程（只依赖 BCL 与已共享的 Core/FileStore/ProfileStore——沿用第二十七轮「链路验证不依赖 /tmp 一次性工程」的先例），新增 `TaskCreationTests`（13）：`LineValues` 混合换行/空行/首尾空白、空白文本空清单；`VersionNote` 去空白与空白交 null；提示文字与冻结页面占位逐字一致；**真实 `FileStore`/`ProfileStore`/`TaskStore` 链路**（临时目录、真实工作区与 .csproj 夹具）——规整（小写方法、缺斜杠、非法行、重复行）、任务级备注空白回退配置值与有值覆盖、落盘后重读逐值一致、未知模块拒绝文案逐字、`InScope` 范围语义（接口身份按原大小写精确匹配——`get /api/users` 不匹配 `GET /api/users`；指定接口与路径前缀取并集，前缀忽略大小写；两者皆空为全项目——**范围判定语义此前无仓内用例**）；对话框接线机械检查（行首正则——提交口径经共享 TaskCreation、两输入框行标签与 `Ui.Field` 包裹、提示与占位取共享常量不复制字面）。
- **变异验证六处实测可红**（各自独立变红、还原后全绿，还原一律手工逆编辑）：`LineValues` 去掉空行过滤（2 红）、`VersionNote` 空白返回空串而非 null（1 红——注意 TaskStore 的回退判 `IsNullOrWhiteSpace`，空串也回退，只有直接断言 null 的用例能抓住）、`TaskStore` 规整去掉方法大写（1 红）、对话框 `EndpointKeys` 接线注释掉（1 红——行首正则不被注释行骗过）、`KeysHint` 字面改写（1 红）、`InScope` 接口身份改忽略大小写（1 红——恢复时 sed 的逆模式没写对，残留由 git diff 揪出手工修正，验证「还原必须核对 diff」的纪律）。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 508 用例全部通过（含令牌接线机械检查对 TasksPage.cs/TaskCreation.cs 改动的扫描——无就地颜色/字体/间距值）。任务创建链路（规整/去重/备注回退/范围判定/落盘重读）在 Linux 真实文件系统上执行。**两个新输入框的布局与描边在 Windows 真机的观感仍未验证**（G-D5 复核）——Windows 真机需按「新建任务：对话框高度正常无裁切 → 指定接口填「get api/users」与非法行 → 创建后任务扫描范围只含该接口 → 版本备注留空时报告回退配置级备注」走一遍。未改写入安全语义（建任务的规整与回退在 `TaskStore.Create` 单一口径一行未动，原生层只收集文本）；本轮未连接或写入 Apifox，未调用任何模型服务。


## 原生切片「壳键盘快捷键：方案 2.1 原生工作流要素收口」（2026-11-07 第二十九轮）

- **接续前确认**：上一轮工作树干净，HEAD（5766a49）的 Linux `dotnet build` 0 警告 0 错误、508 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：方案 2.1 列举的原生工作流要素——原生窗口与菜单、键盘快捷键、原生文件/目录选择、原生确认对话框、系统通知、多窗口协作——原生壳已有菜单（Alt 助记符）、原生文件/目录选择、原生确认、任务栏闪烁（AttentionCue）、对话框即窗口，但主窗口没有任何全局快捷键（只有菜单助记符与对话框 Enter/Esc），「键盘快捷键」是清单最后一个未落地项，设计验收 D12「快捷键可用」也没有落点。本轮补上，2.1 原生工作流要素至此全部落地。
- **手势只取 Windows 惯例**：Ctrl+1 项目设置、Ctrl+2 检查任务（浏览器与 IDE 的页签惯例）、F5 刷新当前页（与两页已有「刷新」按钮同一口径）、F1 关于（帮助菜单唯一入口，F1=帮助的系统惯例）。不给「退出」（系统 Alt+F4 是惯例，不再发明 Ctrl+Q）与「打开资料目录/日志目录」发明手势（保留菜单助记符 Alt+F 后 D/L），如实挂账；壳级快捷键不占用文本框编辑键——保留清单 Ctrl+C/V/X/A/Z/Y 声明在共享口径内并由测试钉住。模态对话框打开期间主窗体菜单不路由（WinForms 模态语义），壳级快捷键不会穿透对话框，无需额外代码。
- **共享口径（唯一落点）**：新增 `Desktop/Native/ShellShortcuts.cs`（不依赖 WinForms，与测试工程共享编译）——`Entry`（Action 动作 id + Gesture 手势文字）、四个具名条目与 `All` 全集、`GestureOf` 查找（未知动作返回 null）、`ReservedEditingKeys` 编辑键保留清单；规则是「新增快捷键必须在此登记，界面代码不就地写手势」。壳经 `KeysConverter.ConvertFromString` 把手势文字接到菜单项 `ShortcutKeys`（绑定值与菜单自动显示的提示取同一来源，不会漂移）；动作 id 由壳分发到既有入口，本文件不执行动作。**运行时解析已核对 .NET 8 官方源码确认**：KeysConverter 显式注册 `"1"`→`Keys.D1`（0–9 全部，注释写明用于菜单快捷键显示）与 `"Ctrl"`→`Keys.Control`、F1–F12 逐键注册——「Ctrl+1」「F5」「F1」解析确定，不存在「Linux 编译通过、真机启动即崩」的隐患。
- **壳接线**：新增「视图(&V)」菜单——项目设置(&P)（Ctrl+1）、检查任务(&T)（Ctrl+2）、刷新当前页(&R)（F5）；「关于 Apifox 助手」绑 F1。侧栏按钮与菜单项收敛为 `ShowProjects`/`ShowTasks` 单一分发（切页口径——目标页面 + 选中标记——不出现第二份）；`RefreshVisiblePage` 只刷可见页、经页面协议新成员 `INativePage.RefreshData()`（两页「刷新」按钮同步改走 RefreshData，刷新路径单一；对话框关闭等内部重载仍走各自 LoadTasks/LoadProfiles，与刷新入口无关）；页面尚未创建（启动中）、退出已开始、窗口已释放时不动作。
- **测试 508 → 515 用例**：新增 `ShellShortcutsTests`（7）：`All` 四条目、动作 id 逐值且唯一；四手势逐字（Ctrl+1/Ctrl+2/F5/F1）；`GestureOf` 查找与未知 null；手势不占编辑键（保留清单）；三条接线机械检查（行首正则，不被注释行骗过）——ShortcutKeys 只能取 `entry.Gesture` 且四个菜单项经 `ShellShortcuts.Projects/Tasks/Refresh/About` 具名条目、侧栏与菜单共用 ShowProjects/ShowTasks 且刷新只刷可见页、`INativePage` 声明 RefreshData 且两页实现并让「刷新」按钮走它。**变异验证六处实测可红**（各自独立变红、还原后全绿，还原一律手工逆编辑）：共享口径 F5 改 F6（2 红）、ShortcutKeys 绑定行注释（1 红）、ShowTasks 导航标发改错（1 红）、刷新改调 OnPageShown 而非 RefreshData（1 红）、任务页刷新按钮回退直调 LoadTasks（1 红）、切页手势改编辑键 Ctrl+C（2 红）。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 515 用例全部通过（含令牌接线机械检查对 NativeShellForm.cs/ShellShortcuts.cs 改动的扫描——无就地颜色/字体/间距值）。快捷键的实际按键路由、菜单内手势提示的呈现是 WinForms 运行时行为，**Linux 无法运行 GUI：「视图」菜单与四个快捷键的 Windows 真机观感仍未验证**（G-D5 复核）——Windows 真机需按「Ctrl+1/Ctrl+2 在两页间切换且侧栏选中标记跟随 → F5 重读当前页数据 → 打开任一模态对话框按键不触发壳动作 → 视图菜单内可见 Ctrl+1/Ctrl+2/F5 提示 → F1 打开关于 → 文本框内 Ctrl+C/V 复制粘贴不受影响」走一遍。未改写入安全语义（纯入口接线：切页只改变可见页、刷新只重读列表数据，均与既有按钮同一口径，不改变任何任务状态）；本轮未连接或写入 Apifox，未调用任何模型服务。


## 原生切片「默认入口：原生桌面客户端成为缺省入口」（2026-11-07 第三十轮）

- **接续前确认**：上一轮工作树干净，HEAD（4d635a1）的 Linux `dotnet build` 0 警告 0 错误、515 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：二十九轮原生切片（F01–F15 全部主工作流）与方案 2.1 原生工作流要素全部落地后，`ApifoxHelper.exe` 不带参数仍打开冻结的 WebView2 过渡壳（内嵌网页），原生客户端要显式 `--native`——默认用户路径仍先经过网页，与产品决定「不继续依赖原 WebView2 内嵌网页作为主交互」（方案 1.4「桌面客户端是必选主交付，先行」）相悖；本轮翻转默认入口。
- **翻转口径**：默认（无参数）启动原生壳 `NativeShellForm`；`--web` 显式回退过渡壳 `MainForm`（内嵌冻结页面，页面层冻结不变、只修阻断性缺陷——G-D6 的最终退场仍待 G-D5 真机复核后执行，本轮不删除任何页面代码）；`--native` 保留为兼容写法（旧快捷方式/脚本不受影响，行为与默认相同）；`--headless` 优先级不变（本地服务保留为实现能力，其「再次启动激活窗口时打开页面」的行为同前）。两壳共用同一单实例锁、资料目录与服务生命周期（各自 `StartAsync`），第二个实例无论带什么参数都只激活已运行窗口——切换默认不改变任何服务行为与写入安全语义。
- **共享口径（唯一落点）**：`LaunchOptions` 原样提取出 `Desktop/DesktopApp.cs` 到 `Desktop/LaunchOptions.cs`（无 WinForms 依赖，与测试工程单一编译——沿用各轮「链路验证不依赖 /tmp 一次性工程」的先例），记录翻转为 `Web` 标记（原 `Native` 标记删除，仅一处使用点随分支翻转同改）；`Usage` 文案说明默认与回退（「默认启动原生桌面客户端；--web 打开冻结的过渡壳（内嵌网页，仅供回退）。」）；`--no-browser` 旧别名保留。
- **接线**：`DesktopApp.Run` 分支顺序 headless → `--web` 过渡壳 → 原生默认，过渡壳注释如实标注「只按 --web 显式回退，不再作为默认交互（方案 2.4）」。README 的入口说明、原生客户端能力清单（此前停留在前七轮的「尚未接入」表述已过时）、WebView2 运行时要求（仅 `--web` 过渡壳需要）与命令行参数节同步更新。
- **测试 515 → 528 用例**：`LaunchOptions.cs` 以共享源码编入测试工程，新增 `LaunchOptionsTests`（13）：默认选原生（Web/Headless 均假、端口 5179、资料目录取环境变量全路径）、`--web` 回退、`--native` 兼容不改行为、`--headless --web` 组合两标记如实记录（优先级由接线检查钉住）、`--no-browser` 别名、端口解析与越界/非数字拒绝文案逐字、`--data` 覆盖环境变量并解析为全路径、环境变量未设回退默认目录、未知参数与缺取值拒绝文案逐字、`Usage` 文案三条、两条接线机械检查（行首正则——`DesktopApp.cs` 分支顺序 headless → web → 原生默认、过渡壳只在 web 分支内、`options.Native` 不再存在；`LaunchOptions.cs` 的 Web 标记与 `--web`/`--native`/`--no-browser` 分支保留）。
- **变异验证四处实测可红**（各自独立变红、还原后全绿，还原一律手工逆编辑并核对 diff）：DesktopApp 两壳创建互换（Entry 接线检查红）、`--web` 分支失效不置位（2 红）、`--native` 兼容分支删除（2 红）、`--web` 分支挪到 headless 之前（Entry 顺序断言红）。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 528 用例全部通过（含令牌接线机械检查对改动文件的扫描——无就地颜色/字体/间距值）。默认入口的实际启动画面与窗口行为是 WinForms 运行时行为，**Linux 无法运行 GUI：双击 exe 直启原生客户端的 Windows 真机验证仍未做**（G-D5 复核）——Windows 真机需按「双击 exe 直接进入原生窗口（不出现 WebView2 页面）→ `--web` 仍能打开过渡壳 → 旧 `--native` 快捷方式行为不变 → 第二次双击激活已运行窗口」走一遍。未改写入安全语义（纯入口选择：两壳的服务层调用路径一行未动）；本轮未连接或写入 Apifox，未调用任何模型服务。


## 原生切片「任务详情可滚动：长内容区宿主」（2026-11-07 第三十一轮）

- **接续前确认**：上一轮工作树干净，HEAD（c03aac4）的 Linux `dotnet build` 0 警告 0 错误、528 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：三十轮切片后 F01–F15 主工作流、方案 2.1 原生工作流要素与设计基线 §6 控件形态（除 DESIGN.md §8 有意挂账项）均已落地，本轮按「每个分区的内容与动作都可达」复核主工作流界面，发现一个真实缺陷：**任务详情网格（九个分区——源码注释/生成文档/响应契约/Apifox 差异与同步/报告与交接及其全部动作按钮）直接停靠在分割面板第二格上，该面板未开滚动——详情实际高度（长提示文字在半栏宽度大量折行）远超可视高度，底部内容被裁掉且无滚动条可达**：同步预览、确认同步、补充说明、查看报告与导出等后半程动作在正常窗口尺寸下操作不到。配置对话框与补充说明编辑区此前已有 AutoScroll 宿主先例，唯独主工作流的详情区没有（全部三十轮 GUI 均未真机运行，此类布局缺陷只有逐屏对照才能发现）。
- **原生交互推进一小步（具名 pass：layout）**：
  - 新共享工厂 `Ui.ScrollHost`（`Desktop/Native/Ui.cs`）：`AutoScroll` 面板（Bg 底）+ 点击内容任意处把焦点交给宿主 + `TabStop = false` 不改变 Tab 序。取焦对子树递归代挂——内容盖住宿主可视面时点击落在子控件上，宿主自己的 MouseDown 收不到，只挂宿主一层在溢出场景（正是需要滚动的场景）是死代码；按钮与输入框（含复选框等 ButtonBase）点击自带焦点、滚轮事件也沿父链传播到宿主，不代劳。
  - 任务详情网格放入该宿主（`_detailScroll`），可见性切换作用于宿主（空态提示标签不变）；详情网格保持 `Dock = Top + AutoSize`——滚动范围来自内容自身高度，机械检查钉住防改成 `Dock = Fill` 退回不可滚动。
  - 「补充说明」编辑区宿主（`_editorsScroll`，第二十五轮前就有的 AutoScroll 面板）收敛为同一工厂——AutoScroll 与取焦口径单一落点；Dock 因「对象初始化器不能跟在方法调用后（CS1003，第二十八轮教训）」在构造函数补设。
- **测试 528 → 533 用例**：新增 `ScrollHostTests`（5）：工厂 AutoScroll 与 Tab 序、点击取焦接线（宿主 OnMouseDown + 子树递归代挂 + ButtonBase/TextBoxBase 豁免，全部行首正则——被注释掉也算删除）、DESIGN.md §6「长内容区」条款在文、任务详情宿主接线（字段经工厂出厂、宿主入 Panel2、旧直连停靠写法不存在、可见性切换作用于宿主且 `_detail.Visible` 直改不存在、详情网格保持 Dock=Top + AutoSize）、补充编辑区同工厂。**变异验证七处实测可红**（各自独立变红、还原后全绿，还原一律以切片后基准副本覆盖并核对 diff）：删 `AutoScroll`、注释子树取焦挂接、注释 `ControlAdded` 代挂接线、删递归下钻（孙层不再取焦）、任务详情回退直连停靠、可见性切换回退详情面板、编辑区宿主回退就地面板。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 533 用例全部通过（含令牌接线机械检查对 Ui.cs/TasksPage.cs/SupplementsDialog.cs 改动的扫描——无就地颜色/字体/间距值）。滚动条拖动与点击内容取焦后滚轮滚动是 WinForms 确定行为（源码与官方文档核对）；**滚轮悬停滚动（焦点在别处时）、键盘滚动、取焦代挂与滚动条的真机观感仍未验证**（Linux 无法运行 WinForms GUI，G-D5 复核）——Windows 真机需按「选中一个已扫描任务 → 详情区内滚动到「报告与交接」区 → 核对同步预览、确认同步、查看报告等底部按钮全部可达可点 → 在详情文字上点一下再滚滚轮 → 核对详情区随之滚动、任务列表不随之滚动 → 补充说明对话框内同样核对」走一遍。未改写入安全语义（纯呈现层：详情内容与全部动作按钮的门禁、确认链路一行未动）；本轮未连接或写入 Apifox，未调用任何模型服务。


## 原生切片「对话框长内容区收口」（2026-11-07 第三十二轮）

- **接续前确认**：上一轮工作树干净，HEAD（ffd7c79）的 Linux `dotnet build` 0 错误、533 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。**基线带伤**：HEAD 编译有 2 个 CS8604 可空警告（`Ui.cs` 的 ScrollPanel 取焦挂接把可空的 `e.Control` 直接传入非空参数——第三十一轮「0 警告」记录与本机 SDK 10.0.402 实测不符，如实修正），本轮顺带修复。缺口对照：三十一轮把任务详情与补充说明编辑区收进 `Ui.ScrollHost` 后，逐屏复核其余固定尺寸对话框发现同类真实缺陷——**配置编辑对话框（ProjectsPage 的 ProfileDialog，680×720）与新建任务对话框（TasksPage 的 NewTaskDialog，560×560）的网格直接停靠窗体、无任何滚动**：按设计基线四档字号与间距估算，配置编辑十七行（核心字段与提示行、底部「模块与模板/序列化与分析/模型服务/导出配置」四个子配置入口）内容高约 690px，超过按钮与状态行之外约 640px 的可视高度；新建任务十二行（模块清单 96 高、指定接口 62 高、版本备注与提示、底部离线模式）内容高约 560px，超过约 486px 的可视高度——底部行被裁掉且 FixedDialog 不可拉高补救，新建配置的「导出配置/模型服务」入口与新建任务的「离线模式」勾选在正常 DPI 下不可达（三十二轮 GUI 均未真机运行，此类布局缺陷只有逐屏估算对照才能发现，与三十一轮同类）。另有口径不一致：「模型服务/模块与模板/序列化与分析」三个配置子对话框在 TableLayoutPanel 上就地开 `AutoScroll = true`（内容可达但没有点击取焦），DESIGN.md §6「长内容区宿主一律用 `Ui.ScrollHost` 工厂」并未为真。
- **原生交互推进（具名 pass：layout，纯呈现层）**：
  - 五个对话框的网格统一入 `Ui.ScrollHost`：网格改 `Dock = Top + AutoSize`（滚动范围来自内容自身高度），外包宿主（`Dock = Fill`）；宿主底色设为 `Panel` 与对话框一致——配置编辑与新建任务的网格底色从控件默认灰对齐到 `Panel` 令牌（§2「面板、输入框、列表底色」），三个子对话框维持原 `Panel` 不变。
  - 三处裸 `AutoScroll` 删除：`AutoScroll` 与点击取焦口径收敛回工厂唯一落点。
  - 修复 HEAD 遗留的 2 个 CS8604：取焦挂接对 `e.Control` 判空后再递归（事件源不保证非空）；既有 `Host_TakesFocusOnClickAnywhere` 断言同步更新并补 `control.ControlAdded` 子树挂接断言（该行此前从未被断言）。
  - 修正两处过时注释（第三十/三十一轮落地后已不成立）：原生壳头注「工作流按迭代逐切片接入……未接入处如实显示禁用态」改为「主工作流全程在原生窗口完成；本壳是默认入口，过渡壳只按 --web 显式回退」；检查结果对话框「同步预览不在本切片」改为指向任务页「同步预览」入口的实际分工。
- **测试 533 → 535 用例**：`ScrollHostTests` 新增两条接线机械检查（行首正则，不被注释行骗过）——`ProfileAndNewTaskDialogs_Grids_UseScrollHost`（两对话框：宿主经工厂出厂、宿主底色 Panel、网格入宿主、旧直连 `Controls.Add(grid)` 写法不得回归、网格保持 `Dock = Top + AutoSize`）与 `ConfigDialogs_Grids_UseScrollHostFactoryOnly`（三个子对话框同上，且 `AutoScroll` 语句不得出现在对话框文件——口径只在工厂内，工厂自身的 `AutoScroll = true` 仍在）。**变异验证五处实测可红**（各自独立变红、还原后全绿，还原一律手工逆编辑并核对 diff）：配置编辑直连回退（1 红）、新建任务网格 Dock=Fill 回退（1 红）、模型服务恢复裸 AutoScroll+Fill（1 红）、取焦守卫删除改 `e.Control!`（1 红——编译器警告被抑制，机械检查是唯一守卫）、宿主底色行删除（1 红）。期间一次还原把 TasksPage 注释行写重，由 `git diff` 揪出手工修正（「还原必须核对 diff」纪律再次生效）。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 **0 警告 0 错误**（警告清零）；`ApifoxHelper.Tests` 535 用例全部通过（含令牌接线机械检查对五个对话框改动的扫描——无就地颜色/字体/间距值，宿主底色取 `Panel` 令牌）。溢出估算基于设计基线四档字号与间距推算（内容高约为可视高度的 1.07–1.15 倍），**滚动可达性、滚轮取焦与滚动条的真机观感仍未验证**（Linux 无法运行 WinForms GUI，G-D5 复核）——Windows 真机需按「新建配置：滚动到底部，「模块与模板/序列化与分析/模型服务/导出配置」四个入口全部可见可点 → 新建任务：滚动到底部，「离线模式」可见可勾选、版本备注提示完整 → 三个配置子对话框滚动到底部提示行可见 → 在网格标签上点击后滚滚轮，网格随之滚动而按钮点击不受影响」走一遍。未改写入安全语义（纯呈现层：五个对话框的字段、校验、门禁与保存链路一行未动）；本轮未连接或写入 Apifox，未调用任何模型服务。


## 原生切片「列表选中恢复键盘焦点与关于文案口径收口」（2026-11-07 第三十三轮）

- **接续前确认**：上一轮工作树干净，HEAD（3e41f4f）的 Linux `dotnet build` 0 警告 0 错误、535 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：逐屏复核主工作流界面与全部对话框（三十三轮 GUI 均未真机运行），发现一个真实交互缺陷与一处文案违规——
  1. **程序化选中不恢复键盘焦点**：全部 10 处程序化列表选中（任务页 `SelectTask` 重建后恢复选中任务；注释回写、候选审阅、检查结果、响应契约、差异确认（两处）、核对结果、补充说明、同步预览八个对话框 populate/重载后选中首行或恢复选中行）只设 `item.Selected = true`。WinForms 列表重建后 `FocusedItem` 丢失、Selected 不携带键盘焦点——用户在列表获得键盘交互（点击选中行、Tab 进入列表）后按方向键，导航从列表头部重新开始、详情跳到第一个条目而不是从选中项继续，违反 Windows 列表惯例（设计验收 D12「键盘习惯」）。触发路径真实存在：作业结束后任务页 OnTick → LoadTasks 重建、审阅/检查类模态对话框关闭后各页 LoadTasks 重建、各对话框内筛选或审阅动作后重载。
  2. **关于对话框自述工程进度**：「Apifox 助手 x.y（原生桌面界面·开发中）」——「开发中」是第一轮原生壳刚落地时的状态描述，第三十轮原生已成为默认入口后既过时又违反 D11「不向用户自述工程进度」；该文案能漏网到今天，正说明 TypographyTests 的 D11 检查只有空话禁词、缺「工程进度自述」条款。
- **原生交互推进（具名 pass：harden + clarify，纯呈现层）**：
  - 新共享口径 `Ui.SelectItem`（程序化列表选中唯一入口）：Selected 与 Focused 同时恢复；10 处调用点全部收敛（TasksPage、ApplyPlanDialog、CandidatesDialog、CheckResultsDialog、ContractsDialog、DecisionsDialog×2、PropagationDialog、SupplementsDialog、SyncPreviewDialog）。刻意不加 `EnsureVisible`：重载后视口是否跳到选中行属于真机观感取舍（用户可能刻意滚到别处），不盲改。
  - 关于对话框去掉「（原生桌面界面·开发中）」，只保留事实信息（版本、本地服务地址、资料目录、.NET 版本）。
- **测试 535 → 542 用例**：`ListRowsTests` 新增 `ProgrammaticSelection_RestoresKeyboardFocus`（机械检查，行首正则不被注释行骗过：Ui.cs 之外任何非注释行的 `.Selected = true` 都被拒绝——绕过入口即红；助手体必须同时含 Selected 与 Focused 两行）；`TypographyTests` 新增 `Copy_DoesNotNarrateOwnProgress` 理论用例（禁词「开发中/迭代中/本切片/后续切片/尚未接入/待接入」逐行扫描全部 Native 源码——注释与字符串同一口径；白名单 `IsDomainStatusLine` 按行首内容精确匹配，只豁免 ProfileAdvanced 接口状态枚举 `("developing", "开发中")` 一行——描述接口自身状态而非工具进度；白名单失配即红，逼任何新出现走显式登记）。
- **变异验证五处实测可红**（各自独立变红、还原后全绿，还原一律手工逆编辑并核对 diff）：任务页调用点回退直连 `Selected`（1 红）、助手删 `Focused` 行（1 红）、候选对话框调用点回退（1 红）、关于对话框重新引入「开发中」（1 红——正是本轮修复前的原始缺陷形态，检查可直接抓住）、白名单匹配串破坏（1 红——领域状态行被标红，证明白名单窄目标而非一揽子豁免）。期间一次还原误用 `git checkout -- CandidatesDialog.cs` 把未提交的切片修改一并回退到 HEAD（该命令对已修改跟踪文件回退到提交点而非切片前副本），由 diff 核对发现后手工重放——「还原必须核对 diff」纪律再次生效。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 542 用例全部通过（含令牌接线机械检查对十个改动文件的扫描——无就地颜色/字体/间距值）。方向键从 FocusedItem 继续导航、程序化设 Selected 不带焦点是 WinForms ListView 的确定行为（源码与文档核对）；**列表键盘导航、选中行焦点恢复与关于对话框文案的 Windows 真机观感仍未验证**（G-D5 复核）——Windows 真机需按「任务列表选中中间某任务 → 触发一次扫描等其结束 → 点击选中行按方向键 → 导航从选中项继续、详情跟随，不再跳回第一行 → 打开候选审阅或检查结果对话框 → Tab 进入列表按方向键同样从选中行继续 → 帮助菜单「关于」不再出现「开发中」字样」走一遍。未改写入安全语义（纯呈现层：选中恢复不影响任何数据、门禁与确认链路）；本轮未连接或写入 Apifox，未调用任何模型服务。


## 原生切片「项目配置页选中保留与保存后定位」（2026-11-07 第三十四轮）

- **接续前确认**：上一轮工作树干净，HEAD（6ab1076）的 Linux `dotnet build` 0 警告 0 错误、542 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：第三十三轮把十处程序化列表选中收敛到 `Ui.SelectItem` 后复核两主页的选中生命周期，发现**项目配置页的列表选中在任何重建后都不恢复**——`LoadProfiles` 重建列表（页面切换 `OnPageShown`、F5 刷新、新建/编辑配置对话框保存返回）后既不恢复 Selected 也不恢复 Focused，也没有任何选中跟踪字段：刚编辑保存完的配置立即失去选中，「编辑」「连接与写入探测」退回禁用，想再改或去探测必须重新点行；切到任务页再切回来同样丢位。对照任务页——`_selectedTask` 跟踪选中、`LoadTasks` 重建后 `SelectTask` 恢复、作业结束与对话框关闭的重建都不丢位——同一壳内两页行为不一致，配置页与 Windows 列表惯例（设计验收 D12 键盘习惯同族）相悖；第三十三轮的十处调用点清单没有配置页，正因为它此前根本没有恢复调用。
- **原生交互推进（具名 pass：harden，纯呈现层）**：
  - `ProjectsPage` 增加选中跟踪字段 `_selectedId`，`SelectedIndexChanged` 处理器随选中变化记录配置 Id；`LoadProfiles` 在重建前先取局部（`Items.Clear` 会触发选中事件把字段抹成 null，与任务页取局部同法），重建后 `RestoreSelection` 按配置 Id 找行并经 `Ui.SelectItem` 唯一入口恢复（Selected + Focused 同时恢复）；原选中项已不存在（如配置在别处被删）时不动作。重建后「编辑/连接与写入探测」先禁用、恢复选中时由选中事件重新启用——不依赖 Clear 是否触发事件，按钮状态确定性成立（不再保留旧代码在重建尾部无条件禁用的时序假设）。
  - `ProfileDialog` 保存成功后暴露 `Saved`（保存失败保持 null），页面在对话框 OK 后定位刚保存的配置再重建——新建配置后新行被选中并可直接「连接与写入探测」，编辑保存后原行保持选中；新建任务后选中新任务并显示「下一步」引导（任务页既有行为）在配置页有了对应物。
- **测试 542 → 543 用例**：`ListRowsTests` 新增 `ProjectsPage_SelectionSurvivesReloads`（机械检查，行首正则不被注释行骗过，八条断言：字段声明、事件处理器记录 Id、重建前取局部、重建后恢复调用、恢复经 `Ui.SelectItem` 唯一入口、`Saved` 属性、保存成功路径赋值、对话框 OK 后回写 Id）；既有 `ProgrammaticSelection_RestoresKeyboardFocus`（「Ui.cs 之外无非注释 `.Selected = true`」）继续覆盖绕过入口的写法。
- **变异验证五处实测可红**（各自独立变红、还原后全绿，还原一律手工逆编辑并核对 diff）：事件处理器注释掉 Id 记录（1 红）、恢复调用改传 null（1 红）、`Ui.SelectItem` 改直设 `Focused`（1 红）、保存成功路径注释掉 `Saved` 赋值（1 红）、对话框 OK 后注释掉 Id 回写（1 红）。期间一次编辑把 `Save` 方法三行缩进破坏（8 空格替了 12 空格），由保存后重读 diff 当场发现修正。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 543 用例全部通过（含令牌接线机械检查对 ProjectsPage.cs 改动的扫描——无就地颜色/字体/间距值）。列表重建后按 Selected + Focused 恢复、`Items.Clear` 触发选中事件均为 WinForms 确定行为（与任务页三十三轮同法同源核对）；**配置页选中恢复与保存后定位的 Windows 真机观感仍未验证**（G-D5 复核）——Windows 真机需按「选中某配置 → 切到检查任务再切回 → 选中行保持且方向键从该行继续 → 编辑该配置保存 → 对话框关闭后该行仍选中、「编辑/连接与写入探测」可用 → 新建配置保存 → 新行被选中」走一遍。未改写入安全语义（纯呈现层：选中恢复不影响任何配置数据、校验与保存链路一行未动；`ProfileDialog.Saved` 是新增只读属性，不进入任何 JSON）；本轮未连接或写入 Apifox，未调用任何模型服务。


## 原生切片「多行输入的 Enter 换行与补充编辑位工厂收口」（2026-11-07 第三十五轮）

- **接续前确认**：上一轮工作树干净，HEAD（5014053）的 Linux `dotnet build` 0 警告 0 错误、543 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：逐个输入框核对键盘语义（三十四轮 GUI 均未真机运行）发现两个真实缺陷——
  1. **多行输入框的 Enter 被对话框主按钮吃掉**：`Ui.Input` 多行模式未设 `AcceptsReturn`，而 WinForms 文档行为是「窗体设了 AcceptButton 时，未设该属性的多行 TextBox 把 Enter 交给主按钮」。全部可编辑多行输入都坐在设了 AcceptButton 的对话框里，六个对话框受影响：新建任务「指定接口」（每行一个「GET /路径」，按 Enter 直接开始扫描作业）、「模块与模板」的模块范围与鉴权/安全方案/公共 Header 三个 JSON 编辑位（多行 JSON 根本打不出来，按 Enter 直接「应用」并报校验错）、「序列化与分析」的响应封装与 XML 文档路径（「每行：…」格式无法换行）、手工填写说明（按 Enter 提前提交）、候选审阅的待确认问题回答与补充说明编辑位（按 Enter 关闭对话框，未保存输入被丢弃）。
  2. **补充说明编辑位绕过输入框工厂**：`SupplementsDialog.EditorBox.Text` 是裸 `new()` TextBox（有 `BackColor=Panel` 但沿用系统 3D 边框，且经只读预览的 `Ui.Hairline` 包裹——无聚焦 Accent 描边，与 §6「输入框」相悖）。第二十五轮「全部 35 处」的清单漏掉它，正因机械检查 `TextBoxes_AreOnlyCreatedByFactory` 只匹配字段声明形式 `TextBox _x = …`，匹配不到属性声明形式 `public TextBox Text { get; } = new()`——与第二十六轮「接线断言被注释行骗过」同类的检查盲区。
- **原生交互推进（具名 pass：harden + polish，纯呈现层）**：
  - `Ui.Input` 工厂统一设 `AcceptsReturn = multiline`（单一落点；单行输入保持 Enter=主按钮的 Windows 惯例）。只读 `Ui.Preview` 不设——Enter 触发查看器对话框的关闭按钮与 Esc 同语义，保持不变。
  - `SupplementsDialog.EditorBox.Text` 改经 `Ui.Input(multiline: true, scrollBars: Vertical)` 出厂，编辑位包裹从 `Ui.Hairline` 改为 `Ui.Field`（获得聚焦 Accent 描边与失败描边能力；补 `Dock = Fill` 保持原 Hairline 面板的占位——Percent 列 + 48px 行不变）。`AcceptsReturn` 随工厂自动获得。
  - 机械检查盲区收口：`TextBoxes_AreOnlyCreatedByFactory` 增加属性/目标类型 new 形式的构造禁令（`TextBox X { … } = new(…)` 同样拒绝），工厂断言的声明正则从字段形式推广到属性形式（两种声明都必须从工厂出厂）。
- **测试 543 → 544 用例**：`Factories_SetBorderlessChromeAndPreviewReadonly` 的 Input 初始化器正则补 `AcceptsReturn = multiline`（Preview 正则不含该属性，继续钉住只读框不设）；`TextBoxes_AreOnlyCreatedByFactory` 收口盲区；新增 `SupplementEditors_UseInputFactoryWithFieldChrome`（行首正则：编辑位经工厂出厂、`Ui.Field` 包裹、`Dock = Fill` 占位、不得回到 `Ui.Hairline`）。
- **变异验证四处实测可红**（各自独立变红、还原后全绿，还原一律以切片后基准副本覆盖并核对 diff）：删 Input 的 `AcceptsReturn = multiline`（1 红）、Preview 加 `AcceptsReturn = true`（1 红——只读框的初始化器正则被钉死）、编辑位回退属性形式裸 `new()`（1 红——正是本轮修复前的原始缺陷形态，新检查直接抓住）、`Ui.Field` 退回 `Ui.Hairline`（2 红：定向接线断言 + 通用「含 Input 必含 Field」文件级检查）。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 544 用例全部通过（含令牌接线机械检查对 Ui.cs/SupplementsDialog.cs 改动的扫描——无就地颜色/字体/间距值）。多行 TextBox 未设 `AcceptsReturn` 时 Enter 触发窗体 AcceptButton 是 WinForms 文档行为（与单行输入、Esc 语义同源核对）；**Enter 换行与聚焦描边在六个对话框的真机观感仍未验证**（Linux 无法运行 WinForms GUI，G-D5 复核）——Windows 真机需按「新建任务：『指定接口』输入两行接口后按 Enter，换行而不开始扫描 → 模块与模板：鉴权 auth 粘贴/键入多行 JSON 按 Enter 换行 → 补充说明：编辑位点入后按 Enter 换行且对话框不关闭、聚焦时描边为 Accent → 单行输入（如配置名称）按 Enter 仍触发主按钮」走一遍。未改写入安全语义（纯呈现层：六处输入的取值、校验与保存链路一行未动——`AcceptsReturn` 只改 Enter 键的解释，不改任何文本内容口径）；本轮未连接或写入 Apifox，未调用任何模型服务。


## 原生切片「壳启动回到上次使用的页面」（2026-11-07 第三十六轮）

- **接续前确认**：上一轮工作树干净，HEAD（5014053）的 Linux `dotnet build` 0 警告 0 错误、544 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：壳级桌面行为的逐项复核（三十五轮 GUI 均未真机运行）发现一个会话型工具的真实体验缺口——**壳每次启动固定落在「项目设置」页**：本工具是「开发后使用」的会话型工具（方案 2.2 形态边界），日常旅程（方案 3.2：扫描 → 审阅 → 检查 → 同步 → 报告）全部在「检查任务」页，配置是一次性的首阶段；窗口位置与最大化状态已按旧壳已验证行为记忆（`WindowPlacement` → desktop-window-native.json），唯独页面位置不记忆——常用用户每次启动都要多点一下「检查任务」，与窗口位置记忆同族的页面位置缺失。
- **原生交互推进（具名 pass：harden，纯壳行为）**：
  - 新共享口径 `Desktop/Native/LastPage`（不依赖 WinForms、与测试工程单一编译；`LastPageKeys` 两页键单一落点，界面代码不就地写字符串字面量）：`Read` 无记录（缺文件）、读取失败、内容损坏或值不在两页键内一律回退项目设置页（首次启动的缺省，fail-closed 到旧行为）；`Save` 只接受两页键、其余忽略，写失败静默（页面记忆只是便利设置，与 `WindowPlacement.Save` 同款注释与异常口径）。落点 `desktop-page-native.json` 与 `desktop-window-native.json` 同目录同族。
  - 壳接线（`NativeShellForm`）：`ShowPage` 成功切换后记录页面键（由导航按钮推导，初始显示保存的是刚读出的同一值、幂等）；`OnLoad` 尾部按 `Read` 分支恢复——无记录/损坏/未知值自然落在项目设置页，与旧行为一致。
  - 过渡壳不动：`WindowPlacement` 在 `Desktop/MainForm.cs`（过渡壳共享源码），本轮未改该文件；原生壳页面记忆是新文件新落点，冻结原则不受影响。
- **测试 544 → 553 用例**：新增 `LastPageTests`（9）：纯逻辑在真实 FileStore 临时目录常驻断言（缺文件回退、两页键落盘往返、未知值回退、损坏 JSON 回退、非法键不落盘、非法键不清掉已存值）+ 壳接线行首正则机械检查（构造处出厂与落点、OnLoad 分支恢复、ShowPage 唯一保存入口；页面记忆文件不出现第二个写入口）。**变异验证五处实测可红**（各自独立变红、还原后全绿，还原一律以切片后基准副本覆盖并核对 diff）：启动恢复分支回退恒项目设置页（1 红）、ShowPage 保存调用删除（1 红）、Read 未知值改回显（1 红——「settings」被原样返回）、Save 非法键守卫删除（2 红：不落盘断言 + 不清值断言）、Read 损坏 JSON 异常外抛（1 红）。期间一次机械检查断言里逐字字符串的引号少写一个（`""` 误作 `"`），编译期即被 CS1056 拦下修正。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 553 用例全部通过（含令牌接线与 G-D4 机械检查对新文件 LastPage.cs 的扫描——无就地颜色/字体/间距值、无禁词）。纯逻辑口径由测试工程在真实 FileStore 临时目录常驻断言（落盘往返即真实磁盘读写，无需另起 /tmp 脚本）；**启动落在上次页面的 Windows 真机观感仍未验证**（G-D5 复核）——Windows 真机需按「切到检查任务 → 关闭 → 重启落在检查任务页 → 切回项目设置 → 重启落在项目设置页 → 手工把 desktop-page-native.json 改坏 → 重启仍落在项目设置页且功能正常」走一遍。不触碰任何业务数据、服务链路与写入安全语义（壳级行为：只读写一个页面键文件）；本轮未连接或写入 Apifox，未调用任何模型服务。


## 原生切片「主页列表列宽自适应可视宽度」（2026-11-07 第三十七轮）

- **接续前确认**：上一轮工作树干净，HEAD（c12c3ab）的 Linux `dotnet build` 0 警告 0 错误、553 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：两主页列表与冻结页面同数据的宽度行为对照发现一个布局缺陷——冻结页面的任务表格是流式布局（`table { width: 100% }`）、任何窗口宽度都完整可见，而原生列表全部是固定像素列宽：任务列表七列合计 872px，壳默认窗口 1400×900 下任务列表面板约 732px（分割面板把详情区固定在右侧 420px），「更新时间」列被横向滚动条藏住，用户每次都要先滚才能看到；最小窗口（1024）下更只剩约 372px；配置列表六列合计 880px，最小窗口下同样被藏。反向问题同样存在：窗口加宽（如 1920 最大化）后列尾留约 380px 空白，自绘行只画到列宽为止，观感是「列表只占半截」。任务名称与配置名称恰是最容易截断的文字（200/180px 固定宽 + 省略号），多出的可视宽度给它们收益最大。
- **原生交互推进（具名 pass：layout，纯呈现层）**：
  - 新共享口径 `Desktop/Native/ListColumns`（不依赖 WinForms、与测试工程单一编译）：`FillWidth(可视宽, 标识列下限, 全列宽快照)` = max(下限, 可视宽 − 其余列合计)——「首列吸收」的约定落在参数顺序里（首列快照值只作占位、不参与合计），数学只有这一份。
  - 两主页接线（`TasksPage`/`ProjectsPage`）：设计宽度提为常量单一落点（`TaskNameColumnWidth` 200 / `ProfileNameColumnWidth` 180，建列与回退下限同值，不存在两处数字）；`_list.Resize` 挂重算（窗口缩放、纵向滚动条显隐改变可视宽都经 Resize 到达），列表重建（`LoadTasks`/`LoadProfiles` 的 `EndUpdate` 后）也重算——列合计始终对齐可视宽度，正常窗口宽度不出横向滚动条；窗口放不下固定列合计时首列回退设计宽度、横向滚动条如实出现（不把固定列挤破）。重算幂等，滚动条显隐引发的再次 Resize 收敛。
- **测试 553 → 561 用例**：新增 `ListColumnsTests`（8）：纯逻辑四条（可视宽恰放下 → 合计铺满且标识列取下限；加宽 → 剩余全给标识列；放不下 → 回退设计宽；首列占位值不参与合计）+ 共享编译清单断言（测试工程 csproj 必须含 ListColumns.cs——口径只有一份）+ 两主页行首正则接线机械检查（常量单一落点、Resize 挂接、FillWidth 调用形、加载方法体内的重算调用——接线被注释掉也算删除）+ DESIGN.md §6 条款断言。
- **变异验证六处实测可红**（各自独立变红、还原后全绿，还原一律以切片后基准副本覆盖并核对 diff）：`FillWidth` 的 `Math.Max` 改 `Math.Min`（4 红）、合计循环从 i=0 起算（首列参与合计，2 红）、任务页 Resize 挂接删除（2 红：挂接正则 + 任务页用例）、任务页加载方法内 `FitColumns();` 调用删除（1 红）、配置页 Resize 挂接删除（2 红）、测试 csproj 共享编译行删除（1 红）。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 561 用例全部通过（含令牌接线与 G-D4 机械检查对新文件 ListColumns.cs 的扫描——无就地颜色/字体/间距值、无禁词）。列宽计算口径由测试工程在共享编译下常驻断言；**滚动条显隐后的列表回填、省略号与列头对位、分割面板拖动时的重算观感仍未验证**（Linux 无法运行 WinForms GUI，G-D5 复核）——Windows 真机需按「默认 1400×900 打开 → 任务列表七列全部可见、无横向滚动条 → 拖窄窗口到最小 → 「更新时间」渐次收窄、极端窄时横向滚动条如实出现 → 拖宽到最大化 → 「任务」列吸收剩余宽度、列尾无空白」走一遍。纯呈现层：任务与配置数据、选中恢复、刷新与轮询口径一行未动（`FitColumns` 只改列宽，不触碰任何数据绑定）；本轮未连接或写入 Apifox，未调用任何模型服务。


## 原生切片「任务页分割面板在窗口缩放与最小宽度下确定」（2026-11-07 第三十八轮）

- **接续前确认**：上一轮工作树干净，HEAD（a981400）的 Linux `dotnet build` 0 警告 0 错误、561 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：承接第三十七轮的列宽复核，继续对照任务页的布局行为（三十七轮 GUI 均未真机运行），发现分割面板的两个确定性行为缺陷——
  1. **最小窗口宽度下分割距离不生效**：`OnPageShown` 的门控是 `_split.Width > 800`，而壳 `MinimumSize` 1024×640 减去导航 200 与内容区左右留白 32 后，分割面板宽约 776px（窗口边框还要再占几像素）——门控恒假，`SplitterDistance` 停在 WinForms 默认值 50px（.NET 8 参考程序集文档核实），任务列表只剩一条 50px 的缝，且分隔条只有 1px（`SplitterWidth = 1` 的发丝边形态）几乎无法手动拖回。触发路径真实且被第三十六轮放大：窗口宽度由 WindowPlacement 记忆、上次使用的页面由 desktop-page-native.json 记忆——用户上次把窗口缩窄后关闭，本次启动直接落在检查任务页，首屏就是 50px 列表。
  2. **窗口拖大时详情区偏离设计宽**：`FixedPanel` 缺省 None（同源核实），两栏按比例分配——1400 默认下详情区 420px，最大化 1920 下按比例放大到约 600px，与设计宽 420 漂移；方向也与第三十七轮「标识列（首列）吸收剩余可视宽度」相反：该吸收剩余宽度的是详情区而不是列表。
- **原生交互推进（具名 pass：layout，纯呈现层）**：
  - 构造期 `_split` 初始化器补 `FixedPanel = FixedPanel.Panel2`：窗口缩放时详情区保持 420 设计宽、列表吸收剩余宽度，与列宽自适应同一方向。
  - 常量单一落点：`DetailDesignWidth = 420`（详情设计宽）、`DetailMinWidth = 360`（详情可收窄下限）、`ListMinWidth = 320`（列表下限）——首次显示的分割距离与可行性门控同用这组值，不出现第二处数字。
  - 门控从魔数「宽度 > 800」改为可行性条件 `宽度 >= 详情设计宽 + 列表下限 + 分隔条宽`（741px 即可布置，壳最小窗口的约 776px 覆盖）：`Panel2MinSize = 360` → `SplitterDistance = 宽 − 420` → `Panel1MinSize = 320`，顺序即安全性——分割距离赋值时 Panel2MinSize 已就位（赋值可行）、Panel1MinSize 在距离之后设置（此时距离已不小于列表下限，不会与当前布局冲突；WinForms 对越界赋值抛 InvalidOperationException，防御性 catch 保留）。最小窗口宽度下首次显示同样正确布置。
- **测试 561 → 564 用例**：新增 `SplitLayoutTests`（3）：FixedPanel 与三个常量的构造期接线（行首正则 + `FixedPanel.None` 禁令）；`OnPageShown` 内门控/分割距离/两侧最小尺寸的接线（行首正则）+「任务页源码不再出现 800」的旧魔数禁令——首轮实测该禁令抓到本切片自己的解释注释里的「800」字面，注释改写为不带数字、检查保持锋利（注释与代码同一口径）；DESIGN.md §6 分割条款断言。
- **变异验证六处实测可红**（各自独立变红、还原后全绿，还原一律以切片后基准副本覆盖并核对 diff）：初始化器删 `FixedPanel`（1 红）、门控回退魔数（1 红：门控正则 + 800 禁令）、分割距离公式改用可收窄下限（1 红——详情区变 360、列表虚增 60）、删 `Panel1MinSize`（1 红）、DESIGN.md 条款去掉「50px」（1 红）、删 `Panel2MinSize`（1 红）。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 564 用例全部通过（含令牌接线与 G-D4 机械检查对 TasksPage.cs 改动的扫描——无就地颜色/字体/间距值）。SplitContainer 的默认值与缩放语义（`SplitterDistance` 默认 50、`FixedPanel` 默认 None 按比例分配、`Panel1MinSize`/`Panel2MinSize` 默认 25、越界赋值抛 InvalidOperationException）以 .NET 8 参考程序集（microsoft.windowsdesktop.app.ref 8.0.32）官方文档逐项核实；**最小窗口宽度下的首次布置、拖动分隔条与最大化时详情区保持设计宽的 Windows 真机观感仍未验证**（G-D5 复核）——Windows 真机需按「窗口缩到最小宽度后启动、落在检查任务页 → 任务列表占据详情左侧全部宽度、不是一条缝 → 最大化 → 详情区保持约 420 宽、列表与七列一起变宽 → 拖动分隔条（1px 发丝边，命中区窄属设计形态，如实挂账）→ 列表不小于约 320」走一遍。纯呈现层：任务数据、选中恢复、刷新与轮询口径一行未动（分割只改布局几何，`FitColumns` 照常经 Resize 重算）；本轮未连接或写入 Apifox，未调用任何模型服务。同期逐屏复核另发现配置列表双击空白区会触发 `ListView.DoubleClick` 打开「新建配置」对话框（事件不区分命中行），属 harden 范畴，留待后续轮次单独处理。


## 原生切片「配置列表双击只在命中行时打开编辑」（2026-11-08 第三十九轮）

- **接续前确认**：上一轮工作树干净，HEAD（9bed81b）的 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误、564 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：处理第三十八轮逐屏复核的明确挂账——配置列表双击空白区会打开「新建配置」对话框。
- **缺陷机理（具名 pass：harden，纯呈现层）**：`ListView.DoubleClick` 事件不区分命中位置——Details 视图下首击空白区先清空选中，二击触发 `DoubleClick` 时 `Selected()` 已为 null，而 `ShowDialog(null)` 的语义是「新建配置」，于是双击空白区（用户通常只是想点掉选中）弹出新建对话框。另外旧接线取的是「当前选中行」（`SelectedItems[0]`）而不是「被双击的行」，语义上就不是行激活。
- **修复口径**：新共享入口 `Ui.ActivateItem(ListView, Action<ListViewItem>)`（挂在 `Ui.SelectItem` 旁，同为列表行为唯一入口）：接线 `MouseDoubleClick`（事件自带命中坐标）+ `HitTest` 守卫——`Item` 为 null（空白区、列头间空白）即短路，命中哪行就把**该行自身**（`item.Tag`，不再借当前选中）交给回调；`ProjectsPage` 接线改为 `Ui.ActivateItem(_list, item => ShowDialog(item.Tag as ProjectProfile))`，双击行=编辑该行、双击空白区=不动作（Windows 资源管理器行激活惯例）。候选审阅与注释缺口两对话框的 `DoubleClick` 是「双击=查看代码证据」的快捷方式，空击时证据按钮本就因无选中而禁用、事件体内有既有守卫自然无操作，不属本缺陷面，如实记录、一行未动。
- **测试 564 → 567 用例**：新增 `ListActivationTests`（3）：`Ui.ActivateItem` 的守卫与接线（行首正则——工厂签名、MouseDoubleClick 接线、HitTest 短路行、回调行，被注释掉也算删除）；`ProjectsPage` 只经共享入口激活（行首正则钉住 `item.Tag` 载荷）+「页面源码不再出现 `_list.DoubleClick`」旧接线禁令；DESIGN.md §6 双击条款断言。
- **变异验证五处实测可红**（各自独立变红、还原后全绿，还原一律以切片后基准副本覆盖并核对 diff）：守卫删除（HitTest 结果直接 `!` 透传，1 红）、页面回退裸 `DoubleClick +=`（1 红）、激活载荷改回借当前选中 `_ => ShowDialog(Selected())`（1 红）、DESIGN.md 条款删除（1 红）、删除 `Ui.ActivateItem` 工厂本身（双重红：源码正则 1 红 + **主工程 `dotnet build` CS0117**——顺带核实一个工程事实：测试工程与主工程是共享源码编译关系而非项目引用，`dotnet build ApifoxHelper.Tests` 不编译 `Desktop/Native` 界面源码，界面接线的「编译可红」以主工程构建为准，此后变异验证两构建都跑）。
- **验证状态**：还原后 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 567 用例全部通过（含令牌接线与 G-D4 机械检查对 Ui.cs/ProjectsPage.cs 改动的扫描——新注释无禁词、无档外值）。**双击行激活与双击空白区不动作的 Windows 真机观感仍未验证**（G-D5 复核）——Windows 真机需按「双击配置行 → 打开该行编辑对话框 → 关闭 → 双击列表空白区 → 无任何对话框弹出 → 方向键导航不受影响」走一遍。纯呈现层：配置数据、选中保留、刷新与保存口径一行未动（`Selected()` 仍服务编辑按钮与选中保留，一行未改）；本轮未连接或写入 Apifox，未调用任何模型服务。


## 原生切片「证据入口按行收口：双击与按钮共用行内门控」（2026-11-08 第四十轮）

- **接续前确认**：上一轮工作树干净，HEAD（51386c5）的 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误、567 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：处理第三十九轮的明确挂账——候选审阅与注释缺口两对话框的「双击=查看代码证据」仍走裸 `ListView.DoubleClick`（借当前选中）；逐行对照两对话框的证据入口时另发现一个真实状态缺陷：**候选审阅对话框清空选中后「查看代码证据」按钮残留启用**——ShowDetail 的空选中分支只禁用了审阅三钮（`SetButtons(null)`），证据按钮的启用态保持上一次选中行的值，按钮看起来可点、点击后经 ShowEvidence 的空选中守卫静默无响应，违反设计基线 §7「禁用：控件禁用 + 一句话说明缺什么」；姊妹对话框 ScanResultsDialog 的空选中分支本就正确禁用证据按钮，同族不同行为。
- **缺陷机理（具名 pass：harden，纯呈现层）**：两对话框的旧双击接线 `_list.DoubleClick += (_, _) => { if (_evidenceButton.Enabled) ShowEvidence(); }` 借「当前选中」与「按钮启用态」做双重中介——`ListView.DoubleClick` 不区分命中行（第三十九轮同型），按钮启用态也可能滞后于选中变化（上述残留缺陷正是同一根因的另一面）。
- **修复口径**：
  - 双击接线换共享入口 `Ui.ActivateItem`（第三十九轮唯一入口）：命中哪行看哪行的证据（行自身 Tag 载荷，`item => ShowEvidence(item.Tag as CommentCandidate/DocSlot)`），双击列头与空白区不动作。
  - `ShowEvidence` 收敛为行载荷参数单一落点（`ShowEvidence(CommentCandidate?/DocSlot?)`），按钮点击传选中行载荷、双击传命中行载荷，共用同一份行内门控——证据数 > 0 才打开（与按钮启用条件同值；旧按钮路径的有效门控是「按钮禁用即点不到」，与该口径等价，统一为纯行为保持）。
  - 候选审阅对话框空选中分支补 `_evidenceButton.Enabled = false;`（对齐注释缺口对话框的既有正确行为）；「覆盖」勾选的启用态在空选中下同样短暂残留、但每次选中都会按该行重算且审阅动作自带空选中守卫，无功能后果，如实记录不动。
- **测试 567 → 569 用例**：`ListActivationTests` +2——`EvidenceDialogs_ActivateOnlyViaSharedEntry`（两对话框 `Ui.ActivateItem` 接线行首正则 + 裸 `_list.DoubleClick` 禁令）与 `EvidenceEntry_SharesOneRowScopedGate`（行载荷签名、按钮载荷、行内门控正则、空选中禁用锚点）；`DesignDoc_DeclaresDoubleClickRule` 条款断言补「证据」「列头」。
- **变异验证六处实测可红**（各自独立变红、还原后全绿，还原一律以切片后基准副本覆盖并核对 diff）：空选中禁用行删除（1 红）、候选审阅 `ActivateItem` 接线删除（1 红）、注释缺口 `ActivateItem` 接线删除（1 红）、行内门控弱化（去证据数条件，1 红）、DESIGN.md 条款去掉「列头」（1 红）、删除 `Ui.ActivateItem` 工厂本体（主工程 `dotnet build` CS0117——ProjectsPage/CandidatesDialog/ScanResultsDialog 三处调用点可红；测试工程不编译界面源码，界面接线的「编译可红」以主工程构建为准）。
- **验证状态**：还原后 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 569 用例全部通过（含令牌接线与 G-D4 机械检查对两对话框改动的扫描）。**证据双击激活与按钮态的 Windows 真机观感仍未验证**（G-D5 复核）——Windows 真机需按「选中带证据的候选 → 双击该行 → 证据对话框打开 → 关闭 → 双击列表空白区与列头 → 无对话框弹出 → 点击空白区清空选中 → 「查看代码证据」按钮变禁用」走一遍。纯呈现层：候选与缺口数据、审阅/手工/导入链路一行未动（`ShowEvidence` 收敛为行载荷参数，两处调用点行为等价）；响应契约对话框的证据按钮态经 `SetButtons(null)` 本就正确、且无双击接线，不属本缺陷面，如实记录一行未动；本轮未连接或写入 Apifox，未调用任何模型服务。


## 原生切片「补充保存主按钮每次尝试后按状态恢复」（2026-11-08 第四十一轮）

- **接续前确认**：上一轮工作树干净，HEAD（20f51b3）的 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误、569 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：逐屏复核继续按第四十轮同族（按钮态纪律）排查全部对话框的异步动作按钮——发现一个真实状态缺陷：**补充说明对话框的保存主按钮禁用后没有任何恢复路径**。
- **缺陷机理（具名 pass：harden，纯呈现层）**：`SaveCurrent` 在保存开始时 `_saveButton.Enabled = false`，但全文件没有任何 `_saveButton.Enabled = true`——成功路径收尾只调 `ReloadEditors()`（只管放弃按钮），被拒路径只调 `MarkDirty()`（同样只管放弃按钮），一次保存（成功或被拒）后主按钮**残留禁用**，无法继续保存其他接口、也无法重试失败保存；F10 补充说明的核心场景（逐接口补充并保存多个接口）在对话框内第二次保存即不可达，只能关闭重开。同族对照：ApplyPlanDialog 的 SetBusy、SyncPreviewDialog/GenerationDialog/ProfileAdvancedDialog/CapabilitiesDialog 的异步按钮全部有对称恢复，本对话框是唯一缺口；与第四十轮「残留启用」同族反向——同一纪律：按钮态必须由状态推导，不得残留上一次的状态。
- **修复口径**：门控收敛为 `UpdateSaveState()` 单一口径（选中接口 + 已保存补充可读 + 不在保存中三者齐备才可用），三处接线——构造尾部（初始禁用，等数据）、`ReloadEditors` 头部（选中变化/放弃修改/保存成功统一经此）、`MarkDirty`（保存被拒后随 `_saving = false` 恢复）；执行中禁用锚点保留（`_saving = true` 后显式禁用；保存期间编辑位 TextChanged 经 MarkDirty→UpdateSaveState 也因 `!_saving` 保持禁用，不会中途亮回）；清空选中后保存随放弃一起禁用，状态行说明缺什么（「选择接口后可编辑与保存说明补充。」，§7 禁用条款）；启用不得写死为 true 由机械检查钉住。
- **测试 569 → 572 用例**：新增 `SupplementSaveStateTests`（3）——门控方法与表达式行首正则、三处接线计数（=3）、写死 `Enabled = true` 禁令、空选中状态行锚点；执行中「置位+禁用」相邻锚点、两条被拒路径 `_saving = false → MarkDirty()` 计数（=2）、成功路径 `_saving = false → ReloadEditors()`；DESIGN.md §8 条款断言。
- **变异验证五处实测可红**（各自独立变红、还原后全绿，还原以切片后基准副本覆盖并核对 diff 干净）：`ReloadEditors` 接线删除（1 红）、门控写死 true（1 红）、执行中禁用锚点删除（1 红）、`MarkDirty` 接线删除（1 红）、DESIGN.md 条款行删除（1 红）。
- **验证状态**：还原后 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 572 用例全部通过（含令牌接线与 G-D4 机械检查对本对话框改动的扫描）。**补充保存按钮状态恢复的 Windows 真机观感仍未验证**（G-D5 复核）——Windows 真机需按「选接口 → 改一处 → 保存 → 按钮仍可用 → 换接口再保存 → 仍可用 → 制造校验拒绝 → 按钮仍可重试 → 清空选中 → 保存禁用且状态行说明缺什么」走一遍。纯呈现层：补充数据、校验与保存链路一行未动（`SupplementEntries`/`SupplementService` 一行未改，保存请求与门禁口径不变）；本轮未连接或写入 Apifox，未调用任何模型服务。


## 原生切片「差异确认筛选空态不锁死」（2026-11-08 第四十二轮）

- **接续前确认**：上一轮工作树干净，HEAD（2118499）的 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误、572 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：逐屏复核继续按第四十/四十一轮同族（按钮与控件态纪律）排查全部对话框的控件启停——发现一个真实且容易触发的死路缺陷：**差异确认对话框筛选为空后筛选器被禁用，对话框锁死**。
- **缺陷机理（具名 pass：harden，纯呈现层）**：`DecisionsDialog.ApplyFilter` 筛选结果为空时调 `ShowHint`，而 `ShowHint` 是共享提示出口且内含 `_filter.Enabled = false`——该行本为打开期「无数据可筛」的门控服务，运行期空筛选被连坐。触发路径两条且都是正常操作：点开一个没有条目的分类筛选（如任务没有模型绑定时点「模型绑定」，一次点击即触发）；或在「待处理」筛选下确认最后一条（保存成功 → RefreshStates → ApplyFilter → 空表收尾）。此后提示语写着「这个筛选下没有条目。换一个筛选再试。」，筛选器本身却是禁用的——**提示所指引的恢复路径不可达**，列表隐藏、动作按钮全禁，对话框只剩「关闭」，想回到「全部」看已处理条目（例如撤销一次误确认）只能关闭重开。同族对照：CheckResultsDialog 的 ShowHint 不触碰筛选器（打开期门控各自显式禁用），空筛选可换筛选返回；ContractsDialog/ScanResultsDialog/CandidatesDialog 的 ApplyFilter 无空表分支（空列表照常显示、筛选可用）；DecisionsDialog 是唯一把运行期空筛选也走禁用的对话框。
- **修复口径**：职责按状态拆开——`ShowHint` 不再触碰筛选器（共享出口只管提示、隐藏列表与停用动作）；打开期四个无数据门控（读取失败/尚未检查/已保存决策读取失败/没有条目）在提示前显式 `_filter.Enabled = false`（与 CheckResultsDialog 同款，禁用 + 说明缺什么）；`ApplyFilter` 空表分支显式 `_filter.Enabled = true`——筛选下没有条目时筛选器本身是「换一个筛选再试」的恢复路径，禁用不得移除提示所指引的下一步（DESIGN.md §7「禁用」条款的另一半）。空表分支换筛选后经既有路径恢复：ApplyFilter 重填 → 提示隐藏 → 选中首行。
- **测试 572 → 576 用例**：新增 `DecisionsFilterStateTests`（4）——空筛选分支「先恢复筛选可用再提示」的相邻锚点与提示语契约（正则允许语句前有注释行，语句被注释掉行首是 `//` 仍算删除）；打开期四门控「先禁用再提示」相邻对计数（=4）；ShowHint 方法体内不得出现任何 `_filter.Enabled` 行（切片方法体定位断言）；DESIGN.md §8 条款断言。
- **变异验证五处实测可红**（各自独立变红、还原后全绿，还原以切片后基准副本覆盖并核对 diff 干净）：删空分支 `_filter.Enabled = true;`（1 红）、删「尚未检查」门控显式禁用行（1 红，正好 LoadGates 用例）、ShowHint 内加回禁用行（1 红，正好 ShowHint_DoesNotTouchFilterState 用例）、删 DESIGN.md §8 条款行（1 红）、改写「换一个筛选再试」提示语（1 红）。
- **验证状态**：还原后 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 576 用例全部通过（含令牌接线与 G-D4 机械检查对 DecisionsDialog 改动的扫描——新注释无禁词、无档外值）。**筛选空态换筛选恢复与打开期禁用态的 Windows 真机观感仍未验证**（G-D5 复核）——Windows 真机需按「打开一个没有模型绑定的任务的差异确认 → 点「模型绑定」筛选 → 提示出现且筛选器仍可点 → 换回「全部」列表恢复 → 「待处理」下确认最后一条 → 空表提示出现且筛选器可点 → 换「全部」能看到已确认条目并「撤销确认」」走一遍。纯呈现层：决策数据、DecisionService 校验与保存链路一行未动（门控与保存路径零改动）；本轮未连接或写入 Apifox，未调用任何模型服务。


## 原生切片「契约审阅关闭后按新任务版本刷新」（2026-11-08 第四十三轮）

- **接续前确认**：上一轮工作树干净，HEAD（c5ff1d5）的 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误、576 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：逐屏复核继续按第四十～四十二轮同族（动作之后的状态必须重新推导）排查任务页九个写任务数据入口的关闭后刷新——发现一个真实的滞留缺陷：**响应契约审阅对话框关闭后，详情与列表滞留旧任务版本**。
- **缺陷机理（具名 pass：harden，纯呈现层）**：契约审阅（确认/拒绝/重置）经共享 `ContractReviewService` 写 responseContract.json 并递增任务版本（`t.Revision++`，使旧检查与旧计划过期），但 `TasksPage.ShowContracts` 关闭对话框后只调 `RefreshContractSection()` 刷新契约计数行。后果三条且都能实际触发：引导横幅（`NextStep.Compute` 按 `CheckedRevision != Revision` 计算）不重算——确认契约后「下一步」仍指向旧动作而不是重新检查；远端状态行的「（任务内容在检查后有变化，需要重新检查）」后缀（按 `CheckedRevision < Revision` 推导）不出现；任务列表「下一步」列同样滞留。要等一次无关刷新（切页、作业结束、换选任务）才纠正，方案 3.2「界面始终给出唯一下一步」在这段窗口内给的是旧的一步。同族对照：注释回写（ShowApply）、差异确认（ShowDecisions）、补充说明（ShowSupplements）、同步预览（ShowSyncPreview）、缺口清单与候选审阅（ShowScanResults/ShowCandidates）关闭后都是 `LoadTasks()` 整页重读，契约对话框是唯一不一致；报告对话框的 `RefreshReportSection()` 只读导出副本、不写任务数据，不在本缺陷面。
- **修复口径**：`ShowContracts` 关闭后与同族入口同一口径 `LoadTasks()` 整页重读——任务对象从磁盘重读（页面内存中的 `CheckTask` 是审阅前的旧实例，契约审阅写的是服务自己从磁盘加载的副本），契约计数（RefreshDetail 内既有 RefreshContractSection）、引导横幅、远端状态行与列表「下一步」列全部从新版本推导，一轮刷新全部到位。
- **测试 576 → 581 用例**：新增 `ContractReviewRefreshTests`（5）——ShowContracts 方法体（锚点定位）「ShowDialog 之后 LoadTasks」相邻锚点（正则允许语句前有注释行，语句被注释掉仍算删除）、方法体内不得出现直连 `RefreshContractSection()`（只刷契约行是修复前的缺陷形态）；`ContractReviewService` 契约写入与 `t.Revision++` 的相邻锚点（刷新必要性的服务侧来源）；TasksPage 远端状态行「（任务内容在检查后有变化，需要重新检查）」按版本推导的正则与横幅重算行（滞留后果的两条呈现，钉住刷新所服务的口径）；DESIGN.md §8 条款断言。
- **变异验证五处实测可红**（各自独立变红、还原后全绿，还原以切片后基准副本覆盖并核对 diff 干净）：删 ShowContracts 的 `LoadTasks();`（1 红）、改回直连 `RefreshContractSection();`（2 红：整页重读锚点 + 直连禁令）、删服务端 `t.Revision++`（1 红）、删远端状态行版本推进提示（1 红）、删 DESIGN.md §8 条款行（1 红）。
- **验证状态**：还原后 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 581 用例全部通过（含令牌接线与 G-D4 机械检查对 TasksPage 改动的扫描——新注释无禁词、无档外值）。**契约审阅关闭后横幅与过期提示即时更新的 Windows 真机观感仍未验证**（G-D5 复核）——Windows 真机需按「选中已检查的任务 → 打开响应契约 → 确认一条 → 关闭 → 引导横幅立即指向重新检查、远端状态行出现「任务内容在检查后有变化」、列表「下一步」列同步更新」走一遍。纯呈现层：ContractReviewService 的审阅校验、写入与版本递增一行未动（刷新只重读磁盘）；本轮未连接或写入 Apifox，未调用任何模型服务。



## 原生切片「接口追踪明细可见」（2026-11-08 第四十四轮）

- **接续前确认**：上一轮工作树干净，HEAD（c999842）的 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误、581 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：第四十～四十三轮按同族纪律（按钮态残留/状态滞留/空态锁死）复核全部对话框与任务页入口，未再发现该族缺陷——本轮转向服务输出与界面的覆盖对照：逐一核对 REST 路由与落盘产物在原生（及冻结页面）的可见性，发现 **evidence.json 的逐接口 Coverage 是唯一没有任何界面呈现的服务输出**（冻结页面同样未使用 `/api/tasks/{id}/coverage/{endpointKey}` 路由）。
- **切片背景（具名 pass：clarify，纯只读）**：扫描时 `EvidenceCollector.Collect` 为每个范围内接口记录调用链追踪明细——读取了哪些方法体（MethodsRead）、未解析的调用（重载无法唯一确定、超过调用深度、超出单接口分析预算，含重载候选）、外部调用（不可回写）与单接口方法预算是否超限（BudgetExceeded）。方法预算（`Analysis.MaxMethodsPerEndpoint`，第十九轮起可在项目设置「序列化与分析」配置）用尽后调用链追踪提前收手——这是逐接口的分析降级：用户在契约审阅里看到「类型未确定」、在候选审阅里看到证据缺失，却没有任何地方能解释为什么，与 F03「明确降级、不按同名猜实现」相悖；第二十轮补了扫描级的警告/未解析符号/外部类型清单（symbols.json + source-manifest.json），逐接口层（evidence.json Coverage）是最后缺口。
- **实现口径**：注释缺口对话框（扫描产物的只读透明视图同族位置）新增「接口追踪明细」ghost——扫描结果可读后显示，读取失败与无数据由追踪对话框自己的空态如实说明（与缺口清单的读取语义解耦，不做连坐）；新只读对话框 `Desktop/Native/EndpointTraceDialog.cs`：逐接口列表（接口等宽、读取方法/未解析调用/外部调用计数，零显示「—」、预算列「超限」警示文字色——颜色+文字双通道）+ 选中接口的明细（方法清单、逐条未解析调用「表达式：原因（候选：…）」、外部调用「外部调用（外部类型，不可回写）：」与降级清单同族前缀、预算超限说明并指向「方法预算」调整入口、证据数）；「查看代码证据」经既有 `EvidenceDialog` 只读对照（行内门控与按钮同值：该行证据数 > 0，清空选中如实禁用——第四十轮同族纪律）；无写入动作。计数、预算与明细文字口径提取为共享的 `Desktop/Native/EndpointTrace`（不依赖 WinForms，输入取原始值/字符串集合，与 ScanOverview 同法不绑模型类型），与测试工程单一编译。
- **测试 581 → 589 用例**：新增 `EndpointTraceTests`（8）——CountText 两档（0→「—」）、BudgetCell 两档（超限 warn/「超限」，正常 muted/「—」）、明细行全段（预算说明含「方法预算」调整入口、方法清单、未解析计数与带候选行、外部调用前缀、证据行）与空段省略（干净接口不显示降级段、无方法体与无证据如实说明）；接线机械检查（行首正则，被注释掉也算删除）：缺口对话框入口的工厂出厂/Click 接线/显示与空态隐藏/ShowTrace 方法，追踪对话框的 Ui.List/Ui.Preview/Ui.Ghost 出厂、证据按钮行内门控与空选中禁用相邻锚点、「Enabled = true」写死禁令；csproj 共享编译清单含 EndpointTrace.cs；DESIGN.md §8 条款。
- **变异验证六处实测可红**（各自独立变红、还原以基准副本覆盖并核对 diff 干净）：删缺口对话框入口 Click 接线（1 红）、删追踪对话框空选中禁用分支（1 红）、BudgetCell 超限色调改 muted（1 红——改共享源码后须重编测试工程再跑，第一遍 `--no-build` 未生效已重做）、删 csproj 共享编译行（14 个 CS0103 编译错误双重可红）、删 DESIGN.md §8 条款行（1 红）、删 DetailLines 预算超限说明行（1 红；还原后未重编导致残留一红，重编后全绿——变异流程记录：改共享源码的变异与还原都要重编）。
- **Linux 运行时验证**：链路不触碰 WinForms，/tmp 一次性控制台链接纯逻辑源文件（Infrastructure 的 Core/FileStore、Profiles 的 ProjectProfile/ProfileStore、Tasks 的 TaskModels/TaskFiles/TaskStore、Source 的 SourceModels、Desktop/Native/EndpointTrace，镜像 GlobalUsings；不进仓库、已清理），在真实 FileStore/ProfileStore/TaskStore 上创建配置与任务，经服务的真实写路径（TaskStore.Write → JsonDefaults）落盘 evidence.json 夹具（超限接口 + 干净接口），23 项断言全部通过：落盘形状（camelCase 键 coverage/endpointKey/methodsRead/budgetExceeded；**JsonDefaults 转义口径核实——`UnicodeRanges.All` 中文原样、HTML 敏感字符仍按默认规则转义，候选「Map<T>(T)」落盘为 `Map\u003CT\u003E(T)`，回读后原值一致**；脚本首版把「落盘无转义」误设为预期，核对编码器配置后确认为脚本断言误设、服务一行未改）、回读逐字段一致（含嵌套候选列表）、口径在回读数据上的全部段落渲染、干净接口空段省略、旧任务缺 coverage 键按空处理（对话框空态口径）。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 589 用例全部通过（含令牌接线与 G-D4 机械检查对新增两文件与 ScanResultsDialog 改动的扫描——无禁词、无档外值）。**追踪明细对话框的 Windows 真机观感仍未验证**（G-D5 复核）——Windows 真机需按「扫描一个含通用封装与外部调用的真实项目 → 注释缺口 → 接口追踪明细 → 超限行警示色与「超限」文字 → 选中接口看方法清单与未解析调用 → 点「查看代码证据」→ 清空选中证据按钮禁用」走一遍。纯只读：服务层（扫描/证据/契约/检查/同步）一行未动；本轮未连接或写入 Apifox，未调用任何模型服务。


## 原生切片「异步落点守卫：对话框关闭后不再触达界面」（2026-11-08 第四十五轮）

- **接续前确认**：上一轮工作树干净，HEAD（558d7f2）的 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误、589 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：按钮态残留（第四十/四十一轮）、状态推导滞留（第四十二/四十三轮）、服务输出可见性（第四十四轮）三族复核完毕，本轮按「UI 线程 await 的落点」逐点排查全部对话框——发现一个真实的同族纪律不统一：**模态对话框的异步操作完成时窗口可能已被用户关闭，九个较早方法在 await 之后无守卫地触达界面（导入结果的按钮恢复也不完整）**。
- **缺陷机理（具名 pass：harden，纯呈现层）**：模态对话框的异步操作执行期间「关闭」（按钮与 Esc）始终可用；用户关闭后窗体释放（任务页与项目页一律 `using` 持有对话框），await 的续体仍经 WinForms 同步上下文回到 UI 线程继续执行——`SetStatus`、按钮恢复、列表重建都作用在已释放控件上，async void 中的未处理异常按 ThreadException 弹错误对话框。触发路径真实且代价不小：连接检查要调用 node 子进程（秒级），打开探测对话框后立即 Esc 即中招；候选/契约审阅、差异确认与补充保存、模板提取、查找 API 项目与 Apifox 项目/分支读取同理。同族对照：较新的路径——注释回写对话框（LoadPlan/RunApply/ExportPatch）、导入编码助手结果（ImportResult 主体）、手工候选（RunManualAdd）、壳（OnLoad/OnJobCompleted）与同步预览轮询（OnTick）——await 之后都有 `IsDisposed` 守卫，纪律存在但不统一；缺失的九个方法：候选审阅 `RunReview`、契约审阅 `RunReview`、差异确认 `SaveDecision`、补充保存 `SaveCurrent`、连接检查 `CheckAsync`、模板提取 `ExtractAsync`、配置对话框的查找 API 项目/读取项目列表/读取分支三个方法；期间还发现 ImportResult 自己的守卫不完整——三条 catch 路径的 `_importButton.Enabled = true` 恢复是裸的（同方法的状态行输出已守卫）。
- **修复口径**：全部异步 UI 方法统一为「await 之后触达界面以前先检查 IsDisposed」——两条 catch 与成功续体各一处（沿用 ApplyPlanDialog.RunApply 的既有形态），finally 里的按钮恢复同样守卫（沿用 ScanResultsDialog.RunManualAdd 的 `if (!IsDisposed) …` 形态；ImportResult 三条恢复一并收口）。守卫之后的候选/决策重读是同步代码：检查点与后续触达之间没有消息泵，窗口不可能变为已释放，不重复检查。守卫只影响呈现：保存与审阅在窗口关闭后照常落盘（数据不丢），服务链路一行未动。
- **测试 589 → 594 用例**：新增 `AsyncDisposeGuardTests`（5）——四个「保存/审阅」方法体切片各断言三处守卫计数与成功续体的相邻锚点；ImportResult 三条守卫恢复计数；CheckAsync/ExtractAsync/三个 ProfileDialog 方法的「await 行 → 守卫行」相邻锚点、finally 守卫恢复与裸恢复禁令；DESIGN.md §8 条款断言。锚点缺失即红（方法体切片不允许断言漂移到空切片）。
- **变异验证六处实测可红**（各自独立变红、还原以基准副本覆盖并核对 diff 干净）：删候选审阅一条 catch 守卫（1 红）、删补充保存成功续体守卫（1 红）、连接检查 finally 恢复去守卫（1 红）、导入结果一条恢复去守卫（1 红）、删读取分支的 await 后守卫（1 红）、删 DESIGN.md §8 条款行（1 红）。
- **验证状态**：还原后 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 594 用例全部通过（含令牌接线与 G-D4 机械检查对七个改动文件的扫描——无禁词、无档外值）。**「操作执行中关闭对话框不出现错误对话框、作业照常完成」的真机行为仍未验证**（G-D5 复核）——Windows 真机需按「打开连接与写入探测 → 立即 Esc → 无错误对话框弹出」与「契约审阅确认进行中关闭对话框 → 任务数据照常落盘」走一遍。如实记录不动：两处 WinForms Timer 回调（TasksPage.OnTick、CapabilitiesDialog.OnTick）不是 await 续体——定时器在各自 Dispose 中释放、释放后不再触发（SyncPreviewDialog.OnTick 的同名守卫属纵深防御，本轮不扩面）。纯呈现层：服务层与服务调用一行未动，写入安全语义不变；本轮未连接或写入 Apifox，未调用任何模型服务。

## 原生切片「重新加载 Swagger 并检查：冻结副本刷新入口」（2026-11-08 第四十六轮）

- **接续前确认**：上一轮工作树干净，HEAD（2ae5f65）的 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误、594 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现。缺口对照：按钮态残留（第四十/四十一轮）、状态推导滞留（第四十二/四十三轮）、服务输出可见性（第四十四轮）、异步落点守卫（第四十五轮）四族复核完毕，本轮对照冻结页面逐入口核对检查链路的可用性——发现一个真实的入口缺口：**检查始终基于冻结副本，但原生在冻结过一次之后再也没有任何重新冻结 Swagger 的入口**。
- **缺陷机理（具名 pass：clarify，纯呈现层）**：方案 3.2 第 6 步「配置了 Swagger 时首次检查先加载并冻结 Swagger，随后整分支只读概览 + 范围内详情读取」——检查（`CheckService.RunAsync`）始终读取任务目录里冻结的 `source.openapi.json`，检查本身不加载、更不悄悄换源；冻结页面的检查入口在「Swagger 已配置且已冻结」时显示「重新加载 Swagger 并检查」（`recheck(true)`，先 `swagger-load` 再 check），工具栏并指引「源码或 Swagger 更新后请重新加载」。原生 `StartRemoteCheck` 只按 `NextStep.NeedsSwaggerLoad`（`SourceHash` 为空，即从未冻结）决定是否先加载——冻结过一次后该判定恒假，任务页只剩「重新检查」，永远比较旧副本；且 NextStep 引导横幅（检查过期分支）明确写着「源码或 Swagger 有更新时，请用『加载 Swagger 并检查』」，所指入口在冻结后的原生界面上并不存在。触发路径真实且是主工作流后半程：注释回写、定向构建（dotnet build 刷新 XML/Swagger）之后想看到新结构，纯原生工作流里无路可走，只能删任务重来。
- **实现口径**：任务页「Apifox 差异与同步」区新增「重新加载 Swagger 并检查」ghost（与「开始检查/重新检查」并列）：文案取 `NextStep.ReloadSwaggerLabel` 单一落点（与冻结页面逐字一致）；可见性按新共享口径 `NextStep.CanReloadSwagger`（配置了 Swagger、已冻结、非离线——与 `NeedsSwaggerLoad` 在配置了 Swagger 的在线任务上恰好互补，加载语义始终恰有一个入口承担；离线任务的结构来源是导入的 JSON，重新冻结与检查无关），默认隐藏、选中任务时按口径推导；启用与其他远端入口同一纪律（扫描完成后可用、作业执行中禁用）；`StartRemoteCheck(bool reloadSwagger)` 参数化，重冻结与首冻结共用同一「契约检查」作业（先 `LoadSwaggerAsync` 再 `RunAsync`，进度与取消沿用既有固定占位控件）；远端区说明行补一句冻结语义与用途。`LoadSwaggerAsync` 冻结时把 previewable/awaitingConfirm 置为 expired 并递增版本（既有服务行为），旧计划随之过期如实呈现。服务层一行未动。
- **说明文字指名的入口同步收口**：引导横幅的说明文字此前无条件指名「加载 Swagger 并检查」——冻结后该入口已不存在（任务页按钮变成「重新检查」、加载语义由新入口承担），未配置 Swagger 时更没有任何加载入口，文字指向界面上不存在的按钮。现 `NextStep.SwaggerEntryLabel` 按任务状态给出真实存在的那个入口名（未冻结=「加载 Swagger 并检查」、已冻结=「重新加载 Swagger 并检查」、未配置 Swagger 或离线=null），「核对生成文档」与「重新检查」两处说明文字按它取值：未配置 Swagger 时只说「重新检查」/只讲结果已失效，不指名入口；文案常量 `LoadSwaggerLabel`/`ReloadSwaggerLabel` 单一落点，任务页按钮与说明文字同源（不复制字面）。
- **测试 594 → 605 用例**：新增 `SwaggerReloadTests`（8）——`CanReloadSwagger` 门控真值表（冻结/未冻结、Swagger 类型 none/空、离线、url）、「与 NeedsSwaggerLoad 恰好互补」断言、文案与冻结页面逐字一致、接线机械检查（工厂出厂、注册进远端动作区相邻锚点、点击强制重冻结、可见性与启用两行、`StartRemoteCheck` 方法体切片的加载条件与加载调用、说明行冻结语句）、DESIGN.md §8 条款断言（行首正则）；`NextStepTests` +3——`SwaggerEntryLabel` 真值表（未配置/未冻结/已冻结/离线/url）、已冻结时「核对生成文档」指名重新加载入口、过期提示在已冻结时指名重新加载入口而未配置 Swagger 时不指名任何入口（逐字断言）。
- **变异验证六处实测可红**（各自独立变红、还原以基准副本覆盖并重建核对）：删 `reloadSwagger ||` 强制加载（1 红：StartRemoteCheck 方法体切片）、可见性门控弱化为按 Swagger 类型恒显（1 红：可见性与启用接线）、删按钮注册（1 红：远端动作区注册）、门控方向反转 `is not null → is null`（5 红：门控真值表、互补断言与三处说明文字断言）、DESIGN.md §8 条款行缺失（1 红：条款断言）、文案常量漂移（4 红：文案逐字与三处说明文字断言）。
- **验证状态**：还原后 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 605 用例全部通过（含令牌接线与 G-D4 机械检查对改动文件的扫描——无禁词、无档外值、无就地值）。**重冻结入口的 Windows 真机观感与「重冻结后旧检查结果按 expired 如实呈现」的界面表现仍未验证**（G-D5 复核）——Windows 真机需按「配置 Swagger 的任务首次检查（冻结）→ 注释回写 + 定向构建 → 远端区出现『重新加载 Swagger 并检查』→ 点击后检查基于新副本」走一遍。纯呈现层：`CheckService`/`OpenApiSource` 一行未动，检查仍只读、不写入远端；本轮未连接或写入 Apifox，未调用任何模型服务。

## 原生切片「列表列排序：点击列头按该列排」（2026-11-08 第四十七轮）

- **接续前确认**：本轮开局工作树不干净——`Desktop/Native/ListSort.cs` 为未跟踪新文件、`Desktop/Native/TasksPage.cs` 已改（上一轮留下的半程「列排序」实现），HEAD（3958e2b）本身构建与 605 用例全绿。先逐行复核半程实现再补完：`ListSort` 四条口径（`None`/`Toggle`/`Header`/`Order`）与任务页接线（列头点击、原标题快照、排序键与建列顺序一一对应）经核对语义自洽、可编译（Linux 主工程 `dotnet build` 0 警告 0 错误），未回退任何一行；缺口在别处——**口径没有编入测试工程、没有任何用例、代码注释引用的 DESIGN.md §6「列表列排序」条款在文档里并不存在**，且只有任务页接线、项目配置页列表仍无排序入口（与第三十四/三十七/四十轮「同族纪律两页一致」的既有口径不一致）。本轮补完这四件事，不重写已完成部分。
- **缺陷机理（具名 pass：clarify，纯呈现层）**：两个主页面列表此前只有数据源固定顺序（`TaskStore.List` 按创建时间倒序、`ProfileStore.List` 按名称升序），`ListView.Sorted` 缺省 None，点击列头没有任何反应。日常排障要看的是「哪些任务更新时间最近」「哪个任务接口数为 0」「现在都在等同一步的有哪些」，固定顺序下用户只能逐行扫一列，任务与配置越多越费眼；冻结页面表格同为固定顺序，原生补的是 Windows 列表惯例里本就存在的手势（资源管理器列头点击排序），不新增任何视觉语言与颜色。
- **实现口径**：口径集中在不依赖 WinForms 的共享 `Desktop/Native/ListSort`（与测试工程单一编译）——`Toggle`：同一列再点反向、换一列一律从升序开始（不带旧方向）；`Header`：排序列追加 ▲/▼、其余列回原标题，取值来自建列时快照的原标题数组，每次重算故反复刷新不叠加标记；`Order`：`None` 保持数据源顺序，比较后以行序号打破同值（升序降序都稳定——反向只反转有差异的行，不把手风琴里同值的行打乱重排），缺键（null、列号越界）视为最小、不抛异常也不丢行，文字按当前区域比较（中文与大小写混排与资源管理器同序，不按码位排），数值与时间戳按值比较。任务页七列、配置页六列的排序键与单元格取值同源：数量列按 `Endpoints`/`Candidates` 数值（单元格零显示「—」不影响排序）、更新时间列按完整 `UpdatedAt`（任务页单元格只显示到月-日-时-分，跨年顺序仍正确）、Apifox 列按 `TaskDisplay.RemoteStatusText`、下一步列按 `NextStep.Compute(task).Title`（纯内存推导，与单元格同一口径）、配置页 Apifox 项目与分支按单元格同一「未绑定/主分支」回退值。排序状态留在本页，`LoadTasks`/`LoadProfiles` 是唯一重建入口（任务页 13 处、配置页全部重建路径共用），刷新、作业结束、切页回来与保存后都按同一列同一方向呈现；只改呈现顺序——不写任务与配置数据、选中按 Id 恢复、列宽仍由 `ListColumns` 口径算、表头标记字面不在两页就地拼写；服务层一行未动。
- **测试 605 → 624 用例**：新增 `ListSortTests`（19）——`Toggle` 首次点击恒升序与同列反向、`Header` 只标排序列/未排序回原标题/按原标题重算不叠加、`Order` 未排序保持源顺序且返回新列表、列排序升降两向、同值行两方向都稳定、缺键视为最小与列号越界按源顺序、文字按当前区域比较（前置断言本机区域比较确实不同于码位比较，无 ICU 环境如实失败不空过）、不改动入参列表；接线机械检查两页各 7 条（初始 `ListSort.None`、原标题快照、ColumnClick 行首正则、`Toggle` 赋值行、`Order` 取值行、`EndUpdate → RefreshSortHeaders → FitColumns` 相邻锚点、`Header` 重算行）；「排序臂与建列一一对应」机械检查（按 `_list.Columns.Add` 计数比对 switch 臂索引序列 0..n-1——界面加列而排序键未跟上即红）、两页排序键与单元格同源断言（含禁止在 SortKey 里用 `ToString` 显示文字）、标记字面不在两页出现（单行引号内扫描，注释不算）、csproj 共享编译清单、DESIGN.md §6/§8 条款。
- **变异验证七处实测可红**（各自独立变红、还原以基准副本覆盖并 `diff` 核对干净）：`Toggle` 换列也取反方向（2 红：首次点击升序与同列反向）、`Order` 降序改为整体反转列表（1 红：同值行稳定性）、`Header` 条件放宽为所有列都加标记（2 红：只标排序列与未排序回原标题）、任务页 ColumnClick 接线注释掉（1 红：接线行首正则，注释行同样骗不过）、配置页删一个排序臂（2 红：一一对应检查与同源断言）、DESIGN.md §8 条款行删除（1 红：条款断言）、csproj 删 ListSort 共享编译（测试工程编译失败——口径未共享时用例根本引用不到 `ListSort`，与「断言对准真实实现」同一目的）。
- **验证状态**：还原后 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 624 用例全部通过（含令牌接线与 G-D4 机械检查对改动文件的扫描——无禁词、无档外值、无就地颜色/字体/间距值）。**列排序的 Windows 真机观感仍未验证**（G-D5 复核，Linux 无法运行 WinForms GUI）——真机需按「任务页点『更新时间』列头 → 表头出现 ▼ 且顺序反转、再点回 ▲ → 点『接口』列头 → 从升序开始且更新时间标记消失 → 刷新/切页回来顺序保持 → 选中任务在重排后仍是同一个」与「配置页点『名称』列头排序后新建/保存配置仍定位到刚保存的行」各走一遍；`Ui.List` 表头经 `DrawDefault = true` 由系统绘制（已核对源码），标记文字能否随系统表头正常显示、窄窗口下标题截断后标记是否可见属真机项。本轮另如实记录两点未扩面的取舍：数字 ID 列（配置页「Apifox 项目」）按文字排序，等长 ID 与数值序一致、混长度时按字典序；排序状态不跨启动记忆（与冻结页面无排序能力一致，不新造持久化）。纯呈现层：任务与配置数据、服务链路一行未动；本轮未连接或写入 Apifox，未调用任何模型服务。

## 原生切片「列表筛选：输入即筛选、Esc 清空」（2026-11-08 第四十八轮）

- **接续前确认**：上一轮工作树干净，HEAD（8ba7c19）的 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误、624 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现（列排序的 `ListSort` 与两页接线一行未动，只把「重建入口」的排序行改为「先筛选再排序」）。
- **缺口（具名 pass：clarify，纯呈现层）**：两个主页面列表此前只有固定顺序与列排序，任务与配置会一直累积——要找到「上次那个任务」「某个配置下的任务」只能逐行扫一列；冻结页面的表格同样没有筛选入口，原生补的是 Windows 列表惯例里本就存在的手势（资源管理器/文件对话框的输入即筛选），不新增视觉语言与颜色。
- **实现口径**：口径集中在不依赖 WinForms、与测试工程共享编译的 `Desktop/Native/ListFilter`——关键词按空白切成多个词，每个词都要在至少一个字段里出现（词间「且」、字段间「或」），按当前区域忽略大小写比较，空关键词即全部行，缺值字段按空处理；`Apply` 无关键词时返回数据源顺序的副本（与 `ListSort.Order` 同一纪律）。两页工具栏各加一个筛选框（`Ui.Input` 出厂、`Ui.Field` 包 1px 描边、占位文字取共享常量），输入即筛选、Esc 清空（空框按 Esc 不吞键）；筛选先于排序（`ListFilter.Apply` → `ListSort.Order`），筛选状态与排序状态都留在本页，刷新、作业结束与切页回来后的重建按同一关键词同一列同一方向呈现；筛选字段与单元格同源（任务=任务名/配置名/下一步标题/Apifox 状态文字，配置=名称/API 项目/Apifox 项目/分支/Swagger 来源，占位值「未绑定/主分支」与单元格同一取值）。
- **空态与计数如实**：筛到零行时空态换成「没有匹配的任务/配置」并给出清空筛选的下一步（此前会显示成「还没有任务」，把筛选结果说成没有数据）；状态行给出「筛选出 N 个任务（共 M 个）」。任务页选中改为按 Id 跟踪（与配置页同一口径，局部取值在清空列表之前），被筛掉的任务在清空筛选后仍回到选中。
- **测试 624 → 641 用例**：新增 `ListFilterTests`（17）——命中口径真值表（空关键词/去空白/字段间或/词间且/大小写与子串/缺值字段）、`Apply` 保持数据源顺序与返回新列表、筛选与排序组合、文案单一落点与两页不就地拼写、csproj 共享编译清单、两页接线行首正则与方法体切片（含「筛选先于排序」同一行断言、「选中 Id 在清空列表之前取局部」两处锚点存在性 + 顺序断言、空态两态与状态行计数）、两页筛选字段与单元格同源、DESIGN.md §6/§8 条款。
- **变异验证八处实测可红**（各自独立变红、还原以基准副本覆盖）：词间「且」改「或」（5 红）、`Apply` 直接返回全部行（2 红）、任务页 `TextChanged` 接线注释掉（1 红）、先排序后筛选（2 红）、选中 Id 局部取值挪到清空列表之后（1 红——首版断言只比 IndexOf 大小，锚点缺失时 -1 会骗过检查，已补两处锚点存在性断言后重做）、删 `RefreshEmptyState` 接线（1 红）、删 DESIGN.md §8 条款行（1 红）、删 csproj 共享编译（测试工程 80 个 CS 错误，编译失败双重可红）。
- **验证状态**：还原后 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 641 用例全部通过（含令牌接线与 G-D4 机械检查对新增文件与两页改动的扫描——无禁词、无档外值、无就地颜色/字体/间距值）。**筛选框的 Windows 真机观感仍未验证**（G-D5 复核，Linux 无法运行 WinForms GUI）——真机需按「任务页输入『订单』→ 列表只剩命中行、状态行显示筛选计数 → 按 Esc 清空回到全部 → 选中某任务后筛选到零行再清空，选中回到原任务 → 配置页同法」走一遍；筛选框在工具栏中的位置与占位文字截断属真机项。如实记录两点未扩面取舍：筛选不跨启动记忆（与排序状态同一取舍）、筛选只覆盖两个主页面列表（对话框内列表条目有限，不扩面）。纯呈现层：任务与配置数据、服务链路一行未动；本轮未连接或写入 Apifox，未调用任何模型服务。

## 原生切片「列表筛选键盘入口：Ctrl+F 聚焦当前页筛选框」（2026-11-08 第四十九轮）

- **接续前确认**：上一轮工作树干净，HEAD（cc16572）的 Linux 主工程 `dotnet build` 0 警告 0 错误、641 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现（第四十八轮的 `ListFilter` 口径、两页筛选框与 Esc 清空接线一行未动，本轮只补键盘入口）。
- **缺口（具名 pass：clarify，纯呈现层）**：上一轮落地的两页筛选框只有鼠标路径可达——列表变长后每次筛选都要先把手移到工具栏点进框，再输入关键词；筛选是「在列表中查找」这一日常动作，Windows 惯例本就有 Ctrl+F 这一手势，原生壳此前只有 Ctrl+1/Ctrl+2 切页、F5 刷新、F1 关于四个壳级快捷键，没有到筛选框的键盘入口。补的是既有手势的接线，不新增视觉语言、颜色或档位。
- **实现口径**：手势登记在 `Desktop/Native/ShellShortcuts` 单一口径——新增 `Find`（action `find`、手势 `Ctrl+F`），`All` 由四项增至五项（顺序 projects/tasks/refresh/find/about），`ReservedEditingKeys` 前置校验确认 `Ctrl+F` 不属被保护的编辑键（Ctrl+C/V/X/A/Z/Y）；壳在「视图」菜单新增 `BoundItem("筛选当前列表(&F)", ShellShortcuts.Find, FocusListFilterOnVisiblePage)`，手势由 `KeysConverter` 接 `ShortcutKeys`、绑定与显示同取共享口径值；分发方法 `FocusListFilterOnVisiblePage` 与既有 `RefreshVisiblePage` 同一纪律（退出未开始、壳未释放、只对当前可见页动作、模态对话框期间主菜单不路由），经页面协议 `INativePage` 新增成员 `FocusListFilter()`；两页各自实现把焦点交给本页 `_filter` 并 `SelectAll` 选中已有关键词（重输即替换，与 Windows 搜索框一致）——只移焦点，不改筛选关键词的值、不重建列表、不写任何数据，服务层一行未动。
- **测试 641 → 643 用例**：`ShellShortcutsTests` 接线扩展——`All_RegistersFiveConventionalEntries`（数量 4→5、动作序含 find、去重不变）、`Gestures_AreWindowsConventions`（补 `Ctrl+F`）、`GestureOf` 解析 `find`、`Shell_BindsMenuItemsFromSharedEntries`（新增「筛选当前列表(&F)」绑定行首正则）、新增 `Shell_RoutesFindToVisiblePageFilter`（分发方法声明 + 两页可见页 `FocusListFilter()` 行首正则）与 `Pages_ImplementFocusListFilter`（协议声明、两页 `FocusListFilter` 方法、`_filter.Focus()` 与 `_filter.SelectAll()` 接线）；`Gestures_AvoidEditingKeys` 前置对全集生效（Ctrl+F 若误占编辑键即红）。
- **变异验证五处实测可红**（各自独立变红、还原以基准副本覆盖并 grep 核对无残留、还原后 643 全绿）：`Find` 手势 `Ctrl+F` 改 `Ctrl+G`（2 红：Gestures 与 GestureOf）、`Find` 从 `All` 全集删除（2 红：All 数量与 GestureOf find 返回 null）、删壳对任务页的 `FocusListFilter` 分发行（1 红：Shell_RoutesFindToVisiblePageFilter）、注释掉视图菜单「筛选当前列表(&F)」绑定行（1 红：Shell_BindsMenuItemsFromSharedEntries）、注释掉任务页 `_filter.SelectAll();`（1 红：Pages_ImplementFocusListFilter——行首正则证实「被注释也算缺失」）。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误；`ApifoxHelper.Tests` 643 用例全部通过（含令牌接线与 G-D4 机械检查对改动文件的扫描——无禁词、无档外值、无就地颜色/字体/间距值）。**Ctrl+F 的实际按键路由、聚焦后关键词被选中的观感随 G-D5 真机复核**（Linux 无法运行 WinForms GUI）——真机需按「在检查任务页按 Ctrl+F → 焦点落进筛选框、已有关键词整段选中 → 直接输入替换、列表即时筛选 → 配置页同法」走一遍。如实记录取舍：本轮只补键盘入口，不改筛选口径、不加清空按钮、不跨启动记忆筛选状态（与第四十八轮同一取舍）；纯呈现层，本轮未连接或写入 Apifox，未调用任何模型服务。

## 原生切片「空态唯一行动按钮：新建与清空筛选按状态切换」（2026-11-08 第五十轮）

- **接续前确认**：上一轮工作树干净，HEAD（c8b8484）的 Linux 主工程 `dotnet build` 0 警告 0 错误、643 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现（第四十八轮的筛选口径、两页筛选框与第四十九轮的 Ctrl+F 接线一行未动，本轮只补空态那一侧的下一步）。
- **缺口（具名 pass：clarify，纯呈现层）**：第四十九轮把「不加清空按钮」列为取舍，本轮按逐屏对照重新判定后处理它——理由是发现了具体的不可达路径而不是观感偏好。三个事实：①§7 状态表模板要求空态给「说明现在有什么可做 + 唯一行动按钮」，任务页空态只有两行文字、根本没有行动按钮，配置页有一个但与工具栏是同一个命令的**两个实心主按钮**（同屏两个实心「新建配置」，违反 §1.3「每屏只有一个实心主按钮」）；②筛到零行时配置页空态给的动作仍是「新建配置」，而真实下一步是清空筛选——把用户引向创建第二份配置；③提示语写着「或按 Esc 清空筛选框」，而 Esc 只挂在筛选框的 KeyDown 上：焦点在工具栏、菜单上，或切页回来后落在别处时按它没有任何反应，提示所指引的恢复路径不可达（与第四十二轮「禁用不得移除提示所指引的下一步」、第四十六轮「说明文字不指向界面上不存在的按钮」同族）。
- **实现口径**：空态行动按筛选状态切换、同一时刻只显示一个——没有数据=「新建任务/新建配置」，动作与工具栏**同一入口方法**（任务页 `ShowNewTaskDialog()`、配置页 `ShowDialog(null)`，不复制逻辑）；筛到零行=「清空筛选」，与筛选框聚焦时的 Esc **共用同一条清空路径** `ClearListFilter`（清空 → 既有 TextChanged 重建 → 焦点回筛选框，接着输入不用先点回去）。两者一律 ghost，实心主按钮只在工具栏那一个；文案单一落点：「清空筛选」取 `ListFilter.ClearActionLabel`（提示语与按钮同源，`TaskNoMatchHint`/`ProfileNoMatchHint` 改为指那个按钮、不再指 Esc），新建文字收为本页常量 `NewTaskLabel`/`NewProfileLabel`（此前配置页 `"新建配置"` 字面写两处）。空态面板多出一行装行动按钮容器（配置页原有那一行，任务页与配置页同形态同列位）。只改呈现：不写任务与配置数据、筛选与排序口径一行未动、服务层未触达。
- **测试 643 → 654 用例**：新增 `EmptyStateActionTests`（11）——按钮文字与提示语同源（`ClearActionLabel` 出现在两条 hint 里、hint 不再含 Esc）、「清空筛选」字面不在两页就地拼写（只扫字符串字面、注释里的引述不算）、两页接线行首正则（ghost 出厂、动作与工具栏同一入口、按钮入容器、`BuildEmpty(actions)` → `layout.Controls.Add(actions, 0, 3)`）、切换两行在 `RefreshEmptyState` 且重建里接线、Esc 与按钮汇聚到 `ClearListFilter` 同一路径（含「Esc 分支不得自己赋空值」禁令）、空态不得再出现实心主按钮（`Ui.Primary(标签)` 每页恰一次 + `Ui.Primary("新建…")` 字面禁令）、任务页无数据空态的说明文字指名的入口就是那个按钮、DESIGN.md §6/§7/§8 条款。`ListFilterTests.Copy_PlaceholdersAndEmptyTextsAreDistinct` 的「hint 含 Esc」改为「hint 含 ClearActionLabel」（口径确实换了，不是放宽）。
- **变异验证七处实测可红**（各自独立变红、还原以基线副本覆盖并 `diff` 核对无残留、还原后 654 全绿）：删任务页 `_emptyClear.Visible = filtered;`（1 红）、配置页空态新建按钮改回 `Ui.Primary`（2 红：接线正则与实心主按钮禁令）、`TaskNoMatchHint` 改回含 Esc 的旧文案（1 红）、任务页 Esc 分支改回直接赋空值绕过 `ClearListFilter`（1 红）、删配置页空态新建按钮的 Click 接线（1 红）、删 DESIGN.md §8 本轮条款行（1 红）、任务页空态按钮的行列位 `0, 3` 改 `0, 4`（1 红）。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误，654 用例全部通过（含令牌接线与 G-D4 机械检查对三个改动文件的扫描——无禁词、无档外值、无就地颜色/字体/间距值）。GUI 真机不可运行本轮再次实测确认：`dotnet run` 启动即报缺 `Microsoft.WindowsDesktop.App` 8.0.0 运行时。**空态按钮的观感、Tab 到按钮后回车执行、筛到零行点「清空筛选」后列表恢复与选中回到原任务，随 G-D5 真机复核**——真机需按「任务页筛选到零行 → 空态标题是「没有匹配的任务」、只有一个「清空筛选」按钮、实心主按钮只有工具栏那一个 → 点它 → 列表恢复全部、焦点在筛选框 → 清空筛选框回到无数据态（删掉所有任务时）→ 空态给「新建任务」且与工具栏同一对话框 → 配置页同法」各走一遍。如实记录本轮未扩面：无数据空态的文字仍只在任务页指名真实入口（配置页那句「先绑定代码仓库与 Apifox 项目」讲的是要做的事、不指向按钮，未改）；筛选框失焦后按 Esc 仍不清空（那属壳级命令键，另有其取舍与风险，不在本轮）；空态按钮不跨状态记忆。纯呈现层：本轮未连接或写入 Apifox，未调用任何模型服务。

## 原生切片「列表键盘通路：筛选框 Enter 到结果第一行、列表行 Enter 打开选中行」（2026-11-08 第五十一轮）

- **接续前确认**：上一轮工作树干净，HEAD（8008206）的 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误、654 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现（第四十八轮筛选口径、第四十九轮 Ctrl+F、第五十轮空态行动按钮一行未动，Esc 那两行逐字保留，本轮只把键盘路径往前接一段）。
- **缺口（具名 pass：clarify，纯呈现与焦点层）**：第四十九轮把焦点交给筛选框就结束了——筛完之后键盘走不出那个框：筛选框里按 Enter 此前没有任何动作（壳窗体不设 AcceptButton——这一前提由用例钉住；Enter 落在列表控件自身而它不处理该键），筛完要看某一行结果仍须回鼠标点进列表；配置页列表又只有双击这一条路能打开该行的编辑，键盘用户既打不开配置、也无法从筛选结果继续。上一轮把「筛选框失焦后按 Esc 仍不清空」列为取舍（那属壳级命令键，风险另一本账），本轮处理的是它前面那段更短的通路，不是同一件事：Enter 到结果第一行是搜索框到结果的既有 Windows 手势，不新造视觉语言、不加控件、不加颜色。
- **实现口径**：判定集中在不依赖 WinForms、与测试工程单一编译的 `Desktop/Native/ListKeys`——`IsEnterGesture`（裸 Enter 才是手势，带 Ctrl/Alt/Shift 的组合留给控件自身、不吞键、不给既有组合键发明新语义）、`FirstResultIndex`（目标行是显示序第一行，即筛选与排序之后的可见顺序；可见行数为 0 返回 null，没有目标）。两页筛选框 KeyDown 各加一个 Enter 分支接 `FocusFirstResult`：列表先取焦点、选中经 `Ui.SelectItem` 唯一入口（Selected + Focused，方向键从该行继续，与鼠标点同一行、与重建恢复选中同一条路，不新造选中路径）、再 `EnsureVisible` 把该行滚入可见区——否则「焦点交给第一行」这句话不成立；选中即出详情、配置页「编辑/连接与写入探测」按钮随之启用，全部由既有 SelectedIndexChanged 承担，页面不额外调用重建、不改筛选关键词、不写任务与配置数据。配置页列表行 Enter 打开该行编辑：`Ui.ActivateItem` 新增 `byEnter`（默认 false），与双击共用同一条 activate 回调，键盘入口以 `FocusedItem` 为准、焦点行缺失即不动作——与第三十九轮「命中哪行激活哪行、不借当前选中」同一纪律，键盘不得做出与鼠标不同的事。接线范围按可核实的语义划界：对话框不接（各自设 AcceptButton，§6「对话框 Enter=确认、Esc=取消」既有语义保持不变），任务页列表没有「打开该行」这一步（选中即出详情）故本轮不为它发明动作，`TaskPage_DoesNotInventARowAction` 把这一边界钉住而不是留成口头取舍。§6「菜单/快捷键」行同步分册：控件级 Esc/Enter 不进 `ShellShortcuts` 快捷键册、不占菜单（菜单里没有对应项，显示出来反而是假入口），壳无 AcceptButton 这一前提由用例钉住。服务层一行未动。
- **测试 654 → 672 用例**：新增 `ListKeysTests`（17）——裸 Enter 真值表六组（含 Shift+Ctrl 与 Alt+Ctrl 组合）、目标行真值表五组（0 与 -1 无目标、1/2/40 都是第一行）、两页 Enter 分支行首正则与「Enter 分支必须在 Esc 守卫之前」顺序断言（两处锚点先各自验存在，缺失时 -1 骗不过检查——沿用第四十八轮教训）、两页 `FocusFirstResult` 切片（口径调用、`Ui.SelectItem`、`EnsureVisible`、「先取焦点再选中」顺序、页内不得赋 `_filter.Text` 也不得调 `LoadTasks/LoadProfiles`）、任务页不发明行级动作、壳窗体无 AcceptButton、csproj 共享编译清单、DESIGN.md §6/§8 条款。`ListActivationTests` +2（默认不接 Enter 的 `!byEnter` 短路、Enter 的修饰键与焦点行双守卫、动作前吞键、Enter 与双击共用同一个 activate 回调——计数恰为 2），并把 `ActivateItem` 签名正则收紧为含 `byEnter = false` 的完整签名（口径确实扩展，不是放宽）；两证据对话框补「不得出现 byEnter」一条。
- **变异验证十二处实测可红**（各自独立变红、还原以基线副本覆盖并 `diff` 核对干净、还原后 672 全绿）：`IsEnterGesture` 恒真（5 红：真值表五组组合）、`FirstResultIndex` 忽略零行恒返回第一行（2 红）、任务页删掉 `FocusFirstResult()` 调用（1 红）、配置页 Enter 分支挪到 Esc 守卫之后（1 红——可达性被短路，正是顺序断言要防的）、配置页删 `EnsureVisible`（1 红）、任务页绕过 `Ui.SelectItem` 直接赋 `Selected`（2 红：唯一入口与切片）、配置页删 `_list.Focus()`（1 红：顺序断言）、`Ui` 丢掉 `!byEnter` 短路（1 红）、候选对话框误接 `byEnter: true`（1 红）、壳窗体出现 AcceptButton（1 红）、删 DESIGN.md §8 本轮条款行（1 红）、删 csproj 共享编译（测试工程编译失败）。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误，672 用例全部通过（含令牌接线与 G-D4 机械检查对新增文件与四处改动文件的扫描——无禁词、无档外值、无就地颜色/字体/间距值）。口径是真执行：`ListKeys.cs` 经共享编译进测试程序集，真值表跑的就是主工程同一份代码，无需临时工程。GUI 真机不可运行本轮再次实测确认（启动即缺 `Microsoft.WindowsDesktop.App` 运行时）——**Enter 的实际按键路由、筛选框 Enter 后焦点与选中落在结果第一行、方向键从该行继续、`EnsureVisible` 的滚动对位、配置页行上 Enter 打开编辑对话框，均随 G-D5 真机复核**；真机需按「任务页 Ctrl+F → 输入关键词 → Enter → 焦点与选中在结果第一行、右侧详情是该任务 → 方向键逐行看 → 配置页同法后再按 Enter → 打开该配置的编辑对话框 → 筛到零行按 Enter 不动作、点空态『清空筛选』仍回到全部 → 列表获得焦点但没有焦点行时按 Enter 不动作」各走一遍。如实记录本轮未扩面：筛选框失焦后按 Esc 仍不清空（壳级命令键，另册取舍不变）；任务页列表 Enter 无动作（没有「打开该行」这一步）；筛选/排序/焦点状态都不跨启动记忆；`ListKeys` 只管这两个手势，不给 Ctrl/Alt/Shift 组合 Enter 定义任何语义。纯呈现与焦点层：未连接或写入 Apifox，未调用任何模型服务。

## 原生切片「列表 Ctrl+C 复制选中行」（2026-11-08 第五十二轮）

- **接续前确认**：上一轮工作树干净，HEAD（1b4cf33）的 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误、672 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现（第四十七轮列排序、第四十八轮筛选、第四十九轮 Ctrl+F、第五十轮空态行动按钮、第五十一轮 Enter 通路一行未动，`Ui.ActivateItem` 的 Enter 分支与两页筛选框的按键处理逐字保留，本轮只在同一个 `Ui.List` 工厂上多装一个控件级手势）。
- **缺口（具名 pass：clarify，纯呈现与焦点层）**：第二十三轮把列表选中态改为 `Ui.List` 工厂自绘（DrawItem/DrawSubItem 逐格用 TextRenderer 画字），单元格文字从此不可选中——这本身不是缺陷，但它把一个既有缺口暴露成死路：**整个原生界面没有任何把文字交给用户的通路**。逐仓核实：无 Clipboard 调用、无右键菜单、无「复制」按钮，唯一能选中复制的是 `Ui.Preview` 只读预览框（报告源文、差异文本）。而产品恰好有一条要抄写同一段文字的路径：新建任务对话框的「指定接口」要求逐行输入「GET /路径」（第二十八轮落地，解析在 `TaskCreation` 与 `TaskStore.Create` 单一口径），这串接口身份只出现在检查结果对话框、补充说明对话框、同步预览接口清单（`check.Method + " " + check.Path`）与契约审阅、接口追踪清单（`contract.EndpointKey`、`coverage.EndpointKey`）的首列——看到它的那一屏和要用它的那一屏之间，此前只能靠肉眼重敲。补的是 Windows 列表里本就存在的手势（资源管理器 Ctrl+C 复制选中项名称列），不新造视觉语言、不加控件、不加颜色、不加提示。
- **实现口径**：判定集中在不依赖 WinForms、与测试工程单一编译的 `Desktop/Native/ListCopy`——`IsCopyGesture`（只认裸 Ctrl+C；带 Shift/Alt 的组合不属本口径、留给控件自身，不吞键也不给既有组合键发明新语义，与第五十一轮 `ListKeys.IsEnterGesture` 同一写法）、`CopyText`（取焦点行首列原文，不拼其余列、不裁剪、不改写大小写——粘贴出去的就是看到的那一格；焦点行未选中或首列无文字返回 null）。接线只装在 `Ui.List` 工厂一处（与自绘、复选框补画同一地点）：列表一律经工厂出厂（第二十三轮起机械检查就钉住这一点），故复制天然也只有一份，19 处列表全覆盖，页面与对话框不各写一套；处理顺序为「修饰键判定 → 取焦点行 → 取文字 → 吞键 → 写剪贴板」，三重守卫缺一不动作。焦点行与选中态一起判定（`row.Selected`）是刻意的：只复制屏幕上真正被标出来的那一行（自绘的 Bg 底 + 3px Accent 标记就是那个标记），且绝不把用户先前复制的内容抹掉——焦点行缺失、未选中、首列空文字三种情况一律不触剪贴板，与第三十九轮「命中哪行激活哪行」、第五十一轮「焦点行缺失即不动作、不借当前选中」同一纪律。剪贴板可能被其他进程占用（`ExternalException`）：不重试、不弹窗、不谎称已复制，界面上也不出现任何「已复制」的成功暗示（无提示与资源管理器一致，粘贴即见实际内容）。手势不进 `ShellShortcuts` 快捷键册（菜单里没有「复制」项，显示出来反而是假入口；Ctrl+C 本来就在 `ReservedEditingKeys` 保留清单内、壳不抢），因此焦点不在列表上时它照旧是文本框自己的复制——筛选框与详情区输入框的行为一行未变。服务层一行未动：不写任务与配置数据、不写源码、不连接 Apifox。
- **测试 672 → 694 用例**：新增 `ListCopyTests`（22）——裸 Ctrl+C 真值表七组（含 Shift+Ctrl、Alt+Ctrl 与裸 C 打字）、目标文字四组判定（原文逐字返回、未选中为 null、空与全空白为 null、无文字为 null）、`Ui.List` 工厂切片内的三重守卫行首正则、「先吞键再写剪贴板」顺序断言与 `catch (ExternalException)` 存在、只取首列不遍历 SubItems 与就地修饰键判定禁令、剪贴板在整个 Desktop/Native 只触达一次（行首匹配，被注释掉的调用不算接线）、其他界面文件不出现 `Keys.C` 与 `ListCopy.`、复制不是壳级快捷键（`GestureOf("copy")` 为空、保留清单含 Ctrl+C、壳源码不出现该手势）、csproj 共享编译清单、DESIGN.md §6/§8 条款。
- **变异验证十四处实测可红**（各自独立变红、还原以基线副本覆盖并 sha1 逐字节核对干净、还原后 694 全绿）：`IsCopyGesture` 恒真（6 红：真值表六组组合）、`CopyText` 去掉选中守卫（2 红）、复制前裁剪文字（1 红）、空文字也返回（2 红）、Clipboard 写入被注释（2 红）、删吞键行（1 红）、吞键排在写入之后（1 红）、就地写修饰键判定（2 红：接线正则与禁令）、不校验选中态（1 红）、改经 SubItems 取列（2 红）、同一逻辑再写一次剪贴板（1 红）、catch 换异常类型（1 红）、DESIGN.md §8 条款改名（1 红）、任务页自装一套复制（1 红）；另删除 csproj 共享编译表现为测试工程编译失败（CS0103）。**期间抓出一处机械检查盲区并当场修复**：剪贴板「只触达一次」的检查原先用子串计数，被注释掉的调用同样计入命中，那条变异因此只红一处而非两处——改为行首正则（一行只允许空白开头再接 Clipboard）后该变异红二处；与第二十六轮「接线断言用子串 Contains 被注释行骗过，改行首正则后变异可红」同族教训。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误，694 用例全部通过（含令牌接线与 G-D4 机械检查对新增文件与 Ui.cs 的扫描——无禁词、无档外值、无就地颜色/字体/间距值；ListCopy.cs 不依赖 WinForms，不含任何颜色与尺寸字面）。口径是真执行：`ListCopy.cs` 经共享编译进测试程序集，真值表跑的就是主工程同一份代码，无需临时工程。GUI 真机不可运行本轮再次实测确认（启动即报缺 `Microsoft.WindowsDesktop.App` 运行时）——**Ctrl+C 的实际按键路由（列表取得焦点时命中工厂、文本框内仍走自身复制）、复制文字与屏幕显示逐字一致、焦点行未选中或首列无文字时不动作（原剪贴板内容仍在）、剪贴板被其他进程占用时的表现，均随 G-D5 真机复核**；真机需按「检查结果对话框点一行接口 → Ctrl+C → 新建任务对话框『指定接口』粘贴出该接口身份且与首列逐字相同 → 任务列表 Ctrl+C 复制任务名 → 焦点回到筛选框后 Ctrl+C 仍是选中文本自身的复制（不被列表抢走）→ Shift+Ctrl+C 与 Alt+Ctrl+C 无动作 → 方向键移到未被标记的行时 Ctrl+C 不动作、剪贴板原内容保持 → 筛到零行 Ctrl+C 不动作」各走一遍。如实记录本轮未扩面：详情区与状态行的标签文字仍不可取（那是「整屏文字都该可出」的另一件事，要另起选择模型，不在一个手势的分寸里）；同步预览「写入清单」首列是序号，复制到的就是那一格数字——本轮不为它自造「哪一列才是标识」的第二套口径；无整行拼接、无跨列表多选复制（工厂出厂 MultiSelect 为 false）、不做右键菜单；新建任务对话框的模块清单是 CheckedListBox、不经 `Ui.List`，不在本轮范围（与第二十二轮 ComboBox、CheckedListBox 同册挂账）。纯呈现与焦点层：只写剪贴板，未连接或写入 Apifox，未调用任何模型服务。

## 原生切片「列表 Ctrl+C 复制的是标识列」（2026-11-08 第五十三轮）

- **接续前确认**：上一轮工作树干净，HEAD（4753559）的 Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误、694 用例全部通过，基线成立；本轮在其上推进，未回退任何已有实现（第四十七轮列排序、第四十八轮筛选、第四十九轮 Ctrl+F、第五十轮空态行动按钮、第五十一轮 Enter 通路、第五十二轮 Ctrl+C 逐字保留，本轮只把「复制哪一格」这一件事收进同一个口径）。
- **缺口（具名 pass：clarify，纯呈现与焦点层）**：第五十二轮把 Ctrl+C 装上时按「首列即标识列」取值，并在同一轮如实挂账「同步预览写入清单的首列是序号，复制到的就是那一格」。本轮核实这一挂账不是边角：`SyncPreviewDialog` 的写入清单列序是 `#`/动作/资源/远端 ID/改动/CLI 校验/状态，首列格子是 `plan.Actions` 的下标（屏幕上就是「3」），Ctrl+C 之后粘出来的是一个数字——既不能贴进新建任务的「指定接口」，又把用户先前复制的内容抹掉了，与第五十二轮自己立的「不把用户先前复制的东西抹掉」相抵。同族第二处是差异确认对话框（`DecisionsDialog`）：首列是「接口/模型」这种分类，许多行同值，认不出是哪一条，而名称格 `entry.Resource` 就在第二列。两张表恰好都是「看到身份的那一屏」与「要用身份的那一屏」之间的关键一跳（`action.ResourceKey`、`entry.Resource` 就是「GET /路径」这一串）。
- **为什么不写成给某张表开特例**：口径收成一条——**Ctrl+C 交出的是这一行的标识列**，标识列默认首列（沿用 §6「列表行」列宽条款与 `ListColumns` 的「标识列（首列）」同一约定，19 处列表中 17 处的名称格本来就在首列，一行代码不改），首列不是名称的列表在出厂处经 `Ui.List(identityColumn:)` 声明真正的名称列。两处声明各以一个具名常量落在自己的对话框里（`ActionResourceColumn = 2`、`ResourceColumn = 1`），紧挨着各自的字段与建列处；取值与越界守卫都在共享 `ListCopy`（`IdentityColumn(cellCount, declared)`：声明列不落在该行实际格数之内即返回 null），工厂不另判一套。默认值也取自口径常量 `ListCopy.DefaultIdentityColumn`，界面代码不写死 0。
- **认不出名称时宁可不动作**：列被增删而声明没跟上时，这一行没有可交出去的一格——不动剪贴板，更不把邻列的文字当成名称抄出去（粘进「指定接口」的错东西比复制不到更难发现）。工厂守卫链随之补为「修饰键判定 → 取焦点行 → 解析标识列 → 取文字 → 吞键 → 写剪贴板」，第五十二轮的三重守卫、组合键不吞键、剪贴板被占用（`ExternalException`）不重试不弹窗不谎称已复制全部原样保留。
- **测试 694 → 706 用例**：`ListCopyTests` 新增 `IdentityColumn` 真值表八组（范围内/首列/末列/单列/越界/负数/零格）、默认常量同源一条、两处接线各一条（常量值必须等于该列表里「资源」列的实际位次、字段确把常量交给工厂、首列标题确为 `#`/类型）、声明点清单一条（`identityColumn:` 只允许出现在这两个文件——新增声明要么改这份清单、要么先说明首列为什么不是名称）；「工厂只复制一格」的检查从上一轮的「切片内不得出现 SubItems」改写为等强度版本：`row.SubItems[` 恰一次、无 `string.Join`、无 `.Text +` 多格相加，并保留就地修饰键判定禁令；§6/§8 条款用例补上标识列口径。
- **变异验证十一处实测可红**（4/1/1/1/1/2/1/2/1/1/1，各自独立变红，还原以备份覆盖并 sha1 逐字节核对干净、还原后 706 全绿）：去掉 `IdentityColumn` 越界守卫、工厂恒取首列（忽略声明）、写入清单声明指错列（2→1）、建列顺序变更而声明未跟上、删掉工厂标识列守卫行、复制改成整行拼接、第三个列表擅自声明、写入清单退回不声明、差异确认常量改名使声明失联、默认值写死 0 不经口径、DESIGN.md §8 台账行改名。
- **验证状态**：Linux 主工程与测试工程 `dotnet build` 均 0 警告 0 错误，706 用例全部通过；`ListCopy.cs` 经共享编译进测试程序集，真值表跑的就是主工程同一份代码。GUI 真机仍不可运行（本机无 `Microsoft.WindowsDesktop.App` 运行时）——**写入清单与差异确认 Ctrl+C 复制到的是「资源」列文字、其余 17 处列表仍复制首列、列序与声明不符时不动作（原剪贴板内容保持）**随 G-D5 真机复核；真机需按「同步预览生成计划后点某一行 → Ctrl+C → 粘进任意文本框得到的是该行的资源标识而不是序号 → 差异确认列表同法 → 任务列表与检查结果列表 Ctrl+C 仍复制首列 → 把写入清单的列删到声明之外后 Ctrl+C 无动作且原剪贴板内容仍在」各走一遍。如实记录本轮未扩面：注释缺口/候选审阅/接口追踪/能力探测等清单的首列（位置、分类、检查项）本身就是那一行在屏幕上被读作的名字，按默认走、不为它们另起判断；详情区与状态行的标签文字仍不可取（需另起选择模型）；无整行拼接、无多选复制、不做右键菜单。纯呈现与焦点层：只写剪贴板，不改任务与配置数据、不写源码、筛选/排序/Enter 口径一行未动、服务层未触达。

## 实现步骤

1. 骨架：项目文件、隔离用 Directory.Build.props / Directory.Packages.props、基础设施。
2. 源码分析：无构建加载（csproj + project.assets.json + targeting pack）、接口发现、注释缺口、证据、响应契约。
3. 注释草稿与回写：规则提取 / 模型服务 / 材料文件交换、审阅、补丁、受限回写与 token 校验。
4. 文档契约：OpenAPI 读取、规范化比对、质量检查、Apifox 快照（export）、影响分析。
5. 同步：计划冻结、前态保存、逐项写入、回读核对、日志、基线。
6. 报告与前端工作台。
