using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 审阅队列打开即选中第一条（DESIGN.md §6「列表行」、§8 第九十轮）：三个审阅视图（注释候选、响应契约、差异确认）
/// 里只有差异确认打开时自动选中一行，另两处打开时清单里一行都没选中——详情区是「没有选中…」那句话、
/// 三个审阅动作全灰，而这一屏存在的理由就是逐条清队列；同一个动作（打开一个审阅队列）在别处有、在这两处没有，
/// 属同壳不同行为，按第五十五、五十七、五十九轮那条判据判定为缺口，不是偏好。
/// 现两处打开时经 <c>Ui.SelectFirstOnOpen</c> 唯一入口选中第一条，落点由
/// <see cref="ListSelection.FirstRowOnOpen"/> 判定（有行才落，一行都没有就不动作、不索引不存在的第一行），
/// 选中仍经 <c>Ui.SelectItem</c>（Selected + Focused + 滚入可见区，与鼠标点同一行同一条路）。
/// 落点只在打开这一处：重建路径（切换筛选、关键词框每敲一个字）不落点——注释候选的回答框按选中行重填，
/// 落点会把用户刚打的字重置掉（与第五十八轮「不抢用户正在改的那一格」同一取舍）。
/// 真机上「打开注释候选/响应契约即看到第一条的详情、三个审阅动作可用」随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class OpenSelectionTests
{
    // ---------- 真执行：打开这一屏时落在哪一行 ----------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(42)]
    [InlineData(int.MaxValue)]
    public void ARowIsThereToLandOnWhenTheListHasRows(int displayedRowCount)
    {
        // 有行就落在第一条（位次 0）——与差异确认、五张只读长清单打开时的落点同一处。
        Assert.Equal(0, ListSelection.FirstRowOnOpen(displayedRowCount));
    }

    [Fact]
    public void AnEmptyListHasNoLandingSpot()
    {
        // 一行都没有就没有落点：不索引不存在的第一行（与第五十六轮「一行不剩则没有落点」同一取舍）。
        Assert.Null(ListSelection.FirstRowOnOpen(0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void AnImpossibleCountIsTreatedAsNoLandingSpot(int displayedRowCount)
    {
        // 负数不可发生，但发生了也不猜它指向哪一行（与第五十三轮「声明的列落在格数之外时宁可复制不到」同一取舍）。
        Assert.Null(ListSelection.FirstRowOnOpen(displayedRowCount));
    }

    [Fact]
    public void Rule_LandsOnTheFirstRowExactlyWhenTheListHasRows()
    {
        // 逐点核对判定窗口：放行当且仅当「行数大于零」，且放行时给的位次恒为 0。
        // 多一分是空清单上索引不存在的第一行（那一次打开变成未处理异常），
        // 少一分是有行时打开仍停在「没有选中…」那句话上（本轮要断掉的那件事）。
        for (var count = -3; count <= 200; count++)
        {
            var landing = ListSelection.FirstRowOnOpen(count);
            Assert.Equal(count > 0 ? 0 : (int?)null, landing);
        }
    }

    [Fact]
    public void Rule_LivesWithoutWinForms()
    {
        // 判定必须能在 Linux 真执行：口径文件不碰 WinForms、不碰控件集合类型。
        var rule = Repo.Source("Desktop", "Native", "ListSelection.cs");
        Assert.DoesNotContain("System.Windows.Forms", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("ListView", rule, StringComparison.Ordinal);
    }

    // ---------- 唯一入口：落点只经 Ui.SelectFirstOnOpen ----------

    private static string SelectFirstOnOpenBody()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var match = Regex.Match(ui, @"public static void SelectFirstOnOpen\(ListView list\)\s*\{(?<body>[^}]*)\}");
        Assert.True(match.Success, "Ui 缺少 SelectFirstOnOpen（打开这一屏时的落点唯一入口）。");
        return match.Groups["body"].Value;
    }

    [Fact]
    public void Entry_AsksTheSharedRuleAndSelectsThroughTheOneEntry()
    {
        var body = SelectFirstOnOpenBody();
        // 落点问共享判定，不就地写 > 0 与 0（另写一份必然与 ListSelection 漂移）。
        Assert.Contains("ListSelection.FirstRowOnOpen(list.Items.Count)", body);
        // 选中仍经 SelectItem 唯一入口：Selected + Focused + 滚入可见区，与鼠标点同一行同一条路。
        Assert.Contains("SelectItem(list.Items[index])", body);
        Assert.DoesNotContain("Items[0]", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Entry_TouchesNothingButTheSelection()
    {
        // 只落选中：不取键盘（那是 OnShown 的 KeepKeyboardOnQueue）、不自己滚动、不重建清单、不写数据。
        var body = SelectFirstOnOpenBody();
        Assert.DoesNotContain(".Focus(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Items.Clear", body, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginUpdate", body, StringComparison.Ordinal);
        Assert.DoesNotContain("KeepKeyboardOnQueue", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Entry_IsTheOnlyPlaceThatLandsOnTheFirstRowOnOpen()
    {
        // 落点只有一处：判定 ListSelection.FirstRowOnOpen 的调用者只有 Ui.cs，界面不各写一套。
        var callers = Repo.NativeSources()
            .Where(s => s.Contains("ListSelection.FirstRowOnOpen"))
            .Select(s => s.Name)
            .ToList();
        Assert.Equal(new[] { "Ui.cs" }, callers);
    }

    // ---------- 接线：两个审阅视图在打开时落一次，重建路径不落 ----------

    [Theory]
    [InlineData("CandidatesDialog.cs")]
    [InlineData("ContractsDialog.cs")]
    public void ReviewQueues_LandOnTheFirstRowWhenTheyOpen(string file)
    {
        var onLoad = MethodBody(file, "OnLoad");
        // 打开时落一次，且经唯一入口。
        Assert.Single(Regex.Matches(onLoad, @"(?m)^\s*Ui\.SelectFirstOnOpen\(_list\);$"));
        // 落点在清单重建之后：先 ApplyFilter 把行填进去，再落点（顺序反了就是在一张空清单上落点）。
        var filter = onLoad.IndexOf("ApplyFilter();", StringComparison.Ordinal);
        var land = onLoad.IndexOf("Ui.SelectFirstOnOpen(_list);", StringComparison.Ordinal);
        Assert.True(filter >= 0 && land > filter, $"{file}:应在 ApplyFilter 之后落点。");
        // 打开时这一处只落选中：不取键盘、不自己索引、不自己滚动（各有自己的唯一入口）。
        Assert.DoesNotContain("KeepKeyboardOnQueue", onLoad, StringComparison.Ordinal);
        Assert.DoesNotContain("Items[0]", onLoad, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", onLoad, StringComparison.Ordinal);
        Assert.DoesNotContain(".Focus(", onLoad, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CandidatesDialog.cs")]
    [InlineData("ContractsDialog.cs")]
    public void ReviewQueues_DoNotLandAgainWhenTheListIsRebuilt(string file)
    {
        // 重建路径（切换筛选、关键词框每敲一个字）不落点：注释候选的回答框按选中行重填，
        // 落点会把用户刚打的字重置掉（与第五十八轮「不抢用户正在改的那一格」同一取舍），响应契约同一理由。
        var applyFilter = MethodBody(file, "ApplyFilter");
        Assert.DoesNotContain("Ui.SelectFirstOnOpen(", applyFilter, StringComparison.Ordinal);
        Assert.DoesNotContain("Ui.SelectItem(", applyFilter, StringComparison.Ordinal);
        Assert.DoesNotContain("Items[0]", applyFilter, StringComparison.Ordinal);
        // 重建后仍问一次详情：没选中时那一屏由详情区那句话交代（第六十二轮口径）。
        Assert.Contains("ShowDetail();", applyFilter);
    }

    [Fact]
    public void OnlyTheTwoReviewQueues_LandOnTheFirstRowOnOpen()
    {
        // 落点的调用点恰两处：注释候选与响应契约。差异确认不另加落点（它的重建路径本就落在第一条，
        // 第五十六、五十九轮），五张只读长清单各有自己的打开落点（第八十一轮等），不经本入口。
        var callers = Repo.NativeSources()
            .Where(s => s.Contains("Ui.SelectFirstOnOpen("))
            .Select(s => s.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(new[] { "CandidatesDialog.cs", "ContractsDialog.cs" }, callers);
    }

    // ---------- 设计基线 ----------

    [Fact]
    public void DesignDoc_RecordsTheOpenLanding()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("**审阅队列打开即选中第一条**（`ListSelection` 同一口径，2026-11-08 第九十轮）", section);
        Assert.Contains("经 `Ui.SelectFirstOnOpen` 唯一入口选中第一条", section);
        Assert.Contains("`ListSelection.FirstRowOnOpen` 判定：有行才落", section);
        // 落点只在打开这一处，重建路径不落点——这条取舍必须写在设计基线里，不能只在代码注释里。
        Assert.Contains("**落点只在打开这一处**", section);
        Assert.Contains("重建路径（切换筛选、关键词框每敲一个字）不落点", section);

        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 审阅队列打开即选中第一条（§6） \|", ledger);
        Assert.Contains("第九十轮", ledger);
        Assert.Contains("`Ui.SelectFirstOnOpen`", ledger);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimMoreThanWasDone()
    {
        // 本轮只改落点与键盘归属：不改选中落点数学、不改筛选与排序口径、不写任务与配置数据、服务层一行未动。
        var ledger = Repo.DesignSection(8);
        var row = Slice(ledger, "| 审阅队列打开即选中第一条（§6） |", "\n以上每项在达成时回写本文并注明证据。");
        Assert.Contains("不改选中落点数学", row);
        Assert.Contains("不改筛选与排序口径", row);
        Assert.Contains("不写任务与配置数据", row);
        Assert.Contains("服务层一行未动", row);
        Assert.DoesNotContain("全部对话框打开时初始焦点已接线", row, StringComparison.Ordinal);
    }

    /// <summary>某个方法的方法体（按花括号配对取，方法体里可以有 try/catch 的嵌套花括号）。</summary>
    private static string MethodBody(string file, string method)
    {
        var source = Repo.Source("Desktop", "Native", file);
        var match = Regex.Match(source, @"(?:protected override void|private void) " + Regex.Escape(method) + @"\([^)]*\)\s*\{");
        Assert.True(match.Success, $"{file}:缺少 {method}。");
        var start = match.Index + match.Length;
        var depth = 1;
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[start..i];
        }
        Assert.Fail($"{file}:{method} 的花括号不配对。");
        return "";
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
