using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 计数格的等宽口径（docs/DESIGN.md §3「等宽栈：计数、进度、ID、路径、差异文本必须等宽」、
/// 功能清单设计验收 D5「比例字体下跳宽即不合规」）。
/// <para>
/// <b>补的是哪个缺口</b>：这条规则在 §3 与 D5 写了很久，但界面上只有检查结果对话框执行了
/// （<c>CheckResultsDialog.AddCount</c> 里设 Mono）。同一个动作在其余清单上有三种落法——任务列表的
/// 「接口 / 注释候选」走控件默认字体、接口追踪明细的三个计数列被就地设成 <c>Body</c>、
/// 同步预览勾选页的「待确认」在对话框里自己设一次 Mono。后果不是观感而是可比性：比例字体下「9」与「10」
/// 宽度不同，两次扫描之间数字一变整列左右抖动，用户没法用余光比较同一列的两个数。
/// 现字体只经 <c>Ui.CountCell</c> 一处出厂，取数与占位口径留在各屏自己那份（不在工厂里统一）。
/// </para>
/// <para>
/// <b>本轮未扩面（由最后两条钉住理由，不是漏掉）</b>：补充说明那一格带中文单位「处」、
/// 项目列表与任务列表的「更新时间」是时间戳——中文进拉丁等宽栈落到的是字形回退而不是等宽，
/// 时间戳在 §3/D5 的列举里没有名分。两处的字体一概不经 <c>Ui.CountCell</c>。
/// </para>
/// </summary>
public class CountCellTests
{
    private static string UiSource => Repo.Source("Desktop", "Native", "Ui.cs");

    private static string NativeFile(string name) => Repo.Source("Desktop", "Native", name);

    /// <summary>工厂本体：只交出「加入这一格 + 等宽字体」两件事，不碰文字。</summary>
    [Fact]
    public void CountCell_IsTheOnlyFactoryAndFormatsNoText()
    {
        Assert.Single(Regex.Matches(UiSource, @"static ListViewItem\.ListViewSubItem CountCell\("));
        var body = FactoryBody();
        Assert.Contains("row.SubItems.Add(text)", body, StringComparison.Ordinal);
        Assert.Contains("sub.Font = UiTokens.Mono.Value;", body, StringComparison.Ordinal);
        // 文字是调用方算好交进来的：工厂不格式化数字、不发明占位符，否则各屏的占位口径会被悄悄换掉。
        Assert.False(Regex.IsMatch(body, @"ToString\(|""—""|PadLeft|N0"),
            "Ui.CountCell 里出现了取数或占位逻辑——计数格的文字口径在各屏自己那一份（EndpointTrace.CountText 等），工厂只管字体。");
    }

    /// <summary>
    /// 五处计数格全部经工厂出厂：任务列表两列、接口追踪明细三列、响应契约「分支」、
    /// 同步预览勾选页「待确认」、检查结果的 AddCount（委托工厂）。
    /// </summary>
    [Fact]
    public void EveryCountCell_GoesThroughTheFactory()
    {
        Assert.Equal(2, Count(NativeFile("TasksPage.cs"), @"Ui\.CountCell\(item, task\.Stages\.Source\."));
        Assert.Equal(3, Count(NativeFile("EndpointTraceDialog.cs"), @"Ui\.CountCell\(row, EndpointTrace\.CountText\("));
        Assert.Equal(1, Count(NativeFile("ContractsDialog.cs"), @"Ui\.CountCell\(item, contract\.Branches\.Count"));
        Assert.Equal(1, Count(NativeFile("SyncPreviewDialog.cs"), @"Ui\.CountCell\(row, SyncSelectSort\.PendingCell\("));
        Assert.Equal(1, Count(NativeFile("CheckResultsDialog.cs"), @"=>\s*Ui\.CountCell\(row, count > 0"));
    }

    /// <summary>
    /// 就地设计数格字体这条路不得复活：界面源码里给「某一格」设字体的地方，只剩明确不属于计数的那几格
    /// （路径与接口身份等标识列、状态列、说明列）。判据是形状——<c>SubItems.Add(...).Font =</c> 或
    /// 先 Add 再对拿到的格子设 <c>Mono</c> 的写法，一旦出现在本应由工厂出厂的计数上就红。
    /// </summary>
    [Fact]
    public void CountCells_NeverSetTheirFontInPlace()
    {
        foreach (var name in new[] { "TasksPage.cs", "EndpointTraceDialog.cs", "ContractsDialog.cs", "SyncPreviewDialog.cs" })
        {
            var text = NativeFile(name);
            foreach (Match match in Regex.Matches(text, @"SubItems\.Add\([^;]*?\)[ \t]*\.Font[ \t]*=[ \t]*UiTokens\.(Mono|Body)\.Value")
                         .Cast<Match>()
                         .Where(m => m.Value.Contains("CountText") || m.Value.Contains("PendingCell")
                                     || m.Value.Contains("Count.") || m.Value.Contains("Count;")))
            {
                Assert.Fail($"{name}:计数格又就地设了一次字体（{match.Value.Trim()}）——等宽字体只在 Ui.CountCell 一处出厂。");
            }
        }

        // 接口追踪明细这一处此前的写法是「Add 之后把那一格设成 Body」：取计数文字的那一行不得再出现 Body。
        // 预算列（「超限」是中文，进不了拉丁等宽栈）设 Body 不在本判据之内，逐行取法因此只看 CountText。
        foreach (var line in NativeFile("EndpointTraceDialog.cs").Split('\n'))
        {
            if (!line.Contains("CountText(", StringComparison.Ordinal)) continue;
            Assert.False(line.Contains("UiTokens.Body", StringComparison.Ordinal),
                $"接口追踪明细的计数格又就地设了一次比例字体（{line.Trim()}）——计数一律经 Ui.CountCell 出厂（DESIGN.md §6「计数格」）。");
        }
    }

