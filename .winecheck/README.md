# wine 试跑脚本（开发期辅助，不属于产品运行路径）

本目录是开发期在 Linux 上用 wine 跑 Windows 桌面界面的辅助脚本：用来在没有 Windows 真机的环境里
把原生界面真的启动起来、点进去、截图看文字。**它们不参与构建、不被产品代码引用、不是用户路径**；
产品在 Windows 上的正常使用与这些脚本无关。

## 依赖

| 依赖 | 用途 | 实测版本 |
| --- | --- | --- |
| wine | 运行 Windows 版可执行文件 | wine-10.0（Ubuntu 打包版） |
| Xvfb | 无显示环境下的虚拟 X 服务器（脚本默认 `DISPLAY=:99`） | 随发行版 |
| xdotool | 找窗口、激活窗口、按坐标点击、真实键入 | 随发行版 |
| ImageMagick（`import` / `identify` / `convert`） | 截图与裁剪 | 随发行版 |
| tesseract（可选，中文需 `chi_sim` 语言包） | 把截图 OCR 成文字，核对界面上到底写了什么 | 随发行版 |

## 前置：先发布 Windows 版

```bash
dotnet publish ApifoxHelper.csproj -c Release -r win-x64 --self-contained -o /tmp/ah-pub
```

冒烟器（构造全部原生对话框并跑交互段）另有一份发布：

```bash
dotnet publish ApifoxHelper.Smoke/ApifoxHelper.Smoke.csproj -c Release -r win-x64 --self-contained -o /tmp/ah-smoke-pub
export WINEPREFIX=$HOME/.wine-ah
xvfb-run -a wine /tmp/ah-smoke-pub/ApifoxHelper.Smoke.exe --report=/tmp/ah-smoke-report.txt
```

**前缀必须是 `~/.wine-ah`（第一百六十六轮实测）**：不带 `WINEPREFIX` 走默认的 `~/.wine` 时，每一扇窗口都构造不出来——报告是 20 通过 / 115 失败，失败行全是 `Win32Exception：Error creating window handle`，三档缩放一样。那是那份前缀没有可用的桌面环境，不是产品的构造缺陷；照上面这一行设好前缀再判。

## 脚本

| 脚本 | 用途 |
| --- | --- |
| `run1.sh` | 启动主窗口（`--data` 指向资料目录），截图并 OCR 主窗口文字 |
| `run_app.sh` | 同上，但先按进程名清掉上一次的实例（反复试跑用） |
| `click.sh` | 在主窗口上按坐标点一下，再截图并 OCR「新建配置」对话框 |
| `fill.sh` | 在「新建配置」对话框里真实键入名称、仓库根目录与 API 项目路径，截图并 OCR |
| `fill2.sh` | `fill.sh` 的精简版：只填名称与 API 项目路径，只 OCR 对话框上半部分 |
| `demo.sh` | **降级答辩的一键复跑演示**：按 wine 三档缩放各跑一次冒烟，并把 `docs/NOTES.md`「降级答辩脚本」逐屏表的每一行与那一档的报告对上一次，逐档产出讲稿（用法见下一节） |

## 演示与录制口径（真机不可得时按此讲）

到期日 **2026-11-20** 仍拿不到 Windows 真机时，初审答辩按这一节讲。它是 `docs/NOTES.md`「降级答辩脚本」
一节的执行方式——**屏单只有一份，就是那张逐屏表**；脚本从表里读行，本 README 不再抄一份（抄的会漂）。

```bash
bash .winecheck/demo.sh                 # 三档各跑一次；产物在 /tmp/ah-demo
PUBLISH=1 bash .winecheck/demo.sh       # 冒烟器还没发布时，让脚本自己 dotnet publish
TIERS=96 bash .winecheck/demo.sh        # 只跑 100% 档（答辩前自查用，一集约 17 秒）
```

| 产物 | 内容 |
| --- | --- |
| `/tmp/ah-demo/report-<档位>.txt` | 该档冒烟报告原文，逐行 PASS/FAIL（缺省三档为 96 / 120 / 192） |
| `/tmp/ah-demo/narration-<档位>.txt` | 该档讲稿：每屏一段——屏名、讲什么、引用的段在该档通过几条失败几条、结论口径 |
| `/tmp/ah-demo/console-<档位>.log` | 该档 wine 控制台输出（报告没落盘时看这里） |

**录制与讲述顺序**：

