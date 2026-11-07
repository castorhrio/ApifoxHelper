using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 任务页分割面板断言（DESIGN.md §6「列表行」分割条款，第三十八轮）：分割距离此前只在
/// `_split.Width &gt; 800` 时设置，而壳最小窗口（1024，减导航 200 与内容留白 32）下分割面板约 776px——
/// 门控恒假，SplitterDistance 停在 WinForms 默认值 50px（.NET 8 参考程序集文档核实），任务列表只剩一条缝；
/// 第三十六轮「启动回到上次使用的页面」落地后，恢复的窄窗口 + 上次页面为检查任务时启动即触发。
/// 另：FixedPanel 缺省 None，窗口拖大时详情区按比例放大、偏离 420 设计宽。修复口径：FixedPanel=Panel2
/// （详情保持设计宽、列表吸收剩余，与第三十七轮标识列吸收剩余可视宽度同一方向）+ 常量化可行性门控。
/// 接线由行首正则机械检查钉住（被注释掉也算删除）；真机观感随 G-D5 复核。
/// </summary>
public class SplitLayoutTests
{
    [Fact]
    public void TasksPage_FixedPanel_KeepsDetailWidthOnResize()
    {
        var tasks = Repo.Source("Desktop", "Native", "TasksPage.cs");
        // 分割口径常量单一落点：首次显示的分割距离与可行性门控用同一组值。
        Assert.Matches(@"(?m)^\s*private const int DetailDesignWidth = 420;", tasks);
        Assert.Matches(@"(?m)^\s*private const int DetailMinWidth = 360;", tasks);
        Assert.Matches(@"(?m)^\s*private const int ListMinWidth = 320;", tasks);
        // 构造期即固定详情区：缺省 None 会让详情区随窗口比例放大，偏离设计宽。
        Assert.Matches(
            @"(?m)^\s*private readonly SplitContainer _split = new\(\) \{ Dock = DockStyle\.Fill, BackColor = UiTokens\.Line, FixedPanel = FixedPanel\.Panel2 \};",
            tasks);
        Assert.DoesNotContain("FixedPanel.None", tasks, StringComparison.Ordinal);
    }

    [Fact]
    public void TasksPage_SplitDistance_IsFeasibleAtMinimumWindowWidth()
    {
        var tasks = Repo.Source("Desktop", "Native", "TasksPage.cs");
        var shown = Slice(tasks, "public void OnPageShown()", "public void OnPageHidden()");
        // 门槛按可行性条件（详情设计宽 + 列表下限 + 分隔条宽）：壳最小窗口宽度下同样成立。
        Assert.Matches(
            @"(?m)^\s*if \(!_splitAdjusted && _split\.Width >= DetailDesignWidth \+ ListMinWidth \+ _split\.SplitterWidth\)",
            shown);
        Assert.Matches(@"(?m)^\s*_split\.Panel2MinSize = DetailMinWidth;", shown);
        Assert.Matches(@"(?m)^\s*_split\.SplitterDistance = _split\.Width - DetailDesignWidth;", shown);
        Assert.Matches(@"(?m)^\s*_split\.Panel1MinSize = ListMinWidth;", shown);
        // 旧魔数门控不得回归：任务页源码不再出现 800。
        Assert.DoesNotContain("800", tasks, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_DeclaresSplitRule()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("分割", section);
        Assert.Contains("FixedPanel", section);
        Assert.Contains("420", section);
        Assert.Contains("50px", section);
    }

    /// <summary>取 [start, end) 之间的源码段：把接线断言限定在方法体内。</summary>
    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
