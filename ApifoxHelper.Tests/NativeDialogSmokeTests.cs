using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 原生对话框冒烟构造器的机械检查（第一百二十九轮）。
/// <para>
/// 存在理由：第一百二十八轮在 wine 下发现「新建配置」对话框一打开就崩——<c>Ui.FieldGridPanel</c>
/// 构造函数里 <c>Padding</c> 先于 <c>ColumnStyles</c> 落地，<c>set_Padding</c> 同步触发
/// <c>ApplyColumnWidths()</c> 时列样式还是空集合，索引 <c>[0]</c> 抛
/// <c>ArgumentOutOfRangeException</c>。这类「编译绿、构造期崩」的缺陷，Linux 上的
/// <c>dotnet build</c> 与源码切片断言都拦不住（切片只能钉住已知的那一处顺序）。
/// 拦截手段是 <c>ApifoxHelper.Smoke</c>：把每个对话框真的 <c>new</c> 出来并建句柄。
/// </para>
/// <para>
/// 本文件钉的是**冒烟器自身的完备性**——它跑在 Windows 桌面运行时上（本环境不可执行），
/// 所以这里不冒充「跑过」，只保证「该构造的一个都不漏、构造方式不会退化成只 new 不建句柄」：
/// ① 磁盘上每个 <c>*Dialog</c> 类都在冒烟清单里；② 清单里没有磁盘上不存在的类；
/// ③ 每个用例都建句柄 + 强制布局；④ 结果按 UTF-8 落报告文件；⑤ 退出码语义；
/// ⑥ 冒烟工程不参与主工程编译、只对冒烟程序集放行 internal；⑦ 冒烟器不起服务端（不是第二个调度器）。
/// </para>
/// <para>
/// wine 模拟器执行证据在 <c>docs/NOTES.md</c> 的「Windows 真机 / wine 模拟器验证清单」一节（wine 10.0 + Xvfb 下
/// 27 通过 / 0 失败，退出码 0；把第一百二十八轮的修法退回后同一命令 7 失败、退出码 1）。
/// </para>
/// </summary>
public class NativeDialogSmokeTests
{
    private static string SmokeProgram => Repo.Source("ApifoxHelper.Smoke", "Program.cs");

    private static string SmokeProject => Repo.Source("ApifoxHelper.Smoke", "ApifoxHelper.Smoke.csproj");

    private static string MainProject => Repo.Source("ApifoxHelper.csproj");

