using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 注释回写跳过清单的列排序断言（DESIGN.md §6「列表列排序」，第一百零八轮）：
/// 两个主页面（第四十七轮）、检查结果的两张清单（第九十七轮）、接口追踪明细（第九十八轮）、
/// 差异确认的条目清单（第九十九轮）、注释候选的候选清单（第一百轮）、响应契约的契约清单（第一百零一轮）、
/// 注释缺口清单（第一百零二轮）、生成文档核对的条目清单（第一百零三轮）、任务日志的记录清单（第一百零四轮）、
/// 注释回写的文件清单（第一百零五轮）、同步预览的两张清单（第一百零六轮）与补充说明的接口清单（第一百零七轮）
/// 点击列头即按该列排序，而同一个对话框里的跳过清单（跳过的位置、原因两列）点击列头没有任何反应——
/// 这一屏的说明行写着「跳过的位置（原因如下；处理后重新生成计划）」，要按原因处理就得先把同一原因的集中看，
/// 属同壳不同行为。排序状态与表头标记仍取 <see cref="ListSort"/> 那一份，
/// 「这一列按什么排」收进 <see cref="ApplyPlanSort"/> 单一口径（与单元格取值同源），
/// 界面接线由行首正则与源码切片机械检查钉住（被注释掉也算删除）。
/// </summary>
public class ApplyPlanSkippedSortTests
{
    /// <summary>一行在屏幕上被读作的那两格（与对话框建行处同一份取值）。</summary>
    private sealed record Row(string Display, string Reason);

    private static IComparable? Key(Row row, int column) => ApplyPlanSort.SkippedKey(row.Display, row.Reason, column);

    // ---------- 排序键与单元格同源（真执行） ----------

    [Fact]
    public void SkippedKey_MirrorsTheTwoCells()
    {
        var row = new Row("Orders/OrderController.Create", "文件不是 UTF-8 编码，不自动修改");
        // 跳过的位置列：与首格同一份取值。
        Assert.Equal("Orders/OrderController.Create", ApplyPlanSort.SkippedKey(row.Display, row.Reason, 0));
        // 原因列：与第二格同一份取值（原因进排序键正是这一屏的要害）。
        Assert.Equal("文件不是 UTF-8 编码，不自动修改", ApplyPlanSort.SkippedKey(row.Display, row.Reason, 1));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(7)]
    public void SkippedKey_OutsideTheColumnsIsNull(int column)
    {
        // 列号落在建列之外交 null：不索引不存在的列，也不把邻列的值当成这一列。
        Assert.Null(ApplyPlanSort.SkippedKey("Orders/OrderController.Create", "无法定位声明", column));
    }

    // ---------- 排序结果（真执行：ListSort.Order 跑主工程同一份代码） ----------

    [Fact]
    public void Ordering_ByReasonGroupsTheSameReasonsTogether()
    {
        // 原因列按那一格的文字排：同一原因的行聚在一起（「按原因处理」正是这一屏的下一步）。
        var rows = new[]
        {
            new Row("A.Create", "无法定位声明"),
            new Row("B.Create", "文件不是 UTF-8 编码，不自动修改"),
            new Row("C.Create", "无法定位声明"),
            new Row("D.Create", "文件不是 UTF-8 编码，不自动修改"),
        };
        var expected = rows.OrderBy(r => r.Reason, StringComparer.CurrentCulture).Select(r => r.Display).ToList();
        Assert.Equal(expected, ListSort.Order(rows, 1, false, Key).Select(r => r.Display));
        // 反向：同值行仍按数据源顺序（两个方向都稳定），故与 LINQ 的稳定降序同序。
        Assert.Equal(
            rows.OrderByDescending(r => r.Reason, StringComparer.CurrentCulture).Select(r => r.Display),
            ListSort.Order(rows, 1, true, Key).Select(r => r.Display));
    }

