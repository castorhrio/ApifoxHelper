using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 同步预览「预览页」的竖直预算（docs/DESIGN.md §6「长内容区」的那一条竖着写的规矩，
/// <c>docs/CRITIQUE.md</c> 第 8 站）。
/// <para>
/// <b>补的是哪个缺口</b>（第二百四十三轮，具名 pass：layout + harden）：上一轮在 <c>RunListColumnFit</c>
/// 自记「本段构造的是空清单，ClientSize 里还没有滚动条，只能按 <c>SystemInformation.VerticalScrollBarWidth</c>
/// 扣一笔当预算」。本轮把那句「量不到」当成开工依据去量，先量到两件比批评更硬的事：
/// ① 那一笔<b>扣对了</b>——真的显示出来并装进行之后，清单可视宽 958px → 941px，少的正是系统给的 17px
/// （五屏同数），于是「可视宽减一笔」从估算变成实测；
/// ② 量测口径本身一直在骗人——冒烟段从不显示窗体，而预览页 <c>Visible = false</c> 到计划出现为止，
/// 那张清单的高从来没人看过：真的显示出来之后实测 <b>ListView 0px ← Panel 2px ← Panel 27px ← Panel 651px ← 窗体 740px</b>，
/// <b>写入清单这一刻装得下 0 行</b>。这一屏存在的理由是逐动作读冻结计划，而那一行一行在主界面上看不见；
/// 上一轮念的「写入清单 984 对预算 1001」取自宽、不取自高，所以这个 0 不在任何读数里。
/// </para>
/// <para>
/// <b>根因是一个形状而不是一个数</b>：那一页把三块区域各写死一个像素高（190 / 130 / 150），窗体高也写死（740），
/// 四份互不相干的字面量加起来比页面还高，被挤掉的是唯一不能滚的那一块（主清单）。本轮改成：每块先说要给几行，
/// 像素由同一份行高与同一份开销算，窗体高跟着这几块走。算式在 <see cref="SyncPreviewPanes"/> 一处。
/// </para>
/// </summary>
public class SyncPreviewPanesTests
{
    private static string NativeFile(string name) => Repo.Source("Desktop", "Native", name);

    private static string Dialog => NativeFile("SyncPreviewDialog.cs");

    private static int Count(string text, string pattern) => Regex.Matches(text, pattern).Count;

    /// <summary>
    /// 四块的高度都由「行数 × 同一份行高 + 那一类块的一处开销」给出，没有一个像素数是单独发明的；
    /// 三块固定区的合计与这一屏的窗体高同源。
    /// </summary>
    [Fact]
    public void PaneHeights_ComeFromRowsAndOneChromePerKind()
    {
        Assert.Equal(4 * 21 + 58, SyncPreviewPanes.ListPaneHeight(4));
        Assert.Equal(3 * 21 + 51, SyncPreviewPanes.TextPaneHeight(3));
        // 主清单那一块的开销与「带关键词行的清单块」不是一个数：折成一个就凭空多要 31px。
        Assert.Equal(8 * 21 + 27, SyncPreviewPanes.ActionPaneHeight(8));
        Assert.NotEqual(SyncPreviewPanes.ListPaneChrome, SyncPreviewPanes.ActionPaneChrome);
        Assert.Equal(SyncPreviewPanes.RowHeight, 21);
        Assert.Equal(
            SyncPreviewPanes.TextPaneHeight(SyncPreviewPanes.PayloadRows)
            + SyncPreviewPanes.TextPaneHeight(SyncPreviewPanes.NotesRows)
            + SyncPreviewPanes.ListPaneHeight(SyncPreviewPanes.ExcludedRows),
            SyncPreviewPanes.FixedPaneTotal);
    }

    /// <summary>
    /// 本轮实测的那两个页面高度喂进同一条算式，一假一真：651px（窗体 740）假——那正是 0 行那一刻；
    /// 701px（窗体 790）真。判据是「主清单这一刻要拿到它那句行数的地方」，写死的三块不够它挤。
    /// </summary>
    [Fact]
    public void ActionListFits_IsTheArithmeticTheOldLayoutFailed()
    {
        Assert.False(SyncPreviewPanes.ActionListFits(651, 154));
        Assert.True(SyncPreviewPanes.ActionListFits(701, 154));
        // 概要区长高一行也不许把主清单吃掉：这一条抓的是「只调窗体高、不算上面那一块」那种改法。
        Assert.False(SyncPreviewPanes.ActionListFits(701 - SyncPreviewPanes.RowHeight * 3, 154));
    }

    /// <summary>主清单的行数是这一页最高的那一条判据：让谁都不让它。</summary>
    [Fact]
    public void ActionList_HoldsTheLargestRowFloor()
    {
        Assert.True(SyncPreviewPanes.ActionRows >= 8);
        Assert.True(SyncPreviewPanes.ActionRows > SyncPreviewPanes.ExcludedRows);
        Assert.True(SyncPreviewPanes.ActionRows > SyncPreviewPanes.PayloadRows);
        Assert.True(SyncPreviewPanes.PayloadRows >= SyncPreviewPanes.NotesRows);
    }

