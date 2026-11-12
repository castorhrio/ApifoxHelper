using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 按钮行不得被占位控件撑高（第二百二十二轮，具名 pass：layout；出处：<c>docs/CRITIQUE.md</c>
/// 文末「本文没动的那几处」第 8 条）。
/// <para>
/// <b>补的是哪个缺口</b>：九处按钮行（八个文件，<c>SyncPreviewDialog</c> 两处）都在某一列放过一个
/// <c>new Panel { Margin = new Padding(0) }</c> 当「把按钮推到右边」的占位——那一列是
/// <c>SizeType.Percent 100</c>，空着就够，而 <see cref="Panel"/> 的默认尺寸是 200×100，
/// 按钮行又是 <c>AutoSize</c>，于是那一格的高度成了整行的高度：按钮 40px、行高 120px，
/// 按钮下方留出一片 60px 的死空白。这与第二百零六轮修掉的提示行是<b>同一个根因</b>
/// （<c>ProfileFormLayout.SpacerSize</c> 记着理由），当时只量了提示行、按钮行「既没改也没量」。
/// </para>
/// <para>
/// <b>先量再改</b>：本轮先在冒烟器里补 <c>RunActionRowFit</c>（真实控件量「行高 / 最高按钮 /
/// 上下内边距 / 多出多少」），量到九处里八处 120px、同步预览预览页那一处 100px（它连内边距都没有），
/// 改完再量一遍：八处 60px（= 按钮 40 + 内边距 20，多出 0px）、预览页那一处 40px（多出 0px）。
/// 这一条纪律来自第 8 站那句「不要不量就改」——上一轮改「#」表头之前先量了列宽，这一轮同理。
/// </para>
/// <para>
/// <b>本文件判什么</b>：① 界面代码里不得再出现那个占位 <c>Panel</c>（只看代码行，注释里引用它是在说理由）；
/// ② 九处按钮行所在的八个文件都在，且每一处「推到右边」那一列是空的（由 ① 覆盖，这里点名到文件）；
/// ③ 冒烟器那一段量测删不掉（段注册、方法体、阈值常量、失败消息各判一次）；
/// ④ 设计基线 §6 记下这条口径、§8 那一格从「既没改也没量」改成实测读数；
/// ⑤ critique 文末第 8 条交代了去向（量到了什么、改了什么）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判真机上按钮行的观感（行高在 125% / 200% 档的实际像素随 G-D5 复核），
/// 也不判「按钮推到右边」这个排布本身该不该换——那是另一次 layout。
/// </para>
/// </summary>
public class ActionRowPlaceholderTests
{
    /// <summary>九处按钮行所在的八个文件（<c>SyncPreviewDialog</c> 两处：底部按钮行与预览页动作行）。</summary>
    private static readonly string[] ActionRowFiles =
    [
        "ApplyPlanDialog.cs",
        "CandidatesDialog.cs",
        "ContractsDialog.cs",
        "DecisionsDialog.cs",
        "EndpointTraceDialog.cs",
        "ReportsDialog.cs",
        "SupplementsDialog.cs",
        "SyncPreviewDialog.cs",
    ];

    private static string SmokeSource => Repo.SmokeProgram;

    private static string DesignSection6 => Repo.DesignSection(6);

    private static string DesignSection8 => Repo.DesignSection(8);

    private static string Critique => Repo.Source("docs", "CRITIQUE.md");

    /// <summary>
    /// 占位 <c>Panel</c> 不得回来：它把 <c>AutoSize</c> 的按钮行撑到 100px 高（按钮 40px）。
    /// 判的是<b>代码行</b>——注释里写「不往里放占位控件」正是本轮留下的理由，不能把它判成违规。
    /// </summary>
    [Fact]
    public void ActionRows_NeverAddAPlaceholderPanel()
    {
        var offenders = Repo.NativeSources()
            .Where(source => Regex.IsMatch(source.Code, @"new Panel\s*\{\s*Margin\s*=\s*new Padding\(0\)\s*\}"))
            .Select(source => source.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "这些界面文件又往按钮行里放了默认尺寸的占位 Panel（200×100）："
            + string.Join("、", offenders)
            + "。那一列是 Percent 100 的「把按钮推到右边」列，空着就够——放控件会把 AutoSize 的按钮行撑到 100px 高"
            + "（第二百二十二轮实测：按钮 40px、行高 120px，按钮下方一片 60px 死空白）。");
    }

