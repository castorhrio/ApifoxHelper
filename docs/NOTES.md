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

## 实现步骤

1. 骨架：项目文件、隔离用 Directory.Build.props / Directory.Packages.props、基础设施。
2. 源码分析：无构建加载（csproj + project.assets.json + targeting pack）、接口发现、注释缺口、证据、响应契约。
3. 注释草稿与回写：规则提取 / 模型服务 / 材料文件交换、审阅、补丁、受限回写与 token 校验。
4. 文档契约：OpenAPI 读取、规范化比对、质量检查、Apifox 快照（export）、影响分析。
5. 同步：计划冻结、前态保存、逐项写入、回读核对、日志、基线。
6. 报告与前端工作台。
