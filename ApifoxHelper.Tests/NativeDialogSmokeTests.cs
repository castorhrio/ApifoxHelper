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
/// 真实执行证据在 <c>docs/NOTES.md</c> 的「真机/模拟器验证清单」一节（wine 10.0 + Xvfb 下
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
            .SelectMany(s => Regex.Matches(s.Text, @"(?m)^\s*(?:internal|public)\s+(?:sealed\s+)?class\s+(\w+Dialog)\s*:\s*Form\b")
                .Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>冒烟清单里 <c>new XxxDialog(</c> 的类名（去重排序）。</summary>
    private static List<string> DialogClassesInSmoke() =>
        Regex.Matches(SmokeProgram, @"new\s+(\w+Dialog)\s*\(")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void EveryDialogClass_IsConstructedByTheSmokeProgram()
    {
        // 漏登记 = 那个对话框永远不会被构造，第一百二十八轮那类缺陷在它身上照样能溜过去。
        var onDisk = DialogClassesOnDisk();
        var inSmoke = DialogClassesInSmoke();
        var missing = onDisk.Except(inSmoke, StringComparer.Ordinal).ToList();
        Assert.True(
            missing.Count == 0,
            "以下对话框未在 ApifoxHelper.Smoke/Program.cs 的 Cases 里登记构造：" + string.Join("、", missing));
        // 地板：对话框数量掉下去说明扫描正则或目录坏了，检查会静默空转。
        Assert.True(onDisk.Count >= 20, $"只扫到 {onDisk.Count} 个对话框类，扫描口径可能已失效。");
    }

    [Fact]
    public void SmokeProgram_ConstructsNoDialogThatDoesNotExist()
    {
        // 反向：清单里留着已删除的类，冒烟工程根本编译不过——但这条断言让「删类忘了删清单」
        // 在 Linux 侧就报出具体名字，而不是等一次 Windows 编译。
        var onDisk = DialogClassesOnDisk();
        var stale = DialogClassesInSmoke().Except(onDisk, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0, "冒烟清单里有磁盘上不存在的对话框：" + string.Join("、", stale));
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
        var code = string.Join('\n', SmokeProgram.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
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

    [Fact]
    public void NotesDoc_RecordsTheSmokeRunAndTheChecklist()
    {
        // 冒烟器的价值一半在「能跑」，一半在「跑过的证据留在文档里」：
        // 本轮 wine 实测的通过数与「退回修法即 7 失败」的变异结论必须可核对。
        var notes = Repo.Source("docs", "NOTES.md");
        Assert.Contains("原生对话框冒烟", notes);
        Assert.Contains("27 通过 / 0 失败", notes);
        Assert.Contains("真机/模拟器验证清单", notes);
    }
}
