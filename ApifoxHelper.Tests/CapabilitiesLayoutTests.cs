using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「连接与写入探测」这一屏的竖直预算（第二百四十七轮，简报 P1-1，具名 pass：layout + harden）。
/// <para>
/// <b>补的是哪个缺口</b>：第二百四十六轮把「哪些屏真的显示过」登记成表并配了门，这一屏
/// （<c>CapabilitiesDialog</c>）在登记里标的是<b>未量</b>——它至今只被冒烟器 <c>new</c> 一次 + 建句柄，
/// 而一张没显示过的窗体 <c>ClientSize</c> 的高是 0，量到的数不能读成「放得下」。本轮真的
/// <c>Show()</c> 出来，第一次量到：窗体 780×620 时四块停靠区 连接检查 309 + 探测区 196 + 状态行 33 = 538px，
/// 留给结果区只有 <b>82px</b>、清单本体 <b>39px</b>——<b>装得下 1 行</b>。而服务层一次探测登记
/// <b>7 条</b>检查，跑完要滚动才看得全。这一屏存在的理由正是「写入前先探测、逐项看结果」。
/// </para>
/// <para>
/// <b>根因是一个形状而不是一个数</b>：窗体高写死 620，与「结果区要给几行」之间没有任何算式绑住。
/// 本轮改成：结果区先说要给几行，像素由同一份行高 + 表头 + 说明行开销算，窗体高由「固定三块 + 结果区」
/// 推出来。算式在 <see cref="CapabilitiesLayout"/> 一处。
/// </para>
/// <para>
/// <b>本轮量到的第二件事（第一版改法当场被否）</b>：把结果区按「7 × 21 + 43」给到 190px、清单本体正好
/// 147px 时，滚动条<b>照样在</b>——因为清单表头先吃掉 24px，留给行的只有 123px（5 行）。
/// 「行数 × 行高」与「装得下」之间差的正是这一笔，所以 <see cref="CapabilitiesLayout.ColumnHeaderHeight"/>
/// 单独记一笔，不并进说明行那一笔。这一发是冒烟段自己报出来的，不是人读出来的。
/// </para>
/// </summary>
public class CapabilitiesLayoutTests
{
    private static string NativeFile(string name) => Repo.Source("Desktop", "Native", name);

    private static string Dialog => NativeFile("CapabilitiesDialog.cs");

    private static int Count(string text, string pattern) => Regex.Matches(text, pattern).Count;

    /// <summary>
    /// 结果区的高度由「行数 × 同一份行高 + 表头 + 说明行与内边距」给出，没有一个像素数是单独发明的；
    /// 窗体高与结果区同源。
    /// </summary>
    [Fact]
    public void ResultPaneHeight_IsRowsTimesRowHeightPlusHeaderAndChrome()
    {
        Assert.Equal(21, CapabilitiesLayout.RowHeight);
        Assert.Equal(7 * 21 + 24 + 43, CapabilitiesLayout.ResultPaneHeight(7));
        Assert.Equal(8 * 21 + 24 + 43, CapabilitiesLayout.ResultPaneHeight(8));
        // 表头与说明行是两笔，不许折成一个数：折了之后改说明行没人知道该动多少。
        Assert.NotEqual(CapabilitiesLayout.ColumnHeaderHeight, CapabilitiesLayout.ResultPaneChrome);
        Assert.Equal(
            CapabilitiesLayout.FixedPanesHeight + CapabilitiesLayout.ResultPaneHeight(CapabilitiesLayout.ProbeRows),
            CapabilitiesLayout.DialogHeight);
    }

    /// <summary>
    /// 窗体高由算式给出，且用的就是产品那一句行数；752 那一格是读数锚点，不是判据。
    /// <para>
    /// <b>为什么判这个</b>：第二百四十四轮在同步预览那一屏上量到过同一课——窗体高写成字面量、
    /// 与「放不放得下」之间没有算式绑住，改行数或改任一块开销，两边不会一起动，而门在原来那一点上照样全绿。
    /// 这一条把「字段用的值」与「算式在实测口径处的取值」钉成同一个数。
    /// </para>
    /// <para>
    /// <b>752 那一格红了怎么办</b>：它是本轮 wine 96 档量出来的窗体高（538 + 7 × 21 + 24 + 43）。
    /// 真要改行数或开销，这一格会红，红了必须去重跑量测并回写 <c>docs/NOTES.md</c> 第二百四十七轮那一节，
    /// 不许顺手改成新值——「算式跟着漂、实测没跟着量」正是这一族最贵的那种绿。
    /// </para>
    /// </summary>
    [Fact]
    public void DialogHeight_IsTheFormulaAtTheMeasuredRowCount()
    {
        Assert.Equal(CapabilitiesLayout.DialogHeightFor(CapabilitiesLayout.ProbeRows), CapabilitiesLayout.DialogHeight);
        Assert.Equal(752, CapabilitiesLayout.DialogHeight);
    }

