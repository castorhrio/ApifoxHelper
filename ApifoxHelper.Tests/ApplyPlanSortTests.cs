using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 注释回写文件清单的列排序断言（DESIGN.md §6「列表列排序」，第一百零五轮）：
/// 两个主页面（第四十七轮）、检查结果的两张清单（第九十七轮）、接口追踪明细（第九十八轮）、
/// 差异确认的条目清单（第九十九轮）、注释候选的候选清单（第一百轮）、响应契约的契约清单（第一百零一轮）、
/// 注释缺口清单（第一百零二轮）、生成文档核对的条目清单（第一百零三轮）与任务日志的记录清单（第一百零四轮）
/// 点击列头即按该列排序，而注释回写的文件清单（源码文件、回写范围、注释处数、回写结果四列）
/// 点击列头没有任何反应——这一屏的清单是整份计划的源码文件，「把只导出补丁的那几个集中看」
/// 「按注释处数排」「把已回写与未写入的分开」只能逐行扫，属同壳不同行为。
/// 排序状态与表头标记仍取 <see cref="ListSort"/> 那一份，「这一列按什么排」收进
/// <see cref="ApplyPlanSort"/> 单一口径（与单元格取值同源），界面接线由行首正则与源码切片
/// 机械检查钉住（被注释掉也算删除）。
/// </summary>
public class ApplyPlanSortTests
{
    /// <summary>一行在屏幕上被读作的那四格（与对话框建行处同一份取值）。</summary>
    private sealed record Row(string Path, string Scope, int Edits, string Result);

    private static IComparable? Key(Row row, int column) => ApplyPlanSort.Key(row.Path, row.Scope, row.Edits, row.Result, column);

    // ---------- 排序键与单元格同源（真执行） ----------

    [Fact]
    public void Key_MirrorsTheFourCells()
    {
        var row = new Row("src/Orders/OrderController.cs", "允许回写", 12, "已回写");
        // 源码文件列：与首格同一份取值。
        Assert.Equal("src/Orders/OrderController.cs", ApplyPlanSort.Key(row.Path, row.Scope, row.Edits, row.Result, 0));
        // 回写范围列：与第二格同一份（ApplyPlanEntries.ScopeText）。
        Assert.Equal("允许回写", ApplyPlanSort.Key(row.Path, row.Scope, row.Edits, row.Result, 1));
        // 注释处数列：处数本身（数值），不是那一格的文字。
        Assert.Equal(12, ApplyPlanSort.Key(row.Path, row.Scope, row.Edits, row.Result, 2));
        // 回写结果列：与第四格同一份中文映射（TaskDisplay.FileApplyStateText）。
        Assert.Equal("已回写", ApplyPlanSort.Key(row.Path, row.Scope, row.Edits, row.Result, 3));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(9)]
    public void Key_OutsideTheColumnsIsNull(int column)
    {
        // 列号落在建列之外交 null：不索引不存在的列，也不把邻列的值当成这一列。
        Assert.Null(ApplyPlanSort.Key("src/A.cs", "允许回写", 3, "已回写", column));
    }

    // ---------- 排序结果（真执行：ListSort.Order 跑主工程同一份代码） ----------

    [Fact]
    public void Ordering_ByScopeGroupsTheSameScopesTogether()
    {
        // 回写范围列按那一格的文字排：同一范围的行聚在一起（「把只导出补丁的那几个集中看」）。
        var rows = new[]
        {
            new Row("src/A.cs", "允许回写", 1, ""),
            new Row("src/B.cs", "只导出补丁", 2, ""),
            new Row("src/C.cs", "允许回写", 3, ""),
            new Row("src/D.cs", "只导出补丁", 4, ""),
        };
        var expected = rows.OrderBy(r => r.Scope, StringComparer.CurrentCulture).Select(r => r.Path).ToList();
        Assert.Equal(expected, ListSort.Order(rows, 1, false, Key).Select(r => r.Path));
        // 反向：同值行仍按数据源顺序（两个方向都稳定），故与 LINQ 的稳定降序同序。
        Assert.Equal(
            rows.OrderByDescending(r => r.Scope, StringComparer.CurrentCulture).Select(r => r.Path),
            ListSort.Order(rows, 1, true, Key).Select(r => r.Path));
    }

    [Fact]
    public void Ordering_ByEditsUsesNumericOrder()
    {
        // 注释处数列按数值排（与两个主页面的数量列同一口径）：按文字排会把「10」排到「2」前。
        var rows = new[]
        {
            new Row("src/A.cs", "允许回写", 10, ""),
            new Row("src/B.cs", "允许回写", 2, ""),
        };
        Assert.Equal(["src/B.cs", "src/A.cs"], ListSort.Order(rows, 2, false, Key).Select(r => r.Path));
        Assert.Equal(["src/A.cs", "src/B.cs"], ListSort.Order(rows, 2, true, Key).Select(r => r.Path));
    }