    /// <summary>
    /// 取数与占位口径逐字留在各屏自己那一份：工厂接管字体不该顺手把「0 显示什么」也统一掉——
    /// 接口追踪明细的零显示「—」（那一格没有零个方法这回事），响应契约的零显示「0」（分支数 0 是事实）。
    /// </summary>
    [Fact]
    public void ThePlaceholderCaliber_StillDiffersByScreen()
    {
        Assert.Contains("public static string CountText(int count) => count > 0 ? count.ToString(CultureInfo.InvariantCulture) : \"—\";",
            NativeFile("EndpointTrace.cs"), StringComparison.Ordinal);
        Assert.Contains("public static string PendingCell(int pending) => pending > 0 ? pending.ToString() : \"—\";",
            NativeFile("SyncSelectSort.cs"), StringComparison.Ordinal);
        // 契约的分支数直接 ToString——0 就是 0，不换成占位符（那一格讲的是「这条契约有几个分支」）。
        Assert.Contains("Ui.CountCell(item, contract.Branches.Count.ToString())",
            NativeFile("ContractsDialog.cs"), StringComparison.Ordinal);
    }

    /// <summary>补充说明的「N 处」带中文单位，不经等宽工厂（中文进拉丁等宽栈落到的是字形回退，不是等宽）。</summary>
    [Fact]
    public void CountCellsWithChineseUnits_StayOutOfTheFactory()
    {
        var text = NativeFile("SupplementsDialog.cs");
        Assert.DoesNotContain("Ui.CountCell(", text, StringComparison.Ordinal);
        Assert.Contains("item.SubItems.Add(SupplementEntries.EntryCountCell(", text, StringComparison.Ordinal);
    }

    /// <summary>时间戳格也不经等宽工厂：§3 与 D5 列举的是计数、进度、ID、路径、差异文本，时间是另一类。</summary>
    [Fact]
    public void TimestampCells_StayOutOfTheFactory()
    {
        Assert.DoesNotContain("Ui.CountCell(item, task.UpdatedAt", NativeFile("TasksPage.cs"), StringComparison.Ordinal);
        Assert.Contains("item.SubItems.Add(task.UpdatedAt.LocalDateTime.ToString(\"MM-dd HH:mm\"));",
            NativeFile("TasksPage.cs"), StringComparison.Ordinal);
    }

    /// <summary>设计基线记下这条口径与它的唯一落点，界面代码引用它而不是引用一段散文。</summary>
    [Fact]
    public void DesignDoc_DeclaresTheMonoCountCellClause()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("Ui.CountCell", section, StringComparison.Ordinal);
        Assert.Contains("计数", section, StringComparison.Ordinal);
        Assert.Contains("D5", section, StringComparison.Ordinal);
    }

    /// <summary>逐子项样式必须在使用之前关掉，否则计数格的字体是加入后才被样式重置的那一种写法。</summary>
    [Fact]
    public void SubItemStyles_AreTurnedOffBeforeTheCountCellsAreAdded()
    {
        var page = NativeFile("TasksPage.cs");
        Assert.True(Regex.IsMatch(page, @"var item = new ListViewItem\(task\.Name\)[^\n]*\n\s*//[^\n]*\n\s*item\.UseItemStyleForSubItems = false;"),
            "任务列表的逐子项样式没有紧跟建行落地——计数格的等宽字体在 UseItemStyleForSubItems 还是 true 时交出。");
    }

    private static string FactoryBody()
    {
        var start = UiSource.IndexOf("static ListViewItem.ListViewSubItem CountCell(", StringComparison.Ordinal);
        Assert.True(start >= 0, "Ui.cs 里没有 CountCell。");
        var end = UiSource.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "Ui.CountCell 切不出方法体边界。");
        return UiSource[start..end];
    }

    private static int Count(string text, string pattern) => Regex.Matches(text, pattern).Count;
}