    /// <summary>
    /// 算式真的跟着行数走：7 / 8 / 9 行三个取值逐个钉死，且一行差恰好是一个行高。
    /// <para>
    /// <b>这一条挡的是「rows 收进参数却没进算式」</b>：把 <c>DialogHeightFor</c> 改回字面量 752，
    /// 上面那条 <see cref="DialogHeight_IsTheFormulaAtTheMeasuredRowCount"/> 照样绿（那一点上两者同值），
    /// 这一条当场红。行数收在参数里，判「跟着变」不必动产品常量。
    /// </para>
    /// </summary>
    [Fact]
    public void DialogHeightFor_TracksEveryRow()
    {
        Assert.Equal(752, CapabilitiesLayout.DialogHeightFor(7));
        Assert.Equal(773, CapabilitiesLayout.DialogHeightFor(8));
        Assert.Equal(731, CapabilitiesLayout.DialogHeightFor(6));
        Assert.Equal(
            CapabilitiesLayout.RowHeight,
            CapabilitiesLayout.DialogHeightFor(CapabilitiesLayout.ProbeRows + 1)
            - CapabilitiesLayout.DialogHeightFor(CapabilitiesLayout.ProbeRows));
    }

    /// <summary>
    /// <see cref="CapabilitiesLayout.ProbeRows"/> 与服务层真的登记几条检查两侧对账。
    /// <para>
    /// <b>补的是哪个缺口</b>：这个 7 此前只写在冒烟段的一句注释里（「见 <c>ProbeAsync</c> 里那七处 Check」），
    /// 服务层多一条检查、少一条检查，都没有一处会红——而「结果区要给几行」正是按这个数算的。
    /// 这一条从 <c>Sync/CompatService.cs</c> 的 <c>ProbeAsync</c> 方法体里取<b>字面量检查名</b>，
    /// 与 <see cref="CapabilitiesLayout.ProbeRows"/> 对账。
    /// </para>
    /// <para>
    /// <b>为什么要有 <see cref="FailureOnlyChecks"/> 这一份</b>：探测在失败路径上也会登记检查
    /// （例如分支列表读不出来时那一条），那些不会出现在「跑完七项」的结果里。把它们点名列出，
    /// 是为了让「多了一条检查」这件事<b>必须有人做决定</b>——是加进结果区的行数，还是登记成失败专用。
    /// 两侧都判：名单里的名字必须真的还在源码里（名单不许烂），源码里的名字必须被某一侧认领。
    /// </para>
    /// </summary>
    [Fact]
    public void ProbeRows_MatchesTheChecksTheServiceRegisters()
    {
        var names = ProbeCheckNames();
        Assert.True(names.Count > 0, "ProbeAsync 里一个 Check(\"…\") 字面量都没取到：取法漂了，这一条会静默全绿。");

        foreach (var name in FailureOnlyChecks)
        {
            Assert.Contains(name, names);
        }

        var counted = names.Where(name => !FailureOnlyChecks.Contains(name, StringComparer.Ordinal)).ToList();
        Assert.Equal(CapabilitiesLayout.ProbeRows, counted.Count);
    }

    /// <summary>
    /// 失败路径上才登记的检查名（不进「跑完七项」的结果区行数）。
    /// <para>加一条检查而没在这里或 <see cref="CapabilitiesLayout.ProbeRows"/> 里表态，上面那条门当场红。</para>
    /// </summary>
    private static readonly string[] FailureOnlyChecks = ["分支列表读取"];