    [Fact]
    public void Ordering_ByResultPutsTheNotYetAppliedRowsFirst()
    {
        // 回写结果列按那一格的文字排：还没有结果的行那一格是空串，升序排在最前
        // （「还没回写」正是回写前这一屏要看的），已回写与失败各自聚在一起。
        var rows = new[]
        {
            new Row("src/A.cs", "允许回写", 1, "已回写"),
            new Row("src/B.cs", "允许回写", 1, ""),
            new Row("src/C.cs", "允许回写", 1, "写入失败"),
        };
        var ordered = ListSort.Order(rows, 3, false, Key).Select(r => r.Result).ToList();
        Assert.Equal("", ordered[0]);
        // 同一结果的行相邻，且升序与降序互为镜像。
        Assert.Equal(ordered.OrderBy(text => text, StringComparer.CurrentCulture), ordered);
        Assert.Equal(Enumerable.Reverse(ordered), ListSort.Order(rows, 3, true, Key).Select(r => r.Result));
    }

    [Fact]
    public void Ordering_ByPathUsesTheNaturalOrder()
    {
        // 源码文件列常带序号（同一目录下第 2 个与第 10 个文件）：数字段按数值排，与资源管理器名称列同序。
        var rows = new[]
        {
            new Row("src/Orders/Order10.cs", "允许回写", 1, ""),
            new Row("src/Orders/Order2.cs", "允许回写", 1, ""),
        };
        Assert.Equal(["src/Orders/Order2.cs", "src/Orders/Order10.cs"], ListSort.Order(rows, 0, false, Key).Select(r => r.Path));
    }

    [Fact]
    public void Ordering_IsStableForEqualKeysInBothDirections()
    {
        // 同值行在两个方向都保持数据源顺序（反向不把同值的行打乱重排）。
        var rows = new[]
        {
            new Row("src/A.cs", "允许回写", 1, ""),
            new Row("src/B.cs", "允许回写", 1, ""),
            new Row("src/C.cs", "允许回写", 1, ""),
        };
        Assert.Equal(["src/A.cs", "src/B.cs", "src/C.cs"], ListSort.Order(rows, 1, false, Key).Select(r => r.Path));
        Assert.Equal(["src/A.cs", "src/B.cs", "src/C.cs"], ListSort.Order(rows, 1, true, Key).Select(r => r.Path));
    }

    [Fact]
    public void FilteringComesBeforeSorting()
    {
        // 筛选先于排序（与另九处同一顺序）：筛掉的行不参与排序。
        // 关键词字段与对话框同一份（源码文件、回写范围、回写结果三格）。
        var rows = new[]
        {
            new Row("src/Orders/OrderController.cs", "允许回写", 3, ""),
            new Row("src/Users/UserController.cs", "允许回写", 1, ""),
            new Row("src/Orders/OrderQuery.cs", "只导出补丁", 7, ""),
        };
        var visible = ListFilter.Apply(rows, "orders", r => ApplyPlanEntries.KeywordFields(r.Path, r.Scope, r.Result));
        Assert.Equal(["src/Orders/OrderController.cs", "src/Orders/OrderQuery.cs"], visible.Select(r => r.Path));
        // 排序只在筛出来的行里发生：按注释处数升序，另一行不在结果里。
        Assert.Equal(["src/Orders/OrderController.cs", "src/Orders/OrderQuery.cs"], ListSort.Order(visible, 2, false, Key).Select(r => r.Path));
    }

    // ---------- 对话框接线（机械检查） ----------

