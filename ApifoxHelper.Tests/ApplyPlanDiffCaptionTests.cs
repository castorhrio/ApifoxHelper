using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 注释回写的差异预览说明行（DESIGN.md §6「列表行」、§8 第九十四轮）：这一屏的核心是逐文件预览统一 diff，
/// 而那块只读框此前没有标题——另六处底部/详情预览区都有（检查结果「选中接口的结构差异…」、
/// 核对传播「选中条目的说明文字…」、代码证据 <c>EvidenceEntries.SnippetCaption</c>、
/// 同步预览「选中动作的写入内容…」与「共享模型对范围外接口的影响…」、本对话框的跳过清单
/// 「跳过的位置：……每条为什么写在「原因」列……」）：同一只壳里同一个动作在别处有、在这一处没有，属同壳不同行为，
/// 按第五十五、五十七、五十九轮那条判据判定为缺口，不是偏好。
/// 说明行讲清三件事：这块框是什么（选中文件的注释差异）、改的是什么（只改 /// 注释行）、
/// 与回写的关系（与回写时写入的内容一致）——用户不必先点一行再猜。
/// 纯呈现层：只加一行说明文字，不改选中落点、不改筛选与排序、不改计划口径、不写任务与配置数据。
/// </summary>
public class ApplyPlanDiffCaptionTests
{
    private const string File = "ApplyPlanDialog.cs";

    // ---------- 句子：一份落点，讲清三件事 ----------

    [Fact]
    public void Caption_SaysWhatTheBoxIsAndWhatItChanges()
    {
        Assert.Equal("选中文件的注释差异（只改 /// 注释行；与回写时写入的内容一致）", ApplyPlanEntries.DiffCaption);
        // 三件事各在句子里：这块框是什么、改的是什么、与回写的关系。
        Assert.Contains("注释差异", ApplyPlanEntries.DiffCaption, StringComparison.Ordinal);
        Assert.Contains("///", ApplyPlanEntries.DiffCaption, StringComparison.Ordinal);
        Assert.Contains("回写", ApplyPlanEntries.DiffCaption, StringComparison.Ordinal);
    }

    [Fact]
    public void Caption_IsTheSingleSource()
    {
        // 字面只在口径文件里出现一次：对话框取常量，不就地拼一份（另写一份必然漂移）。
        var literal = ApplyPlanEntries.DiffCaption;
        var hits = Repo.NativeSources().Count(s => s.Contains(literal));
        Assert.Equal(1, hits);
        Assert.Contains(literal, Repo.Source("Desktop", "Native", "ApplyPlanEntries.cs"), StringComparison.Ordinal);
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Matches(@"(?m)^\s*_diffCaption = Ui\.Caption\(ApplyPlanEntries\.DiffCaption\);$", source);
        Assert.DoesNotContain(literal, source, StringComparison.Ordinal);
    }

    // ---------- 接线：说明行在框上方，框填满剩余 ----------

    [Fact]
    public void Caption_SitsAboveTheDiffBox()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 发丝边只读框填满宿主，说明行停靠顶部（与另六处底部预览区同一形状）。
        Assert.Matches(@"(?m)^\s*var diffInner = Ui\.Hairline\(_diff\);$", source);
        Assert.Matches(@"(?m)^\s*diffInner\.Dock = DockStyle\.Fill;$", source);
        Assert.Matches(@"(?m)^\s*_diffCaption\.Dock = DockStyle\.Top;$", source);
        // 停靠按集合倒序生效：框先加入、说明行后加入，说明行才停在框上方而不是被框盖住。
        var inner = source.IndexOf("diffHost.Controls.Add(diffInner);", StringComparison.Ordinal);
        var caption = source.IndexOf("diffHost.Controls.Add(_diffCaption);", StringComparison.Ordinal);
        Assert.True(inner >= 0 && caption > inner, "说明行应在只读框之后加入宿主（停靠倒序生效）。");
        // 宿主仍是底部固定区，高度不变（这一屏的版式不因加一行标题而重排）。
        Assert.Matches(@"(?m)^\s*var diffHost = new Panel \{ Dock = DockStyle\.Bottom, Height = 210,", source);
    }

    [Fact]
    public void Caption_StaysInEveryState()
    {
        // 说明行讲的是「这块框是什么」，不是「这一行是什么」：选中变化、筛到零行、整份数据不可得都不动它。
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Single(Regex.Matches(source, @"(?m)^\s*_diffCaption = "));
        Assert.Empty(Regex.Matches(source, @"_diffCaption\.(Visible|Text)\s*="));
        foreach (var body in new[] { MethodBody(source, "private void ShowHint(string message, Color color)"), MethodBody(source, "private void ShowDiff()") })
        {
            Assert.DoesNotContain("_diffCaption", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DiffBox_KeepsItsBehaviour()
    {
        // 只加一行说明文字：只读框仍是等宽、不换行、双向滚动条的只读预览，无选中时仍清空（不残留上一行）。
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Matches(
            @"(?m)^\s*private readonly TextBox _diff = Ui\.Preview\(multiline: true, mono: true, wordWrap: false, scrollBars: ScrollBars\.Both\);$",
            source);
        var showDiff = MethodBody(source, "private void ShowDiff()");
        Assert.Contains("_diff.Text = \"\";", showDiff, StringComparison.Ordinal);
        Assert.Contains("_diff.Text = string.IsNullOrWhiteSpace(file.Diff)", showDiff, StringComparison.Ordinal);
    }

    [Fact]
    public void NoBarePreviewAreaIsLeftInThisDialog()
    {
        // 这一屏的两块只读区（跳过清单、差异预览）都有说明行：不留一块没有标题的框。
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Equal(2, Regex.Matches(source, @"Ui\.Caption\(").Count);
        Assert.Contains(
            "Ui.Caption(\"跳过的位置：这些注释位置本次不写回源码，每条为什么写在「原因」列；改完点「重新生成计划」。\")",
            source,
            StringComparison.Ordinal);
        Assert.Contains("Ui.Caption(ApplyPlanEntries.DiffCaption)", source, StringComparison.Ordinal);
    }

    // ---------- 设计基线 ----------

    [Fact]
    public void DesignDoc_RecordsTheDiffCaption()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("**注释回写的差异预览说明行**（2026-11-08 第九十四轮，纯呈现层）", section);
        Assert.Contains("`ApplyPlanEntries.DiffCaption`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 注释回写的差异预览说明行（§6） \|", ledger);
        Assert.Contains("第九十四轮", ledger);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimMoreThanWasDone()
    {
        // 本轮只加一行说明文字：不得写成改了差异内容、改了选中落点或改了计划口径。
        var ledger = Repo.DesignSection(8);
        var row = Slice(ledger, "| 注释回写的差异预览说明行（§6） |", "\n以上每项在达成时回写本文并注明证据。");
        Assert.Contains("纯呈现层", row);
        Assert.DoesNotContain("改了差异内容", row, StringComparison.Ordinal);
        Assert.DoesNotContain("全部预览区已补说明行", row, StringComparison.Ordinal);
    }

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

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
