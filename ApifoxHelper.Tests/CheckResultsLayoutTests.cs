using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「差异检查结果」这一屏的竖直预算（第二百四十八轮，简报 P0-1，具名 pass：layout + harden）。
/// <para>
/// <b>补的是哪个缺口</b>：第二百四十六轮把「哪些屏真的显示过」登记成表并配了门，这一屏
/// （<c>CheckResultsDialog</c>）在登记里标的是<b>未量</b>——它至今只被冒烟器 <c>new</c> 一次 + 建句柄，
/// 而一张没显示过的窗体 <c>ClientSize</c> 的高是 0，量到的数不能读成「放得下」。第二百四十七轮
/// commit message 把这一屏明确挂账成「计划中的新切片…一行未写——留给下一轮，不冒充已做」。
/// 本轮真的 <c>Show()</c> 出来，第一次量到：窗体 1000×720 时六块固定区 顶部 39 + 31 + 底部 124 + 168 + 148 + 60
/// = <b>570px</b>，留给接口清单宿主只有 <b>150px</b>、清单本体 <b>140px</b>——扣掉表头 24px 之后
/// <b>装得下 5 行</b>。而这一屏的主内容正是那张接口清单（真实项目上几十上百个接口）。
/// </para>
/// <para>
/// <b>根因是一个形状而不是一个数</b>：窗体高写死 720，与「接口清单要给几行」之间没有任何算式绑住。
/// 本轮改成：接口清单先说要给几行，像素由同一份行高 + 表头算，窗体高由「固定六块 + 接口清单 + 宿主多出来的那一截」
/// 推出来。算式在 <see cref="CheckResultsLayout"/> 一处。
/// </para>
/// </summary>
public class CheckResultsLayoutTests
{
    private static string NativeFile(string name) => Repo.Source("Desktop", "Native", name);

    private static string Dialog => NativeFile("CheckResultsDialog.cs");

    private static int Count(string text, string pattern) => Regex.Matches(text, pattern).Count;

    /// <summary>
    /// 接口清单的高度由「行数 × 同一份行高 + 表头」给出，没有一个像素数是单独发明的；窗体高与它同源。
    /// </summary>
    [Fact]
    public void MainListHeight_IsRowsTimesRowHeightPlusHeader()
    {
        Assert.Equal(21, CheckResultsLayout.RowHeight);
        Assert.Equal(8 * 21 + 24, CheckResultsLayout.MainListHeight(8));
        Assert.Equal(9 * 21 + 24, CheckResultsLayout.MainListHeight(9));
        // 表头与宿主那一截是两笔，不许折成一个数：折了之后改内边距没人知道该动多少。
        Assert.NotEqual(CheckResultsLayout.ColumnHeaderHeight, CheckResultsLayout.ListHostChrome);
        Assert.Equal(
            CheckResultsLayout.FixedPanesHeight + CheckResultsLayout.MainListHeight(CheckResultsLayout.MainRows) + CheckResultsLayout.ListHostChrome,
            CheckResultsLayout.DialogHeight);
    }

    /// <summary>
    /// 窗体高由算式给出，且用的就是产品那一句行数；772 那一格是读数锚点，不是判据。
    /// <para>
    /// <b>为什么判这个</b>：第二百四十四轮在同步预览那一屏、第二百四十七轮在连接与写入探测那一屏上
    /// 量到过同一课——窗体高写成字面量、与「放不放得下」之间没有算式绑住，改行数或改任一块开销，
    /// 两边不会一起动，而门在原来那一点上照样全绿。
    /// </para>
    /// <para>
    /// <b>772 那一格红了怎么办</b>：它是本轮 wine 96 档量出来的窗体高（570 + 8 × 21 + 24 + 10）。
    /// 真要改行数或开销，这一格会红，红了必须去重跑量测并回写 <c>docs/NOTES.md</c> 第二百四十八轮那一节，
    /// 不许顺手改成新值——「算式跟着漂、实测没跟着量」正是这一族最贵的那种绿。
    /// </para>
    /// </summary>
    [Fact]
    public void DialogHeight_IsTheFormulaAtTheMeasuredRowCount()
    {
        Assert.Equal(CheckResultsLayout.DialogHeightFor(CheckResultsLayout.MainRows), CheckResultsLayout.DialogHeight);
        Assert.Equal(772, CheckResultsLayout.DialogHeight);
    }