    [Fact]
    public void Dialog_WiresTheListThroughTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");
        Assert.Matches(@"(?m)^\s*private int _sortColumn = ListSort\.None;$", source);
        Assert.Matches(@"(?m)^\s*private bool _sortDescending;$", source);
        Assert.Matches(@"(?m)^\s*_titles = _list\.Columns\.Cast<ColumnHeader>\(\)\.Select\(column => column\.Text\)\.ToArray\(\);", source);
        Assert.Matches(@"(?m)^\s*_list\.ColumnClick \+= \(_, e\) => SortBy\(e\.Column\);", source);
        // 方向翻转只在共享口径一处（对话框不自己写 !descending）。
        Assert.Matches(@"(?m)^\s*\(_sortColumn, _sortDescending\) = ListSort\.Toggle\(_sortColumn, _sortDescending, column\);", source);
        Assert.DoesNotContain("_sortDescending = !", source, StringComparison.Ordinal);
        // 表头标记按原标题重算（不叠加、不就地拼方向字面）。
        Assert.Matches(@"(?m)^\s*_list\.Columns\[i\]\.Text = ListSort\.Header\(_titles\[i\], i, _sortColumn, _sortDescending\);", source);
        Assert.DoesNotContain("▲", source, StringComparison.Ordinal);
        Assert.DoesNotContain("▼", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_AppliesSortAfterFilterAndRefreshesHeadersAfterRebuild()
    {
        var source = Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");
        var apply = Slice(source, "private void PopulateList(", "private void SortBy(");
        // 筛选先于排序：ListFilter.Apply 的同一表达式作为 ListSort.Order 的输入，排序键取共享口径。
        Assert.Matches(@"(?m)^\s*var visible = ListSort\.Order\($", apply);
        Assert.Matches(@"(?m)^\s*ListFilter\.Apply\(plan\.Files, _keyword\.Text, file => ApplyPlanEntries\.KeywordFields\($", apply);
        Assert.Contains("_sortColumn, _sortDescending,", apply, StringComparison.Ordinal);
        Assert.Contains("(file, column) => ApplyPlanSort.Key(", apply, StringComparison.Ordinal);
        // 表头标记在清单重建之后刷新（EndUpdate 之后），筛到零行那一支之前。
        Assert.Matches(@"(?m)^\s*_list\.EndUpdate\(\);\s*\n\s*RefreshSortHeaders\(\);", apply);
        var refresh = apply.IndexOf("RefreshSortHeaders();", StringComparison.Ordinal);
        var emptyBranch = apply.IndexOf("IsFilteredEmpty(", StringComparison.Ordinal);
        Assert.True(refresh >= 0 && emptyBranch > refresh, "表头标记要在筛到零行那一支之前刷新（那一支先返回）。");
    }

    [Fact]
    public void SortArmsCoverEveryColumnExactlyOnce()
    {
        // 排序臂与建列一一对应：漏一列则该列点击后顺序不变（表头有标记、内容没排），
        // 多一臂或索引重复都是与界面脱节的死码。
        var dialog = Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");
        var shared = Repo.Source("Desktop", "Native", "ApplyPlanSort.cs");
        Assert.Equal(4, Regex.Matches(dialog, @"(?m)^\s*_list\.Columns\.Add\(").Count);
        Assert.Equal(Enumerable.Range(0, 4), Arms(shared));
    }

    [Fact]
    public void SortKeysLiveOnlyInTheSharedEntry()
    {
        // 排序键只在共享口径一处：对话框不就地写 switch、不就地拼排序键（两套必然漂移）。
        var dialog = Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");
        Assert.DoesNotContain("IComparable?", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("column switch", dialog, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(dialog, "ApplyPlanSort\\.Key").Cast<Match>());
    }

    [Fact]
    public void SortStateStaysInTheDialogAndIsNotPersisted()
    {
        // 排序状态留在本次打开期间、关闭即忘：跨启动记忆只有两个常驻页面（ListSortMemoryKeys 只有两个页键）。
        var dialog = Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");
        Assert.DoesNotContain("ListSortMemory", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("FileStore", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteJson", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionIsRestoredByPathAfterSorting()
    {
        // 排序只改呈现顺序：选中仍按源码文件路径恢复（重建前记下、重建后找回），差异预览随选中事件跟上。
        var source = Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");
        var apply = Slice(source, "private void PopulateList(", "private void SortBy(");
        var remember = apply.IndexOf("var selectedPath = (Ui.SelectedItem(_list)?.Tag as PlannedFile)?.RelativePath;", StringComparison.Ordinal);
        var clear = apply.IndexOf("_list.Items.Clear();", StringComparison.Ordinal);
        Assert.True(remember >= 0 && clear > remember, "选中路径要在清空清单之前取局部。");
        // 恢复仍经唯一入口（Selected + Focused + 定位），不就地设 Selected、不自己滚动。
        Assert.Matches(@"(?m)^\s*Ui\.SelectItem\(target \?\? _list\.Items\[0\]\);$", apply);
        Assert.DoesNotContain(".Selected = true", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", apply, StringComparison.Ordinal);
        // 排序入口只翻状态再重建，不自己选中、不自己滚动、不写数据。
        var sortBy = Slice(source, "private void SortBy(", "private void RefreshSortHeaders");
        Assert.Matches(@"(?m)^\s*PopulateList\(\);$", sortBy);
        Assert.DoesNotContain("SelectItem", sortBy, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", sortBy, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyAsync", sortBy, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedFileIsCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ApplyPlanSort.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheApplyPlanSorting()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("注释回写文件清单的列排序", section);
        Assert.Contains("`ApplyPlanSort`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("注释回写文件清单的列排序（§6）", ledger);
        Assert.Contains("ApplyPlanSort", ledger);
        // 本轮没做的事不能被写成做了。
        var row = ledger.Split('\n').Single(l => l.StartsWith("| 注释回写文件清单的列排序（§6） |", StringComparison.Ordinal));
        Assert.Contains("第一百零五轮", row, StringComparison.Ordinal);
        Assert.Contains("G-D5", row, StringComparison.Ordinal);
        Assert.Contains("不改回写口径", row, StringComparison.Ordinal);
        Assert.DoesNotContain("已真机验证", row, StringComparison.Ordinal);
    }

    private static List<int> Arms(string source)
    {
        var start = source.IndexOf("IComparable? Key(", StringComparison.Ordinal);
        Assert.True(start >= 0, "缺少 Key。");
        var body = source[start..source.IndexOf("};", start, StringComparison.Ordinal)];
        return Regex.Matches(body, @"(?m)^\s*(\d+) => ").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).ToList();
    }

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