1. 三档各录一段，录屏起点是脚本打出的「档位 LogPixels=…」那一行；屏幕固定 1600×1000（Xvfb）。
2. 按 `narration-<档位>.txt` 的行序讲（第 1 屏到最后一屏），每屏念「这一屏讲什么 / 这一段通过几条」。
   念的是报告里的数字，不是这里抄的一句话。
3. 结论口径写「源码 + 编译证据」的那几屏（生成文档核对 / 响应契约 / Apifox 差异检查 / 同步预览与执行）
   **不演示界面**，只说「代码与用例在，真实端到端未跑」，不拿别的屏冒充。
4. 结尾固定一句：**以上均为 Linux 容器内 wine 10.0 + Xvfb 模拟器证据，不是 Windows 真机结果；
   真机 0 台，G-D5 未达成（环境供给缺口），到期日 2026-11-20。**

脚本的失败方式（都不是静默的）：报告没落盘、逐屏表某一行不是 5 列、表里引用的段在报告里找不到、
或任何一屏的引用段有失败行——都非零退出，并打出是哪一屏哪一段。答辩前先看这一行，别带着失败念「wine 量到」。

跑完脚本把 `LogPixels` 改回 96（同一份前缀 `~/.wine-ah` 是共享的开发环境，别把下一个人留在 200% 档）。

## 可覆盖的环境变量

脚本里没有写死任何个人环境的绝对路径，全部可覆盖：

| 变量 | 默认值 | 含义 |
| --- | --- | --- |
| `WINEPREFIX` | `$HOME/.wine-ah` | wine 前缀（独立前缀，不污染默认 `~/.wine`） |
| `WINE_BIN` | `wine`（取自 PATH） | wine 可执行文件 |
| `EXE` | `/tmp/ah-pub/ApifoxHelper.exe` | 已发布的 Windows 版可执行文件 |
| `DATA` | `$HOME/ah-data` | 资料目录（`--data` 参数） |
| `DISPLAY` | `:99` | Xvfb 的显示号 |
| `AH_WS` | `$HOME/ah-ws` | 试跑用的工作区（脚本经 `winepath -w` 转成 Windows 路径） |
| `AH_PROJECT` | `Probe.Api/Probe.Api.csproj` | 工作区里的 API 项目相对路径 |
| `SHOT` / `CROP` / `LOG` | `/tmp/ah-shot.png` 等 | 截图、裁剪图与应用日志的落点 |
| `SMOKE_EXE` | `/tmp/ah-smoke-pub/ApifoxHelper.Smoke.exe` | `demo.sh` 要跑的冒烟器可执行文件 |
| `REPO` | 脚本所在目录的上一级 | 仓库根（`demo.sh` 从这里的 `docs/NOTES.md` 读逐屏表） |
| `NOTES` | `$REPO/docs/NOTES.md` | 逐屏表的出处——屏单只有这一份，脚本从它读行、不另抄一份 |
| `OUT` | `/tmp/ah-demo` | `demo.sh` 三档报告、讲稿与控制台日志的落点 |
| `TIERS` | `96 120 192` | 要跑的 `LogPixels` 档（答辩前自查可只给一档） |
| `PUBLISH` | `0` | 设 `1` 时 `demo.sh` 先自己 `dotnet publish` 冒烟器 |

## 产物位置

| 产物 | 位置 |
| --- | --- |
| 发布产物 | `/tmp/ah-pub`、`/tmp/ah-smoke-pub` |
| 截图与裁剪图 | `/tmp/ah-shot.png`、`/tmp/ah-dialog.png`、`/tmp/ah-dialog-top.png` |
| 应用日志 | `/tmp/ah-app.log` |
| 冒烟报告（UTF-8，逐行 PASS/FAIL） | `/tmp/ah-smoke-report.txt` |
| 演示三档产物（`demo.sh`） | `/tmp/ah-demo/report-<档位>.txt`、`/tmp/ah-demo/narration-<档位>.txt`、`/tmp/ah-demo/console-<档位>.log` |

## 已知限制（不要拿它当 Windows 真机证据）

- wine 的字体度量与 GDI 绘制不是 Windows 实现：**观感、DPI 缩放、字体替换的结论不能替代真机复核**。
- 脚本里的点击坐标随 DPI 与字体变化，换机器或换字号后要重新量。
- 冒烟器能证明「对话框构造得起来、控件点得动、数据落得盘」，不能证明「界面好看」。