    /// <summary>
    /// 九处按钮行所在的八个文件都在，且每一处都留了一句「为什么这一列空着」——下一轮照着注释就知道不能填回去。
    /// 这一条只管「文件还在、理由还在」，不数注释条数（数条数会在正经新增一屏时误报）。
    /// </summary>
    [Fact]
    public void EveryActionRowFile_KeepsTheReasonThePushColumnStaysEmpty()
    {
        foreach (var file in ActionRowFiles)
        {
            var source = Repo.NativeSources().FirstOrDefault(s => s.Name == file);
            Assert.True(source is not null, $"按钮行所在的 {file} 不在了——这一族的登记表要跟着改。");
            Assert.True(source!.Text.Contains("Percent 100", StringComparison.Ordinal),
                $"{file} 里找不到「Percent 100」那一列的理由注释：下一轮看见空列会以为漏了控件，把占位 Panel 填回去。");
        }
    }

    /// <summary>
    /// 冒烟器那一段量测删不掉：段注册、方法体、阈值常量、失败消息各判一次。
    /// 「这一行实际多高」只有真实控件答得出，Linux 侧判不了——所以这一段必须留在冒烟器里。
    /// </summary>
    [Fact]
    public void SmokeSection_MeasuresRowHeightAgainstTheTallestButton()
    {
        Assert.Contains("Section(\"按钮行高度（八屏按钮行量行高与最高按钮，真实控件）\", nameof(RunActionRowFit));", SmokeSource, StringComparison.Ordinal);
        Assert.Contains("private static void RunActionRowFit(IServiceProvider services, string dataRoot)", SmokeSource, StringComparison.Ordinal);
        Assert.Contains("private const int ActionRowSlack = 6;", SmokeSource, StringComparison.Ordinal);
        Assert.Contains("按钮行被撑高了（按钮下方留出一片死空白）：", SmokeSource, StringComparison.Ordinal);
        // 判据是「行高 − 最高按钮 − 上下内边距」，不是「行高等于某个数」：按钮尺寸随 DPI 变，写死一个数会在别的档误报。
        Assert.Contains("var slack = row.Height - tallest - padding;", SmokeSource, StringComparison.Ordinal);
        // 八屏逐个构造：少一屏就等于那一屏的按钮行没人量过。
        foreach (var screen in new[] { "注释回写", "注释候选", "响应契约", "处理差异确认", "接口追踪明细", "报告与交接", "补充说明", "同步预览" })
        {
            Assert.Contains($"(\"{screen}\", () =>", SmokeSource, StringComparison.Ordinal);
        }
    }

    /// <summary>设计基线 §6 记下这条口径（按钮行那一行），§8 那一格从「既没改也没量」改成实测读数。</summary>
    [Fact]
    public void DesignDoc_DeclaresTheButtonRowClauseAndRecordsTheReading()
    {
        Assert.Contains("| 按钮行 |", DesignSection6, StringComparison.Ordinal);
        Assert.Contains("Percent 100", DesignSection6, StringComparison.Ordinal);
        Assert.Contains("RunActionRowFit", DesignSection6, StringComparison.Ordinal);
        // §8 那一格必须交代「量到了什么、改成了什么」，不许还留着「既没改也没量」。
        Assert.Contains("按钮行", DesignSection8, StringComparison.Ordinal);
        Assert.Contains("RunActionRowFit", DesignSection8, StringComparison.Ordinal);
        Assert.False(DesignSection8.Contains("本轮**既没改也没量**", StringComparison.Ordinal),
            "§8 还写着按钮行「既没改也没量」——第二百二十二轮已经量过也改过了，这一句留着就是把已做的事说成没做。");
    }

    /// <summary>critique 文末第 8 条交代去向：量到了什么、改了什么、还剩什么随真机。</summary>
    [Fact]
    public void Critique_RecordsWhatTheMeasurementFound()
    {
        Assert.Contains("RunActionRowFit", Critique, StringComparison.Ordinal);
        Assert.Contains("第二百二十二轮", Critique, StringComparison.Ordinal);
        // 那一处是「量出来才看见」的——这一句是这一轮最值钱的产出（先量再改兑现了第二次），不许被删。
        Assert.Contains("量出来才看见", Critique, StringComparison.Ordinal);
    }
}
