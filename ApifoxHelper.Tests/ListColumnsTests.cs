using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 主页列表列宽自适应断言（DESIGN.md §6「列表行」列宽条款）：冻结页面的表格是流式布局、任何窗口宽度都完整可见；
/// 原生列表固定像素列宽合计超过可视宽度时列尾被横向滚动条藏住——默认窗口（1400×900）下任务详情列表面板约
/// 732px，七列合计 872px，「更新时间」列不可见；最小窗口下配置列表同样被藏。修复口径：标识列（首列）
/// 吸收剩余可视宽度（<see cref="ApifoxHelper.Desktop.Native.ListColumns"/>，与测试工程共享编译），
/// 接线由行首正则机械检查钉住（被注释掉也算删除）；滚动条消失后列表高度回填等真机观感随 G-D5 复核。
/// </summary>
public class ListColumnsTests
{
    private const int FillMinimum = 200;
    private static readonly int[] ColumnWidths = [200, 140, 150, 56, 76, 110, 140];

    [Fact]
    public void FillWidth_MakesColumnsFitVisibleWidthExactly()
    {
        // 可视宽度恰为固定列合计 + 下限：标识列取下限，合计正好铺满、不出横向滚动条。
        var fixedSum = ColumnWidths.Skip(1).Sum();
        Assert.Equal(FillMinimum, ApifoxHelper.Desktop.Native.ListColumns.FillWidth(fixedSum + FillMinimum, FillMinimum, ColumnWidths));
    }

    [Fact]
    public void FillWidth_GivesSurplusToIdentifierColumn()
    {
        // 多出的可视宽度全部给标识列（最长的任务名称），固定列不变。
        var fixedSum = ColumnWidths.Skip(1).Sum();
        Assert.Equal(FillMinimum + 100, ApifoxHelper.Desktop.Native.ListColumns.FillWidth(fixedSum + FillMinimum + 100, FillMinimum, ColumnWidths));
    }

    [Fact]
    public void FillWidth_FallsBackToDesignedWidthWhenTooNarrow()
    {
        // 可视宽度放不下固定列合计：标识列回退设计宽度，横向滚动条如实出现（不把固定列挤破）。
        Assert.Equal(FillMinimum, ApifoxHelper.Desktop.Native.ListColumns.FillWidth(10, FillMinimum, ColumnWidths));
        Assert.Equal(FillMinimum, ApifoxHelper.Desktop.Native.ListColumns.FillWidth(0, FillMinimum, ColumnWidths));
    }

    [Fact]
    public void FillWidth_TreatsFirstEntryAsFillColumnOnly()
    {
        // 首列快照值不参与合计（首列就是要被重算的列）：换一个首列占位值，结果不变。
        // 下限取 100（两份快照的可视宽都放得下固定列），首列若被误计入合计，两次结果立刻不等。
        var shifted = new int[] { 999, 140, 150, 56, 76, 110, 140 };
        Assert.Equal(
            ApifoxHelper.Desktop.Native.ListColumns.FillWidth(1000, 100, ColumnWidths),
            ApifoxHelper.Desktop.Native.ListColumns.FillWidth(1000, 100, shifted));
    }

    [Fact]
    public void SharedSource_IsCompiledIntoTestProject()
    {
        // 口径只有一份：测试断言对准主工程真实实现（共享编译清单里必须有 ListColumns.cs）。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ListColumns.cs", csproj);
    }

    [Fact]
    public void TasksPage_FillsFirstColumn_OnResizeAndRebuild()
    {
        var tasks = Repo.Source("Desktop", "Native", "TasksPage.cs");
        // 设计宽度常量单一落点：建列与回退下限用同一个值。
        Assert.Matches(@"(?m)^\s*private const int TaskNameColumnWidth = 200;", tasks);
        Assert.Matches(@"(?m)^\s*_list\.Columns\.Add\(""任务"", TaskNameColumnWidth\);", tasks);
        // Resize 接线与重算调用（行首正则：接线被注释掉也算删除）。
        Assert.Matches(@"(?m)^\s*_list\.Resize \+= \(_, _\) => FitColumns\(\);", tasks);
        Assert.Matches(@"(?m)^\s*_list\.Columns\[0\]\.Width = ListColumns\.FillWidth\(_list\.ClientSize\.Width, TaskNameColumnWidth, widths\);", tasks);
        var load = Slice(tasks, "private void LoadTasks()", "private void SelectTask(string taskId)");
        Assert.Matches(@"(?m)^\s*_list\.EndUpdate\(\);", load);
        Assert.Matches(@"(?m)^\s*FitColumns\(\);", load);
    }

    [Fact]
    public void ProjectsPage_FillsFirstColumn_OnResizeAndRebuild()
    {
        var projects = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        Assert.Matches(@"(?m)^\s*private const int ProfileNameColumnWidth = 180;", projects);
        Assert.Matches(@"(?m)^\s*_list\.Columns\.Add\(""名称"", ProfileNameColumnWidth\);", projects);
        Assert.Matches(@"(?m)^\s*_list\.Resize \+= \(_, _\) => FitColumns\(\);", projects);
        Assert.Matches(@"(?m)^\s*_list\.Columns\[0\]\.Width = ListColumns\.FillWidth\(_list\.ClientSize\.Width, ProfileNameColumnWidth, widths\);", projects);
        var load = Slice(projects, "private void LoadProfiles()", "private void FitColumns()");
        Assert.Matches(@"(?m)^\s*_list\.EndUpdate\(\);", load);
        Assert.Matches(@"(?m)^\s*FitColumns\(\);", load);
    }

    [Fact]
    public void DesignDoc_DeclaresColumnFitRule()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("列宽", section);
        Assert.Contains("标识列", section);
        Assert.Contains("`ListColumns`", section);
    }

    /// <summary>取 [start, end) 之间的源码段：把「重建后调用」的断言限定在加载方法体内。</summary>
    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
