using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// DPI 缩放的 Linux 侧机械门（第一百六十七轮，简报 P1-1）。
/// <para>
/// <b>为什么这一条值得单独钉</b>：≈200% 档的溢出是第一百六十二轮在 wine 下量到的真实缺陷，
/// 修法是 <c>DpiScale.Apply</c>（第一百六十三轮）。但修完之后，Linux 侧没有任何一条断言看过
/// 「缩放因子怎么算」「框架自动缩放有没有被关掉」「窗体是不是还自己写死尺寸」这三件事——
/// 全部证据只有 wine 报告，而 wine 报告不区分「按因子 2 放大」与「按因子 1 什么都没做」，
/// 它只说这一档放得下。把放得下当成缩放生效，是一个可以自己把自己判绿的门。
/// </para>
/// <para>
/// 本文件把能机械判的那半判掉：算式走 <see cref="DpiScaleFactor"/>（纯算术、与测试工程共享编译，
/// 真值表在 Linux 真执行），接线走行首正则（只判代码行，注释里引用被否决的旧写法是说明理由）。
/// 剩下那半——缩放真的发生后屏幕上放得下——仍然只有构造得起来的窗口能给，随 wine 冒烟与 G-D5 真机复核。
/// </para>
/// </summary>
public class DpiScaleTests
{
    private const string ScaleFile = """
        <Compile Include="..\Desktop\Native\DpiScaleFactor.cs" Link="Shared\DpiScaleFactor.cs" />
        """;

    /// <summary>
    /// 缩放因子 = 屏幕 DPI ÷ 96（DESIGN.md §3/§4 的档位基准）。
    /// 96 / 120 / 192 就是 wine 三档实测改的 LogPixels；144 是真机上最常见的 150%。
    /// </summary>
    [Theory]
    [InlineData(96, 1f)]
    [InlineData(120, 1.25f)]
    [InlineData(144, 1.5f)]
    [InlineData(192, 2f)]
    [InlineData(48, 0.5f)]    // 算式给的就是比值；小于 1 由 ShouldScale 挡掉，不在这里改口径
    public void ScaleFactor_IsScreenDpiOverTheDesignBaseline(int dpi, float expected) =>
        Assert.Equal(expected, DpiScaleFactor.FromScreenDpi(dpi));

    /// <summary>
    /// 量不到 DPI 时回 1，不猜一个数。
    /// <para>
    /// 猜错的因子会把每一处边距、列宽、按钮尺寸一起放大或缩掉，比不缩放更难诊断；
    /// 而 <c>GetDeviceCaps</c> 失败给出的正是 0——这一条钉的是「0 与负数都不算出一个缩放比」。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void ScaleFactor_NeverGuessesWhenTheDpiIsMissing(int dpi) =>
        Assert.Equal(1f, DpiScaleFactor.FromScreenDpi(dpi));

    /// <summary>基准 DPI 只有一个值，且界面代码不得改它：字号档与间距档都按它记录。</summary>
    [Fact]
    public void ScaleFactor_DesignBaselineIs96() => Assert.Equal(96f, DpiScaleFactor.DesignDpi);

    /// <summary>
    /// 因子 1 时不缩放（DESIGN.md §6「DPI 适配」第 ④ 条）。
    /// <para>
    /// 存在的理由：<c>Scale(1,1)</c> 在数学上是恒等，但仍会重算整棵子树的布局；
    /// 「量不到 DPI 就与修复前同一行为」这句话要靠这一条成立，而不是靠看起来一样。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(1f, false)]
    [InlineData(0.5f, false)]     // 缩小不是本口径要做的事（因子只可能 ≥1：量不到回 1）
    [InlineData(1.0001f, true)]
    [InlineData(1.25f, true)]
    [InlineData(2f, true)]
    public void ShouldScale_OnlyWhenTheWindowMustGrow(float factor, bool expected) =>
        Assert.Equal(expected, DpiScaleFactor.ShouldScale(factor));