    /// <summary>磁盘上 <c>Desktop/Native</c> 里所有继承 <c>Form</c> 的对话框类名（按名字排序）。</summary>
    private static List<string> DialogClassesOnDisk() =>
        Repo.NativeSources()
            // 基类自第一百七十五轮起是 DpiAwareForm（DPI 变更要在那一层接消息），两个名字都认——
            // 判据仍是「磁盘上真实存在的 *Dialog 类」，不看用例怎么写，也不放掉任何一个。
            .SelectMany(s => Regex.Matches(s.Text, @"(?m)^\s*(?:internal|public)\s+(?:sealed\s+)?class\s+(\w+Dialog)\s*:\s*(?:Form|DpiAwareForm)\b")
                .Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// 冒烟程序显式声明的对话框清单（<c>SmokeDialogNames</c>，第一百五十四轮）。
    /// <para>
    /// 此前这里用正则匹配 <c>new XxxDialog(</c> 来发现「冒烟登记了哪些对话框」。正则只认这一种写法：
    /// 有人把某个用例改成 helper 方法包装，正则就静默漏发现——那个对话框不再被构造，
    /// 而本文件全绿。现在清单是**数据**（<c>SmokeDialogNames</c> 常量），这里只读这份数据，
    /// 不看用例怎么写。
    /// </para>
    /// </summary>
    private static List<string> DialogClassesInSmoke()
    {
        var start = SmokeProgram.IndexOf("SmokeDialogNames =", StringComparison.Ordinal);
        Assert.True(start >= 0, "ApifoxHelper.Smoke/Program.cs 里找不到 SmokeDialogNames 清单常量。");
        var open = SmokeProgram.IndexOf('[', start);
        var close = SmokeProgram.IndexOf("];", open, StringComparison.Ordinal);
        Assert.True(open > start && close > open, "SmokeDialogNames 清单不是 [ … ]; 的形状，提取器失效。");
        var names = Regex.Matches(SmokeProgram[open..close], "\"([A-Za-z]\\w*)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();
        // 提取器失效时断言会静默空转（比没有断言更糟）：地板与去重都在这里判。
        Assert.True(names.Count >= 20, $"SmokeDialogNames 只提取到 {names.Count} 个类名，提取口径可能已失效。");
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        return names.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void EveryDialogClass_IsDeclaredInTheSmokeList()
    {
        // 漏登记 = 那个对话框永远不会被构造，第一百二十八轮那类缺陷在它身上照样能溜过去。
        // 判定是「磁盘类名 ↔ 清单数据」，与用例怎么写无关（helper 包装也漏不掉）。
        var onDisk = DialogClassesOnDisk();
        var declared = DialogClassesInSmoke();
        var missing = onDisk.Except(declared, StringComparer.Ordinal).ToList();
        Assert.True(
            missing.Count == 0,
            "以下对话框未在 ApifoxHelper.Smoke/Program.cs 的 SmokeDialogNames 里登记：" + string.Join("、", missing));
        // 地板：对话框数量掉下去说明扫描正则或目录坏了，检查会静默空转。
        Assert.True(onDisk.Count >= 20, $"只扫到 {onDisk.Count} 个对话框类，扫描口径可能已失效。");
    }

    [Fact]
    public void SmokeList_DeclaresNoDialogThatDoesNotExist()
    {
        // 反向：清单里留着已删除的类，冒烟工程根本编译不过——但这条断言让「删类忘了删清单」
        // 在 Linux 侧就报出具体名字，而不是等一次 Windows 编译。
        var onDisk = DialogClassesOnDisk();
        var stale = DialogClassesInSmoke().Except(onDisk, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0, "冒烟清单里有磁盘上不存在的对话框：" + string.Join("、", stale));
    }

    /// <summary>
    /// 清单是数据，但「登记了」不等于「构造了」（第一百五十四轮，简报 P1-1）。
    /// <para>
    /// 这一条钉住冒烟程序自己在运行时核对两件事：清单里每个类名都真的被构造出来、
    /// 构造出来的每个对话框都在清单里。少了它，清单可以是一份与用例脱节的死数据——
    /// Linux 侧比对全绿，而某个对话框其实从没被 new 过。
    /// </para>
    /// </summary>
    [Fact]
    public void SmokeProgram_ChecksDeclaredAgainstConstructedAtRuntime()
    {
        // 记录真实构造出来的类型（按控件实际类型，不按用例名）。
        Assert.Contains("_constructedDialogs.Add(form.GetType().Name);", SmokeProgram);
        // 核对方法本身：两个方向都要判，且判失败要落进报告与退出码。
        var body = MethodBody(SmokeProgram, "private static void CheckDialogCoverage(");
        Assert.Contains("SmokeDialogNames.Where(n => !_constructedDialogs.Contains(n))", body);
        Assert.Contains("_constructedDialogs.Where(n => !declared.Contains(n))", body);
        Assert.Contains("_failed++;", body);
        Assert.Contains("_lines.Add(fail);", body);
        // 必须在 Main 里真的调用，且排在结果行之前（否则失败不计入退出码）。
        var main = MethodBody(SmokeProgram, "private static int Main(");
        var call = main.IndexOf("CheckDialogCoverage();", StringComparison.Ordinal);
        var result = main.IndexOf("== 结果：", StringComparison.Ordinal);
        Assert.True(call >= 0, "Main 里没有调用 CheckDialogCoverage。");
        Assert.True(result > call, "CheckDialogCoverage 没有排在结果行之前（失败不会计入退出码）。");
    }


    /// <summary>
    /// P0 接线门：<c>Desktop/Native</c> 里每个 <c>*Dialog</c> 类的基类必须是 <c>DpiAwareForm</c>。
    /// <para>
    /// 存在理由：第一百七十五轮把 25 个对话框批量换基类，只改了、没钉住——新增对话框若写回
    /// <c>: Form</c>，Linux 侧不报红，真机拖动时才发现不重算。这是接线门的结构性缺口。
    /// 这条断言只看代码行（正则），注释里的 <c>: Form</c> 不算；新增对话框若不改基类，这里立即红。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryDialogClass_InheritsDpiAwareForm()
    {
        var offenders = Repo.NativeSources()
            .SelectMany(s => Regex.Matches(s.Text, @"(?m)^\s*(?:internal|public)\s+(?:sealed\s+)?class\s+(\w+Dialog)\s*:\s*(\w+)\b")
                .Select(m => new { File = s.Name, Class = m.Groups[1].Value, Base = m.Groups[2].Value }))
            .Where(x => x.Base != "DpiAwareForm")
            .Select(x => $"{x.File}：class {x.Class} : {x.Base}")
            .ToList();
        Assert.True(
            offenders.Count == 0,
            "以下对话框不继承 DpiAwareForm（DPI 变更不会重算，改回 : Form 即可复现）：" + string.Join(" / ", offenders));
        // 地板：对话框数量掉下去说明扫描正则或目录坏了，检查会静默空转。
        Assert.True(DialogClassesOnDisk().Count >= 20, $"只扫到 {DialogClassesOnDisk().Count} 个对话框类，扫描口径可能已失效。");
    }

    /// <summary>
    /// P0 接线门：<c>DpiAwareForm.WndProc</c> 里 <c>base.WndProc</c> 必须排在重算之前（行首正则）。
    /// <para>
    /// 存在理由：这条消息框架自己也要用（更新窗口边界与它自己的 DPI 账），挡在前面等于替框架
    /// 决定它该收到什么。把 base 挪到重算后，Linux 侧不会有任何东西报红，真机拖动时才发现不重算。
    /// 只判代码行：注释里的 <c>base.WndProc</c> 不算。
    /// </para>
    /// </summary>
    [Fact]
    public void DpiAwareForm_WndProc_BaseBeforeRescale()
    {
        var source = Repo.Source("Desktop", "Native", "DpiAwareForm.cs");
        var start = source.IndexOf("protected override void WndProc(", StringComparison.Ordinal);
        Assert.True(start >= 0, "DpiAwareForm.cs 里没有 WndProc 覆写（DPI 变更消息就没人认）。");
        var tracking = source.IndexOf("internal sealed class DpiTracking", start, StringComparison.Ordinal);
        Assert.True(tracking > start, "WndProc 与 DpiTracking 之间切不出那一段。");
        var wndProc = source[start..tracking];

        // base.WndProc 必须在重算之前（行首正则，只判代码行）。
        Assert.True(
            Regex.IsMatch(wndProc, @"^\s*base\.WndProc\(ref m\);$", RegexOptions.Multiline),
            "DpiAwareForm.WndProc 里没有按行首出现的 base.WndProc(ref m);。");
        Assert.True(
            Regex.IsMatch(wndProc, @"^\s*DpiScale\.RescaleOnDpiChanged\(this, m\.WParam\.ToInt64\(\)\);$", RegexOptions.Multiline),
            "DpiAwareForm.WndProc 里没有按行首出现的 DpiScale.RescaleOnDpiChanged(...)。");
        // 顺序：base.WndProc 在 RescaleOnDpiChanged 之前。
        var baseIdx = IndexOfCodeLine(wndProc, "base.WndProc(ref m);");
        var rescaleIdx = IndexOfCodeLine(wndProc, "DpiScale.RescaleOnDpiChanged(this, m.WParam.ToInt64());");
        Assert.True(baseIdx >= 0 && rescaleIdx >= 0 && baseIdx < rescaleIdx,
            $"WndProc 里 base.WndProc 必须排在重算之前（base {baseIdx} / 重算 {rescaleIdx}）。");
    }

    /// <summary>按行首匹配代码行（不被注释行骗过，同 P0 接线门的判据口径）。</summary>
    private static int IndexOfCodeLine(string body, string code)
    {
        var lines = body.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (Regex.IsMatch(lines[i], @"^\s*" + Regex.Escape(code) + "$")) return i;
        }
        return -1;
    }

    // ---------- 页面类进冒烟清单（第一百五十五轮，控制仓简报 P1-2） ----------

    /// <summary>磁盘上 <c>Desktop/Native</c> 里所有继承 <c>UserControl</c> 的页面类名（按名字排序）。</summary>
    private static List<string> PageClassesOnDisk() =>
        Repo.NativeSources()
            .SelectMany(s => Regex.Matches(s.Text, @"(?m)^\s*(?:internal|public)\s+(?:sealed\s+)?class\s+(\w+Page)\s*:\s*UserControl\b")
                .Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>冒烟程序显式声明的页面清单（<c>SmokePageNames</c>，第一百五十五轮）。</summary>
    private static List<string> PageClassesInSmoke()
    {
        var start = SmokeProgram.IndexOf("SmokePageNames =", StringComparison.Ordinal);
        Assert.True(start >= 0, "ApifoxHelper.Smoke/Program.cs 里找不到 SmokePageNames 清单常量。");
        var open = SmokeProgram.IndexOf('[', start);
        var close = SmokeProgram.IndexOf("];", open, StringComparison.Ordinal);
        Assert.True(open > start && close > open, "SmokePageNames 清单不是 [ … ]; 的形状，提取器失效。");
        var names = Regex.Matches(SmokeProgram[open..close], "\"([A-Za-z]\\w*)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();
        // 提取器失效时断言会静默空转：地板与去重都在这里判。
        Assert.True(names.Count >= 2, $"SmokePageNames 只提取到 {names.Count} 个类名，提取口径可能已失效。");
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        return names.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 页面类与对话框同册（第一百五十五轮）：漏登记 = 那个页面永远不会被构造，
    /// 构造期崩溃（控件树、事件接线、排序记忆文件读取）在它身上照样能溜过去。
    /// </summary>
    [Fact]
    public void EveryPageClass_IsDeclaredInTheSmokeList()
    {
        var onDisk = PageClassesOnDisk();
        var declared = PageClassesInSmoke();
        var missing = onDisk.Except(declared, StringComparer.Ordinal).ToList();
        Assert.True(
            missing.Count == 0,
            "以下页面未在 ApifoxHelper.Smoke/Program.cs 的 SmokePageNames 里登记：" + string.Join("、", missing));
        // 地板：页面数量掉下去说明扫描正则或目录坏了，检查会静默空转。
        Assert.True(onDisk.Count >= 2, $"只扫到 {onDisk.Count} 个页面类，扫描口径可能已失效。");
    }

    /// <summary>反向：清单里留着已删除的页面，Linux 侧就报出具体名字，而不是等一次 Windows 编译。</summary>
    [Fact]
    public void SmokeList_DeclaresNoPageThatDoesNotExist()
    {
        var onDisk = PageClassesOnDisk();
        var stale = PageClassesInSmoke().Except(onDisk, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0, "冒烟清单里有磁盘上不存在的页面：" + string.Join("、", stale));
    }

    /// <summary>
    /// 页面清单同样是数据，「登记了」不等于「构造了」（第一百五十五轮）。
    /// 这一条钉住冒烟程序在运行时核对两个方向，且核对排在结果行之前（否则失败不计入退出码）。
    /// </summary>
    [Fact]
    public void SmokeProgram_ChecksPageCoverageAtRuntime()
    {
        // 记录真实构造出来的页面类型（按控件实际类型，不按用例名）。
        Assert.Contains("_constructedPages.Add(page.GetType().Name);", SmokeProgram);
        var body = MethodBody(SmokeProgram, "private static void CheckPageCoverage(");
        Assert.Contains("SmokePageNames.Where(n => !_constructedPages.Contains(n))", body);
        Assert.Contains("_constructedPages.Where(n => !declared.Contains(n))", body);
        Assert.Contains("_failed++;", body);
        Assert.Contains("_lines.Add(fail);", body);
        // 必须在 Main 里真的调用，且排在结果行之前。
        var main = MethodBody(SmokeProgram, "private static int Main(");
        var call = main.IndexOf("CheckPageCoverage();", StringComparison.Ordinal);
        var result = main.IndexOf("== 结果：", StringComparison.Ordinal);
        Assert.True(call >= 0, "Main 里没有调用 CheckPageCoverage。");
        Assert.True(result > call, "CheckPageCoverage 没有排在结果行之前（失败不会计入退出码）。");
    }

    [Fact]
    public void EveryCase_BuildsHandleAndForcesLayout()
    {
        // 只 new 不建句柄，构造期事件顺序缺陷（第一百二十八轮那一类）不会暴露：
        // 那次崩溃发生在 set_Padding 里，而 set_Padding 在构造里就跑了——但同类缺陷也可能
        // 藏在句柄创建/首次布局路径上（字体度量、DPI、控件树挂载），所以三件事都要做。
        Assert.Contains("_ = control.Handle;", SmokeProgram);
        Assert.Contains("control.CreateControl();", SmokeProgram);
        Assert.Contains("control.PerformLayout();", SmokeProgram);
        // 三件事必须在同一个 Run 里，且 Run 是唯一执行入口（用例不得绕过它直接 new）。
        var run = SmokeProgram[SmokeProgram.IndexOf("private static void Run(", StringComparison.Ordinal)..];
        Assert.Contains("_ = control.Handle;", run);
        Assert.Contains("control.CreateControl();", run);
        Assert.Contains("control.PerformLayout();", run);
    }

    [Fact]
    public void Failures_AreCountedAndTurnIntoNonZeroExit()
    {
        // 退出码是唯一可机读的判定：0 = 全过，1 = 有失败，2 = 环境初始化失败。
        Assert.Contains("return _failed == 0 ? 0 : 1;", SmokeProgram);
        Assert.Contains("return 2;", SmokeProgram);
        // 失败必须带异常类型与首帧堆栈——只打「失败」两个字等于没证据。
        Assert.Contains("ex.GetType().Name", SmokeProgram);
        Assert.Contains("ex.StackTrace", SmokeProgram);
    }

    [Fact]
    public void Report_IsWrittenAsUtf8()
    {
        // wine 的控制台代码页会把中文打成问号（本轮实测：整份 PASS 行读不出是哪一处），
        // 报告文件是「跑过一遍」的可核对证据，必须按 UTF-8 写。
        Assert.Contains("UTF8Encoding", SmokeProgram);
        Assert.Contains("File.WriteAllLines(_reportPath, _lines", SmokeProgram);
        Assert.Contains("--report=", SmokeProgram);
        // 环境初始化失败也要落报告：否则「一个对话框都没构造到」看起来像「没跑」。
        var initFail = SmokeProgram[..SmokeProgram.IndexOf("var slot = new DocSlot", StringComparison.Ordinal)];
        Assert.Contains("WriteReport();", initFail);
    }

    [Fact]
    public void ProbeWorkspace_IsCreatedBeforeSavingTheProfile()
    {
        // 本轮实测踩到的坑：ProfileStore.Validate 要求 API 项目文件真实存在，
        // 不造工作区就在环境初始化阶段退出（退出码 2），一个对话框都构造不到。
        Assert.Contains("Probe.Api.csproj", SmokeProgram);
        Assert.Contains("Directory.CreateDirectory(probeDir)", SmokeProgram);
        // 数据根必须落在临时目录：冒烟不得写用户的真实配置与任务。
        Assert.Contains("Path.GetTempPath()", SmokeProgram);
    }

    [Fact]
    public void ControlFactories_AreSmokedToo()
    {
        // 第一百二十八轮的缺陷本体在 Ui.FieldGridPanel（控件工厂），不在某个对话框里：
        // 只冒烟对话框会漏掉「工厂自己构造期就崩」这一类。
        Assert.Contains("Ui.FieldGrid(", SmokeProgram);
        Assert.Contains("Ui.ScrollHost()", SmokeProgram);
        Assert.Contains("Ui.ListHost(", SmokeProgram);
        // 列宽是第一百二十三/一百二十四轮的修复点，冒烟里要把它打出来（[120, 495]）。
        Assert.Contains("ColumnStyles[0].Width", SmokeProgram);
    }

    [Fact]
    public void SmokeProgram_StartsNoServer()
    {
        // 冒烟器只解析服务层、不起 Kestrel、不注册本机能力桥——它不是第二个调度器，
        // 也不得在跑冒烟时对外开端口。
        // 只看代码行：注释里写「不起 Kestrel」是说明理由，不是起服务端。
        var code = CodeScan.Text(SmokeProgram);
        Assert.DoesNotContain("WebApplication", code);
        Assert.DoesNotContain("Kestrel", code);
        Assert.DoesNotContain("AddHostedService", code);
        Assert.DoesNotContain("JobRunner.Start", code);
    }

    [Fact]
    public void SmokeProject_IsExcludedFromTheMainCompile()
    {
        // 冒烟工程目录在主工程目录内：不排除就会被主工程的默认通配编译进去（重复类型）。
        Assert.Contains(@"<Compile Remove=""ApifoxHelper.Smoke\**"" />", MainProject);
        Assert.Contains(@"<EmbeddedResource Remove=""ApifoxHelper.Smoke\**"" />", MainProject);
    }

    [Fact]
    public void SmokeProject_IsTheOnlyFriendAssembly()
    {
        // 冒烟要 new 出 internal 对话框：只对这一个程序集放行，产品代码的可见性不因此对外公开。
        Assert.Contains(@"<InternalsVisibleTo Include=""ApifoxHelper.Smoke"" />", MainProject);
        var friends = Regex.Matches(MainProject, @"InternalsVisibleTo Include=""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.Equal(["ApifoxHelper.Smoke"], friends);
    }

    [Fact]
    public void SmokeProject_TargetsWindowsDesktopAndCompilesOnLinux()
    {
        // 冒烟必须跑在 Windows 桌面运行时上（WinForms 控件在 Linux 上无法构造），
        // 但 Linux 侧要能编译验证——两条都要钉住，缺一条就变成「本机跑不了、CI 也编不过」。
        Assert.Contains("<TargetFramework>net8.0-windows</TargetFramework>", SmokeProject);
        Assert.Contains("<UseWindowsForms>true</UseWindowsForms>", SmokeProject);
        Assert.Contains("EnableWindowsTargeting", SmokeProject);
        Assert.Contains("<OutputType>Exe</OutputType>", SmokeProject);
        Assert.Contains(@"..\ApifoxHelper.csproj", SmokeProject);
    }

    // ---------- 分段自检（第一百三十三轮，控制仓简报 P1-1） ----------

    /// <summary>
    /// 冒烟器的每一段交互用例都必须自带断言（第一百三十三轮）。
    /// <para>
    /// 存在理由：冒烟器已是主链路唯一的自动化证据，而它的判定方式是「段里的断言抛异常即失败」——
    /// 段还在、断言被删掉，这一段就变成「跑过、什么也没判」，报告照样 PASS。
    /// 这类退化在 Linux 侧看不出来（冒烟器跑在 Windows 桌面运行时上），只能靠源码切片钉住。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryRunActionSegment_AssertsSomething()
    {
        var segments = Segments("RunAction");
        // 地板：段数掉下去说明扫描口径坏了，检查会静默空转。
        Assert.True(segments.Count >= 8, $"只找到 {segments.Count} 个 RunAction 段，扫描口径可能已失效。");
        var silent = segments.Where(segment => !HasAssertion(segment.Body)).Select(segment => segment.Name).ToList();
        Assert.True(silent.Count == 0, "以下 RunAction 段没有任何断言（段在、断言丢）：" + string.Join("、", silent));
    }

    [Fact]
    public void EveryMeasureSection_AssertsSomething()
    {
        // 两段「字段宽度实测」不走 RunAction（它们自己记账），断言必须留在方法体里：
        // 量不到控件、量到的宽度低于下限，两种都要判失败，否则「量了一遍」等于没量。
        foreach (var signature in new[]
        {
            "private static void MeasureFieldWidths(",
            "private static void MeasureFieldWidthsAtScale(",
        })
        {
            var body = MethodBody(SmokeProgram, signature);
            Assert.Contains("tooNarrow", body, StringComparison.Ordinal);
            Assert.Contains("_failed++", body, StringComparison.Ordinal);
            Assert.Contains("measured.Count == 0", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryEndToEndSection_IsMadeOfAssertingSegments()
    {
        // 两段端到端各自至少三段交互用例：段数掉下去说明有人把某一段删了（删段比删断言更难发现）。
        foreach (var signature in new[]
        {
            "private static void RunEndToEnd(",
            "private static void RunWriteBackEndToEnd(",
        })
        {
            var body = MethodBody(SmokeProgram, signature);
            var count = Regex.Matches(body, @"RunAction\(").Count;
            Assert.True(count >= 3, $"{signature} 里只有 {count} 个 RunAction 段。");
        }
    }

    // ---------- 注释缺口对话框的真实控件交互（第一百三十六轮） ----------

    /// <summary>
    /// 注释缺口对话框此前只被「构造一次」（<c>Cases</c> 里 <c>new</c> + 建句柄），而它是整条审阅链路的入口。
    /// 这一段钉住「真的打开、真的点、真的敲」——只构造的话，计数行、覆盖率行、打开即选中第一条、
    /// 上一条/下一条、关键词与状态筛选全都可以是坏的而冒烟照样 PASS。
    /// </summary>
    [Fact]
    public void ScanResultsInteraction_DrivesRealControls()
    {
        var body = MethodBody(SmokeProgram, "private static void RunScanResultsInteraction(");
        // 真的打开（Show 触发 OnLoad 读盘）、真的点按钮、真的敲关键词、真的改筛选下拉。
        Assert.Contains("dialog.Show();", body);
        Assert.Contains("PerformClick()", body);
        Assert.Contains("keyword.Text =", body);
        Assert.Contains("filter.SelectedIndex =", body);
        // 断言取共享口径，不是第二份拼字（否则比的是冒烟器自己那份文字）。
        Assert.Contains("SlotText.CountsLine(", body);
        Assert.Contains("ScanOverview.CoverageText(", body);
        Assert.Contains("ListAdvance.PositionText(", body);
        Assert.Contains("ListFilter.FilteredEmptyHint(", body);
        // 段数与断言：段数掉下去说明有人删了某一段（删段比删断言更难发现）。
        var count = Regex.Matches(body, @"RunAction\(").Count;
        Assert.True(count >= 5, $"缺口对话框交互只有 {count} 个 RunAction 段。");
        // Show 出来的窗体必须释放：不 Dispose 会漏窗口句柄，跑第二遍就换一只壳。
        Assert.Contains("dialog?.Dispose();", body);
    }

    /// <summary>段写好了但没在 Main 里调用 = 永远不会跑，报告照样 PASS。</summary>
    [Fact]
    public void ScanResultsInteraction_IsRegisteredInMain()
    {
        var main = MethodBody(SmokeProgram, "private static int Main(");
        var writeBack = main.IndexOf("RunWriteBackEndToEnd(services, dataRoot);", StringComparison.Ordinal);
        var interaction = main.IndexOf("RunScanResultsInteraction(services, dataRoot);", StringComparison.Ordinal);
        Assert.True(writeBack >= 0, "Main 里没有调用回写链路。");
        Assert.True(interaction > writeBack, "缺口对话框交互没有排在回写链路之后（主链路顺序：构造 → 端到端 → 回写 → 缺口对话框）。");
    }

    // ---------- 注释候选对话框的真实控件交互（第一百三十九轮） ----------

    /// <summary>
    /// 注释候选对话框是真机清单第 6/7/8 项的落点，此前只被「构造一次」。
    /// 这一段钉住「真的打开、真的点、真的敲」：计数行、打开即选中第一条、待确认问题的拒绝路径、
    /// 「接受并下一条」的落点、提示态收掉回答框与覆盖勾选（第一百三十八轮的修法）、撤销接受。
    /// </summary>
    [Fact]
    public void CandidatesInteraction_DrivesRealControls()
    {
        var body = MethodBody(SmokeProgram, "private static void RunCandidatesInteraction(");
        // 真的打开（Show 触发 OnLoad 读盘）、真的点按钮、真的敲回答与关键词。
        Assert.Contains("dialog.Show();", body);
        Assert.Contains("PerformClick()", body);
        Assert.Contains("boxes[1].Text =", body);
        Assert.Contains("keyword.Text =", body);
        // 候选由真实服务产出，不手写 comment-drafts.json。
        Assert.Contains("drafts.ImportResultAsync(", body);
        Assert.Contains("scan.ScanAsync(", body);
        // 断言取共享口径，不是第二份拼字（否则比的是冒烟器自己那份文字）。
        Assert.Contains("QueueText.CandidateCountsLine(", body);
        Assert.Contains("CandidateEdit.ReviewDoneText(", body);
        Assert.Contains("CandidateEdit.UndoDoneText", body);
        Assert.Contains("CandidateEdit.AnswersCaptionNoSelection", body);
        Assert.Contains("ListFilter.FilteredEmptyHint(", body);
        // 审阅状态从磁盘重读（不是内存里那一份）。
        Assert.Contains("drafts.Load(task.Id)", body);
        // 段数与断言：段数掉下去说明有人删了某一段（删段比删断言更难发现）。
        var count = Regex.Matches(body, @"RunAction\(").Count;
        Assert.True(count >= 6, $"候选对话框交互只有 {count} 个 RunAction 段。");
        // Show 出来的窗体必须释放：不 Dispose 会漏窗口句柄，跑第二遍就换一只壳。
        Assert.Contains("dialog?.Dispose();", body);
    }

    /// <summary>段写好了但没在 Main 里调用 = 永远不会跑，报告照样 PASS。</summary>
    [Fact]
    public void CandidatesInteraction_IsRegisteredInMain()
    {
        var main = MethodBody(SmokeProgram, "private static int Main(");
        var scanResults = main.IndexOf("RunScanResultsInteraction(services, dataRoot);", StringComparison.Ordinal);
        var candidates = main.IndexOf("RunCandidatesInteraction(services, dataRoot);", StringComparison.Ordinal);
        Assert.True(scanResults >= 0, "Main 里没有调用缺口对话框交互。");
        Assert.True(candidates > scanResults, "候选对话框交互没有排在缺口对话框之后（主链路顺序：缺口 → 候选）。");
    }

    // ---------- 补充说明对话框的真实控件交互（第一百四十五轮） ----------

    /// <summary>
    /// 补充说明对话框是 F10 人工补充说明的唯一入口，也是原生界面里少数几个「写任务数据」的交互之一，
    /// 此前只被「构造一次」。这一段钉住「真的打开、真的敲、真的点」：编辑位按目标结构派生、
    /// 打开即选中第一个接口、保存后磁盘上有条目且任务版本递增、原样重复保存不递增、
    /// 「保留现状」记 KeepAsIs 且值为空、筛到零行时保存禁用而计数行仍是整份统计。
    /// </summary>
    [Fact]
    public void SupplementsInteraction_DrivesRealControls()
    {
        var body = MethodBody(SmokeProgram, "private static void RunSupplementsInteraction(");
        // 真的打开（Show 触发 OnLoad 读盘）、真的敲、真的点按钮。
        Assert.Contains("dialog.Show();", body);
        // 点按钮走 Click 助手（内部是 Button.PerformClick，WinForms 的点击链路，不是直接调事件处理器）。
        Assert.Contains("Click(dialog, \"保存本接口补充\");", body);
        Assert.Contains("boxes[1].Text =", body);
        Assert.Contains("keeps[0].Checked = true;", body);
        Assert.Contains("keyword.Text =", body);
        // 检查结果是真实落盘的夹具（不是内存里传进去的对象）。
        Assert.Contains("tasks.Write(task.Id, TaskFiles.Diff, fixture);", body);
        Assert.Contains("tasks.Read<CheckResult>(task.Id, TaskFiles.Diff)", body);
        // 断言取共享口径，不是第二份拼字（否则比的是冒烟器自己那份文字）。
        Assert.Contains("SupplementEntries.EditorsFor(", body);
        Assert.Contains("SupplementEntries.SavedText(true)", body);
        Assert.Contains("SupplementEntries.SavedText(false)", body);
        Assert.Contains("ListFilter.FilteredEmptyKeywordHint(", body);
        // 保存结果从磁盘重读（不是内存里那一份），版本号也读磁盘。
        Assert.Contains("tasks.Read<SupplementStore>(task.Id, TaskFiles.Supplement)", body);
        Assert.Contains("tasks.Get(task.Id).Revision", body);
        // 段数与断言：段数掉下去说明有人删了某一段（删段比删断言更难发现）。
        var count = Regex.Matches(body, @"RunAction\(").Count;
        Assert.True(count >= 6, $"补充说明对话框交互只有 {count} 个 RunAction 段。");
        // Show 出来的窗体必须释放：不 Dispose 会漏窗口句柄，跑第二遍就换一只壳。
        Assert.Contains("dialog?.Dispose();", body);
    }

    /// <summary>段写好了但没在 Main 里调用 = 永远不会跑，报告照样 PASS。</summary>
    [Fact]
    public void SupplementsInteraction_IsRegisteredInMain()
    {
        var main = MethodBody(SmokeProgram, "private static int Main(");
        var candidates = main.IndexOf("RunCandidatesInteraction(services, dataRoot);", StringComparison.Ordinal);
        var supplements = main.IndexOf("RunSupplementsInteraction(services, dataRoot);", StringComparison.Ordinal);
        Assert.True(candidates >= 0, "Main 里没有调用候选对话框交互。");
        Assert.True(supplements > candidates, "补充说明对话框交互没有排在候选对话框之后（主链路顺序：缺口 → 候选 → 补充说明）。");
    }

    // ---------- 配置页读取失败态的真实控件交互（第一百五十一轮，简报 P0-2） ----------

    /// <summary>
    /// 配置页的「读取失败」是第一百五十轮补上的第三种状态，但证据只到源码切片与判定真值表——
    /// 真实控件上「失败 → 点重试 → 恢复」这条闭环从未跑过。这一段钉住它真的跑：
    /// 页面真的显示、读取入口真的调、按钮真的点、磁盘真的坏又真的修好。
    /// </summary>
    [Fact]
    public void ProfileLoadFailureInteraction_DrivesRealControls()
    {
        var body = MethodBody(SmokeProgram, "private static void RunProfileLoadFailure(");
        // 真的显示页面（UserControl 没有 Show，挂在 Form 上）、真的走读取入口、真的点按钮。
        Assert.Contains("host.Show();", body);
        Assert.Contains("page.RefreshData();", body);
        Assert.Contains("Click(page, ProfileLoadState.RetryLabel);", body);
        // 失败是真的：往配置目录写坏 JSON，走 ProfileStore.List 的同一个 catch 分支。
        Assert.Contains("File.WriteAllText(broken", body);
        Assert.Contains("File.Delete(broken);", body);
        // 断言取共享口径，不是第二份拼字（否则比的是冒烟器自己那份文字）。
        Assert.Contains("ProfileLoadState.FailureTitle", body);
        Assert.Contains("ProfileLoadState.FailureHint", body);
        Assert.Contains("ProfileLoadState.RetryLabel", body);
        // 「唯一行动按钮」要真的数一遍可见按钮，不是只判「重试可见」。
        Assert.Contains("EmptyActionButtons(page)", body);
        // 段数与断言：段数掉下去说明有人删了某一段（删段比删断言更难发现）。
        var count = Regex.Matches(body, @"RunAction\(").Count;
        Assert.True(count >= 5, $"配置页失败态交互只有 {count} 个 RunAction 段。");
        // 页面与宿主窗体都要释放：不 Dispose 会漏窗口句柄，跑第二遍就换一只壳。
        Assert.Contains("page?.Dispose();", body);
        Assert.Contains("host?.Dispose();", body);
    }

    /// <summary>段写好了但没在 Main 里调用 = 永远不会跑，报告照样 PASS。</summary>
    [Fact]
    public void ProfileLoadFailureInteraction_IsRegisteredInMain()
    {
        var main = MethodBody(SmokeProgram, "private static int Main(");
        var formLayout = main.IndexOf("RunProfileFormLayout(services, profile);", StringComparison.Ordinal);
        var failure = main.IndexOf("RunProfileLoadFailure(services, dataRoot);", StringComparison.Ordinal);
        Assert.True(formLayout >= 0, "Main 里没有调用配置表单布局段。");
        Assert.True(failure > formLayout, "配置页失败态交互没有排在配置表单布局之后（主链路顺序：配置表单 → 配置页失败态）。");
    }

    // ---------- 三屏读取失败态的真实控件交互（第一百五十三轮，简报 P1-3） ----------

    /// <summary>
    /// 第一百五十二轮把「读取失败 → 唯一行动按钮「重试」」推广到任务页 / 报告 / 差异确认三屏，
    /// 但证据只到 Linux 侧判定真值表与源码切片——清单留不留旧行、详情区收不收、按钮点不点得动、
    /// 点完是否真的重读、恢复后入口能否用，全都可以是坏的而 Linux 全绿、冒烟照样 PASS。
    /// 这三段钉住它们真的跑：页面/对话框真的显示、读取入口真的调、按钮真的点、磁盘真的坏又真的修好。
    /// </summary>
    [Fact]
    public void TaskLoadFailureInteraction_DrivesRealControls()
    {
        var body = MethodBody(SmokeProgram, "private static void RunTaskLoadFailure(");
        // 真的显示页面（UserControl 没有 Show，挂在 Form 上）、真的走读取入口、真的点按钮。
        Assert.Contains("host.Show();", body);
        Assert.Contains("page.RefreshData();", body);
        Assert.Contains("Click(page, LoadFailureState.RetryLabel);", body);
        // 失败是真的：往任务目录写坏 JSON，走 TaskStore.List 的同一个 catch 分支。
        Assert.Contains("File.WriteAllText(brokenFile,", body);
        // 断言取共享口径，不是第二份拼字（否则比的是冒烟器自己那份文字）。
        Assert.Contains("LoadFailureState.Tasks.Title", body);
        Assert.Contains("LoadFailureState.Tasks.Hint", body);
        Assert.Contains("LoadFailureState.RetryLabel", body);
        // 「唯一行动按钮」要真的数一遍可见按钮，不是只判「重试可见」。
        Assert.Contains("EmptyActionButtons(page)", body);
        // 清单空了，详情区没有可作用的对象：报告入口必须跟着停用。
        Assert.Contains("ButtonOf(page, \"查看报告与导出\").Enabled", body);
        // 段数与断言：段数掉下去说明有人删了某一段（删段比删断言更难发现）。
        var count = Regex.Matches(body, @"RunAction\(").Count;
        Assert.True(count >= 5, $"任务页失败态交互只有 {count} 个 RunAction 段。");
        // 页面与宿主窗体都要释放：不 Dispose 会漏窗口句柄，跑第二遍就换一只壳。
        Assert.Contains("page?.Dispose();", body);
        Assert.Contains("host?.Dispose();", body);
    }

    /// <summary>段写好了但没在 Main 里调用 = 永远不会跑，报告照样 PASS。</summary>
    [Fact]
    public void TaskLoadFailureInteraction_IsRegisteredInMain()
    {
        var main = MethodBody(SmokeProgram, "private static int Main(");
        var profile = main.IndexOf("RunProfileLoadFailure(services, dataRoot);", StringComparison.Ordinal);
        var task = main.IndexOf("RunTaskLoadFailure(services, dataRoot);", StringComparison.Ordinal);
        Assert.True(profile >= 0, "Main 里没有调用配置页失败态交互。");
        Assert.True(task > profile, "任务页失败态交互没有排在配置页失败态之后（主链路顺序：配置页 → 任务页）。");
    }

    [Fact]
    public void ReportsLoadFailureInteraction_DrivesRealControls()
    {
        var body = MethodBody(SmokeProgram, "private static void RunReportsLoadFailure(");
        // 真的显示对话框、真的点按钮。
        Assert.Contains("dialog.Show();", body);
        Assert.Contains("Click(dialog, LoadFailureState.RetryLabel);", body);
        // 失败是真的：把任务目录整个挪走，报告生成读任务时抛，走产品同一个分支（不伪造异常类型）。
        Assert.Contains("Directory.Move(taskDir, parked);", body);
        Assert.Contains("Directory.Move(parked, taskDir);", body);
        // 断言取共享口径。
        Assert.Contains("LoadFailureState.Reports.Title", body);
        Assert.Contains("LoadFailureState.RetryLabel", body);
        // 四个导出入口逐个判（不是只判一个）：预览都没生成出来，导出的只会是同一份失败。
        Assert.Contains("\"导出 Markdown\", \"导出 HTML\", \"导出 JSON\", \"前端交接说明\"", body);
        var count = Regex.Matches(body, @"RunAction\(").Count;
        Assert.True(count >= 4, $"报告对话框失败态交互只有 {count} 个 RunAction 段。");
        Assert.Contains("dialog?.Dispose();", body);
    }

    [Fact]
    public void ReportsLoadFailureInteraction_IsRegisteredInMain()
    {
        var main = MethodBody(SmokeProgram, "private static int Main(");
        var task = main.IndexOf("RunTaskLoadFailure(services, dataRoot);", StringComparison.Ordinal);
        var reports = main.IndexOf("RunReportsLoadFailure(services, dataRoot);", StringComparison.Ordinal);
        Assert.True(task >= 0, "Main 里没有调用任务页失败态交互。");
        Assert.True(reports > task, "报告对话框失败态交互没有排在任务页之后（主链路顺序：任务页 → 报告）。");
    }

    [Fact]
    public void DecisionsLoadFailureInteraction_DrivesRealControls()
    {
        var body = MethodBody(SmokeProgram, "private static void RunDecisionsLoadFailure(");
        Assert.Contains("dialog.Show();", body);
        Assert.Contains("Click(dialog, LoadFailureState.RetryLabel);", body);
        // 两种失败态都是真的：检查结果与已保存决策各写一份坏 JSON，走产品同一个读取分支。
        Assert.Contains("File.WriteAllText(diffPath,", body);
        Assert.Contains("File.WriteAllText(decisionsPath,", body);
        // 断言取共享口径（两种失败态各有自己的标题与说明）。
        Assert.Contains("LoadFailureState.Decisions.Title", body);
        Assert.Contains("LoadFailureState.Decisions.SavedTitle", body);
        Assert.Contains("LoadFailureState.Decisions.SavedHint", body);
        // 「尚未检查」不是失败态：不显示「重试」（同一屏只给一个下一步）。
        Assert.Contains("\"尚未检查\"", body);
        var count = Regex.Matches(body, @"RunAction\(").Count;
        Assert.True(count >= 6, $"差异确认失败态交互只有 {count} 个 RunAction 段。");
        Assert.Contains("dialog?.Dispose();", body);
    }

    [Fact]
    public void DecisionsLoadFailureInteraction_IsRegisteredInMain()
    {
        var main = MethodBody(SmokeProgram, "private static int Main(");
        var reports = main.IndexOf("RunReportsLoadFailure(services, dataRoot);", StringComparison.Ordinal);
        var decisions = main.IndexOf("RunDecisionsLoadFailure(services, dataRoot);", StringComparison.Ordinal);
        Assert.True(reports >= 0, "Main 里没有调用报告对话框失败态交互。");
        Assert.True(decisions > reports, "差异确认失败态交互没有排在报告之后（主链路顺序：报告 → 差异确认）。");
    }

    // ---------- 任务日志读取失败态的真实控件交互（第一百五十八轮，简报 P1-4 的可达部分） ----------

    /// <summary>
    /// 第一百五十七轮给任务日志补了「读取失败 → 唯一行动按钮『重试』」，证据只到源码切片与文案断言。
    /// 这一段钉住它真的跑：对话框真的显示、读取入口真的调、按钮真的点、磁盘真的读不出来又真的恢复。
    /// </summary>
    [Fact]
    public void JournalLoadFailureInteraction_DrivesRealControls()
    {
        var body = MethodBody(SmokeProgram, "private static void RunJournalLoadFailure(");
        Assert.Contains("dialog.Show();", body);
        Assert.Contains("Click(dialog, LoadFailureState.RetryLabel);", body);
        // 失败是真的：独占打开 journal.jsonl，产品读日志时抛 IOException，走同一个 catch 分支。
        // 不能靠写坏 JSON 造失败——ReadJsonLines 逐行吞 JsonException，那是「没有记录」的空态。
        Assert.Contains("FileShare.None", body);
        Assert.DoesNotContain("File.WriteAllText(journalPath", body);
        // 断言取共享口径。
        Assert.Contains("LoadFailureState.Journal.Title", body);
        Assert.Contains("LoadFailureState.RetryLabel", body);
        Assert.Contains("JournalEntries.CountsLine(2)", body);
        // 打开时键盘归属（第八十八轮纪律）在真实控件上判一次。
        Assert.Contains("if (!list.Focused)", body);
        // 失败提示在真实控件上不得出现双标点（第一百五十八轮：英文异常消息以半角句点收尾）。
        Assert.Contains("失败提示里出现双标点", body);
        // 新增按钮不改 Enter/Esc 语义。
        Assert.Contains("dialog.AcceptButton", body);
        Assert.Contains("dialog.CancelButton", body);
        var count = Regex.Matches(body, @"RunAction\(").Count;
        Assert.True(count >= 6, $"任务日志失败态交互只有 {count} 个 RunAction 段。");
        Assert.Contains("dialog?.Dispose();", body);
        Assert.Contains("hold?.Dispose();", body);
    }

    /// <summary>段写好了但没在 Main 里调用 = 永远不会跑，报告照样 PASS。</summary>
    [Fact]
    public void JournalLoadFailureInteraction_IsRegisteredInMain()
    {
        var main = MethodBody(SmokeProgram, "private static int Main(");
        var decisions = main.IndexOf("RunDecisionsLoadFailure(services, dataRoot);", StringComparison.Ordinal);
        var journal = main.IndexOf("RunJournalLoadFailure(services, dataRoot);", StringComparison.Ordinal);
        Assert.True(decisions >= 0, "Main 里没有调用差异确认失败态交互。");
        Assert.True(journal > decisions, "任务日志失败态交互没有排在差异确认之后（主链路顺序：差异确认 → 任务日志）。");
    }

    // ---------- 极端数据下的详情区与列表（第一百五十九轮，简报 P1-4） ----------

    /// <summary>
    /// 主链路的正常数据都是短名字、短路径、短错误消息，而真实使用里三样都会很长。
    /// 这一段钉住它真的在真实控件上量：页面真的显示、真的按壳的入口布置分割、真的选中那一行。
    /// </summary>
    [Fact]
    public void DetailBreakInteraction_DrivesRealControls()
    {
        var body = MethodBody(SmokeProgram, "private static void RunDetailBreak(");
        // 页面真的显示，且走壳的真实入口（OnPageShown 才按当前宽度布置分割）。
        Assert.Contains("host.Show();", body);
        Assert.Contains("page.OnPageShown();", body);
        // 极端数据是真的：超长中文任务名与超长路径前缀都落进产品数据。
        Assert.Contains("Enumerable.Repeat(\"超长任务名\", 24)", body);
        Assert.Contains("new string('深', 60)", body);
        // 真的选中那一行（详情区才有内容可量）。
        Assert.Contains("item.Selected = true;", body);
        // 判定口径：静默裁掉即失败（换行变高或省略号才算有截断提示）。
        Assert.Contains("AssertNoSilentClipping(grid,", body);
        // 判定口径本身在助手方法里：换行变高或省略号才算有截断提示，静默裁掉即失败。
        var helper = MethodBody(SmokeProgram, "private static void AssertNoSilentClipping(");
        Assert.Contains("label.AutoEllipsis", helper);
        Assert.Contains("var wrapped = label.Height > label.Font.Height * 1.5;", helper);
        Assert.Contains("被静默裁掉", helper);
        // 状态行：省略号 + 高度不撑高（固定占位）。
        Assert.Contains("if (!status.AutoEllipsis)", body);
        // 高度按「放文字前后是否一样」判（状态行是字号高 + 上下内边距的固定占位，本来就比一个字号高）。
        Assert.Contains("if (status.Height != heightBefore)", body);
        // 列宽：按 DESIGN §6 的两支独立重述，不调 ListColumns.FillWidth（否则与被测代码同源、恒真）。
        // 只认「调用」形态（带括号）：注释里提到这个名字是说明为什么不用它，不算违规。
        Assert.Contains("if (sum != expected)", body);
        Assert.DoesNotContain("ListColumns.FillWidth(", body);
        var count = Regex.Matches(body, @"RunAction\(").Count;
        Assert.True(count >= 5, $"极端数据交互只有 {count} 个 RunAction 段。");
        Assert.Contains("page?.Dispose();", body);
        Assert.Contains("host?.Dispose();", body);
    }

    /// <summary>段写好了但没在 Main 里调用 = 永远不会跑，报告照样 PASS。</summary>
    [Fact]
    public void DetailBreakInteraction_IsRegisteredInMain()
    {
        var main = MethodBody(SmokeProgram, "private static int Main(");
        var journal = main.IndexOf("RunJournalLoadFailure(services, dataRoot);", StringComparison.Ordinal);
        var detail = main.IndexOf("RunDetailBreak(services, dataRoot);", StringComparison.Ordinal);
        var journey = main.IndexOf("RunFullJourney(services, dataRoot);", StringComparison.Ordinal);
        Assert.True(journal >= 0, "Main 里没有调用任务日志失败态交互。");
        Assert.True(detail > journal, "极端数据交互没有排在任务日志失败态之后。");
        Assert.True(journey > detail, "极端数据交互没有排在完整旅程之前（它不该影响旅程的干净数据根）。");
    }

    // ---------- 对话框状态行逐屏扫（第一百七十九轮，简报 P0-1） ----------

    /// <summary>
    /// <c>RunDialogStatusSweep</c> 的屏单必须从 <c>Cases(...)</c> 读，不许另列一份名单（第一百七十九轮，简报 P0-1）。
    /// <para>
    /// <b>为什么这条门是 P0</b>：第一百七十六轮把「状态行放不下才给『查看完整错误』入口」这条口径从三处代表屏
    /// 扩到全部 15 处有状态行的对话框，做法是<b>从 <c>Cases(...)</c> 读屏单</b>——新增对话框自动进这一段，
    /// 漏掉不会有第二份名单替它兜底。但这件事当时只有 wine 侧的真实控件证据，<b>Linux 侧一条断言都没有</b>：
    /// 谁把 <c>Cases(...)</c> 换成一份手写数组，冒烟照样跑、报告照样 PASS，而新增的对话框从此不再被扫。
    /// 这与第一百六十二轮「空门」、第一百六十五轮「构造顺序」同一族——<b>Linux 侧全绿拦不住它</b>。
    /// </para>
    /// <para>
    /// 判三件事：① 屏单来自 <c>Cases(services, profile, task, slot)</c>；② 段里没有硬编码的对话框构造
    /// （<c>new XxxDialog(</c>）——有的话就是第二份名单；③ 每屏三段（短句不给 → 长句重判 → 回短句收起），
    /// 段数掉下去说明有人删了某一段（删段比删断言更难发现）。
    /// </para>
    /// </summary>
    [Fact]
    public void DialogStatusSweep_ReadsItsScreenListFromCases()
    {
        var body = MethodBody(SmokeProgram, "private static void RunDialogStatusSweep(");

        // ① 屏单来自 Cases(...)：这是「新增对话框自动进这一段」的唯一保证。
        Assert.Contains("foreach (var (name, build) in Cases(services, profile, task, slot))", body);

        // ② 反面：段里不许自己 new 对话框（那就是第二份名单，会与 Cases 漂开）。
        Assert.DoesNotMatch(@"new\s+\w*Dialog\s*\(", body);

        // ③ 每屏三段，且三段都真的判失败（不是「跑过、什么也没判」）。
        var segments = Regex.Matches(body, @"RunAction\(").Count;
        Assert.True(segments >= 3, $"逐屏扫只有 {segments} 个 RunAction 段（每屏三段：短句不给 → 长句重判 → 回短句收起）。");
        Assert.Contains("host.FullTextButton.Visible != shouldOffer", body);
        Assert.Contains("StatusEntryRule(host.Label)", body);

        // ④ 必须真的 Show：不 Show 时 Control.Visible 读的是有效可见性，量到的是「窗体还没显示」。
        Assert.Contains("dialog.Show();", body);

        // ⑤ 段在 Main 里注册（没注册 = 永远不跑，报告照样 PASS）。
        var main = MethodBody(SmokeProgram, "private static int Main(");
        Assert.Contains("RunDialogStatusSweep(services, profile, task, slot);", main);
    }

    /// <summary>
    /// <c>docs/NOTES.md</c> 必须写明「<c>RunDialogStatusSweep</c> 在第 177 轮三档复跑中包含」（第一百七十九轮，简报 P0-1）。
    /// <para>
    /// <b>为什么这一句要由断言钉住</b>：第一百七十七轮的三档复跑读数是 180 通过 / 0 失败 ×3，
    /// 而 180 = 135（第一百六十六轮）+ 45（15 屏 × 3 段，第一百七十六轮新增）。这个加法<b>只有算过的人知道</b>——
    /// 答辩被追问「15 处状态行入口在三档都量过吗」时，文档答不出来，就得现场从两个数字里推。
    /// 更糟的是下一轮若把这一句改回去（或删掉），没有任何东西会红：<b>同一份报告里两个数字对不上</b>
    /// 正是这道门要挡的形态。
    /// </para>
    /// <para>
    /// 判据是结构不是措辞：<c>docs/NOTES.md</c> 里必须有一行同时出现「<c>RunDialogStatusSweep</c>」
    /// 「第一百七十七轮」「三档」「含本段」四件事。**不判**这一跑是不是真机（那是 G-D5 的事，
    /// 见 <see cref="RealMachineEvidenceTests"/>）。
    /// </para>
    /// </summary>
    [Fact]
    public void Notes_RecordsThatTheThreeTierRerunIncludesTheDialogStatusSweep()
    {
        var notes = Repo.Source("docs", "NOTES.md");
        var line = notes.Split('\n').FirstOrDefault(l =>
            l.Contains("RunDialogStatusSweep", StringComparison.Ordinal)
            && l.Contains("第一百七十七轮", StringComparison.Ordinal)
            && l.Contains("三档", StringComparison.Ordinal)
            && l.Contains("含本段", StringComparison.Ordinal));
        Assert.True(line is not null,
            "docs/NOTES.md 里找不到一句「RunDialogStatusSweep 在第一百七十七轮三档复跑中含本段」："
            + "答辩被追问「15 处状态行入口在三档都量过吗」时，文档要答得出来，而不是靠人从 180 = 135 + 45 里推。");
    }

    // ---------- 字体栈的运行时命中（第一百三十九轮，简报 P0-1 的降级手段） ----------

    /// <summary>
    /// 第一百三十五轮把字体栈的有序回退做成真实现，但证据只到「判定真值表」与「两处入口都调了它」——
    /// 本机实际回退到哪个字体从未在运行时看过。这一段把「字体栈承诺」推进到运行时证据：
    /// 回退结果等于按栈序找到的第一个已装字体、界面控件与进程默认字体是同一份结果、等宽栈同法。
    /// </summary>
    [Fact]
    public void FontStackRuntime_AssertsTheActualHit()
    {
        var body = MethodBody(SmokeProgram, "private static void RunFontStackRuntime(");
        // 回退结果必须与「按栈序找到的第一个已装字体」比对，而不是只判非空。
        Assert.Contains("FontStack.Resolve(FontStack.Families, Installed, FontFamily.GenericSansSerif.Name)", body);
        Assert.Contains("resolved != expected", body);
        // 界面控件工厂、进程默认字体、真实对话框三处同一份「实际渲染名」
        // （第一百三十五轮的缺陷正是两处各写一份）。
        Assert.Contains("var rendered = uiFont.Name;", body);
        Assert.Contains("label.Font.Name != rendered", body);
        Assert.Contains("Control.DefaultFont.Name != rendered", body);
        Assert.Contains("dialog.Font.Name != rendered", body);
        Assert.Contains("UiTokens.CaptionPt", body);
        // 等宽栈与真实对话框上的控件也取同一份。
        Assert.Contains("FontStack.MonoFamilies", body);
        Assert.Contains("new NoticeDialog(", body);
        // 段在 Main 里注册（没注册 = 永远不跑）。
        var main = MethodBody(SmokeProgram, "private static int Main(");
        Assert.Contains("RunFontStackRuntime();", main);
    }

    /// <summary>
    /// 第一百四十轮 wine 实测把「渲染名 == 请求名」这条断言打红过一次：请求 <c>Segoe UI</c>、
    /// <c>Font.Name</c> 却是 <c>Noto Sans CJK SC</c>——GDI+ 在缺这个字体的机器上做字体替换，
    /// 请求名与渲染名本来就可以不同。产品承诺是「三处取同一份」，不是「运行时不替换字体」；
    /// 把后者写成断言等于要求环境配合，wine 与真机都会红。这条用例钉住「别把它加回来」。
    /// </summary>
    [Fact]
    public void FontStackRuntime_ComparesRenderedNamesNotRequestedOnes()
    {
        var body = MethodBody(SmokeProgram, "private static void RunFontStackRuntime(");
        // 反面：拿控件字体去比「请求的名字」——字体替换生效的机器上必红。
        Assert.DoesNotContain("label.Font.Name != resolved", body);
        Assert.DoesNotContain("Control.DefaultFont.Name != resolved", body);
        Assert.DoesNotContain("dialog.Font.Name != resolved", body);
        // 正面：替换事实作为证据行打印出来（不判失败，但要在报告里看得见）。
        Assert.Contains("做了字体替换", body);
        Assert.Contains("UiTokens.Mono.Value.Name", body);
    }

    /// <summary>
    /// 冒烟器里以 <paramref name="call"/> 开头的段（从该行到下一个段或下一个方法定义之前）。
    /// 按行切而不是按括号配平：段体里含插值字符串与字符字面量，配平扫描容易把边界算错。
    /// </summary>
    private static List<(string Name, string Body)> Segments(string call)
    {
        var lines = SmokeProgram.Split('\n');
        var marker = new Regex(@"^\s*" + call + @"\(""([^""]+)""");
        var method = new Regex(@"^    (private|internal|public|protected)\b");
        var starts = new List<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (marker.IsMatch(lines[i])) starts.Add(i);
        }
        var segments = new List<(string Name, string Body)>();
        foreach (var start in starts)
        {
            var end = lines.Length;
            for (var j = start + 1; j < lines.Length; j++)
            {
                if (marker.IsMatch(lines[j]) || method.IsMatch(lines[j]))
                {
                    end = j;
                    break;
                }
            }
            segments.Add((marker.Match(lines[start]).Groups[1].Value, string.Join('\n', lines[start..end])));
        }
        return segments;
    }

    /// <summary>段体里有没有一条「判失败」的等价物：抛异常、记失败、或宽度下限判定。</summary>
    private static bool HasAssertion(string body) =>
        body.Contains("throw new InvalidOperationException", StringComparison.Ordinal)
        || body.Contains("_failed++", StringComparison.Ordinal)
        || body.Contains("tooNarrow", StringComparison.Ordinal);

    private static string MethodBody(string source, string signature)
    {
        var from = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {signature}。");
        var open = source.IndexOf('{', from);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }
        Assert.Fail($"{signature} 的方法体没有闭合。");
        return "";
    }

    [Fact]
    public void NotesDoc_RecordsTheSmokeRunAndTheChecklist()
    {
        // 冒烟器的价值一半在「能跑」，一半在「跑过的证据留在文档里」：
        // 本轮 wine 实测的通过数与「退回修法即 7 失败」的变异结论必须可核对。
        var notes = Repo.Source("docs", "NOTES.md");
        Assert.Contains("原生对话框冒烟", notes);
        Assert.Contains("27 通过 / 0 失败", notes);
        Assert.Contains("Windows 真机 / wine 模拟器验证清单", notes);
    }

    // ---------- 交互断言清单（第一百三十七轮，控制仓简报 P1-1） ----------

    /// <summary>冒烟器里三段真实控件交互用例的方法名（唯一有真实控件触达的三段）。</summary>
    private static readonly string[] InteractionMethods =
        ["RunEndToEnd", "RunWriteBackEndToEnd", "RunScanResultsInteraction", "RunCandidatesInteraction", "RunSupplementsInteraction", "RunProfileLoadFailure", "RunTaskLoadFailure", "RunReportsLoadFailure", "RunDecisionsLoadFailure", "RunJournalLoadFailure", "RunDetailBreak"];

    /// <summary>
    /// 交互断言清单：每行是「方法 + 段名 + 该段里一条判失败的等价物」。
    /// <para>
    /// 存在理由：第一百三十三/一百三十六轮已钉住「每段至少有一条断言」，但**具体哪几条**没有登记——
    /// 段里五条断言删掉四条、只留一条，前两条检查照样全绿，而冒烟报告照样 PASS。
    /// 这里把三段交互用例的断言逐条登记成清单：删掉任何一条（或把它改写成不判失败的样子），
    /// 对应行即红；删掉整段由 <see cref="EveryInteractionSegment_IsCoveredByTheAssertionInventory"/> 拦下；
    /// 段被改名由 <see cref="InteractionAssertionInventory_NamesOnlyExistingSegments"/> 拦下。
    /// </para>
    /// <para>
    /// 清单是**源码切片**，不是「跑过」：冒烟器跑在 Windows 桌面运行时上，本环境不可执行。
    /// wine 模拟器执行证据在 <c>docs/NOTES.md</c>（wine 10.0 + Xvfb 下 46 通过 / 0 失败，退出码 0；
    /// 把「下一条」的门控改成「有行就可点」后同一命令 45 通过 / 1 失败、退出码 1）。
    /// </para>
    /// </summary>
    public static TheoryData<string, string, string> InteractionAssertions => new()
    {
        // 端到端主链路：新建配置 / 新建任务 / 校验失败不落盘。
        { "RunEndToEnd", "端到端：新建配置填写并保存（真实控件）", "dialog.DialogResult != DialogResult.OK" },
        { "RunEndToEnd", "端到端：新建配置填写并保存（真实控件）", "var reread = profiles.Get(saved.Id);" },
        { "RunEndToEnd", "端到端：新建配置填写并保存（真实控件）", "reread.Name != \"端到端配置\"" },
        { "RunEndToEnd", "端到端：新建配置填写并保存（真实控件）", "profiles.List().Count != before + 1" },
        { "RunEndToEnd", "端到端：新建任务填写并创建（真实控件）", "if (dialog.StartScan)" },
        { "RunEndToEnd", "端到端：新建任务填写并创建（真实控件）", "request.EndpointKeys.Count != 2" },
        { "RunEndToEnd", "端到端：新建任务填写并创建（真实控件）", "reread.SourceVersionNote != \"v1.2.3\"" },
        { "RunEndToEnd", "端到端：新建任务填写并创建（真实控件）", "reread.ProfileId != profileId" },
        { "RunEndToEnd", "端到端：保存校验失败不落盘（真实控件）", "dialog.Saved is not null" },
        { "RunEndToEnd", "端到端：保存校验失败不落盘（真实控件）", "profiles.List().Count != before" },
        { "RunEndToEnd", "端到端：保存校验失败不落盘（真实控件）", "请填写配置名称" },

        // 回写链路：扫描 / 候选 / 计划哈希 / 拒绝写入 / 落盘只改 /// 行 / 真实对话框确认。
        { "RunWriteBackEndToEnd", "回写链路：源码扫描产出注释缺口（真实服务）", "output.Endpoints.Count == 0" },
        { "RunWriteBackEndToEnd", "回写链路：源码扫描产出注释缺口（真实服务）", "missing.Count == 0" },
        { "RunWriteBackEndToEnd", "回写链路：手工候选接受（真实服务）", "accepted.ReviewState != \"accepted\"" },
        { "RunWriteBackEndToEnd", "回写链路：生成注释计划且哈希稳定（真实服务）", "first.Hash.Length == 0" },
        { "RunWriteBackEndToEnd", "回写链路：生成注释计划且哈希稳定（真实服务）", "second.Hash != first.Hash" },
        { "RunWriteBackEndToEnd", "回写链路：计划哈希不符时拒绝写入（真实服务）", "ex.StatusCode != 409" },
        { "RunWriteBackEndToEnd", "回写链路：计划哈希不符时拒绝写入（真实服务）", "File.ReadAllText(probeSource) != before" },
        { "RunWriteBackEndToEnd", "回写链路：确认后回写落盘且只改 /// 行（真实服务）", "applied.Status != \"applied\"" },
        { "RunWriteBackEndToEnd", "回写链路：确认后回写落盘且只改 /// 行（真实服务）", "AssertOnlyDocLinesChanged(before, after)" },
        { "RunWriteBackEndToEnd", "回写链路：确认后回写落盘且只改 /// 行（真实服务）", "AssertBomAndEolPreserved(probeSource)" },
        { "RunWriteBackEndToEnd", "回写链路：真实对话框点「确认回写」落盘（真实控件）", "confirmed != 1" },
        { "RunWriteBackEndToEnd", "回写链路：真实对话框点「确认回写」落盘（真实控件）", "AssertOnlyDocLinesChanged(beforeText, afterText)" },
        { "RunWriteBackEndToEnd", "回写链路：真实对话框点「确认回写」落盘（真实控件）", "已回写" },

        // 注释缺口对话框：产物 / 打开落点与计数覆盖率 / 逐条走 / 关键词 / 状态筛选。
        { "RunScanResultsInteraction", "缺口对话框：准备扫描产物（真实服务）", "output.Slots.Count < 2" },
        { "RunScanResultsInteraction", "缺口对话框：准备扫描产物（真实服务）", "扫描后没有 symbols.json" },
        { "RunScanResultsInteraction", "缺口对话框：打开即选中第一条且计数与覆盖率对得上（真实控件）", "counts.Text != expectedCounts" },
        { "RunScanResultsInteraction", "缺口对话框：打开即选中第一条且计数与覆盖率对得上（真实控件）", "coverage.Text != expectedCoverage" },
        { "RunScanResultsInteraction", "缺口对话框：打开即选中第一条且计数与覆盖率对得上（真实控件）", "list.SelectedIndices[0] != 0" },
        { "RunScanResultsInteraction", "缺口对话框：打开即选中第一条且计数与覆盖率对得上（真实控件）", "warnings.Visible != degradations.Count > 0" },
        { "RunScanResultsInteraction", "缺口对话框：上一条/下一条按钮逐条走（真实控件）", "if (prev.Enabled)" },
        { "RunScanResultsInteraction", "缺口对话框：上一条/下一条按钮逐条走（真实控件）", "navText.Text != ListAdvance.PositionText(" },
        { "RunScanResultsInteraction", "缺口对话框：上一条/下一条按钮逐条走（真实控件）", "steps > list.Items.Count" },
        { "RunScanResultsInteraction", "缺口对话框：关键词筛到零行与清空恢复（真实控件）", "list.Items.Count != 0" },
        { "RunScanResultsInteraction", "缺口对话框：关键词筛到零行与清空恢复（真实控件）", "!filter.Enabled" },
        { "RunScanResultsInteraction", "缺口对话框：关键词筛到零行与清空恢复（真实控件）", "hint.Text != expectedHint" },
        { "RunScanResultsInteraction", "缺口对话框：关键词筛到零行与清空恢复（真实控件）", "CountsLabel(dialog).Text != SlotText.CountsLine(output.Slots)" },
        { "RunScanResultsInteraction", "缺口对话框：状态筛选按缺失计数（真实控件）", "list.Items.Count != missing" },
        { "RunScanResultsInteraction", "缺口对话框：状态筛选按缺失计数（真实控件）", "filter.SelectedIndex = 1" },

        // 字体栈运行时命中（第一百三十九轮）：回退结果、界面与进程默认字体、等宽栈、真实对话框。
        { "RunFontStackRuntime", "字体栈：回退结果在运行时命中且界面与进程默认字体一致（真实控件）", "resolved != expected" },
        { "RunFontStackRuntime", "字体栈：回退结果在运行时命中且界面与进程默认字体一致（真实控件）", "if (!Installed(resolved))" },
        { "RunFontStackRuntime", "字体栈：回退结果在运行时命中且界面与进程默认字体一致（真实控件）", "label.Font.Name != rendered" },
        { "RunFontStackRuntime", "字体栈：回退结果在运行时命中且界面与进程默认字体一致（真实控件）", "Control.DefaultFont.Name != rendered" },
        { "RunFontStackRuntime", "字体栈：回退结果在运行时命中且界面与进程默认字体一致（真实控件）", "UiTokens.Mono.Value.FontFamily.Name != monoExpected" },
        { "RunFontStackRuntime", "字体栈：回退结果在运行时命中且界面与进程默认字体一致（真实控件）", "dialog.Font.Name != rendered" },

        // 注释候选对话框（第一百三十九轮）：产物 / 打开落点与计数 / 待确认问题被拒 / 接受并下一条 /
        // 筛到零行收掉回答框与覆盖勾选 / 撤销接受。
        { "RunCandidatesInteraction", "候选对话框：准备候选（真实服务）", "result.Imported != 2" },
        { "RunCandidatesInteraction", "候选对话框：准备候选（真实服务）", "store.Candidates[0].OpenQuestions.Count == 0" },
        { "RunCandidatesInteraction", "候选对话框：打开即选中第一条且计数对得上（真实控件）", "counts.Text != expectedCounts" },
        { "RunCandidatesInteraction", "候选对话框：打开即选中第一条且计数对得上（真实控件）", "list.SelectedIndices[0] != 0" },
        { "RunCandidatesInteraction", "候选对话框：打开即选中第一条且计数对得上（真实控件）", "boxes.Length != 2" },
        { "RunCandidatesInteraction", "候选对话框：打开即选中第一条且计数对得上（真实控件）", "answersCaption.Text != CandidateEdit.AnswersCaptionFor(" },
        { "RunCandidatesInteraction", "候选对话框：待确认问题未回答时接受被拒（真实控件）", "请先填写确认结果再接受" },
        { "RunCandidatesInteraction", "候选对话框：待确认问题未回答时接受被拒（真实控件）", "candidate.ReviewState != \"pending\"" },
        { "RunCandidatesInteraction", "候选对话框：填回答后接受并下一条（真实控件）", "candidate.ReviewState != \"accepted\"" },
        { "RunCandidatesInteraction", "候选对话框：填回答后接受并下一条（真实控件）", "status.Text != expectedStatus" },
        { "RunCandidatesInteraction", "候选对话框：填回答后接受并下一条（真实控件）", "list.SelectedIndices[0] != 1" },
        { "RunCandidatesInteraction", "候选对话框：筛到零行时回答框与覆盖勾选一起收掉（真实控件）", "hint.Text != expectedHint" },
        { "RunCandidatesInteraction", "候选对话框：筛到零行时回答框与覆盖勾选一起收掉（真实控件）", "boxes[1].Enabled || boxes[1].Text.Length != 0" },
        { "RunCandidatesInteraction", "候选对话框：筛到零行时回答框与覆盖勾选一起收掉（真实控件）", "overwrite.Enabled || overwrite.Checked" },
        { "RunCandidatesInteraction", "候选对话框：筛到零行时回答框与覆盖勾选一起收掉（真实控件）", "CandidateEdit.AnswersCaptionNoSelection" },
        { "RunCandidatesInteraction", "候选对话框：撤销接受回到待审阅（真实控件）", "status.Text != CandidateEdit.UndoDoneText" },
        { "RunCandidatesInteraction", "候选对话框：撤销接受回到待审阅（真实控件）", "ReviewState != \"pending\"" },

        // 补充说明对话框（第一百四十五轮）：夹具落盘 / 打开落点与编辑位 / 保存落盘与版本递增 /
        // 重复保存不递增 / 保留现状 / 筛到零行。
        { "RunSupplementsInteraction", "补充说明：准备检查结果（真实落盘）", "reread.Endpoints.Count != 2" },
        { "RunSupplementsInteraction", "补充说明：准备检查结果（真实落盘）", "editors.Count != 5" },
        { "RunSupplementsInteraction", "补充说明：打开即选中第一个接口且编辑位对得上（真实控件）", "list.SelectedIndices[0] != 0" },
        { "RunSupplementsInteraction", "补充说明：打开即选中第一个接口且编辑位对得上（真实控件）", "boxes.Length != expected.Count" },
        { "RunSupplementsInteraction", "补充说明：打开即选中第一个接口且编辑位对得上（真实控件）", "已保存补充 0 处" },
        { "RunSupplementsInteraction", "补充说明：敲接口说明并保存（真实控件）", "entry.Value != \"查询探针数据，按名称过滤。\"" },
        { "RunSupplementsInteraction", "补充说明：敲接口说明并保存（真实控件）", "after <= before" },
        { "RunSupplementsInteraction", "补充说明：敲接口说明并保存（真实控件）", "status.Text != SupplementEntries.SavedText(true)" },
        { "RunSupplementsInteraction", "补充说明：原样重复保存不递增（真实控件）", "status.Text != SupplementEntries.SavedText(false)" },
        { "RunSupplementsInteraction", "补充说明：原样重复保存不递增（真实控件）", "after != before" },
        { "RunSupplementsInteraction", "补充说明：勾选保留现状后保存（真实控件）", "!entry.KeepAsIs || entry.Value is not null" },
        { "RunSupplementsInteraction", "补充说明：关键词筛到零行（真实控件）", "hint.Text != expectedHint" },
        { "RunSupplementsInteraction", "补充说明：关键词筛到零行（真实控件）", "ButtonOf(dialog, \"保存本接口补充\").Enabled" },

        // 配置页读取失败态（第一百五十一轮，简报 P0-2）：正常读取不显示重试 / 失败态换掉清单并给唯一行动按钮 /
        // 仍失败时留在失败态 / 修好磁盘后重试恢复 / 恢复后编辑与探测回来 / 筛选状态下失败优先。
        { "RunProfileLoadFailure", "配置页：读取失败时清单区换成失败态并给出唯一行动按钮（真实控件）", "list.Items.Count != 0" },
        { "RunProfileLoadFailure", "配置页：读取失败时清单区换成失败态并给出唯一行动按钮（真实控件）", "title.Text != ProfileLoadState.FailureTitle" },
        { "RunProfileLoadFailure", "配置页：读取失败时清单区换成失败态并给出唯一行动按钮（真实控件）", "hint.Text != ProfileLoadState.FailureHint" },
        { "RunProfileLoadFailure", "配置页：读取失败时清单区换成失败态并给出唯一行动按钮（真实控件）", "visible.Length != 1 || visible[0].Text != ProfileLoadState.RetryLabel" },
        { "RunProfileLoadFailure", "配置页：读取失败时清单区换成失败态并给出唯一行动按钮（真实控件）", "ButtonOf(page, \"编辑\").Enabled || ButtonOf(page, \"连接与写入探测\").Enabled" },
        { "RunProfileLoadFailure", "配置页：读取失败时清单区换成失败态并给出唯一行动按钮（真实控件）", "status.Text.StartsWith(ProfileLoadState.FailureTitle + \"：\"" },
        { "RunProfileLoadFailure", "配置页：失败态下点「重试」仍失败则留在失败态（真实控件）", "Click(page, ProfileLoadState.RetryLabel);" },
        { "RunProfileLoadFailure", "配置页：失败态下点「重试」仍失败则留在失败态（真实控件）", "title.Text != ProfileLoadState.FailureTitle" },
        { "RunProfileLoadFailure", "配置页：失败态下点「重试」仍失败则留在失败态（真实控件）", "!ButtonOf(page, ProfileLoadState.RetryLabel).Visible" },
        { "RunProfileLoadFailure", "配置页：修好磁盘后点「重试」恢复清单（真实控件）", "list.Items.Count != expected" },
        { "RunProfileLoadFailure", "配置页：修好磁盘后点「重试」恢复清单（真实控件）", "status.Text.StartsWith(ProfileLoadState.FailureTitle" },
        { "RunProfileLoadFailure", "配置页：恢复后选中行启用编辑与探测（真实控件）", "!ButtonOf(page, \"编辑\").Enabled" },
        { "RunProfileLoadFailure", "配置页：恢复后选中行启用编辑与探测（真实控件）", "!ButtonOf(page, \"连接与写入探测\").Enabled" },
        { "RunProfileLoadFailure", "配置页：筛选状态下读取失败仍说失败不说筛到零行（真实控件）", "title.Text != ProfileLoadState.FailureTitle" },
        { "RunProfileLoadFailure", "配置页：筛选状态下读取失败仍说失败不说筛到零行（真实控件）", "visible.Length != 1 || visible[0].Text != ProfileLoadState.RetryLabel" },

        // 任务页读取失败态（第一百五十三轮，简报 P1-3）：正常读取不显示重试 / 失败态换掉清单并给唯一行动按钮 /
        // 仍失败时留在失败态 / 修好磁盘后重试恢复 / 恢复后详情动作回来 / 筛选状态下失败优先。
        { "RunTaskLoadFailure", "任务页：读取失败时清单区换成失败态并给出唯一行动按钮（真实控件）", "list.Items.Count != 0" },
        { "RunTaskLoadFailure", "任务页：读取失败时清单区换成失败态并给出唯一行动按钮（真实控件）", "title.Text != LoadFailureState.Tasks.Title" },
        { "RunTaskLoadFailure", "任务页：读取失败时清单区换成失败态并给出唯一行动按钮（真实控件）", "hint.Text != LoadFailureState.Tasks.Hint" },
        { "RunTaskLoadFailure", "任务页：读取失败时清单区换成失败态并给出唯一行动按钮（真实控件）", "visible.Length != 1 || visible[0].Text != LoadFailureState.RetryLabel" },
        { "RunTaskLoadFailure", "任务页：读取失败时清单区换成失败态并给出唯一行动按钮（真实控件）", "ButtonOf(page, \"查看报告与导出\").Enabled" },
        { "RunTaskLoadFailure", "任务页：读取失败时清单区换成失败态并给出唯一行动按钮（真实控件）", "status.Text.StartsWith(LoadFailureState.Tasks.Title + \"：\"" },
        { "RunTaskLoadFailure", "任务页：失败态下点「重试」仍失败则留在失败态（真实控件）", "Click(page, LoadFailureState.RetryLabel);" },
        { "RunTaskLoadFailure", "任务页：失败态下点「重试」仍失败则留在失败态（真实控件）", "title.Text != LoadFailureState.Tasks.Title" },
        { "RunTaskLoadFailure", "任务页：失败态下点「重试」仍失败则留在失败态（真实控件）", "!ButtonOf(page, LoadFailureState.RetryLabel).Visible" },
        { "RunTaskLoadFailure", "任务页：修好磁盘后点「重试」恢复清单（真实控件）", "list.Items.Count != expected" },
        { "RunTaskLoadFailure", "任务页：修好磁盘后点「重试」恢复清单（真实控件）", "status.Text.StartsWith(LoadFailureState.Tasks.Title" },
        { "RunTaskLoadFailure", "任务页：恢复后选中行启用详情动作（真实控件）", "!ButtonOf(page, \"查看报告与导出\").Enabled" },
        { "RunTaskLoadFailure", "任务页：筛选状态下读取失败仍说失败不说筛到零行（真实控件）", "title.Text != LoadFailureState.Tasks.Title" },
        { "RunTaskLoadFailure", "任务页：筛选状态下读取失败仍说失败不说筛到零行（真实控件）", "visible.Length != 1 || visible[0].Text != LoadFailureState.RetryLabel" },

        // 报告对话框读取失败态（第一百五十三轮，简报 P1-3）：正常打开预览可见且不显示重试 /
        // 任务目录读不出来时换成失败态并停用四个导出 / 仍失败时留在失败态 / 修好后重试恢复预览与导出。
        { "RunReportsLoadFailure", "报告对话框：正常打开时预览可见且不显示「重试」（真实控件）", "if (!preview.Visible)" },
        { "RunReportsLoadFailure", "报告对话框：正常打开时预览可见且不显示「重试」（真实控件）", "ButtonOf(dialog, LoadFailureState.RetryLabel).Visible" },
        { "RunReportsLoadFailure", "报告对话框：正常打开时预览可见且不显示「重试」（真实控件）", "if (!ButtonOf(dialog, label).Enabled)" },
        { "RunReportsLoadFailure", "报告对话框：任务目录读不出来时换成失败态并停用四个导出（真实控件）", "hint.Text.StartsWith(LoadFailureState.Reports.Title + \"：\"" },
        { "RunReportsLoadFailure", "报告对话框：任务目录读不出来时换成失败态并停用四个导出（真实控件）", "if (preview.Visible)" },
        { "RunReportsLoadFailure", "报告对话框：任务目录读不出来时换成失败态并停用四个导出（真实控件）", "if (ButtonOf(dialog, label).Enabled)" },
        { "RunReportsLoadFailure", "报告对话框：仍读不出来时点「重试」留在失败态（真实控件）", "Click(dialog, LoadFailureState.RetryLabel);" },
        { "RunReportsLoadFailure", "报告对话框：仍读不出来时点「重试」留在失败态（真实控件）", "!ButtonOf(dialog, LoadFailureState.RetryLabel).Visible" },
        { "RunReportsLoadFailure", "报告对话框：修好任务目录后点「重试」恢复预览与导出（真实控件）", "if (!preview.Visible || preview.Text.Length == 0)" },
        { "RunReportsLoadFailure", "报告对话框：修好任务目录后点「重试」恢复预览与导出（真实控件）", "if (HintLabel(dialog).Visible)" },

        // 差异确认读取失败态（第一百五十三轮，简报 P1-3）：两种失败态各走「失败 → 重试仍失败 → 修好恢复」，
        // 外加「尚未检查」不是失败态、不显示「重试」。
        { "RunDecisionsLoadFailure", "差异确认：检查结果读不出来时换成失败态并给出「重试」（真实控件）", "hint.Text.StartsWith(LoadFailureState.Decisions.Title + \"：\"" },
        { "RunDecisionsLoadFailure", "差异确认：检查结果读不出来时换成失败态并给出「重试」（真实控件）", "if (filter.Enabled)" },
        { "RunDecisionsLoadFailure", "差异确认：仍读不出来时点「重试」留在失败态（真实控件）", "Click(dialog, LoadFailureState.RetryLabel);" },
        { "RunDecisionsLoadFailure", "差异确认：仍读不出来时点「重试」留在失败态（真实控件）", "!ButtonOf(dialog, LoadFailureState.RetryLabel).Visible" },
        { "RunDecisionsLoadFailure", "差异确认：修好检查结果后点「重试」退出失败态（真实控件）", "hint.Text.StartsWith(LoadFailureState.Decisions.Title" },
        { "RunDecisionsLoadFailure", "差异确认：已保存决策读不出来时指名「重试」且说明保存停用（真实控件）", "hint.Text.StartsWith(LoadFailureState.Decisions.SavedTitle + \"：\"" },
        { "RunDecisionsLoadFailure", "差异确认：已保存决策读不出来时指名「重试」且说明保存停用（真实控件）", "hint.Text.Contains(LoadFailureState.Decisions.SavedHint" },
        { "RunDecisionsLoadFailure", "差异确认：修好已保存决策后点「重试」退出失败态（真实控件）", "hint.Text.StartsWith(LoadFailureState.Decisions.SavedTitle" },
        { "RunDecisionsLoadFailure", "差异确认：尚未检查时不显示「重试」（重试解决不了它，真实控件）", "ButtonOf(dialog, LoadFailureState.RetryLabel).Visible" },

        // 任务日志读取失败态（第一百五十八轮）：失败 → 重试仍失败 → 解锁后恢复。
        { "RunJournalLoadFailure", "任务日志：正常读取有行、不显示「重试」、关键词框可用（真实控件）", "list.Items.Count != 2" },
        { "RunJournalLoadFailure", "任务日志：正常读取有行、不显示「重试」、关键词框可用（真实控件）", "if (!KeywordBox(dialog).Enabled)" },
        { "RunJournalLoadFailure", "任务日志：打开时键盘落在清单上（真实控件）", "if (!list.Focused)" },
        { "RunJournalLoadFailure", "任务日志：打开时键盘落在清单上（真实控件）", "list.SelectedIndices[0] != list.Items.Count - 1" },
        { "RunJournalLoadFailure", "任务日志：日志读不出来时换成失败态并给出「重试」（真实控件）", "FileShare.None" },
        { "RunJournalLoadFailure", "任务日志：日志读不出来时换成失败态并给出「重试」（真实控件）", "LoadFailureState.Journal.Title + \"：\"" },
        { "RunJournalLoadFailure", "任务日志：日志读不出来时换成失败态并给出「重试」（真实控件）", "失败提示里出现双标点" },
        { "RunJournalLoadFailure", "任务日志：日志读不出来时换成失败态并给出「重试」（真实控件）", "if (list.Items.Count != 0)" },
        { "RunJournalLoadFailure", "任务日志：日志读不出来时换成失败态并给出「重试」（真实控件）", "if (KeywordBox(dialog).Enabled)" },
        { "RunJournalLoadFailure", "任务日志：仍读不出来时点「重试」留在失败态（真实控件）", "重试后提示变成" },
        { "RunJournalLoadFailure", "任务日志：解锁后点「重试」恢复清单、计数行与关键词框（真实控件）", "counts.Text != JournalEntries.CountsLine(2)" },
        { "RunJournalLoadFailure", "任务日志：解锁后点「重试」恢复清单、计数行与关键词框（真实控件）", "if (hint.Visible)" },
        { "RunJournalLoadFailure", "任务日志：恢复后仍选中最后一条、Enter/Esc 仍是「关闭」（真实控件）", "dialog.AcceptButton is not Button accept" },
        { "RunJournalLoadFailure", "任务日志：恢复后仍选中最后一条、Enter/Esc 仍是「关闭」（真实控件）", "dialog.CancelButton is not Button cancel" },
        { "RunDetailBreak", "任务页：超长中文任务名在详情区换行而不是被静默裁掉（真实控件）", "if (name.Text != longName)" },
        { "RunDetailBreak", "任务页：超长中文任务名在详情区换行而不是被静默裁掉（真实控件）", "AssertNoSilentClipping(grid, \"超长中文任务名\")" },
        { "RunDetailBreak", "任务页：超长路径的范围行在详情区换行而不是被静默裁掉（真实控件）", "if (!range.Text.Contains(longPath, StringComparison.Ordinal))" },
        { "RunDetailBreak", "任务页：超长路径的范围行在详情区换行而不是被静默裁掉（真实控件）", "AssertNoSilentClipping(grid, \"超长路径的范围行\")" },
        { "RunDetailBreak", "任务页：超长错误消息在状态行有省略号而不是被静默裁掉（真实控件）", "if (!status.AutoEllipsis)" },
        { "RunDetailBreak", "任务页：超长错误消息在状态行有省略号而不是被静默裁掉（真实控件）", "if (status.Height != heightBefore)" },
        { "RunDetailBreak", "任务页：超长任务名下列宽按「标识列吸收剩余宽度」口径落地（真实控件）", "if (sum != expected)" },
        { "RunDetailBreak", "任务页：把分隔条拖到最右后标识列吸收剩余宽度、列合计对齐可视宽度（真实控件）", "if (widths[0] != client - fixedSum)" },
    };

    [Theory]
    [MemberData(nameof(InteractionAssertions))]
    public void InteractionAssertion_IsStillInItsSegment(string method, string segment, string fragment)
    {
        var body = SegmentBody(segment);
        Assert.True(
            body.Contains(fragment, StringComparison.Ordinal),
            $"{method} 的「{segment}」段里找不到断言「{fragment}」——断言被删或改写成不判失败的样子，"
            + "这一段就变成「跑过、什么也没判」，而冒烟报告照样 PASS。");
    }

    [Fact]
    public void EveryInteractionSegment_IsCoveredByTheAssertionInventory()
    {
        var inventory = InteractionAssertions
            .Select(row => (Method: (string)row[0], Segment: (string)row[1]))
            .ToList();
        // 地板：清单行数掉下去说明有人整批删行，检查会静默空转。
        Assert.True(inventory.Count >= 30, $"交互断言清单只有 {inventory.Count} 行（地板 30）。");
        foreach (var method in InteractionMethods)
        {
            var body = MethodBody(SmokeProgram, $"private static void {method}(");
            var names = Regex.Matches(body, @"RunAction\(""([^""]+)""").Select(m => m.Groups[1].Value).ToList();
            Assert.True(names.Count >= 3, $"{method} 里只有 {names.Count} 个 RunAction 段。");
            var covered = inventory.Where(r => r.Method == method).Select(r => r.Segment).ToHashSet(StringComparer.Ordinal);
            var missing = names.Where(n => !covered.Contains(n)).ToList();
            Assert.True(missing.Count == 0, $"{method} 里这些段没有进断言清单：" + string.Join("、", missing));
        }
    }

    [Fact]
    public void InteractionAssertionInventory_NamesOnlyExistingSegments()
    {
        // 段被改名或删除时，清单里那一行会静默失效（找不到段就永远不检查）——这里把它变成红。
        var segments = Segments("RunAction").Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var stale = InteractionAssertions
            .Select(row => (string)row[1])
            .Where(name => !segments.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(stale.Count == 0, "断言清单里这些段名在冒烟器里不存在（段被改名或删除）：" + string.Join("、", stale));
    }

    private static string SegmentBody(string name)
    {
        var segment = Segments("RunAction").FirstOrDefault(s => s.Name == name);
        Assert.True(segment.Body is not null, $"冒烟器里找不到段「{name}」。");
        return segment.Body!;
    }
}
