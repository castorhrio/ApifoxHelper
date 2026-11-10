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

## 产物位置

| 产物 | 位置 |
| --- | --- |
| 发布产物 | `/tmp/ah-pub`、`/tmp/ah-smoke-pub` |
| 截图与裁剪图 | `/tmp/ah-shot.png`、`/tmp/ah-dialog.png`、`/tmp/ah-dialog-top.png` |
| 应用日志 | `/tmp/ah-app.log` |
| 冒烟报告（UTF-8，逐行 PASS/FAIL） | `/tmp/ah-smoke-report.txt` |

## 已知限制（不要拿它当 Windows 真机证据）

- wine 的字体度量与 GDI 绘制不是 Windows 实现：**观感、DPI 缩放、字体替换的结论不能替代真机复核**。
- 脚本里的点击坐标随 DPI 与字体变化，换机器或换字号后要重新量。
- 冒烟器能证明「对话框构造得起来、控件点得动、数据落得盘」，不能证明「界面好看」。