    /// <summary>
    /// 算式真的跟着行数走：8 / 9 / 7 行三个取值逐个钉死，且一行差恰好是一个行高。
    /// <para>
    /// <b>这一条挡的是「rows 收进参数却没进算式」</b>：把 <c>DialogHeightFor</c> 改回字面量 772，
    /// 上面那条照样绿（那一点上两者同值），这一条当场红。
    /// </para>
    /// </summary>
    [Fact]
    public void DialogHeightFor_TracksEveryRow()
    {
        Assert.Equal(772, CheckResultsLayout.DialogHeightFor(8));
        Assert.Equal(793, CheckResultsLayout.DialogHeightFor(9));
        Assert.Equal(751, CheckResultsLayout.DialogHeightFor(7));
        Assert.Equal(
            CheckResultsLayout.RowHeight,
            CheckResultsLayout.DialogHeightFor(CheckResultsLayout.MainRows + 1)
            - CheckResultsLayout.DialogHeightFor(CheckResultsLayout.MainRows));
    }

    /// <summary>
    /// 接线：窗体高经口径给，720 那个字面量不得复活。
    /// 「窗体高写死」正是本轮的根因形状，留一个在文件里就等于留着下一次的挤没。
    /// </summary>
    [Fact]
    public void Dialog_TakesItsHeightFromTheCaliber()
    {
        Assert.Equal(1, Count(Dialog, @"new Size\(1000, CheckResultsLayout\.DialogHeight\)"));
        Assert.DoesNotContain("new Size(1000, 720)", Dialog, StringComparison.Ordinal);
    }

    /// <summary>
    /// 量测那一段不能退回「建句柄不显示」，也不能把行数抄成第二个字面量。
    /// <para>
    /// <b>判的是那一段，不是整份文件</b>：冒烟器另几段本来就写着 <c>dialog.Show();</c>，
    /// 用 <c>Contains</c> 判整份文件会被别处满足（本仓第二百一十、二百一十一轮记过同一形态）。
    /// </para>
    /// </summary>
    [Fact]
    public void TheMeasurement_ShowsTheDialogAndTakesTheRowCountFromTheCaliber()
    {
        var segment = MethodSlice(Repo.SmokeProgram,
            "private static void RunCheckResultsShown(IServiceProvider services, string dataRoot)");
        Assert.Contains("dialog.Show();", segment, StringComparison.Ordinal);
        // 布局要真的跑完：Show() 之后补一次 DoEvents 让停靠重排落地（与 RunCapabilitiesShown 同一路径）。
        // 这里不判 PerformLayout()——Show() 已经跑过布局，多写一句只是重复，判它会把「照抄另一段」当成判据。
        Assert.Contains("Application.DoEvents();", segment, StringComparison.Ordinal);
        // 行数取口径那一处，不抄第二份 8。
        Assert.Contains("CheckResultsLayout.MainRows", segment, StringComparison.Ordinal);
        Assert.DoesNotContain("const int MainRows = 8;", segment, StringComparison.Ordinal);
        // 表头那一笔必须从读数里扣掉：只按「清单高 / 行高」算会把表头也算成一行
        // （第二百四十七轮在探测那一屏撞过同一处）。
        Assert.Contains("headerHeight", segment, StringComparison.Ordinal);
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
    /// 登记表里这一屏从「未量」改成「已量」并写清轮次——简报 P0-1 要的那一行。
    /// <para>
    /// 通用的对账在 <c>MeasuredScreensTests</c>；这一条只钉「这一屏这一轮真的被认领了」，
    /// 免得下一轮有人把 <c>RunCheckResultsShown</c> 删掉而登记表悄悄退回「未量」还全绿。
    /// </para>
    /// </summary>
    [Fact]
    public void MeasuredScreens_RegistersThisScreenAsMeasured()
    {
        var row = MeasuredScreens.All.Single(r => r.Classes.Contains("CheckResultsDialog", StringComparer.Ordinal));
        Assert.True(row.Measured, "「差异检查结果」在登记表里又退回未量了：本轮真的 Show() 过它。");
        Assert.Equal("RunCheckResultsShown", row.Section);
        Assert.Equal("第二百四十八轮", row.Round);
    }

    /// <summary>共享编译：这条算式与测试工程是同一份，不是测试里抄的第二份。</summary>
    [Fact]
    public void CheckResultsLayout_IsSharedCompiledIntoTheTests()
    {
        Assert.Contains(
            @"..\Desktop\Native\CheckResultsLayout.cs",
            Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj"),
            StringComparison.Ordinal);
    }
}