    [Fact]
    public void Ordering_ByDisplayUsesTheNaturalOrder()
    {
        // 位置列常带序号（同一接口的第 2 个与第 10 个标注）：数字段按数值排，与资源管理器名称列同序。
        var rows = new[]
        {
            new Row("Orders/Order10.Create", "无法定位声明"),
            new Row("Orders/Order2.Create", "无法定位声明"),
        };
        Assert.Equal(["Orders/Order2.Create", "Orders/Order10.Create"], ListSort.Order(rows, 0, false, Key).Select(r => r.Display));
    }

    [Fact]
    public void Ordering_IsStableForEqualKeysInBothDirections()
    {
        // 同值行在两个方向都保持数据源顺序（反向不把同值的行打乱重排）。
        var rows = new[]
        {
            new Row("A.Create", "无法定位声明"),
            new Row("B.Create", "无法定位声明"),
            new Row("C.Create", "无法定位声明"),
        };
        Assert.Equal(["A.Create", "B.Create", "C.Create"], ListSort.Order(rows, 1, false, Key).Select(r => r.Display));
        Assert.Equal(["A.Create", "B.Create", "C.Create"], ListSort.Order(rows, 1, true, Key).Select(r => r.Display));
    }

    [Fact]
    public void Ordering_KeepsEveryRow()
    {
        // 排序只改呈现顺序：一行不少、一行不多（跳过清单是「哪些位置没被处理」的凭据，丢一行就是丢一条原因）。
        var rows = new[]
        {
            new Row("A.Create", "无法定位声明"),
            new Row("B.Create", "文件不是 UTF-8 编码，不自动修改"),
            new Row("C.Create", "扫描后源码或证据文件已变化，请重新扫描并审阅：a.cs"),
        };
        foreach (var column in new[] { 0, 1 })
        {
            foreach (var descending in new[] { false, true })
            {
                var ordered = ListSort.Order(rows, column, descending, Key);
                Assert.Equal(rows.Length, ordered.Count);
                Assert.Equal(rows.OrderBy(r => r.Display, StringComparer.Ordinal).Select(r => r.Display),
                    ordered.OrderBy(r => r.Display, StringComparer.Ordinal).Select(r => r.Display));
            }
        }
    }

    [Fact]
    public void UnsortedKeepsThePlanOrder()
    {
        // 未排序（ListSort.None）时保持计划自身的跳过顺序：那一屏的默认读法是「计划按什么顺序跳过的」。
        var rows = new[]
        {
            new Row("C.Create", "无法定位声明"),
            new Row("A.Create", "文件不是 UTF-8 编码，不自动修改"),
            new Row("B.Create", "无法定位声明"),
        };
        Assert.Equal(["C.Create", "A.Create", "B.Create"], ListSort.Order(rows, ListSort.None, false, Key).Select(r => r.Display));
    }

    // ---------- 对话框接线（机械检查） ----------