    /// <summary>
    /// <c>DpiScale.Apply</c> 的接线：字体与设计基准同源、关掉框架自动缩放、按设计尺寸给客户区、
    /// 末尾按因子显式缩放一次——且顺序不能反（先设字体再算尺寸，否则 100% 档就凭空缩放一次）。
    /// </summary>
    [Fact]
    public void Apply_TurnsOffFrameworkAutoScaleAndScalesOnce()
    {
        var body = ApplyBody();
        Assert.True(
            Regex.IsMatch(body, @"^\s*form\.Font = UiTokens\.Body\.Value;$", RegexOptions.Multiline),
            "Apply 没有把窗体字体设成设计基准那一份（UiTokens.Body）：字体与缩放基准不同源时，100% 档也会缩放。");
        Assert.True(
            Regex.IsMatch(body, @"^\s*form\.AutoScaleMode = AutoScaleMode\.None;$", RegexOptions.Multiline),
            "Apply 没有关掉框架自己的自动缩放：留着 Font/Dpi 模式，wine 下它会按窗口 DC 把刚放大的窗体再缩回去。");
        Assert.True(
            Regex.IsMatch(body, @"^\s*form\.ClientSize = designSize;$", RegexOptions.Multiline),
            "Apply 没有把设计尺寸交给窗体：窗体停在默认大小，缩放因子算得再对也没有东西可放大。");
        Assert.True(
            Regex.IsMatch(body, @"^\s*var factor = ScaleFactor\(\);$", RegexOptions.Multiline),
            "Apply 没有走 ScaleFactor 取因子（本文件或别处另算了一份缩放比——缩放比只应有一处算法）。");
        Assert.True(
            Regex.IsMatch(body, @"^\s*if \(DpiScaleFactor\.ShouldScale\(factor\)\) form\.Scale\(new SizeF\(factor, factor\)\);$", RegexOptions.Multiline),
            "Apply 末尾没有按因子显式缩放一次，或不再经 ShouldScale 判该不该缩放。");

        // 顺序：字体 → 基准关 → 设计尺寸 → 缩放（缩放必须在尺寸之后，否则放大的是一次空白布局）。
        var font = IndexOfCode(body, "form.Font = UiTokens.Body.Value;");
        var autoScale = IndexOfCode(body, "form.AutoScaleMode = AutoScaleMode.None;");
        var clientSize = IndexOfCode(body, "form.ClientSize = designSize;");
        var scale = IndexOfCode(body, "form.Scale(new SizeF(factor, factor))");
        Assert.True(font >= 0 && font < autoScale && autoScale < clientSize && clientSize < scale,
            $"Apply 里这四步的顺序错了（字体 {font} → 关自动缩放 {autoScale} → 设计尺寸 {clientSize} → 缩放 {scale}）。");
    }

    /// <summary>
    /// 被 wine 否掉的那条路不许回来：任何界面源码的代码行都不设 <c>AutoScaleDimensions</c>、
    /// 也不把 <c>AutoScaleMode</c> 设成 Font/Dpi（第一百六十三轮实测：屏幕 DC 与窗口 DC 报不同 DPI，
    /// 任何单一基准值都会被建句柄后的那一次重算缩回去）。
    /// <para>
    /// 只判代码行——注释里引用这两个名字是在交代为什么不走这条路，不是第二份实现。
    /// </para>
    /// </summary>
    [Fact]
    public void TheRejectedAutoScalePath_StaysOutOfTheCode()
    {
        var offenders = Repo.NativeSources()
            .SelectMany(src => CodeLines(src.Text)
                .Where(line => line.Contains("AutoScaleDimensions", StringComparison.Ordinal)
                    || Regex.IsMatch(line, @"AutoScaleMode\s*=\s*(AutoScaleMode\.)?(Font|Dpi)"))
                .Select(line => $"{src.Name}：{line.Trim()}"))
            .ToList();
        Assert.True(offenders.Count == 0,
            "这些代码行回到了被 wine 实测否掉的 AutoScaleDimensions 路线：" + string.Join(" / ", offenders));
    }

    /// <summary>
    /// 设计尺寸只有一个落点：界面代码不得自己写 <c>ClientSize = </c> 绕过 <c>DpiScale.Apply</c>。
    /// <para>
    /// 存在的理由：第一百六十二轮量到的缺陷就是「ClientSize 固定逻辑像素且不随缩放放大」。
    /// 写死尺寸本身不是错（对话框要有设计尺寸），错的是绕过 Apply——绕过的那一屏在 ≈200% 档照样溢出，
    /// 而它不会在任何 wine 报告里被认出来（那一屏根本没人给它算过因子）。
    /// </para>
    /// </summary>
    [Fact]
    public void FixedDesignSize_GoesThroughTheDpiGateOnly()
    {
        var offenders = Repo.NativeSources()
            .Where(src => src.Name != "DpiScale.cs")
            .SelectMany(src => CodeLines(src.Text)
                .Where(line => Regex.IsMatch(line, @"^\s*([A-Za-z_][A-Za-z0-9_]*\.)?ClientSize\s*="))
                .Select(line => $"{src.Name}：{line.Trim()}"))
            .ToList();
        Assert.True(offenders.Count == 0,
            "这些代码行直接设 ClientSize、没有经 DpiScale.Apply：" + string.Join(" / ", offenders)
            + "（写死尺寸的那一屏在 ≈200% 档不会放大，第一百六十二轮量到的正是这一族）。");
    }