    /// <summary>
    /// 接线：窗体高与三块固定区都经口径给，四处的字面量不得复活。
    /// 「三个互不相干的像素数」正是本轮的根因形状，留一个在文件里就等于留着下一次的挤没。
    /// </summary>
    [Fact]
    public void Dialog_TakesEveryVerticalNumberFromTheCaliber()
    {
        Assert.Equal(1, Count(Dialog, @"new Size\(1020, SyncPreviewPanes\.DialogHeight\)"));
        Assert.Equal(1, Count(Dialog, @"SyncPreviewPanes\.ListPaneHeight\(SyncPreviewPanes\.ExcludedRows\)"));
        Assert.Equal(1, Count(Dialog, @"SyncPreviewPanes\.TextPaneHeight\(SyncPreviewPanes\.PayloadRows\)"));
        Assert.Equal(1, Count(Dialog, @"SyncPreviewPanes\.TextPaneHeight\(SyncPreviewPanes\.NotesRows\)"));
        Assert.DoesNotContain("Height = 150", Dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("Height = 190", Dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("Height = 130", Dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("new Size(1020, 740)", Dialog, StringComparison.Ordinal);
    }

    /// <summary>
    /// 量测那一段不能退回「建句柄不显示」：显示、点产品自己那颗「查看已有计划」、按真实控件的行高算行数，
    /// 三件事各钉一次。0 行那一支必须是失败而不是被钳制——第二百四十二轮那发红话就是 clamp 自造的。
    /// <para>
    /// <b>判的是那一段，不是整份文件</b>：本轮 M4 那一发第一次是<b>假绿</b>——把这一段的 <c>dialog.Show()</c>
    /// 退回 <c>_ = dialog.Handle;</c>，门照样绿，因为冒烟器另几段本来就写着 <c>dialog.Show();</c>，
    /// <c>Contains</c> 在同一份文件的别处被满足了（本仓第二百一十、二百一十一轮记过同一形态：Presence 型断言
    /// 判的是「这一条里」而不是「这一句里」）。现先切出 <c>RunListColumnFit</c> 那一段，段内判这六件事，
    /// 切不到段本身即红。
    /// </para>
    /// </summary>
    [Fact]
    public void TheMeasurement_ShowsTheDialogAndFailsClosedOnZeroRows()
    {
        var segment = MethodSlice(Repo.SmokeProgram,
            "private static void RunListColumnFit(IServiceProvider services, string dataRoot)");
        // 两处 Show 各属一段：七屏那一段与预览页四块那一段。写成 Contains 会被另一段满足——
        // 本轮 M4 第一次就是那样假绿的（把七屏那一段的显示退回建句柄，另一段的 Show 照样替它过）。
        Assert.Equal(2, Count(segment, @"dialog\.Show\(\);"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(segment, @"_ = dialog\.Handle;"));
        Assert.Contains("\"查看已有计划\"", segment, StringComparison.Ordinal);
        Assert.Contains("viewPlan.PerformClick();", segment, StringComparison.Ordinal);
        Assert.Contains("if (capacity < 3 && designTier)", segment, StringComparison.Ordinal);
        Assert.Contains("那一刻的行数不判", segment, StringComparison.Ordinal);
        Assert.Contains("SyncPreviewPanes.RowHeight", segment, StringComparison.Ordinal);
        // 不许把「装得下几行」的下界钳成 1：钳了就永远不为 0 行报红。
        Assert.DoesNotContain("Math.Max(1, list.ClientSize.Height / rowHeight)", segment, StringComparison.Ordinal);
    }

    /// <summary>取一个方法体：从签名那一行起，到第一个「四空格缩进的收尾大括号」为止（冒烟器里方法都是这个缩进）。</summary>
    private static string MethodSlice(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} 在冒烟器里找不到了——量测段改名或搬家，这一族门就再也管不住它。");
        var body = new List<string>();
        foreach (var line in source[start..].Split('\n'))
        {
            body.Add(line);
            if (line.TrimEnd() == "    }") break;
        }
        return string.Join('\n', body);
    }

    /// <summary>
    /// 计划夹具经 TaskStore 写，文件名取 <c>TaskFiles.Plan</c> 一份——冒烟器里不许出现第二个「plan.json」。
    /// </summary>
    [Fact]
    public void ThePlanFixture_GoesThroughTheStoreAndNamesTheFileOnce()
    {
        Assert.Equal(1, Count(Repo.SmokeProgram, @"tasks\.Write\(task\.Id, TaskFiles\.Plan"));
        Assert.DoesNotContain("\"plan.json\"", Repo.SmokeProgram, StringComparison.Ordinal);
    }

    /// <summary>共享编译：这条算式与测试工程是同一份，不是测试里抄的第二份。</summary>
    [Fact]
    public void SyncPreviewPanes_IsSharedCompiledIntoTheTests()
    {
        Assert.Contains(
            @"..\Desktop\Native\SyncPreviewPanes.cs",
            Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj"),
            StringComparison.Ordinal);
    }

    /// <summary>设计基线那一格与实现差距台账在位（老规矩：代码改了、文档没跟上即红）。</summary>
    [Fact]
    public void DesignDoc_KeepsThePreviewPageBudgetClause()
    {
        Assert.Contains("SyncPreviewPanes", Repo.DesignSection(6), StringComparison.Ordinal);
        Assert.Contains("写入清单", Repo.DesignSection(6), StringComparison.Ordinal);
        Assert.Contains("SyncPreviewPanes", Repo.DesignSection(8), StringComparison.Ordinal);
    }
}