    [Fact]
    public void Dialog_WiresTheSkippedListThroughTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");
        Assert.Matches(@"(?m)^\s*private int _skippedSortColumn = ListSort\.None;$", source);
        Assert.Matches(@"(?m)^\s*private bool _skippedSortDescending;$", source);
        Assert.Matches(@"(?m)^\s*_skippedTitles = _skippedList\.Columns\.Cast<ColumnHeader>\(\)\.Select\(column => column\.Text\)\.ToArray\(\);", source);
        Assert.Matches(@"(?m)^\s*_skippedList\.ColumnClick \+= \(_, e\) => SortSkipped\(e\.Column\);", source);
        // 方向翻转只在共享口径一处（对话框不自己写 !descending）。
        Assert.Matches(@"(?m)^\s*\(_skippedSortColumn, _skippedSortDescending\) = ListSort\.Toggle\(_skippedSortColumn, _skippedSortDescending, column\);", source);
        Assert.DoesNotContain("_skippedSortDescending = !", source, StringComparison.Ordinal);
        // 表头标记按原标题重算（不叠加、不就地拼方向字面）。
        Assert.Matches(@"(?m)^\s*_skippedList\.Columns\[i\]\.Text = ListSort\.Header\(_skippedTitles\[i\], i, _skippedSortColumn, _skippedSortDescending\);", source);
        Assert.DoesNotContain("▲", source, StringComparison.Ordinal);
        Assert.DoesNotContain("▼", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_SortsInPopulateSkippedAndRefreshesHeadersAfterRebuild()
    {
        var source = Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");
        var populate = Slice(source, "private void PopulateSkipped(", "private void SortSkipped(");
        // 排序键取共享口径，排序状态取本清单自己那一份。
        Assert.Matches(@"(?m)^\s*var visible = ListSort\.Order\($", populate);
        Assert.Contains("_skippedSortColumn,", populate, StringComparison.Ordinal);
        Assert.Contains("_skippedSortDescending,", populate, StringComparison.Ordinal);
        Assert.Contains("(skip, column) => ApplyPlanSort.SkippedKey(skip.Display, skip.Reason, column));", populate, StringComparison.Ordinal);
        // 表头标记在清单重建之后刷新（EndUpdate 之后）。
        Assert.Matches(@"(?m)^\s*_skippedList\.EndUpdate\(\);\s*\n\s*RefreshSkippedHeaders\(\);", populate);
        // 跳过清单的可见性仍按整份计划的跳过数判定（排序不改变「有没有跳过」）。
        Assert.Contains("_skippedHost.Visible = plan.Skipped.Count > 0;", populate, StringComparison.Ordinal);
    }

    [Fact]
    public void SkippedSortArmsCoverEveryColumnExactlyOnce()
    {
        // 排序臂与建列一一对应：漏一列则该列点击后顺序不变（表头有标记、内容没排），
        // 多一臂或索引重复都是与界面脱节的死码。
        var dialog = Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");
        var shared = Repo.Source("Desktop", "Native", "ApplyPlanSort.cs");
        Assert.Equal(2, Regex.Matches(dialog, @"(?m)^\s*_skippedList\.Columns\.Add\(").Count);
        Assert.Equal(Enumerable.Range(0, 2), SkippedArms(shared));
    }

    [Fact]
    public void SkippedSortKeysLiveOnlyInTheSharedEntry()
    {
        // 排序键只在共享口径一处：对话框不就地写 switch、不就地拼排序键（两套必然漂移）。
        var dialog = Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");
        Assert.DoesNotContain("IComparable?", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("column switch", dialog, StringComparison.Ordinal);
        // 只数调用形状（带括号）：注释里的 <see cref="ApplyPlanSort.SkippedKey"/> 是引用不是第二份口径。
        Assert.Single(Regex.Matches(dialog, @"ApplyPlanSort\.SkippedKey\(").Cast<Match>());
    }

    [Fact]
    public void SkippedListHasNoSelectionSemantics()
    {
        // 跳过清单没有选中语义（没有详情区、没有动作挂在它上面）：排序只改呈现顺序——
        // 不恢复选中、不滚动、不碰键盘焦点、不写数据。
        var source = Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");
        var populate = Slice(source, "private void PopulateSkipped(", "private void SortSkipped(");
        Assert.DoesNotContain("SelectItem", populate, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", populate, StringComparison.Ordinal);
        Assert.DoesNotContain("KeepKeyboardOnQueue", populate, StringComparison.Ordinal);
        Assert.DoesNotContain("Tag =", populate, StringComparison.Ordinal);
        Assert.DoesNotContain("_counts.Text", populate, StringComparison.Ordinal);
        var sortSkipped = Slice(source, "private void SortSkipped(", "private void RefreshSkippedHeaders(");
        Assert.Matches(@"(?m)^\s*if \(_plan is \{ \} plan\) PopulateSkipped\(plan\);$", sortSkipped);
        Assert.DoesNotContain("SelectItem", sortSkipped, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", sortSkipped, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyAsync", sortSkipped, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoListsKeepTheirOwnSortState()
    {
        // 两张清单各记各的列与方向：排文件清单不会把跳过清单也排了，反之亦然。
        var source = Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");
        var sortBy = Slice(source, "private void SortBy(", "private void RefreshSortHeaders(");
        Assert.DoesNotContain("_skippedSortColumn", sortBy, StringComparison.Ordinal);
        Assert.DoesNotContain("PopulateSkipped", sortBy, StringComparison.Ordinal);
        var sortSkipped = Slice(source, "private void SortSkipped(", "private void RefreshSkippedHeaders(");
        Assert.DoesNotContain("_sortColumn", sortSkipped, StringComparison.Ordinal);
        Assert.DoesNotContain("PopulateList", sortSkipped, StringComparison.Ordinal);
    }

    [Fact]
    public void SkippedListIsRebuiltThroughOneEntry()
    {
        // 跳过清单只有一处填充（PopulateSkipped）：ShowPlan 里不再内联建行，
        // 否则排序只对其中一条路径生效（重建路径会退回计划顺序）。
        var source = Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");
        var showPlan = Slice(source, "private void ShowPlan(", "private void PopulateList(");
        Assert.Matches(@"(?m)^\s*PopulateSkipped\(plan\);$", showPlan);
        Assert.DoesNotContain("_skippedList.Items.Add", showPlan, StringComparison.Ordinal);
        Assert.DoesNotContain("_skippedList.Items.Clear", showPlan, StringComparison.Ordinal);
        // 整个对话框里建行只在 PopulateSkipped 一处。
        Assert.Single(Regex.Matches(source, @"(?m)^\s*_skippedList\.Items\.Add\(").Cast<Match>());
        Assert.Single(Regex.Matches(source, @"(?m)^\s*_skippedList\.Items\.Clear\(\)").Cast<Match>());
    }

    [Fact]
    public void CountsLineDoesNotFollowSorting()
    {
        // 计数行仍按整份计划统计、不随排序变化（「跳过 N」正是这一屏的凭据，被排序抹掉等于把结果藏起来）。
        var source = Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");
        var showPlan = Slice(source, "private void ShowPlan(", "private void PopulateList(");
        var counts = showPlan.IndexOf("_counts.Text = ApplyPlanEntries.CountsLine(", StringComparison.Ordinal);
        var populate = showPlan.IndexOf("PopulateSkipped(plan);", StringComparison.Ordinal);
        Assert.True(counts >= 0 && populate > counts, "计数行要在跳过清单重建之前按整份计划算一次。");
        var populateBody = Slice(source, "private void PopulateSkipped(", "private void SortSkipped(");
        Assert.DoesNotContain("_counts", populateBody, StringComparison.Ordinal);
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
    public void SharedFileIsCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ApplyPlanSort.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheApplyPlanSkippedSorting()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("注释回写跳过清单的列排序", section);
        Assert.Contains("`ApplyPlanSort`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("注释回写跳过清单的列排序（§6）", ledger);
        // 本轮没做的事不能被写成做了。
        var row = ledger.Split('\n').Single(l => l.StartsWith("| 注释回写跳过清单的列排序（§6） |", StringComparison.Ordinal));
        Assert.Contains("第一百零八轮", row, StringComparison.Ordinal);
        Assert.Contains("G-D5", row, StringComparison.Ordinal);
        Assert.Contains("不改回写口径", row, StringComparison.Ordinal);
        Assert.DoesNotContain("已真机验证", row, StringComparison.Ordinal);
    }

    private static List<int> SkippedArms(string source)
    {
        var start = source.IndexOf("IComparable? SkippedKey(", StringComparison.Ordinal);
        Assert.True(start >= 0, "缺少 SkippedKey。");
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
