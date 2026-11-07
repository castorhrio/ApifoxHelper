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

## 实现步骤

1. 骨架：项目文件、隔离用 Directory.Build.props / Directory.Packages.props、基础设施。
2. 源码分析：无构建加载（csproj + project.assets.json + targeting pack）、接口发现、注释缺口、证据、响应契约。
3. 注释草稿与回写：规则提取 / 模型服务 / 材料文件交换、审阅、补丁、受限回写与 token 校验。
4. 文档契约：OpenAPI 读取、规范化比对、质量检查、Apifox 快照（export）、影响分析。
5. 同步：计划冻结、前态保存、逐项写入、回读核对、日志、基线。
6. 报告与前端工作台。
