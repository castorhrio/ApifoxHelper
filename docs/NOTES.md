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

## 实现步骤

1. 骨架：项目文件、隔离用 Directory.Build.props / Directory.Packages.props、基础设施。
2. 源码分析：无构建加载（csproj + project.assets.json + targeting pack）、接口发现、注释缺口、证据、响应契约。
3. 注释草稿与回写：规则提取 / 模型服务 / 材料文件交换、审阅、补丁、受限回写与 token 校验。
4. 文档契约：OpenAPI 读取、规范化比对、质量检查、Apifox 快照（export）、影响分析。
5. 同步：计划冻结、前态保存、逐项写入、回读核对、日志、基线。
6. 报告与前端工作台。