    /// <summary>
    /// 从 <c>Sync/CompatService.cs</c> 的 <c>ProbeAsync</c> 方法体里取检查名的字面量（按出现顺序去重）。
    /// <para>
    /// <b>两处取法都要</b>：直接登记的写 <c>Check("名字", …)</c>，而三次往返写入写的是
    /// <c>RoundTrip("名字", …)</c>——名字在调用点给、<c>Check</c> 在局部函数里用变量接。
    /// 只取 <c>Check("…")</c> 会漏掉那三条（本轮第一版就是这样，量到 4 个名字、判红在 3 对 7）。
    /// <c>Check(name, …)</c> 那种变量调用取不到，也不该取：它的名字来自 <c>RoundTrip</c> 的调用点。
    /// </para>
    /// </summary>
    private static List<string> ProbeCheckNames()
    {
        var source = Repo.Source("Sync", "CompatService.cs");
        var start = source.IndexOf("public async Task<WriteCompatRecord> ProbeAsync(", StringComparison.Ordinal);
        Assert.True(start >= 0, "Sync/CompatService.cs 里找不到 ProbeAsync：探测入口改名或搬家，这一条门需要重定取法。");
        var end = source.IndexOf("private WriteCompatRecord Save(", start, StringComparison.Ordinal);
        Assert.True(end > start, "ProbeAsync 之后找不到 Save(：方法边界取不到，这一条门会读到别的方法里去。");

        var names = new List<string>();
        foreach (Match match in Regex.Matches(source[start..end], @"(?:Check|RoundTrip)\(""(?<name>[^""]+)"""))
        {
            var name = match.Groups["name"].Value;
            if (!names.Contains(name, StringComparer.Ordinal)) names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// 接线：窗体高经口径给，620 那个字面量不得复活。
    /// 「窗体高写死」正是本轮的根因形状，留一个在文件里就等于留着下一次的挤没。
    /// </summary>
    [Fact]
    public void Dialog_TakesItsHeightFromTheCaliber()
    {
        Assert.Equal(1, Count(Dialog, @"new Size\(780, CapabilitiesLayout\.DialogHeight\)"));
        Assert.DoesNotContain("new Size(780, 620)", Dialog, StringComparison.Ordinal);
    }

    /// <summary>
    /// 量测那一段不能退回「建句柄不显示」，也不能把行数抄成第二个字面量。
    /// <para>
    /// <b>判的是那一段，不是整份文件</b>：冒烟器另几段本来就写着 <c>dialog.Show();</c>，
    /// 用 <c>Contains</c> 判整份文件会被别处满足（本仓第二百一十、二百一十一轮记过同一形态）。
    /// 先切出 <c>RunCapabilitiesShown</c> 那一段，段内判这几件事，切不到段本身即红。
    /// </para>
    /// </summary>
    [Fact]
    public void TheMeasurement_ShowsTheDialogAndTakesTheRowCountFromTheCaliber()
    {
        var segment = MethodSlice(Repo.SmokeProgram,
            "private static void RunCapabilitiesShown(IServiceProvider services, string dataRoot)");
        Assert.Contains("dialog.Show();", segment, StringComparison.Ordinal);
        Assert.Contains("dialog.PerformLayout();", segment, StringComparison.Ordinal);
        // 行数取口径那一处，不抄第二份 7。
        Assert.Contains("CapabilitiesLayout.ProbeRows", segment, StringComparison.Ordinal);
        Assert.DoesNotContain("const int ProbeChecks = 7;", segment, StringComparison.Ordinal);
        // 「装不装得下」的直接读数（最后一行底边）必须进判据，不能只看滚动条样式位：
        // 本轮第一版就是「清单 147px、7 × 21 = 147px、滚动条仍在」，只看样式位读不出差在哪。
        Assert.Contains("lastBottom > list.ClientSize.Height", segment, StringComparison.Ordinal);
        // 非设计档不判（窗体被虚拟屏截断，那一刻属于「量不到」而不是「放不下」）。
        Assert.Contains("DpiScaleFactor.ShouldScale", segment, StringComparison.Ordinal);
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
    /// 登记表里这一屏从「未量」改成「已量」并写清轮次——简报 P1-1 要的那一行。
    /// <para>
    /// 通用的对账在 <c>MeasuredScreensTests</c>；这一条只钉「这一屏这一轮真的被认领了」，
    /// 免得下一轮有人把 <c>RunCapabilitiesShown</c> 删掉而登记表悄悄退回「未量」还全绿。
    /// </para>
    /// </summary>
    [Fact]
    public void MeasuredScreens_RegistersThisScreenAsMeasured()
    {
        var row = MeasuredScreens.All.Single(r => r.Classes.Contains("CapabilitiesDialog", StringComparer.Ordinal));
        Assert.True(row.Measured, "「连接与写入探测」在登记表里又退回未量了：本轮真的 Show() 过它。");
        Assert.Equal("RunCapabilitiesShown", row.Section);
        Assert.Equal("第二百四十七轮", row.Round);
    }

    /// <summary>共享编译：这条算式与测试工程是同一份，不是测试里抄的第二份。</summary>
    [Fact]
    public void CapabilitiesLayout_IsSharedCompiledIntoTheTests()
    {
        Assert.Contains(
            @"..\Desktop\Native\CapabilitiesLayout.cs",
            Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj"),
            StringComparison.Ordinal);
    }
}