    /// <summary>
    /// DESIGN.md §6 必须有「DPI 适配」这一条，并且它把挂账写成<b>可被引用的两档</b>。
    /// <para>
    /// 第一百六十九轮补的后半（简报 P1-1 的实底）：条款里现口径那五项用 ①…⑤ 记，
    /// 而挂账当时是一段散文、没有自己的标签，<c>docs/NOTES.md</c> 里四处「挂账第①档/第②档」
    /// 按字面找到的其实是现口径的 ①②（「窗体一律经 `DpiScale.Apply` 出厂」与「因子算式」）——
    /// 引用看着精确，指向的却是错的条目。这正是第一百六十七轮「§6 从来没有这一条」那一族，
    /// 只是这一次的形态是「有条款、没标签」。原先这条门只查 <c>WM_DPICHANGED</c> 与
    /// 「从没量过」两个子串在不在，散文形态照样绿。
    /// </para>
    /// </summary>
    [Fact]
    public void DesignDoc_DeclaresTheDpiRuleAndItsGap()
    {
        var section6 = Repo.DesignSection(6);
        Assert.Contains("| **DPI 适配**", section6);
        Assert.Contains("AutoScaleMode", section6, StringComparison.Ordinal);
        Assert.Contains("DpiScaleFactor", section6, StringComparison.Ordinal);
        // 挂账不许被写成已解决：多屏与运行中改缩放这两档必须点名。
        Assert.Contains("WM_DPICHANGED", section6, StringComparison.Ordinal);
        Assert.Contains("从没量过", section6, StringComparison.Ordinal);

        // 两档必须有标签，且各说清是哪一种情况（标签是 NOTES 那四处引用的落点）。
        Assert.Contains("**第 1 档 多显示器混合缩放**", section6, StringComparison.Ordinal);
        Assert.Contains("**第 2 档 运行中改系统缩放**", section6, StringComparison.Ordinal);
        Assert.Contains("两套序号不共用", section6, StringComparison.Ordinal);

        // 引用侧逐处核：只取「挂账第…档 / 同一挂账的第…档」这一种**引用形状**，
        // 序号必须是本条款里真实存在的第 1 档或第 2 档。
        // 不要改成「禁掉某个子串」：第一百六十九轮第一版就是这么写的，结果它把本轮记录里
        // 「原先写成了什么样子」那句原话一起判红——分不清真引用与对旧写法的说明的门，
        // 下一轮只会被人放宽成没门。圈号那一形取到即红，因为它与本条款现口径的 ①…⑤ 撞号。
        var notes = Repo.Source("docs", "NOTES.md");
        var references = Regex.Matches(notes, @"(?:挂账|同一挂账的)第 ?([0-9①②③④⑤]) ?档");
        Assert.True(references.Count >= 4,
            $"docs/NOTES.md 只有 {references.Count} 处按「第 N 档」引用这条挂账（动作单两行 + B 段两条）。");
        foreach (Match m in references)
        {
            var tier = m.Groups[1].Value;
            Assert.True(tier is "1" or "2",
                $"{m.Value}：挂账只有第 1 档（多显示器混合缩放）与第 2 档（运行中改系统缩放），"
                + "写成 ①② 那一种会落进本条款现口径的 ①…⑤，指向的是别的条目。");
            Assert.Contains($"**第 {tier} 档 ", section6, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 真机复核清单的第 3、4 行必须逐字对上挂账那两档（简报 P1-1 要的那一件事）。
    /// 挂账第 1 档讲的是显示器、第 2 档讲的是运行中改缩放——两行里各引一个标签，
    /// 且第 3 行的动作确实发生在「拖到另一块屏」上、第 4 行确实发生在「不重开窗口改缩放」上。
    /// </summary>
    [Fact]
    public void RealMachineChecklist_PointsWithTheGapTierTheyMeasure()
    {
        var notes = Repo.Source("docs", "NOTES.md").Split('\n');
        var rows = notes.Where(l => l.StartsWith("| [ ] | 3. ", StringComparison.Ordinal)
                                    || l.StartsWith("| [ ] | 4. ", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, rows.Count);

        var mixed = rows.Single(l => l.StartsWith("| [ ] | 3. ", StringComparison.Ordinal));
        Assert.Contains("挂账第 1 档", mixed, StringComparison.Ordinal);
        Assert.Contains("多显示器混合缩放", mixed, StringComparison.Ordinal);
        Assert.Contains("拖到副屏", mixed, StringComparison.Ordinal);

        var runtime = rows.Single(l => l.StartsWith("| [ ] | 4. ", StringComparison.Ordinal));
        Assert.Contains("第 2 档", runtime, StringComparison.Ordinal);
        Assert.Contains("运行中改系统缩放", runtime, StringComparison.Ordinal);
        Assert.Contains("不重开窗口", runtime, StringComparison.Ordinal);
    }

    /// <summary>纯算式那一份必须真的被测试工程编进来，否则上面的真值表是对着一个不存在的类型跑。</summary>
    [Fact]
    public void ScaleFactor_IsSharedCompiledIntoTheTestProject() =>
        Assert.Contains(ScaleFile.Trim(), Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj"), StringComparison.Ordinal);

    /// <summary><c>DpiScale.Apply</c> 的方法体（从声明行到下一个方法声明之前）。</summary>
    private static string ApplyBody()
    {
        var source = Repo.Source("Desktop", "Native", "DpiScale.cs");
        var start = source.IndexOf("public static void Apply(", StringComparison.Ordinal);
        Assert.True(start >= 0, "找不到 DpiScale.Apply。");
        var end = source.IndexOf("public static float ScaleFactor", start, StringComparison.Ordinal);
        Assert.True(end > start, "Apply 与 ScaleFactor 之间切不出方法体。");
        return source[start..end];
    }

    private static int IndexOfCode(string body, string code) => CodeScan.IndexOfLine(body, code);

    private static IEnumerable<string> CodeLines(string source) => CodeScan.Lines(source);
}
